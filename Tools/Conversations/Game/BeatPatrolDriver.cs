using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.Client.UI.Framework;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.Vitaboy;
using Microsoft.Xna.Framework;

namespace Simitone.Conversations
{
    internal enum PatrolPhase
    {
        Inactive = 0,
        WaitingForPatrolLot = 1,
        Active = 2,
        WaitingForHomeLot = 3
    }

    internal enum OfficerMotionMode
    {
        InCruiser = 0,
        IdleOnFoot = 1,
        MovingToTarget = 2,
        PerformingAction = 3,
        EscortingToCruiser = 4
    }

    internal sealed class PatrolActorSlot
    {
        public int IncidentIndex;
        public PatrolIncidentKind Kind;
        public VMEntity PrimaryEntity;
        public VMEntity SecondaryEntity; // e.g., Boombox next to NoiseDisturbance Sim
        public LotTilePos HomeTile;
        public LotTilePos PatrolEndTile;
        public float PatrolPhase;
        public bool ArrestedOrCleared;
    }

    // Runtime coordinator for Law Enforcement Level 3 ("Patrol Officer"):
    // Real-Lot Live Beat Patrol (DISP-06).
    // Teleports the player's Officer to a real Downtown / Old Town / Neighbor Sims 1 lot via
    // Simitone's native TS1GameScreen.SwitchLot (preserving SavedLot), parks a real TS1 Police
    // Squad Car at the street curb, spawns a prowling Burglar + Noise Disturbance + Civic Hazard +
    // Innocent Citizen on verified walkable tiles, wires native Simitone Pie Menu patrol actions,
    // drives live Blender-baked stealth/arrest/citation/inspection skeletal animations directly on
    // live VMAvatars, and returns seamlessly to the player's saved home lot with rewards & rels.
    internal static class BeatPatrolDriver
    {
        public const byte PatrolPieActionBase = 0xF0;

        // Verified Sims 1 Objects.far GUIDs
        const uint GuidPoliceCruiser = 0x76ECEDA7; // CarSquad.iff
        const uint GuidBurglarNpc = 0xC7857D8A;    // People\thief.iff
        const uint GuidCarPortal = 0x865A6812;     // CarPortal.iff
        const uint GuidPedPortal = 0x81E6BEF9;     // PedPortal.iff
        const uint GuidBoombox = 0x46F4E80B;       // Stereos.iff
        const uint GuidTrashPile = 0x7F907075;     // trashpile.iff
        const uint GuidTrashOutside = 0xA4258067;  // TrashOutside.iff
        const uint GuidFallbackPerson = 0x7FD96B54;

        static PatrolPhase phase = PatrolPhase.Inactive;
        static VM sourceVm;
        static VM patrolVm;

        // Saved home officer metadata (rebound on both patrol lot and return home)
        static uint homeOfficerGuid;
        static short homeOfficerNeighborId;
        static string homeOfficerName = "Officer";
        static int homeLogicSkill;
        static int homeCharismaSkill;
        static int homeHouseNumber = 7;
        static Outfit homeOfficerHead;
        static Outfit homeOfficerBody;
        static Outfit homeOfficerHands;
        static short homeOfficerGender;
        static short homeOfficerSkinColor;
        static short homeOfficerPersonAge = 27;

        static int targetPatrolLotId = 21;
        static PatrolLotZone targetPatrolZone = PatrolLotZone.Downtown;
        static string targetSectorName = "DOWNTOWN PLAZA  •  LOT 21";
        static int patrolSeed = 42;
        static bool pendingApplyRewards = true;
        static BeatPatrolOutcome pendingReturnOutcome;

        static BeatPatrolGame game;
        static BeatPatrolOutcome finalOutcome;
        static readonly PatrolAudioEngine audio = new PatrolAudioEngine();

        // Live entities on the real patrol lot
        static VMMultitileGroup cruiserGroup;
        static VMEntity cruiserLead;
        static LotTilePos cruiserTile;
        static LotTilePos curbStandTile;
        static VMAvatar officerAvatar;
        static readonly List<PatrolActorSlot> actorSlots = new List<PatrolActorSlot>(4);

        // Motion & custom Vitaboy skeletal animation drivers
        static OfficerMotionMode officerMode = OfficerMotionMode.InCruiser;
        static readonly VitaboyClipPlayer officerClip = new VitaboyClipPlayer();
        static readonly VitaboyClipPlayer burglarClip = new VitaboyClipPlayer();
        static readonly Stopwatch frameClock = new Stopwatch();
        static readonly Stopwatch animClock = new Stopwatch();
        static double lastFrameSec = -1.0;
        static double lastAnimSec = -1.0;

        static readonly List<Vector2> movePath = new List<Vector2>();
        static int moveWaypointIdx;
        static Vector2 officerSmoothTile;
        static float officerYaw;
        static Vector2 burglarSmoothTile;
        static float burglarYaw;
        static bool burglarHasSmoothPos;
        static float footstepAccumSec;
        static bool footstepToggle;
        static bool activeMoveUsesSneak;
        static PatrolActionKind pendingAction;
        static int pendingIncidentIndex = -1;
        static float actionTimerSec;
        static float actionDurationSec;
        static bool actionMidCueFired;
        static bool actionResolved;
        static bool finishFanfarePlayed;
        static bool cameraFollowOfficer;
        static float alertSweepRemainingSec;
        static string lastActiveClipName = "none";

        // Arrest-to-cruiser escort state
        static PatrolActorSlot escortSlot;
        static VMAvatar escortBurglar;
        static Vector2 escortBurglarStartPos;
        static float escortBlendSec;
        static bool escortLoadingPhase;
        static bool escortLoadedIntoCruiser;
        static float escortLoadTimerSec;
        static Vector2 escortCruiserDoorPoint;
        static Vector2 escortBurglarDoorStart;

        public static bool IsActive { get { return phase != PatrolPhase.Inactive; } }
        public static bool IsPatrolLotLive { get { return phase == PatrolPhase.Active && patrolVm != null && game != null; } }
        public static bool IsOfficerInCruiser { get { return officerMode == OfficerMotionMode.InCruiser; } }
        public static OfficerMotionMode MotionMode { get { return officerMode; } }
        public static BeatPatrolGame Game { get { return game; } }
        public static BeatPatrolOutcome FinalOutcome { get { return finalOutcome; } }
        public static PatrolAudioEngine Audio { get { return audio; } }
        public static string LastActiveClipName { get { return lastActiveClipName; } }

        public static bool IsActiveOn(VM vm)
        {
            return phase == PatrolPhase.Active && vm != null && ReferenceEquals(vm, patrolVm);
        }

        // Begins a Real-Lot Live Beat Patrol by switching the real TS1GameScreen to a real
        // Downtown / Old Town / Neighbor lot while Simitone safely snapshots SavedLot.
        public static string BeginPatrol(VMAvatar homeSim, int? lotIdOverride = null, int? seed = null)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "err no screen";
                var vm = CareerDispatchDriver.CurrentVM();
                if (vm == null || !vm.Ready) return "err vm not ready";

                if (phase != PatrolPhase.Inactive)
                    ForceReset();

                // Close other mini-game overlays if any
                CareerDispatchToast.Close();
                SecurityWatchOverlay.Close(false);
                CadetAcademyOverlay.Close(false);

                CaptureHomeOfficer(homeSim, vm, screen);

                var availableLots = DiscoverAvailablePatrolLots();
                patrolSeed = seed ?? (Environment.TickCount & 0x7FFF);

                if (lotIdOverride.HasValue && lotIdOverride.Value > 0 && lotIdOverride.Value != homeHouseNumber)
                {
                    targetPatrolLotId = lotIdOverride.Value;
                    targetPatrolZone = BeatPatrolGame.ClassifyLotZone(targetPatrolLotId);
                    targetSectorName = ResolveSectorDisplayName(targetPatrolLotId, targetPatrolZone);
                }
                else
                {
                    targetPatrolLotId = BeatPatrolGame.PickPatrolLot(
                        homeHouseNumber, availableLots, patrolSeed, out targetPatrolZone, out targetSectorName);
                    targetSectorName = ResolveSectorDisplayName(targetPatrolLotId, targetPatrolZone);
                }

                // Make sure the chosen house file actually exists; fall back to first existing candidate
                if (!HouseFileExists(targetPatrolLotId) && availableLots.Count > 0)
                {
                    targetPatrolLotId = availableLots.FirstOrDefault(id => id != homeHouseNumber);
                    if (targetPatrolLotId <= 0) targetPatrolLotId = availableLots[0];
                    targetPatrolZone = BeatPatrolGame.ClassifyLotZone(targetPatrolLotId);
                    targetSectorName = ResolveSectorDisplayName(targetPatrolLotId, targetPatrolZone);
                }

                sourceVm = vm;
                patrolVm = null;
                pendingReturnOutcome = null;
                finalOutcome = null;
                phase = PatrolPhase.WaitingForPatrolLot;

                // Populate TS1 cross-lot GameState (matching Simitone's Hole-in-the-Ground / Downtown travel)
                // so real Downtown/Old Town lot scripts don't pop null-reference alerts on arrival.
                try
                {
                    var ngbh = Content.Get()?.Neighborhood;
                    var gs = ngbh?.GameState;
                    if (gs != null)
                    {
                        if (vm.TS1State?.CurrentFamily != null)
                            gs.ActiveFamily = vm.TS1State.CurrentFamily;
                        if (homeOfficerGuid != 0)
                            gs.DowntownSimGUID = homeOfficerGuid;
                    }
                }
                catch { }

                DismissBlockingAlerts(vm);

                if (!TriggerScreenSwitchLot(screen, targetPatrolLotId))
                {
                    phase = PatrolPhase.Inactive;
                    return "err failed to trigger TS1GameScreen.SwitchLot";
                }

                Boot.Log("beat-patrol: switching from home House" + homeHouseNumber + " -> real lot House" + targetPatrolLotId + " (" + targetSectorName + ") for " + homeOfficerName);
                return "ok switching lot=" + targetPatrolLotId + " sector=" + targetSectorName + " officer=" + homeOfficerName;
            }
            catch (Exception e)
            {
                phase = PatrolPhase.Inactive;
                Boot.Log("beat-patrol BeginPatrol error: " + e.Message);
                return "err " + e.Message;
            }
        }

        // Called every simulation tick from Boot.OnTick(vm).
        public static void OnTick(VM vm)
        {
            if (phase == PatrolPhase.Inactive || vm == null || !vm.Ready) return;

            if (phase == PatrolPhase.WaitingForPatrolLot)
            {
                if (!ReferenceEquals(vm, sourceVm) && vm.Context != null && vm.Context.World != null)
                {
                    SetupPatrolLot(vm);
                }
                return;
            }

            if (phase == PatrolPhase.WaitingForHomeLot)
            {
                if (!ReferenceEquals(vm, patrolVm) && vm.Context != null)
                {
                    CompleteReturnHome(vm);
                }
                return;
            }

            if (phase == PatrolPhase.Active)
            {
                if (!ReferenceEquals(vm, patrolVm))
                {
                    // Player manually left lot via neighborhood screen; clean up safely
                    ForceReset();
                    return;
                }

                KeepPatrolActorsControlled();
                UpdateFrame();
            }
        }

        // Runs at the end of VM.InternalTick (completely outside UIScreen.Children and vm.PreDraw
        // iteration), so SetPosition, Delete, ConvoOverlay.Show, and state-machine updates are 100% safe!
        static void UpdateFrame()
        {
            if (phase != PatrolPhase.Active || patrolVm == null || game == null) return;

            if (!frameClock.IsRunning)
            {
                frameClock.Restart();
                lastFrameSec = 0.0;
            }
            double nowSec = frameClock.Elapsed.TotalSeconds;
            float dt = lastFrameSec >= 0.0
                ? (float)Math.Max(0.001, Math.Min(0.08, nowSec - lastFrameSec))
                : (1f / 30f);
            lastFrameSec = nowSec;

            game.Tick(dt);
            if (game.IsFinished && finalOutcome == null &&
                officerMode != OfficerMotionMode.PerformingAction &&
                officerMode != OfficerMotionMode.EscortingToCruiser)
            {
                finalOutcome = game.ComputeOutcome();
                if (!finishFanfarePlayed)
                {
                    finishFanfarePlayed = true;
                    audio.Play(PatrolSfxId.ShiftCompleteFanfare, 0.50f);
                }
            }

            UpdateBurglarProwlLogic(dt);
            UpdateOfficerMotionLogic(dt);
        }

        // Called every frame during BeatPatrolOverlay.PreDraw (just before TS1GameScreen calls vm.PreDraw()).
        // STRICTLY READ-ONLY on VM/UIScreen collections: clears Animations, applies cached VisualPosition
        // & RadianDirection, and updates Vitaboy skeleton bones so vm.PreDraw() uploads them cleanly to GPU.
        public static void PreDrawFrame()
        {
            if (phase != PatrolPhase.Active || patrolVm == null || game == null) return;

            if (!animClock.IsRunning)
            {
                animClock.Restart();
                lastAnimSec = 0.0;
            }
            double nowSec = animClock.Elapsed.TotalSeconds;
            float dt = lastAnimSec >= 0.0
                ? (float)Math.Max(0.001, Math.Min(0.06, nowSec - lastAnimSec))
                : (1f / 60f);
            lastAnimSec = nowSec;

            // 1. Apply Burglar crouched sneak / arrested skeleton pose
            var burglarSlot = actorSlots.FirstOrDefault(s => s.Kind == PatrolIncidentKind.BurglarProwler);
            if (burglarSlot != null && !burglarSlot.ArrestedOrCleared)
            {
                var burglar = burglarSlot.PrimaryEntity as VMAvatar;
                if (burglar != null && !burglar.Dead && burglar.Avatar != null)
                {
                    burglar.Animations?.Clear();
                    burglar.CarryAnimationState = null;
                    burglar.VisualPositionStart = null;
                    if (burglarHasSmoothPos)
                    {
                        burglar.VisualPosition = new Vector3(burglarSmoothTile.X, burglarSmoothTile.Y, 0f);
                        burglar.RadianDirection = burglarYaw;
                    }
                    burglarClip.UpdateAndApply(burglar.Avatar, dt, useSquad: true);
                }
            }

            // 2. Apply Officer crouched sneak / apprehend / ticket / inspect skeleton pose
            if (officerAvatar != null && !officerAvatar.Dead && officerAvatar.Avatar != null &&
                officerMode != OfficerMotionMode.InCruiser)
            {
                officerAvatar.Animations?.Clear();
                officerAvatar.CarryAnimationState = null;
                officerAvatar.VisualPositionStart = null;
                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;
                officerClip.UpdateAndApply(officerAvatar.Avatar, dt, useSquad: true);
            }
        }

        // Native Simitone Pie Menu builder called from ConvoPieMenu.Filter when Beat Patrol is active.
        public static List<VMPieMenuInteraction> TryBuildPieMenu(VMEntity target, VM vm, VMEntity caller)
        {
            if (!IsActiveOn(vm) || target == null || game == null || game.IsFinished)
                return null;

            if (officerMode == OfficerMotionMode.EscortingToCruiser)
                return new List<VMPieMenuInteraction>();

            // 1. Clicked any tile of the parked Police Squad Car?
            if (IsCruiserEntity(target))
            {
                var cruiserOpts = BeatPatrolGame.GetActionsForCruiser(IsOfficerInCruiser);
                return ToPieList(cruiserOpts, target);
            }

            // 2. Clicked any spawned Patrol Incident entity (Burglar, Noise Townie, Boombox, Trash Hazard, Innocent Citizen)?
            var slot = FindSlotForEntity(target);
            if (slot != null && slot.IncidentIndex >= 0 && slot.IncidentIndex < game.Incidents.Count)
            {
                var inc = game.Incidents[slot.IncidentIndex];
                var opts = BeatPatrolGame.GetActionsForIncident(inc.Kind, inc.Resolved);
                if (opts.Count > 0)
                    return ToPieList(opts, target);
                // Already resolved incident -> show clean empty list or informative stub
                return new List<VMPieMenuInteraction>();
            }

            return null;
        }

        // Intercepted from VMNetInteractionCmd.Execute(VM, VMAvatar) via our Cecil hook!
        public static bool TryHandleInteraction(VMNetInteractionCmd cmd, VM vm, VMAvatar caller)
        {
            if (cmd == null || cmd.Interaction < PatrolPieActionBase) return false;
            if (!IsActiveOn(vm) || game == null) return false;

            var action = (PatrolActionKind)(cmd.Interaction - PatrolPieActionBase);
            var target = vm.GetObjectById(cmd.CalleeID);
            if (target == null) return true;

            ExecutePatrolActionOnEntity(target, action);
            return true;
        }

        public static string ExecutePatrolActionOnEntity(VMEntity target, PatrolActionKind action)
        {
            if (!IsPatrolLotLive) return "err patrol not live";

            if (game.IsBriefing)
                BeginActivePatrolFromBriefing();

            if (action == PatrolActionKind.EndShift)
            {
                game.FinishShiftEarly();
                finalOutcome = game.ComputeOutcome();
                if (!finishFanfarePlayed)
                {
                    finishFanfarePlayed = true;
                    audio.Play(PatrolSfxId.ShiftCompleteFanfare, 0.48f);
                }
                return "ok shift ended grade=" + finalOutcome.Grade;
            }

            if (officerMode == OfficerMotionMode.EscortingToCruiser)
                return "err escorting suspect to cruiser";

            if (action == PatrolActionKind.ReturnToCruiser)
            {
                if (officerMode == OfficerMotionMode.InCruiser)
                    return "ok already in cruiser";
                BeginOfficerMove(
                    destTile: curbStandTile,
                    incidentIdx: -1,
                    action: PatrolActionKind.ReturnToCruiser,
                    useSneak: false);
                return "ok returning to cruiser";
            }

            var slot = FindSlotForEntity(target);
            if (slot == null) return "err target not a patrol incident";
            if (slot.IncidentIndex < 0 || slot.IncidentIndex >= game.Incidents.Count) return "err bad incident";
            var inc = game.Incidents[slot.IncidentIndex];
            if (inc.Resolved) return "ok already resolved";

            bool useSneak = (action == PatrolActionKind.SneakApprehend);
            var dest = PickApproachTile(slot, preferBehind: useSneak);

            if (useSneak)
                audio.Play(PatrolSfxId.BurglarSpotSting, 0.42f);

            BeginOfficerMove(dest, slot.IncidentIndex, action, useSneak);
            return "ok action=" + action + " target=" + inc.Kind + " sneak=" + useSneak;
        }

        public static void BeginActivePatrolFromBriefing()
        {
            if (game == null || !game.IsBriefing) return;
            game.BeginPatrol();
            audio.Play(PatrolSfxId.RadioSquelch, 0.44f);
            // Pull camera slightly back to Medium/Near showing the street & front plaza clearly
            FocusCameraOnTile(curbStandTile, WorldZoom.Near);
        }

        public static string RequestReturnHome(bool applyRewards)
        {
            if (phase == PatrolPhase.Inactive) return "not active";
            try
            {
                pendingApplyRewards = applyRewards;
                if (game != null && finalOutcome == null)
                    finalOutcome = game.ComputeOutcome();
                pendingReturnOutcome = finalOutcome;

                audio.Play(PatrolSfxId.ReturnHomeChime, 0.46f);
                audio.EndSession();
                BeatPatrolOverlay.Close();

                var screen = UIScreen.Current;
                if (screen != null && homeHouseNumber > 0)
                {
                    phase = PatrolPhase.WaitingForHomeLot;
                    if (TriggerScreenSwitchLot(screen, homeHouseNumber))
                    {
                        Boot.Log("beat-patrol: returning home to House" + homeHouseNumber + " (applyRewards=" + applyRewards + ")");
                        return "ok returning home house=" + homeHouseNumber;
                    }
                }
            }
            catch (Exception e)
            {
                Boot.Log("beat-patrol return home error: " + e.Message);
            }

            ForceReset();
            return "ok closed";
        }

        public static string StatusSummary()
        {
            if (phase == PatrolPhase.Inactive) return "inactive";
            if (phase == PatrolPhase.WaitingForPatrolLot) return "switching_to_lot=" + targetPatrolLotId;
            if (phase == PatrolPhase.WaitingForHomeLot) return "returning_home=" + homeHouseNumber;
            if (game == null) return "active_no_game";

            return "lot=" + targetPatrolLotId +
                   " zone=" + targetPatrolZone +
                   " briefing=" + game.IsBriefing +
                   " finished=" + game.IsFinished +
                   " mode=" + officerMode +
                   " clip=" + lastActiveClipName +
                   " calls=" + game.ResolvedRequiredCount + "/" + game.RequiredCount +
                   " score=" + game.Score +
                   " audioCues=" + audio.SessionEventCount;
        }

        // Scripted test/demo helper called via `simtool convo dispatch action <cmd>`
        public static string HandleScriptedAction(string cmd, string arg2 = null)
        {
            if (!IsPatrolLotLive) return "err patrol not live (" + phase + ")";
            var c = (cmd ?? "").Trim().ToLowerInvariant();
            switch (c)
            {
                case "start":
                case "begin":
                    BeginActivePatrolFromBriefing();
                    return "ok briefing=false";
                case "sneak_burglar":
                case "sneak":
                    return ExecuteByKind(PatrolIncidentKind.BurglarProwler, PatrolActionKind.SneakApprehend);
                case "confront_burglar":
                    return ExecuteByKind(PatrolIncidentKind.BurglarProwler, PatrolActionKind.ConfrontLoud);
                case "warn_noise":
                case "warn":
                    return ExecuteByKind(PatrolIncidentKind.NoiseDisturbance, PatrolActionKind.WarnCitizen);
                case "ticket_noise":
                case "ticket":
                    return ExecuteByKind(PatrolIncidentKind.NoiseDisturbance, PatrolActionKind.IssueTicket);
                case "inspect_hazard":
                case "inspect":
                    return ExecuteByKind(PatrolIncidentKind.CivicHazard, PatrolActionKind.InspectHazard);
                case "checkin":
                    return ExecuteByKind(PatrolIncidentKind.InnocentCitizen, PatrolActionKind.CheckInCitizen);
                case "cruiser_return":
                    return ExecutePatrolActionOnEntity(cruiserLead, PatrolActionKind.ReturnToCruiser);
                case "end":
                case "finish":
                    return ExecutePatrolActionOnEntity(cruiserLead, PatrolActionKind.EndShift);
                case "claim":
                case "return_home":
                    return RequestReturnHome(true);
                case "skip":
                case "close":
                    return RequestReturnHome(false);
                case "focus_cruiser":
                    cameraFollowOfficer = false;
                    FocusCameraOnTile(cruiserTile, WorldZoom.Near, zoom3D: 2.1f);
                    return "ok focus=cruiser";
                case "focus_burglar":
                {
                    cameraFollowOfficer = false;
                    var b = actorSlots.FirstOrDefault(s => s.Kind == PatrolIncidentKind.BurglarProwler);
                    if (b != null && b.PrimaryEntity != null)
                    {
                        FocusCameraOnTile(b.PrimaryEntity.Position, WorldZoom.Near, zoom3D: 1.85f);
                        return "ok focus=burglar";
                    }
                    return "err no burglar";
                }
                case "focus_officer":
                    cameraFollowOfficer = true;
                    if (officerAvatar != null && !IsOfficerInCruiser)
                        FocusCameraOnTile(officerAvatar.Position, WorldZoom.Near, zoom3D: 1.85f);
                    return "ok focus=officer";
                case "zoom_near":
                    SetCameraZoom(WorldZoom.Near);
                    return "ok zoom=near";
                case "zoom_med":
                    SetCameraZoom(WorldZoom.Medium);
                    return "ok zoom=medium";
                default:
                    return "err unknown patrol action: " + c;
            }
        }

        public static string ExportLastSessionWav(string path, float leadSec = 0f, float tailSec = 1.0f)
        {
            return audio.ExportSessionWav(path, leadSec, tailSec);
        }

        static string ExecuteByKind(PatrolIncidentKind kind, PatrolActionKind action)
        {
            var slot = actorSlots.FirstOrDefault(s => s.Kind == kind && s.PrimaryEntity != null);
            if (slot == null) return "err no entity for " + kind;
            return ExecutePatrolActionOnEntity(slot.PrimaryEntity, action);
        }

        // ---- Real Patrol Lot Setup ----

        static void SetupPatrolLot(VM vm)
        {
            patrolVm = vm;
            actorSlots.Clear();
            cruiserGroup = null;
            cruiserLead = null;
            officerAvatar = null;
            officerMode = OfficerMotionMode.InCruiser;
            movePath.Clear();
            moveWaypointIdx = 0;
            pendingIncidentIndex = -1;
            actionTimerSec = 0f;
            actionResolved = false;
            finishFanfarePlayed = false;
            cameraFollowOfficer = false;
            burglarHasSmoothPos = false;
            officerClip.Reset();
            burglarClip.Reset();
            frameClock.Restart();
            animClock.Restart();
            lastFrameSec = 0.0;
            lastAnimSec = 0.0;

            try
            {
                // 1. Set warm golden-hour early-evening light & normal simulation speed so the real lot,
                // Police Squad Car, uniform, striped Burglar, and hazards are all vividly lit & readable.
                if (vm.Context.Clock != null)
                {
                    vm.Context.Clock.Hours = 17;
                    vm.Context.Clock.Minutes = 15;
                }
                vm.SpeedMultiplier = 1;
                alertSweepRemainingSec = 4.5f;
                ConvoOverlay.Clear();
                DismissBlockingAlerts(vm);

                // 2. Find street curb & spawn the real TS1 Police Squad Car parked cleanly at the curb
                Direction cruiserDir;
                FindStreetCurbPlacement(vm, out cruiserTile, out curbStandTile, out cruiserDir);
                cruiserGroup = TrySpawnObjectNear(vm, GuidPoliceCruiser, cruiserTile, cruiserDir, 4);
                if (cruiserGroup != null && cruiserGroup.Objects != null && cruiserGroup.Objects.Count > 0)
                {
                    cruiserLead = cruiserGroup.BaseObject ?? cruiserGroup.Objects[0];
                    cruiserTile = cruiserLead.Position;
                    FreezeGroup(cruiserGroup, pauseThread: true);
                    // Re-verify a walkable sidewalk tile right beside the parked cruiser
                    curbStandTile = FindNearestWalkableTile(vm, cruiserTile, minDist: 2, maxDist: 6, avoidTiles: null);
                }

                // 3. Spawn & dress the player's Patrol Officer (authentic Level 3 Police Uniform)
                officerAvatar = SpawnAndDressOfficer(vm, curbStandTile);
                if (officerAvatar != null)
                {
                    // Start INSIDE the parked Police Squad Car while player surveys the real lot!
                    FreezeEntity(officerAvatar, pauseThread: true);
                    officerSmoothTile = new Vector2(curbStandTile.TileX + 0.5f, curbStandTile.TileY + 0.5f);
                    officerYaw = (float)Math.PI;
                    officerAvatar.SetPosition(LotTilePos.OUT_OF_WORLD, Direction.SOUTH, vm.Context);
                    officerAvatar.Animations?.Clear();
                    officerMode = OfficerMotionMode.InCruiser;
                    SelectActiveAvatar(officerAvatar);
                }

                // 4. Pick real Neighborhood townies for Noise Disturbance & Innocent Citizen
                var townies = PickDistinctTownieNeighbors(2);
                string disturbName = townies.Count > 0 ? townies[0].Item2 : "Local Resident";
                short disturbNid = townies.Count > 0 ? townies[0].Item1.NeighbourID : (short)0;
                uint disturbGuid = townies.Count > 0 ? townies[0].Item1.GUID : GuidFallbackPerson;

                string innocentName = townies.Count > 1 ? townies[1].Item2 : "Evening Stroller";
                short innocentNid = townies.Count > 1 ? townies[1].Item1.NeighbourID : (short)0;
                uint innocentGuid = townies.Count > 1 ? townies[1].Item1.GUID : GuidFallbackPerson;

                // 5. Create pure-C# BeatPatrolGame state
                game = new BeatPatrolGame(
                    officerName: homeOfficerName,
                    logicSkillLevel: homeLogicSkill,
                    charismaSkillLevel: homeCharismaSkill,
                    patrolLotId: targetPatrolLotId,
                    lotZone: targetPatrolZone,
                    sectorDisplayName: targetSectorName,
                    disturbanceCitizenName: disturbName,
                    disturbanceNeighborId: disturbNid,
                    innocentCitizenName: innocentName,
                    innocentNeighborId: innocentNid,
                    seed: patrolSeed,
                    startInBriefing: true);

                // 6. Pick 4 well-spaced walkable plaza/yard tiles visible near the front of the real lot
                var spawnTiles = PickIncidentSpawnTiles(vm, curbStandTile, 4);

                // Slot 0: Real TS1 Burglar NPC (striped shirt & mask) prowling in crouched sneak pose!
                // Pause its VMThread immediately so thief.iff's autonomous "steal object" / VMRoutingFrame
                // Main tree never runs in the background while VitaboyClipPlayer drives its skeleton.
                var burglarTile = spawnTiles[0];
                var burglarEnd = FindProwlEndTile(vm, burglarTile);
                var burglarEnt = SpawnAvatarAt(vm, GuidBurglarNpc, burglarTile, Direction.SOUTH);
                if (burglarEnt != null)
                {
                    FreezeEntity(burglarEnt, pauseThread: true);
                    burglarSmoothTile = new Vector2(burglarTile.TileX + 0.5f, burglarTile.TileY + 0.5f);
                    burglarYaw = 0f;
                    burglarHasSmoothPos = true;
                    burglarClip.Play("patrol_sneak_loop", 0.0f, 0.72f, true, true);
                }
                actorSlots.Add(new PatrolActorSlot
                {
                    IncidentIndex = 0,
                    Kind = PatrolIncidentKind.BurglarProwler,
                    PrimaryEntity = burglarEnt,
                    HomeTile = burglarTile,
                    PatrolEndTile = burglarEnd,
                });

                // Slot 1: Real Neighborhood Sim + Boombox (Noise Disturbance)
                var noiseTile = spawnTiles[1];
                var noiseEnt = SpawnAvatarAt(vm, disturbGuid, noiseTile, Direction.WEST);
                if (noiseEnt != null)
                {
                    if (!string.IsNullOrWhiteSpace(disturbName)) noiseEnt.Name = disturbName;
                    FreezeEntity(noiseEnt, pauseThread: false);
                }
                var boomTile = FindNearestWalkableTile(vm, noiseTile, minDist: 1, maxDist: 2, avoidTiles: new[] { noiseTile });
                var boomGroup = TrySpawnObjectNear(vm, GuidBoombox, boomTile, Direction.SOUTH, 2);
                VMEntity boomEnt = boomGroup != null ? (boomGroup.BaseObject ?? boomGroup.Objects.FirstOrDefault()) : null;
                if (boomGroup != null) FreezeGroup(boomGroup, pauseThread: true);
                actorSlots.Add(new PatrolActorSlot
                {
                    IncidentIndex = 1,
                    Kind = PatrolIncidentKind.NoiseDisturbance,
                    PrimaryEntity = noiseEnt,
                    SecondaryEntity = boomEnt,
                    HomeTile = noiseTile,
                });

                // Slot 2: Real TS1 Trash Pile / Litter Hazard (Civic Hazard)
                var hazardTile = spawnTiles[2];
                var hazardGroup = TrySpawnObjectNear(vm, GuidTrashPile, hazardTile, Direction.SOUTH, 2)
                                  ?? TrySpawnObjectNear(vm, GuidTrashOutside, hazardTile, Direction.SOUTH, 2);
                VMEntity hazardEnt = hazardGroup != null ? (hazardGroup.BaseObject ?? hazardGroup.Objects.FirstOrDefault()) : null;
                if (hazardGroup != null) FreezeGroup(hazardGroup, pauseThread: true);
                actorSlots.Add(new PatrolActorSlot
                {
                    IncidentIndex = 2,
                    Kind = PatrolIncidentKind.CivicHazard,
                    PrimaryEntity = hazardEnt,
                    HomeTile = hazardTile,
                });

                // Slot 3: Real Neighborhood Bystander (Innocent Citizen)
                var innocentTile = spawnTiles[3];
                var innocentEnt = SpawnAvatarAt(vm, innocentGuid, innocentTile, Direction.EAST);
                if (innocentEnt != null)
                {
                    if (!string.IsNullOrWhiteSpace(innocentName)) innocentEnt.Name = innocentName;
                    FreezeEntity(innocentEnt, pauseThread: false);
                }
                actorSlots.Add(new PatrolActorSlot
                {
                    IncidentIndex = 3,
                    Kind = PatrolIncidentKind.InnocentCitizen,
                    PrimaryEntity = innocentEnt,
                    HomeTile = innocentTile,
                });

                // 7. Frame camera crisply on the parked Police Squad Car + front plaza of the real lot
                var focusBriefTile = new LotTilePos(
                    (short)(((cruiserTile.TileX + curbStandTile.TileX) / 2) * 16),
                    (short)(((cruiserTile.TileY + curbStandTile.TileY + 2) / 2) * 16),
                    1);
                FocusCameraOnTile(focusBriefTile, WorldZoom.Near, zoom3D: 2.35f);

                // 8. Start live Beat Patrol HUD & City Dusk SFX
                phase = PatrolPhase.Active;
                audio.StartSession();
                audio.StartAmbience(0.22f);
                audio.Play(PatrolSfxId.CruiserArriveSiren, 0.46f);
                BeatPatrolOverlay.Open();
                DismissBlockingAlerts(vm);

                Boot.Log("beat-patrol: lot ready! lot=" + targetPatrolLotId +
                         " cruiser=" + (cruiserLead != null ? cruiserLead.Position.ToString() : "none") +
                         " officer=" + (officerAvatar != null ? officerAvatar.Name : "none") +
                         " burglar=" + (burglarEnt != null ? burglarEnt.Position.ToString() : "none") +
                         " noise=" + (noiseEnt != null ? noiseEnt.Name + "@" + noiseEnt.Position : "none") +
                         " hazard=" + (hazardEnt != null ? hazardEnt.Position.ToString() : "none"));
            }
            catch (Exception e)
            {
                Boot.Log("beat-patrol setup error: " + e.GetType().Name + ": " + e.Message);
                ForceReset();
            }
        }

        static void CompleteReturnHome(VM homeVm)
        {
            try
            {
                DismissBlockingAlerts(homeVm);
                VMAvatar homeOfficer = homeVm.Entities.OfType<VMAvatar>().FirstOrDefault(a =>
                    a != null && !a.Dead && !a.IsPet &&
                    ((homeOfficerGuid != 0 && a.Object?.OBJ?.GUID == homeOfficerGuid) ||
                     string.Equals(a.Name, homeOfficerName, StringComparison.OrdinalIgnoreCase)));

                if (pendingApplyRewards && pendingReturnOutcome != null)
                {
                    string rewardSummary = CareerDispatchDriver.ApplyReward(homeOfficer, pendingReturnOutcome.Reward);

                    // Record Beat Patrol commendation in Sim Memory
                    if (!string.IsNullOrWhiteSpace(pendingReturnOutcome.Reward?.MemoryFact) && homeOfficer != null)
                    {
                        try
                        {
                            // Store relationship deltas on real TS1 Neighborhood table + SocialMemory
                            var nb = Content.Get()?.Neighborhood;
                            var officerNb = homeOfficerNeighborId > 0 ? nb?.GetNeighborByID(homeOfficerNeighborId) : null;
                            foreach (var rel in pendingReturnOutcome.RelationshipDeltas)
                            {
                                if (officerNb != null && officerNb.Relationships != null && rel.NeighborId > 0)
                                {
                                    List<short> v;
                                    if (!officerNb.Relationships.TryGetValue(rel.NeighborId, out v) || v == null)
                                    {
                                        v = new List<short> { 0, 0, 0, 0 };
                                        officerNb.Relationships[rel.NeighborId] = v;
                                    }
                                    while (v.Count < 4) v.Add(0);
                                    v[0] = (short)Math.Max(-100, Math.Min(100, v[0] + rel.DailyDelta));
                                    if (v[0] >= 70) v[1] = 1;
                                }
                                if (!string.IsNullOrWhiteSpace(rel.SimName))
                                {
                                    var sp = new RawSim { Name = homeOfficerName, NeighborId = homeOfficerNeighborId };
                                    var ls = new RawSim { Name = rel.SimName, NeighborId = rel.NeighborId };
                                    Boot.Memory.Record(
                                        sp,
                                        ls,
                                        "beat_patrol",
                                        rel.DailyDelta >= 0 ? Tone.Gentle : Tone.Bold,
                                        "beat_patrol",
                                        rel.DailyDelta >= 0 ? 3 : 1,
                                        subjectTitle: rel.MemoryFact);
                                }
                            }
                            Boot.SaveMemoryAsync();
                        }
                        catch (Exception re) { Boot.Log("beat-patrol rel apply error: " + re.Message); }
                    }

                    Boot.Log("beat-patrol: returned home! grade=" + pendingReturnOutcome.Grade +
                             " rewards=[" + rewardSummary + "] rels=" + pendingReturnOutcome.RelationshipDeltas.Count);
                }
                else
                {
                    Boot.Log("beat-patrol: returned home cleanly (no rewards applied).");
                }
            }
            catch (Exception e)
            {
                Boot.Log("beat-patrol return completion error: " + e.Message);
            }
            finally
            {
                ForceReset();
            }
        }

        // ---- Live Movement, Sneak Animation, & Incident Resolution ----

        static void BeginOfficerMove(LotTilePos destTile, int incidentIdx, PatrolActionKind action, bool useSneak)
        {
            if (officerAvatar == null || patrolVm == null) return;

            if (officerMode == OfficerMotionMode.InCruiser)
            {
                audio.Play(PatrolSfxId.CarDoorOpen, 0.44f);
                officerAvatar.SetPosition(curbStandTile, Direction.SOUTH, patrolVm.Context, VMPlaceRequestFlags.AcceptSlots);
                officerSmoothTile = new Vector2(curbStandTile.TileX + 0.5f, curbStandTile.TileY + 0.5f);
                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                audio.Play(PatrolSfxId.CarDoorClose, 0.36f);
            }

            var startTile = new LotTilePos(
                (short)(Math.Round(officerSmoothTile.X - 0.5f) * 16),
                (short)(Math.Round(officerSmoothTile.Y - 0.5f) * 16),
                1);

            movePath.Clear();
            BuildWalkablePath(patrolVm, startTile, destTile, movePath);
            moveWaypointIdx = 0;

            pendingIncidentIndex = incidentIdx;
            pendingAction = action;
            activeMoveUsesSneak = useSneak;
            footstepAccumSec = 0f;
            officerMode = OfficerMotionMode.MovingToTarget;
            cameraFollowOfficer = true;
            FocusCameraOnTile(startTile, WorldZoom.Near, zoom3D: useSneak ? 2.75f : 3.30f);

            string moveClip = useSneak ? "patrol_sneak_loop" : "cadet_sprint_loop";
            float moveAnimSpeed = useSneak ? 1.05f : 0.78f;
            officerClip.Play(moveClip, 0.14f, moveAnimSpeed, true, true);
            lastActiveClipName = moveClip;
        }

        static void UpdateBurglarProwlLogic(float dt)
        {
            var slot = actorSlots.FirstOrDefault(s => s.Kind == PatrolIncidentKind.BurglarProwler);
            if (slot == null || slot.ArrestedOrCleared) return;
            var burglar = slot.PrimaryEntity as VMAvatar;
            if (burglar == null || burglar.Dead || burglar.Avatar == null) return;

            // While being escorted to the Police Squad Car, UpdateEscortToCruiser drives position & cuffed walk
            if (officerMode == OfficerMotionMode.EscortingToCruiser && ReferenceEquals(escortSlot, slot))
                return;

            // Pause prowling while the Officer is right next to the Burglar performing the arrest
            bool beingArrested = (officerMode == OfficerMotionMode.PerformingAction && pendingIncidentIndex == slot.IncidentIndex);

            if (!beingArrested)
            {
                slot.PatrolPhase += dt * 0.48f;
                float pingPong = 0.5f - 0.5f * (float)Math.Cos(slot.PatrolPhase * Math.PI);
                float x0 = slot.HomeTile.TileX + 0.5f;
                float y0 = slot.HomeTile.TileY + 0.5f;
                float x1 = slot.PatrolEndTile.TileX + 0.5f;
                float y1 = slot.PatrolEndTile.TileY + 0.5f;

                float px = x0 + (x1 - x0) * pingPong;
                float py = y0 + (y1 - y0) * pingPong;
                bool goingForward = Math.Sin(slot.PatrolPhase * Math.PI) >= 0.0;
                float dx = goingForward ? (x1 - x0) : (x0 - x1);
                float dy = goingForward ? (y1 - y0) : (y0 - y1);

                if (dx * dx + dy * dy > 0.001f)
                    burglarYaw = (float)Math.Atan2(dx, -dy);

                burglarSmoothTile = new Vector2(px, py);
                burglarHasSmoothPos = true;
                burglar.VisualPosition = new Vector3(px, py, 0f);
                burglar.RadianDirection = burglarYaw;
                burglarClip.Play("patrol_sneak_loop", 0.15f, 0.72f, true, false);
            }
            else
            {
                // Turn toward the arresting officer; once cuffs click mid-clip, lock hands behind back!
                if (officerAvatar != null)
                {
                    float dx = officerSmoothTile.X - burglarSmoothTile.X;
                    float dy = officerSmoothTile.Y - burglarSmoothTile.Y;
                    if (dx * dx + dy * dy > 0.001f)
                        burglarYaw = (float)Math.Atan2(dx, -dy);
                }
                burglar.VisualPosition = new Vector3(burglarSmoothTile.X, burglarSmoothTile.Y, 0f);
                burglar.RadianDirection = burglarYaw;
                if (actionMidCueFired && ShouldEscortToCruiser(slot.Kind, pendingAction))
                    burglarClip.Play("patrol_cuffed_walk_loop", 0.16f, 0.06f, true, false);
                else
                    burglarClip.Play("cadet_idle_attention", 0.14f, 0.9f, true, false);
            }
        }

        static void UpdateOfficerMotionLogic(float dt)
        {
            if (officerAvatar == null || officerAvatar.Avatar == null) return;
            if (officerMode == OfficerMotionMode.InCruiser) return;

            if (officerMode == OfficerMotionMode.MovingToTarget)
            {
                // If sneaking up on a moving Burglar, continuously update final waypoint to stay right behind them!
                if (pendingIncidentIndex == 0 && actorSlots.Count > 0 && actorSlots[0].PrimaryEntity != null && movePath.Count > 0)
                {
                    Vector2 bPos = burglarHasSmoothPos
                        ? burglarSmoothTile
                        : new Vector2(actorSlots[0].PrimaryEntity.VisualPosition.X, actorSlots[0].PrimaryEntity.VisualPosition.Y);
                    float bYaw = burglarHasSmoothPos ? burglarYaw : actorSlots[0].PrimaryEntity.RadianDirection;
                    float behindX = bPos.X - (float)Math.Sin(bYaw) * 1.15f;
                    float behindY = bPos.Y + (float)Math.Cos(bYaw) * 1.15f;
                    movePath[movePath.Count - 1] = new Vector2(behindX, behindY);
                }

                float moveSpeed = activeMoveUsesSneak ? 2.25f : 3.65f;
                float step = moveSpeed * dt;

                while (step > 0f && moveWaypointIdx < movePath.Count)
                {
                    Vector2 targetPt = movePath[moveWaypointIdx];
                    Vector2 delta = targetPt - officerSmoothTile;
                    float dist = delta.Length();
                    if (dist <= step || dist < 0.05f)
                    {
                        officerSmoothTile = targetPt;
                        step -= dist;
                        moveWaypointIdx++;
                    }
                    else
                    {
                        Vector2 dir = delta / dist;
                        officerSmoothTile += dir * step;
                        officerYaw = (float)Math.Atan2(dir.X, -dir.Y);
                        step = 0f;
                    }
                }

                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;

                // Stride-synced footsteps (muffled sneak steps vs brisk patrol boots)
                footstepAccumSec += dt;
                float stepInterval = activeMoveUsesSneak ? 0.36f : 0.28f;
                if (footstepAccumSec >= stepInterval)
                {
                    footstepAccumSec -= stepInterval;
                    footstepToggle = !footstepToggle;
                    if (activeMoveUsesSneak)
                        audio.Play(footstepToggle ? PatrolSfxId.SneakStepA : PatrolSfxId.SneakStepB, 0.34f);
                    else
                        audio.Play(footstepToggle ? PatrolSfxId.PavementStepA : PatrolSfxId.PavementStepB, 0.30f);
                }

                if (cameraFollowOfficer)
                    SmoothCameraToward(officerSmoothTile, dt * 3.8f);

                // Arrived at target?
                if (moveWaypointIdx >= movePath.Count)
                {
                    // Update logical tile so Simitone lighting/visibility matches visual position
                    var arrivedTile = new LotTilePos(
                        (short)(Math.Max(1, Math.Round(officerSmoothTile.X - 0.5f)) * 16),
                        (short)(Math.Max(1, Math.Round(officerSmoothTile.Y - 0.5f)) * 16),
                        1);
                    try { officerAvatar.SetPosition(arrivedTile, Direction.SOUTH, patrolVm.Context, VMPlaceRequestFlags.AcceptSlots); } catch { }
                    officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);

                    BeginPerformingAction();
                }
            }
            else if (officerMode == OfficerMotionMode.PerformingAction)
            {
                actionTimerSec += dt;
                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;

                if (cameraFollowOfficer)
                    SmoothCameraToward(officerSmoothTile, dt * 3.2f);

                if (!actionMidCueFired && actionTimerSec >= 0.38f)
                {
                    actionMidCueFired = true;
                    ResolvePendingActionMidClip();
                }

                if (actionTimerSec >= actionDurationSec)
                {
                    FinishPerformingAction();
                }
            }
            else if (officerMode == OfficerMotionMode.EscortingToCruiser)
            {
                UpdateEscortToCruiser(dt);
            }
            else // IdleOnFoot
            {
                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;
                officerClip.Play("cadet_idle_attention", 0.22f, 0.9f, true, false);
                lastActiveClipName = "cadet_idle_attention";
            }
        }

        internal static bool ShouldEscortToCruiser(PatrolIncidentKind kind, PatrolActionKind action)
        {
            return BeatPatrolGame.ShouldEscortToCruiser(kind, action);
        }

        internal static void SamplePointAheadAlongPath(
            Vector2 currentPos,
            IList<Vector2> path,
            int nextWaypointIdx,
            float aheadDist,
            Vector2 fallbackDirTarget,
            out Vector2 outPos,
            out float outYaw)
        {
            outPos = currentPos;
            Vector2 lastDir = fallbackDirTarget - currentPos;
            float rem = Math.Max(0f, aheadDist);

            if (path != null && path.Count > 0)
            {
                int idx = Math.Max(0, Math.Min(nextWaypointIdx, path.Count));
                Vector2 p = currentPos;
                while (idx < path.Count && rem > 1e-4f)
                {
                    Vector2 seg = path[idx] - p;
                    float len = seg.Length();
                    if (len > 1e-4f)
                    {
                        lastDir = seg / len;
                        if (len >= rem)
                        {
                            p += lastDir * rem;
                            rem = 0f;
                            break;
                        }
                        p = path[idx];
                        rem -= len;
                    }
                    idx++;
                }
                if (rem > 1e-4f && idx >= path.Count)
                {
                    p = path[path.Count - 1];
                    Vector2 toCar = fallbackDirTarget - p;
                    if (toCar.LengthSquared() > 1e-4f)
                        lastDir = Vector2.Normalize(toCar);
                }
                outPos = p;
            }

            if (lastDir.LengthSquared() > 1e-4f)
                outYaw = (float)Math.Atan2(lastDir.X, -lastDir.Y);
            else
                outYaw = 0f;
        }

        static void BeginEscortToCruiser(PatrolActorSlot slot, VMAvatar burglar)
        {
            escortSlot = slot;
            escortBurglar = burglar;
            escortBurglarStartPos = burglarHasSmoothPos
                ? burglarSmoothTile
                : new Vector2(burglar.VisualPosition.X, burglar.VisualPosition.Y);
            escortBlendSec = 0f;
            escortLoadingPhase = false;
            escortLoadedIntoCruiser = false;
            escortLoadTimerSec = 0f;

            var curbPt = new Vector2(curbStandTile.TileX + 0.5f, curbStandTile.TileY + 0.5f);
            var cruiserPt = cruiserTile != LotTilePos.OUT_OF_WORLD
                ? new Vector2(cruiserTile.TileX + 0.5f, cruiserTile.TileY + 0.5f)
                : curbPt;
            escortCruiserDoorPoint = Vector2.Lerp(curbPt, cruiserPt, 0.55f);

            var startTile = new LotTilePos(
                (short)(Math.Max(1, Math.Round(officerSmoothTile.X - 0.5f)) * 16),
                (short)(Math.Max(1, Math.Round(officerSmoothTile.Y - 0.5f)) * 16),
                1);

            movePath.Clear();
            BuildWalkablePath(patrolVm, startTile, curbStandTile, movePath);
            moveWaypointIdx = 0;

            pendingIncidentIndex = -1;
            footstepAccumSec = 0f;
            officerMode = OfficerMotionMode.EscortingToCruiser;
            cameraFollowOfficer = true;

            officerClip.Play("patrol_escort_walk_loop", 0.16f, 1.05f, true, true);
            lastActiveClipName = "patrol_escort_walk_loop";
            burglarClip.Play("patrol_cuffed_walk_loop", 0.16f, 1.02f, true, true);
            Boot.Log("beat-patrol: escorting handcuffed Burglar to Police Squad Car at (" + curbStandTile.TileX + "," + curbStandTile.TileY + ")");
        }

        static void UpdateEscortToCruiser(float dt)
        {
            if (escortSlot == null || patrolVm == null || (!escortLoadedIntoCruiser && (escortBurglar == null || escortBurglar.Dead)))
            {
                FinishEscortToCruiser();
                return;
            }

            if (!escortLoadingPhase)
            {
                const float escortSpeed = 2.65f;
                float step = escortSpeed * dt;

                while (step > 0f && moveWaypointIdx < movePath.Count)
                {
                    Vector2 targetPt = movePath[moveWaypointIdx];
                    Vector2 delta = targetPt - officerSmoothTile;
                    float dist = delta.Length();
                    if (dist <= step || dist < 0.05f)
                    {
                        officerSmoothTile = targetPt;
                        step -= dist;
                        moveWaypointIdx++;
                    }
                    else
                    {
                        Vector2 dir = delta / dist;
                        officerSmoothTile += dir * step;
                        officerYaw = (float)Math.Atan2(dir.X, -dir.Y);
                        step = 0f;
                    }
                }

                // Keep the handcuffed Burglar walking ~0.92 tiles directly ahead of the Officer along the exact path!
                Vector2 targetBurglarPos;
                float targetBurglarYaw;
                SamplePointAheadAlongPath(
                    officerSmoothTile,
                    movePath,
                    moveWaypointIdx,
                    0.92f,
                    escortCruiserDoorPoint,
                    out targetBurglarPos,
                    out targetBurglarYaw);

                escortBlendSec += dt;
                float blend = Math.Max(0f, Math.Min(1f, escortBlendSec / 0.35f));
                burglarSmoothTile = Vector2.Lerp(escortBurglarStartPos, targetBurglarPos, blend);
                burglarYaw = targetBurglarYaw;
                burglarHasSmoothPos = true;

                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;
                escortBurglar.VisualPosition = new Vector3(burglarSmoothTile.X, burglarSmoothTile.Y, 0f);
                escortBurglar.RadianDirection = burglarYaw;

                // Stride-synced escort footsteps
                footstepAccumSec += dt;
                if (footstepAccumSec >= 0.31f)
                {
                    footstepAccumSec -= 0.31f;
                    footstepToggle = !footstepToggle;
                    audio.Play(footstepToggle ? PatrolSfxId.PavementStepA : PatrolSfxId.PavementStepB, 0.30f);
                }

                if (cameraFollowOfficer)
                    SmoothCameraToward(Vector2.Lerp(officerSmoothTile, burglarSmoothTile, 0.5f), dt * 3.8f);

                // Reached the curb right beside the parked Police Squad Car?
                if (moveWaypointIdx >= movePath.Count)
                {
                    try { officerAvatar.SetPosition(curbStandTile, Direction.SOUTH, patrolVm.Context, VMPlaceRequestFlags.AcceptSlots); } catch { }
                    officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);

                    Vector2 toCar = escortCruiserDoorPoint - officerSmoothTile;
                    if (toCar.LengthSquared() > 1e-4f)
                        officerYaw = (float)Math.Atan2(toCar.X, -toCar.Y);

                    escortLoadingPhase = true;
                    escortLoadedIntoCruiser = false;
                    escortLoadTimerSec = 0f;
                    escortBurglarDoorStart = burglarSmoothTile;

                    audio.Play(PatrolSfxId.CarDoorOpen, 0.48f);
                    officerClip.Play("cadet_idle_attention", 0.16f, 0.9f, true, false);
                    lastActiveClipName = "cadet_idle_attention";

                    try
                    {
                        ConvoOverlay.Show(
                            patrolVm,
                            officerAvatar,
                            "Officer " + homeOfficerName,
                            "Into the back seat — precinct booking awaits!",
                            null,
                            null,
                            "");
                    }
                    catch { }
                }
            }
            else
            {
                escortLoadTimerSec += dt;
                officerAvatar.VisualPosition = new Vector3(officerSmoothTile.X, officerSmoothTile.Y, 0f);
                officerAvatar.RadianDirection = officerYaw;

                if (cameraFollowOfficer)
                    SmoothCameraToward(Vector2.Lerp(officerSmoothTile, escortCruiserDoorPoint, 0.45f), dt * 3.4f);

                if (!escortLoadedIntoCruiser && escortBurglar != null && !escortBurglar.Dead)
                {
                    float u = Math.Max(0f, Math.Min(1f, escortLoadTimerSec / 0.95f));
                    burglarSmoothTile = Vector2.Lerp(escortBurglarDoorStart, escortCruiserDoorPoint, u);
                    Vector2 toDoor = escortCruiserDoorPoint - escortBurglarDoorStart;
                    if (toDoor.LengthSquared() > 1e-4f)
                        burglarYaw = (float)Math.Atan2(toDoor.X, -toDoor.Y);
                    escortBurglar.VisualPosition = new Vector3(burglarSmoothTile.X, burglarSmoothTile.Y, 0f);
                    escortBurglar.RadianDirection = burglarYaw;

                    if (escortLoadTimerSec >= 0.95f)
                    {
                        escortLoadedIntoCruiser = true;
                        try
                        {
                            escortBurglar.Delete(true, patrolVm.Context);
                        }
                        catch { }
                        escortSlot.PrimaryEntity = null;
                        escortSlot.ArrestedOrCleared = true;
                        escortBurglar = null;
                        audio.Play(PatrolSfxId.CarDoorClose, 0.46f);
                        audio.Play(PatrolSfxId.RadioSquelch, 0.36f);
                        Boot.Log("beat-patrol: Burglar secured inside Police Squad Car!");
                    }
                }

                if (escortLoadTimerSec >= 1.45f)
                {
                    FinishEscortToCruiser();
                }
            }
        }

        static void FinishEscortToCruiser()
        {
            if (escortSlot != null && !escortSlot.ArrestedOrCleared)
            {
                try
                {
                    if (escortSlot.PrimaryEntity != null && patrolVm != null)
                        escortSlot.PrimaryEntity.Delete(true, patrolVm.Context);
                }
                catch { }
                escortSlot.PrimaryEntity = null;
                escortSlot.ArrestedOrCleared = true;
            }

            escortSlot = null;
            escortBurglar = null;
            escortLoadingPhase = false;
            escortLoadedIntoCruiser = false;
            escortLoadTimerSec = 0f;
            escortBlendSec = 0f;

            officerMode = OfficerMotionMode.IdleOnFoot;
            cameraFollowOfficer = false;
            officerClip.Play("cadet_idle_attention", 0.20f, 0.9f, true, false);
            lastActiveClipName = "cadet_idle_attention";

            if (game != null && game.IsFinished && finalOutcome == null)
            {
                finalOutcome = game.ComputeOutcome();
                if (!finishFanfarePlayed)
                {
                    finishFanfarePlayed = true;
                    audio.Play(PatrolSfxId.ShiftCompleteFanfare, 0.52f);
                }
            }
        }

        static void BeginPerformingAction()
        {
            if (pendingAction == PatrolActionKind.ReturnToCruiser)
            {
                audio.Play(PatrolSfxId.CarDoorOpen, 0.42f);
                officerAvatar.SetPosition(LotTilePos.OUT_OF_WORLD, Direction.SOUTH, patrolVm.Context);
                officerMode = OfficerMotionMode.InCruiser;
                cameraFollowOfficer = false;
                audio.Play(PatrolSfxId.CarDoorClose, 0.38f);
                lastActiveClipName = "in_cruiser";
                return;
            }

            // Face the target entity directly
            var slot = actorSlots.FirstOrDefault(s => s.IncidentIndex == pendingIncidentIndex);
            if (slot != null && slot.PrimaryEntity != null)
            {
                Vector3 tp = slot.PrimaryEntity.VisualPosition;
                float dx = tp.X - officerSmoothTile.X;
                float dy = tp.Y - officerSmoothTile.Y;
                if (dx * dx + dy * dy > 0.001f)
                    officerYaw = (float)Math.Atan2(dx, -dy);
            }

            string resolveClip = "cadet_idle_attention";
            bool loopResolve = false;
            actionDurationSec = 2.40f;

            switch (pendingAction)
            {
                case PatrolActionKind.SneakApprehend:
                case PatrolActionKind.ConfrontLoud:
                    resolveClip = "patrol_apprehend";
                    loopResolve = false;
                    actionDurationSec = 2.65f;
                    break;
                case PatrolActionKind.IssueTicket:
                    resolveClip = "patrol_write_ticket";
                    loopResolve = true;
                    actionDurationSec = 2.25f;
                    break;
                case PatrolActionKind.InspectHazard:
                    resolveClip = "patrol_inspect_clue";
                    loopResolve = true;
                    actionDurationSec = 2.15f;
                    break;
                default:
                    resolveClip = "cadet_idle_attention";
                    loopResolve = true;
                    actionDurationSec = 2.10f;
                    break;
            }

            officerClip.Play(resolveClip, 0.12f, 1.0f, loopResolve, true);
            lastActiveClipName = resolveClip;
            actionTimerSec = 0f;
            actionMidCueFired = false;
            actionResolved = false;
            officerMode = OfficerMotionMode.PerformingAction;
        }

        static void ResolvePendingActionMidClip()
        {
            if (actionResolved || game == null || pendingIncidentIndex < 0 || pendingIncidentIndex >= game.Incidents.Count)
                return;

            actionResolved = true;
            var rating = game.ResolveIncident(pendingIncidentIndex, pendingAction);
            var inc = game.Incidents[pendingIncidentIndex];
            var slot = actorSlots.FirstOrDefault(s => s.IncidentIndex == pendingIncidentIndex);

            // Play domain-specific action SFX
            if (pendingAction == PatrolActionKind.SneakApprehend || pendingAction == PatrolActionKind.ConfrontLoud)
                audio.Play(PatrolSfxId.HandcuffClick, 0.50f);
            else if (pendingAction == PatrolActionKind.IssueTicket)
                audio.Play(PatrolSfxId.TicketRip, 0.48f);
            else if (pendingAction == PatrolActionKind.InspectHazard)
                audio.Play(PatrolSfxId.FlashlightClick, 0.48f);
            else
                audio.Play(PatrolSfxId.RadioSquelch, 0.36f);

            // Play rating feedback chime
            if (rating == PatrolActionRating.Perfect)
                audio.Play(PatrolSfxId.PerfectCallSting, 0.46f);
            else if (rating == PatrolActionRating.Good)
                audio.Play(PatrolSfxId.GoodCallSting, 0.42f);
            else if (rating == PatrolActionRating.Miss)
                audio.Play(PatrolSfxId.WrongCallBuzzer, 0.46f);

            // Pop overhead Simitone speech balloons above Officer & Suspect!
            try
            {
                if (patrolVm != null && officerAvatar != null && !string.IsNullOrWhiteSpace(inc.OfficerLine))
                {
                    VMEntity listener = slot != null ? slot.PrimaryEntity : null;
                    string listenerName = !string.IsNullOrWhiteSpace(inc.ActorName) ? inc.ActorName : "Citizen";
                    ConvoOverlay.Show(
                        patrolVm,
                        officerAvatar,
                        "Officer " + homeOfficerName,
                        inc.OfficerLine,
                        listener,
                        listenerName,
                        inc.SuspectLine ?? "");
                }
            }
            catch { }
        }

        static void FinishPerformingAction()
        {
            var slot = actorSlots.FirstOrDefault(s => s.IncidentIndex == pendingIncidentIndex);
            if (slot != null && patrolVm != null)
            {
                // Arrested the Burglar? Escort them handcuffed all the way back to the parked Police Squad Car!
                if (ShouldEscortToCruiser(slot.Kind, pendingAction) &&
                    slot.PrimaryEntity is VMAvatar arrestedBurglar &&
                    !arrestedBurglar.Dead)
                {
                    BeginEscortToCruiser(slot, arrestedBurglar);
                    return;
                }

                slot.ArrestedOrCleared = true;
                try
                {
                    // Remove escaped Burglar (if WarnCitizen missed), cleared Trash Hazard, and silenced Boombox
                    if (slot.Kind == PatrolIncidentKind.BurglarProwler && slot.PrimaryEntity != null)
                    {
                        slot.PrimaryEntity.Delete(true, patrolVm.Context);
                        slot.PrimaryEntity = null;
                    }
                    else if (slot.Kind == PatrolIncidentKind.CivicHazard && slot.PrimaryEntity != null)
                    {
                        slot.PrimaryEntity.Delete(true, patrolVm.Context);
                        slot.PrimaryEntity = null;
                    }
                    else if (slot.Kind == PatrolIncidentKind.NoiseDisturbance && slot.SecondaryEntity != null)
                    {
                        slot.SecondaryEntity.Delete(true, patrolVm.Context);
                        slot.SecondaryEntity = null;
                    }
                }
                catch { }
            }

            pendingIncidentIndex = -1;
            officerMode = OfficerMotionMode.IdleOnFoot;
            cameraFollowOfficer = false;
            officerClip.Play("cadet_idle_attention", 0.20f, 0.9f, true, false);
            lastActiveClipName = "cadet_idle_attention";

            if (game != null && game.IsFinished && finalOutcome == null)
            {
                finalOutcome = game.ComputeOutcome();
                if (!finishFanfarePlayed)
                {
                    finishFanfarePlayed = true;
                    audio.Play(PatrolSfxId.ShiftCompleteFanfare, 0.52f);
                }
            }
        }

        // ---- Entity & Lot Helpers ----

        static void KeepPatrolActorsControlled()
        {
            if (cruiserGroup != null) FreezeGroup(cruiserGroup, pauseThread: true);
            if (officerAvatar != null) FreezeEntity(officerAvatar, pauseThread: true);
            for (int i = 0; i < actorSlots.Count; i++)
            {
                var s = actorSlots[i];
                bool pausePrimary = (s.Kind == PatrolIncidentKind.BurglarProwler || s.Kind == PatrolIncidentKind.CivicHazard);
                if (s.PrimaryEntity != null) FreezeEntity(s.PrimaryEntity, pauseThread: pausePrimary);
                if (s.SecondaryEntity != null) FreezeEntity(s.SecondaryEntity, pauseThread: true);
            }
        }

        static void FreezeGroup(VMMultitileGroup grp, bool pauseThread = true)
        {
            if (grp == null || grp.Objects == null) return;
            for (int i = 0; i < grp.Objects.Count; i++)
                FreezeEntity(grp.Objects[i], pauseThread);
        }

        static void FreezeEntity(VMEntity ent, bool pauseThread = true)
        {
            if (ent == null || ent.Dead) return;
            try
            {
                var th = ent.Thread;
                if (th == null) return;

                if (pauseThread)
                {
                    if (th.ThreadBreak != VMThreadBreakMode.Pause)
                        th.ThreadBreak = VMThreadBreakMode.Pause;
                    if (th.Stack != null && th.Stack.Count > 0)
                        th.Stack.Clear();
                    if (th.BlockingState != null)
                        th.BlockingState = null;
                }

                if (th.Queue != null && th.Queue.Count > 0)
                    th.Queue.Clear();
            }
            catch { }
        }

        static bool IsCruiserEntity(VMEntity target)
        {
            if (target == null) return false;
            if (cruiserGroup != null && target.MultitileGroup != null && ReferenceEquals(target.MultitileGroup, cruiserGroup))
                return true;
            if (cruiserLead != null && ReferenceEquals(target, cruiserLead))
                return true;
            try { return target.Object?.OBJ?.GUID == GuidPoliceCruiser; } catch { return false; }
        }

        static PatrolActorSlot FindSlotForEntity(VMEntity target)
        {
            if (target == null) return null;
            for (int i = 0; i < actorSlots.Count; i++)
            {
                var s = actorSlots[i];
                if (MatchesEntity(s.PrimaryEntity, target) || MatchesEntity(s.SecondaryEntity, target))
                    return s;
            }
            return null;
        }

        static bool MatchesEntity(VMEntity slotEnt, VMEntity target)
        {
            if (slotEnt == null || target == null) return false;
            if (ReferenceEquals(slotEnt, target) || slotEnt.ObjectID == target.ObjectID) return true;
            if (slotEnt.MultitileGroup != null && target.MultitileGroup != null &&
                ReferenceEquals(slotEnt.MultitileGroup, target.MultitileGroup))
                return true;
            return false;
        }

        static List<VMPieMenuInteraction> ToPieList(List<PatrolPieActionOption> opts, VMEntity target)
        {
            var list = new List<VMPieMenuInteraction>(opts.Count);
            for (int i = 0; i < opts.Count; i++)
            {
                list.Add(new VMPieMenuInteraction
                {
                    Name = opts[i].Label,
                    ID = (byte)(PatrolPieActionBase + (int)opts[i].Action),
                    Param0 = 0,
                    Callee = target,
                    Global = false,
                    Score = 100 - i * 10,
                });
            }
            return list;
        }

        static void CaptureHomeOfficer(VMAvatar sim, VM vm, UIScreen screen)
        {
            homeOfficerGuid = 0;
            homeOfficerNeighborId = 0;
            homeOfficerName = "Officer";
            homeLogicSkill = 3;
            homeCharismaSkill = 3;
            homeOfficerHead = null;
            homeOfficerBody = null;
            homeOfficerHands = null;
            homeOfficerGender = 0;
            homeOfficerSkinColor = 0;
            homeOfficerPersonAge = 27;

            if (sim != null)
            {
                try { homeOfficerGuid = sim.Object?.OBJ?.GUID ?? 0; } catch { }
                try { homeOfficerNeighborId = sim.GetPersonData(VMPersonDataVariable.NeighborId); } catch { }
                if (!string.IsNullOrWhiteSpace(sim.Name)) homeOfficerName = sim.Name.Trim();
                try { homeLogicSkill = Math.Max(0, Math.Min(10, sim.GetPersonData(VMPersonDataVariable.LogicSkill) / 100)); } catch { }
                try { homeCharismaSkill = Math.Max(0, Math.Min(10, sim.GetPersonData(VMPersonDataVariable.CharismaSkill) / 100)); } catch { }
                try { homeOfficerGender = sim.GetPersonData(VMPersonDataVariable.Gender); } catch { }
                try { homeOfficerSkinColor = sim.GetPersonData(VMPersonDataVariable.SkinColor); } catch { }
                try { homeOfficerPersonAge = sim.GetPersonData(VMPersonDataVariable.PersonsAge); } catch { }
                if (sim.Avatar != null)
                {
                    homeOfficerHead = sim.Avatar.Head;
                    homeOfficerBody = sim.Avatar.Body;
                    homeOfficerHands = sim.Avatar.Handgroup;
                }
            }

            homeHouseNumber = 7;
            try
            {
                var activeFam = screen.GetType().GetField("ActiveFamily")?.GetValue(screen) as FAMI
                                ?? vm.TS1State?.CurrentFamily;
                if (activeFam != null && activeFam.HouseNumber > 0)
                    homeHouseNumber = activeFam.HouseNumber;
            }
            catch { }
        }

        static VMAvatar SpawnAndDressOfficer(VM vm, LotTilePos spawnTile)
        {
            VMAvatar av = null;
            if (homeOfficerGuid != 0)
                av = vm.Entities.OfType<VMAvatar>().FirstOrDefault(a => a != null && !a.Dead && a.Object?.OBJ?.GUID == homeOfficerGuid);

            if (av == null && homeOfficerGuid != 0)
                av = SpawnAvatarAt(vm, homeOfficerGuid, spawnTile, Direction.SOUTH);
            if (av == null)
                av = SpawnAvatarAt(vm, GuidFallbackPerson, spawnTile, Direction.SOUTH);
            if (av == null) return null;

            av.Name = homeOfficerName;
            try { av.SetPersonData(VMPersonDataVariable.Gender, homeOfficerGender); } catch { }
            try { av.SetPersonData(VMPersonDataVariable.SkinColor, homeOfficerSkinColor); } catch { }
            try { av.SetPersonData(VMPersonDataVariable.PersonsAge, homeOfficerPersonAge); } catch { }

            if (av.Avatar != null)
            {
                if (homeOfficerHead != null) av.Avatar.Head = homeOfficerHead;
                string uniformTag;
                var uniform = CadetAcademySceneRenderer.ResolveLawEnforcementUniform(
                    Content.Get(), av, out uniformTag, new[] { 2, 1, 0, 3 });
                if (uniform != null)
                {
                    av.Avatar.Body = uniform;
                    av.Avatar.Handgroup = homeOfficerHands ?? uniform;
                }
                else if (homeOfficerBody != null)
                {
                    av.Avatar.Body = homeOfficerBody;
                    if (homeOfficerHands != null) av.Avatar.Handgroup = homeOfficerHands;
                }
            }

            FreezeEntity(av);
            return av;
        }

        static VMAvatar SpawnAvatarAt(VM vm, uint guid, LotTilePos tile, Direction dir)
        {
            var grp = TrySpawnObjectNear(vm, guid, tile, dir, 3);
            if (grp == null || grp.Objects == null) return null;
            var av = grp.BaseObject as VMAvatar ?? grp.Objects.OfType<VMAvatar>().FirstOrDefault();
            if (av != null)
            {
                av.VisualPosition = new Vector3(av.Position.TileX + 0.5f, av.Position.TileY + 0.5f, 0f);
                av.Direction = dir;
            }
            return av;
        }

        static VMMultitileGroup TrySpawnObjectNear(VM vm, uint guid, LotTilePos center, Direction dir, int radius)
        {
            try
            {
                var first = vm.Context.CreateObjectInstance(guid, center, dir);
                if (first != null && first.Objects != null && first.Objects.Count > 0 &&
                    first.Objects[0].Position != LotTilePos.OUT_OF_WORLD)
                    return first;
                if (first != null && first.Objects != null && first.Objects.Count > 0)
                {
                    // Try relocating base object to a nearby clear tile
                    var baseObj = first.BaseObject ?? first.Objects[0];
                    for (int r = 1; r <= radius; r++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            for (int dy = -r; dy <= r; dy++)
                            {
                                var p = new LotTilePos((short)((center.TileX + dx) * 16), (short)((center.TileY + dy) * 16), 1);
                                var res = baseObj.SetPosition(p, dir, vm.Context, VMPlaceRequestFlags.Default);
                                if (res.Status == VMPlacementError.Success)
                                    return first;
                            }
                        }
                    }
                    // Last resort: AcceptSlots on center
                    baseObj.SetPosition(center, dir, vm.Context, VMPlaceRequestFlags.AcceptSlots);
                    return first;
                }
            }
            catch (Exception e)
            {
                Boot.Log("beat-patrol spawn error guid=0x" + guid.ToString("X8") + ": " + e.Message);
            }
            return null;
        }

        static void FindStreetCurbPlacement(VM vm, out LotTilePos roadPos, out LotTilePos standPos, out Direction roadDir)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            roadDir = Direction.EAST;

            // Locate existing CarPortal or PedPortal on the real lot to know exact road row/column!
            var carPortal = vm.Entities.FirstOrDefault(e => e != null && e.Object?.OBJ?.GUID == GuidCarPortal && e.Position != LotTilePos.OUT_OF_WORLD);
            var pedPortal = vm.Entities.FirstOrDefault(e => e != null && e.Object?.OBJ?.GUID == GuidPedPortal && e.Position != LotTilePos.OUT_OF_WORLD);

            if (carPortal != null)
            {
                int px = carPortal.Position.TileX;
                int py = carPortal.Position.TileY;
                // CarPortal sits at one end of the street; park the Squad Car near the center of that street!
                if (py <= 12 || py >= h - 12)
                {
                    // Horizontal street along top or bottom edge
                    int midX = Math.Max(8, Math.Min(w - 10, w / 2));
                    roadPos = new LotTilePos((short)(midX * 16), (short)(py * 16), 1);
                    roadDir = Direction.EAST;
                    int curbY = py <= 12 ? Math.Min(h - 4, py + 3) : Math.Max(4, py - 3);
                    standPos = FindNearestWalkableTile(vm, new LotTilePos((short)(midX * 16), (short)(curbY * 16), 1), 0, 5, null);
                    return;
                }
                else
                {
                    // Vertical street along left or right edge
                    int midY = Math.Max(8, Math.Min(h - 10, h / 2));
                    roadPos = new LotTilePos((short)(px * 16), (short)(midY * 16), 1);
                    roadDir = Direction.SOUTH;
                    int curbX = px <= 12 ? Math.Min(w - 4, px + 3) : Math.Max(4, px - 3);
                    standPos = FindNearestWalkableTile(vm, new LotTilePos((short)(curbX * 16), (short)(midY * 16), 1), 0, 5, null);
                    return;
                }
            }

            if (pedPortal != null)
            {
                int px = pedPortal.Position.TileX;
                int py = pedPortal.Position.TileY;
                roadPos = new LotTilePos((short)(Math.Max(8, Math.Min(w - 10, w / 2)) * 16), (short)(py * 16), 1);
                standPos = FindNearestWalkableTile(vm, new LotTilePos(roadPos.x, (short)(Math.Min(h - 4, py + 3) * 16), 1), 0, 6, null);
                return;
            }

            // Fallback: front street row near y = h - 7
            int fx = Math.Max(8, w / 2);
            int fy = Math.Max(6, h - 7);
            roadPos = new LotTilePos((short)(fx * 16), (short)(fy * 16), 1);
            standPos = FindNearestWalkableTile(vm, new LotTilePos((short)(fx * 16), (short)((fy - 3) * 16), 1), 0, 6, null);
        }

        static List<LotTilePos> PickIncidentSpawnTiles(VM vm, LotTilePos curb, int count)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            var picked = new List<LotTilePos>(count);
            var rng = new Random(patrolSeed + 911);

            // Preferred offsets radiating into the lot from the parked cruiser (so all 4 incidents
            // are cleanly visible within a quick pan across the real lot!)
            int inwardY = curb.TileY < h / 2 ? 1 : -1;
            int inwardX = curb.TileX < w / 2 ? 1 : -1;

            var preferredCenters = new[]
            {
                // Burglar: close to front-side plaza/wall so sneaking from the cruiser looks cinematic!
                new Point(curb.TileX + inwardX * 5, curb.TileY + inwardY * 7),
                // Noise Disturbance: opposite side of front courtyard
                new Point(curb.TileX - inwardX * 6, curb.TileY + inwardY * 6),
                // Civic Hazard: near the front sidewalk/walkway
                new Point(curb.TileX + inwardX * 2, curb.TileY + inwardY * 4),
                // Innocent Citizen: strolling along the opposite sidewalk
                new Point(curb.TileX - inwardX * 3, curb.TileY + inwardY * 9),
            };

            for (int i = 0; i < count; i++)
            {
                var pt = preferredCenters[i % preferredCenters.Length];
                int cx = Math.Max(5, Math.Min(w - 6, pt.X));
                int cy = Math.Max(5, Math.Min(h - 6, pt.Y));
                var candidate = FindNearestWalkableTile(
                    vm,
                    new LotTilePos((short)(cx * 16), (short)(cy * 16), 1),
                    minDist: 0,
                    maxDist: 10,
                    avoidTiles: picked);

                if (candidate == LotTilePos.OUT_OF_WORLD)
                {
                    // Fallback scan anywhere walkable on lot
                    for (int tries = 0; tries < 80; tries++)
                    {
                        int rx = rng.Next(6, Math.Max(7, w - 6));
                        int ry = rng.Next(6, Math.Max(7, h - 6));
                        var tp = new LotTilePos((short)(rx * 16), (short)(ry * 16), 1);
                        if (IsWalkableTile(vm, tp) && IsFarEnough(tp, picked, 3))
                        {
                            candidate = tp;
                            break;
                        }
                    }
                }
                if (candidate == LotTilePos.OUT_OF_WORLD)
                    candidate = curb;

                picked.Add(candidate);
            }

            return picked;
        }

        static LotTilePos FindProwlEndTile(VM vm, LotTilePos start)
        {
            int[] dxs = { 3, -3, 0, 0, 2, -2 };
            int[] dys = { 0, 0, 3, -3, 2, -2 };
            for (int i = 0; i < dxs.Length; i++)
            {
                var p = new LotTilePos((short)((start.TileX + dxs[i]) * 16), (short)((start.TileY + dys[i]) * 16), 1);
                if (IsWalkableTile(vm, p))
                    return p;
            }
            return start;
        }

        static LotTilePos PickApproachTile(PatrolActorSlot slot, bool preferBehind)
        {
            if (slot == null || slot.PrimaryEntity == null || patrolVm == null)
                return curbStandTile;

            var tPos = slot.PrimaryEntity.VisualPosition;
            int tx = (int)Math.Round(tPos.X - 0.5f);
            int ty = (int)Math.Round(tPos.Y - 0.5f);

            if (preferBehind)
            {
                float yaw = slot.PrimaryEntity.RadianDirection;
                int bx = (int)Math.Round(tPos.X - Math.Sin(yaw) * 1.25f - 0.5f);
                int by = (int)Math.Round(tPos.Y + Math.Cos(yaw) * 1.25f - 0.5f);
                var behind = new LotTilePos((short)(bx * 16), (short)(by * 16), 1);
                if (IsWalkableTile(patrolVm, behind))
                    return behind;
            }

            return FindNearestWalkableTile(
                patrolVm,
                new LotTilePos((short)(tx * 16), (short)(ty * 16), 1),
                minDist: 1,
                maxDist: 3,
                avoidTiles: null);
        }

        static LotTilePos FindNearestWalkableTile(VM vm, LotTilePos center, int minDist, int maxDist, IList<LotTilePos> avoidTiles)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            for (int r = minDist; r <= maxDist; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int tx = center.TileX + dx;
                        int ty = center.TileY + dy;
                        if (tx < 3 || ty < 3 || tx >= w - 3 || ty >= h - 3) continue;
                        var p = new LotTilePos((short)(tx * 16), (short)(ty * 16), 1);
                        if (!IsWalkableTile(vm, p)) continue;
                        if (avoidTiles != null && !IsFarEnough(p, avoidTiles, 3)) continue;
                        return p;
                    }
                }
            }
            return center;
        }

        static bool IsWalkableTile(VM vm, LotTilePos pos)
        {
            try
            {
                int w = vm.Context.Architecture.Width;
                int h = vm.Context.Architecture.Height;
                if (pos.TileX < 2 || pos.TileY < 2 || pos.TileX >= w - 2 || pos.TileY >= h - 2) return false;
                return !vm.Context.SolidToAvatars(pos).Solid;
            }
            catch { return false; }
        }

        static bool IsFarEnough(LotTilePos p, IList<LotTilePos> others, int minTiles)
        {
            for (int i = 0; i < others.Count; i++)
            {
                int dx = p.TileX - others[i].TileX;
                int dy = p.TileY - others[i].TileY;
                if (dx * dx + dy * dy < minTiles * minTiles)
                    return false;
            }
            return true;
        }

        // Fast bounded BFS over walkable tiles (with clean direct fallback) so the Officer
        // navigates cleanly around cars, planters, and furniture with zero SimAntics route-stuck hangs!
        static void BuildWalkablePath(VM vm, LotTilePos start, LotTilePos goal, List<Vector2> outPath)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            int sx = Math.Max(1, Math.Min(w - 2, (int)start.TileX));
            int sy = Math.Max(1, Math.Min(h - 2, (int)start.TileY));
            int gx = Math.Max(1, Math.Min(w - 2, (int)goal.TileX));
            int gy = Math.Max(1, Math.Min(h - 2, (int)goal.TileY));

            if (sx == gx && sy == gy)
            {
                outPath.Add(new Vector2(gx + 0.5f, gy + 0.5f));
                return;
            }

            var visited = new bool[w * h];
            var parent = new int[w * h];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;

            var q = new Queue<int>();
            int startIdx = sy * w + sx;
            int goalIdx = gy * w + gx;
            visited[startIdx] = true;
            q.Enqueue(startIdx);

            int[] dxs = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dys = { 0, 0, 1, -1, 1, -1, 1, -1 };
            bool found = false;
            int expanded = 0;

            while (q.Count > 0 && expanded < 1800)
            {
                int cur = q.Dequeue();
                expanded++;
                if (cur == goalIdx) { found = true; break; }

                int cx = cur % w;
                int cy = cur / w;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + dxs[d];
                    int ny = cy + dys[d];
                    if (nx < 2 || ny < 2 || nx >= w - 2 || ny >= h - 2) continue;
                    int nIdx = ny * w + nx;
                    if (visited[nIdx]) continue;
                    if (nIdx != goalIdx && !IsWalkableTile(vm, new LotTilePos((short)(nx * 16), (short)(ny * 16), 1)))
                        continue;
                    visited[nIdx] = true;
                    parent[nIdx] = cur;
                    q.Enqueue(nIdx);
                }
            }

            if (found)
            {
                var rev = new List<Vector2>();
                int cur = goalIdx;
                while (cur != -1 && cur != startIdx)
                {
                    rev.Add(new Vector2((cur % w) + 0.5f, (cur / w) + 0.5f));
                    cur = parent[cur];
                }
                rev.Reverse();
                outPath.AddRange(rev);
            }
            else
            {
                outPath.Add(new Vector2(gx + 0.5f, gy + 0.5f));
            }
        }

        static List<Tuple<Neighbour, string>> PickDistinctTownieNeighbors(int count)
        {
            var result = new List<Tuple<Neighbour, string>>();
            try
            {
                var content = Content.Get();
                var nb = content?.Neighborhood;
                var entries = nb?.Neighbors?.Entries;
                if (entries == null) return result;

                var candidates = new List<Tuple<Neighbour, string>>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var n = entries[i];
                    if (n == null || n.GUID == 0) continue;
                    if (n.GUID == homeOfficerGuid || n.NeighbourID == homeOfficerNeighborId) continue;

                    // Require adult Sim when PersonData is available
                    if (n.PersonData != null && n.PersonData.Length > (int)VMPersonDataVariable.PersonsAge)
                    {
                        short age = n.PersonData[(int)VMPersonDataVariable.PersonsAge];
                        if (age > 0 && age < 18) continue;
                    }

                    string displayName = ResolveNeighborDisplayName(content, n);
                    if (string.IsNullOrWhiteSpace(displayName) || IsServiceNpcName(displayName))
                        continue;

                    candidates.Add(Tuple.Create(n, displayName));
                }

                if (candidates.Count == 0) return result;
                int start = Math.Abs(patrolSeed) % candidates.Count;
                for (int i = 0; i < candidates.Count && result.Count < count; i++)
                {
                    var cand = candidates[(start + i * 3) % candidates.Count];
                    if (!result.Any(r => r.Item1.NeighbourID == cand.Item1.NeighbourID))
                        result.Add(cand);
                }
            }
            catch { }
            return result;
        }

        static string ResolveNeighborDisplayName(Content content, Neighbour n)
        {
            try
            {
                var obj = content?.WorldObjects?.Get(n.GUID);
                if (obj != null)
                {
                    var ctss = obj.Resource?.Get<CTSS>(obj.OBJ.CatalogStringsID);
                    string s = ctss?.GetString(0) ?? obj.OBJ?.ChunkLabel;
                    if (!string.IsNullOrWhiteSpace(s))
                        return s.Trim();
                }
            }
            catch { }
            return (n.Name ?? "").Trim();
        }

        static bool IsServiceNpcName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string low = name.Trim().ToLowerInvariant();
            string[] bad = { "clown", "npc", "maid", "gardener", "repair", "pizza", "burglar", "thief", "police", "cop", "fire", "mime", "grim", "santa", "genie", "bear", "pet", "cat", "dog", "stray", "raccoon", "skunk", "entertainer", "waiter", "chef", "dj", "bartender", "janitor", "sax", "bus", "car", "driver", "social", "skeleton", "bonehilda", "serv", "user0", "mystery", "stranger" };
            for (int i = 0; i < bad.Length; i++)
                if (low.Contains(bad[i])) return true;
            return !char.IsUpper(name.Trim()[0]);
        }

        static List<int> DiscoverAvailablePatrolLots()
        {
            var ids = new List<int>();
            int[] ranges = {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
                21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
                50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78
            };
            for (int i = 0; i < ranges.Length; i++)
            {
                if (HouseFileExists(ranges[i]))
                    ids.Add(ranges[i]);
            }
            return ids;
        }

        static bool HouseFileExists(int houseId)
        {
            try
            {
                var path = Content.Get()?.Neighborhood?.GetHousePath(houseId);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
            catch { return false; }
        }

        static string ResolveSectorDisplayName(int lotId, PatrolLotZone zone)
        {
            try
            {
                var nd = Content.Get()?.Neighborhood?.GetHouseNameDesc(lotId);
                if (nd != null && !string.IsNullOrWhiteSpace(nd.Item1))
                    return nd.Item1.Trim().ToUpperInvariant() + "  -  LOT " + lotId;
            }
            catch { }
            return BeatPatrolGame.FormatSectorName(lotId, zone);
        }

        // ---- TS1GameScreen Reflection Helpers ----

        static bool TriggerScreenSwitchLot(UIScreen screen, int lotId)
        {
            try
            {
                var f = screen.GetType().GetField("SwitchLot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(screen, lotId);
                    return true;
                }
            }
            catch (Exception e) { Boot.Log("beat-patrol SwitchLot reflection error: " + e.Message); }
            return false;
        }

        static void SelectActiveAvatar(VMAvatar av)
        {
            try
            {
                if (patrolVm != null && av != null)
                    patrolVm.MyUID = av.PersistID;
                var screen = UIScreen.Current;
                if (screen == null) return;
                var any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var lotCtrl = screen.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                if (lotCtrl != null)
                {
                    var prop = lotCtrl.GetType().GetProperty("ActiveEntity");
                    if (prop != null && prop.CanWrite) prop.SetValue(lotCtrl, av, null);
                    else
                    {
                        var fld = lotCtrl.GetType().GetField("ActiveEntity", any);
                        if (fld != null) fld.SetValue(lotCtrl, av);
                    }
                    var foundMe = lotCtrl.GetType().GetField("FoundMe", any);
                    if (foundMe != null) foundMe.SetValue(lotCtrl, true);
                }
            }
            catch { }
        }

        static World GetCurrentWorld()
        {
            try
            {
                if (patrolVm?.Context?.World != null) return patrolVm.Context.World;
                var screen = UIScreen.Current;
                return screen?.GetType().GetField("World", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screen) as World;
            }
            catch { return null; }
        }

        public static void TickAlertSweep()
        {
            if (phase != PatrolPhase.Active || patrolVm == null) return;
            if (alertSweepRemainingSec > 0f)
            {
                bool inEarlyBriefingLock = alertSweepRemainingSec > 3.5f && game != null && game.IsBriefing;
                alertSweepRemainingSec -= (1f / 60f);
                DismissBlockingAlerts(patrolVm);
                if (patrolVm.SpeedMultiplier == 0)
                {
                    patrolVm.SpeedMultiplier = 1;
                    patrolVm.LastSpeedMultiplier = 0;
                }
                if (inEarlyBriefingLock && cruiserTile != LotTilePos.OUT_OF_WORLD)
                {
                    var briefFocus = new LotTilePos(
                        (short)(((cruiserTile.TileX + curbStandTile.TileX) / 2) * 16),
                        (short)(((cruiserTile.TileY + curbStandTile.TileY + 1) / 2) * 16),
                        1);
                    FocusCameraOnTile(briefFocus, WorldZoom.Near, zoom3D: 3.1f);
                }
            }
        }

        static void DismissBlockingAlerts(VM vm)
        {
            try
            {
                var any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                var facade = typeof(UIScreen).Assembly.GetType("FSO.Client.GameFacade", false)
                             ?? AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Client.GameFacade", false)).FirstOrDefault(x => x != null);
                var screens = facade?.GetField("Screens", any)?.GetValue(null)
                              ?? facade?.GetProperty("Screens", any)?.GetValue(null, null);
                if (screens != null)
                {
                    var toClose = new List<object>();
                    foreach (var f in screens.GetType().GetFields(any))
                    {
                        if (f.FieldType == typeof(string)) continue;
                        var en = f.GetValue(screens) as System.Collections.IEnumerable;
                        if (en == null) continue;
                        foreach (var item in en)
                        {
                            var dlg = item?.GetType().GetField("Dialog")?.GetValue(item) ?? item;
                            if (dlg == null) continue;
                            // Dismiss any modal alert dialogs (ShowLoadErrors, lot welcome, SimAntics notice)
                            string tn = dlg.GetType().Name;
                            if (tn.Contains("Alert") || dlg.GetType().GetField("m_Options", any) != null)
                                toClose.Add(dlg);
                        }
                    }
                    if (toClose.Count > 0)
                    {
                        var removeMethod = typeof(UIScreen).GetMethod("RemoveDialog", any);
                        foreach (var d in toClose)
                        {
                            try { removeMethod?.Invoke(null, new object[] { d }); } catch { }
                        }
                        if (vm != null && vm.SpeedMultiplier == 0)
                        {
                            vm.SpeedMultiplier = 1;
                            vm.LastSpeedMultiplier = 0;
                        }
                    }
                }

                // Clear UILotControl.BlockingDialog reference too
                var screen = UIScreen.Current;
                var lotCtrl = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                if (lotCtrl != null)
                {
                    var bd = lotCtrl.GetType().GetField("BlockingDialog", any);
                    if (bd != null && bd.GetValue(lotCtrl) != null)
                        bd.SetValue(lotCtrl, null);
                }
            }
            catch { }
        }

        static void FocusCameraOnTile(LotTilePos tile, WorldZoom? zoom = null, float? zoom3D = null)
        {
            try
            {
                var world = GetCurrentWorld();
                if (world == null || world.State == null) return;
                world.State.CenterTile = new Vector2(tile.TileX + 0.5f, tile.TileY + 0.5f);
                world.State.ScrollAnchor = null;
                world.State.BuildMode = 0;
                if (zoom.HasValue)
                    world.State.Zoom = zoom.Value;
                var cam3d = world.State.Cameras?.Camera3D;
                if (cam3d != null && zoom3D.HasValue)
                {
                    cam3d.RotationX = -0.78f;
                    cam3d.RotationY = 0.52f;
                    cam3d.Zoom3D = zoom3D.Value;
                }

                var screen = UIScreen.Current;
                var lotCtrl = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                if (lotCtrl != null)
                {
                    var any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var foundMe = lotCtrl.GetType().GetField("FoundMe", any);
                    if (foundMe != null) foundMe.SetValue(lotCtrl, true);
                    if (zoom3D.HasValue)
                    {
                        // Sync UILotControl.TargetZoom with desired 3D zoom so Update() holds our framing
                        float targetZoom = Math.Max(0.55f, Math.Min(1.52f, 0.25f + (9.75f - zoom3D.Value) / 5.7f));
                        var tz = lotCtrl.GetType().GetField("TargetZoom", any);
                        if (tz != null) tz.SetValue(lotCtrl, targetZoom);
                    }
                }
            }
            catch { }
        }

        static void SetCameraZoom(WorldZoom zoom)
        {
            try
            {
                var world = GetCurrentWorld();
                if (world != null && world.State != null)
                    world.State.Zoom = zoom;
            }
            catch { }
        }

        static void SmoothCameraToward(Vector2 tilePt, float lerpFactor)
        {
            try
            {
                var world = GetCurrentWorld();
                if (world == null || world.State == null) return;
                float t = Math.Max(0.02f, Math.Min(0.35f, lerpFactor));
                world.State.CenterTile = Vector2.Lerp(world.State.CenterTile, tilePt, t);
            }
            catch { }
        }

        static void ForceReset()
        {
            phase = PatrolPhase.Inactive;
            sourceVm = null;
            patrolVm = null;
            cruiserGroup = null;
            cruiserLead = null;
            officerAvatar = null;
            actorSlots.Clear();
            movePath.Clear();
            game = null;
            finalOutcome = null;
            pendingReturnOutcome = null;
            officerMode = OfficerMotionMode.InCruiser;
            cameraFollowOfficer = false;
            lastActiveClipName = "none";
            escortSlot = null;
            escortBurglar = null;
            escortLoadingPhase = false;
            escortLoadedIntoCruiser = false;
            escortLoadTimerSec = 0f;
            escortBlendSec = 0f;
            audio.StopAmbience();
            BeatPatrolOverlay.Close();
        }
    }
}
