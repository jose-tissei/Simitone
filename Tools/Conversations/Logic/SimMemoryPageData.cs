using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // The data the Sim Memory page (Tools/HtmlUi/ui/sim_memory.html) is filled with.
    // Pure logic, no game types: the window passes in what it knows and gets plain dictionaries and lists for the Template engine.
    public static class SimMemoryPageData
    {
        public static Dictionary<string, object> Build(SimMemorySnapshot snap, IList<SpeakerIdentity> speakers, string selectedSpeakerKey,
            string selectedListenerKey, bool showNews, IList<NewsEntry> news, string speakerName, string newsFilter = "all", bool newsAll = false, string search = "", int totalKnown = -1)
        {
            speakerName = string.IsNullOrWhiteSpace(speakerName) ? (snap != null ? snap.SpeakerName : "Sim") : speakerName;
            var d = new Dictionary<string, object>
            {
                { "speakerName", speakerName },
                { "showNews", showNews },
                { "newsCount", news == null ? 0 : news.Count },
                { "headIcon", showNews ? "news" : "book" },
                { "title", speakerName + "'s Sim Memory" },
                { "subtitle", showNews
                    ? "What " + speakerName + " saw or heard about other Sims"
                    : "What " + speakerName + " has learned about " + (TotalOf(snap, totalKnown) == 1 ? "1 Sim" : TotalOf(snap, totalKnown) + " Sims") },
                { "switchLabel", showNews ? "Dossier" : "News" },
                { "switchIcon", showNews ? "book" : "news" },
            };

            d["speakers"] = speakers.Take(6).Select(s => (object)new Dictionary<string, object>
            {
                { "name", s.Name }, { "count", s.KnownCount },
                { "active", string.Equals(s.Key, selectedSpeakerKey, StringComparison.OrdinalIgnoreCase) },
            }).ToList();

            d["search"] = search ?? "";
            d["searchEmpty"] = string.IsNullOrEmpty(search);
            d["noMatches"] = !string.IsNullOrEmpty(search) && snap.KnownSims.Count == 0 && TotalOf(snap, totalKnown) > 0;
            var selected = snap.KnownSims.FirstOrDefault(k => string.Equals(k.ListenerKey, selectedListenerKey, StringComparison.OrdinalIgnoreCase))
                           ?? snap.KnownSims.FirstOrDefault();
            d["known"] = snap.KnownSims.Select(k => (object)new Dictionary<string, object>
            {
                { "name", k.ListenerName }, { "letter", Letter(k.ListenerName) }, { "sim", SimColor(k.ListenerName) },
                { "line1", k.FamiliarityLabel + " · " + k.KnowledgePercent + "%" },
                { "line2", k.Exchanges + (k.Exchanges == 1 ? " chat" : " chats") },
                { "selected", k == selected },
            }).ToList();

            if (selected != null) d["card"] = Card(selected);

            var all = news ?? new List<NewsEntry>();
            var filtered = all.Where(e => NewsMatches(e, newsFilter)).ToList();
            int shown = newsAll ? filtered.Count : Math.Min(5, filtered.Count);
            var newsRows = filtered.Take(shown).Select(News).ToList();
            d["news"] = newsRows;
            d["newsEmpty"] = all.Count == 0;
            d["newsNoMatch"] = all.Count > 0 && filtered.Count == 0;
            d["newsMore"] = filtered.Count > shown ? "Show " + (filtered.Count - shown) + " more" : "";
            d["filters"] = new[] { new[] { "all", "All" }, new[] { "saw", "Saw it" }, new[] { "heard", "Heard" }, new[] { "mismatch", "Mismatch" } }
                .Select(f => (object)new Dictionary<string, object> { { "key", f[0] }, { "label", f[1] }, { "on", string.Equals(f[0], newsFilter ?? "all", StringComparison.OrdinalIgnoreCase) } }).ToList();
            d["newsFooter"] = (news == null ? 0 : news.Count) + (news != null && news.Count == 1 ? " story" : " stories");
            d["newsHint"] = "Newest first. Certainty shows how sure " + speakerName + " is.";
            return d;
        }

        static int TotalOf(SimMemorySnapshot snap, int total) { return total >= 0 ? total : snap.KnownSims.Count; }

        // the Known Sims search: a name contains the typed text (any case)
        public static bool NameMatches(string name, string search)
        {
            return string.IsNullOrWhiteSpace(search) || (name ?? "").IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // the News filter: all | saw (they saw it) | heard (told by someone, or only a guess) | mismatch (their version differs from what happened)
        public static bool NewsMatches(NewsEntry e, string filter)
        {
            switch ((filter ?? "all").ToLowerInvariant())
            {
                case "saw": return string.Equals(e.Certainty, "saw it", StringComparison.OrdinalIgnoreCase);
                case "heard": return !string.Equals(e.Certainty, "saw it", StringComparison.OrdinalIgnoreCase);
                case "mismatch": return e.Differs;
                default: return true;
            }
        }

        static Dictionary<string, object> Card(SimKnowledgeCard c)
        {
            int half = Math.Max(0, Math.Min(100, c.KnowledgePercent)) / 2;
            return new Dictionary<string, object>
            {
                { "name", c.ListenerName }, { "sim", SimColor(c.ListenerName) },
                { "familiarity", c.FamiliarityLabel }, { "percent", c.KnowledgePercent },
                { "relRest", 50 - half }, { "relFill", half },
                { "today", Signed(c.LastStr) }, { "todayGood", c.LastStr >= 0 },
                { "lifetime", Signed(c.LastLtr) },
                { "great", c.PositiveCount }, { "ok", c.NeutralCount }, { "awkward", c.NegativeCount },
                { "tense", c.IsTense },
                { "traits", Strings(c.Traits) }, { "hasTraits", c.Traits.Count > 0 },
                { "temperament", c.Temperament ?? "" },
                { "career", c.Career ?? "" },
                { "likes", Strings(c.LikedInterests, 2) }, { "neutral", Strings(c.NeutralInterests, 2) }, { "dislikes", Strings(c.DislikedInterests, 2) },
                { "noInterests", c.LikedInterests.Count + c.NeutralInterests.Count + c.DislikedInterests.Count == 0 },
                { "skills", Strings(c.Skills) },
                { "works", Strings(c.WorkedInteractions.Concat(c.WorkedTones.Select(t => t + " tone")), 6) },
                { "avoid", Strings(c.AvoidInteractions.Concat(c.AvoidTones.Select(t => t + " tone")), 4) },
                { "topics", c.Topics.Take(2).Select(t => (object)new Dictionary<string, object>
                    { { "title", t.Title }, { "outcome", t.OutcomeLabel.ToLowerInvariant() }, { "awkward", t.LastOutcome <= 1 } }).ToList() },
            };
        }

        // certainty first (colour AND icon AND words, never colour alone), then where it came from, then who it was passed to
        static object News(NewsEntry n)
        {
            var chips = new List<object>();
            string cls = n.Certainty == "saw it" ? "good" : n.Certainty == "not sure" ? "warn" : "";
            string icon = n.Certainty == "saw it" ? "eye" : n.Certainty == "not sure" ? "alert" : n.Certainty == "fuzzy" ? "thought" : "check";
            chips.Add(Chip(Cap(n.Certainty), cls, icon));
            if (!string.IsNullOrEmpty(n.Source)) chips.Add(Chip(Cap(n.Source.Replace("heard it from", "Heard from")), "", "chat"));
            if (!string.IsNullOrEmpty(n.Passed)) chips.Add(Chip(Cap(n.Passed.Replace("told", "Told")), "", "chat"));
            if (n.Differs) chips.Add(Chip("Differs from what really happened", "bad", "close"));
            return new Dictionary<string, object>
            {
                { "text", Cap(n.Text) }, { "flagged", n.Differs },
                { "icon", NewsIcon(n.Text) }, { "chips", chips },
            };
        }

        static string NewsIcon(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            if (t.Contains("romantic") || t.Contains("kiss")) return "heart";
            if (t.Contains("gift")) return "gift";
            if (t.Contains("promot")) return "job";
            if (t.Contains("level") || t.Contains("skill")) return "star";
            if (t.Contains("friends")) return "people";
            return "chat";
        }

        static object Chip(string label, string cls, string icon)
        { return new Dictionary<string, object> { { "label", label }, { "cls", cls }, { "icon", icon } }; }

        // the window has a fixed height (800, design rule), so long lists end in a "+N more" entry
        static List<object> Strings(IEnumerable<string> items, int max = 100)
        {
            var all = items.ToList();
            var list = all.Take(max).Select(s => (object)s).ToList();
            if (all.Count > max) list.Add("+" + (all.Count - max) + " more");
            return list;
        }
        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
        static string Letter(string name) { return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(); }
        static string Signed(short v) { return v > 0 ? "+" + v : v.ToString(); }

        // 1..6, the same colour every time for the same name (design tokens: simIdentity)
        public static int SimColor(string name)
        {
            int h = 17;
            foreach (char ch in name ?? "") h = unchecked(h * 31 + char.ToLowerInvariant(ch));
            return (Math.Abs(h) % 6) + 1;
        }
    }
}
