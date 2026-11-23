using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class LearnedTopicEntry
    {
        public string SubjectId;
        public string Title;
        public int LastOutcome;          // 1 = Awkward, 2 = Okay, 3 = Great
        public int Successes;
        public int Failures;
        public string OutcomeLabel { get { return LastOutcome >= 3 ? "Great" : (LastOutcome <= 1 ? "Awkward" : "Okay"); } }
    }

    public class SimKnowledgeCard
    {
        public string ListenerKey;
        public string ListenerName;
        public int Exchanges;
        public int PositiveCount;
        public int NegativeCount;
        public int NeutralCount { get { return Math.Max(0, Exchanges - PositiveCount - NegativeCount); } }
        public short LastStr;
        public short LastLtr;
        public int KnowledgePercent;
        public string FamiliarityLabel;
        public bool IsTense;
        public int BackoffRemaining;
        public string Career = "";
        public string Temperament = "";
        public List<string> Traits = new List<string>();
        public List<string> LikedInterests = new List<string>();
        public List<string> NeutralInterests = new List<string>();
        public List<string> DislikedInterests = new List<string>();
        public List<string> Skills = new List<string>();
        public List<string> WorkedInteractions = new List<string>();
        public List<string> AvoidInteractions = new List<string>();
        public List<string> WorkedTones = new List<string>();
        public List<string> AvoidTones = new List<string>();
        public List<LearnedTopicEntry> Topics = new List<LearnedTopicEntry>();
    }

    public class SimMemorySnapshot
    {
        public string SpeakerKey;
        public string SpeakerName;
        public List<SimKnowledgeCard> KnownSims = new List<SimKnowledgeCard>();
    }

    // Pure view-model builder for the in-game Sim Memory Menu: turns raw SocialMemory records into
    // readable, categorized dossiers of what a Sim has learned about every Sim they know.
    public static class SimMemoryViewModel
    {
        public static SimMemorySnapshot Build(SocialMemory memory, string speakerKey, string speakerName, AiSubjectBank subjects = null)
        {
            var snap = new SimMemorySnapshot
            {
                SpeakerKey = speakerKey ?? "",
                SpeakerName = string.IsNullOrWhiteSpace(speakerName) ? "Sim" : speakerName.Trim(),
            };
            if (memory == null || string.IsNullOrWhiteSpace(speakerKey)) return snap;

            var pairs = memory.GetKnownSims(speakerKey);
            foreach (var p in pairs)
            {
                if (p == null || p.Exchanges <= 0) continue;
                snap.KnownSims.Add(BuildCard(p, subjects));
            }
            return snap;
        }

        static readonly string[] FriendlyInterests =
        {
            "Travel & Hobbies",
            "Sci-Fi & Action",
            "Politics & Pets",
            "Culture & Nostalgia",
            "Weather",
            "Sports",
            "Music",
            "Outdoors",
        };

        public static string FriendlyInterestName(int idx)
        {
            if (idx >= 0 && idx < FriendlyInterests.Length) return FriendlyInterests[idx];
            if (idx >= 0 && idx < TopicLogic.Interests.Length) return TopicLogic.Interests[idx];
            return "Topic";
        }

        public static SimKnowledgeCard BuildCard(PairMemory p, AiSubjectBank subjects = null)
        {
            double knowledge = EstimateKnowledge(p.LastStr, p.LastLtr, p.Exchanges);
            int pct = (int)Math.Round(knowledge * 100.0);

            var card = new SimKnowledgeCard
            {
                ListenerKey = p.ListenerKey ?? "?",
                ListenerName = string.IsNullOrWhiteSpace(p.ListenerName) ? (p.ListenerKey ?? "Someone") : p.ListenerName.Trim(),
                Exchanges = p.Exchanges,
                PositiveCount = p.PositiveCount,
                NegativeCount = p.NegativeCount,
                LastStr = p.LastStr,
                LastLtr = p.LastLtr,
                KnowledgePercent = pct,
                FamiliarityLabel = FamiliarityOf(knowledge, p.Exchanges),
                IsTense = p.NegativeStreak >= SocialMemory.DeescalateThreshold || p.BackoffRemaining > 0,
                BackoffRemaining = p.BackoffRemaining,
                Career = p.LearnedCareer ?? "",
                Temperament = p.LearnedTemperament ?? "",
                Traits = p.LearnedTraits != null ? new List<string>(p.LearnedTraits) : new List<string>(),
            };

            // Interests grouped by learned sentiment (Likes / Neutral / Dislikes)
            if (p.LearnedInterests != null)
            {
                foreach (var idx in p.LearnedInterests.OrderBy(i => i))
                {
                    if (idx < 0 || idx >= TopicLogic.Interests.Length) continue;
                    var name = FriendlyInterestName(idx);
                    int raw;
                    if (p.LearnedInterestScores != null && p.LearnedInterestScores.TryGetValue(idx, out raw))
                    {
                        int outOf10 = Math.Max(0, Math.Min(10, (int)Math.Round(raw / 100.0)));
                        var label = name + " (" + outOf10 + "/10)";
                        int liking = TopicLogic.Liking((short)raw);
                        if (liking >= 2) card.LikedInterests.Add(label);
                        else if (liking == 1) card.NeutralInterests.Add(label);
                        else card.DislikedInterests.Add(label);
                    }
                    else
                    {
                        card.NeutralInterests.Add(name);
                    }
                }
            }

            // Skills with discovered levels
            var seenSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (p.LearnedSkillLevels != null)
            {
                foreach (var kv in p.LearnedSkillLevels.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                {
                    seenSkills.Add(kv.Key);
                    card.Skills.Add(kv.Key + " (Lv " + kv.Value + ")");
                }
            }
            if (p.LearnedSkills != null)
            {
                foreach (var sk in p.LearnedSkills.OrderBy(s => s))
                {
                    if (!seenSkills.Contains(sk)) card.Skills.Add(sk);
                }
            }

            // Interactions: what works vs what to avoid
            if (p.Interactions != null)
            {
                foreach (var kv in p.Interactions.OrderByDescending(k => k.Value != null ? k.Value.LastSeenTurn : 0))
                {
                    var st = kv.Value;
                    if (st == null) continue;
                    var label = PrettyInteraction(kv.Key);
                    if (st.LastOutcome == 1 || st.Failures > st.Successes)
                        card.AvoidInteractions.Add(label);
                    else if (st.Successes > 0 || st.LastOutcome >= 3)
                        card.WorkedInteractions.Add(label);
                }
            }

            // Tones: what works vs what to avoid
            if (p.Tones != null)
            {
                foreach (var kv in p.Tones)
                {
                    var st = kv.Value;
                    int toneInt;
                    if (st == null || !int.TryParse(kv.Key, out toneInt) || toneInt < 0 || toneInt >= Names.ToneLabels.Length) continue;
                    var label = Names.ToneLabels[toneInt];
                    if (st.LastOutcome == 1 || st.Failures > st.Successes)
                        card.AvoidTones.Add(label);
                    else if (st.Successes > 0 || st.LastOutcome >= 3)
                        card.WorkedTones.Add(label);
                }
            }

            // Discussed AI topics (most recent first, up to 6)
            if (p.Subjects != null)
            {
                foreach (var kv in p.Subjects.OrderByDescending(k => k.Value != null ? k.Value.LastSeenTurn : 0).Take(6))
                {
                    var st = kv.Value;
                    if (st == null) continue;
                    string title = ResolveSubjectTitle(kv.Key, p.SubjectTitles, subjects);
                    card.Topics.Add(new LearnedTopicEntry
                    {
                        SubjectId = kv.Key,
                        Title = title,
                        LastOutcome = st.LastOutcome,
                        Successes = st.Successes,
                        Failures = st.Failures,
                    });
                }
            }

            return card;
        }

        static double Clamp01(double v) { return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v); }

        public static double EstimateKnowledge(short str, short ltr, int exchanges)
        {
            double ltrNorm = Clamp01((ltr + 20.0) / 120.0);
            double strNorm = Clamp01((str + 20.0) / 120.0);
            double relScore = ltrNorm * 0.65 + strNorm * 0.35;
            double historyScore = Clamp01(exchanges / 10.0);
            return Clamp01(0.08 + 0.72 * relScore + 0.30 * historyScore);
        }

        public static string FamiliarityOf(double knowledge, int exchanges)
        {
            if (knowledge >= 0.75 && exchanges >= 4) return "Close";
            if (knowledge >= 0.55) return "Familiar";
            if (knowledge >= 0.32 || exchanges >= 2) return "Acquaintance";
            return "Just Met";
        }

        static string PrettyInteraction(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";
            switch (key.Trim().ToLowerInvariant())
            {
                case "cheerup": return "Cheer Up";
                case "talk": return "Talk";
                case "joke": return "Joke";
                case "compliment": return "Compliment";
                case "flirt": return "Flirt";
                case "tease": return "Tease";
                case "brag": return "Brag";
                case "apologize": return "Apologize";
                case "gossip": return "Gossip";
                case "hug": return "Hug";
                case "kiss": return "Kiss";
                case "dance": return "Dance";
                default:
                    var k = key.Trim();
                    return char.ToUpperInvariant(k[0]) + (k.Length > 1 ? k.Substring(1) : "");
            }
        }

        static string ResolveSubjectTitle(string id, Dictionary<string, string> storedTitles, AiSubjectBank subjects)
        {
            string t;
            if (storedTitles != null && storedTitles.TryGetValue(id, out t) && !string.IsNullOrWhiteSpace(t))
                return t;
            if (subjects != null)
            {
                var found = subjects.All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
                if (found != null && !string.IsNullOrWhiteSpace(found.Title)) return found.Title;
            }
            return id;
        }

        public static string FormatCardLine(SimKnowledgeCard c)
        {
            if (c == null) return "";
            var parts = new List<string>
            {
                c.ListenerName + " (" + c.FamiliarityLabel + " " + c.KnowledgePercent + "%, " + c.Exchanges + " chats, Daily " + c.LastStr + "/Lifetime " + c.LastLtr + ")"
            };
            if (c.Traits.Count > 0) parts.Add("traits=[" + string.Join(", ", c.Traits) + "]");
            if (!string.IsNullOrEmpty(c.Temperament)) parts.Add("temper=" + c.Temperament);
            if (!string.IsNullOrEmpty(c.Career)) parts.Add("career=" + c.Career);
            if (c.LikedInterests.Count > 0) parts.Add("likes=[" + string.Join(", ", c.LikedInterests) + "]");
            if (c.DislikedInterests.Count > 0) parts.Add("dislikes=[" + string.Join(", ", c.DislikedInterests) + "]");
            if (c.Skills.Count > 0) parts.Add("skills=[" + string.Join(", ", c.Skills) + "]");
            if (c.Topics.Count > 0) parts.Add("topics=[" + string.Join(", ", c.Topics.Select(x => x.Title + ":" + x.OutcomeLabel)) + "]");
            return string.Join(" | ", parts);
        }
    }
}
