using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Simitone.Conversations;
using Xunit;

public class GossipLinesTests
{
    static GossipLineBank RepoGossip()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Tools", "Conversations", "bank", "bank.json");
            if (File.Exists(candidate)) return DialogueBank.Load(candidate).Gossip;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("bank.json");
    }

    static readonly string[] Kinds = Enum.GetNames(typeof(FactKind)).Select(k => k.ToLowerInvariant()).ToArray();

    static ISet<string> Placeholders(string line)
    {
        return new HashSet<string>(Regex.Matches(line, @"\{(\w+)\}").Cast<Match>().Select(m => m.Groups[1].Value));
    }

    static HashSet<string> Set(params string[] s) { return new HashSet<string>(s); }

    [Fact]
    public void EveryPlaceholderIsOneTheRendererFills()
    {
        var bad = RepoGossip().AllLines().SelectMany(l => Placeholders(l)).Where(p => !GossipLineBank.PlaceholderNames.Contains(p)).Distinct().ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void EveryKindHasNewsLinesForEverySituation()
    {
        var g = RepoGossip();
        foreach (var kind in Kinds)
        {
            foreach (var certainty in new[] { "sure", "hedged" })
            {
                var list = g.Tell[kind][certainty];
                Assert.True(list.Count >= 6, kind + " " + certainty + " has " + list.Count);
                // a Sim who only remembers who it was about can still say something; so can one who remembers a second person but no day
                Assert.True(GossipLines.Usable(list, Set("name", "me", "who", "label")).Count >= 1, kind + " " + certainty + ": nothing to say with only the name");
                // a Sim who saw it happen must never say they only heard it
                Assert.True(GossipLines.Usable(list, Set("name", "me", "who", "label"), true).Count >= 1, kind + " " + certainty + ": nothing a witness can say with only the name");
                Assert.True(GossipLines.Usable(list, Set("name", "me", "who", "with", "label", "amount", "when"), true).Count >= 3, kind + " " + certainty + ": too few lines for a witness");
                if (FactRules.Carries((FactKind)Array.IndexOf(Kinds, kind), Slot.With))
                    Assert.True(GossipLines.Usable(list, Set("name", "me", "who", "with", "label")).Count >= 3, kind + " " + certainty + ": too few lines without the day");
                Assert.True(GossipLines.Usable(list, Set("name", "me", "who", "with", "label", "amount", "when")).Count >= 5);
            }
            Assert.True(g.Ask[kind].Count >= 5, kind + " asks");
            Assert.True(GossipLines.Usable(g.Ask[kind], Set("name", "me", "who")).Count >= 1, kind + ": no question that needs only a name");
        }
    }

    [Fact]
    public void ThereAreGreetingsGoodbyesLeadInsAndReactionsInEveryTone()
    {
        var g = RepoGossip();
        foreach (var t in new[] { "0", "1", "2" })
        {
            Assert.True(g.Greet[t].Count >= 6); Assert.True(g.Bye[t].Count >= 6); Assert.True(g.Lead[t].Count >= 6);
        }
        foreach (var r in new[] { "surprised", "knewit", "doubtful", "filledgap" }) Assert.True(g.Reaction[r].Count >= 6, r);
        Assert.True(g.NoIdea.Count >= 5 && g.Source.Count >= 5 && g.FirstHand.Count >= 3 && g.AboutMe.Count >= 5);
        Assert.All(g.Source, l => Assert.Contains("{src}", l));
    }

    [Fact]
    public void ALineNeverMentionsSomethingTheSimDoesNotSay()
    {
        var list = new List<string> { "{who} got promoted {when}.", "{who} got promoted to {label}.", "{who} got promoted." };
        Assert.Equal(new[] { "{who} got promoted." }, GossipLines.Usable(list, Set("name", "who")).ToArray());
        Assert.Equal(3, GossipLines.Usable(list, Set("name", "who", "when", "label")).Count);
    }

    [Fact]
    public void DaysAreSaidTheWayPeopleSayThem()
    {
        Assert.Equal("today", GossipLines.DayPhrase(5, 5));
        Assert.Equal("yesterday", GossipLines.DayPhrase(5, 4));
        Assert.Equal("three days ago", GossipLines.DayPhrase(5, 2));
        Assert.Equal("12 days ago", GossipLines.DayPhrase(20, 8));
        Assert.Equal("today", GossipLines.DayPhrase(5, 9));
    }

    [Fact]
    public void ToneComesFromThePersonality()
    {
        Assert.Equal(Tone.Bold, GossipLines.ToneOf(new RawSim { Nice = 200 }));
        Assert.Equal(Tone.Playful, GossipLines.ToneOf(new RawSim { Nice = 600, Playful = 800 }));
        Assert.Equal(Tone.Gentle, GossipLines.ToneOf(new RawSim { Nice = 700, Playful = 300, Outgoing = 300 }));
    }

    // every balloon of 2,000 frozen conversations is a complete, short sentence with nothing left unfilled
    [Fact]
    public void EveryRenderedLineIsCompleteAndShort()
    {
        var bank = RepoGossip();
        var names = new[] { "Bella", "Goran", "Dina", "Mara", "Sam", "Joe", "Kim", "Lee", "Pat" };
        int lines = 0, tells = 0, maxLen = 0;
        var seen = new HashSet<string>();
        for (ulong world = 1; world <= 200; world++)
        {
            var b = new GossipBook(); var sims = Enumerable.Range(1, 9).ToList();
            foreach (var id in sims) b.SetName(id, names[id - 1]);
            var rng = new GossipRng(world);
            var kinds = (FactKind[])Enum.GetValues(typeof(FactKind));
            for (int i = 0; i < 8; i++)
            {
                var kind = kinds[rng.Below(kinds.Length)];
                int who = rng.Range(1, 9), with = rng.Range(1, 9); if (with == who) with = who % 9 + 1;
                var label = kind == FactKind.Promoted ? "Recruit" : kind == FactKind.SkillGain ? "cooking" : "";
                var f = b.AddFact(kind, label, who, with, rng.Range(1, 10), rng.Range(1, 6));
                b.Witness(who, f.Id, 1);
                for (int w = 0; w < 3; w++) b.Witness(rng.Range(1, 9), f.Id, 1);
            }
            for (int i = 0; i < 6; i++) b.Forget(rng.Range(1, 9), world, i + 1, 400000, 100000);
            for (int c = 0; c < 10; c++)
            {
                int x = rng.Range(1, 9), y = rng.Range(1, 9); if (x == y) y = x % 9 + 1;
                var tr = new GossipTraits { TellPpm = 800000, CuriosityPpm = 400000, GuessPpm = 600000 };
                var t = GossipConversation.Run(b, x, y, tr, tr, sims, 7, 10 + c, world, 8, (Tone)rng.Below(3), (Tone)rng.Below(3));
                var lr = new GossipRng(world * 31 + (ulong)c);
                foreach (var turn in t.Turns)
                {
                    var ctx = new GossipContext { Book = b, Lines = bank, Today = 7, SpeakerName = names[turn.Speaker - 1], ListenerName = names[turn.Listener - 1], Tone = turn.Tone };
                    var line = GossipLines.Render(turn, ctx, ref lr);
                    lines++; if (turn.Act == GossipAct.Tell) tells++;
                    maxLen = Math.Max(maxLen, line.Length);
                    seen.Add(line);
                    Assert.False(line.Contains("{") || line.Contains("}"), "unfilled: " + line);
                    Assert.True(line.Length >= 3 && line.Length <= GossipLines.MaxLength, "length " + line.Length + ": " + line);
                    Assert.Matches(@"[.!?]$", line);
                    if (turn.Act == GossipAct.Tell)
                    {
                        // a Sim never narrates their own news in the third person
                        if (turn.Claim.Values[(int)Slot.Who] == turn.Speaker || turn.Claim.Values[(int)Slot.With] == turn.Speaker) Assert.DoesNotContain(names[turn.Speaker - 1], line);
                        if (turn.ToldBy == 0 && turn.ReactionToPrevious == Reaction.None) Assert.False(GossipLines.IsHearsay(line), "a witness says they heard it: " + line);
                        if (turn.ToldBy == turn.Listener) Assert.DoesNotContain("told me", line);
                    }
                    Assert.DoesNotContain("someone", line.ToLowerInvariant().Replace("somebody", "").Replace("with someone", "").Replace("someone else", ""));   // "someone" is what a name with no value would show
                }
                b.Apply(t);
            }
        }
        Assert.True(tells > 500, "tells " + tells);
        Assert.True(seen.Count > 800, "only " + seen.Count + " different lines in " + lines);
    }

    [Fact]
    public void RenderingIsReplayable()
    {
        var bank = RepoGossip();
        var b = new GossipBook(); var sims = new List<int> { 1, 2, 3 };
        b.SetName(1, "Bella"); b.SetName(2, "Goran"); b.SetName(3, "Dina");
        var f = b.AddFact(FactKind.Promoted, "Recruit", 1, 0, 0, 5); b.Witness(2, f.Id, 1);
        var tr = new GossipTraits { TellPpm = 1000000 };
        string Play()
        {
            var t = GossipConversation.Run(b, 2, 3, tr, tr, sims, 6, 40, 3, 8, Tone.Playful, Tone.Gentle);
            var lr = new GossipRng(5);
            return string.Join("|", t.Turns.Select(x => GossipLines.Render(x, new GossipContext { Book = b, Lines = bank, Today = 6, SpeakerName = b.NameOf(x.Speaker), ListenerName = b.NameOf(x.Listener), Tone = x.Tone }, ref lr)));
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void WithoutAGossipBankTheSimsStillSpeakInCompleteSentences()
    {
        var b = new GossipBook(); var sims = new List<int> { 1, 2, 3 };
        b.SetName(1, "Bella"); b.SetName(2, "Goran"); b.SetName(3, "Dina");
        var f = b.AddFact(FactKind.Romance, "", 1, 3, 0, 5); b.Witness(2, f.Id, 1);
        var tr = new GossipTraits { TellPpm = 1000000 };
        var t = GossipConversation.Run(b, 2, 3, tr, tr, sims, 6, 40, 3);
        var lr = new GossipRng(1);
        foreach (var turn in t.Turns)
        {
            var line = GossipLines.Render(turn, new GossipContext { Book = b, Lines = new GossipLineBank(), Today = 6, SpeakerName = "Goran", ListenerName = "Dina", Tone = Tone.Gentle }, ref lr);
            Assert.False(string.IsNullOrWhiteSpace(line));
            Assert.DoesNotContain("{", line);
        }
    }

    // not a check: writes a few finished conversations to the temp folder so the wording can be read as a whole
    [Fact]
    public void WriteSampleConversations()
    {
        var bank = RepoGossip();
        var names = new[] { "Bella", "Goran", "Dina", "Mara", "Sam", "Joe", "Kim", "Lee", "Pat" };
        var sb = new System.Text.StringBuilder();
        for (ulong world = 1; world <= 12; world++)
        {
            var b = new GossipBook(); var sims = Enumerable.Range(1, 9).ToList();
            foreach (var id in sims) b.SetName(id, names[id - 1]);
            var rng = new GossipRng(world * 7);
            var kinds = (FactKind[])Enum.GetValues(typeof(FactKind));
            for (int i = 0; i < 6; i++)
            {
                var kind = kinds[rng.Below(kinds.Length)];
                int who = rng.Range(1, 9), with = rng.Range(1, 9); if (with == who) with = who % 9 + 1;
                var f = b.AddFact(kind, kind == FactKind.Promoted ? "Recruit" : kind == FactKind.SkillGain ? "cooking" : "", who, with, rng.Range(1, 10), rng.Range(3, 7));
                b.Witness(who, f.Id, 1); b.Witness(rng.Range(1, 9), f.Id, 1);
            }
            for (int i = 0; i < 5; i++) b.Forget(rng.Range(1, 9), world, i + 1, 450000, 100000);
            for (int c = 0; c < 6; c++)
            {
                int x = rng.Range(1, 9), y = rng.Range(1, 9); if (x == y) y = x % 9 + 1;
                var tr = new GossipTraits { TellPpm = 900000, CuriosityPpm = 500000, GuessPpm = 600000 };
                var t = GossipConversation.Run(b, x, y, tr, tr, sims, 7, 10 + c, world, 8, (Tone)rng.Below(3), (Tone)rng.Below(3));
                if (!t.HasContent) { b.Apply(t); continue; }
                var lr = new GossipRng(world + (ulong)c);
                sb.AppendLine("--- " + names[x - 1] + " meets " + names[y - 1] + " (world " + world + ")");
                foreach (var turn in t.Turns)
                {
                    var line = GossipLines.Render(turn, new GossipContext { Book = b, Lines = bank, Today = 7, SpeakerName = names[turn.Speaker - 1], ListenerName = names[turn.Listener - 1], Tone = turn.Tone }, ref lr);
                    sb.AppendLine("  " + names[turn.Speaker - 1] + " [" + turn.Tone + " " + turn.Act + "]: " + line);
                }
                b.Apply(t);
            }
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "gossip-samples.txt"), sb.ToString());
    }
}

public class GossipViewTests
{
    static GossipBook Book()
    {
        var b = new GossipBook();
        foreach (var id in new[] { 1, 2, 3 }) b.SetName(id, new[] { "", "Bob", "Betty", "Carl" }[id]);
        return b;
    }

    [Fact]
    public void NewestNewsComesFirstAndSaysHowTheSimKnows()
    {
        var b = Book();
        var f1 = b.AddFact(FactKind.Promoted, "Recruit", 3, 0, 0, 5); b.Witness(1, f1.Id, 1);
        var f2 = b.AddFact(FactKind.SkillGain, "cooking", 2, 0, 6, 6);
        var heard = new Claim { FactId = f2.Id }; heard.Values[(int)Slot.Who] = 2; heard.Values[(int)Slot.Amount] = 6; heard.Values[(int)Slot.When] = 6;
        b.Hear(1, 3, heard, 2);
        var news = GossipView.NewsOf(b, 1, 6);
        Assert.Equal(2, news.Count);
        Assert.Equal("Betty reached level 6 in cooking today", news[0].Text);
        Assert.Equal("sure | heard it from Carl", news[0].Detail);
        Assert.Equal("Carl got promoted to Recruit yesterday", news[1].Text);
        Assert.Equal("saw it", news[1].Detail);
        Assert.False(news[1].Fuzzy);
    }

    [Fact]
    public void FuzzyAndWrongVersionsAreMarked()
    {
        var b = Book();
        var f = b.AddFact(FactKind.SkillGain, "cooking", 2, 0, 6, 5);
        var guess = new Claim { FactId = f.Id }; guess.Values[(int)Slot.Who] = 2; guess.Values[(int)Slot.Amount] = 9; guess.Guess[(int)Slot.Amount] = true; guess.Values[(int)Slot.When] = 5;
        b.Hear(1, 3, guess, 2);
        var n = GossipView.NewsOf(b, 1, 7).Single();
        Assert.Equal("not sure", n.Certainty);
        Assert.True(n.Fuzzy); Assert.True(n.Differs);
        Assert.Contains("maybe level 9", n.Text);
        b.Forget(1, 5, 9, 1000000, 0);   // forgets everything that is left
        var forgot = GossipView.NewsOf(b, 1, 7).Single();
        Assert.Equal("fuzzy", forgot.Certainty);
        Assert.DoesNotContain("level 9", forgot.Text);
        Assert.Contains("somebody", forgot.Text);
    }

    [Fact]
    public void WhoTheNewsWasPassedToIsListed()
    {
        var b = Book();
        var f = b.AddFact(FactKind.Gift, "", 3, 2, 0, 5); b.Witness(1, f.Id, 1); b.MarkTold(1, f.Id, 2);
        Assert.Equal("saw it | told Betty", GossipView.NewsOf(b, 1, 5).Single().Detail);
        Assert.Empty(GossipView.NewsOf(b, 99, 5));
        Assert.Empty(GossipView.NewsOf(null, 1, 5));
    }
}
