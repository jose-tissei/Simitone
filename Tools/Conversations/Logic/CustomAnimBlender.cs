using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    // Wrap-safe SQUAD/SLERP skeletal evaluator, smooth multi-clip crossfader,
    // SPINE-split upper/lower masked layering, timed events, and root-motion tracking.
    public sealed class CustomAnimBlender
    {
        public sealed class LayerState
        {
            public CustomAnimClip Clip;
            public float TimeSeconds;
            public float Speed = 1f;
            public bool Loop = true;
            public float Weight = 1f;
            public float TargetWeight = 1f;
            public float FadeRate;
            public AnimBoneMask Mask = AnimBoneMask.Full;
            public int LastEventFrame = -1;
        }

        readonly List<LayerState> baseLayers = new List<LayerState>(4);
        LayerState overlayLayer;
        readonly List<CustomAnimEvent> firedEvents = new List<CustomAnimEvent>(8);

        public IReadOnlyList<CustomAnimEvent> FiredEvents => firedEvents;
        public float LastStepForwardUnits { get; private set; }
        public string ActiveClipName => CurrentBase != null && CurrentBase.Clip != null ? CurrentBase.Clip.Name : "none";
        public float ActiveClipTime => CurrentBase != null ? CurrentBase.TimeSeconds : 0f;
        public float ActiveRootSpeedUnitsPerSec => CurrentBase != null && CurrentBase.Clip != null
            ? CurrentBase.Clip.RootSpeedUnitsPerSec * CurrentBase.Speed
            : 0f;

        LayerState CurrentBase => baseLayers.Count > 0 ? baseLayers[baseLayers.Count - 1] : null;

        public void Reset()
        {
            baseLayers.Clear();
            overlayLayer = null;
            firedEvents.Clear();
            LastStepForwardUnits = 0f;
        }

        // Transitions smoothly to clip over fadeSeconds. Calling Play with the already-active
        // clip updates speed/loop without restarting playback time (ideal for per-frame state loops).
        public void Play(CustomAnimClip clip, float fadeSeconds = 0.16f, float speed = 1f, bool? loopOverride = null, bool forceRestart = false)
        {
            if (clip == null) return;
            bool loop = loopOverride ?? clip.Loop;

            var cur = CurrentBase;
            if (!forceRestart && cur != null && ReferenceEquals(cur.Clip, clip))
            {
                cur.Speed = speed;
                cur.Loop = loop;
                cur.TargetWeight = 1f;
                return;
            }

            if (fadeSeconds <= 0.001f || baseLayers.Count == 0)
            {
                baseLayers.Clear();
                baseLayers.Add(new LayerState
                {
                    Clip = clip,
                    TimeSeconds = 0f,
                    Speed = speed,
                    Loop = loop,
                    Weight = 1f,
                    TargetWeight = 1f,
                    FadeRate = 0f,
                    Mask = AnimBoneMask.Full
                });
                return;
            }

            float rate = 1f / fadeSeconds;
            for (int i = 0; i < baseLayers.Count; i++)
            {
                baseLayers[i].TargetWeight = 0f;
                baseLayers[i].FadeRate = rate;
            }

            // Cap active crossfade history to 3 layers to prevent unbounded accumulation on rapid toggling
            while (baseLayers.Count >= 3)
                baseLayers.RemoveAt(0);

            baseLayers.Add(new LayerState
            {
                Clip = clip,
                TimeSeconds = 0f,
                Speed = speed,
                Loop = loop,
                Weight = 0f,
                TargetWeight = 1f,
                FadeRate = rate,
                Mask = AnimBoneMask.Full
            });
        }

        public void SetTime(float timeSeconds)
        {
            var cur = CurrentBase;
            if (cur == null || cur.Clip == null) return;
            cur.TimeSeconds = WrapOrClampTime(cur.Clip, timeSeconds, cur.Loop);
        }

        public void SetOverlay(CustomAnimClip clip, AnimBoneMask mask = AnimBoneMask.UpperBody, float weight = 1f, float fadeSeconds = 0.12f, float speed = 1f, bool? loopOverride = null)
        {
            if (clip == null || weight <= 0f)
            {
                ClearOverlay(fadeSeconds);
                return;
            }

            bool loop = loopOverride ?? clip.Loop;
            if (overlayLayer != null && ReferenceEquals(overlayLayer.Clip, clip) && overlayLayer.Mask == mask)
            {
                overlayLayer.Speed = speed;
                overlayLayer.Loop = loop;
                overlayLayer.TargetWeight = Math.Max(0f, Math.Min(1f, weight));
                overlayLayer.FadeRate = fadeSeconds > 0.001f ? (1f / fadeSeconds) : 1000f;
                return;
            }

            overlayLayer = new LayerState
            {
                Clip = clip,
                TimeSeconds = 0f,
                Speed = speed,
                Loop = loop,
                Weight = fadeSeconds <= 0.001f ? weight : 0f,
                TargetWeight = Math.Max(0f, Math.Min(1f, weight)),
                FadeRate = fadeSeconds > 0.001f ? (1f / fadeSeconds) : 1000f,
                Mask = mask
            };
        }

        public void ClearOverlay(float fadeSeconds = 0.12f)
        {
            if (overlayLayer == null) return;
            if (fadeSeconds <= 0.001f)
            {
                overlayLayer = null;
                return;
            }
            overlayLayer.TargetWeight = 0f;
            overlayLayer.FadeRate = 1f / fadeSeconds;
        }

        public void Tick(float dt)
        {
            firedEvents.Clear();
            LastStepForwardUnits = 0f;
            if (dt < 0f) dt = 0f;

            float weightedSpeed = 0f;
            float totalWeight = 0f;

            for (int i = baseLayers.Count - 1; i >= 0; i--)
            {
                var layer = baseLayers[i];
                AdvanceLayer(layer, dt, i == baseLayers.Count - 1);
                if (layer.Weight <= 0.0001f && layer.TargetWeight <= 0.0001f && baseLayers.Count > 1)
                {
                    baseLayers.RemoveAt(i);
                    continue;
                }
                if (layer.Clip != null)
                {
                    weightedSpeed += layer.Clip.RootSpeedUnitsPerSec * layer.Speed * layer.Weight;
                    totalWeight += layer.Weight;
                }
            }

            if (totalWeight > 1e-5f)
                LastStepForwardUnits = (weightedSpeed / totalWeight) * dt;

            if (overlayLayer != null)
            {
                AdvanceLayer(overlayLayer, dt, true);
                if (overlayLayer.Weight <= 0.0001f && overlayLayer.TargetWeight <= 0.0001f)
                    overlayLayer = null;
            }
        }

        void AdvanceLayer(LayerState layer, float dt, bool emitEvents)
        {
            if (layer.Clip == null) return;

            if (layer.FadeRate > 0f && Math.Abs(layer.TargetWeight - layer.Weight) > 1e-5f)
            {
                float step = layer.FadeRate * dt;
                if (layer.Weight < layer.TargetWeight)
                    layer.Weight = Math.Min(layer.TargetWeight, layer.Weight + step);
                else
                    layer.Weight = Math.Max(layer.TargetWeight, layer.Weight - step);
            }

            float prevTime = layer.TimeSeconds;
            float nextTime = prevTime + dt * layer.Speed;
            float dur = Math.Max(1e-4f, layer.Clip.Duration);

            if (emitEvents && layer.Clip.Events != null && layer.Clip.Events.Count > 0 && dt > 0f)
            {
                int prevFrame = (int)Math.Floor(prevTime * layer.Clip.Fps);
                int nextFrame = (int)Math.Floor(nextTime * layer.Clip.Fps);
                int fc = Math.Max(1, layer.Clip.FrameCount);
                for (int i = 0; i < layer.Clip.Events.Count; i++)
                {
                    var ev = layer.Clip.Events[i];
                    int ef = ((ev.Frame % fc) + fc) % fc;
                    if (CrossedFrame(prevFrame, nextFrame, ef, fc, layer.Loop))
                        firedEvents.Add(ev);
                }
            }

            layer.TimeSeconds = WrapOrClampTime(layer.Clip, nextTime, layer.Loop);
        }

        static bool CrossedFrame(int prevFrame, int nextFrame, int targetFrame, int frameCount, bool loop)
        {
            if (nextFrame < prevFrame) return false;
            if (!loop)
                return targetFrame > prevFrame && targetFrame <= nextFrame;

            int span = Math.Min(nextFrame - prevFrame, frameCount);
            for (int step = 1; step <= span; step++)
            {
                int f = ((prevFrame + step) % frameCount + frameCount) % frameCount;
                if (f == targetFrame) return true;
            }
            return false;
        }

        static float WrapOrClampTime(CustomAnimClip clip, float timeSec, bool loop)
        {
            float dur = Math.Max(1e-4f, clip.Duration);
            if (loop)
            {
                float wrapped = timeSec % dur;
                if (wrapped < 0f) wrapped += dur;
                return wrapped;
            }
            if (timeSec < 0f) return 0f;
            if (timeSec > dur) return dur;
            return timeSec;
        }

        public bool EvaluateBone(
            string boneName,
            AnimVec3 restTranslation,
            AnimQuat restRotation,
            out AnimVec3 outTranslation,
            out AnimQuat outRotation,
            bool useSquad = true)
        {
            outTranslation = restTranslation;
            outRotation = restRotation;
            if (baseLayers.Count == 0 && overlayLayer == null) return false;

            bool anyWritten = false;
            float accumWeight = 0f;
            AnimVec3 accumTrans = restTranslation;
            AnimQuat accumRot = restRotation;

            // 1. Blend base crossfade stack
            for (int i = 0; i < baseLayers.Count; i++)
            {
                var layer = baseLayers[i];
                if (layer.Clip == null || layer.Weight <= 0.0001f) continue;

                CustomBonePose sample;
                bool hasTrack = EvaluateClipBone(layer.Clip, boneName, layer.TimeSeconds, layer.Loop, useSquad, out sample);
                AnimVec3 tVal = hasTrack && sample.HasTranslation ? sample.Translation : restTranslation;
                AnimQuat rVal = hasTrack && sample.HasRotation ? sample.Rotation : restRotation;

                if (accumWeight <= 0.0001f)
                {
                    accumTrans = tVal;
                    accumRot = rVal;
                    accumWeight = layer.Weight;
                }
                else
                {
                    float alpha = layer.Weight / (accumWeight + layer.Weight);
                    accumTrans = AnimVec3.Lerp(accumTrans, tVal, alpha);
                    accumRot = AnimQuat.Slerp(accumRot, rVal, alpha);
                    accumWeight += layer.Weight;
                }
                if (hasTrack) anyWritten = true;
            }

            // 2. Blend masked overlay (e.g., upper-body salute or aim on top of lower-body walk/sprint)
            if (overlayLayer != null && overlayLayer.Clip != null && overlayLayer.Weight > 0.0001f &&
                AdultSkeletonMask.Includes(overlayLayer.Mask, boneName))
            {
                CustomBonePose ovSample;
                if (EvaluateClipBone(overlayLayer.Clip, boneName, overlayLayer.TimeSeconds, overlayLayer.Loop, useSquad, out ovSample))
                {
                    float w = overlayLayer.Weight;
                    if (ovSample.HasTranslation)
                        accumTrans = AnimVec3.Lerp(accumTrans, ovSample.Translation, w);
                    if (ovSample.HasRotation)
                        accumRot = AnimQuat.Slerp(accumRot, ovSample.Rotation, w);
                    anyWritten = true;
                }
            }

            outTranslation = accumTrans;
            outRotation = accumRot;
            return anyWritten;
        }

        public void GetActiveHandGestures(out string leftGesture, out string rightGesture)
        {
            leftGesture = null;
            rightGesture = null;
            ReadGestureFromLayer(CurrentBase, ref leftGesture, ref rightGesture);
            if (overlayLayer != null && overlayLayer.Weight > 0.35f)
                ReadGestureFromLayer(overlayLayer, ref leftGesture, ref rightGesture);
        }

        static void ReadGestureFromLayer(LayerState layer, ref string left, ref string right)
        {
            if (layer == null || layer.Clip == null || layer.Clip.HandGestures == null || layer.Clip.HandGestures.Count == 0)
                return;
            int curFrame = (int)Math.Floor(layer.TimeSeconds * layer.Clip.Fps);
            CustomHandCue best = null;
            for (int i = 0; i < layer.Clip.HandGestures.Count; i++)
            {
                var cue = layer.Clip.HandGestures[i];
                if (cue.Frame <= curFrame && (best == null || cue.Frame >= best.Frame))
                    best = cue;
            }
            if (best == null) best = layer.Clip.HandGestures[0];
            if (!string.IsNullOrEmpty(best.Left)) left = best.Left;
            if (!string.IsNullOrEmpty(best.Right)) right = best.Right;
        }

        // Wrap-safe track evaluation: fixes FreeSO Animator.RenderFrame's loop-end freeze bug
        // by wrapping frameCount - 1 -> 0 seamlessly when loop == true.
        public static bool EvaluateClipBone(
            CustomAnimClip clip,
            string boneName,
            float timeSeconds,
            bool loop,
            bool useSquad,
            out CustomBonePose pose)
        {
            pose = default(CustomBonePose);
            if (clip == null) return false;
            var track = clip.GetTrack(boneName);
            if (track == null) return false;

            if (track.Times != null && track.Times.Length > 0)
                return EvaluateSparseTrack(track, clip.Duration, timeSeconds, loop, out pose);

            int nRot = track.Rotations != null ? track.Rotations.Length : 0;
            int nTrans = track.Translations != null ? track.Translations.Length : 0;
            int n = Math.Max(nRot, nTrans);
            if (n <= 0) return false;

            if (n == 1)
            {
                if (nTrans > 0) { pose.HasTranslation = true; pose.Translation = track.Translations[0]; }
                if (nRot > 0) { pose.HasRotation = true; pose.Rotation = track.Rotations[0]; }
                return true;
            }

            float exactFrame;
            if (loop)
            {
                float dur = Math.Max(1e-4f, clip.Duration);
                float u = ((timeSeconds % dur) + dur) % dur;
                exactFrame = (u / dur) * n;
            }
            else
            {
                float dur = Math.Max(1e-4f, clip.Duration);
                float clamped = Math.Max(0f, Math.Min(dur, timeSeconds));
                exactFrame = (clamped / dur) * (n - 1);
            }

            int f1 = (int)Math.Floor(exactFrame);
            float frac = exactFrame - f1;
            int f0, f2, f3;
            if (loop)
            {
                f1 = ((f1 % n) + n) % n;
                f0 = (f1 - 1 + n) % n;
                f2 = (f1 + 1) % n;
                f3 = (f1 + 2) % n;
            }
            else
            {
                f1 = Math.Max(0, Math.Min(n - 1, f1));
                f0 = Math.Max(0, f1 - 1);
                f2 = Math.Min(n - 1, f1 + 1);
                f3 = Math.Min(n - 1, f1 + 2);
            }

            if (nTrans > 0)
            {
                int t0 = Math.Min(f0, nTrans - 1);
                int t1 = Math.Min(f1, nTrans - 1);
                int t2 = Math.Min(f2, nTrans - 1);
                int t3 = Math.Min(f3, nTrans - 1);
                pose.HasTranslation = true;
                pose.Translation = (useSquad && nTrans >= 4)
                    ? AnimVec3.CatmullRom(track.Translations[t0], track.Translations[t1], track.Translations[t2], track.Translations[t3], frac)
                    : AnimVec3.Lerp(track.Translations[t1], track.Translations[t2], frac);
            }

            if (nRot > 0)
            {
                int r0 = Math.Min(f0, nRot - 1);
                int r1 = Math.Min(f1, nRot - 1);
                int r2 = Math.Min(f2, nRot - 1);
                int r3 = Math.Min(f3, nRot - 1);
                pose.HasRotation = true;
                pose.Rotation = (useSquad && nRot >= 4)
                    ? AnimQuat.Squad(track.Rotations[r0], track.Rotations[r1], track.Rotations[r2], track.Rotations[r3], frac)
                    : AnimQuat.Slerp(track.Rotations[r1], track.Rotations[r2], frac);
            }

            return pose.HasTranslation || pose.HasRotation;
        }

        static bool EvaluateSparseTrack(CustomBoneTrack track, float duration, float timeSeconds, bool loop, out CustomBonePose pose)
        {
            pose = default(CustomBonePose);
            var times = track.Times;
            int n = times.Length;
            float t = loop && duration > 1e-4f
                ? ((timeSeconds % duration) + duration) % duration
                : Math.Max(times[0], Math.Min(times[n - 1], timeSeconds));

            int i0 = 0;
            int i1 = 0;
            float alpha = 0f;

            if (t <= times[0])
            {
                if (loop && n > 1 && duration > times[n - 1])
                {
                    i0 = n - 1;
                    i1 = 0;
                    float gap = (duration - times[n - 1]) + times[0];
                    alpha = gap > 1e-5f ? ((t + (duration - times[n - 1])) / gap) : 0f;
                }
            }
            else if (t >= times[n - 1])
            {
                if (loop && n > 1 && duration > times[n - 1])
                {
                    i0 = n - 1;
                    i1 = 0;
                    float gap = (duration - times[n - 1]) + times[0];
                    alpha = gap > 1e-5f ? ((t - times[n - 1]) / gap) : 0f;
                }
                else
                {
                    i0 = i1 = n - 1;
                }
            }
            else
            {
                for (int i = 0; i < n - 1; i++)
                {
                    if (t >= times[i] && t <= times[i + 1])
                    {
                        i0 = i;
                        i1 = i + 1;
                        float seg = times[i1] - times[i0];
                        alpha = seg > 1e-5f ? (t - times[i0]) / seg : 0f;
                        break;
                    }
                }
            }

            if (track.Translations != null && track.Translations.Length > Math.Max(i0, i1))
            {
                pose.HasTranslation = true;
                pose.Translation = AnimVec3.Lerp(track.Translations[i0], track.Translations[i1], alpha);
            }
            if (track.Rotations != null && track.Rotations.Length > Math.Max(i0, i1))
            {
                pose.HasRotation = true;
                pose.Rotation = AnimQuat.Slerp(track.Rotations[i0], track.Rotations[i1], alpha);
            }
            return pose.HasTranslation || pose.HasRotation;
        }
    }
}
