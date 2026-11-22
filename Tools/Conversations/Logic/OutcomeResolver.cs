using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    // Decides the social outcome (the number the game's script stores in the social object, 1..3).
    // Policy B (decided with the user): the result is the script's own outcome moved by at most one step, using a
    // small table of (receiver temperament x tone). Unknown temperament or an unsupported interaction = no change.
    public static class OutcomeResolver
    {
        // keys match the "<Name> - Person A" script names in SocialInteractions.iff, lower case, spaces removed
        public static readonly string[] Interactions =
        {
            "apologize", "backrub", "brag", "cheerup", "compliment", "dancewith", "entertain", "flirt", "givegift",
            "hug", "insult", "joke", "kiss", "propose", "scare", "slap", "tease", "tickle",
        };

        // outcome numbers go 1..Max; interactions with two tuning groups only have 1..2
        static readonly HashSet<string> ThreeOutcomes = new HashSet<string> { "cheerup", "flirt", "hug", "insult", "joke", "kiss" };

        // for these, a LOWER outcome number is the better result (their tuning tables list the good result first)
        static readonly HashSet<string> BestIsLow = new HashSet<string> { "cheerup", "backrub" };

        // the receiver's reaction is not "better or worse" for these, or has its own flow, so the table does not apply
        static readonly HashSet<string> NoShift = new HashSet<string> { "insult", "scare", "slap", "tease", "propose" };

        public static string KeyFromScriptName(string scriptLabel)
        {
            if (scriptLabel == null) return null;
            var s = scriptLabel;
            var i = s.IndexOf(" - Person", StringComparison.OrdinalIgnoreCase);
            if (i > 0) s = s.Substring(0, i);
            var dash = s.IndexOf(" - ", StringComparison.Ordinal);   // "Hug - Friendly" -> "Hug": variants share the interaction
            if (dash > 0) s = s.Substring(0, dash);
            s = s.Replace(" ", "").ToLowerInvariant();
            return Array.IndexOf(Interactions, s) >= 0 ? s : null;
        }

        public static int MaxOutcome(string key) { return key != null && ThreeOutcomes.Contains(key) ? 3 : 2; }

        public static bool Supported(string key) { return key != null && Array.IndexOf(Interactions, key) >= 0; }

        // -1, 0 or +1 in "better for the speaker" terms
        public static int Shift(Tone tone, Temperament t)
        {
            switch (t)
            {
                case Temperament.EasilyOffended: return tone == Tone.Gentle ? 1 : (tone == Tone.Playful ? 0 : -1);
                case Temperament.ThickSkinned: return tone == Tone.Gentle ? 0 : 1;
                case Temperament.Cold: return tone == Tone.Gentle ? 0 : -1;
                case Temperament.Receptive: return tone == Tone.Playful ? 1 : 0;
                default: return 0;
            }
        }

        public static int Resolve(int scriptOutcome, string key, Tone tone, Temperament t)
        {
            int max = MaxOutcome(key);
            int baseOutcome = Math.Max(1, Math.Min(max, scriptOutcome));
            // unknown outcome (0), unsupported interaction or no-shift family: leave the game's own result alone
            if (!Supported(key) || NoShift.Contains(key) || scriptOutcome < 1) return scriptOutcome;
            int shift = Shift(tone, t);
            if (BestIsLow.Contains(key)) shift = -shift;
            return Math.Max(1, Math.Min(max, baseOutcome + shift));
        }

        // how good the result is for the speaker: 1 = bad, 2 = fine, 3 = great. Two-outcome interactions only use 1 and 3.
        // Hostile and special interactions have no clear "good" result, so they always use the neutral replies (2).
        public static int Quality(int outcome, string key)
        {
            if (key != null && NoShift.Contains(key)) return 2;
            int max = MaxOutcome(key);
            int o = Math.Max(1, Math.Min(max, outcome));
            int q = BestIsLow.Contains(key) ? max - o + 1 : o;   // 1 = worst .. max = best
            return max == 2 ? (q == 1 ? 1 : 3) : q;
        }
    }
}
