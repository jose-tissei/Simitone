using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Content;
using FSO.Content.Interfaces;
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
    public enum TriageModalMode
    {
        None = 0,
        PhoneSwitchboard = 1,
        PaperForms = 2
    }

    public enum TriagePieActionKind
    {
        AnswerPhone911 = 0,
        AssistWalkInCitizen = 1,
        EndShift = 2
    }

    // Real-Lot, 100%-Real-TS1-Game-Assets Driver for Law Enforcement Level 4 ("Desk Sergeant"):
    // 911 Dispatch Triage (DISP-07).
    // Switches safely into a real Sims 1 lot (preserving the player's saved home lot), builds a
    // SimCity PD Precinct Front Desk from real TS1 walls, floors, counters, table phone, computer,
    // lamp, office chair, bookcases, plants, and parked Police Squad Car, dresses the player's
    // real VMAvatar in the authentic TS1 Desk Sergeant uniform, and spawns verified textured real
    // neighborhood VMAvatars who physically walk up to the counter!
    // Works seamlessly in BOTH 3D mode AND 2D Isometric mode.
    public static class DispatchTriageDriver
    {
        const uint GuidPoliceCruiser = 0x76ECEDA7; // CarSquad.iff
        const uint GuidCarPortal = 0x865A6812;     // CarPortal.iff
        const uint GuidFallbackPerson = 0x7FD96B54;
        const uint TriagePieActionBase = 8900;

        enum TriagePhase
        {
            Inactive,
            WaitingForPrecinctLot,
            Active,
            WaitingForHomeLot
        }

        static TriagePhase phase = TriagePhase.Inactive;
        static VM sourceVm;
        static VM precinctVm;

        // Home-lot officer snapshot (for seamless return home & real VM rewards)
        static string homeOfficerName = "Bob";
        static uint homeOfficerGuid;
        static short homeOfficerNeighborId;
        static int homeLogicSkill = 3;
        static int homeCharismaSkill = 3;
        static int homeHouseNumber = 7;
        static Outfit homeOfficerHead;
        static int triageSeed = 42;
        static int targetPrecinctLotId = 8;

        // Real-lot game state & outcome
        static DispatchTriageGame game;
        static DispatchTriageOutcome finalOutcome;
        static DispatchTriageOutcome pendingReturnOutcome;
        static bool finishFanfarePlayed;
        static TriageModalMode activeModal = TriageModalMode.None;
        static int activeModalCaseIndex = -1;

        // Real TS1 entities in the precinct lot
        static VMMultitileGroup cruiserGroup;
        static VMEntity cruiserLead;
        static VMEntity centerCounterEntity;
        static VMEntity deskPhoneEntity;
        static readonly List<VMEntity> precinctProps = new List<VMEntity>();
        static VMAvatar sergeantAvatar;

        // Walk-in live real VMAvatar choreography
        static VMAvatar walkInAvatar;
        static int walkInCaseIndex = -1;
        static Vector2 walkInEntryTile;
        static Vector2 walkInCounterTile;
        static Vector2 walkInDeptExitTile;
        static Vector2 sergeantStandTile;
        static float sergeantFacingYaw;
        static LotTilePos counterCenterTile;
        static LotTilePos cruiserTile = LotTilePos.OUT_OF_WORLD;

        // Smooth Blender-baked Vitaboy animations on real VMAvatars
        static readonly VitaboyClipPlayer sergeantClip = new VitaboyClipPlayer();
        static readonly VitaboyClipPlayer walkInClip = new VitaboyClipPlayer();
        static readonly Stopwatch frameClock = new Stopwatch();
        static double lastFrameSec = -1.0;
        static readonly Stopwatch animClock = new Stopwatch();
        static double lastAnimSec = -1.0;
        static string lastSergeantClipName = "desk_idle_watch";
        static string boundUniformTag = "none";
        static string spawnedAssetsSummary = "";

        // Audio & ambience
        static readonly DispatchAudioEngine audio = new DispatchAudioEngine();
        static float walkInStepAccumSec;
        static bool walkInStepToggle;
        static float urgentBeepAccumSec;
        static float phoneRingRepeatSec;
        static float alertSweepRemainingSec;
        static bool walkInArrivedBalloonShown;

        public static bool IsActive { get { return phase != TriagePhase.Inactive; } }
        public static bool IsPrecinctLotLive { get { return phase == TriagePhase.Active && precinctVm != null && game != null; } }
        public static DispatchTriageGame Game { get { return game; } }
        public static DispatchTriageOutcome FinalOutcome { get { return finalOutcome; } }
        public static TriageModalMode ActiveModal { get { return activeModal; } }
        public static int ActiveModalCaseIndex { get { return activeModalCaseIndex; } }
        internal static DispatchAudioEngine Audio { get { return audio; } }
        public static VMEntity DeskPhoneEntity { get { return deskPhoneEntity ?? centerCounterEntity; } }
        public static VMAvatar WalkInAvatar { get { return walkInAvatar; } }
        public static VMAvatar SergeantAvatar { get { return sergeantAvatar; } }

        public static bool IsActiveOn(VM vm)
        {
            return phase == TriagePhase.Active && vm != null && ReferenceEquals(vm, precinctVm);
        }

        public static string BeginTriage(
            VMAvatar homeSim,
            int? logicLevelOverride = null,
            int? charismaLevelOverride = null,
            int? seed = null)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "err no screen";
                var vm = CareerDispatchDriver.CurrentVM();
                if (vm == null || !vm.Ready) return "err vm not ready";

                if (phase != TriagePhase.Inactive)
                    ForceReset();

                CareerDispatchToast.Close();
                SecurityWatchOverlay.Close(false);
                CadetAcademyOverlay.Close(false);
                if (BeatPatrolDriver.IsActive) BeatPatrolDriver.RequestReturnHome(false);

                CaptureHomeOfficer(homeSim, vm, screen, logicLevelOverride, charismaLevelOverride);
                triageSeed = seed ?? (Environment.TickCount & 0x7FFF);
                targetPrecinctLotId = PickCleanPrecinctLot(homeHouseNumber);

                sourceVm = vm;
                precinctVm = null;
                pendingReturnOutcome = null;
                finalOutcome = null;
                activeModal = TriageModalMode.None;
                activeModalCaseIndex = -1;
                phase = TriagePhase.WaitingForPrecinctLot;

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

                if (!TriggerScreenSwitchLot(screen, targetPrecinctLotId))
                {
                    phase = TriagePhase.Inactive;
                    return "err failed to trigger TS1GameScreen.SwitchLot";
                }

                Boot.Log("dispatch-triage: switching from House" + homeHouseNumber + " -> real-asset Precinct on House" + targetPrecinctLotId + " for Sgt. " + homeOfficerName);
                return "ok switching precinctLot=" + targetPrecinctLotId + " officer=" + homeOfficerName;
            }
            catch (Exception e)
            {
                phase = TriagePhase.Inactive;
                Boot.Log("dispatch-triage BeginTriage error: " + e.Message);
                return "err " + e.Message;
            }
        }

        public static void OnTick(VM vm)
        {
            if (phase == TriagePhase.Inactive || vm == null || !vm.Ready) return;

            if (phase == TriagePhase.WaitingForPrecinctLot)
            {
                if (!ReferenceEquals(vm, sourceVm) && vm.Context != null && vm.Context.World != null)
                {
                    SetupPrecinctLot(vm);
                }
                return;
            }

            if (phase == TriagePhase.WaitingForHomeLot)
            {
                if (!ReferenceEquals(vm, precinctVm) && vm.Context != null)
                {
                    CompleteReturnHome(vm);
                }
                return;
            }

            if (phase == TriagePhase.Active)
            {
                if (!ReferenceEquals(vm, precinctVm))
                {
                    ForceReset();
                    return;
                }

                KeepPrecinctActorsControlled();
                UpdateFrame();
            }
        }

        public static void TickAlertSweep()
        {
            if (alertSweepRemainingSec > 0f && precinctVm != null)
            {
                alertSweepRemainingSec = Math.Max(0f, alertSweepRemainingSec - (1f / 60f));
                DismissBlockingAlerts(precinctVm);
            }
        }

        static void UpdateFrame()
        {
            if (phase != TriagePhase.Active || precinctVm == null || game == null) return;

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

            if (!game.IsBriefing && !game.IsFinished)
            {
                game.Tick(dt);
                ProcessGameEvents();
                SyncWalkInAvatar(dt);
                UpdateAudioTimers(dt);

                // If the currently open modal's case resolved or expired, close modal cleanly!
                if (activeModal != TriageModalMode.None)
                {
                    var mc = GetModalCase();
                    if (mc == null || mc.IsResolved)
                    {
                        activeModal = TriageModalMode.None;
                        activeModalCaseIndex = -1;
                    }
                }
            }

            if (game.IsFinished && finalOutcome == null)
            {
                activeModal = TriageModalMode.None;
                activeModalCaseIndex = -1;
                finalOutcome = game.ComputeOutcome();
                if (!finishFanfarePlayed)
                {
                    finishFanfarePlayed = true;
                    audio.Play(DispatchSfxId.ShiftFanfare, 0.52f);
                    if (sergeantAvatar != null)
                    {
                        sergeantClip.Play("desk_seated_salute", 0.20f, 0.95f, true, false);
                        lastSergeantClipName = "desk_seated_salute";
                        ConvoOverlay.Show(precinctVm, sergeantAvatar, "Sgt. " + homeOfficerName,
                            "Shift complete — SimCity PD Precinct Desk secured!", null, null, null);
                    }
                }
            }
        }

        public static void PreDrawFrame()
        {
            if (phase != TriagePhase.Active || precinctVm == null || game == null) return;

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

            // 1. Update Sgt. Bob seated skeletal animation in his office chair behind the escrivaninha desk!
            if (sergeantAvatar != null && !sergeantAvatar.Dead && sergeantAvatar.Avatar != null)
            {
                string desiredClip = "desk_idle_watch";
                bool loop = true;
                float speed = 1.0f;

                if (game.IsFinished)
                {
                    desiredClip = "desk_seated_salute";
                    loop = true;
                    speed = 0.95f;
                }
                else if (activeModal == TriageModalMode.PhoneSwitchboard)
                {
                    desiredClip = "desk_answer_phone";
                    loop = true;
                    speed = 1.0f;
                }
                else if (!game.IsBriefing)
                {
                    switch (game.CurrentGesture)
                    {
                        case DeskSergeantGesture.RadioDispatch:
                            desiredClip = "desk_radio_dispatch";
                            loop = false;
                            speed = 1.05f;
                            break;
                        case DeskSergeantGesture.WaveDismiss:
                            desiredClip = "desk_wave_dismiss";
                            loop = false;
                            speed = 1.05f;
                            break;
                        case DeskSergeantGesture.CalmTalk:
                            desiredClip = activeModal == TriageModalMode.PhoneSwitchboard ? "desk_answer_phone" : "desk_idle_watch";
                            loop = true;
                            speed = 1.08f;
                            break;
                        default:
                            desiredClip = "desk_idle_watch";
                            loop = true;
                            speed = 1.0f;
                            break;
                    }
                }

                if (desiredClip != lastSergeantClipName)
                {
                    sergeantClip.Play(desiredClip, 0.16f, speed, loop, true);
                    lastSergeantClipName = desiredClip;
                }

                sergeantAvatar.Animations?.Clear();
                sergeantAvatar.CarryAnimationState = null;
                sergeantAvatar.VisualPositionStart = null;
                sergeantAvatar.VisualPosition = new Vector3(sergeantStandTile.X, sergeantStandTile.Y, 0f);
                sergeantAvatar.RadianDirection = sergeantFacingYaw;
                sergeantClip.UpdateAndApply(sergeantAvatar.Avatar, dt, useSquad: true);
            }

            // 2. Update Live 3D/2D Walk-In Neighborhood Citizen animation & smooth tile position!
            if (walkInAvatar != null && !walkInAvatar.Dead && walkInAvatar.Avatar != null)
            {
                walkInAvatar.Animations?.Clear();
                walkInAvatar.CarryAnimationState = null;
                walkInAvatar.VisualPositionStart = null;
                walkInClip.UpdateAndApply(walkInAvatar.Avatar, dt, useSquad: true);
            }
        }

        // Native Simitone Pie Menu hook — lets players click the REAL TS1 Desk Phone OR the REAL Walk-In Sim!
        public static List<VMPieMenuInteraction> TryBuildPieMenu(VMEntity target, VM vm, VMEntity caller)
        {
            if (!IsActiveOn(vm) || target == null || game == null || game.IsFinished)
                return null;

            // 1. Clicked the Real TS1 Desk Phone (or the Center Reception Counter holding it)?
            if (IsPhoneOrCenterCounter(target))
            {
                var phoneCase = GetFirstActivePhoneCase();
                if (phoneCase != null)
                {
                    return new List<VMPieMenuInteraction>
                    {
                        MakePieEntry(target, TriagePieActionKind.AnswerPhone911, "Answer 911 Call (" + phoneCase.CallerName + ")")
                    };
                }
                return new List<VMPieMenuInteraction>();
            }

            // 2. Clicked the Real Walk-In Citizen at the front counter?
            if (walkInAvatar != null && MatchesEntity(walkInAvatar, target))
            {
                var wi = GetArrivedWalkInCase();
                if (wi != null)
                {
                    return new List<VMPieMenuInteraction>
                    {
                        MakePieEntry(target, TriagePieActionKind.AssistWalkInCitizen, "Assist " + wi.CallerName + " (Hand Form)")
                    };
                }
                return new List<VMPieMenuInteraction>();
            }

            // 3. Clicked Sgt. Bob or parked Police Squad Car?
            if ((sergeantAvatar != null && MatchesEntity(sergeantAvatar, target)) || IsCruiserEntity(target))
            {
                return new List<VMPieMenuInteraction>
                {
                    MakePieEntry(target, TriagePieActionKind.EndShift, "End Desk Shift & Return Home")
                };
            }

            return null;
        }

        public static bool TryHandleInteraction(VMNetInteractionCmd cmd, VM vm, VMAvatar caller)
        {
            if (cmd == null || cmd.Interaction < TriagePieActionBase || cmd.Interaction > TriagePieActionBase + 10)
                return false;
            if (!IsActiveOn(vm) || game == null) return false;

            var act = (TriagePieActionKind)(cmd.Interaction - TriagePieActionBase);
            switch (act)
            {
                case TriagePieActionKind.AnswerPhone911:
                    OpenPhoneSwitchboardModal();
                    return true;
                case TriagePieActionKind.AssistWalkInCitizen:
                    OpenPaperFormsModal();
                    return true;
                case TriagePieActionKind.EndShift:
                    RequestReturnHome(game.IsFinished);
                    return true;
            }
            return true;
        }

        public static bool OpenPhoneSwitchboardModal()
        {
            if (!IsPrecinctLotLive || game.IsFinished) return false;
            if (game.IsBriefing)
            {
                game.BeginShift();
                ProcessGameEvents();
            }
            var phoneCase = GetFirstActivePhoneCase();
            if (phoneCase == null) return false;

            game.SelectCase(phoneCase.CaseIndex);
            activeModal = TriageModalMode.PhoneSwitchboard;
            activeModalCaseIndex = phoneCase.CaseIndex;
            audio.Play(DispatchSfxId.PhonePickup, 0.48f);
            ConvoOverlay.Clear();

            if (sergeantAvatar != null)
            {
                sergeantClip.Play("desk_answer_phone", 0.14f, 1.0f, true, true);
                lastSergeantClipName = "desk_answer_phone";
            }
            return true;
        }

        public static bool OpenPaperFormsModal()
        {
            if (!IsPrecinctLotLive || game.IsFinished) return false;
            if (game.IsBriefing)
            {
                game.BeginShift();
                ProcessGameEvents();
            }
            var wi = GetArrivedWalkInCase();
            if (wi == null) return false;

            game.SelectCase(wi.CaseIndex);
            activeModal = TriageModalMode.PaperForms;
            activeModalCaseIndex = wi.CaseIndex;
            audio.Play(DispatchSfxId.PhonePickup, 0.34f);
            ConvoOverlay.Clear();
            return true;
        }

        public static void CloseActiveModal()
        {
            activeModal = TriageModalMode.None;
            activeModalCaseIndex = -1;
        }

        public static bool RouteActiveModalCase(DispatchRouteAction action)
        {
            if (!IsPrecinctLotLive || game.IsBriefing || game.IsFinished) return false;
            var mc = GetModalCase() ?? game.SelectedCase;
            if (mc == null || mc.IsResolved) return false;

            bool isWalkIn = mc.Channel == DispatchCallChannel.WalkIn;
            string callerName = mc.CallerName;
            game.SelectCase(mc.CaseIndex);
            bool ok = game.RouteSelectedCase(action);
            ProcessGameEvents();

            if (ok)
            {
                activeModal = TriageModalMode.None;
                activeModalCaseIndex = -1;

                if (isWalkIn)
                {
                    audio.Play(DispatchSfxId.PaperFormStamp, 0.56f);
                    if (sergeantAvatar != null)
                    {
                        string formLine = BuildPaperFormHandoverLine(action, callerName);
                        ConvoOverlay.Show(precinctVm, sergeantAvatar, "Sgt. " + homeOfficerName,
                            formLine, walkInAvatar, callerName,
                            action == DispatchRouteAction.DismissCall
                                ? "Oh — wrong office! Sorry Sergeant!"
                                : "Got the stamped form — heading right to that department!");
                    }
                }
                else
                {
                    if (sergeantAvatar != null && !string.IsNullOrWhiteSpace(game.SergeantSpokenLine))
                    {
                        ConvoOverlay.Show(precinctVm, sergeantAvatar, "Sgt. " + homeOfficerName,
                            game.SergeantSpokenLine, null, null, null);
                    }
                }
            }
            return ok;
        }

        public static bool CalmActiveModalCaller()
        {
            if (!IsPrecinctLotLive || game.IsBriefing || game.IsFinished) return false;
            var mc = GetModalCase() ?? game.SelectedCase;
            if (mc == null || mc.IsResolved) return false;
            game.SelectCase(mc.CaseIndex);
            bool ok = game.CalmSelectedCaller();
            ProcessGameEvents();
            if (ok && sergeantAvatar != null)
            {
                ConvoOverlay.Show(precinctVm, sergeantAvatar, "Sgt. " + homeOfficerName,
                    "Take a deep breath — stay on the line, I'm right here.", null, null, null);
            }
            return ok;
        }

        public static DispatchActiveCase GetModalCase()
        {
            if (game == null || activeModal == TriageModalMode.None) return null;
            if (activeModalCaseIndex >= 0 && activeModalCaseIndex < game.AllCases.Count)
            {
                var c = game.AllCases[activeModalCaseIndex];
                if (c != null && c.IsActive && !c.IsResolved) return c;
            }
            return activeModal == TriageModalMode.PhoneSwitchboard
                ? GetFirstActivePhoneCase()
                : GetArrivedWalkInCase();
        }

        public static DispatchActiveCase GetFirstActivePhoneCase()
        {
            if (game == null) return null;
            var active = game.GetActiveCases();
            for (int i = 0; i < active.Count; i++)
                if (active[i].Channel == DispatchCallChannel.Phone911)
                    return active[i];
            return null;
        }

        public static DispatchActiveCase GetArrivedWalkInCase()
        {
            if (game == null) return null;
            var wi = game.ActiveWalkIn;
            if (wi != null && !wi.IsResolved && !wi.WalkInLeaving && wi.WalkInApproach >= 0.85f)
                return wi;
            return null;
        }

        public static Vector2? GetEntityScreenPos(VMEntity entity, float yOffsetPx = -46f)
        {
            if (precinctVm == null || entity == null || entity.Dead) return null;
            try
            {
                var world = precinctVm.Context?.World;
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

        public static string RequestReturnHome(bool applyRewards)
        {
            if (phase != TriagePhase.Active) return "err triage not active";

            pendingReturnOutcome = applyRewards && game != null
                ? (finalOutcome ?? game.ComputeOutcome())
                : null;

            audio.StopAmbience();
            DispatchTriageOverlay.Close();
            ConvoOverlay.Clear();
            activeModal = TriageModalMode.None;
            activeModalCaseIndex = -1;

            try
            {
                var screen = UIScreen.Current;
                if (screen != null && homeHouseNumber > 0)
                {
                    phase = TriagePhase.WaitingForHomeLot;
                    if (TriggerScreenSwitchLot(screen, homeHouseNumber))
                    {
                        Boot.Log("dispatch-triage: returning home to House" + homeHouseNumber + " (applyRewards=" + applyRewards + ")");
                        return "ok returning home house=" + homeHouseNumber;
                    }
                }
            }
            catch (Exception e) { Boot.Log("dispatch-triage return home error: " + e.Message); }

            ForceReset();
            return "ok closed";
        }

        public static string StatusSummary()
        {
            if (phase == TriagePhase.Inactive) return "inactive";
            if (phase == TriagePhase.WaitingForPrecinctLot) return "switching_lot=" + targetPrecinctLotId;
            if (phase == TriagePhase.WaitingForHomeLot) return "returning_home=" + homeHouseNumber;
            if (game == null) return "active_no_game";

            var ph = GetFirstActivePhoneCase();
            var wi = game.ActiveWalkIn;
            return "lot=" + targetPrecinctLotId +
                   " briefing=" + game.IsBriefing +
                   " finished=" + game.IsFinished +
                   " resolved=" + game.ResolvedCount + "/" + game.TotalCases +
                   " active=" + game.GetActiveCases().Count +
                   " score=" + game.Score +
                   " modal=" + activeModal +
                   " phone=" + (ph != null ? (ph.CaseIndex + ":" + ph.CallerName + ":" + ph.CorrectAction) : "none") +
                   " walkin=" + (wi != null ? (wi.CallerName + "@" + wi.WalkInApproach.ToString("0.00", CultureInfo.InvariantCulture)) : "none") +
                   " units=P" + game.AvailablePatrolUnits + "F" + game.AvailableFireUnits + "C" + game.AvailableCommunityUnits +
                   " uniform=" + boundUniformTag +
                   " anim=" + lastSergeantClipName +
                   " audioCues=" + audio.RecordedCueCount +
                   " assets=[" + spawnedAssetsSummary + "]";
        }

        public static string ExportLastSessionWav(string path, float leadSec = 0f, float tailSec = 1.0f)
        {
            return audio.ExportSessionWav(path, leadSec, tailSec);
        }

        public static string HandleTestAction(string cmd)
        {
            if (!IsPrecinctLotLive) return "err dispatch triage lot not live";
            string c = (cmd ?? "").Trim().ToLowerInvariant();

            if (c == "start" || c == "begin")
            {
                game.BeginShift();
                ProcessGameEvents();
                return "ok " + StatusSummary();
            }
            if (c == "click_phone" || c == "phone" || c == "answer")
            {
                bool ok = OpenPhoneSwitchboardModal();
                return (ok ? "ok " : "err no ringing phone ") + StatusSummary();
            }
            if (c == "click_walkin" || c == "walkin" || c == "assist" || c == "select_walkin")
            {
                bool ok = OpenPaperFormsModal();
                return (ok ? "ok " : "err no arrived walkin ") + StatusSummary();
            }
            if (c == "patrol" || c == "1")
            {
                bool ok = RouteActiveModalCase(DispatchRouteAction.DispatchPatrol);
                return (ok ? "ok " : "busy ") + StatusSummary();
            }
            if (c == "fire" || c == "2")
            {
                bool ok = RouteActiveModalCase(DispatchRouteAction.DispatchFire);
                return (ok ? "ok " : "busy ") + StatusSummary();
            }
            if (c == "aid" || c == "community" || c == "3")
            {
                bool ok = RouteActiveModalCase(DispatchRouteAction.CommunityAid);
                return (ok ? "ok " : "busy ") + StatusSummary();
            }
            if (c == "dismiss" || c == "prank" || c == "4")
            {
                bool ok = RouteActiveModalCase(DispatchRouteAction.DismissCall);
                return (ok ? "ok " : "busy ") + StatusSummary();
            }
            if (c == "auto" || c == "correct")
            {
                var mc = GetModalCase() ?? game.SelectedCase;
                if (mc == null) return "err no active case";
                bool ok = RouteActiveModalCase(mc.CorrectAction);
                return (ok ? "ok " : "busy ") + StatusSummary();
            }
            if (c == "calm" || c == "c")
            {
                bool ok = CalmActiveModalCaller();
                return (ok ? "ok " : "err ") + StatusSummary();
            }
            if (c == "claim" || c == "finish" || c == "end")
            {
                return RequestReturnHome(game.IsFinished);
            }
            if (c == "skip" || c == "close")
            {
                return RequestReturnHome(false);
            }
            return "err unknown triage action: " + c;
        }

        // ---- Real-Lot Precinct Setup (100% Real Sims 1 Game Assets!) ----

        static void SetupPrecinctLot(VM vm)
        {
            try
            {
                precinctVm = vm;
                precinctProps.Clear();
                cruiserGroup = null;
                cruiserLead = null;
                centerCounterEntity = null;
                deskPhoneEntity = null;
                sergeantAvatar = null;
                walkInAvatar = null;
                walkInCaseIndex = -1;
                walkInArrivedBalloonShown = false;
                finishFanfarePlayed = false;
                activeModal = TriageModalMode.None;
                activeModalCaseIndex = -1;
                frameClock.Reset();
                lastFrameSec = -1.0;
                animClock.Reset();
                lastAnimSec = -1.0;
                walkInStepAccumSec = 0f;
                urgentBeepAccumSec = 0f;
                phoneRingRepeatSec = 0f;

                // Bright daytime precinct lighting!
                if (vm.Context.Clock != null)
                {
                    vm.Context.Clock.Hours = 11;
                    vm.Context.Clock.Minutes = 0;
                }
                vm.SpeedMultiplier = 1;
                alertSweepRemainingSec = 4.5f;
                ConvoOverlay.Clear();
                DismissBlockingAlerts(vm);

                var content = Content.Get();

                // 1. Park the real TS1 Police Squad Car cleanly at the street curb!
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

                // 2. Choose an open 9x8 precinct footprint cleanly near the front sidewalk & Police Squad Car!
                int roomMinX, roomMinY;
                PickPrecinctFootprint(vm, cruiserTile, out roomMinX, out roomMinY);
                int roomW = 8;
                int roomH = 7;

                // Remove any stray trees/bushes/weeds inside the precinct lobby footprint
                ClearAreaObjects(vm, roomMinX - 1, roomMinY - 1, roomMinX + roomW + 1, roomMinY + roomH + 2);

                // 3. Lay down authentic real TS1 lobby floor tiles & back cutaway precinct walls!
                ushort lobbyFloorId = ResolveFloorPattern(content, isWalkway: false);
                ushort walkFloorId = ResolveFloorPattern(content, isWalkway: true);
                ushort wallPatId = ResolveWallPattern(content);

                BuildPrecinctArchitecture(vm, roomMinX, roomMinY, roomW, roomH, cruiserTile, lobbyFloorId, walkFloorId, wallPatId);

                // 4. Furnish the SimCity PD Precinct Office Desk (Escrivaninha) using 100% REAL TS1 GAME OBJECTS!
                int deskY = roomMinY + 2;
                int deskCenterX = roomMinX + 4;
                counterCenterTile = new LotTilePos((short)(deskCenterX * 16), (short)(deskY * 16), 1);

                var spawnedLog = new List<string>();
                uint deskGuid = FindCatalogObjectGuid(content,
                    new[] { "desk - executive", "desk - moderate", "desk - cheap", "desk - ", "writing desk", "office desk", "desk" },
                    new[] { "chair", "lamp", "phone", "computer", "counter", "sink", "podium", "reception", "check", "cash", "register", "hotel", "npc", "teacher", "school", "bar", "vanity", "dresser", "buffet", "island", "food", "stand", "stall", "study", "magic", "castle", "western", "lab", "chemistry", "dj", "djbooth", "part b", "right" },
                    preferSingleTile: false);
                uint phoneGuid = FindCatalogObjectGuid(content,
                    new[] { "phone - desk", "desk phone", "phone" },
                    new[] { "wall", "booth", "pay", "cell", "public", "npc", "television", "tv", "left", "right" },
                    preferSingleTile: true);
                uint computerGuid = FindCatalogObjectGuid(content,
                    new[] { "computer -", "computer" },
                    new[] { "desk", "table", "chair", "npc", "popcorn", "left", "right" },
                    preferSingleTile: true);
                uint endTableGuid = FindCatalogObjectGuid(content,
                    new[] { "end table", "table - end", "side table" },
                    new[] { "coffee", "dining", "picnic", "patio", "outdoor", "magic", "castle", "western", "left", "right", "buffet" },
                    preferSingleTile: true);
                uint lampGuid = FindCatalogObjectGuid(content,
                    new[] { "lamp - table", "table lamp", "desk lamp", "lamp - desk", "banker", "lamp" },
                    new[] { "garden", "yard", "lawn", "tiki", "torch", "lantern", "patio", "outdoor", "floor", "wall", "street", "sconce", "hanging", "chandelier", "lava", "post", "path", "unleashed", "camping", "pool", "tomb", "magic", "left", "right" },
                    preferSingleTile: true);
                uint chairGuid = FindCatalogObjectGuid(content,
                    new[] { "chair - office", "office chair", "desk chair", "office" },
                    new[] { "recliner", "lounge", "beach", "lawn", "bar", "stool", "sofa", "couch", "loveseat", "high", "electric", "massage", "throne", "western", "left", "right", "desk", "table" },
                    preferSingleTile: true);
                if (chairGuid == 0)
                {
                    chairGuid = FindCatalogObjectGuid(content,
                        new[] { "dining chair", "chair - dining", "chair" },
                        new[] { "recliner", "lounge", "beach", "lawn", "bar", "stool", "sofa", "couch", "loveseat", "high", "electric", "massage", "throne", "western", "left", "right" },
                        preferSingleTile: true);
                }
                uint bookcaseGuid = FindCatalogObjectGuid(content,
                    new[] { "bookshelf - dorm", "bookcase - pine", "bookcase - amish", "bookcase - cheap", "bookcase - expensive", "bookcase", "bookshelf" },
                    new[] { "western", "left", "right", "center", "wall", "magic", "spell", "secret", "door", "castle", "dungeon", "store", "shop", "display", "rack", "counter", "npc" },
                    preferSingleTile: true);
                uint plantGuid = FindCatalogObjectGuid(content,
                    new[] { "plant - floor", "ficus", "palm", "fern", "plant" },
                    new[] { "shrub", "hedge", "tree", "flower", "garden", "hanging", "wall", "cactus", "table", "left", "right" },
                    preferSingleTile: true);

                // Place Sergeant's Escrivaninha (real TS1 Office Desk, kneehole facing NORTH toward seated Sgt. Bob!)
                // If it's a 2-tile Executive Desk, it gives us 2 seamless surface tiles automatically (Computer + Phone);
                // if it's a 1-tile Desk, spawn a matching second desk right beside it so BOTH Computer & Phone sit on top!
                VMEntity mainDesk = null;
                VMEntity phoneDesk = null;
                if (deskGuid != 0)
                {
                    var deskPos = new LotTilePos((short)(deskCenterX * 16), (short)(deskY * 16), 1);
                    var deskGrp = vm.Context.CreateObjectInstance(deskGuid, deskPos, Direction.NORTH);
                    if (deskGrp != null && deskGrp.Objects != null && deskGrp.Objects.Count > 0)
                    {
                        FreezeGroup(deskGrp, pauseThread: true);
                        for (int i = 0; i < deskGrp.Objects.Count; i++)
                            precinctProps.Add(deskGrp.Objects[i]);

                        var tiles = deskGrp.Objects
                            .Where(o => o != null && o.Position != LotTilePos.OUT_OF_WORLD)
                            .OrderBy(o => o.Position.TileX)
                            .ToList();
                        if (tiles.Count >= 2)
                        {
                            mainDesk = tiles[0];
                            phoneDesk = tiles[1];
                        }
                        else
                        {
                            mainDesk = deskGrp.BaseObject ?? deskGrp.Objects[0];
                            phoneDesk = SpawnPrecinctProp(vm, deskGuid, deskCenterX + 1, deskY, Direction.NORTH);
                        }
                    }
                }
                int seatX = (mainDesk != null && mainDesk.Position != LotTilePos.OUT_OF_WORLD) ? mainDesk.Position.TileX : deskCenterX;
                int seatY = (mainDesk != null && mainDesk.Position != LotTilePos.OUT_OF_WORLD) ? (mainDesk.Position.TileY - 1) : (deskY - 1);
                int phoneDeskX = (phoneDesk != null && phoneDesk.Position != LotTilePos.OUT_OF_WORLD) ? phoneDesk.Position.TileX : (seatX + 1);
                int phoneDeskY = (phoneDesk != null && phoneDesk.Position != LotTilePos.OUT_OF_WORLD) ? phoneDesk.Position.TileY : deskY;

                centerCounterEntity = phoneDesk ?? mainDesk;
                if (mainDesk != null) spawnedLog.Add("Desk=" + SafeObjName(mainDesk));

                // Place REAL TS1 Computer directly ON TOP of Sgt. Bob's Main Office Desk!
                if (computerGuid != 0 && mainDesk != null)
                {
                    var comp = SpawnPrecinctProp(vm, computerGuid, seatX, deskY + 1, Direction.NORTH);
                    if (comp != null)
                    {
                        bool slotted = false;
                        try { slotted = mainDesk.PlaceInSlot(comp, 0, true, vm.Context); } catch { }
                        if (!slotted)
                        {
                            try { comp.SetPosition(new LotTilePos((short)(seatX * 16), (short)(deskY * 16), 1), Direction.NORTH, vm.Context, VMPlaceRequestFlags.AcceptSlots); } catch { }
                        }
                        spawnedLog.Add("PC=" + SafeObjName(comp) + (slotted ? "(slotted)" : "(placed)"));
                    }
                }

                // Place REAL TS1 Desk Telephone directly ON TOP of the Phone Desk surface!
                if (phoneGuid != 0)
                {
                    VMEntity phoneSurface = phoneDesk ?? mainDesk;
                    deskPhoneEntity = SpawnPrecinctProp(vm, phoneGuid, phoneDeskX, phoneDeskY + 1, Direction.SOUTH);
                    if (deskPhoneEntity != null && phoneSurface != null)
                    {
                        bool slotted = false;
                        try { slotted = phoneSurface.PlaceInSlot(deskPhoneEntity, 0, true, vm.Context); } catch { }
                        if (!slotted)
                        {
                            try { deskPhoneEntity.SetPosition(new LotTilePos((short)(phoneDeskX * 16), (short)(phoneDeskY * 16), 1), Direction.SOUTH, vm.Context, VMPlaceRequestFlags.AcceptSlots); } catch { }
                        }
                        spawnedLog.Add("Phone=" + SafeObjName(deskPhoneEntity) + (slotted ? "(slotted)" : "(placed)"));
                    }
                }

                // Place Sgt. Bob's REAL TS1 Office Chair directly behind the Computer Desk (facing SOUTH toward the desk)!
                if (chairGuid != 0)
                {
                    var deskChair = SpawnPrecinctProp(vm, chairGuid, seatX, seatY, Direction.SOUTH);
                    if (deskChair != null) spawnedLog.Add("DeskChair=" + SafeObjName(deskChair));

                    // Waiting chairs along the left wall cleanly away from the desk
                    var wait1 = SpawnPrecinctProp(vm, chairGuid, roomMinX + 1, roomMinY + 4, Direction.EAST);
                    var wait2 = SpawnPrecinctProp(vm, chairGuid, roomMinX + 1, roomMinY + 5, Direction.EAST);
                    if (wait1 != null || wait2 != null) spawnedLog.Add("WaitChairs=" + SafeObjName(wait1 ?? wait2));
                }

                // Side End Table with Table Lamp neatly beside waiting chairs
                if (endTableGuid != 0 && lampGuid != 0)
                {
                    var sideTbl = SpawnPrecinctProp(vm, endTableGuid, roomMinX + 1, roomMinY + 3, Direction.EAST);
                    if (sideTbl != null)
                    {
                        var lamp = SpawnPrecinctProp(vm, lampGuid, roomMinX + 2, roomMinY + 3, Direction.SOUTH);
                        if (lamp != null)
                        {
                            bool slotted = false;
                            try { slotted = sideTbl.PlaceInSlot(lamp, 0, true, vm.Context); } catch { }
                            if (!slotted) { try { lamp.Delete(true, vm.Context); } catch { } }
                            else spawnedLog.Add("Lamp=" + SafeObjName(lamp));
                        }
                    }
                }

                // One clean single-tile classic indoor wood Bookshelf inside the room (away from back wall corners!)
                if (bookcaseGuid != 0)
                {
                    var b1 = SpawnPrecinctProp(vm, bookcaseGuid, roomMinX + 1, roomMinY + 1, Direction.EAST);
                    if (b1 != null) spawnedLog.Add("Files=" + SafeObjName(b1));
                }
                if (plantGuid != 0)
                {
                    var p1 = SpawnPrecinctProp(vm, plantGuid, roomMinX + 2, roomMinY + roomH - 1, Direction.SOUTH);
                    var p2 = SpawnPrecinctProp(vm, plantGuid, roomMinX + 6, roomMinY + roomH - 1, Direction.SOUTH);
                    if (p1 != null || p2 != null) spawnedLog.Add("Plants=" + SafeObjName(p1 ?? p2));
                }

                spawnedAssetsSummary = string.Join(", ", spawnedLog);

                // 5. Spawn Sgt. Bob SEATED in his Office Chair behind the Escrivaninha Desk in authentic Desk Sergeant uniform!
                var sgtTile = new LotTilePos((short)(seatX * 16), (short)(seatY * 16), 1);
                sergeantStandTile = new Vector2(seatX + 0.50f, seatY + 0.48f);
                sergeantFacingYaw = (float)Math.PI; // Facing SOUTH across the escrivaninha toward the computer, phone & citizens!
                sergeantAvatar = SpawnAndDressSergeant(vm, sgtTile);
                if (sergeantAvatar != null)
                {
                    FreezeEntity(sergeantAvatar, pauseThread: true);
                    sergeantAvatar.VisualPosition = new Vector3(sergeantStandTile.X, sergeantStandTile.Y, 0f);
                    sergeantAvatar.RadianDirection = sergeantFacingYaw;
                    sergeantClip.Play("desk_idle_watch", 0.0f, 1.0f, true, true);
                    lastSergeantClipName = "desk_idle_watch";
                    SelectActiveAvatar(sergeantAvatar);
                }

                // Key walk-in choreography tiles:
                // Citizens enter from the front walkway, walk straight up to the front of the Sergeant's escrivaninha,
                // and after receiving their stamped Paper Form walk right toward the department corridor & exit!
                walkInEntryTile = new Vector2(seatX + 0.5f, roomMinY + roomH + 1.2f);
                walkInCounterTile = new Vector2(seatX + 0.5f, deskY + 1.38f);
                walkInDeptExitTile = new Vector2(roomMinX + roomW + 0.8f, deskY + 2.5f);

                // 6. Create pure-C# DispatchTriageGame with verified textured neighborhood callers!
                var callers = CollectVerifiedNeighborhoodCallers(content);
                game = new DispatchTriageGame(
                    sergeantName: homeOfficerName,
                    logicSkillLevel: homeLogicSkill,
                    charismaSkillLevel: homeCharismaSkill,
                    neighborhoodCallers: callers,
                    seed: triageSeed,
                    startInBriefing: true);

                // 7. Frame Simitone's real camera tightly on the Precinct Front Desk (works in 3D AND 2D!)
                var camFocus = new LotTilePos((short)(deskCenterX * 16), (short)((deskY + 1) * 16), 1);
                FocusCameraOnTile(camFocus, WorldZoom.Near, zoom3D: 2.15f);

                // 8. Activate live Precinct HUD & procedural precinct ambience!
                phase = TriagePhase.Active;
                audio.StartSession();
                audio.StartAmbience(0.18f);
                audio.Play(DispatchSfxId.ArrivalCue, 0.44f);
                DispatchTriageOverlay.OpenForRealLot();
                DismissBlockingAlerts(vm);

                Boot.Log("dispatch-triage: REAL-ASSET Precinct ready! lot=" + targetPrecinctLotId +
                         " uniform=" + boundUniformTag +
                         " assets=[" + spawnedAssetsSummary + "]" +
                         " callers=" + callers.Count +
                         (callers.Count > 0 ? (" (" + string.Join(", ", callers.Take(6).Select(c => c.Name)) + ")") : ""));
            }
            catch (Exception e)
            {
                Boot.Log("dispatch-triage SetupPrecinctLot error: " + e.GetType().Name + ": " + e.Message);
                ForceReset();
            }
        }

        // ---- Walk-In Real VMAvatar Spawning & Smooth Counter Approach ----

        static void SyncWalkInAvatar(float dt)
        {
            var wi = game != null ? game.ActiveWalkIn : null;
            if (wi == null)
            {
                if (walkInAvatar != null)
                {
                    try { walkInAvatar.Delete(true, precinctVm.Context); } catch { }
                    walkInAvatar = null;
                    walkInCaseIndex = -1;
                    walkInArrivedBalloonShown = false;
                }
                return;
            }

            // Spawn fresh verified textured real VMAvatar for this Walk-In citizen!
            if (walkInAvatar == null || walkInAvatar.Dead || walkInCaseIndex != wi.CaseIndex)
            {
                if (walkInAvatar != null)
                {
                    try { walkInAvatar.Delete(true, precinctVm.Context); } catch { }
                    walkInAvatar = null;
                }

                var entryTile = new LotTilePos((short)((int)walkInEntryTile.X * 16), (short)((int)walkInEntryTile.Y * 16), 1);
                walkInAvatar = SpawnVerifiedWalkInAvatar(precinctVm, wi.CallerOutfitGuid, wi.CallerName, entryTile);
                walkInCaseIndex = wi.CaseIndex;
                walkInArrivedBalloonShown = false;
                if (walkInAvatar != null)
                {
                    FreezeEntity(walkInAvatar, pauseThread: true);
                    walkInClip.Play("desk_walkin_walk", 0.0f, 1.05f, true, true);
                }
            }

            if (walkInAvatar == null || walkInAvatar.Dead) return;

            Vector2 pos;
            float yaw;
            bool walking;

            if (!wi.WalkInLeaving)
            {
                float u = Math.Max(0f, Math.Min(1f, wi.WalkInApproach));
                float s = u * u * (3f - 2f * u);
                pos = Vector2.Lerp(walkInEntryTile, walkInCounterTile, s);
                Vector2 dir = walkInCounterTile - walkInEntryTile;
                yaw = (float)Math.Atan2(dir.X, -dir.Y);
                walking = u < 0.96f;

                if (walking)
                    walkInClip.Play("desk_walkin_walk", 0.14f, 1.05f, true, false);
                else
                {
                    // Face NORTH toward Sgt. Bob across the reception counter!
                    yaw = 0f;
                    walkInClip.Play("desk_walkin_plead", 0.18f, 1.0f, true, false);
                    if (!walkInArrivedBalloonShown)
                    {
                        walkInArrivedBalloonShown = true;
                        // Ensure world tile matches stand position for accurate native Pie Menu raycasting!
                        var standLotPos = new LotTilePos((short)((int)Math.Floor(pos.X) * 16), (short)((int)Math.Floor(pos.Y) * 16), 1);
                        try { walkInAvatar.SetPosition(standLotPos, Direction.NORTH, precinctVm.Context, VMPlaceRequestFlags.AcceptSlots); } catch { }
                        if (activeModal == TriageModalMode.None)
                        {
                            ConvoOverlay.Show(precinctVm, walkInAvatar, wi.CallerName,
                                wi.TranscriptLine1, null, null, null);
                        }
                    }
                }
            }
            else
            {
                float u = Math.Max(0f, Math.Min(1f, wi.WalkInLeaveProgress));
                float s = u * u * (3f - 2f * u);
                pos = Vector2.Lerp(walkInCounterTile, walkInDeptExitTile, s);
                Vector2 dir = walkInDeptExitTile - walkInCounterTile;
                yaw = (float)Math.Atan2(dir.X, -dir.Y);
                walking = u < 0.97f;
                walkInClip.Play("desk_walkin_walk", 0.14f, 1.08f, true, false);
            }

            walkInAvatar.VisualPosition = new Vector3(pos.X, pos.Y, 0f);
            walkInAvatar.RadianDirection = yaw;

            if (walking)
            {
                walkInStepAccumSec += dt;
                if (walkInStepAccumSec >= 0.34f)
                {
                    walkInStepAccumSec -= 0.34f;
                    walkInStepToggle = !walkInStepToggle;
                    audio.Play(walkInStepToggle ? DispatchSfxId.WalkInStepA : DispatchSfxId.WalkInStepB, 0.28f);
                }
            }
            else
            {
                walkInStepAccumSec = 0f;
            }
        }

        static void UpdateAudioTimers(float dt)
        {
            var active = game.GetActiveCases();

            // Gentle reminder phone ring every 4.2s while a 911 line is ringing unanswered!
            bool ringingUnanswered = activeModal != TriageModalMode.PhoneSwitchboard && GetFirstActivePhoneCase() != null;
            if (ringingUnanswered)
            {
                phoneRingRepeatSec += dt;
                if (phoneRingRepeatSec >= 4.2f)
                {
                    phoneRingRepeatSec = 0f;
                    audio.Play(DispatchSfxId.PhoneRing, 0.34f);
                }
            }
            else
            {
                phoneRingRepeatSec = 0f;
            }

            bool anyUrgent = active.Any(c => c.UrgencyFraction < 0.25f);
            if (anyUrgent)
            {
                urgentBeepAccumSec += dt;
                if (urgentBeepAccumSec >= 1.4f)
                {
                    urgentBeepAccumSec = 0f;
                    audio.Play(DispatchSfxId.UrgentBeep, 0.22f);
                }
            }
            else
            {
                urgentBeepAccumSec = 0f;
            }
        }

        static void ProcessGameEvents()
        {
            if (game == null) return;
            var events = game.DrainEvents();
            if (events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                switch (ev.Kind)
                {
                    case "call_arrived":
                        audio.Play(DispatchSfxId.PhoneRing, 0.46f);
                        phoneRingRepeatSec = 0f;
                        break;
                    case "walkin_arrived":
                        audio.Play(DispatchSfxId.WalkInBell, 0.50f);
                        break;
                    case "routed_good":
                        PlayDispatchSfx(ev.Action);
                        audio.Play(DispatchSfxId.CorrectChime, 0.42f);
                        break;
                    case "routed_wrong":
                        PlayDispatchSfx(ev.Action);
                        audio.Play(DispatchSfxId.WrongBuzzer, 0.46f);
                        break;
                    case "calmed":
                        audio.Play(DispatchSfxId.CalmCallerChime, 0.44f);
                        break;
                    case "missed":
                    case "unit_busy":
                        audio.Play(DispatchSfxId.WrongBuzzer, 0.42f);
                        break;
                }
            }
        }

        static void PlayDispatchSfx(DispatchRouteAction action)
        {
            switch (action)
            {
                case DispatchRouteAction.DispatchPatrol:
                    audio.Play(DispatchSfxId.RadioDispatchPatrol, 0.48f);
                    break;
                case DispatchRouteAction.DispatchFire:
                    audio.Play(DispatchSfxId.RadioDispatchFire, 0.48f);
                    break;
                case DispatchRouteAction.CommunityAid:
                    audio.Play(DispatchSfxId.RadioDispatchCommunity, 0.46f);
                    break;
                case DispatchRouteAction.DismissCall:
                    audio.Play(DispatchSfxId.HangUpDismiss, 0.48f);
                    break;
            }
        }

        static string BuildPaperFormHandoverLine(DispatchRouteAction action, string citizen)
        {
            switch (action)
            {
                case DispatchRouteAction.DispatchPatrol:
                    return "Here's Form 10-A, " + citizen + " — take it straight to Patrol Intake!";
                case DispatchRouteAction.DispatchFire:
                    return "Urgent Form 22-F stamped — Fire Marshal is rolling out right now!";
                case DispatchRouteAction.CommunityAid:
                    return "Take Form 07-C to Community Aid right down the hall, " + citizen + ".";
                default:
                    return "That's a Form 00-X No-Action slip — City Hall is two blocks over!";
            }
        }

        // ---- Verified Textured Avatar & Real TS1 Object Resolution ----

        static VMAvatar SpawnVerifiedWalkInAvatar(VM vm, uint preferredGuid, string displayName, LotTilePos tile)
        {
            VMAvatar av = null;
            if (preferredGuid != 0)
                av = TrySpawnTexturedAvatar(vm, preferredGuid, tile);

            // If the first neighbor GUID lacked valid textures/body, try other verified adult neighbors!
            if (av == null || !AvatarHasLoadedTextures(av))
            {
                if (av != null) { try { av.Delete(true, vm.Context); } catch { } av = null; }
                var fallbacks = CollectVerifiedNeighborhoodCallers(Content.Get());
                for (int i = 0; i < fallbacks.Count; i++)
                {
                    if (fallbacks[i].OutfitGuid == preferredGuid) continue;
                    var cand = TrySpawnTexturedAvatar(vm, fallbacks[i].OutfitGuid, tile);
                    if (cand != null && AvatarHasLoadedTextures(cand))
                    {
                        av = cand;
                        break;
                    }
                    if (cand != null) { try { cand.Delete(true, vm.Context); } catch { } }
                }
            }

            if (av == null)
                av = TrySpawnTexturedAvatar(vm, GuidFallbackPerson, tile);

            if (av != null && !string.IsNullOrWhiteSpace(displayName))
                av.Name = displayName;
            if (av != null && av.Avatar != null)
            {
                Boot.Log("dispatch-triage: spawned Walk-In Sim '" + (av.Name ?? "?") +
                         "' head=" + (av.Avatar.Head?.TS1AppearanceID ?? "null") + "/" + (av.Avatar.Head?.TS1TextureID ?? "null") +
                         " body=" + (av.Avatar.Body?.TS1AppearanceID ?? "null") + "/" + (av.Avatar.Body?.TS1TextureID ?? "null") +
                         " textured=" + AvatarHasLoadedTextures(av));
            }
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
                // Ensure hands are always populated
                if (av.Avatar != null && av.Avatar.Handgroup == null && av.Avatar.Body != null)
                    av.Avatar.Handgroup = av.Avatar.Body;
                return av;
            }
            catch { return null; }
        }

        static bool AvatarHasLoadedTextures(VMAvatar av)
        {
            if (av == null || av.Avatar == null) return false;
            var content = Content.Get();
            if (content == null) return true;
            var head = av.Avatar.Head;
            var body = av.Avatar.Body;
            if (head == null || body == null) return false;

            if (!AppearanceAndTextureExist(content, head.TS1AppearanceID, head.TS1TextureID))
                return false;
            if (!AppearanceAndTextureExist(content, body.TS1AppearanceID, body.TS1TextureID))
                return false;
            return true;
        }

        static bool VerifyNeighborHasTextures(Content content, uint guid)
        {
            try
            {
                var obj = content?.WorldObjects?.Get(guid);
                if (obj?.Resource == null || obj.OBJ == null) return false;

                ushort bodyStrId = obj.OBJ.BodyStringID != 0 ? obj.OBJ.BodyStringID : (ushort)200;
                var str = obj.Resource.Get<STR>(bodyStrId) ?? obj.Resource.Get<STR>(200);
                if (str == null || str.Length < 3) return false;

                string bodySpec = str.GetString(1);
                string headSpec = str.GetString(2);
                if (string.IsNullOrWhiteSpace(bodySpec) || string.IsNullOrWhiteSpace(headSpec)) return false;

                // Use FreeSO's native VMOutfitReference parser to handle TS1's exact "mesh,BODY=tex" / "mesh,HEAD-HEAD=tex" format!
                var bodyRef = new VMOutfitReference(bodySpec, false);
                var headRef = new VMOutfitReference(headSpec, false);
                var bOft = bodyRef.OftData;
                var hOft = headRef.OftData;
                if (bOft == null || hOft == null) return false;

                if (!AppearanceAndTextureExist(content, bOft.TS1AppearanceID, bOft.TS1TextureID)) return false;
                if (!AppearanceAndTextureExist(content, hOft.TS1AppearanceID, hOft.TS1TextureID)) return false;
                return true;
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

            // Fallback when no explicit TS1TextureID override: require valid bindings on the appearance
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

        static List<DispatchCallerInfo> CollectVerifiedNeighborhoodCallers(Content content)
        {
            var list = new List<DispatchCallerInfo>();
            try
            {
                var entries = content?.Neighborhood?.Neighbors?.Entries;
                if (entries == null) return list;

                string[] addresses = { "5 Sim Lane", "7 Sim Lane", "21 Downtown Plaza", "55 Old Town Market", "10 Sim Lane", "3 Maple Street" };
                var residents = new List<DispatchCallerInfo>();
                var townies = new List<DispatchCallerInfo>();
                int addrIdx = 0;

                for (int i = 0; i < entries.Count; i++)
                {
                    var n = entries[i];
                    if (n == null || n.NeighbourID <= 0 || n.GUID == 0) continue;
                    if (homeOfficerNeighborId > 0 && n.NeighbourID == homeOfficerNeighborId) continue;
                    if (homeOfficerGuid != 0 && n.GUID == homeOfficerGuid) continue;

                    if (n.PersonData != null && n.PersonData.Length > (int)VMPersonDataVariable.PersonsAge)
                    {
                        short age = n.PersonData[(int)VMPersonDataVariable.PersonsAge];
                        if (age > 0 && age < 18) continue;
                    }

                    string name = ResolveNeighborDisplayName(content, n);
                    if (string.IsNullOrWhiteSpace(name) || IsServiceNpcName(name)) continue;
                    if (string.Equals(name.Trim(), homeOfficerName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                    // STRICT texture verification — guarantees zero untextured Walk-In Sims!
                    if (!VerifyNeighborHasTextures(content, n.GUID)) continue;

                    var info = new DispatchCallerInfo
                    {
                        Name = name.Trim(),
                        NeighborId = n.NeighbourID,
                        OutfitGuid = n.GUID,
                        Address = addresses[addrIdx++ % addresses.Length]
                    };
                    bool isResident = !string.IsNullOrEmpty(n.Name) && n.Name.StartsWith("User", StringComparison.OrdinalIgnoreCase);
                    if (isResident) residents.Add(info);
                    else townies.Add(info);
                }

                list.AddRange(residents);
                list.AddRange(townies);
                if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
            }
            catch { }
            return list;
        }

        static VMAvatar SpawnAndDressSergeant(VM vm, LotTilePos tile)
        {
            uint guid = homeOfficerGuid != 0 ? homeOfficerGuid : GuidFallbackPerson;
            var av = TrySpawnTexturedAvatar(vm, guid, tile);
            if (av == null && guid != GuidFallbackPerson)
                av = TrySpawnTexturedAvatar(vm, GuidFallbackPerson, tile);
            if (av == null) return null;

            av.Name = homeOfficerName;
            try
            {
                if (homeOfficerHead != null && av.Avatar != null)
                    av.Avatar.Head = homeOfficerHead;
                var content = Content.Get();
                string tag;
                var uniform = CadetAcademySceneRenderer.ResolveLawEnforcementUniform(content, av, out tag, new[] { 3, 2, 1, 0 });
                if (uniform != null && av.Avatar != null)
                {
                    av.Avatar.Body = uniform;
                    av.Avatar.Handgroup = uniform;
                    boundUniformTag = tag;
                }
                else
                {
                    boundUniformTag = "civilian";
                }
            }
            catch { }
            return av;
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

                    // Verify object exists in WorldObjects and inspect tile count
                    var obj = content.WorldObjects?.Get(item.GUID);
                    if (obj == null || obj.OBJ == null) continue;
                    if (obj.OBJ.ObjectType == OBJDType.Person || obj.OBJ.ObjectType == OBJDType.SimType) continue;
                    // Never pick secondary half-tile sub-pieces (SubIndex > 0)
                    if (obj.OBJ.SubIndex > 0) continue;
                    if (preferSingleTile && obj.OBJ.MasterID != 0) continue;

                    int score = matchRank * 100;
                    if (preferSingleTile && obj.OBJ.MasterID == 0) score += 40;
                    // Prefer moderate-priced base TS1 items (classic clean styling)
                    if (item.Price > 0 && item.Price <= 1800) score += 20;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGuid = item.GUID;
                    }
                }

                // Fallback: scan WorldObjects.Entries directly by FileName/Name (catches PhoneTable.iff if not in Buy catalog!)
                if (bestGuid == 0 && content?.WorldObjects?.Entries != null)
                {
                    foreach (var kv in content.WorldObjects.Entries)
                    {
                        var r = kv.Value;
                        if (r == null) continue;
                        string fn = ((r.FileName ?? "") + " " + (r.Name ?? "")).ToLowerInvariant();
                        bool ex = excludeTerms != null && excludeTerms.Any(t => fn.Contains(t));
                        if (ex) continue;
                        if (includeTerms.Any(t => fn.Contains(t)))
                        {
                            if (r.SubIndex <= 0)
                                return (uint)r.ID;
                        }
                    }
                }

                return bestGuid;
            }
            catch { return 0; }
        }

        static VMEntity SpawnPrecinctProp(VM vm, uint guid, int tx, int ty, Direction dir)
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
                    precinctProps.Add(grp.Objects[i]);
                return lead;
            }
            catch { return null; }
        }

        static void BuildPrecinctArchitecture(
            VM vm,
            int minX, int minY, int w, int h,
            LotTilePos curbTile,
            ushort lobbyFloorId, ushort walkFloorId, ushort wallPatId)
        {
            try
            {
                var arch = vm.Context.Architecture;
                if (arch == null) return;

                // 1. Lay crisp tiled floor across the Precinct Lobby
                var lobbyFloor = new FloorTile { Pattern = lobbyFloorId };
                for (int x = minX; x < minX + w; x++)
                {
                    for (int y = minY; y < minY + h; y++)
                    {
                        try { arch.SetFloor((short)x, (short)y, 1, lobbyFloor, true); } catch { }
                    }
                }

                // 2. Lay stone entry walkway from precinct lobby entrance out toward the street sidewalk!
                var walkFloor = new FloorTile { Pattern = walkFloorId };
                int entryX = minX + 3;
                int maxWalkY = Math.Min(arch.Height - 4, Math.Max(minY + h + 3, (int)curbTile.TileY - 1));
                for (int y = minY + h; y <= maxWalkY; y++)
                {
                    for (int x = entryX; x <= entryX + 2; x++)
                    {
                        try { arch.SetFloor((short)x, (short)y, 1, walkFloor, true); } catch { }
                    }
                }

                // 3. Build two crisp cutaway back walls (top-back along Y = minY and left-back along X = minX)
                for (int x = minX; x < minX + w; x++)
                {
                    var wt = arch.GetWall((short)x, (short)minY, 1);
                    wt.Segments |= WallSegments.TopRight;
                    wt.TopRightPattern = wallPatId;
                    wt.BottomLeftPattern = wallPatId;
                    wt.TopRightStyle = 1;
                    try { arch.SetWall((short)x, (short)minY, 1, wt); } catch { }
                }

                for (int y = minY; y < minY + h; y++)
                {
                    var wt = arch.GetWall((short)minX, (short)y, 1);
                    wt.Segments |= WallSegments.TopLeft;
                    wt.TopLeftPattern = wallPatId;
                    wt.BottomRightPattern = wallPatId;
                    wt.TopLeftStyle = 1;
                    try { arch.SetWall((short)minX, (short)y, 1, wt); } catch { }
                }

                try { arch.RegenWallsAt(); } catch { }
            }
            catch (Exception e)
            {
                Boot.Log("dispatch-triage architecture build note: " + e.Message);
            }
        }

        static ushort ResolveFloorPattern(Content content, bool isWalkway)
        {
            try
            {
                var floors = content?.WorldFloors?.List();
                if (floors != null && floors.Count > 0)
                {
                    string[] prefs = isWalkway
                        ? new[] { "stone", "paver", "concrete", "brick", "slate" }
                        : new[] { "marble", "tile", "checker", "granite", "parquet", "wood" };
                    for (int p = 0; p < prefs.Length; p++)
                    {
                        for (int i = 0; i < floors.Count; i++)
                        {
                            ushort id; string name;
                            if (!TryReadPatternRef(floors[i], out id, out name) || id == 0) continue;
                            if (name.Contains(prefs[p])) return id;
                        }
                    }
                    for (int i = 0; i < floors.Count; i++)
                    {
                        ushort id; string name;
                        if (TryReadPatternRef(floors[i], out id, out name) && id > 0) return id;
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
                    string[] prefs = { "wainscot", "panel", "plaster", "blue", "stucco", "paint", "brick" };
                    for (int p = 0; p < prefs.Length; p++)
                    {
                        for (int i = 0; i < walls.Count; i++)
                        {
                            ushort id; string name;
                            if (!TryReadPatternRef(walls[i], out id, out name) || id == 0) continue;
                            if (name.Contains(prefs[p])) return id;
                        }
                    }
                    for (int i = 0; i < walls.Count; i++)
                    {
                        ushort id; string name;
                        if (TryReadPatternRef(walls[i], out id, out name) && id > 0) return id;
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
                    if (ent == null || ent.Dead) continue;
                    if (ent is VMAvatar) continue;
                    if (IsCruiserEntity(ent)) continue;
                    var p = ent.Position;
                    if (p == LotTilePos.OUT_OF_WORLD) continue;
                    if (p.TileX >= minX && p.TileX <= maxX && p.TileY >= minY && p.TileY <= maxY)
                    {
                        // Never delete portals/controllers/mailbox
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

        static void PickPrecinctFootprint(VM vm, LotTilePos curbTile, out int minX, out int minY)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            int cx = Math.Max(10, Math.Min(w - 14, curbTile.TileX - 4));
            int cy = Math.Max(10, Math.Min(h - 16, curbTile.TileY - 11));
            minX = cx;
            minY = cy;
        }

        // ---- Return Home & Reward Persistence ----

        static void CompleteReturnHome(VM homeVm)
        {
            try
            {
                if (pendingReturnOutcome != null)
                {
                    VMAvatar homeAv = null;
                    if (homeVm.Entities != null)
                    {
                        homeAv = homeVm.Entities.OfType<VMAvatar>()
                            .FirstOrDefault(a => a != null && !a.Dead &&
                                ((homeOfficerNeighborId > 0 && a.GetPersonData(VMPersonDataVariable.NeighborId) == homeOfficerNeighborId) ||
                                 string.Equals((a.Name ?? "").Trim(), homeOfficerName.Trim(), StringComparison.OrdinalIgnoreCase)));
                    }

                    string rewardSummary = CareerDispatchDriver.ApplyReward(homeAv, pendingReturnOutcome.Reward);

                    try
                    {
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
                                Boot.Memory.Record(sp, ls, "dispatch_triage",
                                    rel.DailyDelta >= 0 ? Tone.Gentle : Tone.Bold,
                                    "dispatch_triage", rel.DailyDelta >= 0 ? 3 : 1,
                                    subjectTitle: rel.MemoryFact);
                            }
                        }
                        Boot.SaveMemoryAsync();
                    }
                    catch (Exception re) { Boot.Log("dispatch-triage rel save error: " + re.Message); }

                    Boot.Log("dispatch-triage: returned home! grade=" + pendingReturnOutcome.Grade +
                             " score=" + pendingReturnOutcome.Score +
                             " rewards=[" + rewardSummary + "]" +
                             " rels=" + pendingReturnOutcome.RelationshipDeltas.Count);
                }
            }
            catch (Exception e)
            {
                Boot.Log("dispatch-triage CompleteReturnHome error: " + e.Message);
            }
            finally
            {
                ForceReset();
            }
        }

        // ---- Shared Lot, Camera, Entity & Pie Helpers ----

        static void KeepPrecinctActorsControlled()
        {
            if (cruiserGroup != null) FreezeGroup(cruiserGroup, pauseThread: true);
            if (sergeantAvatar != null) FreezeEntity(sergeantAvatar, pauseThread: true);
            if (walkInAvatar != null) FreezeEntity(walkInAvatar, pauseThread: true);
            for (int i = 0; i < precinctProps.Count; i++)
                FreezeEntity(precinctProps[i], pauseThread: true);
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

        static bool IsPhoneOrCenterCounter(VMEntity target)
        {
            if (target == null) return false;
            if (deskPhoneEntity != null && MatchesEntity(deskPhoneEntity, target)) return true;
            if (centerCounterEntity != null && MatchesEntity(centerCounterEntity, target)) return true;
            return false;
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

        static bool MatchesEntity(VMEntity a, VMEntity b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b) || a.ObjectID == b.ObjectID) return true;
            if (a.MultitileGroup != null && ReferenceEquals(a.MultitileGroup, b.MultitileGroup)) return true;
            return false;
        }

        static VMPieMenuInteraction MakePieEntry(VMEntity target, TriagePieActionKind kind, string label)
        {
            return new VMPieMenuInteraction
            {
                Name = label,
                ID = (byte)(TriagePieActionBase + (uint)kind),
                Param0 = 0,
                Entry = new TTABInteraction { TTAIndex = (uint)(TriagePieActionBase + (uint)kind) },
                Callee = target,
                Global = false,
                Score = 100
            };
        }

        static void CaptureHomeOfficer(VMAvatar av, VM vm, UIScreen screen, int? logicOverride, int? chaOverride)
        {
            homeOfficerName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
            homeOfficerGuid = av?.Object?.OBJ?.GUID ?? 0;
            homeOfficerNeighborId = av != null ? av.GetPersonData(VMPersonDataVariable.NeighborId) : (short)0;
            homeLogicSkill = logicOverride ?? (av != null ? av.GetPersonData(VMPersonDataVariable.LogicSkill) / 100 : 3);
            homeCharismaSkill = chaOverride ?? (av != null ? av.GetPersonData(VMPersonDataVariable.CharismaSkill) / 100 : 3);
            homeOfficerHead = av?.Avatar?.Head;

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

        static int PickCleanPrecinctLot(int excludeHouse)
        {
            // Prefer open empty Neighborhood residential lots (House 08, 09, 10, 03, 04, 05) for a clean Precinct canvas!
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
                string file = Path.Combine(userData, "UserData", "Houses", "House" + houseId.ToString("00") + ".iff");
                return File.Exists(file);
            }
            catch { return true; }
        }

        static bool TriggerScreenSwitchLot(UIScreen screen, int lotId)
        {
            if (screen == null) return false;
            try
            {
                var f = screen.GetType().GetField("SwitchLot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(screen, lotId);
                    return true;
                }
            }
            catch (Exception e) { Boot.Log("dispatch-triage SwitchLot error: " + e.Message); }
            return false;
        }

        static void FindStreetCurbPlacement(VM vm, out LotTilePos carPos, out LotTilePos standPos, out Direction carDir)
        {
            carDir = Direction.WEST;
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            carPos = new LotTilePos((short)((w / 2) * 16), (short)((h - 5) * 16), 1);
            standPos = new LotTilePos((short)((w / 2) * 16), (short)((h - 8) * 16), 1);

            try
            {
                var portal = vm.Entities.FirstOrDefault(e => e != null && !e.Dead && e.Object?.OBJ?.GUID == GuidCarPortal);
                if (portal != null && portal.Position != LotTilePos.OUT_OF_WORLD)
                {
                    int py = portal.Position.TileY;
                    int px = Math.Max(14, Math.Min(w - 14, w / 2));
                    carPos = new LotTilePos((short)(px * 16), (short)(Math.Max(6, py) * 16), 1);
                    standPos = new LotTilePos((short)(px * 16), (short)(Math.Max(6, py - 3) * 16), 1);
                    carDir = Direction.WEST;
                }
            }
            catch { }
        }

        static VMMultitileGroup TrySpawnObjectNear(VM vm, uint guid, LotTilePos center, Direction dir, int radius)
        {
            try
            {
                for (int r = 0; r <= radius; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            var pos = new LotTilePos((short)((center.TileX + dx) * 16), (short)((center.TileY + dy) * 16), 1);
                            var grp = vm.Context.CreateObjectInstance(guid, pos, dir);
                            if (grp != null && grp.BaseObject != null && grp.BaseObject.Position != LotTilePos.OUT_OF_WORLD)
                                return grp;
                            if (grp != null) { try { grp.Delete(vm.Context); } catch { } }
                        }
                    }
                }
                var fb = vm.Context.CreateObjectInstance(guid, center, dir);
                if (fb != null && fb.BaseObject != null)
                {
                    fb.BaseObject.SetPosition(center, dir, vm.Context, VMPlaceRequestFlags.AcceptSlots);
                    return fb;
                }
            }
            catch { }
            return null;
        }

        static void FocusCameraOnTile(LotTilePos tile, WorldZoom zoom2D, float zoom3D)
        {
            try
            {
                var world = precinctVm?.Context?.World;
                if (world == null || world.State == null) return;
                world.State.CenterTile = new Vector2(tile.TileX + 0.5f, tile.TileY + 0.5f);
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

        static void SelectActiveAvatar(VMAvatar av)
        {
            try
            {
                if (precinctVm != null && av != null)
                    precinctVm.MyUID = av.PersistID;
                var screen = UIScreen.Current;
                if (screen == null || av == null) return;
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
                        {
                            vm.SpeedMultiplier = 1;
                            vm.LastSpeedMultiplier = 0;
                        }
                    }
                }

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

        static string ResolveNeighborDisplayName(Content content, Neighbour n)
        {
            try
            {
                var obj = content?.WorldObjects?.Get(n.GUID);
                if (obj != null)
                {
                    var ctss = obj.Resource?.Get<CTSS>(obj.OBJ.CatalogStringsID);
                    string s = ctss?.GetString(0) ?? obj.OBJ?.ChunkLabel;
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            catch { }
            return (n.Name ?? "").Trim();
        }

        static bool IsServiceNpcName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            string low = name.Trim().ToLowerInvariant();
            string[] bad = { "clown", "npc", "maid", "gardener", "repair", "pizza", "burglar", "thief", "police", "cop", "fire", "mime", "grim", "santa", "genie", "bear", "pet", "cat", "dog", "stray", "raccoon", "skunk", "entertainer", "waiter", "chef", "dj", "bartender", "janitor", "sax", "bus", "car", "driver", "social", "skeleton", "bonehilda", "serv", "user0", "mystery", "stranger", "monster" };
            for (int i = 0; i < bad.Length; i++)
                if (low.Contains(bad[i])) return true;
            return !char.IsUpper(name.Trim()[0]);
        }

        static string SafeObjName(VMEntity e)
        {
            if (e == null) return "none";
            try { return e.Object?.OBJ?.ChunkLabel ?? e.Name ?? "obj"; }
            catch { return "obj"; }
        }

        static void ForceReset()
        {
            audio.StopAmbience();
            DispatchTriageOverlay.Close();
            phase = TriagePhase.Inactive;
            sourceVm = null;
            precinctVm = null;
            cruiserGroup = null;
            cruiserLead = null;
            centerCounterEntity = null;
            deskPhoneEntity = null;
            precinctProps.Clear();
            sergeantAvatar = null;
            walkInAvatar = null;
            walkInCaseIndex = -1;
            game = null;
            finalOutcome = null;
            pendingReturnOutcome = null;
            activeModal = TriageModalMode.None;
            activeModalCaseIndex = -1;
        }
    }
}
