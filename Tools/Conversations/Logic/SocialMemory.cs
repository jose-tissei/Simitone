using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Simitone.Conversations
{
    public class OptionStats
    {
        [JsonProperty("s")] public int Successes;
        [JsonProperty("f")] public int Failures;
        [JsonProperty("last")] public int LastOutcome;     // 1 = bad, 2 = ok, 3 = great
        [JsonProperty("turn")] public int LastSeenTurn;
    }

    public class PairMemory
    {
        [JsonProperty("exchanges")] public int Exchanges;
        [JsonProperty("pos")] public int PositiveCount;
        [JsonProperty("neg")] public int NegativeCount;
        [JsonProperty("streak")] public int NegativeStreak;
        [JsonProperty("backoff")] public int BackoffRemaining;
        [JsonProperty("interactions")] public Dictionary<string, OptionStats> Interactions = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("subjects")] public Dictionary<string, OptionStats> Subjects = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("tones")] public Dictionary<string, OptionStats> Tones = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("interests")] public HashSet<int> LearnedInterests = new HashSet<int>();
        [JsonProperty("skills")] public HashSet<string> LearnedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    // Per-pair learned social memory and anti-negative-loop guard.
    // Thread-safe: may be read on worker threads (planning) and updated on the VM tick thread.
    public class SocialMemory
    {
        public const int DeescalateThreshold = 2;
        public const int BackoffThreshold = 3;
        public const int DefaultBackoffCycles = 3;

        static readonly HashSet<string> RiskyInteractions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tease", "brag", "flirt", "insult", "scare", "slap" };

        readonly object gate = new object();
        Dictionary<string, PairMemory> pairs = new Dictionary<string, PairMemory>(StringComparer.OrdinalIgnoreCase);

        public int PairCount { get { lock (gate) return pairs.Count; } }

        public static string PairKey(RawSim speaker, RawSim listener)
        {
            return (speaker == null ? "?" : speaker.MemoryKey()) + "->" + (listener == null ? "?" : listener.MemoryKey());
        }

        public PairMemory GetPair(RawSim speaker, RawSim listener)
        {
            var key = PairKey(speaker, listener);
            lock (gate)
            {
                PairMemory p;
                if (!pairs.TryGetValue(key, out p) || p == null) return new PairMemory();
                return ClonePair(p);
            }
        }

        PairMemory GetOrCreateUnlocked(string key)
        {
            PairMemory p;
            if (!pairs.TryGetValue(key, out p) || p == null) { p = new PairMemory(); pairs[key] = p; }
            if (p.Interactions == null) p.Interactions = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.Subjects == null) p.Subjects = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.Tones == null) p.Tones = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.LearnedInterests == null) p.LearnedInterests = new HashSet<int>();
            if (p.LearnedSkills == null) p.LearnedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return p;
        }

        public void Record(RawSim speaker, RawSim listener, string interaction, Tone tone, string subjectId, int quality, int listenerInterestIndex = -1, SkillKind listenerSkill = SkillKind.None)
        {
            if (speaker == null || listener == null) return;
            var key = PairKey(speaker, listener);
            lock (gate)
            {
                var p = GetOrCreateUnlocked(key);
                p.Exchanges++;
                if (quality <= 1)
                {
                    p.NegativeCount++;
                    p.NegativeStreak++;
                    if (p.NegativeStreak >= BackoffThreshold) p.BackoffRemaining = DefaultBackoffCycles;
                }
                else if (quality >= 3)
                {
                    p.PositiveCount++;
                    p.NegativeStreak = 0;
                    p.BackoffRemaining = 0;
                }
                else
                {
                    p.NegativeStreak = Math.Max(0, p.NegativeStreak - 1);
                }

                if (!string.IsNullOrEmpty(interaction)) UpdateStats(p.Interactions, interaction.ToLowerInvariant(), quality, p.Exchanges);
                UpdateStats(p.Tones, ((int)tone).ToString(), quality, p.Exchanges);
                if (!string.IsNullOrEmpty(subjectId)) UpdateStats(p.Subjects, subjectId, quality, p.Exchanges);
                if (listenerInterestIndex >= 0 && listenerInterestIndex < 8) p.LearnedInterests.Add(listenerInterestIndex);
                if (listenerSkill != SkillKind.None) p.LearnedSkills.Add(listenerSkill.ToString());
            }
        }

        static void UpdateStats(Dictionary<string, OptionStats> map, string key, int quality, int turn)
        {
            OptionStats st;
            if (!map.TryGetValue(key, out st) || st == null) { st = new OptionStats(); map[key] = st; }
            if (quality <= 1) st.Failures++;
            else if (quality >= 3) st.Successes++;
            st.LastOutcome = quality;
            st.LastSeenTurn = turn;
        }

        public bool IsDeescalating(RawSim speaker, RawSim listener)
        {
            lock (gate)
            {
                PairMemory p;
                return pairs.TryGetValue(PairKey(speaker, listener), out p) && p != null && p.NegativeStreak >= DeescalateThreshold;
            }
        }

        public bool IsInteractionForbidden(RawSim speaker, RawSim listener, string interaction)
        {
            if (string.IsNullOrEmpty(interaction)) return false;
            return IsDeescalating(speaker, listener) && RiskyInteractions.Contains(interaction);
        }

        // returns true if the pair should cool down (skipping this autonomous trigger) and decrements remaining backoff
        public bool ConsumeBackoff(RawSim speaker, RawSim listener)
        {
            lock (gate)
            {
                PairMemory p;
                if (!pairs.TryGetValue(PairKey(speaker, listener), out p) || p == null || p.BackoffRemaining <= 0) return false;
                p.BackoffRemaining--;
                return true;
            }
        }

        public bool HasLearnedInterest(RawSim speaker, RawSim listener, int interestIndex)
        {
            lock (gate)
            {
                PairMemory p;
                return pairs.TryGetValue(PairKey(speaker, listener), out p) && p?.LearnedInterests != null && p.LearnedInterests.Contains(interestIndex);
            }
        }

        public bool HasLearnedSkill(RawSim speaker, RawSim listener, SkillKind skill)
        {
            if (skill == SkillKind.None) return false;
            lock (gate)
            {
                PairMemory p;
                return pairs.TryGetValue(PairKey(speaker, listener), out p) && p?.LearnedSkills != null && p.LearnedSkills.Contains(skill.ToString());
            }
        }

        static double ScoreFromStats(OptionStats st, int currentTurn)
        {
            if (st == null) return 0.0;
            int age = Math.Max(0, currentTurn - st.LastSeenTurn);
            double decay = 1.0 / (1.0 + age * 0.25);
            double score = 0.0;
            if (st.LastOutcome == 1)
                score -= Math.Min(0.85, (0.45 + 0.15 * Math.Min(3, st.Failures)) * decay);
            else if (st.Failures > st.Successes)
                score -= Math.Min(0.45, 0.15 * (st.Failures - st.Successes) * decay);
            if (st.Successes > st.Failures)
                score += Math.Min(0.30, 0.12 * (st.Successes - st.Failures));
            return score;
        }

        public double InteractionAdjustment(RawSim speaker, RawSim listener, string interaction)
        {
            if (string.IsNullOrEmpty(interaction)) return 0.0;
            var k = interaction.ToLowerInvariant();
            lock (gate)
            {
                PairMemory p;
                if (!pairs.TryGetValue(PairKey(speaker, listener), out p) || p == null) return 0.0;
                double adj = 0.0;
                OptionStats st;
                if (p.Interactions != null && p.Interactions.TryGetValue(k, out st)) adj += ScoreFromStats(st, p.Exchanges);
                if (p.NegativeStreak >= DeescalateThreshold)
                {
                    if (RiskyInteractions.Contains(k)) adj -= 1.0;
                    else if (k == "apologize") adj += 0.45;
                    else if (k == "talk") adj += 0.35;
                    else if (k == "cheerup") adj += 0.25;
                    else if (k == "compliment") adj += 0.20;
                }
                return adj;
            }
        }

        public double SubjectAdjustment(RawSim speaker, RawSim listener, string subjectId)
        {
            if (string.IsNullOrEmpty(subjectId)) return 0.0;
            lock (gate)
            {
                PairMemory p;
                if (!pairs.TryGetValue(PairKey(speaker, listener), out p) || p == null) return 0.0;
                OptionStats st;
                return p.Subjects != null && p.Subjects.TryGetValue(subjectId, out st) ? ScoreFromStats(st, p.Exchanges) : 0.0;
            }
        }

        public double ToneAdjustment(RawSim speaker, RawSim listener, Tone tone)
        {
            lock (gate)
            {
                PairMemory p;
                if (!pairs.TryGetValue(PairKey(speaker, listener), out p) || p == null) return 0.0;
                double adj = 0.0;
                OptionStats st;
                if (p.Tones != null && p.Tones.TryGetValue(((int)tone).ToString(), out st)) adj += ScoreFromStats(st, p.Exchanges) * 0.6;
                if (p.NegativeStreak >= DeescalateThreshold)
                {
                    if (tone == Tone.Gentle) adj += 0.30;
                    else if (tone == Tone.Bold) adj -= 0.70;
                    else adj -= 0.15;
                }
                return adj;
            }
        }

        // short natural-language lessons included in the Laya prompt so Laya knows what went badly before
        public string LessonsNote(RawSim speaker, RawSim listener)
        {
            var p = GetPair(speaker, listener);
            if (p.Exchanges == 0) return "";
            var notes = new List<string>();
            var failed = p.Interactions.Where(kv => kv.Value != null && kv.Value.LastOutcome == 1).Select(kv => kv.Key).Take(3).ToList();
            if (failed.Count > 0) notes.Add("Recently " + string.Join(", ", failed) + " went poorly with " + listener.Name + ".");
            var worked = p.Interactions.Where(kv => kv.Value != null && kv.Value.Successes > kv.Value.Failures).Select(kv => kv.Key).Take(2).ToList();
            if (worked.Count > 0) notes.Add(listener.Name + " responded well to " + string.Join(", ", worked) + ".");
            if (p.NegativeStreak >= DeescalateThreshold) notes.Add("Recent exchanges were tense, so a gentle or apologetic approach is much safer.");
            return string.Join(" ", notes);
        }

        public string Summary()
        {
            lock (gate)
            {
                if (pairs.Count == 0) return "empty";
                return string.Join(" | ", pairs.Take(6).Select(kv => kv.Key + ":ex=" + kv.Value.Exchanges + ",neg=" + kv.Value.NegativeCount + ",streak=" + kv.Value.NegativeStreak));
            }
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string json;
                lock (gate) json = JsonConvert.SerializeObject(pairs, Formatting.Indented);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            catch { }
        }

        public static SocialMemory Load(string path)
        {
            var mem = new SocialMemory();
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return mem;
                var data = JsonConvert.DeserializeObject<Dictionary<string, PairMemory>>(File.ReadAllText(path));
                if (data != null) mem.pairs = new Dictionary<string, PairMemory>(data, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return mem;
        }

        static PairMemory ClonePair(PairMemory p)
        {
            var c = new PairMemory
            {
                Exchanges = p.Exchanges,
                PositiveCount = p.PositiveCount,
                NegativeCount = p.NegativeCount,
                NegativeStreak = p.NegativeStreak,
                BackoffRemaining = p.BackoffRemaining,
                LearnedInterests = p.LearnedInterests != null ? new HashSet<int>(p.LearnedInterests) : new HashSet<int>(),
                LearnedSkills = p.LearnedSkills != null ? new HashSet<string>(p.LearnedSkills, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
            if (p.Interactions != null) foreach (var kv in p.Interactions) c.Interactions[kv.Key] = kv.Value;
            if (p.Subjects != null) foreach (var kv in p.Subjects) c.Subjects[kv.Key] = kv.Value;
            if (p.Tones != null) foreach (var kv in p.Tones) c.Tones[kv.Key] = kv.Value;
            return c;
        }
    }
}
