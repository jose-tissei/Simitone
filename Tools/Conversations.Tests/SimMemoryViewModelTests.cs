using System;
using System.IO;
using System.Linq;
using Simitone.Conversations;
using Xunit;

public class SimMemoryViewModelTests
{
    static RawSim MakeBob() => new RawSim
    {
        Name = "Bob",
        NeighborId = 48,
        Nice = 620,
        Outgoing = 410,
        Playful = 720,
        Active = 300,
        Neat = 280,
        Cooking = 400,
        Mechanical = 600,
        Career = "Culinary",
        JobTitle = "Dishwasher",
        StrToTarget = 28,
        LtrToTarget = 14,
        Interests = new short[] { 400, 200, 500, 350, 750, 820, 610, 900 },
    };

    static RawSim MakeBetty() => new RawSim
    {
        Name = "Betty",
        NeighborId = 47,
        Nice = 820,       // Very Nice (dist 320)
        Outgoing = 760,   // Outgoing (dist 260)
        Playful = 690,    // Playful (dist 190)
        Active = 550,
        Neat = 700,
        Cooking = 700,
        Creativity = 500,
        Career = "Medical",
        JobTitle = "Paramedic",
        StrToTarget = 35,
        LtrToTarget = 20,
        Interests = new short[] { 550, 120, 480, 300, 850, 620, 740, 910 },
    };

    [Fact]
    public void Record_ProgressivelyRevealsTraitsInterestsSkillsTemperamentCareerAndTopics()
    {
        var mem = new SocialMemory();
        var bob = MakeBob();
        var betty = MakeBetty();

        // Chat 1: Talk about Outdoors (Betty loves it: 910) with Creativity skill
        mem.Record(bob, betty, "talk", Tone.Playful, "ai-outdoors-01", quality: 3,
            listenerInterestIndex: 7, listenerSkill: SkillKind.Creativity,
            subjectTitle: "Sketching in the Park", observedTemperament: Temperament.Receptive);

        var snap1 = SimMemoryViewModel.Build(mem, bob.MemoryKey(), bob.Name);
        var card1 = Assert.Single(snap1.KnownSims);
        Assert.Equal("Betty", card1.ListenerName);
        Assert.Single(card1.Traits);
        Assert.Equal("Very Nice", card1.Traits[0]);
        Assert.Equal("Warm & Receptive", card1.Temperament);
        Assert.Equal("Medical (Paramedic)", card1.Career);
        Assert.Contains(card1.LikedInterests, x => x.StartsWith("Outdoors"));
        Assert.Contains(card1.Skills, x => x.Contains("Creativity") && x.Contains("Lv 5"));
        Assert.Contains("Talk", card1.WorkedInteractions);
        Assert.Contains("Playful", card1.WorkedTones);
        Assert.Single(card1.Topics);
        Assert.Equal("Sketching in the Park", card1.Topics[0].Title);
        Assert.Equal("Great", card1.Topics[0].OutcomeLabel);

        // Chat 2: Talk about Violence and Aliens (Betty dislikes it: 120) -> bad outcome, reveals 2nd trait & top skill (Cooking)
        mem.Record(bob, betty, "tease", Tone.Bold, "ai-aliens-01", quality: 1,
            listenerInterestIndex: 1, listenerSkill: SkillKind.None,
            subjectTitle: "Alien Abductions", observedTemperament: Temperament.Receptive);

        var card2 = Assert.Single(SimMemoryViewModel.Build(mem, bob.MemoryKey(), bob.Name).KnownSims);
        Assert.Equal(2, card2.Traits.Count);
        Assert.Contains("Outgoing", card2.Traits);
        Assert.Contains(card2.DislikedInterests, x => x.StartsWith("Sci-Fi & Action"));
        Assert.Contains(card2.Skills, x => x.Contains("Cooking") && x.Contains("Lv 7"));
        Assert.Contains("Tease", card2.AvoidInteractions);
        Assert.Contains("Bold", card2.AvoidTones);
        Assert.Equal(2, card2.Topics.Count);
        Assert.Equal("Alien Abductions", card2.Topics[0].Title);
        Assert.Equal("Awkward", card2.Topics[0].OutcomeLabel);
    }

    [Fact]
    public void ReciprocalDiscovery_KeepsEachSimsMemoryDistinct()
    {
        var mem = new SocialMemory();
        var bob = MakeBob();
        var betty = MakeBetty();

        mem.Record(bob, betty, "talk", Tone.Gentle, "ai-weather-01", quality: 3,
            listenerInterestIndex: 4, listenerSkill: SkillKind.None,
            subjectTitle: "Crisp Autumn Evenings", observedTemperament: Temperament.Receptive);
        mem.RecordReciprocal(betty, bob, "talk", quality: 3,
            topicInterestIndex: 4, speakerSkill: SkillKind.Mechanical,
            subjectId: "ai-weather-01", subjectTitle: "Crisp Autumn Evenings");

        var bobSnap = SimMemoryViewModel.Build(mem, bob.MemoryKey(), bob.Name);
        var bettySnap = SimMemoryViewModel.Build(mem, betty.MemoryKey(), betty.Name);

        var bobKnowsBetty = Assert.Single(bobSnap.KnownSims);
        var bettyKnowsBob = Assert.Single(bettySnap.KnownSims);

        Assert.Equal("Betty", bobKnowsBetty.ListenerName);
        Assert.Equal("Medical (Paramedic)", bobKnowsBetty.Career);

        Assert.Equal("Bob", bettyKnowsBob.ListenerName);
        Assert.Equal("Culinary (Dishwasher)", bettyKnowsBob.Career);
        Assert.Contains(bettyKnowsBob.Skills, x => x.Contains("Mechanical") && x.Contains("Lv 6"));
        Assert.Equal(2, mem.GetAllSpeakers().Count);
    }

    [Fact]
    public void Persistence_RoundTripsLearnedDossierAndSupportsLegacyJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "simmem-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var mem = new SocialMemory();
            var bob = MakeBob();
            var betty = MakeBetty();
            mem.Record(bob, betty, "talk", Tone.Playful, "ai-outdoors-01", quality: 3,
                listenerInterestIndex: 7, listenerSkill: SkillKind.Cooking,
                subjectTitle: "Backyard Grilling", observedTemperament: Temperament.ThickSkinned);
            mem.Save(tmp);

            var reloaded = SocialMemory.Load(tmp);
            var card = Assert.Single(SimMemoryViewModel.Build(reloaded, bob.MemoryKey(), bob.Name).KnownSims);
            Assert.Equal("Betty", card.ListenerName);
            Assert.Equal("Thick-Skinned", card.Temperament);
            Assert.Equal("Medical (Paramedic)", card.Career);
            Assert.Contains(card.LikedInterests, x => x.StartsWith("Outdoors"));
            Assert.Contains(card.Skills, x => x.Contains("Cooking") && x.Contains("Lv 7"));
            Assert.Equal("Backyard Grilling", card.Topics[0].Title);

            // Legacy JSON without new fields still loads cleanly
            File.WriteAllText(tmp, "{\"n:48->n:99\":{\"exchanges\":2,\"pos\":1,\"neg\":0,\"interests\":[5],\"skills\":[\"Logic\"]}}");
            var legacy = SocialMemory.Load(tmp);
            var legacyCard = Assert.Single(SimMemoryViewModel.Build(legacy, "n:48", "Bob").KnownSims);
            Assert.Equal("n:99", legacyCard.ListenerName);
            Assert.Contains("Sports", legacyCard.NeutralInterests);
            Assert.Contains("Logic", legacyCard.Skills);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void MultipleKnownSims_SortedByExchangesDescending()
    {
        var mem = new SocialMemory();
        var bob = MakeBob();
        var betty = MakeBetty();
        var mortimer = new RawSim { Name = "Mortimer", NeighborId = 10, Nice = 400, Outgoing = 300, Playful = 250 };

        mem.Record(bob, mortimer, "talk", Tone.Gentle, "t1", quality: 2, listenerInterestIndex: 2, subjectTitle: "Local Politics");
        mem.Record(bob, betty, "talk", Tone.Playful, "t2", quality: 3, listenerInterestIndex: 7, subjectTitle: "Morning Hike");
        mem.Record(bob, betty, "compliment", Tone.Gentle, null, quality: 3);

        var snap = SimMemoryViewModel.Build(mem, bob.MemoryKey(), bob.Name);
        Assert.Equal(2, snap.KnownSims.Count);
        Assert.Equal("Betty", snap.KnownSims[0].ListenerName);
        Assert.Equal("Mortimer", snap.KnownSims[1].ListenerName);
    }
}
