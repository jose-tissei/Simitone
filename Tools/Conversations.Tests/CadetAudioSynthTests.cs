using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Simitone.Conversations.Tests
{
    public class CadetAudioSynthTests
    {
        [Fact]
        public void AllCadetSfxIds_ProduceNonSilentBoundedClickFreePcm()
        {
            var ids = (CadetSfxId[])Enum.GetValues(typeof(CadetSfxId));
            Assert.True(ids.Length >= 18);

            foreach (var id in ids)
            {
                short[] pcm = CadetAudioSynth.BuildSound(id);
                Assert.NotNull(pcm);
                Assert.True(pcm.Length >= 1500, $"{id} too short ({pcm.Length} samples)");

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

                Assert.True(rms > 0.015, $"{id} RMS too quiet ({rms:0.0000})");
                Assert.True(peak <= 32760, $"{id} clipped ({peak})");
                // Edge fade guarantees click-free playback start and end
                Assert.True(Math.Abs((int)pcm[0]) <= 200, $"{id} non-zero start ({pcm[0]})");
                Assert.True(Math.Abs((int)pcm[pcm.Length - 1]) <= 200, $"{id} non-zero end ({pcm[pcm.Length - 1]})");
            }
        }

        [Fact]
        public void AcademyAmbienceLoop_IsGentleSeamlessAndExactLength()
        {
            short[] amb = CadetAudioSynth.BuildAcademyAmbienceLoop(8.0f);
            Assert.Equal(CadetAudioSynth.SampleRate * 8, amb.Length);

            double sumSq = 0.0;
            for (int i = 0; i < amb.Length; i++)
            {
                double f = amb[i] / 32768.0;
                sumSq += f * f;
            }
            double rms = Math.Sqrt(sumSq / amb.Length);

            Assert.InRange(rms, 0.005, 0.20);
            int seamDelta = Math.Abs(amb[0] - amb[amb.Length - 1]);
            Assert.True(seamDelta < 1200, $"Ambience loop seam jump too large: {seamDelta}");
        }

        [Fact]
        public void BuildSound_IsDeterministic()
        {
            short[] a = CadetAudioSynth.BuildSound(CadetSfxId.RangeShot);
            short[] b = CadetAudioSynth.BuildSound(CadetSfxId.RangeShot);
            Assert.Equal(a.Length, b.Length);
            Assert.Equal(a, b);
        }

        [Fact]
        public void RenderMixedTimelineWav_ProducesValidRiffWaveWithMixedAudio()
        {
            var events = new List<CadetSfxEvent>
            {
                new CadetSfxEvent(0.10, CadetSfxId.ArrivalCue, 0.44f),
                new CadetSfxEvent(0.95, CadetSfxId.Whistle, 0.42f),
                new CadetSfxEvent(1.50, CadetSfxId.RangeShot, 0.54f),
                new CadetSfxEvent(1.60, CadetSfxId.SteelBullseye, 0.50f),
                new CadetSfxEvent(2.10, CadetSfxId.VictoryFanfareHigh, 0.52f),
            };

            byte[] wav = CadetAudioSynth.RenderMixedTimelineWav(
                totalDurationSec: 3.6f,
                includeAmbience: true,
                ambienceVolume: 0.24f,
                ambienceStartSec: 0.05f,
                ambienceEndSec: 3.4f,
                events: events);

            int expectedSamples = (int)Math.Round(CadetAudioSynth.SampleRate * 3.6f);
            Assert.Equal(44 + expectedSamples * 2, wav.Length);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

            int sampleRate = BitConverter.ToInt32(wav, 24);
            short bitsPerSample = BitConverter.ToInt16(wav, 34);
            Assert.Equal(CadetAudioSynth.SampleRate, sampleRate);
            Assert.Equal(16, bitsPerSample);

            double sumSq = 0.0;
            for (int off = 44; off < wav.Length; off += 2)
            {
                short s = BitConverter.ToInt16(wav, off);
                double f = s / 32768.0;
                sumSq += f * f;
            }
            double rms = Math.Sqrt(sumSq / expectedSamples);
            Assert.True(rms > 0.02, $"Mixed WAV unexpectedly quiet (RMS={rms:0.0000})");
        }
    }
}
