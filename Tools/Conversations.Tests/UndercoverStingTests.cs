using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Simitone.Conversations;

namespace Simitone.Conversations.Tests
{
    public sealed class UndercoverStingTests
    {
        sealed class StubLayaTransport : ILayaTransport
        {
            readonly string reply;
            public string LastBody;
            public StubLayaTransport(string reply) { this.reply = reply; }
            public string Post(string url, string body, string bearer, int timeoutMs)
            {
                LastBody = body;
                return "{\"answers\":{\"q\":{\"choice\":\"" + reply + "\",\"probabilities\":{\"" + reply + "\":0.85,\"guarded\":0.10,\"suspicious\":0.05}}}}";
            }
        }

        [Fact]
        public void Registry_ResolvesLevel5ViceSquad_AsImplemented()
        {
            var desc = CareerDispatchRegistry.Resolve("Law Enforcement", "Vice Squad", 4);
            Assert.NotNull(desc);
            Assert.Equal(CareerMiniGameKind.UndercoverSting, desc.Kind);
            Assert.Equal("Vice Squad", desc.RankTitle);
            Assert.Equal("Undercover Sting", desc.MiniGameTitle);
            Assert.Equal(22, desc.DefaultShiftStartHour);
            Assert.Equal(4, desc.DefaultShiftEndHour);
            Assert.True(desc.IsImplemented);
        }

        [Fact]
        public void AllFourCases_HaveThreeTurnsAndThreeDistinctTones()
        {
            var seenIds = new HashSet<string>();
            for (int seed = 0; seed < 4; seed++)
            {
                var g = new UndercoverStingGame("Bob", 4, 4, "Mortimer Goth", 0x1234, "Bella Goth", 0x5678, seed: seed, startInBriefing: false);
                seenIds.Add(g.CaseProfile.CaseId);
                Assert.Equal(3, g.CaseProfile.Turns.Length);
                foreach (var t in g.CaseProfile.Turns)
                {
                    Assert.Equal(3, t.Options.Length);
                    Assert.Contains(t.Options, o => o.Tone == StingTone.Gentle);
                    Assert.Contains(t.Options, o => o.Tone == StingTone.Playful);
                    Assert.Contains(t.Options, o => o.Tone == StingTone.Bold);
                    Assert.Contains(t.Options, o => o.IsBestForTurn);
                }
            }
            Assert.Equal(4, seenIds.Count);
        }

        [Fact]
        public void OptimalPlay_BuildsConfessionUnlocksBustAndAwardsSRank()
        {
            var g = new UndercoverStingGame("Bob", charismaSkillLevel: 5, logicSkillLevel: 4,
                suspectName: "Mortimer Goth", suspectGuid: 0x1111,
                informantName: "Bella Goth", informantGuid: 0x2222,
                seed: 42, startInBriefing: true);

            Assert.True(g.IsBriefing);
            Assert.True(g.LogicProfileUnlocked);
            Assert.Equal(2, g.MaxWireCharges);
            g.BeginShift();
            Assert.False(g.IsBriefing);

            for (int i = 0; i < UndercoverStingGame.MaxTurns && !g.IsFinished; i++)
            {
                var turn = g.CurrentTurn;
                Assert.NotNull(turn);
                var best = turn.Options.First(o => o.IsBestForTurn);
                var res = g.ChooseTone(best.Tone);
                Assert.NotNull(res);
                Assert.True(res.ConfessionDelta > 0);
            }

            Assert.True(g.CanFlashBadgeBust);
            Assert.True(g.Confession >= UndercoverStingGame.BustConfessionThreshold);
            Assert.True(g.RecordedSlips.Count >= 2);

            string msg;
            Assert.True(g.TryFlashBadgeBust(out msg));
            Assert.True(g.IsFinished);
            Assert.Equal(StingEndingKind.SuspectBusted, g.Ending);

            var outcome = g.ComputeOutcome();
            Assert.Equal("S", outcome.Grade);
            Assert.True(outcome.BonusSimoleons >= 200);
            Assert.True(outcome.JobPerformanceDelta >= 28);
            Assert.True(outcome.CharismaXpDelta >= 50);
            Assert.True(outcome.LogicXpDelta >= 35);
            Assert.True(outcome.SuspectDailyRelDelta < 0);
            Assert.True(outcome.InformantDailyRelDelta > 0);
        }

        [Fact]
        public void CheckWire_ReducesSuspicionRevealsTellAndConsumesCharge()
        {
            var g = new UndercoverStingGame("Bob", charismaSkillLevel: 2, logicSkillLevel: 1,
                suspectName: "Michael Bachelor", suspectGuid: 0x1,
                informantName: "Betty Newbie", informantGuid: 0x2,
                seed: 1, startInBriefing: false);

            Assert.False(g.LogicProfileUnlocked);
            Assert.Equal(1, g.MaxWireCharges);
            Assert.False(g.WireTellRevealedThisTurn);

            // Pick a risky option first so suspicion rises above 20
            var trap = g.CurrentTurn.Options.FirstOrDefault(o => o.IsRiskyTrap) ?? g.CurrentTurn.Options[2];
            g.ChooseTone(trap.Tone);
            int suspBefore = g.Suspicion;
            int confBefore = g.Confession;

            string msg;
            Assert.True(g.TryCheckWire(out msg));
            Assert.Equal(0, g.WireChargesRemaining);
            Assert.True(g.Suspicion < suspBefore);
            Assert.Equal(Math.Min(100, confBefore + 6), g.Confession);
            Assert.True(g.WireTellRevealedThisTurn);

            // Second wire check is rejected when charges are spent
            Assert.False(g.TryCheckWire(out msg));
        }

        [Fact]
        public void HighSuspicionTraps_BlowCoverAndEndOperation()
        {
            var g = new UndercoverStingGame("Bob", charismaSkillLevel: 0, logicSkillLevel: 0,
                suspectName: "Claire Charming", suspectGuid: 0x1,
                informantName: "Chris Roomies", informantGuid: 0x2,
                seed: 0, startInBriefing: false);

            while (!g.IsFinished && g.CurrentTurn != null)
            {
                var trap = g.CurrentTurn.Options.OrderByDescending(o => o.BaseSuspicionDelta).First();
                g.ChooseTone(trap.Tone, layaVerdict: "suspicious");
            }

            Assert.True(g.IsFinished);
            Assert.Equal(StingEndingKind.CoverBlown, g.Ending);
            Assert.Equal(100, g.Suspicion);

            var outcome = g.ComputeOutcome();
            Assert.Equal("C", outcome.Grade);
            Assert.True(outcome.BonusSimoleons < 100);
        }

        [Fact]
        public void LayaClassifier_IntegratesWithCustomTransport()
        {
            var stub = new StubLayaTransport("opens_up");
            var client = new LayaClient(stub);
            client.Configure("http://127.0.0.1:9", "test-key");

            var g = new UndercoverStingGame("Bob", 4, 4, "Mortimer Goth", 1, "Bella Goth", 2, seed: 42, startInBriefing: false);
            var turn = g.CurrentTurn;
            var opt = turn.Options[0];

            string verdict = UndercoverStingGame.TryClassifyWithLaya(client, g.CaseProfile, turn, opt);
            Assert.Equal("opens_up", verdict);
            Assert.NotNull(stub.LastBody);
            Assert.Contains("opens_up", stub.LastBody);
        }

        [Fact]
        public void AudioSynth_GeneratesAllTwelveCuesAndValidWav()
        {
            foreach (StingSfxId id in Enum.GetValues(typeof(StingSfxId)))
            {
                var pcm = StingAudioSynth.SynthesizePcm16(id);
                Assert.NotNull(pcm);
                Assert.True(pcm.Length > 2000, "Expected non-trivial PCM for " + id);
            }

            var cues = new List<StingAudioEvent>
            {
                new StingAudioEvent { TimeSec = 0.2, Id = StingSfxId.WireClick, Volume = 0.5f },
                new StingAudioEvent { TimeSec = 1.0, Id = StingSfxId.TonePlayful, Volume = 0.45f },
                new StingAudioEvent { TimeSec = 1.8, Id = StingSfxId.BadgeFlashBust, Volume = 0.6f },
            };
            byte[] wav = StingAudioSynth.ExportTimelineWav(cues, totalDurationSec: 3.5, leadSec: 0.0, ambienceVolume: 0.16f);
            Assert.NotNull(wav);
            Assert.True(wav.Length > 44);
            Assert.Equal((byte)'R', wav[0]);
            Assert.Equal((byte)'I', wav[1]);
            Assert.Equal((byte)'F', wav[2]);
            Assert.Equal((byte)'F', wav[3]);
        }
    }
}
