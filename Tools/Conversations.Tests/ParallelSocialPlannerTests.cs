using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Simitone.Conversations;
using Xunit;

public class ParallelSocialPlannerTests
{
    class ScriptedLaya : ILayaTransport
    {
        public string TemperamentChoice = "receptive";
        public string InteractionChoice = "talk";
        public string SubjectChoice;
        public int Calls;
        public List<string> Questions = new List<string>();

        public string Post(string url, string json, string bearer, int timeoutMs)
        {
            Calls++;
            var body = JObject.Parse(json);
            var q = (string)body["questions"]["q"]["instructions"];
            Questions.Add(q);
            var crit = (JObject)body["questions"]["q"]["criteria"];
            var keys = crit.Properties().Select(p => p.Name).ToList();

            string win = keys[0];
            if (q.Contains("jokes and teasing")) win = TemperamentChoice;
            else if (q.Contains("interaction") && keys.Contains(InteractionChoice)) win = InteractionChoice;
            else if (q.Contains("topic") && SubjectChoice != null && keys.Contains(SubjectChoice)) win = SubjectChoice;

            var probs = new JObject();
            foreach (var k in keys) probs[k] = k == win ? 0.72 : 0.28 / Math.Max(1, keys.Count - 1);
            return new JObject { ["answers"] = new JObject { ["q"] = new JObject { ["choice"] = win, ["probabilities"] = probs } } }.ToString();
        }
    }

    static RawSim Adult(string name, short id, short social = 10, short mood = 40, short charisma = 500, short ltr = 40, short str = 40)
    {
        return new RawSim
        {
            Name = name, NeighborId = id, Social = social, Mood = mood, Charisma = charisma,
            LtrToTarget = ltr, StrToTarget = str, TargetLtrToMe = ltr, TargetStrToMe = str,
            Nice = 600, Outgoing = 600, Playful = 500,
            Interests = new short[] { 700, 300, 500, 400, 600, 500, 800, 500 },
        };
    }

    [Theory]
    [InlineData(-100, true)] [InlineData(0, true)] [InlineData(20, true)] [InlineData(21, false)] [InlineData(60, false)] [InlineData(100, false)]
    public void TriggersWhenSocialIsSixtyPercentOrUnder(int social, bool expected)
    {
        Assert.Equal(expected, ParallelSocialPlanner.ShouldTriggerSocial((short)social));
    }

    [Fact]
    public void SameRoomRulesHandleIndoorsAndOutdoors()
    {
        Assert.True(ParallelSocialPlanner.IsSameRoom(3, false, 10, 10, 3, false, 25, 25));     // same indoor room, any distance
        Assert.False(ParallelSocialPlanner.IsSameRoom(3, false, 10, 10, 4, false, 11, 10));    // different indoor rooms
        Assert.True(ParallelSocialPlanner.IsSameRoom(0, true, 10, 10, 0, true, 15, 13));       // outdoors within 8 tiles
        Assert.False(ParallelSocialPlanner.IsSameRoom(0, true, 10, 10, 0, true, 30, 30));      // outdoors too far apart
        Assert.False(ParallelSocialPlanner.IsSameRoom(3, false, 10, 10, 0, true, 11, 10));     // indoor vs outdoor
    }

    [Fact]
    public void LayaSelectsInteractionAndAiSubjectAndProducesLinesAndDeltas()
    {
        var fake = new ScriptedLaya { TemperamentChoice = "receptive", InteractionChoice = "talk" };
        var laya = new LayaClient(fake);
        laya.Configure("http://127.0.0.1:9999", "k");
        var subjects = AiSubjectBank.Builtin();
        var bank = new DialogueBank();
        var mem = new SocialMemory();

        var plan = ParallelSocialPlanner.Plan(Adult("Bob", 1), Adult("Betty", 2), laya, bank, subjects, mem, new Random(5));
        Assert.NotNull(plan);
        Assert.Equal("talk", plan.Interaction);
        Assert.NotNull(plan.Subject);
        Assert.DoesNotContain(plan.Subject.Title, TopicLogic.Interests);
        Assert.False(string.IsNullOrWhiteSpace(plan.SpeakerLine));
        Assert.False(string.IsNullOrWhiteSpace(plan.ReplyLine));
        Assert.True(fake.Calls >= 2, "Expected Laya calls for temperament/interaction/topic");
        Assert.InRange(plan.Quality, 1, 3);
    }

    [Fact]
    public void WorksGracefullyWhenLayaIsOffline()
    {
        var laya = new LayaClient(new ScriptedLaya());   // unconfigured = offline
        var plan = ParallelSocialPlanner.Plan(Adult("Bob", 1), Adult("Betty", 2), laya, new DialogueBank(), AiSubjectBank.Builtin(), new SocialMemory(), new Random(9));
        Assert.NotNull(plan);
        Assert.False(string.IsNullOrWhiteSpace(plan.Interaction));
        Assert.False(string.IsNullOrWhiteSpace(plan.SpeakerLine));
        Assert.False(string.IsNullOrWhiteSpace(plan.ReplyLine));
    }

    [Fact]
    public void LearnsFromBadInteractionsAndEscapesNegativeLoop()
    {
        var fake = new ScriptedLaya { TemperamentChoice = "easily_offended", InteractionChoice = "joke" };
        var laya = new LayaClient(fake);
        laya.Configure("http://127.0.0.1:9999", "k");
        var subjects = AiSubjectBank.Builtin();
        var bank = new DialogueBank();
        var mem = new SocialMemory();
        var bob = Adult("Bob", 1, mood: 20, charisma: 600, ltr: 50, str: 50);
        var betty = Adult("Betty", 2, mood: -20, charisma: 400, ltr: 50, str: 50);

        // Record two consecutive bad outcomes (including a failed joke & brag)
        mem.Record(bob, betty, "joke", Tone.Bold, null, 1);
        mem.Record(bob, betty, "brag", Tone.Bold, null, 1);

        // Even if Laya leans toward joke, memory de-escalation forces safe/repair picks and Gentle tone
        var plan = ParallelSocialPlanner.Plan(bob, betty, laya, bank, subjects, mem, new Random(12));
        Assert.NotEqual("joke", plan.Interaction);
        Assert.NotEqual("tease", plan.Interaction);
        Assert.NotEqual("brag", plan.Interaction);
        Assert.NotEqual("flirt", plan.Interaction);
        Assert.Equal(Tone.Gentle, plan.Tone);
        Assert.Contains(plan.Interaction, new[] { "apologize", "talk", "cheerup", "compliment" });
    }

    [Fact]
    public void SkilledHappyFriendAchievesHigherOutcomeRateThanGrumpyUnskilledStranger()
    {
        var subjects = AiSubjectBank.Builtin();
        var bank = new DialogueBank();
        var listener = Adult("Betty", 2, mood: -40);
        listener.Interests = new short[] { 50, 50, 50, 50, 50, 50, 950, 50 };   // loves Music only
        listener.Creativity = 900;

        var skilledFriend = Adult("Bob", 1, mood: 85, charisma: 950, ltr: 90, str: 90);
        skilledFriend.Creativity = 800;
        var grumpyStranger = Adult("Dan", 3, mood: -85, charisma: 0, ltr: 0, str: 0);
        grumpyStranger.Nice = 150;
        grumpyStranger.Playful = 800;

        double skilledSum = 0, strangerSum = 0;
        for (int seed = 1; seed <= 30; seed++)
        {
            var m1 = new SocialMemory();
            var m2 = new SocialMemory();
            var p1 = ParallelSocialPlanner.Plan(skilledFriend, listener, null, bank, subjects, m1, new Random(seed));
            var p2 = ParallelSocialPlanner.Plan(grumpyStranger, listener, null, bank, subjects, m2, new Random(seed));
            skilledSum += p1.Quality;
            strangerSum += p2.Quality;
        }

        Assert.True(skilledSum / 30.0 > strangerSum / 30.0 + 0.25,
            "Skilled happy friend avg quality (" + (skilledSum / 30.0) + ") should clearly beat grumpy unskilled stranger (" + (strangerSum / 30.0) + ")");
    }
}
