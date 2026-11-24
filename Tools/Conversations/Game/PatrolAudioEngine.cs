using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Conversations
{
    // Live MonoGame SoundEffect engine for Law Enforcement Level 3 ("Patrol Officer"):
    // Real-Lot Live Beat Patrol (DISP-06). Synthesizes all 17 Beat Patrol cues + seamless
    // city dusk street ambience via PatrolAudioSynth, plays live through MonoGame, and records
    // a sample-accurate cue timeline for synchronized verification WAV export.
    internal sealed class PatrolAudioEngine : IDisposable
    {
        bool initialized;
        bool initFailed;

        SoundEffect ambientSfx;
        SoundEffectInstance ambientInst;
        readonly Dictionary<PatrolSfxId, SoundEffect> effects = new Dictionary<PatrolSfxId, SoundEffect>();

        readonly Stopwatch sessionTimer = new Stopwatch();
        readonly List<PatrolSfxEvent> sessionEvents = new List<PatrolSfxEvent>(96);
        double ambienceStartSec = -1.0;
        double ambienceEndSec = -1.0;
        double sessionEndSec = -1.0;

        public bool IsReady { get { return initialized && !initFailed; } }
        public int SessionEventCount { get { return sessionEvents.Count; } }

        public void StartSession()
        {
            sessionEvents.Clear();
            ambienceStartSec = -1.0;
            ambienceEndSec = -1.0;
            sessionEndSec = -1.0;
            sessionTimer.Restart();
        }

        public void EndSession()
        {
            StopAmbience();
            if (sessionTimer.IsRunning)
            {
                sessionEndSec = sessionTimer.Elapsed.TotalSeconds;
                sessionTimer.Stop();
            }
        }

        public void StartAmbience(float volume = 0.22f)
        {
            if (ambienceStartSec < 0.0)
                ambienceStartSec = sessionTimer.IsRunning ? sessionTimer.Elapsed.TotalSeconds : 0.0;

            if (!EnsureInitialized()) return;
            try
            {
                if (ambientInst == null || ambientInst.IsDisposed)
                {
                    ambientInst = ambientSfx.CreateInstance();
                    ambientInst.IsLooped = true;
                }
                ambientInst.Volume = Math.Max(0f, Math.Min(1f, volume));
                if (ambientInst.State != SoundState.Playing)
                    ambientInst.Play();
            }
            catch (Exception e)
            {
                Boot.Log("patrol audio start ambience error: " + e.Message);
            }
        }

        public void StopAmbience()
        {
            if (ambienceStartSec >= 0.0 && ambienceEndSec < 0.0)
                ambienceEndSec = sessionTimer.IsRunning ? sessionTimer.Elapsed.TotalSeconds : ambienceStartSec;

            try
            {
                if (ambientInst != null && !ambientInst.IsDisposed && ambientInst.State != SoundState.Stopped)
                    ambientInst.Stop();
            }
            catch { }
        }

        public void Play(PatrolSfxId id, float volume = 0.44f, float pitch = 0f)
        {
            float clampedVol = Math.Max(0f, Math.Min(1f, volume));
            float clampedPitch = Math.Max(-1f, Math.Min(1f, pitch));

            if (sessionTimer.IsRunning)
            {
                sessionEvents.Add(new PatrolSfxEvent(
                    sessionTimer.Elapsed.TotalSeconds,
                    id,
                    clampedVol,
                    clampedPitch));
            }

            if (!EnsureInitialized()) return;
            try
            {
                SoundEffect sfx;
                if (effects.TryGetValue(id, out sfx) && sfx != null && !sfx.IsDisposed)
                    sfx.Play(clampedVol, clampedPitch, 0f);
            }
            catch { }
        }

        public string ExportSessionWav(string outputPath, float leadSec = 0f, float tailSec = 1.0f)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return "err empty output path";
            try
            {
                double nowSec = sessionTimer.IsRunning
                    ? sessionTimer.Elapsed.TotalSeconds
                    : (sessionEndSec >= 0.0 ? sessionEndSec : 0.0);

                float safeLead = Math.Max(0f, leadSec);
                float safeTail = Math.Max(0.25f, tailSec);

                var shifted = new List<PatrolSfxEvent>(sessionEvents.Count);
                double maxEventSec = nowSec;
                for (int i = 0; i < sessionEvents.Count; i++)
                {
                    var ev = sessionEvents[i];
                    if (ev.TimeSec > maxEventSec) maxEventSec = ev.TimeSec;
                    shifted.Add(new PatrolSfxEvent(ev.TimeSec + safeLead, ev.Id, ev.Volume, ev.Pitch));
                }

                float totalDur = (float)(safeLead + maxEventSec + safeTail);
                bool hasAmb = ambienceStartSec >= 0.0;
                float ambStart = hasAmb ? (float)(safeLead + ambienceStartSec) : 0f;
                float ambEnd = hasAmb
                    ? (float)(safeLead + (ambienceEndSec >= 0.0 ? ambienceEndSec : nowSec))
                    : 0f;

                byte[] wav = PatrolAudioSynth.RenderMixedTimelineWav(
                    totalDur,
                    includeAmbience: hasAmb,
                    ambienceVolume: 0.24f,
                    ambienceStartSec: ambStart,
                    ambienceEndSec: ambEnd,
                    events: shifted);

                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, wav);

                return "ok wav=" + outputPath + " bytes=" + wav.Length + " cues=" + shifted.Count + " dur=" + totalDur.ToString("0.00", CultureInfo.InvariantCulture) + "s";
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        bool EnsureInitialized()
        {
            if (initialized) return !initFailed;
            initialized = true;
            try
            {
                ambientSfx = CreateEffect(PatrolAudioSynth.BuildCityDuskAmbienceLoop(8.0f));
                foreach (PatrolSfxId id in Enum.GetValues(typeof(PatrolSfxId)))
                    effects[id] = CreateEffect(PatrolAudioSynth.BuildSound(id));

                Boot.Log("patrol audio: initialized city dusk ambience & " + effects.Count + " procedural SFX");
                return true;
            }
            catch (Exception e)
            {
                initFailed = true;
                Boot.Log("patrol audio disabled (no audio device?): " + e.Message);
                return false;
            }
        }

        static SoundEffect CreateEffect(short[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            return new SoundEffect(bytes, PatrolAudioSynth.SampleRate, AudioChannels.Mono);
        }

        public void Dispose()
        {
            StopAmbience();
            try { ambientInst?.Dispose(); } catch { }
            try { ambientSfx?.Dispose(); } catch { }
            foreach (var kv in effects)
            {
                try { kv.Value?.Dispose(); } catch { }
            }
            effects.Clear();
            ambientInst = null;
            ambientSfx = null;
        }
    }
}
