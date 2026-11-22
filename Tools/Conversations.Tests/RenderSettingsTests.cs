using System;
using System.IO;
using Simitone.Render;
using Xunit;

public class RenderSettingsTests
{
    [Theory]
    [InlineData("fxaa", AaMode.Fxaa)] [InlineData("FXAA", AaMode.Fxaa)] [InlineData(" 1 ", AaMode.Fxaa)] [InlineData("on", AaMode.Fxaa)]
    [InlineData("off", AaMode.Off)] [InlineData("", AaMode.Off)] [InlineData(null, AaMode.Off)] [InlineData("nonsense", AaMode.Off)]
    public void ParseAa(string text, AaMode expected) { Assert.Equal(expected, RenderSettings.ParseAa(text)); }

    [Fact]
    public void ParsesTheProfileLines()
    {
        Assert.Equal(AaMode.Fxaa, RenderSettings.Parse(new[] { "# render", "aa = fxaa" }).Aa);
        Assert.Equal(AaMode.Off, RenderSettings.Parse(new[] { "bogus", "=x", "aa=off" }).Aa);
        Assert.Equal(AaMode.Off, RenderSettings.Parse(null).Aa);
    }

    [Fact]
    public void SaveAndLoadRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "render-" + Guid.NewGuid().ToString("N"), "render.profile");
        try
        {
            new RenderSettings { Aa = AaMode.Fxaa }.Save(path);
            Assert.Equal(AaMode.Fxaa, RenderSettings.Load(path).Aa);
            new RenderSettings { Aa = AaMode.Off }.Save(path);
            Assert.Equal(AaMode.Off, RenderSettings.Load(path).Aa);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
    }

    [Fact]
    public void MissingOrUnreadableProfile_MeansOff()
    {
        Assert.Equal(AaMode.Off, RenderSettings.Load(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))).Aa);
    }
}
