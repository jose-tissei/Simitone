using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FSO.Client;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Camera;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Conversations
{
    // Unified Real-Time 3D Academy World Renderer for Law Enforcement Level 2 Cadet (DISP-05).
    // Uses the Option 2 unified-world principle: BOTH the 3D Police Academy training yard
    // (grass field, terracotta track, hurdles, brick-and-timber vault wall, firing range berm,
    // hinged steel bullseye targets, cones, floodlight towers, grounded contact shadow) AND the
    // player's actual 3D SimAvatar (wearing their authentic TS1 Law Enforcement Career Uniform!)
    // render into ONE shared RenderTarget2D + Depth24 Z-buffer through ONE dynamic 3D follow-camera.
    internal sealed class CadetAcademySceneRenderer : IDisposable
    {
        public const int TargetW = 760;
        public const int TargetH = 340;

        // Standard adult Vitaboy height (~5.45 units at scale 1.0) -> ~3.0 world units tall at 0.55
        const float SimScale = 0.55f;

        GraphicsDevice boundDevice;
        RenderTarget2D renderTarget;
        BasicEffect worldEffect;
        BasicCamera camera;
        _3DScene avatarScene;

        SimAvatar cadetAvatar;
        string boundSimKey;
        string boundUniformTag = "none";
        string lastActiveAnimName = "none";
        string lastAnimSource = "none";
        float lastElapsedSeconds = -1f;
        readonly Stopwatch sceneStopwatch = Stopwatch.StartNew();
        double lastWallSeconds = -1.0;
        float finishExtraSeconds;
        readonly VitaboyClipPlayer customPlayer = new VitaboyClipPlayer();
        Animation runAnim;
        Animation walkAnim;
        Animation idleAnim;
        Animation jumpAnim;
        Animation shootAnim;
        Animation cheerAnim;

        bool initialized;
        bool initFailed;
        bool hasFrame;

        // Reusable geometry buffer per frame (zero GC churn after warm-up)
        readonly List<VertexPositionColorNormal> litTris = new List<VertexPositionColorNormal>(4096);
        readonly List<VertexPositionColor> unlitTris = new List<VertexPositionColor>(1024);
        VertexPositionColorNormal[] litArray = new VertexPositionColorNormal[0];
        VertexPositionColor[] unlitArray = new VertexPositionColor[0];

        struct VertexPositionColorNormal : IVertexType
        {
            public Vector3 Position;
            public Color Color;
            public Vector3 Normal;

            public static readonly VertexDeclaration VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 0),
                new VertexElement(16, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0));

            VertexDeclaration IVertexType.VertexDeclaration { get { return VertexDeclaration; } }

            public VertexPositionColorNormal(Vector3 pos, Color col, Vector3 norm)
            {
                Position = pos;
                Color = col;
                Normal = norm;
            }
        }

        public bool HasLiveFrame { get { return hasFrame && renderTarget != null && !renderTarget.IsDisposed; } }
        public Texture2D TargetTexture { get { return renderTarget; } }

        public string DiagnosticSummary()
        {
            return "live3d=" + HasLiveFrame +
                   " sim=" + (cadetAvatar != null) +
                   " key=" + (boundSimKey ?? "none") +
                   " uniform=" + boundUniformTag +
                   " source=" + lastAnimSource +
                   " anim=" + lastActiveAnimName +
                   " run=" + (runAnim != null ? runAnim.Name : "none") +
                   " idle=" + (idleAnim != null ? idleAnim.Name : "none") +
                   " jump=" + (jumpAnim != null ? jumpAnim.Name : "none") +
                   " cheer=" + (cheerAnim != null ? cheerAnim.Name : "none") +
                   (string.IsNullOrEmpty(lastAvatarErr) ? "" : " err=" + lastAvatarErr);
        }

        public void PreDraw(GraphicsDevice gd, CadetAcademyGame game, VMAvatar sourceSim)
        {
            hasFrame = false;
            if (gd == null || game == null) return;

            EnsureDevice(gd);
            if (!EnsureResources(gd)) return;
            EnsureCadetAvatar(sourceSim);

            try
            {
                var st = game.ActiveStage;
                // Briefing & Victory Scorecard both render on the open sunlit Sprint track so the camera
                // always has an unobstructed heroic view (never behind TargetDrill's sand backstop berm!).
                var kind = (game.IsBriefing || game.IsFinished)
                    ? CadetDrillStageKind.Sprint
                    : (st != null ? st.Kind : CadetDrillStageKind.Sprint);
                double wallNow = sceneStopwatch.Elapsed.TotalSeconds;
                float wallDt = lastWallSeconds >= 0.0
                    ? (float)Math.Max(0.0, Math.Min(0.10, wallNow - lastWallSeconds))
                    : (1f / 30f);
                lastWallSeconds = wallNow;

                float gameT = game.ElapsedSeconds;
                if (game.IsFinished)
                    finishExtraSeconds += wallDt;
                else
                    finishExtraSeconds = 0f;

                float t = gameT + finishExtraSeconds;
                float dt = (lastElapsedSeconds >= 0f && t >= lastElapsedSeconds)
                    ? Math.Min(0.10f, t - lastElapsedSeconds)
                    : wallDt;
                if (dt <= 0.0001f && game.IsFinished)
                    dt = wallDt;
                lastElapsedSeconds = t;
                float resP = game.ResolutionProgress;
                bool resolving = game.IsResolvingStage;
                var rating = st != null ? st.Rating : CadetHitRating.None;

                // 1. Compute real-time 3D Sim position, height, rotation, animation, & dynamic 3D camera
                Vector3 simPos;
                float simYaw;
                float simPitch = 0f;
                string customClipName = "cadet_sprint_loop";
                float customFade = 0.16f;
                float customSpeed = 1.0f;
                bool? customLoop = null;
                float? customSeekTime = null;
                Animation activeAnim = runAnim ?? walkAnim ?? idleAnim;
                float animSpeed = 1.15f;
                float animTime = t;
                float targetKnockAngle = 0f;
                bool showShotFlash = false;
                Vector3 shotImpactPos = new Vector3(0f, 1.75f, 7.45f);

                if (game.IsBriefing)
                {
                    // Pre-drill establishing shot: Cadet stands sharp at attention at the starting line (z = -7.8)
                    // in full Police Cadet uniform; camera orbits cleanly so Bob fits head-to-boots inside Briefing.
                    simPos = new Vector3(0f, 0f, -7.8f);
                    simYaw = 0.24f + (float)Math.Sin(t * 0.65f) * 0.12f;
                    customClipName = "cadet_idle_attention";
                    customFade = 0.18f;
                    customSpeed = 1.0f;
                    activeAnim = idleAnim ?? walkAnim ?? runAnim;
                    animSpeed = 0.85f;
                    float orbit = (float)Math.Sin(t * 0.45f) * 0.90f;
                    camera.Position = new Vector3(3.6f + orbit, 2.85f, 1.35f);
                    camera.Target = new Vector3(0f, 1.56f, -7.8f);
                }
                else if (game.IsFinished)
                {
                    // Victory showcase right past the Police Academy Finish Gantry (arch at z = 10.0)
                    // with crisp Police Academy right-hand temple salute!
                    simPos = new Vector3(0f, 0f, 11.4f);
                    simYaw = 0.18f + (float)Math.Sin(t * 0.85f) * 0.10f;
                    customClipName = "cadet_victory_salute";
                    customFade = 0.20f;
                    customSpeed = 1.0f;
                    activeAnim = cheerAnim ?? idleAnim ?? walkAnim;
                    animSpeed = 0.95f;
                    float sway = (float)Math.Sin(t * 0.50f) * 0.85f;
                    camera.Position = new Vector3(2.8f + sway, 2.75f, 18.2f);
                    camera.Target = new Vector3(0f, 1.72f, 10.8f);
                }
                else if (kind == CadetDrillStageKind.Sprint)
                {
                    if (!resolving)
                    {
                        // Continuous athletic sprint along the terracotta track:
                        // cadet_sprint_loop encodes rootSpeedUnitsPerSec=10.9 -> 10.9 * 0.55 SimScale == 6.0 world u/s (zero skating!)
                        float cycle = (t * 6.0f) % 15.5f;
                        float z = -8.5f + cycle;
                        float hop = ComputeHurdleHop(z, 2.0f) + ComputeHurdleHop(z, 6.2f);
                        simPos = new Vector3(0f, hop, z);
                        simYaw = 0f;
                        if (hop > 0.12f)
                        {
                            customClipName = "cadet_hurdle_hop";
                            customFade = 0.10f;
                            customSpeed = 1.0f;
                            customLoop = false;
                        }
                        else
                        {
                            customClipName = "cadet_sprint_loop";
                            customFade = 0.12f;
                            customSpeed = 1.0f;
                            customLoop = true;
                        }
                        simPitch = hop > 0.25f ? 0.14f : 0.08f;
                        activeAnim = hop > 0.35f ? (jumpAnim ?? runAnim ?? walkAnim) : (runAnim ?? walkAnim ?? idleAnim);
                        animSpeed = hop > 0.35f ? 1.15f : 1.75f;
                    }
                    else
                    {
                        float burst = rating == CadetHitRating.Perfect ? 13.5f : (rating == CadetHitRating.Good ? 10.5f : 6.0f);
                        float z = 1.5f + resP * burst;
                        float hop = rating == CadetHitRating.Miss
                            ? (resP < 0.35f ? (float)Math.Sin(resP / 0.35f * Math.PI) * 0.25f : 0f)
                            : ComputeHurdleHop(z, 6.2f);
                        simPos = new Vector3(0f, hop, Math.Min(13.5f, z));
                        simYaw = 0f;
                        if (resP > 0.72f && rating == CadetHitRating.Perfect)
                        {
                            customClipName = "cadet_victory_salute";
                            customFade = 0.16f;
                            customSpeed = 1.0f;
                        }
                        else if (hop > 0.12f)
                        {
                            customClipName = "cadet_hurdle_hop";
                            customFade = 0.09f;
                            customSpeed = 1.15f;
                            customLoop = false;
                        }
                        else
                        {
                            customClipName = "cadet_sprint_loop";
                            customFade = 0.12f;
                            // Sync cadence proportionally to burst speed so feet never skate!
                            customSpeed = rating == CadetHitRating.Perfect ? 1.25f : (rating == CadetHitRating.Good ? 1.05f : 0.72f);
                            customLoop = true;
                        }
                        activeAnim = (resP > 0.72f && rating == CadetHitRating.Perfect && cheerAnim != null)
                            ? cheerAnim
                            : (hop > 0.35f ? (jumpAnim ?? runAnim ?? walkAnim) : (runAnim ?? walkAnim ?? idleAnim));
                        animSpeed = rating == CadetHitRating.Perfect ? 2.05f : (rating == CadetHitRating.Good ? 1.70f : 1.15f);
                        simPitch = (rating == CadetHitRating.Miss && resP < 0.45f)
                            ? 0.24f * (float)Math.Sin(resP / 0.45f * Math.PI)
                            : ((resP > 0.72f && rating == CadetHitRating.Perfect) ? 0f : 0.10f);
                    }

                    // Dynamic 3/4 side-front tracking camera glides smoothly beside the sprinter
                    camera.Position = new Vector3(simPos.X + 6.4f, 3.55f, simPos.Z + 5.2f);
                    camera.Target = new Vector3(simPos.X, 1.55f, simPos.Z + 1.2f);
                }
                else if (kind == CadetDrillStageKind.Vault)
                {
                    if (!resolving)
                    {
                        // Energetic approach run toward the timber-topped brick vault wall (at z = +2.0)
                        float cycle = (t * 5.2f) % 7.5f;
                        float z = -6.8f + cycle;
                        simPos = new Vector3(0f, 0f, z);
                        simYaw = 0f;
                        customClipName = "cadet_sprint_loop";
                        customFade = 0.14f;
                        customSpeed = 5.2f / 6.0f; // exact stride-to-speed match!
                        simPitch = 0.08f;
                        activeAnim = runAnim ?? walkAnim ?? idleAnim;
                        animSpeed = 1.70f;
                    }
                    else if (rating == CadetHitRating.Miss)
                    {
                        // Clips the top of the wall and stumbles backward onto the track
                        if (resP < 0.45f)
                        {
                            float p = resP / 0.45f;
                            simPos = new Vector3(0f, (float)Math.Sin(p * Math.PI * 0.65f) * 1.35f, -1.2f + p * 2.45f);
                            simPitch = 0.25f * p;
                            customClipName = "cadet_wall_vault";
                            customFade = 0.08f;
                            customLoop = false;
                            customSeekTime = p * 0.35f;
                        }
                        else
                        {
                            float p = (resP - 0.45f) / 0.55f;
                            simPos = new Vector3(0f, Math.Max(0f, (1f - p) * 1.15f), 1.25f - p * 1.85f);
                            simPitch = -0.18f * (1f - p);
                            customClipName = "cadet_idle_attention";
                            customFade = 0.18f;
                        }
                        simYaw = 0f;
                        activeAnim = walkAnim ?? runAnim ?? idleAnim;
                        animSpeed = 0.85f;
                    }
                    else
                    {
                        // Clean parabolic parkour speed-vault right over the 2.15-unit timber wall onto the blue mat!
                        float peakH = rating == CadetHitRating.Perfect ? 2.45f : 2.20f;
                        float endZ = rating == CadetHitRating.Perfect ? 5.6f : 4.7f;
                        float z = -1.6f + resP * (endZ + 1.6f);
                        bool airborne = resP >= 0.12f && resP <= 0.84f;
                        float arc = (float)Math.Sin(Math.Min(1f, Math.Max(0f, (resP - 0.12f) / 0.72f)) * Math.PI);
                        float y = airborne ? arc * peakH : 0f;
                        simPos = new Vector3(0f, y, z);
                        simYaw = 0f;

                        if (resP > 0.86f && rating == CadetHitRating.Perfect)
                        {
                            customClipName = "cadet_victory_salute";
                            customFade = 0.16f;
                            customSpeed = 1.0f;
                        }
                        else
                        {
                            customClipName = "cadet_wall_vault";
                            customFade = 0.08f;
                            customLoop = false;
                            // Sync vault hand-plant -> tucked knee clearance -> landing crouch directly to resP!
                            customSeekTime = Math.Min(0.98f, resP * 1.05f);
                        }

                        activeAnim = airborne
                            ? (jumpAnim ?? runAnim ?? walkAnim)
                            : ((resP > 0.84f && rating == CadetHitRating.Perfect && cheerAnim != null) ? cheerAnim : (runAnim ?? walkAnim));
                        animSpeed = airborne ? 1.0f : 1.25f;
                        if (airborne)
                        {
                            animTime = (resP - 0.12f) * 1.15f;
                            simPitch = 0.30f * (float)Math.Cos(((resP - 0.12f) / 0.72f) * Math.PI);
                        }
                    }

                    // Heroic side-quarter vault camera with generous headroom above the timber wall
                    camera.Position = new Vector3(9.2f, 4.05f, 2.4f);
                    camera.Target = new Vector3(0f, 2.25f, 1.9f);
                }
                else // TargetDrill
                {
                    simPos = new Vector3(0f, 0f, -2.8f);
                    // Subtle aim sweep linked directly to the live timing marker!
                    float aimOffset = (game.MarkerPosition - 0.5f) * 0.45f;
                    simYaw = resolving ? 0f : aimOffset;

                    if (resolving && resP < 0.50f)
                    {
                        customClipName = "cadet_target_fire_recoil";
                        customFade = 0.04f;
                        customLoop = false;
                        customSeekTime = Math.Min(0.49f, resP * 1.05f);
                        activeAnim = shootAnim ?? idleAnim;
                        animSpeed = 1.45f;
                        animTime = resP * 1.35f;
                    }
                    else if (resolving && resP >= 0.50f && rating == CadetHitRating.Perfect)
                    {
                        customClipName = "cadet_victory_salute";
                        customFade = 0.18f;
                        customSpeed = 1.0f;
                        activeAnim = cheerAnim ?? idleAnim;
                        animSpeed = 1.0f;
                    }
                    else
                    {
                        customClipName = "cadet_target_aim_loop";
                        customFade = 0.14f;
                        customSpeed = 1.0f;
                        activeAnim = idleAnim ?? walkAnim ?? runAnim;
                        animSpeed = 0.80f;
                    }

                    if (resolving)
                    {
                        showShotFlash = resP < 0.24f;
                        if (rating == CadetHitRating.Perfect)
                        {
                            // Steel target snaps back 88 degrees flat!
                            float k = Math.Min(1f, resP / 0.32f);
                            targetKnockAngle = k * k * 1.52f;
                            shotImpactPos = new Vector3(0f, 1.72f, 7.42f);
                        }
                        else if (rating == CadetHitRating.Good)
                        {
                            float k = Math.Min(1f, resP / 0.38f);
                            targetKnockAngle = k * 0.92f + (float)Math.Sin(resP * 14f) * 0.08f * (1f - resP);
                            shotImpactPos = new Vector3(0.32f, 1.98f, 7.42f);
                        }
                        else
                        {
                            shotImpactPos = new Vector3(1.55f, 1.85f, 7.42f);
                        }
                    }

                    // Over-the-shoulder academy firing range view framing both Cadet and all 3 steel targets
                    camera.Position = new Vector3(4.3f, 3.35f, -7.6f);
                    camera.Target = new Vector3(0f, 1.62f, 5.2f);
                }

                // 2. Build all 3D Academy Training Yard meshes for this frame
                litTris.Clear();
                unlitTris.Clear();
                BuildAcademyEnvironment(kind, simPos, targetKnockAngle, showShotFlash, shotImpactPos, t);

                // 3. Pose & animate the real 3D Vitaboy SimAvatar in the EXACT SAME 3D coordinates!
                if (cadetAvatar != null)
                {
                    bool usedCustom = false;
                    if (!string.IsNullOrEmpty(customClipName) &&
                        customPlayer.Play(customClipName, customFade, customSpeed, customLoop))
                    {
                        usedCustom = customPlayer.UpdateAndApply(
                            cadetAvatar, dt, useSquad: true, seekTime: customSeekTime);
                    }

                    if (usedCustom)
                    {
                        lastAnimSource = "custom";
                        lastActiveAnimName = customPlayer.ActiveClipName;
                        // Athletic forward lean & vault body pitch are baked natively into PELVIS/SPINE bones —
                        // ZERO whole-mesh simPitch tilt hack!
                        simPitch = 0f;
                    }
                    else
                    {
                        lastAnimSource = "ts1";
                        ResetSkeletonToBase(cadetAvatar);
                        lastActiveAnimName = activeAnim != null ? activeAnim.Name : "none";
                        if (activeAnim != null)
                        {
                            float fps = activeAnim.FramesPerSecond > 0 ? activeAnim.FramesPerSecond : 30f;
                            int numFrames = Math.Max(1, activeAnim.NumFrames);
                            float exact = (Math.Max(0f, animTime) * fps * animSpeed) % numFrames;
                            int frame = (int)exact;
                            Animator.RenderFrame(cadetAvatar, activeAnim, frame, exact - frame, 1.0f);
                        }
                    }

                    cadetAvatar.Scale = Vector3.One * SimScale;
                    cadetAvatar.Position = simPos;
                    cadetAvatar.RotationY = simYaw;
                    cadetAvatar.RotationX = simPitch;
                    cadetAvatar.AmbientLight = new Vector4(1.18f, 1.16f, 1.12f, 1.0f);

                    // CRITICAL: ReloadSkeleton() recomputes bone matrices AND refreshes SkelBones for the GPU skinning shader!
                    cadetAvatar.ReloadSkeleton();
                }

                // 4. Render unified 3D world + SimAvatar into our RenderTarget2D with full Depth24 Z-buffering
                var prevTargets = gd.GetRenderTargets();
                var prevVp = gd.Viewport;
                var prevRast = gd.RasterizerState;
                var prevDepth = gd.DepthStencilState;
                var prevBlend = gd.BlendState;

                try
                {
                    gd.SetRenderTarget(renderTarget);
                    gd.Viewport = new Viewport(0, 0, TargetW, TargetH);
                    camera.AspectRatioMultiplier = 1f;
                    gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(116, 174, 228), 1f, 0);

                    gd.RasterizerState = RasterizerState.CullNone;
                    gd.DepthStencilState = DepthStencilState.Default;
                    gd.BlendState = BlendState.Opaque;

                    // Draw sunlit 3D Academy world meshes
                    worldEffect.World = Matrix.Identity;
                    worldEffect.View = camera.View;
                    worldEffect.Projection = camera.Projection;
                    worldEffect.VertexColorEnabled = true;
                    worldEffect.TextureEnabled = false;
                    worldEffect.LightingEnabled = true;
                    worldEffect.AmbientLightColor = new Vector3(0.56f, 0.60f, 0.66f);
                    worldEffect.DirectionalLight0.Enabled = true;
                    worldEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.55f, -0.82f, -0.35f));
                    worldEffect.DirectionalLight0.DiffuseColor = new Vector3(0.88f, 0.86f, 0.80f);
                    worldEffect.DirectionalLight0.SpecularColor = Vector3.Zero;
                    worldEffect.DirectionalLight1.Enabled = true;
                    worldEffect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.6f, -0.35f, 0.65f));
                    worldEffect.DirectionalLight1.DiffuseColor = new Vector3(0.24f, 0.30f, 0.42f);
                    worldEffect.DirectionalLight1.SpecularColor = Vector3.Zero;
                    worldEffect.DirectionalLight2.Enabled = false;

                    if (litTris.Count >= 3)
                    {
                        if (litArray.Length < litTris.Count)
                            litArray = new VertexPositionColorNormal[Math.Max(litTris.Count, 2048)];
                        litTris.CopyTo(litArray, 0);
                        foreach (var pass in worldEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            gd.DrawUserPrimitives(PrimitiveType.TriangleList, litArray, 0, litTris.Count / 3);
                        }
                    }

                    // Draw unlit emissive accents (contact shadow, lane chalk glow, shot flash, floodlight bulbs)
                    if (unlitTris.Count >= 3)
                    {
                        worldEffect.LightingEnabled = false;
                        gd.BlendState = BlendState.AlphaBlend;
                        if (unlitArray.Length < unlitTris.Count)
                            unlitArray = new VertexPositionColor[Math.Max(unlitTris.Count, 512)];
                        unlitTris.CopyTo(unlitArray, 0);
                        foreach (var pass in worldEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            gd.DrawUserPrimitives(PrimitiveType.TriangleList, unlitArray, 0, unlitTris.Count / 3);
                        }
                    }

                    // Draw the player's real 3D Vitaboy SimAvatar into the EXACT SAME Depth24 Z-buffer!
                    if (cadetAvatar != null && avatarScene != null)
                    {
                        gd.RasterizerState = RasterizerState.CullCounterClockwise;
                        gd.DepthStencilState = DepthStencilState.Default;
                        gd.BlendState = BlendState.Opaque;
                        avatarScene.Draw(gd);
                    }

                    hasFrame = true;
                }
                finally
                {
                    if (prevTargets == null || prevTargets.Length == 0)
                        gd.SetRenderTarget(null);
                    else
                        gd.SetRenderTargets(prevTargets);
                    gd.Viewport = prevVp;
                    gd.RasterizerState = prevRast ?? RasterizerState.CullNone;
                    gd.DepthStencilState = prevDepth ?? DepthStencilState.None;
                    gd.BlendState = prevBlend ?? BlendState.AlphaBlend;
                }
            }
            catch (Exception e)
            {
                Boot.Log("cadet3d predraw error: " + e.Message);
                hasFrame = false;
            }
        }

        static float ComputeHurdleHop(float simZ, float hurdleZ)
        {
            float d = Math.Abs(simZ - hurdleZ);
            if (d >= 1.55f) return 0f;
            float u = 1f - d / 1.55f;
            return (float)Math.Sin(u * Math.PI * 0.5f) * 1.15f;
        }

        void BuildAcademyEnvironment(
            CadetDrillStageKind kind,
            Vector3 simPos,
            float targetKnockAngle,
            bool showShotFlash,
            Vector3 shotImpactPos,
            float elapsed)
        {
            // 1. Mowed green academy turf field (striped for crisp speed & depth cues!)
            for (int iz = -22; iz < 36; iz += 4)
            {
                Color grass = ((iz / 4) % 2 == 0) ? new Color(78, 134, 72) : new Color(68, 120, 64);
                AddQuadY(-26f, 26f, iz, iz + 4f, 0f, grass);
            }

            // 2. Terracotta running track surface + crisp white chalk lane dividers
            AddQuadY(-4.4f, 4.4f, -18f, 30f, 0.012f, new Color(166, 78, 58));
            // Concrete track curbs
            AddBox(new Vector3(-4.65f, 0.08f, 6f), new Vector3(0.5f, 0.16f, 48f), new Color(188, 194, 200));
            AddBox(new Vector3(4.65f, 0.08f, 6f), new Vector3(0.5f, 0.16f, 48f), new Color(188, 194, 200));

            float[] laneXs = { -3.9f, -1.35f, 1.35f, 3.9f };
            for (int i = 0; i < laneXs.Length; i++)
                AddQuadY(laneXs[i] - 0.06f, laneXs[i] + 0.06f, -17.5f, 29.5f, 0.022f, new Color(242, 245, 248));

            // 3. Perimeter concrete wall + navy police academy trim + distant skyline silhouettes
            AddBox(new Vector3(-11.5f, 1.1f, 6f), new Vector3(0.5f, 2.2f, 54f), new Color(160, 168, 178));
            AddBox(new Vector3(-11.5f, 2.25f, 6f), new Vector3(0.62f, 0.22f, 54f), new Color(40, 82, 148));
            AddBox(new Vector3(11.5f, 1.1f, 6f), new Vector3(0.5f, 2.2f, 54f), new Color(160, 168, 178));
            AddBox(new Vector3(11.5f, 2.25f, 6f), new Vector3(0.62f, 0.22f, 54f), new Color(40, 82, 148));
            // Back stadium wall with gold academy crest stripe
            AddBox(new Vector3(0f, 1.8f, 31f), new Vector3(24f, 3.6f, 0.7f), new Color(56, 78, 114));
            AddBox(new Vector3(0f, 3.1f, 30.6f), new Vector3(22f, 0.32f, 0.18f), new Color(235, 192, 66));

            // Tall stadium floodlight towers along both flanks
            float[] poleZs = { -6f, 8f, 22f };
            for (int i = 0; i < poleZs.Length; i++)
            {
                AddLightTower(-10.2f, poleZs[i]);
                AddLightTower(10.2f, poleZs[i]);
            }

            // Orange training cones along the track edge
            for (float cz = -10f; cz <= 14f; cz += 4.5f)
            {
                AddCone(-5.25f, cz);
                AddCone(5.25f, cz);
            }

            // 4. Stage-specific interactive 3D obstacles & props!
            if (kind == CadetDrillStageKind.Sprint)
            {
                // Two track hurdles (leaving z = -8.5 .. +1.5 completely clear for Briefing & Sprint takeoff)
                AddHurdle(2.0f);
                AddHurdle(6.2f);

                // Checkered finish line + Police Academy finish gantry arch at z = 10.0
                for (int sq = -4; sq < 4; sq++)
                {
                    Color c1 = (sq % 2 == 0) ? new Color(245, 248, 252) : new Color(30, 36, 46);
                    Color c2 = (sq % 2 == 0) ? new Color(30, 36, 46) : new Color(245, 248, 252);
                    AddQuadY(sq * 1.0f, (sq + 1) * 1.0f, 9.5f, 10.0f, 0.028f, c1);
                    AddQuadY(sq * 1.0f, (sq + 1) * 1.0f, 10.0f, 10.5f, 0.028f, c2);
                }
                AddBox(new Vector3(-4.5f, 2.3f, 10.0f), new Vector3(0.35f, 4.6f, 0.35f), new Color(36, 74, 134));
                AddBox(new Vector3(4.5f, 2.3f, 10.0f), new Vector3(0.35f, 4.6f, 0.35f), new Color(36, 74, 134));
                AddBox(new Vector3(0f, 4.35f, 10.0f), new Vector3(9.6f, 0.85f, 0.42f), new Color(28, 62, 118));
                AddBox(new Vector3(0f, 4.35f, 9.75f), new Vector3(8.6f, 0.16f, 0.08f), new Color(242, 198, 68));
                AddBox(new Vector3(0f, 4.35f, 10.25f), new Vector3(8.6f, 0.16f, 0.08f), new Color(242, 198, 68));
            }
            else if (kind == CadetDrillStageKind.Vault)
            {
                // Approach chalk takeoff box
                AddQuadY(-2.4f, 2.4f, -0.4f, 0.8f, 0.026f, new Color(242, 206, 86));

                // Brick-and-timber Academy Vault Wall at z = 2.0 (height = 2.15 vs 3.0-tall Sim!)
                AddBox(new Vector3(0f, 0.92f, 2.0f), new Vector3(5.8f, 1.84f, 0.82f), new Color(152, 74, 58));
                // Horizontal mortar accent stripes on the brick face
                AddBox(new Vector3(0f, 0.55f, 1.58f), new Vector3(5.82f, 0.06f, 0.04f), new Color(196, 186, 174));
                AddBox(new Vector3(0f, 1.15f, 1.58f), new Vector3(5.82f, 0.06f, 0.04f), new Color(196, 186, 174));
                // Heavy timber top rail
                AddBox(new Vector3(0f, 2.00f, 2.0f), new Vector3(6.1f, 0.30f, 0.96f), new Color(188, 136, 74));
                // Side steel support brackets
                AddBox(new Vector3(-3.0f, 1.05f, 2.0f), new Vector3(0.28f, 2.1f, 1.15f), new Color(54, 68, 86));
                AddBox(new Vector3(3.0f, 1.05f, 2.0f), new Vector3(0.28f, 2.1f, 1.15f), new Color(54, 68, 86));

                // Padded navy blue landing mat behind the wall
                AddBox(new Vector3(0f, 0.12f, 4.9f), new Vector3(5.6f, 0.24f, 4.0f), new Color(42, 92, 168));
                AddQuadY(-2.5f, 2.5f, 3.2f, 6.6f, 0.25f, new Color(58, 114, 198));
            }
            else // TargetDrill
            {
                // Firing line wooden barrier & stall posts
                AddBox(new Vector3(0f, 0.04f, -1.5f), new Vector3(8.2f, 0.08f, 0.32f), new Color(240, 202, 74));
                AddBox(new Vector3(-3.8f, 1.1f, -1.5f), new Vector3(0.22f, 2.2f, 0.22f), new Color(138, 98, 60));
                AddBox(new Vector3(3.8f, 1.1f, -1.5f), new Vector3(0.22f, 2.2f, 0.22f), new Color(138, 98, 60));

                // Sloped earth & timber range backstop berm behind the targets
                AddBox(new Vector3(0f, 1.7f, 11.4f), new Vector3(16.5f, 3.4f, 2.6f), new Color(134, 108, 76));
                AddBox(new Vector3(0f, 0.45f, 9.9f), new Vector3(14.5f, 0.9f, 0.7f), new Color(166, 138, 98));

                // 3 Steel Silhouette Targets with bright red bullseyes (center one hinges backward on hit!)
                AddSteelTarget(-3.0f, 7.5f, 0f, false);
                AddSteelTarget(0.0f, 7.5f, targetKnockAngle, true);
                AddSteelTarget(3.0f, 7.5f, 0f, false);

                // Crisp golden shot tracer + impact star flash when player locks in
                if (showShotFlash)
                {
                    Vector3 muzzle = new Vector3(simPos.X, 2.45f, simPos.Z + 0.85f);
                    AddUnlitBeam(muzzle, shotImpactPos, 0.06f, new Color(255, 232, 110, 235));
                    AddUnlitDiamond(shotImpactPos, 0.46f + (float)Math.Sin(elapsed * 45f) * 0.08f, new Color(255, 240, 140, 245));
                }
            }

            // 5. Grounded 3D contact shadow beneath the Sim's feet (scales down dynamically during Vault leap!)
            float shadowScale = Math.Max(0.32f, 1.0f - simPos.Y * 0.22f);
            byte shadowAlpha = (byte)Math.Round(135f * shadowScale);
            AddUnlitDisc(simPos.X, 0.032f, simPos.Z, 0.85f * shadowScale, new Color((byte)10, (byte)16, (byte)24, shadowAlpha));
        }

        void AddHurdle(float z)
        {
            // Two white side legs + red-and-white striped crossbar (height 0.96)
            AddBox(new Vector3(-1.35f, 0.48f, z), new Vector3(0.10f, 0.96f, 0.12f), new Color(236, 240, 245));
            AddBox(new Vector3(1.35f, 0.48f, z), new Vector3(0.10f, 0.96f, 0.12f), new Color(236, 240, 245));
            AddBox(new Vector3(-1.35f, 0.05f, z - 0.25f), new Vector3(0.12f, 0.10f, 0.65f), new Color(210, 216, 224));
            AddBox(new Vector3(1.35f, 0.05f, z - 0.25f), new Vector3(0.12f, 0.10f, 0.65f), new Color(210, 216, 224));
            AddBox(new Vector3(0f, 0.94f, z), new Vector3(2.85f, 0.16f, 0.09f), new Color(244, 246, 250));
            AddBox(new Vector3(-0.75f, 0.94f, z - 0.05f), new Vector3(0.55f, 0.17f, 0.03f), new Color(218, 56, 56));
            AddBox(new Vector3(0.75f, 0.94f, z - 0.05f), new Vector3(0.55f, 0.17f, 0.03f), new Color(218, 56, 56));
        }

        void AddSteelTarget(float x, float z, float tiltBackRad, bool isCenter)
        {
            // Base plate stays fixed on the ground
            AddBox(new Vector3(x, 0.08f, z), new Vector3(1.25f, 0.16f, 0.95f), new Color(68, 76, 88));

            // Pivot the target post + silhouette backward around (x, 0.16, z) when hit!
            var pivot = new Vector3(x, 0.16f, z);
            var rot = Matrix.CreateRotationX(tiltBackRad);

            // Steel stand pole
            AddTransformedBox(new Vector3(0f, 0.72f, 0f), new Vector3(0.14f, 1.44f, 0.14f), pivot, rot, new Color(86, 94, 108));
            // Silhouette torso plate
            Color steel = isCenter ? new Color(46, 54, 68) : new Color(62, 72, 86);
            AddTransformedBox(new Vector3(0f, 1.68f, 0f), new Vector3(1.12f, 1.32f, 0.12f), pivot, rot, steel);
            // Silhouette head plate
            AddTransformedBox(new Vector3(0f, 2.52f, 0f), new Vector3(0.46f, 0.46f, 0.12f), pivot, rot, steel);
            // Concentric white ring + vivid red bullseye facing the shooter (-Z)
            AddTransformedBox(new Vector3(0f, 1.66f, -0.07f), new Vector3(0.68f, 0.68f, 0.03f), pivot, rot, new Color(244, 246, 250));
            AddTransformedBox(new Vector3(0f, 1.66f, -0.09f), new Vector3(0.36f, 0.36f, 0.03f), pivot, rot, isCenter ? new Color(236, 48, 56) : new Color(190, 74, 74));
        }

        void AddCone(float x, float z)
        {
            AddBox(new Vector3(x, 0.05f, z), new Vector3(0.56f, 0.10f, 0.56f), new Color(234, 106, 36));
            AddBox(new Vector3(x, 0.28f, z), new Vector3(0.36f, 0.38f, 0.36f), new Color(242, 114, 38));
            AddBox(new Vector3(x, 0.56f, z), new Vector3(0.20f, 0.22f, 0.20f), new Color(246, 248, 252));
            AddBox(new Vector3(x, 0.72f, z), new Vector3(0.11f, 0.14f, 0.11f), new Color(242, 114, 38));
        }

        void AddLightTower(float x, float z)
        {
            AddBox(new Vector3(x, 5.2f, z), new Vector3(0.24f, 10.4f, 0.24f), new Color(152, 164, 178));
            AddBox(new Vector3(x, 10.3f, z), new Vector3(1.6f, 0.52f, 0.44f), new Color(70, 82, 96));
            // Bright lamp bar
            AddBox(new Vector3(x + (x < 0 ? 0.35f : -0.35f), 10.25f, z), new Vector3(0.20f, 0.36f, 1.25f), new Color(255, 248, 224));
        }

        void AddQuadY(float x0, float x1, float z0, float z1, float y, Color c)
        {
            var n = Vector3.Up;
            var a = new Vector3(x0, y, z0);
            var b = new Vector3(x1, y, z0);
            var d = new Vector3(x1, y, z1);
            var e = new Vector3(x0, y, z1);
            litTris.Add(new VertexPositionColorNormal(a, c, n));
            litTris.Add(new VertexPositionColorNormal(b, c, n));
            litTris.Add(new VertexPositionColorNormal(d, c, n));
            litTris.Add(new VertexPositionColorNormal(a, c, n));
            litTris.Add(new VertexPositionColorNormal(d, c, n));
            litTris.Add(new VertexPositionColorNormal(e, c, n));
        }

        void AddBox(Vector3 center, Vector3 size, Color c)
        {
            AddTransformedBox(center, size, Vector3.Zero, Matrix.Identity, c);
        }

        void AddTransformedBox(Vector3 localCenter, Vector3 size, Vector3 pivot, Matrix rot, Color c)
        {
            Vector3 h = size * 0.5f;
            Vector3[] p =
            {
                localCenter + new Vector3(-h.X, -h.Y, -h.Z),
                localCenter + new Vector3( h.X, -h.Y, -h.Z),
                localCenter + new Vector3( h.X,  h.Y, -h.Z),
                localCenter + new Vector3(-h.X,  h.Y, -h.Z),
                localCenter + new Vector3(-h.X, -h.Y,  h.Z),
                localCenter + new Vector3( h.X, -h.Y,  h.Z),
                localCenter + new Vector3( h.X,  h.Y,  h.Z),
                localCenter + new Vector3(-h.X,  h.Y,  h.Z),
            };
            for (int i = 0; i < 8; i++)
                p[i] = Vector3.Transform(p[i], rot) + pivot;

            AddFace(p[0], p[1], p[2], p[3], Vector3.TransformNormal(Vector3.Forward, rot), c);
            AddFace(p[5], p[4], p[7], p[6], Vector3.TransformNormal(Vector3.Backward, rot), c);
            AddFace(p[4], p[0], p[3], p[7], Vector3.TransformNormal(Vector3.Left, rot), c);
            AddFace(p[1], p[5], p[6], p[2], Vector3.TransformNormal(Vector3.Right, rot), c);
            AddFace(p[3], p[2], p[6], p[7], Vector3.TransformNormal(Vector3.Up, rot), c);
            AddFace(p[4], p[5], p[1], p[0], Vector3.TransformNormal(Vector3.Down, rot), c);
        }

        void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            litTris.Add(new VertexPositionColorNormal(a, col, n));
            litTris.Add(new VertexPositionColorNormal(b, col, n));
            litTris.Add(new VertexPositionColorNormal(c, col, n));
            litTris.Add(new VertexPositionColorNormal(a, col, n));
            litTris.Add(new VertexPositionColorNormal(c, col, n));
            litTris.Add(new VertexPositionColorNormal(d, col, n));
        }

        void AddUnlitDisc(float cx, float y, float cz, float radius, Color col)
        {
            const int segs = 14;
            var center = new Vector3(cx, y, cz);
            for (int i = 0; i < segs; i++)
            {
                double a0 = i * Math.PI * 2.0 / segs;
                double a1 = (i + 1) * Math.PI * 2.0 / segs;
                var p0 = new Vector3(cx + (float)Math.Cos(a0) * radius, y, cz + (float)Math.Sin(a0) * radius);
                var p1 = new Vector3(cx + (float)Math.Cos(a1) * radius, y, cz + (float)Math.Sin(a1) * radius);
                unlitTris.Add(new VertexPositionColor(center, col));
                unlitTris.Add(new VertexPositionColor(p0, col));
                unlitTris.Add(new VertexPositionColor(p1, col));
            }
        }

        void AddUnlitBeam(Vector3 a, Vector3 b, float halfW, Color col)
        {
            var side = new Vector3(halfW, 0f, 0f);
            var up = new Vector3(0f, halfW, 0f);
            unlitTris.Add(new VertexPositionColor(a - side, col));
            unlitTris.Add(new VertexPositionColor(a + side, col));
            unlitTris.Add(new VertexPositionColor(b + side, col));
            unlitTris.Add(new VertexPositionColor(a - side, col));
            unlitTris.Add(new VertexPositionColor(b + side, col));
            unlitTris.Add(new VertexPositionColor(b - side, col));

            unlitTris.Add(new VertexPositionColor(a - up, col));
            unlitTris.Add(new VertexPositionColor(a + up, col));
            unlitTris.Add(new VertexPositionColor(b + up, col));
            unlitTris.Add(new VertexPositionColor(a - up, col));
            unlitTris.Add(new VertexPositionColor(b + up, col));
            unlitTris.Add(new VertexPositionColor(b - up, col));
        }

        void AddUnlitDiamond(Vector3 c, float r, Color col)
        {
            var l = c + new Vector3(-r, 0f, 0f);
            var ri = c + new Vector3(r, 0f, 0f);
            var u = c + new Vector3(0f, r, 0f);
            var d = c + new Vector3(0f, -r, 0f);
            unlitTris.Add(new VertexPositionColor(l, col));
            unlitTris.Add(new VertexPositionColor(u, col));
            unlitTris.Add(new VertexPositionColor(ri, col));
            unlitTris.Add(new VertexPositionColor(l, col));
            unlitTris.Add(new VertexPositionColor(ri, col));
            unlitTris.Add(new VertexPositionColor(d, col));
        }

        void EnsureDevice(GraphicsDevice gd)
        {
            if (ReferenceEquals(boundDevice, gd)) return;
            DisposeResources();
            boundDevice = gd;
        }

        bool EnsureResources(GraphicsDevice gd)
        {
            if (initialized && renderTarget != null && !renderTarget.IsDisposed && worldEffect != null && !worldEffect.IsDisposed)
                return true;
            if (initFailed) return false;

            try
            {
                renderTarget = new RenderTarget2D(gd, TargetW, TargetH, false, SurfaceFormat.Color, DepthFormat.Depth24);
                worldEffect = new BasicEffect(gd);
                camera = new BasicCamera(gd, new Vector3(6.8f, 3.65f, 5.8f), new Vector3(0f, 1.55f, 1.3f), Vector3.Up);
                camera.NearPlane = 0.1f;
                camera.FarPlane = 140f;
                avatarScene = new _3DScene(gd, camera);
                if (GameFacade.Scenes != null)
                    avatarScene.Initialize(GameFacade.Scenes);
                initialized = true;
                return true;
            }
            catch (Exception e)
            {
                Boot.Log("cadet3d init error: " + e.Message);
                initFailed = true;
                return false;
            }
        }

        string lastAvatarErr;

        void EnsureCadetAvatar(VMAvatar sourceSim)
        {
            string key = sourceSim != null ? (sourceSim.Name + "#" + sourceSim.ObjectID) : "default";
            if (cadetAvatar != null && key == boundSimKey) return;

            try
            {
                lastAvatarErr = null;
                boundUniformTag = "civilian";
                var content = Content.Get();
                if (content == null || content.AvatarSkeletons == null) return;

                var baseSkel = content.AvatarSkeletons.Get("adult.skel");
                if (baseSkel == null) return;

                var sim = new SimAvatar(baseSkel.Clone());

                // 1. Keep the player's actual active Sim head (face + hair) so Bob/Betty is unmistakable!
                if (sourceSim != null && sourceSim.Avatar != null)
                {
                    try { if (sourceSim.Avatar.Head != null) sim.Head = sourceSim.Avatar.Head; } catch { }
                }

                // 2. Dress the Sim in the authentic Sims 1 Law Enforcement Cadet Career Uniform (from work.iff)!
                string uniformTag;
                var uniformOutfit = ResolveLawEnforcementUniform(content, sourceSim, out uniformTag);
                if (uniformOutfit != null)
                {
                    sim.Body = uniformOutfit;
                    sim.Handgroup = uniformOutfit;
                    boundUniformTag = uniformTag;
                }
                else if (sourceSim != null && sourceSim.Avatar != null)
                {
                    try { if (sourceSim.Avatar.Body != null) sim.Body = sourceSim.Avatar.Body; } catch { }
                    try { if (sourceSim.Avatar.Handgroup != null) sim.Handgroup = sourceSim.Avatar.Handgroup; } catch { }
                }

                // 3. Fallback to authentic TS1 adult rig with valid TS1 handgroup if any slot wasn't populated
                if (sim.Head == null)
                    sim.Head = new Outfit { TS1AppearanceID = "c_pizzaguy.apr", TS1TextureID = "pizzaguyface" };
                if (sim.Body == null)
                {
                    var fallbackBody = new Outfit
                    {
                        TS1AppearanceID = "b001mafit_01.apr",
                        TS1TextureID = "pizzaguysuit",
                        LiteralHandgroup = BuildAdultHands("lgt")
                    };
                    sim.Body = fallbackBody;
                    if (sim.Handgroup == null) sim.Handgroup = fallbackBody;
                }
                else if (sim.Handgroup == null)
                {
                    sim.Handgroup = new Outfit { LiteralHandgroup = BuildAdultHands("lgt") };
                }

                // Verified authentic Sims 1 Animation.far names:
                // Use a2o-walking-fast-loop (natural swinging arms down by sides!) rather than
                // a2o-running-loop / a2o-walking-frantic-loop which hold/wave both arms up in TS1 fire-panic!
                runAnim = PickAnimation(content, new[]
                {
                    "a2o-walking-fast-loop.anim",
                    "a2o-walking-loop.anim",
                });
                walkAnim = PickAnimation(content, new[]
                {
                    "a2o-walking-loop.anim",
                    "a2o-walking-fast-loop.anim",
                });
                jumpAnim = PickAnimation(content, new[]
                {
                    "a2o-pool-dive-jumpin.anim",
                    "a2o-walking-fast-loop.anim",
                });
                idleAnim = PickAnimation(content, new[]
                {
                    "a2o-standing-loop.anim",
                    "a2o-idle-armsdown-fidget.anim",
                    "a2o-idle-handships-shiftside.anim",
                    "a2o-idle1.anim",
                    "a2o-walking-loop.anim",
                });
                shootAnim = PickAnimation(content, new[]
                {
                    "a2o-bball-shoot.anim",
                    "a2o-standing-loop.anim",
                });
                cheerAnim = PickAnimation(content, new[]
                {
                    "a2o-greet-salute-heroic.anim",
                    "a2a-cheerer-loop.anim",
                    "a2o-happy-clap.anim",
                    "a2o-standing-loop.anim",
                });

                if (avatarScene != null)
                {
                    if (cadetAvatar != null) avatarScene.Remove(cadetAvatar);
                    avatarScene.Add(sim);
                }

                cadetAvatar = sim;
                boundSimKey = key;
                Boot.Log("cadet3d avatar ready for " + key +
                         " uniform=" + boundUniformTag +
                         " (run=" + (runAnim != null ? runAnim.Name : "none") +
                         " jump=" + (jumpAnim != null ? jumpAnim.Name : "none") +
                         " idle=" + (idleAnim != null ? idleAnim.Name : "none") +
                         " shoot=" + (shootAnim != null ? shootAnim.Name : "none") +
                         " cheer=" + (cheerAnim != null ? cheerAnim.Name : "none") + ")");
            }
            catch (Exception e)
            {
                lastAvatarErr = e.GetType().Name + ":" + e.Message;
                Boot.Log("cadet3d avatar error: " + e.Message);
            }
        }

        // Mirrors VMSuitProvider.GetPersonSuitTS1 (case 3: Work Outfit) to dress any Sim
        // (matching their exact gender, fit/fat/skn build, and lgt/med/drk skin tone)
        // in the authentic Sims 1 Law Enforcement Cadet / Police uniform from work.iff.
        static Outfit ResolveLawEnforcementUniform(Content content, VMAvatar sourceSim, out string tag)
        {
            tag = "fallback";
            try
            {
                if (content == null || content.Jobs == null || content.Jobs.JobResource == null)
                    return null;

                var careers = content.Jobs.JobResource.List<CARR>();
                if (careers == null) return null;

                CARR lawCareer = careers.FirstOrDefault(c =>
                    c != null &&
                    ((c.Name ?? "").IndexOf("Law", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (c.Name ?? "").IndexOf("Police", StringComparison.OrdinalIgnoreCase) >= 0));

                if (lawCareer == null)
                {
                    lawCareer = careers.FirstOrDefault(c =>
                        c != null && c.JobLevels != null &&
                        c.JobLevels.Any(l => l != null &&
                            ((l.JobName ?? "").IndexOf("Cadet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             (l.JobName ?? "").IndexOf("Security Guard", StringComparison.OrdinalIgnoreCase) >= 0)));
                }
                if (lawCareer == null || lawCareer.JobLevels == null || lawCareer.JobLevels.Length == 0)
                    return null;

                // Determine Sim gender, body build (fit/fat/skn), skin tone (lgt/med/drk), and hands
                bool isMale = true;
                string build = "fit";
                string skinTone = "lgt";
                HandGroup hands = null;

                if (sourceSim != null)
                {
                    try { isMale = sourceSim.GetPersonData(VMPersonDataVariable.Gender) == 0; } catch { }
                    try
                    {
                        var bodyStr = sourceSim.Object?.Resource?.Get<STR>(sourceSim.Object.OBJ.BodyStringID);
                        if (bodyStr != null)
                        {
                            string s14 = (bodyStr.GetString(14) ?? "").Trim().ToLowerInvariant();
                            if (s14 == "lgt" || s14 == "med" || s14 == "drk") skinTone = s14;
                            string s1 = (bodyStr.GetString(1) ?? "").ToLowerInvariant();
                            if (s1.Contains("fat")) build = "fat";
                            else if (s1.Contains("skn")) build = "skn";
                        }
                    }
                    catch { }
                    try { hands = sourceSim.DefaultSuits?.Daywear?.OftData?.LiteralHandgroup; } catch { }
                }

                if (hands == null) hands = BuildAdultHands(skinTone);

                string gChar = isMale ? "m" : "f";
                // Prefer Level 2 (Cadet = index 1); if Cadet has an empty uniform string in work.iff,
                // fall back to Patrol Officer [2], Security Guard [0], Desk Sergeant [3] — all authentic Law Enforcement uniforms!
                int[] levelOrder = { 1, 2, 0, 3, 4 };
                for (int i = 0; i < levelOrder.Length; i++)
                {
                    int idx = levelOrder[i];
                    if (idx < 0 || idx >= lawCareer.JobLevels.Length) continue;
                    var lvl = lawCareer.JobLevels[idx];
                    if (lvl == null) continue;

                    string mesh = isMale ? lvl.MaleUniformMesh : (!string.IsNullOrEmpty(lvl.FemaleUniformMesh) ? lvl.FemaleUniformMesh : lvl.MaleUniformMesh);
                    string skin = lvl.UniformSkin;
                    if (string.IsNullOrWhiteSpace(mesh) || string.IsNullOrWhiteSpace(skin)) continue;

                    mesh = mesh.Trim().ToLowerInvariant()
                        .Replace("$g", gChar)
                        .Replace("$b", build)
                        .Replace("$c", skinTone);
                    skin = skin.Trim().ToLowerInvariant()
                        .Replace("$g", gChar)
                        .Replace("$b", build)
                        .Replace("$c", skinTone);

                    var outfitRef = new VMOutfitReference(mesh + ",BODY=" + skin, false);
                    if (outfitRef.OftData != null)
                    {
                        outfitRef.OftData.LiteralHandgroup = hands;
                        // Verify the appearance actually resolves in TS1 content before committing
                        var apr = content.AvatarAppearances?.Get(outfitRef.OftData.TS1AppearanceID);
                        if (apr != null)
                        {
                            tag = (lvl.JobName ?? ("L" + (idx + 1))).Replace(" ", "") + ":" + mesh + "/" + skin;
                            return outfitRef.OftData;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Boot.Log("cadet3d uniform lookup error: " + e.Message);
            }
            return null;
        }

        static void ResetSkeletonToBase(SimAvatar sim)
        {
            if (sim == null || sim.Skeleton == null || sim.BaseSkeleton == null) return;
            var dst = sim.Skeleton.Bones;
            var src = sim.BaseSkeleton.Bones;
            if (dst == null || src == null) return;
            int n = Math.Min(dst.Length, src.Length);
            for (int i = 0; i < n; i++)
            {
                dst[i].Translation = src[i].Translation;
                dst[i].Rotation = src[i].Rotation;
            }
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

        static Animation PickAnimation(Content content, string[] candidates)
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
            try { renderTarget?.Dispose(); } catch { }
            try { worldEffect?.Dispose(); } catch { }
            renderTarget = null;
            worldEffect = null;
            camera = null;
            avatarScene = null;
            cadetAvatar = null;
            boundSimKey = null;
            initialized = false;
            initFailed = false;
            hasFrame = false;
        }

        public void Dispose()
        {
            DisposeResources();
            boundDevice = null;
        }
    }
}
