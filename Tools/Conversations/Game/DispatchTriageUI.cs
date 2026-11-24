using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Full-Screen 3D "Virtual Pocket Lot" Overlay for Law Enforcement Level 4 ("Desk Sergeant"):
    // 911 Dispatch Triage (DISP-07). Renders the live 3D SimCity PD Precinct Front Desk
    // edge-to-edge while the home lot VM stays safely paused, combining retro 911 phone lines
    // with live 3D Walk-in Neighborhood Citizens, custom Blender desk animations, and procedural SFX.
    public class DispatchTriageOverlay : SimUiElement
    {
        static DispatchTriageOverlay instance;
        static bool isOpen;

        VMAvatar avatar;
        DispatchTriageGame game;
        DispatchTriageOutcome finalOutcome;
        bool rewardsApplied;
        int savedSpeed = 1;
        bool finishSoundPlayed;
        float walkInStepAccumSec;
        bool walkInStepToggle;
        float urgentBeepAccumSec;

        bool prevLeftDown, prevEscDown, prevEnterDown, prevSpaceDown, prevTabDown;
        bool prevD1Down, prevD2Down, prevD3Down, prevD4Down, prevCDown;

        int hoverCaseIndex = -1;
        bool hoverStart, hoverSkip, hoverClose, hoverClaim, hoverCalm;
        bool hoverPatrol, hoverFire, hoverAid, hoverDismiss;

        Rectangle startBtnRect, skipBtnRect, closeBtnRect, claimBtnRect, calmBtnRect;
        Rectangle patrolBtnRect, fireBtnRect, aidBtnRect, dismissBtnRect;
        readonly List<Tuple<int, Rectangle>> queueHitRects = new List<Tuple<int, Rectangle>>(4);

        readonly DispatchTriageSceneRenderer sceneRenderer = new DispatchTriageSceneRenderer();
        readonly VirtualLotTransition transition = new VirtualLotTransition();
        readonly DispatchAudioEngine audio = new DispatchAudioEngine();

        readonly TextStyle titleStyle;
        readonly TextStyle bannerTitleStyle;
        readonly TextStyle bannerSubStyle;
        readonly TextStyle subStyle;
        readonly TextStyle bodyStyle;
        readonly TextStyle smallStyle;
        readonly TextStyle badgeStyle;
        readonly TextStyle cyanStyle;
        readonly TextStyle mintStyle;
        readonly TextStyle amberStyle;
        readonly TextStyle redStyle;
        readonly TextStyle gradeStyle;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        DispatchTriageOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(16, Color.White, true);
            bannerTitleStyle = SimUiTokens.MakeStyle(22, Color.White, true);
            bannerSubStyle = SimUiTokens.MakeStyle(11, SimUiTokens.Cyan, true);
            subStyle = SimUiTokens.MakeStyle(12, new Color(176, 196, 218), true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            smallStyle = SimUiTokens.MakeStyle(11, new Color(228, 240, 252), true);
            badgeStyle = SimUiTokens.MakeStyle(12, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Mint, true);
            amberStyle = SimUiTokens.MakeStyle(12, new Color(255, 206, 92), true);
            redStyle = SimUiTokens.MakeStyle(12, new Color(255, 118, 112), true);
            gradeStyle = SimUiTokens.MakeStyle(40, Color.White, true);
        }

        public static string Open(
            VMAvatar av,
            int? logicLevelOverride = null,
            int? charismaLevelOverride = null,
            int? seed = null)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new DispatchTriageOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);

                instance.avatar = av;
                string simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                int logicLvl = logicLevelOverride.HasValue
                    ? logicLevelOverride.Value
                    : (av != null ? av.GetPersonData(VMPersonDataVariable.LogicSkill) / 100 : 3);
                int charismaLvl = charismaLevelOverride.HasValue
                    ? charismaLevelOverride.Value
                    : (av != null ? av.GetPersonData(VMPersonDataVariable.CharismaSkill) / 100 : 3);

                var callers = CollectNeighborhoodCallers(av);
                instance.game = new DispatchTriageGame(
                    sergeantName: simName,
                    logicSkillLevel: logicLvl,
                    charismaSkillLevel: charismaLvl,
                    neighborhoodCallers: callers,
                    seed: seed,
                    startInBriefing: true);

                instance.finalOutcome = null;
                instance.rewardsApplied = false;
                instance.finishSoundPlayed = false;
                instance.walkInStepAccumSec = 0f;
                instance.walkInStepToggle = false;
                instance.urgentBeepAccumSec = 0f;

                instance.transition.BeginEnter(
                    "SIMCITY PD  •  911 DISPATCH DESK",
                    "PRECINCT FRONT COUNTER  •  VIRTUAL SHIFT LOT (HOME LOT PAUSED)");

                var vm = CareerDispatchDriver.CurrentVM();
                if (vm != null)
                {
                    instance.savedSpeed = vm.SpeedMultiplier > 0 ? vm.SpeedMultiplier : 1;
                    vm.SpeedMultiplier = 0;
                }
                else
                {
                    instance.savedSpeed = 1;
                }

                instance.prevLeftDown = true;
                var kb = Keyboard.GetState();
                instance.prevEscDown = kb.IsKeyDown(Keys.Escape);
                instance.prevEnterDown = kb.IsKeyDown(Keys.Enter);
                instance.prevSpaceDown = kb.IsKeyDown(Keys.Space);
                instance.prevTabDown = kb.IsKeyDown(Keys.Tab);
                instance.prevD1Down = kb.IsKeyDown(Keys.D1);
                instance.prevD2Down = kb.IsKeyDown(Keys.D2);
                instance.prevD3Down = kb.IsKeyDown(Keys.D3);
                instance.prevD4Down = kb.IsKeyDown(Keys.D4);
                instance.prevCDown = kb.IsKeyDown(Keys.C);

                instance.Visible = true;
                isOpen = true;

                instance.audio.StartSession();
                instance.audio.StartAmbience(0.18f);
                instance.audio.Play(DispatchSfxId.ArrivalCue, 0.44f);
                Boot.Log("dispatch-triage: opened 3D Precinct Front Desk for " + simName + " (Logic=" + logicLvl + " Charisma=" + charismaLvl + ")");
                return "ok " + instance.StatusText();
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        public static void Close(bool applyRewards)
        {
            if (instance == null) return;
            try
            {
                if (applyRewards && !instance.rewardsApplied && instance.game != null)
                {
                    var outcome = instance.finalOutcome ?? instance.game.ComputeOutcome();
                    ApplyOutcomeRewards(instance.avatar, outcome);
                    instance.rewardsApplied = true;
                }
            }
            catch (Exception e) { Boot.Log("dispatch-triage reward error: " + e.Message); }

            instance.audio.StopAmbience();
            var vm = CareerDispatchDriver.CurrentVM();
            if (vm != null && vm.SpeedMultiplier == 0)
                vm.SpeedMultiplier = instance.savedSpeed > 0 ? instance.savedSpeed : 1;

            isOpen = false;
            instance.Visible = false;
            if (instance.Parent != null)
                instance.Parent.Remove(instance);
        }

        public static string StatusSummary()
        {
            return IsOpen ? instance.StatusText() : "closed";
        }

        public static string ExportLastSessionWav(string path, float leadSec = 0f, float tailSec = 1.0f)
        {
            if (instance == null) return "err no dispatch triage session";
            return instance.audio.ExportSessionWav(path, leadSec, tailSec);
        }

        public static string HandleTestAction(string cmd)
        {
            if (!IsOpen || instance.game == null) return "err dispatch triage not open";
            string c = (cmd ?? "").Trim().ToLowerInvariant();
            var g = instance.game;

            if (c == "start" || c == "begin")
            {
                g.BeginShift();
                instance.ProcessGameEvents();
                return "ok " + instance.StatusText();
            }
            if (c == "patrol" || c == "1")
            {
                bool ok = g.RouteSelectedCase(DispatchRouteAction.DispatchPatrol);
                instance.ProcessGameEvents();
                return (ok ? "ok " : "busy ") + instance.StatusText();
            }
            if (c == "fire" || c == "2")
            {
                bool ok = g.RouteSelectedCase(DispatchRouteAction.DispatchFire);
                instance.ProcessGameEvents();
                return (ok ? "ok " : "busy ") + instance.StatusText();
            }
            if (c == "aid" || c == "community" || c == "3")
            {
                bool ok = g.RouteSelectedCase(DispatchRouteAction.CommunityAid);
                instance.ProcessGameEvents();
                return (ok ? "ok " : "busy ") + instance.StatusText();
            }
            if (c == "dismiss" || c == "prank" || c == "4")
            {
                bool ok = g.RouteSelectedCase(DispatchRouteAction.DismissCall);
                instance.ProcessGameEvents();
                return (ok ? "ok " : "busy ") + instance.StatusText();
            }
            if (c == "auto" || c == "correct")
            {
                var sel = g.SelectedCase;
                if (sel == null) return "err no active case";
                bool ok = g.RouteSelectedCase(sel.CorrectAction);
                instance.ProcessGameEvents();
                return (ok ? "ok " : "busy ") + instance.StatusText();
            }
            if (c == "calm" || c == "c")
            {
                bool ok = g.CalmSelectedCaller();
                instance.ProcessGameEvents();
                return (ok ? "ok " : "err ") + instance.StatusText();
            }
            if (c == "cycle" || c == "tab")
            {
                g.CycleSelectedCase();
                return "ok " + instance.StatusText();
            }
            if (c == "select_walkin")
            {
                var wi = g.GetActiveCases().FirstOrDefault(x => x.Channel == DispatchCallChannel.WalkIn);
                if (wi == null) return "err no active walkin";
                g.SelectCase(wi.CaseIndex);
                return "ok " + instance.StatusText();
            }
            if (c.StartsWith("select "))
            {
                int idx;
                if (int.TryParse(c.Substring(7).Trim(), out idx) && g.SelectCase(idx))
                    return "ok " + instance.StatusText();
                return "err invalid case index";
            }
            if (c == "claim" || c == "finish")
            {
                if (g.IsFinished)
                {
                    Close(true);
                    return "ok claimed";
                }
                return "err shift not finished";
            }
            if (c == "skip" || c == "close")
            {
                Close(false);
                return "ok closed";
            }
            return "err unknown triage action: " + c;
        }

        string StatusText()
        {
            if (game == null) return "none";
            var sel = game.SelectedCase;
            var wi = game.ActiveWalkIn;
            return "triage: briefing=" + game.IsBriefing +
                   " finished=" + game.IsFinished +
                   " resolved=" + game.ResolvedCount + "/" + game.TotalCases +
                   " active=" + game.GetActiveCases().Count +
                   " score=" + game.Score +
                   " gesture=" + game.CurrentGesture +
                   " sel=" + (sel != null ? (sel.CaseIndex + ":" + sel.Channel + ":" + sel.CorrectAction) : "none") +
                   " walkin=" + (wi != null ? (wi.CallerName + "@" + wi.WalkInApproach.ToString("0.00", CultureInfo.InvariantCulture)) : "none") +
                   " units=P" + game.AvailablePatrolUnits + "F" + game.AvailableFireUnits + "C" + game.AvailableCommunityUnits +
                   " audioCues=" + audio.RecordedCueCount +
                   " [" + sceneRenderer.DiagnosticSummary() + "]";
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (!isOpen || game == null) return;
            var gd = batch != null ? batch.GraphicsDevice : GameFacade.GraphicsDevice;
            sceneRenderer.PreDraw(gd, game, avatar);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || game == null) return;

            float dt = 1f / 60f;
            if (transition.Tick(dt))
            {
                Close(transition.ExitApplyRewards);
                return;
            }

            if (!game.IsBriefing && !game.IsFinished)
            {
                game.Tick(dt);
                ProcessGameEvents();

                // Footsteps while 3D Walk-in townie strides across marble precinct floor!
                if (sceneRenderer.WalkInIsWalking)
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

                // Low-urgency subtle warning beep if any call drops below 25%
                bool anyUrgent = game.GetActiveCases().Any(c => c.UrgencyFraction < 0.25f);
                if (anyUrgent)
                {
                    urgentBeepAccumSec += dt;
                    if (urgentBeepAccumSec >= 1.35f)
                    {
                        urgentBeepAccumSec = 0f;
                        audio.Play(DispatchSfxId.UrgentBeep, 0.24f);
                    }
                }
                else
                {
                    urgentBeepAccumSec = 0f;
                }
            }

            if (game.IsFinished && finalOutcome == null)
            {
                finalOutcome = game.ComputeOutcome();
                if (!finishSoundPlayed)
                {
                    finishSoundPlayed = true;
                    audio.Play(DispatchSfxId.ShiftFanfare, 0.50f);
                }
            }

            HandleInput();
        }

        void ProcessGameEvents()
        {
            var events = game.DrainEvents();
            if (events == null) return;
            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                switch (ev.Kind)
                {
                    case "call_arrived":
                        audio.Play(DispatchSfxId.PhoneRing, 0.44f);
                        break;
                    case "walkin_arrived":
                        audio.Play(DispatchSfxId.WalkInBell, 0.48f);
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

        void PlayDispatchSfx(DispatchRouteAction action)
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

        void HandleInput()
        {
            var ms = Mouse.GetState();
            var kb = Keyboard.GetState();
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            var pt = new Point((int)(ms.X / scale), (int)(ms.Y / scale));
            bool leftDown = ms.LeftButton == ButtonState.Pressed;
            bool clicked = leftDown && !prevLeftDown;

            bool escPressed = kb.IsKeyDown(Keys.Escape) && !prevEscDown;
            bool enterPressed = kb.IsKeyDown(Keys.Enter) && !prevEnterDown;
            bool spacePressed = kb.IsKeyDown(Keys.Space) && !prevSpaceDown;
            bool tabPressed = kb.IsKeyDown(Keys.Tab) && !prevTabDown;
            bool d1Pressed = kb.IsKeyDown(Keys.D1) && !prevD1Down;
            bool d2Pressed = kb.IsKeyDown(Keys.D2) && !prevD2Down;
            bool d3Pressed = kb.IsKeyDown(Keys.D3) && !prevD3Down;
            bool d4Pressed = kb.IsKeyDown(Keys.D4) && !prevD4Down;
            bool cPressed = kb.IsKeyDown(Keys.C) && !prevCDown;

            hoverStart = startBtnRect.Contains(pt);
            hoverSkip = skipBtnRect.Contains(pt);
            hoverClose = closeBtnRect.Contains(pt);
            hoverClaim = claimBtnRect.Contains(pt);
            hoverCalm = calmBtnRect.Contains(pt);
            hoverPatrol = patrolBtnRect.Contains(pt);
            hoverFire = fireBtnRect.Contains(pt);
            hoverAid = aidBtnRect.Contains(pt);
            hoverDismiss = dismissBtnRect.Contains(pt);

            hoverCaseIndex = -1;
            for (int i = 0; i < queueHitRects.Count; i++)
            {
                if (queueHitRects[i].Item2.Contains(pt))
                {
                    hoverCaseIndex = queueHitRects[i].Item1;
                    break;
                }
            }

            if (transition.Phase != VirtualLotPhase.Exiting)
            {
                if (game.IsBriefing)
                {
                    if ((clicked && hoverStart) || enterPressed || spacePressed)
                    {
                        game.BeginShift();
                        ProcessGameEvents();
                    }
                    else if ((clicked && hoverSkip) || escPressed)
                    {
                        transition.BeginExit(false);
                    }
                }
                else if (game.IsFinished)
                {
                    if ((clicked && hoverClaim) || enterPressed || spacePressed)
                    {
                        transition.BeginExit(true);
                    }
                    else if (escPressed)
                    {
                        transition.BeginExit(true);
                    }
                }
                else
                {
                    if ((clicked && hoverClose) || escPressed)
                    {
                        transition.BeginExit(false);
                    }
                    else if (clicked && hoverCaseIndex >= 0)
                    {
                        if (game.SelectCase(hoverCaseIndex))
                            audio.Play(DispatchSfxId.PhonePickup, 0.36f);
                    }
                    else if (tabPressed)
                    {
                        if (game.CycleSelectedCase())
                            audio.Play(DispatchSfxId.PhonePickup, 0.34f);
                    }
                    else if ((clicked && hoverPatrol) || d1Pressed)
                    {
                        game.RouteSelectedCase(DispatchRouteAction.DispatchPatrol);
                        ProcessGameEvents();
                    }
                    else if ((clicked && hoverFire) || d2Pressed)
                    {
                        game.RouteSelectedCase(DispatchRouteAction.DispatchFire);
                        ProcessGameEvents();
                    }
                    else if ((clicked && hoverAid) || d3Pressed)
                    {
                        game.RouteSelectedCase(DispatchRouteAction.CommunityAid);
                        ProcessGameEvents();
                    }
                    else if ((clicked && hoverDismiss) || d4Pressed)
                    {
                        game.RouteSelectedCase(DispatchRouteAction.DismissCall);
                        ProcessGameEvents();
                    }
                    else if ((clicked && hoverCalm) || cPressed)
                    {
                        game.CalmSelectedCaller();
                        ProcessGameEvents();
                    }
                }
            }

            prevLeftDown = leftDown;
            prevEscDown = kb.IsKeyDown(Keys.Escape);
            prevEnterDown = kb.IsKeyDown(Keys.Enter);
            prevSpaceDown = kb.IsKeyDown(Keys.Space);
            prevTabDown = kb.IsKeyDown(Keys.Tab);
            prevD1Down = kb.IsKeyDown(Keys.D1);
            prevD2Down = kb.IsKeyDown(Keys.D2);
            prevD3Down = kb.IsKeyDown(Keys.D3);
            prevD4Down = kb.IsKeyDown(Keys.D4);
            prevCDown = kb.IsKeyDown(Keys.C);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen || game == null || batch == null) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float fsw, fsh;
            GetScreenSize(batch, out fsw, out fsh);
            int sw = Math.Max(800, (int)Math.Round(fsw));
            int sh = Math.Max(600, (int)Math.Round(fsh));
            var fullRect = new Rectangle(0, 0, sw, sh);

            // 1. Draw Full-Screen Live 3D Precinct Front Desk edge-to-edge!
            if (sceneRenderer.HasLiveFrame && sceneRenderer.TargetTexture != null)
                batch.Draw(sceneRenderer.TargetTexture, fullRect, Color.White);
            else
                batch.Draw(px, fullRect, new Color(22, 32, 48));

            startBtnRect = skipBtnRect = closeBtnRect = claimBtnRect = calmBtnRect = Rectangle.Empty;
            patrolBtnRect = fireBtnRect = aidBtnRect = dismissBtnRect = Rectangle.Empty;
            queueHitRects.Clear();

            // 2. Draw Overhead 3D Speech Callouts above Desk Sergeant & Live 3D Walk-in Citizen!
            if (!game.IsBriefing && !game.IsFinished)
                DrawWorldSpeechCallouts(batch, px, sw, sh);

            // 3. Draw Briefing, Active 911 Switchboard HUD, or S/A/B/C Scorecard
            if (game.IsBriefing)
                DrawBriefingCard(batch, px, sw, sh);
            else if (game.IsFinished)
                DrawScorecard(batch, px, sw, sh);
            else
                DrawActiveSwitchboardHud(batch, px, sw, sh);

            // 4. Cinematic Virtual Pocket Lot Fade & Arrival Banner
            DrawTransitionOverlay(batch, px, sw, sh);
        }

        void DrawWorldSpeechCallouts(UISpriteBatch batch, Texture2D px, int sw, int sh)
        {
            bool showSgt = game.SergeantSpeechTimerSec > 0.05f && !string.IsNullOrWhiteSpace(game.SergeantSpokenLine);
            var wi = game.ActiveWalkIn;
            bool showWalkIn = wi != null && sceneRenderer.WalkInVisible && !wi.WalkInLeaving && wi.WalkInApproach >= 0.85f;

            // Desk Sergeant spoken radio/phone confirmation bubble (upper-left lane above counter)
            if (showSgt)
            {
                int cx = (int)(Math.Max(0.36f, Math.Min(0.56f, sceneRenderer.SergeantHeadNorm.X)) * sw);
                int cy = (int)(Math.Max(0.14f, Math.Min(0.34f, sceneRenderer.SergeantHeadNorm.Y)) * sh) - 52;
                DrawSpeechPill(batch, px, sw, cx, cy, "SGT. " + game.SergeantName.ToUpperInvariant(),
                    "\u201C" + game.SergeantSpokenLine + "\u201D", SimUiTokens.Cyan, 326, sw - 20);
            }

            // Live 3D Walk-in Citizen bubble once standing at the front counter (offset cleanly right & lower if Sgt. is speaking)
            if (showWalkIn)
            {
                int cx = (int)(Math.Max(0.66f, Math.Min(0.84f, sceneRenderer.WalkInHeadNorm.X)) * sw);
                int cy = (int)(Math.Max(0.22f, Math.Min(0.46f, sceneRenderer.WalkInHeadNorm.Y)) * sh) - (showSgt ? 6 : 42);
                int minX = showSgt ? Math.Max(326, sw / 2 + 48) : 326;
                DrawSpeechPill(batch, px, sw, cx, cy, wi.CallerName.ToUpperInvariant() + " (WALK-IN)",
                    "\u201C" + wi.TranscriptLine1 + "\u201D", SimUiTokens.Mint, minX, sw - 20);
            }
        }

        void DrawSpeechPill(UISpriteBatch batch, Texture2D px, int sw, int centerX, int topY, string speaker, string line, Color accent, int minX, int maxRight)
        {
            string trimmed = Truncate(line, 56);
            int w = Math.Max(220, Math.Min(400, (int)bodyStyle.MeasureString(trimmed).X + 28));
            int h = 48;
            int x = Math.Max(minX, Math.Min(maxRight - w, centerX - w / 2));
            int y = Math.Max(68, topY);
            var r = new Rectangle(x, y, w, h);

            DrawRoundedPanel(batch, px, r, 10, new Color(10, 18, 30, 224), new Color((int)accent.R, (int)accent.G, (int)accent.B, 195), 2);
            DrawLocalString(batch, speaker, new Vector2(x + 12, y + 6), accent == SimUiTokens.Mint ? mintStyle : cyanStyle);
            DrawLocalString(batch, trimmed, new Vector2(x + 12, y + 23), bodyStyle);
        }

        void DrawBriefingCard(UISpriteBatch batch, Texture2D px, int sw, int sh)
        {
            int w = Math.Min(500, sw - 48);
            int h = 424;
            int x = 24;
            int y = Math.Max(82, (sh - h) / 2);
            var card = new Rectangle(x, y, w, h);

            DrawRoundedPanel(batch, px, card, 14, new Color(12, 20, 34, 232), new Color(68, 178, 236, 170), 2);

            int cy = y + 18;
            DrawLocalString(batch, "LAW ENFORCEMENT  •  LEVEL 4", new Vector2(x + 20, cy), cyanStyle);
            cy += 22;
            DrawLocalString(batch, "DESK SERGEANT: 911 TRIAGE", new Vector2(x + 20, cy), titleStyle);
            cy += 26;
            DrawLocalString(batch, "Officer on Duty: Sgt. " + game.SergeantName, new Vector2(x + 20, cy), mintStyle);
            cy += 26;

            batch.Draw(px, new Rectangle(x + 20, cy, w - 40, 1), new Color(72, 130, 180, 90));
            cy += 14;

            DrawLocalString(batch, "Juggle incoming 911 phone lines AND live 3D", new Vector2(x + 20, cy), bodyStyle);
            cy += 20;
            DrawLocalString(batch, "walk-in townies at the SimCity PD Front Desk:", new Vector2(x + 20, cy), bodyStyle);
            cy += 28;

            DrawLocalString(batch, "[1] PATROL (2 units) — Burglars & break-ins", new Vector2(x + 24, cy), cyanStyle);
            cy += 22;
            DrawLocalString(batch, "[2] FIRE DEPT (1 unit) — Stove, grill & spark fires", new Vector2(x + 24, cy), amberStyle);
            cy += 22;
            DrawLocalString(batch, "[3] COMMUNITY AID (2) — Raccoons, pets & noise", new Vector2(x + 24, cy), mintStyle);
            cy += 22;
            DrawLocalString(batch, "[4] DISMISS (Instant) — Pranks & wrong numbers", new Vector2(x + 24, cy), subStyle);
            cy += 28;

            DrawLocalString(batch, "SKILL BONUSES:", new Vector2(x + 20, cy), cyanStyle);
            cy += 20;
            string logicTxt = game.LogicCluesUnlocked
                ? "Logic " + game.LogicSkillLevel + " — Dispatch Clues & Prank Tells UNLOCKED!"
                : "Logic " + game.LogicSkillLevel + " — Reach Logic 2 to reveal caller tells.";
            DrawLocalString(batch, "• " + logicTxt, new Vector2(x + 24, cy), game.LogicCluesUnlocked ? mintStyle : subStyle);
            cy += 20;
            DrawLocalString(batch, "• Charisma " + game.CharismaSkillLevel + " — Calm Caller [C] (" + game.CalmChargesRemaining + " charges, +6s)", new Vector2(x + 24, cy), bodyStyle);
            cy += 34;

            startBtnRect = new Rectangle(x + 20, cy, w - 160, 44);
            skipBtnRect = new Rectangle(x + w - 130, cy, 110, 44);

            DrawRoundedPanel(batch, px, startBtnRect, 10,
                hoverStart ? new Color(46, 152, 224) : new Color(32, 118, 186),
                new Color(142, 224, 255), 2);
            DrawCentered(batch, "OPEN 911 BOARD [ENTER]", startBtnRect, badgeStyle);

            DrawRoundedPanel(batch, px, skipBtnRect, 10,
                hoverSkip ? new Color(68, 78, 96) : new Color(42, 50, 64),
                new Color(120, 136, 158), 1);
            DrawCentered(batch, "SKIP [ESC]", skipBtnRect, subStyle);
        }

        void DrawActiveSwitchboardHud(UISpriteBatch batch, Texture2D px, int sw, int sh)
        {
            // ---- 1. Slim Top Precinct Bar ----
            var topBar = new Rectangle(16, 12, sw - 32, 48);
            DrawRoundedPanel(batch, px, topBar, 12, new Color(11, 19, 32, 224), new Color(68, 168, 226, 150), 2);

            string topTitle = "SIMCITY PD  •  911 DISPATCH DESK";
            DrawLocalString(batch, topTitle, new Vector2(topBar.X + 16, topBar.Y + 14), titleStyle);
            int afterTitleX = topBar.X + 16 + (int)Math.Ceiling(titleStyle.MeasureString(topTitle).X) + 28;
            DrawLocalString(batch, "CASES " + game.ResolvedCount + "/" + game.TotalCases, new Vector2(afterTitleX, topBar.Y + 16), cyanStyle);
            DrawLocalString(batch, "SCORE " + game.Score, new Vector2(afterTitleX + 106, topBar.Y + 16), mintStyle);

            // Unit Pool Status Pills
            int pillX = afterTitleX + 214;
            DrawUnitPill(batch, px, pillX, topBar.Y + 9, 132, "PATROL", game.AvailablePatrolUnits, DispatchTriageGame.MaxPatrolUnits, game.NextPatrolReadySec, new Color(44, 122, 206));
            DrawUnitPill(batch, px, pillX + 142, topBar.Y + 9, 120, "FIRE", game.AvailableFireUnits, DispatchTriageGame.MaxFireUnits, game.NextFireReadySec, new Color(214, 78, 46));
            DrawUnitPill(batch, px, pillX + 272, topBar.Y + 9, 132, "AID", game.AvailableCommunityUnits, DispatchTriageGame.MaxCommunityUnits, game.NextCommunityReadySec, new Color(36, 156, 132));

            closeBtnRect = new Rectangle(topBar.Right - 108, topBar.Y + 9, 96, 30);
            DrawRoundedPanel(batch, px, closeBtnRect, 8,
                hoverClose ? new Color(168, 52, 58) : new Color(54, 64, 82),
                new Color(140, 154, 178), 1);
            DrawCentered(batch, "END [ESC]", closeBtnRect, smallStyle);

            // ---- 2. Floating Action Feedback Toast (top-center) ----
            if (game.FeedbackBannerTimerSec > 0.05f && !string.IsNullOrWhiteSpace(game.LastFeedbackBanner))
            {
                int fw = Math.Max(280, Math.Min(540, (int)badgeStyle.MeasureString(game.LastFeedbackBanner).X + 40));
                var fbRect = new Rectangle((sw - fw) / 2 + 80, 68, fw, 32);
                Color fill = game.LastFeedbackPositive ? new Color(20, 72, 54, 232) : new Color(92, 28, 32, 232);
                Color border = game.LastFeedbackPositive ? SimUiTokens.Mint : new Color(255, 108, 96);
                DrawRoundedPanel(batch, px, fbRect, 10, fill, border, 2);
                DrawCentered(batch, game.LastFeedbackBanner, fbRect, badgeStyle);
            }

            // ---- 3. Left Compact Switchboard Queue (up to 3 active lines / walk-in) ----
            var active = game.GetActiveCases();
            var sel = game.SelectedCase;
            int qx = 16;
            int qy = 74;
            int qw = 300;

            var qHdr = new Rectangle(qx, qy, qw, 28);
            DrawRoundedPanel(batch, px, qHdr, 8, new Color(14, 24, 40, 218), new Color(68, 168, 226, 130), 1);
            DrawLocalString(batch, "INCOMING QUEUE (" + active.Count + "/3)   [TAB]", new Vector2(qx + 12, qy + 6), cyanStyle);
            qy += 34;

            if (active.Count == 0)
            {
                var emptyR = new Rectangle(qx, qy, qw, 64);
                DrawRoundedPanel(batch, px, emptyR, 10, new Color(14, 22, 36, 190), new Color(64, 96, 132, 110), 1);
                DrawLocalString(batch, "Switchboard clear...", new Vector2(qx + 14, qy + 14), mintStyle);
                DrawLocalString(batch, "Waiting for next 911 call or walk-in.", new Vector2(qx + 14, qy + 34), subStyle);
            }
            else
            {
                for (int i = 0; i < active.Count; i++)
                {
                    var c = active[i];
                    bool isSelected = sel != null && sel.CaseIndex == c.CaseIndex;
                    bool isHover = hoverCaseIndex == c.CaseIndex;
                    var cardR = new Rectangle(qx, qy, qw, 92);
                    queueHitRects.Add(Tuple.Create(c.CaseIndex, cardR));

                    Color borderCol = isSelected
                        ? (c.Channel == DispatchCallChannel.WalkIn ? SimUiTokens.Mint : SimUiTokens.Cyan)
                        : (isHover ? new Color(142, 188, 224) : new Color(66, 92, 124, 150));
                    Color fillCol = isSelected
                        ? new Color(20, 38, 62, 238)
                        : new Color(12, 20, 34, 214);
                    DrawRoundedPanel(batch, px, cardR, 10, fillCol, borderCol, isSelected ? 2 : 1);

                    string slotTag = c.Channel == DispatchCallChannel.WalkIn
                        ? "WALK-IN COUNTER"
                        : ("911 LINE " + (c.SlotNumber + 1));
                    DrawLocalString(batch, slotTag, new Vector2(qx + 12, qy + 8),
                        c.Channel == DispatchCallChannel.WalkIn ? mintStyle : amberStyle);

                    string secTxt = c.RemainingUrgencySec.ToString("0.0", CultureInfo.InvariantCulture) + "s";
                    DrawLocalString(batch, secTxt, new Vector2(qx + qw - 48, qy + 8),
                        c.UrgencyFraction < 0.28f ? redStyle : bodyStyle);

                    DrawLocalString(batch, Truncate(c.CallerName + " — " + c.Address, 34), new Vector2(qx + 12, qy + 27), bodyStyle);
                    DrawLocalString(batch, Truncate(c.SubjectTitle, 34), new Vector2(qx + 12, qy + 46), cyanStyle);

                    // Urgency countdown progress bar
                    var barBg = new Rectangle(qx + 12, qy + 70, qw - 24, 10);
                    batch.Draw(px, barBg, new Color(26, 36, 52));
                    int fillW = (int)Math.Round(barBg.Width * c.UrgencyFraction);
                    Color barCol = c.UrgencyFraction > 0.50f
                        ? SimUiTokens.Mint
                        : (c.UrgencyFraction > 0.25f ? new Color(246, 184, 56) : new Color(242, 72, 68));
                    if (fillW > 0)
                        batch.Draw(px, new Rectangle(barBg.X, barBg.Y, fillW, barBg.Height), barCol);

                    qy += 100;
                }
            }

            // ---- 4. Bottom Compact Dispatch Console (Active Transcript + 4 Triage Actions) ----
            int ch = 152;
            var console = new Rectangle(16, sh - ch - 14, sw - 32, ch);
            DrawRoundedPanel(batch, px, console, 14, new Color(11, 19, 32, 236), new Color(68, 168, 226, 175), 2);

            int splitX = console.X + (int)(console.Width * 0.54f);
            batch.Draw(px, new Rectangle(splitX, console.Y + 14, 1, console.Height - 28), new Color(72, 120, 168, 110));

            if (sel != null)
            {
                int tx = console.X + 18;
                int ty = console.Y + 12;
                string badge = sel.Channel == DispatchCallChannel.WalkIn
                    ? "LIVE 3D WALK-IN AT FRONT COUNTER"
                    : "INCOMING 911 EMERGENCY CALL";
                DrawLocalString(batch, badge + "  •  " + sel.CallerName.ToUpperInvariant() + " (" + sel.Address + ")",
                    new Vector2(tx, ty), sel.Channel == DispatchCallChannel.WalkIn ? mintStyle : amberStyle);
                ty += 22;

                DrawLocalString(batch, "\u201C" + Truncate(sel.TranscriptLine1, 74), new Vector2(tx, ty), bodyStyle);
                ty += 20;
                DrawLocalString(batch, Truncate(sel.TranscriptLine2, 74) + "\u201D", new Vector2(tx, ty), bodyStyle);
                ty += 24;

                if (game.LogicCluesUnlocked)
                    DrawLocalString(batch, "LOGIC CLUE: " + Truncate(sel.LogicClue, 68), new Vector2(tx, ty), cyanStyle);
                else
                    DrawLocalString(batch, "Reach Logic 2 to unlock dispatch audio & visual tells.", new Vector2(tx, ty), subStyle);

                // Calm Caller Charisma button
                calmBtnRect = new Rectangle(tx, console.Bottom - 40, 232, 30);
                bool canCalm = game.CalmChargesRemaining > 0;
                DrawRoundedPanel(batch, px, calmBtnRect, 8,
                    canCalm ? (hoverCalm ? new Color(46, 134, 128) : new Color(28, 92, 92)) : new Color(34, 42, 54),
                    canCalm ? SimUiTokens.Mint : new Color(76, 88, 106), 1);
                DrawCentered(batch, "[C] CALM CALLER +6s (" + game.CalmChargesRemaining + " left)", calmBtnRect, smallStyle);
            }
            else
            {
                DrawLocalString(batch, "ALL LINES STANDING BY — WATCH THE PRECINCT COUNTER & SWITCHBOARD",
                    new Vector2(console.X + 20, console.Y + 58), mintStyle);
            }

            // Right half: 2x2 Big Color-Coded Dispatch Buttons!
            int rx = splitX + 18;
            int rw = console.Right - rx - 18;
            int bw = (rw - 12) / 2;
            int bh = 54;
            int by1 = console.Y + 16;
            int by2 = by1 + bh + 12;

            patrolBtnRect = new Rectangle(rx, by1, bw, bh);
            fireBtnRect = new Rectangle(rx + bw + 12, by1, bw, bh);
            aidBtnRect = new Rectangle(rx, by2, bw, bh);
            dismissBtnRect = new Rectangle(rx + bw + 12, by2, bw, bh);

            string pSub = game.AvailablePatrolUnits > 0
                ? (game.AvailablePatrolUnits + "/" + DispatchTriageGame.MaxPatrolUnits + " READY • Burglars / Crime")
                : ("BUSY — Ready in " + game.NextPatrolReadySec.ToString("0.0") + "s");
            DrawDispatchActionBtn(batch, px, patrolBtnRect, "[1] DISPATCH PATROL", pSub,
                hoverPatrol, game.AvailablePatrolUnits > 0 && sel != null, new Color(30, 96, 178), new Color(96, 192, 255));

            string fSub = game.AvailableFireUnits > 0
                ? (game.AvailableFireUnits + "/" + DispatchTriageGame.MaxFireUnits + " READY • Fires / Smoke")
                : ("BUSY — Ready in " + game.NextFireReadySec.ToString("0.0") + "s");
            DrawDispatchActionBtn(batch, px, fireBtnRect, "[2] DISPATCH FIRE", fSub,
                hoverFire, game.AvailableFireUnits > 0 && sel != null, new Color(184, 62, 34), new Color(255, 166, 94));

            string aSub = game.AvailableCommunityUnits > 0
                ? (game.AvailableCommunityUnits + "/" + DispatchTriageGame.MaxCommunityUnits + " READY • Pets / Raccoons / Noise")
                : ("BUSY — Ready in " + game.NextCommunityReadySec.ToString("0.0") + "s");
            DrawDispatchActionBtn(batch, px, aidBtnRect, "[3] COMMUNITY AID", aSub,
                hoverAid, game.AvailableCommunityUnits > 0 && sel != null, new Color(26, 134, 114), SimUiTokens.Mint);

            DrawDispatchActionBtn(batch, px, dismissBtnRect, "[4] DISMISS / HANG UP", "INSTANT • Pranks & Non-Emergencies",
                hoverDismiss, sel != null, new Color(84, 72, 108), new Color(204, 182, 242));
        }

        void DrawUnitPill(UISpriteBatch batch, Texture2D px, int x, int y, int w, string label, int avail, int max, float nextReady, Color accent)
        {
            var r = new Rectangle(x, y, w, 30);
            bool ready = avail > 0;
            DrawRoundedPanel(batch, px, r, 8,
                ready ? new Color(accent.R / 3, accent.G / 3, accent.B / 3, 210) : new Color(64, 34, 30, 210),
                ready ? accent : new Color(246, 168, 62), 1);
            string txt = ready
                ? (label + " " + avail + "/" + max)
                : (label + " " + nextReady.ToString("0.0", CultureInfo.InvariantCulture) + "s");
            DrawCentered(batch, txt, r, smallStyle);
        }

        void DrawDispatchActionBtn(UISpriteBatch batch, Texture2D px, Rectangle r, string title, string subtitle, bool hover, bool enabled, Color baseCol, Color borderCol)
        {
            Color fill = !enabled
                ? new Color(32, 38, 48, 200)
                : (hover ? new Color(Math.Min(255, baseCol.R + 28), Math.Min(255, baseCol.G + 28), Math.Min(255, baseCol.B + 28)) : baseCol);
            Color border = enabled ? borderCol : new Color(72, 82, 98);
            DrawRoundedPanel(batch, px, r, 10, fill, border, hover && enabled ? 2 : 1);
            DrawLocalString(batch, title, new Vector2(r.X + 14, r.Y + 9), enabled ? badgeStyle : subStyle);
            DrawLocalString(batch, Truncate(subtitle, 38), new Vector2(r.X + 14, r.Y + 30), enabled ? smallStyle : subStyle);
        }

        void DrawScorecard(UISpriteBatch batch, Texture2D px, int sw, int sh)
        {
            var o = finalOutcome ?? game.ComputeOutcome();
            int w = Math.Min(560, sw - 48);
            int h = 472;
            int x = 24;
            int y = Math.Max(66, (sh - h) / 2);
            var card = new Rectangle(x, y, w, h);

            DrawRoundedPanel(batch, px, card, 14, new Color(12, 20, 34, 238), new Color(84, 224, 178, 190), 2);

            int cy = y + 18;
            DrawLocalString(batch, "SIMCITY PD  •  911 SHIFT REPORT", new Vector2(x + 20, cy), titleStyle);
            cy += 24;
            DrawLocalString(batch, "Desk Sergeant " + game.SergeantName + "  •  Score " + o.Score, new Vector2(x + 20, cy), cyanStyle);

            // Big Rank Badge on top-right of scorecard
            var gradeBox = new Rectangle(x + w - 90, y + 16, 70, 70);
            Color gCol = o.Grade == "S" ? new Color(40, 168, 128) : (o.Grade == "A" ? new Color(46, 134, 212) : new Color(188, 134, 48));
            DrawRoundedPanel(batch, px, gradeBox, 12, gCol, Color.White, 2);
            DrawCentered(batch, o.Grade, gradeBox, gradeStyle);

            cy += 34;
            DrawLocalString(batch, o.Summary, new Vector2(x + 20, cy), mintStyle);
            cy += 24;
            batch.Draw(px, new Rectangle(x + 20, cy, w - 40, 1), new Color(76, 124, 172, 100));
            cy += 12;

            // Per-case log
            for (int i = 0; i < game.AllCases.Count; i++)
            {
                var c = game.AllCases[i];
                string chTag = c.Channel == DispatchCallChannel.WalkIn ? "[Walk-In]" : "[911]";
                string rTag = c.Rating == DispatchCaseRating.Perfect ? "PERFECT"
                    : (c.Rating == DispatchCaseRating.Good ? "GOOD"
                    : (c.Rating == DispatchCaseRating.WrongUnit ? "WRONG" : "MISSED"));
                var rStyle = (c.Rating == DispatchCaseRating.Perfect || c.Rating == DispatchCaseRating.Good) ? mintStyle : redStyle;
                DrawLocalString(batch, Truncate(chTag + " " + c.CallerName + ": " + c.SubjectTitle, 46), new Vector2(x + 20, cy), bodyStyle);
                DrawLocalString(batch, rTag, new Vector2(x + w - 86, cy), rStyle);
                cy += 23;
            }

            cy += 10;
            batch.Draw(px, new Rectangle(x + 20, cy, w - 40, 1), new Color(76, 124, 172, 100));
            cy += 12;

            var rw = o.Reward;
            DrawLocalString(batch, "REWARDS:  +$" + rw.BonusSimoleons + "  |  Perf +" + rw.JobPerformanceDelta +
                "  |  Logic +" + rw.LogicXpDelta + " XP  |  Charisma +" + rw.CharismaXpDelta + " XP",
                new Vector2(x + 20, cy), amberStyle);
            cy += 22;

            if (o.RelationshipDeltas.Count > 0)
            {
                string relLine = "NEIGHBORS: " + string.Join(", ", o.RelationshipDeltas.Take(3).Select(d =>
                    d.SimName + " (" + (d.DailyDelta >= 0 ? "+" : "") + d.DailyDelta + " Rel)"));
                DrawLocalString(batch, Truncate(relLine, 68), new Vector2(x + 20, cy), cyanStyle);
            }

            claimBtnRect = new Rectangle(x + 20, card.Bottom - 58, w - 40, 44);
            DrawRoundedPanel(batch, px, claimBtnRect, 10,
                hoverClaim ? new Color(46, 174, 134) : new Color(32, 138, 106),
                SimUiTokens.Mint, 2);
            DrawCentered(batch, "CLAIM REWARDS & RETURN HOME [ENTER]", claimBtnRect, badgeStyle);
        }

        void DrawRoundedPanel(UISpriteBatch batch, Texture2D px, Rectangle r, int radius, Color fill, Color border, int thickness = 1)
        {
            var sr = new SimRect(r.X, r.Y, r.Width, r.Height);
            FillRoundedRect(batch, sr, radius, fill);
            if (thickness > 0)
                DrawRoundedBorder(batch, sr, radius, border, thickness);
        }

        void DrawTransitionOverlay(UISpriteBatch batch, Texture2D px, int sw, int sh)
        {
            if (transition.BlackVeilAlpha > 0.005f)
            {
                int veilA = Math.Max(0, Math.Min(255, (int)(transition.BlackVeilAlpha * 255f)));
                batch.Draw(px, new Rectangle(0, 0, sw, sh), new Color(0, 0, 0, veilA));
            }

            if (transition.BannerAlpha > 0.01f)
            {
                int bw = 540;
                int bh = 66;
                var br = new Rectangle((sw - bw) / 2, 74, bw, bh);
                int a = Math.Max(0, Math.Min(255, (int)(transition.BannerAlpha * 228f)));
                DrawRoundedPanel(batch, px, br, 12, new Color(10, 18, 32, a), new Color(84, 198, 255, a), 2);
                DrawCentered(batch, transition.LocationTitle, new Rectangle(br.X, br.Y + 8, br.Width, 28), bannerTitleStyle);
                DrawCentered(batch, transition.LocationSubtitle, new Rectangle(br.X, br.Y + 38, br.Width, 20), bannerSubStyle);
            }
        }

        void DrawCentered(UISpriteBatch batch, string text, Rectangle r, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            var sz = style.MeasureString(text);
            DrawLocalString(batch, text, new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f), style);
        }

        static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, Math.Max(1, max - 1)) + "\u2026";
        }

        static List<DispatchCallerInfo> CollectNeighborhoodCallers(VMAvatar officer)
        {
            var list = new List<DispatchCallerInfo>();
            try
            {
                string officerName = officer != null ? (officer.Name ?? "") : "";
                short officerNid = officer != null ? officer.GetPersonData(VMPersonDataVariable.NeighborId) : (short)0;
                var content = Content.Get();
                var entries = content?.Neighborhood?.Neighbors?.Entries;
                if (entries != null)
                {
                    string[] addresses = { "5 Sim Lane", "7 Sim Lane", "21 Downtown Plaza", "55 Old Town Market", "10 Sim Lane", "3 Maple Street" };
                    var residents = new List<DispatchCallerInfo>();
                    var townies = new List<DispatchCallerInfo>();
                    int addrIdx = 0;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var n = entries[i];
                        if (n == null || n.NeighbourID <= 0 || n.GUID == 0) continue;
                        if (officerNid > 0 && n.NeighbourID == officerNid) continue;

                        // Prefer adult human Sims for 911 callers & live 3D precinct walk-ins
                        if (n.PersonData != null && n.PersonData.Length > (int)VMPersonDataVariable.PersonsAge)
                        {
                            short age = n.PersonData[(int)VMPersonDataVariable.PersonsAge];
                            if (age > 0 && age < 18) continue;
                        }

                        string name = ResolveNeighborDisplayName(content, n);
                        if (string.IsNullOrWhiteSpace(name) || IsServiceNpcName(name)) continue;
                        if (string.Equals(name.Trim(), officerName.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

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
                    // Iconic neighborhood residents (Goths, Bachelors, Mashugas, Newbies) first, then townies!
                    list.AddRange(residents);
                    list.AddRange(townies);
                    if (list.Count > 12) list.RemoveRange(12, list.Count - 12);
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
            string[] bad = { "clown", "npc", "maid", "gardener", "repair", "pizza", "burglar", "thief", "police", "cop", "fire", "mime", "grim", "santa", "genie", "bear", "pet", "cat", "dog", "stray", "raccoon", "skunk", "entertainer", "waiter", "chef", "dj", "bartender", "janitor", "sax", "bus", "car", "driver", "social", "skeleton", "bonehilda", "serv", "user0", "mystery", "stranger", "monster" };
            for (int i = 0; i < bad.Length; i++)
                if (low.Contains(bad[i])) return true;
            return !char.IsUpper(name.Trim()[0]);
        }

        static void ApplyOutcomeRewards(VMAvatar sim, DispatchTriageOutcome outcome)
        {
            if (outcome == null) return;
            string rewardSummary = CareerDispatchDriver.ApplyReward(sim, outcome.Reward);

            // Persist real TS1 Neighbor relationship deltas + Sim Memory dossier entries!
            try
            {
                var nb = Content.Get()?.Neighborhood;
                short officerNid = sim != null ? sim.GetPersonData(VMPersonDataVariable.NeighborId) : (short)0;
                string officerName = sim != null ? (sim.Name ?? "Sim") : "Sim";
                var officerNb = officerNid > 0 ? nb?.GetNeighborByID(officerNid) : null;

                foreach (var rel in outcome.RelationshipDeltas)
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
                        var sp = new RawSim { Name = officerName, NeighborId = officerNid };
                        var ls = new RawSim { Name = rel.SimName, NeighborId = rel.NeighborId };
                        Boot.Memory.Record(
                            sp,
                            ls,
                            "dispatch_triage",
                            rel.DailyDelta >= 0 ? Tone.Gentle : Tone.Bold,
                            "dispatch_triage",
                            rel.DailyDelta >= 0 ? 3 : 1,
                            subjectTitle: rel.MemoryFact);
                    }
                }
                Boot.SaveMemoryAsync();
            }
            catch (Exception re) { Boot.Log("dispatch-triage rel error: " + re.Message); }

            Boot.Log("dispatch-triage: shift complete grade=" + outcome.Grade +
                     " score=" + outcome.Score +
                     " rewards=[" + rewardSummary + "] rels=" + outcome.RelationshipDeltas.Count);
        }
    }
}
