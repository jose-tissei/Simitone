using System;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class TopicLogicTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(199, 0)] [InlineData(200, 1)] [InlineData(300, 1)] [InlineData(599, 1)] [InlineData(600, 1)] [InlineData(601, 2)] [InlineData(1000, 2)]
    public void Liking(int interest, int expected) { Assert.Equal(expected, TopicLogic.Liking((short)interest)); }

    [Theory]
    [InlineData(100, 1)] [InlineData(450, 2)] [InlineData(800, 3)]
    public void OutcomeFollowsTheListenersInterest(int interest, int outcome) { Assert.Equal(outcome, TopicLogic.OutcomeFor((short)interest)); }

    [Fact]
    public void InterestsOnTheTenPointScaleAreScaledUp()
    {
        var n = TopicLogic.Normalize(new short[] { 9, 0, 10, 5, 2, 7, 1, 8 });
        Assert.Equal(new short[] { 900, 0, 1000, 500, 200, 700, 100, 800 }, n);
        Assert.Equal(2, TopicLogic.Liking(n[0]));   // 9 of 10 is liked
        Assert.Equal(0, TopicLogic.Liking(n[6]));   // 1 of 10 is not
        var big = new short[] { 900, 0, 10, 5, 2, 7, 1, 8 };
        Assert.Same(big, TopicLogic.Normalize(big));   // already 0..1000: untouched
    }

    [Fact]
    public void OffersThreeDistinctSubjects()
    {
        var speaker = new short[] { 900, 0, 0, 0, 0, 0, 0, 0 };
        var listener = new short[] { 0, 0, 0, 0, 0, 0, 0, 800 };
        var s = TopicLogic.Offer(speaker, listener, new Random(3));
        Assert.Equal(3, s.Count);
        Assert.Equal(3, s.Select(x => x.Interest).Distinct().Count());
        Assert.Contains(s, x => x.Interest == 0);   // the speaker's favourite
        Assert.Contains(s, x => x.Interest == 7);   // the listener's favourite
    }

    [Fact]
    public void SharedFavouriteStillGivesThreeSubjects()
    {
        var both = new short[] { 0, 0, 0, 0, 0, 900, 0, 0 };
        var s = TopicLogic.Offer(both, both, new Random(1));
        Assert.Equal(3, s.Count);
        Assert.Equal(3, s.Select(x => x.Interest).Distinct().Count());
    }

    [Fact]
    public void EveryInterestHasAName() { Assert.Equal(8, TopicLogic.Interests.Length); Assert.All(TopicLogic.Interests, n => Assert.False(string.IsNullOrWhiteSpace(n))); }
}
