using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Conversations
{
    public enum DetectiveSfxId
    {
        CrimeSceneAmbience = 0,
        ClueCameraFlash = 1,
        ClueNotebookScribble = 2,
        ClueFoundChime = 3,
        SuspectQuestionCue = 4,
        ContradictionPing = 5,
        ForensicsEliminate = 6,
        CaseBoardOpen = 7,
        AccuseDramaticHit = 8,
        CulpritCaughtBust = 9,
        WrongAccusationSting = 10,
        ShiftVictoryFanfare = 11,
    }

    public sealed class DetectiveAudioEvent
    {
        public double TimeSec;
        public DetectiveSfxId Id;
        public float Volume;
    }

    // Pure-C# procedural noir-mystery & crime-scene forensics audio synthesizer (22050 Hz, 16-bit mono PCM).
    public static class DetectiveAudioSynth
    {
        public const int SampleRate = 22050;

        public static byte[] SynthesizePcm16(DetectiveSfxId id)
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

        public static float[] SynthesizeFloat(DetectiveSfxId id)
        {
            switch (id)
            {
                case DetectiveSfxId.CrimeSceneAmbience: return MakeCrimeSceneAmbience();
                case DetectiveSfxId.ClueCameraFlash: return MakeClueCameraFlash();
                case DetectiveSfxId.ClueNotebookScribble: return MakeClueNotebookScribble();
                case DetectiveSfxId.ClueFoundChime: return MakeClueFoundChime();
                case DetectiveSfxId.SuspectQuestionCue: return MakeSuspectQuestionCue();
                case DetectiveSfxId.ContradictionPing: return MakeContradictionPing();
                case DetectiveSfxId.ForensicsEliminate: return MakeForensicsEliminate();
                case DetectiveSfxId.CaseBoardOpen: return MakeCaseBoardOpen();
                case DetectiveSfxId.AccuseDramaticHit: return MakeAccuseDramaticHit();
                case DetectiveSfxId.CulpritCaughtBust: return MakeCulpritCaughtBust();
                case DetectiveSfxId.WrongAccusationSting: return MakeWrongAccusationSting();
                case DetectiveSfxId.ShiftVictoryFanfare: return MakeShiftVictoryFanfare();
                default: return new float[SampleRate / 10];
            }
        }

        // Seamless 4.0s noir rainy parlor ambience + soft grandfather clock ticks + warm minor vibraphone pad
        static float[] MakeCrimeSceneAmbience()
        {
            int n = SampleRate * 4;
            float[] buf = new float[n];
            uint seed = 0xD37EC71Fu;
            float rainLp = 0f;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                // Gentle parlor window rain hiss
                float noise = NextNoise(ref seed);
                rainLp = rainLp * 0.88f + noise * 0.12f;
                float s = rainLp * 0.09f;

                // Soft grandfather-clock pendulum ticks at 0.0, 1.0, 2.0, 3.0s
                double tickPhase = t % 1.0;
                if (tickPhase < 0.025)
                {
                    float env = (float)Math.Exp(-tickPhase * 180.0);
                    double freq = ((int)t % 2 == 0) ? 1420.0 : 1180.0;
                    s += (float)Math.Sin(2.0 * Math.PI * freq * tickPhase) * env * 0.10f;
                }

                // Subtle D-minor noir vibraphone chord pad (seamless 4s loop)
                double trem = 0.72 + 0.28 * Math.Sin(2.0 * Math.PI * 1.5 * t);
                double pad = Math.Sin(2.0 * Math.PI * 146.83 * t) * 0.045
                           + Math.Sin(2.0 * Math.PI * 220.00 * t) * 0.032
                           + Math.Sin(2.0 * Math.PI * 261.63 * t) * 0.024
                           + Math.Sin(2.0 * Math.PI * 349.23 * t) * 0.018;
                s += (float)(pad * trem);

                buf[i] = s;
            }
            return buf;
        }

        // Crisp forensic camera shutter click + warm flash capacitor whine
        static float[] MakeClueCameraFlash()
        {
            int n = (int)(SampleRate * 0.42);
            float[] buf = new float[n];
            uint seed = 0xCA03FA11u;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                if (t < 0.045)
                {
                    float env = (float)Math.Exp(-t * 110.0);
                    s += NextNoise(ref seed) * env * 0.55f;
                    s += (float)Math.Sin(2.0 * Math.PI * 2400.0 * t) * env * 0.25f;
                }
                if (t >= 0.035)
                {
                    double dt = t - 0.035;
                    double f = 1800.0 + dt * 2600.0;
                    float env = (float)(Math.Min(1.0, dt * 35.0) * Math.Exp(-dt * 8.5));
                    s += (float)Math.Sin(2.0 * Math.PI * f * dt) * env * 0.16f;
                }
                buf[i] = s;
            }
            return buf;
        }

        // Crisp pencil-on-notepad double stroke
        static float[] MakeClueNotebookScribble()
        {
            int n = (int)(SampleRate * 0.36);
            float[] buf = new float[n];
            uint seed = 0x5C81BB1Eu;
            float bp = 0f;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float raw = NextNoise(ref seed);
                bp = bp * 0.72f + raw * 0.28f;
                float hp = raw - bp;
                double stroke = (t < 0.14) ? Math.Sin(t / 0.14 * Math.PI)
                              : (t >= 0.18 && t < 0.33) ? Math.Sin((t - 0.18) / 0.15 * Math.PI) : 0.0;
                buf[i] = hp * (float)stroke * 0.35f;
            }
            return buf;
        }

        // Warm detective "clue discovered!" 3-note vibraphone arpeggio (D5 -> F5 -> A5)
        static float[] MakeClueFoundChime()
        {
            int n = (int)(SampleRate * 0.65);
            float[] buf = new float[n];
            double[] notes = { 587.33, 698.46, 880.00 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    double st = k * 0.095;
                    if (t < st) continue;
                    double dt = t - st;
                    float env = (float)(Math.Min(1.0, dt * 90.0) * Math.Exp(-dt * 5.8));
                    s += (float)(Math.Sin(2.0 * Math.PI * notes[k] * dt) * 0.26
                               + Math.Sin(2.0 * Math.PI * notes[k] * 2.0 * dt) * 0.08) * env;
                }
                buf[i] = s;
            }
            return buf;
        }

        // Low noir upright-bass interrogation pluck (G2 -> D3)
        static float[] MakeSuspectQuestionCue()
        {
            int n = (int)(SampleRate * 0.45);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                if (t < 0.22)
                {
                    float env = (float)Math.Exp(-t * 11.0);
                    s += (float)(Math.Sin(2.0 * Math.PI * 98.0 * t) * 0.42 + Math.Sin(2.0 * Math.PI * 196.0 * t) * 0.16) * env;
                }
                if (t >= 0.15)
                {
                    double dt = t - 0.15;
                    float env = (float)Math.Exp(-dt * 9.5);
                    s += (float)(Math.Sin(2.0 * Math.PI * 146.83 * dt) * 0.36 + Math.Sin(2.0 * Math.PI * 293.66 * dt) * 0.12) * env;
                }
                buf[i] = s;
            }
            return buf;
        }

        // Sharp Logic-detected contradiction "AHA!" bell-chord stinger
        static float[] MakeContradictionPing()
        {
            int n = (int)(SampleRate * 0.55);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float env = (float)(Math.Min(1.0, t * 140.0) * Math.Exp(-t * 6.5));
                double s = Math.Sin(2.0 * Math.PI * 880.0 * t) * 0.28
                         + Math.Sin(2.0 * Math.PI * 1244.51 * t) * 0.22 // tritone tension
                         + Math.Sin(2.0 * Math.PI * 1760.0 * t) * 0.12;
                buf[i] = (float)s * env;
            }
            return buf;
        }

        // Forensics lab scan sweep + clean verified stamp
        static float[] MakeForensicsEliminate()
        {
            int n = (int)(SampleRate * 0.62);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                if (t < 0.30)
                {
                    double f = 440.0 + (t / 0.30) * 880.0;
                    float env = (float)Math.Sin(t / 0.30 * Math.PI);
                    s += (float)Math.Sin(2.0 * Math.PI * f * t) * env * 0.22f;
                }
                if (t >= 0.28)
                {
                    double dt = t - 0.28;
                    float env = (float)Math.Exp(-dt * 7.0);
                    s += (float)(Math.Sin(2.0 * Math.PI * 523.25 * dt) * 0.25
                               + Math.Sin(2.0 * Math.PI * 659.25 * dt) * 0.22
                               + Math.Sin(2.0 * Math.PI * 783.99 * dt) * 0.20) * env;
                }
                buf[i] = s;
            }
            return buf;
        }

        // Corkboard pin & dossier rustle
        static float[] MakeCaseBoardOpen()
        {
            int n = (int)(SampleRate * 0.22);
            float[] buf = new float[n];
            uint seed = 0xB0A8D123u;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float env = (float)Math.Exp(-t * 22.0);
                float thud = (float)Math.Sin(2.0 * Math.PI * 185.0 * t) * (float)Math.Exp(-t * 38.0) * 0.42f;
                float rustle = NextNoise(ref seed) * env * 0.20f;
                buf[i] = thud + rustle;
            }
            return buf;
        }

        // Dramatic courtroom / detective pointing brass-and-timpani hit!
        static float[] MakeAccuseDramaticHit()
        {
            int n = (int)(SampleRate * 0.85);
            float[] buf = new float[n];
            double[] chord = { 146.83, 220.00, 293.66, 349.23, 440.00 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float env = (float)(Math.Min(1.0, t * 120.0) * Math.Exp(-t * 3.8));
                float s = 0f;
                foreach (double f in chord)
                    s += (float)(Math.Sin(2.0 * Math.PI * f * t) + 0.35 * Math.Sin(4.0 * Math.PI * f * t)) * 0.11f;
                // Low timpani punch
                double timpF = 92.0 - t * 28.0;
                s += (float)Math.Sin(2.0 * Math.PI * Math.Max(48.0, timpF) * t) * (float)Math.Exp(-t * 9.0) * 0.36f;
                buf[i] = s * env;
            }
            return buf;
        }

        // Handcuff metallic clicks + triumphant case-cracked major chord!
        static float[] MakeCulpritCaughtBust()
        {
            int n = (int)(SampleRate * 1.15);
            float[] buf = new float[n];
            uint seed = 0xC0FF5123u;
            double[] chord = { 293.66, 369.99, 440.00, 587.33 }; // D Major triumph
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                // Twin handcuff ratchet clicks at 0.04s & 0.14s
                if ((t >= 0.04 && t < 0.08) || (t >= 0.14 && t < 0.18))
                {
                    double dt = t < 0.1 ? t - 0.04 : t - 0.14;
                    float env = (float)Math.Exp(-dt * 140.0);
                    s += (float)Math.Sin(2.0 * Math.PI * 3600.0 * dt) * env * 0.34f + NextNoise(ref seed) * env * 0.22f;
                }
                if (t >= 0.20)
                {
                    double dt = t - 0.20;
                    float env = (float)(Math.Min(1.0, dt * 45.0) * Math.Exp(-dt * 2.6));
                    foreach (double f in chord)
                        s += (float)(Math.Sin(2.0 * Math.PI * f * dt) + 0.25 * Math.Sin(4.0 * Math.PI * f * dt)) * 0.16f * env;
                }
                buf[i] = s;
            }
            return buf;
        }

        // Descending dissonant brass groan on false accusation
        static float[] MakeWrongAccusationSting()
        {
            int n = (int)(SampleRate * 0.78);
            float[] buf = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f1 = 233.08 - t * 55.0;
                double f2 = 220.00 - t * 60.0;
                float env = (float)(Math.Min(1.0, t * 40.0) * Math.Exp(-t * 3.2));
                buf[i] = (float)(Math.Sin(2.0 * Math.PI * f1 * t) * 0.28 + Math.Sin(2.0 * Math.PI * f2 * t) * 0.28) * env;
            }
            return buf;
        }

        // Classic noir-jazz victory cadence (D4 -> F#4 -> A4 -> D5)
        static float[] MakeShiftVictoryFanfare()
        {
            int n = (int)(SampleRate * 1.45);
            float[] buf = new float[n];
            double[] notes = { 293.66, 369.99, 440.00, 587.33 };
            double[] starts = { 0.00, 0.16, 0.32, 0.50 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                float s = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    if (t < starts[k]) continue;
                    double dt = t - starts[k];
                    double decay = (k == 3) ? 2.1 : 5.5;
                    float env = (float)(Math.Min(1.0, dt * 65.0) * Math.Exp(-dt * decay));
                    s += (float)(Math.Sin(2.0 * Math.PI * notes[k] * dt) * 0.22
                               + Math.Sin(2.0 * Math.PI * notes[k] * 2.0 * dt) * 0.07) * env;
                }
                buf[i] = s;
            }
            return buf;
        }

        static float NextNoise(ref uint seed)
        {
            seed = seed * 1664525u + 1013904223u;
            return ((seed >> 8) / 8388608f) - 1f;
        }

        public static void WriteMixedTrackWav(string path, double totalDurationSec, IReadOnlyList<DetectiveAudioEvent> events)
        {
            int totalSamples = Math.Max(SampleRate, (int)Math.Ceiling(totalDurationSec * SampleRate));
            float[] mix = new float[totalSamples];

            float[] amb = SynthesizeFloat(DetectiveSfxId.CrimeSceneAmbience);
            for (int i = 0; i < totalSamples; i++)
                mix[i] += amb[i % amb.Length] * 0.60f;

            var cache = new Dictionary<DetectiveSfxId, float[]>();
            if (events != null)
            {
                foreach (var ev in events)
                {
                    if (!cache.TryGetValue(ev.Id, out float[] clip))
                    {
                        clip = SynthesizeFloat(ev.Id);
                        cache[ev.Id] = clip;
                    }
                    int start = Math.Max(0, (int)Math.Round(ev.TimeSec * SampleRate));
                    for (int j = 0; j < clip.Length && start + j < totalSamples; j++)
                        mix[start + j] += clip[j] * ev.Volume;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                int dataBytes = totalSamples * 2;
                bw.Write(new[] { 'R', 'I', 'F', 'F' });
                bw.Write(36 + dataBytes);
                bw.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                bw.Write(16);
                bw.Write((short)1);
                bw.Write((short)1);
                bw.Write(SampleRate);
                bw.Write(SampleRate * 2);
                bw.Write((short)2);
                bw.Write((short)16);
                bw.Write(new[] { 'd', 'a', 't', 'a' });
                bw.Write(dataBytes);
                for (int i = 0; i < totalSamples; i++)
                {
                    float v = Math.Max(-0.98f, Math.Min(0.98f, mix[i]));
                    bw.Write((short)(v * 32767f));
                }
            }
        }
    }
}
