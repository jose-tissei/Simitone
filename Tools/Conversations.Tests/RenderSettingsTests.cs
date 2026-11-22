using System;
using System.IO;
using Simitone.Render;
using Xunit;

public class RenderSettingsTests
{
    [Theory]
    [InlineData("fxaa", AaMode.Fxaa)] [InlineData("FXAA", AaMode.Fxaa)] [InlineData(" 1 ", AaMode.Fxaa)] [InlineData("on", AaMode.Fxaa)]
    [InlineData("smaa", AaMode.Smaa)] [InlineData("SMAA", AaMode.Smaa)] [InlineData("2", AaMode.Smaa)]
    [InlineData("off", AaMode.Off)] [InlineData("", AaMode.Off)] [InlineData(null, AaMode.Off)] [InlineData("nonsense", AaMode.Off)]
    public void ParseAa(string text, AaMode expected) { Assert.Equal(expected, RenderSettings.ParseAa(text)); }

    [Fact]
    public void TheOptionsButtonCyclesThroughEveryMode()
    {
        var m = AaMode.Off;
        var seen = new System.Collections.Generic.List<AaMode>();
        for (int i = 0; i < 3; i++) { m = RenderSettings.Next(m); seen.Add(m); }
        Assert.Equal(new[] { AaMode.Fxaa, AaMode.Smaa, AaMode.Off }, seen.ToArray());
    }

    [Fact]
    public void NamesRoundTrip()
    {
        foreach (AaMode m in Enum.GetValues(typeof(AaMode))) Assert.Equal(m, RenderSettings.ParseAa(RenderSettings.AaName(m)));
    }

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

    [Theory]
    [InlineData("bounce", LightMode.Bounce)] [InlineData("BOUNCE", LightMode.Bounce)] [InlineData("on", LightMode.Bounce)]
    [InlineData("off", LightMode.Off)] [InlineData("", LightMode.Off)] [InlineData(null, LightMode.Off)] [InlineData("x", LightMode.Off)]
    public void ParseLight(string text, LightMode expected) { Assert.Equal(expected, RenderSettings.ParseLight(text)); }

    [Fact]
    public void LightModeIsSavedWithTheOtherChoices()
    {
        var path = Path.Combine(Path.GetTempPath(), "render-" + Guid.NewGuid().ToString("N"), "render.profile");
        try
        {
            new RenderSettings { Aa = AaMode.Smaa, Light = LightMode.Bounce, HiDef = false }.Save(path);
            var back = RenderSettings.Load(path);
            Assert.Equal(AaMode.Smaa, back.Aa);
            Assert.Equal(LightMode.Bounce, back.Light);
            Assert.False(back.HiDef);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path), true); } catch { } }
    }

    [Fact]
    public void DefaultsAreTheOriginalGame()
    {
        var s = new RenderSettings();
        Assert.Equal(AaMode.Off, s.Aa);
        Assert.Equal(LightMode.Off, s.Light);
    }
}
