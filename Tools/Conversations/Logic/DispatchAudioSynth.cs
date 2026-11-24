using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Conversations
{
    public enum DispatchSfxId
    {
        PrecinctAmbience = 0,
        ArrivalCue = 1,
        PhoneRing = 2,
        PhonePickup = 3,
        WalkInBell = 4,
        WalkInStepA = 5,
        WalkInStepB = 6,
        RadioDispatchPatrol = 7,
        RadioDispatchFire = 8,
        RadioDispatchCommunity = 9,
        HangUpDismiss = 10,
        CalmCallerChime = 11,
        UrgentBeep = 12,
        CorrectChime = 13,
        WrongBuzzer = 14,
        ShiftFanfare = 15
    }

    public sealed class DispatchAudioEvent
    {
        public float TimeSec;
        public DispatchSfxId Cue;
        public float Gain;
    }

    // Pure-C# 16-bit 22.05 kHz PCM synthesizer for Law Enforcement Level 4 ("Desk Sergeant"):
    // 911 Dispatch Triage — retro precinct switchboard rings, lobby door chimes, footsteps,
    // police/fire radio squelch + siren/klaxon cues, handset hang-up, calm chime, & fanfare.
    public static class DispatchAudioSynth
    {
        public const int SampleRate = 22050;

        public static byte[] SynthesizePcm16(DispatchSfxId cue)
        {
            switch (cue)
            {
                case DispatchSfxId.PrecinctAmbience: return MakePrecinctAmbience(4.0f);
                case DispatchSfxId.ArrivalCue: return MakeArrivalCue();
                case DispatchSfxId.PhoneRing: return MakePhoneRing();
                case DispatchSfxId.PhonePickup: return MakePhonePickup();
                case DispatchSfxId.WalkInBell: return MakeWalkInBell();
                case DispatchSfxId.WalkInStepA: return MakeTileStep(0.96f, 301);
                case DispatchSfxId.WalkInStepB: return MakeTileStep(1.07f, 619);
                case DispatchSfxId.RadioDispatchPatrol: return MakeRadioPatrol();
                case DispatchSfxId.RadioDispatchFire: return MakeRadioFire();
                case DispatchSfxId.RadioDispatchCommunity: return MakeRadioCommunity();
                case DispatchSfxId.HangUpDismiss: return MakeHangUpDismiss();
                case DispatchSfxId.CalmCallerChime: return MakeCalmChime();
                case DispatchSfxId.UrgentBeep: return MakeUrgentBeep();
                case DispatchSfxId.CorrectChime: return MakeCorrectChime();
                case DispatchSfxId.WrongBuzzer: return MakeWrongBuzzer();
                case DispatchSfxId.ShiftFanfare: return MakeShiftFanfare();
                default: return MakePhonePickup();
            }
        }

        public static byte[] RenderTimelineToWav(
            IReadOnlyList<DispatchAudioEvent> events,
            float totalDurationSec,
            float ambienceGain = 0.20f)
        {
            int totalSamples = Math.Max(SampleRate, (int)Math.Ceiling(Math.Max(1.0f, totalDurationSec) * SampleRate));
            var mix = new float[totalSamples];

            if (ambienceGain > 0.001f)
            {
                var amb = Pcm16ToFloat(SynthesizePcm16(DispatchSfxId.PrecinctAmbience));
                for (int i = 0; i < totalSamples; i++)
                    mix[i] += amb[i % amb.Length] * ambienceGain;
            }

            if (events != null)
            {
                var cache = new Dictionary<DispatchSfxId, float[]>();
                for (int e = 0; e < events.Count; e++)
                {
                    var ev = events[e];
                    if (ev == null || ev.TimeSec < 0f) continue;
                    if (!cache.TryGetValue(ev.Cue, out var clip))
                    {
                        clip = Pcm16ToFloat(SynthesizePcm16(ev.Cue));
                        cache[ev.Cue] = clip;
                    }
                    int start = (int)Math.Round(ev.TimeSec * SampleRate);
                    float g = ev.Gain > 0f ? ev.Gain : 0.5f;
                    for (int i = 0; i < clip.Length && start + i < totalSamples; i++)
                        if (start + i >= 0) mix[start + i] += clip[i] * g;
                }
            }

            for (int i = 0; i < totalSamples; i++)
                mix[i] = (float)Math.Tanh(mix[i] * 1.10f) * 0.92f;

            return EncodeWavMono16(FloatToPcm16(mix), SampleRate);
        }

        // Warm precinct lobby room tone + soft electrical switchboard hum + gentle distant key clicks
        static byte[] MakePrecinctAmbience(float sec)
        {
            int n = (int)(sec * SampleRate);
            var buf = new float[n];
            var rng = new Random(4041);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp = lp * 0.94f + white * 0.06f;
                float hum = (float)(Math.Sin(2.0 * Math.PI * 60.0 * t) * 0.05 +
                                    Math.Sin(2.0 * Math.PI * 120.0 * t) * 0.025);
                // Soft distant typewriter/keyboard ticks twice per second
                float mod = (t * 2.5f) % 1.0f;
                float tick = mod < 0.012f ? (float)(Math.Sin(2.0 * Math.PI * 1850.0 * mod) * Math.Exp(-mod * 220.0) * 0.06) : 0f;
                // Seamless cross-loop envelope
                float env = 1f;
                if (t < 0.08f) env = t / 0.08f;
                else if (t > sec - 0.08f) env = (sec - t) / 0.08f;
                buf[i] = (lp * 0.32f + hum + tick) * env;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeArrivalCue()
        {
            float[] notes = { 392.0f, 523.25f, 659.25f, 783.99f };
            float dur = 0.62f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                float t0 = k * 0.11f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate - t0;
                    if (t < 0f || t > 0.28f) continue;
                    float env = (float)(Math.Min(1.0, t / 0.012) * Math.Exp(-t * 9.5));
                    buf[i] += (float)(Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.28 +
                                      Math.Sin(4.0 * Math.PI * notes[k] * t) * 0.10) * env;
                }
            }
            return FloatToPcm16(buf);
        }

        // Classic retro desk telephone dual-tone brrring!
        static byte[] MakePhoneRing()
        {
            float dur = 0.56f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                // Two short classic warble bursts
                bool burst = (t < 0.24f) || (t > 0.30f && t < 0.54f);
                if (!burst) continue;
                float warble = 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * 22.0 * t);
                float tone = (float)(Math.Sin(2.0 * Math.PI * 440.0 * t) * 0.35 +
                                     Math.Sin(2.0 * Math.PI * 480.0 * t) * 0.35 +
                                     Math.Sin(2.0 * Math.PI * 1320.0 * t) * 0.18 * warble);
                buf[i] = tone * (0.65f + 0.35f * warble) * 0.55f;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakePhonePickup()
        {
            float dur = 0.12f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            var rng = new Random(192);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = (float)Math.Exp(-t * 48.0);
                float click = (float)(Math.Sin(2.0 * Math.PI * 520.0 * t) * 0.45 +
                                      (rng.NextDouble() * 2.0 - 1.0) * 0.35);
                buf[i] = click * env * 0.7f;
            }
            return FloatToPcm16(buf);
        }

        // Brass precinct lobby entry bell (two warm harmonic dings!)
        static byte[] MakeWalkInBell()
        {
            float dur = 0.55f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float e1 = (float)Math.Exp(-t * 8.5);
                float s1 = (float)(Math.Sin(2.0 * Math.PI * 1174.66 * t) * 0.42 +
                                   Math.Sin(2.0 * Math.PI * 2349.32 * t) * 0.18) * e1;
                float s2 = 0f;
                if (t >= 0.13f)
                {
                    float t2 = t - 0.13f;
                    float e2 = (float)Math.Exp(-t2 * 7.5);
                    s2 = (float)(Math.Sin(2.0 * Math.PI * 1567.98 * t2) * 0.42 +
                                 Math.Sin(2.0 * Math.PI * 3135.96 * t2) * 0.16) * e2;
                }
                buf[i] = (s1 + s2) * 0.65f;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeTileStep(float pitch, int seed)
        {
            float dur = 0.085f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            var rng = new Random(seed);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = (float)Math.Exp(-t * 58.0);
                float thud = (float)Math.Sin(2.0 * Math.PI * (240.0 * pitch) * t) * 0.55f;
                float grit = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
                buf[i] = (thud + grit) * env * 0.65f;
            }
            return FloatToPcm16(buf);
        }

        // Radio squelch + crisp police cruiser siren chirp outside!
        static byte[] MakeRadioPatrol()
        {
            float dur = 0.65f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            var rng = new Random(711);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                if (t < 0.08f)
                    s = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.28f * (1f - t / 0.08f);
                else if (t >= 0.10f && t < 0.58f)
                {
                    float u = (t - 0.10f) / 0.48f;
                    float freq = 640f + 520f * (float)Math.Sin(u * Math.PI);
                    phase += (float)(2.0 * Math.PI * freq / SampleRate);
                    float env = (float)Math.Sin(u * Math.PI);
                    s = (float)(Math.Sin(phase) * 0.40 + Math.Sin(phase * 2f) * 0.14) * env;
                }
                buf[i] = s * 0.72f;
            }
            return FloatToPcm16(buf);
        }

        // Fire station two-tone klaxon bell + air-horn pulse!
        static byte[] MakeRadioFire()
        {
            float dur = 0.70f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float s = 0f;
                if (t < 0.30f)
                {
                    float env = (float)Math.Exp(-t * 4.5);
                    s = (float)(Math.Sin(2.0 * Math.PI * 784.0 * t) * 0.35 +
                                Math.Sin(2.0 * Math.PI * 1046.5 * t) * 0.28) * env;
                }
                if (t >= 0.24f && t < 0.66f)
                {
                    float u = (t - 0.24f) / 0.42f;
                    float env = (float)Math.Sin(u * Math.PI);
                    // Deep brassy fire-truck horn chord
                    s += (float)(Math.Sin(2.0 * Math.PI * 293.66 * t) * 0.30 +
                                 Math.Sin(2.0 * Math.PI * 369.99 * t) * 0.26 +
                                 Math.Sin(2.0 * Math.PI * 587.33 * t) * 0.12) * env;
                }
                buf[i] = s * 0.68f;
            }
            return FloatToPcm16(buf);
        }

        // Warm friendly Community Aid radio dispatch chime
        static byte[] MakeRadioCommunity()
        {
            float dur = 0.45f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f };
            for (int k = 0; k < notes.Length; k++)
            {
                float t0 = k * 0.085f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate - t0;
                    if (t < 0f || t > 0.24f) continue;
                    float env = (float)(Math.Min(1.0, t / 0.01) * Math.Exp(-t * 11.0));
                    buf[i] += (float)Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.34f * env;
                }
            }
            return FloatToPcm16(buf);
        }

        // Crisp retro telephone handset slam-down click + dial-tone cut
        static byte[] MakeHangUpDismiss()
        {
            float dur = 0.20f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            var rng = new Random(911);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float e1 = (float)Math.Exp(-t * 42.0);
                float clack = (float)(Math.Sin(2.0 * Math.PI * 320.0 * t) * 0.55 +
                                      (rng.NextDouble() * 2.0 - 1.0) * 0.35) * e1;
                float clack2 = 0f;
                if (t >= 0.06f)
                {
                    float t2 = t - 0.06f;
                    clack2 = (float)(Math.Sin(2.0 * Math.PI * 240.0 * t2) * 0.48) * (float)Math.Exp(-t2 * 38.0);
                }
                buf[i] = (clack + clack2) * 0.72f;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeCalmChime()
        {
            float dur = 0.52f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            float[] notes = { 440.0f, 554.37f, 659.25f };
            for (int k = 0; k < notes.Length; k++)
            {
                float t0 = k * 0.06f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate - t0;
                    if (t < 0f || t > 0.38f) continue;
                    float env = (float)(Math.Min(1.0, t / 0.03) * Math.Exp(-t * 6.5));
                    buf[i] += (float)Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.28f * env;
                }
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeUrgentBeep()
        {
            float dur = 0.16f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = (float)Math.Sin((t / dur) * Math.PI);
                buf[i] = (float)Math.Sin(2.0 * Math.PI * 980.0 * t) * 0.36f * env;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeCorrectChime()
        {
            float dur = 0.42f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            float[] notes = { 587.33f, 880.0f };
            for (int k = 0; k < notes.Length; k++)
            {
                float t0 = k * 0.095f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate - t0;
                    if (t < 0f || t > 0.28f) continue;
                    float env = (float)(Math.Min(1.0, t / 0.01) * Math.Exp(-t * 10.5));
                    buf[i] += (float)(Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.34 +
                                      Math.Sin(4.0 * Math.PI * notes[k] * t) * 0.12) * env;
                }
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeWrongBuzzer()
        {
            float dur = 0.34f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = (float)(Math.Min(1.0, t / 0.015) * Math.Exp(-t * 6.5));
                buf[i] = (float)(Math.Sin(2.0 * Math.PI * 165.0 * t) * 0.35 +
                                 Math.Sin(2.0 * Math.PI * 220.0 * t) * 0.25) * env;
            }
            return FloatToPcm16(buf);
        }

        static byte[] MakeShiftFanfare()
        {
            float dur = 1.05f;
            int n = (int)(dur * SampleRate);
            var buf = new float[n];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
            float[] times = { 0.00f, 0.14f, 0.28f, 0.44f };
            for (int k = 0; k < notes.Length; k++)
            {
                float t0 = times[k];
                float len = k == notes.Length - 1 ? 0.58f : 0.24f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SampleRate - t0;
                    if (t < 0f || t > len) continue;
                    float env = (float)(Math.Min(1.0, t / 0.015) * Math.Exp(-t * (k == notes.Length - 1 ? 3.5 : 8.0)));
                    buf[i] += (float)(Math.Sin(2.0 * Math.PI * notes[k] * t) * 0.32 +
                                      Math.Sin(4.0 * Math.PI * notes[k] * t) * 0.12) * env;
                }
            }
            return FloatToPcm16(buf);
        }

        static byte[] FloatToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                float v = Math.Max(-1f, Math.Min(1f, samples[i]));
                short s = (short)Math.Round(v * 32767f);
                bytes[i * 2] = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        static float[] Pcm16ToFloat(byte[] pcm)
        {
            int count = pcm.Length / 2;
            var res = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                res[i] = s / 32768f;
            }
            return res;
        }

        static byte[] EncodeWavMono16(byte[] pcm16, int sampleRate)
        {
            using (var ms = new MemoryStream(44 + pcm16.Length))
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + pcm16.Length);
                bw.Write(new[] { 'W', 'A', 'V', 'E' });
                bw.Write(new[] { 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(sampleRate);
                bw.Write(sampleRate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(pcm16.Length);
                bw.Write(pcm16);
                return ms.ToArray();
            }
        }
    }
}
