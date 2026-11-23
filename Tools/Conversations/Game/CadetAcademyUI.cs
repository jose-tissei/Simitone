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
    // Native Simitone-styled floating overlay for Law Enforcement Level 2 ("Cadet"):
    // Academy Obstacle & Drill (DISP-05) featuring unified real-time 3D academy course rendering.
    public class CadetAcademyOverlay : SimUiElement
    {
        static CadetAcademyOverlay instance;
        static bool isOpen;

        const float MaxW = 860f;
        const float MaxH = 668f;

        VMAvatar avatar;
        CadetAcademyGame game;
        CadetAcademyOutcome finalOutcome;
        bool rewardsApplied;
        int savedSpeed = 1;
        bool prevLeftDown, prevSpaceDown, prevEnterDown, prevEscDown;
        bool hoverClose, hoverLock, hoverClaim;
        bool finishSoundPlayed;

        readonly CadetAcademySceneRenderer sceneRenderer = new CadetAcademySceneRenderer();
        readonly CctvAudioEngine audio = new CctvAudioEngine();

        Rectangle closeRect, timingBarHitRect, lockBtnRect, claimBtnRect;

        readonly TextStyle titleStyle;
        readonly TextStyle subStyle;
        readonly TextStyle bodyStyle;
        readonly TextStyle smallStyle;
        readonly TextStyle badgeStyle;
        readonly TextStyle cyanStyle;
        readonly TextStyle mintStyle;
        readonly TextStyle gradeStyle;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        CadetAcademyOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(19, SimUiTokens.DialogTitle, false);
            subStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            smallStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextSubtle, true);
            badgeStyle = SimUiTokens.MakeStyle(12, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
            gradeStyle = SimUiTokens.MakeStyle(42, Color.White, true);
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

                instance.game = new CadetAcademyGame(simName, bodyLvl, seed);
                instance.finalOutcome = null;
                instance.rewardsApplied = false;
                instance.finishSoundPlayed = false;

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
            string rewardSummary = "";
            if (applyRewards && instance.game != null && instance.game.IsFinished && !instance.rewardsApplied)
            {
                if (instance.finalOutcome == null)
                    instance.finalOutcome = instance.game.ComputeOutcome();
                rewardSummary = CareerDispatchDriver.ApplyReward(instance.avatar, instance.finalOutcome.Reward);
                instance.rewardsApplied = true;
            }

            isOpen = false;
            instance.Visible = false;
            if (instance.Parent != null)
                instance.Parent.Remove(instance);

            var vm = CareerDispatchDriver.CurrentVM();
            if (vm != null)
                vm.SpeedMultiplier = instance.savedSpeed > 0 ? instance.savedSpeed : 1;

            return "ok closed" + (rewardSummary.Length > 0 ? " [" + rewardSummary + "]" : "");
        }

        public static string StatusSummary()
        {
            return instance != null ? instance.StatusText() : "closed";
        }

        string StatusText()
        {
            if (game == null) return "idle";
            if (game.IsFinished)
            {
                var o = finalOutcome ?? game.ComputeOutcome();
                return "game=CadetAcademy open=" + IsOpen +
                       " finished=True grade=" + o.Grade +
                       " pts=" + o.TotalPoints +
                       " bodyLvl=" + game.BodySkillLevel +
                       " bodyXp=" + o.Reward.BodyXpDelta +
                       " bonus=$" + o.Reward.BonusSimoleons +
                       " perf=" + o.Reward.JobPerformanceDelta;
            }
            var st = game.ActiveStage;
            return "game=CadetAcademy open=" + IsOpen +
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
                case "lock":
                case "hit":
                case "space":
                    instance.TriggerLock();
                    return "ok " + instance.StatusText();
                case "perfect":
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
                default:
                    return "err unknown cadet action '" + a + "' (use: lock, perfect, auto, claim)";
            }
        }

        public static string FinishToSummary()
        {
            if (!IsOpen || instance == null || instance.game == null) return "err cadet academy not open";
            instance.game.AutoCompleteRemainingPerfect();
            instance.finalOutcome = instance.game.ComputeOutcome();
            return "ok " + instance.StatusText();
        }

        void TriggerLock()
        {
            if (game == null || game.IsFinished || game.IsResolvingStage) return;
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

            var kb = Keyboard.GetState();
            bool escNow = kb.IsKeyDown(Keys.Escape);
            bool spaceNow = kb.IsKeyDown(Keys.Space);
            bool enterNow = kb.IsKeyDown(Keys.Enter);

            if (escNow && !prevEscDown)
            {
                Close(game.IsFinished);
                prevEscDown = true;
                return;
            }

            if (!game.IsFinished)
            {
                if ((spaceNow && !prevSpaceDown) || (enterNow && !prevEnterDown))
                    TriggerLock();
            }
            else if ((spaceNow && !prevSpaceDown) || (enterNow && !prevEnterDown))
            {
                Close(true);
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

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverClose)
                {
                    Close(game.IsFinished);
                    return;
                }
                if (game.IsFinished)
                {
                    if (hoverClaim)
                    {
                        Close(true);
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

            float w = Math.Min(MaxW, screenW - 36f);
            float h = Math.Min(MaxH, screenH - 36f);
            var panel = new SimRect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);

            var headerBar = new SimRect(panel.X, panel.Y, panel.Width, 50f);
            var titleTextRect = new SimRect(panel.X + 18f, panel.Y + 10f, panel.Width - 76f, 30f);
            var closeSimRect = new SimRect(panel.Right - 42f, panel.Y + 10f, 30f, 30f);
            closeRect = ToXnaRect(closeSimRect);

            // 1. Native Simitone rounded glass modal shell + ocean-blue diagonal backdrop veil
            DrawDialogShell(batch, panel, true, screenW, screenH, SimUiTokens.RadiusModal);
            DrawTitleBar(batch, headerBar, titleTextRect, "POLICE ACADEMY  •  LEVEL 2 CADET — OBSTACLE & DRILL", titleStyle, SimUiTokens.RadiusModal);
            DrawPillButton(batch, closeSimRect, "X", SimButtonVariant.Close, hoverClose);

            // 2. Cadet status strip + 3-stage progress pills
            float pad = 18f;
            int sweetPct = (int)Math.Round(CadetAcademyGame.ComputePerfectZoneWidth(game.BodySkillLevel) * 100f);
            DrawFittedLine(batch,
                "Cadet " + game.SimName + "   •   Body Skill Lv." + game.BodySkillLevel + "  (Green Sweet Spot: " + sweetPct + "%)",
                new SimRect(panel.X + pad, headerBar.Bottom + 8f, panel.Width - pad * 2f, 20f),
                subStyle, SimTextAlign.Left);

            DrawStagePills(batch, panel, headerBar.Bottom + 32f, pad);

            // 3. Live Unified 3D Academy Course Viewport (Option 2 real-time 3D world!)
            float vpY = headerBar.Bottom + 72f;
            float vpW = panel.Width - pad * 2f;
            float vpH = Math.Min(346f, Math.Max(220f, panel.Height - 280f));
            var viewport = new SimRect(panel.X + pad, vpY, vpW, vpH);
            DrawLive3DViewport(batch, viewport);

            // 4. Interactive Timing Deck OR Final Scorecard
            if (!game.IsFinished)
                DrawTimingDeck(batch, px, panel, viewport, pad);
            else
                DrawScorecard(batch, panel, viewport, pad);
        }

        void DrawStagePills(UISpriteBatch batch, SimRect panel, float y, float pad)
        {
            float gap = 10f;
            float pillW = (panel.Width - pad * 2f - gap * 2f) / 3f;
            string[] labels = { "1. SPRINT", "2. VAULT", "3. TARGET DRILL" };

            for (int i = 0; i < CadetAcademyGame.TotalStages; i++)
            {
                var st = game.Stages[i];
                var r = new SimRect(panel.X + pad + i * (pillW + gap), y, pillW, 30f);
                bool active = !game.IsFinished && i == game.CurrentStageIndex;

                Color fill, border;
                string suffix = "";
                if (st.Rating == CadetHitRating.Perfect)
                {
                    fill = new Color(18, 92, 62, 232);
                    border = SimUiTokens.Mint;
                    suffix = "  ★ PERFECT";
                }
                else if (st.Rating == CadetHitRating.Good)
                {
                    fill = new Color(108, 82, 24, 232);
                    border = SimUiTokens.Amber;
                    suffix = "  ✓ GOOD";
                }
                else if (st.Rating == CadetHitRating.Miss)
                {
                    fill = new Color(118, 34, 42, 232);
                    border = SimUiTokens.Danger;
                    suffix = "  ✕ MISS";
                }
                else if (active)
                {
                    fill = new Color(22, 74, 128, 242);
                    border = SimUiTokens.Cyan;
                    suffix = "  ● LIVE";
                }
                else
                {
                    fill = SimUiTokens.CardBg;
                    border = SimUiTokens.InnerBorder;
                }

                FillRoundedRect(batch, r, SimUiTokens.RadiusControl, fill);
                DrawRoundedBorder(batch, r, SimUiTokens.RadiusControl, border, 1);
                DrawFittedLine(batch, labels[i] + suffix, new SimRect(r.X + 8f, r.Y + 6f, r.Width - 16f, 18f), badgeStyle, SimTextAlign.Center);
            }
        }

        void DrawLive3DViewport(UISpriteBatch batch, SimRect viewport)
        {
            FillRoundedRect(batch, viewport, SimUiTokens.RadiusCard, new Color(8, 16, 26, 245));
            var inner = new SimRect(viewport.X + 3f, viewport.Y + 3f, viewport.Width - 6f, viewport.Height - 6f);

            if (sceneRenderer.HasLiveFrame && sceneRenderer.TargetTexture != null)
            {
                var tex = sceneRenderer.TargetTexture;
                DrawLocalTexture(batch, tex, null,
                    new Vector2(inner.X, inner.Y),
                    new Vector2(inner.Width / tex.Width, inner.Height / tex.Height),
                    Color.White);
            }
            else
            {
                DrawFittedLine(batch, "INITIALIZING LIVE 3D ACADEMY COURSE...", inner, bodyStyle, SimTextAlign.Center);
            }

            DrawRoundedBorder(batch, viewport, SimUiTokens.RadiusCard, SimUiTokens.OuterBorder, 2);

            // Top-left live 3D camera telemetry pill
            var st = game.ActiveStage;
            string camTag = st != null
                ? "LIVE 3D COURSE  •  " + st.Title
                : "LIVE 3D COURSE  •  DRILL COMPLETE";
            var tagRect = new SimRect(inner.X + 10f, inner.Y + 10f, Math.Min(340f, inner.Width - 165f), 26f);
            FillRoundedRect(batch, tagRect, SimUiTokens.RadiusControl, new Color(8, 18, 30, 205));
            DrawFittedLine(batch, camTag, new SimRect(tagRect.X + 10f, tagRect.Y + 5f, tagRect.Width - 20f, 16f), cyanStyle, SimTextAlign.Left);

            // Top-right Body skill pill
            var bodyPill = new SimRect(inner.Right - 134f, inner.Y + 10f, 124f, 26f);
            FillRoundedRect(batch, bodyPill, SimUiTokens.RadiusControl, new Color(14, 62, 112, 215));
            DrawRoundedBorder(batch, bodyPill, SimUiTokens.RadiusControl, SimUiTokens.Cyan * 0.65f, 1);
            DrawFittedLine(batch, "BODY LV." + game.BodySkillLevel, new SimRect(bodyPill.X + 6f, bodyPill.Y + 5f, bodyPill.Width - 12f, 16f), badgeStyle, SimTextAlign.Center);

            // Bottom live callout strip
            if (!string.IsNullOrEmpty(game.LastCallout))
            {
                Color bg = new Color(8, 18, 30, 212);
                if (game.LastRating == CadetHitRating.Perfect) bg = new Color(16, 108, 70, 232);
                else if (game.LastRating == CadetHitRating.Good) bg = new Color(142, 100, 22, 232);
                else if (game.LastRating == CadetHitRating.Miss) bg = new Color(146, 36, 44, 232);

                var callRect = new SimRect(inner.X + 12f, inner.Bottom - 34f, inner.Width - 24f, 26f);
                FillRoundedRect(batch, callRect, SimUiTokens.RadiusControl, bg);
                DrawFittedLine(batch, game.LastCallout, new SimRect(callRect.X + 10f, callRect.Y + 5f, callRect.Width - 20f, 16f), badgeStyle, SimTextAlign.Center);
            }
        }

        void DrawTimingDeck(UISpriteBatch batch, Texture2D px, SimRect panel, SimRect viewport, float pad)
        {
            var st = game.ActiveStage;
            if (st == null) return;

            float subY = viewport.Bottom + 14f;
            DrawFittedLine(batch, st.Subtitle,
                new SimRect(panel.X + pad + 8f, subY, panel.Width - (pad + 8f) * 2f, 20f),
                bodyStyle, SimTextAlign.Center);

            var barRect = new SimRect(panel.X + pad + 14f, subY + 26f, panel.Width - (pad + 14f) * 2f, 38f);
            timingBarHitRect = ToXnaRect(barRect);

            // Outer dark timing track
            FillRoundedRect(batch, barRect, SimUiTokens.RadiusControl, new Color(42, 22, 30, 240));
            DrawRoundedBorder(batch, barRect, SimUiTokens.RadiusControl, SimUiTokens.InnerBorder, 2);

            float ix = barRect.X + 4f;
            float iy = barRect.Y + 4f;
            float iw = barRect.Width - 8f;
            float ih = barRect.Height - 8f;

            // Amber GOOD zone
            float g0 = Math.Max(0f, st.ZoneCenter - st.GoodHalfWidth);
            float g1 = Math.Min(1f, st.ZoneCenter + st.GoodHalfWidth);
            FillRoundedRect(batch, new SimRect(ix + g0 * iw, iy, Math.Max(6f, (g1 - g0) * iw), ih), SimUiTokens.RadiusSmall, new Color(216, 152, 36, 232));

            // Emerald PERFECT sweet-spot zone (scaled by Body skill!)
            float p0 = Math.Max(0f, st.ZoneCenter - st.PerfectHalfWidth);
            float p1 = Math.Min(1f, st.ZoneCenter + st.PerfectHalfWidth);
            FillRoundedRect(batch, new SimRect(ix + p0 * iw, iy, Math.Max(8f, (p1 - p0) * iw), ih), SimUiTokens.RadiusSmall, new Color(36, 208, 124, 248));

            // Sweet-spot center crosshair line
            float cx = ix + st.ZoneCenter * iw;
            FillRect(batch, px, cx - 1f, iy + 2f, 2f, ih - 4f, Color.White * 0.85f);

            // Moving luminous needle marker
            float pos = game.IsResolvingStage ? st.LockedMarkerPos : game.MarkerPosition;
            float nx = ix + pos * iw;
            FillRoundedRect(batch, new SimRect(nx - 6f, barRect.Y - 3f, 12f, barRect.Height + 6f), 5, Color.White * 0.35f);
            FillRoundedRect(batch, new SimRect(nx - 3f, barRect.Y - 5f, 6f, barRect.Height + 10f), 3, Color.White);

            // Primary LOCK IN button
            float btnW = 248f;
            var btnSimRect = new SimRect(panel.X + (panel.Width - btnW) * 0.5f, barRect.Bottom + 18f, btnW, 40f);
            lockBtnRect = ToXnaRect(btnSimRect);
            string btnLabel = game.IsResolvingStage ? "RESOLVING..." : (st.ActionVerb + "  [SPACE]");
            DrawPillButton(batch, btnSimRect, btnLabel, SimButtonVariant.Primary, hoverLock && !game.IsResolvingStage, !game.IsResolvingStage);

            DrawFittedLine(batch,
                "Green = Perfect (+100)   •   Gold = Good (+65)   •   Higher Body Skill widens the Green Zone",
                new SimRect(panel.X + pad, btnSimRect.Bottom + 10f, panel.Width - pad * 2f, 18f),
                smallStyle, SimTextAlign.Center);
        }

        void DrawScorecard(UISpriteBatch batch, SimRect panel, SimRect viewport, float pad)
        {
            var o = finalOutcome ?? game.ComputeOutcome();
            var r = o.Reward;

            var card = new SimRect(panel.X + pad + 10f, viewport.Bottom + 14f, panel.Width - (pad + 10f) * 2f, 102f);
            FillRoundedRect(batch, card, SimUiTokens.RadiusCard, SimUiTokens.CardBgAlt);
            DrawRoundedBorder(batch, card, SimUiTokens.RadiusCard, SimUiTokens.OuterBorder, 1);

            var gRect = new SimRect(card.X + 16f, card.Y + 12f, 78f, 78f);
            Color gCol = o.Grade == "S" ? new Color(28, 176, 104)
                       : (o.Grade == "A" ? new Color(36, 134, 212)
                       : (o.Grade == "B" ? new Color(198, 144, 36) : new Color(176, 62, 68)));
            FillRoundedRect(batch, gRect, SimUiTokens.RadiusCard, gCol);
            DrawFittedLine(batch, o.Grade, new SimRect(gRect.X, gRect.Y + 14f, gRect.Width, 50f), gradeStyle, SimTextAlign.Center);

            float tx = gRect.Right + 16f;
            float tw = card.Right - tx - 14f;
            DrawFittedLine(batch,
                "ACADEMY SCORECARD  —  " + o.TotalPoints + " / 300 PTS  (" + o.PerfectCount + " Perfect, " + o.GoodCount + " Good)",
                new SimRect(tx, card.Y + 12f, tw, 22f), bodyStyle, SimTextAlign.Left);
            DrawFittedLine(batch, o.Summary,
                new SimRect(tx, card.Y + 38f, tw, 20f), subStyle, SimTextAlign.Left);

            string rewardsLine = "+$" + r.BonusSimoleons +
                                 "   •   Perf " + (r.JobPerformanceDelta >= 0 ? "+" : "") + r.JobPerformanceDelta +
                                 "   •   Body +" + (r.BodyXpDelta / 100f).ToString("0.00", CultureInfo.InvariantCulture) + " pt" +
                                 "   •   Fun " + (r.FunDelta >= 0 ? "+" : "") + r.FunDelta;
            DrawFittedLine(batch, rewardsLine,
                new SimRect(tx, card.Y + 64f, tw, 22f), mintStyle, SimTextAlign.Left);

            float claimW = 280f;
            var claimSimRect = new SimRect(panel.X + (panel.Width - claimW) * 0.5f, card.Bottom + 16f, claimW, 40f);
            claimBtnRect = ToXnaRect(claimSimRect);
            DrawPillButton(batch, claimSimRect, "CLAIM REWARDS & RETURN", SimButtonVariant.Primary, hoverClaim);
        }
    }
}
