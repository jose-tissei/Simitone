using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Conversations
{
    // Live MonoGame SoundEffect playback + timeline recorder for Law Enforcement Level 5
    // ("Vice Squad" — Undercover Sting). Plays real-time procedural speakeasy & wiretap SFX
    // AND exports a sample-accurate synchronized WAV for HD gameplay recordings.
    internal sealed class StingAudioEngine : IDisposable
    {
        readonly Dictionary<StingSfxId, SoundEffect> bank = new Dictionary<StingSfxId, SoundEffect>();
        readonly List<StingAudioEvent> sessionEvents = new List<StingAudioEvent>();
        readonly Stopwatch sessionClock = new Stopwatch();
        SoundEffectInstance ambienceInstance;
        bool initialized;
        bool failed;
        float ambienceVol = 0.16f;

        public bool IsReady { get { return initialized && !failed; } }
        public int RecordedCueCount { get { return sessionEvents.Count; } }

        void EnsureInit()
        {
            if (initialized || failed) return;
            try
            {
                foreach (StingSfxId id in Enum.GetValues(typeof(StingSfxId)))
                {
                    byte[] pcm = StingAudioSynth.SynthesizePcm16(id);
                    if (pcm != null && pcm.Length > 0)
                        bank[id] = new SoundEffect(pcm, StingAudioSynth.SampleRate, AudioChannels.Mono);
                }
                initialized = true;
                Boot.Log("sting audio: initialized speakeasy ambience & " + bank.Count + " procedural SFX");
            }
            catch (Exception e)
            {
                failed = true;
                Boot.Log("sting audio init failed (muted): " + e.Message);
            }
        }

        public void StartSession()
        {
            EnsureInit();
            sessionEvents.Clear();
            sessionClock.Restart();
        }

        public void StartAmbience(float volume = 0.16f)
        {
            EnsureInit();
            ambienceVol = Math.Max(0f, Math.Min(1f, volume));
            if (failed) return;
            try
            {
                StopAmbience();
                if (bank.TryGetValue(StingSfxId.LoungeAmbience, out var sfx) && sfx != null)
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

        public void Play(StingSfxId id, float volume = 0.55f)
        {
            EnsureInit();
            float vol = Math.Max(0f, Math.Min(1f, volume));
            double tSec = sessionClock.IsRunning ? sessionClock.Elapsed.TotalSeconds : 0.0;
            sessionEvents.Add(new StingAudioEvent { TimeSec = tSec, Id = id, Volume = vol });

            if (failed) return;
            try
            {
                if (bank.TryGetValue(id, out var sfx) && sfx != null)
                    sfx.Play(vol, 0f, 0f);
            }
            catch { }
        }

        public string ExportWav(string outputPath, float leadSec = 0f, float tailSec = 1.0f)
        {
            try
            {
                double maxCue = 0.5;
                for (int i = 0; i < sessionEvents.Count; i++)
                    if (sessionEvents[i].TimeSec > maxCue) maxCue = sessionEvents[i].TimeSec;

                double elapsed = sessionClock.IsRunning ? sessionClock.Elapsed.TotalSeconds : maxCue;
                double totalDur = Math.Max(elapsed, maxCue) + Math.Max(0f, leadSec) + Math.Max(0.5f, tailSec);
                byte[] wav = StingAudioSynth.ExportTimelineWav(sessionEvents, totalDur, leadSec, ambienceVol);
                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, wav);
                return "ok wav=" + outputPath + " bytes=" + wav.Length + " cues=" + sessionEvents.Count + " dur=" + totalDur.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
            }
            catch (Exception e)
            {
                return "error exportwav: " + e.Message;
            }
        }

        public void Dispose()
        {
            StopAmbience();
            foreach (var kv in bank)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            bank.Clear();
        }
    }
}
