using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // One turn of a finished conversation, with everything needed to put it into words and to show it in the memory window.
    public class GossipTurn
    {
        public int Speaker, Listener;
        public GossipAct Act;
        public int FactId;
        public Claim Claim;                              // what was really said (failing slots cleared), for Tell
        public Validated Validation;
        public HearResult Heard;                         // for Tell: what changed in the listener's mind
        public Reaction ReactionToPrevious;              // how the speaker took what the other Sim just told them
        public Tone Tone;                                // the speaker's way of talking
        public FactKind Kind;                            // what the fact is about (for Tell and Ask)
        public string Label = "";                        // job title or skill name
        public int ToldBy;                               // who told the speaker (0 = saw it themselves)
        public int SpeakerWho = FactRules.NoValue, SpeakerWith = FactRules.NoValue;   // whom the speaker thinks it is about (for Ask)
    }

    // a finished line, ready to show over a Sim's head
    public class GossipBalloon
    {
        public int Speaker;      // neighbour id
        public string Text;
        public GossipAct Act;
    }

    public class GossipEffect
    {
        public int Teller, Listener;
        public Claim Claim;
    }

    public class GossipTranscript
    {
        public int A, B;
        public long Tick;
        public List<GossipTurn> Turns = new List<GossipTurn>();
        public List<GossipEffect> Effects = new List<GossipEffect>();   // what to apply to the real book
        public int Rejected;                                            // proposals the validator refused (the Sim said goodbye instead)
        public int FailedSlots;                                         // told slots the validator had to clear
        public bool HasContent { get { return Turns.Any(t => t.Act == GossipAct.Tell); } }
    }

    // Plays a whole conversation between two Sims on a scratch copy of their minds, so it can run off the game thread.
    // The real book is only touched when the transcript is applied (GossipBook.Apply), on the game thread.
    public static class GossipConversation
    {
        public static GossipTranscript Run(GossipBook book, int a, int b, GossipTraits traitsA, GossipTraits traitsB, IList<int> allSims, int today, long tick, ulong seed, int maxTurns = 8, Tone toneA = Tone.Gentle, Tone toneB = Tone.Gentle)
        {
            var scratch = book.CloneFor(a, b);
            var t = new GossipTranscript { A = a, B = b, Tick = tick };
            var conv = new List<TurnRecord>();
            var rngA = new GossipRng(GossipRng.Derive(seed, tick, a, 2));
            var rngB = new GossipRng(GossipRng.Derive(seed, tick, b, 2));
            Func<int, bool> known = id => scratch.KnownSim(id) || allSims.Contains(id);
            int speaker = a;
            Reaction pending = Reaction.None;

            for (int i = 0; i < maxTurns; i++)
            {
                int other = speaker == a ? b : a;
                var ctx = new TurnContext
                {
                    Self = speaker, Other = other, SelfMind = scratch.GetMind(speaker), Conversation = conv,
                    Traits = speaker == a ? traitsA : traitsB, AllSims = allSims, Today = today, MaxTurns = maxTurns,
                };
                var action = speaker == a ? GossipPolicy.DecideTurn(ctx, ref rngA) : GossipPolicy.DecideTurn(ctx, ref rngB);
                var v = GossipValidator.Validate(ctx.SelfMind, speaker, other, conv, action, known);
                if (!v.Ok)
                {
                    t.Rejected++;
                    action = GossipAction.Of(GossipAct.Bye);   // never applied: the engine decides
                    v = GossipValidator.Validate(ctx.SelfMind, speaker, other, conv, action, known);
                }
                var turn = new GossipTurn { Speaker = speaker, Listener = other, Act = v.Action.Act, FactId = v.Action.FactId, Validation = v, ReactionToPrevious = pending, Tone = speaker == a ? toneA : toneB };
                var own = v.Action.FactId == 0 ? null : ctx.SelfMind.Find(v.Action.FactId);
                if (own != null)
                {
                    turn.Kind = own.Kind; turn.Label = own.Label; turn.ToldBy = own.ToldBy;
                    if (own.Get(Slot.Who).HasValue) turn.SpeakerWho = own.Get(Slot.Who).Value;
                    if (own.Get(Slot.With).HasValue) turn.SpeakerWith = own.Get(Slot.With).Value;
                }
                pending = Reaction.None;
                if (v.Action.Act == GossipAct.Tell)
                {
                    turn.Claim = v.Action.Claim;
                    t.FailedSlots += v.Slots.Count(Validated.IsFailure);
                    turn.Heard = scratch.Hear(other, speaker, turn.Claim, tick);
                    scratch.MarkTold(speaker, turn.FactId, other);
                    t.Effects.Add(new GossipEffect { Teller = speaker, Listener = other, Claim = turn.Claim });
                    pending = turn.Heard.IsNew ? Reaction.Surprised : turn.Heard.Conflicts.Count > 0 ? Reaction.Doubtful : turn.Heard.Filled.Count > 0 ? Reaction.FilledGap : Reaction.KnewIt;
                }
                t.Turns.Add(turn);
                conv.Add(new TurnRecord { Speaker = speaker, Act = turn.Act, FactId = turn.FactId });
                if (turn.Act == GossipAct.Bye || turn.Act == GossipAct.None) break;
                speaker = other;
            }
            return t;
        }
    }
}
