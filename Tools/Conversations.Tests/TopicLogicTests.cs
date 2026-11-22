using System;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class TopicLogicTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(299, 0)] [InlineData(300, 1)] [InlineData(599, 1)] [InlineData(600, 2)] [InlineData(1000, 2)]
    public void Liking(int interest, int expected) { Assert.Equal(expected, TopicLogic.Liking((short)interest)); }

    [Theory]
    [InlineData(100, 1)] [InlineData(450, 2)] [InlineData(800, 3)]
    public void OutcomeFollowsTheListenersInterest(int interest, int outcome) { Assert.Equal(outcome, TopicLogic.OutcomeFor((short)interest)); }

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
