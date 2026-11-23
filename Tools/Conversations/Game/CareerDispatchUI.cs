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

    // Pulsing top-right Radio Dispatch toast when a Security Guard Sim receives a shift call.
    public class CareerDispatchToast : UIElement
    {
        const float ToastTimeoutSeconds = 30f;

        static CareerDispatchToast instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }
        public static VMAvatar CurrentAvatar { get { return instance != null ? instance.avatar : null; } }

        readonly TextStyle channelStyle, titleStyle, bodyStyle, btnStyle;
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
            channelStyle = MakeStyle(11, new Color(255, 214, 102));
            titleStyle = MakeStyle(15, new Color(246, 250, 255));
            bodyStyle = MakeStyle(12, new Color(185, 210, 224));
            btnStyle = MakeStyle(12, new Color(246, 250, 255));
        }

        static TextStyle MakeStyle(int size, Color color)
        {
            var s = TextStyle.DefaultLabel.Clone();
            s.Size = size;
            s.Color = color;
            s.Shadow = true;
            return s;
        }

        public static string Show(VMAvatar av, string rankTitle)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new CareerDispatchToast();
                if (instance.Parent != screen)
                {
                    if (instance.Parent != null) instance.Parent.Remove(instance);
                    screen.Add(instance);
                }
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
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float screenW = batch.GraphicsDevice.Viewport.Width / scale;

            float w = 372f, h = 118f;
            float x = screenW - w - 22f;
            float y = 22f;

            float pulse = 0.5f + 0.5f * (float)Math.Sin(pulseTime * 6.0f);
            var accent = new Color((int)(220 + 35 * pulse), (int)(168 + 56 * pulse), 65, 255);

            // Outer border & radio chassis
            DrawLocalTexture(batch, px, null, new Vector2(x - 2, y - 2), new Vector2(w + 4, h + 4), new Color(75, 130, 165, 210));
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), new Color(14, 22, 34, 242));
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, 3), accent);

            // Pulsing radio LED + signal bars
            DrawLocalTexture(batch, px, null, new Vector2(x + 14, y + 14), new Vector2(10, 10),
                pulse > 0.45f ? new Color(255, 198, 72, 255) : new Color(110, 82, 32, 220));
            for (int b = 0; b < 4; b++)
            {
                int bh = 4 + b * 2;
                bool lit = b < 2 || pulse > 0.35f;
                DrawLocalTexture(batch, px, null, new Vector2(x + w - 42 + b * 7, y + 22 - bh), new Vector2(4, bh),
                    lit ? new Color(96, 232, 168, 240) : new Color(40, 62, 74, 180));
            }

            DrawLocalString(batch, "RADIO DISPATCH  •  CH-04 LAW ENFORCEMENT", new Vector2(x + 30, y + 12), channelStyle);
            DrawLocalString(batch, simName + "  •  " + rankTitle + " (Lvl 1)", new Vector2(x + 14, y + 32), titleStyle);
            DrawLocalString(batch, "Night Watch motion alert on precinct CCTV feeds!", new Vector2(x + 14, y + 54), bodyStyle);

            // Action buttons
            answerRect = new Rectangle((int)(x + 14), (int)(y + 78), 164, 28);
            ignoreRect = new Rectangle((int)(x + 188), (int)(y + 78), 96, 28);

            DrawLocalTexture(batch, px, null, new Vector2(answerRect.X, answerRect.Y), new Vector2(answerRect.Width, answerRect.Height),
                hoverAnswer ? new Color(54, 148, 104, 250) : new Color(36, 114, 82, 240));
            DrawLocalTexture(batch, px, null, new Vector2(answerRect.X, answerRect.Y + answerRect.Height - 2), new Vector2(answerRect.Width, 2),
                new Color(255, 224, 120, 240));
            CenterText(batch, "ANSWER DISPATCH", answerRect, btnStyle);

            DrawLocalTexture(batch, px, null, new Vector2(ignoreRect.X, ignoreRect.Y), new Vector2(ignoreRect.Width, ignoreRect.Height),
                hoverIgnore ? new Color(68, 76, 94, 240) : new Color(38, 48, 64, 225));
            CenterText(batch, "IGNORE", ignoreRect, btnStyle);

            // Countdown timer text + bar
            string secLabel = Math.Max(0, (int)Math.Ceiling(remaining)) + "s";
            DrawLocalString(batch, secLabel, new Vector2(x + w - 38, y + 84), bodyStyle);

            float frac = Math.Max(0f, Math.Min(1f, remaining / ToastTimeoutSeconds));
            DrawLocalTexture(batch, px, null, new Vector2(x, y + h - 3), new Vector2(w, 3), new Color(32, 46, 62, 200));
            DrawLocalTexture(batch, px, null, new Vector2(x, y + h - 3), new Vector2(w * frac, 3), accent);
        }

        void CenterText(UISpriteBatch batch, string text, Rectangle r, TextStyle style)
        {
            var sz = style.MeasureString(text);
            DrawLocalString(batch, text, new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f), style);
        }
    }

    // Floating CCTV Night Watch mini-game window for Law Enforcement Level 1 (Security Guard).
    public class SecurityWatchOverlay : UIElement
    {
        static SecurityWatchOverlay instance;
        static bool isOpen;

        public static bool IsOpen { get { return isOpen && instance != null; } }

        readonly TextStyle titleStyle, subStyle, goldStyle, bodyStyle, mutedStyle, camStyle, quietStyle, alertStyle, greenStyle, redStyle, btnStyle, gradeStyle;
        SecurityWatchGame game;
        SecurityWatchOutcome finalOutcome;
        VMAvatar avatar;
        int savedSpeed = 1;
        bool rewardsApplied;
        string appliedSummary = "";

        readonly Rectangle[] camRects = new Rectangle[4];
        Rectangle panelRect, closeRect, alarmRect, clearRect, coffeeRect, claimRect;
        bool prevLeftDown, prevEscDown, prevADown, prevDDown, prevSpaceDown, prevEnterDown;
        readonly bool[] prevNumDown = new bool[4];
        int hoveredCam = -1;
        bool hoverAlarm, hoverClear, hoverCoffee, hoverClaim;

        SecurityWatchOverlay()
        {
            titleStyle = MakeStyle(20, new Color(255, 224, 120));
            subStyle = MakeStyle(13, new Color(172, 202, 224));
            goldStyle = MakeStyle(14, new Color(255, 220, 118));
            bodyStyle = MakeStyle(14, new Color(238, 244, 250));
            mutedStyle = MakeStyle(12, new Color(165, 184, 202));
            camStyle = MakeStyle(12, new Color(142, 245, 178));
            quietStyle = MakeStyle(12, new Color(68, 128, 92, 165));
            quietStyle.Shadow = false;
            alertStyle = MakeStyle(12, new Color(255, 116, 108));
            greenStyle = MakeStyle(14, new Color(122, 238, 162));
            redStyle = MakeStyle(14, new Color(255, 134, 128));
            btnStyle = MakeStyle(14, new Color(248, 251, 255));
            gradeStyle = MakeStyle(44, new Color(255, 228, 124));
        }

        static TextStyle MakeStyle(int size, Color color)
        {
            var s = TextStyle.DefaultLabel.Clone();
            s.Size = size;
            s.Color = color;
            s.Shadow = true;
            return s;
        }

        public static string Open(VMAvatar av, int? seed)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new SecurityWatchOverlay();
                if (instance.Parent != screen)
                {
                    if (instance.Parent != null) instance.Parent.Remove(instance);
                    screen.Add(instance);
                }

                instance.avatar = av;
                string simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                int energy = av != null ? av.GetMotiveData(VMMotive.Energy) : 50;
                instance.game = new SecurityWatchGame(simName, energy, SecurityWatchGame.DefaultTotalRounds, seed);
                instance.finalOutcome = null;
                instance.rewardsApplied = false;
                instance.appliedSummary = "";

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
            appliedSummary = CareerDispatchDriver.ApplyRewards(avatar, finalOutcome);
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
                if (closeRect.Contains(mx, my))
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
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float screenW = batch.GraphicsDevice.Viewport.Width / scale;
            float screenH = batch.GraphicsDevice.Viewport.Height / scale;

            float pw = Math.Min(880f, screenW - 60f);
            float ph = Math.Min(594f, screenH - 64f);
            float px0 = (screenW - pw) * 0.5f;
            float py0 = (screenH - ph) * 0.5f;
            panelRect = new Rectangle((int)px0, (int)py0, (int)pw, (int)ph);

            // Dim backdrop + chassis + gold badge accent
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(screenW, screenH), new Color(4, 8, 14, 155));
            DrawLocalTexture(batch, px, null, new Vector2(px0 - 2, py0 - 2), new Vector2(pw + 4, ph + 4), new Color(80, 128, 156, 200));
            DrawLocalTexture(batch, px, null, new Vector2(px0, py0), new Vector2(pw, ph), new Color(14, 20, 30, 246));
            DrawLocalTexture(batch, px, null, new Vector2(px0, py0), new Vector2(pw, 4), new Color(255, 214, 98, 245));

            // Header bar
            DrawLocalString(batch, "CCTV NIGHT WATCH  •  SECURITY GUARD (LVL 1)", new Vector2(px0 + 20, py0 + 14), titleStyle);
            string sub = game.SimName + " on Duty  |  Shift: 12:00 AM - 6:00 AM  |  Law Enforcement Precinct";
            DrawLocalString(batch, sub, new Vector2(px0 + 20, py0 + 40), subStyle);

            // Round & Score pills
            string roundPill = game.IsFinished
                ? "SHIFT COMPLETE"
                : "EVENT " + (game.CurrentRoundIndex + 1) + " / " + game.TotalRounds;
            string scorePill = "SCORE " + game.CorrectCount + "/" + game.TotalRounds;
            DrawBadge(batch, px, px0 + pw - 296, py0 + 16, 134, 28, roundPill, new Color(28, 48, 68, 235), goldStyle);
            DrawBadge(batch, px, px0 + pw - 154, py0 + 16, 98, 28, scorePill, new Color(24, 56, 48, 235), greenStyle);

            // Close button [X]
            closeRect = new Rectangle((int)(px0 + pw - 46), (int)(py0 + 16), 30, 28);
            DrawLocalTexture(batch, px, null, new Vector2(closeRect.X, closeRect.Y), new Vector2(closeRect.Width, closeRect.Height), new Color(72, 42, 50, 235));
            CenterText(batch, "X", closeRect, btnStyle);

            DrawLocalTexture(batch, px, null, new Vector2(px0 + 16, py0 + 64), new Vector2(pw - 32, 1), new Color(68, 96, 122, 160));

            if (game.IsFinished)
            {
                DrawSummary(batch, px, px0, py0 + 74, pw, ph - 84);
                return;
            }

            // 2x2 CCTV camera feed grid
            float padX = 20f, gapX = 14f, gapY = 12f;
            float gridY = py0 + 76f;
            float camW = (pw - padX * 2f - gapX) * 0.5f;
            float camH = 182f;

            var ev = game.ActiveEvent;
            for (int i = 0; i < 4; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float cx = px0 + padX + col * (camW + gapX);
                float cy = gridY + row * (camH + gapY);
                camRects[i] = new Rectangle((int)cx, (int)cy, (int)camW, (int)camH);
                DrawCameraFeed(batch, px, i, camRects[i], ev != null && ev.CameraIndex == i ? ev : null);
            }

            // Bottom tactical control deck
            float deckY = gridY + camH * 2f + gapY + 14f;
            float deckH = ph - (deckY - py0) - 16f;
            DrawLocalTexture(batch, px, null, new Vector2(px0 + 20, deckY), new Vector2(pw - 40, deckH), new Color(18, 26, 40, 230));
            DrawLocalTexture(batch, px, null, new Vector2(px0 + 20, deckY), new Vector2(pw - 40, 2), new Color(72, 110, 140, 175));

            // Event report + timer bar
            string report = ev != null
                ? SecurityWatchGame.CameraLabels[ev.CameraIndex] + "  —  " + ev.Headline
                : "Scanning perimeter feeds...";
            string clue = game.FeedbackTimer > 0f && !string.IsNullOrWhiteSpace(game.LastFeedback)
                ? game.LastFeedback
                : (ev != null ? ev.ClueText : "");
            TextStyle clueStyle = subStyle;
            if (game.FeedbackTimer > 0f)
            {
                if (game.LastResult == SecurityRoundResult.ThreatCaught || game.LastResult == SecurityRoundResult.FalseAlarmCleared)
                    clueStyle = greenStyle;
                else if (game.LastResult != SecurityRoundResult.None)
                    clueStyle = redStyle;
            }

            DrawLocalString(batch, report, new Vector2(px0 + 34, deckY + 10), goldStyle);
            DrawLocalString(batch, clue, new Vector2(px0 + 34, deckY + 32), clueStyle);

            // Event countdown bar on top-right of deck
            if (ev != null)
            {
                float tBarW = 190f;
                float tBarX = px0 + pw - 34f - tBarW;
                float tFrac = Math.Max(0f, Math.Min(1f, ev.RemainingSeconds / Math.Max(0.1f, ev.DurationSeconds)));
                DrawLocalString(batch, "TIME LEFT: " + ev.RemainingSeconds.ToString("0.0") + "s", new Vector2(tBarX, deckY + 10), tFrac < 0.3f ? alertStyle : mutedStyle);
                DrawLocalTexture(batch, px, null, new Vector2(tBarX, deckY + 30), new Vector2(tBarW, 9), new Color(34, 46, 62, 220));
                DrawLocalTexture(batch, px, null, new Vector2(tBarX, deckY + 30), new Vector2(tBarW * tFrac, 9),
                    tFrac < 0.3f ? new Color(245, 96, 86, 245) : new Color(96, 214, 172, 245));
            }

            // Action buttons row
            float btnY = deckY + 62f;
            alarmRect = new Rectangle((int)(px0 + 34), (int)btnY, 218, 42);
            clearRect = new Rectangle((int)(px0 + 266), (int)btnY, 218, 42);

            DrawLocalTexture(batch, px, null, new Vector2(alarmRect.X, alarmRect.Y), new Vector2(alarmRect.Width, alarmRect.Height),
                hoverAlarm ? new Color(196, 64, 58, 250) : new Color(152, 46, 44, 240));
            DrawLocalTexture(batch, px, null, new Vector2(alarmRect.X, alarmRect.Y + alarmRect.Height - 3), new Vector2(alarmRect.Width, 3),
                new Color(255, 196, 98, 245));
            CenterText(batch, "[A]  SOUND ALARM  (Threat)", alarmRect, btnStyle);

            DrawLocalTexture(batch, px, null, new Vector2(clearRect.X, clearRect.Y), new Vector2(clearRect.Width, clearRect.Height),
                hoverClear ? new Color(46, 146, 114, 250) : new Color(32, 112, 88, 240));
            DrawLocalTexture(batch, px, null, new Vector2(clearRect.X, clearRect.Y + clearRect.Height - 3), new Vector2(clearRect.Width, 3),
                new Color(142, 245, 190, 235));
            CenterText(batch, "[D]  ALL CLEAR  (False Alarm)", clearRect, btnStyle);

            // Stay Awake meter + Sip Coffee button on right side
            float awakeX = px0 + 506f;
            float awakeW = 148f;
            float aFrac = Math.Max(0f, Math.Min(1f, game.Alertness / 100f));
            DrawLocalString(batch, "STAY AWAKE: " + (int)Math.Round(game.Alertness) + "%", new Vector2(awakeX, btnY + 2),
                aFrac < 0.30f ? alertStyle : mutedStyle);
            DrawLocalTexture(batch, px, null, new Vector2(awakeX, btnY + 24), new Vector2(awakeW, 12), new Color(34, 46, 62, 220));
            Color awakeColor = aFrac < 0.30f ? new Color(245, 96, 86, 245) : (aFrac < 0.55f ? new Color(245, 196, 82, 245) : new Color(108, 224, 152, 245));
            DrawLocalTexture(batch, px, null, new Vector2(awakeX, btnY + 24), new Vector2(awakeW * aFrac, 12), awakeColor);

            coffeeRect = new Rectangle((int)(awakeX + awakeW + 12), (int)btnY, (int)(px0 + pw - 34 - (awakeX + awakeW + 12)), 42);
            bool hasCoffee = game.CoffeeSipsLeft > 0;
            DrawLocalTexture(batch, px, null, new Vector2(coffeeRect.X, coffeeRect.Y), new Vector2(coffeeRect.Width, coffeeRect.Height),
                hasCoffee ? (hoverCoffee ? new Color(148, 98, 46, 250) : new Color(112, 74, 34, 240)) : new Color(42, 46, 56, 190));
            if (hasCoffee)
                DrawLocalTexture(batch, px, null, new Vector2(coffeeRect.X, coffeeRect.Y + coffeeRect.Height - 2), new Vector2(coffeeRect.Width, 2), new Color(255, 214, 120, 220));
            CenterText(batch, "[SPC] COFFEE (" + game.CoffeeSipsLeft + ")", coffeeRect, hasCoffee ? btnStyle : mutedStyle);
        }

        void DrawCameraFeed(UISpriteBatch batch, Texture2D px, int camIdx, Rectangle r, SecurityEvent ev)
        {
            bool selected = game.SelectedCamera == camIdx;
            bool hovered = hoveredCam == camIdx;
            bool activeMotion = ev != null;
            float blink = (float)Math.Sin(game.ElapsedSeconds * 7.5f);

            // Outer frame
            Color border = activeMotion
                ? (blink > 0 ? new Color(245, 92, 82, 245) : new Color(210, 146, 68, 230))
                : (selected ? new Color(255, 220, 118, 235) : (hovered ? new Color(110, 178, 148, 200) : new Color(50, 78, 72, 190)));
            DrawLocalTexture(batch, px, null, new Vector2(r.X - 2, r.Y - 2), new Vector2(r.Width + 4, r.Height + 4), border);

            // Night-vision CRT phosphor screen
            float dimFactor = game.Alertness < 20f ? 0.55f : 1f;
            var bg = new Color((int)(10 * dimFactor), (int)(26 * dimFactor), (int)(18 * dimFactor), 245);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), bg);

            // Subtle architectural backdrop per camera (floor horizon + fence/wall/crates)
            int horizonY = r.Y + r.Height - 46;
            DrawLocalTexture(batch, px, null, new Vector2(r.X, horizonY), new Vector2(r.Width, 46), new Color(16, 38, 28, 220));
            DrawLocalTexture(batch, px, null, new Vector2(r.X, horizonY), new Vector2(r.Width, 2), new Color(42, 92, 66, 160));

            if (camIdx == 0) // Front Gate: iron fence bars
            {
                for (int fx = r.X + 24; fx < r.Right - 20; fx += 24)
                    DrawLocalTexture(batch, px, null, new Vector2(fx, r.Y + 42), new Vector2(3, horizonY - (r.Y + 42)), new Color(24, 58, 42, 165));
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 16, r.Y + 56), new Vector2(r.Width - 32, 3), new Color(30, 70, 50, 175));
            }
            else if (camIdx == 1) // Loading Dock: stacked wooden crates
            {
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 24, horizonY - 42), new Vector2(54, 42), new Color(26, 62, 44, 180));
                DrawLocalTexture(batch, px, null, new Vector2(r.Right - 96, horizonY - 52), new Vector2(68, 52), new Color(22, 54, 38, 180));
            }
            else if (camIdx == 2) // Vault Hall: steel doorway arch
            {
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 120, r.Y + 44), new Vector2(r.Width - 240, horizonY - (r.Y + 44)), new Color(18, 46, 34, 190));
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 128, r.Y + 52), new Vector2(r.Width - 256, 3), new Color(46, 108, 76, 175));
            }
            else // Back Alley: brick wall & dumpster
            {
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 26, horizonY - 38), new Vector2(66, 38), new Color(24, 56, 42, 185));
                DrawLocalTexture(batch, px, null, new Vector2(r.X + 22, horizonY - 43), new Vector2(74, 6), new Color(34, 78, 56, 200));
            }

            // Draw the animated subject silhouette when motion is on this camera
            if (ev != null)
            {
                DrawSilhouette(batch, px, r, horizonY, ev);
            }
            else
            {
                DrawLocalString(batch, "ALL QUIET", new Vector2(r.X + r.Width * 0.5f - 34, r.Y + r.Height * 0.5f - 8), quietStyle);
            }

            // CRT scanlines overlay
            for (int sy = r.Y + 2; sy < r.Bottom; sy += 4)
                DrawLocalTexture(batch, px, null, new Vector2(r.X, sy), new Vector2(r.Width, 1), new Color(0, 0, 0, 42));

            // HUD overlays inside monitor
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, 24), new Color(4, 12, 9, 175));
            DrawLocalString(batch, "[" + (camIdx + 1) + "] " + SecurityWatchGame.CameraLabels[camIdx], new Vector2(r.X + 8, r.Y + 5), camStyle);

            if (activeMotion)
            {
                if (blink > -0.2f)
                {
                    DrawLocalTexture(batch, px, null, new Vector2(r.Right - 96, r.Y + 4), new Vector2(88, 17), new Color(176, 36, 34, 235));
                    DrawLocalString(batch, "! MOTION", new Vector2(r.Right - 84, r.Y + 5), btnStyle);
                }
            }
            else
            {
                DrawLocalTexture(batch, px, null, new Vector2(r.Right - 50, r.Y + 8), new Vector2(7, 7),
                    blink > 0 ? new Color(220, 62, 54, 230) : new Color(90, 34, 30, 180));
                DrawLocalString(batch, "REC", new Vector2(r.Right - 38, r.Y + 5), mutedStyle);
            }
        }

        void DrawSilhouette(UISpriteBatch batch, Texture2D px, Rectangle r, int floorY, SecurityEvent ev)
        {
            float sway = (float)Math.Sin(ev.MotionPhase * 4.5f) * 8f;
            int cx = r.X + r.Width / 2 + (int)sway;

            // Motion tracking brackets around subject
            var bracketCol = ev.IsThreat ? new Color(255, 112, 96, 210) : new Color(255, 224, 120, 185);
            int bx = cx - 44, by = floorY - 82, bw = 88, bh = 86;
            DrawLocalTexture(batch, px, null, new Vector2(bx, by), new Vector2(14, 2), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx, by), new Vector2(2, 14), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx + bw - 14, by), new Vector2(14, 2), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx + bw - 2, by), new Vector2(2, 14), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx, by + bh - 2), new Vector2(14, 2), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx, by + bh - 14), new Vector2(2, 14), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx + bw - 14, by + bh - 2), new Vector2(14, 2), bracketCol);
            DrawLocalTexture(batch, px, null, new Vector2(bx + bw - 2, by + bh - 14), new Vector2(2, 14), bracketCol);

            var litGreen = new Color(168, 255, 198, 235);
            var darkSil = new Color(18, 34, 26, 245);
            var midGreen = new Color(96, 206, 142, 235);

            switch (ev.Kind)
            {
                case SecurityEventKind.Burglar:
                    // Beanie + masked head
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 10, floorY - 74), new Vector2(20, 8), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 9, floorY - 66), new Vector2(18, 16), litGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 9, floorY - 61), new Vector2(18, 5), darkSil); // domino mask
                    // Classic black & white striped burglar shirt
                    for (int s = 0; s < 5; s++)
                        DrawLocalTexture(batch, px, null, new Vector2(cx - 14, floorY - 48 + s * 6), new Vector2(28, 6), s % 2 == 0 ? darkSil : litGreen);
                    // Legs
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 11, floorY - 18), new Vector2(8, 20), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 3, floorY - 18), new Vector2(8, 20), darkSil);
                    // Loot sack + crowbar
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 32, floorY - 52), new Vector2(18, 24), midGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 16, floorY - 56), new Vector2(4, 26), new Color(230, 248, 238, 245));
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 16, floorY - 58), new Vector2(11, 4), new Color(230, 248, 238, 245));
                    break;

                case SecurityEventKind.Vandal:
                    // Hooded figure
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 12, floorY - 72), new Vector2(24, 22), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 15, floorY - 50), new Vector2(30, 32), midGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 11, floorY - 18), new Vector2(9, 20), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 2, floorY - 18), new Vector2(9, 20), darkSil);
                    // Extended arm + spray can + bright paint splash
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 14, floorY - 46), new Vector2(16, 6), midGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 28, floorY - 54), new Vector2(6, 12), litGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 36, floorY - 64), new Vector2(18, 16), new Color(255, 118, 168, 215));
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 42, floorY - 50), new Vector2(14, 12), new Color(110, 232, 255, 205));
                    break;

                case SecurityEventKind.Prowler:
                    // Crouched prowler + flashlight beam
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 22, floorY - 48), new Vector2(18, 16), litGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 26, floorY - 32), new Vector2(30, 20), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 20, floorY - 12), new Vector2(24, 14), darkSil);
                    // Flashlight cone
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 6, floorY - 30), new Vector2(10, 5), litGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 16, floorY - 38), new Vector2(28, 22), new Color(255, 245, 150, 110));
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 44, floorY - 44), new Vector2(24, 34), new Color(255, 245, 150, 65));
                    break;

                case SecurityEventKind.Raccoon:
                    // Tipped trash can + cute hunched raccoon with ringed tail
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 38, floorY - 24), new Vector2(26, 22), new Color(92, 140, 118, 220));
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 10, floorY - 24), new Vector2(26, 18), midGreen);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 16, floorY - 22), new Vector2(12, 12), litGreen); // head
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 16, floorY - 18), new Vector2(10, 4), darkSil);  // raccoon mask
                    // Ringed tail
                    for (int t = 0; t < 4; t++)
                        DrawLocalTexture(batch, px, null, new Vector2(cx + 16 + t * 5, floorY - 20 - t * 2), new Vector2(5, 6), t % 2 == 0 ? darkSil : litGreen);
                    break;

                case SecurityEventKind.StrayCat:
                    // Small cat silhouette + pointy ears + curved tail + 2 glowing yellow eyes
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 14, floorY - 20), new Vector2(24, 14), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 22, floorY - 30), new Vector2(14, 13), darkSil);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 22, floorY - 35), new Vector2(4, 6), darkSil); // left ear
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 12, floorY - 35), new Vector2(4, 6), darkSil); // right ear
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 20, floorY - 25), new Vector2(3, 3), new Color(255, 240, 110, 255)); // eye
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 14, floorY - 25), new Vector2(3, 3), new Color(255, 240, 110, 255)); // eye
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 10, floorY - 36), new Vector2(4, 20), darkSil); // upright tail
                    DrawLocalTexture(batch, px, null, new Vector2(cx + 14, floorY - 38), new Vector2(6, 4), darkSil);
                    break;

                case SecurityEventKind.TreeShadow:
                    // Swaying tree branches casting soft shadows
                    int lean = (int)(Math.Sin(ev.MotionPhase * 3.0f) * 10f);
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 6, floorY - 68), new Vector2(12, 68), new Color(24, 66, 46, 210));
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 34 + lean, floorY - 76), new Vector2(68, 18), new Color(40, 112, 74, 185));
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 24 + lean / 2, floorY - 58), new Vector2(54, 16), new Color(34, 96, 64, 180));
                    DrawLocalTexture(batch, px, null, new Vector2(cx - 38 + lean, floorY - 12), new Vector2(76, 10), new Color(8, 22, 16, 165));
                    break;
            }
        }

        void DrawSummary(UISpriteBatch batch, Texture2D px, float px0, float y0, float pw, float h)
        {
            var o = finalOutcome ?? game.ComputeOutcome();
            float cardX = px0 + 48f;
            float cardW = pw - 96f;
            DrawLocalTexture(batch, px, null, new Vector2(cardX, y0 + 18), new Vector2(cardW, h - 42), new Color(18, 26, 40, 232));
            DrawLocalTexture(batch, px, null, new Vector2(cardX, y0 + 18), new Vector2(cardW, 3), new Color(255, 220, 118, 240));

            // Big Rank Badge
            Color gradeBg = o.Grade == "S" ? new Color(46, 112, 86, 245)
                          : (o.Grade == "A" ? new Color(42, 92, 128, 245)
                          : (o.Grade == "B" ? new Color(118, 92, 42, 245) : new Color(122, 48, 52, 245)));
            var badgeRect = new Rectangle((int)(cardX + 36), (int)(y0 + 52), 116, 116);
            DrawLocalTexture(batch, px, null, new Vector2(badgeRect.X, badgeRect.Y), new Vector2(badgeRect.Width, badgeRect.Height), gradeBg);
            DrawLocalTexture(batch, px, null, new Vector2(badgeRect.X, badgeRect.Y), new Vector2(badgeRect.Width, 3), new Color(255, 224, 120, 255));
            CenterText(batch, o.Grade, badgeRect, gradeStyle);
            DrawLocalString(batch, "SHIFT GRADE", new Vector2(badgeRect.X + 16, badgeRect.Bottom + 8), mutedStyle);

            // Title & Summary
            float tx = badgeRect.Right + 32f;
            DrawLocalString(batch, "NIGHT WATCH SHIFT REPORT  •  " + game.SimName, new Vector2(tx, y0 + 52), titleStyle);
            DrawLocalString(batch, o.Summary, new Vector2(tx, y0 + 84), bodyStyle);

            // Stats grid
            float statY = y0 + 126f;
            DrawStatBox(batch, px, tx, statY, 170, 62, "THREATS CAUGHT", o.ThreatsCaught.ToString(), greenStyle);
            DrawStatBox(batch, px, tx + 182, statY, 190, 62, "FALSE ALARMS CLEARED", o.FalseAlarmsCleared.ToString(), subStyle);
            DrawStatBox(batch, px, tx + 384, statY, 150, 62, "MISTAKES", o.Mistakes.ToString(), o.Mistakes == 0 ? greenStyle : redStyle);

            // Rewards panel
            float rewY = y0 + 216f;
            DrawLocalTexture(batch, px, null, new Vector2(cardX + 36, rewY), new Vector2(cardW - 72, 112), new Color(13, 20, 32, 230));
            DrawLocalString(batch, "SHIFT REWARDS APPLIED TO " + game.SimName.ToUpperInvariant(), new Vector2(cardX + 54, rewY + 14), goldStyle);

            string cashStr = "+$" + o.BonusSimoleons + " Overtime Bonus";
            string perfStr = "Job Performance " + (o.JobPerformanceDelta >= 0 ? "+" : "") + o.JobPerformanceDelta;
            string motiveStr = "Energy " + (o.EnergyDelta >= 0 ? "+" : "") + o.EnergyDelta + "   |   Fun " + (o.FunDelta >= 0 ? "+" : "") + o.FunDelta;

            DrawLocalString(batch, cashStr, new Vector2(cardX + 54, rewY + 46), greenStyle);
            DrawLocalString(batch, perfStr, new Vector2(cardX + 290, rewY + 46), o.JobPerformanceDelta >= 0 ? greenStyle : redStyle);
            DrawLocalString(batch, motiveStr, new Vector2(cardX + 54, rewY + 74), bodyStyle);

            // Claim button
            claimRect = new Rectangle((int)(cardX + cardW * 0.5f - 145), (int)(y0 + h - 90), 290, 44);
            DrawLocalTexture(batch, px, null, new Vector2(claimRect.X, claimRect.Y), new Vector2(claimRect.Width, claimRect.Height),
                hoverClaim ? new Color(54, 152, 108, 250) : new Color(36, 118, 84, 242));
            DrawLocalTexture(batch, px, null, new Vector2(claimRect.X, claimRect.Y + claimRect.Height - 3), new Vector2(claimRect.Width, 3),
                new Color(255, 224, 120, 250));
            CenterText(batch, "CLAIM REWARDS & RETURN", claimRect, btnStyle);
        }

        void DrawStatBox(UISpriteBatch batch, Texture2D px, float x, float y, float w, float h, string label, string val, TextStyle valStyle)
        {
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), new Color(14, 22, 34, 220));
            DrawLocalString(batch, label, new Vector2(x + 12, y + 10), mutedStyle);
            DrawLocalString(batch, val, new Vector2(x + 12, y + 32), valStyle);
        }

        void DrawBadge(UISpriteBatch batch, Texture2D px, float x, float y, float w, float h, string text, Color bg, TextStyle style)
        {
            var r = new Rectangle((int)x, (int)y, (int)w, (int)h);
            DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), bg);
            CenterText(batch, text, r, style);
        }

        void CenterText(UISpriteBatch batch, string text, Rectangle r, TextStyle style)
        {
            var sz = style.MeasureString(text);
            DrawLocalString(batch, text, new Vector2(r.X + (r.Width - sz.X) * 0.5f, r.Y + (r.Height - sz.Y) * 0.5f), style);
        }
    }
}
