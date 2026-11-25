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
    public enum StingModalMode
    {
        None = 0,
        WiretapDialogue = 1,
    }

    public enum StingPieActionKind
    {
        TalkToSuspect = 0,
        CheckWireAtBar = 1,
        FlashBadgeBust = 2,
        EndShift = 3,
    }

    // Real-Lot, 100%-Real-TS1-Game-Assets Driver for Law Enforcement Level 5 ("Vice Squad"):
    // Undercover Sting (DISP-08).
    // Switches safely into a live Sims 1 lot (preserving the player's saved home lot), sets moody
    // evening speakeasy lighting, builds a SimCity Underground Lounge from 100% REAL TS1 game
    // objects (VIP Lounge Table with AcceptSlots Table Lamp, 2 facing Lounge Chairs, Bar Counter,
    // Jukebox/Stereo, Sofa, Floor Lamps, Floor Plants, and parked Police Backup Cruiser outside),
    // spawns the player's plainclothes Undercover Officer + verified fully-textured real neighborhood
    // Suspect & Informant/Bartender Sims, and drives 6 custom Blender animations + procedural
    // noir-jazz SFX + optional Laya AI reaction classification! Works seamlessly in BOTH 3D and 2D.
    public static class UndercoverStingDriver
    {
        const uint GuidPoliceCruiser = 0x76ECEDA7; // CarSquad.iff
        const uint GuidCarPortal = 0x865A6812;     // CarPortal.iff
        const uint GuidFallbackPerson = 0x7FD96B54;
        const uint StingPieActionBase = 9100;

        enum StingPhase
        {
            Inactive,
            WaitingForLoungeLot,
            Active,
            WaitingForHomeLot,
        }

        sealed class VerifiedNeighborInfo
        {
            public string Name;
            public short NeighborId;
            public uint OutfitGuid;
        }

        static StingPhase phase = StingPhase.Inactive;
        static VM sourceVm;
        static VM loungeVm;

        // Home-lot snapshot (for seamless return home & real VM rewards)
        static string homeOfficerName = "Bob";
        static uint homeOfficerGuid;
        static short homeOfficerNeighborId;
        static int homeCharismaSkill = 4;
        static int homeLogicSkill = 4;
        static int homeHouseNumber = 7;
        static Outfit homeOfficerHead;
        static Outfit homeOfficerBody;
        static int stingSeed = 42;
        static int targetLoungeLotId = 8;

        // Live game state & outcome
        static UndercoverStingGame game;
        static UndercoverStingOutcome finalOutcome;
        static UndercoverStingOutcome pendingReturnOutcome;
        static short suspectNeighborId;
        static short informantNeighborId;
        static bool finishFanfarePlayed;
        static bool scorecardReady;
        static float bustClimaxTimerSec;
        static StingModalMode activeModal = StingModalMode.None;

        // Real TS1 entities in the speakeasy lounge
        static VMMultitileGroup cruiserGroup;
        static VMEntity cruiserLead;
        static LotTilePos cruiserTile = LotTilePos.OUT_OF_WORLD;
        static VMEntity vipTableEntity;
        static VMEntity tableLampEntity;
        static VMEntity barCounterEntity;
        static readonly List<VMEntity> loungeProps = new List<VMEntity>();

        static VMAvatar officerAvatar;
        static VMAvatar suspectAvatar;
        static VMAvatar informantAvatar;

        static Vector2 officerSeatWorld;
        static Vector2 officerBustWorld;
        static Vector2 suspectSeatWorld;
        static Vector2 suspectBustWorld;
        static Vector2 informantStandWorld;

        // Smooth Blender-baked Vitaboy animations on real VMAvatars
        static readonly VitaboyClipPlayer officerClip = new VitaboyClipPlayer();
        static readonly VitaboyClipPlayer suspectClip = new VitaboyClipPlayer();
        static float officerActionTimerSec;
        static string lastOfficerClipName = "sting_seated_idle";
        static string lastSuspectClipName = "sting_suspect_nervous";

        static readonly Stopwatch frameClock = new Stopwatch();
        static double lastFrameSec = -1.0;
        static readonly Stopwatch animClock = new Stopwatch();
        static double lastAnimSec = -1.0;
        static float alertSweepRemainingSec;
        static string spawnedAssetsSummary = "";

        static readonly StingAudioEngine audio = new StingAudioEngine();

        public static bool IsActive { get { return phase != StingPhase.Inactive; } }
        public static bool IsLoungeLotLive { get { return phase == StingPhase.Active && loungeVm != null && game != null; } }
        public static bool IsActiveOn(VM vm) { return IsLoungeLotLive && vm != null && ReferenceEquals(vm, loungeVm); }
        public static UndercoverStingGame Game { get { return game; } }
        public static UndercoverStingOutcome FinalOutcome { get { return finalOutcome; } }
        public static bool ShowScorecardNow { get { return game != null && game.IsFinished && scorecardReady && finalOutcome != null; } }
        public static StingModalMode ActiveModal { get { return activeModal; } }
        public static VMAvatar OfficerAvatar { get { return officerAvatar; } }
        public static VMAvatar SuspectAvatar { get { return suspectAvatar; } }
        public static VMAvatar InformantAvatar { get { return informantAvatar; } }
        public static VMEntity BarCounterEntity { get { return barCounterEntity; } }
        public static VMEntity VipTableEntity { get { return vipTableEntity; } }
        public static string SpawnedAssetsSummary { get { return spawnedAssetsSummary; } }
        internal static StingAudioEngine Audio { get { return audio; } }

        public static string BeginSting(VMAvatar officer, int? charismaOverride = null, int? logicOverride = null, int? seedOverride = null)
        {
            if (phase != StingPhase.Inactive)
                return "error undercover sting already active (" + phase + ")";

            try
            {
                var screen = UIScreen.Current;
                var vm = screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
                if (screen == null || vm == null || !vm.Ready)
                    return "error no active lot loaded";

                CaptureHomeOfficer(officer, vm, screen, charismaOverride, logicOverride);
                stingSeed = seedOverride ?? (Environment.TickCount & 0x7FFF);

                // Save home lot quietly so the player loses zero household progress!
                try { screen.GetType().GetMethod("SaveHouse")?.Invoke(screen, null); } catch { }

                targetLoungeLotId = PickCleanLoungeLot(homeHouseNumber);
                sourceVm = vm;
                loungeVm = null;
                game = null;
                finalOutcome = null;
                pendingReturnOutcome = null;
                finishFanfarePlayed = false;
                scorecardReady = false;
                bustClimaxTimerSec = 0f;
                activeModal = StingModalMode.None;
                loungeProps.Clear();
                vipTableEntity = null;
                tableLampEntity = null;
                barCounterEntity = null;
                officerAvatar = null;
                suspectAvatar = null;
                informantAvatar = null;
                cruiserGroup = null;
                cruiserLead = null;
                spawnedAssetsSummary = "";
                phase = StingPhase.WaitingForLoungeLot;

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

                if (!TriggerScreenSwitchLot(screen, targetLoungeLotId))
                {
                    phase = StingPhase.Inactive;
                    return "error failed to trigger TS1GameScreen.SwitchLot";
                }

                Boot.Log("undercover-sting: switching from House" + homeHouseNumber + " -> Speakeasy Lounge on House" + targetLoungeLotId +
                         " officer=" + homeOfficerName + " (Cha=" + homeCharismaSkill + ", Log=" + homeLogicSkill + ", seed=" + stingSeed + ")");
                return "ok switching loungeLot=" + targetLoungeLotId + " officer=" + homeOfficerName;
            }
            catch (Exception e)
            {
                phase = StingPhase.Inactive;
                Boot.Log("undercover-sting BeginSting error: " + e.Message);
                return "error " + e.Message;
            }
        }

        public static void OnTick(VM vm)
        {
            if (phase == StingPhase.Inactive || vm == null || !vm.Ready) return;

            if (phase == StingPhase.WaitingForLoungeLot)
            {
                if (!ReferenceEquals(vm, sourceVm) && vm.Context != null && vm.Context.World != null)
                    SetupSpeakeasyLot(vm);
                return;
            }

            if (phase == StingPhase.WaitingForHomeLot)
            {
                if (!ReferenceEquals(vm, loungeVm) && vm.Context != null)
                    CompleteReturnHome(vm);
                return;
            }

            if (phase == StingPhase.Active)
            {
                if (!ReferenceEquals(vm, loungeVm))
                {
                    ForceReset();
                    return;
                }

                KeepLoungeActorsControlled();
                UpdateFrame();
            }
        }

        public static void TickAlertSweep()
        {
            if (alertSweepRemainingSec > 0f && loungeVm != null)
            {
                alertSweepRemainingSec = Math.Max(0f, alertSweepRemainingSec - (1f / 60f));
                DismissBlockingAlerts(loungeVm);
            }
        }

        static void UpdateFrame()
        {
            if (phase != StingPhase.Active || loungeVm == null || game == null) return;

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

            // Keep moody speakeasy evening lighting steady (readable warm evening!)
            if (loungeVm.Context?.Clock != null)
            {
                loungeVm.Context.Clock.Hours = 19;
                loungeVm.Context.Clock.Minutes = 30;
            }

            if (officerActionTimerSec > 0f)
            {
                officerActionTimerSec -= dt;
                if (officerActionTimerSec <= 0f && game.Ending != StingEndingKind.SuspectBusted)
                    PlayOfficerClip("sting_seated_idle", loop: true);
            }

            // After Bust / Cover Blown, let the dramatic climax play before popping Scorecard
            if (game.IsFinished && !scorecardReady)
            {
                bustClimaxTimerSec -= dt;
                if (bustClimaxTimerSec <= 0f)
                {
                    scorecardReady = true;
                    if (finalOutcome == null) finalOutcome = game.ComputeOutcome();
                    if (!finishFanfarePlayed)
                    {
                        finishFanfarePlayed = true;
                        if (finalOutcome.Ending == StingEndingKind.SuspectBusted)
                            audio.Play(StingSfxId.ShiftVictory, 0.52f);
                    }
                }
            }

            ProcessGameEvents();
        }

        public static void PreDrawFrame()
        {
            if (phase != StingPhase.Active || loungeVm == null || game == null) return;

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

            bool busted = game.Ending == StingEndingKind.SuspectBusted;

            // 1. Update plainclothes Undercover Officer animation & exact VIP table seat!
            if (officerAvatar != null && !officerAvatar.Dead && officerAvatar.Avatar != null)
            {
                if (busted && lastOfficerClipName != "sting_flash_badge")
                    PlayOfficerClip("sting_flash_badge", loop: true);

                Vector2 targetPos = busted ? officerBustWorld : officerSeatWorld;
                officerAvatar.Animations?.Clear();
                officerAvatar.CarryAnimationState = null;
                officerAvatar.VisualPositionStart = null;
                officerAvatar.VisualPosition = new Vector3(targetPos.X, targetPos.Y, 0f);
                officerAvatar.RadianDirection = (float)Math.PI; // Facing SOUTH toward Suspect
                officerClip.UpdateAndApply(officerAvatar.Avatar, dt, useSquad: true);
            }

            // 2. Update real neighborhood Suspect animation & exact seat across table!
            if (suspectAvatar != null && !suspectAvatar.Dead && suspectAvatar.Avatar != null)
            {
                if (busted && lastSuspectClipName != "sting_suspect_handsup")
                    PlaySuspectClip("sting_suspect_handsup", loop: true);
                else if (!busted && lastSuspectClipName != "sting_suspect_nervous")
                    PlaySuspectClip("sting_suspect_nervous", loop: true);

                Vector2 targetPos = busted ? suspectBustWorld : suspectSeatWorld;
                suspectAvatar.Animations?.Clear();
                suspectAvatar.CarryAnimationState = null;
                suspectAvatar.VisualPositionStart = null;
                suspectAvatar.VisualPosition = new Vector3(targetPos.X, targetPos.Y, 0f);
                suspectAvatar.RadianDirection = 0f; // Facing NORTH toward Officer
                suspectClip.UpdateAndApply(suspectAvatar.Avatar, dt, useSquad: true);
            }

            // 3. Keep Informant / Bartender calmly stationed at the Bar!
            if (informantAvatar != null && !informantAvatar.Dead)
            {
                informantAvatar.VisualPositionStart = null;
                informantAvatar.VisualPosition = new Vector3(informantStandWorld.X, informantStandWorld.Y, 0f);
                informantAvatar.RadianDirection = (float)(Math.PI * 0.5); // Facing EAST toward VIP table
            }
        }

        // ---- Interactive Modal & World-Click Entry Points ----

        public static void OpenWiretapModal()
        {
            if (!IsLoungeLotLive || game.IsFinished) return;
            if (game.IsBriefing)
            {
                game.BeginShift();
                ProcessGameEvents();
            }
            activeModal = StingModalMode.WiretapDialogue;
            audio.Play(StingSfxId.WireClick, 0.44f);
        }

        public static void CloseActiveModal()
        {
            activeModal = StingModalMode.None;
        }

        public static string ChooseDialogueTone(StingTone tone)
        {
            if (!IsLoungeLotLive) return "error not active";
            if (game.IsBriefing) game.BeginShift();
            if (game.IsFinished) return "error already finished";
            if (game.CurrentTurn == null) return "error all turns complete — flash badge to bust!";

            // Optional fast Laya AI reaction classification (strictly non-blocking / instant offline fallback)
            string layaVerdict = null;
            if (Boot.Laya != null && Boot.Laya.Available)
            {
                var turn = game.CurrentTurn;
                StingLineOption opt = turn.Options.FirstOrDefault(o => o.Tone == tone) ?? turn.Options[0];
                int prevTimeout = Boot.Laya.TimeoutMs;
                try
                {
                    Boot.Laya.TimeoutMs = 220;
                    layaVerdict = UndercoverStingGame.TryClassifyWithLaya(Boot.Laya, game.CaseProfile, turn, opt);
                }
                catch { layaVerdict = null; }
                finally { Boot.Laya.TimeoutMs = prevTimeout; }
            }

            var entry = game.ChooseTone(tone, layaVerdict);
            if (entry == null) return "error no active turn";

            // Play smooth undercover talk animation & floating world speech balloons!
            PlayOfficerClip("sting_talk_smooth", loop: false);
            officerActionTimerSec = 2.2f;

            audio.Play(tone == StingTone.Gentle ? StingSfxId.ToneGentle
                     : tone == StingTone.Playful ? StingSfxId.TonePlayful
                     : StingSfxId.ToneBold, 0.44f);

            if (officerAvatar != null && suspectAvatar != null)
            {
                ConvoOverlay.Show(loungeVm, officerAvatar, homeOfficerName, entry.OfficerLine,
                    suspectAvatar, game.SuspectName, entry.SuspectReply);
            }

            if (game.IsFinished)
            {
                activeModal = StingModalMode.None;
                finalOutcome = game.ComputeOutcome();
                bustClimaxTimerSec = 2.2f;
                scorecardReady = false;
            }
            else if (game.CanFlashBadgeBust)
            {
                // Close modal cleanly so player sees the glowing "FLASH BADGE & BUST!" marker over the suspect
                activeModal = StingModalMode.None;
            }

            ProcessGameEvents();
            return "ok " + StatusSummary();
        }

        public static string TriggerWireCheck()
        {
            if (!IsLoungeLotLive) return "error not active";
            if (game.IsBriefing) game.BeginShift();
            string msg;
            if (!game.TryCheckWire(out msg))
                return "error " + msg;

            PlayOfficerClip("sting_wire_check", loop: false);
            officerActionTimerSec = 1.6f;

            if (officerAvatar != null && informantAvatar != null)
            {
                ConvoOverlay.Show(loungeVm, officerAvatar, homeOfficerName,
                    "Checking the wire — keep the music rolling, " + game.InformantName + ".",
                    informantAvatar, game.InformantName,
                    "Cover's solid, officer. Watch their tells!");
            }

            ProcessGameEvents();
            return "ok " + StatusSummary();
        }

        public static string TriggerFlashBadgeBust()
        {
            if (!IsLoungeLotLive) return "error not active";
            if (game.IsBriefing) game.BeginShift();
            string msg;
            if (!game.TryFlashBadgeBust(out msg))
                return "error " + msg;

            activeModal = StingModalMode.None;
            finalOutcome = game.ComputeOutcome();
            bustClimaxTimerSec = 2.6f;
            scorecardReady = false;

            if (game.Ending == StingEndingKind.SuspectBusted)
            {
                PlayOfficerClip("sting_flash_badge", loop: true);
                PlaySuspectClip("sting_suspect_handsup", loop: true);
                if (officerAvatar != null && suspectAvatar != null)
                {
                    ConvoOverlay.Show(loungeVm, officerAvatar, homeOfficerName,
                        "SimCity PD Vice Squad! Hands up — we got your confession on tape!",
                        suspectAvatar, game.SuspectName,
                        "No way... you're a cop?! Don't shoot — I'm coming quietly!");
                }
            }

            ProcessGameEvents();
            return "ok " + StatusSummary();
        }

        // ---- Native Simitone Pie Menu Hooks (Clicking Suspect / Informant / Bar!) ----

        public static List<VMPieMenuInteraction> TryBuildPieMenu(VMEntity target, VM vm, VMEntity caller)
        {
            if (!IsActiveOn(vm) || target == null || game == null || game.IsFinished)
                return null;

            if (suspectAvatar != null && MatchesEntity(suspectAvatar, target))
            {
                var list = new List<VMPieMenuInteraction>();
                if (game.CanFlashBadgeBust)
                    list.Add(MakePieEntry(target, StingPieActionKind.FlashBadgeBust, "Vice Squad: FLASH BADGE & BUST " + game.SuspectName + "!"));
                if (game.CurrentTurn != null)
                    list.Add(MakePieEntry(target, StingPieActionKind.TalkToSuspect, "Undercover: Talk to " + game.SuspectName + " (Turn " + (game.CurrentTurnIndex + 1) + "/3)"));
                return list;
            }

            if ((informantAvatar != null && MatchesEntity(informantAvatar, target)) ||
                (barCounterEntity != null && MatchesEntity(barCounterEntity, target)))
            {
                var list = new List<VMPieMenuInteraction>();
                if (game.WireChargesRemaining > 0)
                    list.Add(MakePieEntry(target, StingPieActionKind.CheckWireAtBar, "Vice Squad: Check Wire / Sip Drink (-18% Suspicion)"));
                return list;
            }

            if ((officerAvatar != null && MatchesEntity(officerAvatar, target)) || IsCruiserEntity(target))
            {
                return new List<VMPieMenuInteraction>
                {
                    MakePieEntry(target, StingPieActionKind.EndShift, "Abort Sting & Return Home")
                };
            }

            return null;
        }

        public static bool TryHandleInteraction(VMNetInteractionCmd cmd, VM vm, VMAvatar caller)
        {
            if (cmd == null || cmd.Interaction < StingPieActionBase || cmd.Interaction > StingPieActionBase + 10)
                return false;
            if (!IsActiveOn(vm) || game == null) return false;

            var kind = (StingPieActionKind)(cmd.Interaction - StingPieActionBase);
            switch (kind)
            {
                case StingPieActionKind.TalkToSuspect:
                    OpenWiretapModal();
                    return true;
                case StingPieActionKind.CheckWireAtBar:
                    TriggerWireCheck();
                    return true;
                case StingPieActionKind.FlashBadgeBust:
                    TriggerFlashBadgeBust();
                    return true;
                case StingPieActionKind.EndShift:
                    RequestReturnHome(game.IsFinished);
                    return true;
            }
            return true;
        }

        // ---- Test Hook Commands (`convo dispatch action ...`) ----

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
                case "diag":
                    return BuildSceneDiag();
                case "start":
                    if (!IsLoungeLotLive) return "error lounge lot not active yet";
                    game.BeginShift();
                    ProcessGameEvents();
                    return "ok " + StatusSummary();
                case "talk":
                case "open":
                    if (!IsLoungeLotLive) return "error lounge lot not active";
                    OpenWiretapModal();
                    return "ok " + StatusSummary();
                case "choose":
                case "say":
                case "line":
                {
                    if (!IsLoungeLotLive) return "error lounge lot not active";
                    StingTone tone = StingTone.Gentle;
                    if (arg1 == "2" || arg1 == "playful") tone = StingTone.Playful;
                    else if (arg1 == "3" || arg1 == "bold") tone = StingTone.Bold;
                    else if (arg1 == "best")
                    {
                        var turn = game.CurrentTurn;
                        var best = turn?.Options?.FirstOrDefault(o => o.IsBestForTurn);
                        if (best != null) tone = best.Tone;
                    }
                    return ChooseDialogueTone(tone);
                }
                case "wire":
                case "drink":
                case "calm":
                    return TriggerWireCheck();
                case "bust":
                case "badge":
                    return TriggerFlashBadgeBust();
                case "scorecard":
                    if (!IsLoungeLotLive || !game.IsFinished) return "error shift not finished";
                    bustClimaxTimerSec = 0f;
                    scorecardReady = true;
                    if (finalOutcome == null) finalOutcome = game.ComputeOutcome();
                    if (!finishFanfarePlayed)
                    {
                        finishFanfarePlayed = true;
                        if (finalOutcome.Ending == StingEndingKind.SuspectBusted)
                            audio.Play(StingSfxId.ShiftVictory, 0.52f);
                    }
                    return "ok " + StatusSummary();
                case "claim":
                    return RequestReturnHome(true);
                case "close":
                case "abort":
                    return RequestReturnHome(false);
                default:
                    return "usage: convo dispatch action status|diag|start|talk|choose <1|2|3|gentle|playful|bold|best>|wire|bust|scorecard|claim|close";
            }
        }

        public static string StatusSummary()
        {
            if (game == null) return "phase=" + phase;
            return "lot=" + targetLoungeLotId +
                   " case=" + (game.CaseProfile?.CaseId ?? "?") +
                   " briefing=" + game.IsBriefing +
                   " finished=" + game.IsFinished +
                   " scorecard=" + scorecardReady +
                   " ending=" + game.Ending +
                   " turn=" + Math.Min(UndercoverStingGame.MaxTurns, game.CurrentTurnIndex + 1) + "/" + UndercoverStingGame.MaxTurns +
                   " susp=" + game.Suspicion +
                   " conf=" + game.Confession +
                   " wire=" + game.WireChargesRemaining + "/" + game.MaxWireCharges +
                   " canBust=" + game.CanFlashBadgeBust +
                   " modal=" + activeModal +
                   " suspect=" + game.SuspectName +
                   " informant=" + game.InformantName +
                   " officerAnim=" + lastOfficerClipName +
                   " suspectAnim=" + lastSuspectClipName +
                   " audioCues=" + audio.RecordedCueCount +
                   " assets=[" + spawnedAssetsSummary + "]";
        }

        public static string ExportLastSessionWav(string path, float leadSec = 0f, float tailSec = 1.0f)
        {
            return audio.ExportWav(path, leadSec, tailSec);
        }

        public static string RequestReturnHome(bool applyRewards)
        {
            if (phase == StingPhase.Inactive) return "error not active";

            activeModal = StingModalMode.None;
            UndercoverStingOverlay.Close();
            ConvoOverlay.Clear();
            audio.StopAmbience();

            if (applyRewards && game != null)
            {
                if (!game.IsFinished) game.Abort();
                pendingReturnOutcome = finalOutcome ?? game.ComputeOutcome();
            }
            else
            {
                pendingReturnOutcome = null;
            }

            var screen = UIScreen.Current;
            if (TriggerScreenSwitchLot(screen, homeHouseNumber))
            {
                phase = StingPhase.WaitingForHomeLot;
                return "ok returning home house=" + homeHouseNumber;
            }

            ForceReset();
            return "error failed switching back to home house=" + homeHouseNumber;
        }

        // ---- Scene Diagnostics (`convo dispatch action diag`) ----

        static string BuildSceneDiag()
        {
            if (loungeVm == null) return "error no lounge vm";
            var sb = new System.Text.StringBuilder();
            bool mode3d = FSOEnvironment.Enable3D;
            sb.Append("ok mode3d=").Append(mode3d);

            var ents = new List<VMEntity>(loungeProps);
            if (officerAvatar != null) ents.Add(officerAvatar);
            if (suspectAvatar != null) ents.Add(suspectAvatar);
            if (informantAvatar != null) ents.Add(informantAvatar);

            for (int i = 0; i < ents.Count; i++)
            {
                var e = ents[i];
                if (e == null || e.Dead) continue;
                var p = e.Position;
                var ui = e.WorldUI;
                var scr = GetEntityScreenPos(e, 0f);
                sb.Append(" | ").Append(SafeObjName(e))
                  .Append("(id=").Append(e.ObjectID)
                  .Append(" pos=").Append(p.TileX).Append(",").Append(p.TileY).Append(",L").Append(p.Level)
                  .Append(" dir=").Append(e.Direction)
                  .Append(" cont=").Append(e.Container != null ? e.Container.ObjectID.ToString() : "none")
                  .Append(" slot=").Append(e.ContainerSlot);
                if (ui != null)
                {
                    sb.Append(" uiPos=").Append(ui.Position.X.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append(",").Append(ui.Position.Y.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append(",").Append(ui.Position.Z.ToString("0.0", CultureInfo.InvariantCulture))
                      .Append(" uiL=").Append(ui.Level)
                      .Append(" uiCont=").Append(ui.Container != null ? "Y" : "N")
                      .Append(" uiSlot=").Append(ui.ContainerSlot);
                }
                if (scr.HasValue)
                    sb.Append(" scr=").Append((int)scr.Value.X).Append(",").Append((int)scr.Value.Y);
                sb.Append(")");
            }
            return sb.ToString();
        }

        // ---- Speakeasy / Underground Lounge Construction (100% REAL TS1 ASSETS!) ----

        static void SetupSpeakeasyLot(VM vm)
        {
            try
            {
                loungeVm = vm;
                loungeProps.Clear();
                cruiserGroup = null;
                cruiserLead = null;
                vipTableEntity = null;
                tableLampEntity = null;
                barCounterEntity = null;
                officerAvatar = null;
                suspectAvatar = null;
                informantAvatar = null;
                finishFanfarePlayed = false;
                scorecardReady = false;
                bustClimaxTimerSec = 0f;
                activeModal = StingModalMode.None;
                frameClock.Reset();
                lastFrameSec = -1.0;
                animClock.Reset();
                lastAnimSec = -1.0;

                var content = Content.Get();
                vm.SpeedMultiplier = 1;
                alertSweepRemainingSec = 4.5f;
                ConvoOverlay.Clear();
                DismissBlockingAlerts(vm);

                // 1. Set moody evening Vice Squad lighting (19:30 — warm atmospheric twilight, clearly visible!)
                if (vm.Context?.Clock != null)
                {
                    vm.Context.Clock.Hours = 19;
                    vm.Context.Clock.Minutes = 30;
                }

                // 2. Park real TS1 Police Backup Cruiser outside on the curb!
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

                // 3. Pick clean interior Speakeasy Lounge footprint (8x7 tiles) & clear stray outdoor props
                int roomMinX, roomMinY;
                PickLoungeFootprint(vm, cruiserTile, out roomMinX, out roomMinY);
                int roomW = 8;
                int roomH = 7;
                ClearAreaObjects(vm, roomMinX - 1, roomMinY - 1, roomMinX + roomW + 1, roomMinY + roomH + 2);

                ushort loungeFloorId = ResolveFloorPattern(content, isWalkway: false);
                ushort walkFloorId = ResolveFloorPattern(content, isWalkway: true);
                ushort wallPatId = ResolveWallPattern(content);
                BuildLoungeArchitecture(vm, roomMinX, roomMinY, roomW, roomH, cruiserTile, loungeFloorId, walkFloorId, wallPatId);

                // 4. Furnish the SimCity Underground Speakeasy Lounge with 100% REAL TS1 OBJECTS!
                int tableX = roomMinX + 4;
                int tableY = roomMinY + 3;
                var spawnedLog = new List<string>();

                uint tableGuid = FindCatalogObjectGuid(content,
                    new[] { "table - dining -", "table - cafe", "table - end", "dining table", "end table" },
                    new[] { "picnic", "patio", "outdoor", "magic", "castle", "western", "buffet", "coffee", "left", "right", "part b", "pool", "ping", "chess" },
                    preferSingleTile: true);
                uint chairGuid = FindCatalogObjectGuid(content,
                    new[] { "chair - dining", "dining chair", "chair - office", "chair - living", "chair" },
                    new[] { "recliner", "beach", "lawn", "bar", "stool", "sofa", "couch", "loveseat", "high", "electric", "massage", "throne", "western", "left", "right", "desk", "table" },
                    preferSingleTile: true);
                uint tableLampGuid = FindCatalogObjectGuid(content,
                    new[] { "lamp - table", "table lamp", "lamp - desk", "banker", "lamp" },
                    new[] { "garden", "yard", "lawn", "tiki", "torch", "lantern", "patio", "outdoor", "floor", "wall", "street", "sconce", "hanging", "chandelier", "lava", "post", "path", "pool", "tomb", "magic", "left", "right" },
                    preferSingleTile: true);
                uint floorLampGuid = FindCatalogObjectGuid(content,
                    new[] { "lamp - floor", "floor lamp", "torchere" },
                    new[] { "garden", "yard", "lawn", "tiki", "patio", "outdoor", "street", "post", "table", "wall", "magic", "left", "right" },
                    preferSingleTile: true);
                uint barGuid = FindCatalogObjectGuid(content,
                    new[] { "bacchus", "puckish", "minibar", "bar - classic", "bar - home", "wet bar", "bar - ", "bar" },
                    new[] { "stool", "chair", "barbecue", "bbq", "monkey", "pullup", "weight", "gym", "ballet", "dance", "salad", "sushi", "candy", "chocolate", "soap", "door", "corner", "beach", "tiki", "vegas", "resort", "hotel", "tropical", "luau", "island", "outdoor", "patio", "pool", "espresso", "coffee", "juice", "left", "right", "part b" },
                    preferSingleTile: false);
                uint stereoGuid = FindCatalogObjectGuid(content,
                    new[] { "jukebox", "stereo -", "stereo", "boombox" },
                    new[] { "tv", "television", "table", "chair", "left", "right", "part b", "dj" },
                    preferSingleTile: true);
                uint sofaGuid = FindCatalogObjectGuid(content,
                    new[] { "loveseat", "sofa -", "sofa", "couch" },
                    new[] { "bed", "patio", "outdoor", "lawn", "beach", "left", "right", "part b" },
                    preferSingleTile: false);
                uint plantGuid = FindCatalogObjectGuid(content,
                    new[] { "plant - floor", "ficus", "palm", "fern", "plant" },
                    new[] { "shrub", "hedge", "tree", "flower", "garden", "hanging", "wall", "cactus", "table", "left", "right" },
                    preferSingleTile: true);

                // VIP Lounge Table at (tableX, tableY) — DO NOT freeze until Table Lamp is slotted!
                VMMultitileGroup tableGrp = null;
                if (tableGuid != 0)
                {
                    var tPos = new LotTilePos((short)(tableX * 16), (short)(tableY * 16), 1);
                    tableGrp = vm.Context.CreateObjectInstance(tableGuid, tPos, Direction.NORTH);
                    if (tableGrp != null && tableGrp.Objects != null && tableGrp.Objects.Count > 0)
                    {
                        for (int i = 0; i < tableGrp.Objects.Count; i++)
                            loungeProps.Add(tableGrp.Objects[i]);
                        vipTableEntity = tableGrp.BaseObject ?? tableGrp.Objects[0];
                        spawnedLog.Add("Table=" + SafeObjName(vipTableEntity));
                    }
                }

                // Slot REAL TS1 Table Lamp cleanly onto VIP Lounge Table using Buy-Mode AcceptSlots!
                if (tableLampGuid != 0 && vipTableEntity != null)
                {
                    string lampMode;
                    tableLampEntity = SpawnOnSurface(vm, tableLampGuid, vipTableEntity, Direction.SOUTH, out lampMode);
                    if (tableLampEntity != null)
                    {
                        TryTurnLampOn(tableLampEntity);
                        spawnedLog.Add("TableLamp=" + SafeObjName(tableLampEntity) + "(" + lampMode + ")");
                    }
                }
                if (tableGrp != null) FreezeGroup(tableGrp, pauseThread: true);

                // 2 Facing Lounge Chairs across the VIP Table!
                if (chairGuid != 0)
                {
                    var officerChair = SpawnLoungeProp(vm, chairGuid, tableX, tableY - 1, Direction.SOUTH);
                    var suspectChair = SpawnLoungeProp(vm, chairGuid, tableX, tableY + 1, Direction.NORTH);
                    if (officerChair != null) spawnedLog.Add("Chairs=" + SafeObjName(officerChair));
                }

                // Real TS1 Bar Counter along the left wall
                if (barGuid != 0)
                {
                    barCounterEntity = SpawnLoungeProp(vm, barGuid, roomMinX + 1, roomMinY + 3, Direction.EAST);
                    if (barCounterEntity != null) spawnedLog.Add("Bar=" + SafeObjName(barCounterEntity));
                }

                // Jukebox / Stereo against the back wall
                if (stereoGuid != 0)
                {
                    var st = SpawnLoungeProp(vm, stereoGuid, roomMinX + 2, roomMinY + 1, Direction.SOUTH);
                    if (st != null) spawnedLog.Add("Music=" + SafeObjName(st));
                }

                // Warm Floor Lamps in both rear corners so the speakeasy glows warmly!
                if (floorLampGuid != 0)
                {
                    var fl1 = SpawnLoungeProp(vm, floorLampGuid, roomMinX + 1, roomMinY + 1, Direction.SOUTH);
                    var fl2 = SpawnLoungeProp(vm, floorLampGuid, roomMinX + 6, roomMinY + 1, Direction.SOUTH);
                    if (fl1 != null) { TryTurnLampOn(fl1); spawnedLog.Add("FloorLamp=" + SafeObjName(fl1)); }
                    if (fl2 != null) TryTurnLampOn(fl2);
                }

                // Lounge Sofa along the right wall + Floor Plants near the entrance
                if (sofaGuid != 0)
                {
                    var sf = SpawnLoungeProp(vm, sofaGuid, roomMinX + 6, roomMinY + 3, Direction.WEST);
                    if (sf != null) spawnedLog.Add("Sofa=" + SafeObjName(sf));
                }
                if (plantGuid != 0)
                {
                    var p1 = SpawnLoungeProp(vm, plantGuid, roomMinX + 2, roomMinY + roomH - 1, Direction.SOUTH);
                    var p2 = SpawnLoungeProp(vm, plantGuid, roomMinX + 6, roomMinY + roomH - 1, Direction.SOUTH);
                    if (p1 != null || p2 != null) spawnedLog.Add("Plants=" + SafeObjName(p1 ?? p2));
                }

                spawnedAssetsSummary = string.Join(", ", spawnedLog);

                // 5. Pick 2 DISTINCT verified fully-textured real neighborhood Sims (Suspect + Informant/Bartender)!
                var verifiedSims = CollectVerifiedNeighborhoodSims(content);
                var suspectInfo = verifiedSims.Count > 0
                    ? verifiedSims[Math.Abs(stingSeed) % verifiedSims.Count]
                    : null;
                var informantInfo = verifiedSims.Count > 1
                    ? verifiedSims[(Math.Abs(stingSeed) + 1) % verifiedSims.Count]
                    : null;

                string suspectName = suspectInfo != null ? suspectInfo.Name : "Slick Eddie";
                uint suspectGuid = suspectInfo != null ? suspectInfo.OutfitGuid : GuidFallbackPerson;
                suspectNeighborId = suspectInfo != null ? suspectInfo.NeighborId : (short)0;

                string informantName = informantInfo != null ? informantInfo.Name : "Louie";
                uint informantGuid = informantInfo != null ? informantInfo.OutfitGuid : GuidFallbackPerson;
                informantNeighborId = informantInfo != null ? informantInfo.NeighborId : (short)0;

                // 6. Spawn all 3 Sims on OPEN Level-1 tiles (uiL=1 lighting!), then align to exact WorldUI chair centers!
                // FreeSO WorldUI rule: ObjectComponent at tile (tx, ty) renders at (tx - 0.5, ty - 0.5).
                var officerSpawnTile = new LotTilePos((short)((tableX + 1) * 16), (short)((tableY - 1) * 16), 1);
                officerSeatWorld = new Vector2(tableX - 0.50f, tableY - 1.52f);
                officerBustWorld = new Vector2(tableX - 0.50f, tableY - 1.32f);
                officerAvatar = SpawnUndercoverOfficer(vm, officerSpawnTile);
                if (officerAvatar != null)
                {
                    FreezeEntity(officerAvatar, pauseThread: true);
                    officerAvatar.VisualPosition = new Vector3(officerSeatWorld.X, officerSeatWorld.Y, 0f);
                    officerAvatar.RadianDirection = (float)Math.PI;
                    PlayOfficerClip("sting_seated_idle", loop: true);
                    SelectActiveAvatar(officerAvatar);
                }

                var suspectSpawnTile = new LotTilePos((short)((tableX + 1) * 16), (short)((tableY + 1) * 16), 1);
                suspectSeatWorld = new Vector2(tableX - 0.50f, tableY + 0.52f);
                suspectBustWorld = new Vector2(tableX - 0.50f, tableY + 0.72f);
                suspectAvatar = SpawnVerifiedSim(vm, suspectGuid, suspectName, suspectSpawnTile, verifiedSims);
                if (suspectAvatar != null)
                {
                    FreezeEntity(suspectAvatar, pauseThread: true);
                    suspectAvatar.VisualPosition = new Vector3(suspectSeatWorld.X, suspectSeatWorld.Y, 0f);
                    suspectAvatar.RadianDirection = 0f;
                    PlaySuspectClip("sting_suspect_nervous", loop: true);
                }

                var informantSpawnTile = new LotTilePos((short)((roomMinX + 2) * 16), (short)((roomMinY + 3) * 16), 1);
                informantStandWorld = new Vector2(roomMinX + 1.55f, roomMinY + 2.50f);
                informantAvatar = SpawnVerifiedSim(vm, informantGuid, informantName, informantSpawnTile, verifiedSims);
                if (informantAvatar != null)
                {
                    FreezeEntity(informantAvatar, pauseThread: true);
                    informantAvatar.VisualPosition = new Vector3(informantStandWorld.X, informantStandWorld.Y, 0f);
                    informantAvatar.RadianDirection = (float)(Math.PI * 0.5);
                }

                // 7. Create pure-C# UndercoverStingGame
                game = new UndercoverStingGame(
                    officerName: homeOfficerName,
                    charismaSkillLevel: homeCharismaSkill,
                    logicSkillLevel: homeLogicSkill,
                    suspectName: suspectAvatar != null ? suspectAvatar.Name : suspectName,
                    suspectGuid: suspectGuid,
                    informantName: informantAvatar != null ? informantAvatar.Name : informantName,
                    informantGuid: informantGuid,
                    seed: stingSeed,
                    startInBriefing: true);

                // 8. Frame Simitone's camera dead-center on VIP Lounge Table + Slotted Lamp + Officer + Suspect + Bar!
                FocusCameraOnWorldPoint(new Vector2(tableX - 0.75f, tableY - 0.30f), WorldZoom.Near, zoom3D: 5.5f);

                // 9. Start live speakeasy audio & HUD overlay
                phase = StingPhase.Active;
                audio.StartSession();
                audio.StartAmbience(0.16f);
                audio.Play(StingSfxId.WireClick, 0.45f);
                UndercoverStingOverlay.OpenForRealLot();
                DismissBlockingAlerts(vm);

                Boot.Log("undercover-sting: REAL-ASSET Speakeasy ready! lot=" + targetLoungeLotId +
                         " case=" + game.CaseProfile.CaseTitle +
                         " suspect=" + game.SuspectName +
                         " informant=" + game.InformantName +
                         " assets=[" + spawnedAssetsSummary + "]");
            }
            catch (Exception e)
            {
                Boot.Log("undercover-sting SetupSpeakeasyLot error: " + e.GetType().Name + ": " + e.Message);
                ForceReset();
            }
        }

        static void PlayOfficerClip(string clipName, bool loop)
        {
            officerClip.Play(clipName, 0.14f, 1.0f, loop, true);
            lastOfficerClipName = clipName;
        }

        static void PlaySuspectClip(string clipName, bool loop)
        {
            suspectClip.Play(clipName, 0.14f, 1.0f, loop, true);
            lastSuspectClipName = clipName;
        }

        static void ProcessGameEvents()
        {
            if (game == null) return;
            var evs = game.DrainEvents();
            for (int i = 0; i < evs.Count; i++)
            {
                switch (evs[i].Kind)
                {
                    case "case_start":
                        audio.Play(StingSfxId.WireClick, 0.42f);
                        break;
                    case "dialogue_good":
                        audio.Play(StingSfxId.SlipCaptured, 0.46f);
                        break;
                    case "dialogue_risky":
                        audio.Play(StingSfxId.SuspicionSpike, 0.50f);
                        break;
                    case "wire_check":
                        audio.Play(StingSfxId.WireCalm, 0.46f);
                        break;
                    case "confession_ready":
                        audio.Play(StingSfxId.ConfessionReady, 0.48f);
                        break;
                    case "bust_success":
                        audio.Play(StingSfxId.BadgeFlashBust, 0.58f);
                        break;
                    case "cover_blown":
                        audio.Play(StingSfxId.CoverBlown, 0.52f);
                        break;
                }
            }
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
                                ((homeOfficerNeighborId > 0 && a.GetPersonData(VMPersonDataVariable.NeighborId) == homeOfficerNeighborId) ||
                                 string.Equals((a.Name ?? "").Trim(), homeOfficerName.Trim(), StringComparison.OrdinalIgnoreCase)));
                    }

                    var reward = new CareerDispatchReward
                    {
                        Grade = o.Grade,
                        Summary = o.Summary,
                        BonusSimoleons = o.BonusSimoleons,
                        JobPerformanceDelta = o.JobPerformanceDelta,
                        CharismaXpDelta = o.CharismaXpDelta,
                        LogicXpDelta = o.LogicXpDelta,
                        EnergyDelta = o.EnergyDelta,
                        FunDelta = o.FunDelta,
                        MemoryFact = o.MemoryFact,
                    };
                    string rewardSummary = CareerDispatchDriver.ApplyReward(homeAv, reward);

                    // Apply real neighborhood relationship deltas & Sim Memory records!
                    try
                    {
                        var nb = Content.Get()?.Neighborhood;
                        var officerNb = homeOfficerNeighborId > 0 ? nb?.GetNeighborByID(homeOfficerNeighborId) : null;
                        ApplyNeighborDailyDelta(officerNb, suspectNeighborId, o.SuspectDailyRelDelta);
                        ApplyNeighborDailyDelta(officerNb, informantNeighborId, o.InformantDailyRelDelta);

                        if (!string.IsNullOrWhiteSpace(o.SuspectName))
                        {
                            var sp = new RawSim { Name = homeOfficerName, NeighborId = homeOfficerNeighborId };
                            var ls = new RawSim { Name = o.SuspectName, NeighborId = suspectNeighborId };
                            Boot.Memory.Record(sp, ls, "undercover_sting",
                                o.Ending == StingEndingKind.SuspectBusted ? Tone.Bold : Tone.Gentle,
                                "undercover_sting", o.Ending == StingEndingKind.SuspectBusted ? 3 : 1,
                                subjectTitle: o.MemoryFact);
                            Boot.SaveMemoryAsync();
                        }
                    }
                    catch (Exception re) { Boot.Log("undercover-sting rel save error: " + re.Message); }

                    Boot.Log("undercover-sting: returned home! grade=" + o.Grade +
                             " score=" + o.Score +
                             " rewards=[" + rewardSummary + "]");
                }
            }
            catch (Exception e)
            {
                Boot.Log("undercover-sting CompleteReturnHome error: " + e.Message);
            }
            finally
            {
                ForceReset();
            }
        }

        static void ApplyNeighborDailyDelta(Neighbour officerNb, short targetId, short delta)
        {
            if (officerNb == null || officerNb.Relationships == null || targetId <= 0 || delta == 0) return;
            List<short> v;
            if (!officerNb.Relationships.TryGetValue(targetId, out v) || v == null)
            {
                v = new List<short> { 0, 0, 0, 0 };
                officerNb.Relationships[targetId] = v;
            }
            while (v.Count < 4) v.Add(0);
            v[0] = (short)Math.Max(-100, Math.Min(100, v[0] + delta));
            if (v[0] >= 70) v[1] = 1;
        }

        static void ForceReset()
        {
            phase = StingPhase.Inactive;
            sourceVm = null;
            loungeVm = null;
            game = null;
            finalOutcome = null;
            pendingReturnOutcome = null;
            activeModal = StingModalMode.None;
            loungeProps.Clear();
            vipTableEntity = null;
            tableLampEntity = null;
            barCounterEntity = null;
            officerAvatar = null;
            suspectAvatar = null;
            informantAvatar = null;
            cruiserGroup = null;
            cruiserLead = null;
            scorecardReady = false;
            finishFanfarePlayed = false;
            audio.StopAmbience();
            UndercoverStingOverlay.Close();
        }

        // ---- Verified Textured Avatar & Real TS1 Object Helpers ----

        static VMAvatar SpawnUndercoverOfficer(VM vm, LotTilePos tile)
        {
            uint guid = homeOfficerGuid != 0 ? homeOfficerGuid : GuidFallbackPerson;
            var av = TrySpawnTexturedAvatar(vm, guid, tile);
            if (av == null && guid != GuidFallbackPerson)
                av = TrySpawnTexturedAvatar(vm, GuidFallbackPerson, tile);
            if (av == null) return null;

            av.Name = homeOfficerName;
            // Plainclothes undercover disguise: keep their everyday head & body (NO police uniform!)
            try
            {
                if (av.Avatar != null)
                {
                    if (homeOfficerHead != null) av.Avatar.Head = homeOfficerHead;
                    if (homeOfficerBody != null)
                    {
                        av.Avatar.Body = homeOfficerBody;
                        av.Avatar.Handgroup = homeOfficerBody;
                    }
                }
            }
            catch { }
            return av;
        }

        static VMAvatar SpawnVerifiedSim(VM vm, uint preferredGuid, string displayName, LotTilePos tile, List<VerifiedNeighborInfo> fallbacks)
        {
            VMAvatar av = preferredGuid != 0 ? TrySpawnTexturedAvatar(vm, preferredGuid, tile) : null;
            if (av == null || !AvatarHasLoadedTextures(av))
            {
                if (av != null) { try { av.Delete(true, vm.Context); } catch { } av = null; }
                for (int i = 0; i < fallbacks.Count; i++)
                {
                    if (fallbacks[i].OutfitGuid == preferredGuid) continue;
                    var cand = TrySpawnTexturedAvatar(vm, fallbacks[i].OutfitGuid, tile);
                    if (cand != null && AvatarHasLoadedTextures(cand))
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
            if (av == null || av.Avatar == null) return false;
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
                ushort bodyStrId = obj.OBJ.BodyStringID != 0 ? obj.OBJ.BodyStringID : (ushort)200;
                var str = obj.Resource.Get<STR>(bodyStrId) ?? obj.Resource.Get<STR>(200);
                if (str == null || str.Length < 3) return false;
                string bodySpec = str.GetString(1);
                string headSpec = str.GetString(2);
                if (string.IsNullOrWhiteSpace(bodySpec) || string.IsNullOrWhiteSpace(headSpec)) return false;

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
                    if (!VerifyNeighborHasTextures(content, n.GUID)) continue;

                    var info = new VerifiedNeighborInfo
                    {
                        Name = name.Trim(),
                        NeighborId = n.NeighbourID,
                        OutfitGuid = n.GUID,
                    };
                    bool isRes = !string.IsNullOrEmpty(n.Name) && n.Name.StartsWith("User", StringComparison.OrdinalIgnoreCase);
                    if (isRes) residents.Add(info);
                    else townies.Add(info);
                }

                if (residents.Count >= 2)
                    list.AddRange(residents);
                else
                {
                    list.AddRange(residents);
                    list.AddRange(townies);
                }
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
            string[] skip = { "maid", "gardener", "repair", "pizza", "burglar", "cop", "police", "fire", "grim", "reaper", "santa", "clown", "bear", "raccoon", "skunk", "stray", "cat", "dog", "pet", "servo", "genie", "carpool", "bus", "taxi", "npc", "controller", "portal", "social", "clerk", "vendor", "waiter", "waitress", "bartender", "chef", "cook", "cashier", "janitor", "dj", "host", "bellhop", "butler", "driver", "musician", "sax", "singer", "mime", "magician", "director", "paparazzi", "fan", "bot", "townie", "carnie", "rental", "shack", "ranger", "tourist", "resort", "concierge", "guide", "mascot", "vampire", "skeleton", "bonehilda", "yeti", "shark", "penguin", "mayor" };
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
                    if (item.Price > 0 && item.Price <= 2200) score += 20;

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

        static VMEntity SpawnLoungeProp(VM vm, uint guid, int tx, int ty, Direction dir)
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
                    loungeProps.Add(grp.Objects[i]);
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
                    loungeProps.Add(grp.Objects[i]);
                return lead;
            }
            catch { return null; }
        }

        static void TryTurnLampOn(VMEntity lamp)
        {
            try
            {
                if (lamp == null) return;
                lamp.SetValue(VMStackObjectVariable.LightingContribution, 100);
            }
            catch { }
        }

        static void BuildLoungeArchitecture(
            VM vm, int minX, int minY, int roomW, int roomH, LotTilePos curbTile,
            ushort loungeFloor, ushort walkFloor, ushort wallPat)
        {
            try
            {
                var arch = vm.Context.Architecture;
                if (arch == null) return;

                for (int x = minX; x < minX + roomW; x++)
                    for (int y = minY; y < minY + roomH; y++)
                        arch.SetFloor((short)x, (short)y, 1, new FloorTile { Pattern = loungeFloor }, true);

                // Walkway to the parked backup cruiser
                int walkX = minX + roomW / 2;
                int walkEndY = Math.Min(arch.Height - 2, Math.Max(minY + roomH + 2, curbTile.TileY - 2));
                for (int y = minY + roomH; y <= walkEndY; y++)
                {
                    arch.SetFloor((short)(walkX - 1), (short)y, 1, new FloorTile { Pattern = walkFloor }, true);
                    arch.SetFloor((short)walkX, (short)y, 1, new FloorTile { Pattern = walkFloor }, true);
                }

                // Back cutaway speakeasy walls (North + West)
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
            catch (Exception e) { Boot.Log("undercover-sting architecture build note: " + e.Message); }
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
                        : new[] { "wood", "parquet", "hardwood", "mahogany", "oak", "carpet", "check" };
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
                    string[] prefs = { "brick", "wainscot", "wood", "panel", "mahogany", "red", "plaster" };
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

        static void PickLoungeFootprint(VM vm, LotTilePos curbTile, out int minX, out int minY)
        {
            int w = vm.Context.Architecture.Width;
            int h = vm.Context.Architecture.Height;
            minX = Math.Max(10, Math.Min(w - 14, curbTile.TileX - 4));
            minY = Math.Max(10, Math.Min(h - 16, curbTile.TileY - 11));
        }

        static void KeepLoungeActorsControlled()
        {
            if (cruiserGroup != null) FreezeGroup(cruiserGroup, pauseThread: true);
            if (officerAvatar != null) FreezeEntity(officerAvatar, pauseThread: true);
            if (suspectAvatar != null) FreezeEntity(suspectAvatar, pauseThread: true);
            if (informantAvatar != null) FreezeEntity(informantAvatar, pauseThread: true);
            for (int i = 0; i < loungeProps.Count; i++)
                FreezeEntity(loungeProps[i], pauseThread: true);
        }

        static void FreezeGroup(VMMultitileGroup grp, bool pauseThread = true)
        {
            if (grp?.Objects == null) return;
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

        static bool MatchesEntity(VMEntity a, VMEntity b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b) || a.ObjectID == b.ObjectID) return true;
            if (a.MultitileGroup != null && ReferenceEquals(a.MultitileGroup, b.MultitileGroup)) return true;
            return false;
        }

        static VMPieMenuInteraction MakePieEntry(VMEntity target, StingPieActionKind kind, string label)
        {
            return new VMPieMenuInteraction
            {
                Name = label,
                ID = (byte)(StingPieActionBase + (uint)kind),
                Param0 = 0,
                Entry = new TTABInteraction { TTAIndex = (uint)(StingPieActionBase + (uint)kind) },
                Callee = target,
                Global = false,
                Score = 100
            };
        }

        static string SafeObjName(VMEntity e)
        {
            if (e == null) return "null";
            try
            {
                if (!string.IsNullOrWhiteSpace(e.Name)) return e.Name.Trim();
                if (!string.IsNullOrWhiteSpace(e.Object?.OBJ?.ChunkLabel)) return e.Object.OBJ.ChunkLabel.Trim();
            }
            catch { }
            return "obj#" + e.ObjectID;
        }

        // ---- Camera & Screen Projection (Exact in BOTH 3D and 2D Isometric!) ----

        public static Vector2? GetEntityScreenPos(VMEntity entity, float yOffsetPx = -46f)
        {
            if (loungeVm == null || entity == null || entity.Dead) return null;
            try
            {
                var world = loungeVm.Context?.World;
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
                var world = loungeVm?.Context?.World;
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

        static void CaptureHomeOfficer(VMAvatar av, VM vm, UIScreen screen, int? chaOverride, int? logicOverride)
        {
            homeOfficerName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
            homeOfficerGuid = av?.Object?.OBJ?.GUID ?? 0;
            homeOfficerNeighborId = av != null ? av.GetPersonData(VMPersonDataVariable.NeighborId) : (short)0;
            homeCharismaSkill = chaOverride ?? (av != null ? av.GetPersonData(VMPersonDataVariable.CharismaSkill) / 100 : 4);
            homeLogicSkill = logicOverride ?? (av != null ? av.GetPersonData(VMPersonDataVariable.LogicSkill) / 100 : 4);
            try
            {
                homeOfficerHead = av?.Avatar?.Head;
                homeOfficerBody = av?.Avatar?.Body;
            }
            catch
            {
                homeOfficerHead = null;
                homeOfficerBody = null;
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

        static int PickCleanLoungeLot(int excludeHouse)
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
            catch (Exception e) { Boot.Log("undercover-sting SwitchLot error: " + e.Message); }
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

        static void SelectActiveAvatar(VMAvatar av)
        {
            try
            {
                if (loungeVm != null && av != null)
                    loungeVm.MyUID = av.PersistID;
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
                            vm.SpeedMultiplier = 1;
                    }
                }
            }
            catch { }
        }
    }
}
