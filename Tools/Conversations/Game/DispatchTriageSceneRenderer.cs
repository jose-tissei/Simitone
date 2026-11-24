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
    // Full-Screen 3D "Virtual Pocket Lot" Renderer for Law Enforcement Level 4 ("Desk Sergeant"):
    // SimCity PD Precinct Front Desk & 911 Switchboard Lobby.
    // Renders the warm-lit 3D precinct interior (checkered marble floor, oak front counter, flashing
    // retro 911 switchboard, lifting telephone handset, SimCity sector corkboard with live incident pins,
    // paned windows onto a twilight street where 3D Police Cruisers & a Fire Engine physically roll out
    // with strobing lights!) AND both your actual 3D Sim (in authentic TS1 Law Enforcement Sergeant uniform)
    // AND live 3D Walk-in Neighborhood Citizens walking up to the counter in ONE shared Depth24 Z-buffer!
    internal sealed class DispatchTriageSceneRenderer : IDisposable
    {
        public const int DefaultTargetW = 1280;
        public const int DefaultTargetH = 720;
        const int MaxTargetW = 1600;
        const int MaxTargetH = 900;
        const float SimScale = 0.55f;

        GraphicsDevice boundDevice;
        RenderTarget2D renderTarget;
        int targetW = DefaultTargetW;
        int targetH = DefaultTargetH;
        BasicEffect worldEffect;
        BasicCamera camera;
        _3DScene avatarScene;

        SimAvatar sergeantAvatar;
        SimAvatar walkInAvatar;
        string boundSergeantKey;
        string boundWalkInKey;
        string boundUniformTag = "none";
        string lastActiveAnimName = "none";

        readonly VitaboyClipPlayer sergeantPlayer = new VitaboyClipPlayer();
        readonly VitaboyClipPlayer walkInPlayer = new VitaboyClipPlayer();
        readonly Stopwatch sceneClock = Stopwatch.StartNew();
        double lastWallSec = -1.0;

        bool initialized;
        bool initFailed;
        bool hasFrame;

        // Screen-space projected anchor points for crisp overhead speech bubbles!
        public Vector2 SergeantHeadNorm { get; private set; }
        public Vector2 WalkInHeadNorm { get; private set; }
        public bool WalkInVisible { get; private set; }
        public bool WalkInIsWalking { get; private set; }

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
                   " sgt=" + (sergeantAvatar != null) +
                   " uniform=" + boundUniformTag +
                   " walkin=" + WalkInVisible +
                   " anim=" + lastActiveAnimName;
        }

        public void PreDraw(GraphicsDevice gd, DispatchTriageGame game, VMAvatar sourceSim)
        {
            hasFrame = false;
            WalkInVisible = false;
            WalkInIsWalking = false;
            if (gd == null || game == null) return;

            EnsureDevice(gd);
            if (!EnsureResources(gd)) return;
            EnsureSergeantAvatar(sourceSim);

            try
            {
                double wallNow = sceneClock.Elapsed.TotalSeconds;
                float dt = lastWallSec >= 0.0
                    ? (float)Math.Max(0.001, Math.Min(0.08, wallNow - lastWallSec))
                    : (1f / 30f);
                lastWallSec = wallNow;
                float t = (float)wallNow;

                var walkIn = game.ActiveWalkIn;
                if (walkIn != null)
                    EnsureWalkInAvatar(walkIn, sourceSim);

                // 1. Compute Desk Sergeant position, yaw, and custom Blender skeletal animation
                Vector3 sgtPos = new Vector3(0.45f, 0f, -1.35f);
                float sgtYaw = 0.08f;
                string sgtClip = "desk_idle_watch";
                bool sgtLoop = true;
                float sgtSpeed = 1.0f;
                bool handsetLifted = false;

                if (game.IsFinished)
                {
                    sgtClip = "cadet_victory_salute";
                    sgtLoop = true;
                    sgtSpeed = 0.95f;
                    sgtYaw = 0.14f;
                }
                else if (game.IsBriefing)
                {
                    sgtClip = "desk_idle_watch";
                    sgtLoop = true;
                    sgtYaw = 0.12f + (float)Math.Sin(t * 0.6f) * 0.06f;
                }
                else
                {
                    switch (game.CurrentGesture)
                    {
                        case DeskSergeantGesture.AnswerPhone:
                            sgtClip = "desk_answer_phone";
                            sgtLoop = true;
                            sgtSpeed = 1.0f;
                            sgtYaw = 0.06f;
                            handsetLifted = true;
                            break;
                        case DeskSergeantGesture.RadioDispatch:
                            sgtClip = "desk_radio_dispatch";
                            sgtLoop = false;
                            sgtSpeed = 1.05f;
                            sgtYaw = 0.04f;
                            break;
                        case DeskSergeantGesture.WaveDismiss:
                            sgtClip = "desk_wave_dismiss";
                            sgtLoop = false;
                            sgtSpeed = 1.05f;
                            sgtYaw = walkIn != null ? 0.30f : 0.08f;
                            break;
                        case DeskSergeantGesture.CalmTalk:
                            sgtClip = "desk_idle_watch";
                            sgtLoop = true;
                            sgtSpeed = 1.15f;
                            sgtYaw = walkIn != null ? 0.34f : 0.10f;
                            break;
                        default:
                            sgtClip = "desk_idle_watch";
                            sgtLoop = true;
                            sgtSpeed = 0.92f;
                            sgtYaw = walkIn != null ? 0.32f : 0.08f;
                            break;
                    }
                }

                // 2. Compute Live 3D Walk-in Citizen choreography (door -> front counter -> door)
                Vector3 doorPos = new Vector3(-5.25f, 0f, 2.75f);
                Vector3 counterStandPos = new Vector3(1.55f, 0f, 1.02f);
                Vector3 walkInPos = new Vector3(0f, -50f, 0f);
                float walkInYaw = (float)Math.PI;
                string walkInClip = "desk_walkin_plead";

                if (walkIn != null && walkInAvatar != null && !game.IsBriefing && !game.IsFinished)
                {
                    WalkInVisible = true;
                    if (!walkIn.WalkInLeaving)
                    {
                        float u = Math.Max(0f, Math.Min(1f, walkIn.WalkInApproach));
                        float s = u * u * (3f - 2f * u);
                        walkInPos = Vector3.Lerp(doorPos, counterStandPos, s);
                        if (u < 0.97f)
                        {
                            Vector3 dir = counterStandPos - doorPos;
                            walkInYaw = (float)Math.Atan2(dir.X, dir.Z);
                            walkInClip = "desk_walkin_walk";
                            WalkInIsWalking = true;
                        }
                        else
                        {
                            // Face diagonally across the front counter toward the Desk Sergeant!
                            Vector3 toSgt = sgtPos - counterStandPos;
                            walkInYaw = (float)Math.Atan2(toSgt.X, toSgt.Z);
                            walkInClip = "desk_walkin_plead";
                        }
                    }
                    else
                    {
                        float u = Math.Max(0f, Math.Min(1f, walkIn.WalkInLeaveProgress));
                        float s = u * u * (3f - 2f * u);
                        walkInPos = Vector3.Lerp(counterStandPos, doorPos, s);
                        Vector3 dir = doorPos - counterStandPos;
                        walkInYaw = (float)Math.Atan2(dir.X, dir.Z);
                        walkInClip = "desk_walkin_walk";
                        WalkInIsWalking = u < 0.96f;
                    }
                }

                // 3. Dynamic 3D Camera (frames Desk Sergeant + Counter + Walk-In + Window cleanly in open right 72%!)
                float gentleSway = (float)Math.Sin(t * 0.38f) * 0.28f;
                if (game.IsBriefing)
                {
                    camera.Position = new Vector3(2.30f + gentleSway * 1.4f, 3.05f, 5.45f);
                    camera.Target = new Vector3(-0.35f, 1.62f, -1.10f);
                }
                else if (game.IsFinished)
                {
                    camera.Position = new Vector3(1.85f + gentleSway * 1.2f, 2.75f, 4.45f);
                    camera.Target = new Vector3(-0.15f, 1.72f, -1.25f);
                }
                else
                {
                    float walkBias = WalkInVisible ? 0.35f : 0f;
                    camera.Position = new Vector3(2.25f + walkBias + gentleSway, 3.10f, 5.55f);
                    camera.Target = new Vector3(-0.30f + walkBias * 0.6f, 1.56f, -0.95f);
                }

                // 4. Build all 3D Precinct Lobby meshes (room, desk, switchboard, corkboard, outdoor cruisers/fire truck!)
                litTris.Clear();
                unlitTris.Clear();
                BuildPrecinctEnvironment(game, sgtPos, WalkInVisible ? (Vector3?)walkInPos : null, handsetLifted, t);

                // 5. Pose & animate Desk Sergeant & Walk-in Townie avatars
                if (sergeantAvatar != null)
                {
                    if (sergeantPlayer.Play(sgtClip, 0.18f, sgtSpeed, sgtLoop))
                    {
                        sergeantPlayer.UpdateAndApply(sergeantAvatar, dt, useSquad: true);
                        lastActiveAnimName = sergeantPlayer.ActiveClipName;
                    }
                    sergeantAvatar.Scale = Vector3.One * SimScale;
                    sergeantAvatar.Position = sgtPos;
                    sergeantAvatar.RotationY = sgtYaw;
                    sergeantAvatar.RotationX = 0f;
                    sergeantAvatar.AmbientLight = new Vector4(1.18f, 1.15f, 1.10f, 1.0f);
                    sergeantAvatar.ReloadSkeleton();
                }

                if (walkInAvatar != null)
                {
                    if (WalkInVisible)
                    {
                        if (walkInPlayer.Play(walkInClip, 0.16f, 1.0f, true))
                            walkInPlayer.UpdateAndApply(walkInAvatar, dt, useSquad: true);
                        walkInAvatar.Scale = Vector3.One * SimScale;
                        walkInAvatar.Position = walkInPos;
                        walkInAvatar.RotationY = walkInYaw;
                        walkInAvatar.RotationX = 0f;
                        walkInAvatar.AmbientLight = new Vector4(1.15f, 1.13f, 1.10f, 1.0f);
                        walkInAvatar.ReloadSkeleton();
                    }
                    else
                    {
                        walkInAvatar.Position = new Vector3(0f, -60f, 0f);
                        walkInAvatar.ReloadSkeleton();
                    }
                }

                // 6. Render unified 3D Precinct + Avatars into RenderTarget2D with Depth24 Z-buffer
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
                    // Twilight SimCity sky visible through the tall precinct street windows!
                    gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(34, 52, 86), 1f, 0);

                    gd.RasterizerState = RasterizerState.CullNone;
                    gd.DepthStencilState = DepthStencilState.Default;
                    gd.BlendState = BlendState.Opaque;

                    worldEffect.World = Matrix.Identity;
                    worldEffect.View = camera.View;
                    worldEffect.Projection = camera.Projection;
                    worldEffect.VertexColorEnabled = true;
                    worldEffect.TextureEnabled = false;
                    worldEffect.LightingEnabled = true;
                    worldEffect.AmbientLightColor = new Vector3(0.54f, 0.56f, 0.62f);
                    worldEffect.DirectionalLight0.Enabled = true;
                    worldEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.40f));
                    worldEffect.DirectionalLight0.DiffuseColor = new Vector3(0.92f, 0.87f, 0.78f);
                    worldEffect.DirectionalLight0.SpecularColor = Vector3.Zero;
                    worldEffect.DirectionalLight1.Enabled = true;
                    worldEffect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.65f, -0.30f, 0.70f));
                    worldEffect.DirectionalLight1.DiffuseColor = new Vector3(0.28f, 0.36f, 0.52f);
                    worldEffect.DirectionalLight1.SpecularColor = Vector3.Zero;
                    worldEffect.DirectionalLight2.Enabled = false;

                    if (litTris.Count >= 3)
                    {
                        if (litArray.Length < litTris.Count)
                            litArray = new VertexPositionColorNormal[Math.Max(litTris.Count, 4096)];
                        litTris.CopyTo(litArray, 0);
                        foreach (var pass in worldEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            gd.DrawUserPrimitives(PrimitiveType.TriangleList, litArray, 0, litTris.Count / 3);
                        }
                    }

                    if (unlitTris.Count >= 3)
                    {
                        worldEffect.LightingEnabled = false;
                        gd.BlendState = BlendState.AlphaBlend;
                        if (unlitArray.Length < unlitTris.Count)
                            unlitArray = new VertexPositionColor[Math.Max(unlitTris.Count, 1024)];
                        unlitTris.CopyTo(unlitArray, 0);
                        foreach (var pass in worldEffect.CurrentTechnique.Passes)
                        {
                            pass.Apply();
                            gd.DrawUserPrimitives(PrimitiveType.TriangleList, unlitArray, 0, unlitTris.Count / 3);
                        }
                    }

                    if (avatarScene != null)
                    {
                        gd.RasterizerState = RasterizerState.CullCounterClockwise;
                        gd.DepthStencilState = DepthStencilState.Default;
                        gd.BlendState = BlendState.Opaque;
                        avatarScene.Draw(gd);
                    }

                    // Project avatar head positions to normalized screen coords for floating speech callouts
                    var vp = new Viewport(0, 0, targetW, targetH);
                    SergeantHeadNorm = ProjectNorm(vp, sgtPos + new Vector3(0f, 3.15f, 0f));
                    if (WalkInVisible)
                        WalkInHeadNorm = ProjectNorm(vp, walkInPos + new Vector3(0f, 3.15f, 0f));

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
                Boot.Log("dispatch3d predraw error: " + e.Message);
                hasFrame = false;
            }
        }

        Vector2 ProjectNorm(Viewport vp, Vector3 worldPt)
        {
            var sp = vp.Project(worldPt, camera.Projection, camera.View, Matrix.Identity);
            return new Vector2(sp.X / Math.Max(1, vp.Width), sp.Y / Math.Max(1, vp.Height));
        }

        void BuildPrecinctEnvironment(
            DispatchTriageGame game,
            Vector3 sgtPos,
            Vector3? walkInPos,
            bool handsetLifted,
            float t)
        {
            // 1. Polished Checkered Marble & Slate Precinct Lobby Floor
            for (int ix = -9; ix < 9; ix++)
            {
                for (int iz = -5; iz < 8; iz++)
                {
                    bool even = ((ix + iz) & 1) == 0;
                    Color tile = even ? new Color(188, 192, 198) : new Color(62, 74, 94);
                    AddQuadY(ix * 1.1f, (ix + 1) * 1.1f, iz * 1.1f, (iz + 1) * 1.1f, 0f, tile);
                }
            }

            // Recessed Navy & Brass SimCity PD Floor Medallion right in front of the counter!
            AddDisk(new Vector3(0.6f, 0.014f, 1.45f), 1.35f, new Color(28, 52, 92), 20);
            AddDisk(new Vector3(0.6f, 0.020f, 1.45f), 1.05f, new Color(214, 174, 62), 20);
            AddDisk(new Vector3(0.6f, 0.024f, 1.45f), 0.88f, new Color(34, 62, 108), 20);

            // 2. Precinct Back Wall (left solid plaster + right wide paned street window!)
            // Left solid wall section (X = -9.5..+1.5)
            AddBox(new Vector3(-4.0f, 2.75f, -4.35f), new Vector3(11.0f, 5.5f, 0.35f), new Color(218, 212, 196));
            // Dark navy wainscoting along lower back wall
            AddBox(new Vector3(-4.0f, 0.78f, -4.16f), new Vector3(11.0f, 1.56f, 0.08f), new Color(36, 52, 78));
            AddBox(new Vector3(-4.0f, 1.58f, -4.12f), new Vector3(11.0f, 0.12f, 0.14f), new Color(156, 114, 64));

            // Right wall frame around tall paned street window (X = +1.5..+9.5, window opening X = +1.8..+7.6, Y = 1.4..4.4)
            AddBox(new Vector3(5.5f, 0.72f, -4.35f), new Vector3(8.0f, 1.44f, 0.35f), new Color(36, 52, 78));
            AddBox(new Vector3(5.5f, 4.95f, -4.35f), new Vector3(8.0f, 1.10f, 0.35f), new Color(218, 212, 196));
            AddBox(new Vector3(8.6f, 2.90f, -4.35f), new Vector3(2.0f, 3.10f, 0.35f), new Color(218, 212, 196));
            // Window mullions & sill
            AddBox(new Vector3(4.7f, 1.44f, -4.18f), new Vector3(6.2f, 0.12f, 0.30f), new Color(156, 114, 64));
            AddBox(new Vector3(4.7f, 4.40f, -4.22f), new Vector3(6.2f, 0.14f, 0.24f), new Color(56, 68, 86));
            AddBox(new Vector3(1.65f, 2.92f, -4.22f), new Vector3(0.18f, 3.05f, 0.24f), new Color(56, 68, 86));
            AddBox(new Vector3(4.70f, 2.92f, -4.22f), new Vector3(0.14f, 3.05f, 0.22f), new Color(56, 68, 86));
            AddBox(new Vector3(7.65f, 2.92f, -4.22f), new Vector3(0.18f, 3.05f, 0.24f), new Color(56, 68, 86));
            AddBox(new Vector3(4.70f, 2.92f, -4.22f), new Vector3(6.05f, 0.10f, 0.18f), new Color(56, 68, 86));

            // Left Side Wall with Glass Precinct Entry Door (where 3D Walk-in Citizens enter!)
            AddBox(new Vector3(-7.4f, 2.75f, 1.2f), new Vector3(0.35f, 5.5f, 11.4f), new Color(204, 198, 182));
            AddBox(new Vector3(-7.22f, 0.78f, 1.2f), new Vector3(0.08f, 1.56f, 11.4f), new Color(36, 52, 78));
            // Entry Door Frame on left wall (around Z = 2.75)
            AddBox(new Vector3(-7.18f, 1.48f, 2.75f), new Vector3(0.14f, 2.96f, 1.95f), new Color(52, 66, 88));
            AddBox(new Vector3(-7.12f, 1.50f, 2.75f), new Vector3(0.08f, 2.68f, 1.65f), new Color(92, 134, 178));
            // Brass door push-bar
            AddBox(new Vector3(-7.05f, 1.22f, 2.75f), new Vector3(0.08f, 0.08f, 1.35f), new Color(224, 184, 72));
            // Warm glowing "ENTRANCE" lintel sign above door
            AddBox(new Vector3(-7.10f, 3.16f, 2.75f), new Vector3(0.12f, 0.28f, 1.30f), new Color(24, 34, 52));
            AddUnlitQuadX(-7.03f, 3.06f, 3.26f, 2.25f, 3.25f, new Color(110, 236, 175, 220));

            // Right Side Wall & Ceiling Beams
            AddBox(new Vector3(8.5f, 2.75f, 1.2f), new Vector3(0.35f, 5.5f, 11.4f), new Color(204, 198, 182));
            AddBox(new Vector3(0.5f, 5.45f, 1.0f), new Vector3(18.0f, 0.24f, 12.0f), new Color(228, 226, 218));
            // Ceiling fluorescent warm light panels
            AddUnlitQuadY(-2.0f, 2.4f, -1.6f, -0.4f, 5.30f, new Color(255, 248, 226, 235));
            AddUnlitQuadY(1.2f, 4.6f, 1.4f, 2.4f, 5.30f, new Color(255, 248, 226, 215));

            // 3. OUTSIDE THROUGH THE PRECINCT WINDOW: SimCity Twilight Street + Rolling Police Cruiser & Fire Engine!
            AddQuadY(-6f, 20f, -18f, -4.5f, -0.05f, new Color(56, 62, 72)); // Street asphalt
            AddQuadY(-6f, 20f, -6.2f, -4.5f, 0.04f, new Color(142, 146, 152)); // Precinct sidewalk curb
            // Dashed yellow street center stripe
            for (float sx = -2f; sx < 16f; sx += 2.6f)
                AddQuadY(sx, sx + 1.3f, -11.0f, -10.75f, -0.02f, new Color(230, 190, 56));
            // Distant lit SimCity buildings across the boulevard
            AddDistantCityBuilding(2.2f, -15.5f, 3.2f, 6.2f, new Color(46, 60, 88));
            AddDistantCityBuilding(6.2f, -16.2f, 3.8f, 7.6f, new Color(54, 68, 96));
            AddDistantCityBuilding(10.5f, -15.0f, 3.5f, 5.4f, new Color(42, 54, 80));

            // 3D Police Patrol Cruiser parked outside window — ROLLS OUT with flashing sirens when Patrol is dispatched!
            float patrolDrive = game.PatrolRolloutTimerSec > 0f ? (3.2f - game.PatrolRolloutTimerSec) / 3.2f : 0f;
            float cruiserX = 3.2f + patrolDrive * patrolDrive * 14.5f;
            AddOutdoorPoliceCruiser(cruiserX, -7.8f, game.PatrolRolloutTimerSec > 0f, t);

            // 3D Red SimCity Fire Engine parked further out — ROLLS OUT with flashing beacon when Fire is dispatched!
            float fireDrive = game.FireRolloutTimerSec > 0f ? (3.6f - game.FireRolloutTimerSec) / 3.6f : 0f;
            float fireX = 6.8f - fireDrive * fireDrive * 15.5f;
            AddOutdoorFireTruck(fireX, -12.2f, game.FireRolloutTimerSec > 0f, t);

            // 4. SIMCITY SECTOR MAP CORKBOARD + LIVE BLINKING INCIDENT PINS (Back Wall behind Desk Sergeant!)
            AddBox(new Vector3(-1.35f, 2.90f, -4.14f), new Vector3(4.10f, 2.05f, 0.12f), new Color(138, 92, 48)); // Oak frame
            AddBox(new Vector3(-1.35f, 2.90f, -4.08f), new Vector3(3.84f, 1.80f, 0.06f), new Color(46, 68, 96));  // Map navy backing
            // Stylized SimCity road grid lines on the map
            AddBox(new Vector3(-1.35f, 3.15f, -4.04f), new Vector3(3.50f, 0.06f, 0.02f), new Color(110, 152, 198));
            AddBox(new Vector3(-1.35f, 2.58f, -4.04f), new Vector3(3.50f, 0.06f, 0.02f), new Color(110, 152, 198));
            AddBox(new Vector3(-2.15f, 2.90f, -4.04f), new Vector3(0.06f, 1.55f, 0.02f), new Color(110, 152, 198));
            AddBox(new Vector3(-0.60f, 2.90f, -4.04f), new Vector3(0.06f, 1.55f, 0.02f), new Color(110, 152, 198));
            // Park green & river blue accents on the SimCity map
            AddBox(new Vector3(-2.65f, 3.35f, -4.04f), new Vector3(0.75f, 0.45f, 0.02f), new Color(72, 134, 86));
            AddBox(new Vector3(-0.05f, 2.35f, -4.04f), new Vector3(0.95f, 0.36f, 0.02f), new Color(58, 128, 188));

            // Live blinking incident LEDs on the SimCity Sector Map matching active calls!
            var activeCases = game.GetActiveCases();
            Vector3[] mapPinSpots =
            {
                new Vector3(-2.35f, 3.18f, -3.99f),
                new Vector3(-1.15f, 2.65f, -3.99f),
                new Vector3(-0.20f, 3.28f, -3.99f),
            };
            for (int i = 0; i < activeCases.Count && i < mapPinSpots.Length; i++)
            {
                var c = activeCases[i];
                bool pulse = ((int)(t * 5.0f + i) % 2) == 0;
                Color pinCol = c.CorrectAction == DispatchRouteAction.DispatchFire
                    ? new Color(255, 118, 42)
                    : (c.Channel == DispatchCallChannel.WalkIn ? new Color(72, 236, 178) : new Color(255, 76, 86));
                if (pulse)
                    AddUnlitBox(mapPinSpots[i], new Vector3(0.22f, 0.22f, 0.08f), new Color(pinCol.R, pinCol.G, pinCol.B, (byte)155));
                AddUnlitBox(mapPinSpots[i], new Vector3(0.12f, 0.12f, 0.10f), pinCol);
            }

            // Gold SimCity PD Crest Plaque & Precinct Wall Clock
            AddBox(new Vector3(0.45f, 4.35f, -4.14f), new Vector3(0.85f, 0.92f, 0.10f), new Color(222, 182, 62));
            AddBox(new Vector3(0.45f, 4.35f, -4.08f), new Vector3(0.62f, 0.70f, 0.08f), new Color(28, 52, 92));
            // Wall clock beside corkboard
            AddBox(new Vector3(-4.15f, 3.35f, -4.14f), new Vector3(0.76f, 0.76f, 0.10f), new Color(46, 56, 72));
            AddBox(new Vector3(-4.15f, 3.35f, -4.08f), new Vector3(0.62f, 0.62f, 0.08f), new Color(242, 240, 232));
            AddBox(new Vector3(-4.15f, 3.44f, -4.03f), new Vector3(0.04f, 0.22f, 0.03f), new Color(26, 30, 38));
            AddBox(new Vector3(-4.06f, 3.35f, -4.03f), new Vector3(0.18f, 0.04f, 0.03f), new Color(26, 30, 38));

            // Tall steel filing cabinets behind left desk wing
            AddBox(new Vector3(-4.35f, 1.05f, -3.55f), new Vector3(0.88f, 2.10f, 0.85f), new Color(96, 108, 122));
            AddBox(new Vector3(-5.30f, 1.05f, -3.55f), new Vector3(0.88f, 2.10f, 0.85f), new Color(88, 100, 114));
            for (int d = 0; d < 4; d++)
            {
                float dy = 0.34f + d * 0.48f;
                AddBox(new Vector3(-4.35f, dy, -3.11f), new Vector3(0.72f, 0.38f, 0.04f), new Color(116, 128, 142));
                AddBox(new Vector3(-4.35f, dy + 0.05f, -3.08f), new Vector3(0.22f, 0.05f, 0.04f), new Color(216, 220, 224));
            }

            // 5. MAIN PRECINCT FRONT DESK COUNTER (oak paneling, marble top, brass rail & crest!)
            // Main counter body (X = -2.3..+3.3, Z = -0.35)
            AddBox(new Vector3(0.50f, 0.58f, -0.35f), new Vector3(5.60f, 1.16f, 1.05f), new Color(122, 80, 44));
            // Recessed decorative front panels
            for (float px = -1.6f; px <= 2.6f; px += 1.4f)
                AddBox(new Vector3(px, 0.56f, 0.18f), new Vector3(1.12f, 0.78f, 0.05f), new Color(98, 62, 34));
            // Dark navy granite countertop & raised front reception lip
            AddBox(new Vector3(0.50f, 1.19f, -0.35f), new Vector3(5.82f, 0.09f, 1.22f), new Color(40, 52, 72));
            AddBox(new Vector3(0.50f, 1.32f, 0.14f), new Vector3(5.78f, 0.18f, 0.24f), new Color(138, 92, 52));
            AddBox(new Vector3(0.50f, 1.43f, 0.14f), new Vector3(5.84f, 0.06f, 0.28f), new Color(46, 58, 80));
            // Gold "SIMCITY PD • DESK SERGEANT" front badge emblem on the counter facade
            AddBox(new Vector3(0.50f, 0.68f, 0.22f), new Vector3(0.95f, 0.52f, 0.06f), new Color(224, 184, 66));
            AddBox(new Vector3(0.50f, 0.68f, 0.25f), new Vector3(0.76f, 0.36f, 0.04f), new Color(28, 48, 86));
            // Brass lobby footrail
            AddBox(new Vector3(0.50f, 0.12f, 0.34f), new Vector3(5.40f, 0.06f, 0.06f), new Color(214, 174, 62));

            // 6. INTERACTIVE DESK PROPS (Retro 911 Switchboard, Lifting Telephone, Radio Mic, Lamp, Coffee Mug!)
            // Retro Multi-Line 911 Switchboard Console (left-center of counter)
            AddBox(new Vector3(-0.95f, 1.42f, -0.35f), new Vector3(1.05f, 0.42f, 0.56f), new Color(48, 56, 68));
            AddBox(new Vector3(-0.95f, 1.66f, -0.45f), new Vector3(0.96f, 0.16f, 0.32f), new Color(34, 40, 50));
            // 3 Live blinking switchboard line lamps!
            for (int slot = 0; slot < 3; slot++)
            {
                bool lit = activeCases.Any(c => c.SlotNumber == slot);
                bool blink = lit && (((int)(t * 6.0f + slot) % 2) == 0);
                Color lampCol = !lit
                    ? new Color(42, 58, 52)
                    : (slot == 2
                        ? (blink ? new Color(92, 255, 198) : new Color(44, 152, 118))
                        : (blink ? new Color(255, 84, 72) : new Color(172, 46, 42)));
                AddUnlitBox(new Vector3(-1.25f + slot * 0.30f, 1.54f, -0.06f), new Vector3(0.16f, 0.10f, 0.05f), lampCol);
            }

            // Classic Retro 911 Desk Telephone (handset lifts off cradle when Sergeant answers!)
            AddBox(new Vector3(0.32f, 1.32f, -0.45f), new Vector3(0.34f, 0.16f, 0.30f), new Color(32, 36, 44));
            if (!handsetLifted)
            {
                AddBox(new Vector3(0.32f, 1.44f, -0.45f), new Vector3(0.42f, 0.09f, 0.11f), new Color(22, 26, 32));
            }
            else
            {
                // Handset held right beside Sergeant's right ear!
                AddBox(new Vector3(sgtPos.X - 0.30f, 2.66f, sgtPos.Z + 0.12f), new Vector3(0.10f, 0.34f, 0.12f), new Color(24, 28, 36));
            }

            // Police Radio Base Console + glowing TX light when dispatching!
            AddBox(new Vector3(1.22f, 1.36f, -0.48f), new Vector3(0.48f, 0.24f, 0.34f), new Color(44, 52, 64));
            bool txActive = game.CurrentGesture == DeskSergeantGesture.RadioDispatch ||
                            game.PatrolRolloutTimerSec > 2.2f || game.FireRolloutTimerSec > 2.6f;
            AddUnlitBox(new Vector3(1.22f, 1.40f, -0.30f), new Vector3(0.14f, 0.07f, 0.04f),
                txActive ? new Color(84, 255, 156) : new Color(52, 92, 68));

            // Stack of case folders, notepad & warm brass precinct desk lamp + coffee mug
            AddBox(new Vector3(2.15f, 1.28f, -0.38f), new Vector3(0.46f, 0.10f, 0.36f), new Color(228, 206, 154));
            AddBox(new Vector3(2.18f, 1.34f, -0.40f), new Vector3(0.42f, 0.04f, 0.32f), new Color(244, 242, 234));
            // Classic green-shade banker's desk lamp on the right wing of the counter
            AddBox(new Vector3(2.75f, 1.42f, -0.42f), new Vector3(0.12f, 0.38f, 0.12f), new Color(210, 172, 64));
            AddBox(new Vector3(2.75f, 1.66f, -0.42f), new Vector3(0.44f, 0.16f, 0.26f), new Color(46, 134, 88));
            AddUnlitBox(new Vector3(2.75f, 1.56f, -0.42f), new Vector3(0.32f, 0.08f, 0.16f), new Color(255, 232, 166, 225));
            // Precinct coffee mug!
            AddBox(new Vector3(-0.18f, 1.32f, -0.26f), new Vector3(0.14f, 0.18f, 0.14f), new Color(238, 240, 244));

            // Lobby Wooden Waiting Bench & Potted Ficus Plant
            AddBox(new Vector3(5.65f, 0.54f, 2.35f), new Vector3(2.20f, 0.14f, 0.72f), new Color(138, 92, 52));
            AddBox(new Vector3(5.65f, 0.94f, 2.66f), new Vector3(2.20f, 0.68f, 0.12f), new Color(122, 80, 44));
            AddBox(new Vector3(4.75f, 0.27f, 2.35f), new Vector3(0.14f, 0.54f, 0.62f), new Color(56, 62, 72));
            AddBox(new Vector3(6.55f, 0.27f, 2.35f), new Vector3(0.14f, 0.54f, 0.62f), new Color(56, 62, 72));

            // Potted Lobby Ficus Plant next to window
            AddBox(new Vector3(4.65f, 0.42f, -3.35f), new Vector3(0.68f, 0.84f, 0.68f), new Color(164, 94, 58));
            AddBox(new Vector3(4.65f, 1.35f, -3.35f), new Vector3(0.16f, 1.10f, 0.16f), new Color(98, 70, 42));
            AddBox(new Vector3(4.65f, 2.08f, -3.35f), new Vector3(1.10f, 0.95f, 1.10f), new Color(54, 118, 62));
            AddBox(new Vector3(4.65f, 2.62f, -3.35f), new Vector3(0.76f, 0.64f, 0.76f), new Color(68, 138, 74));

            // Grounded soft contact shadows beneath Desk Sergeant & Walk-In Citizen!
            AddUnlitDisk(new Vector3(sgtPos.X, 0.028f, sgtPos.Z), 0.62f, new Color(10, 14, 24, 110), 16);
            if (walkInPos.HasValue)
                AddUnlitDisk(new Vector3(walkInPos.Value.X, 0.028f, walkInPos.Value.Z), 0.62f, new Color(10, 14, 24, 120), 16);
        }

        void AddOutdoorPoliceCruiser(float x, float z, bool sirenActive, float t)
        {
            // Classic black-and-white SimCity Police Squad Car seen through the precinct window
            AddBox(new Vector3(x, 0.58f, z), new Vector3(3.25f, 0.64f, 1.48f), new Color(30, 38, 52));
            AddBox(new Vector3(x + 0.05f, 0.62f, z), new Vector3(1.45f, 0.56f, 1.50f), new Color(238, 242, 246));
            AddBox(new Vector3(x - 0.15f, 1.12f, z), new Vector3(1.75f, 0.52f, 1.32f), new Color(238, 242, 246));
            AddBox(new Vector3(x - 0.15f, 1.12f, z), new Vector3(1.45f, 0.36f, 1.36f), new Color(74, 108, 142));
            // Wheels
            AddBox(new Vector3(x - 1.0f, 0.28f, z + 0.68f), new Vector3(0.56f, 0.56f, 0.22f), new Color(24, 26, 30));
            AddBox(new Vector3(x + 1.0f, 0.28f, z + 0.68f), new Vector3(0.56f, 0.56f, 0.22f), new Color(24, 26, 30));
            // Pulsing red/blue lightbar!
            float rate = sirenActive ? 9.5f : 2.5f;
            bool flip = ((int)(t * rate) % 2) == 0;
            AddUnlitBox(new Vector3(x - 0.15f, 1.46f, z - 0.26f), new Vector3(0.32f, 0.16f, 0.42f),
                flip ? new Color(255, 62, 62) : new Color(110, 32, 36));
            AddUnlitBox(new Vector3(x - 0.15f, 1.46f, z + 0.26f), new Vector3(0.32f, 0.16f, 0.42f),
                !flip ? new Color(64, 152, 255) : new Color(32, 62, 118));
        }

        void AddOutdoorFireTruck(float x, float z, bool sirenActive, float t)
        {
            // Bright crimson SimCity Fire Engine with white stripe & chrome ladder!
            AddBox(new Vector3(x, 0.96f, z), new Vector3(4.60f, 1.38f, 1.72f), new Color(198, 42, 42));
            AddBox(new Vector3(x, 0.92f, z), new Vector3(4.64f, 0.22f, 1.74f), new Color(242, 244, 248));
            AddBox(new Vector3(x - 1.55f, 1.26f, z), new Vector3(1.25f, 0.65f, 1.74f), new Color(84, 122, 158));
            AddBox(new Vector3(x + 0.55f, 1.76f, z), new Vector3(3.20f, 0.22f, 0.62f), new Color(206, 214, 224));
            bool flash = sirenActive && (((int)(t * 8.0f) % 2) == 0);
            AddUnlitBox(new Vector3(x - 1.65f, 1.82f, z), new Vector3(0.38f, 0.22f, 0.92f),
                flash ? new Color(255, 198, 56) : new Color(224, 62, 46));
        }

        void AddDistantCityBuilding(float x, float z, float w, float h, Color col)
        {
            AddBox(new Vector3(x, h * 0.5f, z), new Vector3(w, h, 2.2f), col);
            for (float wy = 1.4f; wy < h - 0.8f; wy += 1.25f)
            {
                for (float wx = x - w * 0.32f; wx <= x + w * 0.32f; wx += 0.95f)
                    AddUnlitBox(new Vector3(wx, wy, z + 1.12f), new Vector3(0.46f, 0.52f, 0.04f), new Color(248, 224, 146, 180));
            }
        }

        // ---- Geometry Primitives ----

        void AddQuadY(float x0, float x1, float z0, float z1, float y, Color c)
        {
            var n = Vector3.Up;
            var a = new VertexPositionColorNormal(new Vector3(x0, y, z0), c, n);
            var b = new VertexPositionColorNormal(new Vector3(x1, y, z0), c, n);
            var d = new VertexPositionColorNormal(new Vector3(x1, y, z1), c, n);
            var e = new VertexPositionColorNormal(new Vector3(x0, y, z1), c, n);
            litTris.Add(a); litTris.Add(b); litTris.Add(d);
            litTris.Add(a); litTris.Add(d); litTris.Add(e);
        }

        void AddDisk(Vector3 center, float radius, Color c, int segs)
        {
            var n = Vector3.Up;
            var vc = new VertexPositionColorNormal(center, c, n);
            for (int i = 0; i < segs; i++)
            {
                float a0 = (float)(i * 2.0 * Math.PI / segs);
                float a1 = (float)((i + 1) * 2.0 * Math.PI / segs);
                var p0 = new Vector3(center.X + (float)Math.Cos(a0) * radius, center.Y, center.Z + (float)Math.Sin(a0) * radius);
                var p1 = new Vector3(center.X + (float)Math.Cos(a1) * radius, center.Y, center.Z + (float)Math.Sin(a1) * radius);
                litTris.Add(vc);
                litTris.Add(new VertexPositionColorNormal(p0, c, n));
                litTris.Add(new VertexPositionColorNormal(p1, c, n));
            }
        }

        void AddUnlitDisk(Vector3 center, float radius, Color c, int segs)
        {
            var vc = new VertexPositionColor(center, c);
            for (int i = 0; i < segs; i++)
            {
                float a0 = (float)(i * 2.0 * Math.PI / segs);
                float a1 = (float)((i + 1) * 2.0 * Math.PI / segs);
                var p0 = new Vector3(center.X + (float)Math.Cos(a0) * radius, center.Y, center.Z + (float)Math.Sin(a0) * radius);
                var p1 = new Vector3(center.X + (float)Math.Cos(a1) * radius, center.Y, center.Z + (float)Math.Sin(a1) * radius);
                unlitTris.Add(vc);
                unlitTris.Add(new VertexPositionColor(p0, c));
                unlitTris.Add(new VertexPositionColor(p1, c));
            }
        }

        void AddUnlitQuadY(float x0, float x1, float z0, float z1, float y, Color c)
        {
            var a = new VertexPositionColor(new Vector3(x0, y, z0), c);
            var b = new VertexPositionColor(new Vector3(x1, y, z0), c);
            var d = new VertexPositionColor(new Vector3(x1, y, z1), c);
            var e = new VertexPositionColor(new Vector3(x0, y, z1), c);
            unlitTris.Add(a); unlitTris.Add(b); unlitTris.Add(d);
            unlitTris.Add(a); unlitTris.Add(d); unlitTris.Add(e);
        }

        void AddUnlitQuadX(float x, float y0, float y1, float z0, float z1, Color c)
        {
            var a = new VertexPositionColor(new Vector3(x, y0, z0), c);
            var b = new VertexPositionColor(new Vector3(x, y1, z0), c);
            var d = new VertexPositionColor(new Vector3(x, y1, z1), c);
            var e = new VertexPositionColor(new Vector3(x, y0, z1), c);
            unlitTris.Add(a); unlitTris.Add(b); unlitTris.Add(d);
            unlitTris.Add(a); unlitTris.Add(d); unlitTris.Add(e);
        }

        void AddBox(Vector3 center, Vector3 size, Color c)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Vector3 p000 = center + new Vector3(-hx, -hy, -hz);
            Vector3 p100 = center + new Vector3(hx, -hy, -hz);
            Vector3 p110 = center + new Vector3(hx, hy, -hz);
            Vector3 p010 = center + new Vector3(-hx, hy, -hz);
            Vector3 p001 = center + new Vector3(-hx, -hy, hz);
            Vector3 p101 = center + new Vector3(hx, -hy, hz);
            Vector3 p111 = center + new Vector3(hx, hy, hz);
            Vector3 p011 = center + new Vector3(-hx, hy, hz);

            AddFace(p001, p101, p111, p011, Vector3.UnitZ, c);
            AddFace(p100, p000, p010, p110, -Vector3.UnitZ, c);
            AddFace(p011, p111, p110, p010, Vector3.UnitY, c);
            AddFace(p000, p100, p101, p001, -Vector3.UnitY, c);
            AddFace(p101, p100, p110, p111, Vector3.UnitX, c);
            AddFace(p000, p001, p011, p010, -Vector3.UnitX, c);
        }

        void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            var va = new VertexPositionColorNormal(a, col, n);
            var vb = new VertexPositionColorNormal(b, col, n);
            var vc = new VertexPositionColorNormal(c, col, n);
            var vd = new VertexPositionColorNormal(d, col, n);
            litTris.Add(va); litTris.Add(vb); litTris.Add(vc);
            litTris.Add(va); litTris.Add(vc); litTris.Add(vd);
        }

        void AddUnlitBox(Vector3 center, Vector3 size, Color c)
        {
            float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
            Vector3 p000 = center + new Vector3(-hx, -hy, -hz);
            Vector3 p100 = center + new Vector3(hx, -hy, -hz);
            Vector3 p110 = center + new Vector3(hx, hy, -hz);
            Vector3 p010 = center + new Vector3(-hx, hy, -hz);
            Vector3 p001 = center + new Vector3(-hx, -hy, hz);
            Vector3 p101 = center + new Vector3(hx, -hy, hz);
            Vector3 p111 = center + new Vector3(hx, hy, hz);
            Vector3 p011 = center + new Vector3(-hx, hy, hz);

            AddUnlitFace(p001, p101, p111, p011, c);
            AddUnlitFace(p100, p000, p010, p110, c);
            AddUnlitFace(p011, p111, p110, p010, c);
            AddUnlitFace(p101, p100, p110, p111, c);
            AddUnlitFace(p000, p001, p011, p010, c);
        }

        void AddUnlitFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            var va = new VertexPositionColor(a, col);
            var vb = new VertexPositionColor(b, col);
            var vc = new VertexPositionColor(c, col);
            var vd = new VertexPositionColor(d, col);
            unlitTris.Add(va); unlitTris.Add(vb); unlitTris.Add(vc);
            unlitTris.Add(va); unlitTris.Add(vc); unlitTris.Add(vd);
        }

        // ---- Resource & Authentic TS1 Avatar Setup ----

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
                    camera = new BasicCamera(gd, new Vector3(2.25f, 3.10f, 5.55f), new Vector3(-0.30f, 1.56f, -0.95f), Vector3.Up);
                    camera.NearPlane = 0.1f;
                    camera.FarPlane = 120f;
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
                Boot.Log("dispatch3d init error: " + e.Message);
                initFailed = true;
                return false;
            }
        }

        void EnsureSergeantAvatar(VMAvatar sourceSim)
        {
            string key = sourceSim != null ? (sourceSim.Name + "#" + sourceSim.ObjectID) : "default";
            if (sergeantAvatar != null && key == boundSergeantKey) return;

            try
            {
                var content = Content.Get();
                if (content == null || content.AvatarSkeletons == null) return;
                var baseSkel = content.AvatarSkeletons.Get("adult.skel");
                if (baseSkel == null) return;

                var sim = new SimAvatar(baseSkel.Clone());

                // Preserve player's actual Sim head/face/hair!
                if (sourceSim != null && sourceSim.Avatar != null)
                {
                    try { if (sourceSim.Avatar.Head != null) sim.Head = sourceSim.Avatar.Head; } catch { }
                }

                // Dress in authentic TS1 Law Enforcement Desk Sergeant uniform (level 3 = Desk Sergeant)!
                string tag;
                var uniform = CadetAcademySceneRenderer.ResolveLawEnforcementUniform(content, sourceSim, out tag, new[] { 3, 2, 1, 0 });
                if (uniform != null)
                {
                    sim.Body = uniform;
                    sim.Handgroup = uniform;
                    boundUniformTag = tag;
                }
                else if (sourceSim != null && sourceSim.Avatar != null)
                {
                    try { if (sourceSim.Avatar.Body != null) sim.Body = sourceSim.Avatar.Body; } catch { }
                    try { if (sourceSim.Avatar.Handgroup != null) sim.Handgroup = sourceSim.Avatar.Handgroup; } catch { }
                    boundUniformTag = "civilian";
                }

                if (sim.Head == null)
                    sim.Head = new Outfit { TS1AppearanceID = "c_pizzaguy.apr", TS1TextureID = "pizzaguyface" };
                if (sim.Body == null)
                {
                    var fb = new Outfit
                    {
                        TS1AppearanceID = "b001mafit_01.apr",
                        TS1TextureID = "pizzaguysuit",
                        LiteralHandgroup = BuildAdultHands("lgt")
                    };
                    sim.Body = fb;
                    sim.Handgroup = fb;
                }
                else if (sim.Handgroup == null)
                {
                    sim.Handgroup = new Outfit { LiteralHandgroup = BuildAdultHands("lgt") };
                }

                if (avatarScene != null)
                {
                    if (sergeantAvatar != null) avatarScene.Remove(sergeantAvatar);
                    avatarScene.Add(sim);
                }
                sergeantAvatar = sim;
                boundSergeantKey = key;
            }
            catch (Exception e)
            {
                Boot.Log("dispatch3d sergeant avatar error: " + e.Message);
            }
        }

        void EnsureWalkInAvatar(DispatchActiveCase walkIn, VMAvatar sourceSim)
        {
            string key = walkIn.CaseIndex + ":" + walkIn.CallerName;
            if (walkInAvatar != null && key == boundWalkInKey) return;

            try
            {
                var content = Content.Get();
                if (content == null || content.AvatarSkeletons == null) return;
                var baseSkel = content.AvatarSkeletons.Get("adult.skel");
                if (baseSkel == null) return;

                var sim = new SimAvatar(baseSkel.Clone());

                // 1. Prefer real live townie/household Sim avatar from current VM (different from Desk Sergeant!)
                var vm = CareerDispatchDriver.CurrentVM();
                VMAvatar otherSim = null;
                if (vm != null && vm.Entities != null)
                {
                    var adults = vm.Entities.OfType<VMAvatar>()
                        .Where(a => a != null && !a.Dead && !a.IsPet && !ReferenceEquals(a, sourceSim) && a.Avatar != null && a.Avatar.Head != null && a.Avatar.Body != null)
                        .ToList();
                    if (adults.Count > 0)
                        otherSim = adults[walkIn.CaseIndex % adults.Count];
                }

                if (otherSim != null && walkIn.CaseIndex % 2 == 1)
                {
                    sim.Head = otherSim.Avatar.Head;
                    sim.Body = otherSim.Avatar.Body;
                    sim.Handgroup = otherSim.Avatar.Handgroup ?? otherSim.Avatar.Body;
                }
                else
                {
                    // 2. Try resolving the real TS1 Neighborhood Sim's actual outfit from Objects/Characters IFF!
                    Outfit realHead = null, realBody = null;
                    TryLoadNeighborhoodSimOutfits(content, walkIn.CallerOutfitGuid, out realHead, out realBody);
                    if (realHead != null && realBody != null)
                    {
                        sim.Head = realHead;
                        sim.Body = realBody;
                        sim.Handgroup = realBody;
                    }
                    else if (otherSim != null)
                    {
                        sim.Head = otherSim.Avatar.Head;
                        sim.Body = otherSim.Avatar.Body;
                        sim.Handgroup = otherSim.Avatar.Handgroup ?? otherSim.Avatar.Body;
                    }
                    else
                    {
                        // Guaranteed authentic textured TS1 fallback townie
                        sim.Head = new Outfit { TS1AppearanceID = "c_pizzaguy.apr", TS1TextureID = "pizzaguyface" };
                        var fb = new Outfit
                        {
                            TS1AppearanceID = "b001mafit_01.apr",
                            TS1TextureID = "pizzaguysuit",
                            LiteralHandgroup = BuildAdultHands("lgt")
                        };
                        sim.Body = fb;
                        sim.Handgroup = fb;
                    }
                }

                if (sim.Handgroup == null)
                    sim.Handgroup = new Outfit { LiteralHandgroup = BuildAdultHands("lgt") };

                if (avatarScene != null)
                {
                    if (walkInAvatar != null) avatarScene.Remove(walkInAvatar);
                    avatarScene.Add(sim);
                }
                walkInAvatar = sim;
                boundWalkInKey = key;
            }
            catch (Exception e)
            {
                Boot.Log("dispatch3d walk-in avatar error: " + e.Message);
            }
        }

        static void TryLoadNeighborhoodSimOutfits(Content content, uint guid, out Outfit head, out Outfit body)
        {
            head = null;
            body = null;
            if (content == null || guid == 0) return;
            try
            {
                var obj = content.WorldObjects?.Get(guid);
                var str = obj?.Resource?.Get<STR>(200);
                if (str == null || str.Length < 3) return;
                string bodySpec = str.GetString(1);
                string headSpec = str.GetString(2);
                string bApr, bTex, hApr, hTex;
                if (ParseSuitSpec(bodySpec, out bApr, out bTex) && ParseSuitSpec(headSpec, out hApr, out hTex))
                {
                    if (content.AvatarAppearances?.Get(bApr) != null && content.AvatarAppearances?.Get(hApr) != null)
                    {
                        head = new Outfit { TS1AppearanceID = hApr, TS1TextureID = hTex };
                        body = new Outfit { TS1AppearanceID = bApr, TS1TextureID = bTex, LiteralHandgroup = BuildAdultHands("lgt") };
                    }
                }
            }
            catch { }
        }

        static bool ParseSuitSpec(string spec, out string apr, out string tex)
        {
            apr = null;
            tex = null;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            var parts = spec.Split(',');
            if (parts.Length < 1) return false;
            string mesh = parts[0].Trim();
            string tx = parts.Length >= 2 ? parts[1].Trim() : mesh;
            if (string.IsNullOrEmpty(mesh)) return false;
            apr = mesh.EndsWith(".apr", StringComparison.OrdinalIgnoreCase) ? mesh : (mesh + ".apr");
            tex = tx;
            return true;
        }

        static HandGroup BuildAdultHands(string skin)
        {
            string tone = string.IsNullOrEmpty(skin) ? "lgt" : skin;
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

        void DisposeResources()
        {
            if (renderTarget != null) { try { renderTarget.Dispose(); } catch { } renderTarget = null; }
            if (worldEffect != null) { try { worldEffect.Dispose(); } catch { } worldEffect = null; }
            initialized = false;
            hasFrame = false;
        }

        public void Dispose()
        {
            DisposeResources();
        }
    }
}
