using System.Linq;
using Simitone.Conversations;
using Xunit;

public class GossipEventsTests
{
    [Fact]
    public void ANewSkillPointIsNews()
    {
        var before = new RawSim { Cooking = 290, Logic = 500 };
        var now = new RawSim { Cooking = 310, Logic = 540 };
        var e = EventDetector.Diff(before, now);
        Assert.Single(e);
        Assert.Equal(FactKind.SkillGain, e[0].Kind);
        Assert.Equal("cooking", e[0].Label);
        Assert.Equal(3, e[0].Amount);
    }

    [Fact]
    public void SkillGoingDownOrStayingIsNotNews()
    {
        Assert.Empty(EventDetector.Diff(new RawSim { Body = 500 }, new RawSim { Body = 450 }));
        Assert.Empty(EventDetector.Diff(new RawSim { Body = 500 }, new RawSim { Body = 599 }));
        Assert.Empty(EventDetector.Diff(new RawSim { Body = 0 }, new RawSim { Body = 90 }));
    }

    [Fact]
    public void APromotionOrANewJobIsNews()
    {
        var up = EventDetector.Diff(new RawSim { Career = "Military", JobTitle = "Recruit" }, new RawSim { Career = "Military", JobTitle = "Elite Forces" });
        Assert.Equal("Elite Forces", up.Single().Label);
        Assert.Equal(FactKind.Promoted, up.Single().Kind);
        Assert.Single(EventDetector.Diff(new RawSim(), new RawSim { Career = "Law", JobTitle = "Paralegal" }));
        Assert.Empty(EventDetector.Diff(new RawSim { Career = "Law", JobTitle = "Paralegal" }, new RawSim { Career = "Law", JobTitle = "Paralegal" }));
        Assert.Empty(EventDetector.Diff(new RawSim { Career = "Law", JobTitle = "Paralegal" }, new RawSim()));   // quitting is not a promotion
        Assert.Empty(EventDetector.Diff(null, new RawSim()));
    }

    [Theory]
    [InlineData("kiss", 3, FactKind.Romance)] [InlineData("kiss", 2, FactKind.Romance)] [InlineData("flirt", 3, FactKind.Romance)]
    [InlineData("insult", 2, FactKind.Argument)] [InlineData("slap", 1, FactKind.Argument)] [InlineData("tease", 1, FactKind.Argument)]
    [InlineData("givegift", 3, FactKind.Gift)] [InlineData("givegift", 2, FactKind.Gift)]
    public void SocialsThatGetTalkedAbout(string interaction, int quality, FactKind kind)
    {
        var e = EventDetector.FromInteraction(interaction, quality);
        Assert.Equal(kind, e.Single().Kind);
        Assert.True(e.Single().NeedsOther);
    }

    [Theory]
    [InlineData("kiss", 1)] [InlineData("flirt", 2)] [InlineData("givegift", 1)] [InlineData("hug", 3)] [InlineData("talk", 3)] [InlineData("joke", 1)] [InlineData("tease", 3)] [InlineData("", 3)]
    public void EverydayOnesAreNot(string interaction, int quality)
    {
        Assert.Empty(EventDetector.FromInteraction(interaction, quality));
    }

    [Fact]
    public void FriendshipHappensWhenTheRelationshipCrossesTheLine()
    {
        Assert.True(EventDetector.BecameFriends(60, 72));
        Assert.True(EventDetector.BecameFriends(69, 70));
        Assert.False(EventDetector.BecameFriends(70, 90));
        Assert.False(EventDetector.BecameFriends(10, 40));
    }
}
