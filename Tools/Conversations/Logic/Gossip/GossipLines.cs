using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Simitone.Conversations
{
    // The hand-written wording for gossip (bank.json "gossip"). Placeholders besides {name} (the listener) and {me} (the speaker):
    //   {who} {with}   the people the news is about, as the speaker remembers them
    //   {label}        job title or skill name      {amount}   a skill level      {when}   "today", "yesterday", "3 days ago"
    //   {src}          the Sim who told the speaker
    // A line is only used when everything it mentions is part of what the Sim really says, so a Sim who forgot the day never says one.
    public class GossipLineBank
    {
        [JsonProperty("greet")] public Dictionary<string, List<string>> Greet = new Dictionary<string, List<string>>();       // by tone 0..2
        [JsonProperty("bye")] public Dictionary<string, List<string>> Bye = new Dictionary<string, List<string>>();         // by tone
        [JsonProperty("lead")] public Dictionary<string, List<string>> Lead = new Dictionary<string, List<string>>();       // by tone
        [JsonProperty("tell")] public Dictionary<string, Dictionary<string, List<string>>> Tell = new Dictionary<string, Dictionary<string, List<string>>>();   // kind -> sure|hedged
        [JsonProperty("ask")] public Dictionary<string, List<string>> Ask = new Dictionary<string, List<string>>();         // by kind
        [JsonProperty("reaction")] public Dictionary<string, List<string>> Reaction = new Dictionary<string, List<string>>();   // surprised|knewit|doubtful|filledgap
        [JsonProperty("noidea")] public List<string> NoIdea = new List<string>();
        [JsonProperty("source")] public List<string> Source = new List<string>();
        [JsonProperty("firsthand")] public List<string> FirstHand = new List<string>();
        [JsonProperty("aboutme")] public List<string> AboutMe = new List<string>();       // when the news turns out to be about the speaker

        public bool IsEmpty { get { return Tell.Count == 0 && Greet.Count == 0; } }

        public static readonly string[] PlaceholderNames = { "name", "me", "who", "with", "label", "amount", "when", "src" };

        public IEnumerable<string> AllLines()
        {
            foreach (var l in Greet.Values.Concat(Bye.Values).Concat(Lead.Values).Concat(Ask.Values).Concat(Reaction.Values)) foreach (var s in l) yield return s;
            foreach (var k in Tell.Values) foreach (var l in k.Values) foreach (var s in l) yield return s;
            foreach (var s in NoIdea.Concat(Source).Concat(FirstHand).Concat(AboutMe)) yield return s;
        }
    }

    public class GossipContext
    {
        public GossipBook Book;
        public GossipLineBank Lines;
        public int Today;
        public string SpeakerName, ListenerName;
        public Tone Tone;
    }

    public static class GossipLines
    {
        public const int MaxLength = 150;

        static readonly Regex Placeholder = new Regex(@"\{(\w+)\}", RegexOptions.Compiled);

        public static Tone ToneOf(RawSim s)
        {
            s = s ?? new RawSim();
            if (s.Nice < 350 || (s.Outgoing > 700 && s.Nice < 550)) return Tone.Bold;
            if (s.Playful >= 600) return Tone.Playful;
            return Tone.Gentle;
        }

        public static string DayPhrase(int today, int day)
        {
            int n = today - day;
            if (n <= 0) return "today";
            if (n == 1) return "yesterday";
            string[] words = { "", "", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
            return (n < words.Length ? words[n] : n.ToString()) + " days ago";
        }

        // A turn as a speech balloon. The same turn, tone, bank and generator always give the same words.
        public static string Render(GossipTurn turn, GossipContext c, ref GossipRng rng)
        {
            var bank = c.Lines ?? new GossipLineBank();
            string body;
            switch (turn.Act)
            {
                case GossipAct.Greet: body = One(Pool(bank.Greet, (int)c.Tone), BuiltIn.Greet, ref rng); break;
                case GossipAct.Bye:
                case GossipAct.None: body = One(Pool(bank.Bye, (int)c.Tone), BuiltIn.Bye, ref rng); break;
                case GossipAct.NoIdea: body = One(bank.NoIdea, BuiltIn.NoIdea, ref rng); break;
                case GossipAct.Ask: body = RenderAsk(turn, c, bank, ref rng); break;
                default: body = RenderTell(turn, c, bank, ref rng); break;
            }
            string reaction = ReactionLine(turn.ReactionToPrevious, bank, ref rng);
            var parts = new List<string>();
            if (reaction != null) parts.Add(reaction);
            parts.Add(body);
            var text = string.Join(" ", parts);
            if (text.Length > MaxLength && reaction != null) text = body;   // keep the balloon short: the news matters more than the reaction
            return Fill(text, c, turn, null);
        }

        static string ReactionLine(Reaction r, GossipLineBank bank, ref GossipRng rng)
        {
            if (r == Reaction.None) return null;
            List<string> list;
            string key = r == Reaction.Surprised ? "surprised" : r == Reaction.KnewIt ? "knewit" : r == Reaction.Doubtful ? "doubtful" : "filledgap";
            if (!bank.Reaction.TryGetValue(key, out list) || list == null || list.Count == 0) list = BuiltIn.ReactionFor(r);
            return list[rng.Below(list.Count)];
        }

        static string RenderAsk(GossipTurn turn, GossipContext c, GossipLineBank bank, ref GossipRng rng)
        {
            List<string> list;
            bank.Ask.TryGetValue(FactRules.Key(turn.Kind), out list);
            var usable = Usable(list, SpeakerSlots(turn));
            return One(usable.Count > 0 ? usable : BuiltIn.Ask, null, ref rng);
        }

        static HashSet<string> SpeakerSlots(GossipTurn turn)
        {
            var s = new HashSet<string> { "name", "me" };
            if (turn.SpeakerWho != FactRules.NoValue) s.Add("who");
            if (turn.SpeakerWith != FactRules.NoValue) s.Add("with");
            return s;
        }

        static string RenderTell(GossipTurn turn, GossipContext c, GossipLineBank bank, ref GossipRng rng)
        {
            var told = new HashSet<string> { "name", "me" };
            bool hedged = false;
            var claim = turn.Claim;
            // answering a question about themselves: a Sim does not narrate their own news in the third person
            if ((claim.Has(Slot.Who) && claim.Values[(int)Slot.Who] == turn.Speaker) || (claim.Has(Slot.With) && claim.Values[(int)Slot.With] == turn.Speaker))
                return One(bank.AboutMe, BuiltIn.AboutMe, ref rng);
            if (claim.Has(Slot.Who)) { told.Add("who"); hedged |= claim.Guess[(int)Slot.Who]; }
            if (claim.Has(Slot.With)) { told.Add("with"); hedged |= claim.Guess[(int)Slot.With]; }
            if (claim.Has(Slot.Amount)) { told.Add("amount"); hedged |= claim.Guess[(int)Slot.Amount]; }
            if (claim.Has(Slot.When)) { told.Add("when"); hedged |= claim.Guess[(int)Slot.When]; }
            if (!string.IsNullOrEmpty(turn.Label)) told.Add("label");

            Dictionary<string, List<string>> byCertainty;
            List<string> list = null;
            if (bank.Tell.TryGetValue(FactRules.Key(turn.Kind), out byCertainty)) byCertainty.TryGetValue(hedged ? "hedged" : "sure", out list);
            var usable = Usable(list, told, turn.ToldBy == 0);
            string news = One(usable.Count > 0 ? usable : BuiltIn.Tell(turn.Kind, hedged), null, ref rng);

            // a lead-in in the speaker's tone, and how they know it
            bool reacting = turn.ReactionToPrevious != Reaction.None;
            string lead = rng.Chance(reacting ? 150000u : 600000u) ? One(Pool(bank.Lead, (int)turn.Tone), null, ref rng, true) : null;
            string how = null;
            if (turn.ToldBy == 0 && !hedged && rng.Chance(450000)) how = One(bank.FirstHand, null, ref rng, true);
            else if (turn.ToldBy != 0 && turn.ToldBy != turn.Listener && rng.Chance(550000)) { var src = One(bank.Source, null, ref rng, true); if (src != null) how = src.Replace("{src}", c.Book == null ? "someone" : c.Book.NameOf(turn.ToldBy)); }
            var text = news;
            if (lead != null && (lead + " " + text).Length <= MaxLength) text = lead + " " + text;
            if (how != null && (text + " " + how).Length <= MaxLength) text = text + " " + how;
            return text;
        }

        static List<string> Pool(Dictionary<string, List<string>> byTone, int tone)
        {
            List<string> l;
            return byTone != null && byTone.TryGetValue(tone.ToString(), out l) ? l : null;
        }

        // the lines whose placeholders are all things the speaker really says
        static readonly Regex Hearsay = new Regex(@"\bI heard\b|\bWord is\b|\bSomebody (said|told)\b|\bor so I heard\b|\bapparently\b|\bPeople say\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool IsHearsay(string line) { return Hearsay.IsMatch(line ?? ""); }

        // `firstHand`: the speaker saw it happen, so lines that say they only heard it are left out
        public static List<string> Usable(List<string> list, ISet<string> available, bool firstHand = false)
        {
            var result = new List<string>();
            if (list == null) return result;
            foreach (var line in list)
            {
                bool ok = !(firstHand && IsHearsay(line));
                foreach (Match m in Placeholder.Matches(line))
                    if (m.Groups[1].Value != "src" && !available.Contains(m.Groups[1].Value)) { ok = false; break; }
                if (ok) result.Add(line);
            }
            return result;
        }

        static string One(List<string> list, List<string> fallback, ref GossipRng rng, bool allowNull = false)
        {
            if (list == null || list.Count == 0) list = fallback;
            if (list == null || list.Count == 0) return allowNull ? null : "";
            return list[rng.Below(list.Count)];
        }

        static string Fill(string text, GossipContext c, GossipTurn turn, object unused)
        {
            var book = c.Book;
            Func<int, string> nm = id => book == null || id == FactRules.NoValue ? "someone" : book.NameOf(id);
            int who = turn.Claim != null && turn.Claim.Has(Slot.Who) ? turn.Claim.Values[(int)Slot.Who] : turn.SpeakerWho;
            int with = turn.Claim != null && turn.Claim.Has(Slot.With) ? turn.Claim.Values[(int)Slot.With] : turn.SpeakerWith;
            text = text.Replace("{who}", nm(who)).Replace("{with}", nm(with));
            text = text.Replace("{label}", turn.Label ?? "").Replace("{name}", c.ListenerName ?? "").Replace("{me}", c.SpeakerName ?? "");
            if (turn.Claim != null)
            {
                if (turn.Claim.Has(Slot.Amount)) text = text.Replace("{amount}", turn.Claim.Values[(int)Slot.Amount].ToString());
                if (turn.Claim.Has(Slot.When)) text = text.Replace("{when}", DayPhrase(c.Today, turn.Claim.Values[(int)Slot.When]));
            }
            return text;
        }

        // Lines that always exist, so a bank without gossip (or with every line dropped) still gives complete sentences.
        static class BuiltIn
        {
            public static readonly List<string> Greet = new List<string> { "Hello, {name}.", "Hi, {name}!" };
            public static readonly List<string> Bye = new List<string> { "See you later, {name}.", "I have to go. Bye!" };
            public static readonly List<string> AboutMe = new List<string> { "That one is about me, actually!", "Ha, that is about me, {name}." };
            public static readonly List<string> NoIdea = new List<string> { "I haven't heard anything about that.", "No idea, I'm afraid." };
            public static readonly List<string> Ask = new List<string> { "What did you hear about {who}?", "Do you know anything about {who}?" };

            public static List<string> Tell(FactKind k, bool hedged)
            {
                return new List<string> { hedged ? "I heard something about {who}, but I might be wrong." : "I have some news about {who}." };
            }

            public static List<string> ReactionFor(Reaction r)
            {
                switch (r)
                {
                    case Reaction.Surprised: return new List<string> { "Really? I didn't know that!" };
                    case Reaction.KnewIt: return new List<string> { "Yes, I heard that too." };
                    case Reaction.Doubtful: return new List<string> { "Hm, that's not how I heard it." };
                    default: return new List<string> { "Ah, that's right, thank you." };
                }
            }
        }
    }
}
