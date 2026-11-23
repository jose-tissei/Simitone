using System;
using Simitone.Conversations;
using Xunit;

namespace Conversations.Tests
{
    public class CadetAcademyTests
    {
        [Fact]
        public void Registry_ResolvesAllTenLawEnforcementRanks_AndRejectsOtherCareers()
        {
            Assert.Equal(10, CareerDispatchRegistry.AllRanks.Count);
            for (int lvl = 0; lvl < 10; lvl++)
            {
                var desc = CareerDispatchRegistry.Resolve("Law Enforcement", "", lvl);
                Assert.NotNull(desc);
                Assert.Equal(lvl, desc.PromotionLevel);
                Assert.False(string.IsNullOrWhiteSpace(desc.RankTitle));
                Assert.False(string.IsNullOrWhiteSpace(desc.MiniGameTitle));
                Assert.False(string.IsNullOrWhiteSpace(desc.DispatchTeaser));
                Assert.False(string.IsNullOrWhiteSpace(desc.AssignmentButtonText));
            }

            Assert.Equal(CareerMiniGameKind.SecurityWatch, CareerDispatchRegistry.Resolve("Law Enforcement", "Security Guard", 0).Kind);
            Assert.True(CareerDispatchRegistry.Resolve("Law Enforcement", "Security Guard", 0).IsImplemented);

            Assert.Equal(CareerMiniGameKind.CadetAcademy, CareerDispatchRegistry.Resolve("Law Enforcement", "Cadet", 1).Kind);
            Assert.True(CareerDispatchRegistry.Resolve("Law Enforcement", "Cadet", 1).IsImplemented);

            Assert.Null(CareerDispatchRegistry.Resolve("Culinary", "Dishwasher", 0));
            Assert.Null(CareerDispatchRegistry.Resolve("", "", 0));
        }

        [Fact]
        public void DailyQuota_PreventsDoubleFarmingAcrossToastAndCareerTab()
        {
            var quota = new CareerDispatchQuota();

            Assert.False(quota.HasUsedToday("n:7", 5));
            Assert.True(quota.TryConsume("n:7", 5));
            Assert.True(quota.HasUsedToday("n:7", 5));
            // Second attempt same day (e.g. Career Tab after Radio Dispatch) is blocked
            Assert.False(quota.TryConsume("n:7", 5));

            // Different household Sim on same day still has their own daily quota
            Assert.True(quota.TryConsume("n:8", 5));

            // Next game day resets cleanly
            Assert.False(quota.HasUsedToday("n:7", 6));
            Assert.True(quota.TryConsume("n:7", 6));
        }

        [Fact]
        public void CadetSweetSpot_WidensProportionallyWithBodySkill()
        {
            float w0 = CadetAcademyGame.ComputePerfectZoneWidth(0);
            float w5 = CadetAcademyGame.ComputePerfectZoneWidth(5);
            float w10 = CadetAcademyGame.ComputePerfectZoneWidth(10);

            Assert.True(w0 > 0.08f && w0 < w5);
            Assert.True(w5 < w10);
            Assert.True(w10 <= 0.35f);

            var gLow = new CadetAcademyGame("Bob", bodySkillLevel: 0, seed: 42);
            var gHigh = new CadetAcademyGame("Betty", bodySkillLevel: 10, seed: 42);
            Assert.True(gHigh.ActiveStage.PerfectHalfWidth > gLow.ActiveStage.PerfectHalfWidth * 1.8f);
        }

        [Fact]
        public void CadetBriefing_HoldsMarkerAndIgnoresLock_UntilBeginDrill()
        {
            var g = new CadetAcademyGame("Bob", bodySkillLevel: 3, seed: 21);
            Assert.True(g.IsBriefing);
            Assert.False(g.IsFinished);
            float initialMarker = g.MarkerPosition;

            // Ticking during briefing advances ElapsedSeconds (live 3D camera/warm-up)
            // without moving the timing needle or accepting accidental LockIn calls.
            g.Tick(0.5f);
            Assert.True(g.ElapsedSeconds >= 0.49f);
            Assert.Equal(initialMarker, g.MarkerPosition);
            Assert.Equal(CadetHitRating.None, g.LockIn());
            Assert.False(g.IsResolvingStage);

            // BeginDrill transitions cleanly into Stage 1 (Sprint)
            Assert.True(g.BeginDrill());
            Assert.False(g.IsBriefing);
            Assert.False(g.BeginDrill()); // idempotent
            g.Tick(0.25f);
            Assert.NotEqual(initialMarker, g.MarkerPosition);
        }

        [Fact]
        public void CadetDrill_ThreeStageFlow_ScoresPerfectGoodMiss_AndAwardsBodyXp()
        {
            var g = new CadetAcademyGame("Bob", bodySkillLevel: 4, seed: 12);
            Assert.True(g.IsBriefing);
            Assert.True(g.BeginDrill());
            Assert.Equal(CadetDrillStageKind.Sprint, g.ActiveStage.Kind);
            Assert.False(g.IsFinished);

            // Stage 1 (Sprint): land right inside green sweet spot -> Perfect (+100)
            MoveMarkerTo(g, g.ActiveStage.ZoneCenter);
            Assert.Equal(CadetHitRating.Perfect, g.LockIn());
            Assert.True(g.IsResolvingStage);
            Assert.Equal(100, g.Stages[0].Points);

            // Advance past 3D action beat -> Stage 2 (Vault)
            g.Tick(CadetAcademyGame.ResolutionBeatSeconds + 0.05f);
            Assert.False(g.IsResolvingStage);
            Assert.Equal(CadetDrillStageKind.Vault, g.ActiveStage.Kind);

            // Stage 2 (Vault): land in amber Good zone (+65)
            float goodTarget = g.ActiveStage.ZoneCenter + g.ActiveStage.PerfectHalfWidth + 0.03f;
            MoveMarkerTo(g, goodTarget);
            Assert.Equal(CadetHitRating.Good, g.LockIn());
            Assert.Equal(65, g.Stages[1].Points);

            g.Tick(CadetAcademyGame.ResolutionBeatSeconds + 0.05f);
            Assert.Equal(CadetDrillStageKind.TargetDrill, g.ActiveStage.Kind);

            // Stage 3 (TargetDrill): land far outside -> Miss (+15)
            float missTarget = g.ActiveStage.ZoneCenter > 0.5f ? 0.02f : 0.98f;
            MoveMarkerTo(g, missTarget);
            Assert.Equal(CadetHitRating.Miss, g.LockIn());
            Assert.Equal(15, g.Stages[2].Points);

            g.Tick(CadetAcademyGame.ResolutionBeatSeconds + 0.05f);
            Assert.True(g.IsFinished);

            var o = g.ComputeOutcome();
            Assert.Equal(180, o.TotalPoints);
            Assert.Equal("B", o.Grade);
            Assert.Equal(1, o.PerfectCount);
            Assert.Equal(1, o.GoodCount);
            Assert.Equal(1, o.MissCount);
            Assert.True(o.Reward.BodyXpDelta > 0);
            Assert.True(o.Reward.JobPerformanceDelta > 0);
            Assert.True(o.Reward.BonusSimoleons > 0);
        }

        [Fact]
        public void CadetDrill_AllPerfect_ScoresSRankAndMaxBodyXp()
        {
            var g = new CadetAcademyGame("Betty", bodySkillLevel: 6, seed: 7);
            g.AutoCompleteRemainingPerfect();
            Assert.False(g.IsBriefing);
            Assert.True(g.IsFinished);

            var o = g.ComputeOutcome();
            Assert.Equal("S", o.Grade);
            Assert.Equal(300, o.TotalPoints);
            Assert.Equal(3, o.PerfectCount);
            Assert.Equal(75, o.Reward.BodyXpDelta);
            Assert.Equal(75, o.Reward.BonusSimoleons);
            Assert.Equal(24, o.Reward.JobPerformanceDelta);
        }

        static void MoveMarkerTo(CadetAcademyGame g, float target)
        {
            for (int i = 0; i < 600; i++)
            {
                if (Math.Abs(g.MarkerPosition - target) <= 0.015f)
                    return;
                g.Tick(0.01f);
            }
            Assert.True(Math.Abs(g.MarkerPosition - target) <= 0.025f,
                "Marker never reached target " + target + " (at " + g.MarkerPosition + ")");
        }
    }
}
