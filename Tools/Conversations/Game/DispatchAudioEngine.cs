using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Conversations
{
    // Live MonoGame SoundEffect playback + timeline recorder for Law Enforcement Level 4
    // ("Desk Sergeant" — 911 Dispatch Triage). Plays real-time procedural precinct SFX
    // AND exports a sample-accurate synchronized WAV for HD gameplay recordings.
    internal sealed class DispatchAudioEngine : IDisposable
    {
        readonly Dictionary<DispatchSfxId, SoundEffect> bank = new Dictionary<DispatchSfxId, SoundEffect>();
        readonly List<DispatchAudioEvent> sessionEvents = new List<DispatchAudioEvent>();
        readonly Stopwatch sessionClock = new Stopwatch();
        SoundEffectInstance ambienceInstance;
        bool initialized;
        bool failed;
        float ambienceVol = 0.18f;

        public bool IsReady { get { return initialized && !failed; } }
        public int RecordedCueCount { get { return sessionEvents.Count; } }

        void EnsureInit()
        {
            if (initialized || failed) return;
            try
            {
                foreach (DispatchSfxId id in Enum.GetValues(typeof(DispatchSfxId)))
                {
                    byte[] pcm = DispatchAudioSynth.SynthesizePcm16(id);
                    if (pcm != null && pcm.Length > 0)
                        bank[id] = new SoundEffect(pcm, DispatchAudioSynth.SampleRate, AudioChannels.Mono);
                }
                initialized = true;
                Boot.Log("dispatch audio: initialized precinct ambience & " + bank.Count + " procedural SFX");
            }
            catch (Exception e)
            {
                failed = true;
                Boot.Log("dispatch audio init failed (muted): " + e.Message);
            }
        }

        public void StartSession()
        {
            EnsureInit();
            sessionEvents.Clear();
            sessionClock.Restart();
        }

        public void StartAmbience(float volume = 0.18f)
        {
            EnsureInit();
            ambienceVol = Math.Max(0f, Math.Min(1f, volume));
            if (failed) return;
            try
            {
                StopAmbience();
                if (bank.TryGetValue(DispatchSfxId.PrecinctAmbience, out var sfx) && sfx != null)
                {
                    ambienceInstance = sfx.CreateInstance();
                    ambienceInstance.IsLooped = true;
                    ambienceInstance.Volume = ambienceVol;
                    ambienceInstance.Play();
                }
            }
            catch { }
        }

        public void StopAmbience()
        {
            if (ambienceInstance == null) return;
            try
            {
                ambienceInstance.Stop();
                ambienceInstance.Dispose();
            }
            catch { }
            ambienceInstance = null;
        }

        public void Play(DispatchSfxId cue, float volume = 0.50f, float pitch = 0f)
        {
            EnsureInit();
            float clampedVol = Math.Max(0f, Math.Min(1f, volume));
            if (sessionClock.IsRunning)
            {
                sessionEvents.Add(new DispatchAudioEvent
                {
                    TimeSec = (float)sessionClock.Elapsed.TotalSeconds,
                    Cue = cue,
                    Gain = clampedVol
                });
            }
            if (failed) return;
            try
            {
                if (bank.TryGetValue(cue, out var sfx) && sfx != null)
                    sfx.Play(clampedVol, Math.Max(-0.5f, Math.Min(0.5f, pitch)), 0f);
            }
            catch { }
        }

        public string ExportSessionWav(string path, float leadSec = 0f, float tailSec = 1.0f)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return "err empty wav path";
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var shifted = new List<DispatchAudioEvent>(sessionEvents.Count);
                float maxTime = sessionClock.IsRunning ? (float)sessionClock.Elapsed.TotalSeconds : 2.0f;
                for (int i = 0; i < sessionEvents.Count; i++)
                {
                    var ev = sessionEvents[i];
                    float t = Math.Max(0f, ev.TimeSec + leadSec);
                    shifted.Add(new DispatchAudioEvent { TimeSec = t, Cue = ev.Cue, Gain = ev.Gain });
                    if (t > maxTime) maxTime = t;
                }
                float totalDur = Math.Max(1.5f, maxTime + Math.Max(0.4f, tailSec));
                byte[] wav = DispatchAudioSynth.RenderTimelineToWav(shifted, totalDur, ambienceVol);
                File.WriteAllBytes(path, wav);
                return "ok wav=" + path + " bytes=" + wav.Length + " cues=" + shifted.Count + " dur=" + totalDur.ToString("0.00") + "s";
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        public void Dispose()
        {
            StopAmbience();
            foreach (var kv in bank)
                try { kv.Value?.Dispose(); } catch { }
            bank.Clear();
            initialized = false;
        }
    }
}
