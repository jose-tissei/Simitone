using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Simitone.Conversations
{
    public static class CustomAnimLoader
    {
        public static CustomAnimClip LoadFromFile(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "Animation file not found: " + (path ?? "<null>");
                return null;
            }
            try
            {
                return ParseJson(File.ReadAllText(path), out error);
            }
            catch (Exception e)
            {
                error = "Failed to read animation file: " + e.Message;
                return null;
            }
        }

        public static CustomAnimClip ParseJson(string json, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty animation JSON";
                return null;
            }

            try
            {
                var root = JObject.Parse(json);
                var clip = new CustomAnimClip
                {
                    Version = root.Value<int?>("version") ?? 1,
                    Name = root.Value<string>("name") ?? "",
                    Skeleton = root.Value<string>("skeleton") ?? "adult",
                    Fps = root.Value<float?>("fps") ?? 30f,
                    FrameCount = root.Value<int?>("frameCount") ?? 0,
                    Duration = root.Value<float?>("duration") ?? 0f,
                    Loop = root.Value<bool?>("loop") ?? true,
                    Mask = ParseMask(root.Value<string>("mask")),
                    RootSpeedUnitsPerSec = root.Value<float?>("rootSpeedUnitsPerSec") ?? 0f,
                    Source = root.Value<string>("source") ?? ""
                };

                if (clip.Fps <= 0.01f) clip.Fps = 30f;

                var eventsArr = root["events"] as JArray;
                if (eventsArr != null)
                {
                    foreach (var item in eventsArr)
                    {
                        var evObj = item as JObject;
                        if (evObj == null) continue;
                        clip.Events.Add(new CustomAnimEvent
                        {
                            Frame = evObj.Value<int?>("frame") ?? 0,
                            Name = evObj.Value<string>("name") ?? "",
                            Value = evObj.Value<string>("value") ?? ""
                        });
                    }
                }

                var handArr = root["handGestures"] as JArray;
                if (handArr != null)
                {
                    foreach (var item in handArr)
                    {
                        var hObj = item as JObject;
                        if (hObj == null) continue;
                        clip.HandGestures.Add(new CustomHandCue
                        {
                            Frame = hObj.Value<int?>("frame") ?? 0,
                            Left = hObj.Value<string>("left"),
                            Right = hObj.Value<string>("right")
                        });
                    }
                }

                int inferredFrames = clip.FrameCount;
                var tracksArr = root["tracks"] as JArray;
                if (tracksArr != null)
                {
                    foreach (var item in tracksArr)
                    {
                        var tObj = item as JObject;
                        if (tObj == null) continue;
                        string bone = tObj.Value<string>("bone");
                        if (string.IsNullOrEmpty(bone)) continue;

                        var track = new CustomBoneTrack { Bone = bone };

                        var timesArr = tObj["times"] as JArray;
                        if (timesArr != null && timesArr.Count > 0)
                        {
                            track.Times = new float[timesArr.Count];
                            for (int i = 0; i < timesArr.Count; i++)
                                track.Times[i] = timesArr[i].Value<float>();
                        }

                        var transArr = tObj["translations"] as JArray;
                        if (transArr != null && transArr.Count > 0)
                        {
                            track.Translations = new AnimVec3[transArr.Count];
                            for (int i = 0; i < transArr.Count; i++)
                            {
                                var v = transArr[i] as JArray;
                                if (v != null && v.Count >= 3)
                                    track.Translations[i] = new AnimVec3(SafeFloat(v[0]), SafeFloat(v[1]), SafeFloat(v[2]));
                            }
                            if (track.Times == null && transArr.Count > inferredFrames)
                                inferredFrames = transArr.Count;
                        }

                        var rotArr = tObj["rotations"] as JArray;
                        if (rotArr != null && rotArr.Count > 0)
                        {
                            track.Rotations = new AnimQuat[rotArr.Count];
                            for (int i = 0; i < rotArr.Count; i++)
                            {
                                var q = rotArr[i] as JArray;
                                if (q != null && q.Count >= 4)
                                {
                                    var parsed = new AnimQuat(SafeFloat(q[0]), SafeFloat(q[1]), SafeFloat(q[2]), SafeFloat(q[3]));
                                    track.Rotations[i] = parsed.Normalized();
                                }
                                else
                                {
                                    track.Rotations[i] = AnimQuat.Identity;
                                }
                            }
                            // Enforce continuous quaternion hemisphere across adjacent keyframes
                            for (int i = 1; i < track.Rotations.Length; i++)
                            {
                                if (AnimQuat.Dot(track.Rotations[i - 1], track.Rotations[i]) < 0f)
                                    track.Rotations[i] = -track.Rotations[i];
                            }
                            if (track.Times == null && rotArr.Count > inferredFrames)
                                inferredFrames = rotArr.Count;
                        }

                        clip.Tracks.Add(track);
                    }
                }

                clip.FrameCount = Math.Max(1, inferredFrames);
                if (clip.Duration <= 0f)
                    clip.Duration = clip.FrameCount / clip.Fps;

                clip.RebuildIndex();
                return clip;
            }
            catch (Exception e)
            {
                error = "Invalid .simanim.json: " + e.Message;
                return null;
            }
        }

        public static string ToJson(CustomAnimClip clip, bool indented = false)
        {
            if (clip == null) return "{}";
            var root = new JObject
            {
                ["version"] = clip.Version,
                ["name"] = clip.Name ?? "",
                ["skeleton"] = clip.Skeleton ?? "adult",
                ["fps"] = clip.Fps,
                ["frameCount"] = clip.FrameCount,
                ["duration"] = clip.Duration,
                ["loop"] = clip.Loop,
                ["mask"] = FormatMask(clip.Mask),
                ["rootSpeedUnitsPerSec"] = clip.RootSpeedUnitsPerSec,
                ["source"] = clip.Source ?? ""
            };

            if (clip.Events != null && clip.Events.Count > 0)
            {
                var evArr = new JArray();
                for (int i = 0; i < clip.Events.Count; i++)
                {
                    var ev = clip.Events[i];
                    evArr.Add(new JObject
                    {
                        ["frame"] = ev.Frame,
                        ["name"] = ev.Name ?? "",
                        ["value"] = ev.Value ?? ""
                    });
                }
                root["events"] = evArr;
            }

            if (clip.HandGestures != null && clip.HandGestures.Count > 0)
            {
                var hArr = new JArray();
                for (int i = 0; i < clip.HandGestures.Count; i++)
                {
                    var h = clip.HandGestures[i];
                    var obj = new JObject { ["frame"] = h.Frame };
                    if (!string.IsNullOrEmpty(h.Left)) obj["left"] = h.Left;
                    if (!string.IsNullOrEmpty(h.Right)) obj["right"] = h.Right;
                    hArr.Add(obj);
                }
                root["handGestures"] = hArr;
            }

            var trArr = new JArray();
            if (clip.Tracks != null)
            {
                for (int i = 0; i < clip.Tracks.Count; i++)
                {
                    var tr = clip.Tracks[i];
                    if (tr == null || string.IsNullOrEmpty(tr.Bone)) continue;
                    var tObj = new JObject { ["bone"] = tr.Bone };
                    if (tr.Times != null && tr.Times.Length > 0)
                    {
                        var ta = new JArray();
                        for (int k = 0; k < tr.Times.Length; k++) ta.Add(Math.Round(tr.Times[k], 5));
                        tObj["times"] = ta;
                    }
                    if (tr.Translations != null && tr.Translations.Length > 0)
                    {
                        var va = new JArray();
                        for (int k = 0; k < tr.Translations.Length; k++)
                        {
                            var v = tr.Translations[k];
                            va.Add(new JArray(Math.Round(v.X, 5), Math.Round(v.Y, 5), Math.Round(v.Z, 5)));
                        }
                        tObj["translations"] = va;
                    }
                    if (tr.Rotations != null && tr.Rotations.Length > 0)
                    {
                        var qa = new JArray();
                        for (int k = 0; k < tr.Rotations.Length; k++)
                        {
                            var q = tr.Rotations[k].Normalized();
                            qa.Add(new JArray(Math.Round(q.X, 5), Math.Round(q.Y, 5), Math.Round(q.Z, 5), Math.Round(q.W, 5)));
                        }
                        tObj["rotations"] = qa;
                    }
                    trArr.Add(tObj);
                }
            }
            root["tracks"] = trArr;

            return root.ToString(indented ? Formatting.Indented : Formatting.None);
        }

        static float SafeFloat(JToken token)
        {
            if (token == null) return 0f;
            float v = token.Value<float>();
            return (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v;
        }

        static AnimBoneMask ParseMask(string mask)
        {
            if (string.IsNullOrEmpty(mask)) return AnimBoneMask.Full;
            switch (mask.Trim().ToLowerInvariant())
            {
                case "upper":
                case "upperbody": return AnimBoneMask.UpperBody;
                case "lower":
                case "lowerbody": return AnimBoneMask.LowerBody;
                default: return AnimBoneMask.Full;
            }
        }

        static string FormatMask(AnimBoneMask mask)
        {
            switch (mask)
            {
                case AnimBoneMask.UpperBody: return "upper";
                case AnimBoneMask.LowerBody: return "lower";
                default: return "full";
            }
        }
    }
}
