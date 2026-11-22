using Simitone.Render;
using Xunit;

public class LightGridTests
{
    [Theory]
    [InlineData(63, 4, 256)] [InlineData(64, 4, 256)] [InlineData(65, 4, 272)] [InlineData(1, 1, 16)]
    public void FloorsArePaddedToABlockMultiple(int tiles, int ppt, int expected)
    {
        Assert.Equal(expected, LightGrid.FloorPixels(tiles, ppt));
    }

    [Fact]
    public void AtlasIsLaidOutLikeTheLightMap()
    {
        int w, h;
        var px = LightGrid.Rasterize(1, 10, 4, (f, x, y) => 0, out w, out h);
        Assert.Equal(48 * 3, w);   // 10 tiles * 4 px = 40, padded to 48
        Assert.Equal(48 * 2, h);
        Assert.Equal(w * h, px.Length);
    }

    [Fact]
    public void ATopLeftWallIsALineAlongTheTilesLeftEdge()
    {
        int w, h;
        var px = LightGrid.Rasterize(1, 10, 4, (f, x, y) => x == 2 && y == 3 ? LightGrid.TopLeft : 0, out w, out h);
        for (int y = 12; y <= 16; y++) Assert.Equal(255, px[y * w + 8]);   // x = 2 tiles * 4, rows of tile 3 plus one pixel at the end
        Assert.Equal(0, px[12 * w + 9]);
        Assert.Equal(0, px[11 * w + 8]);
    }

    [Fact]
    public void ATopRightWallIsALineAlongTheTilesTopEdge()
    {
        int w, h;
        var px = LightGrid.Rasterize(1, 10, 4, (f, x, y) => x == 2 && y == 3 ? LightGrid.TopRight : 0, out w, out h);
        for (int x = 8; x <= 12; x++) Assert.Equal(255, px[12 * w + x]);
        Assert.Equal(0, px[13 * w + 9]);
    }

    [Fact]
    public void EachFloorGoesToItsOwnAtlasCell()
    {
        int w, h;
        int fp = LightGrid.FloorPixels(10, 4);
        var px = LightGrid.Rasterize(5, 10, 4, (f, x, y) => f == 4 && x == 0 && y == 0 ? LightGrid.TopLeft : 0, out w, out h);
        // floor 4 is the second cell of the second row
        Assert.Equal(255, px[fp * w + fp]);
        Assert.Equal(0, px[0]);
    }

    [Fact]
    public void DiagonalsAndOutOfRangeTilesStayInsideTheImage()
    {
        int w, h;
        var px = LightGrid.Rasterize(1, 3, 4, (f, x, y) => LightGrid.DiagH | LightGrid.DiagV | LightGrid.BottomRight | LightGrid.BottomLeft, out w, out h);
        Assert.Equal(w * h, px.Length);
        Assert.Equal(255, px[0]);
    }
}
