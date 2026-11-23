using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Camera;
using FSO.Content;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Conversations
{
    // Hybrid Real-Time 3D CCTV Renderer for the Law Enforcement Level 1 Security Guard mini-game:
    //   1. Loads 4 real Sims 1 night-time Full 3D lot backdrops embedded in Conversations.dll
    //      and applies an anti-aliased rounded-corner SDF alpha mask matching SimUiTokens.RadiusCamera.
    //   2. Renders live, real-time 60 FPS animated Sims 1 3D SimAvatar actors (Burglar, Pickpocket,
    //      Pizza Delivery, Stray Tabby Cat, El Bandito Raccoon) into a transparent _3DTargetScene
    //      during PreDraw(...) so they composite cleanly over the real lot backdrops.
    internal sealed class CctvSceneRenderer : IDisposable
    {
        static readonly string[] BackdropSuffixes =
        {
            "cam1_gate.png",
            "cam2_dock.png",
            "cam3_vault.png",
            "cam4_alley.png"
        };

        const int TargetW = 384;
        const int TargetH = 216;
        const int BackdropCornerRadiusPx = 12; // Maps to ~10px on ~400px CCTV tiles (SimUiTokens.RadiusCamera)

        sealed class ActorRig
        {
            public SimAvatar Avatar;
            public Animation Anim;
            public bool IsPet;
            public float AnimSpeed = 1f;
            public float BaseScale = 0.5f;
            public bool PatrolWalk;
            public float WalkAmplitude = 1.15f;
            public float WalkFreq = 0.42f;
            public float FacingYaw = 0.35f;
            public string Label;
        }

        GraphicsDevice boundDevice;
        readonly Texture2D[] backdrops = new Texture2D[4];
        bool backdropsLoaded;

        BasicCamera camera;
        _3DTargetScene scene;
        bool sceneInitialized;
        bool sceneFailed;

        readonly Dictionary<SecurityEventKind, ActorRig> rigs = new Dictionary<SecurityEventKind, ActorRig>();
        readonly HashSet<SecurityEventKind> failedKinds = new HashSet<SecurityEventKind>();
        SimAvatar attachedAvatar;
        SecurityEventKind lastRenderedKind = SecurityEventKind.TreeShadow;
        bool hasLiveActorFrame;
        float actorNormalizedX = 0.5f;
        float actorNormalizedY = 0.5f;
        float actorFootNormalizedY = 0.84f;

        public bool HasLiveActorFrame { get { return hasLiveActorFrame && scene != null && scene.Target != null && !scene.Target.IsDisposed; } }
        public Texture2D ActorTarget { get { return scene != null ? scene.Target : null; } }
        public float ActorNormalizedX { get { return actorNormalizedX; } }
        public float ActorNormalizedY { get { return actorNormalizedY; } }
        public float ActorFootNormalizedY { get { return actorFootNormalizedY; } }

        public Texture2D GetBackdrop(GraphicsDevice gd, int camIdx)
        {
            if (camIdx < 0 || camIdx >= 4 || gd == null) return null;
            EnsureDevice(gd);
            if (!backdropsLoaded) LoadBackdrops(gd);
            var tex = backdrops[camIdx];
            return (tex != null && !tex.IsDisposed) ? tex : null;
        }

        public string DiagnosticSummary()
        {
            int loaded = 0;
            for (int i = 0; i < 4; i++)
                if (backdrops[i] != null && !backdrops[i].IsDisposed) loaded++;
            return "backdrops=" + loaded + "/4 live3d=" + HasLiveActorFrame + " kind=" + lastRenderedKind;
        }

        public void PreDraw(GraphicsDevice gd, SecurityWatchGame game)
        {
            hasLiveActorFrame = false;
            if (gd == null || game == null || game.IsFinished) return;

            EnsureDevice(gd);
            if (!backdropsLoaded) LoadBackdrops(gd);

            var ev = game.ActiveEvent;
            if (ev == null || ev.Kind == SecurityEventKind.TreeShadow)
            {
                lastRenderedKind = SecurityEventKind.TreeShadow;
                return;
            }

            if (!EnsureScene(gd)) return;

            var rig = GetOrCreateRig(ev.Kind);
            if (rig == null || rig.Avatar == null) return;

            try
            {
                if (!ReferenceEquals(attachedAvatar, rig.Avatar))
                {
                    if (attachedAvatar != null) scene.Remove(attachedAvatar);
                    scene.Add(rig.Avatar);
                    attachedAvatar = rig.Avatar;
                }

                float t = game.ElapsedSeconds;

                // Advance real Sims 1 Vitaboy animation with smooth sub-frame interpolation
                if (rig.Anim != null)
                {
                    float fps = rig.Anim.FramesPerSecond > 0 ? rig.Anim.FramesPerSecond : 30f;
                    int numFrames = Math.Max(1, rig.Anim.NumFrames);
                    float exact = (t * fps * rig.AnimSpeed) % numFrames;
                    int frame = (int)exact;
                    float frac = exact - frame;
                    Animator.RenderFrame(rig.Avatar, rig.Anim, frame, frac, 1.0f);
                }

                // Compute CCTV-staged world position & heading
                float worldX = 0f;
                float yaw = rig.FacingYaw;
                if (rig.PatrolWalk)
                {
                    // Smooth back-and-forth sweep across the CCTV view so the actor stays framed
                    float phase = t * rig.WalkFreq * (float)(Math.PI * 2.0);
                    float s = (float)Math.Sin(phase);
                    float c = (float)Math.Cos(phase);
                    worldX = s * rig.WalkAmplitude;
                    // Face direction of movement, angled slightly toward the camera so face/suit/stripes read clearly
                    yaw = c >= 0f ? 1.10f : -1.10f;
                    yaw += -s * 0.25f;
                }
                else
                {
                    // Stationary tampering / vault theft with subtle body sway
                    worldX = (float)Math.Sin(t * 0.9f) * 0.12f;
                    yaw = rig.FacingYaw + (float)Math.Sin(t * 1.3f) * 0.18f;
                }

                rig.Avatar.Scale = Vector3.One * rig.BaseScale;
                rig.Avatar.Position = new Vector3(worldX, 0f, 0f);
                rig.Avatar.RotationY = yaw;

                // Crisp IR night-vision illumination on the 3D mesh; phosphor tint is applied during 2D composite
                rig.Avatar.AmbientLight = new Vector4(1.42f, 1.48f, 1.42f, 1.0f);

                if (rig.Avatar.Skeleton != null && rig.Avatar.Skeleton.RootBone != null)
                    rig.Avatar.Skeleton.ComputeBonePositions(rig.Avatar.Skeleton.RootBone, Matrix.Identity);

                // High-angle security-cam framing tailored to human vs pet height so nothing clips the monitor edges
                if (rig.IsPet)
                {
                    camera.Position = new Vector3(0f, 2.30f, 4.70f);
                    camera.Target = new Vector3(0f, 0.48f, 0f);
                }
                else
                {
                    camera.Position = new Vector3(0f, 2.65f, 4.35f);
                    camera.Target = new Vector3(0f, 1.05f, 0f);
                }

                // Exact View * Projection screen coordinates for dead-center tracking brackets & grounded foot shadow
                var viewProj = camera.View * camera.Projection;
                float centerY = rig.IsPet ? 0.28f : 1.08f;
                var bodyClip = Vector4.Transform(new Vector3(worldX, centerY, 0f), viewProj);
                if (Math.Abs(bodyClip.W) > 0.001f)
                {
                    actorNormalizedX = SimLayout.Clamp(0.5f + (bodyClip.X / bodyClip.W) * 0.5f, 0.16f, 0.84f);
                    actorNormalizedY = SimLayout.Clamp(0.5f - (bodyClip.Y / bodyClip.W) * 0.5f, 0.22f, 0.80f);
                }
                var footClip = Vector4.Transform(new Vector3(worldX, 0f, 0f), viewProj);
                if (Math.Abs(footClip.W) > 0.001f)
                {
                    actorFootNormalizedY = SimLayout.Clamp(0.5f - (footClip.Y / footClip.W) * 0.5f, 0.45f, 0.94f);
                }

                // Render the 3D scene off-screen BEFORE UISpriteBatch begins (matches UISelectSkinPanel.PreDraw)
                var prevRast = gd.RasterizerState;
                var prevDepth = gd.DepthStencilState;
                var prevBlend = gd.BlendState;
                try
                {
                    gd.RasterizerState = RasterizerState.CullCounterClockwise;
                    gd.DepthStencilState = DepthStencilState.Default;
                    gd.BlendState = BlendState.Opaque;
                    scene.ClearColor = Color.Transparent;
                    scene.Draw(gd);
                    hasLiveActorFrame = true;
                    lastRenderedKind = ev.Kind;
                }
                finally
                {
                    gd.RasterizerState = prevRast ?? RasterizerState.CullNone;
                    gd.DepthStencilState = prevDepth ?? DepthStencilState.None;
                    gd.BlendState = prevBlend ?? BlendState.AlphaBlend;
                }
            }
            catch (Exception e)
            {
                Boot.Log("cctv3d predraw error (" + ev.Kind + "): " + e.Message);
                failedKinds.Add(ev.Kind);
                hasLiveActorFrame = false;
            }
        }

        void EnsureDevice(GraphicsDevice gd)
        {
            if (ReferenceEquals(boundDevice, gd)) return;
            DisposeResources();
            boundDevice = gd;
        }

        bool EnsureScene(GraphicsDevice gd)
        {
            if (sceneInitialized && scene != null && scene.Target != null && !scene.Target.IsDisposed)
                return true;
            if (sceneFailed) return false;

            try
            {
                camera = new BasicCamera(gd, new Vector3(0f, 3.05f, 5.10f), new Vector3(0f, 1.05f, 0f), Vector3.Up);
                camera.NearPlane = 0.05f;
                camera.FarPlane = 80f;

                // 0 MSAA keeps transparent RenderTarget alpha crisp with zero dark MSAA resolve halos
                scene = new _3DTargetScene(gd, camera, new Point(TargetW, TargetH), 0);
                scene.ClearColor = Color.Transparent;
                if (GameFacade.Scenes != null)
                    scene.Initialize(GameFacade.Scenes);
                sceneInitialized = true;
                return true;
            }
            catch (Exception e)
            {
                Boot.Log("cctv3d scene init error: " + e.Message);
                sceneFailed = true;
                return false;
            }
        }

        void LoadBackdrops(GraphicsDevice gd)
        {
            backdropsLoaded = true;
            try
            {
                var asm = typeof(CctvSceneRenderer).Assembly;
                var resNames = asm.GetManifestResourceNames();

                for (int i = 0; i < BackdropSuffixes.Length; i++)
                {
                    string suffix = BackdropSuffixes[i];
                    string match = resNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                    {
                        Boot.Log("cctv3d missing embedded backdrop: " + suffix);
                        continue;
                    }

                    using (var stream = asm.GetManifestResourceStream(match))
                    {
                        if (stream == null) continue;
                        var tex = Texture2D.FromStream(gd, stream);
                        ApplyRoundedAlphaMask(tex, BackdropCornerRadiusPx);
                        backdrops[i] = tex;
                    }
                }
            }
            catch (Exception e)
            {
                Boot.Log("cctv3d backdrop load error: " + e.Message);
            }
        }

        // Bakes an anti-aliased SDF rounded-corner alpha mask (premultiplied RGBA) directly into the backdrop texture
        // so it nests seamlessly inside SimUiTokens.RadiusCamera rounded bezels without needing a stencil shader.
        static void ApplyRoundedAlphaMask(Texture2D tex, int radius)
        {
            if (tex == null || tex.IsDisposed) return;
            int w = tex.Width;
            int h = tex.Height;
            var data = new Color[w * h];
            tex.GetData(data);

            float r = Math.Max(2f, Math.Min(radius, Math.Min(w, h) * 0.25f));
            float halfW = w * 0.5f;
            float halfH = h * 0.5f;
            float innerX = halfW - r;
            float innerY = halfH - r;

            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f;
                float dy = Math.Abs(py - halfH) - innerY;
                if (dy <= -1f) continue; // Interior rows are untouched

                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f;
                    float dx = Math.Abs(px - halfW) - innerX;
                    if (dx <= -1f) continue;

                    float dist = (float)Math.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f))
                                 + Math.Min(Math.Max(dx, dy), 0f) - r;
                    if (dist <= -0.5f) continue;

                    float cov = SimLayout.Clamp(0.5f - dist, 0f, 1f);
                    int idx = y * w + x;
                    Color c = data[idx];
                    data[idx] = new Color(
                        (byte)Math.Round(c.R * cov),
                        (byte)Math.Round(c.G * cov),
                        (byte)Math.Round(c.B * cov),
                        (byte)Math.Round(255f * cov));
                }
            }

            tex.SetData(data);
        }

        ActorRig GetOrCreateRig(SecurityEventKind kind)
        {
            if (rigs.TryGetValue(kind, out var existing)) return existing;
            if (failedKinds.Contains(kind)) return null;

            try
            {
                var content = Content.Get();
                if (content == null || content.AvatarSkeletons == null) return null;

                ActorRig rig = null;
                switch (kind)
                {
                    case SecurityEventKind.Burglar:
                        rig = BuildHumanRig(content,
                            headApr: "c_burglar.apr", headTex: "c_burglar",
                            bodyApr: "b001mafit_01.apr", bodyTex: "burglarmlgt_01",
                            handSkin: "lgt",
                            animCandidates: new[] { "a2o-burglar-takefromwall.anim", "a2o-walking-frantic-loop.anim", "a2o-walking-loop.anim" },
                            patrolWalk: false, walkAmp: 0f, walkFreq: 0f, animSpeed: 0.88f, facingYaw: 0.52f,
                            label: "Sims 1 Burglar");
                        break;

                    case SecurityEventKind.Vandal:
                        rig = BuildHumanRig(content,
                            headApr: "cvacationma_pickpocket.apr", headTex: "cvacationmalgt_pickpocket",
                            bodyApr: "b001mafit_01.apr", bodyTex: "burglarmdrk_01",
                            handSkin: "drk",
                            animCandidates: new[] { "a2o-burglar-takefromwall.anim", "a2o-walking-loop.anim", "a2o-idle1.anim" },
                            patrolWalk: false, walkAmp: 0f, walkFreq: 0f, animSpeed: 1.05f, facingYaw: -0.48f,
                            label: "Graffiti Vandal");
                        break;

                    case SecurityEventKind.Prowler:
                        rig = BuildHumanRig(content,
                            headApr: "c_burglar.apr", headTex: "c_burglar",
                            bodyApr: "b001mafit_01.apr", bodyTex: "burglarmmed_01",
                            handSkin: "med",
                            animCandidates: new[] { "a2o-walking-frantic-loop.anim", "a2o-walking-loop.anim", "a2o-idle1.anim" },
                            patrolWalk: true, walkAmp: 0.90f, walkFreq: 0.34f, animSpeed: 1.0f, facingYaw: 0.45f,
                            label: "Night Prowler");
                        break;

                    case SecurityEventKind.StrayCat:
                        rig = BuildPetRig(content,
                            skelName: "kat.skel",
                            bodyApr: "b000cat_tabby.apr", bodyTex: "b000cat_tabby",
                            fallbackApr: "b008cat_bombay.apr", fallbackTex: "b008cat_bombay",
                            animCandidates: new[] { "k2o-walking-loop.anim", "k2o-stand-tailswish-loop-slow.anim", "k2o-stand-breathe-loop.anim" },
                            scale: 0.84f, patrolWalk: true, walkAmp: 0.85f, walkFreq: 0.34f, animSpeed: 1.0f,
                            label: "Stray Tabby Cat");
                        break;

                    case SecurityEventKind.Raccoon:
                        rig = BuildPetRig(content,
                            skelName: "dog.skel",
                            bodyApr: "racoon.apr", bodyTex: "racoon",
                            fallbackApr: "b000dog_lab.apr", fallbackTex: "b000dog_lab",
                            animCandidates: new[] { "d2o-raccoon-walk-loop.anim", "d2o-walking-loop.anim", "d2o-sniff-sidetoside.anim" },
                            scale: 0.80f, patrolWalk: true, walkAmp: 0.85f, walkFreq: 0.38f, animSpeed: 1.05f,
                            label: "El Bandito Raccoon");
                        break;
                }

                if (rig != null)
                {
                    rigs[kind] = rig;
                    Boot.Log("cctv3d built rig for " + kind + " (" + rig.Label + ", anim=" + (rig.Anim != null ? rig.Anim.Name : "none") + ")");
                    return rig;
                }
            }
            catch (Exception e)
            {
                Boot.Log("cctv3d rig build error (" + kind + "): " + e.Message);
            }

            failedKinds.Add(kind);
            return null;
        }

        static ActorRig BuildHumanRig(
            Content content,
            string headApr, string headTex,
            string bodyApr, string bodyTex,
            string handSkin,
            string[] animCandidates,
            bool patrolWalk, float walkAmp, float walkFreq, float animSpeed, float facingYaw,
            string label)
        {
            var baseSkel = content.AvatarSkeletons.Get("adult.skel");
            if (baseSkel == null) return null;

            var ava = new SimAvatar(baseSkel.Clone());
            ava.Head = new Outfit
            {
                TS1AppearanceID = headApr,
                TS1TextureID = headTex
            };

            var body = new Outfit
            {
                TS1AppearanceID = bodyApr,
                TS1TextureID = bodyTex,
                LiteralHandgroup = BuildAdultHands(handSkin)
            };
            ava.Body = body;
            ava.Handgroup = body;

            var anim = ResolveAnimation(content, animCandidates);
            return new ActorRig
            {
                Avatar = ava,
                Anim = anim,
                IsPet = false,
                BaseScale = 0.56f,
                PatrolWalk = patrolWalk,
                WalkAmplitude = walkAmp,
                WalkFreq = walkFreq,
                AnimSpeed = animSpeed,
                FacingYaw = facingYaw,
                Label = label
            };
        }

        static ActorRig BuildPetRig(
            Content content,
            string skelName,
            string bodyApr, string bodyTex,
            string fallbackApr, string fallbackTex,
            string[] animCandidates,
            float scale, bool patrolWalk, float walkAmp, float walkFreq, float animSpeed,
            string label)
        {
            var baseSkel = content.AvatarSkeletons.Get(skelName);
            if (baseSkel == null) return null;

            // Verify primary appearance exists before falling back
            string apr = bodyApr;
            string tex = bodyTex;
            try
            {
                if (content.AvatarAppearances != null && content.AvatarAppearances.Get(apr) == null)
                {
                    apr = fallbackApr;
                    tex = fallbackTex;
                }
            }
            catch { }

            var ava = new SimAvatar(baseSkel.Clone());
            ava.Body = new Outfit
            {
                TS1AppearanceID = apr,
                TS1TextureID = tex
            };

            var anim = ResolveAnimation(content, animCandidates);
            return new ActorRig
            {
                Avatar = ava,
                Anim = anim,
                IsPet = true,
                BaseScale = scale,
                PatrolWalk = patrolWalk,
                WalkAmplitude = walkAmp,
                WalkFreq = walkFreq,
                AnimSpeed = animSpeed,
                FacingYaw = 0.6f,
                Label = label
            };
        }

        static HandGroup BuildAdultHands(string skinTone)
        {
            string tone = string.IsNullOrEmpty(skinTone) ? "lgt" : skinTone;
            return new HandGroup
            {
                TS1HandSet = true,
                LightSkin = new HandSet
                {
                    LeftHand = new Hand
                    {
                        Idle = new Gesture { Name = "hmlo.apr", TexName = "huao" + tone },
                        Pointing = new Gesture { Name = "hmlp.apr", TexName = "huap" + tone },
                        Fist = new Gesture { Name = "hmlc.apr", TexName = "huac" + tone }
                    },
                    RightHand = new Hand
                    {
                        Idle = new Gesture { Name = "hmro.apr", TexName = "huao" + tone },
                        Pointing = new Gesture { Name = "hmrp.apr", TexName = "huap" + tone },
                        Fist = new Gesture { Name = "hmrc.apr", TexName = "huac" + tone }
                    }
                }
            };
        }

        static Animation ResolveAnimation(Content content, string[] candidates)
        {
            if (content == null || content.AvatarAnimations == null || candidates == null) return null;
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var a = content.AvatarAnimations.Get(candidates[i]);
                    if (a != null) return a;
                }
                catch { }
            }
            return null;
        }

        void DisposeResources()
        {
            try
            {
                if (scene != null)
                {
                    scene.Dispose();
                    scene = null;
                }
            }
            catch { }

            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (backdrops[i] != null && !backdrops[i].IsDisposed)
                        backdrops[i].Dispose();
                }
                catch { }
                backdrops[i] = null;
            }

            rigs.Clear();
            failedKinds.Clear();
            attachedAvatar = null;
            backdropsLoaded = false;
            sceneInitialized = false;
            sceneFailed = false;
            hasLiveActorFrame = false;
        }

        public void Dispose()
        {
            DisposeResources();
            boundDevice = null;
        }
    }
}
