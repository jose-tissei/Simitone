using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class ParallelSocialPlan
    {
        public string Interaction;          // "talk", "compliment", "joke", "brag", "cheerup", "apologize", "flirt", "tease"
        public AiSubject Subject;           // non-null when Interaction == "talk"
        public Tone Tone;
        public Temperament ListenerTemperament;
        public int Quality;                 // 1 = bad, 2 = ok, 3 = great
        public double Knowledge;
        public double Accuracy;
        public bool UsedLaya;
        public string SpeakerLine;
        public string ReplyLine;
        public short SpeakerSocialDelta, ListenerSocialDelta;
        public short SpeakerStrDelta, ListenerStrDelta;
        public short SpeakerLtrDelta, ListenerLtrDelta;
        public SkillKind DiscoveredSkill = SkillKind.None;
        public GossipTranscript Gossip;                    // non-null when Interaction == "gossip": the whole conversation
        public List<GossipBalloon> Balloons;               // its lines, in order, ready to show

        public string Summary(string speakerName, string listenerName)
        {
            var what = Interaction == "talk" && Subject != null ? "talk[" + Subject.Title + "]" : Interaction;
            return "parallel " + what + " " + Names.Label(Tone) + " q=" + Quality + " know=" + Knowledge.ToString("0.00") +
                   " acc=" + Accuracy.ToString("0.00") + " temper=" + ListenerTemperament + " laya=" + UsedLaya +
                   " (" + speakerName + " -> " + listenerName + ")";
        }
    }

    // Pure-logic planner for autonomous room-level parallel conversations.
    // Runs off the game thread: asks Laya to score candidate verbal interactions and AI talk subjects,
    // modulates decisions by Knowledge, Skills, Mood, and SocialMemory (avoiding negative loops),
    // and resolves authentic Sims-style outcomes and motive/relationship deltas.
    public static class ParallelSocialPlanner
    {
        // Social motive ranges -100..+100; 75% of the bar is +50 (a Sim starts chatting once the bar drops below that).
        public const short SocialTriggerThreshold = 50;
        public const double MaxOutdoorTileDistance = 8.0;

        public const string InteractionQuestion = "Which verbal social interaction should this Sim choose next in this conversation?";
        public const string SubjectQuestion = "Which conversation topic should this Sim bring up next?";

        static readonly Dictionary<string, string> InteractionDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["talk"] = "have a relaxed conversation about a specific topic",
            ["compliment"] = "give a warm compliment to brighten their mood",
            ["joke"] = "tell a playful joke to make them laugh",
            ["brag"] = "boast about a recent skill or achievement",
            ["cheerup"] = "comfort and cheer them up when they seem down",
            ["apologize"] = "apologize sincerely to smooth over tension",
            ["flirt"] = "drop a charming, mildly romantic remark",
            ["tease"] = "playfully tease or banter with them",
            ["gossip"] = "pass on a piece of news about another Sim",
        };

        public static bool ShouldTriggerSocial(short socialMotive)
        {
            return socialMotive <= SocialTriggerThreshold;
        }

        public static bool IsSameRoom(ushort roomA, bool outsideA, int tileXA, int tileYA, ushort roomB, bool outsideB, int tileXB, int tileYB)
        {
            if (outsideA != outsideB) return false;
            if (roomA != roomB) return false;
            if (!outsideA && roomA > 0) return true;
            double dx = tileXA - tileXB, dy = tileYA - tileYB;
            return Math.Sqrt(dx * dx + dy * dy) <= MaxOutdoorTileDistance;
        }

        // test aid (simtool convo interaction joke): make every autonomous chat use this interaction when it is possible
        public static string ForcedInteraction;

        public static ParallelSocialPlan Plan(RawSim speaker, RawSim listener, LayaClient laya, DialogueBank bank, AiSubjectBank subjects, SocialMemory memory, Random rng, GossipBook gossip = null, int today = 0, long tick = 0)
        {
            speaker = speaker ?? new RawSim();
            listener = listener ?? new RawSim();
            bank = bank ?? new DialogueBank();
            subjects = subjects ?? AiSubjectBank.Builtin();
            memory = memory ?? new SocialMemory();
            rng = rng ?? new Random();

            var sp = TraitProfile.From(speaker);
            var lp = TraitProfile.From(listener);

            // 1. Determine listener's true temperament (via Laya when available, otherwise unknown)
            bool usedLaya = false;
            Temperament trueTemper = Temperament.Unknown;
            if (laya != null && laya.Available)
            {
                trueTemper = laya.ClassifyTemperament(lp);
                if (trueTemper != Temperament.Unknown) usedLaya = true;
            }
            if (trueTemper == Temperament.Unknown) trueTemper = FallbackTemperament(listener);

            var assessment = SocialIntelligence.Assess(speaker, listener, trueTemper, memory);
            bool deescalating = memory.IsDeescalating(speaker, listener);

            // 2. Build eligible candidate verbal interactions
            var candidates = BuildCandidateInteractions(speaker, listener, assessment, memory);
            TurnContext gossipCtx = null;
            if (gossip != null && speaker.NeighborId > 0 && listener.NeighborId > 0 && !deescalating)
            {
                gossip.SetName(speaker.NeighborId, speaker.Name); gossip.SetName(listener.NeighborId, listener.Name);
                gossipCtx = new TurnContext { Self = speaker.NeighborId, Other = listener.NeighborId, SelfMind = gossip.SnapshotMind(speaker.NeighborId), Today = today };
                if (GossipPolicy.HasCandidates(gossipCtx) && !memory.IsInteractionForbidden(speaker, listener, "gossip")) candidates.Add("gossip");
            }

            // 3. Ask Laya to score candidate interactions
            Dictionary<string, double> layaScores = null;
            if (laya != null && laya.Available)
            {
                var prompt = SocialIntelligence.BuildInteractionPrompt(speaker, listener, trueTemper, memory);
                var criteria = candidates.Select(c => c + "|" + InteractionDescriptions[c]).ToArray();
                layaScores = laya.ScoreChoices(prompt, InteractionQuestion, criteria);
                if (layaScores != null && layaScores.Count > 0) usedLaya = true;
            }

            // 4. Blend Laya scores + SocialIntelligence heuristics + SocialMemory + misjudgment noise
            string chosenInteraction = PickBestInteraction(candidates, layaScores, speaker, listener, assessment, memory, rng);
            var forced = ForcedInteraction;
            if (!string.IsNullOrEmpty(forced) && candidates.Contains(forced)) chosenInteraction = forced;

            if (chosenInteraction == "gossip")
            {
                var gp = PlanGossip(speaker, listener, bank, gossip, today, tick, rng, usedLaya, trueTemper, assessment);
                if (gp != null) return gp;
                chosenInteraction = "talk";   // nothing came of it (the Sim did not feel like telling): have a normal chat instead
            }

            // 5. If "talk", use Laya + SocialIntelligence + SocialMemory to choose an AI subject
            AiSubject chosenSubject = null;
            if (chosenInteraction == "talk")
            {
                var subjCandidates = subjects.SelectCandidates(speaker, listener, assessment.Knowledge, memory, 5, rng);
                Dictionary<string, double> subjProbs = null;
                if (laya != null && laya.Available && subjCandidates.Count > 1)
                {
                    var topicPrompt = SocialIntelligence.BuildInteractionPrompt(speaker, listener, trueTemper, memory) +
                                      " " + sp.Name + " decides to strike up a conversation with " + lp.Name + ".";
                    var subjCriteria = subjCandidates.Select(s => s.Id + "|" + s.Title + ": " + s.Description).ToArray();
                    subjProbs = laya.ScoreChoices(topicPrompt, SubjectQuestion, subjCriteria);
                    if (subjProbs != null && subjProbs.Count > 0) usedLaya = true;
                }
                chosenSubject = PickBestSubject(subjCandidates, subjProbs, speaker, listener, assessment, memory, rng);
            }

            // 6. Choose tone (Gentle forced during de-escalation; otherwise guided by traits, mood, perception, memory)
            Tone tone = ChooseTone(speaker, listener, assessment, memory, chosenSubject, deescalating, rng);

            // 7. Evaluate outcome against listener's TRUE state (so misreading the room or bad mood can genuinely backfire)
            int quality = EvaluateQuality(chosenInteraction, chosenSubject, tone, speaker, listener, trueTemper, assessment);

            // 8. Compute authentic Social motive and STR/LTR deltas
            short spSoc, lsSoc, spStr, lsStr, spLtr, lsLtr;
            ComputeDeltas(chosenInteraction, quality, out spSoc, out lsSoc, out spStr, out lsStr, out spLtr, out lsLtr);

            // 9. Fill AI dialogue lines from the bank
            string topicTitle = chosenSubject == null ? null : chosenSubject.Title;
            string speakerLine = DialogueBank.Fill(bank.SpeakerLine(chosenInteraction, tone, rng), sp.Name, lp.Name, lp.JobTitle, topicTitle);
            string replyLine = DialogueBank.Fill(bank.ReplyLine(chosenInteraction, quality, trueTemper, rng), sp.Name, lp.Name, lp.JobTitle, topicTitle);

            var topLsSkills = listener.TopSkills(300);
            SkillKind discoveredSkill = chosenSubject != null && chosenSubject.Skill != SkillKind.None && listener.SkillValue(chosenSubject.Skill) >= 300
                ? chosenSubject.Skill
                : (topLsSkills.Length > 0 && quality >= 2 ? topLsSkills[0] : SkillKind.None);

            return new ParallelSocialPlan
            {
                Interaction = chosenInteraction,
                Subject = chosenSubject,
                Tone = tone,
                ListenerTemperament = trueTemper,
                Quality = quality,
                Knowledge = assessment.Knowledge,
                Accuracy = assessment.Accuracy,
                UsedLaya = usedLaya,
                SpeakerLine = speakerLine,
                ReplyLine = replyLine,
                SpeakerSocialDelta = spSoc,
                ListenerSocialDelta = lsSoc,
                SpeakerStrDelta = spStr,
                ListenerStrDelta = lsStr,
                SpeakerLtrDelta = spLtr,
                ListenerLtrDelta = lsLtr,
                DiscoveredSkill = discoveredSkill,
            };
        }

        // A whole news conversation (greeting, the news, maybe a question, goodbye). Null when nothing was actually told.
        static ParallelSocialPlan PlanGossip(RawSim speaker, RawSim listener, DialogueBank bank, GossipBook gossip, int today, long tick, Random rng, bool usedLaya, Temperament temper, SocialAssessment a)
        {
            var spTraits = GossipTraits.From(speaker, speaker.StrToTarget, speaker.LtrToTarget);
            var lsTraits = GossipTraits.From(listener, speaker.TargetStrToMe, speaker.TargetLtrToMe);
            var sims = gossip.SimIds();
            GossipTranscript t = null;
            for (int attempt = 0; attempt < 4 && (t == null || !t.HasContent); attempt++)
            {
                ulong seed = ((ulong)(uint)rng.Next() << 32) | (uint)rng.Next();
                t = GossipConversation.Run(gossip, speaker.NeighborId, listener.NeighborId, spTraits, lsTraits, sims, today, tick, seed, 8, GossipLines.ToneOf(speaker), GossipLines.ToneOf(listener));
            }
            if (t == null || !t.HasContent) return null;

            var lines = bank == null ? new GossipLineBank() : bank.Gossip;
            var lrng = new GossipRng(((ulong)(uint)rng.Next() << 32) | (uint)rng.Next());
            var balloons = new List<GossipBalloon>();
            foreach (var turn in t.Turns)
            {
                var ctx = new GossipContext
                {
                    Book = gossip, Lines = lines, Today = today, Tone = turn.Tone,
                    SpeakerName = turn.Speaker == speaker.NeighborId ? speaker.Name : listener.Name,
                    ListenerName = turn.Speaker == speaker.NeighborId ? listener.Name : speaker.Name,
                };
                balloons.Add(new GossipBalloon { Speaker = turn.Speaker, Act = turn.Act, Text = GossipLines.Render(turn, ctx, ref lrng) });
            }
            int quality = t.Turns.Any(x => x.Heard != null && (x.Heard.IsNew || x.Heard.Filled.Count > 0)) ? 3 : 2;
            short spSoc, lsSoc, spStr, lsStr, spLtr, lsLtr;
            ComputeDeltas("gossip", quality, out spSoc, out lsSoc, out spStr, out lsStr, out spLtr, out lsLtr);
            return new ParallelSocialPlan
            {
                Interaction = "gossip", Tone = GossipLines.ToneOf(speaker), ListenerTemperament = temper, Quality = quality,
                Knowledge = a.Knowledge, Accuracy = a.Accuracy, UsedLaya = usedLaya,
                SpeakerLine = balloons[0].Text, ReplyLine = balloons.Count > 1 ? balloons[1].Text : "",
                SpeakerSocialDelta = spSoc, ListenerSocialDelta = lsSoc, SpeakerStrDelta = spStr, ListenerStrDelta = lsStr, SpeakerLtrDelta = spLtr, ListenerLtrDelta = lsLtr,
                Gossip = t, Balloons = balloons,
            };
        }

        static List<string> BuildCandidateInteractions(RawSim speaker, RawSim listener, SocialAssessment a, SocialMemory memory)
        {
            var list = new List<string> { "talk", "compliment", "joke" };
            var pair = memory.GetPair(speaker, listener);
            bool deesc = memory.IsDeescalating(speaker, listener);

            if (a.PerceivesListenerBadMood || pair.NegativeStreak >= 1) list.Add("cheerup");
            if (pair.NegativeStreak >= 1 || speaker.StrToTarget < 15) list.Add("apologize");
            if (!deesc)
            {
                list.Add("brag");
                if (speaker.StrToTarget >= 35 && speaker.LtrToTarget >= 20) list.Add("flirt");
                if (speaker.Nice < 750) list.Add("tease");
            }
            return list.Where(i => !memory.IsInteractionForbidden(speaker, listener, i)).ToList();
        }

        static string PickBestInteraction(List<string> candidates, Dictionary<string, double> layaScores, RawSim speaker, RawSim listener, SocialAssessment a, SocialMemory memory, Random rng)
        {
            if (candidates.Count == 0) return "talk";
            string best = candidates[0];
            double bestScore = double.NegativeInfinity;
            double noiseScale = (1.0 - a.Accuracy) * 0.70;
            bool deesc = memory.IsDeescalating(speaker, listener);

            foreach (var c in candidates)
            {
                double h = SocialIntelligence.InteractionHeuristic(c, speaker, listener, a, memory);
                double laya = (layaScores != null && layaScores.ContainsKey(c)) ? layaScores[c] : 0.0;
                double layaWeight = deesc ? 0.25 : 0.55;
                double combined = (layaScores != null ? laya * layaWeight + h * (1.0 - layaWeight) : h);
                // Laya only knows the interaction names; whether this Sim really has news to share is something the engine knows,
                // so Laya's opinion of "gossip" would only dilute that: the heuristic decides, with a small nudge
                if (c == "gossip") combined = h + 0.10;
                double noise = (rng.NextDouble() * 2.0 - 1.0) * noiseScale * (deesc ? 0.35 : 1.0);
                double total = combined + noise;
                if (total > bestScore) { bestScore = total; best = c; }
            }
            return best;
        }

        static AiSubject PickBestSubject(List<AiSubject> candidates, Dictionary<string, double> layaScores, RawSim speaker, RawSim listener, SocialAssessment a, SocialMemory memory, Random rng)
        {
            if (candidates == null || candidates.Count == 0) return AiSubjectBank.Builtin().All[0];
            AiSubject best = candidates[0];
            double bestScore = double.NegativeInfinity;
            double noiseScale = (1.0 - a.Accuracy) * 0.35;
            var spInt = speaker.Interests != null && speaker.Interests.Length >= 8 ? speaker.Interests : new short[8];
            var lsInt = listener.Interests != null && listener.Interests.Length >= 8 ? listener.Interests : new short[8];

            foreach (var s in candidates)
            {
                double h = (spInt[s.Interest] / 1000.0) * 0.30;
                bool knowsInt = a.Knowledge >= 0.35 || memory.HasLearnedInterest(speaker, listener, s.Interest);
                if (knowsInt) h += (lsInt[s.Interest] / 1000.0) * 0.60 * Math.Max(0.6, a.Knowledge);
                h += SocialIntelligence.SubjectSkillBonus(s, speaker, listener, a.Knowledge, memory);
                h += memory.SubjectAdjustment(speaker, listener, s.Id);

                double laya = (layaScores != null && layaScores.ContainsKey(s.Id)) ? layaScores[s.Id] : 0.0;
                double combined = layaScores != null ? laya * 0.45 + h * 0.55 : h;
                double noise = (rng.NextDouble() * 2.0 - 1.0) * noiseScale;
                double total = combined + noise;
                if (total > bestScore) { bestScore = total; best = s; }
            }
            return best;
        }

        static Tone ChooseTone(RawSim speaker, RawSim listener, SocialAssessment a, SocialMemory memory, AiSubject subject, bool deescalating, Random rng)
        {
            if (deescalating) return Tone.Gentle;
            var scores = new double[3];
            scores[(int)Tone.Gentle] = 0.30 + (speaker.Nice / 1000.0) * 0.35 + (a.PerceivesListenerBadMood ? 0.30 : 0.0)
                                     + (a.PerceivedTemperament == Temperament.EasilyOffended || a.PerceivedTemperament == Temperament.Cold ? 0.25 : 0.0);
            scores[(int)Tone.Playful] = 0.25 + (speaker.Playful / 1000.0) * 0.40
                                      + (a.PerceivedTemperament == Temperament.Receptive || a.PerceivedTemperament == Temperament.ThickSkinned ? 0.20 : 0.0)
                                      - (a.PerceivesListenerBadMood ? 0.20 : 0.0);
            scores[(int)Tone.Bold] = 0.15 + (speaker.Outgoing / 1000.0) * 0.25 + ((1000 - speaker.Nice) / 1000.0) * 0.20
                                   + (speaker.Mood < -30 ? 0.22 : 0.0)
                                   + (a.PerceivedTemperament == Temperament.ThickSkinned ? 0.15 : 0.0)
                                   - (a.PerceivedTemperament == Temperament.EasilyOffended || a.PerceivedTemperament == Temperament.Cold ? 0.30 : 0.0);

            if (subject != null) scores[(int)subject.ToneHint] += 0.10;
            for (int t = 0; t < 3; t++)
            {
                scores[t] += memory.ToneAdjustment(speaker, listener, (Tone)t);
                scores[t] += (rng.NextDouble() * 2.0 - 1.0) * (1.0 - a.Accuracy) * 0.45;
            }
            int best = 0;
            if (scores[1] > scores[best]) best = 1;
            if (scores[2] > scores[best]) best = 2;
            return (Tone)best;
        }

        static Temperament FallbackTemperament(RawSim listener)
        {
            if (listener.Mood < -40 && listener.Nice < 450) return Temperament.Cold;
            if (listener.Nice < 350 && listener.Playful < 400) return Temperament.EasilyOffended;
            if (listener.Playful >= 650 && listener.Outgoing >= 550) return Temperament.ThickSkinned;
            if (listener.Nice >= 550) return Temperament.Receptive;
            return Temperament.Unknown;
        }

        // Evaluates the outcome against the listener's TRUE temperament, mood, interests, skills, and relationship.
        static int EvaluateQuality(string interaction, AiSubject subject, Tone tone, RawSim speaker, RawSim listener, Temperament trueTemper, SocialAssessment a)
        {
            int toneShift = OutcomeResolver.Shift(tone, trueTemper);
            int charismaBoost = speaker.Charisma >= 650 ? 1 : 0;

            if (interaction == "talk" && subject != null)
            {
                var lsInt = listener.Interests != null && listener.Interests.Length >= 8 ? listener.Interests : new short[8];
                int liking = TopicLogic.Liking(lsInt[subject.Interest]);   // 0, 1, 2
                int baseQ = liking == 2 ? 3 : (liking == 1 ? 2 : 1);
                if (subject.Skill != SkillKind.None && listener.SkillValue(subject.Skill) >= 400 && speaker.SkillValue(subject.Skill) >= 300)
                    baseQ = Math.Min(3, baseQ + 1);
                int moodShift = listener.Mood < -50 && tone == Tone.Bold ? -1 : 0;
                int total = baseQ + (toneShift > 0 ? 1 : (toneShift < 0 ? -1 : 0)) + moodShift;
                if (total <= 1 && charismaBoost > 0 && tone == Tone.Gentle && liking >= 1) total = 2;
                return Math.Max(1, Math.Min(3, total));
            }

            // Verbal non-talk interactions scored against listener's true relationship, mood, and temperament
            double score = speaker.TargetStrToMe * 0.65 + listener.Mood * 0.60 + speaker.Charisma * 0.04 + toneShift * 28.0;
            if (interaction == "cheerup") score += listener.Mood < -20 ? 35.0 : -10.0;
            else if (interaction == "apologize") score += tone == Tone.Gentle ? 35.0 : 10.0;
            else if (interaction == "compliment") score += listener.Nice >= 400 ? 22.0 : 5.0;
            else if (interaction == "joke") score += (listener.Playful - 450) * 0.07;
            else if (interaction == "brag") score += (speaker.TopSkills(600).Length > 0 ? 10.0 : -15.0) - (listener.Nice < 400 ? 20.0 : 0.0);
            else if (interaction == "flirt") score += speaker.TargetLtrToMe * 0.55 - 25.0;
            else if (interaction == "tease") score += (trueTemper == Temperament.ThickSkinned ? 25.0 : -35.0);

            if (score >= 42.0) return 3;
            if (score >= -20.0) return 2;
            return 1;
        }

        static void ComputeDeltas(string interaction, int quality, out short spSoc, out short lsSoc, out short spStr, out short lsStr, out short spLtr, out short lsLtr)
        {
            if (quality >= 3)
            {
                spSoc = 10; lsSoc = 10;
                spStr = (short)(interaction == "flirt" || interaction == "apologize" ? 5 : 4);
                lsStr = (short)(interaction == "flirt" || interaction == "apologize" ? 5 : 4);
                spLtr = 1; lsLtr = 1;
            }
            else if (quality == 2)
            {
                spSoc = 5; lsSoc = 5;
                spStr = 1; lsStr = 1;
                spLtr = 0; lsLtr = 0;
            }
            else
            {
                spSoc = -2; lsSoc = -3;
                spStr = (short)(interaction == "talk" ? -3 : -4);
                lsStr = (short)(interaction == "talk" ? -3 : -5);
                spLtr = (short)(interaction == "talk" || interaction == "apologize" ? 0 : -1);
                lsLtr = (short)(interaction == "talk" || interaction == "apologize" ? 0 : -1);
            }
        }
    }
}
