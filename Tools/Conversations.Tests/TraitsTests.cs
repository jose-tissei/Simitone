using Simitone.Conversations;
using Xunit;

public class TraitsTests
{
    [Theory]
    [InlineData(0, 'L')] [InlineData(333, 'L')] [InlineData(334, 'M')] [InlineData(666, 'M')] [InlineData(667, 'H')] [InlineData(1000, 'H')]
    public void PersonalityBuckets(int value, char expected) { Assert.Equal(expected, TraitProfile.Level((short)value)); }

    [Theory]
    [InlineData(-100, "bad")] [InlineData(-31, "bad")] [InlineData(-30, "ok")] [InlineData(0, "ok")] [InlineData(30, "ok")] [InlineData(31, "good")] [InlineData(100, "good")]
    public void MoodBuckets(int mood, string expected) { Assert.Equal(expected, TraitProfile.MoodLevel((short)mood)); }

    [Theory]
    [InlineData("Military", "military")] [InlineData("Criminal", "criminal")] [InlineData("Medicine", "medical")] [InlineData("Science", "science")]
    [InlineData("Law Enforcement", "law")] [InlineData("Culinary", "culinary")] [InlineData("Entertainment", "entertainment")] [InlineData("Politics", "politics")]
    [InlineData("Pro Athlete", "athlete")] [InlineData("Xtreme", "xtreme")] [InlineData("Business", "business")]
    [InlineData("Gardening", "other")] [InlineData("", "none")] [InlineData(null, "none")]
    public void CareerGroups(string career, string expected) { Assert.Equal(expected, TraitProfile.CareerGroupOf(career)); }

    [Fact]
    public void DefaultsDoNotThrow()
    {
        var p = TraitProfile.From(null);
        Assert.Equal("Someone", p.Name);
        Assert.False(string.IsNullOrEmpty(p.Summary));
    }

    [Fact]
    public void ChildrenAreFlagged()
    {
        Assert.True(TraitProfile.From(new RawSim { Age = 12 }).IsChild);
        Assert.False(TraitProfile.From(new RawSim { Age = 18 }).IsChild);
    }

    [Fact]
    public void SummaryDescribesTheSimInPlainWords()
    {
        var p = TraitProfile.From(new RawSim { Name = "Bella", Nice = 900, Outgoing = 100, Playful = 500, Mood = -60, Career = "Medicine", JobTitle = "Nurse" });
        Assert.Equal("Bella is very nice, shy, playful at times, and in a bad mood. Bella works as a Nurse.", p.Summary);
    }

    [Fact]
    public void UnemployedSimsHaveNoJobSentence()
    {
        Assert.DoesNotContain("works as", TraitProfile.From(new RawSim { Name = "Bob" }).Summary);
    }

    [Fact]
    public void SummaryNeverLeaksTheTemperamentLabels()
    {
        var s = TraitProfile.From(new RawSim { Name = "X", Nice = 0, Outgoing = 0, Mood = -100 }).Summary.ToLowerInvariant();
        foreach (var w in new[] { "offended", "thick", "cold", "receptive", "hurt" }) Assert.DoesNotContain(w, s);
    }

    [Fact]
    public void KeySeparatesDifferentSimsAndMoods()
    {
        var a = TraitProfile.From(new RawSim { Name = "A", Mood = 0 });
        Assert.NotEqual(a.Key, TraitProfile.From(new RawSim { Name = "B", Mood = 0 }).Key);
        Assert.NotEqual(a.Key, TraitProfile.From(new RawSim { Name = "A", Mood = 90 }).Key);
        Assert.Equal(a.Key, TraitProfile.From(new RawSim { Name = "A", Mood = 10 }).Key);
    }

    [Fact]
    public void TemperamentNamesRoundTrip()
    {
        foreach (Temperament t in new[] { Temperament.EasilyOffended, Temperament.ThickSkinned, Temperament.Cold, Temperament.Receptive })
            Assert.Equal(t, Names.ParseTemperament(Names.Key(t)));
        Assert.Equal(Temperament.Unknown, Names.ParseTemperament("nonsense"));
        Assert.Equal(Temperament.Unknown, Names.ParseTemperament(null));
    }

    [Theory]
    [InlineData(-100, 0.0)] [InlineData(0, 50.0)] [InlineData(20, 60.0)] [InlineData(100, 100.0)]
    public void SocialPercentMapsMotiveScale(int motive, double expected)
    {
        Assert.Equal(expected, RawSim.SocialPercent((short)motive), 3);
    }

    [Fact]
    public void SkillValueAndTopSkillsWork()
    {
        var sim = new RawSim { Cooking = 700, Charisma = 500, Logic = 200, Creativity = 800, Mechanical = 0, Body = 400 };
        Assert.Equal(700, sim.SkillValue(SkillKind.Cooking));
        Assert.Equal(500, sim.SkillValue(SkillKind.Charisma));
        Assert.Equal(0, sim.SkillValue(SkillKind.None));
        var top = sim.TopSkills(400);
        Assert.Equal(new[] { SkillKind.Creativity, SkillKind.Cooking, SkillKind.Charisma, SkillKind.Body }, top);
    }

    [Fact]
    public void MemoryKeyPrefersNeighborIdThenName()
    {
        Assert.Equal("n:42", new RawSim { NeighborId = 42, Name = "Bob" }.MemoryKey());
        Assert.Equal("name:bob", new RawSim { NeighborId = 0, Name = "  Bob " }.MemoryKey());
    }
}
