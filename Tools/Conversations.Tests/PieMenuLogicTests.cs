using System.Collections.Generic;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class PieMenuLogicTests
{
    static List<string> FullTs1Menu() => new List<string>
    {
        "Talk.../About Interests",
        "Talk.../Chat",
        "Talk.../Gossip",
        "Compliment.../Admire",
        "Joke",
        "Brag",
        "Entertain",
        "Tease",
        "Insult",
        "Slap",
        "Scare",
        "Tickle.../Ribs",
        "Tickle.../Extreme",
        "Cheer Up",
        "Apologize",
        "Give Gift...",
        "Ask.../How Are You?",
        "Hug.../Friendly",
        "Hug.../Romantic",
        "Hug.../Intimate",
        "Hug.../Leap Into Arms",
        "Flirt.../Sweet Talk",
        "Kiss.../Polite",
        "Kiss.../Passionate",
        "Back Rub",
        "Dance",
        "Propose.../Marriage",
    };

    [Fact]
    public void TalkCollapsesToOneBubbleAndRomanticActionsGoUnderRomance()
    {
        var result = PieMenuLogic.Filter(FullTs1Menu(), enabled: true, isAdultHumanTarget: true, isSelf: false);

        // one Talk bubble, mapped to Talk.../About Interests (index 0); the other talk variants are gone
        var talkItems = result.Where(r => r.DisplayName == "Talk").ToList();
        Assert.Single(talkItems);
        Assert.Equal(0, talkItems[0].OriginalIndex);
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Gossip") || r.DisplayName.StartsWith("Talk...") );

        var romanceNames = result.Where(r => r.DisplayName.StartsWith("Romance/")).Select(r => r.DisplayName).ToList();
        Assert.Contains("Romance/Hug - Romantic", romanceNames);
        Assert.Contains("Romance/Hug - Intimate", romanceNames);
        Assert.Contains("Romance/Hug - Leap Into Arms", romanceNames);
        Assert.Contains("Romance/Flirt - Sweet Talk", romanceNames);
        Assert.Contains("Romance/Kiss - Polite", romanceNames);
        Assert.Contains("Romance/Kiss - Passionate", romanceNames);
        Assert.Contains("Romance/Back Rub", romanceNames);
        Assert.Contains("Romance/Propose - Marriage", romanceNames);
        Assert.DoesNotContain("Romance/Hug - Friendly", romanceNames);
        Assert.DoesNotContain("Romance/Dance", romanceNames);
    }

    [Fact]
    public void FriendlyHugDanceAndTheOtherSocialActionsStayOnTheMenuUnchanged()
    {
        var raw = FullTs1Menu();
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);

        foreach (var keep in new[] { "Hug.../Friendly", "Dance", "Joke", "Brag", "Entertain", "Tease", "Insult", "Slap", "Scare",
                                     "Tickle.../Ribs", "Tickle.../Extreme", "Cheer Up", "Apologize", "Give Gift...", "Ask.../How Are You?", "Compliment.../Admire" })
        {
            var e = result.SingleOrDefault(r => r.DisplayName == keep);
            Assert.NotNull(e);                                          // same name, so the game draws it as before
            Assert.Equal(raw[e.OriginalIndex], keep);                   // and it still points at the same game interaction
        }
    }

    [Fact]
    public void EveryOriginalInteractionIsReachableExactlyOnceExceptTheDroppedTalkVariants()
    {
        var raw = FullTs1Menu();
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Equal(result.Count, result.Select(r => r.OriginalIndex).Distinct().Count());
        // only the Chat and Gossip variants of Talk (indices 1 and 2) are not reachable
        var missing = Enumerable.Range(0, raw.Count).Except(result.Select(r => r.OriginalIndex)).ToList();
        Assert.Equal(new[] { 1, 2 }, missing);
    }

    [Fact]
    public void AdultMenuWithOnlyTalkKeepsTalkAndTheRestOfTheMenu()
    {
        var raw = new List<string> { "Talk.../About Interests", "Joke", "Brag", "Compliment", "Hug.../Friendly" };
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Equal(new[] { "Talk", "Joke", "Brag", "Compliment", "Hug.../Friendly" }, result.Select(r => r.DisplayName).ToArray());
        Assert.Equal(0, result[0].OriginalIndex);
    }

    [Fact]
    public void DisabledOrNonAdultOrSelfLeavesMenuUntouched()
    {
        var raw = FullTs1Menu();
        Assert.Equal(raw.Count, PieMenuLogic.Filter(raw, enabled: false, isAdultHumanTarget: true, isSelf: false).Count);
        Assert.Equal(raw.Count, PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: false, isSelf: false).Count);
        Assert.Equal(raw.Count, PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: true).Count);
    }

    [Fact]
    public void UngreetedVisitorOrNpcWithNeitherTalkNorRomanceKeepsOriginalMenu()
    {
        var greetOnly = new List<string> { "Greet" };
        var result = PieMenuLogic.Filter(greetOnly, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Single(result);
        Assert.Equal("Greet", result[0].DisplayName);
        Assert.Equal(0, result[0].OriginalIndex);
    }

    [Fact]
    public void SlowDanceIsRomanceButPlainDanceIsNot()
    {
        var raw = new List<string> { "Talk.../Chat", "Dance", "Dance.../Slow" };
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Equal(new[] { "Talk", "Dance", "Romance/Dance - Slow" }, result.Select(r => r.DisplayName).ToArray());
    }

    [Fact]
    public void HandlesBackslashSeparatorsAndColorModSuffixes()
    {
        var raw = new List<string> { "Talk...\\Chat;0", "Flirt...\\Wink;1", "Joke;0" };
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Equal(3, result.Count);
        Assert.Equal("Talk", result[0].DisplayName);
        Assert.Equal("Joke;0", result[1].DisplayName);
        Assert.Equal("Romance/Flirt - Wink", result[2].DisplayName);
    }
}
