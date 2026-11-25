using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Simitone.Conversations;

namespace Simitone.Conversations.Tests
{
    public sealed class CrimeSceneDeductionTests
    {
        [Fact]
        public void Registry_ResolvesLevel6Detective_AsImplemented()
        {
            var desc = CareerDispatchRegistry.Resolve("Law Enforcement", "Detective", 5);
            Assert.NotNull(desc);
            Assert.Equal(CareerMiniGameKind.CrimeSceneDeduction, desc.Kind);
            Assert.Equal("Detective", desc.RankTitle);
            Assert.Equal("Crime Scene Deduction", desc.MiniGameTitle);
            Assert.Equal(9, desc.DefaultShiftStartHour);
            Assert.Equal(15, desc.DefaultShiftEndHour);
            Assert.True(desc.IsImplemented);
        }

        [Fact]
        public void AllFourCases_HaveThreeDistinctCluesAndShuffledSuspectRoles()
        {
            var seenCaseIds = new HashSet<string>();
            var seenCulpritSlots = new HashSet<int>();

            for (int seed = 0; seed < 24; seed++)
            {
                var g = new CrimeSceneDeductionGame("Bob", logicSkill: 5, charismaSkill: 4,
                    suspectNames: new[] { "Mortimer", "Bella", "Betty" }, seed: seed);
                seenCaseIds.Add(g.CaseId);
                seenCulpritSlots.Add(g.CulpritIndex);

                Assert.Equal(3, g.Clues.Count);
                Assert.Equal(ClueCategory.Footprint, g.Clues[0].Category);
                Assert.Equal(ClueCategory.Motive, g.Clues[1].Category);
                Assert.Equal(ClueCategory.Alibi, g.Clues[2].Category);

                Assert.Equal(3, g.Suspects.Count);
                Assert.Equal(1, g.Suspects.Count(s => s.IsCulprit));

                var culprit = g.Suspects[g.CulpritIndex];
                Assert.True(culprit.IsCulprit);
                Assert.True(culprit.ContradictsFootprint && culprit.ContradictsMotive && culprit.ContradictsAlibi);

                // Exactly one red herring (contradicts motive only) and one clean innocent (0 contradictions)
                var others = g.Suspects.Where(s => !s.IsCulprit).ToList();
                Assert.Equal(2, others.Count);
                Assert.Contains(others, s => s.ContradictsMotive && !s.ContradictsFootprint && !s.ContradictsAlibi);
                Assert.Contains(others, s => !s.ContradictsMotive && !s.ContradictsFootprint && !s.ContradictsAlibi);
            }

            Assert.Equal(4, seenCaseIds.Count);
            Assert.True(seenCulpritSlots.Count >= 2, "Culprit slot should shuffle across seeds");
        }

        [Fact]
        public void ThoroughInvestigationWithForensicsAndCorrectAccusation_AwardsSRank()
        {
            var g = new CrimeSceneDeductionGame("Bob", logicSkill: 6, charismaSkill: 5,
                suspectNames: new[] { "Mortimer", "Bella", "Betty" }, seed: 42);

            Assert.Equal(DetectivePhase.Briefing, g.Phase);
            Assert.True(g.LogicFlagsContradictions);
            Assert.True(g.HasForensicsCrossCheck);
            Assert.Equal(1, g.ForensicsChargesLeft);

            g.BeginInvestigation();
            Assert.Equal(DetectivePhase.Investigating, g.Phase);

            // Inspect all 3 forensic clues
            for (int c = 0; c < 3; c++)
            {
                var r = g.InspectClue(c);
                Assert.True(r.Accepted);
                Assert.False(r.AlreadyDiscovered);
                Assert.Equal(15, r.PointsEarned);
            }
            Assert.Equal(3, g.CluesFoundCount);

            // Re-inspecting an already discovered clue awards 0 extra points
            var repeatClue = g.InspectClue(0);
            Assert.True(repeatClue.Accepted);
            Assert.True(repeatClue.AlreadyDiscovered);
            Assert.Equal(0, repeatClue.PointsEarned);

            // Forensics Lab Cross-Check eliminates a verified innocent (never culprit)
            var lab = g.UseForensicsCrossCheck();
            Assert.True(lab.Accepted);
            Assert.NotEqual(g.CulpritIndex, lab.EliminatedIndex);
            Assert.True(g.Suspects[lab.EliminatedIndex].EliminatedByForensics);
            Assert.Equal(0, g.ForensicsChargesLeft);
            Assert.False(g.UseForensicsCrossCheck().Accepted);

            // Question remaining suspects
            for (int s = 0; s < 3; s++)
            {
                var q = g.QuestionSuspect(s);
                Assert.True(q.Accepted);
            }
            Assert.Equal(3, g.SuspectsQuestionedCount);
            Assert.Equal(3, g.GetVisibleContradictions(g.CulpritIndex));

            // Accuse the true culprit
            var acc = g.AccuseSuspect(g.CulpritIndex);
            Assert.True(acc.Accepted);
            Assert.True(acc.CorrectCulprit);
            Assert.Equal(DetectivePhase.AccusationClimax, g.Phase);
            Assert.NotNull(acc.Outcome);

            g.CompleteClimax();
            Assert.Equal(DetectivePhase.Finished, g.Phase);
            Assert.True(g.IsFinished);
            Assert.Equal("S", g.Outcome.Grade);
            Assert.True(g.Outcome.SolvedCorrectly);
            Assert.Equal(450, g.Outcome.Reward.BonusSimoleons);
            Assert.Equal(15, g.Outcome.Reward.JobPerformanceDelta);
            Assert.Equal(125, g.Outcome.Reward.LogicXpDelta);
            Assert.Equal(65, g.Outcome.Reward.CharismaXpDelta);
        }

        [Fact]
        public void LowLogicDetective_NoContradictionFlagsAndNoForensicsCrossCheck()
        {
            var g = new CrimeSceneDeductionGame("Bob", logicSkill: 1, charismaSkill: 1, seed: 7);
            Assert.False(g.LogicFlagsContradictions);
            Assert.False(g.HasForensicsCrossCheck);
            Assert.Equal(0, g.ForensicsChargesLeft);

            g.BeginInvestigation();
            g.InspectClue(0);
            g.InspectClue(1);
            g.InspectClue(2);

            var q = g.QuestionSuspect(g.CulpritIndex);
            Assert.True(q.Accepted);
            Assert.Equal(0, q.VisibleContradictions); // Logic < 2 does not auto-flag contradictions

            var lab = g.UseForensicsCrossCheck();
            Assert.False(lab.Accepted);
        }

        [Fact]
        public void LogicContradictions_ExposeRetroactivelyWhenInspectingClueAfterQuestioning()
        {
            var g = new CrimeSceneDeductionGame("Bob", logicSkill: 3, charismaSkill: 2, seed: 11);
            g.BeginInvestigation();

            // Question culprit before finding any clues
            var q = g.QuestionSuspect(g.CulpritIndex);
            Assert.True(q.Accepted);
            Assert.Equal(0, q.VisibleContradictions);

            // Inspecting Footprint clue now retroactively flags 1 contradiction on the already-questioned culprit
            var r0 = g.InspectClue(0);
            Assert.Equal(1, r0.NewlyExposedContradictions);
            Assert.Equal(1, g.GetVisibleContradictions(g.CulpritIndex));

            var r2 = g.InspectClue(2);
            Assert.Equal(1, r2.NewlyExposedContradictions);
            Assert.Equal(2, g.GetVisibleContradictions(g.CulpritIndex));
        }

        [Fact]
        public void WrongAccusation_MarksUnsolvedAndReducesScore()
        {
            var g = new CrimeSceneDeductionGame("Bob", logicSkill: 4, charismaSkill: 3, seed: 5);
            g.BeginInvestigation();
            g.InspectClue(0);
            int wrongIdx = (g.CulpritIndex + 1) % 3;

            var acc = g.AccuseSuspect(wrongIdx);
            Assert.True(acc.Accepted);
            Assert.False(acc.CorrectCulprit);
            Assert.False(g.SolvedCorrectly);
            Assert.NotNull(acc.Outcome);
            Assert.Equal("C", acc.Outcome.Grade);
            Assert.Equal(g.Suspects[g.CulpritIndex].Name, acc.TrueCulpritName);

            // Cannot accuse twice
            Assert.False(g.AccuseSuspect(g.CulpritIndex).Accepted);
        }

        [Fact]
        public void ForensicsCrossCheck_NeverEliminatesCulpritAcrossSeeds()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var g = new CrimeSceneDeductionGame("Bob", logicSkill: 5, charismaSkill: 3, seed: seed);
                g.BeginInvestigation();
                var res = g.UseForensicsCrossCheck();
                Assert.True(res.Accepted);
                Assert.NotEqual(g.CulpritIndex, res.EliminatedIndex);
                Assert.False(g.Suspects[res.EliminatedIndex].IsCulprit);
            }
        }

        [Fact]
        public void ForceAbortAndFinish_CreatesValidFinishedOutcome()
        {
            var g = new CrimeSceneDeductionGame("Bob", logicSkill: 4, charismaSkill: 3, seed: 3);
            g.BeginInvestigation();
            var o = g.ForceAbortAndFinish();
            Assert.NotNull(o);
            Assert.True(g.IsFinished);
            Assert.False(o.SolvedCorrectly);
            Assert.Equal("C", o.Grade);
        }

        [Fact]
        public void DetectiveAudioSynth_SynthesizesAllTwelveCuesAndWritesValidWav()
        {
            foreach (DetectiveSfxId id in Enum.GetValues(typeof(DetectiveSfxId)))
            {
                byte[] pcm = DetectiveAudioSynth.SynthesizePcm16(id);
                Assert.NotNull(pcm);
                Assert.True(pcm.Length > 2000, id + " waveform too short");

                // Ensure waveform is not flat silence
                bool hasEnergy = false;
                for (int i = 0; i < pcm.Length - 1; i += 2)
                {
                    short sample = (short)(pcm[i] | (pcm[i + 1] << 8));
                    if (Math.Abs((int)sample) > 250) { hasEnergy = true; break; }
                }
                Assert.True(hasEnergy, id + " produced silent audio");
            }

            string tmpWav = Path.Combine(Path.GetTempPath(), "detective_synth_test_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                var events = new List<DetectiveAudioEvent>
                {
                    new DetectiveAudioEvent { TimeSec = 0.2, Id = DetectiveSfxId.CaseBoardOpen, Volume = 0.5f },
                    new DetectiveAudioEvent { TimeSec = 0.8, Id = DetectiveSfxId.ClueCameraFlash, Volume = 0.6f },
                    new DetectiveAudioEvent { TimeSec = 1.5, Id = DetectiveSfxId.ContradictionPing, Volume = 0.55f },
                    new DetectiveAudioEvent { TimeSec = 2.2, Id = DetectiveSfxId.AccuseDramaticHit, Volume = 0.6f },
                    new DetectiveAudioEvent { TimeSec = 2.9, Id = DetectiveSfxId.CulpritCaughtBust, Volume = 0.6f },
                    new DetectiveAudioEvent { TimeSec = 3.5, Id = DetectiveSfxId.ShiftVictoryFanfare, Volume = 0.55f },
                };
                DetectiveAudioSynth.WriteMixedTrackWav(tmpWav, 4.5, events);
                Assert.True(File.Exists(tmpWav));
                byte[] bytes = File.ReadAllBytes(tmpWav);
                Assert.True(bytes.Length > 44 + 44100);
                Assert.Equal((byte)'R', bytes[0]);
                Assert.Equal((byte)'I', bytes[1]);
                Assert.Equal((byte)'F', bytes[2]);
                Assert.Equal((byte)'F', bytes[3]);
                Assert.Equal((byte)'W', bytes[8]);
                Assert.Equal((byte)'A', bytes[9]);
                Assert.Equal((byte)'V', bytes[10]);
                Assert.Equal((byte)'E', bytes[11]);
            }
            finally
            {
                try { if (File.Exists(tmpWav)) File.Delete(tmpWav); } catch { }
            }
        }
    }
}
