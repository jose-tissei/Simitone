using System;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class OutcomeResolverTests
{
    static Temperament[] AllTemperaments { get { return (Temperament[])Enum.GetValues(typeof(Temperament)); } }
    static Tone[] AllTones { get { return (Tone[])Enum.GetValues(typeof(Tone)); } }

    [Theory]
    [InlineData("Hug - Person A", "hug")] [InlineData("Give Gift - Person A", "givegift")] [InlineData("Dance With - Person A", "dancewith")]
    [InlineData("Hug - Friendly - Person A", "hug")] [InlineData("Kiss - Passionate - Person A", "kiss")] [InlineData("Cheer Up - Person A", "cheerup")] [InlineData("Back Rub - Person A", "backrub")] [InlineData("Kiss", "kiss")]
    public void ScriptNamesMapToKeys(string label, string key) { Assert.Equal(key, OutcomeResolver.KeyFromScriptName(label)); }

    [Theory]
    [InlineData("Talk - Person A")] [InlineData("Say Goodbye - Person A")] [InlineData("")] [InlineData(null)]
    public void UnsupportedScriptsAreIgnored(string label) { Assert.Null(OutcomeResolver.KeyFromScriptName(label)); }

    [Fact]
    public void ThereAreEighteenInteractions() { Assert.Equal(18, OutcomeResolver.Interactions.Length); }

    [Fact]
    public void NoResultEverMovesMoreThanOneStepFromTheScript()
    {
        foreach (var key in OutcomeResolver.Interactions)
            for (int script = 1; script <= OutcomeResolver.MaxOutcome(key); script++)
                foreach (var tone in AllTones) foreach (var t in AllTemperaments)
                {
                    int r = OutcomeResolver.Resolve(script, key, tone, t);
                    Assert.InRange(Math.Abs(r - script), 0, 1);
                    Assert.InRange(r, 1, OutcomeResolver.MaxOutcome(key));
                }
    }

    [Fact]
    public void UnknownTemperamentChangesNothing()
    {
        foreach (var key in OutcomeResolver.Interactions) foreach (var tone in AllTones)
            for (int s = 1; s <= OutcomeResolver.MaxOutcome(key); s++) Assert.Equal(s, OutcomeResolver.Resolve(s, key, tone, Temperament.Unknown));
    }

    [Fact]
    public void GentleHelpsWithEasilyOffendedSimsAndBoldHurts()
    {
        Assert.Equal(3, OutcomeResolver.Resolve(2, "hug", Tone.Gentle, Temperament.EasilyOffended));
        Assert.Equal(2, OutcomeResolver.Resolve(2, "hug", Tone.Playful, Temperament.EasilyOffended));
        Assert.Equal(1, OutcomeResolver.Resolve(2, "hug", Tone.Bold, Temperament.EasilyOffended));
    }

    [Fact]
    public void PlayfulWorksOnThickSkinnedAndReceptiveSims()
    {
        Assert.Equal(3, OutcomeResolver.Resolve(2, "joke", Tone.Playful, Temperament.ThickSkinned));
        Assert.Equal(3, OutcomeResolver.Resolve(2, "joke", Tone.Playful, Temperament.Receptive));
        Assert.Equal(2, OutcomeResolver.Resolve(2, "joke", Tone.Gentle, Temperament.Receptive));
    }

    [Fact]
    public void ColdSimsPunishPlayfulAndBold()
    {
        Assert.Equal(1, OutcomeResolver.Resolve(2, "kiss", Tone.Playful, Temperament.Cold));
        Assert.Equal(1, OutcomeResolver.Resolve(2, "kiss", Tone.Bold, Temperament.Cold));
        Assert.Equal(2, OutcomeResolver.Resolve(2, "kiss", Tone.Gentle, Temperament.Cold));
    }

    [Fact]
    public void ResultsAreClampedToTheRange()
    {
        Assert.Equal(3, OutcomeResolver.Resolve(3, "hug", Tone.Gentle, Temperament.EasilyOffended));
        Assert.Equal(1, OutcomeResolver.Resolve(1, "hug", Tone.Bold, Temperament.EasilyOffended));
        Assert.Equal(2, OutcomeResolver.Resolve(2, "compliment", Tone.Playful, Temperament.Receptive));   // two-outcome interactions stop at 2
    }

    [Fact]
    public void InteractionsWhereLowIsBestAreFlipped()
    {
        // cheer up and back rub list their good result first, so a "better" shift lowers the number
        Assert.Equal(1, OutcomeResolver.Resolve(2, "cheerup", Tone.Playful, Temperament.Receptive));
        Assert.Equal(2, OutcomeResolver.Resolve(1, "cheerup", Tone.Bold, Temperament.Cold));
    }

    [Theory]
    [InlineData("insult")] [InlineData("scare")] [InlineData("slap")] [InlineData("tease")] [InlineData("propose")]
    public void HostileAndSpecialInteractionsKeepTheGamesOutcome(string key)
    {
        foreach (var tone in AllTones) foreach (var t in AllTemperaments) Assert.Equal(2, OutcomeResolver.Resolve(2, key, tone, t));
    }

    [Fact]
    public void ZeroMeansUnknownAndIsNotTouched()
    {
        Assert.Equal(0, OutcomeResolver.Resolve(0, "hug", Tone.Gentle, Temperament.EasilyOffended));
    }

    [Fact]
    public void UnsupportedKeyIsNotTouched()
    {
        Assert.Equal(2, OutcomeResolver.Resolve(2, "talk", Tone.Gentle, Temperament.EasilyOffended));
        Assert.Equal(2, OutcomeResolver.Resolve(2, null, Tone.Gentle, Temperament.EasilyOffended));
    }

    [Theory]
    [InlineData("hug", 1, 1)] [InlineData("hug", 2, 2)] [InlineData("hug", 3, 3)]
    [InlineData("cheerup", 1, 3)] [InlineData("cheerup", 3, 1)]
    [InlineData("compliment", 1, 1)] [InlineData("compliment", 2, 3)]
    [InlineData("backrub", 1, 3)] [InlineData("backrub", 2, 1)]
    public void ReplyQuality(string key, int outcome, int quality) { Assert.Equal(quality, OutcomeResolver.Quality(outcome, key)); }
}

public class ReplyQualityForHostileTests
{
    [Theory]
    [InlineData("insult")] [InlineData("slap")] [InlineData("tease")] [InlineData("scare")] [InlineData("propose")]
    public void HostileInteractionsAlwaysUseNeutralReplies(string key)
    {
        for (int o = 1; o <= 3; o++) Assert.Equal(2, OutcomeResolver.Quality(o, key));
    }
}
