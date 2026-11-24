using System;
using System.Collections.Generic;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;

namespace Simitone.Conversations
{
    // Converts a CustomAnimClip (.simanim.json) into a runtime FSO.Vitaboy.Animation
    // instance so future on-lot VMAvatar perks (Ranks 3, 6, 10) can play custom clips
    // directly through FreeSO's native Animator pipeline (ANIM-09).
    internal static class VitaboyAnimationAdapter
    {
        public static Animation ToVitaboyAnimation(CustomAnimClip clip)
        {
            if (clip == null) return null;

            var translations = new List<Vector3>();
            var rotations = new List<Quaternion>();
            var motions = new List<AnimationMotion>();

            int fc = Math.Max(1, clip.FrameCount);
            float fps = clip.Fps > 0.01f ? clip.Fps : 30f;

            for (int i = 0; i < clip.Tracks.Count; i++)
            {
                var tr = clip.Tracks[i];
                if (tr == null || string.IsNullOrEmpty(tr.Bone)) continue;

                bool hasT = tr.Translations != null && tr.Translations.Length > 0;
                bool hasR = tr.Rotations != null && tr.Rotations.Length > 0;
                if (!hasT && !hasR) continue;

                int firstT = hasT ? translations.Count : -1;
                if (hasT)
                {
                    for (int f = 0; f < fc; f++)
                    {
                        var v = tr.Translations[Math.Min(f, tr.Translations.Length - 1)];
                        translations.Add(new Vector3(v.X, v.Y, v.Z));
                    }
                }

                int firstR = hasR ? rotations.Count : -1;
                if (hasR)
                {
                    for (int f = 0; f < fc; f++)
                    {
                        var q = tr.Rotations[Math.Min(f, tr.Rotations.Length - 1)].Normalized();
                        rotations.Add(new Quaternion(q.X, q.Y, q.Z, q.W));
                    }
                }

                motions.Add(new AnimationMotion
                {
                    BoneName = tr.Bone,
                    FrameCount = (uint)fc,
                    Duration = (fc / fps) * 1000f,
                    HasTranslation = hasT,
                    HasRotation = hasR,
                    FirstTranslationIndex = firstT,
                    FirstRotationIndex = firstR
                });
            }

            float durationMs = (fc / fps) * 1000f;
            return new Animation
            {
                Name = clip.Name ?? "custom",
                Duration = durationMs,
                Distance = clip.RootSpeedUnitsPerSec * (fc / fps),
                IsMoving = clip.RootSpeedUnitsPerSec > 0.01f ? (byte)1 : (byte)0,
                Translations = translations.ToArray(),
                Rotations = rotations.ToArray(),
                Motions = motions.ToArray()
            };
        }
    }
}
