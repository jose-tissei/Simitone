using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class FilteredPieEntry
    {
        public int OriginalIndex;
        public string DisplayName;
    }

    // Pure-logic manual pie menu filter for adult-to-adult Sim interactions (the "Simplified" pie menu).
    // The several Talk variants collapse into one "Talk" bubble and the romantic actions are grouped in a "Romance" submenu;
    // everything else (friendly Hug, Dance, Joke, Tickle, Give Gift, ...) stays on the menu exactly as the game made it.
    public static class PieMenuLogic
    {
        public static List<FilteredPieEntry> Filter(IList<string> rawNames, bool enabled, bool isAdultHumanTarget, bool isSelf)
        {
            var passthrough = new List<FilteredPieEntry>();
            if (rawNames == null) return passthrough;
            for (int i = 0; i < rawNames.Count; i++) passthrough.Add(new FilteredPieEntry { OriginalIndex = i, DisplayName = rawNames[i] });
            if (!enabled || !isAdultHumanTarget || isSelf || rawNames.Count == 0) return passthrough;

            int bestTalkIdx = -1;
            int bestTalkRank = -1;
            var romance = new List<FilteredPieEntry>();
            var seenRomance = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var others = new List<FilteredPieEntry>();
            bool anyTalkOrRomance = false;

            for (int i = 0; i < rawNames.Count; i++)
            {
                var keep = new FilteredPieEntry { OriginalIndex = i, DisplayName = rawNames[i] };
                var cleaned = CleanName(rawNames[i]);
                if (cleaned.Length == 0) { others.Add(keep); continue; }
                var segs = cleaned.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length == 0) { others.Add(keep); continue; }
                string root = StripEllipsis(segs[0]);
                string sub = segs.Length > 1 ? string.Join(" - ", segs.Skip(1).Select(StripEllipsis)) : "";
                string rootLow = root.ToLowerInvariant();
                string subLow = sub.ToLowerInvariant();

                if (rootLow == "talk" || rootLow.StartsWith("talk "))
                {
                    anyTalkOrRomance = true;
                    int rank = RankTalk(subLow);
                    if (rank > bestTalkRank) { bestTalkRank = rank; bestTalkIdx = i; }
                    continue;
                }

                string romanceSub;
                if (TryRomanceSubLabel(root, rootLow, sub, subLow, out romanceSub))
                {
                    anyTalkOrRomance = true;
                    var label = "Romance/" + romanceSub;
                    if (seenRomance.Add(label))
                        romance.Add(new FilteredPieEntry { OriginalIndex = i, DisplayName = label });
                    continue;
                }
                others.Add(keep);
            }

            if (!anyTalkOrRomance) return passthrough;   // e.g., ungreeted visitor with only "Greet"

            var result = new List<FilteredPieEntry>();
            if (bestTalkIdx >= 0) result.Add(new FilteredPieEntry { OriginalIndex = bestTalkIdx, DisplayName = "Talk" });
            result.AddRange(others);
            result.AddRange(romance);
            return result;
        }

        static string CleanName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Replace('\\', '/').Trim();
            int semi = s.LastIndexOf(';');
            if (semi > 0)
            {
                int dummy;
                if (int.TryParse(s.Substring(semi + 1).Trim(), out dummy)) s = s.Substring(0, semi);
            }
            return s.Trim();
        }

        static string StripEllipsis(string s)
        {
            s = (s ?? "").Trim();
            while (s.EndsWith(".")) s = s.Substring(0, s.Length - 1);
            return s.Trim();
        }

        static int RankTalk(string subLow)
        {
            if (subLow.Contains("about interests")) return 100;
            if (subLow == "chat" || subLow.Contains("chat")) return 80;
            if (subLow.Length == 0) return 70;
            if (subLow.Contains("gossip")) return 30;
            return 50;
        }

        static bool TryRomanceSubLabel(string root, string rootLow, string sub, string subLow, out string romanceSub)
        {
            romanceSub = null;
            bool isDirectRomance =
                rootLow == "flirt" || rootLow.StartsWith("flirt ") ||
                rootLow == "kiss" || rootLow.StartsWith("kiss ") ||
                rootLow == "back rub" || rootLow == "backrub" ||
                rootLow == "propose" || rootLow.StartsWith("propose ") ||
                rootLow == "romance" || rootLow == "romantic" ||
                rootLow == "serenade" || rootLow == "make out";

            bool isRomanticHug =
                (rootLow == "hug" || rootLow.StartsWith("hug ")) &&
                (subLow.Contains("romantic") || subLow.Contains("intimate") || subLow.Contains("leap") || subLow.Contains("passionate"));

            // a plain "Dance" stays on the menu; only the slow / romantic dances are romance
            bool isRomanticDance =
                (rootLow == "dance" || rootLow == "dance with") &&
                (subLow.Contains("slow") || subLow.Contains("romantic"));

            bool isExplicitRomanceOther =
                !rootLow.StartsWith("compliment") &&
                (subLow.Contains("romantic") || subLow.Contains("passionate") || subLow.Contains("intimate") || subLow.Contains("make out"));

            if (!isDirectRomance && !isRomanticHug && !isRomanticDance && !isExplicitRomanceOther) return false;

            if (rootLow == "romance" && sub.Length > 0) romanceSub = sub;
            else romanceSub = sub.Length > 0 ? root + " - " + sub : root;
            return true;
        }
    }
}
