using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
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
    public enum DetectiveModalMode
    {
        None = 0,
        CaseBoard = 1,
    }

    public enum DetectivePieActionKind
    {
        InspectFootprint = 0,
        InspectMotive = 1,
        InspectAlibi = 2,
        QuestionSuspect0 = 3,
        QuestionSuspect1 = 4,
        QuestionSuspect2 = 5,
        OpenCaseBoard = 6,
        AccuseSuspect0 = 7,
        AccuseSuspect1 = 8,
        AccuseSuspect2 = 9,
        EndShift = 10,
    }

    // Real-Lot, 100%-Real-TS1-Game-Assets Driver for Law Enforcement Level 6 ("Detective"):
    // Crime Scene Deduction.
    // Switches safely into a live Sims 1 lot (preserving the player's saved home lot), sets atmospheric
    // twilight manor lighting, builds a Manor Parlor Crime Scene from 100% REAL TS1 catalog objects
    // (Entry Sculpture/Pedestal for Footprint clue, 2-tile Executive Desk with AcceptSlots Table Lamp
    // for Motive clue, Grandfather Clock / Parlor Timepiece for Alibi clue, Bookshelf, Study Chair,
    // Floor Lamps, Floor Plants, and parked Police Squad Car at the street curb), spawns the player's
    // Detective + 3 verified fully-textured real neighborhood Sims as parlor Suspects, drives 7 custom
    // Blender animations + procedural noir-forensics audio, and works seamlessly in BOTH 3D and 2D!
    public static class CrimeSceneDeductionDriver
    {
        const uint GuidPoliceCruiser = 0x76ECEDA7; // CarSquad.iff
        const uint GuidCarPortal = 0x865A6812;     // CarPortal.iff
        const uint GuidFallbackPerson = 0x7FD96B54;
        const uint DetectivePieActionBase = 9200;

        enum ScenePhase
        {
            Inactive,
            WaitingForSceneLot,
            Active,
            WaitingForHomeLot,
        }

        sealed class VerifiedNeighborInfo
        {
            public string Name;
            public short NeighborId;
            public uint OutfitGuid;
        }

        static ScenePhase phase = ScenePhase.Inactive;
        static VM sourceVm;
        static VM sceneVm;

        // Home-lot snapshot for seamless return home & real VM rewards
        static string homeDetectiveName = "Bob";
        static uint homeDetectiveGuid;
        static short homeDetectiveNeighborId;
        static int homeLogicSkill = 5;
        static int homeCharismaSkill = 4;
        static int homeHouseNumber = 7;
        static Outfit homeDetectiveHead;
        static Outfit homeDetectiveBody;
        static int caseSeed = 42;
        static int targetSceneLotId = 8;

        // Live game state & outcome
        static CrimeSceneDeductionGame game;
        static CrimeSceneOutcome pendingReturnOutcome;
        static readonly short[] suspectNeighborIds = new short[3];
        static bool finishFanfarePlayed;
        static bool scorecardReady;
        static float climaxTimerSec;
        static DetectiveModalMode activeModal = DetectiveModalMode.None;
        static int focusedSuspectIndex = -1;

        // Real TS1 entities in the Manor Parlor Crime Scene
        static VMMultitileGroup cruiserGroup;
        static VMEntity cruiserLead;
        static LotTilePos cruiserTile = LotTilePos.OUT_OF_WORLD;
        static readonly VMEntity[] clueEntities = new VMEntity[3];
        static readonly Vector2[] clueInspectSpots = new Vector2[3];
        static readonly float[] clueInspectFacing = new float[3];
        static readonly List<VMEntity> sceneProps = new List<VMEntity>();

        static VMAvatar detectiveAvatar;
        static readonly VMAvatar[] suspectAvatars = new VMAvatar[3];
        static readonly Vector2[] suspectWorldPos = new Vector2[3];
        static readonly float[] suspectFacing = new float[3];
        static readonly Vector2[] suspectApproachSpots = new Vector2[3];
        static readonly float[] suspectApproachFacing = new float[3];

        // Detective walking + action animation state
        static Vector2 detectiveHomePos;
        static Vector2 detectivePos;
        static Vector2 detectiveWalkStart;
        static Vector2 detectiveWalkTarget;
        static float detectiveTargetFacing = 1.6f;
        static float detectiveFacing = 1.6f;
        static float walkElapsed;
        static float walkDuration;
        static bool isWalking;
        static Action onWalkArrived;
        static float detectiveActionTimerSec;

        static readonly VitaboyClipPlayer detectiveClip = new VitaboyClipPlayer();
        static readonly VitaboyClipPlayer[] suspectClips =
        {
            new VitaboyClipPlayer(),
            new VitaboyClipPlayer(),
            new VitaboyClipPlayer(),
        };
        static string lastDetectiveClipName = "detective_idle_think";
        static readonly string[] lastSuspectClipNames = { "detective_suspect_nervous", "detective_suspect_nervous", "detective_suspect_nervous" };

        static readonly Stopwatch frameClock = new Stopwatch();
        static double lastFrameSec = -1.0;
        static readonly Stopwatch animClock = new Stopwatch();
        static double lastAnimSec = -1.0;
        static float alertSweepRemainingSec;
        static float cameraFlashAlpha;
        static string spawnedAssetsSummary = "";

        static readonly DetectiveAudioEngine audio = new DetectiveAudioEngine();

        public static bool IsActive { get { return phase != ScenePhase.Inactive; } }
        public static bool IsSceneLotLive { get { return phase == ScenePhase.Active && sceneVm != null && game != null; } }
        public static bool IsActiveOn(VM vm) { return IsSceneLotLive && vm != null && ReferenceEquals(vm, sceneVm); }
        public static CrimeSceneDeductionGame Game { get { return game; } }
        public static CrimeSceneOutcome FinalOutcome { get { return game?.Outcome; } }
        public static bool ShowScorecardNow { get { return game != null && game.IsFinished && scorecardReady && game.Outcome != null; } }
        public static DetectiveModalMode ActiveModal { get { return activeModal; } }
        public static int FocusedSuspectIndex { get { return focusedSuspectIndex; } }
        public static float CameraFlashAlpha { get { return cameraFlashAlpha; } }
        public static VMAvatar DetectiveAvatar { get { return detectiveAvatar; } }
        public static VMAvatar[] SuspectAvatars { get { return suspectAvatars; } }
        public static VMEntity[] ClueEntities { get { return clueEntities; } }
        public static string SpawnedAssetsSummary { get { return spawnedAssetsSummary; } }
        internal static DetectiveAudioEngine Audio { get { return audio; } }

        public static string ExportLastSessionWav(string path)
        {
            try
            {
                audio.ExportRecordedTrackWav(path);
                return "ok wav=" + path;
            }
            catch (Exception e) { return "err " + e.Message; }
        }

        public static string BeginCase(VMAvatar detective, int? logicOverride = null, int? charismaOverride = null, int? seedOverride = null)
        {
            if (phase != ScenePhase.Inactive)
                return "error detective case already active (" + phase + ")";

            try
            {
                var screen = UIScreen.Current;
                var vm = screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
                if (screen == null || vm == null || !vm.Ready)
                    return "error no active lot loaded";

                CaptureHomeDetective(detective, vm, screen, logicOverride, charismaOverride);
                caseSeed = seedOverride ?? (Environment.TickCount & 0x7FFF);

                // Save home lot quietly so zero household progress is lost
                try { screen.GetType().GetMethod("SaveHouse")?.Invoke(screen, null); } catch { }

                targetSceneLotId = PickCleanSceneLot(homeHouseNumber);
                sourceVm = vm;
                sceneVm = null;
                game = null;
                pendingReturnOutcome = null;
                finishFanfarePlayed = false;
                scorecardReady = false;
                climaxTimerSec = 0f;
                activeModal = DetectiveModalMode.None;
                focusedSuspectIndex = -1;
                isWalking = false;
                onWalkArrived = null;
                sceneProps.Clear();
                Array.Clear(clueEntities, 0, clueEntities.Length);
                Array.Clear(suspectAvatars, 0, suspectAvatars.Length);
                Array.Clear(suspectNeighborIds, 0, suspectNeighborIds.Length);
                detectiveAvatar = null;
                cruiserGroup = null;
                cruiserLead = null;
                spawnedAssetsSummary = "";
                phase = ScenePhase.WaitingForSceneLot;

                try
                {
                    var ngbh = Content.Get()?.Neighborhood;
                    var gs = ngbh?.GameState;
                    if (gs != null)
                    {
                        if (vm.TS1State?.CurrentFamily != null)
                            gs.ActiveFamily = vm.TS1State.CurrentFamily;
                        if (homeDetectiveGuid != 0)
                            gs.DowntownSimGUID = homeDetectiveGuid;
                    }
                }
                catch { }

                DismissBlockingAlerts(vm);

                if (!TriggerScreenSwitchLot(screen, targetSceneLotId))
                {
                    phase = ScenePhase.Inactive;
                    return "error failed to trigger TS1GameScreen.SwitchLot";
                }

                Boot.Log("crime-scene: switching from House" + homeHouseNumber + " -> Manor Crime Scene on House" + targetSceneLotId +
                         " detective=" + homeDetectiveName + " (Log=" + homeLogicSkill + ", Cha=" + homeCharismaSkill + ", seed=" + caseSeed + ")");
                return "ok switching sceneLot=" + targetSceneLotId + " detective=" + homeDetectiveName;
            }
            catch (Exception e)
            {
                phase = ScenePhase.Inactive;
                Boot.Log("crime-scene BeginCase error: " + e.Message);
                return "error " + e.Message;
            }
        }

        public static void OnTick(VM vm)
        {
            if (phase == ScenePhase.Inactive || vm == null || !vm.Ready) return;

            if (phase == ScenePhase.WaitingForSceneLot)
            {
                if (!ReferenceEquals(vm, sourceVm) && vm.Context != null && vm.Context.World != null)
                    SetupCrimeSceneLot(vm);
                return;
            }

            if (phase == ScenePhase.WaitingForHomeLot)
            {
                if (!ReferenceEquals(vm, sceneVm) && vm.Context != null)
                    CompleteReturnHome(vm);
                return;
            }

            if (phase == ScenePhase.Active)
            {
                if (!ReferenceEquals(vm, sceneVm))
                {
                    ForceReset();
                    return;
                }

                KeepSceneActorsControlled();
                UpdateFrame();
            }
        }

        public static void TickAlertSweep()
        {
            if (alertSweepRemainingSec > 0f && sceneVm != null)
            {
                alertSweepRemainingSec = Math.Max(0f, alertSweepRemainingSec - (1f / 60f));
                DismissBlockingAlerts(sceneVm);
            }
        }

        static void UpdateFrame()
        {
            if (phase != ScenePhase.Active || sceneVm == null || game == null) return;

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

            // Warm twilight parlor lighting (18:45 — moody detective vibe while super readable!)
            if (sceneVm.Context?.Clock != null)
            {
                sceneVm.Context.Clock.Hours = 18;
                sceneVm.Context.Clock.Minutes = 45;
            }

            if (cameraFlashAlpha > 0f)
                cameraFlashAlpha = Math.Max(0f, cameraFlashAlpha - dt * 2.4f);

            // Smooth detective walk interpolation to clue / suspect
            if (isWalking)
            {
                walkElapsed += dt;
                float u = walkDuration <= 0.01f ? 1f : Math.Min(1f, walkElapsed / walkDuration);
                float smooth = u * u * (3f - 2f * u);
                detectivePos = Vector2.Lerp(detectiveWalkStart, detectiveWalkTarget, smooth);
                if (u >= 1f)
                {
                    isWalking = false;
                    detectivePos = detectiveWalkTarget;
                    detectiveFacing = detectiveTargetFacing;
                    var cb = onWalkArrived;
                    onWalkArrived = null;
                    cb?.Invoke();
                }
            }
            else if (detectiveActionTimerSec > 0f)
            {
                detectiveActionTimerSec -= dt;
                if (detectiveActionTimerSec <= 0f && game.Phase == DetectivePhase.Investigating)
                    PlayDetectiveClip("detective_idle_think", loop: true);
            }

            if (game.Phase == DetectivePhase.AccusationClimax && !scorecardReady)
            {
                climaxTimerSec -= dt;
                if (climaxTimerSec <= 0f)
                {
                    game.CompleteClimax();
                    ConvoOverlay.Clear();
                    scorecardReady = true;
                    if (!finishFanfarePlayed)
                    {
                        finishFanfarePlayed = true;
                        if (game.SolvedCorrectly)
                            audio.Play(DetectiveSfxId.ShiftVictoryFanfare, 0.72f);
                    }
                }
            }
        }

        public static void PreDrawFrame()
        {
            if (phase != ScenePhase.Active || sceneVm == null || game == null) return;

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

            if (detectiveAvatar != null && !detectiveAvatar.Dead && detectiveAvatar.Avatar != null)
            {
                detectiveAvatar.Animations?.Clear();
                detectiveAvatar.CarryAnimationState = null;
                detectiveAvatar.VisualPositionStart = null;
                detectiveAvatar.VisualPosition = new Vector3(detectivePos.X, detectivePos.Y, 0f);
                detectiveAvatar.RadianDirection = detectiveFacing;
                detectiveClip.UpdateAndApply(detectiveAvatar.Avatar, dt, useSquad: true);
            }

            for (int i = 0; i < 3; i++)
            {
                var av = suspectAvatars[i];
                if (av == null || av.Dead || av.Avatar == null) continue;
                av.Animations?.Clear();
                av.CarryAnimationState = null;
                av.VisualPositionStart = null;
                av.VisualPosition = new Vector3(suspectWorldPos[i].X, suspectWorldPos[i].Y, 0f);
                av.RadianDirection = suspectFacing[i];
                suspectClips[i].UpdateAndApply(av.Avatar, dt, useSquad: true);
            }
        }

        // ---- Interactive Investigation & Accusation Entry Points ----

        public static string BeginInvestigation()
        {
            if (!IsSceneLotLive) return "error scene not active";
            game.BeginInvestigation();
            audio.Play(DetectiveSfxId.CaseBoardOpen, 0.55f);
            return "ok " + StatusSummary();
        }

        public static string TriggerInspectClue(int clueIndex, bool instantWalk = false)
        {
            if (!IsSceneLotLive) return "error scene not active";
            if (game.Phase == DetectivePhase.Briefing) game.BeginInvestigation();
            if (game.Phase != DetectivePhase.Investigating) return "error investigation already finished";
            if (clueIndex < 0 || clueIndex > 2) return "error clueIndex must be 0, 1, or 2";

            activeModal = DetectiveModalMode.None;

            Action arriveAction = () =>
            {
                var res = game.InspectClue(clueIndex);
                if (!res.Accepted) return;

                if (clueIndex == 0)
                {
                    PlayDetectiveClip("detective_inspect_ground", loop: false);
                    detectiveActionTimerSec = 1.9f;
                    cameraFlashAlpha = 0.65f;
                    audio.Play(DetectiveSfxId.ClueCameraFlash, 0.72f);
                }
                else
                {
                    PlayDetectiveClip("detective_take_notes", loop: true);
                    detectiveActionTimerSec = 1.9f;
                    audio.Play(DetectiveSfxId.ClueNotebookScribble, 0.68f);
                }

                if (!res.AlreadyDiscovered)
                    audio.Play(DetectiveSfxId.ClueFoundChime, 0.62f);
                if (res.NewlyExposedContradictions > 0)
                    audio.Play(DetectiveSfxId.ContradictionPing, 0.65f);

                if (detectiveAvatar != null && !string.IsNullOrWhiteSpace(res.DetectiveLine))
                {
                    ConvoOverlay.Show(sceneVm, detectiveAvatar, homeDetectiveName, res.DetectiveLine,
                        null, "", "");
                }
            };

            WalkDetectiveTo(clueInspectSpots[clueIndex], clueInspectFacing[clueIndex], arriveAction, instantWalk);
            return "ok " + StatusSummary();
        }

        public static string TriggerQuestionSuspect(int suspectIndex, bool openBoardAfter = true, bool instantWalk = false)
        {
            if (!IsSceneLotLive) return "error scene not active";
            if (game.Phase == DetectivePhase.Briefing) game.BeginInvestigation();
            if (game.Phase != DetectivePhase.Investigating) return "error investigation already finished";
            if (suspectIndex < 0 || suspectIndex > 2) return "error suspectIndex must be 0, 1, or 2";

            focusedSuspectIndex = suspectIndex;

            Action arriveAction = () =>
            {
                var res = game.QuestionSuspect(suspectIndex);
                if (!res.Accepted) return;

                PlayDetectiveClip("detective_question", loop: true);
                detectiveActionTimerSec = 2.2f;
                audio.Play(DetectiveSfxId.SuspectQuestionCue, 0.66f);

                if (res.VisibleContradictions > 0)
                    audio.Play(DetectiveSfxId.ContradictionPing, 0.68f);

                var susAv = suspectAvatars[suspectIndex];
                if (detectiveAvatar != null && susAv != null)
                {
                    ConvoOverlay.Show(sceneVm, detectiveAvatar, homeDetectiveName, res.DetectiveLine,
                        susAv, res.Suspect.Name, res.SuspectLine);
                }

                if (openBoardAfter)
                    activeModal = DetectiveModalMode.CaseBoard;
            };

            WalkDetectiveTo(suspectApproachSpots[suspectIndex], suspectApproachFacing[suspectIndex], arriveAction, instantWalk);
            return "ok " + StatusSummary();
        }

        public static void ToggleCaseBoard(bool? open = null)
        {
            if (!IsSceneLotLive || game.Phase != DetectivePhase.Investigating) return;
            bool next = open ?? (activeModal != DetectiveModalMode.CaseBoard);
            activeModal = next ? DetectiveModalMode.CaseBoard : DetectiveModalMode.None;
            audio.Play(DetectiveSfxId.CaseBoardOpen, 0.52f);
        }

        public static string TriggerForensicsCrossCheck()
        {
            if (!IsSceneLotLive) return "error scene not active";
            if (game.Phase == DetectivePhase.Briefing) game.BeginInvestigation();
            if (game.Phase != DetectivePhase.Investigating) return "error investigation already finished";

            var res = game.UseForensicsCrossCheck();
            if (!res.Accepted) return "error forensics cross-check unavailable (requires Logic >= 5 and unused charge)";

            PlayDetectiveClip("detective_take_notes", loop: true);
            detectiveActionTimerSec = 1.6f;
            audio.Play(DetectiveSfxId.ForensicsEliminate, 0.72f);

            if (detectiveAvatar != null)
            {
                ConvoOverlay.Clear();
                ConvoOverlay.Show(sceneVm, detectiveAvatar, homeDetectiveName,
                    "Lab report just came in — " + res.EliminatedName + "'s alibi & forensics are 100% clean!",
                    null, "", "");
            }
            return "ok " + StatusSummary();
        }

        public static string TriggerAccuseSuspect(int suspectIndex, bool instantWalk = false)
        {
            if (!IsSceneLotLive) return "error scene not active";
            if (game.Phase == DetectivePhase.Briefing) game.BeginInvestigation();
            if (game.Phase != DetectivePhase.Investigating) return "error case already resolved";
            if (suspectIndex < 0 || suspectIndex > 2) return "error suspectIndex must be 0, 1, or 2";

            activeModal = DetectiveModalMode.None;
            focusedSuspectIndex = suspectIndex;

            Action arriveAction = () =>
            {
                var res = game.AccuseSuspect(suspectIndex);
                if (!res.Accepted) return;

                climaxTimerSec = 2.8f;
                scorecardReady = false;

                PlayDetectiveClip("detective_point_accuse", loop: true);
                audio.Play(DetectiveSfxId.AccuseDramaticHit, 0.78f);

                if (res.CorrectCulprit)
                {
                    PlaySuspectClip(suspectIndex, "detective_culprit_caught", loop: true);
                    audio.Play(DetectiveSfxId.CulpritCaughtBust, 0.76f);
                }
                else
                {
                    audio.Play(DetectiveSfxId.WrongAccusationSting, 0.74f);
                }

                var susAv = suspectAvatars[suspectIndex];
                if (detectiveAvatar != null && susAv != null)
                {
                    ConvoOverlay.Clear();
                    ConvoOverlay.Show(sceneVm, detectiveAvatar, homeDetectiveName, res.DetectiveAccuseLine,
                        susAv, res.AccusedName, res.SuspectReactionLine);
                }
            };

            WalkDetectiveTo(suspectApproachSpots[suspectIndex], suspectApproachFacing[suspectIndex], arriveAction, instantWalk);
            return "ok " + StatusSummary();
        }

        static void WalkDetectiveTo(Vector2 target, float finalFacing, Action onArrived, bool instant)
        {
            float dist = Vector2.Distance(detectivePos, target);
            if (instant || dist < 0.25f)
            {
                isWalking = false;
                detectivePos = target;
                detectiveFacing = finalFacing;
                onWalkArrived = null;
                onArrived?.Invoke();
                return;
            }

            detectiveWalkStart = detectivePos;
            detectiveWalkTarget = target;
            detectiveTargetFacing = finalFacing;
            detectiveFacing = (float)Math.Atan2(target.X - detectivePos.X, -(target.Y - detectivePos.Y));
            walkElapsed = 0f;
            walkDuration = Math.Max(0.35f, Math.Min(1.15f, dist * 0.28f));
            onWalkArrived = onArrived;
            isWalking = true;
            PlayDetectiveClip("patrol_escort_walk_loop", loop: true);
        }

        // ---- Native Simitone Pie Menu Hooks (Clicking Clues & Suspects!) ----

        public static List<VMPieMenuInteraction> TryBuildPieMenu(VMEntity target, VM vm, VMEntity caller)
        {
            if (!IsActiveOn(vm) || target == null || game == null || game.Phase != DetectivePhase.Investigating)
                return null;

            for (int c = 0; c < 3; c++)
            {
                if (clueEntities[c] != null && MatchesEntity(clueEntities[c], target))
                {
                    var clue = game.Clues[c];
                    string verb = clue.Discovered
                        ? "Detective: Review " + clue.Category + " Clue"
                        : "Detective: Inspect " + clue.Category + " (" + clue.StationLabel + ")";
                    return new List<VMPieMenuInteraction>
                    {
                        MakePieEntry(target, (DetectivePieActionKind)c, verb)
                    };
                }
            }

            for (int s = 0; s < 3; s++)
            {
                if (suspectAvatars[s] != null && MatchesEntity(suspectAvatars[s], target))
                {
                    var sus = game.Suspects[s];
                    var list = new List<VMPieMenuInteraction>
                    {
                        MakePieEntry(target, (DetectivePieActionKind)(3 + s),
                            sus.Questioned ? "Detective: Review " + sus.Name + "'s Statement" : "Detective: Question " + sus.Name),
                        MakePieEntry(target, DetectivePieActionKind.OpenCaseBoard, "Detective: Open Case Evidence Board"),
                    };
                    if (!sus.EliminatedByForensics)
                        list.Add(MakePieEntry(target, (DetectivePieActionKind)(7 + s), "Detective: ACCUSE " + sus.Name + " as Culprit!"));
                    return list;
                }
            }

            if ((detectiveAvatar != null && MatchesEntity(detectiveAvatar, target)) || IsCruiserEntity(target))
            {
                return new List<VMPieMenuInteraction>
                {
                    MakePieEntry(target, DetectivePieActionKind.OpenCaseBoard, "Detective: Open Case Evidence Board"),
                    MakePieEntry(target, DetectivePieActionKind.EndShift, "Close Case & Return Home"),
                };
            }

            return null;
        }

        public static bool TryHandleInteraction(VMNetInteractionCmd cmd, VM vm, VMAvatar caller)
        {
            if (cmd == null || cmd.Interaction < DetectivePieActionBase || cmd.Interaction > DetectivePieActionBase + 15)
                return false;
            if (!IsActiveOn(vm) || game == null) return false;

            var kind = (DetectivePieActionKind)(cmd.Interaction - DetectivePieActionBase);
            switch (kind)
            {
                case DetectivePieActionKind.InspectFootprint: TriggerInspectClue(0); return true;
                case DetectivePieActionKind.InspectMotive: TriggerInspectClue(1); return true;
                case DetectivePieActionKind.InspectAlibi: TriggerInspectClue(2); return true;
                case DetectivePieActionKind.QuestionSuspect0: TriggerQuestionSuspect(0); return true;
                case DetectivePieActionKind.QuestionSuspect1: TriggerQuestionSuspect(1); return true;
                case DetectivePieActionKind.QuestionSuspect2: TriggerQuestionSuspect(2); return true;
                case DetectivePieActionKind.OpenCaseBoard: ToggleCaseBoard(true); return true;
                case DetectivePieActionKind.AccuseSuspect0: TriggerAccuseSuspect(0); return true;
                case DetectivePieActionKind.AccuseSuspect1: TriggerAccuseSuspect(1); return true;
                case DetectivePieActionKind.AccuseSuspect2: TriggerAccuseSuspect(2); return true;
                case DetectivePieActionKind.EndShift: RequestReturnHome(game.IsFinished); return true;
            }
            return true;
        }

        const uint OnLotQuestionSimActionId = 9220;

        // Level 6 Detective+ on-lot unlock: adds "Question Sim" to adult-to-adult Pie Menus!
        public static void TryAppendOnLotQuestionPie(List<VMPieMenuInteraction> list, VMEntity target, VM vm, VMEntity caller)
        {
            if (list == null || IsActive || target == null || caller == null || vm == null) return;
            var targetAv = target as VMAvatar;
            var callerAv = caller as VMAvatar;
            if (targetAv == null || callerAv == null || ReferenceEquals(targetAv, callerAv)) return;
            if (targetAv.IsPet || callerAv.IsPet || targetAv.Dead || callerAv.Dead) return;
            if (targetAv.GetPersonData(VMPersonDataVariable.PersonsAge) < 18 ||
                callerAv.GetPersonData(VMPersonDataVariable.PersonsAge) < 18) return;

            var rank = CareerDispatchDriver.ResolveRankForAvatar(callerAv, out _, out _, out _);
            if (rank == null || rank.PromotionLevel < 5) return; // Detective (Level 6, PromotionLevel 5) or higher!

            if (list.Any(e => e != null && string.Equals(e.Name, "Question Sim", StringComparison.OrdinalIgnoreCase)))
                return;

            list.Add(new VMPieMenuInteraction
            {
                Name = "Question Sim",
                ID = unchecked((byte)OnLotQuestionSimActionId),
                Param0 = 0,
                Entry = new TTABInteraction { TTAIndex = OnLotQuestionSimActionId },
                Callee = target,
                Global = false,
                Score = 95,
            });
        }

        public static bool TryHandleOnLotQuestionSim(VMNetInteractionCmd cmd, VM vm, VMAvatar caller)
        {
            if (cmd == null || cmd.Interaction != OnLotQuestionSimActionId || vm == null || caller == null)
                return false;

            var targetAv = vm.GetObjectById(cmd.CalleeID) as VMAvatar;
            if (targetAv == null || targetAv.Dead) return true;

            try
            {
                string detName = !string.IsNullOrWhiteSpace(caller.Name) ? caller.Name.Trim() : "Detective";
                string targetName = !string.IsNullOrWhiteSpace(targetAv.Name) ? targetAv.Name.Trim() : "Neighbor";

                // Read real personality & career intel to record into Sim Memory!
                short nice = targetAv.GetPersonData(VMPersonDataVariable.NicePersonality);
                short outgoing = targetAv.GetPersonData(VMPersonDataVariable.OutgoingPersonality);
                short neat = targetAv.GetPersonData(VMPersonDataVariable.NeatPersonality);
                string traitNote = nice >= 600 ? "warm & cooperative" : nice <= 300 ? "guarded & prickly" : "composed";
                if (outgoing >= 600) traitNote += ", very talkative";
                else if (neat >= 700) traitNote += ", meticulous";

                short curLogic = caller.GetPersonData(VMPersonDataVariable.LogicSkill);
                caller.SetPersonData(VMPersonDataVariable.LogicSkill, (short)Math.Min(1000, curLogic + 15));

                var sp = new RawSim { Name = detName, NeighborId = caller.GetPersonData(VMPersonDataVariable.NeighborId) };
                var ls = new RawSim { Name = targetName, NeighborId = targetAv.GetPersonData(VMPersonDataVariable.NeighborId) };
                Boot.Memory.Record(sp, ls, "question_sim", Tone.Gentle, "question_sim", 2,
                    subjectTitle: "Detective field interview: " + targetName + " seemed " + traitNote + ".");
                Boot.SaveMemoryAsync();

                ConvoOverlay.Show(vm, caller, detName,
                    "Just a few routine detective questions, " + targetName + " — noticed anything unusual nearby?",
                    targetAv, targetName,
                    "Happy to help, Detective! Nothing strange on my watch, promise.");
                Boot.Log("detective-unlock: " + detName + " questioned " + targetName + " (+15 Logic XP, intel logged to Sim Memory)");
            }
            catch (Exception e) { Boot.Log("detective-unlock Question Sim error: " + e.Message); }
            return true;
        }

        // ---- Test Hook Commands (`convo dispatch detective ...`) ----

        public static string HandleTestAction(string rawCmd)
        {
            string trimmed = (rawCmd ?? "").Trim();
            string[] parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";
            string arg1 = parts.Length > 1 ? parts[1].ToLowerInvariant() : "";

            switch (verb)
            {
                case "status":
                    return "ok " + StatusSummary();
                case "begin":
                case "go":
                    return BeginInvestigation();
                case "inspect":
                case "clue":
                {
                    int idx = 0;
                    if (arg1 == "1" || arg1 == "motive") idx = 1;
                    else if (arg1 == "2" || arg1 == "alibi") idx = 2;
                    return TriggerInspectClue(idx, instantWalk: false);
                }
                case "question":
                case "ask":
                {
                    int idx = 0;
                    int.TryParse(arg1, out idx);
                    bool openBoard = parts.Length < 3 || parts[2].ToLowerInvariant() != "noboard";
                    return TriggerQuestionSuspect(Math.Max(0, Math.Min(2, idx)), openBoardAfter: openBoard, instantWalk: false);
                }
                case "board":
                {
                    if (!IsSceneLotLive) return "error scene not active";
                    if (arg1 == "open") ToggleCaseBoard(true);
                    else if (arg1 == "close") ToggleCaseBoard(false);
                    else ToggleCaseBoard(null);
                    return "ok " + StatusSummary();
                }
                case "forensics":
                case "lab":
                    return TriggerForensicsCrossCheck();
                case "accuse":
                {
                    if (!IsSceneLotLive) return "error scene not active";
                    int idx;
                    if (arg1 == "culprit" || arg1 == "correct")
                        idx = game.CulpritIndex;
                    else if (!int.TryParse(arg1, out idx))
                        idx = game.CulpritIndex;
                    return TriggerAccuseSuspect(Math.Max(0, Math.Min(2, idx)), instantWalk: false);
                }
                case "scorecard":
                    if (!IsSceneLotLive || game.Outcome == null) return "error case not accused yet";
                    climaxTimerSec = 0f;
                    game.CompleteClimax();
                    ConvoOverlay.Clear();
                    scorecardReady = true;
                    if (!finishFanfarePlayed)
                    {
                        finishFanfarePlayed = true;
                        if (game.SolvedCorrectly) audio.Play(DetectiveSfxId.ShiftVictoryFanfare, 0.72f);
                    }
                    return "ok " + StatusSummary();
                case "wav":
                {
                    string outPath = parts.Length > 1 ? trimmed.Substring(verb.Length).Trim().Trim('"') : "";
                    if (string.IsNullOrWhiteSpace(outPath)) return "error provide output wav path";
                    audio.ExportRecordedTrackWav(outPath);
                    return "ok wav=" + outPath;
                }
                case "claim":
                    return RequestReturnHome(true);
                case "abort":
                case "close":
                    return RequestReturnHome(false);
                default:
                    return "usage: convo dispatch detective status|begin|inspect <0|1|2>|question <0|1|2>|board [open|close]|forensics|accuse <0|1|2|culprit>|scorecard|wav <path>|claim|abort";
            }
        }

        public static string StatusSummary()
        {
            if (game == null) return "phase=" + phase;
            return "lot=" + targetSceneLotId +
                   " case=" + game.CaseId +
                   " phase=" + game.Phase +
                   " scorecard=" + scorecardReady +
                   " clues=" + game.CluesFoundCount + "/3" +
                   " questioned=" + game.SuspectsQuestionedCount + "/3" +
                   " score=" + game.Score +
                   " culpritIdx=" + game.CulpritIndex +
                   " accusedIdx=" + game.AccusedIndex +
                   " solved=" + game.SolvedCorrectly +
                   " forensics=" + game.ForensicsChargesLeft +
                   " modal=" + activeModal +
                   " walking=" + isWalking +
                   " detAnim=" + lastDetectiveClipName +
                   " suspects=[" + string.Join(",", game.Suspects.Select(s => s.Name)) + "]" +
                   " assets=[" + spawnedAssetsSummary + "]";
        }

        public static string RequestReturnHome(bool applyRewards)
        {
            if (phase == ScenePhase.Inactive) return "error not active";

            activeModal = DetectiveModalMode.None;
            CrimeSceneDeductionOverlay.Close();
            ConvoOverlay.Clear();
            audio.StopAmbience();

            if (applyRewards && game != null)
            {
                pendingReturnOutcome = game.Outcome ?? game.ForceAbortAndFinish();
            }
            else
            {
                pendingReturnOutcome = null;
            }

            var screen = UIScreen.Current;
            if (TriggerScreenSwitchLot(screen, homeHouseNumber))
            {
                phase = ScenePhase.WaitingForHomeLot;
                return "ok returning home house=" + homeHouseNumber;
            }

            ForceReset();
            return "error failed switching back to home house=" + homeHouseNumber;
        }

        // ---- Manor Parlor Crime Scene Construction (100% REAL TS1 ASSETS!) ----

        static void SetupCrimeSceneLot(VM vm)
        {
            try
            {
                sceneVm = vm;
                sceneProps.Clear();
                Array.Clear(clueEntities, 0, clueEntities.Length);
                Array.Clear(suspectAvatars, 0, suspectAvatars.Length);
                cruiserGroup = null;
                cruiserLead = null;
                detectiveAvatar = null;
                finishFanfarePlayed = false;
                scorecardReady = false;
                climaxTimerSec = 0f;
                activeModal = DetectiveModalMode.None;
                focusedSuspectIndex = -1;
                isWalking = false;
                onWalkArrived = null;
                frameClock.Reset();
                lastFrameSec = -1.0;
                animClock.Reset();
                lastAnimSec = -1.0;

                var content = Content.Get();
                vm.SpeedMultiplier = 1;
                alertSweepRemainingSec = 4.5f;
                ConvoOverlay.Clear();
                DismissBlockingAlerts(vm);

                if (vm.Context?.Clock != null)
                {
                    vm.Context.Clock.Hours = 18;
                    vm.Context.Clock.Minutes = 45;
                }

                // 1. Park real TS1 Police Squad Car outside at the curb
                Direction cruiserDir;
                LotTilePos curbStandTile;
                FindStreetCurbPlacement(vm, out cruiserTile, out curbStandTile, out cruiserDir);
                cruiserGroup = TrySpawnObjectNear(vm, GuidPoliceCruiser, cruiserTile, cruiserDir, 4);
                if (cruiserGroup != null && cruiserGroup.Objects != null && cruiserGroup.Objects.Count > 0)
                {
                    cruiserLead = cruiserGroup.BaseObject ?? cruiserGroup.Objects[0];
                    cruiserTile = cruiserLead.Position;
                    FreezeGroup(cruiserGroup, pauseThread: true);
                }

                // 2. Build spacious 9x8 Manor Parlor Crime Scene (cutaway North+West walls, wood floor)
                int roomMinX, roomMinY;
                PickSceneFootprint(vm, cruiserTile, out roomMinX, out roomMinY);
                int roomW = 9;
                int roomH = 8;
                ClearAreaObjects(vm, roomMinX - 1, roomMinY - 1, roomMinX + roomW + 1, roomMinY + roomH + 2);

                ushort parlorFloor = ResolveFloorPattern(content, isWalkway: false);
                ushort walkFloor = ResolveFloorPattern(content, isWalkway: true);
                ushort wallPat = ResolveWallPattern(content);
                BuildParlorArchitecture(vm, roomMinX, roomMinY, roomW, roomH, cruiserTile, parlorFloor, walkFloor, wallPat);

                // 3. Furnish with 100% REAL TS1 catalog objects!
                var spawnedLog = new List<string>();

                // Station 0 (Footprint): Display Pedestal / Bust / Sculpture on open parlor floor near doorway
                uint footprintPropGuid = FindCatalogObjectGuid(content,
                    new[] { "bust", "vase", "urn", "statue", "sculpture", "pedestal", "table - end" },
                    new[] { "sundial", "lawn", "flamingo", "gnome", "garden", "fountain", "wall", "painting", "poster", "magic", "tomb", "grave", "left", "right", "part b" },
                    preferSingleTile: true);

                // Station 1 (Motive): Real 2-tile Executive Study Desk (escrivaninha) + slotted Table Lamp
                uint deskGuid = FindCatalogObjectGuid(content,
                    new[] { "desk - antique", "desk - executive", "desk - office", "desk - modern", "desk -", "writing desk", "desk" },
                    new[] { "lamp", "chair", "phone", "computer", "pc", "school", "kid", "child", "magic", "left", "right", "part b" },
                    preferSingleTile: false);
                uint tableLampGuid = FindCatalogObjectGuid(content,
                    new[] { "lamp - table", "table lamp", "lamp - desk", "banker", "lamp" },
                    new[] { "garden", "yard", "lawn", "tiki", "torch", "lantern", "patio", "outdoor", "floor", "wall", "street", "sconce", "hanging", "chandelier", "lava", "post", "path", "pool", "tomb", "magic", "left", "right" },
                    preferSingleTile: true);

                // Station 2 (Alibi): Grandfather Clock / Parlor Timepiece (fallback Stereo)
                uint clockGuid = FindCatalogObjectGuid(content,
                    new[] { "grandfather", "clock - grand", "clock - floor", "clock", "stereo -", "jukebox" },
                    new[] { "alarm", "wall", "table", "radio", "left", "right", "part b", "magic" },
                    preferSingleTile: true);

                // Parlor Dressing: Study Chair, Bookshelf, Floor Lamps, Plants
                uint chairGuid = FindCatalogObjectGuid(content,
                    new[] { "chair - office", "chair - dining", "chair - living", "armchair", "chair" },
                    new[] { "recliner", "beach", "lawn", "bar", "stool", "sofa", "couch", "loveseat", "high", "electric", "massage", "throne", "western", "left", "right", "desk", "table" },
                    preferSingleTile: true);
                uint bookshelfGuid = FindCatalogObjectGuid(content,
                    new[] { "bookcase", "bookshelf", "pine bookcase", "amish" },
                    new[] { "magic", "secret", "door", "left", "right", "part b" },
                    preferSingleTile: true);
                uint floorLampGuid = FindCatalogObjectGuid(content,
                    new[] { "lamp - floor", "floor lamp", "torchere" },
                    new[] { "garden", "yard", "lawn", "tiki", "patio", "outdoor", "street", "post", "table", "wall", "magic", "left", "right" },
                    preferSingleTile: true);
                uint plantGuid = FindCatalogObjectGuid(content,
                    new[] { "plant - floor", "ficus", "palm", "fern", "plant" },
                    new[] { "shrub", "hedge", "tree", "flower", "garden", "hanging", "wall", "cactus", "table", "left", "right" },
                    preferSingleTile: true);

                // Spawn Clue 0: Footprint Pedestal/Sculpture at (minX+2, minY+5)
                int fpX = roomMinX + 2, fpY = roomMinY + 5;
                if (footprintPropGuid != 0)
                {
                    clueEntities[0] = SpawnSceneProp(vm, footprintPropGuid, fpX, fpY, Direction.SOUTH);
                    if (clueEntities[0] != null) spawnedLog.Add("Clue0_Footprint=" + SafeObjName(clueEntities[0]));
                }
                clueInspectSpots[0] = new Vector2(fpX + 0.15f, fpY - 1.15f);
                clueInspectFacing[0] = (float)(Math.PI * 1.15);

                // Spawn Clue 1: Study Desk (Motive) at (minX+2, minY+2) + Slotted Lamp!
                int deskX = roomMinX + 2, deskY = roomMinY + 2;
                VMMultitileGroup deskGrp = null;
                if (deskGuid != 0)
                {
                    var dPos = new LotTilePos((short)(deskX * 16), (short)(deskY * 16), 1);
                    deskGrp = vm.Context.CreateObjectInstance(deskGuid, dPos, Direction.SOUTH);
                    if (deskGrp != null && deskGrp.Objects != null && deskGrp.Objects.Count > 0)
                    {
                        for (int i = 0; i < deskGrp.Objects.Count; i++)
                            sceneProps.Add(deskGrp.Objects[i]);
                        clueEntities[1] = deskGrp.BaseObject ?? deskGrp.Objects[0];
                        spawnedLog.Add("Clue1_Motive=" + SafeObjName(clueEntities[1]));
                    }
                }
                if (tableLampGuid != 0 && clueEntities[1] != null)
                {
                    string lampMode;
                    var lamp = SpawnOnSurface(vm, tableLampGuid, clueEntities[1], Direction.SOUTH, out lampMode);
                    if (lamp != null)
                    {
                        TryTurnLampOn(lamp);
                        spawnedLog.Add("DeskLamp=" + SafeObjName(lamp) + "(" + lampMode + ")");
                    }
                }
                if (deskGrp != null) FreezeGroup(deskGrp, pauseThread: true);
                clueInspectSpots[1] = new Vector2(deskX - 0.10f, deskY + 0.72f);
                clueInspectFacing[1] = 0f; // Facing NORTH toward desk

                // Spawn Clue 2: Grandfather Clock / Timepiece (Alibi) at (minX+5, minY+1)
                int clkX = roomMinX + 5, clkY = roomMinY + 1;
                if (clockGuid != 0)
                {
                    clueEntities[2] = SpawnSceneProp(vm, clockGuid, clkX, clkY, Direction.SOUTH);
                    if (clueEntities[2] != null) spawnedLog.Add("Clue2_Alibi=" + SafeObjName(clueEntities[2]));
                }
                clueInspectSpots[2] = new Vector2(clkX - 0.45f, clkY + 0.65f);
                clueInspectFacing[2] = 0f; // Facing NORTH toward clock

                // Study Chair neatly behind desk, Bookshelf along West wall, Floor Lamps & Plants
                if (chairGuid != 0)
                {
                    var ch = SpawnSceneProp(vm, chairGuid, deskX, deskY - 1, Direction.SOUTH);
                    if (ch != null) spawnedLog.Add("Chair=" + SafeObjName(ch));
                }
                if (bookshelfGuid != 0)
                {
                    var bs = SpawnSceneProp(vm, bookshelfGuid, roomMinX + 1, roomMinY + 3, Direction.EAST);
                    if (bs != null) spawnedLog.Add("Bookcase=" + SafeObjName(bs));
                }
                if (floorLampGuid != 0)
                {
                    var fl1 = SpawnSceneProp(vm, floorLampGuid, roomMinX + 1, roomMinY + 1, Direction.SOUTH);
                    var fl2 = SpawnSceneProp(vm, floorLampGuid, roomMinX + 7, roomMinY + 1, Direction.SOUTH);
                    if (fl1 != null) { TryTurnLampOn(fl1); spawnedLog.Add("Lamp=" + SafeObjName(fl1)); }
                    if (fl2 != null) TryTurnLampOn(fl2);
                }
                if (plantGuid != 0)
                {
                    var p1 = SpawnSceneProp(vm, plantGuid, roomMinX + 1, roomMinY + roomH - 2, Direction.SOUTH);
                    var p2 = SpawnSceneProp(vm, plantGuid, roomMinX + 7, roomMinY + roomH - 2, Direction.SOUTH);
                    if (p1 != null || p2 != null) spawnedLog.Add("Plants=" + SafeObjName(p1 ?? p2));
                }

                spawnedAssetsSummary = string.Join(", ", spawnedLog);

                // 4. Pick 3 DISTINCT verified fully-textured real neighborhood Sims as parlor Suspects!
                var verifiedSims = CollectVerifiedNeighborhoodSims(content);
                var chosenNames = new string[3];
                var chosenGuids = new uint[3];
                string[] fallbackNames = { "Mortimer", "Bella", "Betty" };
                for (int i = 0; i < 3; i++)
                {
                    if (verifiedSims.Count > i)
                    {
                        var info = verifiedSims[(Math.Abs(caseSeed) + i) % verifiedSims.Count];
                        chosenNames[i] = info.Name;
                        chosenGuids[i] = info.OutfitGuid;
                        suspectNeighborIds[i] = info.NeighborId;
                    }
                    else
                    {
                        chosenNames[i] = fallbackNames[i];
                        chosenGuids[i] = GuidFallbackPerson;
                        suspectNeighborIds[i] = 0;
                    }
                }

                // 5. Spawn Detective in parlor center + 3 Suspects in clean semi-circle parlor lineup!
                int detTileX = roomMinX + 4, detTileY = roomMinY + 4;
                detectiveHomePos = new Vector2(detTileX - 0.50f, detTileY - 0.45f);
                detectivePos = detectiveHomePos;
                detectiveFacing = (float)(Math.PI * 0.45);
                detectiveAvatar = SpawnDetectiveAvatar(vm, new LotTilePos((short)(detTileX * 16), (short)(detTileY * 16), 1));
                if (detectiveAvatar != null)
                {
                    FreezeEntity(detectiveAvatar, pauseThread: true);
                    detectiveAvatar.VisualPosition = new Vector3(detectivePos.X, detectivePos.Y, 0f);
                    detectiveAvatar.RadianDirection = detectiveFacing;
                    PlayDetectiveClip("detective_idle_think", loop: true);
                    SelectActiveAvatar(detectiveAvatar);
                }

                int[] susTx = { roomMinX + 7, roomMinX + 7, roomMinX + 5 };
                int[] susTy = { roomMinY + 2, roomMinY + 5, roomMinY + 6 };
                float[] susDir = { (float)(Math.PI * 1.40), (float)(Math.PI * 1.55), (float)(Math.PI * 1.85) };

                for (int i = 0; i < 3; i++)
                {
                    var tile = new LotTilePos((short)(susTx[i] * 16), (short)(susTy[i] * 16), 1);
                    suspectWorldPos[i] = new Vector2(susTx[i] - 0.50f, susTy[i] - 0.50f);
                    suspectFacing[i] = susDir[i];
                    suspectApproachSpots[i] = new Vector2(suspectWorldPos[i].X - 1.15f, suspectWorldPos[i].Y - (i == 2 ? 0.55f : 0.05f));
                    suspectApproachFacing[i] = (float)Math.Atan2(suspectWorldPos[i].X - suspectApproachSpots[i].X,
                                                                 -(suspectWorldPos[i].Y - suspectApproachSpots[i].Y));

                    suspectAvatars[i] = SpawnVerifiedSim(vm, chosenGuids[i], chosenNames[i], tile, verifiedSims);
                    if (suspectAvatars[i] != null)
                    {
                        FreezeEntity(suspectAvatars[i], pauseThread: true);
                        suspectAvatars[i].VisualPosition = new Vector3(suspectWorldPos[i].X, suspectWorldPos[i].Y, 0f);
                        suspectAvatars[i].RadianDirection = suspectFacing[i];
                        PlaySuspectClip(i, "detective_suspect_nervous", loop: true);
                        // Stagger idle phase slightly so suspects look naturally independent
                        suspectClips[i].UpdateAndApply(suspectAvatars[i].Avatar, 0.35f * i, useSquad: true);
                        chosenNames[i] = suspectAvatars[i].Name;
                    }
                }

                // 6. Initialize pure-C# CrimeSceneDeductionGame
                game = new CrimeSceneDeductionGame(
                    detectiveName: homeDetectiveName,
                    logicSkill: homeLogicSkill,
                    charismaSkill: homeCharismaSkill,
                    suspectNames: chosenNames,
                    seed: caseSeed);

                // 7. Frame camera cleanly on the Manor Parlor (all 3 Clues + Detective + all 3 Suspects visible in 3D & 2D!)
                FocusCameraOnWorldPoint(new Vector2(roomMinX + 4.10f, roomMinY + 3.45f), WorldZoom.Near, zoom3D: 6.4f);

                // 8. Start noir forensics audio & overlay
                phase = ScenePhase.Active;
                audio.EnsureStarted();
                audio.Play(DetectiveSfxId.CaseBoardOpen, 0.45f);
                CrimeSceneDeductionOverlay.OpenForRealLot();
                DismissBlockingAlerts(vm);

                Boot.Log("crime-scene: REAL-ASSET Manor Parlor ready! lot=" + targetSceneLotId +
                         " case=" + game.CaseTitle +
                         " culprit=" + game.Suspects[game.CulpritIndex].Name +
                         " assets=[" + spawnedAssetsSummary + "]");
            }
            catch (Exception e)
            {
                Boot.Log("crime-scene SetupCrimeSceneLot error: " + e.GetType().Name + ": " + e.Message);
                ForceReset();
            }
        }

        static void PlayDetectiveClip(string clipName, bool loop)
        {
            detectiveClip.Play(clipName, 0.14f, 1.0f, loop, true);
            lastDetectiveClipName = clipName;
        }

        static void PlaySuspectClip(int index, string clipName, bool loop)
        {
            if (index < 0 || index > 2) return;
            suspectClips[index].Play(clipName, 0.14f, 1.0f, loop, true);
            lastSuspectClipNames[index] = clipName;
        }

        // ---- Return Home & Real VM Rewards Persistence ----

        static void CompleteReturnHome(VM homeVm)
        {
            try
            {
                if (pendingReturnOutcome != null)
                {
                    var o = pendingReturnOutcome;
                    VMAvatar homeAv = null;
                    if (homeVm.Entities != null)
                    {
                        homeAv = homeVm.Entities.OfType<VMAvatar>()
                            .FirstOrDefault(a => a != null && !a.Dead &&
                                ((homeDetectiveNeighborId > 0 && a.GetPersonData(VMPersonDataVariable.NeighborId) == homeDetectiveNeighborId) ||
                                 string.Equals((a.Name ?? "").Trim(), homeDetectiveName.Trim(), StringComparison.OrdinalIgnoreCase)));
                    }

                    string rewardSummary = CareerDispatchDriver.ApplyReward(homeAv, o.Reward);

                    try
                    {
                        var nb = Content.Get()?.Neighborhood;
                        var detNb = homeDetectiveNeighborId > 0 ? nb?.GetNeighborByID(homeDetectiveNeighborId) : null;
                        for (int i = 0; i < 3; i++)
                        {
                            if (suspectNeighborIds[i] <= 0) continue;
                            short relDelta = 0;
                            if (i == o.AccusedIndex) relDelta = o.SolvedCorrectly ? (short)-12 : (short)-15;
                            else if (o.SolvedCorrectly) relDelta = (short)+6;
                            ApplyNeighborDailyDelta(detNb, suspectNeighborIds[i], relDelta);
                        }

                        if (!string.IsNullOrWhiteSpace(o.TrueCulpritName))
                        {
                            var sp = new RawSim { Name = homeDetectiveName, NeighborId = homeDetectiveNeighborId };
                            short targetId = (o.CulpritIndex >= 0 && o.CulpritIndex < 3) ? suspectNeighborIds[o.CulpritIndex] : (short)0;
                            var ls = new RawSim { Name = o.TrueCulpritName, NeighborId = targetId };
                            Boot.Memory.Record(sp, ls, "crime_scene_deduction",
                                o.SolvedCorrectly ? Tone.Bold : Tone.Gentle,
                                "crime_scene_deduction", o.SolvedCorrectly ? 3 : 1,
                                subjectTitle: o.Reward.MemoryFact);
                            Boot.SaveMemoryAsync();
                        }
                    }
                    catch (Exception re) { Boot.Log("crime-scene rel save error: " + re.Message); }

                    Boot.Log("crime-scene: returned home! grade=" + o.Grade +
                             " score=" + o.FinalScore +
                             " rewards=[" + rewardSummary + "]");
                }
            }
            catch (Exception e)
            {
                Boot.Log("crime-scene CompleteReturnHome error: " + e.Message);
            }
            finally
            {
                ForceReset();
            }
        }

        static void ApplyNeighborDailyDelta(Neighbour detNb, short targetId, short delta)
        {
            if (detNb == null || detNb.Relationships == null || targetId <= 0 || delta == 0) return;
            List<short> v;
            if (!detNb.Relationships.TryGetValue(targetId, out v) || v == null)
            {
                v = new List<short> { 0, 0, 0, 0 };
                detNb.Relationships[targetId] = v;
            }
            while (v.Count < 4) v.Add(0);
            v[0] = (short)Math.Max(-100, Math.Min(100, v[0] + delta));
            if (v[0] >= 70) v[1] = 1;
        }

        static void ForceReset()
        {
            phase = ScenePhase.Inactive;
            sourceVm = null;
            sceneVm = null;
            game = null;
            pendingReturnOutcome = null;
            activeModal = DetectiveModalMode.None;
            focusedSuspectIndex = -1;
            isWalking = false;
            onWalkArrived = null;
            sceneProps.Clear();
            Array.Clear(clueEntities, 0, clueEntities.Length);
            Array.Clear(suspectAvatars, 0, suspectAvatars.Length);
            detectiveAvatar = null;
            cruiserGroup = null;
            cruiserLead = null;
            scorecardReady = false;
            finishFanfarePlayed = false;
            audio.StopAmbience();
            CrimeSceneDeductionOverlay.Close();
        }

        // ---- Verified Textured Avatar & Real TS1 Object Helpers ----

        static VMAvatar SpawnDetectiveAvatar(VM vm, LotTilePos tile)
        {
            uint guid = homeDetectiveGuid != 0 ? homeDetectiveGuid : GuidFallbackPerson;
            var av = TrySpawnTexturedAvatar(vm, guid, tile);
            if (av == null && guid != GuidFallbackPerson)
                av = TrySpawnTexturedAvatar(vm, GuidFallbackPerson, tile);
            if (av == null) return null;

            av.Name = homeDetectiveName;
            try
            {
                if (av.Avatar != null)
                {
                    if (homeDetectiveHead != null) av.Avatar.Head = homeDetectiveHead;
                    if (homeDetectiveBody != null)
                    {
                        av.Avatar.Body = homeDetectiveBody;
                        av.Avatar.Handgroup = homeDetectiveBody;
                    }
                }
            }
            catch { }
            return av;
        }

        static VMAvatar SpawnVerifiedSim(VM vm, uint preferredGuid, string displayName, LotTilePos tile, List<VerifiedNeighborInfo> fallbacks)
        {
            VMAvatar av = preferredGuid != 0 ? TrySpawnTexturedAvatar(vm, preferredGuid, tile) : null;
            if (av == null || av.IsPet || !AvatarHasLoadedTextures(av))
            {
                if (av != null) { try { av.Delete(true, vm.Context); } catch { } av = null; }
                for (int i = 0; i < fallbacks.Count; i++)
                {
                    if (fallbacks[i].OutfitGuid == preferredGuid) continue;
                    var cand = TrySpawnTexturedAvatar(vm, fallbacks[i].OutfitGuid, tile);
                    if (cand != null && !cand.IsPet && AvatarHasLoadedTextures(cand))
                    {
                        av = cand;
                        if (string.IsNullOrWhiteSpace(displayName)) displayName = fallbacks[i].Name;
                        break;
                    }
                    if (cand != null) { try { cand.Delete(true, vm.Context); } catch { } }
                }
            }
            if (av == null) av = TrySpawnTexturedAvatar(vm, GuidFallbackPerson, tile);
            if (av != null && !string.IsNullOrWhiteSpace(displayName)) av.Name = displayName;
            return av;
        }

        static VMAvatar TrySpawnTexturedAvatar(VM vm, uint guid, LotTilePos tile)
        {
            try
            {
                var grp = vm.Context.CreateObjectInstance(guid, tile, Direction.NORTH);
                var av = grp?.BaseObject as VMAvatar ?? grp?.Objects?.OfType<VMAvatar>().FirstOrDefault();
                if (av == null) return null;
                if (av.Position == LotTilePos.OUT_OF_WORLD)
                    av.SetPosition(tile, Direction.NORTH, vm.Context, VMPlaceRequestFlags.AcceptSlots);
                if (av.Avatar != null && av.Avatar.Handgroup == null && av.Avatar.Body != null)
                    av.Avatar.Handgroup = av.Avatar.Body;
                return av;
            }
            catch { return null; }
        }

        static bool AvatarHasLoadedTextures(VMAvatar av)
        {
            if (av == null || av.Avatar == null || av.IsPet) return false;
            var content = Content.Get();
            if (content == null) return true;
            var head = av.Avatar.Head;
            var body = av.Avatar.Body;
            if (head == null || body == null) return false;
            return AppearanceAndTextureExist(content, head.TS1AppearanceID, head.TS1TextureID)
                && AppearanceAndTextureExist(content, body.TS1AppearanceID, body.TS1TextureID);
        }

        static bool VerifyNeighborHasTextures(Content content, uint guid)
        {
            try
            {
                var obj = content?.WorldObjects?.Get(guid);
                if (obj?.Resource == null || obj.OBJ == null) return false;
                if (obj.OBJ.ObjectType != OBJDType.Person) return false;
                ushort bodyStrId = obj.OBJ.BodyStringID != 0 ? obj.OBJ.BodyStringID : (ushort)200;
                var str = obj.Resource.Get<STR>(bodyStrId) ?? obj.Resource.Get<STR>(200);
                if (str == null || str.Length < 3) return false;
                string bodySpec = str.GetString(1);
                string headSpec = str.GetString(2);
                if (string.IsNullOrWhiteSpace(bodySpec) || string.IsNullOrWhiteSpace(headSpec)) return false;

                string combined = (bodySpec + " " + headSpec).ToLowerInvariant();
                if (combined.Contains("dog") || combined.Contains("cat") || combined.Contains("pet")
                    || combined.Contains("puppy") || combined.Contains("kitten")
                    || combined.Contains("chd") || combined.Contains("child"))
                    return false;

                string bodyLow = bodySpec.ToLowerInvariant();
                if (!bodyLow.Contains("ma") && !bodyLow.Contains("fa"))
                    return false;

                var bodyRef = new VMOutfitReference(bodySpec, false);
                var headRef = new VMOutfitReference(headSpec, false);
                var bOft = bodyRef.OftData;
                var hOft = headRef.OftData;
                if (bOft == null || hOft == null) return false;
                return AppearanceAndTextureExist(content, bOft.TS1AppearanceID, bOft.TS1TextureID)
                    && AppearanceAndTextureExist(content, hOft.TS1AppearanceID, hOft.TS1TextureID);
            }
            catch { return false; }
        }

        static bool AppearanceAndTextureExist(Content content, string aprId, string texId)
        {
            if (content == null || string.IsNullOrWhiteSpace(aprId)) return false;
            string aprLower = aprId.Trim().ToLowerInvariant();
            if (!aprLower.EndsWith(".apr", StringComparison.Ordinal)) aprLower += ".apr";
            var apr = content.AvatarAppearances?.Get(aprId) ?? content.AvatarAppearances?.Get(aprLower);
            if (apr == null) return false;
            if (!string.IsNullOrWhiteSpace(texId))
                return TextureExists(content, texId);
            return apr.Bindings != null && apr.Bindings.Length > 0;
        }

        static bool TextureExists(Content content, string texId)
        {
            if (content?.AvatarTextures == null || string.IsNullOrWhiteSpace(texId)) return false;
            string raw = texId.Trim();
            string low = raw.ToLowerInvariant();
            string stem = low.EndsWith(".bmp", StringComparison.Ordinal) ? low.Substring(0, low.Length - 4) : low;
            return content.AvatarTextures.Get(stem + ".bmp") != null
                || content.AvatarTextures.Get(stem) != null
                || content.AvatarTextures.Get(raw + ".bmp") != null
                || content.AvatarTextures.Get(raw) != null;
        }

        static List<VerifiedNeighborInfo> CollectVerifiedNeighborhoodSims(Content content)
        {
            var list = new List<VerifiedNeighborInfo>();
            try
            {
                var entries = content?.Neighborhood?.Neighbors?.Entries;
                if (entries == null) return list;
                var residents = new List<VerifiedNeighborInfo>();
                var townies = new List<VerifiedNeighborInfo>();

                for (int i = 0; i < entries.Count; i++)
                {
                    var n = entries[i];
                    if (n == null || n.NeighbourID <= 0 || n.GUID == 0) continue;
                    if (homeDetectiveNeighborId > 0 && n.NeighbourID == homeDetectiveNeighborId) continue;
                    if (homeDetectiveGuid != 0 && n.GUID == homeDetectiveGuid) continue;

                    if (n.PersonData == null || n.PersonData.Length <= (int)VMPersonDataVariable.PersonsAge)
                        continue;
                    short age = n.PersonData[(int)VMPersonDataVariable.PersonsAge];
                    if (age < 18) continue;

                    string name = ResolveNeighborDisplayName(content, n);
                    if (string.IsNullOrWhiteSpace(name) || IsServiceNpcName(name)) continue;
                    if (string.Equals(name.Trim(), homeDetectiveName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!VerifyNeighborHasTextures(content, n.GUID)) continue;

                    var info = new VerifiedNeighborInfo
                    {
                        Name = name.Trim(),
                        NeighborId = n.NeighbourID,
                        OutfitGuid = n.GUID,
                    };
                    bool isClassicResident = n.NeighbourID <= 24
                        && !string.IsNullOrEmpty(n.Name)
                        && n.Name.StartsWith("User", StringComparison.OrdinalIgnoreCase);
                    if (isClassicResident) residents.Add(info);
                    else townies.Add(info);
                }

                if (residents.Count >= 3)
                {
                    list.AddRange(residents);
                    return list;
                }
                list.AddRange(residents);
                list.AddRange(townies);
            }
            catch { }
            return list;
        }

        static string ResolveNeighborDisplayName(Content content, Neighbour n)
        {
            try
            {
                var obj = content?.WorldObjects?.Get(n.GUID);
                var ctss = obj?.Resource?.Get<CTSS>(obj.OBJ.CatalogStringsID);
                string s = ctss?.GetString(0);
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                if (!string.IsNullOrWhiteSpace(obj?.OBJ?.ChunkLabel)) return obj.OBJ.ChunkLabel.Trim();
            }
            catch { }
            return n?.Name ?? "";
        }

        static bool IsServiceNpcName(string name)
        {
            string low = (name ?? "").ToLowerInvariant();
            string[] skip = { "maid", "gardener", "repair", "pizza", "burglar", "cop", "police", "fire", "grim", "reaper", "santa", "clown", "bear", "raccoon", "skunk", "stray", "cat", "dog", "pet", "servo", "genie", "carpool", "bus", "taxi", "npc", "controller", "portal", "social", "clerk", "vendor", "waiter", "waitress", "bartender", "chef", "cook", "cashier", "janitor", "dj", "host", "bellhop", "butler", "driver", "musician", "sax", "singer", "mime", "magician", "director", "paparazzi", "fan", "bot", "townie", "carnie", "rental", "shack", "ranger", "tourist", "resort", "concierge", "guide", "mascot", "vampire", "skeleton", "bonehilda", "yeti", "shark", "penguin", "mayor", "travelin", "danish", "dancer", "backup", "performer", "barista", "judge", "guard" };
            for (int i = 0; i < skip.Length; i++)
                if (low.Contains(skip[i])) return true;
            return false;
        }

        static uint FindCatalogObjectGuid(Content content, string[] includeTerms, string[] excludeTerms, bool preferSingleTile)
        {
            try
            {
                var cat = content?.WorldCatalog?.All();
                if (cat == null) return 0;

                uint bestGuid = 0;
                int bestScore = int.MinValue;

                for (int i = 0; i < cat.Count; i++)
                {
                    var item = cat[i];
                    if (item.GUID == 0) continue;
                    string combined = ((item.CatalogName ?? "") + " " + (item.Name ?? "")).ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(combined)) continue;

                    bool excluded = false;
                    if (excludeTerms != null)
                    {
                        for (int e = 0; e < excludeTerms.Length; e++)
                            if (combined.Contains(excludeTerms[e])) { excluded = true; break; }
                    }
                    if (excluded) continue;

                    int matchRank = -1;
                    for (int k = 0; k < includeTerms.Length; k++)
                    {
                        if (combined.Contains(includeTerms[k]))
                        {
                            matchRank = includeTerms.Length - k;
                            break;
                        }
                    }
                    if (matchRank < 0) continue;

                    var obj = content.WorldObjects?.Get(item.GUID);
                    if (obj == null || obj.OBJ == null) continue;
                    if (obj.OBJ.ObjectType == OBJDType.Person || obj.OBJ.ObjectType == OBJDType.SimType) continue;
                    if (obj.OBJ.SubIndex > 0) continue;
                    if (preferSingleTile && obj.OBJ.MasterID != 0) continue;

                    string chunkLow = (obj.OBJ.ChunkLabel ?? "").ToLowerInvariant();
                    if (excludeTerms != null && !string.IsNullOrEmpty(chunkLow))
                    {
                        for (int e = 0; e < excludeTerms.Length; e++)
                            if (chunkLow.Contains(excludeTerms[e])) { excluded = true; break; }
                    }
                    if (excluded) continue;

                    int score = matchRank * 100;
                    if (preferSingleTile && obj.OBJ.MasterID == 0) score += 40;
                    if (item.Price > 0 && item.Price <= 2800) score += 20;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGuid = item.GUID;
                    }
                }
                return bestGuid;
            }
            catch { return 0; }
        }

        static VMEntity SpawnSceneProp(VM vm, uint guid, int tx, int ty, Direction dir)
        {
            if (vm == null || guid == 0) return null;
            try
            {
                var pos = new LotTilePos((short)(tx * 16), (short)(ty * 16), 1);
                var grp = vm.Context.CreateObjectInstance(guid, pos, dir);
                if (grp == null || grp.Objects == null || grp.Objects.Count == 0) return null;
                var lead = grp.BaseObject ?? grp.Objects[0];
                if (lead.Position == LotTilePos.OUT_OF_WORLD)
                    lead.SetPosition(pos, dir, vm.Context, VMPlaceRequestFlags.AcceptSlots);
                FreezeGroup(grp, pauseThread: true);
                for (int i = 0; i < grp.Objects.Count; i++)
                    sceneProps.Add(grp.Objects[i]);
                return lead;
            }
            catch { return null; }
        }

        static VMEntity SpawnOnSurface(VM vm, uint guid, VMEntity surface, Direction dir, out string mode)
        {
            mode = "failed";
            if (vm == null || guid == 0 || surface == null || surface.Position == LotTilePos.OUT_OF_WORLD)
                return null;
            try
            {
                var surfPos = surface.Position;
                var grp = vm.Context.CreateObjectInstance(guid, surfPos, dir);
                if (grp == null || grp.Objects == null || grp.Objects.Count == 0) return null;
                var lead = grp.BaseObject ?? grp.Objects[0];

                if (lead.Container != null && lead.Position != LotTilePos.OUT_OF_WORLD)
                {
                    mode = "buy-slot" + lead.ContainerSlot;
                }
                else
                {
                    var res = lead.SetPosition(surfPos, dir, vm.Context, VMPlaceRequestFlags.AcceptSlots);
                    if (lead.Container != null && lead.Position != LotTilePos.OUT_OF_WORLD)
                    {
                        mode = "accept-slot" + lead.ContainerSlot;
                    }
                    else
                    {
                        bool placed = false;
                        try { placed = surface.PlaceInSlot(lead, 0, true, vm.Context); } catch { }
                        if (placed)
                        {
                            try
                            {
                                lead.Direction = dir;
                                if (lead.WorldUI != null && surface.WorldUI != null)
                                {
                                    lead.WorldUI.Level = surface.WorldUI.Level;
                                    lead.WorldUI.Container = surface.WorldUI;
                                    lead.WorldUI.ContainerSlot = lead.ContainerSlot;
                                    lead.WorldUI.Direction = dir;
                                }
                            }
                            catch { }
                            mode = "manual-slot" + lead.ContainerSlot;
                        }
                        else mode = "err:" + res.Status;
                    }
                }

                FreezeGroup(grp, pauseThread: true);
                for (int i = 0; i < grp.Objects.Count; i++)
                    sceneProps.Add(grp.Objects[i]);
                return lead;
            }
            catch { return null; }
        }

        static void TryTurnLampOn(VMEntity lamp)
        {
            try { if (lamp != null) lamp.SetValue(VMStackObjectVariable.LightingContribution, 100); } catch { }
        }

        static void BuildParlorArchitecture(
            VM vm, int minX, int minY, int roomW, int roomH, LotTilePos curbTile,
            ushort parlorFloor, ushort walkFloor, ushort wallPat)
        {
            try
            {
                var arch = vm.Context.Architecture;
                if (arch == null) return;

                for (int x = minX; x < minX + roomW; x++)
                    for (int y = minY; y < minY + roomH; y++)
                        arch.SetFloor((short)x, (short)y, 1, new FloorTile { Pattern = parlorFloor }, true);

                int walkX = minX + roomW / 2;
                int walkEndY = Math.Min(arch.Height - 2, Math.Max(minY + roomH + 2, curbTile.TileY - 2));
                for (int y = minY + roomH; y <= walkEndY; y++)
                {
                    arch.SetFloor((short)(walkX - 1), (short)y, 1, new FloorTile { Pattern = walkFloor }, true);
                    arch.SetFloor((short)walkX, (short)y, 1, new FloorTile { Pattern = walkFloor }, true);
                }

                for (int x = minX; x < minX + roomW; x++)
                {
                    var wt = arch.GetWall((short)x, (short)minY, 1);
                    wt.Segments |= WallSegments.TopRight;
                    wt.TopRightPattern = wallPat;
                    wt.BottomLeftPattern = wallPat;
                    wt.TopRightStyle = 1;
                    try { arch.SetWall((short)x, (short)minY, 1, wt); } catch { }
                }
                for (int y = minY; y < minY + roomH; y++)
                {
                    var wt = arch.GetWall((short)minX, (short)y, 1);
                    wt.Segments |= WallSegments.TopLeft;
                    wt.TopLeftPattern = wallPat;
                    wt.BottomRightPattern = wallPat;
                    wt.TopLeftStyle = 1;
                    try { arch.SetWall((short)minX, (short)y, 1, wt); } catch { }
                }

                try { arch.RegenWallsAt(); } catch { }
            }
            catch (Exception e) { Boot.Log("crime-scene architecture note: " + e.Message); }
        }

        static ushort ResolveFloorPattern(Content content, bool isWalkway)
        {
            try
            {
                var floors = content?.WorldFloors?.List();
                if (floors != null && floors.Count > 0)
                {
                    string[] prefs = isWalkway
                        ? new[] { "stone", "paver", "brick", "concrete", "cobble" }
                        : new[] { "parquet", "hardwood", "mahogany", "wood", "oak", "carpet", "check" };
                    for (int p = 0; p < prefs.Length; p++)
                    {
                        for (int i = 0; i < floors.Count; i++)
                        {
                            ushort id; string name;
                            if (TryReadPatternRef(floors[i], out id, out name) && name.Contains(prefs[p]))
                                return id;
                        }
                    }
                    for (int i = 0; i < floors.Count; i++)
                    {
                        ushort id; string name;
                        if (TryReadPatternRef(floors[i], out id, out name)) return id;
                    }
                }
            }
            catch { }
            return isWalkway ? (ushort)2 : (ushort)1;
        }

        static ushort ResolveWallPattern(Content content)
        {
            try
            {
                var walls = content?.WorldWalls?.List();
                if (walls != null && walls.Count > 0)
                {
                    string[] prefs = { "wainscot", "panel", "wood", "mahogany", "brick", "wallpaper", "plaster" };
                    for (int p = 0; p < prefs.Length; p++)
                    {
                        for (int i = 0; i < walls.Count; i++)
                        {
                            ushort id; string name;
                            if (TryReadPatternRef(walls[i], out id, out name) && name.Contains(prefs[p]))
                                return id;
                        }
                    }
                    for (int i = 0; i < walls.Count; i++)
                    {
                        ushort id; string name;
                        if (TryReadPatternRef(walls[i], out id, out name)) return id;
                    }
                }
            }
            catch { }
            return 1;
        }

        static bool TryReadPatternRef(object item, out ushort id, out string lowerName)
        {
            id = 0;
            lowerName = "";
            if (item == null) return false;
            try
            {
                var t = item.GetType();
                var any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                object rawId = t.GetProperty("ID", any)?.GetValue(item, null)
                               ?? t.GetField("ID", any)?.GetValue(item)
                               ?? t.GetProperty("Id", any)?.GetValue(item, null)
                               ?? t.GetField("Id", any)?.GetValue(item);
                if (rawId != null)
                {
                    long v = Convert.ToInt64(rawId, CultureInfo.InvariantCulture);
                    if (v > 0 && v <= ushort.MaxValue) id = (ushort)v;
                }
                object rawName = t.GetProperty("Name", any)?.GetValue(item, null)
                                 ?? t.GetField("Name", any)?.GetValue(item);
                if (rawName != null) lowerName = rawName.ToString().ToLowerInvariant();
                return id > 0;
            }
            catch { return false; }
        }

        static void ClearAreaObjects(VM vm, int minX, int minY, int maxX, int maxY)
        {
            try
            {
                var toDelete = new List<VMEntity>();
                foreach (var ent in vm.Entities)
                {
                    if (ent == null || ent.Dead || ent is VMAvatar) continue;
                    if (IsCruiserEntity(ent)) continue;
                    var p = ent.Position;
                    if (p == LotTilePos.OUT_OF_WORLD) continue;
                    if (p.TileX >= minX && p.TileX <= maxX && p.TileY >= minY && p.TileY <= maxY)
                    {
                        string nm = (ent.Object?.OBJ?.ChunkLabel ?? "").ToLowerInvariant();
                        if (nm.Contains("portal") || nm.Contains("mail") || nm.Contains("controller") || nm.Contains("trash"))
                            continue;
                        toDelete.Add(ent);
                    }
                }
                for (int i = 0; i < toDelete.Count; i++)
                {
                    try { if (!toDelete[i].Dead) toDelete[i].Delete(true, vm.Context); } catch { }
                }
            }
            catch { }
        }

        static void PickSceneFootprint(VM vm, LotTilePos curbTile, out int minX, out int minY)
        {
            int w = vm?.Context?.Architecture?.Width ?? 48;
            int h = vm?.Context?.Architecture?.Height ?? 48;
            int cx = curbTile != LotTilePos.OUT_OF_WORLD ? curbTile.TileX : (w / 2);
            int cy = curbTile != LotTilePos.OUT_OF_WORLD ? Math.Max(10, curbTile.TileY - 12) : (h / 2 - 4);
            minX = Math.Max(6, Math.Min(w - 16, cx - 4));
            minY = Math.Max(6, Math.Min(h - 16, cy));
        }

        static void FindStreetCurbPlacement(VM vm, out LotTilePos carPos, out LotTilePos standPos, out Direction carDir)
        {
            int w = vm?.Context?.Architecture?.Width ?? 48;
            int h = vm?.Context?.Architecture?.Height ?? 48;
            carDir = Direction.WEST;
            VMEntity portal = vm?.Entities?.FirstOrDefault(e => e != null && !e.Dead && e.Object?.OBJ?.GUID == GuidCarPortal);
            if (portal != null && portal.Position != LotTilePos.OUT_OF_WORLD)
            {
                int px = Math.Max(10, Math.Min(w - 10, (int)portal.Position.TileX));
                int py = Math.Max(10, Math.Min(h - 6, (int)portal.Position.TileY));
                carPos = new LotTilePos((short)(px * 16), (short)(py * 16), 1);
                standPos = new LotTilePos((short)(px * 16), (short)(Math.Max(6, py - 2) * 16), 1);
                return;
            }
            carPos = new LotTilePos((short)((w / 2) * 16), (short)(Math.Max(12, h - 8) * 16), 1);
            standPos = new LotTilePos((short)((w / 2) * 16), (short)(Math.Max(10, h - 10) * 16), 1);
        }

        static VMMultitileGroup TrySpawnObjectNear(VM vm, uint guid, LotTilePos origin, Direction dir, int maxRadius)
        {
            for (int r = 0; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        var pos = new LotTilePos((short)(origin.x + dx * 16), (short)(origin.y + dy * 16), 1);
                        try
                        {
                            var grp = vm.Context.CreateObjectInstance(guid, pos, dir);
                            if (grp != null && grp.Objects != null && grp.Objects.Count > 0 &&
                                (grp.BaseObject ?? grp.Objects[0]).Position != LotTilePos.OUT_OF_WORLD)
                                return grp;
                        }
                        catch { }
                    }
                }
            }
            return null;
        }

        static void KeepSceneActorsControlled()
        {
            if (cruiserGroup != null) FreezeGroup(cruiserGroup, pauseThread: true);
            if (detectiveAvatar != null && !detectiveAvatar.Dead)
                FreezeEntity(detectiveAvatar, pauseThread: true);
            for (int i = 0; i < 3; i++)
                if (suspectAvatars[i] != null && !suspectAvatars[i].Dead)
                    FreezeEntity(suspectAvatars[i], pauseThread: true);
            for (int i = 0; i < sceneProps.Count; i++)
                FreezeEntity(sceneProps[i], pauseThread: true);
        }

        static void FreezeGroup(VMMultitileGroup grp, bool pauseThread)
        {
            if (grp?.Objects == null) return;
            for (int i = 0; i < grp.Objects.Count; i++)
                FreezeEntity(grp.Objects[i], pauseThread);
        }

        static void FreezeEntity(VMEntity ent, bool pauseThread)
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

        static void CaptureHomeDetective(VMAvatar detective, VM vm, UIScreen screen, int? logicOverride, int? charismaOverride)
        {
            VMAvatar av = detective ?? FindBestHomeDetective(vm);
            homeDetectiveName = !string.IsNullOrWhiteSpace(av?.Name) ? av.Name.Trim() : "Bob";
            homeDetectiveGuid = av?.Object?.OBJ?.GUID ?? 0;
            homeDetectiveNeighborId = av != null ? av.GetPersonData(VMPersonDataVariable.NeighborId) : (short)0;
            homeDetectiveHead = av?.Avatar?.Head;
            homeDetectiveBody = av?.Avatar?.Body;

            int rawLogic = av != null ? av.GetPersonData(VMPersonDataVariable.LogicSkill) / 100 : 5;
            int rawCha = av != null ? av.GetPersonData(VMPersonDataVariable.CharismaSkill) / 100 : 4;
            homeLogicSkill = logicOverride ?? Math.Max(0, Math.Min(10, rawLogic));
            homeCharismaSkill = charismaOverride ?? Math.Max(0, Math.Min(10, rawCha));
            homeHouseNumber = ReadCurrentHouseNumber(screen, vm);
        }

        static VMAvatar FindBestHomeDetective(VM vm)
        {
            try
            {
                var fam = vm?.TS1State?.CurrentFamily;
                var avatars = vm?.Entities?.OfType<VMAvatar>().Where(a => a != null && !a.Dead && !a.IsPet).ToList();
                if (avatars == null || avatars.Count == 0) return null;
                if (fam?.RuntimeSubset != null)
                {
                    var match = avatars.FirstOrDefault(a => fam.RuntimeSubset.Contains(a.Object.OBJ.GUID));
                    if (match != null) return match;
                }
                return avatars[0];
            }
            catch { return null; }
        }

        static int ReadCurrentHouseNumber(UIScreen screen, VM vm)
        {
            try
            {
                var activeFam = screen?.GetType().GetField("ActiveFamily")?.GetValue(screen) as FAMI
                                ?? vm?.TS1State?.CurrentFamily;
                if (activeFam != null && activeFam.HouseNumber > 0)
                    return activeFam.HouseNumber;
            }
            catch { }
            return 7;
        }

        static int PickCleanSceneLot(int excludeHouse)
        {
            int[] preferred = { 8, 9, 10, 3, 4, 5, 21, 22, 23 };
            for (int i = 0; i < preferred.Length; i++)
            {
                int id = preferred[i];
                if (id == excludeHouse) continue;
                if (HouseFileExists(id)) return id;
            }
            return excludeHouse == 21 ? 22 : 21;
        }

        static bool HouseFileExists(int houseId)
        {
            try
            {
                var content = Content.Get();
                string userData = content?.TS1BasePath;
                if (string.IsNullOrEmpty(userData)) return true;
                string file = System.IO.Path.Combine(userData, "UserData", "Houses", "House" + houseId.ToString("00") + ".iff");
                return System.IO.File.Exists(file);
            }
            catch { return true; }
        }

        static bool TriggerScreenSwitchLot(UIScreen screen, int houseId)
        {
            if (screen == null) return false;
            try
            {
                var f = screen.GetType().GetField("SwitchLot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(screen, houseId);
                    return true;
                }
            }
            catch (Exception e) { Boot.Log("crime-scene SwitchLot error: " + e.Message); }
            return false;
        }

        static void SelectActiveAvatar(VMAvatar av)
        {
            try
            {
                var screen = UIScreen.Current;
                var lotCtrl = screen?.GetType().GetField("LotControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screen)
                           ?? screen?.GetType().GetProperty("LotControl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(screen, null);
                var prop = lotCtrl?.GetType().GetProperty("ActiveEntity", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.CanWrite) prop.SetValue(lotCtrl, av, null);
            }
            catch { }
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
                            vm.SpeedMultiplier = 1;
                    }
                }
            }
            catch { }
        }

        public static Vector2? GetEntityScreenPos(VMEntity entity, float yOffsetPx = -46f)
        {
            if (sceneVm == null || entity == null || entity.Dead) return null;
            try
            {
                var world = sceneVm.Context?.World;
                if (world == null || world.State == null) return null;
                var st = world.State;
                var ui = entity.WorldUI;
                if (ui == null) return null;
                var scr = ui.GetScreenPos(st);
                var scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
                var middle = new Vector2((int)(UIScreen.Current.ScreenWidth / (2 / scale)), (int)(UIScreen.Current.ScreenHeight / (2 / scale)));
                var zoomed = ((scr - middle) * world.BackbufferScale) + middle;
                var off2 = new Vector2(st.WorldSpace.WorldPxWidth, st.WorldSpace.WorldPxHeight);
                off2 = (off2 / st.PreciseZoom - off2) / 2;
                var pt = ((zoomed + new Vector2(0, yOffsetPx) / (1 << (3 - (int)st.Zoom))) + off2) * st.PreciseZoom / scale;
                if (float.IsNaN(pt.X) || float.IsNaN(pt.Y)) return null;
                return pt;
            }
            catch { return null; }
        }

        static void FocusCameraOnWorldPoint(Vector2 worldCenter, WorldZoom zoom2D, float zoom3D)
        {
            try
            {
                var world = sceneVm?.Context?.World;
                if (world == null || world.State == null) return;
                world.State.CenterTile = worldCenter;
                world.State.ScrollAnchor = null;
                world.State.BuildMode = 0;
                world.State.Zoom = zoom2D;
                var cam = world.State.Cameras?.Camera3D;
                if (cam != null)
                {
                    cam.RotationX = -0.78f;
                    cam.RotationY = 0.52f;
                    cam.Zoom3D = zoom3D;
                }

                var screen = UIScreen.Current;
                var lotCtrl = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                if (lotCtrl != null)
                {
                    var any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    var foundMe = lotCtrl.GetType().GetField("FoundMe", any);
                    if (foundMe != null) foundMe.SetValue(lotCtrl, true);
                    float targetZoom = Math.Max(0.55f, Math.Min(1.52f, 0.25f + (9.75f - zoom3D) / 5.7f));
                    var tz = lotCtrl.GetType().GetField("TargetZoom", any);
                    if (tz != null) tz.SetValue(lotCtrl, targetZoom);
                }
            }
            catch { }
        }

        static VMPieMenuInteraction MakePieEntry(VMEntity target, DetectivePieActionKind kind, string label)
        {
            return new VMPieMenuInteraction
            {
                Name = label,
                ID = (byte)(DetectivePieActionBase + (uint)kind),
                Param0 = 0,
                Entry = new TTABInteraction { TTAIndex = (uint)(DetectivePieActionBase + (uint)kind) },
                Callee = target,
                Global = false,
                Score = 100,
            };
        }

        static bool MatchesEntity(VMEntity a, VMEntity b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b) || a.ObjectID == b.ObjectID) return true;
            if (a.MultitileGroup != null && ReferenceEquals(a.MultitileGroup, b.MultitileGroup)) return true;
            return false;
        }

        static bool IsCruiserEntity(VMEntity ent)
        {
            if (ent == null) return false;
            if (cruiserGroup != null && ent.MultitileGroup != null && ReferenceEquals(ent.MultitileGroup, cruiserGroup))
                return true;
            return ent.Object?.OBJ?.GUID == GuidPoliceCruiser;
        }

        static string SafeObjName(VMEntity ent)
        {
            if (ent == null) return "null";
            try
            {
                string n = ent.Name;
                if (!string.IsNullOrWhiteSpace(n)) return n.Trim();
                return ent.Object?.OBJ?.ChunkLabel ?? ("obj#" + ent.ObjectID);
            }
            catch { return "obj"; }
        }
    }
}
