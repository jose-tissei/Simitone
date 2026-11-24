using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum CadetSfxId
    {
        ArrivalCue,
        Whistle,
        FootstepA,
        FootstepB,
        HurdleHop,
        VaultLaunch,
        VaultLand,
        WallClip,
        RangeShot,
        SteelBullseye,
        SteelGraze,
        SandMiss,
        PerfectSting,
        GoodSting,
        MissBuzzer,
        VictoryFanfareHigh,
        VictoryFanfareStandard,
        ReturnChime
    }

    // Pure-C# procedural 16-bit PCM synthesizer for the Law Enforcement Level 2 Cadet
    // Full-Screen Virtual Pocket Lot ("SimCity Police Academy — Training Grounds").
    // Zero MonoGame/FSO dependencies so every clip is unit-testable and deterministic.
    public static class CadetAudioSynth
    {
        public const int SampleRate = 22050;

        public static short[] BuildSound(CadetSfxId id)
        {
            switch (id)
            {
                case CadetSfxId.ArrivalCue: return BuildArrivalCue();
                case CadetSfxId.Whistle: return BuildPeaWhistle();
                case CadetSfxId.FootstepA: return BuildTrackFootstep(seed: 101, pitchScale: 0.96);
                case CadetSfxId.FootstepB: return BuildTrackFootstep(seed: 202, pitchScale: 1.05);
                case CadetSfxId.HurdleHop: return BuildAirWhoosh(durationSec: 0.24, startHz: 260.0, endHz: 640.0, gain: 0.42);
                case CadetSfxId.VaultLaunch: return BuildAirWhoosh(durationSec: 0.34, startHz: 190.0, endHz: 560.0, gain: 0.52);
                case CadetSfxId.VaultLand: return BuildLandingThud(heavy: true);
                case CadetSfxId.WallClip: return BuildWallClipThud();
                case CadetSfxId.RangeShot: return BuildRangeGunshot();
                case CadetSfxId.SteelBullseye: return BuildSteelTargetPing(bullseye: true);
                case CadetSfxId.SteelGraze: return BuildSteelTargetPing(bullseye: false);
                case CadetSfxId.SandMiss: return BuildSandBermPuff();
                case CadetSfxId.PerfectSting: return BuildPerfectSting();
                case CadetSfxId.GoodSting: return BuildGoodSting();
                case CadetSfxId.MissBuzzer: return BuildMissBuzzer();
                case CadetSfxId.VictoryFanfareHigh: return BuildVictoryFanfare(highGrade: true);
                case CadetSfxId.VictoryFanfareStandard: return BuildVictoryFanfare(highGrade: false);
                case CadetSfxId.ReturnChime: return BuildReturnChime();
                default: return new short[SampleRate / 10];
            }
        }

        // Seamless 8-second outdoor Police Academy grounds atmosphere:
        // gentle morning stadium breeze, faint distant city wash, and two soft sunlit morning birds.
        public static short[] BuildAcademyAmbienceLoop(float durationSec = 8.0f)
        {
            int n = Math.Max(SampleRate, (int)Math.Round(SampleRate * durationSec));
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double Snap(double hz) { return Math.Round(hz * durationSec) / durationSec; }

            double fBreezeLfo = Snap(0.25);
            double fCityHum = Snap(82.0);
            double fCityHarm = Snap(164.0);
            var rng = new Random(4042);

            double lp1 = 0.0, lp2 = 0.0;
            double bpPrev = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;

                // Soft warm outdoor breeze (two-pole low-pass + subtle band shape)
                lp1 = lp1 * 0.92 + white * 0.08;
                lp2 = lp2 * 0.96 + lp1 * 0.04;
                double band = lp1 - lp2;
                bpPrev = bpPrev * 0.85 + band * 0.15;

                double gust = 0.65 + 0.35 * Math.Sin(twoPi * fBreezeLfo * t);
                double wind = (lp2 * 0.14 + bpPrev * 0.22) * gust;

                // Very subtle warm distant city/stadium bed
                double city = (0.018 * Math.Sin(twoPi * fCityHum * t)
                             + 0.008 * Math.Sin(twoPi * fCityHarm * t + 0.6));

                // Subtle sunlit outdoor bird chirp twice inside the 8s loop
                double bird = 0.0;
                double b1 = t - 1.8;
                if (b1 >= 0.0 && b1 < 0.24)
                {
                    double f = 2650.0 + 480.0 * Math.Sin(twoPi * 11.0 * b1);
                    double env = Math.Sin(Math.PI * b1 / 0.24);
                    bird += 0.022 * Math.Sin(twoPi * f * b1) * env * env;
                }
                double b2 = t - 5.4;
                if (b2 >= 0.0 && b2 < 0.18)
                {
                    double f = 3120.0 - 420.0 * (b2 / 0.18);
                    double env = Math.Sin(Math.PI * b2 / 0.18);
                    bird += 0.018 * Math.Sin(twoPi * f * b2) * env;
                }

                buf[i] = ToPcm16(wind + city + bird);
            }

            // Short wrap crossfade at loop seam (120 samples) for guaranteed zero-click looping
            int seam = Math.Min(120, n / 8);
            for (int i = 0; i < seam; i++)
            {
                double u = (double)i / seam;
                int tail = n - seam + i;
                double mixed = buf[i] * u + buf[tail] * (1.0 - u);
                buf[i] = (short)Math.Round(mixed);
            }

            return buf;
        }

        // Crisp police radio squelch chirp + 3-note brassy Academy assembly call (C5 -> E5 -> G5)
        static short[] BuildArrivalCue()
        {
            double dur = 0.72;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(717);
            double[] notes = { 523.25, 659.25, 783.99 };
            double[] starts = { 0.12, 0.26, 0.40 };
            double[] lens = { 0.14, 0.14, 0.30 };

            double hpPrev = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;

                // Brief police radio squelch burst (0.00 .. 0.10s)
                if (t < 0.10)
                {
                    double white = rng.NextDouble() * 2.0 - 1.0;
                    double hp = white - hpPrev * 0.65;
                    hpPrev = white;
                    double env = Math.Min(1.0, t * 140.0) * Math.Exp(-t * 32.0);
                    s += (hp * 0.18 + 0.16 * Math.Sin(twoPi * 1680.0 * t)) * env;
                }

                // Brassy 3-note Academy call
                for (int k = 0; k < 3; k++)
                {
                    double lt = t - starts[k];
                    if (lt >= 0.0 && lt < lens[k])
                    {
                        double f = notes[k];
                        double env = Math.Min(1.0, lt * 95.0) * Math.Exp(-lt * (k == 2 ? 5.2 : 9.0));
                        double brass = 0.36 * Math.Sin(twoPi * f * lt)
                                     + 0.18 * Math.Sin(twoPi * f * 2.0 * lt)
                                     + 0.08 * Math.Sin(twoPi * f * 3.0 * lt);
                        s += brass * env;
                    }
                }

                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 32, 160);
        }

        // Authentic Drill Sergeant referee pea-whistle: dual high tones + 29Hz pea trill + breath air
        static short[] BuildPeaWhistle()
        {
            double dur = 0.44;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(909);
            double ph1 = 0.0, ph2 = 0.0;
            double lpNoise = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double env = Math.Min(1.0, t * 75.0) * Math.Min(1.0, Math.Max(0.0, (dur - t) * 18.0));

                // Classic oscillating pea ball inside the whistle chamber (~29 Hz AM + slight FM)
                double pea = 0.5 + 0.5 * Math.Sin(twoPi * 29.0 * t);
                double pitchBend = (t < 0.04 ? (t / 0.04) * 140.0 : 140.0) - (t > 0.34 ? (t - 0.34) * 900.0 : 0.0);
                double f1 = 2660.0 + pitchBend + pea * 95.0;
                double f2 = 2850.0 + pitchBend + (1.0 - pea) * 80.0;
                ph1 += twoPi * f1 / SampleRate;
                ph2 += twoPi * f2 / SampleRate;

                double am = 0.62 + 0.38 * pea;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lpNoise = lpNoise * 0.55 + white * 0.45;
                double breath = (white - lpNoise) * 0.14;

                double s = ((0.30 * Math.Sin(ph1) + 0.22 * Math.Sin(ph2)) * am + breath) * env;
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 24, 140);
        }

        // Crisp terracotta running-track footstep (low heel thud + gravel scuff transient)
        static short[] BuildTrackFootstep(int seed, double pitchScale)
        {
            double dur = 0.11;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(seed);
            double ph = 0.0;
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double thudHz = (132.0 * pitchScale) * Math.Exp(-t * 24.0) + 48.0;
                ph += twoPi * thudHz / SampleRate;
                double thud = 0.34 * Math.Sin(ph) * Math.Exp(-t * 38.0);

                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.72 + white * 0.28;
                double grit = (white - lp) * 0.28 * Math.Exp(-t * 48.0);

                buf[i] = ToPcm16((thud + grit) * Math.Min(1.0, t * 320.0));
            }
            return ApplyEdgeFade(buf, 12, 80);
        }

        // Athletic jump / hurdle / wall-vault air whoosh
        static short[] BuildAirWhoosh(double durationSec, double startHz, double endHz, double gain)
        {
            int n = (int)(SampleRate * durationSec);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random((int)(startHz * 17));
            double ph = 0.0;
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double u = t / durationSec;
                double env = Math.Sin(Math.PI * u);
                env = Math.Pow(Math.Max(0.0, env), 1.35);

                double f = startHz + (endHz - startHz) * Math.Sin(u * Math.PI * 0.5);
                ph += twoPi * f / SampleRate;

                double white = rng.NextDouble() * 2.0 - 1.0;
                double alpha = 0.08 + 0.22 * u;
                lp = lp * (1.0 - alpha) + white * alpha;

                double s = (lp * 0.65 + 0.22 * Math.Sin(ph)) * env * gain;
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 20, 120);
        }

        // Deep mat / track landing thud
        static short[] BuildLandingThud(bool heavy)
        {
            double dur = heavy ? 0.22 : 0.16;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(heavy ? 313 : 414);
            double ph = 0.0;
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f = (heavy ? 105.0 : 128.0) * Math.Exp(-t * 18.0) + 38.0;
                ph += twoPi * f / SampleRate;
                double body = (heavy ? 0.55 : 0.38) * Math.Sin(ph) * Math.Exp(-t * 19.0);

                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.80 + white * 0.20;
                double scuff = lp * (heavy ? 0.30 : 0.22) * Math.Exp(-t * 26.0);

                buf[i] = ToPcm16((body + scuff) * Math.Min(1.0, t * 280.0));
            }
            return ApplyEdgeFade(buf, 12, 120);
        }

        // Wood/brick wall clip impact when missing the vault
        static short[] BuildWallClipThud()
        {
            double dur = 0.24;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(515);
            double ph = 0.0;
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f = 175.0 * Math.Exp(-t * 22.0) + 64.0;
                ph += twoPi * f / SampleRate;
                double knock = (0.48 * Math.Sin(ph) + 0.18 * Math.Sin(twoPi * 265.0 * t)) * Math.Exp(-t * 21.0);
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.65 + white * 0.35;
                double crack = lp * 0.35 * Math.Exp(-t * 40.0);
                buf[i] = ToPcm16((knock + crack) * Math.Min(1.0, t * 350.0));
            }
            return ApplyEdgeFade(buf, 10, 120);
        }

        // Punchy outdoor range gunshot: sharp supersonic crack + low-end muzzle punch + outdoor echo tail
        static short[] BuildRangeGunshot()
        {
            double dur = 0.42;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(808);
            double ph = 0.0;
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.84 + white * 0.16;

                // Initial tactical crack (0..40ms)
                double crack = white * 0.72 * Math.Exp(-t * 68.0);

                // Low-end muzzle thump
                double f = 190.0 * Math.Exp(-t * 32.0) + 52.0;
                ph += twoPi * f / SampleRate;
                double punch = 0.50 * Math.Sin(ph) * Math.Exp(-t * 28.0);

                // Outdoor shooting range reverb tail
                double tail = lp * 0.34 * Math.Exp(-t * 9.5);

                // Soft saturation for punchy cinematic sound without harsh digital clipping
                double raw = (crack + punch + tail) * Math.Min(1.0, t * 600.0);
                double shaped = Math.Tanh(raw * 1.35) * 0.82;
                buf[i] = ToPcm16(shaped);
            }
            return ApplyEdgeFade(buf, 8, 180);
        }

        // Metallic steel bullseye target ring (inharmonic steel plate partials)
        static short[] BuildSteelTargetPing(bool bullseye)
        {
            double dur = bullseye ? 0.62 : 0.38;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(bullseye ? 999 : 998);

            double f1 = bullseye ? 1185.0 : 890.0;
            double f2 = bullseye ? 1790.0 : 1345.0;
            double f3 = bullseye ? 2910.0 : 2180.0;
            double f4 = bullseye ? 4120.0 : 3110.0;
            double decay = bullseye ? 6.2 : 11.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double impact = (rng.NextDouble() * 2.0 - 1.0) * 0.35 * Math.Exp(-t * 140.0);
                double ring = (0.38 * Math.Sin(twoPi * f1 * t)
                             + 0.26 * Math.Sin(twoPi * f2 * t + 0.5) * Math.Exp(-t * 3.0)
                             + 0.16 * Math.Sin(twoPi * f3 * t + 1.1) * Math.Exp(-t * 6.0)
                             + 0.09 * Math.Sin(twoPi * f4 * t + 1.7) * Math.Exp(-t * 9.5))
                             * Math.Exp(-t * decay);
                double s = (impact + ring) * Math.Min(1.0, t * 400.0);
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 8, 180);
        }

        // Dry sand backstop berm puff on target miss
        static short[] BuildSandBermPuff()
        {
            double dur = 0.24;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            var rng = new Random(606);
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.76 + white * 0.24;
                double env = Math.Min(1.0, t * 240.0) * Math.Exp(-t * 17.0);
                buf[i] = ToPcm16(lp * 0.45 * env);
            }
            return ApplyEdgeFade(buf, 12, 120);
        }

        // Crisp victorious PERFECT two-note power chime (G5 -> C6 + octave shimmer)
        static short[] BuildPerfectSting()
        {
            double dur = 0.44;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.22)
                {
                    double env = Math.Min(1.0, t * 140.0) * Math.Exp(-t * 12.0);
                    s += (0.30 * Math.Sin(twoPi * 783.99 * t) + 0.12 * Math.Sin(twoPi * 1567.98 * t)) * env;
                }
                double t2 = t - 0.095;
                if (t2 >= 0.0)
                {
                    double env = Math.Min(1.0, t2 * 140.0) * Math.Exp(-t2 * 7.8);
                    s += (0.34 * Math.Sin(twoPi * 1046.50 * t2)
                        + 0.16 * Math.Sin(twoPi * 1567.98 * t2)
                        + 0.10 * Math.Sin(twoPi * 2093.00 * t2)) * env;
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 16, 160);
        }

        // Clean encouraging GOOD confirmation chime (E5 -> G5)
        static short[] BuildGoodSting()
        {
            double dur = 0.34;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.18)
                {
                    double env = Math.Min(1.0, t * 120.0) * Math.Exp(-t * 14.0);
                    s += 0.30 * Math.Sin(twoPi * 659.25 * t) * env;
                }
                double t2 = t - 0.09;
                if (t2 >= 0.0)
                {
                    double env = Math.Min(1.0, t2 * 120.0) * Math.Exp(-t2 * 10.0);
                    s += (0.30 * Math.Sin(twoPi * 783.99 * t2) + 0.08 * Math.Sin(twoPi * 1567.98 * t2)) * env;
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 16, 140);
        }

        // Distinct low tactical miss buzzer
        static short[] BuildMissBuzzer()
        {
            double dur = 0.36;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double ph = 0.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f = t < 0.16 ? 196.0 : 155.56;
                ph += twoPi * f / SampleRate;
                double env = Math.Min(1.0, t * 90.0) * Math.Exp(-t * 6.8);
                double saw = Math.Sin(ph) + 0.45 * Math.Sin(ph * 2.0) + 0.22 * Math.Sin(ph * 3.0);
                buf[i] = ToPcm16(saw * 0.22 * env);
            }
            return ApplyEdgeFade(buf, 16, 150);
        }

        // Heroic Police Academy Victory Fanfare: quick snare-tap flourish + bright brassy major cadence
        static short[] BuildVictoryFanfare(bool highGrade)
        {
            double dur = highGrade ? 1.35 : 0.95;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(highGrade ? 2026 : 2025);

            // Snare taps at 0.00, 0.09, 0.17s
            double[] snareTimes = { 0.0, 0.085, 0.165 };
            double[] notes = highGrade
                ? new[] { 523.25, 659.25, 783.99, 1046.50 } // C5 -> E5 -> G5 -> C6!
                : new[] { 440.00, 523.25, 659.25 };         // A4 -> C5 -> E5
            double[] nStarts = highGrade
                ? new[] { 0.20, 0.34, 0.48, 0.64 }
                : new[] { 0.18, 0.34, 0.50 };
            double[] nLens = highGrade
                ? new[] { 0.16, 0.16, 0.18, 0.68 }
                : new[] { 0.16, 0.16, 0.42 };

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;

                // Crisp military snare taps
                for (int k = 0; k < snareTimes.Length; k++)
                {
                    double st = t - snareTimes[k];
                    if (st >= 0.0 && st < 0.07)
                    {
                        double white = rng.NextDouble() * 2.0 - 1.0;
                        double env = Math.Min(1.0, st * 300.0) * Math.Exp(-st * 42.0);
                        s += (white * 0.24 + 0.16 * Math.Sin(twoPi * 210.0 * st)) * env;
                    }
                }

                // Heroic brassy fanfare notes + warm triad chord on final note
                for (int k = 0; k < notes.Length; k++)
                {
                    double lt = t - nStarts[k];
                    if (lt >= 0.0 && lt < nLens[k])
                    {
                        bool isFinal = k == notes.Length - 1;
                        double f = notes[k];
                        double env = Math.Min(1.0, lt * 80.0) * Math.Exp(-lt * (isFinal ? 2.6 : 7.0));
                        double voice = 0.30 * Math.Sin(twoPi * f * lt)
                                     + 0.15 * Math.Sin(twoPi * f * 2.0 * lt)
                                     + 0.07 * Math.Sin(twoPi * f * 3.0 * lt);
                        if (isFinal && highGrade)
                        {
                            // Warm C-major brass chord sustain (C5 + E5 + G5 beneath C6)
                            voice += 0.16 * Math.Sin(twoPi * 523.25 * lt)
                                   + 0.14 * Math.Sin(twoPi * 659.25 * lt)
                                   + 0.14 * Math.Sin(twoPi * 783.99 * lt);
                        }
                        s += voice * env;
                    }
                }

                buf[i] = ToPcm16(Math.Tanh(s * 1.1) * 0.85);
            }
            return ApplyEdgeFade(buf, 20, 260);
        }

        // Soft warm descending return-home chime
        static short[] BuildReturnChime()
        {
            double dur = 0.34;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.18)
                {
                    double env = Math.Min(1.0, t * 110.0) * Math.Exp(-t * 14.0);
                    s += 0.26 * Math.Sin(twoPi * 659.25 * t) * env;
                }
                double t2 = t - 0.10;
                if (t2 >= 0.0)
                {
                    double env = Math.Min(1.0, t2 * 110.0) * Math.Exp(-t2 * 10.5);
                    s += 0.28 * Math.Sin(twoPi * 523.25 * t2) * env;
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 16, 140);
        }

        static short[] ApplyEdgeFade(short[] buf, int fadeInSamples, int fadeOutSamples)
        {
            int n = buf.Length;
            int fi = Math.Min(fadeInSamples, n / 4);
            int fo = Math.Min(fadeOutSamples, n / 3);
            for (int i = 0; i < fi; i++)
                buf[i] = (short)Math.Round(buf[i] * ((double)i / Math.Max(1, fi)));
            for (int i = 0; i < fo; i++)
            {
                int idx = n - 1 - i;
                buf[idx] = (short)Math.Round(buf[idx] * ((double)i / Math.Max(1, fo)));
            }
            return buf;
        }

        static short ToPcm16(double sample)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample)) return 0;
            if (sample > 0.96) sample = 0.96;
            if (sample < -0.96) sample = -0.96;
            return (short)Math.Round(sample * 32760.0);
        }

        // Utility for pure-C# WAV export & test verification (also enables clean video audio muxing!)
        public static byte[] RenderMixedTimelineWav(
            float totalDurationSec,
            bool includeAmbience,
            float ambienceVolume,
            float ambienceStartSec,
            float ambienceEndSec,
            IList<CadetSfxEvent> events)
        {
            int totalSamples = Math.Max(SampleRate / 2, (int)Math.Round(SampleRate * totalDurationSec));
            var mix = new float[totalSamples];

            if (includeAmbience)
            {
                var amb = BuildAcademyAmbienceLoop(8.0f);
                int a0 = Math.Max(0, (int)Math.Round(ambienceStartSec * SampleRate));
                int a1 = Math.Min(totalSamples, (int)Math.Round(ambienceEndSec * SampleRate));
                int fadeLen = SampleRate / 3;
                for (int i = a0; i < a1; i++)
                {
                    float env = 1f;
                    if (i - a0 < fadeLen) env = (float)(i - a0) / fadeLen;
                    if (a1 - i < fadeLen) env = Math.Min(env, (float)(a1 - i) / fadeLen);
                    mix[i] += (amb[(i - a0) % amb.Length] / 32768f) * ambienceVolume * env;
                }
            }

            if (events != null)
            {
                var cache = new Dictionary<CadetSfxId, short[]>();
                for (int e = 0; e < events.Count; e++)
                {
                    var ev = events[e];
                    short[] clip;
                    if (!cache.TryGetValue(ev.Id, out clip))
                    {
                        clip = BuildSound(ev.Id);
                        cache[ev.Id] = clip;
                    }
                    int startIdx = (int)Math.Round(ev.TimeSec * SampleRate);
                    if (startIdx < 0 || startIdx >= totalSamples) continue;
                    double rate = Math.Pow(2.0, ev.Pitch);
                    int outLen = Math.Max(1, (int)Math.Floor(clip.Length / rate));
                    for (int k = 0; k < outLen && startIdx + k < totalSamples; k++)
                    {
                        double srcPos = k * rate;
                        int i0 = (int)srcPos;
                        if (i0 >= clip.Length) break;
                        int i1 = Math.Min(clip.Length - 1, i0 + 1);
                        double frac = srcPos - i0;
                        float s = (float)((clip[i0] * (1.0 - frac) + clip[i1] * frac) / 32768.0);
                        mix[startIdx + k] += s * ev.Volume;
                    }
                }
            }

            var pcm = new short[totalSamples];
            for (int i = 0; i < totalSamples; i++)
                pcm[i] = ToPcm16(Math.Tanh(mix[i] * 1.05));

            return EncodePcm16Wav(pcm, SampleRate);
        }

        public static byte[] EncodePcm16Wav(short[] pcm, int sampleRate)
        {
            int dataBytes = pcm.Length * 2;
            var wav = new byte[44 + dataBytes];
            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + dataBytes);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);
            WriteInt16(wav, 20, 1); // PCM
            WriteInt16(wav, 22, 1); // Mono
            WriteInt32(wav, 24, sampleRate);
            WriteInt32(wav, 28, sampleRate * 2);
            WriteInt16(wav, 32, 2);
            WriteInt16(wav, 34, 16);
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, dataBytes);
            Buffer.BlockCopy(pcm, 0, wav, 44, dataBytes);
            return wav;
        }

        static void WriteAscii(byte[] dst, int off, string s)
        {
            for (int i = 0; i < s.Length; i++) dst[off + i] = (byte)s[i];
        }

        static void WriteInt16(byte[] dst, int off, short v)
        {
            dst[off] = (byte)(v & 0xFF);
            dst[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        static void WriteInt32(byte[] dst, int off, int v)
        {
            dst[off] = (byte)(v & 0xFF);
            dst[off + 1] = (byte)((v >> 8) & 0xFF);
            dst[off + 2] = (byte)((v >> 16) & 0xFF);
            dst[off + 3] = (byte)((v >> 24) & 0xFF);
        }
    }

    public struct CadetSfxEvent
    {
        public double TimeSec;
        public CadetSfxId Id;
        public float Volume;
        public float Pitch;

        public CadetSfxEvent(double timeSec, CadetSfxId id, float volume, float pitch = 0f)
        {
            TimeSec = timeSec;
            Id = id;
            Volume = volume;
            Pitch = pitch;
        }
    }
}
