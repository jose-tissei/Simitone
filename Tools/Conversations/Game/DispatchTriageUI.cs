using System;
using System.Collections.Generic;
using System.Globalization;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Real-Lot UI Overlay for Law Enforcement Level 4 ("Desk Sergeant"):
    // 911 Dispatch Triage (DISP-07).
    // Renders cleanly over the live 100%-real-TS1-game-assets Precinct Front Desk (works in 3D & 2D):
    //   - Pulsing clickable world markers directly over the real TS1 Desk Phone & real Walk-In Sim
    //   - Tactile Phone Switchboard Modal (SimCity PD desk telephone console with Department Buttons)
    //   - Tactile Paper Forms Clipboard Modal (4 physical color-coded stamped Referral Forms to hand to Walk-In Sims)
    //   - S/A/B/C Shift Scorecard & seamless return to the player's home lot
    public sealed class DispatchTriageOverlay : SimUiElement
    {
        static DispatchTriageOverlay instance;
        static bool isOpen;

        readonly TextStyle titleStyle;
        readonly TextStyle headerStyle;
        readonly TextStyle subStyle;
        readonly TextStyle bodyStyle;
        readonly TextStyle darkBodyStyle;
        readonly TextStyle darkHeaderStyle;
        readonly TextStyle darkSmallStyle;
        readonly TextStyle smallStyle;
        readonly TextStyle badgeStyle;
        readonly TextStyle cyanStyle;
        readonly TextStyle mintStyle;
        readonly TextStyle goldStyle;
        readonly TextStyle redStyle;
        readonly TextStyle gradeStyle;
        readonly TextStyle lcdStyle;
        readonly TextStyle lcdDimStyle;

        Rectangle startBtnRect;
        Rectangle skipBtnRect;
        Rectangle endShiftBtnRect;
        Rectangle claimBtnRect;

        Rectangle phoneMarkerRect;
        Rectangle walkInMarkerRect;
        Rectangle queuePhoneBtnRect;
        Rectangle queueWalkInBtnRect;

        Rectangle modalCloseRect;
        Rectangle modalCalmRect;
        readonly Rectangle[] deptBtnRects = new Rectangle[4];

        bool hoverStart, hoverSkip, hoverEnd, hoverClaim;
        bool hoverPhoneMarker, hoverWalkInMarker, hoverQueuePhone, hoverQueueWalkIn;
        bool hoverModalClose, hoverModalCalm;
        int hoverDeptIdx = -1;

        bool prevLeftDown, prevEnterDown, prevSpaceDown, prevEscDown;
        bool prevD1, prevD2, prevD3, prevD4, prevKeyC;
        float uiPulseSec;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        DispatchTriageOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(17, SimUiTokens.DialogTitle, true);
            headerStyle = SimUiTokens.MakeStyle(14, SimUiTokens.DialogTitle, true);
            subStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextMuted, true);
            bodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            darkBodyStyle = SimUiTokens.MakeStyle(12, new Color(36, 32, 28), true);
            darkHeaderStyle = SimUiTokens.MakeStyle(13, new Color(26, 24, 22), true);
            darkSmallStyle = SimUiTokens.MakeStyle(10, new Color(82, 74, 66), true);
            smallStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextSubtle, true);
            badgeStyle = SimUiTokens.MakeStyle(11, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Mint, true);
            goldStyle = SimUiTokens.MakeStyle(12, new Color(246, 206, 94), true);
            redStyle = SimUiTokens.MakeStyle(12, new Color(244, 118, 118), true);
            gradeStyle = SimUiTokens.MakeStyle(36, Color.White, true);
            lcdStyle = SimUiTokens.MakeStyle(13, new Color(172, 248, 186), true);
            lcdDimStyle = SimUiTokens.MakeStyle(11, new Color(118, 192, 138), true);
        }

        public static void OpenForRealLot()
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null) instance = new DispatchTriageOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                isOpen = true;
                instance.prevLeftDown = true;
                instance.prevEnterDown = true;
                instance.prevSpaceDown = true;
                instance.prevEscDown = true;
            }
            catch (Exception e) { Boot.Log("dispatch-triage overlay open error: " + e.Message); }
        }

        public static void Close()
        {
            isOpen = false;
            try
            {
                if (instance != null && instance.Parent != null)
                    instance.Parent.Remove(instance);
            }
            catch { }
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            if (!isOpen) return;
            try { DispatchTriageDriver.PreDrawFrame(); }
            catch (Exception e) { Boot.Log("dispatch-triage PreDraw error: " + e.Message); }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen) return;

            uiPulseSec += 1f / 60f;
            DispatchTriageDriver.TickAlertSweep();

            var game = DispatchTriageDriver.Game;
            if (game == null || !DispatchTriageDriver.IsPrecinctLotLive)
            {
                Close();
                return;
            }

            var m = state.MouseState;
            var kb = state.KeyboardState;
            int mx = m.X;
            int my = m.Y;
            bool leftDown = m.LeftButton == ButtonState.Pressed;
            bool justClicked = leftDown && !prevLeftDown;
            prevLeftDown = leftDown;

            bool enterDown = kb.IsKeyDown(Keys.Enter);
            bool spaceDown = kb.IsKeyDown(Keys.Space);
            bool escDown = kb.IsKeyDown(Keys.Escape);
            bool d1 = kb.IsKeyDown(Keys.D1) || kb.IsKeyDown(Keys.NumPad1);
            bool d2 = kb.IsKeyDown(Keys.D2) || kb.IsKeyDown(Keys.NumPad2);
            bool d3 = kb.IsKeyDown(Keys.D3) || kb.IsKeyDown(Keys.NumPad3);
            bool d4 = kb.IsKeyDown(Keys.D4) || kb.IsKeyDown(Keys.NumPad4);
            bool keyC = kb.IsKeyDown(Keys.C);

            bool enterPressed = enterDown && !prevEnterDown;
            bool spacePressed = spaceDown && !prevSpaceDown;
            bool escPressed = escDown && !prevEscDown;
            bool d1Pressed = d1 && !prevD1;
            bool d2Pressed = d2 && !prevD2;
            bool d3Pressed = d3 && !prevD3;
            bool d4Pressed = d4 && !prevD4;
            bool cPressed = keyC && !prevKeyC;

            prevEnterDown = enterDown;
            prevSpaceDown = spaceDown;
            prevEscDown = escDown;
            prevD1 = d1;
            prevD2 = d2;
            prevD3 = d3;
            prevD4 = d4;
            prevKeyC = keyC;

            hoverStart = hoverSkip = hoverEnd = hoverClaim = false;
            hoverPhoneMarker = hoverWalkInMarker = hoverQueuePhone = hoverQueueWalkIn = false;
            hoverModalClose = hoverModalCalm = false;
            hoverDeptIdx = -1;

            if (game.IsBriefing)
            {
                hoverStart = startBtnRect.Contains(mx, my);
                hoverSkip = skipBtnRect.Contains(mx, my);

                if (enterPressed || spacePressed || (justClicked && hoverStart))
                    DispatchTriageDriver.HandleTestAction("start");
                else if (escPressed || (justClicked && hoverSkip))
                    DispatchTriageDriver.RequestReturnHome(false);
                return;
            }

            if (game.IsFinished && DispatchTriageDriver.FinalOutcome != null)
            {
                hoverClaim = claimBtnRect.Contains(mx, my);
                if (enterPressed || spacePressed || escPressed || (justClicked && hoverClaim))
                    DispatchTriageDriver.RequestReturnHome(true);
                return;
            }

            var modal = DispatchTriageDriver.ActiveModal;
            if (modal != TriageModalMode.None)
            {
                hoverModalClose = modalCloseRect.Contains(mx, my);
                hoverModalCalm = modalCalmRect.Contains(mx, my);
                for (int i = 0; i < 4; i++)
                    if (deptBtnRects[i].Contains(mx, my)) hoverDeptIdx = i;

                if (escPressed || (justClicked && hoverModalClose))
                {
                    DispatchTriageDriver.CloseActiveModal();
                    return;
                }
                if (cPressed || (justClicked && hoverModalCalm))
                {
                    DispatchTriageDriver.CalmActiveModalCaller();
                    return;
                }
                if (d1Pressed || (justClicked && hoverDeptIdx == 0))
                {
                    DispatchTriageDriver.RouteActiveModalCase(DispatchRouteAction.DispatchPatrol);
                    return;
                }
                if (d2Pressed || (justClicked && hoverDeptIdx == 1))
                {
                    DispatchTriageDriver.RouteActiveModalCase(DispatchRouteAction.DispatchFire);
                    return;
                }
                if (d3Pressed || (justClicked && hoverDeptIdx == 2))
                {
                    DispatchTriageDriver.RouteActiveModalCase(DispatchRouteAction.CommunityAid);
                    return;
                }
                if (d4Pressed || (justClicked && hoverDeptIdx == 3))
                {
                    DispatchTriageDriver.RouteActiveModalCase(DispatchRouteAction.DismissCall);
                    return;
                }
                return;
            }

            // Live precinct lobby (no modal open): player can click world markers, queue pills, or native Pie Menu!
            hoverEnd = endShiftBtnRect.Contains(mx, my);
            hoverPhoneMarker = !phoneMarkerRect.IsEmpty && phoneMarkerRect.Contains(mx, my);
            hoverWalkInMarker = !walkInMarkerRect.IsEmpty && walkInMarkerRect.Contains(mx, my);
            hoverQueuePhone = !queuePhoneBtnRect.IsEmpty && queuePhoneBtnRect.Contains(mx, my);
            hoverQueueWalkIn = !queueWalkInBtnRect.IsEmpty && queueWalkInBtnRect.Contains(mx, my);

            if (justClicked && hoverEnd)
            {
                DispatchTriageDriver.RequestReturnHome(false);
                return;
            }
            if (justClicked && (hoverPhoneMarker || hoverQueuePhone))
            {
                DispatchTriageDriver.OpenPhoneSwitchboardModal();
                return;
            }
            if (justClicked && (hoverWalkInMarker || hoverQueueWalkIn))
            {
                DispatchTriageDriver.OpenPaperFormsModal();
                return;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen) return;
            var game = DispatchTriageDriver.Game;
            if (game == null) return;

            try
            {
                var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
                float sw, sh;
                GetScreenSize(batch, out sw, out sh);

                startBtnRect = Rectangle.Empty;
                skipBtnRect = Rectangle.Empty;
                endShiftBtnRect = Rectangle.Empty;
                claimBtnRect = Rectangle.Empty;
                phoneMarkerRect = Rectangle.Empty;
                walkInMarkerRect = Rectangle.Empty;
                queuePhoneBtnRect = Rectangle.Empty;
                queueWalkInBtnRect = Rectangle.Empty;
                modalCloseRect = Rectangle.Empty;
                modalCalmRect = Rectangle.Empty;
                for (int i = 0; i < 4; i++) deptBtnRects[i] = Rectangle.Empty;

                if (game.IsBriefing)
                {
                    DrawBriefingCard(batch, px, sw, sh, game);
                    return;
                }

                if (game.IsFinished && DispatchTriageDriver.FinalOutcome != null)
                {
                    DrawScorecard(batch, px, sw, sh, game, DispatchTriageDriver.FinalOutcome);
                    return;
                }

                // Always show slim Top Precinct Bar
                DrawTopPrecinctBar(batch, px, sw, game);

                var modal = DispatchTriageDriver.ActiveModal;
                if (modal == TriageModalMode.None)
                {
                    DrawLeftAlertStrip(batch, px, game);
                    DrawWorldActionMarkers(batch, px, sw, sh);
                }
                else
                {
                    // Dim world subtly behind modal
                    FillRect(batch, px, 0, 0, sw, sh, new Color(8, 12, 20, 122));
                    var mc = DispatchTriageDriver.GetModalCase();
                    if (mc != null)
                    {
                        if (modal == TriageModalMode.PhoneSwitchboard)
                            DrawPhoneSwitchboardModal(batch, px, sw, sh, game, mc);
                        else if (modal == TriageModalMode.PaperForms)
                            DrawPaperFormsModal(batch, px, sw, sh, game, mc);
                    }
                }

                // Bottom feedback banner
                if (!string.IsNullOrEmpty(game.LastFeedbackBanner) && game.FeedbackBannerTimerSec > 0f)
                {
                    int bw = (int)Math.Min(640f, sw - 48f);
                    int bx = (int)((sw - bw) * 0.5f);
                    int by = (int)(sh - 56f);
                    Color bg = game.LastFeedbackPositive
                        ? new Color(16, 78, 56, 232)
                        : new Color(118, 28, 34, 232);
                    Color border = game.LastFeedbackPositive
                        ? new Color(96, 224, 168, 230)
                        : new Color(246, 118, 118, 230);
                    DrawCard(batch, new Rectangle(bx, by, bw, 36), bg, border, 10);
                    DrawFittedLine(batch, game.LastFeedbackBanner, new SimRect(bx + 14, by + 8, bw - 28, 20), badgeStyle, SimTextAlign.Center);
                }
            }
            catch (Exception e)
            {
                Boot.Log("dispatch-triage draw error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---- Briefing Card (compact left card so real TS1 Precinct Desk stays clearly visible!) ----

        void DrawBriefingCard(UISpriteBatch batch, Texture2D px, float sw, float sh, DispatchTriageGame game)
        {
            int cw = (int)Math.Min(482f, sw - 48f);
            int ch = 336;
            int cx = 24;
            int cy = (int)Math.Max(24f, Math.Min(sh - ch - 24f, (sh - ch) * 0.42f));
            var rect = new Rectangle(cx, cy, cw, ch);

            DrawCard(batch, rect, new Color(14, 24, 38, 236), new Color(78, 152, 226, 205), 14);

            int x = cx + 18;
            int innerW = cw - 36;
            int y = cy + 16;

            DrawPill(batch, new Rectangle(x, y, innerW, 22), new Color(36, 98, 178, 230), 6);
            DrawFittedLine(batch, "LAW ENFORCEMENT  |  LVL 4 DESK SERGEANT", new SimRect(x + 10, y + 2, innerW - 20, 18), badgeStyle, SimTextAlign.Left);
            y += 30;

            DrawFittedLine(batch, "PRECINCT DESK: 911 TRIAGE", new SimRect(x, y, innerW, 22), titleStyle, SimTextAlign.Left);
            y += 24;

            DrawFittedLine(batch, "Sgt. " + game.SergeantName + "  |  Logic " + game.LogicSkillLevel + "  |  Charisma " + game.CharismaSkillLevel,
                new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 24;

            FillRect(batch, px, x, y, innerW, 1f, new Color(90, 140, 200, 95));
            y += 12;

            DrawFittedLine(batch, "- Live SimCity PD Precinct Front Desk (3D & 2D).", new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, "- CLICK THE DESK PHONE when 911 rings ->", new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 19;
            DrawFittedLine(batch, "  opens Switchboard Phone UI with Dept Buttons.", new SimRect(x, y, innerW, 18), subStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, "- CLICK WALK-IN CITIZENS at the counter ->", new SimRect(x, y, innerW, 18), mintStyle, SimTextAlign.Left);
            y += 19;
            DrawFittedLine(batch, "  opens Precinct Paper Forms to stamp & hand off.", new SimRect(x, y, innerW, 18), subStyle, SimTextAlign.Left);
            y += 24;
            DrawFittedLine(batch, "Route 6 real calls & walk-ins cleanly for S-Rank!", new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 28;

            int btnW = (innerW - 12) * 2 / 3;
            int skipW = innerW - 12 - btnW;
            startBtnRect = new Rectangle(x, y, btnW, 38);
            skipBtnRect = new Rectangle(x + btnW + 12, y, skipW, 38);

            DrawButton(batch, startBtnRect,
                hoverStart ? new Color(42, 168, 122, 245) : new Color(28, 138, 98, 240),
                new Color(128, 240, 194, 230), 8, "START SHIFT [ENTER]", badgeStyle);
            DrawButton(batch, skipBtnRect,
                hoverSkip ? new Color(68, 82, 102, 235) : new Color(44, 54, 70, 225),
                new Color(138, 156, 182, 180), 8, "SKIP [ESC]", bodyStyle);
        }

        // ---- Slim Top Precinct Bar ----

        void DrawTopPrecinctBar(UISpriteBatch batch, Texture2D px, float sw, DispatchTriageGame game)
        {
            int barW = (int)Math.Min(1000f, sw - 28f);
            int barX = (int)((sw - barW) * 0.5f);
            int barY = 12;
            int barH = 44;

            DrawCard(batch, new Rectangle(barX, barY, barW, barH),
                new Color(14, 24, 38, 228), new Color(76, 146, 218, 195), 11);

            int x = barX + 14;
            int cy = barY + 10;

            string title = "SIMCITY PD  |  FRONT DESK";
            float titleW = Math.Min(240f, headerStyle.MeasureString(title).X + 16f);
            DrawFittedLine(batch, title, new SimRect(x, barY + 12, titleW, 20), headerStyle, SimTextAlign.Left);
            x += (int)titleW + 10;

            // Cases counter pill
            var casesPill = new Rectangle(x, cy, 112, 24);
            DrawPill(batch, casesPill, new Color(28, 72, 134, 230), 6);
            DrawFittedLine(batch, "CASES " + game.ResolvedCount + "/" + game.TotalCases,
                new SimRect(casesPill.X + 6, casesPill.Y + 3, casesPill.Width - 12, 18), badgeStyle, SimTextAlign.Center);
            x += 120;

            // Score pill
            var scorePill = new Rectangle(x, cy, 114, 24);
            DrawPill(batch, scorePill, new Color(24, 96, 74, 230), 6);
            DrawFittedLine(batch, "SCORE " + game.Score,
                new SimRect(scorePill.X + 6, scorePill.Y + 3, scorePill.Width - 12, 18), badgeStyle, SimTextAlign.Center);
            x += 124;

            // Live Department Unit availability pills
            x = DrawUnitPill(batch, x, cy, 118, "PATROL " + game.AvailablePatrolUnits + "/" + DispatchTriageGame.MaxPatrolUnits,
                game.AvailablePatrolUnits > 0, new Color(36, 104, 192, 230));
            x = DrawUnitPill(batch, x, cy, 98, "FIRE " + game.AvailableFireUnits + "/" + DispatchTriageGame.MaxFireUnits,
                game.AvailableFireUnits > 0, new Color(186, 52, 46, 230));
            DrawUnitPill(batch, x, cy, 94, "AID " + game.AvailableCommunityUnits + "/" + DispatchTriageGame.MaxCommunityUnits,
                game.AvailableCommunityUnits > 0, new Color(28, 134, 102, 230));

            // Right End button
            endShiftBtnRect = new Rectangle(barX + barW - 96, barY + 8, 84, 28);
            DrawButton(batch, endShiftBtnRect,
                hoverEnd ? new Color(138, 46, 52, 240) : new Color(86, 36, 44, 225),
                new Color(224, 128, 132, 195), 7, "END", badgeStyle);
        }

        int DrawUnitPill(UISpriteBatch batch, int x, int y, int w, string text, bool ready, Color activeBg)
        {
            Color bg = ready ? activeBg : new Color(56, 62, 74, 210);
            DrawPill(batch, new Rectangle(x, y, w, 24), bg, 6);
            DrawFittedLine(batch, text, new SimRect(x + 4, y + 3, w - 8, 18),
                ready ? badgeStyle : subStyle, SimTextAlign.Center);
            return x + w + 8;
        }

        // ---- Compact Left Alert Strip (shows ringing 911 lines & approaching/arrived Walk-Ins) ----

        void DrawLeftAlertStrip(UISpriteBatch batch, Texture2D px, DispatchTriageGame game)
        {
            var ph = DispatchTriageDriver.GetFirstActivePhoneCase();
            var wi = game.ActiveWalkIn;
            if (ph == null && (wi == null || wi.IsResolved)) return;

            int x = 18;
            int y = 68;
            int w = 278;

            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiPulseSec * 6.5f);

            if (ph != null)
            {
                queuePhoneBtnRect = new Rectangle(x, y, w, 68);
                Color bg = hoverQueuePhone ? new Color(46, 30, 38, 242) : new Color(24, 28, 42, 234);
                Color border = Color.Lerp(new Color(220, 74, 68, 215), new Color(252, 206, 84, 245), pulse);
                DrawCard(batch, queuePhoneBtnRect, bg, border, 10);

                DrawPill(batch, new Rectangle(x + 10, y + 8, 102, 18), new Color(202, 52, 52, 235), 5);
                DrawFittedLine(batch, "911 RINGING", new SimRect(x + 14, y + 8, 94, 17), badgeStyle, SimTextAlign.Center);
                bool urgentPh = ph.RemainingUrgencySec < 6.0f;
                DrawFittedLine(batch, Math.Ceiling(ph.RemainingUrgencySec).ToString("0", CultureInfo.InvariantCulture) + "s",
                    new SimRect(x + w - 54, y + 8, 42, 18), urgentPh ? redStyle : goldStyle, SimTextAlign.Right);

                DrawFittedLine(batch, ph.CallerName + " — Click Desk Phone",
                    new SimRect(x + 10, y + 30, w - 20, 18), bodyStyle, SimTextAlign.Left);

                // Urgency mini-bar
                float frac = ph.UrgencyFraction;
                FillRect(batch, px, x + 10, y + 52, w - 20, 6, new Color(42, 48, 62, 220));
                FillRect(batch, px, x + 10, y + 52, (w - 20) * frac, 6,
                    frac < 0.35f ? new Color(238, 78, 72) : new Color(244, 192, 68));

                y += 78;
            }

            if (wi != null && !wi.IsResolved)
            {
                bool arrived = !wi.WalkInLeaving && wi.WalkInApproach >= 0.85f;
                var cardRect = new Rectangle(x, y, w, 68);
                if (arrived) queueWalkInBtnRect = cardRect;

                Color bg = (arrived && hoverQueueWalkIn) ? new Color(24, 52, 56, 242) : new Color(18, 32, 46, 234);
                Color border = arrived
                    ? Color.Lerp(new Color(62, 206, 178, 225), new Color(138, 246, 212, 250), pulse)
                    : new Color(84, 140, 192, 185);
                DrawCard(batch, cardRect, bg, border, 10);

                DrawPill(batch, new Rectangle(x + 10, y + 8, 128, 18),
                    arrived ? new Color(28, 146, 114, 235) : new Color(56, 98, 152, 225), 5);
                DrawFittedLine(batch, arrived ? "WALK-IN AT DESK" : "WALK-IN ENTERING",
                    new SimRect(x + 14, y + 8, 120, 17), badgeStyle, SimTextAlign.Center);
                bool urgentWi = wi.RemainingUrgencySec < 6.0f;
                DrawFittedLine(batch, Math.Ceiling(wi.RemainingUrgencySec).ToString("0", CultureInfo.InvariantCulture) + "s",
                    new SimRect(x + w - 54, y + 8, 42, 18), urgentWi ? redStyle : cyanStyle, SimTextAlign.Right);

                DrawFittedLine(batch, arrived ? (wi.CallerName + " — Click Citizen!") : (wi.CallerName + " walking to counter..."),
                    new SimRect(x + 10, y + 30, w - 20, 18), bodyStyle, SimTextAlign.Left);

                float frac = wi.UrgencyFraction;
                FillRect(batch, px, x + 10, y + 52, w - 20, 6, new Color(42, 48, 62, 220));
                FillRect(batch, px, x + 10, y + 52, (w - 20) * frac, 6,
                    arrived ? new Color(68, 218, 172) : new Color(86, 168, 234));
            }
        }

        // ---- Direct Clickable Pulsing World Markers over Real TS1 Desk Phone & Real Walk-In Sim ----

        void DrawWorldActionMarkers(UISpriteBatch batch, Texture2D px, float sw, float sh)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiPulseSec * 7.2f);
            float bob = (float)Math.Sin(uiPulseSec * 5.2f) * 4f;

            // 1. Pulsing Marker above the REAL TS1 Desk Phone when 911 is ringing!
            var ph = DispatchTriageDriver.GetFirstActivePhoneCase();
            if (ph != null && DispatchTriageDriver.DeskPhoneEntity != null)
            {
                var pt = DispatchTriageDriver.GetEntityScreenPos(DispatchTriageDriver.DeskPhoneEntity, -54f);
                if (pt.HasValue)
                {
                    int mw = 252;
                    int mh = 34;
                    int mx = (int)Math.Max(18f, Math.Min(sw - mw - 18f, pt.Value.X - mw * 0.5f));
                    int my = (int)Math.Max(68f, Math.Min(sh - mh - 64f, pt.Value.Y - mh + bob));
                    phoneMarkerRect = new Rectangle(mx, my, mw, mh);

                    Color bg = hoverPhoneMarker
                        ? new Color(220, 62, 52, 248)
                        : Color.Lerp(new Color(172, 36, 36, 236), new Color(214, 72, 44, 245), pulse);
                    Color border = new Color(255, 226, 128, 245);
                    DrawCard(batch, phoneMarkerRect, bg, border, 10);
                    DrawFittedLine(batch, "911 RINGING! CLICK PHONE",
                        new SimRect(mx + 10, my + 7, mw - 20, 20), badgeStyle, SimTextAlign.Center);

                    // Downward pointer tick toward phone
                    FillRect(batch, px, mx + mw / 2 - 4, my + mh, 8, 6, border);
                }
            }

            // 2. Pulsing Marker above the REAL Walk-In Citizen once arrived at the front counter!
            var wi = DispatchTriageDriver.GetArrivedWalkInCase();
            if (wi != null && DispatchTriageDriver.WalkInAvatar != null)
            {
                var pt = DispatchTriageDriver.GetEntityScreenPos(DispatchTriageDriver.WalkInAvatar, -92f);
                if (pt.HasValue)
                {
                    int mw = 268;
                    int mh = 34;
                    int mx = (int)Math.Max(18f, Math.Min(sw - mw - 18f, pt.Value.X - mw * 0.5f));
                    int my = (int)Math.Max(68f, Math.Min(sh - mh - 64f, pt.Value.Y - mh - bob));
                    // Avoid overlapping phone marker if close on screen
                    if (!phoneMarkerRect.IsEmpty && new Rectangle(mx, my, mw, mh).Intersects(phoneMarkerRect))
                        my = Math.Min((int)(sh - mh - 64f), phoneMarkerRect.Bottom + 12);

                    walkInMarkerRect = new Rectangle(mx, my, mw, mh);

                    Color bg = hoverWalkInMarker
                        ? new Color(34, 174, 134, 248)
                        : Color.Lerp(new Color(22, 126, 98, 236), new Color(34, 166, 128, 245), pulse);
                    Color border = new Color(156, 250, 220, 245);
                    DrawCard(batch, walkInMarkerRect, bg, border, 10);
                    DrawFittedLine(batch, "CLICK " + wi.CallerName.ToUpperInvariant() + " (FORMS)",
                        new SimRect(mx + 10, my + 7, mw - 20, 20), badgeStyle, SimTextAlign.Center);

                    FillRect(batch, px, mx + mw / 2 - 4, my + mh, 8, 6, border);
                }
            }
        }

        // ---- MODAL 1: Tactile Phone Switchboard UI (Clicking the ringing real TS1 Desk Phone!) ----

        void DrawPhoneSwitchboardModal(UISpriteBatch batch, Texture2D px, float sw, float sh, DispatchTriageGame game, DispatchActiveCase mc)
        {
            int mw = (int)Math.Min(768f, sw - 40f);
            int mh = 446;
            int mx = (int)((sw - mw) * 0.5f);
            int my = (int)Math.Max(66f, (sh - mh) * 0.5f);

            // Outer Desk Phone Chassis (dark gunmetal console)
            DrawCard(batch, new Rectangle(mx, my, mw, mh),
                new Color(24, 29, 38, 248), new Color(98, 122, 152, 230), 16);

            // Top handset cradle bar
            DrawPill(batch, new Rectangle(mx + 16, my + 12, mw - 32, 34), new Color(16, 20, 27, 245), 9);
            DrawPill(batch, new Rectangle(mx + 24, my + 18, 14, 22), new Color(218, 64, 58, 240), 4); // Line 1 LED
            DrawFittedLine(batch, "SIMCITY PD  911 SWITCHBOARD CONSOLE  —  LINE 1 ACTIVE",
                new SimRect(mx + 48, my + 19, mw - 204, 20), headerStyle, SimTextAlign.Left);

            modalCloseRect = new Rectangle(mx + mw - 144, my + 16, 120, 26);
            DrawButton(batch, modalCloseRect,
                hoverModalClose ? new Color(84, 96, 114, 240) : new Color(52, 62, 78, 230),
                new Color(142, 158, 182, 185), 6, "HOLD [ESC]", smallStyle);

            // Illuminated Green-Phosphor LCD Caller Readout Screen
            int lcdX = mx + 20;
            int lcdY = my + 56;
            int lcdW = mw - 40;
            int lcdH = 148;
            DrawCard(batch, new Rectangle(lcdX, lcdY, lcdW, lcdH),
                new Color(14, 34, 28, 248), new Color(72, 178, 128, 220), 10);

            // Caller ID & Countdown Row
            string callerHeader = "CALLER ID: " + mc.CallerName.ToUpperInvariant() + "   |   " + (mc.Address ?? "SimCity").ToUpperInvariant();
            DrawFittedLine(batch, callerHeader, new SimRect(lcdX + 14, lcdY + 10, lcdW - 180, 20), lcdStyle, SimTextAlign.Left);
            bool urgent = mc.RemainingUrgencySec < 6.0f;
            DrawFittedLine(batch, "HOLD TIME: " + Math.Ceiling(mc.RemainingUrgencySec).ToString("0", CultureInfo.InvariantCulture) + "s",
                new SimRect(lcdX + lcdW - 164, lcdY + 10, 150, 20), urgent ? redStyle : goldStyle, SimTextAlign.Right);

            // Urgency Timer Bar
            float frac = mc.UrgencyFraction;
            FillRect(batch, px, lcdX + 14, lcdY + 34, lcdW - 28, 7, new Color(24, 52, 42, 235));
            FillRect(batch, px, lcdX + 14, lcdY + 34, (lcdW - 28) * frac, 7,
                frac < 0.35f ? new Color(240, 82, 74) : new Color(98, 232, 146));

            // Spoken Caller Transcript inside LCD
            DrawFittedLine(batch, "\"" + mc.TranscriptLine1 + "\"",
                new SimRect(lcdX + 14, lcdY + 48, lcdW - 28, 20), bodyStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "\"" + mc.TranscriptLine2 + "\"",
                new SimRect(lcdX + 14, lcdY + 70, lcdW - 28, 20), bodyStyle, SimTextAlign.Left);

            // Logic Clue + Calm Caller Button row
            bool hasClue = game.LogicCluesUnlocked && !string.IsNullOrWhiteSpace(mc.LogicClue);
            string clueText = hasClue ? ("LINE SCAN: " + mc.LogicClue) : "LINE SCAN: Standard 911 audio feed.";
            DrawFittedLine(batch, clueText,
                new SimRect(lcdX + 14, lcdY + 104, lcdW - 238, 18),
                hasClue ? cyanStyle : lcdDimStyle, SimTextAlign.Left);

            modalCalmRect = new Rectangle(lcdX + lcdW - 216, lcdY + 98, 202, 34);
            bool canCalm = !mc.CalmedOnce && game.CalmChargesRemaining > 0;
            DrawButton(batch, modalCalmRect,
                !canCalm ? new Color(40, 52, 48, 200) : (hoverModalCalm ? new Color(44, 148, 134, 245) : new Color(30, 112, 102, 235)),
                canCalm ? new Color(118, 236, 212, 225) : new Color(86, 108, 102, 150),
                7,
                canCalm ? "CALM CALLER +6s [C]" : (mc.CalmedOnce ? "CALLER CALMED" : "NO CALM LEFT"),
                canCalm ? badgeStyle : subStyle);

            // Tactile Department Keypad Label
            int gridY = lcdY + lcdH + 14;
            DrawFittedLine(batch, "SELECT DEPARTMENT BUTTON TO TRANSFER & DISPATCH:",
                new SimRect(lcdX, gridY, lcdW, 18), subStyle, SimTextAlign.Left);
            gridY += 24;

            // 2x2 Illuminated Switchboard Department Buttons
            int gap = 14;
            int bw = (lcdW - gap) / 2;
            int bh = 88;

            DrawPhoneDeptButton(batch, 0, new Rectangle(lcdX, gridY, bw, bh),
                "[1]  PATROL DEPT", "Burglars, prowlers, theft & vandalism",
                game.AvailablePatrolUnits > 0 ? ("UNIT READY (" + game.AvailablePatrolUnits + "/" + DispatchTriageGame.MaxPatrolUnits + ")") : ("BUSY " + game.NextPatrolReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailablePatrolUnits > 0,
                new Color(32, 86, 168, 242), new Color(46, 112, 208, 250), new Color(122, 188, 252, 235));

            DrawPhoneDeptButton(batch, 1, new Rectangle(lcdX + bw + gap, gridY, bw, bh),
                "[2]  FIRE DEPT", "Smoke, kitchen fires, sparks & gas",
                game.AvailableFireUnits > 0 ? ("UNIT READY (" + game.AvailableFireUnits + "/" + DispatchTriageGame.MaxFireUnits + ")") : ("BUSY " + game.NextFireReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailableFireUnits > 0,
                new Color(168, 42, 42, 242), new Color(204, 58, 54, 250), new Color(252, 142, 134, 235));

            DrawPhoneDeptButton(batch, 2, new Rectangle(lcdX, gridY + bh + gap, bw, bh),
                "[3]  COMMUNITY AID", "Lost pets, elderly checkups & noise",
                game.AvailableCommunityUnits > 0 ? ("UNIT READY (" + game.AvailableCommunityUnits + "/" + DispatchTriageGame.MaxCommunityUnits + ")") : ("BUSY " + game.NextCommunityReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailableCommunityUnits > 0,
                new Color(24, 122, 92, 242), new Color(36, 154, 118, 250), new Color(126, 242, 198, 235));

            DrawPhoneDeptButton(batch, 3, new Rectangle(lcdX + bw + gap, gridY + bh + gap, bw, bh),
                "[4]  HANG UP / PRANK", "Prank calls & wrong numbers",
                "INSTANT DISCONNECT",
                true,
                new Color(86, 62, 126, 242), new Color(112, 82, 158, 250), new Color(192, 162, 244, 235));
        }

        void DrawPhoneDeptButton(
            UISpriteBatch batch,
            int idx,
            Rectangle rect,
            string title,
            string desc,
            string status,
            bool ready,
            Color baseBg,
            Color hoverBg,
            Color border)
        {
            deptBtnRects[idx] = rect;
            bool hov = hoverDeptIdx == idx;
            Color bg = !ready ? new Color(46, 50, 58, 225) : (hov ? hoverBg : baseBg);
            Color bdr = !ready ? new Color(102, 108, 120, 175) : border;

            DrawCard(batch, rect, bg, bdr, 10);
            DrawFittedLine(batch, title, new SimRect(rect.X + 14, rect.Y + 12, rect.Width - 28, 22), titleStyle, SimTextAlign.Left);
            DrawFittedLine(batch, desc, new SimRect(rect.X + 14, rect.Y + 38, rect.Width - 28, 18), bodyStyle, SimTextAlign.Left);

            var pill = new Rectangle(rect.X + 14, rect.Y + 60, rect.Width - 28, 20);
            DrawPill(batch, pill, ready ? new Color(14, 22, 32, 150) : new Color(96, 36, 36, 205), 5);
            DrawFittedLine(batch, status, new SimRect(pill.X + 8, pill.Y + 1, pill.Width - 16, 18),
                ready ? mintStyle : goldStyle, SimTextAlign.Center);
        }

        // ---- MODAL 2: Precinct Paper Forms Clipboard UI (Clicking a real Walk-In Sim at the counter!) ----

        void DrawPaperFormsModal(UISpriteBatch batch, Texture2D px, float sw, float sh, DispatchTriageGame game, DispatchActiveCase mc)
        {
            int mw = (int)Math.Min(792f, sw - 40f);
            int mh = 474;
            int mx = (int)((sw - mw) * 0.5f);
            int my = (int)Math.Max(62f, (sh - mh) * 0.5f);

            // Warm Walnut Precinct Clipboard Board
            DrawCard(batch, new Rectangle(mx, my, mw, mh),
                new Color(104, 70, 42, 248), new Color(168, 124, 78, 235), 16);

            // Metallic Silver/Brass Clipboard Clamp at Top Center
            int clipW = 240;
            DrawCard(batch, new Rectangle(mx + (mw - clipW) / 2, my - 8, clipW, 28),
                new Color(168, 174, 182, 250), new Color(224, 228, 234, 240), 7);
            DrawFittedLine(batch, "PRECINCT DESK FORMS",
                new SimRect(mx + (mw - clipW) / 2 + 8, my - 3, clipW - 16, 18), darkHeaderStyle, SimTextAlign.Center);

            // Close / Step Back button on top right of clipboard
            modalCloseRect = new Rectangle(mx + mw - 128, my + 12, 108, 26);
            DrawButton(batch, modalCloseRect,
                hoverModalClose ? new Color(134, 92, 58, 245) : new Color(78, 52, 32, 235),
                new Color(204, 168, 126, 205), 6, "STEP BACK", badgeStyle);

            // Top Cream-Ivory Citizen Walk-In Intake Slip
            int slipX = mx + 20;
            int slipY = my + 44;
            int slipW = mw - 40;
            int slipH = 142;
            DrawCard(batch, new Rectangle(slipX, slipY, slipW, slipH),
                new Color(246, 240, 226, 252), new Color(188, 174, 148, 235), 8);

            // Top Stamp Header Strip on Intake Slip
            FillRect(batch, px, slipX + 12, slipY + 10, slipW - 24, 22, new Color(36, 56, 86, 235));
            DrawFittedLine(batch, "WALK-IN CITIZEN INTAKE SLIP   |   " + mc.CallerName.ToUpperInvariant() + " (" + (mc.Address ?? "SimCity") + ")",
                new SimRect(slipX + 20, slipY + 12, slipW - 186, 18), badgeStyle, SimTextAlign.Left);
            bool urgent = mc.RemainingUrgencySec < 6.0f;
            DrawFittedLine(batch, "PATIENCE: " + Math.Ceiling(mc.RemainingUrgencySec).ToString("0", CultureInfo.InvariantCulture) + "s",
                new SimRect(slipX + slipW - 164, slipY + 12, 146, 18), urgent ? redStyle : goldStyle, SimTextAlign.Right);

            // Ruled Paper Lines & Citizen Statement
            FillRect(batch, px, slipX + 14, slipY + 62, slipW - 28, 1, new Color(192, 204, 222, 180));
            FillRect(batch, px, slipX + 14, slipY + 86, slipW - 28, 1, new Color(192, 204, 222, 180));
            DrawFittedLine(batch, "\"" + mc.TranscriptLine1 + "\"",
                new SimRect(slipX + 16, slipY + 40, slipW - 32, 20), darkHeaderStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "\"" + mc.TranscriptLine2 + "\"",
                new SimRect(slipX + 16, slipY + 64, slipW - 32, 20), darkBodyStyle, SimTextAlign.Left);

            // Visual Logic Inspection Note + Reassure Citizen button
            bool hasClue = game.LogicCluesUnlocked && !string.IsNullOrWhiteSpace(mc.LogicClue);
            string obs = hasClue ? ("DESK NOTE: " + mc.LogicClue) : "DESK NOTE: Citizen waiting for stamped referral slip.";
            DrawFittedLine(batch, obs, new SimRect(slipX + 16, slipY + 98, slipW - 230, 20), darkBodyStyle, SimTextAlign.Left);

            modalCalmRect = new Rectangle(slipX + slipW - 204, slipY + 94, 188, 34);
            bool canCalm = !mc.CalmedOnce && game.CalmChargesRemaining > 0;
            DrawButton(batch, modalCalmRect,
                !canCalm ? new Color(164, 160, 150, 220) : (hoverModalCalm ? new Color(36, 138, 108, 245) : new Color(28, 108, 86, 238)),
                canCalm ? new Color(20, 82, 64, 230) : new Color(132, 128, 120, 180),
                7,
                canCalm ? "REASSURE +6s [C]" : (mc.CalmedOnce ? "REASSURED" : "NO CALM LEFT"),
                badgeStyle);

            // Instruction Divider
            int gridY = slipY + slipH + 12;
            DrawFittedLine(batch, "CHOOSE & STAMP A DEPARTMENT PAPER FORM TO HAND TO " + mc.CallerName.ToUpperInvariant() + ":",
                new SimRect(slipX, gridY, slipW, 18), badgeStyle, SimTextAlign.Left);
            gridY += 22;

            // 2x2 Grid of 4 Physical Color-Coded Paper Referral Forms!
            int gap = 14;
            int fw = (slipW - gap) / 2;
            int fh = 112;

            DrawPaperFormCard(batch, px, 0, new Rectangle(slipX, gridY, fw, fh),
                "FORM 10-A  |  PATROL DIVISION",
                "Criminal Report & Officer Escort",
                "Send citizen to Patrol Intake Desk.",
                game.AvailablePatrolUnits > 0 ? "STAMP & HAND FORM [1]" : ("PATROL BUSY " + game.NextPatrolReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailablePatrolUnits > 0,
                new Color(230, 240, 252, 252), new Color(212, 230, 252, 255),
                new Color(34, 86, 166, 245), new Color(46, 114, 208, 240));

            DrawPaperFormCard(batch, px, 1, new Rectangle(slipX + fw + gap, gridY, fw, fh),
                "FORM 22-F  |  FIRE MARSHAL",
                "Fire & Utility Hazard Pass",
                "Rush citizen to Fire Dispatch Desk.",
                game.AvailableFireUnits > 0 ? "STAMP & HAND FORM [2]" : ("FIRE BUSY " + game.NextFireReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailableFireUnits > 0,
                new Color(254, 236, 234, 252), new Color(254, 222, 218, 255),
                new Color(176, 44, 44, 245), new Color(214, 62, 56, 240));

            DrawPaperFormCard(batch, px, 2, new Rectangle(slipX, gridY + fh + gap, fw, fh),
                "FORM 07-C  |  COMMUNITY AID",
                "Welfare & Pet Aid Voucher",
                "Refer citizen to Community Services.",
                game.AvailableCommunityUnits > 0 ? "STAMP & HAND FORM [3]" : ("AID BUSY " + game.NextCommunityReadySec.ToString("0.0", CultureInfo.InvariantCulture) + "s"),
                game.AvailableCommunityUnits > 0,
                new Color(230, 248, 240, 252), new Color(210, 244, 228, 255),
                new Color(26, 124, 92, 245), new Color(36, 162, 122, 240));

            DrawPaperFormCard(batch, px, 3, new Rectangle(slipX + fw + gap, gridY + fh + gap, fw, fh),
                "FORM 00-X  |  NO-ACTION SLIP",
                "Civil Matter / Dismiss Slip",
                "Stamp DENIED — non-police matter.",
                "STAMP & HAND SLIP [4]",
                true,
                new Color(242, 236, 248, 252), new Color(232, 220, 246, 255),
                new Color(98, 68, 138, 245), new Color(132, 92, 182, 240));
        }

        void DrawPaperFormCard(
            UISpriteBatch batch,
            Texture2D px,
            int idx,
            Rectangle rect,
            string formHeader,
            string formTitle,
            string formDesc,
            string stampText,
            bool ready,
            Color paperBg,
            Color paperHoverBg,
            Color headerStripBg,
            Color stampBorder)
        {
            deptBtnRects[idx] = rect;
            bool hov = hoverDeptIdx == idx;

            Color bg = !ready ? new Color(214, 210, 204, 235) : (hov ? paperHoverBg : paperBg);
            Color bdr = hov && ready ? stampBorder : new Color(162, 152, 138, 225);

            // Physical paper sheet rectangle
            DrawCard(batch, rect, bg, bdr, 6);

            // Colored department header strip printed across the top of the paper form
            FillRect(batch, px, rect.X + 4, rect.Y + 4, rect.Width - 8, 22,
                ready ? headerStripBg : new Color(112, 110, 108, 225));
            DrawFittedLine(batch, formHeader, new SimRect(rect.X + 10, rect.Y + 6, rect.Width - 20, 18), badgeStyle, SimTextAlign.Left);

            // Printed ruled lines on the paper form
            FillRect(batch, px, rect.X + 12, rect.Y + 50, rect.Width - 24, 1, new Color(180, 186, 198, 150));
            FillRect(batch, px, rect.X + 12, rect.Y + 72, rect.Width - 24, 1, new Color(180, 186, 198, 150));

            DrawFittedLine(batch, formTitle, new SimRect(rect.X + 12, rect.Y + 30, rect.Width - 24, 19), darkHeaderStyle, SimTextAlign.Left);
            DrawFittedLine(batch, formDesc, new SimRect(rect.X + 12, rect.Y + 53, rect.Width - 24, 18), darkSmallStyle, SimTextAlign.Left);

            // Official Rubber-Stamp Action Pill at bottom of paper sheet
            var stampRect = new Rectangle(rect.X + 12, rect.Y + 79, rect.Width - 24, 25);
            DrawButton(batch, stampRect,
                ready ? (hov ? stampBorder : headerStripBg) : new Color(138, 92, 86, 220),
                ready ? stampBorder : new Color(166, 120, 114, 200),
                5, stampText, badgeStyle);
        }

        // ---- Shift Scorecard (S / A / B / C) ----

        void DrawScorecard(UISpriteBatch batch, Texture2D px, float sw, float sh, DispatchTriageGame game, DispatchTriageOutcome o)
        {
            FillRect(batch, px, 0, 0, sw, sh, new Color(8, 14, 24, 148));

            int cw = (int)Math.Min(568f, sw - 44f);
            int ch = 368;
            int cx = (int)((sw - cw) * 0.5f);
            int cy = (int)((sh - ch) * 0.5f);

            DrawCard(batch, new Rectangle(cx, cy, cw, ch),
                new Color(14, 24, 38, 244), new Color(92, 168, 236, 225), 16);

            int x = cx + 22;
            int innerW = cw - 44;
            int y = cy + 18;

            DrawPill(batch, new Rectangle(x, y, innerW, 24), new Color(34, 96, 174, 235), 6);
            DrawFittedLine(batch, "SIMCITY PD  |  DESK SERGEANT SHIFT REPORT",
                new SimRect(x + 10, y + 3, innerW - 20, 18), badgeStyle, SimTextAlign.Center);
            y += 34;

            // Grade badge + Headline
            Color gBg = o.Grade == "S" ? new Color(212, 162, 34, 245)
                      : o.Grade == "A" ? new Color(36, 152, 108, 245)
                      : o.Grade == "B" ? new Color(52, 118, 198, 245)
                      : new Color(142, 74, 68, 240);
            DrawCard(batch, new Rectangle(x, y, 76, 76), gBg, new Color(255, 255, 255, 195), 14);
            DrawFittedLine(batch, o.Grade, new SimRect(x, y + 14, 76, 48), gradeStyle, SimTextAlign.Center);

            string headline = o.Grade == "S" ? "PRECINCT COMMENDATION!"
                            : o.Grade == "A" ? "SHARP DISPATCH SHIFT"
                            : o.Grade == "B" ? "SOLID FRONT DESK WORK"
                            : "SHIFT COMPLETE";
            int correctCount = o.PerfectCount + o.GoodCount;
            DrawFittedLine(batch, headline, new SimRect(x + 92, y + 8, innerW - 92, 24), titleStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Total Score: " + o.Score + "   |   Correct: " + correctCount + "/" + o.TotalCases,
                new SimRect(x + 92, y + 36, innerW - 92, 18), cyanStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Perfect: " + o.PerfectCount + "   |   Walk-Ins: " + o.WalkInsHandled + "   |   Pranks: " + o.PranksBlocked,
                new SimRect(x + 92, y + 56, innerW - 92, 18), subStyle, SimTextAlign.Left);
            y += 92;

            FillRect(batch, px, x, y, innerW, 1, new Color(90, 140, 200, 100));
            y += 14;

            DrawFittedLine(batch, "REAL VM REWARDS APPLIED ON RETURN HOME:", new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 24;

            var r = o.Reward ?? new CareerDispatchReward();
            string rewardsLine1 = "+$" + r.BonusSimoleons + " Simoleons   |   Job Performance +" + r.JobPerformanceDelta;
            string rewardsLine2 = "Logic XP +" + r.LogicXpDelta + "   |   Charisma XP +" + r.CharismaXpDelta +
                                  (o.RelationshipDeltas != null && o.RelationshipDeltas.Count > 0
                                      ? ("   |   Citizens Helped: " + o.RelationshipDeltas.Count)
                                      : "");
            DrawFittedLine(batch, rewardsLine1, new SimRect(x, y, innerW, 20), mintStyle, SimTextAlign.Left);
            y += 24;
            DrawFittedLine(batch, rewardsLine2, new SimRect(x, y, innerW, 20), bodyStyle, SimTextAlign.Left);
            y += 28;

            DrawFittedLine(batch, o.Summary ?? "", new SimRect(x, y, innerW, 20), subStyle, SimTextAlign.Left);
            y += 32;

            claimBtnRect = new Rectangle(x, y, innerW, 42);
            DrawButton(batch, claimBtnRect,
                hoverClaim ? new Color(42, 174, 126, 250) : new Color(28, 142, 102, 242),
                new Color(138, 246, 202, 235), 9,
                "CLAIM REWARDS & RETURN HOME [ENTER]", badgeStyle);
        }

        // ---- Small Helpers ----

        void DrawButton(UISpriteBatch batch, Rectangle r, Color bg, Color border, int radius, string text, TextStyle style)
        {
            DrawCard(batch, r, bg, border, radius);
            DrawFittedLine(batch, text, new SimRect(r.X + 8, r.Y + (r.Height - 20) / 2, r.Width - 16, 20), style, SimTextAlign.Center);
        }

        void DrawCard(UISpriteBatch batch, Rectangle r, Color fill, Color border, int radius)
        {
            var sr = ToSimRect(r);
            FillRoundedRect(batch, sr, radius, fill);
            DrawRoundedBorder(batch, sr, radius, border, 1);
        }

        void DrawPill(UISpriteBatch batch, Rectangle r, Color fill, int radius)
        {
            FillRoundedRect(batch, ToSimRect(r), radius, fill);
        }

        static SimRect ToSimRect(Rectangle r)
        {
            return new SimRect(r.X, r.Y, r.Width, r.Height);
        }
    }
}
