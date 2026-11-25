using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework.Audio;

namespace Simitone.Conversations
{
    // Real-time MonoGame SoundEffect playback & timeline event recorder for Level 6 Detective Crime Scene Deduction.
    internal sealed class DetectiveAudioEngine
    {
        readonly Dictionary<DetectiveSfxId, SoundEffect> clips = new Dictionary<DetectiveSfxId, SoundEffect>();
        readonly List<DetectiveAudioEvent> recordedEvents = new List<DetectiveAudioEvent>();
        readonly Stopwatch sessionClock = new Stopwatch();
        SoundEffectInstance ambienceInstance;
        bool initialized;

        public int RecordedCueCount { get { return recordedEvents.Count; } }

        public void EnsureStarted()
        {
            if (!sessionClock.IsRunning)
            {
                recordedEvents.Clear();
                sessionClock.Restart();
            }

            if (!initialized)
            {
                initialized = true;
                try
                {
                    foreach (DetectiveSfxId id in Enum.GetValues(typeof(DetectiveSfxId)))
                    {
                        byte[] pcm = DetectiveAudioSynth.SynthesizePcm16(id);
                        clips[id] = new SoundEffect(pcm, DetectiveAudioSynth.SampleRate, AudioChannels.Mono);
                    }
                }
                catch (Exception e)
                {
                    Boot.Log("detective-audio init warning: " + e.Message);
                }
            }

            try
            {
                if (ambienceInstance == null && clips.TryGetValue(DetectiveSfxId.CrimeSceneAmbience, out var amb))
                {
                    ambienceInstance = amb.CreateInstance();
                    ambienceInstance.IsLooped = true;
                    ambienceInstance.Volume = 0.28f;
                    ambienceInstance.Play();
                }
                else if (ambienceInstance != null && ambienceInstance.State != SoundState.Playing)
                {
                    ambienceInstance.Play();
                }
            }
            catch { }
        }

        public void Play(DetectiveSfxId id, float volume = 0.72f)
        {
            EnsureStarted();
            recordedEvents.Add(new DetectiveAudioEvent
            {
                TimeSec = sessionClock.Elapsed.TotalSeconds,
                Id = id,
                Volume = Math.Max(0f, Math.Min(1f, volume)),
            });

            try
            {
                if (clips.TryGetValue(id, out var sfx))
                    sfx.Play(Math.Max(0f, Math.Min(1f, volume)), 0f, 0f);
            }
            catch { }
        }

        public void StopAmbience()
        {
            try
            {
                if (ambienceInstance != null)
                {
                    ambienceInstance.Stop();
                    ambienceInstance.Dispose();
                    ambienceInstance = null;
                }
            }
            catch { }
            sessionClock.Stop();
        }

        public void ExportRecordedTrackWav(string path)
        {
            double duration = Math.Max(4.0, sessionClock.Elapsed.TotalSeconds + 1.0);
            DetectiveAudioSynth.WriteMixedTrackWav(path, duration, recordedEvents);
        }
    }
}
