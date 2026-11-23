using System;
using Simitone.Conversations;
using Xunit;

namespace Conversations.Tests
{
    public class SecurityWatchTests
    {
        [Theory]
        [InlineData("Law Enforcement", "Security Guard", 0, true)]
        [InlineData("Law Enforcement", "", 0, true)]
        [InlineData("Police", "Security Guard", 0, true)]
        [InlineData("Law Enforcement", "Cadet", 1, false)]
        [InlineData("Law Enforcement", "Detective", 5, false)]
        [InlineData("Culinary", "Dishwasher", 0, false)]
        [InlineData("", "", 0, false)]
        public void IsSecurityGuard_MatchesLevel1LawEnforcement(string career, string title, int level, bool expected)
        {
            Assert.Equal(expected, CareerDispatchRules.IsSecurityGuard(career, title, level));
        }

        [Fact]
        public void ShouldTriggerDispatch_HandlesNightShiftAndOncePerDay()
        {
            // Midnight shift (00:00 - 06:00)
            Assert.True(CareerDispatchRules.ShouldTriggerDispatch(true, true, currentDay: 3, lastDispatchedDay: 2, currentHour: 1, shiftStartHour: 0, shiftEndHour: 6));
            Assert.False(CareerDispatchRules.ShouldTriggerDispatch(true, true, currentDay: 3, lastDispatchedDay: 3, currentHour: 2, shiftStartHour: 0, shiftEndHour: 6));
            Assert.False(CareerDispatchRules.ShouldTriggerDispatch(true, false, currentDay: 3, lastDispatchedDay: 2, currentHour: 2, shiftStartHour: 0, shiftEndHour: 6));
            Assert.False(CareerDispatchRules.ShouldTriggerDispatch(true, true, currentDay: 3, lastDispatchedDay: 2, currentHour: 10, shiftStartHour: 0, shiftEndHour: 6));
            // Wrap-around shift (22:00 - 04:00)
            Assert.True(CareerDispatchRules.IsWithinShiftHours(23, 22, 4));
            Assert.True(CareerDispatchRules.IsWithinShiftHours(2, 22, 4));
            Assert.False(CareerDispatchRules.IsWithinShiftHours(5, 22, 4));
        }

        [Fact]
        public void PerfectShift_ScoresSRankAndFullRewards()
        {
            var g = new SecurityWatchGame("Bob", initialEnergyMotive: 80, totalRounds: 5, seed: 42);
            Assert.False(g.IsFinished);
            Assert.NotNull(g.ActiveEvent);
            Assert.True(g.ActiveEvent.IsThreat); // Round 1 always starts with an exciting real threat

            g.AutoCompleteRemainingPerfect();

            Assert.True(g.IsFinished);
            Assert.Equal(5, g.CorrectCount);
            Assert.Equal(0, g.MistakeCount);
            Assert.True(g.ThreatsCaught >= 2);
            Assert.True(g.FalseAlarmsCleared >= 2);

            var outcome = g.ComputeOutcome();
            Assert.Equal("S", outcome.Grade);
            Assert.Equal(90, outcome.BonusSimoleons);
            Assert.Equal(25, outcome.JobPerformanceDelta);
        }

        [Fact]
        public void WrongActions_RecordMistakesAndLowerGrade()
        {
            var g = new SecurityWatchGame("Bob", initialEnergyMotive: 60, totalRounds: 5, seed: 7);

            // Round 1 is a threat -> marking All Clear counts as a missed threat
            var r1 = g.MarkAllClear();
            Assert.Equal(SecurityRoundResult.MissedThreat, r1);
            Assert.Equal(1, g.MissedThreats);

            // Wrong camera alarm counts as WrongCamera mistake
            var wrongCam = (g.ActiveEvent.CameraIndex + 1) % SecurityWatchGame.CameraCount;
            g.SelectCamera(wrongCam);
            var r2 = g.SoundAlarm();
            Assert.Equal(SecurityRoundResult.WrongCamera, r2);
            Assert.Equal(1, g.WrongCameraMistakes);

            // Play remaining rounds intentionally backwards
            while (!g.IsFinished && g.ActiveEvent != null)
            {
                g.SelectCamera(g.ActiveEvent.CameraIndex);
                if (g.ActiveEvent.IsThreat) g.MarkAllClear();
                else g.SoundAlarm();
            }

            var outcome = g.ComputeOutcome();
            Assert.Equal("C", outcome.Grade);
            Assert.Equal(15, outcome.BonusSimoleons);
            Assert.True(outcome.JobPerformanceDelta < 0);
        }

        [Fact]
        public void AlertnessDrains_SipCoffeeRestoresAndDozeOffPenalizes()
        {
            var g = new SecurityWatchGame("Betty", initialEnergyMotive: -40, totalRounds: 5, seed: 11);
            float startAlert = g.Alertness;
            g.Tick(4f);
            Assert.True(g.Alertness < startAlert);

            float lowAlert = g.Alertness;
            Assert.True(g.SipCoffee());
            Assert.Equal(2, g.CoffeeSipsLeft);
            Assert.True(g.Alertness > lowAlert);

            // Exhaust remaining coffee sips
            Assert.True(g.SipCoffee());
            Assert.True(g.SipCoffee());
            Assert.False(g.SipCoffee());

            // Drain alertness all the way to 0 -> DozedOff
            for (int i = 0; i < 60 && g.DozedOffMistakes == 0 && !g.IsFinished; i++)
                g.Tick(0.5f);

            Assert.True(g.DozedOffMistakes >= 1);
        }

        [Fact]
        public void Timeout_OnFalseAlarm_CountsAsCleared()
        {
            var g = new SecurityWatchGame("Bob", initialEnergyMotive: 100, totalRounds: 5, seed: 99);
            // Advance until we hit a false alarm round
            while (!g.IsFinished && g.ActiveEvent != null && g.ActiveEvent.IsThreat)
            {
                g.SelectCamera(g.ActiveEvent.CameraIndex);
                g.SoundAlarm();
            }
            Assert.NotNull(g.ActiveEvent);
            Assert.False(g.ActiveEvent.IsThreat);

            int beforeCleared = g.FalseAlarmsCleared;
            g.Tick(g.ActiveEvent.DurationSeconds + 0.5f);
            Assert.Equal(beforeCleared + 1, g.FalseAlarmsCleared);
        }

        [Fact]
        public void DefaultShift_IsShortThreeRounds_AndIncludesPizzaDeliveryFalseAlarm()
        {
            Assert.Equal(3, SecurityWatchGame.DefaultTotalRounds);
            Assert.False(SecurityWatchGame.IsThreatKind(SecurityEventKind.PizzaDelivery));
            Assert.Equal("Pizza Delivery", SecurityWatchGame.LabelOf(SecurityEventKind.PizzaDelivery));

            bool sawPizza = false;
            for (int seed = 1; seed <= 40 && !sawPizza; seed++)
            {
                var g = new SecurityWatchGame("Bob", 60, SecurityWatchGame.DefaultTotalRounds, seed);
                while (!g.IsFinished && g.ActiveEvent != null)
                {
                    if (g.ActiveEvent.Kind == SecurityEventKind.PizzaDelivery)
                    {
                        sawPizza = true;
                        Assert.False(g.ActiveEvent.IsThreat);
                        break;
                    }
                    g.SelectCamera(g.ActiveEvent.CameraIndex);
                    if (g.ActiveEvent.IsThreat) g.SoundAlarm();
                    else g.MarkAllClear();
                }
            }
            Assert.True(sawPizza, "Expected PizzaDelivery to appear as a false alarm across seeded shifts");
        }

        [Fact]
        public void ScanPause_HoldsQuietBeforeTriggeringMotionEvent()
        {
            var g = new SecurityWatchGame("Bob", 70, 3, seed: 42, scanPauseSeconds: 1.5f);
            Assert.True(g.IsScanning);
            Assert.Null(g.ActiveEvent);
            Assert.Equal(0, g.EventTriggerSequence);

            g.Tick(0.8f);
            Assert.True(g.IsScanning);
            Assert.Null(g.ActiveEvent);

            g.Tick(0.9f);
            Assert.False(g.IsScanning);
            Assert.NotNull(g.ActiveEvent);
            Assert.Equal(1, g.EventTriggerSequence);
        }

        [Fact]
        public void QuietNightShift_RewardsCalmSignOff_AndPenalizesFalseAlarm()
        {
            var calm = new SecurityWatchGame("Bob", 70, quietNight: true);
            Assert.True(calm.IsQuietShift);
            Assert.Null(calm.ActiveEvent);
            Assert.False(calm.IsFinished);

            var r = calm.MarkAllClear();
            Assert.Equal(SecurityRoundResult.QuietPatrolCleared, r);
            Assert.True(calm.IsFinished);
            var okOutcome = calm.ComputeOutcome();
            Assert.True(okOutcome.WasQuietShift);
            Assert.Equal("A", okOutcome.Grade);
            Assert.True(okOutcome.BonusSimoleons > 0);

            var panic = new SecurityWatchGame("Bob", 70, quietNight: true);
            var r2 = panic.SoundAlarm();
            Assert.Equal(SecurityRoundResult.FalseAlarmTriggered, r2);
            Assert.True(panic.IsFinished);
            Assert.Equal("B", panic.ComputeOutcome().Grade);

            var timed = new SecurityWatchGame("Bob", 70, quietNight: true);
            timed.Tick(SecurityWatchGame.DefaultQuietShiftSeconds + 0.5f);
            Assert.True(timed.IsFinished);
            Assert.Equal("A", timed.ComputeOutcome().Grade);
        }

        [Fact]
        public void DailyDispatchCadence_LeavesRoughlyHalfOfNightsQuiet()
        {
            int activeDays = 0;
            int quietPatrols = 0;
            for (int day = 1; day <= 60; day++)
            {
                if (CareerDispatchRules.IsActiveDispatchDay("n:7", day))
                {
                    activeDays++;
                    if (CareerDispatchRules.IsQuietNightShift("n:7", day))
                        quietPatrols++;
                }
            }
            Assert.InRange(activeDays, 20, 42);
            Assert.True(quietPatrols >= 1, "Expected at least one quiet night patrol over 60 days");
        }
    }
}
