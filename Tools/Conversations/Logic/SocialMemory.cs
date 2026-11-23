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
        [JsonProperty("speakerKey")] public string SpeakerKey;
        [JsonProperty("speakerName")] public string SpeakerName;
        [JsonProperty("listenerKey")] public string ListenerKey;
        [JsonProperty("listenerName")] public string ListenerName;
        [JsonProperty("exchanges")] public int Exchanges;
        [JsonProperty("pos")] public int PositiveCount;
        [JsonProperty("neg")] public int NegativeCount;
        [JsonProperty("streak")] public int NegativeStreak;
        [JsonProperty("backoff")] public int BackoffRemaining;
        [JsonProperty("lastStr")] public short LastStr;
        [JsonProperty("lastLtr")] public short LastLtr;
        [JsonProperty("updatedUtc")] public long LastUpdatedUtc;
        [JsonProperty("interactions")] public Dictionary<string, OptionStats> Interactions = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("subjects")] public Dictionary<string, OptionStats> Subjects = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("subjectTitles")] public Dictionary<string, string> SubjectTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("tones")] public Dictionary<string, OptionStats> Tones = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("interests")] public HashSet<int> LearnedInterests = new HashSet<int>();
        [JsonProperty("interestScores")] public Dictionary<int, int> LearnedInterestScores = new Dictionary<int, int>();
        [JsonProperty("skills")] public HashSet<string> LearnedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("skillLevels")] public Dictionary<string, int> LearnedSkillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        [JsonProperty("traits")] public List<string> LearnedTraits = new List<string>();
        [JsonProperty("temperament")] public string LearnedTemperament;
        [JsonProperty("career")] public string LearnedCareer;
    }

    public class SpeakerIdentity
    {
        public string Key;
        public string Name;
        public int KnownCount;
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

        public List<PairMemory> GetKnownSims(RawSim speaker)
        {
            if (speaker == null) return new List<PairMemory>();
            return GetKnownSims(speaker.MemoryKey());
        }

        public List<PairMemory> GetKnownSims(string speakerKey)
        {
            if (string.IsNullOrWhiteSpace(speakerKey)) return new List<PairMemory>();
            var prefix = speakerKey + "->";
            lock (gate)
            {
                var list = new List<PairMemory>();
                foreach (var kv in pairs)
                {
                    if (kv.Value == null) continue;
                    var spKey = !string.IsNullOrEmpty(kv.Value.SpeakerKey) ? kv.Value.SpeakerKey : ExtractKeyPart(kv.Key, true);
                    if (!string.Equals(spKey, speakerKey, StringComparison.OrdinalIgnoreCase) &&
                        !kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var clone = ClonePair(kv.Value);
                    if (string.IsNullOrEmpty(clone.SpeakerKey)) clone.SpeakerKey = spKey;
                    if (string.IsNullOrEmpty(clone.ListenerKey)) clone.ListenerKey = ExtractKeyPart(kv.Key, false);
                    if (string.IsNullOrEmpty(clone.ListenerName)) clone.ListenerName = clone.ListenerKey;
                    list.Add(clone);
                }
                return list.OrderByDescending(x => x.Exchanges).ThenByDescending(x => x.LastUpdatedUtc).ThenBy(x => x.ListenerName, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public List<SpeakerIdentity> GetAllSpeakers()
        {
            lock (gate)
            {
                var map = new Dictionary<string, SpeakerIdentity>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in pairs)
                {
                    if (kv.Value == null) continue;
                    var spKey = !string.IsNullOrEmpty(kv.Value.SpeakerKey) ? kv.Value.SpeakerKey : ExtractKeyPart(kv.Key, true);
                    var spName = !string.IsNullOrEmpty(kv.Value.SpeakerName) ? kv.Value.SpeakerName : spKey;
                    SpeakerIdentity id;
                    if (!map.TryGetValue(spKey, out id))
                    {
                        id = new SpeakerIdentity { Key = spKey, Name = spName, KnownCount = 0 };
                        map[spKey] = id;
                    }
                    else if (string.Equals(id.Name, id.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(kv.Value.SpeakerName))
                    {
                        id.Name = kv.Value.SpeakerName;
                    }
                    id.KnownCount++;
                }
                return map.Values.OrderByDescending(x => x.KnownCount).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        static string ExtractKeyPart(string pairKey, bool speaker)
        {
            if (string.IsNullOrEmpty(pairKey)) return "?";
            int idx = pairKey.IndexOf("->", StringComparison.Ordinal);
            if (idx < 0) return pairKey;
            return speaker ? pairKey.Substring(0, idx) : pairKey.Substring(idx + 2);
        }

        PairMemory GetOrCreateUnlocked(string key)
        {
            PairMemory p;
            if (!pairs.TryGetValue(key, out p) || p == null) { p = new PairMemory(); pairs[key] = p; }
            if (p.Interactions == null) p.Interactions = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.Subjects == null) p.Subjects = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.SubjectTitles == null) p.SubjectTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (p.Tones == null) p.Tones = new Dictionary<string, OptionStats>(StringComparer.OrdinalIgnoreCase);
            if (p.LearnedInterests == null) p.LearnedInterests = new HashSet<int>();
            if (p.LearnedInterestScores == null) p.LearnedInterestScores = new Dictionary<int, int>();
            if (p.LearnedSkills == null) p.LearnedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.LearnedSkillLevels == null) p.LearnedSkillLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (p.LearnedTraits == null) p.LearnedTraits = new List<string>();
            return p;
        }

        public void Record(
            RawSim speaker,
            RawSim listener,
            string interaction,
            Tone tone,
            string subjectId,
            int quality,
            int listenerInterestIndex = -1,
            SkillKind listenerSkill = SkillKind.None,
            string subjectTitle = null,
            Temperament observedTemperament = Temperament.Unknown)
        {
            if (speaker == null || listener == null) return;
            var key = PairKey(speaker, listener);
            lock (gate)
            {
                var p = GetOrCreateUnlocked(key);
                p.SpeakerKey = speaker.MemoryKey();
                p.SpeakerName = SafeName(speaker.Name);
                p.ListenerKey = listener.MemoryKey();
                p.ListenerName = SafeName(listener.Name);
                p.LastStr = speaker.StrToTarget;
                p.LastLtr = speaker.LtrToTarget;
                p.LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                if (!string.IsNullOrEmpty(subjectId))
                {
                    UpdateStats(p.Subjects, subjectId, quality, p.Exchanges);
                    if (!string.IsNullOrWhiteSpace(subjectTitle)) p.SubjectTitles[subjectId] = subjectTitle.Trim();
                }

                RecordDiscoveriesUnlocked(p, listener, listenerInterestIndex, listenerSkill, observedTemperament);
            }
        }

        // Records what the listener observed about the speaker during an exchange without altering
        // the listener's own initiation backoff counters.
        public void RecordReciprocal(
            RawSim listener,
            RawSim speaker,
            string interaction,
            int quality,
            int topicInterestIndex = -1,
            SkillKind speakerSkill = SkillKind.None,
            string subjectId = null,
            string subjectTitle = null,
            Temperament observedSpeakerTemperament = Temperament.Unknown)
        {
            if (listener == null || speaker == null) return;
            var key = PairKey(listener, speaker);
            lock (gate)
            {
                var p = GetOrCreateUnlocked(key);
                p.SpeakerKey = listener.MemoryKey();
                p.SpeakerName = SafeName(listener.Name);
                p.ListenerKey = speaker.MemoryKey();
                p.ListenerName = SafeName(speaker.Name);
                p.LastStr = listener.StrToTarget != 0 ? listener.StrToTarget : speaker.TargetStrToMe;
                p.LastLtr = listener.LtrToTarget != 0 ? listener.LtrToTarget : speaker.TargetLtrToMe;
                p.LastUpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                p.Exchanges++;
                if (quality >= 3) p.PositiveCount++;
                else if (quality <= 1) p.NegativeCount++;
                if (!string.IsNullOrEmpty(subjectId) && !string.IsNullOrWhiteSpace(subjectTitle))
                {
                    UpdateStats(p.Subjects, subjectId, quality, p.Exchanges);
                    p.SubjectTitles[subjectId] = subjectTitle.Trim();
                }
                RecordDiscoveriesUnlocked(p, speaker, topicInterestIndex, speakerSkill, observedSpeakerTemperament);
            }
        }

        static void RecordDiscoveriesUnlocked(PairMemory p, RawSim about, int interestIndex, SkillKind explicitSkill, Temperament observedTemperament)
        {
            // 1. Interest & sentiment (0..1000)
            if (interestIndex >= 0 && interestIndex < 8)
            {
                p.LearnedInterests.Add(interestIndex);
                if (about.Interests != null && interestIndex < about.Interests.Length)
                    p.LearnedInterestScores[interestIndex] = about.Interests[interestIndex];
            }

            // 2. Explicit skill discussed, plus progressive discovery of a notable skill after 2+ chats
            if (explicitSkill != SkillKind.None)
            {
                var skName = explicitSkill.ToString();
                p.LearnedSkills.Add(skName);
                int lv = Math.Max(0, Math.Min(10, (int)Math.Round(about.SkillValue(explicitSkill) / 100.0)));
                if (lv > 0) p.LearnedSkillLevels[skName] = lv;
            }
            if (p.Exchanges >= 2)
            {
                foreach (var sk in about.TopSkills(200))
                {
                    var skName = sk.ToString();
                    if (!p.LearnedSkillLevels.ContainsKey(skName))
                    {
                        p.LearnedSkills.Add(skName);
                        p.LearnedSkillLevels[skName] = Math.Max(1, Math.Min(10, (int)Math.Round(about.SkillValue(sk) / 100.0)));
                        break;
                    }
                }
            }

            // 3. Progressive personality trait discovery (1 distinct trait revealed per exchange, ordered by salience)
            foreach (var trait in SalientTraits(about))
            {
                if (!p.LearnedTraits.Any(t => string.Equals(t, trait, StringComparison.OrdinalIgnoreCase)))
                {
                    p.LearnedTraits.Add(trait);
                    break;
                }
            }

            // 4. Temperament once observed
            if (observedTemperament != Temperament.Unknown)
                p.LearnedTemperament = FormatTemperament(observedTemperament);

            // 5. Career once they've talked
            var career = FormatCareer(about);
            if (!string.IsNullOrEmpty(career))
                p.LearnedCareer = career;
        }

        static string SafeName(string n) { return string.IsNullOrWhiteSpace(n) ? "Someone" : n.Trim(); }

        static string FormatCareer(RawSim sim)
        {
            var c = (sim.Career ?? "").Trim();
            var j = (sim.JobTitle ?? "").Trim();
            if (c.Length == 0 && j.Length == 0) return "";
            if (c.Length > 0 && j.Length > 0 && !c.Equals(j, StringComparison.OrdinalIgnoreCase)) return c + " (" + j + ")";
            return j.Length > 0 ? j : c;
        }

        public static string FormatTemperament(Temperament t)
        {
            switch (t)
            {
                case Temperament.Receptive: return "Warm & Receptive";
                case Temperament.ThickSkinned: return "Thick-Skinned";
                case Temperament.EasilyOffended: return "Sensitive";
                case Temperament.Cold: return "Cold & Distant";
                default: return "";
            }
        }

        static IEnumerable<string> SalientTraits(RawSim sim)
        {
            var axes = new[]
            {
                new { Dist = Math.Abs(sim.Nice - 500), Label = sim.Nice >= 667 ? "Very Nice" : (sim.Nice < 334 ? "Grumpy" : "Easygoing") },
                new { Dist = Math.Abs(sim.Outgoing - 500), Label = sim.Outgoing >= 667 ? "Outgoing" : (sim.Outgoing < 334 ? "Shy" : "Sociable") },
                new { Dist = Math.Abs(sim.Playful - 500), Label = sim.Playful >= 667 ? "Playful" : (sim.Playful < 334 ? "Serious" : "Lighthearted") },
                new { Dist = Math.Abs(sim.Active - 500), Label = sim.Active >= 667 ? "Active" : (sim.Active < 334 ? "Laid-Back" : "Steady") },
                new { Dist = Math.Abs(sim.Neat - 500), Label = sim.Neat >= 667 ? "Neat" : (sim.Neat < 334 ? "Messy" : "Tidy") },
            };
            return axes.OrderByDescending(a => a.Dist).Select(a => a.Label);
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
                SpeakerKey = p.SpeakerKey,
                SpeakerName = p.SpeakerName,
                ListenerKey = p.ListenerKey,
                ListenerName = p.ListenerName,
                Exchanges = p.Exchanges,
                PositiveCount = p.PositiveCount,
                NegativeCount = p.NegativeCount,
                NegativeStreak = p.NegativeStreak,
                BackoffRemaining = p.BackoffRemaining,
                LastStr = p.LastStr,
                LastLtr = p.LastLtr,
                LastUpdatedUtc = p.LastUpdatedUtc,
                LearnedInterests = p.LearnedInterests != null ? new HashSet<int>(p.LearnedInterests) : new HashSet<int>(),
                LearnedSkills = p.LearnedSkills != null ? new HashSet<string>(p.LearnedSkills, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                LearnedTraits = p.LearnedTraits != null ? new List<string>(p.LearnedTraits) : new List<string>(),
                LearnedTemperament = p.LearnedTemperament,
                LearnedCareer = p.LearnedCareer,
            };
            if (p.Interactions != null) foreach (var kv in p.Interactions) c.Interactions[kv.Key] = kv.Value;
            if (p.Subjects != null) foreach (var kv in p.Subjects) c.Subjects[kv.Key] = kv.Value;
            if (p.SubjectTitles != null) foreach (var kv in p.SubjectTitles) c.SubjectTitles[kv.Key] = kv.Value;
            if (p.Tones != null) foreach (var kv in p.Tones) c.Tones[kv.Key] = kv.Value;
            if (p.LearnedInterestScores != null) foreach (var kv in p.LearnedInterestScores) c.LearnedInterestScores[kv.Key] = kv.Value;
            if (p.LearnedSkillLevels != null) foreach (var kv in p.LearnedSkillLevels) c.LearnedSkillLevels[kv.Key] = kv.Value;
            return c;
        }
    }
}
