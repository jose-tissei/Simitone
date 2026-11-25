using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Simitone.Conversations;
using Xunit;

// Checks the shipped bank.json (assembled from bank/lines/*.txt by bank/Build-Bank.ps1): every line is clean, every combination the game can ask for has lines.
public class RepoBankLinesTests
{
    static readonly string[] Temperaments = { "easily_offended", "thick_skinned", "cold", "receptive", "any" };

    static string BankPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Tools", "Conversations", "bank", "bank.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("bank.json");
    }

    static BankFile Raw() { return JsonConvert.DeserializeObject<BankFile>(File.ReadAllText(BankPath())); }

    static IEnumerable<Tuple<string, string>> AllLines(BankFile f)
    {
        foreach (var i in f.Speaker) foreach (var t in i.Value) foreach (var l in t.Value) yield return Tuple.Create("speaker " + i.Key + " " + t.Key, l);
        foreach (var i in f.Reply) foreach (var q in i.Value) foreach (var t in q.Value) foreach (var l in t.Value) yield return Tuple.Create("reply " + i.Key + " q" + q.Key + " " + t.Key, l);
    }

    [Fact]
    public void NoLineIsDroppedWhenTheBankLoads()
    {
        Assert.Equal(0, DialogueBank.Load(BankPath()).Dropped);
    }

    [Fact]
    public void EveryLineIsCleanAndAlreadyTidy()
    {
        var bad = new List<string>();
        foreach (var l in AllLines(Raw()))
        {
            string reason;
            if (l.Item2 != DialogueBank.Tidy(l.Item2) || !DialogueBank.IsCleanLine(l.Item2, out reason)) bad.Add(l.Item1 + ": " + l.Item2);
            else if (l.Item2.Length > 90) bad.Add(l.Item1 + " is long: " + l.Item2);
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void EveryInteractionAndToneHasEnoughSpeakerLines()
    {
        var f = Raw();
        var missing = new List<string>();
        foreach (var key in OutcomeResolver.Interactions.Concat(new[] { "talk" }))
            for (int tone = 0; tone < 3; tone++)
            {
                Dictionary<string, List<string>> byTone;
                List<string> lines;
                if (!f.Speaker.TryGetValue(key, out byTone) || !byTone.TryGetValue(tone.ToString(), out lines) || lines.Count < 6) missing.Add(key + " tone " + tone);
            }
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }

    [Fact]
    public void EveryReplyTheGameCanAskForHasLinesForEveryTemperament()
    {
        var f = Raw();
        var missing = new List<string>();
        foreach (var key in OutcomeResolver.Interactions.Concat(new[] { "talk" }))
        {
            var qualities = key == "talk"
                ? new HashSet<int> { 1, 2, 3 }
                : new HashSet<int>(Enumerable.Range(1, OutcomeResolver.MaxOutcome(key)).Select(o => OutcomeResolver.Quality(o, key)));
            foreach (var q in qualities)
                foreach (var t in Temperaments)
                {
                    Dictionary<string, Dictionary<string, List<string>>> byQuality;
                    Dictionary<string, List<string>> byTemper;
                    List<string> lines;
                    if (!f.Reply.TryGetValue(key, out byQuality) || !byQuality.TryGetValue(q.ToString(), out byTemper) || !byTemper.TryGetValue(t, out lines) || lines.Count < 5) missing.Add(key + " q" + q + " " + t);
                }
        }
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }

    [Fact]
    public void PlaceholdersAreUsedWhereTheyMakeSense()
    {
        var bad = new List<string>();
        foreach (var l in AllLines(Raw()))
        {
            bool isReply = l.Item1.StartsWith("reply");
            bool isTalk = l.Item1.Contains(" talk ");
            if (isReply && l.Item2.Contains("{name}")) bad.Add(l.Item1 + " uses {name} in a reply: " + l.Item2);
            if (!isReply && l.Item2.Contains("{me}")) bad.Add(l.Item1 + " uses {me} in what the speaker says: " + l.Item2);
            if (!isTalk && l.Item2.Contains("{topic}")) bad.Add(l.Item1 + " uses {topic} outside Talk: " + l.Item2);
        }
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    [Fact]
    public void NoBucketRepeatsALine()
    {
        var f = Raw();
        var dupes = new List<string>();
        foreach (var g in AllLines(f).GroupBy(l => l.Item1)) foreach (var d in g.GroupBy(l => l.Item2).Where(x => x.Count() > 1)) dupes.Add(g.Key + ": " + d.Key);
        Assert.True(dupes.Count == 0, string.Join("\n", dupes));
    }
}
