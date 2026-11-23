using System;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Conversations
{
    // Procedural MonoGame audio synthesizer for the CCTV Night Watch security booth.
    // Generates an eerie, seamless 8-second late-night CRT hum + dark ambient drone loop
    // plus crisp console SFX (motion ping, alarm siren, all-clear chirp, coffee sip, chimes)
    // directly as in-memory 16-bit PCM SoundEffects with zero external file dependencies.
    internal sealed class CctvAudioEngine : IDisposable
    {
        const int SampleRate = 22050;

        bool initialized;
        bool initFailed;
        SoundEffect ambientSfx;
        SoundEffectInstance ambientInst;
        SoundEffect motionAlertSfx;
        SoundEffect alarmSfx;
        SoundEffect allClearSfx;
        SoundEffect coffeeSfx;
        SoundEffect goodFeedbackSfx;
        SoundEffect badFeedbackSfx;
        SoundEffect shiftCompleteSfx;

        public bool IsReady { get { return initialized && !initFailed; } }
        public bool IsAmbientPlaying
        {
            get
            {
                try { return ambientInst != null && !ambientInst.IsDisposed && ambientInst.State == SoundState.Playing; }
                catch { return false; }
            }
        }

        public void StartAmbient()
        {
            if (!EnsureInitialized()) return;
            try
            {
                if (ambientInst == null || ambientInst.IsDisposed)
                {
                    ambientInst = ambientSfx.CreateInstance();
                    ambientInst.IsLooped = true;
                    ambientInst.Volume = 0.28f;
                }
                if (ambientInst.State != SoundState.Playing)
                    ambientInst.Play();
            }
            catch (Exception e) { Boot.Log("cctv audio start ambient error: " + e.Message); }
        }

        public void StopAmbient()
        {
            try
            {
                if (ambientInst != null && !ambientInst.IsDisposed && ambientInst.State != SoundState.Stopped)
                    ambientInst.Stop();
            }
            catch { }
        }

        public void PlayMotionAlert() { PlayOneShot(motionAlertSfx, 0.34f); }
        public void PlayAlarmSiren() { PlayOneShot(alarmSfx, 0.36f); }
        public void PlayAllClear() { PlayOneShot(allClearSfx, 0.30f); }
        public void PlayCoffeeSip() { PlayOneShot(coffeeSfx, 0.28f); }
        public void PlayFeedback(bool positive) { PlayOneShot(positive ? goodFeedbackSfx : badFeedbackSfx, 0.28f); }
        public void PlayShiftComplete() { PlayOneShot(shiftCompleteSfx, 0.34f); }

        void PlayOneShot(SoundEffect sfx, float volume)
        {
            if (!EnsureInitialized() || sfx == null || sfx.IsDisposed) return;
            try { sfx.Play(volume, 0f, 0f); }
            catch { }
        }

        bool EnsureInitialized()
        {
            if (initialized) return !initFailed;
            initialized = true;
            try
            {
                ambientSfx = CreateEffect(BuildEerieNightAmbientLoop(8.0f));
                motionAlertSfx = CreateEffect(BuildMotionAlertPing());
                alarmSfx = CreateEffect(BuildAlarmSiren());
                allClearSfx = CreateEffect(BuildAllClearChirp());
                coffeeSfx = CreateEffect(BuildCoffeeChime());
                goodFeedbackSfx = CreateEffect(BuildFeedbackTone(true));
                badFeedbackSfx = CreateEffect(BuildFeedbackTone(false));
                shiftCompleteSfx = CreateEffect(BuildShiftCompleteChime());
                Boot.Log("cctv audio: initialized procedural eerie ambience & console SFX");
                return true;
            }
            catch (Exception e)
            {
                initFailed = true;
                Boot.Log("cctv audio disabled (no audio device?): " + e.Message);
                return false;
            }
        }

        static SoundEffect CreateEffect(short[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            return new SoundEffect(bytes, SampleRate, AudioChannels.Mono);
        }

        // Seamless 8-second eerie security-booth atmosphere:
        // - Soft 60Hz + 120Hz CRT monitor hum
        // - Slow breathing dark minor pad (D2, F2, A2 + haunted Eb3 tension tone)
        // - Filtered low analog tape hiss + two subtle distant night creaks/sonar swells
        // All oscillator frequencies are rounded to exact integer cycles over durationSec for click-free looping.
        static short[] BuildEerieNightAmbientLoop(float durationSec)
        {
            int n = (int)(SampleRate * durationSec);
            var outBuf = new short[n];
            double twoPi = Math.PI * 2.0;

            double Snap(double hz) { return Math.Round(hz * durationSec) / durationSec; }

            double fHum60 = Snap(60.0);
            double fHum120 = Snap(120.0);
            double fDroneD2 = Snap(73.5);
            double fDroneF2 = Snap(87.25);
            double fDroneA2 = Snap(110.0);
            double fEerieEb3 = Snap(155.5);
            double fGhostHigh = Snap(311.125); // Distant haunted Eb4 shimmer
            double fLfoSlow = Snap(0.25);      // 2 breaths per 8s loop
            double fLfoSwirl = Snap(0.125);    // 1 slow eerie swell per 8s loop

            var rng = new Random(1337);
            double lpNoise = 0.0;

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;

                // Subtle CRT mains hum
                double hum = 0.055 * Math.Sin(twoPi * fHum60 * t)
                           + 0.022 * Math.Sin(twoPi * fHum120 * t);

                // Dark minor ambient pad with slow eerie breathing
                double breath = 0.65 + 0.35 * Math.Sin(twoPi * fLfoSlow * t - Math.PI * 0.5);
                double swirl = 0.5 + 0.5 * Math.Sin(twoPi * fLfoSwirl * t);
                double drone = (0.17 * Math.Sin(twoPi * fDroneD2 * t)
                              + 0.11 * Math.Sin(twoPi * fDroneF2 * t + 0.4)
                              + 0.08 * Math.Sin(twoPi * fDroneA2 * t + 1.1)) * breath;

                // Haunting dissonant Eb shimmer fading gently in and out during the midnight watch
                double ghostEnv = Math.Pow(swirl, 2.2);
                double ghost = (0.065 * Math.Sin(twoPi * fEerieEb3 * t)
                              + 0.032 * Math.Sin(twoPi * fGhostHigh * t + 0.3 * Math.Sin(twoPi * 1.5 * t))) * ghostEnv;

                // Distant eerie night wind / security booth tape floor (low-pass filtered)
                double white = rng.NextDouble() * 2.0 - 1.0;
                lpNoise = lpNoise * 0.94 + white * 0.06;
                double hiss = lpNoise * 0.085;

                // Two soft atmospheric distant radar/pipe resonances inside the loop
                double pulse = 0.0;
                double d1 = t - 2.4;
                if (d1 >= 0.0 && d1 < 1.1)
                    pulse += 0.06 * Math.Sin(twoPi * 233.0 * d1) * Math.Exp(-d1 * 3.8) * Math.Min(1.0, d1 * 18.0);
                double d2 = t - 5.9;
                if (d2 >= 0.0 && d2 < 1.3)
                    pulse += 0.05 * Math.Sin(twoPi * 196.0 * d2) * Math.Exp(-d2 * 3.2) * Math.Min(1.0, d2 * 15.0);

                double sample = hum + drone + ghost + hiss + pulse;
                outBuf[i] = ToPcm16(sample);
            }

            return outBuf;
        }

        // Crisp 2-tone CCTV motion detector ping
        static short[] BuildMotionAlertPing()
        {
            int n = (int)(SampleRate * 0.36);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f = t < 0.13 ? 880.0 : 1174.66;
                double lt = t < 0.13 ? t : t - 0.13;
                double env = Math.Min(1.0, lt * 120.0) * Math.Exp(-lt * 13.0);
                double s = (0.42 * Math.Sin(twoPi * f * t) + 0.14 * Math.Sin(twoPi * f * 2.0 * t)) * env;
                buf[i] = ToPcm16(s);
            }
            return buf;
        }

        // Short precinct alarm whoop (unmistakable but not ear-piercing)
        static short[] BuildAlarmSiren()
        {
            int n = (int)(SampleRate * 0.52);
            var buf = new short[n];
            double phase = 0.0;
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double sweep = 0.5 + 0.5 * Math.Sin(twoPi * 5.2 * t - Math.PI * 0.5);
                double freq = 520.0 + sweep * 310.0;
                phase += twoPi * freq / SampleRate;
                double env = Math.Min(1.0, t * 40.0) * Math.Min(1.0, (0.52 - t) * 14.0);
                double raw = Math.Sin(phase) + 0.25 * Math.Sin(phase * 2.0);
                buf[i] = ToPcm16(raw * 0.32 * env);
            }
            return buf;
        }

        // Calm radio confirmation chirp for All Clear
        static short[] BuildAllClearChirp()
        {
            int n = (int)(SampleRate * 0.28);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                double f = t < 0.11 ? 440.0 : 587.33;
                double lt = t < 0.11 ? t : t - 0.11;
                double env = Math.Min(1.0, lt * 110.0) * Math.Exp(-lt * 14.0);
                buf[i] = ToPcm16(0.38 * Math.Sin(twoPi * f * t) * env);
            }
            return buf;
        }

        // Warm 3-note upbeat perk chime for sipping station coffee
        static short[] BuildCoffeeChime()
        {
            int n = (int)(SampleRate * 0.34);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double[] notes = { 523.25, 659.25, 783.99 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                int idx = Math.Min(2, (int)(t / 0.10));
                double lt = t - idx * 0.10;
                double env = Math.Min(1.0, lt * 100.0) * Math.Exp(-lt * 12.0);
                double s = (0.36 * Math.Sin(twoPi * notes[idx] * t) + 0.10 * Math.Sin(twoPi * notes[idx] * 2.0 * t)) * env;
                buf[i] = ToPcm16(s);
            }
            return buf;
        }

        static short[] BuildFeedbackTone(bool positive)
        {
            int n = (int)(SampleRate * (positive ? 0.32 : 0.38));
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                if (positive)
                {
                    double env = Math.Min(1.0, t * 90.0) * Math.Exp(-t * 9.5);
                    double s = (0.28 * Math.Sin(twoPi * 659.25 * t) + 0.24 * Math.Sin(twoPi * 987.77 * t)) * env;
                    buf[i] = ToPcm16(s);
                }
                else
                {
                    double env = Math.Min(1.0, t * 80.0) * Math.Exp(-t * 6.5);
                    double s = (0.28 * Math.Sin(twoPi * 185.0 * t) + 0.22 * Math.Sin(twoPi * 174.6 * t)) * env;
                    buf[i] = ToPcm16(s);
                }
            }
            return buf;
        }

        static short[] BuildShiftCompleteChime()
        {
            int n = (int)(SampleRate * 0.65);
            var buf = new short[n];
            double twoPi = Math.PI * 2.0;
            double[] notes = { 523.25, 659.25, 783.99, 1046.50 };
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / SampleRate;
                int idx = Math.Min(3, (int)(t / 0.13));
                double lt = t - idx * 0.13;
                double env = Math.Min(1.0, lt * 90.0) * Math.Exp(-lt * (idx == 3 ? 5.5 : 10.0));
                double s = (0.32 * Math.Sin(twoPi * notes[idx] * t) + 0.09 * Math.Sin(twoPi * notes[idx] * 2.0 * t)) * env;
                buf[i] = ToPcm16(s);
            }
            return buf;
        }

        static short ToPcm16(double sample)
        {
            if (sample > 0.98) sample = 0.98;
            if (sample < -0.98) sample = -0.98;
            return (short)Math.Round(sample * 32760.0);
        }

        public void Dispose()
        {
            StopAmbient();
            TryDispose(ambientInst); ambientInst = null;
            TryDispose(ambientSfx); ambientSfx = null;
            TryDispose(motionAlertSfx); motionAlertSfx = null;
            TryDispose(alarmSfx); alarmSfx = null;
            TryDispose(allClearSfx); allClearSfx = null;
            TryDispose(coffeeSfx); coffeeSfx = null;
            TryDispose(goodFeedbackSfx); goodFeedbackSfx = null;
            TryDispose(badFeedbackSfx); badFeedbackSfx = null;
            TryDispose(shiftCompleteSfx); shiftCompleteSfx = null;
        }

        static void TryDispose(IDisposable d)
        {
            try { if (d != null) d.Dispose(); }
            catch { }
        }
    }
}
