using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Conversations
{
    public enum StingSfxId
    {
        LoungeAmbience = 0,
        WireClick = 1,
        ToneGentle = 2,
        TonePlayful = 3,
        ToneBold = 4,
        SlipCaptured = 5,
        SuspicionSpike = 6,
        WireCalm = 7,
        ConfessionReady = 8,
        BadgeFlashBust = 9,
        CoverBlown = 10,
        ShiftVictory = 11,
    }

    public sealed class StingAudioEvent
    {
        public double TimeSec;
        public StingSfxId Id;
        public float Volume;
    }

    // Pure-C# procedural noir-jazz & undercover speakeasy audio synthesizer (22050 Hz, 16-bit mono PCM).
    public static class StingAudioSynth
    {
        public const int SampleRate = 22050;

        public static byte[] SynthesizePcm16(StingSfxId id)
        {
            float[] samples = SynthesizeFloat(id);
            byte[] pcm = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float v = Math.Max(-1f, Math.Min(1f, samples[i]));
                short s = (short)(v * 32767f);
                pcm[i * 2] = (byte)(s & 0xFF);
                pcm[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return pcm;
        }

        public static float[] SynthesizeFloat(StingSfxId id)
        {
            switch (id)
            {
                case StingSfxId.LoungeAmbience: return SynthLoungeAmbience();
                case StingSfxId.WireClick: return SynthWireClick();
                case StingSfxId.ToneGentle: return SynthJazzChord(new[] { 261.63f, 311.13f, 392.00f, 466.16f }, 0.45f, 0.34f); // Cm7 warm
                case StingSfxId.TonePlayful: return SynthArp(new[] { 349.23f, 440.00f, 523.25f }, 0.08f, 0.35f);
                case StingSfxId.ToneBold: return SynthBrassHit(new[] { 146.83f, 220.00f, 293.66f, 349.23f }, 0.42f, 0.44f);
                case StingSfxId.SlipCaptured: return SynthSlipCaptured();
                case StingSfxId.SuspicionSpike: return SynthSuspicionSpike();
                case StingSfxId.WireCalm: return SynthWireCalm();
                case StingSfxId.ConfessionReady: return SynthConfessionReady();
                case StingSfxId.BadgeFlashBust: return SynthBadgeFlashBust();
                case StingSfxId.CoverBlown: return SynthCoverBlown();
                case StingSfxId.ShiftVictory: return SynthShiftVictory();
                default: return SynthWireClick();
            }
        }

        // 4.0-second seamless smoky speakeasy upright walking bass + warm Rhodes pad + subtle glass clinks
        static float[] SynthLoungeAmbience()
        {
            float dur = 4.0f;
            int n = (int)(SampleRate * dur);
            float[] buf = new float[n];
            // Walking noir bass in C minor: C2 -> Eb2 -> F2 -> G2 -> Ab2 -> G2 -> F2 -> D2
            float[] bassNotes = { 65.41f, 77.78f, 87.31f, 98.00f, 103.83f, 98.00f, 87.31f, 73.42f };
            float beatDur = dur / bassNotes.Length;
            var rng = new Random(1955);

            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                int bIdx = Math.Min(bassNotes.Length - 1, (int)(t / beatDur));
                float bt = t - bIdx * beatDur;
                float env = (float)Math.Exp(-bt * 4.5f) * Math.Min(1f, bt / 0.015f);
                float f = bassNotes[bIdx];
                float bass = (float)(Math.Sin(2.0 * Math.PI * f * bt) * 0.55 + Math.Sin(2.0 * Math.PI * f * 2.0 * bt) * 0.22) * env;

                // Soft smoky Rhodes minor-9th pad
                float pad = (float)(
                    Math.Sin(2.0 * Math.PI * 261.63 * t) * 0.10 +
                    Math.Sin(2.0 * Math.PI * 311.13 * t) * 0.09 +
                    Math.Sin(2.0 * Math.PI * 466.16 * t) * 0.07 +
                    Math.Sin(2.0 * Math.PI * 587.33 * t) * 0.05) * (0.75f + 0.25f * (float)Math.Sin(2.0 * Math.PI * 0.5 * t));

                // Subtle brush snare whisper on offbeats
                float brush = 0f;
                float halfBeat = bt % (beatDur * 0.5f);
                if (halfBeat < 0.045f)
                    brush = ((float)rng.NextDouble() * 2f - 1f) * (1f - halfBeat / 0.045f) * 0.045f;

                // Seamless loop fade at edges
                float edge = 1f;
                if (i < 440) edge = i / 440f;
                else if (i > n - 440) edge = (n - i) / 440f;

                buf[i] = (bass * 0.45f + pad * 0.38f + brush) * edge;
            }
            return buf;
        }

        static float[] SynthWireClick()
        {
            int n = (int)(SampleRate * 0.16f);
            float[] buf = new float[n];
            var rng = new Random(77);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float click = (t < 0.02f) ? (float)Math.Sin(2.0 * Math.PI * 1900.0 * t) * (1f - t / 0.02f) * 0.55f : 0f;
                float beep = (t >= 0.03f && t < 0.11f) ? (float)Math.Sin(2.0 * Math.PI * 1480.0 * t) * (float)Math.Exp(-(t - 0.03f) * 28f) * 0.35f : 0f;
                float staticNoise = ((float)rng.NextDouble() * 2f - 1f) * (float)Math.Exp(-t * 24f) * 0.12f;
                buf[i] = click + beep + staticNoise;
            }
            return buf;
        }

        static float[] SynthJazzChord(float[] freqs, float dur, float gain)
        {
            int n = (int)(SampleRate * dur);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float env = Math.Min(1f, t / 0.015f) * (float)Math.Exp(-t * 5.2f);
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                    s += (float)(Math.Sin(2.0 * Math.PI * freqs[k] * t) + 0.25 * Math.Sin(2.0 * Math.PI * freqs[k] * 2.0 * t));
                buf[i] = (s / freqs.Length) * env * gain;
            }
            return buf;
        }

        static float[] SynthArp(float[] freqs, float stepDur, float gain)
        {
            float dur = stepDur * freqs.Length + 0.22f;
            int n = (int)(SampleRate * dur);
            float[] buf = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(k * stepDur * SampleRate);
                int len = (int)(0.24f * SampleRate);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Math.Min(1f, t / 0.008f) * (float)Math.Exp(-t * 11f);
                    buf[start + i] += (float)Math.Sin(2.0 * Math.PI * freqs[k] * t) * env * gain;
                }
            }
            return buf;
        }

        static float[] SynthBrassHit(float[] freqs, float dur, float gain)
        {
            int n = (int)(SampleRate * dur);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float env = Math.Min(1f, t / 0.018f) * (float)Math.Exp(-t * 4.5f);
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float f = freqs[k];
                    s += (float)(Math.Sin(2.0 * Math.PI * f * t) * 0.6 +
                                 Math.Sin(2.0 * Math.PI * f * 2.0 * t) * 0.3 +
                                 Math.Sin(2.0 * Math.PI * f * 3.0 * t) * 0.15);
                }
                buf[i] = (s / freqs.Length) * env * gain;
            }
            return buf;
        }

        // Reel-to-reel tape latch + bright double confirmation chime
        static float[] SynthSlipCaptured()
        {
            int n = (int)(SampleRate * 0.46f);
            float[] buf = new float[n];
            float[] notes = { 587.33f, 880.00f, 1174.66f };
            for (int k = 0; k < notes.Length; k++)
            {
                int st = (int)(k * 0.08f * SampleRate);
                int len = (int)(0.24f * SampleRate);
                for (int i = 0; i < len && st + i < n; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Math.Min(1f, t / 0.006f) * (float)Math.Exp(-t * 12f);
                    buf[st + i] += (float)(Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.42 +
                                           Math.Sin(2.0 * Math.PI * notes[k] * 2.0 * t) * 0.14) * env;
                }
            }
            return buf;
        }

        // Tense double heartbeat thump + minor-second noir sting
        static float[] SynthSuspicionSpike()
        {
            int n = (int)(SampleRate * 0.55f);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float beat1 = (float)Math.Sin(2.0 * Math.PI * (78f - 28f * t) * t) * (float)Math.Exp(-t * 14f) * 0.65f;
                float t2 = t - 0.18f;
                float beat2 = (t2 > 0f) ? (float)Math.Sin(2.0 * Math.PI * (72f - 22f * t2) * t2) * (float)Math.Exp(-t2 * 14f) * 0.55f : 0f;
                float tension = (float)(Math.Sin(2.0 * Math.PI * 233.08 * t) + Math.Sin(2.0 * Math.PI * 246.94 * t)) * 0.16f * (float)Math.Exp(-t * 4.5f);
                buf[i] = beat1 + beat2 + tension;
            }
            return buf;
        }

        // Ice-cube clink + smooth Rhodes cool-down chord
        static float[] SynthWireCalm()
        {
            int n = (int)(SampleRate * 0.55f);
            float[] buf = new float[n];
            float[] chord = { 349.23f, 440.00f, 523.25f, 659.25f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float clink = (t < 0.09f) ? (float)Math.Sin(2.0 * Math.PI * 3420.0 * t) * (float)Math.Exp(-t * 45f) * 0.35f : 0f;
                float clink2 = (t >= 0.06f && t < 0.16f) ? (float)Math.Sin(2.0 * Math.PI * 4120.0 * (t - 0.06f)) * (float)Math.Exp(-(t - 0.06f) * 42f) * 0.25f : 0f;
                float env = Math.Min(1f, Math.Max(0f, t - 0.05f) / 0.02f) * (float)Math.Exp(-t * 4.6f);
                float c = 0f;
                for (int k = 0; k < chord.Length; k++)
                    c += (float)Math.Sin(2.0 * Math.PI * chord[k] * t);
                buf[i] = clink + clink2 + (c / chord.Length) * env * 0.34f;
            }
            return buf;
        }

        static float[] SynthConfessionReady()
        {
            return SynthArp(new[] { 523.25f, 659.25f, 783.99f, 1046.50f }, 0.075f, 0.42f);
        }

        // Dramatic Vice Squad Bust: brass slam + radio squelch + crisp two-tone police siren chirp!
        static float[] SynthBadgeFlashBust()
        {
            int n = (int)(SampleRate * 1.35f);
            float[] buf = new float[n];
            var rng = new Random(911);
            float[] hit = { 130.81f, 196.00f, 261.63f, 329.63f, 392.00f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float s = 0f;
                // Opening brass slam (0..0.45s)
                if (t < 0.50f)
                {
                    float env = Math.Min(1f, t / 0.012f) * (float)Math.Exp(-t * 4.2f);
                    float b = 0f;
                    for (int k = 0; k < hit.Length; k++)
                        b += (float)(Math.Sin(2.0 * Math.PI * hit[k] * t) * 0.6 + Math.Sin(2.0 * Math.PI * hit[k] * 2.0 * t) * 0.28);
                    s += (b / hit.Length) * env * 0.65f;
                }
                // Radio squelch burst (0.28..0.44s)
                if (t >= 0.28f && t < 0.44f)
                {
                    float st = t - 0.28f;
                    s += ((float)rng.NextDouble() * 2f - 1f) * (float)Math.Sin(st / 0.16f * Math.PI) * 0.18f;
                }
                // Iconic police cruiser yelp siren sweep (0.45..1.30s)
                if (t >= 0.45f && t < 1.30f)
                {
                    float st = t - 0.45f;
                    float env = Math.Min(1f, st / 0.03f) * Math.Min(1f, (0.85f - st) / 0.12f);
                    float freq = 720f + 460f * (0.5f - 0.5f * (float)Math.Cos(2.0 * Math.PI * 2.35 * st));
                    s += (float)(Math.Sin(2.0 * Math.PI * freq * st) * 0.32 + Math.Sin(2.0 * Math.PI * freq * 2.0 * st) * 0.10) * env;
                }
                buf[i] = Math.Max(-1f, Math.Min(1f, s));
            }
            return buf;
        }

        static float[] SynthCoverBlown()
        {
            int n = (int)(SampleRate * 0.80f);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SampleRate;
                float env = Math.Min(1f, t / 0.015f) * (float)Math.Exp(-t * 3.0f);
                float f = 210f - 95f * (t / 0.80f);
                buf[i] = (float)(Math.Sin(2.0 * Math.PI * f * t) * 0.50 + Math.Sin(2.0 * Math.PI * f * 1.414f * t) * 0.25) * env;
            }
            return buf;
        }

        static float[] SynthShiftVictory()
        {
            int n = (int)(SampleRate * 1.15f);
            float[] buf = new float[n];
            float[] notes = { 261.63f, 329.63f, 392.00f, 493.88f, 523.25f };
            for (int k = 0; k < notes.Length; k++)
            {
                int st = (int)(k * 0.11f * SampleRate);
                int len = (int)((k == notes.Length - 1 ? 0.65f : 0.26f) * SampleRate);
                for (int i = 0; i < len && st + i < n; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Math.Min(1f, t / 0.01f) * (float)Math.Exp(-t * (k == notes.Length - 1 ? 3.2f : 9.0f));
                    buf[st + i] += (float)(Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.36 +
                                           Math.Sin(2.0 * Math.PI * notes[k] * 2.0 * t) * 0.14) * env;
                }
            }
            return buf;
        }

        public static byte[] ExportTimelineWav(
            IReadOnlyList<StingAudioEvent> events,
            double totalDurationSec,
            double leadSec = 0.0,
            float ambienceVolume = 0.16f)
        {
            double dur = Math.Max(1.0, totalDurationSec);
            int totalSamples = (int)(dur * SampleRate);
            float[] mix = new float[totalSamples];

            // Lay down continuous looping speakeasy lounge ambience
            if (ambienceVolume > 0.001f)
            {
                float[] amb = SynthesizeFloat(StingSfxId.LoungeAmbience);
                if (amb.Length > 0)
                {
                    for (int i = 0; i < totalSamples; i++)
                        mix[i] += amb[i % amb.Length] * ambienceVolume;
                }
            }

            var cache = new Dictionary<StingSfxId, float[]>();
            if (events != null)
            {
                for (int e = 0; e < events.Count; e++)
                {
                    var ev = events[e];
                    if (ev == null) continue;
                    double tSec = ev.TimeSec + leadSec;
                    if (tSec < 0 || tSec >= dur) continue;
                    if (!cache.TryGetValue(ev.Id, out var clip))
                    {
                        clip = SynthesizeFloat(ev.Id);
                        cache[ev.Id] = clip;
                    }
                    int start = (int)(tSec * SampleRate);
                    float vol = Math.Max(0f, Math.Min(1f, ev.Volume));
                    for (int i = 0; i < clip.Length && start + i < totalSamples; i++)
                        mix[start + i] += clip[i] * vol;
                }
            }

            // Soft limiter + 16-bit WAV pack
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                int dataBytes = totalSamples * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)1); // Mono
                bw.Write(SampleRate);
                bw.Write(SampleRate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataBytes);
                for (int i = 0; i < totalSamples; i++)
                {
                    float v = (float)Math.Tanh(mix[i]);
                    short s = (short)(Math.Max(-1f, Math.Min(1f, v)) * 32767f);
                    bw.Write(s);
                }
                return ms.ToArray();
            }
        }
    }
}
