using System.Collections.Generic;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class TalkSheetTests
{
    [Theory]
    [InlineData(false, 2, "New topic")]
    [InlineData(true, 2, "Betty likes this")]
    [InlineData(true, 1, "Betty is fine with it")]
    [InlineData(true, 0, "Betty may not care")]
    public void WhyChipSaysWhatTheSpeakerKnows(bool knows, int liking, string expected)
    {
        Assert.Equal(expected, TalkSheetPageData.Why("Betty", knows, liking));
    }

    [Fact]
    public void ToneAdviceFollowsWhatWorkedBefore()
    {
        var adj = new[] { -0.4, 0.5, 0.0 };   // gentle went badly, playful works
        Assert.Equal("playful", TalkSheetPageData.BestTone(adj));
        Assert.Equal("gentle", TalkSheetPageData.WorstTone(adj));
        Assert.Equal("Playful works well", TalkSheetPageData.Advice(adj, "playful"));
        Assert.Equal("Gentle went badly before", TalkSheetPageData.Advice(adj, "gentle"));
        Assert.Equal("", TalkSheetPageData.Advice(adj, "bold"));
        Assert.Contains("Betty responds well to a playful tone.", TalkSheetPageData.Hint("Betty", adj));
        Assert.Contains("Avoid a gentle tone", TalkSheetPageData.Hint("Betty", adj));
        Assert.Equal("You are still getting to know Betty.", TalkSheetPageData.Hint("Betty", new double[3]));
        Assert.Null(TalkSheetPageData.BestTone(new double[3]));
    }

    [Theory]
    [InlineData("1", 3, "pick-0")] [InlineData("3", 3, "pick-2")] [InlineData("4", 3, null)] [InlineData("0", 3, null)]
    [InlineData("Enter", 3, "say")] [InlineData("Escape", 3, "never")] [InlineData("x", 3, null)] [InlineData("2", 1, null)]
    public void ShortcutKeys(string key, int rows, string expected) { Assert.Equal(expected, TalkSheetPageData.ActionForKey(key, rows)); }

    [Fact]
    public void ReplyRoundTripsToneAndWords()
    {
        string tone, text;
        TalkSheetPageData.DecodeReply(TalkSheetPageData.EncodeReply("bold", "Let's go | now"), out tone, out text);
        Assert.Equal("bold", tone); Assert.Equal("Let's go   now", text);   // the separator cannot be typed into the words
        TalkSheetPageData.DecodeReply("", out tone, out text);
        Assert.Equal("gentle", tone); Assert.Equal("", text);
        TalkSheetPageData.DecodeReply("nonsense", out tone, out text);
        Assert.Equal("gentle", tone); Assert.Equal("", text);
        TalkSheetPageData.DecodeReply("weird|hello", out tone, out text);
        Assert.Equal("gentle", tone); Assert.Equal("hello", text);          // unknown tone falls back, the words stay
    }

    [Fact]
    public void BuildFillsTheTalkPage()
    {
        var rows = new List<TalkRow> { new TalkRow { Title = "Cooking", Why = "Betty likes this" }, new TalkRow { Title = "Chess", Why = "New topic" }, new TalkRow { Title = "Hikes" } };
        var d = TalkSheetPageData.Build("Bob", "Betty", "What should Bob talk about?", rows, 1, "playful", false, "", new[] { 0.0, 0.5, 0.0 }, true);
        Assert.Equal("Talk with Betty", d["title"]); Assert.Equal("B", d["letter"]);
        var list = (List<object>)d["rows"];
        Assert.Equal(3, list.Count);
        Assert.True((bool)((Dictionary<string, object>)list[1])["on"]);
        Assert.False((bool)((Dictionary<string, object>)list[2])["hasWhy"]);
        Assert.True((bool)d["showTone"]); Assert.True((bool)d["showText"]); Assert.True((bool)d["textEmpty"]);
        Assert.Equal("Playful works well", d["advice"]); Assert.True((bool)d["advicePositive"]);
        Assert.True((bool)d["canOpenMemory"]);
        Assert.Equal(1, ((List<object>)d["tones"]).Count(t => (bool)((Dictionary<string, object>)t)["on"]));
    }

    [Fact]
    public void TheThreeLinesVariantHasNoToneOrTextOrMemory()
    {
        var rows = new List<TalkRow> { new TalkRow { Title = "Hi", Why = "Gentle" }, new TalkRow { Title = "Yo", Why = "Playful" }, new TalkRow { Title = "Hey", Why = "Bold" } };
        var d = TalkSheetPageData.Build("Bob", "Betty", "Joke", rows, 0, "gentle", true, "", null, true);
        Assert.Equal("Joke", d["title"]); Assert.Equal("What do you say to Betty?", d["prompt"]);
        Assert.False((bool)d["showTone"]); Assert.False((bool)d["showText"]); Assert.False((bool)d["canOpenMemory"]); Assert.False((bool)d["hasHint"]);
    }

    [Theory]
    [InlineData(13, 0, 13)] [InlineData(13, 13, 0)] [InlineData(5, 9, 0)] [InlineData(0, 0, 0)] [InlineData(14, 13, 1)]
    public void BadgeCountsOnlyStoriesNotShownYet(int total, int shown, int expected) { Assert.Equal(expected, TabBadgeLogic.Unread(total, shown)); }

    [Fact]
    public void BadgeLabelCapsAtNinetyNinePlus() { Assert.Equal("7", TabBadgeLogic.Label(7)); Assert.Equal("99+", TabBadgeLogic.Label(100)); }
}