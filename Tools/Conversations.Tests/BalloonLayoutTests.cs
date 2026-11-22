using System.Linq;
using Simitone.Conversations;
using Xunit;

public class BalloonLayoutTests
{
    static float Len(string s) { return s.Length * 10; }   // every character 10 px wide

    [Fact]
    public void ShortTextStaysOnOneLine()
    {
        var l = BalloonLayout.Wrap("Hi Betty", 200, Len);
        Assert.Equal(new[] { "Hi Betty" }, l.ToArray());
    }

    [Fact]
    public void LongTextWrapsAtWordBoundaries()
    {
        var l = BalloonLayout.Wrap("I really like how you laugh at everything", 150, Len);
        Assert.True(l.Count >= 3);
        Assert.All(l, line => Assert.True(Len(line) <= 150));
        Assert.Equal("I really like how you laugh at everything", string.Join(" ", l));
    }

    [Fact]
    public void AnOverLongWordGetsItsOwnLine()
    {
        var l = BalloonLayout.Wrap("ok supercalifragilistic ok", 100, Len);
        Assert.Equal(new[] { "ok", "supercalifragilistic", "ok" }, l.ToArray());
    }

    [Fact]
    public void EmptyTextGivesNoLines() { Assert.Empty(BalloonLayout.Wrap("", 100, Len)); Assert.Empty(BalloonLayout.Wrap(null, 100, Len)); }

    [Fact]
    public void BalloonSitsCentredAboveTheAnchor()
    {
        var b = BalloonLayout.PlaceAbove(500, 400, 200, 60, 1920, 1080);
        Assert.Equal(400, b.X);
        Assert.True(b.Y + b.H < 400);
    }

    [Fact]
    public void BalloonIsKeptOnScreen()
    {
        var left = BalloonLayout.PlaceAbove(5, 300, 200, 60, 1920, 1080);
        Assert.True(left.X >= 0);
        var right = BalloonLayout.PlaceAbove(1915, 300, 200, 60, 1920, 1080);
        Assert.True(right.X + right.W <= 1920);
        var top = BalloonLayout.PlaceAbove(500, 10, 200, 60, 1920, 1080);
        Assert.True(top.Y >= 0);
    }

    [Fact]
    public void TwoClosePlacedBalloonsDoNotCoverEachOther()
    {
        var a = BalloonLayout.PlaceAbove(600, 500, 220, 70, 1920, 1080);
        var b = BalloonLayout.PlaceAbove(680, 500, 220, 70, 1920, 1080);
        Assert.True(BalloonLayout.Overlaps(a, b));
        var moved = BalloonLayout.AvoidOverlap(a, b);
        Assert.False(BalloonLayout.Overlaps(a, moved));
    }

    [Fact]
    public void FarApartBalloonsAreLeftAlone()
    {
        var a = BalloonLayout.PlaceAbove(300, 500, 200, 60, 1920, 1080);
        var b = BalloonLayout.PlaceAbove(1200, 500, 200, 60, 1920, 1080);
        var same = BalloonLayout.AvoidOverlap(a, b);
        Assert.Equal(b.Y, same.Y);
    }
}
