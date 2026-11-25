using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class GossipBookTests
{
    static GossipBook Book(out Fact promo, out Fact skill)
    {
        var b = new GossipBook();
        foreach (var id in new[] { 1, 2, 3, 4 }) b.SetName(id, "Sim" + id);
        promo = b.AddFact(FactKind.Promoted, "Recruit", 1, 0, 0, 5);
        skill = b.AddFact(FactKind.SkillGain, "Cooking", 2, 0, 4, 6);
        return b;
    }

    [Fact]
    public void TheSameEventOnTheSameDayIsOneFact()
    {
        var b = new GossipBook();
        var a = b.AddFact(FactKind.Romance, "", 1, 2, 0, 3);
        Assert.Same(a, b.AddFact(FactKind.Romance, "", 1, 2, 0, 3));
        Assert.NotSame(a, b.AddFact(FactKind.Romance, "", 1, 2, 0, 4));
        Assert.Equal(2, b.FactCount);
    }

    [Fact]
    public void SeeingItHappenMakesEverySlotSure()
    {
        Fact p, s; var b = Book(out p, out s);
        b.Witness(2, s.Id, 10);
        var belief = b.GetMind(2).Find(s.Id);
        Assert.True(belief.FirstHand);
        foreach (var slot in new[] { Slot.Who, Slot.Amount, Slot.When }) Assert.Equal(SlotState.Sure, belief.Get(slot).State);
        Assert.Equal(4, belief.Get(Slot.Amount).Value);
        Assert.Equal(SlotState.Unknown, belief.Get(Slot.With).State);   // a skill gain has no second person
    }

    [Fact]
    public void HearingFillsGapsAndNeverOverwritesWhatTheListenerIsSureOf()
    {
        Fact p, s; var b = Book(out p, out s);
        b.Witness(2, s.Id, 1);
        var claim = new Claim { FactId = s.Id }; claim.Values[(int)Slot.Who] = 2; claim.Values[(int)Slot.Amount] = 9; claim.Values[(int)Slot.When] = 6;
        var r = b.Hear(2, 3, claim, 2);
        Assert.Equal(new[] { Slot.Amount }, r.Conflicts.ToArray());
        Assert.Equal(4, b.GetMind(2).Find(s.Id).Get(Slot.Amount).Value);   // unchanged
        Assert.False(r.IsNew);

        var r2 = b.Hear(4, 3, claim, 2);
        Assert.True(r2.IsNew);
        Assert.Equal(3, r2.Filled.Count);
        Assert.Equal(3, b.GetMind(4).Find(s.Id).ToldBy);
    }

    [Fact]
    public void AGuessIsAdoptedAsADoubtAndHardensWhenTheSameThingIsToldAsFact()
    {
        Fact p, s; var b = Book(out p, out s);
        var guess = new Claim { FactId = s.Id }; guess.Values[(int)Slot.Who] = 2; guess.Values[(int)Slot.Amount] = 7; guess.Guess[(int)Slot.Amount] = true;
        b.Hear(4, 3, guess, 1);
        Assert.Equal(SlotState.Doubt, b.GetMind(4).Find(s.Id).Get(Slot.Amount).State);
        var fact = guess.Clone(); fact.Guess[(int)Slot.Amount] = false; fact.Values[(int)Slot.Amount] = 4;
        b.Hear(4, 1, fact, 2);
        var slot = b.GetMind(4).Find(s.Id).Get(Slot.Amount);
        Assert.Equal(SlotState.Sure, slot.State);
        Assert.Equal(4, slot.Value);
    }

    [Fact]
    public void ForgettingErasesTheOldValue()
    {
        Fact p, s; var b = Book(out p, out s);
        b.Witness(3, s.Id, 1);
        var heard = new Claim { FactId = p.Id }; heard.Values[(int)Slot.Who] = 1; heard.Values[(int)Slot.When] = 5;
        b.Hear(3, 1, heard, 1);
        b.Forget(3, 77, 100, 1000000, 0);   // everything is forgotten
        foreach (var belief in b.GetMind(3).Beliefs)
            foreach (var slot in belief.Slots.Where(x => x.State == SlotState.Forgotten)) Assert.Equal(FactRules.NoValue, slot.Value);
        Assert.Contains(b.GetMind(3).Find(p.Id).Slots, x => x.State == SlotState.Forgotten);
        // what a Sim saw with their own eyes about who it was is never forgotten
        Assert.Equal(SlotState.Sure, b.GetMind(3).Find(s.Id).Get(Slot.Who).State);
    }

    [Fact]
    public void TheBookSurvivesSavingAndLoading()
    {
        Fact p, s; var b = Book(out p, out s);
        b.Witness(1, p.Id, 1); b.MarkTold(1, p.Id, 2);
        var path = Path.Combine(Path.GetTempPath(), "gossip-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            b.Save(path);
            var back = GossipBook.Load(path);
            Assert.Equal(b.FactCount, back.FactCount);
            Assert.Equal("Sim1", back.NameOf(1));
            var belief = back.GetMind(1).Find(p.Id);
            Assert.Equal(SlotState.Sure, belief.Get(Slot.Who).State);
            Assert.Equal(new[] { 2 }, belief.ToldTo.ToArray());
            Assert.Equal(5, back.GetFact(p.Id).Get(Slot.When));
        }
        finally { File.Delete(path); }
        Assert.Equal(0, GossipBook.Load(@"C:\nowhere\x.json").FactCount);
    }

    [Fact]
    public void OldFactsAreDroppedWithTheBeliefsAboutThem()
    {
        Fact p, s; var b = Book(out p, out s);
        b.Witness(1, p.Id, 1);
        b.Prune(5 + GossipBook.KeepDays + 1);
        Assert.Null(b.GetFact(p.Id));
        Assert.Empty(b.GetMind(1).Beliefs);
    }
}

public class GossipValidatorTests
{
    static Mind MindWith(Belief b) { var m = new Mind(); m.Beliefs.Add(b); return m; }

    static Belief Promo(int who = 1, int when = 5)
    {
        var b = new Belief { FactId = 7, Kind = FactKind.Promoted, Label = "Recruit", ToldBy = 0 };
        b.Get(Slot.Who).State = SlotState.Sure; b.Get(Slot.Who).Value = who;
        b.Get(Slot.When).State = SlotState.Sure; b.Get(Slot.When).Value = when;
        return b;
    }

    static GossipAction Tell(int fact, int who, int when, bool whenGuess = false)
    {
        var c = new Claim { FactId = fact };
        c.Values[(int)Slot.Who] = who; c.Values[(int)Slot.When] = when; c.Guess[(int)Slot.When] = whenGuess;
        return new GossipAction { Act = GossipAct.Tell, FactId = fact, Claim = c };
    }

    static Validated V(Mind m, GossipAction a, IList<TurnRecord> conv = null, int me = 2, int other = 3)
    {
        return GossipValidator.Validate(m, me, other, conv ?? new List<TurnRecord>(), a, id => id >= 1 && id <= 9);
    }

    [Fact]
    public void FaithfulTellPasses()
    {
        var v = V(MindWith(Promo()), Tell(7, 1, 5));
        Assert.True(v.Ok);
        Assert.Equal(SlotVerdict.Faithful, v.Slots[(int)Slot.Who]);
        Assert.Equal(SlotVerdict.Faithful, v.Slots[(int)Slot.When]);
        Assert.False(v.HasSlotFailure);
    }

    [Fact]
    public void ASureSlotToldDifferentlyIsAContradictionAndIsCleared()
    {
        var v = V(MindWith(Promo()), Tell(7, 1, 3));
        Assert.Equal(SlotVerdict.Contradiction, v.Slots[(int)Slot.When]);
        Assert.False(v.Action.Claim.Has(Slot.When));
        Assert.True(v.Action.Claim.Has(Slot.Who));
    }

    [Fact]
    public void AValueTheSimNeverHadIsInventedUnlessItIsMarkedAsAGuess()
    {
        var b = Promo(); b.Get(Slot.When).State = SlotState.Forgotten; b.Get(Slot.When).Value = FactRules.NoValue;
        Assert.Equal(SlotVerdict.Invented, V(MindWith(b), Tell(7, 1, 4)).Slots[(int)Slot.When]);
        Assert.Equal(SlotVerdict.DeclaredGuess, V(MindWith(b), Tell(7, 1, 4, true)).Slots[(int)Slot.When]);
    }

    [Fact]
    public void ADoubtToldAsFactIsHardenedDoubtAndIsCleared()
    {
        var b = Promo(); b.Get(Slot.When).State = SlotState.Doubt;
        var v = V(MindWith(b), Tell(7, 1, 5));
        Assert.Equal(SlotVerdict.HardenedDoubt, v.Slots[(int)Slot.When]);
        Assert.False(v.Action.Claim.Has(Slot.When));
        Assert.Equal(SlotVerdict.Faithful, V(MindWith(b), Tell(7, 1, 5, true)).Slots[(int)Slot.When]);
    }

    [Fact]
    public void AFactTheSimDoesNotKnowIsRefused()
    {
        Assert.Equal(Reject.UnknownFact, V(new Mind(), Tell(7, 1, 5)).Reject);
        Assert.Equal(Reject.UnknownFact, V(new Mind(), new GossipAction { Act = GossipAct.Ask, FactId = 7 }).Reject);
    }

    [Fact]
    public void NamesAndDaysMustBeInsideTheVocabulary()
    {
        Assert.Equal(Reject.BadVocabulary, V(MindWith(Promo()), Tell(7, 99, 5)).Reject);
        Assert.Equal(Reject.BadVocabulary, V(MindWith(Promo()), Tell(7, 1, -2)).Reject);
    }

    [Fact]
    public void ASlotTheKindDoesNotCarryIsRefused()
    {
        var a = Tell(7, 1, 5); a.Claim.Values[(int)Slot.Amount] = 3;
        Assert.Equal(Reject.BadFields, V(MindWith(Promo()), a).Reject);
    }

    [Fact]
    public void NothingIsToldTwiceToTheSameListener()
    {
        var b = Promo(); b.ToldTo.Add(3);
        Assert.Equal(Reject.Repeat, V(MindWith(b), Tell(7, 1, 5)).Reject);
        var c = Promo(); c.ToldBy = 3;   // they told it to me
        Assert.Equal(Reject.Repeat, V(MindWith(c), Tell(7, 1, 5)).Reject);
        var told = new List<TurnRecord> { new TurnRecord { Speaker = 3, Act = GossipAct.Tell, FactId = 7 } };
        Assert.Equal(Reject.Repeat, V(MindWith(Promo()), Tell(7, 1, 5), told).Reject);
    }

    [Fact]
    public void AnAnswerToAQuestionMayRepeatWhatWasAlreadyTold()
    {
        var b = Promo(); b.ToldTo.Add(3);
        var asked = new List<TurnRecord> { new TurnRecord { Speaker = 3, Act = GossipAct.Ask, FactId = 7 } };
        Assert.True(V(MindWith(b), Tell(7, 1, 5), asked).Ok);
    }

    [Fact]
    public void SayingYouHaveNoIdeaIsOnlyHonestWhenYouDont()
    {
        var asked = new List<TurnRecord> { new TurnRecord { Speaker = 3, Act = GossipAct.Ask, FactId = 7 } };
        var noIdea = new GossipAction { Act = GossipAct.NoIdea, FactId = 7 };
        Assert.True(V(new Mind(), noIdea, asked).Ok);
        Assert.Equal(Reject.NotAllowed, V(MindWith(Promo()), noIdea, asked).Reject);
        Assert.Equal(Reject.NotAllowed, V(new Mind(), noIdea).Reject);   // nobody asked
    }

    [Fact]
    public void OneGreetingEachAndFieldsMustMatchTheAct()
    {
        var greeted = new List<TurnRecord> { new TurnRecord { Speaker = 2, Act = GossipAct.Greet } };
        Assert.Equal(Reject.NotAllowed, V(new Mind(), GossipAction.Of(GossipAct.Greet), greeted).Reject);
        Assert.Equal(Reject.BadFields, V(new Mind(), new GossipAction { Act = GossipAct.Bye, FactId = 3 }).Reject);
        Assert.Equal(Reject.BadFields, V(new Mind(), new GossipAction { Act = GossipAct.Tell, FactId = 3 }).Reject);
        Assert.Equal(Reject.BadFields, GossipValidator.Validate(new Mind(), 1, 2, null, null, null).Reject);
    }
}

public class GossipPolicyTests
{
    static TurnContext Ctx(Mind mine, params TurnRecord[] conv)
    {
        return new TurnContext
        {
            Self = 2, Other = 3, SelfMind = mine, Conversation = conv.ToList(), AllSims = new[] { 1, 2, 3, 4, 5 },
            Traits = new GossipTraits { TellPpm = 1000000, CuriosityPpm = 0 }, Today = 6,
        };
    }

    static Mind MindWithPromo(int who = 1)
    {
        var b = new Belief { FactId = 7, Kind = FactKind.Promoted, Label = "Recruit" };
        b.Get(Slot.Who).State = SlotState.Sure; b.Get(Slot.Who).Value = who;
        b.Get(Slot.When).State = SlotState.Sure; b.Get(Slot.When).Value = 5;
        var m = new Mind(); m.Beliefs.Add(b); return m;
    }

    static TurnRecord T(int speaker, GossipAct act, int fact = 0) { return new TurnRecord { Speaker = speaker, Act = act, FactId = fact }; }

    [Fact]
    public void WhoOpensGreets()
    {
        var rng = new GossipRng(1);
        Assert.Equal(GossipAct.Greet, GossipPolicy.DecideTurn(Ctx(new Mind()), ref rng).Act);
    }

    [Fact]
    public void AQuestionGetsAnAnswerOrAnHonestNoIdea()
    {
        var rng = new GossipRng(1);
        var asked = T(3, GossipAct.Ask, 7);
        var knows = GossipPolicy.DecideTurn(Ctx(MindWithPromo(), T(2, GossipAct.Greet), asked), ref rng);
        Assert.Equal(GossipAct.Tell, knows.Act);
        var doesNot = GossipPolicy.DecideTurn(Ctx(new Mind(), T(2, GossipAct.Greet), asked), ref rng);
        Assert.Equal(GossipAct.NoIdea, doesNot.Act);
    }

    [Fact]
    public void ASimWithSomethingToTellTellsIt()
    {
        var rng = new GossipRng(1);
        var a = GossipPolicy.DecideTurn(Ctx(MindWithPromo(), T(3, GossipAct.Greet)), ref rng);
        Assert.Equal(GossipAct.Tell, a.Act);
        Assert.Equal(7, a.FactId);
    }

    [Fact]
    public void NobodyIsToldAboutThemselves()
    {
        var rng = new GossipRng(1);
        var c = Ctx(MindWithPromo(who: 3), T(3, GossipAct.Greet));   // the promotion is the listener's own
        Assert.False(GossipPolicy.HasCandidates(c));
        Assert.Equal(GossipAct.Greet, GossipPolicy.DecideTurn(c, ref rng).Act);
    }

    [Fact]
    public void WithNothingLeftToSayTheSimSaysGoodbye()
    {
        var rng = new GossipRng(1);
        var a = GossipPolicy.DecideTurn(Ctx(new Mind(), T(3, GossipAct.Greet), T(2, GossipAct.Greet), T(3, GossipAct.Bye)), ref rng);
        Assert.Equal(GossipAct.Bye, a.Act);
    }

    [Fact]
    public void TheLastTurnIsAlwaysGoodbye()
    {
        var rng = new GossipRng(1);
        var conv = Enumerable.Range(0, 7).Select(i => T(i % 2 == 0 ? 3 : 2, GossipAct.Greet)).ToArray();
        var c = Ctx(MindWithPromo(), conv); c.MaxTurns = 8;
        Assert.Equal(GossipAct.Bye, GossipPolicy.DecideTurn(c, ref rng).Act);
    }

    [Fact]
    public void ASimWhoForgotASlotMayGuessItAndTheGuessIsAlwaysMarked()
    {
        var m = MindWithPromo();
        m.Beliefs[0].Get(Slot.When).State = SlotState.Forgotten; m.Beliefs[0].Get(Slot.When).Value = FactRules.NoValue;
        int guessed = 0, omitted = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var rng = new GossipRng(seed);
            var c = Ctx(m, T(3, GossipAct.Greet)); c.Traits.GuessPpm = 500000;
            var a = GossipPolicy.DecideTurn(c, ref rng);
            if (a.Act != GossipAct.Tell) continue;
            if (a.Claim.Has(Slot.When)) { guessed++; Assert.True(a.Claim.Guess[(int)Slot.When]); } else omitted++;
        }
        Assert.True(guessed > 40 && omitted > 40, "guessed " + guessed + ", omitted " + omitted);
    }

    [Fact]
    public void TraitsMakeOutgoingSimsTalkAndRivalsStaySilent()
    {
        var chatty = new RawSim { Outgoing = 900, Playful = 800, Nice = 500 };
        var quiet = new RawSim { Outgoing = 150, Playful = 300, Nice = 800 };
        Assert.True(GossipTraits.From(chatty, 60, 0).TellPpm > GossipTraits.From(quiet, 60, 0).TellPpm);
        Assert.True(GossipTraits.From(chatty, 60, 0).TellPpm > GossipTraits.From(chatty, -60, 0).TellPpm * 4);
        Assert.True(GossipTraits.From(chatty, 60, 80).TellPpm >= GossipTraits.From(chatty, 60, 0).TellPpm);
    }
}

public class GossipConversationTests
{
    // a neighbourhood of nine Sims with a handful of events that some of them saw and some were told
    static GossipBook World(ulong seed, out List<int> sims)
    {
        var rng = new GossipRng(seed);
        var b = new GossipBook();
        sims = Enumerable.Range(1, 9).ToList();
        foreach (var id in sims) b.SetName(id, "Sim" + id);
        var kinds = (FactKind[])Enum.GetValues(typeof(FactKind));
        for (int i = 0; i < 8; i++)
        {
            var kind = kinds[rng.Below(kinds.Length)];
            int who = rng.Range(1, 9), with = rng.Range(1, 9);
            if (with == who) with = who % 9 + 1;
            var f = b.AddFact(kind, kind == FactKind.Promoted ? "Recruit" : kind == FactKind.SkillGain ? "Cooking" : "", who, with, rng.Range(1, 10), rng.Range(1, 6));
            b.Witness(who, f.Id, 1);
            for (int w = 0; w < 3; w++) b.Witness(rng.Range(1, 9), f.Id, 1);
        }
        for (int i = 0; i < 4; i++) b.Forget(rng.Range(1, 9), seed, i + 1, 400000, 100000);
        return b;
    }

    static GossipTraits Tr(int s) { return new GossipTraits { TellPpm = 300000u + (uint)(s * 70000), CuriosityPpm = 300000, GuessPpm = 600000 }; }

    [Fact]
    public void ConversationsAreReplayableFromTheSeed()
    {
        List<int> sims;
        var one = GossipConversation.Run(World(5, out sims), 1, 2, Tr(5), Tr(3), sims, 7, 100, 42);
        var two = GossipConversation.Run(World(5, out sims), 1, 2, Tr(5), Tr(3), sims, 7, 100, 42);
        Assert.Equal(Describe(one), Describe(two));
    }

    static string Describe(GossipTranscript t)
    {
        return string.Join(";", t.Turns.Select(x => x.Speaker + ":" + x.Act + ":" + x.FactId + (x.Claim == null ? "" : "[" + string.Join(",", x.Claim.Values) + "|" + string.Join(",", x.Claim.Guess) + "]")));
    }

    // the frozen test: 2,000 conversations in 200 fixed worlds. The engine must refuse nothing, clear nothing, and never run past eight turns.
    [Fact]
    public void FrozenScenariosNeverProduceARejectedOrInventedClaim()
    {
        int conversations = 0, withContent = 0, tells = 0, guesses = 0, newFacts = 0, longest = 0;
        for (ulong world = 1; world <= 200; world++)
        {
            List<int> sims;
            var b = World(world, out sims);
            for (int c = 0; c < 10; c++)
            {
                var rng = new GossipRng(world * 1000 + (ulong)c);
                int x = rng.Range(1, 9), y = rng.Range(1, 9);
                if (x == y) y = x % 9 + 1;
                var t = GossipConversation.Run(b, x, y, Tr(rng.Below(10)), Tr(rng.Below(10)), sims, 7, 10 + c, world);
                Assert.Equal(0, t.Rejected);
                Assert.Equal(0, t.FailedSlots);
                Assert.True(t.Turns.Count <= 8);
                Assert.Equal(GossipAct.Greet == t.Turns[0].Act, true);
                foreach (var turn in t.Turns)
                {
                    Assert.True(turn.Validation.Ok);
                    if (turn.Act != GossipAct.Tell) continue;
                    tells++;
                    foreach (var s in FactRules.AllSlots) if (turn.Claim.Has(s) && turn.Claim.Guess[(int)s]) guesses++;
                    if (turn.Heard.IsNew) newFacts++;
                }
                conversations++; if (t.HasContent) withContent++;
                longest = Math.Max(longest, t.Turns.Count);
                b.Apply(t);   // later conversations in the same world build on this one
            }
        }
        Assert.Equal(2000, conversations);
        Assert.True(withContent > 400, "conversations with news: " + withContent);
        Assert.True(newFacts > 100, "facts that were news: " + newFacts);
        Assert.True(guesses > 0, "no rumour was ever twisted");
        Assert.True(longest >= 4);
    }

    [Fact]
    public void ARumourSpreadsFromOneWitnessToTheWholeStreetAndGetsTwistedOnTheWay()
    {
        var b = new GossipBook(); var sims = Enumerable.Range(1, 9).ToList();
        foreach (var id in sims) b.SetName(id, "Sim" + id);
        var f = b.AddFact(FactKind.SkillGain, "Cooking", 1, 0, 6, 3);
        b.Witness(2, f.Id, 1);   // Sim 2 saw it and gossips (Sims never gossip about themselves)
        var talkative = new GossipTraits { TellPpm = 1000000, CuriosityPpm = 400000, GuessPpm = 700000 };
        for (int round = 0; round < 150; round++)
        {
            for (int day = 0; day < 1; day++) foreach (var id in sims) b.Forget(id, 3, round, GossipBook.DefaultForgetPpm, GossipBook.DefaultDoubtDropPpm);
            var rng = new GossipRng((ulong)round + 1);
            int x = rng.Range(1, 9), y = rng.Range(1, 9); if (x == y) y = x % 9 + 1;
            b.Apply(GossipConversation.Run(b, x, y, talkative, talkative, sims, 4, round, 11));
        }
        int knowers = sims.Count(id => b.GetMind(id).Find(f.Id) != null);
        Assert.True(knowers >= 6, "only " + knowers + " Sims ever heard it");
        int off = sims.Count(id => { var bel = b.GetMind(id).Find(f.Id); return bel != null && bel.Get(Slot.Amount).HasValue && bel.Get(Slot.Amount).Value != 6; });
        Assert.True(off > 0, "the value never drifted: " + off);   // retelling from a faded memory twists it, and only ever as a declared guess
    }

    [Fact]
    public void ApplyingATranscriptGivesTheListenerTheNewsAndTheTellerAMemoryOfTellingIt()
    {
        var b = new GossipBook(); var sims = new List<int> { 1, 2, 3 };
        foreach (var id in sims) b.SetName(id, "Sim" + id);
        var f = b.AddFact(FactKind.Promoted, "Recruit", 1, 0, 0, 4);
        b.Witness(2, f.Id, 1);
        var always = new GossipTraits { TellPpm = 1000000 };
        var t = GossipConversation.Run(b, 2, 3, always, always, sims, 5, 20, 9);
        Assert.True(t.HasContent);
        Assert.Null(b.GetMind(3).Find(f.Id));   // nothing changes until it is applied
        b.Apply(t);
        Assert.NotNull(b.GetMind(3).Find(f.Id));
        Assert.Equal(2, b.GetMind(3).Find(f.Id).ToldBy);
        Assert.Contains(3, b.GetMind(2).Find(f.Id).ToldTo);
    }
}

public class GossipPlannerTests
{
    static RawSim Sim(string name, short id, short outgoing = 800) { return new RawSim { Name = name, NeighborId = id, Outgoing = outgoing, Playful = 600, Nice = 500, StrToTarget = 55, LtrToTarget = 30, TargetStrToMe = 55, TargetLtrToMe = 30, Social = -50 }; }

    static GossipBook BookWithNews()
    {
        var b = new GossipBook();
        b.SetName(1, "Bob"); b.SetName(2, "Betty"); b.SetName(3, "Carl");
        var f = b.AddFact(FactKind.Promoted, "Recruit", 3, 0, 0, 5);
        b.Witness(1, f.Id, 1);
        return b;
    }

    static DialogueBank RepoBank()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "Tools", "Conversations", "bank", "bank.json");
            if (File.Exists(c)) return DialogueBank.Load(c);
            dir = dir.Parent;
        }
        return new DialogueBank();
    }

    [Fact]
    public void ASimWithNewsOftenSharesItAndTheWholeChatIsPlanned()
    {
        var bank = RepoBank(); int gossipPlans = 0;
        for (int seed = 1; seed <= 60; seed++)
        {
            var book = BookWithNews();
            var plan = ParallelSocialPlanner.Plan(Sim("Bob", 1), Sim("Betty", 2), null, bank, AiSubjectBank.Builtin(), new SocialMemory(), new Random(seed), book, 6, 100);
            if (plan.Interaction != "gossip") continue;
            gossipPlans++;
            Assert.NotNull(plan.Gossip);
            Assert.True(plan.Gossip.HasContent);
            Assert.True(plan.Balloons.Count >= 2);
            Assert.Equal(1, plan.Balloons[0].Speaker);                   // the speaker starts
            Assert.All(plan.Balloons, b => Assert.False(string.IsNullOrWhiteSpace(b.Text)));
            Assert.Contains(plan.Quality, new[] { 2, 3 });
            Assert.Null(book.GetMind(2).Find(1));                        // planning alone changes nothing
        }
        Assert.True(gossipPlans >= 15, "gossip was chosen " + gossipPlans + " times of 60");
    }

    [Fact]
    public void WithNothingToTellThereIsNoGossip()
    {
        var bank = RepoBank();
        for (int seed = 1; seed <= 40; seed++)
        {
            var book = new GossipBook();
            var plan = ParallelSocialPlanner.Plan(Sim("Bob", 1), Sim("Betty", 2), null, bank, AiSubjectBank.Builtin(), new SocialMemory(), new Random(seed), book, 6, 100);
            Assert.NotEqual("gossip", plan.Interaction);
        }
    }

    [Fact]
    public void NewsAboutTheListenerIsNotTold()
    {
        var bank = RepoBank();
        for (int seed = 1; seed <= 40; seed++)
        {
            var book = BookWithNews();   // the promotion is Carl's
            var plan = ParallelSocialPlanner.Plan(Sim("Bob", 1), Sim("Carl", 3), null, bank, AiSubjectBank.Builtin(), new SocialMemory(), new Random(seed), book, 6, 100);
            Assert.NotEqual("gossip", plan.Interaction);
        }
    }

    [Fact]
    public void ApplyingThePlanTeachesTheListener()
    {
        var bank = RepoBank();
        for (int seed = 1; seed <= 60; seed++)
        {
            var book = BookWithNews();
            var plan = ParallelSocialPlanner.Plan(Sim("Bob", 1), Sim("Betty", 2), null, bank, AiSubjectBank.Builtin(), new SocialMemory(), new Random(seed), book, 6, 100);
            if (plan.Interaction != "gossip") continue;
            book.Apply(plan.Gossip);
            Assert.NotNull(book.GetMind(2).Find(1));
            return;
        }
        Assert.True(false, "never chose to gossip");
    }

    [Fact]
    public void WithoutABookThePlannerBehavesAsBefore()
    {
        var plan = ParallelSocialPlanner.Plan(Sim("Bob", 1), Sim("Betty", 2), null, RepoBank(), AiSubjectBank.Builtin(), new SocialMemory(), new Random(3));
        Assert.NotEqual("gossip", plan.Interaction);
        Assert.Null(plan.Gossip);
    }
}
