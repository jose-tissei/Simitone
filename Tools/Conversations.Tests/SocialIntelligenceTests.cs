using Simitone.Conversations;
using Xunit;

public class SocialIntelligenceTests
{
    static RawSim Sim(string name, short mood = 0, short charisma = 0, short ltr = 0, short str = 0, short cooking = 0, short logic = 0)
    {
        return new RawSim
        {
            Name = name, Mood = mood, Charisma = charisma, LtrToTarget = ltr, StrToTarget = str, Cooking = cooking, Logic = logic
        };
    }

    [Fact]
    public void KnowledgeGrowsWithRelationshipAndConversationHistory()
    {
        var mem = new SocialMemory();
        var stranger = Sim("Bob", ltr: 0, str: 0);
        var listener = Sim("Betty");
        double k0 = SocialIntelligence.ComputeKnowledge(stranger, listener, mem);

        for (int i = 0; i < 6; i++) mem.Record(stranger, listener, "talk", Tone.Gentle, "subj_" + i, 3);
        double kAfterChat = SocialIntelligence.ComputeKnowledge(stranger, listener, mem);

        var closeFriend = Sim("Bob", ltr: 85, str: 90);
        double kClose = SocialIntelligence.ComputeKnowledge(closeFriend, listener, mem);

        Assert.True(k0 < 0.35, "Stranger knowledge should be low, got " + k0);
        Assert.True(kAfterChat > k0 + 0.15, "History should increase knowledge");
        Assert.True(kClose > 0.75, "Close friend with history should have high knowledge, got " + kClose);
    }

    [Fact]
    public void AccuracyScalesWithKnowledgeCharismaAndSpeakerMood()
    {
        var mem = new SocialMemory();
        var listener = Sim("Betty");

        var lowAll = Sim("Bob", mood: -80, charisma: 0, ltr: 0, str: 0);
        var highCharisma = Sim("Bob", mood: -80, charisma: 900, ltr: 0, str: 0);
        var goodMood = Sim("Bob", mood: 80, charisma: 900, ltr: 0, str: 0);
        var closeAndSkilled = Sim("Bob", mood: 80, charisma: 900, ltr: 90, str: 90);

        double a1 = SocialIntelligence.Assess(lowAll, listener, Temperament.EasilyOffended, mem).Accuracy;
        double a2 = SocialIntelligence.Assess(highCharisma, listener, Temperament.EasilyOffended, mem).Accuracy;
        double a3 = SocialIntelligence.Assess(goodMood, listener, Temperament.EasilyOffended, mem).Accuracy;
        double a4 = SocialIntelligence.Assess(closeAndSkilled, listener, Temperament.EasilyOffended, mem).Accuracy;

        Assert.True(a1 < a2, "Higher Charisma must improve accuracy");
        Assert.True(a2 < a3, "Better speaker mood must improve accuracy");
        Assert.True(a3 < a4, "Higher knowledge must improve accuracy");
        Assert.InRange(a1, 0.20, 0.98);
        Assert.InRange(a4, 0.75, 0.98);
    }

    [Fact]
    public void PromptRedactsListenerDetailsForStrangerAndRevealsForCloseFriend()
    {
        var mem = new SocialMemory();
        var listener = Sim("Betty", mood: -70, cooking: 900, logic: 800);
        var stranger = Sim("Bob", mood: 20, charisma: 100, ltr: 0, str: 0);
        var close = Sim("Bob", mood: 50, charisma: 700, ltr: 85, str: 85);

        var strangerPrompt = SocialIntelligence.BuildInteractionPrompt(stranger, listener, Temperament.EasilyOffended, mem);
        var closePrompt = SocialIntelligence.BuildInteractionPrompt(close, listener, Temperament.EasilyOffended, mem);

        Assert.Contains("barely knows", strangerPrompt.ToLowerInvariant());
        Assert.DoesNotContain("easily offended", strangerPrompt.ToLowerInvariant());
        Assert.DoesNotContain("cooking", strangerPrompt.ToLowerInvariant());

        Assert.Contains("knows betty well", closePrompt.ToLowerInvariant());
        Assert.Contains("bad mood", closePrompt.ToLowerInvariant());
        Assert.Contains("cooking", closePrompt.ToLowerInvariant());
    }

    [Fact]
    public void SkilledSpeakerReadsBadListenerMoodAndPrefersSupportiveInteractions()
    {
        var mem = new SocialMemory();
        var speaker = Sim("Bob", mood: 50, charisma: 800, ltr: 60, str: 60);
        var grumpyListener = Sim("Betty", mood: -80);
        var ctx = SocialIntelligence.Assess(speaker, grumpyListener, Temperament.EasilyOffended, mem);

        Assert.True(ctx.PerceivesListenerBadMood);
        double cheerScore = SocialIntelligence.InteractionHeuristic("cheerup", speaker, grumpyListener, ctx, mem);
        double teaseScore = SocialIntelligence.InteractionHeuristic("tease", speaker, grumpyListener, ctx, mem);
        Assert.True(cheerScore > teaseScore + 0.30, "Cheer Up should strongly outscore Tease for a grumpy listener");
    }

    [Fact]
    public void SharedSkillsBoostSubjectResonance()
    {
        var mem = new SocialMemory();
        var chef = Sim("Bob", cooking: 900, ltr: 70, str: 70);
        var fellowChef = Sim("Betty", cooking: 800);
        var nonChef = Sim("Bella", cooking: 0);

        var cookingSubject = new AiSubject { Id = "recipes", Title = "Rainy-Day Comfort Food", Interest = 4, Skill = SkillKind.Cooking };
        double shared = SocialIntelligence.SubjectSkillBonus(cookingSubject, chef, fellowChef, 0.8, mem);
        double solo = SocialIntelligence.SubjectSkillBonus(cookingSubject, chef, nonChef, 0.8, mem);

        Assert.True(shared > solo + 0.15, "Shared Cooking skill should give stronger subject bonus");
    }
}
