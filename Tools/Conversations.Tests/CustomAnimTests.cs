using System;
using Xunit;
using Simitone.Conversations;

namespace Simitone.Conversations.Tests
{
    public sealed class CustomAnimTests
    {
        static CustomAnimClip BuildSimpleSpinClip(string name, string bone, int frames, float fps, bool loop, float rootSpeed = 0f)
        {
            var clip = new CustomAnimClip
            {
                Version = 1,
                Name = name,
                Skeleton = "adult",
                Fps = fps,
                FrameCount = frames,
                Duration = frames / fps,
                Loop = loop,
                RootSpeedUnitsPerSec = rootSpeed
            };
            var rots = new AnimQuat[frames];
            var trans = new AnimVec3[frames];
            for (int i = 0; i < frames; i++)
            {
                float angle = (float)(2.0 * Math.PI * i / frames);
                rots[i] = AnimQuat.FromAxisAngle(AnimVec3.UnitY, angle);
                trans[i] = new AnimVec3(0f, 3.0f + 0.2f * (float)Math.Sin(angle), 0f);
            }
            clip.Tracks.Add(new CustomBoneTrack { Bone = bone, Rotations = rots, Translations = trans });
            clip.RebuildIndex();
            return clip;
        }

        [Fact]
        public void ParseJson_NormalizesQuaternions_AndAlignsHemispheres()
        {
            const string json = @"{
              ""version"": 1,
              ""name"": ""test_clip"",
              ""fps"": 30,
              ""frameCount"": 3,
              ""loop"": true,
              ""rootSpeedUnitsPerSec"": 9.6,
              ""events"": [{ ""frame"": 1, ""name"": ""footstep_l"", ""value"": ""1"" }],
              ""handGestures"": [{ ""frame"": 0, ""left"": ""Fist"", ""right"": ""Open"" }],
              ""tracks"": [{
                ""bone"": ""SPINE"",
                ""rotations"": [
                  [0, 0, 0, 2],
                  [0, 0, 0, -3],
                  [0, 0, 0, 0]
                ]
              }]
            }";

            string err;
            var clip = CustomAnimLoader.ParseJson(json, out err);
            Assert.Null(err);
            Assert.NotNull(clip);
            Assert.Equal("test_clip", clip.Name);
            Assert.Equal(9.6f, clip.RootSpeedUnitsPerSec, 3);
            Assert.Single(clip.Events);
            Assert.Single(clip.HandGestures);

            var tr = clip.GetTrack("SPINE");
            Assert.NotNull(tr);
            Assert.Equal(3, tr.Rotations.Length);
            // Frame 0 normalized -> (0,0,0,1)
            Assert.InRange(Math.Abs(tr.Rotations[0].Length() - 1f), 0f, 1e-4f);
            // Frame 1 aligned to frame 0 hemisphere -> dot > 0
            Assert.True(AnimQuat.Dot(tr.Rotations[0], tr.Rotations[1]) > 0.999f);
            // Frame 2 zero quat safely replaced with Identity
            Assert.True(tr.Rotations[2].IsValid());
            Assert.InRange(Math.Abs(tr.Rotations[2].W), 0.999f, 1.001f);
        }

        [Fact]
        public void ParseJson_MalformedInput_ReturnsNullWithError()
        {
            string err;
            Assert.Null(CustomAnimLoader.ParseJson("{not valid json", out err));
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Null(CustomAnimLoader.ParseJson("", out err));
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void RoundTrip_ToJson_ParseJson_PreservesTracksEventsAndSpeed()
        {
            var clip = BuildSimpleSpinClip("sprint_test", "ROOT", 12, 30f, true, 10.5f);
            clip.Events.Add(new CustomAnimEvent { Frame = 3, Name = "footstep_l", Value = "1" });
            clip.HandGestures.Add(new CustomHandCue { Frame = 0, Left = "Fist", Right = "Fist" });

            string json = CustomAnimLoader.ToJson(clip, indented: false);
            string err;
            var parsed = CustomAnimLoader.ParseJson(json, out err);
            Assert.Null(err);
            Assert.NotNull(parsed);
            Assert.Equal(clip.Name, parsed.Name);
            Assert.Equal(clip.FrameCount, parsed.FrameCount);
            Assert.Equal(clip.RootSpeedUnitsPerSec, parsed.RootSpeedUnitsPerSec, 3);
            Assert.Single(parsed.Events);
            Assert.Single(parsed.HandGestures);

            var origTr = clip.GetTrack("ROOT");
            var newTr = parsed.GetTrack("ROOT");
            Assert.Equal(origTr.Rotations.Length, newTr.Rotations.Length);
            for (int i = 0; i < origTr.Rotations.Length; i++)
            {
                Assert.True(Math.Abs(AnimQuat.Dot(origTr.Rotations[i], newTr.Rotations[i])) > 0.9999f);
            }
        }

        [Fact]
        public void EvaluateClipBone_LoopWrap_IsContinuousWithoutEndFrameFreeze()
        {
            // Full 360-degree spin over 16 frames: frame 15 -> frame 0 must wrap smoothly!
            var clip = BuildSimpleSpinClip("loop_spin", "SPINE", 16, 30f, loop: true);

            CustomBonePose beforeWrap, afterWrap, midWrap;
            Assert.True(CustomAnimBlender.EvaluateClipBone(clip, "SPINE", clip.Duration - 0.001f, true, true, out beforeWrap));
            Assert.True(CustomAnimBlender.EvaluateClipBone(clip, "SPINE", 0.001f, true, true, out afterWrap));
            Assert.True(CustomAnimBlender.EvaluateClipBone(clip, "SPINE", clip.Duration - 0.5f / clip.Fps, true, true, out midWrap));

            // Seamless wrap across Duration -> 0
            Assert.True(Math.Abs(AnimQuat.Dot(beforeWrap.Rotation, afterWrap.Rotation)) > 0.99f);
            // Midpoint between last frame (15) and frame 0 must NOT equal frame 15 (FreeSO bug!)
            var lastFrameRot = clip.GetTrack("SPINE").Rotations[15];
            Assert.True(Math.Abs(AnimQuat.Dot(midWrap.Rotation, lastFrameRot)) < 0.999f);
        }

        [Fact]
        public void EvaluateClipBone_NonLoop_ClampsToLastFrame()
        {
            var clip = new CustomAnimClip
            {
                Name = "oneshot",
                Fps = 30f,
                FrameCount = 2,
                Duration = 1f,
                Loop = false
            };
            var q0 = AnimQuat.Identity;
            var q1 = AnimQuat.FromAxisAngle(AnimVec3.UnitZ, 1.2f);
            clip.Tracks.Add(new CustomBoneTrack { Bone = "R_ARM1", Rotations = new[] { q0, q1 } });
            clip.RebuildIndex();

            CustomBonePose pose;
            Assert.True(CustomAnimBlender.EvaluateClipBone(clip, "R_ARM1", 5.0f, false, false, out pose));
            Assert.True(Math.Abs(AnimQuat.Dot(pose.Rotation, q1)) > 0.9999f);
        }

        [Fact]
        public void Crossfade_BlendsMonotonicallyFromClipAToClipB()
        {
            var clipA = new CustomAnimClip { Name = "A", Fps = 30f, FrameCount = 2, Duration = 1f, Loop = true };
            var qA = AnimQuat.Identity;
            clipA.Tracks.Add(new CustomBoneTrack { Bone = "SPINE", Rotations = new[] { qA, qA } });
            clipA.RebuildIndex();

            var clipB = new CustomAnimClip { Name = "B", Fps = 30f, FrameCount = 2, Duration = 1f, Loop = true };
            var qB = AnimQuat.FromAxisAngle(AnimVec3.UnitY, 1.5f);
            clipB.Tracks.Add(new CustomBoneTrack { Bone = "SPINE", Rotations = new[] { qB, qB } });
            clipB.RebuildIndex();

            var blender = new CustomAnimBlender();
            blender.Play(clipA, fadeSeconds: 0f);
            blender.Play(clipB, fadeSeconds: 0.20f);

            float prevDotB = -1f;
            for (int step = 0; step <= 10; step++)
            {
                AnimVec3 t;
                AnimQuat r;
                blender.EvaluateBone("SPINE", AnimVec3.Zero, AnimQuat.Identity, out t, out r);
                float dotB = Math.Abs(AnimQuat.Dot(r, qB));
                Assert.True(dotB >= prevDotB - 1e-4f, $"Expected monotonic blend toward B at step {step}: {dotB} < {prevDotB}");
                prevDotB = dotB;
                blender.Tick(0.025f);
            }
            Assert.True(prevDotB > 0.999f);
        }

        [Fact]
        public void UpperBodyOverlayMask_LeavesLowerBodyUntouched()
        {
            var baseClip = new CustomAnimClip { Name = "sprint", Fps = 30f, FrameCount = 2, Duration = 1f, Loop = true };
            var legRot = AnimQuat.FromAxisAngle(AnimVec3.UnitZ, 0.8f);
            var spineBase = AnimQuat.Identity;
            baseClip.Tracks.Add(new CustomBoneTrack { Bone = "L_LEG1", Rotations = new[] { legRot, legRot } });
            baseClip.Tracks.Add(new CustomBoneTrack { Bone = "SPINE", Rotations = new[] { spineBase, spineBase } });
            baseClip.RebuildIndex();

            var overlayClip = new CustomAnimClip { Name = "salute", Fps = 30f, FrameCount = 2, Duration = 1f, Loop = true };
            var legOverride = AnimQuat.FromAxisAngle(AnimVec3.UnitZ, -1.1f);
            var spineOverride = AnimQuat.FromAxisAngle(AnimVec3.UnitX, 0.6f);
            overlayClip.Tracks.Add(new CustomBoneTrack { Bone = "L_LEG1", Rotations = new[] { legOverride, legOverride } });
            overlayClip.Tracks.Add(new CustomBoneTrack { Bone = "SPINE", Rotations = new[] { spineOverride, spineOverride } });
            overlayClip.RebuildIndex();

            var blender = new CustomAnimBlender();
            blender.Play(baseClip, fadeSeconds: 0f);
            blender.SetOverlay(overlayClip, AnimBoneMask.UpperBody, weight: 1f, fadeSeconds: 0f);

            AnimVec3 t;
            AnimQuat rLeg, rSpine;
            blender.EvaluateBone("L_LEG1", AnimVec3.Zero, AnimQuat.Identity, out t, out rLeg);
            blender.EvaluateBone("SPINE", AnimVec3.Zero, AnimQuat.Identity, out t, out rSpine);

            Assert.True(Math.Abs(AnimQuat.Dot(rLeg, legRot)) > 0.9999f);
            Assert.True(Math.Abs(AnimQuat.Dot(rSpine, spineOverride)) > 0.9999f);
        }

        [Fact]
        public void Tick_FiresEventsAcrossFramesAndTracksRootSpeed()
        {
            var clip = BuildSimpleSpinClip("run", "ROOT", 10, 10f, loop: true, rootSpeed: 8.0f);
            clip.Events.Add(new CustomAnimEvent { Frame = 3, Name = "footstep_l", Value = "1" });
            clip.Events.Add(new CustomAnimEvent { Frame = 8, Name = "footstep_r", Value = "1" });

            var blender = new CustomAnimBlender();
            blender.Play(clip, fadeSeconds: 0f, speed: 1.5f);

            int firedCount = 0;
            float totalDist = 0f;
            for (int i = 0; i < 10; i++)
            {
                blender.Tick(0.1f); // 1.0s total at speed 1.5 => 1.5 loops => 12.0 units
                firedCount += blender.FiredEvents.Count;
                totalDist += blender.LastStepForwardUnits;
            }

            Assert.True(firedCount >= 2);
            Assert.InRange(totalDist, 11.9f, 12.1f);
        }

        [Fact]
        public void TwoBoneIkSolver_ReachesTargetPreservesLengthsAndRespectsPole()
        {
            // Hip at (0, 2, 0), knee at (0, 1, 0), ankle at (0, 0, 0); bone lengths = 1, 1
            var root = new AnimVec3(0f, 2f, 0f);
            var mid = new AnimVec3(0f, 1f, 0f);
            var end = new AnimVec3(0f, 0f, 0f);
            var target = new AnimVec3(0f, 0.8f, 0.6f); // dist from root = sqrt(1.44 + 0.36) = 1.3416 (< 2.0)
            var pole = new AnimVec3(0f, 1.5f, 3.0f);   // knee bends forward (+Z)

            var res = TwoBoneIkSolver.Solve(root, mid, end, target, pole);
            Assert.True(res.ReachedTarget);
            Assert.True((res.EndPosition - target).Length() < 1e-3f);
            Assert.InRange((res.MidPosition - root).Length(), 0.999f, 1.001f);
            Assert.InRange((res.EndPosition - res.MidPosition).Length(), 0.999f, 1.001f);
            // Knee must bend toward +Z pole!
            Assert.True(res.MidPosition.Z > 0.3f);

            // Verify delta rotations actually map rest limb onto solved positions
            var checkMid = root + res.UpperWorldDelta.Rotate(mid - root);
            var checkEnd = checkMid + (res.LowerWorldDelta * res.UpperWorldDelta).Rotate(end - mid);
            Assert.True((checkMid - res.MidPosition).Length() < 1e-3f);
            Assert.True((checkEnd - target).Length() < 1e-3f);
        }

        [Fact]
        public void ExportedCadetClips_AllSevenLoadAndEvaluateCleanlyInCSharp()
        {
            string[] names =
            {
                "cadet_idle_attention",
                "cadet_sprint_loop",
                "cadet_hurdle_hop",
                "cadet_wall_vault",
                "cadet_target_aim_loop",
                "cadet_target_fire_recoil",
                "cadet_victory_salute"
            };
            string animDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "Conversations", "Assets", "Animations"));

            Assert.True(System.IO.Directory.Exists(animDir), "Expected exported animations dir: " + animDir);

            var blender = new CustomAnimBlender();
            for (int i = 0; i < names.Length; i++)
            {
                string path = System.IO.Path.Combine(animDir, names[i] + ".simanim.json");
                string err;
                var clip = CustomAnimLoader.LoadFromFile(path, out err);
                Assert.Null(err);
                Assert.NotNull(clip);
                Assert.Equal(names[i], clip.Name);
                Assert.Equal(29, clip.Tracks.Count);

                blender.Play(clip, fadeSeconds: 0.12f);
                blender.Tick(0.05f);
                AnimVec3 t;
                AnimQuat r;
                Assert.True(blender.EvaluateBone("ROOT", AnimVec3.Zero, AnimQuat.Identity, out t, out r));
                Assert.True(r.IsValid());
                Assert.True(blender.EvaluateBone("R_ARM2", AnimVec3.Zero, AnimQuat.Identity, out t, out r));
                Assert.True(r.IsValid());
            }
        }

        [Fact]
        public void SeekTimeAfterTick_CompletesCrossfadeAndPinsExactClipTime()
        {
            var clipA = new CustomAnimClip { Name = "a", Fps = 30f, FrameCount = 10, Duration = 10f / 30f, Loop = true };
            var trA = new CustomBoneTrack { Bone = "ROOT", Translations = new AnimVec3[10] };
            for (int i = 0; i < 10; i++) trA.Translations[i] = new AnimVec3(0f, 3f, 0f);
            clipA.Tracks.Add(trA);

            var clipB = new CustomAnimClip { Name = "b", Fps = 30f, FrameCount = 10, Duration = 9f / 30f, Loop = false };
            var trB = new CustomBoneTrack { Bone = "ROOT", Translations = new AnimVec3[10] };
            for (int i = 0; i < 10; i++) trB.Translations[i] = new AnimVec3(0f, 10f + i, 0f);
            clipB.Tracks.Add(trB);

            var blender = new CustomAnimBlender();
            blender.Play(clipA, fadeSeconds: 0f);
            blender.Tick(0.05f);

            // Transition to clipB with a 0.08s crossfade, ticking real dt and pinning seekTime each frame
            blender.Play(clipB, fadeSeconds: 0.08f, loopOverride: false);
            for (int step = 0; step < 4; step++)
            {
                blender.Tick(0.03f);
                blender.SetTime(5f / 30f); // exact frame 5 -> Y = 15.0
            }

            AnimVec3 outT;
            AnimQuat outR;
            Assert.True(blender.EvaluateBone("ROOT", AnimVec3.Zero, AnimQuat.Identity, out outT, out outR));
            Assert.InRange(outT.Y, 14.99f, 15.01f);
            Assert.InRange(blender.ActiveClipTime, (5f / 30f) - 1e-4f, (5f / 30f) + 1e-4f);
        }

        [Fact]
        public void ExportedPatrolClips_AllFourLoadWithNonCrossingAnatomicalArms()
        {
            string[] names =
            {
                "patrol_sneak_loop",
                "patrol_apprehend",
                "patrol_write_ticket",
                "patrol_inspect_clue"
            };
            string toolsDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", ".."));
            string animDir = System.IO.Path.Combine(toolsDir, "Conversations", "Assets", "Animations");
            string skelRefPath = System.IO.Path.Combine(toolsDir, "AnimPipeline", "adult_skel_ref.json");
            Assert.True(System.IO.File.Exists(skelRefPath), "Expected adult_skel_ref.json: " + skelRefPath);

            // Parse rest bone hierarchy (name, parent, localTranslation, localRotation)
            var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(skelRefPath));
            var bones = new System.Collections.Generic.List<(string Name, string Parent, AnimVec3 T, AnimQuat R)>();
            foreach (var b in doc.RootElement.GetProperty("bones").EnumerateArray())
            {
                var lt = b.GetProperty("localTranslation");
                var lr = b.GetProperty("localRotation");
                bones.Add((
                    b.GetProperty("name").GetString(),
                    b.GetProperty("parent").GetString(),
                    new AnimVec3(lt[0].GetSingle(), lt[1].GetSingle(), lt[2].GetSingle()),
                    new AnimQuat(lr[0].GetSingle(), lr[1].GetSingle(), lr[2].GetSingle(), lr[3].GetSingle())
                ));
            }

            foreach (var clipName in names)
            {
                string path = System.IO.Path.Combine(animDir, clipName + ".simanim.json");
                string err;
                var clip = CustomAnimLoader.LoadFromFile(path, out err);
                Assert.Null(err);
                Assert.NotNull(clip);
                Assert.Equal(29, clip.Tracks.Count);

                var byBone = new System.Collections.Generic.Dictionary<string, CustomBoneTrack>(StringComparer.OrdinalIgnoreCase);
                foreach (var tr in clip.Tracks) byBone[tr.Bone] = tr;

                for (int fi = 0; fi < clip.FrameCount; fi++)
                {
                    var wPos = new System.Collections.Generic.Dictionary<string, AnimVec3>();
                    var wRot = new System.Collections.Generic.Dictionary<string, AnimQuat>();
                    foreach (var b in bones)
                    {
                        CustomBoneTrack tr;
                        byBone.TryGetValue(b.Name, out tr);
                        AnimVec3 lt = (tr != null && tr.Translations != null && fi < tr.Translations.Length) ? tr.Translations[fi] : b.T;
                        AnimQuat lr = (tr != null && tr.Rotations != null && fi < tr.Rotations.Length) ? tr.Rotations[fi] : b.R;
                        if (b.Parent == "NULL")
                        {
                            wPos[b.Name] = lt;
                            wRot[b.Name] = lr;
                        }
                        else
                        {
                            wPos[b.Name] = wPos[b.Parent] + wRot[b.Parent].Rotate(lt);
                            wRot[b.Name] = (wRot[b.Parent] * lr).Normalized();
                        }
                    }

                    // +X = Sim Left, -X = Sim Right in MonoGame RH adult.skel
                    AssertArmAnatomical(clipName, fi, "L", +1f, wPos["L_ARM1"], wPos["L_ARM2"], wPos["L_HAND"]);
                    AssertArmAnatomical(clipName, fi, "R", -1f, wPos["R_ARM1"], wPos["R_ARM2"], wPos["R_HAND"]);
                }
            }
        }

        static void AssertArmAnatomical(string clip, int frame, string side, float sign, AnimVec3 sh, AnimVec3 el, AnimVec3 hd)
        {
            Assert.True(sign * (el.X - sh.X) >= -0.18f,
                $"{clip} f={frame} {side} elbow crossed inward past shoulder: sh.X={sh.X:F2}, el.X={el.X:F2}");
            Assert.True(sign * hd.X >= -0.22f,
                $"{clip} f={frame} {side} hand crossed opposite flank: hd.X={hd.X:F2}");

            var u = (el - sh).Normalized();
            var l = (hd - el).Normalized();
            float dot = Math.Max(-1f, Math.Min(1f, AnimVec3.Dot(u, l)));
            float bendDeg = (float)(Math.Acos(dot) * 180.0 / Math.PI);
            Assert.InRange(bendDeg, 22f, 135f);
        }
    }
}
