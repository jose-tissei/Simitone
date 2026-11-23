using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Simitone.Conversations;
using Xunit;

public class AiSubjectBankTests
{
    class FakeLaya : ILayaTransport
    {
        public string PreferKey;
        public int Calls;
        public string Post(string url, string json, string bearer, int timeoutMs)
        {
            Calls++;
            var body = JObject.Parse(json);
            var crit = (JObject)body["questions"]["q"]["criteria"];
            var probs = new JObject();
            var keys = crit.Properties().Select(p => p.Name).ToList();
            string win = keys.FirstOrDefault(k => PreferKey != null && k.Equals(PreferKey, StringComparison.OrdinalIgnoreCase)) ?? keys[0];
            foreach (var k in keys) probs[k] = k == win ? 0.70 : 0.30 / Math.Max(1, keys.Count - 1);
            return new JObject { ["answers"] = new JObject { ["q"] = new JObject { ["choice"] = win, ["probabilities"] = probs } } }.ToString();
        }
    }

    [Fact]
    public void BuiltinCatalogCoversAllEightInterestsAndAllSixSkillsAndNeverUsesRawTs1Labels()
    {
        var bank = AiSubjectBank.Builtin();
        Assert.True(bank.Count >= 40, "Expected rich AI subject catalog, got " + bank.Count);
        for (int i = 0; i < 8; i++)
            Assert.True(bank.All.Count(s => s.Interest == i) >= 4, "Interest " + i + " should have at least 4 AI subjects");
        foreach (var sk in new[] { SkillKind.Cooking, SkillKind.Charisma, SkillKind.Mechanical, SkillKind.Creativity, SkillKind.Body, SkillKind.Logic })
            Assert.Contains(bank.All, s => s.Skill == sk);
        var rawTs1 = new HashSet<string>(TopicLogic.Interests, StringComparer.OrdinalIgnoreCase);
        Assert.All(bank.All, s =>
        {
            string reason;
            Assert.True(DialogueBank.IsAcceptable(s.Title, out reason), s.Title + ": " + reason);
            Assert.DoesNotContain(s.Title, rawTs1);
            Assert.False(string.IsNullOrWhiteSpace(s.Id));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
        });
    }

    [Fact]
    public void RejectsRawTs1InterestNamesAndInvalidSubjectsFromJson()
    {
        var custom = new List<AiSubject>
        {
            new AiSubject { Id = "bad1", Title = "Travel and Toys", Interest = 0, Description = "raw ts1" },
            new AiSubject { Id = "bad2", Title = "Sports", Interest = 5, Description = "raw ts1" },
            new AiSubject { Id = "bad3", Title = "Out of Range", Interest = 99, Description = "invalid" },
            new AiSubject { Id = "good1", Title = "Midnight Street Food", Interest = 0, Skill = SkillKind.Cooking, Description = "Late-night dishes and secret street stalls" },
        };
        var bank = AiSubjectBank.FromList(custom, includeBuiltinFallback: false);
        Assert.Equal(1, bank.Count);
        Assert.Equal("good1", bank.All[0].Id);
    }

    [Fact]
    public void OfferForManualTalkReturnsThreeDistinctAiSubjects()
    {
        var bank = AiSubjectBank.Builtin();
        var speaker = new RawSim { Name = "Bob", Cooking = 800, Interests = new short[] { 900, 100, 100, 100, 100, 100, 100, 100 } };
        var listener = new RawSim { Name = "Betty", Logic = 800, Interests = new short[] { 100, 100, 100, 100, 100, 100, 100, 900 } };

        var offered = bank.OfferForManualTalk(speaker, listener, null, new SocialMemory(), new Random(7));
        Assert.Equal(3, offered.Count);
        Assert.Equal(3, offered.Select(s => s.Id).Distinct().Count());
        var rawTs1 = new HashSet<string>(TopicLogic.Interests, StringComparer.OrdinalIgnoreCase);
        Assert.All(offered, s => Assert.DoesNotContain(s.Title, rawTs1));
    }

    [Fact]
    public void OfferForManualTalkUsesLayaRankingWhenAvailable()
    {
        var bank = AiSubjectBank.Builtin();
        var speaker = new RawSim { Name = "Bob", Interests = new short[] { 500, 500, 500, 500, 500, 500, 500, 500 } };
        var listener = new RawSim { Name = "Betty", Interests = new short[] { 500, 500, 500, 500, 500, 500, 500, 500 } };
        var candidates = bank.SelectCandidates(speaker, listener, 0.5, new SocialMemory(), 6, new Random(11));
        var target = candidates[candidates.Count - 1];

        var fake = new FakeLaya { PreferKey = target.Id };
        var laya = new LayaClient(fake);
        laya.Configure("http://127.0.0.1:9999", "k");

        var offered = bank.OfferForManualTalk(speaker, listener, laya, new SocialMemory(), new Random(11));
        Assert.True(fake.Calls >= 1);
        Assert.Contains(offered, s => s.Id == target.Id);
    }

    [Fact]
    public void SelectCandidatesAvoidsRecentlyFailedSubjects()
    {
        var bank = AiSubjectBank.Builtin();
        var speaker = new RawSim { NeighborId = 1, Name = "Bob", Interests = new short[] { 950, 500, 500, 500, 500, 500, 500, 500 } };
        var listener = new RawSim { NeighborId = 2, Name = "Betty", Interests = new short[] { 500, 500, 500, 500, 500, 500, 500, 500 } };
        var mem = new SocialMemory();
        var first = bank.All.First(s => s.Interest == 0);
        mem.Record(speaker, listener, "talk", Tone.Gentle, first.Id, 1, 0);
        mem.Record(speaker, listener, "talk", Tone.Gentle, first.Id, 1, 0);

        var picks = bank.SelectCandidates(speaker, listener, 0.8, mem, 3, new Random(3));
        Assert.DoesNotContain(picks, s => s.Id == first.Id);
    }

    [Fact]
    public void RepoBankJsonContainsValidAiSubjects()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string bankPath = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Tools", "Conversations", "bank", "bank.json");
            if (File.Exists(candidate)) { bankPath = candidate; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(bankPath);
        var db = DialogueBank.Load(bankPath);
        Assert.True(db.Loaded);
        var subjects = AiSubjectBank.FromBank(db);
        Assert.True(subjects.LoadedFromBank, "bank.json should contain AI subjects");
        Assert.True(subjects.Count >= 40);
    }
}
