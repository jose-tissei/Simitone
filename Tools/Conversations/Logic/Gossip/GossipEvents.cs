using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class DetectedEvent
    {
        public FactKind Kind;
        public string Label = "";
        public int Amount;
        public bool NeedsOther;     // the event involves a second Sim (the listener of the interaction, or the other Sim of the pair)
    }

    // Turns what the game shows (a Sim now compared with a moment ago, a social interaction and how it went) into things worth gossiping about.
    // Pure logic, so it can be tested without a game.
    public static class EventDetector
    {
        public const short FriendThreshold = 70;   // the game marks a pair as friends when the short-term relationship reaches this

        public static readonly string[] SkillLabels = { "cooking", "charisma", "mechanics", "creativity", "fitness", "logic" };

        static readonly SkillKind[] Skills = { SkillKind.Cooking, SkillKind.Charisma, SkillKind.Mechanical, SkillKind.Creativity, SkillKind.Body, SkillKind.Logic };

        public static string LabelOf(SkillKind k) { int i = Array.IndexOf(Skills, k); return i < 0 ? "" : SkillLabels[i]; }

        // skill points: 100 per point, 0..10
        public static int SkillLevel(short value) { return Math.Max(0, Math.Min(10, value / 100)); }

        // What happened to one Sim between two looks: a new skill point or a promotion (or a new job).
        public static List<DetectedEvent> Diff(RawSim before, RawSim now)
        {
            var list = new List<DetectedEvent>();
            if (before == null || now == null) return list;
            for (int i = 0; i < Skills.Length; i++)
            {
                int was = SkillLevel(before.SkillValue(Skills[i])), level = SkillLevel(now.SkillValue(Skills[i]));
                if (level > was && level >= 1) list.Add(new DetectedEvent { Kind = FactKind.SkillGain, Label = SkillLabels[i], Amount = level });
            }
            bool hadJob = !string.IsNullOrWhiteSpace(before.JobTitle), hasJob = !string.IsNullOrWhiteSpace(now.JobTitle);
            if (hasJob && (!hadJob || !string.Equals(before.JobTitle.Trim(), now.JobTitle.Trim(), StringComparison.OrdinalIgnoreCase) || !string.Equals(before.Career ?? "", now.Career ?? "", StringComparison.OrdinalIgnoreCase)))
                list.Add(new DetectedEvent { Kind = FactKind.Promoted, Label = now.JobTitle.Trim() });
            return list;
        }

        // What a social interaction between two Sims amounts to, for whoever saw it. `quality` is 1 bad, 2 ok, 3 great.
        public static List<DetectedEvent> FromInteraction(string interaction, int quality)
        {
            var list = new List<DetectedEvent>();
            switch ((interaction ?? "").ToLowerInvariant())
            {
                case "kiss":
                case "flirt":
                    if (quality >= 3 || (interaction == "kiss" && quality >= 2)) list.Add(new DetectedEvent { Kind = FactKind.Romance, NeedsOther = true });
                    break;
                case "insult":
                case "slap":
                    list.Add(new DetectedEvent { Kind = FactKind.Argument, NeedsOther = true });
                    break;
                case "tease":
                case "brag":
                    if (quality <= 1) list.Add(new DetectedEvent { Kind = FactKind.Argument, NeedsOther = true });
                    break;
                case "givegift":
                    if (quality >= 2) list.Add(new DetectedEvent { Kind = FactKind.Gift, NeedsOther = true });
                    break;
            }
            return list;
        }

        // two Sims who were not friends and now are
        public static bool BecameFriends(short strBefore, short strNow) { return strBefore < FriendThreshold && strNow >= FriendThreshold; }
    }
}
