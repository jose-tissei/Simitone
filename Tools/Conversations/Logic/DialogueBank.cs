using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Simitone.Conversations
{
    // The fixed list of lines. Written by hand (bank/lines/*.txt, assembled into bank.json by bank/Build-Bank.ps1), so no AI call is ever needed during play.
    //   speaker[interaction][tone]            -> lines the speaker can say, one list per tone (Gentle, Playful, Bold)
    //   reply[interaction][quality][temper]   -> lines the receiver answers with; quality 1 = bad .. 3 = best,
    //                                            temper = easily_offended | thick_skinned | cold | receptive | any
    // Lines may contain {me} (speaker's name), {name} (receiver's name) and {job} (receiver's job title).
    public class BankFile
    {
        [JsonProperty("version")] public int Version = 1;
        [JsonProperty("speaker")] public Dictionary<string, Dictionary<string, List<string>>> Speaker = new Dictionary<string, Dictionary<string, List<string>>>();
        [JsonProperty("reply")] public Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>> Reply = new Dictionary<string, Dictionary<string, Dictionary<string, List<string>>>>();
        [JsonProperty("subjects")] public List<AiSubject> Subjects = new List<AiSubject>();
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
        public IList<AiSubject> RawSubjects { get { return file.Subjects ?? new List<AiSubject>(); } }

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
            try
            {
                var f = JsonConvert.DeserializeObject<BankFile>(json);
                var dropped = Scrub(f);
                return new DialogueBank(f) { Dropped = dropped };
            }
            catch { return new DialogueBank(); }
        }

        // lines removed while loading because they failed the style filter (see IsCleanLine)
        public int Dropped { get; private set; }

        // Applies Tidy + IsCleanLine to every speaker and reply line, drops what fails and leaves emptied lists empty
        // (Find treats an empty list as missing, so the built-in lines are used). Returns how many lines were dropped.
        static int Scrub(BankFile f)
        {
            if (f == null) return 0;
            int dropped = 0;
            Func<List<string>, List<string>> clean = list =>
            {
                var kept = new List<string>();
                foreach (var raw in list ?? new List<string>())
                {
                    var s = Tidy(raw);
                    string reason;
                    if (IsCleanLine(s, out reason)) kept.Add(s); else dropped++;
                }
                return kept;
            };
            if (f.Speaker != null)
                foreach (var byTone in f.Speaker.Values.Where(v => v != null))
                    foreach (var k in byTone.Keys.ToList()) byTone[k] = clean(byTone[k]);
            if (f.Reply != null)
                foreach (var byQuality in f.Reply.Values.Where(v => v != null))
                    foreach (var byTemper in byQuality.Values.Where(v => v != null))
                        foreach (var k in byTemper.Keys.ToList()) byTemper[k] = clean(byTemper[k]);
            return dropped;
        }

        // curly single quotes become plain apostrophes (they look the same in the game font and are easier to edit); repeated spaces are collapsed
        public static string Tidy(string line)
        {
            if (line == null) return null;
            var s = line.Replace('’', '\'').Replace('‘', '\'');
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        // Style rules for a line in a speech balloon, on top of IsAcceptable: no markdown asterisks, no stray or curly double quotes,
        // no dashes used as punctuation, and it must end like a sentence.
        public static bool IsCleanLine(string line, out string reason)
        {
            if (!IsAcceptable(line, out reason)) return false;
            var s = line.Trim();
            if (s.Contains("*")) { reason = "asterisk"; return false; }
            if (s.Contains("–") || s.Contains("—") || s.Contains(" - ")) { reason = "dash"; return false; }
            if (s.Contains("\"") || s.Contains("“") || s.Contains("”") || s.Contains("‘")) { reason = "quote marks"; return false; }
            var end = s[s.Length - 1];
            if (end != '.' && end != '!' && end != '?' && end != '…') { reason = "no end punctuation"; return false; }
            return true;
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

        // safety filter (length, characters, blocked words, placeholders); IsCleanLine adds the style rules applied when the bank is loaded
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
