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
    public void AdultSocialMenuExposesOnlyTalkAndRomanceTopLevelBubbles()
    {
        var result = PieMenuLogic.Filter(FullTs1Menu(), enabled: true, isAdultHumanTarget: true, isSelf: false);

        var topLevel = result.Select(r => r.DisplayName.Split('/')[0]).Distinct().ToList();
        Assert.Equal(2, topLevel.Count);
        Assert.Contains("Talk", topLevel);
        Assert.Contains("Romance", topLevel);

        // Single direct Talk bubble mapped to Talk.../About Interests (index 0)
        var talkItems = result.Where(r => r.DisplayName == "Talk").ToList();
        Assert.Single(talkItems);
        Assert.Equal(0, talkItems[0].OriginalIndex);

        // Romance contains romantic actions and NEVER Friendly Hug, Joke, Brag, Insult, Tickle, etc.
        var romanceNames = result.Where(r => r.DisplayName.StartsWith("Romance/")).Select(r => r.DisplayName).ToList();
        Assert.Contains("Romance/Hug - Romantic", romanceNames);
        Assert.Contains("Romance/Hug - Intimate", romanceNames);
        Assert.Contains("Romance/Hug - Leap Into Arms", romanceNames);
        Assert.Contains("Romance/Flirt - Sweet Talk", romanceNames);
        Assert.Contains("Romance/Kiss - Polite", romanceNames);
        Assert.Contains("Romance/Kiss - Passionate", romanceNames);
        Assert.Contains("Romance/Back Rub", romanceNames);
        Assert.Contains("Romance/Dance", romanceNames);
        Assert.Contains("Romance/Propose - Marriage", romanceNames);

        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Friendly"));
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Joke"));
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Brag"));
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Insult"));
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Tickle"));
        Assert.DoesNotContain(result, r => r.DisplayName.Contains("Gossip"));
    }

    [Fact]
    public void LowRelationshipAdultMenuWithOnlyTalkShowsOnlyTalk()
    {
        var raw = new List<string> { "Talk.../About Interests", "Joke", "Brag", "Compliment", "Hug.../Friendly" };
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Single(result);
        Assert.Equal("Talk", result[0].DisplayName);
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
    public void HandlesBackslashSeparatorsAndColorModSuffixes()
    {
        var raw = new List<string> { "Talk...\\Chat;0", "Flirt...\\Wink;1", "Joke;0" };
        var result = PieMenuLogic.Filter(raw, enabled: true, isAdultHumanTarget: true, isSelf: false);
        Assert.Equal(2, result.Count);
        Assert.Equal("Talk", result[0].DisplayName);
        Assert.Equal("Romance/Flirt - Wink", result[1].DisplayName);
    }
}
