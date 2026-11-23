using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Drives automatic mid-shift Radio Dispatch notifications during work hours
    // and hosts the Security Guard Level 1 ("CCTV Night Watch") floating mini-game.
    public static class CareerDispatchDriver
    {
        static readonly Dictionary<string, int> lastDispatchedDayBySim = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static int tickCounter;

        public static bool ToastVisible { get { return CareerDispatchToast.IsOpen; } }
        public static bool GameOpen { get { return SecurityWatchOverlay.IsOpen; } }

        public static void OnTick(VM vm)
        {
            if (vm == null || !vm.Ready || !Boot.Enabled) return;
            if (++tickCounter % 60 != 0) return;
            if (ToastVisible || GameOpen) return;

            try
            {
                var fam = vm.TS1State?.CurrentFamily;
                if (fam == null || fam.FamilyGUIDs == null) return;
                int day = vm.Context?.Clock?.DayOfMonth ?? 1;
                int hour = vm.Context?.Clock?.Hours ?? 0;

                foreach (var av in vm.Entities.OfType<VMAvatar>())
                {
                    if (av == null || av.Dead || av.IsPet) continue;
                    if (!IsInActiveFamily(av, fam)) continue;

                    var jobType = av.GetPersonData(VMPersonDataVariable.JobType);
                    if (jobType <= 0) continue;
                    var level = av.GetPersonData(VMPersonDataVariable.JobPromotionLevel);

                    string careerName = "", jobTitle = "";
                    int startHour = 0, endHour = 6;
                    try
                    {
                        var job = Content.Get().Jobs.GetJob((ushort)jobType);
                        if (job != null)
                        {
                            careerName = job.Name ?? "";
                            if (job.JobLevels != null && level >= 0 && level < job.JobLevels.Length)
                            {
                                jobTitle = job.JobLevels[level].JobName ?? "";
                                startHour = job.JobLevels[level].StartTime;
                                endHour = job.JobLevels[level].EndTime;
                            }
                        }
                    }
                    catch { }

                    bool isGuard = CareerDispatchRules.IsSecurityGuard(careerName, jobTitle, level);
                    bool isAtWork = av.Position == LotTilePos.OUT_OF_WORLD;
                    string simKey = KeyOf(av);
                    int lastDay = lastDispatchedDayBySim.TryGetValue(simKey, out var d) ? d : -1;

                    if (CareerDispatchRules.ShouldTriggerDispatch(isGuard, isAtWork, day, lastDay, hour, startHour, endHour))
                    {
                        lastDispatchedDayBySim[simKey] = day;
                        CareerDispatchToast.Show(av, string.IsNullOrWhiteSpace(jobTitle) ? "Security Guard" : jobTitle);
                        Boot.Log("dispatch: triggered radio call for " + av.Name + " (" + jobTitle + ") day=" + day + " hour=" + hour);
                        break;
                    }
                }
            }
            catch (Exception e) { Boot.Log("dispatch tick error: " + e.Message); }
        }

        static bool IsInActiveFamily(VMAvatar av, FSO.Files.Formats.IFF.Chunks.FAMI fam)
        {
            if (fam == null) return false;
            try
            {
                if (fam.RuntimeSubset != null && fam.RuntimeSubset.Contains(av.Object.OBJ.GUID)) return true;
                if (fam.FamilyGUIDs != null && fam.FamilyGUIDs.Contains(av.Object.OBJ.GUID)) return true;
            }
            catch { }
            return false;
        }

        public static VMAvatar ResolveTargetAvatar(string query)
        {
            try
            {
                var screen = UIScreen.Current;
                var vm = screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
                if (vm != null && vm.Ready && !string.IsNullOrWhiteSpace(query))
                {
                    var match = vm.Entities.OfType<VMAvatar>().FirstOrDefault(a =>
                        !a.Dead && !a.IsPet &&
                        (a.Name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) return match;
                }
                var sel = screen?.GetType().GetProperty("SelectedAvatar")?.GetValue(screen, null) as VMAvatar;
                if (sel != null) return sel;
                var lot = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                var active = lot?.GetType().GetProperty("ActiveEntity")?.GetValue(lot, null) as VMAvatar;
                if (active != null) return active;
                if (vm != null && vm.Ready)
                    return vm.Entities.OfType<VMAvatar>().FirstOrDefault(a => !a.Dead && !a.IsPet);
            }
            catch { }
            return null;
        }

        public static VM CurrentVM()
        {
            try
            {
                var screen = UIScreen.Current;
                return screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;
            }
            catch { return null; }
        }

        public static string ApplyRewards(VMAvatar sim, SecurityWatchOutcome outcome)
        {
            if (outcome == null) return "no outcome";
            var applied = new List<string>();
            try
            {
                var vm = CurrentVM();
                var fam = vm?.TS1State?.CurrentFamily;
                if (fam != null && outcome.BonusSimoleons != 0)
                {
                    fam.Budget = Math.Max(0, fam.Budget + outcome.BonusSimoleons);
                    applied.Add("+$" + outcome.BonusSimoleons);
                }

                if (sim != null)
                {
                    if (outcome.JobPerformanceDelta != 0)
                    {
                        short cur = sim.GetPersonData(VMPersonDataVariable.JobPerformance);
                        short next = (short)Math.Max(-100, Math.Min(100, cur + outcome.JobPerformanceDelta));
                        sim.SetPersonData(VMPersonDataVariable.JobPerformance, next);
                        applied.Add("Perf " + Signed(outcome.JobPerformanceDelta));
                    }
                    if (outcome.EnergyDelta != 0)
                    {
                        short cur = sim.GetMotiveData(VMMotive.Energy);
                        short next = (short)Math.Max(-100, Math.Min(100, cur + outcome.EnergyDelta));
                        sim.SetMotiveData(VMMotive.Energy, next);
                    }
                    if (outcome.FunDelta != 0)
                    {
                        short cur = sim.GetMotiveData(VMMotive.Fun);
                        short next = (short)Math.Max(-100, Math.Min(100, cur + outcome.FunDelta));
                        sim.SetMotiveData(VMMotive.Fun, next);
                    }
                }
            }
            catch (Exception e) { Boot.Log("dispatch reward apply error: " + e.Message); }

            string summary = applied.Count > 0 ? string.Join(", ", applied) : "recorded";
            Boot.Log("dispatch: shift finished grade=" + outcome.Grade + " score=" + outcome.CorrectCount + "/" + outcome.TotalRounds + " (" + summary + ")");
            return summary;
        }

        public static string Command(string[] args)
        {
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            var arg = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

            switch (sub)
            {
                case "status":
                    return "toast=" + ToastVisible + " game=" + GameOpen +
                           (GameOpen ? " [" + SecurityWatchOverlay.Status() + "]" : "");
                case "toast":
                {
                    var av = ResolveTargetAvatar(arg);
                    return CareerDispatchToast.Show(av, "Security Guard");
                }
                case "start":
                case "open":
                {
                    CareerDispatchToast.Close();
                    var av = ResolveTargetAvatar(arg);
                    return SecurityWatchOverlay.Open(av, 42);
                }
                case "answer":
                {
                    var av = CareerDispatchToast.CurrentAvatar ?? ResolveTargetAvatar(arg);
                    CareerDispatchToast.Close();
                    return SecurityWatchOverlay.Open(av, null);
                }
                case "action":
                    return SecurityWatchOverlay.PerformCommandAction(args.Length > 1 ? args[1] : "", args.Length > 2 ? args[2] : "");
                case "finish":
                    return SecurityWatchOverlay.FinishToSummary();
                case "close":
                case "dismiss":
                    CareerDispatchToast.Close();
                    SecurityWatchOverlay.Close(false);
                    return "closed";
                default:
                    return "usage: convo dispatch status|toast [Sim]|start [Sim]|answer|action alarm|clear|coffee|select N|finish|close";
            }
        }

        static string KeyOf(VMAvatar a)
        {
            short nid = a.GetPersonData(VMPersonDataVariable.NeighborId);
            if (nid > 0) return "n:" + nid;
            return "name:" + (a.Name ?? "sim").Trim().ToLowerInvariant();
        }

        static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }
    }

    // Native Simitone-styled top-right Radio Dispatch toast when a Security Guard Sim receives a shift call.
    public class CareerDispatchToast : SimUiElement
    {
        const float ToastTimeoutSeconds = 30f;

        static CareerDispatchToast instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }
        public static VMAvatar CurrentAvatar { get { return instance != null ? instance.avatar : null; } }

        readonly TextStyle channelStyle, titleStyle, bodyStyle, timerStyle;
        VMAvatar avatar;
        string simName = "Sim";
        string rankTitle = "Security Guard";
        float remaining = ToastTimeoutSeconds;
        float pulseTime;
        bool prevLeftDown;
        bool hoverAnswer, hoverIgnore;
        Rectangle answerRect, ignoreRect;

        CareerDispatchToast()
        {
            channelStyle = SimUiTokens.MakeStyle(12, SimUiTokens.DialogTitle, false);
            titleStyle = SimUiTokens.MakeStyle(15, SimUiTokens.TextPrimary, true);
            bodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            timerStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
        }

        public static string Show(VMAvatar av, string rankTitle)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new CareerDispatchToast();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                instance.avatar = av;
                instance.simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                instance.rankTitle = string.IsNullOrWhiteSpace(rankTitle) ? "Security Guard" : rankTitle.Trim();
                instance.remaining = ToastTimeoutSeconds;
                instance.pulseTime = 0f;
                instance.prevLeftDown = true;
                instance.Visible = true;
                isOpen = true;
                return "toast=open sim=" + instance.simName + " rank=" + instance.rankTitle;
            }
            catch (Exception e) { return "error: " + e.Message; }
        }

        public static void Close()
        {
            isOpen = false;
            if (instance != null)
            {
                instance.Visible = false;
                if (instance.Parent != null) instance.Parent.Remove(instance);
            }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || !Visible) return;

            float dt = 1f / 60f;
            pulseTime += dt;
            remaining -= dt;
            if (remaining <= 0f)
            {
                Close();
                return;
            }

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);
            hoverAnswer = answerRect.Contains(mx, my);
            hoverIgnore = ignoreRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverAnswer)
                {
                    var av = avatar;
                    Close();
                    SecurityWatchOverlay.Open(av, null);
                    return;
                }
                if (hoverIgnore)
                {
                    Close();
                    return;
                }
            }
            prevLeftDown = left;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen || !Visible) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            var layout = DispatchToastLayout.Compute(screenW, screenH);
            answerRect = ToXnaRect(layout.AnswerBtn);
            ignoreRect = ToXnaRect(layout.IgnoreBtn);

            float pulse = 0.5f + 0.5f * (float)Math.Sin(pulseTime * 6.0f);

            // 1. Translucent rounded Simitone dark glass toast shell + top-rounded gradient header bar
            DrawDialogShell(batch, layout.Panel, false, screenW, screenH, SimUiTokens.RadiusToast);
            DrawTitleBar(batch, layout.HeaderBar, layout.ChannelTextRect, "RADIO DISPATCH • CH-04", channelStyle, SimUiTokens.RadiusToast);

            // Pulsing circular radio LED inside reserved left header slot
            Color ledBg = Color.Black * 0.55f;
            FillRoundedRect(batch, new SimRect(layout.LedRect.X - 1f, layout.LedRect.Y - 1f, layout.LedRect.Width + 2f, layout.LedRect.Height + 2f), 6, ledBg);
            FillRoundedRect(batch, layout.LedRect, 5, pulse > 0.45f ? SimUiTokens.Mint : new Color(0, 110, 68, 220));

            // Animated signal bars inside rounded right header slot (never overlaps title!)
            FillRoundedRect(batch, layout.SignalBarsRect, SimUiTokens.RadiusSmall, Color.Black * 0.48f);
            for (int b = 0; b < 4; b++)
            {
                float bh = 4f + b * 3f;
                bool lit = b < 2 || pulse > 0.35f;
                FillRect(batch, px,
                    layout.SignalBarsRect.X + 4f + b * 7f,
                    layout.SignalBarsRect.Bottom - 2f - bh,
                    4f, bh,
                    lit ? SimUiTokens.Mint : new Color(40, 82, 74, 190));
            }

            // 2. Sim & alert copy (auto-ellipsized within layout rects)
            DrawFittedLine(batch, simName + "  •  " + rankTitle + " (Lvl 1)", layout.TitleRect, titleStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Motion alert on precinct CCTV feeds!", layout.BodyRect, bodyStyle, SimTextAlign.Left);

            // 3. Native Simitone pill buttons + countdown seconds
            DrawPillButton(batch, layout.AnswerBtn, "ANSWER", SimButtonVariant.Primary, hoverAnswer);
            DrawPillButton(batch, layout.IgnoreBtn, "IGNORE", SimButtonVariant.Secondary, hoverIgnore);

            string secLabel = Math.Max(0, (int)Math.Ceiling(remaining)) + "s";
            DrawFittedLine(batch, secLabel, layout.TimerTextRect, timerStyle, SimTextAlign.Right);

            // 4. Native motive-style countdown bar
            float frac = SimLayout.Clamp(remaining / ToastTimeoutSeconds, 0f, 1f);
            DrawMotiveBar(batch, layout.TimerBarRect, frac, frac < 0.25f);
        }
    }

    // Native Simitone-styled floating CCTV Night Watch mini-game for Law Enforcement Level 1 (Security Guard).
    public class SecurityWatchOverlay : SimUiElement
    {
        static SecurityWatchOverlay instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }

        readonly TextStyle headerTitleStyle, subStyle, cyanStyle, mintStyle, bodyStyle, mutedStyle, camStyle, quietStyle, alertStyle, redStyle, amberStyle, badgeTextStyle, statValMintStyle, statValCyanStyle, statValRedStyle, gradeStyle;
        SecurityWatchGame game;
        SecurityWatchOutcome finalOutcome;
        VMAvatar avatar;
        int savedSpeed = 1;
        bool rewardsApplied;

        readonly Rectangle[] camRects = new Rectangle[4];
        Rectangle panelRect, closeRect, alarmRect, clearRect, coffeeRect, claimRect;
        bool prevLeftDown, prevEscDown, prevADown, prevDDown, prevSpaceDown, prevEnterDown;
        readonly bool[] prevNumDown = new bool[4];
        int hoveredCam = -1;
        bool hoverClose, hoverAlarm, hoverClear, hoverCoffee, hoverClaim;

        SecurityWatchOverlay()
        {
            headerTitleStyle = SimUiTokens.MakeStyle(20, SimUiTokens.DialogTitle, false);
            subStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            cyanStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            mutedStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            camStyle = SimUiTokens.MakeStyle(12, SimUiTokens.MintSoft, true);
            quietStyle = SimUiTokens.MakeStyle(12, new Color(78, 148, 108, 170), false);
            alertStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Danger, true);
            redStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Danger, true);
            amberStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Amber, true);
            badgeTextStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            statValMintStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Mint, true);
            statValCyanStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Cyan, true);
            statValRedStyle = SimUiTokens.MakeStyle(20, SimUiTokens.Danger, true);
            gradeStyle = SimUiTokens.MakeStyle(42, Color.White, true);
        }

        public static string Open(VMAvatar av, int? seed)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new SecurityWatchOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);

                instance.avatar = av;
                string simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                int energy = av != null ? av.GetMotiveData(VMMotive.Energy) : 50;
                instance.game = new SecurityWatchGame(simName, energy, SecurityWatchGame.DefaultTotalRounds, seed);
                instance.finalOutcome = null;
                instance.rewardsApplied = false;

                // Pause live simulation while player focuses on the CCTV monitor
                var vm = CareerDispatchDriver.CurrentVM();
                if (vm != null)
                {
                    instance.savedSpeed = vm.SpeedMultiplier > 0 ? vm.SpeedMultiplier : 1;
                    vm.SpeedMultiplier = 0;
                }

                instance.prevLeftDown = true;
                instance.prevEscDown = true;
                instance.Visible = true;
                isOpen = true;
                return Status();
            }
            catch (Exception e) { return "error: " + e.Message; }
        }

        public static void Close(bool applyIfFinished)
        {
            if (instance != null && applyIfFinished && instance.game != null && instance.game.IsFinished && !instance.rewardsApplied)
                instance.ClaimRewards();

            isOpen = false;
            if (instance != null)
            {
                instance.Visible = false;
                if (instance.Parent != null) instance.Parent.Remove(instance);
                try
                {
                    var vm = CareerDispatchDriver.CurrentVM();
                    if (vm != null && vm.SpeedMultiplier == 0) vm.SpeedMultiplier = instance.savedSpeed;
                }
                catch { }
            }
        }

        public static string Status()
        {
            if (!IsOpen || instance == null || instance.game == null) return "open=False";
            var g = instance.game;
            if (g.IsFinished)
            {
                var o = instance.finalOutcome ?? g.ComputeOutcome();
                return "open=True finished=True grade=" + o.Grade + " score=" + o.CorrectCount + "/" + o.TotalRounds +
                       " bonus=$" + o.BonusSimoleons + " perf=" + o.JobPerformanceDelta;
            }
            var ev = g.ActiveEvent;
            return "open=True round=" + (g.CurrentRoundIndex + 1) + "/" + g.TotalRounds +
                   " cam=" + (ev != null ? ev.CameraIndex + 1 : 0) +
                   " kind=" + (ev != null ? ev.Kind.ToString() : "None") +
                   " threat=" + (ev != null && ev.IsThreat) +
                   " score=" + g.CorrectCount + " awake=" + (int)g.Alertness + "%";
        }

        public static string PerformCommandAction(string action, string arg)
        {
            if (!IsOpen || instance == null || instance.game == null) return "not open";
            var a = (action ?? "").ToLowerInvariant();
            if (a == "alarm") instance.game.SoundAlarm();
            else if (a == "clear") instance.game.MarkAllClear();
            else if (a == "coffee") instance.game.SipCoffee();
            else if (a == "select" && int.TryParse(arg, out var idx)) instance.game.SelectCamera(idx - 1);
            else if (a == "claim" || a == "close") { Close(true); return "closed"; }
            else return "unknown action: " + action;

            if (instance.game.IsFinished && instance.finalOutcome == null)
                instance.finalOutcome = instance.game.ComputeOutcome();
            return Status();
        }

        public static string FinishToSummary()
        {
            if (!IsOpen || instance == null || instance.game == null) return "not open";
            instance.game.AutoCompleteRemainingPerfect();
            instance.finalOutcome = instance.game.ComputeOutcome();
            return Status();
        }

        void ClaimRewards()
        {
            if (rewardsApplied || game == null) return;
            if (finalOutcome == null) finalOutcome = game.ComputeOutcome();
            CareerDispatchDriver.ApplyRewards(avatar, finalOutcome);
            rewardsApplied = true;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || !Visible || game == null) return;

            if (!game.IsFinished)
            {
                game.Tick(1f / 60f);
                if (game.IsFinished && finalOutcome == null)
                    finalOutcome = game.ComputeOutcome();
            }

            var kb = state.KeyboardState;
            bool esc = kb.IsKeyDown(Keys.Escape);
            if (esc && !prevEscDown)
            {
                prevEscDown = true;
                Close(game.IsFinished);
                return;
            }
            prevEscDown = esc;

            if (game.IsFinished)
            {
                bool enter = kb.IsKeyDown(Keys.Enter);
                if (enter && !prevEnterDown)
                {
                    prevEnterDown = true;
                    Close(true);
                    return;
                }
                prevEnterDown = enter;
            }
            else
            {
                bool aKey = kb.IsKeyDown(Keys.A);
                if (aKey && !prevADown) game.SoundAlarm();
                prevADown = aKey;

                bool dKey = kb.IsKeyDown(Keys.D);
                if (dKey && !prevDDown) game.MarkAllClear();
                prevDDown = dKey;

                bool spKey = kb.IsKeyDown(Keys.Space);
                if (spKey && !prevSpaceDown) game.SipCoffee();
                prevSpaceDown = spKey;

                var numKeys = new[] { Keys.D1, Keys.D2, Keys.D3, Keys.D4 };
                for (int i = 0; i < 4; i++)
                {
                    bool down = kb.IsKeyDown(numKeys[i]);
                    if (down && !prevNumDown[i]) game.SelectCamera(i);
                    prevNumDown[i] = down;
                }
            }

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);

            hoverClose = closeRect.Contains(mx, my);
            hoveredCam = -1;
            for (int i = 0; i < 4; i++)
                if (camRects[i].Contains(mx, my)) { hoveredCam = i; break; }
            hoverAlarm = alarmRect.Contains(mx, my);
            hoverClear = clearRect.Contains(mx, my);
            hoverCoffee = coffeeRect.Contains(mx, my);
            hoverClaim = claimRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverClose)
                {
                    prevLeftDown = true;
                    Close(game.IsFinished);
                    return;
                }

                if (game.IsFinished)
                {
                    if (hoverClaim)
                    {
                        prevLeftDown = true;
                        Close(true);
                        return;
                    }
                }
                else
                {
                    if (hoveredCam >= 0) game.SelectCamera(hoveredCam);
                    else if (hoverAlarm) game.SoundAlarm();
                    else if (hoverClear) game.MarkAllClear();
                    else if (hoverCoffee) game.SipCoffee();

                    if (game.IsFinished && finalOutcome == null)
                        finalOutcome = game.ComputeOutcome();
                }
            }
            prevLeftDown = left;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen || !Visible || game == null) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            var layout = SecurityWatchLayout.Compute(screenW, screenH, game.IsFinished);
            panelRect = ToXnaRect(layout.Panel);
            closeRect = ToXnaRect(layout.CloseBtn);

            // 1. Native Simitone modal backdrop + gradient title bar + badges + close pill
            DrawDialogShell(batch, layout.Panel, true, screenW, screenH);
            DrawTitleBar(batch, layout.HeaderBar, layout.TitleRect, "CCTV NIGHT WATCH", headerTitleStyle);

            string roundPill = game.IsFinished
                ? "SHIFT COMPLETE"
                : "EVENT " + (game.CurrentRoundIndex + 1) + " / " + game.TotalRounds;
            string scorePill = "SCORE " + game.CorrectCount + "/" + game.TotalRounds;

            DrawPillBadge(batch, layout.RoundBadge, roundPill, SimUiTokens.PillDarkBg, cyanStyle);
            DrawPillBadge(batch, layout.ScoreBadge, scorePill, SimUiTokens.PillDarkBg, mintStyle);
            DrawPillButton(batch, layout.CloseBtn, "X", SimButtonVariant.Close, hoverClose);

            // Clean subtitle line below gradient header
            string sub = game.SimName + " on Duty  •  Security Guard (Lvl 1)  •  12:00 AM - 6:00 AM  •  Law Enforcement Precinct";
            DrawFittedLine(batch, sub, layout.SubtitleBar, subStyle, SimTextAlign.Left);

            if (game.IsFinished)
            {
                DrawSummary(batch, px, layout);
                return;
            }

            // 2. Responsive 2x2 CCTV camera feed grid
            var ev = game.ActiveEvent;
            for (int i = 0; i < 4; i++)
            {
                camRects[i] = ToXnaRect(layout.Cameras[i]);
                DrawCameraFeed(batch, px, i, layout.Cameras[i], ev != null && ev.CameraIndex == i ? ev : null);
            }

            // 3. Responsive bottom tactical control deck (glass card)
            DrawGlassCard(batch, layout.DeckPanel, SimUiTokens.CardBgAlt, SimUiTokens.TealHighlight * 0.85f);

            string report = ev != null
                ? SecurityWatchGame.CameraLabels[ev.CameraIndex] + "  —  " + ev.Headline
                : "Scanning perimeter feeds...";
            string clue = game.FeedbackTimer > 0f && !string.IsNullOrWhiteSpace(game.LastFeedback)
                ? game.LastFeedback
                : (ev != null ? ev.ClueText : "");
            TextStyle clueStyle = mutedStyle;
            if (game.FeedbackTimer > 0f)
            {
                if (game.LastResult == SecurityRoundResult.ThreatCaught || game.LastResult == SecurityRoundResult.FalseAlarmCleared)
                    clueStyle = mintStyle;
                else if (game.LastResult != SecurityRoundResult.None)
                    clueStyle = redStyle;
            }

            DrawFittedLine(batch, report, layout.ReportTextRect, amberStyle, SimTextAlign.Left);
            DrawFittedLine(batch, clue, layout.ClueTextRect, clueStyle, SimTextAlign.Left);

            // Event countdown motive bar on top-right of deck (strictly separated from report/clue!)
            if (ev != null)
            {
                float tFrac = SimLayout.Clamp(ev.RemainingSeconds / Math.Max(0.1f, ev.DurationSeconds), 0f, 1f);
                DrawFittedLine(batch, "TIME LEFT: " + ev.RemainingSeconds.ToString("0.0") + "s",
                    layout.TimerLabelRect, tFrac < 0.30f ? alertStyle : mutedStyle, SimTextAlign.Right);
                DrawMotiveBar(batch, layout.TimerBarRect, tFrac, tFrac < 0.30f);
            }

            // Tactical controls (native Simitone pill buttons + motive alertness bar)
            alarmRect = ToXnaRect(layout.AlarmBtn);
            clearRect = ToXnaRect(layout.ClearBtn);
            coffeeRect = ToXnaRect(layout.CoffeeBtn);

            DrawPillButton(batch, layout.AlarmBtn, "[A] SOUND ALARM", SimButtonVariant.Danger, hoverAlarm);
            DrawPillButton(batch, layout.ClearBtn, "[D] ALL CLEAR", SimButtonVariant.Primary, hoverClear);

            float aFrac = SimLayout.Clamp(game.Alertness / 100f, 0f, 1f);
            DrawFittedLine(batch, "STAY AWAKE: " + (int)Math.Round(game.Alertness) + "%",
                layout.AwakeLabelRect, aFrac < 0.30f ? alertStyle : mutedStyle, SimTextAlign.Left);
            DrawMotiveBar(batch, layout.AwakeBarRect, aFrac, aFrac < 0.30f);

            bool hasCoffee = game.CoffeeSipsLeft > 0;
            DrawPillButton(batch, layout.CoffeeBtn, "[SPC] COFFEE (" + game.CoffeeSipsLeft + ")",
                SimButtonVariant.Warning, hoverCoffee && hasCoffee, hasCoffee);
        }

        void DrawCameraFeed(UISpriteBatch batch, Texture2D px, int camIdx, SimRect r, SecurityEvent ev)
        {
            bool selected = game.SelectedCamera == camIdx;
            bool hovered = hoveredCam == camIdx;
            bool activeMotion = ev != null;
            float blink = (float)Math.Sin(game.ElapsedSeconds * 7.5f);

            // Crisp Sims-styled outer border color
            Color border = activeMotion
                ? (blink > 0 ? SimUiTokens.Danger : SimUiTokens.Amber)
                : (selected ? SimUiTokens.Mint : (hovered ? SimUiTokens.Cyan : SimUiTokens.InnerBorder));

            // Night-vision rounded CRT phosphor screen
            float dimFactor = game.Alertness < 20f ? 0.55f : 1f;
            var bg = new Color((int)(8 * dimFactor), (int)(26 * dimFactor), (int)(22 * dimFactor), 245);
            FillRoundedRect(batch, r, SimUiTokens.RadiusCamera, bg);

            // Responsive architectural horizon & backdrop fitted cleanly inside rounded bottom corners
            float hudH = 24f;
            float floorH = SimLayout.Clamp(r.Height * 0.24f, 22f, 44f);
            float horizonY = r.Bottom - floorH;
            FillRoundedBottomRect(batch, new SimRect(r.X, horizonY, r.Width, floorH), SimUiTokens.RadiusCamera, new Color(14, 40, 32, 220));
            FillRect(batch, px, r.X + 2f, horizonY, Math.Max(0f, r.Width - 4f), 2f, new Color(36, 108, 80, 170));

            float archTop = r.Y + hudH + 14f;
            float archH = Math.Max(14f, horizonY - archTop);

            if (camIdx == 0) // Front Gate: iron fence bars
            {
                for (float fx = r.X + 22f; fx < r.Right - 18f; fx += 24f)
                    FillRect(batch, px, fx, archTop, 3f, archH, new Color(22, 62, 48, 165));
                FillRect(batch, px, r.X + 14f, archTop + archH * 0.22f, r.Width - 28f, 3f, new Color(28, 76, 58, 175));
            }
            else if (camIdx == 1) // Loading Dock: stacked crates
            {
                float c1 = Math.Min(42f, archH * 0.65f);
                float c2 = Math.Min(52f, archH * 0.80f);
                FillRect(batch, px, r.X + 20f, horizonY - c1, 50f, c1, new Color(24, 66, 50, 180));
                FillRect(batch, px, r.Right - 86f, horizonY - c2, 62f, c2, new Color(20, 56, 42, 180));
            }
            else if (camIdx == 2) // Vault Hall: steel doorway arch
            {
                float inset = Math.Min(110f, r.Width * 0.26f);
                FillRect(batch, px, r.X + inset, archTop, Math.Max(40f, r.Width - inset * 2f), archH, new Color(16, 48, 38, 190));
                FillRect(batch, px, r.X + inset + 8f, archTop + 8f, Math.Max(24f, r.Width - (inset + 8f) * 2f), 3f, new Color(42, 114, 86, 175));
            }
            else // Back Alley: dumpster
            {
                float dH = Math.Min(36f, archH * 0.60f);
                FillRect(batch, px, r.X + 22f, horizonY - dH, 62f, dH, new Color(22, 58, 46, 185));
                FillRect(batch, px, r.X + 18f, horizonY - dH - 5f, 70f, 5f, new Color(34, 84, 64, 200));
            }

            // Animated subject silhouette scaled to fit cleanly below HUD bar
            if (ev != null)
            {
                DrawSilhouette(batch, px, r, horizonY, hudH, ev);
            }
            else
            {
                var quietRect = new SimRect(r.X + 8f, r.Y + hudH, r.Width - 16f, r.Height - hudH);
                DrawFittedLine(batch, "ALL QUIET", quietRect, quietStyle, SimTextAlign.Center);
            }

            // Subtle CRT scanlines inset cleanly inside the rounded bezel
            for (float sy = r.Y + hudH + 2f; sy < r.Bottom - 4f; sy += 4f)
                FillRect(batch, px, r.X + 3f, sy, Math.Max(0f, r.Width - 6f), 1f, new Color(0, 0, 0, 38));

            // Top-rounded HUD bar inside camera monitor (strictly split: left label, right status pill)
            FillRoundedTopRect(batch, new SimRect(r.X, r.Y, r.Width, hudH), SimUiTokens.RadiusCamera, Color.Black * 0.72f);

            float statusW = activeMotion ? 86f : 56f;
            var labelRect = new SimRect(r.X + 10f, r.Y, Math.Max(40f, r.Width - statusW - 20f), hudH);
            DrawFittedLine(batch, "[" + (camIdx + 1) + "] " + SecurityWatchGame.CameraLabels[camIdx], labelRect, camStyle, SimTextAlign.Left);

            if (activeMotion)
            {
                if (blink > -0.2f)
                {
                    var motionRect = new SimRect(r.Right - statusW - 8f, r.Y + 3f, statusW, hudH - 6f);
                    DrawPillBadge(batch, motionRect, "! MOTION", SimUiTokens.DangerBtnTint, badgeTextStyle);
                }
            }
            else
            {
                float dotX = r.Right - statusW - 4f;
                FillRoundedRect(batch, new SimRect(dotX, r.Y + (hudH - 7f) * 0.5f, 7f, 7f), 3,
                    blink > 0 ? SimUiTokens.Danger : new Color(96, 36, 36, 180));
                DrawFittedLine(batch, "REC", new SimRect(dotX + 11f, r.Y, statusW - 14f, hudH), mutedStyle, SimTextAlign.Left);
            }

            // Rounded glowing monitor border drawn on top for crisp silhouette
            DrawRoundedBorder(batch, new SimRect(r.X - 1f, r.Y - 1f, r.Width + 2f, r.Height + 2f), SimUiTokens.RadiusCamera + 1, border, 2);
        }

        void DrawSilhouette(UISpriteBatch batch, Texture2D px, SimRect r, float floorY, float hudH, SecurityEvent ev)
        {
            float availH = Math.Max(46f, floorY - (r.Y + hudH + 6f));
            float sc = SimLayout.Clamp(availH / 92f, 0.54f, 1.05f);
            float sway = (float)Math.Sin(ev.MotionPhase * 4.5f) * 8f * sc;
            float cx = r.X + r.Width * 0.5f + sway;

            // Motion tracking brackets
            var bracketCol = ev.IsThreat ? SimUiTokens.Danger * 0.9f : SimUiTokens.Amber * 0.85f;
            float bw = 88f * sc, bh = 86f * sc, br = 12f * sc;
            float bx = cx - bw * 0.5f;
            float by = Math.Max(r.Y + hudH + 3f, floorY - bh + 2f);
            FillRect(batch, px, bx, by, br, 2f, bracketCol);
            FillRect(batch, px, bx, by, 2f, br, bracketCol);
            FillRect(batch, px, bx + bw - br, by, br, 2f, bracketCol);
            FillRect(batch, px, bx + bw - 2f, by, 2f, br, bracketCol);
            FillRect(batch, px, bx, by + bh - 2f, br, 2f, bracketCol);
            FillRect(batch, px, bx, by + bh - br, 2f, br, bracketCol);
            FillRect(batch, px, bx + bw - br, by + bh - 2f, br, 2f, bracketCol);
            FillRect(batch, px, bx + bw - 2f, by + bh - br, 2f, br, bracketCol);

            var litGreen = new Color(168, 255, 198, 235);
            var darkSil = new Color(16, 34, 28, 245);
            var midGreen = new Color(86, 212, 146, 235);

            Action<float, float, float, float, Color> part = (ox, oy, pw, ph, col) =>
                FillRect(batch, px, cx + ox * sc, floorY + oy * sc, pw * sc, ph * sc, col);

            switch (ev.Kind)
            {
                case SecurityEventKind.Burglar:
                    part(-10, -74, 20, 8, darkSil);
                    part(-9, -66, 18, 16, litGreen);
                    part(-9, -61, 18, 5, darkSil);
                    for (int s = 0; s < 5; s++)
                        part(-14, -48 + s * 6, 28, 6, s % 2 == 0 ? darkSil : litGreen);
                    part(-11, -18, 8, 20, darkSil);
                    part(3, -18, 8, 20, darkSil);
                    part(-32, -52, 18, 24, midGreen);
                    part(16, -56, 4, 26, new Color(230, 248, 238, 245));
                    part(16, -58, 11, 4, new Color(230, 248, 238, 245));
                    break;

                case SecurityEventKind.Vandal:
                    part(-12, -72, 24, 22, darkSil);
                    part(-15, -50, 30, 32, midGreen);
                    part(-11, -18, 9, 20, darkSil);
                    part(2, -18, 9, 20, darkSil);
                    part(14, -46, 16, 6, midGreen);
                    part(28, -54, 6, 12, litGreen);
                    part(36, -64, 18, 16, new Color(255, 118, 168, 215));
                    part(42, -50, 14, 12, SimUiTokens.Cyan * 0.85f);
                    break;

                case SecurityEventKind.Prowler:
                    part(-22, -48, 18, 16, litGreen);
                    part(-26, -32, 30, 20, darkSil);
                    part(-20, -12, 24, 14, darkSil);
                    part(6, -30, 10, 5, litGreen);
                    part(16, -38, 28, 22, new Color(255, 245, 150, 115));
                    part(44, -44, 24, 34, new Color(255, 245, 150, 68));
                    break;

                case SecurityEventKind.Raccoon:
                    part(-38, -24, 26, 22, new Color(92, 144, 124, 220));
                    part(-10, -24, 26, 18, midGreen);
                    part(-16, -22, 12, 12, litGreen);
                    part(-16, -18, 10, 4, darkSil);
                    for (int t = 0; t < 4; t++)
                        part(16 + t * 5, -20 - t * 2, 5, 6, t % 2 == 0 ? darkSil : litGreen);
                    break;

                case SecurityEventKind.StrayCat:
                    part(-14, -20, 24, 14, darkSil);
                    part(-22, -30, 14, 13, darkSil);
                    part(-22, -35, 4, 6, darkSil);
                    part(-12, -35, 4, 6, darkSil);
                    part(-20, -25, 3, 3, new Color(255, 240, 110, 255));
                    part(-14, -25, 3, 3, new Color(255, 240, 110, 255));
                    part(10, -36, 4, 20, darkSil);
                    part(14, -38, 6, 4, darkSil);
                    break;

                case SecurityEventKind.TreeShadow:
                    float lean = (float)(Math.Sin(ev.MotionPhase * 3.0f) * 10.0);
                    part(-6, -68, 12, 68, new Color(24, 68, 48, 210));
                    part(-34 + lean, -76, 68, 18, new Color(40, 116, 78, 185));
                    part(-24 + lean * 0.5f, -58, 54, 16, new Color(34, 98, 66, 180));
                    part(-38 + lean, -12, 76, 10, new Color(8, 22, 16, 165));
                    break;
            }
        }

        void DrawSummary(UISpriteBatch batch, Texture2D px, SecurityWatchLayout layout)
        {
            var o = finalOutcome ?? game.ComputeOutcome();

            // Main summary glass card
            DrawGlassCard(batch, layout.SummaryCard, SimUiTokens.CardBgAlt, SimUiTokens.Mint * 0.75f);

            // Big Simitone Grade Badge
            Color gradeBg = o.Grade == "S" ? new Color(10, 118, 76, 240)
                          : (o.Grade == "A" ? new Color(12, 92, 128, 240)
                          : (o.Grade == "B" ? new Color(142, 98, 20, 240) : SimUiTokens.DangerBg));
            Color gradeAccent = o.Grade == "S" ? SimUiTokens.Mint
                              : (o.Grade == "A" ? SimUiTokens.Cyan
                              : (o.Grade == "B" ? SimUiTokens.Amber : SimUiTokens.Danger));
            DrawGlassCard(batch, layout.GradeBadge, gradeBg, gradeAccent, SimUiTokens.RadiusModal);
            DrawFittedLine(batch, o.Grade, layout.GradeBadge, gradeStyle, SimTextAlign.Center);
            DrawFittedLine(batch, "SHIFT GRADE", layout.GradeCaptionRect, mutedStyle, SimTextAlign.Center);

            // Title & Wrapped Summary (strictly clamped inside SummaryBodyRect!)
            DrawFittedLine(batch, "NIGHT WATCH SHIFT REPORT  •  " + game.SimName, layout.SummaryTitleRect, mintStyle, SimTextAlign.Left);
            DrawFittedWrapped(batch, o.Summary, layout.SummaryBodyRect, bodyStyle, 3);

            // 3 Responsive Flex Stat Cards
            DrawStatCard(batch, layout.StatCards[0], "THREATS CAUGHT", o.ThreatsCaught.ToString(), SimUiTokens.Mint, statValMintStyle);
            DrawStatCard(batch, layout.StatCards[1], "FALSE ALARMS", o.FalseAlarmsCleared.ToString(), SimUiTokens.Cyan, statValCyanStyle);
            DrawStatCard(batch, layout.StatCards[2], "MISTAKES", o.Mistakes.ToString(), o.Mistakes == 0 ? SimUiTokens.Mint : SimUiTokens.Danger, o.Mistakes == 0 ? statValMintStyle : statValRedStyle);

            // Rewards Glass Card (2 balanced columns + motives line — zero collision!)
            DrawGlassCard(batch, layout.RewardsCard, SimUiTokens.CardBg, SimUiTokens.Cyan * 0.7f);
            DrawFittedLine(batch, "SHIFT REWARDS APPLIED TO " + game.SimName.ToUpperInvariant(), layout.RewardsHeaderRect, cyanStyle, SimTextAlign.Left);

            string cashStr = "+\u00a7" + o.BonusSimoleons + " Overtime Bonus";
            string perfStr = "Job Performance " + (o.JobPerformanceDelta >= 0 ? "+" : "") + o.JobPerformanceDelta;
            string motiveStr = "Energy " + (o.EnergyDelta >= 0 ? "+" : "") + o.EnergyDelta + "   •   Fun " + (o.FunDelta >= 0 ? "+" : "") + o.FunDelta;

            DrawFittedLine(batch, cashStr, layout.RewardCashRect, mintStyle, SimTextAlign.Left);
            DrawFittedLine(batch, perfStr, layout.RewardPerfRect, o.JobPerformanceDelta >= 0 ? mintStyle : redStyle, SimTextAlign.Left);
            DrawFittedLine(batch, motiveStr, layout.RewardMotivesRect, bodyStyle, SimTextAlign.Left);

            // Centered native green Simitone pill button
            claimRect = ToXnaRect(layout.ClaimBtn);
            DrawPillButton(batch, layout.ClaimBtn, "CLAIM REWARDS & RETURN", SimButtonVariant.Primary, hoverClaim);
        }

        void DrawStatCard(UISpriteBatch batch, SimRect rect, string label, string value, Color accent, TextStyle valueStyle)
        {
            DrawGlassCard(batch, rect, SimUiTokens.CardBg, accent * 0.8f);
            var inner = rect.Deflate(12f, 8f);
            var labelRect = new SimRect(inner.X, inner.Y, inner.Width, 17f);
            var valRect = new SimRect(inner.X, labelRect.Bottom + 3f, inner.Width, Math.Max(20f, inner.Bottom - (labelRect.Bottom + 3f)));
            DrawFittedLine(batch, label, labelRect, mutedStyle, SimTextAlign.Left);
            DrawFittedLine(batch, value, valRect, valueStyle, SimTextAlign.Left);
        }
    }
}
