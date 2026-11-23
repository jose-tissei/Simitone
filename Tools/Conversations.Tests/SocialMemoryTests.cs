using System;
using System.IO;
using Simitone.Conversations;
using Xunit;

public class SocialMemoryTests
{
    static RawSim Bob() => new RawSim { NeighborId = 1, Name = "Bob" };
    static RawSim Betty() => new RawSim { NeighborId = 2, Name = "Betty" };
    static RawSim Bella() => new RawSim { NeighborId = 3, Name = "Bella" };

    [Fact]
    public void PairMemoryIsDirectedAndIsolated()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "joke", Tone.Playful, null, 1);
        Assert.Equal(1, mem.GetPair(Bob(), Betty()).NegativeCount);
        Assert.Equal(0, mem.GetPair(Betty(), Bob()).NegativeCount);
        Assert.Equal(0, mem.GetPair(Bob(), Bella()).NegativeCount);
    }

    [Fact]
    public void BadInteractionReceivesStrongPenaltyThatDecaysOverTurns()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "joke", Tone.Bold, null, 1);
        double rightAfter = mem.InteractionAdjustment(Bob(), Betty(), "joke");
        Assert.True(rightAfter < -0.35, "Expected strong penalty right after failure, got " + rightAfter);

        // several unrelated neutral exchanges let the penalty decay gently
        for (int i = 0; i < 6; i++) mem.Record(Bob(), Betty(), "talk", Tone.Gentle, "subj_" + i, 2);
        double later = mem.InteractionAdjustment(Bob(), Betty(), "joke");
        Assert.True(later > rightAfter, "Expected penalty to decay over turns");
        Assert.True(later < 0, "Failed interaction should still be slightly disfavored");
    }

    [Fact]
    public void FailedSubjectIsPenalizedAndSuccessfulSubjectIsBoosted()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "talk", Tone.Gentle, "alien_theories", 1, listenerInterestIndex: 1);
        mem.Record(Bob(), Betty(), "talk", Tone.Gentle, "gourmet_recipes", 3, listenerInterestIndex: 4);

        Assert.True(mem.SubjectAdjustment(Bob(), Betty(), "alien_theories") < -0.30);
        Assert.True(mem.SubjectAdjustment(Bob(), Betty(), "gourmet_recipes") > 0.05);
        Assert.Contains(1, mem.GetPair(Bob(), Betty()).LearnedInterests);
        Assert.Contains(4, mem.GetPair(Bob(), Betty()).LearnedInterests);
    }

    [Fact]
    public void DeescalationTriggersAtTwoConsecutiveNegativesAndForbidsRiskyPicks()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "joke", Tone.Bold, null, 1);
        Assert.False(mem.IsDeescalating(Bob(), Betty()));

        mem.Record(Bob(), Betty(), "brag", Tone.Bold, null, 1);
        Assert.True(mem.IsDeescalating(Bob(), Betty()));
        Assert.True(mem.IsInteractionForbidden(Bob(), Betty(), "tease"));
        Assert.True(mem.IsInteractionForbidden(Bob(), Betty(), "brag"));
        Assert.True(mem.IsInteractionForbidden(Bob(), Betty(), "flirt"));
        Assert.False(mem.IsInteractionForbidden(Bob(), Betty(), "apologize"));
        Assert.False(mem.IsInteractionForbidden(Bob(), Betty(), "talk"));
        Assert.True(mem.InteractionAdjustment(Bob(), Betty(), "apologize") > 0.30);
        Assert.True(mem.InteractionAdjustment(Bob(), Betty(), "talk") > 0.20);
        Assert.True(mem.ToneAdjustment(Bob(), Betty(), Tone.Gentle) > 0.15);
        Assert.True(mem.ToneAdjustment(Bob(), Betty(), Tone.Bold) < -0.50);
    }

    [Fact]
    public void ThreeConsecutiveNegativesTriggerBackoffAndPositiveResetsStreak()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "joke", Tone.Playful, null, 1);
        mem.Record(Bob(), Betty(), "brag", Tone.Bold, null, 1);
        Assert.False(mem.ConsumeBackoff(Bob(), Betty()));

        mem.Record(Bob(), Betty(), "compliment", Tone.Gentle, null, 1);
        Assert.Equal(3, mem.GetPair(Bob(), Betty()).NegativeStreak);
        Assert.True(mem.ConsumeBackoff(Bob(), Betty()));
        Assert.True(mem.ConsumeBackoff(Bob(), Betty()));
        Assert.True(mem.ConsumeBackoff(Bob(), Betty()));
        Assert.False(mem.ConsumeBackoff(Bob(), Betty()));   // backoff finished

        // positive outcome resets streak and backoff
        mem.Record(Bob(), Betty(), "apologize", Tone.Gentle, null, 3);
        Assert.Equal(0, mem.GetPair(Bob(), Betty()).NegativeStreak);
        Assert.False(mem.IsDeescalating(Bob(), Betty()));
        Assert.False(mem.ConsumeBackoff(Bob(), Betty()));
    }

    [Fact]
    public void LearnedSkillsAreTracked()
    {
        var mem = new SocialMemory();
        mem.Record(Bob(), Betty(), "talk", Tone.Gentle, "recipes", 3, listenerInterestIndex: 4, listenerSkill: SkillKind.Cooking);
        Assert.True(mem.HasLearnedSkill(Bob(), Betty(), SkillKind.Cooking));
        Assert.False(mem.HasLearnedSkill(Bob(), Betty(), SkillKind.Logic));
    }

    [Fact]
    public void JsonRoundTripAndCorruptFileSafety()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "simitone-mem-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var mem = new SocialMemory();
            mem.Record(Bob(), Betty(), "joke", Tone.Playful, null, 1);
            mem.Record(Bob(), Betty(), "talk", Tone.Gentle, "classic_jazz", 3, listenerInterestIndex: 6, listenerSkill: SkillKind.Creativity);
            mem.Save(tmp);

            var loaded = SocialMemory.Load(tmp);
            var pair = loaded.GetPair(Bob(), Betty());
            Assert.Equal(2, pair.Exchanges);
            Assert.Equal(1, pair.NegativeCount);
            Assert.Equal(1, pair.PositiveCount);
            Assert.Equal(0, pair.NegativeStreak);
            Assert.Contains(6, pair.LearnedInterests);
            Assert.True(loaded.HasLearnedSkill(Bob(), Betty(), SkillKind.Creativity));

            File.WriteAllText(tmp, "{broken json!!!");
            var corrupt = SocialMemory.Load(tmp);
            Assert.Equal(0, corrupt.PairCount);

            var missing = SocialMemory.Load(tmp + ".missing");
            Assert.Equal(0, missing.PairCount);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
