using Simitone.Conversations;
using Xunit;

namespace Simitone.Conversations.Tests
{
    public class VirtualLotTransitionTests
    {
        [Fact]
        public void Enter_FadesFromBlackAndShowsThenDissolvesArrivalBanner()
        {
            var tr = new VirtualLotTransition();
            Assert.Equal(VirtualLotPhase.Idle, tr.Phase);
            Assert.Equal(0f, tr.BlackVeilAlpha);
            Assert.Equal(0f, tr.BannerAlpha);

            tr.BeginEnter("SIMCITY POLICE ACADEMY", "TRAINING GROUNDS", enterFadeSeconds: 0.40f, bannerHoldSeconds: 1.00f, bannerFadeSeconds: 0.40f);
            Assert.Equal(VirtualLotPhase.Entering, tr.Phase);
            Assert.Equal("SIMCITY POLICE ACADEMY", tr.LocationTitle);
            Assert.Equal("TRAINING GROUNDS", tr.LocationSubtitle);
            Assert.Equal(1f, tr.BlackVeilAlpha, 3);
            Assert.Equal(0f, tr.BannerAlpha, 3);

            // Halfway through fade-in: veil is fading down, banner is ramping up
            Assert.False(tr.Tick(0.20f));
            Assert.Equal(VirtualLotPhase.Entering, tr.Phase);
            Assert.InRange(tr.BlackVeilAlpha, 0.20f, 0.30f); // (1 - 0.5)^2 = 0.25
            Assert.True(tr.BannerAlpha > 0.5f);

            // Fade-in finishes -> Active, veil cleared, banner held at full opacity
            Assert.False(tr.Tick(0.25f));
            Assert.Equal(VirtualLotPhase.Active, tr.Phase);
            Assert.Equal(0f, tr.BlackVeilAlpha);
            Assert.Equal(1f, tr.BannerAlpha, 3);

            // Still holding near end of hold window (elapsed = 0.45 + 0.85 = 1.30 < 1.40)
            Assert.False(tr.Tick(0.85f));
            Assert.Equal(1f, tr.BannerAlpha, 3);

            // Halfway through banner dissolve (elapsed = 1.60 -> 0.20 / 0.40 = 0.5 remaining)
            Assert.False(tr.Tick(0.30f));
            Assert.InRange(tr.BannerAlpha, 0.45f, 0.55f);

            // Fully dissolved (elapsed = 2.00 > 1.80)
            Assert.False(tr.Tick(0.40f));
            Assert.Equal(0f, tr.BannerAlpha, 3);
            Assert.Equal(VirtualLotPhase.Active, tr.Phase);
        }

        [Fact]
        public void Exit_FadesToBlackPreservesRewardFlagAndReturnsTrueOnComplete()
        {
            var tr = new VirtualLotTransition();
            tr.BeginEnter("ACADEMY", "VIRTUAL LOT");
            tr.Tick(1.0f);
            Assert.Equal(VirtualLotPhase.Active, tr.Phase);

            tr.BeginExit(applyRewards: true, exitFadeSeconds: 0.30f);
            Assert.True(tr.IsExiting);
            Assert.True(tr.ExitApplyRewards);
            Assert.Equal(0f, tr.BannerAlpha);
            Assert.Equal(0f, tr.BlackVeilAlpha, 3);

            // Duplicate BeginExit while exiting must not overwrite ExitApplyRewards or reset elapsed
            Assert.False(tr.Tick(0.15f));
            tr.BeginExit(applyRewards: false, exitFadeSeconds: 0.50f);
            Assert.True(tr.ExitApplyRewards);
            Assert.InRange(tr.BlackVeilAlpha, 0.20f, 0.30f); // 0.5^2 = 0.25

            // Final tick completes fade and returns true once
            Assert.True(tr.Tick(0.16f));
            Assert.Equal(VirtualLotPhase.Idle, tr.Phase);
            Assert.False(tr.IsExiting);
            Assert.False(tr.Tick(0.10f));
        }

        [Fact]
        public void Reset_ClearsStateCleanly()
        {
            var tr = new VirtualLotTransition();
            tr.BeginEnter("ACADEMY", "SUB");
            tr.Tick(0.1f);
            tr.BeginExit(true);
            tr.Tick(0.1f);

            tr.Reset();
            Assert.Equal(VirtualLotPhase.Idle, tr.Phase);
            Assert.Equal(0f, tr.BlackVeilAlpha);
            Assert.Equal(0f, tr.BannerAlpha);
            Assert.False(tr.ExitApplyRewards);
            Assert.False(tr.IsExiting);
        }
    }
}
