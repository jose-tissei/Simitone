using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;

namespace Simitone.Conversations
{
    // Hot-reloadable custom skeletal animation clip library.
    // Searches <GameDir>/CustomAnims/<name>.simanim.json first (for live hot-reload editing),
    // and falls back to embedded Conversations.dll resources so clips are always available.
    internal static class CustomAnimLibrary
    {
        static readonly object Gate = new object();
        static readonly Dictionary<string, CustomAnimClip> Cache = new Dictionary<string, CustomAnimClip>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> LoggedMissing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static int reloadVersion = 1;

        public static int ReloadVersion => reloadVersion;

        public static string CustomAnimsDir =>
            Path.Combine(Boot.GameDir ?? AppDomain.CurrentDomain.BaseDirectory, "CustomAnims");

        public static CustomAnimClip TryGet(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string key = name.Trim();
            if (key.EndsWith(".simanim.json", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - ".simanim.json".Length);

            lock (Gate)
            {
                CustomAnimClip cached;
                if (Cache.TryGetValue(key, out cached))
                    return cached;

                CustomAnimClip loaded = LoadClipInternal(key);
                if (loaded != null)
                {
                    Cache[key] = loaded;
                    return loaded;
                }

                if (LoggedMissing.Add(key))
                    Boot.Log("custom-anim missing: " + key + " (checked " + CustomAnimsDir + " & embedded)");
                return null;
            }
        }

        public static string ReloadAll()
        {
            lock (Gate)
            {
                int prevCount = Cache.Count;
                Cache.Clear();
                LoggedMissing.Clear();
                reloadVersion++;

                int diskCount = 0;
                int errors = 0;
                string dir = CustomAnimsDir;
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.simanim.json"))
                    {
                        string err;
                        var clip = CustomAnimLoader.LoadFromFile(file, out err);
                        if (clip != null)
                        {
                            string key = !string.IsNullOrEmpty(clip.Name)
                                ? clip.Name
                                : Path.GetFileName(file).Replace(".simanim.json", "");
                            Cache[key] = clip;
                            diskCount++;
                        }
                        else
                        {
                            errors++;
                            Boot.Log("custom-anim reload error (" + Path.GetFileName(file) + "): " + err);
                        }
                    }
                }
                return "reloaded v=" + reloadVersion + " disk=" + diskCount + " errors=" + errors + " prev=" + prevCount;
            }
        }

        public static string ListSummary()
        {
            lock (Gate)
            {
                var names = new HashSet<string>(Cache.Keys, StringComparer.OrdinalIgnoreCase);
                string dir = CustomAnimsDir;
                if (Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.simanim.json"))
                    {
                        string fn = Path.GetFileName(file);
                        names.Add(fn.Substring(0, fn.Length - ".simanim.json".Length));
                    }
                }
                var asm = typeof(CustomAnimLibrary).Assembly;
                foreach (var res in asm.GetManifestResourceNames())
                {
                    if (res.EndsWith(".simanim.json", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = res.LastIndexOf(".Animations.", StringComparison.OrdinalIgnoreCase);
                        string shortName = idx >= 0 ? res.Substring(idx + ".Animations.".Length) : res;
                        shortName = shortName.Substring(0, shortName.Length - ".simanim.json".Length);
                        names.Add(shortName);
                    }
                }
                return "v=" + reloadVersion + " dir=" + dir + " clips=[" + string.Join(",", names.OrderBy(s => s)) + "]";
            }
        }

        static CustomAnimClip LoadClipInternal(string key)
        {
            // 1. Prefer loose file on disk (<GameDir>/CustomAnims/<key>.simanim.json) for instant hot-reload
            try
            {
                string diskPath = Path.Combine(CustomAnimsDir, key + ".simanim.json");
                if (File.Exists(diskPath))
                {
                    string err;
                    var clip = CustomAnimLoader.LoadFromFile(diskPath, out err);
                    if (clip != null) return clip;
                    Boot.Log("custom-anim disk parse error (" + key + "): " + err);
                }
            }
            catch (Exception e)
            {
                Boot.Log("custom-anim disk load exception (" + key + "): " + e.Message);
            }

            // 2. Fall back to embedded resource inside Conversations.dll
            try
            {
                var asm = typeof(CustomAnimLibrary).Assembly;
                string suffix = "." + key + ".simanim.json";
                string resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                if (resName != null)
                {
                    using (var stream = asm.GetManifestResourceStream(resName))
                    using (var reader = new StreamReader(stream))
                    {
                        string err;
                        var clip = CustomAnimLoader.ParseJson(reader.ReadToEnd(), out err);
                        if (clip != null) return clip;
                        Boot.Log("custom-anim embedded parse error (" + key + "): " + err);
                    }
                }
            }
            catch (Exception e)
            {
                Boot.Log("custom-anim embedded load exception (" + key + "): " + e.Message);
            }

            return null;
        }
    }

    // Thin MonoGame/Vitaboy bridge driving SimAvatar.Skeleton.Bones from CustomAnimBlender
    // without touching or patching FreeSO DLLs.
    internal sealed class VitaboyClipPlayer
    {
        readonly CustomAnimBlender blender = new CustomAnimBlender();
        int observedReloadVersion = -1;
        string requestedClipName;
        float requestedSpeed = 1f;
        bool? requestedLoop;

        public CustomAnimBlender Blender => blender;
        public string ActiveClipName => blender.ActiveClipName;
        public float ActiveRootSpeedUnitsPerSec => blender.ActiveRootSpeedUnitsPerSec;
        public bool HasActiveCustomClip => !string.IsNullOrEmpty(blender.ActiveClipName) && blender.ActiveClipName != "none";

        public void Reset()
        {
            blender.Reset();
            requestedClipName = null;
        }

        public bool Play(string clipName, float fadeSeconds = 0.16f, float speed = 1f, bool? loopOverride = null, bool forceRestart = false)
        {
            if (observedReloadVersion != CustomAnimLibrary.ReloadVersion)
            {
                observedReloadVersion = CustomAnimLibrary.ReloadVersion;
                forceRestart = true;
            }
            requestedClipName = clipName;
            requestedSpeed = speed;
            requestedLoop = loopOverride;

            var clip = CustomAnimLibrary.TryGet(clipName);
            if (clip == null) return false;
            blender.Play(clip, fadeSeconds, speed, loopOverride, forceRestart);
            return true;
        }

        public void SetTime(float timeSeconds)
        {
            blender.SetTime(timeSeconds);
        }

        public bool SetOverlay(string clipName, AnimBoneMask mask = AnimBoneMask.UpperBody, float weight = 1f, float fadeSeconds = 0.12f, float speed = 1f, bool? loopOverride = null)
        {
            var clip = CustomAnimLibrary.TryGet(clipName);
            if (clip == null)
            {
                blender.ClearOverlay(fadeSeconds);
                return false;
            }
            blender.SetOverlay(clip, mask, weight, fadeSeconds, speed, loopOverride);
            return true;
        }

        public void ClearOverlay(float fadeSeconds = 0.12f)
        {
            blender.ClearOverlay(fadeSeconds);
        }

        // Advances blend state by dt and writes all 29 evaluated bone transforms directly onto
        // avatar.Skeleton.Bones, sets hand gestures, and refreshes GPU skinning matrices via ReloadSkeleton().
        // When seekTime is supplied, crossfade weights still advance smoothly by dt while the active
        // base clip is pinned to seekTime (preventing fade-in deadlock on timeline-scrubbed one-shots).
        public bool UpdateAndApply(SimAvatar avatar, float dt, bool useSquad = true, float? seekTime = null)
        {
            if (avatar == null || avatar.Skeleton == null || avatar.Skeleton.Bones == null)
                return false;

            if (observedReloadVersion != CustomAnimLibrary.ReloadVersion && !string.IsNullOrEmpty(requestedClipName))
            {
                observedReloadVersion = CustomAnimLibrary.ReloadVersion;
                var refreshed = CustomAnimLibrary.TryGet(requestedClipName);
                if (refreshed != null)
                    blender.Play(refreshed, 0f, requestedSpeed, requestedLoop, forceRestart: false);
            }

            if (!HasActiveCustomClip) return false;

            blender.Tick(dt);
            if (seekTime.HasValue)
                blender.SetTime(seekTime.Value);

            var bones = avatar.Skeleton.Bones;
            var baseBones = avatar.BaseSkeleton != null ? avatar.BaseSkeleton.Bones : null;
            int count = bones.Length;

            for (int i = 0; i < count; i++)
            {
                var bone = bones[i];
                if (bone == null || string.IsNullOrEmpty(bone.Name)) continue;

                var restT = (baseBones != null && i < baseBones.Length && baseBones[i] != null)
                    ? baseBones[i].Translation
                    : bone.Translation;
                var restR = (baseBones != null && i < baseBones.Length && baseBones[i] != null)
                    ? baseBones[i].Rotation
                    : bone.Rotation;

                AnimVec3 outT;
                AnimQuat outR;
                if (blender.EvaluateBone(
                        bone.Name,
                        new AnimVec3(restT.X, restT.Y, restT.Z),
                        new AnimQuat(restR.X, restR.Y, restR.Z, restR.W),
                        out outT,
                        out outR,
                        useSquad))
                {
                    bone.Translation = new Vector3(outT.X, outT.Y, outT.Z);
                    bone.Rotation = new Quaternion(outR.X, outR.Y, outR.Z, outR.W);
                }
                else
                {
                    bone.Translation = restT;
                    bone.Rotation = restR;
                }
            }

            string leftG, rightG;
            blender.GetActiveHandGestures(out leftG, out rightG);
            ApplyHandGesture(avatar, true, leftG);
            ApplyHandGesture(avatar, false, rightG);

            return true;
        }

        static void ApplyHandGesture(SimAvatar avatar, bool isLeft, string gestureName)
        {
            if (string.IsNullOrEmpty(gestureName)) return;
            try
            {
                SimHandGesture g = SimHandGesture.Idle;
                string key = gestureName.Trim().ToLowerInvariant();
                if (key == "fist" || key == "closed" || key == "grip") g = SimHandGesture.Fist;
                else if (key == "point" || key == "pointing") g = SimHandGesture.Pointing;
                else g = SimHandGesture.Idle;

                if (isLeft)
                {
                    if (avatar.LeftHandGesture != g) avatar.LeftHandGesture = g;
                }
                else
                {
                    if (avatar.RightHandGesture != g) avatar.RightHandGesture = g;
                }
            }
            catch { }
        }
    }
}
