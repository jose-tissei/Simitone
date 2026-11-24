using System;
using System.Globalization;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Full-Screen "Virtual Pocket Lot" HUD for Law Enforcement Level 2 ("Cadet"):
    // Academy Obstacle & Drill (DISP-05). Renders the live 3D Police Academy Training Grounds
    // edge-to-edge across the entire screen while the player's home lot VM stays safely paused,
    // wrapped in cinematic fade-from-black entry, arrival location banner, and fade-to-black return.
    public class CadetAcademyOverlay : SimUiElement
    {
        static CadetAcademyOverlay instance;
        static bool isOpen;

        VMAvatar avatar;
        CadetAcademyGame game;
        CadetAcademyOutcome finalOutcome;
        bool rewardsApplied;
        int savedSpeed = 1;
        bool prevLeftDown, prevSpaceDown, prevEnterDown, prevEscDown;
        bool hoverClose, hoverLock, hoverClaim, hoverStart, hoverSkip;
        bool finishSoundPlayed;

        readonly CadetAcademySceneRenderer sceneRenderer = new CadetAcademySceneRenderer();
        readonly VirtualLotTransition transition = new VirtualLotTransition();
        readonly CctvAudioEngine audio = new CctvAudioEngine();

        Rectangle closeRect, timingBarHitRect, lockBtnRect, claimBtnRect, startBtnRect, skipBtnRect;

        readonly TextStyle titleStyle;
        readonly TextStyle bannerTitleStyle;
        readonly TextStyle bannerSubStyle;
        readonly TextStyle subStyle;
        readonly TextStyle bodyStyle;
        readonly TextStyle smallStyle;
        readonly TextStyle badgeStyle;
        readonly TextStyle cyanStyle;
        readonly TextStyle mintStyle;
        readonly TextStyle briefBodyStyle;
        readonly TextStyle briefCyanStyle;
        readonly TextStyle briefMintStyle;
        readonly TextStyle gradeStyle;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        CadetAcademyOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(17, SimUiTokens.DialogTitle, true);
            bannerTitleStyle = SimUiTokens.MakeStyle(22, Color.White, true);
            bannerSubStyle = SimUiTokens.MakeStyle(11, SimUiTokens.Cyan, true);
            subStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            smallStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextSubtle, true);
            badgeStyle = SimUiTokens.MakeStyle(12, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
            briefBodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            briefCyanStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            briefMintStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Mint, true);
            gradeStyle = SimUiTokens.MakeStyle(40, Color.White, true);
        }

        public static string Open(VMAvatar av, int? bodyLevelOverride = null, int? seed = null)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new CadetAcademyOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);

                instance.avatar = av;
                string simName = av != null && !string.IsNullOrWhiteSpace(av.Name) ? av.Name.Trim() : "Sim";
                int bodyLvl = bodyLevelOverride.HasValue
                    ? bodyLevelOverride.Value
                    : (av != null ? av.GetPersonData(VMPersonDataVariable.BodySkill) / 100 : 0);

                instance.game = new CadetAcademyGame(simName, bodyLvl, seed, startInBriefing: true);
                instance.finalOutcome = null;
                instance.rewardsApplied = false;
                instance.finishSoundPlayed = false;
                instance.transition.BeginEnter(
                    "SIMCITY POLICE ACADEMY",
                    "TRAINING GROUNDS  \u2022  VIRTUAL SHIFT LOT (HOME LOT PAUSED)");

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
                instance.prevSpaceDown = kb.IsKeyDown(Keys.Space);
                instance.prevEnterDown = kb.IsKeyDown(Keys.Enter);
                instance.prevEscDown = kb.IsKeyDown(Keys.Escape);
                instance.Visible = true;
                isOpen = true;

                instance.audio.PlayMotionAlert();
                return "ok " + instance.StatusText();
            }
            catch (Exception e)
            {
                return "err " + e.Message;
            }
        }

        public static string Close(bool applyRewards)
        {
            if (instance == null) return "ok already closed";
            return instance.FinalizeClose(applyRewards);
        }

        void RequestExit(bool applyRewards, bool immediate = false)
        {
            if (immediate)
            {
                FinalizeClose(applyRewards);
                return;
            }
            if (!transition.IsExiting)
                transition.BeginExit(applyRewards);
        }

        string FinalizeClose(bool applyRewards)
        {
            string rewardSummary = "";
            if (applyRewards && game != null && game.IsFinished && !rewardsApplied)
            {
                if (finalOutcome == null)
                    finalOutcome = game.ComputeOutcome();
                rewardSummary = CareerDispatchDriver.ApplyReward(avatar, finalOutcome.Reward);
                rewardsApplied = true;
            }

            transition.Reset();
            isOpen = false;
            Visible = false;
            if (Parent != null)
                Parent.Remove(this);

            var vm = CareerDispatchDriver.CurrentVM();
            if (vm != null)
                vm.SpeedMultiplier = savedSpeed > 0 ? savedSpeed : 1;

            return "ok closed" + (rewardSummary.Length > 0 ? " [" + rewardSummary + "]" : "");
        }

        public static string StatusSummary()
        {
            return instance != null ? instance.StatusText() : "closed";
        }

        string StatusText()
        {
            if (game == null) return "idle";
            string phaseTag = " phase=" + transition.Phase;
            if (game.IsFinished)
            {
                var o = finalOutcome ?? game.ComputeOutcome();
                return "game=CadetAcademy open=" + IsOpen +
                       phaseTag +
                       " briefing=False finished=True grade=" + o.Grade +
                       " pts=" + o.TotalPoints +
                       " bodyLvl=" + game.BodySkillLevel +
                       " bodyXp=" + o.Reward.BodyXpDelta +
                       " bonus=$" + o.Reward.BonusSimoleons +
                       " perf=" + o.Reward.JobPerformanceDelta +
                       " " + sceneRenderer.DiagnosticSummary();
            }
            var st = game.ActiveStage;
            return "game=CadetAcademy open=" + IsOpen +
                   phaseTag +
                   " briefing=" + game.IsBriefing +
                   " stage=" + (game.CurrentStageIndex + 1) + "/" + CadetAcademyGame.TotalStages +
                   " kind=" + (st != null ? st.Kind.ToString() : "None") +
                   " resolving=" + game.IsResolvingStage +
                   " bodyLvl=" + game.BodySkillLevel +
                   " marker=" + game.MarkerPosition.ToString("0.00", CultureInfo.InvariantCulture) +
                   " sweetW=" + (st != null ? (st.PerfectHalfWidth * 2f).ToString("0.00", CultureInfo.InvariantCulture) : "0") +
                   " " + sceneRenderer.DiagnosticSummary();
        }

        public static string HandleScriptedAction(string action)
        {
            if (!IsOpen || instance == null || instance.game == null) return "err cadet academy not open";
            var a = (action ?? "").Trim().ToLowerInvariant();
            switch (a)
            {
                case "start":
                case "begin":
                    instance.StartDrill();
                    return "ok " + instance.StatusText();
                case "skip":
                    return Close(false);
                case "lock":
                case "hit":
                case "space":
                    instance.TriggerLock();
                    return "ok " + instance.StatusText();
                case "perfect":
                    if (instance.game.IsBriefing)
                        instance.StartDrill();
                    if (instance.game.ActiveStage != null && !instance.game.IsResolvingStage)
                    {
                        int guard = 0;
                        while (guard++ < 400 &&
                               Math.Abs(instance.game.MarkerPosition - instance.game.ActiveStage.ZoneCenter) >
                               instance.game.ActiveStage.PerfectHalfWidth * 0.45f)
                            instance.game.Tick(0.015f);
                        instance.TriggerLock();
                    }
                    return "ok " + instance.StatusText();
                case "auto":
                    instance.game.AutoCompleteRemainingPerfect();
                    instance.finalOutcome = instance.game.ComputeOutcome();
                    return "ok " + instance.StatusText();
                case "claim":
                    return Close(true);
                case "fade_claim":
                    instance.RequestExit(true, immediate: false);
                    return "ok " + instance.StatusText();
                default:
                    return "err unknown cadet action '" + a + "' (use: start, skip, lock, perfect, auto, claim, fade_claim)";
            }
        }

        public static string FinishToSummary()
        {
            if (!IsOpen || instance == null || instance.game == null) return "err cadet academy not open";
            instance.game.AutoCompleteRemainingPerfect();
            instance.finalOutcome = instance.game.ComputeOutcome();
            return "ok " + instance.StatusText();
        }

        void StartDrill()
        {
            if (game == null || !game.IsBriefing) return;
            if (game.BeginDrill())
                audio.PlayAllClear();
        }

        void TriggerLock()
        {
            if (game == null || game.IsBriefing || game.IsFinished || game.IsResolvingStage) return;
            var rating = game.LockIn();
            if (rating == CadetHitRating.Perfect) audio.PlayFeedback(true);
            else if (rating == CadetHitRating.Good) audio.PlayAllClear();
            else if (rating == CadetHitRating.Miss) audio.PlayFeedback(false);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!IsOpen || !Visible || game == null) return;

            float dt = 1f / 60f;
            if (state != null && state.Time != null && state.Time.ElapsedGameTime.TotalSeconds > 0.0001)
                dt = (float)Math.Min(0.05, state.Time.ElapsedGameTime.TotalSeconds);

            if (transition.Tick(dt))
            {
                FinalizeClose(transition.ExitApplyRewards);
                return;
            }

            game.Tick(dt);

            if (game.IsFinished)
            {
                if (finalOutcome == null) finalOutcome = game.ComputeOutcome();
                if (!finishSoundPlayed)
                {
                    finishSoundPlayed = true;
                    audio.PlayShiftComplete();
                }
            }

            if (transition.IsExiting)
                return;

            var kb = Keyboard.GetState();
            bool escNow = kb.IsKeyDown(Keys.Escape);
            bool spaceNow = kb.IsKeyDown(Keys.Space);
            bool enterNow = kb.IsKeyDown(Keys.Enter);

            if (escNow && !prevEscDown)
            {
                RequestExit(game.IsFinished, immediate: false);
                prevEscDown = true;
                return;
            }

            if (game.IsBriefing)
            {
                if ((spaceNow && !prevSpaceDown) || (enterNow && !prevEnterDown))
                {
                    StartDrill();
                    prevEscDown = escNow;
                    prevSpaceDown = spaceNow;
                    prevEnterDown = enterNow;
                    return;
                }
            }
            else if (!game.IsFinished)
            {
                if ((spaceNow && !prevSpaceDown) || (enterNow && !prevEnterDown))
                    TriggerLock();
            }
            else if ((spaceNow && !prevSpaceDown) || (enterNow && !prevEnterDown))
            {
                RequestExit(true, immediate: false);
                prevSpaceDown = spaceNow;
                prevEnterDown = enterNow;
                return;
            }

            prevEscDown = escNow;
            prevSpaceDown = spaceNow;
            prevEnterDown = enterNow;

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);
            hoverClose = closeRect.Contains(mx, my);
            hoverLock = lockBtnRect.Contains(mx, my);
            hoverClaim = claimBtnRect.Contains(mx, my);
            hoverStart = startBtnRect.Contains(mx, my);
            hoverSkip = skipBtnRect.Contains(mx, my);

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverClose)
                {
                    RequestExit(game.IsFinished, immediate: false);
                    return;
                }
                if (game.IsBriefing)
                {
                    if (hoverStart)
                    {
                        StartDrill();
                        prevLeftDown = true;
                        return;
                    }
                    if (hoverSkip)
                    {
                        RequestExit(false, immediate: false);
                        return;
                    }
                }
                else if (game.IsFinished)
                {
                    if (hoverClaim)
                    {
                        RequestExit(true, immediate: false);
                        return;
                    }
                }
                else if (hoverLock || timingBarHitRect.Contains(mx, my))
                {
                    TriggerLock();
                }
            }
            prevLeftDown = left;
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (!IsOpen || !Visible || game == null) return;
            var gd = batch != null ? batch.GraphicsDevice : GameFacade.GraphicsDevice;
            if (gd != null)
                sceneRenderer.PreDraw(gd, game, avatar);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!IsOpen || !Visible || game == null) return;

            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            // Reset hitboxes each frame so inactive buttons never capture clicks
            startBtnRect = Rectangle.Empty;
            skipBtnRect = Rectangle.Empty;
            lockBtnRect = Rectangle.Empty;
            timingBarHitRect = Rectangle.Empty;
            claimBtnRect = Rectangle.Empty;

            // 1. Edge-to-edge Full-Screen 3D Virtual Pocket Lot (no modal box, no home lot peeking around edges!)
            DrawFullScreen3DWorld(batch, px, screenW, screenH);

            // 2. Subtle top HUD scrim so the floating top strip pops against sunlit sky
            FillRect(batch, px, 0f, 0f, screenW, 64f, new Color(6, 14, 24, 132));
            FillRect(batch, px, 0f, 64f, screenW, 2f, new Color(72, 168, 232, 95));

            // 3. Sleek floating Top Strip: Venue + Cadet Pill (left), 3 Stage Pills (center), Return Home button (right)
            DrawTopFloatingStrip(batch, screenW);

            // 4. Context-specific minimal translucent floating HUD over the live 3D Academy Grounds
            if (game.IsBriefing)
                DrawFloatingBriefingCard(batch, px, screenW, screenH);
            else if (!game.IsFinished)
                DrawFloatingTimingDock(batch, px, screenW, screenH);
            else
                DrawFloatingScorecard(batch, px, screenW, screenH);

            // 5. Cinematic Virtual Pocket Lot Arrival Banner & Smooth Fade-From/To-Black Veil
            DrawTransitionOverlay(batch, px, screenW, screenH);
        }

        void DrawFullScreen3DWorld(UISpriteBatch batch, Texture2D px, float screenW, float screenH)
        {
            FillRect(batch, px, 0f, 0f, screenW, screenH, new Color(10, 18, 30, 255));

            if (sceneRenderer.HasLiveFrame && sceneRenderer.TargetTexture != null)
            {
                var tex = sceneRenderer.TargetTexture;
                float targetAspect = screenW / Math.Max(1f, screenH);
                float texAspect = (float)tex.Width / Math.Max(1, tex.Height);
                Rectangle srcRect;
                if (targetAspect > texAspect + 0.01f)
                {
                    int srcH = Math.Max(1, Math.Min(tex.Height, (int)Math.Round(tex.Width / targetAspect)));
                    int srcY = Math.Max(0, Math.Min(tex.Height - srcH, (tex.Height - srcH) / 2));
                    srcRect = new Rectangle(0, srcY, tex.Width, srcH);
                }
                else if (targetAspect < texAspect - 0.01f)
                {
                    int srcW = Math.Max(1, Math.Min(tex.Width, (int)Math.Round(tex.Height * targetAspect)));
                    int srcX = Math.Max(0, (tex.Width - srcW) / 2);
                    srcRect = new Rectangle(srcX, 0, srcW, tex.Height);
                }
                else
                {
                    srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
                }

                DrawLocalTexture(batch, tex, srcRect,
                    Vector2.Zero,
                    new Vector2(screenW / srcRect.Width, screenH / srcRect.Height),
                    Color.White);
            }
            else
            {
                DrawFittedLine(batch, "ENTERING SIMCITY POLICE ACADEMY...",
                    new SimRect(0f, screenH * 0.45f, screenW, 32f), titleStyle, SimTextAlign.Center);
            }
        }

        void DrawTopFloatingStrip(UISpriteBatch batch, float screenW)
        {
            float pad = 18f;
            float y = 12f;

            // Right: RETURN HOME [ESC] pill button
            float closeW = 166f;
            var closeSimRect = new SimRect(screenW - pad - closeW, y + 3f, closeW, 34f);
            closeRect = ToXnaRect(closeSimRect);
            DrawPillButton(batch, closeSimRect, "RETURN HOME [ESC]", SimButtonVariant.Secondary, hoverClose);

            // Center: 3 compact stage pills (cleanly centered between left badge and right button)
            float stageGroupW = Math.Min(490f, Math.Max(300f, screenW * 0.36f));
            float leftMaxW = Math.Max(220f, screenW - pad * 2f - closeW - stageGroupW - 28f);
            float leftW = Math.Min(420f, leftMaxW);

            var badgeRect = new SimRect(pad, y + 2f, leftW, 36f);
            FillRoundedRect(batch, badgeRect, SimUiTokens.RadiusControl, new Color(10, 24, 42, 212));
            DrawRoundedBorder(batch, badgeRect, SimUiTokens.RadiusControl, SimUiTokens.Cyan * 0.55f, 1);

            int sweetPct = (int)Math.Round(CadetAcademyGame.ComputePerfectZoneWidth(game.BodySkillLevel) * 100f);
            DrawFittedLine(batch,
                "POLICE ACADEMY  \u2022  CADET " + game.SimName.ToUpperInvariant(),
                new SimRect(badgeRect.X + 12f, badgeRect.Y + 3f, badgeRect.Width - 24f, 16f),
                briefCyanStyle, SimTextAlign.Left);
            DrawFittedLine(batch,
                "Body Skill Lv." + game.BodySkillLevel + "  \u2022  Green Sweet Spot: " + sweetPct + "%  \u2022  Home Lot Paused",
                new SimRect(badgeRect.X + 12f, badgeRect.Y + 18f, badgeRect.Width - 24f, 15f),
                smallStyle, SimTextAlign.Left);

            float stagesX = badgeRect.Right + ((closeSimRect.X - badgeRect.Right) - stageGroupW) * 0.5f;
            DrawStagePills(batch, stagesX, y + 5f, stageGroupW, 30f);
        }

        void DrawStagePills(UISpriteBatch batch, float x, float y, float totalW, float h)
        {
            float gap = 8f;
            float pillW = (totalW - gap * 2f) / 3f;
            string[] labels = { "1. SPRINT", "2. VAULT", "3. TARGET" };

            for (int i = 0; i < CadetAcademyGame.TotalStages; i++)
            {
                var st = game.Stages[i];
                var r = new SimRect(x + i * (pillW + gap), y, pillW, h);
                bool active = !game.IsBriefing && !game.IsFinished && i == game.CurrentStageIndex;

                Color fill, border;
                string suffix = "";
                if (st.Rating == CadetHitRating.Perfect)
                {
                    fill = new Color(18, 102, 68, 232);
                    border = SimUiTokens.Mint;
                    suffix = " \u2605";
                }
                else if (st.Rating == CadetHitRating.Good)
                {
                    fill = new Color(116, 86, 24, 232);
                    border = SimUiTokens.Amber;
                    suffix = " \u2713";
                }
                else if (st.Rating == CadetHitRating.Miss)
                {
                    fill = new Color(126, 36, 44, 232);
                    border = SimUiTokens.Danger;
                    suffix = " \u2715";
                }
                else if (active)
                {
                    fill = new Color(20, 82, 142, 238);
                    border = SimUiTokens.Cyan;
                    suffix = " \u25cf";
                }
                else
                {
                    fill = new Color(12, 24, 42, 198);
                    border = SimUiTokens.InnerBorder;
                }

                FillRoundedRect(batch, r, SimUiTokens.RadiusControl, fill);
                DrawRoundedBorder(batch, r, SimUiTokens.RadiusControl, border, 1);
                DrawFittedLine(batch, labels[i] + suffix, new SimRect(r.X + 6f, r.Y + 6f, r.Width - 12f, 18f), badgeStyle, SimTextAlign.Center);
            }
        }

        void DrawFloatingBriefingCard(UISpriteBatch batch, Texture2D px, float screenW, float screenH)
        {
            // Left-anchored frosted card leaving the right ~62% of the full 16:9 screen completely unobstructed
            // for Cadet Bob + the Sideline Drill Instructor standing ready at the Academy track!
            float cardW = Math.Max(336f, Math.Min(428f, screenW * 0.36f));
            float cardH = Math.Min(462f, Math.Max(360f, screenH - 110f));
            float cardX = 24f;
            float cardY = Math.Max(82f, (screenH - cardH) * 0.5f + 16f);
            var card = new SimRect(cardX, cardY, cardW, cardH);

            // Soft drop shadow + translucent glass card
            FillRoundedRect(batch, new SimRect(card.X + 4f, card.Y + 6f, card.Width, card.Height), SimUiTokens.RadiusModal, new Color(0, 0, 0, 115));
            FillRoundedRect(batch, card, SimUiTokens.RadiusModal, new Color(10, 22, 38, 222));
            DrawRoundedBorder(batch, card, SimUiTokens.RadiusModal, SimUiTokens.Cyan * 0.72f, 2);

            float lx = card.X + 18f;
            float lw = card.Width - 36f;
            float ly = card.Y + 16f;
            float btnH = 40f;
            float contentBottom = card.Bottom - btnH - 22f;

            DrawFittedLine(batch, "ACADEMY BRIEFING  \u2022  LEVEL 2 CADET", new SimRect(lx, ly, lw, 22f), titleStyle, SimTextAlign.Left);
            ly += 24f;
            DrawFittedLine(batch, "Complete all 3 timed drills under the Drill Instructor's watch.", new SimRect(lx, ly, lw, 16f), subStyle, SimTextAlign.Left);
            ly += 20f;
            FillRect(batch, px, lx, ly, lw, 1f, SimUiTokens.Divider);
            ly += 10f;

            DrawFittedLine(batch, "WHAT WILL HAPPEN  (3 STAGES)", new SimRect(lx, ly, lw, 17f), briefCyanStyle, SimTextAlign.Left);
            ly += 20f;
            ly = DrawFittedWrapped(batch, "1. Sprint Dash: Burst down the track and clear the hurdles.",
                new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), briefBodyStyle, 2) + 5f;
            ly = DrawFittedWrapped(batch, "2. Hurdle Vault: Time your leap cleanly over the academy wall.",
                new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), briefBodyStyle, 2) + 5f;
            ly = DrawFittedWrapped(batch, "3. Target Drill: Hold steady and strike the red bullseye.",
                new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), briefBodyStyle, 2) + 8f;

            if (ly + 36f <= contentBottom)
            {
                FillRect(batch, px, lx, ly, lw, 1f, SimUiTokens.Divider);
                ly += 8f;
                DrawFittedLine(batch, "CONTROLS & SCORING", new SimRect(lx, ly, lw, 17f), briefCyanStyle, SimTextAlign.Left);
                ly += 19f;
                ly = DrawFittedWrapped(batch, "Lock Timing: Press SPACE, ENTER, or click the bar when the needle hits green.",
                    new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), briefBodyStyle, 2) + 4f;
                int sweetPct = (int)Math.Round(CadetAcademyGame.ComputePerfectZoneWidth(game.BodySkillLevel) * 100f);
                ly = DrawFittedWrapped(batch, "Body Lv." + game.BodySkillLevel + " sets Green Perfect zone to " + sweetPct + "%  (Perfect +100, Good +65).",
                    new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), briefMintStyle, 2) + 8f;
            }

            if (ly + 30f <= contentBottom)
            {
                FillRect(batch, px, lx, ly, lw, 1f, SimUiTokens.Divider);
                ly += 8f;
                DrawFittedLine(batch, "SHIFT REWARDS (UP TO S-RANK / 300 PTS)", new SimRect(lx, ly, lw, 17f), briefMintStyle, SimTextAlign.Left);
                ly += 19f;
                DrawFittedWrapped(batch, "+Body Skill XP, +Job Performance, +Fun & Bonus Simoleons. Skip anytime with zero penalty.",
                    new SimRect(lx, ly, lw, Math.Max(0f, contentBottom - ly)), smallStyle, 2);
            }

            // Action buttons at bottom of Briefing card
            float btnY = card.Bottom - btnH - 14f;
            float btnGap = 10f;
            float startW = (lw - btnGap) * 0.58f;
            float skipW = lw - btnGap - startW;

            var startSimRect = new SimRect(lx, btnY, startW, btnH);
            var skipSimRect = new SimRect(startSimRect.Right + btnGap, btnY, skipW, btnH);
            startBtnRect = ToXnaRect(startSimRect);
            skipBtnRect = ToXnaRect(skipSimRect);

            DrawPillButton(batch, startSimRect, "START DRILL [SPACE]", SimButtonVariant.Primary, hoverStart);
            DrawPillButton(batch, skipSimRect, "SKIP [ESC]", SimButtonVariant.Secondary, hoverSkip);
        }

        void DrawFloatingTimingDock(UISpriteBatch batch, Texture2D px, float screenW, float screenH)
        {
            var st = game.ActiveStage;
            if (st == null) return;

            float dockW = Math.Min(700f, screenW - 40f);
            float dockH = 122f;
            float dockX = (screenW - dockW) * 0.5f;
            float dockY = screenH - dockH - 18f;
            var dock = new SimRect(dockX, dockY, dockW, dockH);

            // Floating Callout ribbon directly above the timing dock
            if (!string.IsNullOrEmpty(game.LastCallout))
            {
                Color bg = new Color(10, 22, 38, 222);
                Color border = SimUiTokens.Cyan * 0.7f;
                if (game.LastRating == CadetHitRating.Perfect)
                {
                    bg = new Color(16, 114, 72, 238);
                    border = SimUiTokens.Mint;
                }
                else if (game.LastRating == CadetHitRating.Good)
                {
                    bg = new Color(148, 104, 22, 238);
                    border = SimUiTokens.Amber;
                }
                else if (game.LastRating == CadetHitRating.Miss)
                {
                    bg = new Color(152, 36, 46, 238);
                    border = SimUiTokens.Danger;
                }

                float callW = Math.Min(dockW - 24f, 540f);
                var callRect = new SimRect((screenW - callW) * 0.5f, dock.Y - 36f, callW, 28f);
                FillRoundedRect(batch, callRect, SimUiTokens.RadiusControl, bg);
                DrawRoundedBorder(batch, callRect, SimUiTokens.RadiusControl, border, 1);
                DrawFittedLine(batch, game.LastCallout, new SimRect(callRect.X + 12f, callRect.Y + 6f, callRect.Width - 24f, 16f), badgeStyle, SimTextAlign.Center);
            }

            // Translucent frosted bottom timing dock (leaving >80% of the 3D Academy screen open!)
            FillRoundedRect(batch, new SimRect(dock.X + 3f, dock.Y + 5f, dock.Width, dock.Height), SimUiTokens.RadiusModal, new Color(0, 0, 0, 110));
            FillRoundedRect(batch, dock, SimUiTokens.RadiusModal, new Color(10, 22, 38, 218));
            DrawRoundedBorder(batch, dock, SimUiTokens.RadiusModal, SimUiTokens.OuterBorder * 0.9f, 2);

            DrawFittedLine(batch,
                st.Title + "  \u2014  " + st.Subtitle,
                new SimRect(dock.X + 16f, dock.Y + 9f, dock.Width - 32f, 20f),
                bodyStyle, SimTextAlign.Center);

            var barRect = new SimRect(dock.X + 18f, dock.Y + 34f, dock.Width - 36f, 34f);
            timingBarHitRect = ToXnaRect(barRect);

            // Outer dark timing track
            FillRoundedRect(batch, barRect, SimUiTokens.RadiusControl, new Color(46, 22, 32, 240));
            DrawRoundedBorder(batch, barRect, SimUiTokens.RadiusControl, SimUiTokens.InnerBorder, 2);

            float ix = barRect.X + 4f;
            float iy = barRect.Y + 4f;
            float iw = barRect.Width - 8f;
            float ih = barRect.Height - 8f;

            // Amber GOOD zone
            float g0 = Math.Max(0f, st.ZoneCenter - st.GoodHalfWidth);
            float g1 = Math.Min(1f, st.ZoneCenter + st.GoodHalfWidth);
            FillRoundedRect(batch, new SimRect(ix + g0 * iw, iy, Math.Max(6f, (g1 - g0) * iw), ih), SimUiTokens.RadiusSmall, new Color(218, 154, 36, 236));

            // Emerald PERFECT sweet-spot zone (scaled by Body skill)
            float p0 = Math.Max(0f, st.ZoneCenter - st.PerfectHalfWidth);
            float p1 = Math.Min(1f, st.ZoneCenter + st.PerfectHalfWidth);
            FillRoundedRect(batch, new SimRect(ix + p0 * iw, iy, Math.Max(8f, (p1 - p0) * iw), ih), SimUiTokens.RadiusSmall, new Color(36, 212, 126, 248));

            // Sweet-spot center crosshair line
            float cx = ix + st.ZoneCenter * iw;
            FillRect(batch, px, cx - 1f, iy + 2f, 2f, ih - 4f, Color.White * 0.88f);

            // Luminous moving needle
            float pos = game.IsResolvingStage ? st.LockedMarkerPos : game.MarkerPosition;
            float nx = ix + pos * iw;
            FillRoundedRect(batch, new SimRect(nx - 6f, barRect.Y - 3f, 12f, barRect.Height + 6f), 5, Color.White * 0.36f);
            FillRoundedRect(batch, new SimRect(nx - 3f, barRect.Y - 5f, 6f, barRect.Height + 10f), 3, Color.White);

            // Primary LOCK IN button + concise hints
            float btnW = 224f;
            var btnSimRect = new SimRect(dock.X + (dock.Width - btnW) * 0.5f, barRect.Bottom + 10f, btnW, 34f);
            lockBtnRect = ToXnaRect(btnSimRect);
            string btnLabel = game.IsResolvingStage ? "RESOLVING..." : (st.ActionVerb + "  [SPACE]");
            DrawPillButton(batch, btnSimRect, btnLabel, SimButtonVariant.Primary, hoverLock && !game.IsResolvingStage, !game.IsResolvingStage);

            float sideW = (btnSimRect.X - dock.X) - 24f;
            if (sideW >= 110f)
            {
                DrawFittedLine(batch, "Green = Perfect (+100)",
                    new SimRect(dock.X + 16f, btnSimRect.Y + 9f, sideW, 16f), briefMintStyle, SimTextAlign.Left);
                DrawFittedLine(batch, "Gold = Good (+65)",
                    new SimRect(btnSimRect.Right + 8f, btnSimRect.Y + 9f, sideW, 16f), subStyle, SimTextAlign.Right);
            }
        }

        void DrawFloatingScorecard(UISpriteBatch batch, Texture2D px, float screenW, float screenH)
        {
            var o = finalOutcome ?? game.ComputeOutcome();
            var r = o.Reward;

            // Left-anchored frosted Scorecard so BOTH Bob and the Sideline Drill Instructor salute unobstructed on the right!
            float cardW = Math.Max(352f, Math.Min(438f, screenW * 0.37f));
            float cardH = Math.Min(420f, Math.Max(340f, screenH - 110f));
            float cardX = 24f;
            float cardY = Math.Max(82f, (screenH - cardH) * 0.5f + 16f);
            var card = new SimRect(cardX, cardY, cardW, cardH);

            FillRoundedRect(batch, new SimRect(card.X + 4f, card.Y + 6f, card.Width, card.Height), SimUiTokens.RadiusModal, new Color(0, 0, 0, 120));
            FillRoundedRect(batch, card, SimUiTokens.RadiusModal, new Color(10, 22, 38, 226));
            DrawRoundedBorder(batch, card, SimUiTokens.RadiusModal, SimUiTokens.Mint * 0.78f, 2);

            float lx = card.X + 18f;
            float lw = card.Width - 36f;
            float ly = card.Y + 16f;

            DrawFittedLine(batch, "ACADEMY DRILL COMPLETE", new SimRect(lx, ly, lw, 22f), titleStyle, SimTextAlign.Left);
            ly += 26f;

            // Grade badge + score headline
            var gRect = new SimRect(lx, ly, 76f, 76f);
            Color gCol = o.Grade == "S" ? new Color(28, 178, 106)
                       : (o.Grade == "A" ? new Color(36, 134, 212)
                       : (o.Grade == "B" ? new Color(198, 144, 36) : new Color(176, 62, 68)));
            FillRoundedRect(batch, gRect, SimUiTokens.RadiusCard, gCol);
            DrawFittedLine(batch, o.Grade, new SimRect(gRect.X, gRect.Y + 14f, gRect.Width, 48f), gradeStyle, SimTextAlign.Center);

            float tx = gRect.Right + 14f;
            float tw = card.Right - tx - 18f;
            DrawFittedLine(batch, o.TotalPoints + " / 300 POINTS", new SimRect(tx, ly + 4f, tw, 22f), cyanStyle, SimTextAlign.Left);
            DrawFittedLine(batch, o.PerfectCount + " Perfect  \u2022  " + o.GoodCount + " Good", new SimRect(tx, ly + 28f, tw, 18f), bodyStyle, SimTextAlign.Left);
            DrawFittedWrapped(batch, o.Summary, new SimRect(tx, ly + 48f, tw, 36f), smallStyle, 2);
            ly = gRect.Bottom + 14f;

            FillRect(batch, px, lx, ly, lw, 1f, SimUiTokens.Divider);
            ly += 10f;

            // 3-Stage Breakdown
            DrawFittedLine(batch, "STAGE BREAKDOWN", new SimRect(lx, ly, lw, 17f), briefCyanStyle, SimTextAlign.Left);
            ly += 20f;
            for (int i = 0; i < CadetAcademyGame.TotalStages; i++)
            {
                var st = game.Stages[i];
                string rateText = st.Rating == CadetHitRating.Perfect ? "\u2605 PERFECT (+100)"
                                : (st.Rating == CadetHitRating.Good ? "\u2713 GOOD (+65)" : "\u2715 MISS (+15)");
                TextStyle rateStyle = st.Rating == CadetHitRating.Perfect ? briefMintStyle : (st.Rating == CadetHitRating.Good ? briefCyanStyle : subStyle);

                var rowRect = new SimRect(lx, ly, lw, 24f);
                FillRoundedRect(batch, rowRect, SimUiTokens.RadiusSmall, new Color(16, 34, 56, 175));
                DrawFittedLine(batch, (i + 1) + ". " + st.Title, new SimRect(rowRect.X + 10f, rowRect.Y + 4f, rowRect.Width * 0.56f, 16f), briefBodyStyle, SimTextAlign.Left);
                DrawFittedLine(batch, rateText, new SimRect(rowRect.X + rowRect.Width * 0.56f, rowRect.Y + 4f, rowRect.Width * 0.44f - 10f, 16f), rateStyle, SimTextAlign.Right);
                ly += 29f;
            }

            ly += 4f;
            FillRect(batch, px, lx, ly, lw, 1f, SimUiTokens.Divider);
            ly += 10f;

            // Rewards panel
            DrawFittedLine(batch, "REWARDS EARNED", new SimRect(lx, ly, lw, 17f), briefMintStyle, SimTextAlign.Left);
            ly += 20f;
            string rewardsLine1 = "+$" + r.BonusSimoleons +
                                  " Bonus   \u2022   Job Perf " + (r.JobPerformanceDelta >= 0 ? "+" : "") + r.JobPerformanceDelta;
            string rewardsLine2 = "Body +" + (r.BodyXpDelta / 100f).ToString("0.00", CultureInfo.InvariantCulture) + " pt" +
                                  "   \u2022   Fun " + (r.FunDelta >= 0 ? "+" : "") + r.FunDelta;
            DrawFittedLine(batch, rewardsLine1, new SimRect(lx, ly, lw, 19f), bodyStyle, SimTextAlign.Left);
            ly += 20f;
            DrawFittedLine(batch, rewardsLine2, new SimRect(lx, ly, lw, 19f), mintStyle, SimTextAlign.Left);

            // Claim & Return Home CTA
            float claimH = 42f;
            var claimSimRect = new SimRect(lx, card.Bottom - claimH - 14f, lw, claimH);
            claimBtnRect = ToXnaRect(claimSimRect);
            DrawPillButton(batch, claimSimRect, "CLAIM REWARDS & RETURN HOME [SPACE]", SimButtonVariant.Primary, hoverClaim);
        }

        void DrawTransitionOverlay(UISpriteBatch batch, Texture2D px, float screenW, float screenH)
        {
            // 1. Sleek Arrival Location Ribbon (fades smoothly over the live full-screen 3D Academy lot)
            float ba = transition.BannerAlpha;
            if (ba > 0.01f)
            {
                float bw = Math.Min(560f, screenW - 60f);
                float bh = 74f;
                float bx = (screenW - bw) * 0.5f;
                float by = Math.Max(78f, screenH * 0.13f);
                var bRect = new SimRect(bx, by, bw, bh);

                FillRoundedRect(batch, bRect, SimUiTokens.RadiusCard, new Color(8, 18, 34) * (0.86f * ba));
                DrawRoundedBorder(batch, bRect, SimUiTokens.RadiusCard, new Color(242, 198, 72) * (0.90f * ba), 2);
                FillRect(batch, px, bRect.X + 26f, bRect.Y + 43f, bRect.Width - 52f, 1f, new Color(242, 198, 72) * (0.55f * ba));

                bannerTitleStyle.Color = Color.White * ba;
                bannerSubStyle.Color = SimUiTokens.Cyan * ba;
                DrawFittedLine(batch, transition.LocationTitle, new SimRect(bRect.X + 16f, bRect.Y + 11f, bRect.Width - 32f, 28f), bannerTitleStyle, SimTextAlign.Center);
                DrawFittedLine(batch, transition.LocationSubtitle, new SimRect(bRect.X + 16f, bRect.Y + 48f, bRect.Width - 32f, 18f), bannerSubStyle, SimTextAlign.Center);
            }

            // 2. Smooth Fade-From-Black / Fade-To-Black Veil
            float va = transition.BlackVeilAlpha;
            if (va > 0.01f)
                FillRect(batch, px, 0f, 0f, screenW, screenH, Color.Black * va);
        }
    }
}
