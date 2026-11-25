using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public enum Reject { None = 0, NotAllowed, BadFields, UnknownFact, BadVocabulary, Repeat }

    // What a told slot is worth, measured against what the teller really holds in their own memory.
    public enum SlotVerdict
    {
        Omitted = 0,        // not said
        Faithful,           // the teller's own value, with the right certainty
        DeclaredGuess,      // a value the teller does not remember, honestly marked as a guess
        Contradiction,      // the teller is sure of a different value
        Invented,           // a value the teller never had, said as if it were fact
        HardenedDoubt,      // the teller only had a guess, and said it as fact
    }

    // One thing already said in the conversation.
    public class TurnRecord
    {
        public int Speaker;
        public GossipAct Act;
        public int FactId;
    }

    public class Validated
    {
        public Reject Reject;
        public GossipAction Action;                       // the action as it will be applied (failing slots cleared)
        public SlotVerdict[] Slots = new SlotVerdict[FactRules.SlotCount];
        public bool HasSlotFailure { get { return Slots.Any(IsFailure); } }
        public bool Ok { get { return Reject == Reject.None; } }

        public static bool IsFailure(SlotVerdict v) { return v == SlotVerdict.Contradiction || v == SlotVerdict.Invented || v == SlotVerdict.HardenedDoubt; }
    }

    // The engine decides, the proposal (from the rules, or from anything else) only suggests. Nothing is applied without passing here:
    //  1. the act is allowed now and carries exactly the fields it must,
    //  2. the fact is one the speaker really knows, and every value is inside the vocabulary (real Sims, real days),
    //  3. the speaker does not repeat what the listener already heard from them, or tell them what they just said,
    //  4. every told slot is compared with the speaker's own memory (SlotVerdict); a failing slot is cleared rather than applied.
    public static class GossipValidator
    {
        public static Validated Validate(Mind speakerMind, int speaker, int listener, IList<TurnRecord> conversation, GossipAction a, Func<int, bool> knownSim)
        {
            var v = new Validated { Action = a };
            if (a == null) { v.Reject = Reject.BadFields; return v; }
            conversation = conversation ?? new List<TurnRecord>();
            v.Reject = Check(speakerMind ?? new Mind(), speaker, listener, conversation, a, knownSim ?? (id => id > 0));
            if (v.Reject != Reject.None || a.Act != GossipAct.Tell) return v;

            var belief = speakerMind.Find(a.FactId);
            var claim = a.Claim.Clone();
            foreach (var s in FactRules.AllSlots)
            {
                int i = (int)s;
                v.Slots[i] = SlotVerdictOf(belief.Slots[i], claim.Has(s) ? claim.Values[i] : FactRules.NoValue, claim.Guess[i]);
                if (Validated.IsFailure(v.Slots[i])) claim.Values[i] = FactRules.NoValue;
                if (!claim.Has(s)) claim.Guess[i] = false;
            }
            v.Action = new GossipAction { Act = a.Act, FactId = a.FactId, Claim = claim };
            return v;
        }

        static Reject Check(Mind mind, int speaker, int listener, IList<TurnRecord> conv, GossipAction a, Func<int, bool> knownSim)
        {
            var last = conv.Count == 0 ? null : conv[conv.Count - 1];
            switch (a.Act)
            {
                case GossipAct.Bye:
                case GossipAct.None:
                    if (a.Claim != null || a.FactId != 0) return Reject.BadFields;
                    return Reject.None;

                case GossipAct.Greet:
                    if (a.Claim != null || a.FactId != 0) return Reject.BadFields;
                    if (conv.Any(t => t.Speaker == speaker && t.Act == GossipAct.Greet)) return Reject.NotAllowed;   // one greeting each
                    return Reject.None;

                case GossipAct.Ask:
                    if (a.Claim != null || a.FactId == 0) return Reject.BadFields;
                    if (mind.Find(a.FactId) == null) return Reject.UnknownFact;
                    if (conv.Any(t => t.Speaker == speaker && t.Act == GossipAct.Ask && t.FactId == a.FactId)) return Reject.Repeat;
                    return Reject.None;

                case GossipAct.NoIdea:
                    if (a.Claim != null || a.FactId == 0) return Reject.BadFields;
                    if (last == null || last.Speaker == speaker || last.Act != GossipAct.Ask || last.FactId != a.FactId) return Reject.NotAllowed;
                    if (mind.Find(a.FactId) != null) return Reject.NotAllowed;   // they do know it: saying they don't would be a lie
                    return Reject.None;

                case GossipAct.Tell:
                {
                    if (a.Claim == null || a.FactId == 0 || a.Claim.FactId != a.FactId) return Reject.BadFields;
                    var belief = mind.Find(a.FactId);
                    if (belief == null) return Reject.UnknownFact;
                    foreach (var s in FactRules.AllSlots)
                    {
                        if (!a.Claim.Has(s)) continue;
                        if (!FactRules.Carries(belief.Kind, s)) return Reject.BadFields;
                        int value = a.Claim.Values[(int)s];
                        if (!InVocabulary(s, value, knownSim)) return Reject.BadVocabulary;
                    }
                    bool answering = last != null && last.Speaker != speaker && last.Act == GossipAct.Ask && last.FactId == a.FactId;
                    if (!answering)
                    {
                        if (belief.ToldTo.Contains(listener)) return Reject.Repeat;
                        if (belief.ToldBy == listener) return Reject.Repeat;
                        if (conv.Any(t => t.Speaker != speaker && t.Act == GossipAct.Tell && t.FactId == a.FactId)) return Reject.Repeat;
                        if (conv.Any(t => t.Speaker == speaker && t.Act == GossipAct.Tell && t.FactId == a.FactId)) return Reject.Repeat;
                    }
                    return Reject.None;
                }
            }
            return Reject.BadFields;
        }

        static bool InVocabulary(Slot slot, int value, Func<int, bool> knownSim)
        {
            if (value == FactRules.NoValue) return true;
            switch (slot)
            {
                case Slot.Who:
                case Slot.With: return knownSim(value);
                default: return value >= 0;
            }
        }

        // the table of verdicts; a sure or doubt slot without a value (which memory never holds) is read as unknown
        public static SlotVerdict SlotVerdictOf(SlotBelief mine, int said, bool guess)
        {
            if (said == FactRules.NoValue) return SlotVerdict.Omitted;
            var state = mine.State;
            if ((state == SlotState.Sure || state == SlotState.Doubt) && mine.Value == FactRules.NoValue) state = SlotState.Unknown;
            bool same = mine.Value == said;
            switch (state)
            {
                case SlotState.Sure: return same ? SlotVerdict.Faithful : SlotVerdict.Contradiction;
                case SlotState.Doubt:
                    if (same) return guess ? SlotVerdict.Faithful : SlotVerdict.HardenedDoubt;
                    return guess ? SlotVerdict.DeclaredGuess : SlotVerdict.Invented;
                default:
                    return guess ? SlotVerdict.DeclaredGuess : SlotVerdict.Invented;
            }
        }
    }
}
