using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class SocialAssessment
    {
        public double Knowledge;              // 0..1: how well speaker knows listener
        public double CharismaNorm;           // 0..1: speaker Charisma / 1000
        public double SpeakerMoodNorm;        // 0..1: (mood + 100) / 200
        public double EffectiveReading;       // 0..1: combined ability to read listener's mood & temperament
        public double Accuracy;               // 0.20..0.98: overall choice accuracy (1 - Accuracy = misjudgment noise)
        public bool PerceivesListenerBadMood;
        public bool PerceivesListenerGoodMood;
        public Temperament PerceivedTemperament;
    }

    // Models how knowledge of the other Sim, skills (Charisma + shared/known skills), and mood impact
    // how accurately a Sim reads the room and chooses the right interaction and topic.
    public static class SocialIntelligence
    {
        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static double Clamp01(double v) { return Clamp(v, 0.0, 1.0); }

        public static double ComputeKnowledge(RawSim speaker, RawSim listener, SocialMemory memory)
        {
            speaker = speaker ?? new RawSim();
            var pair = memory == null ? new PairMemory() : memory.GetPair(speaker, listener);
            double ltrNorm = Clamp01((speaker.LtrToTarget + 20.0) / 120.0);
            double strNorm = Clamp01((speaker.StrToTarget + 20.0) / 120.0);
            double relScore = ltrNorm * 0.65 + strNorm * 0.35;
            double historyScore = Clamp01(pair.Exchanges / 10.0);
            return Clamp01(0.08 + 0.72 * relScore + 0.30 * historyScore);
        }

        public static SocialAssessment Assess(RawSim speaker, RawSim listener, Temperament trueListenerTemperament, SocialMemory memory)
        {
            speaker = speaker ?? new RawSim();
            listener = listener ?? new RawSim();
            double knowledge = ComputeKnowledge(speaker, listener, memory);
            double charisma = Clamp01(speaker.Charisma / 1000.0);
            double speakerMood = Clamp01((speaker.Mood + 100.0) / 200.0);
            double reading = Clamp01(knowledge * 0.72 + charisma * 0.38);
            double accuracy = Clamp(0.22 + 0.35 * knowledge + 0.26 * charisma + 0.15 * speakerMood, 0.20, 0.98);

            return new SocialAssessment
            {
                Knowledge = knowledge,
                CharismaNorm = charisma,
                SpeakerMoodNorm = speakerMood,
                EffectiveReading = reading,
                Accuracy = accuracy,
                PerceivesListenerBadMood = listener.Mood < -30 && reading >= 0.45,
                PerceivesListenerGoodMood = listener.Mood > 30 && reading >= 0.40,
                PerceivedTemperament = reading >= 0.52 ? trueListenerTemperament : Temperament.Unknown,
            };
        }

        // Builds the natural-language context prompt sent to Laya. Information about the listener is redacted/blurred
        // when Knowledge & Charisma are low, and enriched with known skills, mood, temperament, and memory lessons as they grow.
        public static string BuildInteractionPrompt(RawSim speaker, RawSim listener, Temperament trueListenerTemperament, SocialMemory memory)
        {
            var a = Assess(speaker, listener, trueListenerTemperament, memory);
            var sp = TraitProfile.From(speaker);
            var lp = TraitProfile.From(listener);
            var parts = new List<string> { sp.Summary };

            var spSkills = speaker.TopSkills(300);
            if (spSkills.Length > 0) parts.Add(sp.Name + " is skilled in " + string.Join(", ", spSkills) + ".");
            if (speaker.Charisma >= 600) parts.Add(sp.Name + " is socially perceptive and charismatic.");
            else if (speaker.Charisma < 200) parts.Add(sp.Name + " is awkward at reading social cues.");

            if (a.Knowledge < 0.35)
                parts.Add(sp.Name + " barely knows " + lp.Name + " yet and has to guess how they will react.");
            else if (a.Knowledge < 0.70)
                parts.Add(sp.Name + " is getting to know " + lp.Name + ".");
            else
                parts.Add(sp.Name + " knows " + lp.Name + " well.");

            if (a.PerceivesListenerBadMood) parts.Add(lp.Name + " seems to be in a bad mood.");
            else if (a.PerceivesListenerGoodMood) parts.Add(lp.Name + " seems to be in a good mood.");

            if (a.PerceivedTemperament != Temperament.Unknown)
                parts.Add(lp.Name + " is " + TemperamentPhrase(a.PerceivedTemperament) + ".");

            if (a.Knowledge >= 0.45)
            {
                var lsSkills = listener.TopSkills(300).Where(sk => a.Knowledge >= 0.65 || (memory != null && memory.HasLearnedSkill(speaker, listener, sk))).ToArray();
                if (lsSkills.Length > 0) parts.Add(lp.Name + " is skilled in " + string.Join(", ", lsSkills) + ".");
            }

            var lessons = memory == null ? "" : memory.LessonsNote(speaker, listener);
            if (!string.IsNullOrEmpty(lessons)) parts.Add(lessons);

            return string.Join(" ", parts);
        }

        static string TemperamentPhrase(Temperament t)
        {
            switch (t)
            {
                case Temperament.EasilyOffended: return "sensitive and easily offended";
                case Temperament.ThickSkinned: return "thick-skinned and enjoys banter";
                case Temperament.Cold: return "cold and distant";
                case Temperament.Receptive: return "warm and receptive";
                default: return "hard to read";
            }
        }

        // Deterministic affinity heuristic for a parallel verbal interaction given speaker traits, mood, skills, and what they perceive.
        public static double InteractionHeuristic(string interaction, RawSim speaker, RawSim listener, SocialAssessment a, SocialMemory memory)
        {
            double score = 0.20;
            bool speakerBadMood = speaker.Mood < -30;
            bool deescalating = memory != null && memory.IsDeescalating(speaker, listener);

            switch ((interaction ?? "").ToLowerInvariant())
            {
                case "talk":
                    score = 0.42 + (a.PerceivesListenerBadMood ? 0.08 : 0.0) + (a.Knowledge < 0.35 ? 0.10 : 0.0);
                    break;
                case "compliment":
                    score = 0.28 + (speaker.Nice / 1000.0) * 0.25 + (a.PerceivedTemperament == Temperament.Receptive ? 0.14 : 0.0)
                          + (a.PerceivesListenerBadMood ? 0.12 : 0.0) - (speakerBadMood ? 0.15 : 0.0);
                    break;
                case "joke":
                    score = 0.22 + (speaker.Playful / 1000.0) * 0.30 + (a.PerceivedTemperament == Temperament.ThickSkinned ? 0.18 : 0.0)
                          - (a.PerceivedTemperament == Temperament.EasilyOffended ? 0.22 : 0.0)
                          - (a.PerceivesListenerBadMood ? 0.18 : 0.0) - (speakerBadMood ? 0.10 : 0.0);
                    break;
                case "cheerup":
                    score = a.PerceivesListenerBadMood ? 0.56 + (speaker.Nice / 1000.0) * 0.18 : 0.08;
                    break;
                case "apologize":
                    var pair = memory == null ? new PairMemory() : memory.GetPair(speaker, listener);
                    score = pair.NegativeStreak >= 1 ? 0.45 + 0.15 * Math.Min(2, pair.NegativeStreak) : (speaker.StrToTarget < 10 ? 0.28 : 0.05);
                    break;
                case "brag":
                    score = 0.14 + (speaker.Outgoing / 1000.0) * 0.18 + ((1000 - speaker.Nice) / 1000.0) * 0.14
                          + (speaker.TopSkills(600).Length > 0 ? 0.10 : 0.0)
                          + (speakerBadMood && !deescalating ? 0.12 : 0.0)
                          - (a.PerceivesListenerBadMood ? 0.18 : 0.0)
                          - (a.PerceivedTemperament == Temperament.Cold ? 0.15 : 0.0);
                    break;
                case "flirt":
                    score = (speaker.StrToTarget >= 35 && speaker.LtrToTarget >= 20)
                        ? 0.26 + (speaker.Outgoing / 1000.0) * 0.15 + (a.PerceivesListenerGoodMood ? 0.14 : 0.0) - (a.PerceivesListenerBadMood ? 0.25 : 0.0)
                        : -0.50;
                    break;
                case "gossip":
                    // only offered when the speaker really has news for this listener: outgoing Sims jump at it, quiet and kind ones keep it to themselves
                    score = 0.55 + (speaker.Outgoing / 1000.0) * 0.25 - (speaker.Nice > 650 && speaker.Outgoing < 400 ? 0.25 : 0.0)
                          - (a.PerceivesListenerBadMood ? 0.15 : 0.0) - (speakerBadMood ? 0.10 : 0.0);
                    break;
                case "tease":
                    score = 0.10 + (speaker.Playful / 1000.0) * 0.16 + ((1000 - speaker.Nice) / 1000.0) * 0.16
                          + (speakerBadMood && !deescalating ? 0.16 : 0.0)
                          + (a.PerceivedTemperament == Temperament.ThickSkinned ? 0.12 : 0.0)
                          - (a.PerceivedTemperament == Temperament.EasilyOffended ? 0.30 : 0.0)
                          - (a.PerceivesListenerBadMood ? 0.25 : 0.0);
                    break;
            }

            if (memory != null) score += memory.InteractionAdjustment(speaker, listener, interaction);
            return score;
        }

        // Skill affinity bonus for discussing a subject aligned with the speaker's or known listener's skills.
        public static double SubjectSkillBonus(AiSubject subject, RawSim speaker, RawSim listener, double knowledge, SocialMemory memory)
        {
            if (subject == null || subject.Skill == SkillKind.None) return 0.0;
            double bonus = 0.0;
            double spSkill = Clamp01(speaker.SkillValue(subject.Skill) / 1000.0);
            double lsSkill = Clamp01(listener.SkillValue(subject.Skill) / 1000.0);
            if (spSkill >= 0.3) bonus += spSkill * 0.18;
            bool knowsListenerSkill = knowledge >= 0.45 || (memory != null && memory.HasLearnedSkill(speaker, listener, subject.Skill));
            if (knowsListenerSkill && lsSkill >= 0.3) bonus += lsSkill * 0.24;
            if (spSkill >= 0.4 && knowsListenerSkill && lsSkill >= 0.4) bonus += 0.12;   // shared expertise resonance
            return bonus;
        }
    }
}
