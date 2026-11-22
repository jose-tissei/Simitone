using System;
using System.Collections.Generic;
using Simitone.Conversations;
using Xunit;

public class DialogueBankTests
{
    const string Json = @"{ ""version"":1,
      ""speaker"": { ""hug"": { ""0"": [""Come here, {name}.""], ""1"": [""Big hug incoming!"", ""Squeeze time, {name}!""], ""2"": [""I will not take no for an answer.""] } },
      ""reply"":   { ""hug"": { ""3"": { ""receptive"": [""Aww, thanks {me}.""], ""any"": [""That was lovely.""] }, ""1"": { ""any"": [""Too close, {me}.""] } } } }";

    [Fact]
    public void ReturnsTheLineForATone()
    {
        var b = DialogueBank.Parse(Json);
        Assert.True(b.Loaded);
        Assert.Equal("Come here, {name}.", b.SpeakerLine("hug", Tone.Gentle, new Random(1)));
        Assert.Equal("I will not take no for an answer.", b.SpeakerLine("hug", Tone.Bold, new Random(1)));
    }

    [Fact]
    public void RepliesUseTheTemperamentAndFallBackToAny()
    {
        var b = DialogueBank.Parse(Json);
        Assert.Equal("Aww, thanks {me}.", b.ReplyLine("hug", 3, Temperament.Receptive, new Random(1)));
        Assert.Equal("That was lovely.", b.ReplyLine("hug", 3, Temperament.Cold, new Random(1)));
        Assert.Equal("Too close, {me}.", b.ReplyLine("hug", 1, Temperament.Unknown, new Random(1)));
    }

    [Fact]
    public void MissingEntriesFallBackToBuiltInLines()
    {
        var b = DialogueBank.Parse(Json);
        Assert.False(string.IsNullOrWhiteSpace(b.SpeakerLine("kiss", Tone.Playful, new Random(1))));
        Assert.False(string.IsNullOrWhiteSpace(b.ReplyLine("hug", 2, Temperament.Cold, new Random(1))));
        foreach (Tone t in Enum.GetValues(typeof(Tone))) Assert.False(string.IsNullOrWhiteSpace(new DialogueBank().SpeakerLine("x", t, new Random(1))));
        for (int q = 0; q <= 4; q++) Assert.False(string.IsNullOrWhiteSpace(new DialogueBank().ReplyLine("x", q, Temperament.Unknown, new Random(1))));
    }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("{")] [InlineData("[]")] [InlineData("null")]
    public void DamagedFilesNeverThrow(string json)
    {
        var b = DialogueBank.Parse(json);
        Assert.False(string.IsNullOrWhiteSpace(b.SpeakerLine("hug", Tone.Gentle, new Random(1))));
    }

    [Fact]
    public void MissingFileIsEmptyBank()
    {
        var b = DialogueBank.Load(@"C:\definitely\not\here\bank.json");
        Assert.False(b.Loaded);
        Assert.False(string.IsNullOrWhiteSpace(b.SpeakerLine("hug", Tone.Gentle, new Random(1))));
    }

    [Fact]
    public void DoesNotRepeatTheSameLineTwiceInARow()
    {
        var b = DialogueBank.Parse(Json);
        var rng = new Random(7);
        string prev = null;
        for (int i = 0; i < 50; i++)
        {
            var l = b.SpeakerLine("hug", Tone.Playful, rng);
            Assert.NotEqual(prev, l);
            prev = l;
        }
    }

    [Fact]
    public void PlaceholdersAreFilled()
    {
        Assert.Equal("Hi Betty, I am Bob the Nurse", DialogueBank.Fill("Hi {name}, I am {me} the {job}", "Bob", "Betty", "Nurse"));
        Assert.Equal("Hello friend", DialogueBank.Fill("Hello {job}", "a", "b", ""));
        Assert.Equal("", DialogueBank.Fill(null, "a", "b", "c"));
    }

    [Theory]
    [InlineData("Hey {name}, nice to see you.", true)]
    [InlineData("", false)] [InlineData("  ", false)] [InlineData("ok", false)]
    [InlineData("This has a\nline break", false)]
    [InlineData("You are a bitch.", false)] [InlineData("Go shit yourself", false)]
    [InlineData("Hello {unknown}", false)] [InlineData("Hello {name", false)]
    [InlineData("Ol\u00e1 amigo, tudo bem?", true)] [InlineData("\u4f60\u597d\u4e16\u754c\u4f60\u597d", false)]
    public void LineFilter(string line, bool ok)
    {
        string reason;
        Assert.Equal(ok, DialogueBank.IsAcceptable(line, out reason));
        if (!ok) Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void LongLinesAreRejected()
    {
        string reason;
        Assert.False(DialogueBank.IsAcceptable(new string('a', DialogueBank.MaxLineLength + 1), out reason));
        Assert.True(DialogueBank.IsAcceptable(new string('a', DialogueBank.MaxLineLength), out reason));
    }
}
