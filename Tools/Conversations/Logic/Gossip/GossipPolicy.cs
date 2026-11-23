using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // How willing one Sim is to pass things on, worked out from the Sim and from how they feel about the listener.
    public class GossipTraits
    {
        public uint TellPpm = 500000;        // chance to tell something they could tell
        public uint CuriosityPpm = 250000;   // chance to ask about something they half-remember
        public uint GuessPpm = 500000;       // chance to guess a forgotten slot instead of leaving it out
        public int Warmth;                   // -100..100, how much they like the listener (for the overview)

        // Outgoing Sims gossip, playful ones a little more, Sims who are nice but quiet keep things to themselves;
        // the better they get on with the listener, the more they share (family and best friends tell nearly everything, rivals almost nothing)
        public static GossipTraits From(RawSim self, short strToListener, short ltrToListener)
        {
            self = self ?? new RawSim();
            double factor = 0.45 + self.Outgoing / 1000.0 * 0.85 + self.Playful / 1000.0 * 0.25;
            if (self.Nice > 650 && self.Outgoing < 400) factor -= 0.2;   // discreet
            factor = Math.Max(0.3, Math.Min(1.5, factor));
            double relation = strToListener >= 50 ? 0.85 : strToListener >= 20 ? 0.65 : strToListener >= 0 ? 0.45 : strToListener > -30 ? 0.25 : 0.08;
            if (ltrToListener >= 60) relation = Math.Min(0.95, relation + 0.10);
            return new GossipTraits
            {
                TellPpm = (uint)(Math.Min(1.0, relation * factor) * GossipRng.Ppm),
                CuriosityPpm = (uint)(Math.Max(0.1, Math.Min(0.6, self.Outgoing / 1000.0 * 0.5 + 0.1)) * GossipRng.Ppm),
                GuessPpm = (uint)(Math.Max(0.2, Math.Min(0.8, 0.3 + self.Playful / 1000.0 * 0.5)) * GossipRng.Ppm),
                Warmth = strToListener,
            };
        }
    }

    // What a Sim can see when it is their turn.
    public class TurnContext
    {
        public int Self, Other;
        public Mind SelfMind = new Mind();
        public IList<TurnRecord> Conversation = new List<TurnRecord>();
        public GossipTraits Traits = new GossipTraits();
        public IList<int> AllSims = new List<int>();         // everyone who can be named (for guessing a forgotten name)
        public int Today;
        public int MaxTurns = 8;
        public int MaxTellsEach = 2;
    }

    // The rules that stand in for a model: who speaks when, what is worth telling, when the conversation is over.
    // Same input and same generator give the same action, always.
    public static class GossipPolicy
    {
        // facts this Sim could tell the other one right now: never to the person it is about, never about themselves (that is bragging, a different
        // interaction), not back to whoever told it, not twice
        public static List<Belief> Candidates(TurnContext c)
        {
            var told = new HashSet<int>(c.Conversation.Where(t => t.Act == GossipAct.Tell).Select(t => t.FactId));
            return c.SelfMind.Beliefs.Where(b =>
                !b.ToldTo.Contains(c.Other) && b.ToldBy != c.Other && !told.Contains(b.FactId) &&
                !IsAbout(b, c.Other) && !IsAbout(b, c.Self) && b.HasValue(Slot.Who)).ToList();
        }

        static bool IsAbout(Belief b, int sim)
        {
            return (b.Get(Slot.Who).HasValue && b.Get(Slot.Who).Value == sim) || (b.Get(Slot.With).HasValue && b.Get(Slot.With).Value == sim);
        }

        public static bool HasCandidates(TurnContext c) { return Candidates(c).Count > 0; }

        public static GossipAction DecideTurn(TurnContext c, ref GossipRng rng)
        {
            var conv = c.Conversation;
            // 1. whoever opens the conversation greets
            if (conv.Count == 0) return GossipAction.Of(GossipAct.Greet);

            // keep it short: the last turn is always goodbye
            if (conv.Count >= c.MaxTurns - 1) return GossipAction.Of(GossipAct.Bye);

            var last = conv[conv.Count - 1];
            // 2. answer a question: tell what you know, or say you have no idea
            if (last.Speaker == c.Other && last.Act == GossipAct.Ask)
            {
                var b = c.SelfMind.Find(last.FactId);
                if (b == null) return new GossipAction { Act = GossipAct.NoIdea, FactId = last.FactId };
                return Tell(b, c, ref rng);
            }

            int mine = conv.Count(t => t.Speaker == c.Self);
            int tellsMine = conv.Count(t => t.Speaker == c.Self && t.Act == GossipAct.Tell);

            // 3. a chance to tell something: right after being greeted, and again after hearing something back
            if (tellsMine < c.MaxTellsEach)
            {
                var candidates = Candidates(c);
                if (candidates.Count > 0)
                {
                    var pick = candidates[rng.Below(candidates.Count)];
                    if (rng.Chance(c.Traits.TellPpm)) return Tell(pick, c, ref rng);
                }
            }

            // 4. a Sim who only half remembers something may ask about it (once per fact)
            if (mine >= 1 && conv.Any(t => t.Speaker == c.Other && t.Act == GossipAct.Tell))
            {
                var gaps = c.SelfMind.Beliefs.Where(b => b.Slots.Any(s => s.State == SlotState.Forgotten || s.State == SlotState.Doubt)
                    && !conv.Any(t => t.Speaker == c.Self && t.Act == GossipAct.Ask && t.FactId == b.FactId) && !IsAbout(b, c.Other) && !IsAbout(b, c.Self)).ToList();
                if (gaps.Count > 0 && rng.Chance(c.Traits.CuriosityPpm))
                    return new GossipAction { Act = GossipAct.Ask, FactId = gaps[rng.Below(gaps.Count)].FactId };
            }

            // 5. otherwise greet back, or leave if that is done
            return GossipAction.Of(mine == 0 ? GossipAct.Greet : GossipAct.Bye);
        }

        // What the Sim says when telling a fact: sure as it is, doubt as a guess, forgotten guessed (or left out), unknown left out.
        public static GossipAction Tell(Belief b, TurnContext c, ref GossipRng rng)
        {
            var claim = new Claim { FactId = b.FactId };
            foreach (var s in FactRules.AllSlots)
            {
                if (!FactRules.Carries(b.Kind, s)) continue;
                var sb = b.Get(s);
                int i = (int)s;
                if (sb.State == SlotState.Sure || sb.State == SlotState.Doubt)
                {
                    claim.Values[i] = sb.Value;
                    claim.Guess[i] = sb.State == SlotState.Doubt;
                }
                else if (sb.State == SlotState.Forgotten && rng.Chance(c.Traits.GuessPpm))
                {
                    int g = GuessValue(s, b, c, ref rng);
                    if (g != FactRules.NoValue) { claim.Values[i] = g; claim.Guess[i] = true; }
                }
            }
            return new GossipAction { Act = GossipAct.Tell, FactId = b.FactId, Claim = claim };
        }

        // a value inside the vocabulary, picked by chance: this is where rumours get twisted
        static int GuessValue(Slot s, Belief b, TurnContext c, ref GossipRng rng)
        {
            switch (s)
            {
                case Slot.Who:
                case Slot.With:
                {
                    var pool = c.AllSims.Where(id => id != c.Self && id != c.Other).ToList();
                    int avoid = s == Slot.Who ? (b.Get(Slot.With).HasValue ? b.Get(Slot.With).Value : 0) : (b.Get(Slot.Who).HasValue ? b.Get(Slot.Who).Value : 0);
                    pool = pool.Where(id => id != avoid).ToList();
                    return pool.Count == 0 ? FactRules.NoValue : pool[rng.Below(pool.Count)];
                }
                case Slot.Amount: return rng.Range(1, 10);
                case Slot.When: return Math.Max(0, c.Today - rng.Range(0, 3));
                default: return FactRules.NoValue;
            }
        }
    }
}
