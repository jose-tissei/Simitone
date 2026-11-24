using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Simitone.Conversations.Tests
{
    public class PatrolAudioSynthTests
    {
        [Fact]
        public void AllPatrolSfxIds_ProduceNonSilentBoundedClickFreePcm()
        {
            var ids = (PatrolSfxId[])Enum.GetValues(typeof(PatrolSfxId));
            Assert.True(ids.Length >= 17);

            foreach (var id in ids)
            {
                short[] pcm = PatrolAudioSynth.BuildSound(id);
                Assert.NotNull(pcm);
                Assert.True(pcm.Length >= 1200, $"{id} too short ({pcm.Length} samples)");

                double sumSq = 0.0;
                int peak = 0;
                for (int i = 0; i < pcm.Length; i++)
                {
                    int abs = Math.Abs((int)pcm[i]);
                    if (abs > peak) peak = abs;
                    double f = pcm[i] / 32768.0;
                    sumSq += f * f;
                }
                double rms = Math.Sqrt(sumSq / pcm.Length);

                Assert.True(rms > 0.012, $"{id} RMS too quiet ({rms:0.0000})");
                Assert.True(peak <= 32760, $"{id} clipped ({peak})");
                Assert.True(Math.Abs((int)pcm[0]) <= 220, $"{id} non-zero start ({pcm[0]})");
                Assert.True(Math.Abs((int)pcm[pcm.Length - 1]) <= 220, $"{id} non-zero end ({pcm[pcm.Length - 1]})");
            }
        }

        [Fact]
        public void CityDuskAmbienceLoop_IsGentleSeamlessAndExactLength()
        {
            short[] amb = PatrolAudioSynth.BuildCityDuskAmbienceLoop(8.0f);
            Assert.Equal(PatrolAudioSynth.SampleRate * 8, amb.Length);

            double sumSq = 0.0;
            for (int i = 0; i < amb.Length; i++)
            {
                double f = amb[i] / 32768.0;
                sumSq += f * f;
            }
            double rms = Math.Sqrt(sumSq / amb.Length);

            Assert.InRange(rms, 0.004, 0.20);
            int seamDelta = Math.Abs(amb[0] - amb[amb.Length - 1]);
            Assert.True(seamDelta < 1200, $"Dusk ambience loop seam jump too large: {seamDelta}");
        }

        [Fact]
        public void RenderMixedTimelineWav_ProducesValidRiffWaveWithMixedAudio()
        {
            var events = new List<PatrolSfxEvent>
            {
                new PatrolSfxEvent(0.10, PatrolSfxId.CruiserArriveSiren, 0.45f),
                new PatrolSfxEvent(0.65, PatrolSfxId.CarDoorOpen, 0.40f),
                new PatrolSfxEvent(1.00, PatrolSfxId.SneakStepA, 0.36f),
                new PatrolSfxEvent(1.35, PatrolSfxId.SneakStepB, 0.36f),
                new PatrolSfxEvent(1.75, PatrolSfxId.HandcuffClick, 0.48f),
                new PatrolSfxEvent(2.05, PatrolSfxId.PerfectCallSting, 0.46f),
                new PatrolSfxEvent(2.50, PatrolSfxId.ShiftCompleteFanfare, 0.50f),
            };

            byte[] wav = PatrolAudioSynth.RenderMixedTimelineWav(
                totalDurationSec: 3.5f,
                includeAmbience: true,
                ambienceVolume: 0.22f,
                ambienceStartSec: 0.0f,
                ambienceEndSec: 3.3f,
                events: events);

            Assert.NotNull(wav);
            Assert.True(wav.Length > 44 + PatrolAudioSynth.SampleRate * 3 * 2);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

            int sampleCount = (wav.Length - 44) / 2;
            int peak = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(wav, 44 + i * 2);
                int a = Math.Abs((int)s);
                if (a > peak) peak = a;
            }
            Assert.True(peak > 4000, $"Mixed WAV peak too quiet ({peak})");
        }
    }
}
