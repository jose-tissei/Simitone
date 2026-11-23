using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public enum Tone { Gentle = 0, Playful = 1, Bold = 2 }

    public enum Temperament { Unknown = 0, EasilyOffended, ThickSkinned, Cold, Receptive }

    public static class Names
    {
        public static readonly string[] ToneLabels = { "Gentle", "Playful", "Bold" };

        public static string Label(Tone t) { return ToneLabels[(int)t]; }

        // the category names Laya is asked to choose between (see ConvoText.TemperamentQuestion)
        public static string Key(Temperament t)
        {
            switch (t)
            {
                case Temperament.EasilyOffended: return "easily_offended";
                case Temperament.ThickSkinned: return "thick_skinned";
                case Temperament.Cold: return "cold";
                case Temperament.Receptive: return "receptive";
                default: return "any";
            }
        }

        public static Temperament ParseTemperament(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "easily_offended": return Temperament.EasilyOffended;
                case "thick_skinned": return Temperament.ThickSkinned;
                case "cold": return Temperament.Cold;
                case "receptive": return Temperament.Receptive;
                default: return Temperament.Unknown;
            }
        }
    }

    public enum SkillKind { None = -1, Cooking = 0, Charisma = 1, Mechanical = 2, Creativity = 3, Body = 4, Logic = 5 }

    // What the game tells us about one Sim. Personality and skills are 0..1000, mood and motives are -100..100.
    public class RawSim
    {
        public string Name = "Someone";
        public short NeighborId;
        public short Nice = 500, Active = 500, Generous = 500, Playful = 500, Outgoing = 500, Neat = 500;
        public short Mood;
        public short Social = 100;      // -100..100; 60% = +20
        public short Cooking, Charisma, Mechanical, Creativity, Body, Logic;   // 0..1000 (100 per skill point)
        public short StrToTarget, LtrToTarget;       // speaker -> listener (-100..100)
        public short TargetStrToMe, TargetLtrToMe;   // listener -> speaker (-100..100)
        public short Age = 20;          // 0 = baby/toddler, under 18 = child
        public bool Female;
        public string Career = "";     // e.g. "Military", empty when unemployed
        public string JobTitle = "";   // e.g. "Recruit"
        public short[] Interests = new short[8];

        public static double SocialPercent(short socialMotive)
        {
            return Math.Max(0.0, Math.Min(100.0, (socialMotive + 100.0) * 0.5));
        }

        public short SkillValue(SkillKind kind)
        {
            switch (kind)
            {
                case SkillKind.Cooking: return Cooking;
                case SkillKind.Charisma: return Charisma;
                case SkillKind.Mechanical: return Mechanical;
                case SkillKind.Creativity: return Creativity;
                case SkillKind.Body: return Body;
                case SkillKind.Logic: return Logic;
                default: return 0;
            }
        }

        public SkillKind[] TopSkills(int minLevel = 300)
        {
            var all = new[] { SkillKind.Cooking, SkillKind.Charisma, SkillKind.Mechanical, SkillKind.Creativity, SkillKind.Body, SkillKind.Logic };
            return all.Where(k => SkillValue(k) >= minLevel).OrderByDescending(k => SkillValue(k)).ToArray();
        }

        public string MemoryKey()
        {
            if (NeighborId > 0) return "n:" + NeighborId;
            return "name:" + (Name ?? "someone").Trim().ToLowerInvariant();
        }
    }

    // A Sim reduced to a few buckets, so the dialogue bank stays small and Laya gets a short description.
    public class TraitProfile
    {
        public string Name;
        public char Nice, Outgoing, Playful;   // L, M or H
        public string Mood;                    // bad, ok, good
        public string CareerGroup;             // military, medical, ... or "none"
        public string JobTitle;
        public bool IsChild;

        public static char Level(short v) { return v < 334 ? 'L' : (v < 667 ? 'M' : 'H'); }

        public static string MoodLevel(short mood) { return mood < -30 ? "bad" : (mood > 30 ? "good" : "ok"); }

        public static string CareerGroupOf(string career)
        {
            var c = (career ?? "").ToLowerInvariant();
            if (c.Length == 0) return "none";
            var table = new[]
            {
                new[] { "milit", "military" }, new[] { "crim", "criminal" }, new[] { "medic", "medical" }, new[] { "scien", "science" },
                new[] { "law", "law" }, new[] { "polic", "law" }, new[] { "culin", "culinary" }, new[] { "entert", "entertainment" },
                new[] { "polit", "politics" }, new[] { "athl", "athlete" }, new[] { "sport", "athlete" }, new[] { "xtreme", "xtreme" },
                new[] { "extreme", "xtreme" }, new[] { "busin", "business" },
            };
            foreach (var row in table) if (c.Contains(row[0])) return row[1];
            return "other";
        }

        public static TraitProfile From(RawSim s)
        {
            s = s ?? new RawSim();
            return new TraitProfile
            {
                Name = string.IsNullOrWhiteSpace(s.Name) ? "Someone" : s.Name.Trim(),
                Nice = Level(s.Nice), Outgoing = Level(s.Outgoing), Playful = Level(s.Playful),
                Mood = MoodLevel(s.Mood),
                CareerGroup = CareerGroupOf(s.Career),
                JobTitle = (s.JobTitle ?? "").Trim(),
                IsChild = s.Age < 18,
            };
        }

        // stable text used as a cache key; two Sims with the same buckets share it, the name keeps them apart
        public string Key { get { return Name + "|" + Nice + Outgoing + Playful + "|" + Mood + "|" + CareerGroup; } }

        static string NiceWord(char l) { return l == 'H' ? "very nice" : (l == 'M' ? "fairly nice" : "not nice at all"); }
        static string OutgoingWord(char l) { return l == 'H' ? "very outgoing" : (l == 'M' ? "sociable" : "shy"); }
        static string PlayfulWord(char l) { return l == 'H' ? "very playful" : (l == 'M' ? "playful at times" : "serious"); }
        static string MoodWord(string m) { return m == "good" ? "a great mood" : (m == "bad" ? "a bad mood" : "an average mood"); }

        // The description sent to Laya; wording follows the examples that classified well in the spike.
        public string Summary
        {
            get
            {
                var s = Name + " is " + NiceWord(Nice) + ", " + OutgoingWord(Outgoing) + ", " + PlayfulWord(Playful) + ", and in " + MoodWord(Mood) + ".";
                if (CareerGroup != "none") s += " " + Name + " works as a " + (JobTitle.Length > 0 ? JobTitle : CareerGroup) + ".";
                return s;
            }
        }
    }
}
