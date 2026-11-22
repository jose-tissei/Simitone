using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Simitone.Conversations
{
    // The fixed list of lines. Built offline (Tools/ConvoBank) so no AI call is ever needed during play.
    //   speaker[interaction][tone]            -> lines the speaker can say, one list per tone (Gentle, Playful, Bold)
    //   reply[interaction][quality][temper]   -> lines the receiver answers with; quality 1 = bad .. 3 = best,
    //                                            temper = easily_offended | thick_skinned | cold | receptive | any
    // Lines may contain {me} (speaker's name), {name} (receiver's name) and {job} (receiver's job title).
    public class BankFile
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("speaker")] public Dictionary<string, Dictionary<string, List<string>>> Speaker = new Dictionary<string, Dictionary<string, List<string>>>();
        [JsonProperty("reply")] public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> Reply = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
    }

    public class LinePair
    {
        public string Speaker, Reply;
    }

    public class DialogueBank
    {
        public const int MaxLineLength = 110;

        readonly BankFile file;
        readonly Dictionary<string, int> last = new Dictionary<string, int>();
        readonly object missLock = new object();
        readonly SortedDictionary<string, int> misses = new SortedDictionary<string, int>();

        // combinations the bank had no line for (the built-in line was used): a to-do list for the offline builder
        public IList<string> Misses { get { lock (missLock) return misses.Select(kv => kv.Key + " x" + kv.Value).ToList(); } }

        void Miss(string what) { lock (missLock) { int n; misses.TryGetValue(what, out n); misses[what] = n + 1; } }
        public bool Loaded { get; private set; }

        public DialogueBank() { file = new BankFile(); }

        DialogueBank(BankFile f) { file = f ?? new BankFile(); Loaded = f != null; }

        // an empty or damaged file means "use the built-in lines", never an exception
        public static DialogueBank Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new DialogueBank();
                return Parse(File.ReadAllText(path));
            }
            catch { return new DialogueBank(); }
        }

        public static DialogueBank Parse(string json)
        {
            try { return new DialogueBank(JsonConvert.DeserializeObject<BankFile>(json)); }
            catch { return new DialogueBank(); }
        }

        static string Tkey(Tone t) { return ((int)t).ToString(); }

        List<string> Find(string interaction, Tone tone)
        {
            Dictionary<string, List<string>> byTone;
            List<string> list;
            if (file.Speaker != null && file.Speaker.TryGetValue(interaction ?? "", out byTone) && byTone != null &&
                byTone.TryGetValue(Tkey(tone), out list) && list != null && list.Count > 0) return list;
            return null;
        }

        List<string> Find(string interaction, int quality, Temperament t)
        {
            Dictionary<string, Dictionary<string, List<string>>> byQuality;
            Dictionary<string, List<string>> byTemper;
            List<string> list;
            if (file.Reply == null || !file.Reply.TryGetValue(interaction ?? "", out byQuality) || byQuality == null) return null;
            if (!byQuality.TryGetValue(quality.ToString(), out byTemper) || byTemper == null) return null;
            if (byTemper.TryGetValue(Names.Key(t), out list) && list != null && list.Count > 0) return list;
            if (byTemper.TryGetValue("any", out list) && list != null && list.Count > 0) return list;
            return null;
        }

        string Pick(string group, IList<string> lines, Random rng)
        {
            if (lines.Count == 1) return lines[0];
            int i = rng.Next(lines.Count), prev;
            if (last.TryGetValue(group, out prev) && i == prev) i = (i + 1) % lines.Count;   // do not repeat the last line
            last[group] = i;
            return lines[i];
        }

        public string SpeakerLine(string interaction, Tone tone, Random rng)
        {
            var found = Find(interaction, tone);
            if (found == null) Miss("speaker " + interaction + " " + Names.Label(tone));
            var lines = found ?? Builtin.Speaker(tone);
            return Pick("s|" + interaction + "|" + (int)tone, lines, rng);
        }

        public string ReplyLine(string interaction, int quality, Temperament t, Random rng)
        {
            var found = Find(interaction, quality, t);
            if (found == null) Miss("reply " + interaction + " q" + quality + " " + Names.Key(t));
            var lines = found ?? Builtin.Reply(quality);
            return Pick("r|" + interaction + "|" + quality + "|" + (int)t, lines, rng);
        }

        public static string Fill(string line, string me, string name, string job, string topic = null)
        {
            return (line ?? "").Replace("{me}", me ?? "").Replace("{name}", name ?? "").Replace("{job}", string.IsNullOrWhiteSpace(job) ? "friend" : job).Replace("{topic}", string.IsNullOrWhiteSpace(topic) ? "that" : topic);
        }

        // lines that pass the quality filter used both when building the bank and when loading hand edits
        public static bool IsAcceptable(string line, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(line)) { reason = "empty"; return false; }
            var s = line.Trim();
            if (s.Length > MaxLineLength) { reason = "longer than " + MaxLineLength; return false; }
            if (s.Length < 3) { reason = "too short"; return false; }
            if (s.Contains("\n")) { reason = "has a line break"; return false; }
            if (s.Any(ch => ch > 0x24F && ch != '’' && ch != '‘' && ch != '“' && ch != '”' && ch != '…' && ch != '—' && ch != '–')) { reason = "non-latin characters"; return false; }
            var low = s.ToLowerInvariant();
            foreach (var bad in Blocked) if (low.Contains(bad)) { reason = "blocked word '" + bad + "'"; return false; }
            int open = s.Count(ch => ch == '{');
            if (open != s.Count(ch => ch == '}')) { reason = "unbalanced braces"; return false; }
            var stripped = s.Replace("{me}", "").Replace("{name}", "").Replace("{job}", "").Replace("{topic}", "");
            if (stripped.Contains("{") || stripped.Contains("}")) { reason = "unknown placeholder"; return false; }
            return true;
        }

        static readonly string[] Blocked =
        {
            "fuck", "shit", "bitch", "cunt", "nigg", "fagg", "rape", "kill yourself", "suicide", "porn", "cock", "dick", "pussy", "whore", "slut",
        };
    }

    // Used when the bank has nothing for a combination, so the feature never shows an empty line.
    static class Builtin
    {
        static readonly List<string>[] speaker =
        {
            new List<string> { "Hey {name}, I was just thinking of you.", "{name}, you always brighten my day." },
            new List<string> { "Guess what, {name}? I have a surprise for you!", "Ready for some fun, {name}?" },
            new List<string> { "{name}, let's not pretend. I want to do this.", "Listen, {name}. I will just say it." },
        };

        static readonly List<string>[] reply =
        {
            new List<string> { "Hmm. Not really my thing.", "Please, not right now." },
            new List<string> { "That is nice of you.", "Okay, sure." },
            new List<string> { "I love that. Thank you, {me}!", "You made my day!" },
        };

        public static List<string> Speaker(Tone t) { return speaker[(int)t]; }

        public static List<string> Reply(int quality) { return reply[Math.Max(1, Math.Min(3, quality)) - 1]; }
    }
}
