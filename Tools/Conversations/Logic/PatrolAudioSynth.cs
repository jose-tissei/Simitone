using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    public enum PatrolSfxId
    {
        CruiserArriveSiren,
        CarDoorOpen,
        CarDoorClose,
        RadioSquelch,
        SneakStepA,
        SneakStepB,
        PavementStepA,
        PavementStepB,
        HandcuffClick,
        TicketRip,
        FlashlightClick,
        BurglarSpotSting,
        PerfectCallSting,
        GoodCallSting,
        WrongCallBuzzer,
        ShiftCompleteFanfare,
        ReturnHomeChime
    }

    public struct PatrolSfxEvent
    {
        public double TimeSec;
        public PatrolSfxId Id;
        public float Volume;
        public float Pitch;

        public PatrolSfxEvent(double timeSec, PatrolSfxId id, float volume, float pitch = 0f)
        {
            TimeSec = timeSec;
            Id = id;
            Volume = volume;
            Pitch = pitch;
        }
    }

    // Pure-C# procedural 16-bit PCM synthesizer for Law Enforcement Level 3 ("Patrol Officer"):
    // Real-Lot Live Beat Patrol (DISP-06). Zero MonoGame/FSO dependencies.
    public static class PatrolAudioSynth
    {
        public const int SampleRate = 22050;

        public static short[] BuildSound(PatrolSfxId id)
        {
            switch (id)
            {
                case PatrolSfxId.CruiserArriveSiren: return BuildCruiserChirp();
                case PatrolSfxId.CarDoorOpen: return BuildCarDoor(opening: true);
                case PatrolSfxId.CarDoorClose: return BuildCarDoor(opening: false);
                case PatrolSfxId.RadioSquelch: return BuildRadioSquelch();
                case PatrolSfxId.SneakStepA: return BuildSneakStep(seed: 311, pitchScale: 0.94);
                case PatrolSfxId.SneakStepB: return BuildSneakStep(seed: 317, pitchScale: 1.07);
                case PatrolSfxId.PavementStepA: return BuildPavementStep(seed: 401, pitchScale: 0.96);
                case PatrolSfxId.PavementStepB: return BuildPavementStep(seed: 409, pitchScale: 1.05);
                case PatrolSfxId.HandcuffClick: return BuildHandcuffClick();
                case PatrolSfxId.TicketRip: return BuildTicketRip();
                case PatrolSfxId.FlashlightClick: return BuildFlashlightClick();
                case PatrolSfxId.BurglarSpotSting: return BuildBurglarSpotSting();
                case PatrolSfxId.PerfectCallSting: return BuildToneChime(783.99, 1046.50, 1567.98, 0.42);
                case PatrolSfxId.GoodCallSting: return BuildToneChime(659.25, 783.99, 0.0, 0.34);
                case PatrolSfxId.WrongCallBuzzer: return BuildWrongBuzzer();
                case PatrolSfxId.ShiftCompleteFanfare: return BuildShiftFanfare();
                case PatrolSfxId.ReturnHomeChime: return BuildToneChime(659.25, 523.25, 0.0, 0.34);
                default: return new short[SampleRate / 10];
            }
        }

        // Seamless 8-second dusk SimCity neighborhood street ambience:
        // distant evening city hum, gentle breeze, and warm evening crickets.
        public static short[] BuildCityDuskAmbienceLoop(float durationSec = 8.0f)
        {
            int n = Math.Max(SampleRate, (int)Math.Round(SampleRate * durationSec));
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double Snap(double hz) { return Math.Round(hz * durationSec) / durationSec; }

            double fCity1 = Snap(64.0);
            double fCity2 = Snap(128.0);
            double fCricketMod = Snap(3.5);
            double fCricketCar = Snap(4150.0);
            var rng = new Random(7301);
            double lp1 = 0.0, lp2 = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp1 = lp1 * 0.93 + white * 0.07;
                lp2 = lp2 * 0.97 + lp1 * 0.03;

                double streetWash = lp2 * 0.16;
                double cityBed = 0.016 * Math.Sin(twoPi * fCity1 * t) + 0.008 * Math.Sin(twoPi * fCity2 * t + 0.5);

                // Soft dusk cricket trill pulsing gently twice in the 8s loop
                double cGate = Math.Max(0.0, Math.Sin(twoPi * Snap(0.25) * t));
                double cPulse = Math.Max(0.0, Math.Sin(twoPi * fCricketMod * t));
                double cricket = 0.016 * cGate * cPulse * Math.Sin(twoPi * fCricketCar * t);

                buf[i] = ToPcm16(streetWash + cityBed + cricket);
            }

            int seam = Math.Min(120, n / 8);
            for (int i = 0; i < seam; i++)
            {
                double u = (double)i / seam;
                int tailIdx = n - seam + i;
                buf[i] = (short)Math.Round(buf[tailIdx] * (1.0 - u) + buf[i] * u);
            }
            return buf;
        }

        // Authentic two-blip police cruiser yelp/chirp on curb arrival
        static short[] BuildCruiserChirp()
        {
            double dur = 0.56;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double ph = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.22)
                {
                    double u = t / 0.22;
                    double f = 680.0 + 540.0 * Math.Sin(Math.PI * u);
                    ph += twoPi * f / SampleRate;
                    double env = Math.Sin(Math.PI * u);
                    s = (0.34 * Math.Sin(ph) + 0.12 * Math.Sin(ph * 2.0)) * env;
                }
                else if (t >= 0.28 && t < 0.50)
                {
                    double u = (t - 0.28) / 0.22;
                    double f = 720.0 + 560.0 * Math.Sin(Math.PI * u);
                    ph += twoPi * f / SampleRate;
                    double env = Math.Sin(Math.PI * u);
                    s = (0.36 * Math.Sin(ph) + 0.12 * Math.Sin(ph * 2.0)) * env;
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 16, 160);
        }

        // Heavy squad-car door latch + thump
        static short[] BuildCarDoor(bool opening)
        {
            double dur = 0.26;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(opening ? 811 : 821);
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.80 + white * 0.20;
                double s = 0.0;

                if (opening)
                {
                    // Latch click at 0.01s + metallic swing creak
                    double click = t < 0.035 ? (white * 0.30 + 0.22 * Math.Sin(twoPi * 1650.0 * t)) * Math.Exp(-t * 95.0) : 0.0;
                    double body = 0.22 * Math.Sin(twoPi * (145.0 - 40.0 * t) * t) * Math.Exp(-t * 16.0);
                    s = click + body + lp * 0.18 * Math.Exp(-t * 14.0);
                }
                else
                {
                    // Deep solid car door slam thud
                    double thud = 0.48 * Math.Sin(twoPi * (112.0 * Math.Exp(-t * 8.0)) * t) * Math.Exp(-t * 20.0);
                    double latch = 0.22 * Math.Sin(twoPi * 920.0 * t) * Math.Exp(-t * 55.0);
                    s = thud + latch + lp * 0.28 * Math.Exp(-t * 24.0);
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 10, 120);
        }

        // Police shoulder-mic radio squelch + short chirp
        static short[] BuildRadioSquelch()
        {
            double dur = 0.24;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(907);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                double hiss = white * 0.20 * Math.Sin(Math.PI * Math.Min(1.0, t / 0.14)) * (t < 0.14 ? 1.0 : 0.0);
                double beep = (t >= 0.09 && t < 0.21)
                    ? 0.28 * Math.Sin(twoPi * 1480.0 * t) * Math.Exp(-(t - 0.09) * 18.0)
                    : 0.0;
                buf[i] = ToPcm16(hiss + beep);
            }
            return ApplyEdgeFade(buf, 12, 120);
        }

        // Soft muffled rubber-sole tiptoe footstep while sneaking behind Burglar!
        static short[] BuildSneakStep(int seed, double pitchScale)
        {
            double dur = 0.11;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(seed);
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.86 + white * 0.14;
                double env = Math.Min(1.0, t * 220.0) * Math.Exp(-t * 36.0);
                double thump = 0.24 * Math.Sin(twoPi * (88.0 * pitchScale) * t) * Math.Exp(-t * 44.0);
                buf[i] = ToPcm16((lp * 0.26 + thump) * env);
            }
            return ApplyEdgeFade(buf, 8, 80);
        }

        // Crisp leather patrol boot step on sidewalk pavement
        static short[] BuildPavementStep(int seed, double pitchScale)
        {
            double dur = 0.12;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(seed);
            double lp = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                lp = lp * 0.72 + white * 0.28;
                double env = Math.Min(1.0, t * 280.0) * Math.Exp(-t * 34.0);
                double heel = 0.34 * Math.Sin(twoPi * (142.0 * pitchScale) * t) * Math.Exp(-t * 40.0);
                buf[i] = ToPcm16((lp * 0.34 + heel) * env);
            }
            return ApplyEdgeFade(buf, 8, 90);
        }

        // Crisp double metallic ratchet click of handcuffs snapping shut!
        static short[] BuildHandcuffClick()
        {
            double dur = 0.30;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(1013);
            double[] clicks = { 0.015, 0.075, 0.145 };

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                for (int k = 0; k < clicks.Length; k++)
                {
                    double lt = t - clicks[k];
                    if (lt >= 0.0 && lt < 0.085)
                    {
                        double env = Math.Min(1.0, lt * 600.0) * Math.Exp(-lt * 52.0);
                        double metal = 0.30 * Math.Sin(twoPi * (2420.0 + k * 210.0) * lt)
                                     + 0.22 * Math.Sin(twoPi * 4180.0 * lt)
                                     + (rng.NextDouble() * 2.0 - 1.0) * 0.16;
                        s += metal * env;
                    }
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 8, 120);
        }

        // Crisp paper citation tear-off sound
        static short[] BuildTicketRip()
        {
            double dur = 0.26;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            var rng = new Random(1109);
            double prev = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double white = rng.NextDouble() * 2.0 - 1.0;
                double hp = white - prev * 0.72;
                prev = white;
                double env = Math.Sin(Math.PI * Math.Min(1.0, t / dur)) * (0.65 + 0.35 * Math.Sin(t * 95.0));
                buf[i] = ToPcm16(hp * 0.36 * env);
            }
            return ApplyEdgeFade(buf, 12, 120);
        }

        // Tactile heavy mag-lite flashlight switch click
        static short[] BuildFlashlightClick()
        {
            double dur = 0.14;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            var rng = new Random(1217);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.04)
                    s += (0.34 * Math.Sin(twoPi * 2100.0 * t) + (rng.NextDouble() * 2.0 - 1.0) * 0.22) * Math.Exp(-t * 110.0);
                double t2 = t - 0.045;
                if (t2 >= 0.0 && t2 < 0.06)
                    s += (0.38 * Math.Sin(twoPi * 2850.0 * t2) + 0.18 * Math.Sin(twoPi * 1240.0 * t2)) * Math.Exp(-t2 * 90.0);
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 6, 80);
        }

        // Classic sneaky low suspense pizzicato/bass sting when sneaking toward Burglar
        static short[] BuildBurglarSpotSting()
        {
            double dur = 0.42;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double[] freqs = { 220.0, 207.65, 196.0 };
            double[] starts = { 0.0, 0.11, 0.22 };

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                for (int k = 0; k < freqs.Length; k++)
                {
                    double lt = t - starts[k];
                    if (lt >= 0.0 && lt < 0.16)
                    {
                        double env = Math.Min(1.0, lt * 180.0) * Math.Exp(-lt * 18.0);
                        s += (0.34 * Math.Sin(twoPi * freqs[k] * lt) + 0.14 * Math.Sin(twoPi * freqs[k] * 2.0 * lt)) * env;
                    }
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 12, 140);
        }

        static short[] BuildToneChime(double f1, double f2, double f3, double dur)
        {
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                if (t < 0.20)
                {
                    double env = Math.Min(1.0, t * 140.0) * Math.Exp(-t * 13.0);
                    s += 0.30 * Math.Sin(twoPi * f1 * t) * env;
                }
                double t2 = t - 0.095;
                if (t2 >= 0.0)
                {
                    double env = Math.Min(1.0, t2 * 140.0) * Math.Exp(-t2 * 8.5);
                    s += 0.34 * Math.Sin(twoPi * f2 * t2) * env;
                    if (f3 > 0.0)
                        s += 0.14 * Math.Sin(twoPi * f3 * t2) * env;
                }
                buf[i] = ToPcm16(s);
            }
            return ApplyEdgeFade(buf, 16, 150);
        }

        static short[] BuildWrongBuzzer()
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
                buf[i] = ToPcm16(saw * 0.24 * env);
            }
            return ApplyEdgeFade(buf, 16, 150);
        }

        static short[] BuildShiftFanfare()
        {
            double dur = 1.15;
            int n = (int)(SampleRate * dur);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };
            double[] starts = { 0.02, 0.16, 0.30, 0.46 };
            double[] lens = { 0.16, 0.16, 0.18, 0.66 };

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double s = 0.0;
                for (int k = 0; k < notes.Length; k++)
                {
                    double lt = t - starts[k];
                    if (lt >= 0.0 && lt < lens[k])
                    {
                        bool isFinal = k == notes.Length - 1;
                        double env = Math.Min(1.0, lt * 90.0) * Math.Exp(-lt * (isFinal ? 2.8 : 7.0));
                        double voice = 0.30 * Math.Sin(twoPi * notes[k] * lt)
                                     + 0.14 * Math.Sin(twoPi * notes[k] * 2.0 * lt);
                        if (isFinal)
                        {
                            voice += 0.14 * Math.Sin(twoPi * 523.25 * lt)
                                   + 0.12 * Math.Sin(twoPi * 659.25 * lt)
                                   + 0.12 * Math.Sin(twoPi * 783.99 * lt);
                        }
                        s += voice * env;
                    }
                }
                buf[i] = ToPcm16(Math.Tanh(s * 1.1) * 0.85);
            }
            return ApplyEdgeFade(buf, 18, 220);
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

        public static byte[] RenderMixedTimelineWav(
            float totalDurationSec,
            bool includeAmbience,
            float ambienceVolume,
            float ambienceStartSec,
            float ambienceEndSec,
            IList<PatrolSfxEvent> events)
        {
            int totalSamples = Math.Max(SampleRate / 2, (int)Math.Round(SampleRate * totalDurationSec));
            var mix = new float[totalSamples];

            if (includeAmbience)
            {
                var amb = BuildCityDuskAmbienceLoop(8.0f);
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
                var cache = new Dictionary<PatrolSfxId, short[]>();
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

            return CadetAudioSynth.EncodePcm16Wav(pcm, SampleRate);
        }
    }
}
