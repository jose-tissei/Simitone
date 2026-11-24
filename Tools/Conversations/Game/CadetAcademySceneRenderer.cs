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
        public const int DefaultTargetW = 1280;
        public const int DefaultTargetH = 720;
        const int MaxTargetW = 1600;
        const int MaxTargetH = 900;

        // Standard adult Vitaboy height (~5.45 units at scale 1.0) -> ~3.0 world units tall at 0.55
        const float SimScale = 0.55f;

        GraphicsDevice boundDevice;
        RenderTarget2D renderTarget;
        int targetW = DefaultTargetW;
        int targetH = DefaultTargetH;
        BasicEffect worldEffect;
        BasicCamera camera;
        _3DScene avatarScene;

        SimAvatar cadetAvatar;
        SimAvatar instructorAvatar;
        string boundSimKey;
        string boundUniformTag = "none";
        string instructorTag = "none";
        string lastActiveAnimName = "none";
        string lastAnimSource = "none";
        float lastElapsedSeconds = -1f;
        readonly Stopwatch sceneStopwatch = Stopwatch.StartNew();
        double lastWallSeconds = -1.0;
        float finishExtraSeconds;
        readonly VitaboyClipPlayer customPlayer = new VitaboyClipPlayer();
        readonly VitaboyClipPlayer instructorPlayer = new VitaboyClipPlayer();
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
        readonly List<VertexPositionColorNormal> litTris = new List<VertexPositionColorNormal>(8192);
        readonly List<VertexPositionColor> unlitTris = new List<VertexPositionColor>(2048);
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
                   " res=" + targetW + "x" + targetH +
                   " sim=" + (cadetAvatar != null) +
                   " instr=" + (instructorAvatar != null) +
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
            EnsureDrillInstructorAvatar(sourceSim);

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

                Vector3 instructorPos = new Vector3(-5.2f, 0f, 2.0f);
                float instructorYaw = 1.45f;
                string instructorClip = "cadet_idle_attention";

                if (game.IsBriefing)
                {
                    // Full-screen 16:9 establishing shot: Bob + Drill Instructor stand sharp at the starting line
                    // framed cleanly in the right 65% of the screen so the floating Briefing card on the left never covers them!
                    simPos = new Vector3(0.4f, 0f, -7.8f);
                    simYaw = 0.22f + (float)Math.Sin(t * 0.65f) * 0.10f;
                    customClipName = "cadet_idle_attention";
                    customFade = 0.18f;
                    customSpeed = 1.0f;
                    activeAnim = idleAnim ?? walkAnim ?? runAnim;
                    animSpeed = 0.85f;

                    instructorPos = new Vector3(2.85f, 0f, -8.3f);
                    instructorYaw = -0.32f + (float)Math.Sin(t * 0.45f) * 0.08f;
                    instructorClip = "cadet_idle_attention";

                    float orbit = (float)Math.Sin(t * 0.40f) * 0.95f;
                    camera.Position = new Vector3(3.4f + orbit, 2.95f, 2.1f);
                    camera.Target = new Vector3(-1.35f, 1.62f, -7.8f);
                }
                else if (game.IsFinished)
                {
                    // Full-screen Victory Showcase right past the Police Academy Finish Gantry (arch at z = 10.0)
                    // with BOTH Cadet Bob and the Sideline Drill Instructor rendering crisp temple salutes!
                    simPos = new Vector3(0.4f, 0f, 11.4f);
                    simYaw = 0.16f + (float)Math.Sin(t * 0.80f) * 0.09f;
                    customClipName = "cadet_victory_salute";
                    customFade = 0.20f;
                    customSpeed = 1.0f;
                    activeAnim = cheerAnim ?? idleAnim ?? walkAnim;
                    animSpeed = 0.95f;

                    instructorPos = new Vector3(2.75f, 0f, 11.0f);
                    instructorYaw = -0.18f;
                    instructorClip = "cadet_victory_salute";

                    float sway = (float)Math.Sin(t * 0.45f) * 0.90f;
                    camera.Position = new Vector3(3.1f + sway, 2.85f, 19.4f);
                    camera.Target = new Vector3(-1.35f, 1.72f, 10.8f);
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

                    // Sideline Drill Instructor tracks Bob along the grass sideline and salutes on Perfect!
                    instructorPos = new Vector3(-5.35f, 0f, Math.Max(-4.0f, Math.Min(8.5f, simPos.Z + 1.2f)));
                    instructorYaw = (float)Math.Atan2(simPos.X - instructorPos.X, simPos.Z - instructorPos.Z);
                    instructorClip = (resolving && rating == CadetHitRating.Perfect) ? "cadet_victory_salute" : "cadet_idle_attention";

                    // Dynamic 16:9 3/4 tracking camera framing Cadet, hurdles, sideline Instructor & Academy grounds
                    camera.Position = new Vector3(simPos.X + 7.2f, 3.65f, simPos.Z + 5.9f);
                    camera.Target = new Vector3(simPos.X - 0.85f, 1.60f, simPos.Z + 1.3f);
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

                    // Sideline Drill Instructor watches right beside the vault wall & landing mat
                    instructorPos = new Vector3(-5.15f, 0f, 2.3f);
                    instructorYaw = (float)Math.Atan2(simPos.X - instructorPos.X, simPos.Z - instructorPos.Z);
                    instructorClip = (resolving && rating == CadetHitRating.Perfect && resP > 0.45f)
                        ? "cadet_victory_salute"
                        : "cadet_idle_attention";

                    // Heroic 16:9 side-quarter vault camera with generous headroom above the timber wall
                    camera.Position = new Vector3(10.0f, 4.15f, 2.3f);
                    camera.Target = new Vector3(-0.6f, 2.15f, 1.9f);
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

                    // Range Safety Officer / Drill Instructor observes beside the firing line
                    instructorPos = new Vector3(-3.35f, 0f, -3.1f);
                    instructorYaw = 0.34f;
                    instructorClip = (resolving && rating == CadetHitRating.Perfect && resP > 0.30f)
                        ? "cadet_victory_salute"
                        : "cadet_idle_attention";

                    // Full-screen 16:9 over-the-shoulder firing range view framing Cadet, Instructor, and all 3 steel targets
                    camera.Position = new Vector3(4.9f, 3.45f, -8.3f);
                    camera.Target = new Vector3(-0.45f, 1.62f, 5.2f);
                }

                // 2. Build all 3D Virtual Police Academy Grounds meshes for this frame
                litTris.Clear();
                unlitTris.Clear();
                BuildAcademyEnvironment(kind, simPos, instructorPos, targetKnockAngle, showShotFlash, shotImpactPos, t);

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

                // 3b. Pose & animate the live Sideline Drill Instructor in the same 3D world!
                if (instructorAvatar != null)
                {
                    bool instrCustom = !string.IsNullOrEmpty(instructorClip) &&
                                       instructorPlayer.Play(instructorClip, 0.22f, 0.92f, true) &&
                                       instructorPlayer.UpdateAndApply(instructorAvatar, dt, useSquad: true);
                    if (!instrCustom)
                    {
                        ResetSkeletonToBase(instructorAvatar);
                        if (idleAnim != null)
                        {
                            float fps = idleAnim.FramesPerSecond > 0 ? idleAnim.FramesPerSecond : 30f;
                            int numFrames = Math.Max(1, idleAnim.NumFrames);
                            float exact = (Math.Max(0f, t) * fps * 0.85f) % numFrames;
                            int frame = (int)exact;
                            Animator.RenderFrame(instructorAvatar, idleAnim, frame, exact - frame, 1.0f);
                        }
                    }

                    instructorAvatar.Scale = Vector3.One * SimScale;
                    instructorAvatar.Position = instructorPos;
                    instructorAvatar.RotationY = instructorYaw;
                    instructorAvatar.RotationX = 0f;
                    instructorAvatar.AmbientLight = new Vector4(1.14f, 1.13f, 1.10f, 1.0f);
                    instructorAvatar.ReloadSkeleton();
                }

                // 4. Render unified 3D world + SimAvatars into our full-screen RenderTarget2D with Depth24 Z-buffering
                var prevTargets = gd.GetRenderTargets();
                var prevVp = gd.Viewport;
                var prevRast = gd.RasterizerState;
                var prevDepth = gd.DepthStencilState;
                var prevBlend = gd.BlendState;

                try
                {
                    gd.SetRenderTarget(renderTarget);
                    gd.Viewport = new Viewport(0, 0, targetW, targetH);
                    camera.AspectRatioMultiplier = 1f;
                    gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(108, 168, 226), 1f, 0);

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
            Vector3 instructorPos,
            float targetKnockAngle,
            bool showShotFlash,
            Vector3 shotImpactPos,
            float elapsed)
        {
            // 1. Mowed green academy turf field (striped for crisp speed & depth cues!)
            for (int iz = -28; iz < 44; iz += 4)
            {
                Color grass = ((iz / 4) % 2 == 0) ? new Color(78, 134, 72) : new Color(68, 120, 64);
                AddQuadY(-34f, 34f, iz, iz + 4f, 0f, grass);
            }

            // 1b. Paved academy service apron along the -X precinct side for squad cars & bleachers
            AddQuadY(-22f, -6.4f, -20f, 30f, 0.008f, new Color(118, 124, 132));
            AddBox(new Vector3(-6.4f, 0.06f, 5f), new Vector3(0.32f, 0.12f, 50f), new Color(188, 194, 200));

            // 2. Terracotta running track surface + crisp white chalk lane dividers
            AddQuadY(-4.4f, 4.4f, -18f, 30f, 0.012f, new Color(166, 78, 58));
            // Concrete track curbs
            AddBox(new Vector3(-4.65f, 0.08f, 6f), new Vector3(0.5f, 0.16f, 48f), new Color(188, 194, 200));
            AddBox(new Vector3(4.65f, 0.08f, 6f), new Vector3(0.5f, 0.16f, 48f), new Color(188, 194, 200));

            float[] laneXs = { -3.9f, -1.35f, 1.35f, 3.9f };
            for (int i = 0; i < laneXs.Length; i++)
                AddQuadY(laneXs[i] - 0.06f, laneXs[i] + 0.06f, -17.5f, 29.5f, 0.022f, new Color(242, 245, 248));

            // 3. Perimeter walls, Police Academy Precinct Building, Bleachers, Patrol Cruisers, Flagpole, Trees & Skyline
            AddBox(new Vector3(-12.6f, 1.05f, 6f), new Vector3(0.45f, 2.1f, 56f), new Color(156, 164, 174));
            AddBox(new Vector3(-12.6f, 2.18f, 6f), new Vector3(0.58f, 0.22f, 56f), new Color(36, 76, 142));
            AddBox(new Vector3(12.6f, 1.05f, 6f), new Vector3(0.45f, 2.1f, 56f), new Color(156, 164, 174));
            AddBox(new Vector3(12.6f, 2.18f, 6f), new Vector3(0.58f, 0.22f, 56f), new Color(36, 76, 142));
            // Back stadium wall with gold academy crest stripe
            AddBox(new Vector3(0f, 1.8f, 31f), new Vector3(26f, 3.6f, 0.7f), new Color(52, 74, 110));
            AddBox(new Vector3(0f, 3.1f, 30.6f), new Vector3(24f, 0.32f, 0.18f), new Color(235, 192, 66));

            // Police Academy Precinct Building along the -X flank (visible in Sprint, Vault, Target Drill)
            AddPrecinctBuilding(-16.8f, 4.0f);
            // 3-tier sideline viewing bleachers behind the Drill Instructor
            AddBleachers(-8.4f, 3.2f);
            // Parked SimCity Police Patrol Cruisers with live pulsing red/blue roof lightbars!
            AddPatrolCruiser(-9.6f, -8.8f, 0.08f, elapsed);
            AddPatrolCruiser(8.4f, -12.6f, -0.35f, elapsed + 0.8f);
            // Tall Academy flagpole with gently waving navy-and-gold banner beside the Finish Gantry
            AddAcademyFlagpole(-6.8f, 10.4f, elapsed);
            // Perimeter trees & hedges framing both sides of the virtual Academy lot
            float[] treeZs = { -16f, -6f, 14f, 24f };
            for (int i = 0; i < treeZs.Length; i++)
            {
                AddAcademyTree(14.8f, treeZs[i], (i % 2 == 0) ? 1.15f : 0.95f);
                if (i == 0 || i >= 2)
                    AddAcademyTree(-14.6f, treeZs[i], 1.05f);
            }
            AddAcademyTree(-7.6f, -15.8f, 1.1f);
            AddAcademyTree(7.4f, 23.5f, 1.2f);
            // Distant SimCity skyline silhouettes along both horizons for rich 16:9 depth
            AddDistantSkyline();

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

            // 5. Grounded 3D contact shadows beneath both Cadet and Sideline Drill Instructor
            float shadowScale = Math.Max(0.32f, 1.0f - simPos.Y * 0.22f);
            byte shadowAlpha = (byte)Math.Round(135f * shadowScale);
            AddUnlitDisc(simPos.X, 0.032f, simPos.Z, 0.85f * shadowScale, new Color((byte)10, (byte)16, (byte)24, shadowAlpha));
            if (instructorAvatar != null)
                AddUnlitDisc(instructorPos.X, 0.032f, instructorPos.Z, 0.82f, new Color((byte)10, (byte)16, (byte)24, (byte)130));
        }

        void AddPrecinctBuilding(float x, float centerZ)
        {
            // Two-story brick & slate Police Academy Precinct facade along the left perimeter
            AddBox(new Vector3(x, 2.2f, centerZ), new Vector3(4.2f, 4.4f, 24f), new Color(156, 86, 68));
            AddBox(new Vector3(x, 5.4f, centerZ), new Vector3(4.3f, 2.2f, 24.2f), new Color(64, 82, 112));
            // Gold Academy fascia cornice & roofline trim
            AddBox(new Vector3(x + 2.15f, 4.35f, centerZ), new Vector3(0.24f, 0.28f, 24.2f), new Color(235, 194, 70));
            AddBox(new Vector3(x, 6.65f, centerZ), new Vector3(4.6f, 0.34f, 24.6f), new Color(40, 56, 84));
            // Recessed illuminated Academy windows (two stories)
            for (float wz = centerZ - 9.5f; wz <= centerZ + 9.5f; wz += 3.8f)
            {
                AddBox(new Vector3(x + 2.14f, 2.55f, wz), new Vector3(0.14f, 1.45f, 1.65f), new Color(216, 224, 232));
                AddBox(new Vector3(x + 2.19f, 2.55f, wz), new Vector3(0.08f, 1.22f, 1.42f), new Color(92, 146, 198));
                AddBox(new Vector3(x + 2.14f, 5.35f, wz), new Vector3(0.14f, 1.15f, 1.65f), new Color(210, 218, 228));
                AddBox(new Vector3(x + 2.19f, 5.35f, wz), new Vector3(0.08f, 0.92f, 1.42f), new Color(118, 174, 224));
            }
            // Navy entrance awning + gold badge emblem at centerZ
            AddBox(new Vector3(x + 2.75f, 2.85f, centerZ), new Vector3(1.4f, 0.22f, 4.2f), new Color(26, 62, 122));
            AddBox(new Vector3(x + 3.42f, 2.85f, centerZ), new Vector3(0.08f, 0.14f, 4.2f), new Color(240, 198, 66));
        }

        void AddBleachers(float x, float centerZ)
        {
            // 3-tier aluminum sideline viewing bleachers facing the track (+X)
            for (int tier = 0; tier < 3; tier++)
            {
                float bx = x - tier * 0.95f;
                float by = 0.55f + tier * 0.62f;
                AddBox(new Vector3(bx, by, centerZ), new Vector3(0.72f, 0.14f, 11.6f), new Color(196, 204, 214));
                AddBox(new Vector3(bx + 0.15f, by - 0.28f, centerZ), new Vector3(0.55f, 0.10f, 11.6f), new Color(142, 152, 166));
            }
            // Steel support frames + navy side handrails
            for (float fz = centerZ - 5.2f; fz <= centerZ + 5.3f; fz += 2.6f)
            {
                AddBox(new Vector3(x - 1.0f, 0.9f, fz), new Vector3(2.8f, 1.75f, 0.14f), new Color(66, 78, 96));
            }
            AddBox(new Vector3(x - 2.35f, 1.95f, centerZ), new Vector3(0.12f, 1.2f, 11.8f), new Color(36, 74, 134));
            AddBox(new Vector3(x - 2.35f, 2.52f, centerZ), new Vector3(0.16f, 0.12f, 11.8f), new Color(235, 194, 70));
        }

        void AddPatrolCruiser(float x, float z, float yawRad, float elapsed)
        {
            var pivot = new Vector3(x, 0f, z);
            var rot = Matrix.CreateRotationY(yawRad);

            // Lower navy/black chassis + crisp white police doors
            AddTransformedBox(new Vector3(0f, 0.68f, 0f), new Vector3(1.92f, 0.68f, 4.45f), pivot, rot, new Color(24, 34, 52));
            AddTransformedBox(new Vector3(0f, 0.72f, 0.05f), new Vector3(1.98f, 0.56f, 1.95f), pivot, rot, new Color(242, 246, 250));
            // Gold police star stripe along sides
            AddTransformedBox(new Vector3(0f, 0.74f, 0.05f), new Vector3(2.01f, 0.12f, 1.85f), pivot, rot, new Color(236, 192, 62));
            // Cabin roof (white) + dark tinted windshield & side glass
            AddTransformedBox(new Vector3(0f, 1.32f, -0.15f), new Vector3(1.66f, 0.62f, 2.05f), pivot, rot, new Color(242, 246, 250));
            AddTransformedBox(new Vector3(0f, 1.30f, -0.15f), new Vector3(1.70f, 0.44f, 1.82f), pivot, rot, new Color(38, 60, 86));
            // Front chrome push-bumper + headlights + taillights
            AddTransformedBox(new Vector3(0f, 0.58f, 2.28f), new Vector3(1.55f, 0.42f, 0.18f), pivot, rot, new Color(46, 54, 66));
            AddTransformedBox(new Vector3(-0.68f, 0.72f, 2.24f), new Vector3(0.32f, 0.18f, 0.08f), pivot, rot, new Color(255, 246, 200));
            AddTransformedBox(new Vector3(0.68f, 0.72f, 2.24f), new Vector3(0.32f, 0.18f, 0.08f), pivot, rot, new Color(255, 246, 200));
            AddTransformedBox(new Vector3(-0.68f, 0.72f, -2.24f), new Vector3(0.34f, 0.16f, 0.08f), pivot, rot, new Color(214, 42, 46));
            AddTransformedBox(new Vector3(0.68f, 0.72f, -2.24f), new Vector3(0.34f, 0.16f, 0.08f), pivot, rot, new Color(214, 42, 46));
            // 4 Wheels
            float[] wx = { -0.92f, 0.92f };
            float[] wz = { -1.35f, 1.35f };
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                    AddTransformedBox(new Vector3(wx[ix], 0.34f, wz[iz]), new Vector3(0.28f, 0.68f, 0.68f), pivot, rot, new Color(26, 28, 32));
            // Alternating pulsing red / blue roof lightbar!
            bool flashA = Math.Sin(elapsed * 7.5f) >= 0.0;
            Color redLamp = flashA ? new Color(255, 58, 62) : new Color(138, 30, 36);
            Color blueLamp = flashA ? new Color(36, 92, 180) : new Color(64, 178, 255);
            AddTransformedBox(new Vector3(0f, 1.70f, -0.05f), new Vector3(1.18f, 0.12f, 0.24f), pivot, rot, new Color(190, 198, 210));
            AddTransformedBox(new Vector3(-0.34f, 1.74f, -0.05f), new Vector3(0.46f, 0.16f, 0.22f), pivot, rot, redLamp);
            AddTransformedBox(new Vector3(0.34f, 1.74f, -0.05f), new Vector3(0.46f, 0.16f, 0.22f), pivot, rot, blueLamp);
        }

        void AddAcademyFlagpole(float x, float z, float elapsed)
        {
            // Concrete plinth + tall brushed steel pole + gold finial orb
            AddBox(new Vector3(x, 0.22f, z), new Vector3(0.9f, 0.44f, 0.9f), new Color(180, 186, 196));
            AddBox(new Vector3(x, 4.4f, z), new Vector3(0.14f, 8.8f, 0.14f), new Color(214, 222, 232));
            AddBox(new Vector3(x, 8.95f, z), new Vector3(0.32f, 0.32f, 0.32f), new Color(242, 202, 68));
            // Gently waving 4-segment navy-and-gold Police Academy banner
            for (int seg = 0; seg < 4; seg++)
            {
                float sx = x + 0.36f + seg * 0.50f;
                float waveZ = z + (float)Math.Sin(elapsed * 3.8f - seg * 0.9f) * (0.06f + seg * 0.05f);
                AddBox(new Vector3(sx, 8.15f, waveZ), new Vector3(0.52f, 1.15f, 0.06f), new Color(28, 66, 134));
                AddBox(new Vector3(sx, 8.15f, waveZ), new Vector3(0.52f, 0.22f, 0.08f), new Color(240, 198, 68));
            }
        }

        void AddAcademyTree(float x, float z, float scale)
        {
            // Stylized sunlit SimCity cypress / maple silhouette
            AddBox(new Vector3(x, 1.2f * scale, z), new Vector3(0.52f * scale, 2.4f * scale, 0.52f * scale), new Color(116, 82, 54));
            AddBox(new Vector3(x, 3.2f * scale, z), new Vector3(2.4f * scale, 2.2f * scale, 2.4f * scale), new Color(54, 118, 56));
            AddBox(new Vector3(x, 4.7f * scale, z), new Vector3(1.75f * scale, 1.6f * scale, 1.75f * scale), new Color(68, 138, 64));
            AddBox(new Vector3(x, 5.8f * scale, z), new Vector3(1.05f * scale, 1.1f * scale, 1.05f * scale), new Color(86, 156, 76));
        }

        void AddDistantSkyline()
        {
            // Soft atmospheric blue-grey distant SimCity towers far past both ends of the Academy yard
            Color b1 = new Color(116, 152, 194);
            Color b2 = new Color(102, 138, 182);
            AddBox(new Vector3(-22f, 7.5f, 44f), new Vector3(6.5f, 15f, 5f), b1);
            AddBox(new Vector3(-12f, 10.5f, 46f), new Vector3(7.0f, 21f, 5f), b2);
            AddBox(new Vector3(0f, 8.5f, 47f), new Vector3(8.5f, 17f, 5f), b1);
            AddBox(new Vector3(13f, 11.5f, 45f), new Vector3(6.8f, 23f, 5f), b2);
            AddBox(new Vector3(24f, 6.5f, 43f), new Vector3(7.2f, 13f, 5f), b1);

            AddBox(new Vector3(-20f, 6.5f, -34f), new Vector3(7f, 13f, 5f), b2);
            AddBox(new Vector3(-8f, 9.0f, -36f), new Vector3(8f, 18f, 5f), b1);
            AddBox(new Vector3(8f, 7.5f, -35f), new Vector3(7.5f, 15f, 5f), b2);
            AddBox(new Vector3(21f, 8.5f, -34f), new Vector3(7f, 17f, 5f), b1);
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
            if (initFailed) return false;

            int bbW = gd.PresentationParameters != null ? gd.PresentationParameters.BackBufferWidth : DefaultTargetW;
            int bbH = gd.PresentationParameters != null ? gd.PresentationParameters.BackBufferHeight : DefaultTargetH;
            if (bbW < 320 || bbH < 240) { bbW = DefaultTargetW; bbH = DefaultTargetH; }

            float scale = Math.Min(1f, Math.Min((float)MaxTargetW / bbW, (float)MaxTargetH / bbH));
            int desiredW = Math.Max(640, ((int)Math.Round(bbW * scale)) & ~1);
            int desiredH = Math.Max(360, ((int)Math.Round(bbH * scale)) & ~1);

            if (initialized && renderTarget != null && !renderTarget.IsDisposed &&
                worldEffect != null && !worldEffect.IsDisposed &&
                targetW == desiredW && targetH == desiredH)
                return true;

            try
            {
                if (renderTarget != null && !renderTarget.IsDisposed)
                {
                    try { renderTarget.Dispose(); } catch { }
                    renderTarget = null;
                }

                targetW = desiredW;
                targetH = desiredH;
                renderTarget = new RenderTarget2D(gd, targetW, targetH, false, SurfaceFormat.Color, DepthFormat.Depth24);
                if (worldEffect == null || worldEffect.IsDisposed)
                    worldEffect = new BasicEffect(gd);

                if (camera == null)
                {
                    camera = new BasicCamera(gd, new Vector3(6.8f, 3.65f, 5.8f), new Vector3(0f, 1.55f, 1.3f), Vector3.Up);
                    camera.NearPlane = 0.1f;
                    camera.FarPlane = 160f;
                }
                if (avatarScene == null)
                {
                    avatarScene = new _3DScene(gd, camera);
                    if (GameFacade.Scenes != null)
                        avatarScene.Initialize(GameFacade.Scenes);
                }
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

        void EnsureDrillInstructorAvatar(VMAvatar sourceSim)
        {
            if (instructorAvatar != null) return;
            try
            {
                var content = Content.Get();
                if (content == null || content.AvatarSkeletons == null) return;

                var baseSkel = content.AvatarSkeletons.Get("adult.skel");
                if (baseSkel == null) return;

                var instr = new SimAvatar(baseSkel.Clone());

                // 1. Pick a distinct, authentic TS1 adult head for the Drill Instructor (prefer police cap / senior officer,
                //    then another adult Sim in the current VM, then verified TS1 adult head fallback)
                Outfit headOutfit = TryPickInstructorHead(content, sourceSim);
                if (headOutfit != null)
                    instr.Head = headOutfit;

                // 2. Dress the Drill Instructor in an authentic Senior Law Enforcement Officer / Sergeant uniform from work.iff
                string tag;
                var uniformOutfit = ResolveLawEnforcementUniform(content, null, out tag, new[] { 3, 2, 4, 1, 0 });
                if (uniformOutfit != null)
                {
                    instr.Body = uniformOutfit;
                    instr.Handgroup = uniformOutfit;
                    instructorTag = tag;
                }
                else
                {
                    var fallbackBody = new Outfit
                    {
                        TS1AppearanceID = "b001mafit_01.apr",
                        TS1TextureID = "pizzaguysuit",
                        LiteralHandgroup = BuildAdultHands("lgt")
                    };
                    instr.Body = fallbackBody;
                    instr.Handgroup = fallbackBody;
                    instructorTag = "fallback";
                }

                if (instr.Head == null)
                    instr.Head = new Outfit { TS1AppearanceID = "c_pizzaguy.apr", TS1TextureID = "pizzaguyface" };
                if (instr.Handgroup == null)
                    instr.Handgroup = new Outfit { LiteralHandgroup = BuildAdultHands("lgt") };

                if (avatarScene != null)
                    avatarScene.Add(instr);

                instructorAvatar = instr;
                Boot.Log("cadet3d sideline Drill Instructor ready uniform=" + instructorTag);
            }
            catch (Exception e)
            {
                Boot.Log("cadet3d instructor avatar error: " + e.Message);
            }
        }

        static Outfit TryPickInstructorHead(Content content, VMAvatar sourceSim)
        {
            // Try authentic TS1 NPC cop / officer heads first
            var candidates = new[]
            {
                new[] { "c_police.apr", "policeface" },
                new[] { "c_cop.apr", "copface" },
                new[] { "c001ma_cop.apr", "c001malgt_cop" },
                new[] { "c001ma_police.apr", "c001malgt_police" },
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    var apr = content.AvatarAppearances?.Get(candidates[i][0]);
                    if (apr != null)
                        return new Outfit { TS1AppearanceID = candidates[i][0], TS1TextureID = candidates[i][1] };
                }
                catch { }
            }

            // Next try another adult Sim on the lot (excluding the player's Cadet) for a guaranteed crisp TS1 head
            try
            {
                var vm = CareerDispatchDriver.CurrentVM();
                if (vm != null && vm.Entities != null)
                {
                    foreach (var ent in vm.Entities)
                    {
                        var av = ent as VMAvatar;
                        if (av == null || ReferenceEquals(av, sourceSim) || av.Avatar == null || av.Avatar.Head == null)
                            continue;
                        if (av.GetPersonData(VMPersonDataVariable.PersonsAge) < 18) continue;
                        if (av.GetPersonData(VMPersonDataVariable.Gender) == 0)
                            return av.Avatar.Head;
                    }
                    foreach (var ent in vm.Entities)
                    {
                        var av = ent as VMAvatar;
                        if (av == null || ReferenceEquals(av, sourceSim) || av.Avatar == null || av.Avatar.Head == null)
                            continue;
                        if (av.GetPersonData(VMPersonDataVariable.PersonsAge) >= 18)
                            return av.Avatar.Head;
                    }
                }
            }
            catch { }

            return new Outfit { TS1AppearanceID = "c_pizzaguy.apr", TS1TextureID = "pizzaguyface" };
        }

        // Mirrors VMSuitProvider.GetPersonSuitTS1 (case 3: Work Outfit) to dress any Sim
        // (matching their exact gender, fit/fat/skn build, and lgt/med/drk skin tone)
        // in the authentic Sims 1 Law Enforcement Cadet / Police uniform from work.iff.
        static Outfit ResolveLawEnforcementUniform(Content content, VMAvatar sourceSim, out string tag, int[] preferredLevels = null)
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
                // Prefer Level 2 (Cadet = index 1) by default, or caller's preferred rank order (e.g. Sergeant/Officer for Drill Instructor)
                int[] levelOrder = (preferredLevels != null && preferredLevels.Length > 0)
                    ? preferredLevels
                    : new[] { 1, 2, 0, 3, 4 };
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
            instructorAvatar = null;
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
