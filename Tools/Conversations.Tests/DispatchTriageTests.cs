using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Simitone.Conversations;

namespace Simitone.Conversations.Tests
{
    public sealed class DispatchTriageTests
    {
        static List<DispatchCallerInfo> SampleCallers()
        {
            return new List<DispatchCallerInfo>
            {
                new DispatchCallerInfo { Name = "Mortimer Goth", NeighborId = 10, Address = "5 Sim Lane" },
                new DispatchCallerInfo { Name = "Bella Goth", NeighborId = 11, Address = "5 Sim Lane" },
                new DispatchCallerInfo { Name = "Michael Bachelor", NeighborId = 12, Address = "10 Sim Lane" },
                new DispatchCallerInfo { Name = "Betty Newbie", NeighborId = 13, Address = "7 Sim Lane" },
                new DispatchCallerInfo { Name = "Chris Roomies", NeighborId = 14, Address = "21 Downtown Plaza" },
                new DispatchCallerInfo { Name = "Claire Charming", NeighborId = 15, Address = "55 Old Town Market" },
            };
        }

        [Fact]
        public void Registry_ResolvesLevel4DeskSergeant_AsImplemented()
        {
            var desc = CareerDispatchRegistry.Resolve("Law Enforcement", "Desk Sergeant", 3);
            Assert.NotNull(desc);
            Assert.Equal(CareerMiniGameKind.DispatchTriage, desc.Kind);
            Assert.Equal("Desk Sergeant", desc.RankTitle);
            Assert.True(desc.IsImplemented);
        }

        [Fact]
        public void ShiftPlan_ContainsBoth911PhoneCallsAndLiveWalkIns()
        {
            var g = new DispatchTriageGame("Bob", logicSkillLevel: 4, charismaSkillLevel: 3, neighborhoodCallers: SampleCallers(), seed: 42, startInBriefing: true);
            Assert.True(g.IsBriefing);
            Assert.Equal(DispatchTriageGame.DefaultTotalCases, g.TotalCases);
            Assert.True(g.LogicCluesUnlocked);
            Assert.Contains(g.AllCases, c => c.Channel == DispatchCallChannel.Phone911);
            Assert.Contains(g.AllCases, c => c.Channel == DispatchCallChannel.WalkIn);
            Assert.Contains(g.AllCases, c => c.CorrectAction == DispatchRouteAction.DispatchPatrol);
            Assert.Contains(g.AllCases, c => c.CorrectAction == DispatchRouteAction.DismissCall);
            Assert.Equal("Mortimer Goth", g.AllCases[0].CallerName);
        }

        [Fact]
        public void PerfectShift_AwardsSRankRewardsAndPositiveNeighborRels()
        {
            var g = new DispatchTriageGame("Bob", logicSkillLevel: 5, charismaSkillLevel: 5, neighborhoodCallers: SampleCallers(), seed: 42, startInBriefing: false);

            int safety = 0;
            while (!g.IsFinished && safety++ < 600)
            {
                g.Tick(0.25f);
                var sel = g.SelectedCase;
                if (sel != null)
                {
                    // Wait for walk-in to approach counter briefly, then route correctly!
                    if (sel.Channel == DispatchCallChannel.WalkIn && sel.WalkInApproach < 1f)
                        continue;
                    g.RouteSelectedCase(sel.CorrectAction);
                }
            }

            Assert.True(g.IsFinished);
            var outcome = g.ComputeOutcome();
            Assert.Equal("S", outcome.Grade);
            Assert.Equal(6, outcome.PerfectCount + outcome.GoodCount);
            Assert.Equal(0, outcome.WrongCount);
            Assert.Equal(0, outcome.MissedCount);
            Assert.True(outcome.WalkInsHandled >= 2);
            Assert.True(outcome.PranksBlocked >= 1);
            Assert.Equal(175, outcome.Reward.BonusSimoleons);
            Assert.Equal(28, outcome.Reward.JobPerformanceDelta);
            Assert.True(outcome.Reward.LogicXpDelta >= 50);
            Assert.True(outcome.Reward.CharismaXpDelta >= 50);
            Assert.True(outcome.RelationshipDeltas.Count >= 3);
            Assert.All(outcome.RelationshipDeltas, d => Assert.Equal(3, d.DailyDelta));
        }

        [Fact]
        public void FireUnitCooldown_BlocksImmediateDoubleDispatch_UntilReady()
        {
            var g = new DispatchTriageGame("Bob", 3, 3, SampleCallers(), seed: 42, startInBriefing: false);
            Assert.Equal(1, g.AvailableFireUnits);

            // Dispatch Fire on Case 0
            Assert.True(g.RouteSelectedCase(DispatchRouteAction.DispatchFire));
            Assert.Equal(0, g.AvailableFireUnits);
            Assert.True(g.NextFireReadySec > 5f);

            // Advance slightly so Case 1 arrives, try dispatching Fire again immediately -> blocked!
            for (int i = 0; i < 12; i++) g.Tick(0.25f);
            Assert.NotNull(g.SelectedCase);
            Assert.False(g.RouteSelectedCase(DispatchRouteAction.DispatchFire));

            // Advance past cooldown -> Fire Engine becomes ready again!
            for (int i = 0; i < 30; i++) g.Tick(0.25f);
            Assert.Equal(1, g.AvailableFireUnits);
        }

        [Fact]
        public void CalmCaller_ExtendsTimerAndConsumesCharge()
        {
            var g = new DispatchTriageGame("Bob", 1, 4, SampleCallers(), seed: 99, startInBriefing: false);
            Assert.False(g.LogicCluesUnlocked);
            int beforeCharges = g.CalmChargesRemaining;
            Assert.True(beforeCharges >= 2);

            // Let urgency drain by 8 seconds
            for (int i = 0; i < 32; i++) g.Tick(0.25f);
            var sel = g.SelectedCase;
            Assert.NotNull(sel);
            float beforeSec = sel.RemainingUrgencySec;

            Assert.True(g.CalmSelectedCaller());
            Assert.Equal(beforeCharges - 1, g.CalmChargesRemaining);
            Assert.True(sel.RemainingUrgencySec > beforeSec + 4.5f);
        }

        [Fact]
        public void MissedCall_PenalizesScoreAndProducesNegativeNeighborDelta()
        {
            var g = new DispatchTriageGame("Bob", 2, 0, SampleCallers(), seed: 42, startInBriefing: false);
            // Ignore all calls until shift finishes via timeouts
            for (int i = 0; i < 600 && !g.IsFinished; i++)
                g.Tick(0.25f);

            Assert.True(g.IsFinished);
            var o = g.ComputeOutcome();
            Assert.Equal("C", o.Grade);
            Assert.Equal(6, o.MissedCount);
            Assert.Contains(o.RelationshipDeltas, d => d.DailyDelta < 0);
        }

        [Fact]
        public void DispatchAudioSynth_SynthesizesAllCuesAndValidWavTimeline()
        {
            foreach (DispatchSfxId id in Enum.GetValues(typeof(DispatchSfxId)))
            {
                byte[] pcm = DispatchAudioSynth.SynthesizePcm16(id);
                Assert.NotNull(pcm);
                Assert.True(pcm.Length > 200, "Cue " + id + " too short");
                bool anyNonZero = pcm.Any(b => b != 0);
                Assert.True(anyNonZero, "Cue " + id + " was completely silent");
            }

            var events = new List<DispatchAudioEvent>
            {
                new DispatchAudioEvent { TimeSec = 0.2f, Cue = DispatchSfxId.PhoneRing, Gain = 0.5f },
                new DispatchAudioEvent { TimeSec = 1.0f, Cue = DispatchSfxId.WalkInBell, Gain = 0.5f },
                new DispatchAudioEvent { TimeSec = 1.4f, Cue = DispatchSfxId.PaperFormStamp, Gain = 0.55f },
                new DispatchAudioEvent { TimeSec = 1.8f, Cue = DispatchSfxId.RadioDispatchPatrol, Gain = 0.5f },
            };
            byte[] wav = DispatchAudioSynth.RenderTimelineToWav(events, 3.0f, 0.18f);
            Assert.True(wav.Length > 44 + DispatchAudioSynth.SampleRate * 2 * 2);
            Assert.Equal((byte)'R', wav[0]);
            Assert.Equal((byte)'I', wav[1]);
            Assert.Equal((byte)'F', wav[2]);
            Assert.Equal((byte)'F', wav[3]);
            Assert.Equal((byte)'W', wav[8]);
            Assert.Equal((byte)'A', wav[9]);
            Assert.Equal((byte)'V', wav[10]);
            Assert.Equal((byte)'E', wav[11]);
        }

        [Fact]
        public void DeskSergeantAnimationPack_All6ClipsExistAndParseCleanly()
        {
            string animDir = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "Conversations", "Assets", "Animations"));
            string[] expected =
            {
                "desk_idle_watch",
                "desk_answer_phone",
                "desk_radio_dispatch",
                "desk_wave_dismiss",
                "desk_walkin_walk",
                "desk_walkin_plead"
            };

            foreach (var name in expected)
            {
                string path = Path.Combine(animDir, name + ".simanim.json");
                Assert.True(File.Exists(path), "Missing exported Desk Sergeant animation: " + path);
                string err;
                var clip = CustomAnimLoader.ParseJson(File.ReadAllText(path), out err);
                Assert.Null(err);
                Assert.NotNull(clip);
                Assert.Equal(name, clip.Name);
                Assert.True(clip.FrameCount >= 24);
                Assert.NotNull(clip.GetTrack("PELVIS"));
                Assert.NotNull(clip.GetTrack("R_ARM1"));
                Assert.NotNull(clip.GetTrack("L_ARM1"));
            }
        }
    }
}
