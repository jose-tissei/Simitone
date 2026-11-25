using System;
using System.Globalization;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Real-Lot UI Overlay for Law Enforcement Level 5 ("Vice Squad"):
    // Undercover Sting (DISP-08).
    // Renders cleanly over the live 100%-real-TS1-game-assets Speakeasy Lounge (works in 3D & 2D):
    //   - Compact left Briefing Dossier leaving the VIP Table, Slotted Lamp, Officer, Suspect & Bar unobstructed
    //   - Slim Top Vice HUD Bar with live Suspicion & Confession gauges + Wiretap charges
    //   - Pulsing clickable world markers directly over the real Suspect & real Informant/Bar
    //   - Lower-band Wiretap Dialogue Console (no full-screen wall — Sims stay visible above!)
    //   - Dramatic Bust banner during climax + S/A/B/C Vice Squad Sting Report Scorecard
    public sealed class UndercoverStingOverlay : SimUiElement
    {
        static UndercoverStingOverlay instance;
        static bool isOpen;

        readonly TextStyle titleStyle;
        readonly TextStyle headerStyle;
        readonly TextStyle subStyle;
        readonly TextStyle bodyStyle;
        readonly TextStyle smallStyle;
        readonly TextStyle badgeStyle;
        readonly TextStyle cyanStyle;
        readonly TextStyle mintStyle;
        readonly TextStyle goldStyle;
        readonly TextStyle redStyle;
        readonly TextStyle gradeStyle;

        Rectangle startBtnRect;
        Rectangle skipBtnRect;
        Rectangle endShiftBtnRect;
        Rectangle claimBtnRect;

        Rectangle suspectMarkerRect;
        Rectangle wireMarkerRect;
        Rectangle bustBannerBtnRect;

        Rectangle modalCloseRect;
        Rectangle modalWireRect;
        readonly Rectangle[] toneBtnRects = new Rectangle[3];

        bool hoverStart, hoverSkip, hoverEnd, hoverClaim;
        bool hoverSuspectMarker, hoverWireMarker, hoverBustBanner;
        bool hoverModalClose, hoverModalWire;
        int hoverToneIdx = -1;

        bool prevLeftDown, prevEnterDown, prevSpaceDown, prevEscDown;
        bool prevD1, prevD2, prevD3, prevKeyW, prevKeyB;
        float uiPulseSec;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        UndercoverStingOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(17, SimUiTokens.DialogTitle, true);
            headerStyle = SimUiTokens.MakeStyle(14, SimUiTokens.DialogTitle, true);
            subStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextMuted, true);
            bodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            smallStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextSubtle, true);
            badgeStyle = SimUiTokens.MakeStyle(11, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Mint, true);
            goldStyle = SimUiTokens.MakeStyle(12, new Color(248, 208, 102), true);
            redStyle = SimUiTokens.MakeStyle(12, new Color(248, 114, 114), true);
            gradeStyle = SimUiTokens.MakeStyle(36, Color.White, true);
        }

        public static void OpenForRealLot()
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null) instance = new UndercoverStingOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                isOpen = true;
                instance.prevLeftDown = true;
                instance.prevEnterDown = true;
                instance.prevSpaceDown = true;
                instance.prevEscDown = true;
            }
            catch (Exception e) { Boot.Log("undercover-sting overlay open error: " + e.Message); }
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
            try { UndercoverStingDriver.PreDrawFrame(); }
            catch (Exception e) { Boot.Log("undercover-sting PreDraw error: " + e.Message); }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen) return;

            uiPulseSec += 1f / 60f;
            UndercoverStingDriver.TickAlertSweep();

            var game = UndercoverStingDriver.Game;
            if (game == null || !UndercoverStingDriver.IsLoungeLotLive)
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
            bool keyW = kb.IsKeyDown(Keys.W);
            bool keyB = kb.IsKeyDown(Keys.B);

            bool enterPressed = enterDown && !prevEnterDown;
            bool spacePressed = spaceDown && !prevSpaceDown;
            bool escPressed = escDown && !prevEscDown;
            bool d1Pressed = d1 && !prevD1;
            bool d2Pressed = d2 && !prevD2;
            bool d3Pressed = d3 && !prevD3;
            bool wPressed = keyW && !prevKeyW;
            bool bPressed = keyB && !prevKeyB;

            prevEnterDown = enterDown;
            prevSpaceDown = spaceDown;
            prevEscDown = escDown;
            prevD1 = d1;
            prevD2 = d2;
            prevD3 = d3;
            prevKeyW = keyW;
            prevKeyB = keyB;

            hoverStart = hoverSkip = hoverEnd = hoverClaim = false;
            hoverSuspectMarker = hoverWireMarker = hoverBustBanner = false;
            hoverModalClose = hoverModalWire = false;
            hoverToneIdx = -1;

            if (game.IsBriefing)
            {
                hoverStart = startBtnRect.Contains(mx, my);
                hoverSkip = skipBtnRect.Contains(mx, my);
                if (enterPressed || spacePressed || (justClicked && hoverStart))
                    UndercoverStingDriver.HandleTestAction("start");
                else if (escPressed || (justClicked && hoverSkip))
                    UndercoverStingDriver.RequestReturnHome(false);
                return;
            }

            if (UndercoverStingDriver.ShowScorecardNow)
            {
                hoverClaim = claimBtnRect.Contains(mx, my);
                if (enterPressed || spacePressed || escPressed || (justClicked && hoverClaim))
                    UndercoverStingDriver.RequestReturnHome(true);
                return;
            }

            if (game.IsFinished)
            {
                // Climax animation playing; allow skipping straight to scorecard on Enter/Click
                if (enterPressed || spacePressed)
                    UndercoverStingDriver.HandleTestAction("scorecard");
                return;
            }

            if (UndercoverStingDriver.ActiveModal == StingModalMode.WiretapDialogue)
            {
                hoverModalClose = modalCloseRect.Contains(mx, my);
                hoverModalWire = !modalWireRect.IsEmpty && modalWireRect.Contains(mx, my);
                for (int i = 0; i < 3; i++)
                    if (toneBtnRects[i].Contains(mx, my)) hoverToneIdx = i;

                if (escPressed || (justClicked && hoverModalClose))
                {
                    UndercoverStingDriver.CloseActiveModal();
                    return;
                }
                if (wPressed || (justClicked && hoverModalWire))
                {
                    UndercoverStingDriver.TriggerWireCheck();
                    return;
                }
                if (d1Pressed || (justClicked && hoverToneIdx == 0))
                {
                    UndercoverStingDriver.ChooseDialogueTone(StingTone.Gentle);
                    return;
                }
                if (d2Pressed || (justClicked && hoverToneIdx == 1))
                {
                    UndercoverStingDriver.ChooseDialogueTone(StingTone.Playful);
                    return;
                }
                if (d3Pressed || (justClicked && hoverToneIdx == 2))
                {
                    UndercoverStingDriver.ChooseDialogueTone(StingTone.Bold);
                    return;
                }
                return;
            }

            // Live Speakeasy Lounge (no modal open): click world markers, hotkeys, or native Pie Menu!
            hoverEnd = endShiftBtnRect.Contains(mx, my);
            hoverSuspectMarker = !suspectMarkerRect.IsEmpty && suspectMarkerRect.Contains(mx, my);
            hoverWireMarker = !wireMarkerRect.IsEmpty && wireMarkerRect.Contains(mx, my);
            hoverBustBanner = !bustBannerBtnRect.IsEmpty && bustBannerBtnRect.Contains(mx, my);

            if (justClicked && hoverEnd)
            {
                UndercoverStingDriver.RequestReturnHome(false);
                return;
            }
            if ((bPressed || (justClicked && hoverBustBanner)) && game.CanFlashBadgeBust)
            {
                UndercoverStingDriver.TriggerFlashBadgeBust();
                return;
            }
            if (justClicked && hoverSuspectMarker)
            {
                if (game.CanFlashBadgeBust)
                    UndercoverStingDriver.TriggerFlashBadgeBust();
                else
                    UndercoverStingDriver.OpenWiretapModal();
                return;
            }
            if ((wPressed || (justClicked && hoverWireMarker)) && game.WireChargesRemaining > 0)
            {
                UndercoverStingDriver.TriggerWireCheck();
                return;
            }
            if (enterPressed || spacePressed)
            {
                if (game.CanFlashBadgeBust)
                    UndercoverStingDriver.TriggerFlashBadgeBust();
                else
                    UndercoverStingDriver.OpenWiretapModal();
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen) return;
            var game = UndercoverStingDriver.Game;
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
                suspectMarkerRect = Rectangle.Empty;
                wireMarkerRect = Rectangle.Empty;
                bustBannerBtnRect = Rectangle.Empty;
                modalCloseRect = Rectangle.Empty;
                modalWireRect = Rectangle.Empty;
                for (int i = 0; i < 3; i++) toneBtnRects[i] = Rectangle.Empty;

                if (game.IsBriefing)
                {
                    DrawBriefingCard(batch, px, sw, sh, game);
                    return;
                }

                if (UndercoverStingDriver.ShowScorecardNow)
                {
                    DrawTopViceHudBar(batch, px, sw, game);
                    DrawScorecard(batch, px, sw, sh, game, UndercoverStingDriver.FinalOutcome);
                    return;
                }

                // Always show slim Top Vice HUD Bar
                DrawTopViceHudBar(batch, px, sw, game);

                if (game.IsFinished)
                {
                    // Dramatic Bust / Cover Blown Climax Banner at bottom — keep whole lounge unobstructed!
                    DrawClimaxBanner(batch, px, sw, sh, game);
                    return;
                }

                // Compact left Evidence & Temperament Dossier
                DrawLeftEvidenceStrip(batch, px, game);

                if (UndercoverStingDriver.ActiveModal == StingModalMode.WiretapDialogue)
                {
                    DrawWiretapModal(batch, px, sw, sh, game);
                }
                else
                {
                    DrawWorldActionMarkers(batch, px, sw, sh, game);
                    if (game.CanFlashBadgeBust)
                        DrawReadyToBustCallout(batch, px, sw, sh, game);
                }

                // Latest feedback ticker pill (only when modal is closed)
                if (UndercoverStingDriver.ActiveModal == StingModalMode.None &&
                    !game.CanFlashBadgeBust &&
                    !string.IsNullOrWhiteSpace(game.LastStatusBanner))
                {
                    int bw = (int)Math.Min(680f, sw - 48f);
                    int bx = (int)((sw - bw) * 0.5f);
                    int by = (int)(sh - 50f);
                    bool risky = game.Suspicion >= 65;
                    Color bg = !risky
                        ? new Color(18, 66, 56, 234)
                        : new Color(108, 30, 38, 234);
                    Color bdr = !risky
                        ? new Color(98, 228, 182, 225)
                        : new Color(246, 120, 120, 225);
                    DrawCard(batch, new Rectangle(bx, by, bw, 34), bg, bdr, 10);
                    DrawFittedLine(batch, game.LastStatusBanner, new SimRect(bx + 14, by + 7, bw - 28, 20), badgeStyle, SimTextAlign.Center);
                }
            }
            catch (Exception e)
            {
                Boot.Log("undercover-sting draw error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---- Briefing Card (compact left dossier so real TS1 Speakeasy stays wide open!) ----

        void DrawBriefingCard(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game)
        {
            int cw = (int)Math.Min(468f, sw - 48f);
            int ch = 356;
            int cx = 24;
            int cy = (int)Math.Max(24f, Math.Min(sh - ch - 24f, (sh - ch) * 0.42f));

            DrawCard(batch, new Rectangle(cx, cy, cw, ch),
                new Color(16, 18, 30, 238), new Color(214, 162, 74, 215), 14);

            int x = cx + 18;
            int innerW = cw - 36;
            int y = cy + 16;

            DrawPill(batch, new Rectangle(x, y, innerW, 22), new Color(134, 42, 68, 232), 6);
            DrawFittedLine(batch, "LAW ENFORCEMENT  |  LVL 5 VICE SQUAD",
                new SimRect(x + 10, y + 2, innerW - 20, 18), badgeStyle, SimTextAlign.Left);
            y += 30;

            DrawFittedLine(batch, "UNDERCOVER STING: " + game.CaseProfile.CaseTitle.ToUpperInvariant(),
                new SimRect(x, y, innerW, 22), titleStyle, SimTextAlign.Left);
            y += 24;

            DrawFittedLine(batch, "Agent " + game.OfficerName + " (Plainclothes)  |  Cha " + game.CharismaSkill + "  |  Log " + game.LogicSkill,
                new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 22;

            DrawFittedLine(batch, "Suspect: " + game.SuspectName + "   |   Bar Informant: " + game.InformantName,
                new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 24;

            FillRect(batch, px, x, y, innerW, 1f, new Color(214, 162, 74, 95));
            y += 12;

            DrawFittedLine(batch, "- Underground Speakeasy Lounge (3D & 2D).", new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, "- CLICK " + game.SuspectName.ToUpperInvariant() + " across the table to work the wire.", new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, "- Push CONFESSION >= 75% without hitting 100% SUSPICION!", new SimRect(x, y, innerW, 18), mintStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, "- CLICK " + game.InformantName.ToUpperInvariant() + " / BAR [W] to Check Wire (-18% Susp).", new SimRect(x, y, innerW, 18), subStyle, SimTextAlign.Left);
            y += 21;
            string perkLine = game.LogicProfileUnlocked
                ? ("- LOGIC PERK: Suspect Profile -> " + game.CaseProfile.TemperamentLabel)
                : "- Reach Logic 3+ to reveal Suspect Temperament & [RISK] tells.";
            DrawFittedLine(batch, perkLine, new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 28;

            int btnW = (innerW - 12) * 2 / 3;
            int skipW = innerW - 12 - btnW;
            startBtnRect = new Rectangle(x, y, btnW, 38);
            skipBtnRect = new Rectangle(x + btnW + 12, y, skipW, 38);

            DrawButton(batch, startBtnRect,
                hoverStart ? new Color(198, 134, 42, 248) : new Color(162, 106, 30, 240),
                new Color(252, 214, 126, 235), 8, "START STING [ENTER]", badgeStyle);
            DrawButton(batch, skipBtnRect,
                hoverSkip ? new Color(68, 76, 98, 238) : new Color(44, 50, 66, 225),
                new Color(138, 152, 182, 180), 8, "SKIP [ESC]", bodyStyle);
        }

        // ---- Slim Top Vice HUD Bar ----

        void DrawTopViceHudBar(UISpriteBatch batch, Texture2D px, float sw, UndercoverStingGame game)
        {
            int barW = (int)Math.Min(1080f, sw - 28f);
            int barX = (int)((sw - barW) * 0.5f);
            int barY = 12;
            int barH = 48;

            DrawCard(batch, new Rectangle(barX, barY, barW, barH),
                new Color(16, 18, 30, 232), new Color(206, 154, 72, 200), 11);

            int x = barX + 14;
            int cy = barY + 12;

            string title = "VICE SQUAD  |  STING OP";
            float titleW = Math.Min(212f, headerStyle.MeasureString(title).X + 12f);
            DrawFittedLine(batch, title, new SimRect(x, barY + 14, titleW, 20), headerStyle, SimTextAlign.Left);
            x += (int)titleW + 8;

            // Turn pill
            var turnPill = new Rectangle(x, cy, 88, 24);
            DrawPill(batch, turnPill, new Color(86, 44, 98, 232), 6);
            string turnText = game.IsFinished ? "CLOSED" : ("TURN " + Math.Min(UndercoverStingGame.MaxTurns, game.CurrentTurnIndex + 1) + "/3");
            DrawFittedLine(batch, turnText, new SimRect(turnPill.X + 4, turnPill.Y + 3, turnPill.Width - 8, 18), badgeStyle, SimTextAlign.Center);
            x += 96;

            // Suspicion Meter (0..100%)
            int gaugeW = 196;
            DrawHudGauge(batch, px, x, barY + 7, gaugeW,
                "SUSPICION " + game.Suspicion + "%",
                game.Suspicion / 100f,
                game.Suspicion >= 75 ? new Color(238, 66, 66) : (game.Suspicion >= 45 ? new Color(242, 166, 52) : new Color(214, 186, 78)),
                game.Suspicion >= 75 ? redStyle : goldStyle,
                thresholdTick: -1f);
            x += gaugeW + 14;

            // Confession Meter (0..100%, with 75% Bust tick!)
            DrawHudGauge(batch, px, x, barY + 7, gaugeW,
                "CONFESSION " + game.Confession + "%" + (game.CanFlashBadgeBust ? " [BUST!]" : ""),
                game.Confession / 100f,
                game.Confession >= UndercoverStingGame.BustConfessionThreshold ? new Color(56, 224, 152) : new Color(64, 182, 232),
                game.Confession >= UndercoverStingGame.BustConfessionThreshold ? mintStyle : cyanStyle,
                thresholdTick: UndercoverStingGame.BustConfessionThreshold / 100f);
            x += gaugeW + 14;

            // Wire Charges pill
            var wirePill = new Rectangle(x, cy, 114, 24);
            bool hasWire = game.WireChargesRemaining > 0;
            DrawPill(batch, wirePill, hasWire ? new Color(28, 108, 118, 232) : new Color(54, 58, 70, 210), 6);
            DrawFittedLine(batch, "WIRE " + game.WireChargesRemaining + "/" + game.MaxWireCharges + " [W]",
                new SimRect(wirePill.X + 4, wirePill.Y + 3, wirePill.Width - 8, 18),
                hasWire ? badgeStyle : subStyle, SimTextAlign.Center);

            // End button on right
            endShiftBtnRect = new Rectangle(barX + barW - 92, barY + 10, 80, 28);
            DrawButton(batch, endShiftBtnRect,
                hoverEnd ? new Color(142, 46, 54, 240) : new Color(86, 36, 44, 225),
                new Color(228, 128, 134, 195), 7, "END", badgeStyle);
        }

        void DrawHudGauge(UISpriteBatch batch, Texture2D px, int x, int y, int w, string label, float frac, Color fill, TextStyle labelStyle, float thresholdTick)
        {
            DrawFittedLine(batch, label, new SimRect(x, y, w, 17), labelStyle, SimTextAlign.Left);
            int by = y + 21;
            int bh = 11;
            FillRect(batch, px, x, by, w, bh, new Color(34, 38, 54, 235));
            FillRect(batch, px, x, by, w * Math.Max(0f, Math.Min(1f, frac)), bh, fill);
            if (thresholdTick > 0f && thresholdTick < 1f)
                FillRect(batch, px, x + w * thresholdTick - 1f, by - 2f, 2f, bh + 4f, new Color(255, 255, 255, 225));
        }

        // ---- Compact Left Evidence & Temperament Strip ----

        void DrawLeftEvidenceStrip(UISpriteBatch batch, Texture2D px, UndercoverStingGame game)
        {
            int x = 18;
            int y = 72;
            int w = 276;
            int clues = game.RecordedSlips.Count;
            int h = 92 + clues * 22 + (game.LogicProfileUnlocked ? 24 : 0);

            DrawCard(batch, new Rectangle(x, y, w, h),
                new Color(16, 20, 34, 224), new Color(118, 146, 198, 180), 11);

            int iy = y + 10;
            DrawPill(batch, new Rectangle(x + 10, iy, w - 20, 20), new Color(46, 62, 104, 225), 5);
            DrawFittedLine(batch, "CASE: " + game.CaseProfile.CaseTitle.ToUpperInvariant(),
                new SimRect(x + 16, iy + 1, w - 32, 18), badgeStyle, SimTextAlign.Left);
            iy += 26;

            DrawFittedLine(batch, "Target: " + game.SuspectName, new SimRect(x + 12, iy, w - 24, 18), goldStyle, SimTextAlign.Left);
            iy += 20;

            if (game.LogicProfileUnlocked)
            {
                DrawFittedLine(batch, "Profile: " + game.CaseProfile.TemperamentLabel,
                    new SimRect(x + 12, iy, w - 24, 18), cyanStyle, SimTextAlign.Left);
                iy += 22;
            }

            FillRect(batch, px, x + 12, iy, w - 24, 1, new Color(102, 128, 168, 100));
            iy += 8;

            DrawFittedLine(batch, clues > 0 ? "WIRETAP EVIDENCE RECORDED:" : "WIRETAP: Awaiting slip-up...",
                new SimRect(x + 12, iy, w - 24, 18), clues > 0 ? mintStyle : subStyle, SimTextAlign.Left);
            iy += 20;

            for (int i = 0; i < clues; i++)
            {
                DrawFittedLine(batch, "+ " + game.RecordedSlips[i],
                    new SimRect(x + 14, iy, w - 28, 18), bodyStyle, SimTextAlign.Left);
                iy += 22;
            }
        }

        // ---- Clickable Pulsing World Markers over Real Suspect & Real Bar Informant ----

        void DrawWorldActionMarkers(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiPulseSec * 6.8f);
            float bob = (float)Math.Sin(uiPulseSec * 4.8f) * 4f;

            // 1. Pulsing Marker above the REAL Neighborhood Suspect across the VIP Table!
            if (UndercoverStingDriver.SuspectAvatar != null)
            {
                var pt = UndercoverStingDriver.GetEntityScreenPos(UndercoverStingDriver.SuspectAvatar, -90f);
                if (pt.HasValue)
                {
                    bool canBust = game.CanFlashBadgeBust;
                    int mw = canBust ? 286 : 254;
                    int mh = 36;
                    int mx = (int)Math.Max(20f, Math.Min(sw - mw - 20f, pt.Value.X - mw * 0.5f));
                    int my = (int)Math.Max(72f, Math.Min(sh - mh - 76f, pt.Value.Y - mh + bob));
                    suspectMarkerRect = new Rectangle(mx, my, mw, mh);

                    Color bg = canBust
                        ? (hoverSuspectMarker ? new Color(224, 52, 62, 250) : Color.Lerp(new Color(176, 30, 46, 240), new Color(218, 74, 42, 248), pulse))
                        : (hoverSuspectMarker ? new Color(204, 142, 46, 248) : Color.Lerp(new Color(148, 96, 28, 236), new Color(188, 128, 40, 245), pulse));
                    Color border = canBust ? new Color(255, 224, 124, 250) : new Color(252, 218, 138, 242);

                    DrawCard(batch, suspectMarkerRect, bg, border, 10);
                    string label = canBust
                        ? ("FLASH BADGE & BUST " + game.SuspectName.ToUpperInvariant() + "!")
                        : ("CLICK " + game.SuspectName.ToUpperInvariant() + " (TURN " + (game.CurrentTurnIndex + 1) + "/3)");
                    DrawFittedLine(batch, label, new SimRect(mx + 10, my + 8, mw - 20, 20), badgeStyle, SimTextAlign.Center);
                    FillRect(batch, px, mx + mw / 2 - 4, my + mh, 8, 6, border);
                }
            }

            // 2. Pulsing Marker above the REAL Bar Informant (Check Wire / Sip Drink)
            var wireTarget = (FSO.SimAntics.VMEntity)UndercoverStingDriver.InformantAvatar ?? UndercoverStingDriver.BarCounterEntity;
            if (game.WireChargesRemaining > 0 && wireTarget != null)
            {
                var pt = UndercoverStingDriver.GetEntityScreenPos(wireTarget, -86f);
                if (pt.HasValue)
                {
                    int mw = 236;
                    int mh = 34;
                    int mx = (int)Math.Max(20f, Math.Min(sw - mw - 20f, pt.Value.X - mw * 0.5f));
                    int my = (int)Math.Max(72f, Math.Min(sh - mh - 76f, pt.Value.Y - mh - bob));
                    if (!suspectMarkerRect.IsEmpty && new Rectangle(mx, my, mw, mh).Intersects(suspectMarkerRect))
                        my = Math.Max(72, suspectMarkerRect.Top - mh - 10);
                    wireMarkerRect = new Rectangle(mx, my, mw, mh);

                    Color bg = hoverWireMarker
                        ? new Color(34, 152, 162, 248)
                        : Color.Lerp(new Color(22, 102, 114, 232), new Color(32, 134, 146, 242), pulse);
                    Color border = new Color(138, 238, 242, 240);
                    DrawCard(batch, wireMarkerRect, bg, border, 10);
                    DrawFittedLine(batch, "CHECK WIRE AT BAR [W]",
                        new SimRect(mx + 10, my + 7, mw - 20, 20), badgeStyle, SimTextAlign.Center);
                    FillRect(batch, px, mx + mw / 2 - 4, my + mh, 8, 6, border);
                }
            }
        }

        void DrawReadyToBustCallout(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiPulseSec * 8.0f);
            int bw = (int)Math.Min(560f, sw - 48f);
            int bh = 54;
            int bx = (int)((sw - bw) * 0.5f);
            int by = (int)(sh - bh - 20f);
            bustBannerBtnRect = new Rectangle(bx, by, bw, bh);

            Color bg = hoverBustBanner
                ? new Color(216, 46, 56, 250)
                : Color.Lerp(new Color(162, 28, 42, 242), new Color(206, 62, 46, 248), pulse);
            Color bdr = new Color(255, 224, 128, 250);
            DrawCard(batch, bustBannerBtnRect, bg, bdr, 12);
            DrawFittedLine(batch, "CONFESSION ON TAPE (" + game.Confession + "%)  —  CLICK TO FLASH BADGE & BUST [B]",
                new SimRect(bx + 14, by + 16, bw - 28, 22), titleStyle, SimTextAlign.Center);
        }

        // ---- Lower-Band Wiretap Dialogue Console (Real TS1 Lounge & Sims stay visible above!) ----

        void DrawWiretapModal(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game)
        {
            var turn = game.CurrentTurn;
            if (turn == null) return;

            int mw = (int)Math.Min(1020f, sw - 36f);
            int mh = 282;
            int mx = (int)((sw - mw) * 0.5f);
            int my = (int)Math.Max(70f, sh - mh - 18f);

            DrawCard(batch, new Rectangle(mx, my, mw, mh),
                new Color(14, 18, 30, 246), new Color(218, 168, 76, 232), 14);

            // Header bar inside lower wiretap console
            DrawPill(batch, new Rectangle(mx + 14, my + 12, mw - 28, 30), new Color(24, 30, 48, 242), 8);
            DrawPill(batch, new Rectangle(mx + 22, my + 18, 12, 18), new Color(234, 62, 62, 245), 4);
            DrawFittedLine(batch, "WIRETAP LIVE  |  " + game.SuspectName.ToUpperInvariant() + "  —  TURN " + (game.CurrentTurnIndex + 1) + "/3 (" + turn.StageTitle.ToUpperInvariant() + ")",
                new SimRect(mx + 42, my + 17, mw - 360, 20), headerStyle, SimTextAlign.Left);

            // Check Wire button inside modal header
            bool canWire = game.WireChargesRemaining > 0;
            modalWireRect = new Rectangle(mx + mw - 308, my + 14, 188, 26);
            DrawButton(batch, modalWireRect,
                !canWire ? new Color(46, 52, 64, 210) : (hoverModalWire ? new Color(36, 150, 162, 248) : new Color(24, 114, 126, 238)),
                canWire ? new Color(138, 238, 244, 225) : new Color(96, 104, 118, 165),
                6,
                canWire ? "CHECK WIRE -18% [W]" : "NO WIRE LEFT",
                canWire ? badgeStyle : subStyle);

            modalCloseRect = new Rectangle(mx + mw - 112, my + 14, 92, 26);
            DrawButton(batch, modalCloseRect,
                hoverModalClose ? new Color(84, 92, 112, 242) : new Color(52, 58, 74, 232),
                new Color(146, 158, 186, 185), 6, "STEP BACK", smallStyle);

            // Suspect's current spoken prompt + optional Wiretap Tell
            int py = my + 50;
            DrawFittedLine(batch, game.SuspectName + ": \"" + turn.SuspectPrompt + "\"",
                new SimRect(mx + 18, py, mw - 36, 22), goldStyle, SimTextAlign.Left);
            py += 26;

            string intel = game.WireTellRevealedThisTurn
                ? ("WIRETAP TELL: " + turn.WiretapTell)
                : (game.LogicProfileUnlocked
                    ? ("PROFILE (" + game.CaseProfile.TemperamentLabel + "): Watch [RISK] tags — stay cool.")
                    : "Choose your undercover angle — keep Suspicion below 100% and build Confession!");
            DrawFittedLine(batch, intel, new SimRect(mx + 18, py, mw - 36, 18),
                game.WireTellRevealedThisTurn ? mintStyle : cyanStyle, SimTextAlign.Left);
            py += 26;

            // 3 Side-by-Side Tactile Dialogue Cards: [1] GENTLE | [2] PLAYFUL | [3] BOLD
            int gap = 12;
            int cw = (mw - 36 - gap * 2) / 3;
            int ch = mh - (py - my) - 16;

            for (int i = 0; i < 3 && i < turn.Options.Length; i++)
            {
                var opt = turn.Options[i];
                var rect = new Rectangle(mx + 18 + i * (cw + gap), py, cw, ch);
                toneBtnRects[i] = rect;
                bool hov = hoverToneIdx == i;

                Color baseBg = opt.Tone == StingTone.Gentle
                    ? new Color(24, 72, 92, 240)
                    : (opt.Tone == StingTone.Playful ? new Color(68, 48, 108, 240) : new Color(118, 48, 42, 240));
                Color hovBg = opt.Tone == StingTone.Gentle
                    ? new Color(34, 98, 124, 248)
                    : (opt.Tone == StingTone.Playful ? new Color(92, 66, 144, 248) : new Color(152, 64, 54, 248));
                Color bdr = opt.Tone == StingTone.Gentle
                    ? new Color(112, 214, 242, 232)
                    : (opt.Tone == StingTone.Playful ? new Color(196, 158, 250, 232) : new Color(252, 166, 118, 232));

                DrawCard(batch, rect, hov ? hovBg : baseBg, bdr, 10);

                string toneTag = "[" + (i + 1) + "]  " + opt.Tone.ToString().ToUpperInvariant();
                if (game.LogicProfileUnlocked && opt.IsRiskyTrap)
                    toneTag += "  [RISK]";
                else if (game.WireTellRevealedThisTurn && opt.IsBestForTurn)
                    toneTag += "  [BEST]";

                DrawFittedLine(batch, toneTag,
                    new SimRect(rect.X + 12, rect.Y + 10, rect.Width - 24, 20),
                    (game.LogicProfileUnlocked && opt.IsRiskyTrap) ? redStyle : titleStyle, SimTextAlign.Left);

                DrawFittedLine(batch, opt.Label,
                    new SimRect(rect.X + 12, rect.Y + 34, rect.Width - 24, 18), goldStyle, SimTextAlign.Left);

                // Split dialogue line neatly across two lines if long
                string line = "\"" + opt.OfficerLine + "\"";
                string l1 = line;
                string l2 = "";
                if (line.Length > 42)
                {
                    int split = line.LastIndexOf(' ', Math.Min(42, line.Length - 1));
                    if (split > 12)
                    {
                        l1 = line.Substring(0, split);
                        l2 = line.Substring(split + 1);
                    }
                }
                DrawFittedLine(batch, l1, new SimRect(rect.X + 12, rect.Y + 60, rect.Width - 24, 18), bodyStyle, SimTextAlign.Left);
                if (!string.IsNullOrEmpty(l2))
                    DrawFittedLine(batch, l2, new SimRect(rect.X + 12, rect.Y + 80, rect.Width - 24, 18), bodyStyle, SimTextAlign.Left);

                var footer = new Rectangle(rect.X + 12, rect.Bottom - 30, rect.Width - 24, 20);
                DrawPill(batch, footer, new Color(12, 16, 26, 165), 5);
                string hint = opt.Tone == StingTone.Gentle ? "Low Suspicion  |  Steady Trust"
                            : (opt.Tone == StingTone.Playful ? "Charm Angle  |  Draws Bragging"
                            : "High Pressure  |  Big Confession");
                DrawFittedLine(batch, hint, new SimRect(footer.X + 6, footer.Y + 1, footer.Width - 12, 18), subStyle, SimTextAlign.Center);
            }
        }

        // ---- Dramatic Climax Banner (Officer flashing badge + Suspect hands up!) ----

        void DrawClimaxBanner(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game)
        {
            bool busted = game.Ending == StingEndingKind.SuspectBusted;
            int bw = (int)Math.Min(680f, sw - 48f);
            int bh = 58;
            int bx = (int)((sw - bw) * 0.5f);
            int by = (int)(sh - bh - 22f);

            Color bg = busted ? new Color(18, 74, 58, 244) : new Color(118, 26, 36, 244);
            Color bdr = busted ? new Color(118, 244, 192, 245) : new Color(252, 134, 134, 245);
            DrawCard(batch, new Rectangle(bx, by, bw, bh), bg, bdr, 12);

            string headline = busted
                ? ("SUSPECT BUSTED!  —  " + game.SuspectName.ToUpperInvariant() + " CAUGHT ON WIRETAP!")
                : ("COVER BLOWN!  —  " + game.SuspectName.ToUpperInvariant() + " MADE YOU & BOLTED!");
            DrawFittedLine(batch, headline, new SimRect(bx + 16, by + 10, bw - 32, 22), titleStyle, SimTextAlign.Center);
            DrawFittedLine(batch, "Filing Vice Squad Sting Report... [Press ENTER to skip]",
                new SimRect(bx + 16, by + 34, bw - 32, 18), subStyle, SimTextAlign.Center);
        }

        // ---- Vice Squad Sting Report Scorecard ----

        void DrawScorecard(UISpriteBatch batch, Texture2D px, float sw, float sh, UndercoverStingGame game, UndercoverStingOutcome o)
        {
            int cw = (int)Math.Min(568f, sw - 44f);
            int ch = 432;
            int cx = (int)((sw - cw) * 0.5f);
            int cy = (int)Math.Max(70f, (sh - ch) * 0.52f);

            Color borderCol = o.Grade == "S" ? new Color(248, 206, 94, 240)
                            : o.Grade == "A" ? new Color(96, 232, 178, 235)
                            : o.Grade == "B" ? new Color(108, 184, 248, 230)
                            : new Color(236, 114, 114, 225);
            DrawCard(batch, new Rectangle(cx, cy, cw, ch), new Color(14, 18, 30, 246), borderCol, 16);

            int x = cx + 22;
            int innerW = cw - 44;
            int y = cy + 18;

            DrawPill(batch, new Rectangle(x, y, innerW, 24), new Color(128, 42, 68, 235), 6);
            DrawFittedLine(batch, "VICE SQUAD  |  UNDERCOVER STING REPORT",
                new SimRect(x + 10, y + 3, innerW - 20, 18), badgeStyle, SimTextAlign.Left);
            y += 34;

            // Grade badge + Headline
            var gradeBox = new Rectangle(x, y, 74, 74);
            Color gradeBg = o.Grade == "S" ? new Color(188, 134, 28, 245)
                          : o.Grade == "A" ? new Color(28, 138, 98, 245)
                          : o.Grade == "B" ? new Color(36, 102, 176, 245)
                          : new Color(148, 46, 52, 245);
            DrawCard(batch, gradeBox, gradeBg, borderCol, 12);
            DrawFittedLine(batch, o.Grade, new SimRect(gradeBox.X, gradeBox.Y + 14, gradeBox.Width, 46), gradeStyle, SimTextAlign.Center);

            string statusTitle = o.Ending == StingEndingKind.SuspectBusted ? "STING SUCCESSFUL — SUSPECT ARRESTED!" : "COVER BLOWN — OPERATION ABORTED";
            DrawFittedLine(batch, statusTitle, new SimRect(x + 88, y + 4, innerW - 88, 22),
                o.Ending == StingEndingKind.SuspectBusted ? mintStyle : redStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Case: " + o.CaseTitle + "  |  Target: " + o.SuspectName,
                new SimRect(x + 88, y + 28, innerW - 88, 18), goldStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Confession: " + o.FinalConfession + "%   |   Suspicion: " + o.FinalSuspicion + "%   |   Score: " + o.Score,
                new SimRect(x + 88, y + 50, innerW - 88, 18), bodyStyle, SimTextAlign.Left);
            y += 88;

            FillRect(batch, px, x, y, innerW, 1, new Color(118, 140, 178, 110));
            y += 12;

            // Captured evidence summary
            DrawFittedLine(batch, "EVIDENCE LOGGED (" + game.RecordedSlips.Count + "):", new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 22;
            if (game.RecordedSlips.Count == 0)
            {
                DrawFittedLine(batch, "  - No incriminating slips recorded before cover broke.", new SimRect(x, y, innerW, 18), subStyle, SimTextAlign.Left);
                y += 22;
            }
            else
            {
                for (int i = 0; i < Math.Min(3, game.RecordedSlips.Count); i++)
                {
                    DrawFittedLine(batch, "  + " + game.RecordedSlips[i], new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
                    y += 20;
                }
            }

            y += 8;
            FillRect(batch, px, x, y, innerW, 1, new Color(118, 140, 178, 110));
            y += 12;

            // Real Simitone VM Rewards Box
            DrawFittedLine(batch, "REWARDS APPLIED TO " + game.OfficerName.ToUpperInvariant() + " ON RETURN HOME:",
                new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 24;

            string rew1 = "+$" + o.BonusSimoleons + " Simoleons   |   Job Performance +" + o.JobPerformanceDelta;
            string rew2 = "Charisma +" + o.CharismaXpDelta + " XP   |   Logic +" + o.LogicXpDelta + " XP";
            string rew3 = o.SuspectName + " Rel " + (o.SuspectDailyRelDelta >= 0 ? "+" : "") + o.SuspectDailyRelDelta +
                          "   |   " + o.InformantName + " Rel +" + o.InformantDailyRelDelta;

            DrawFittedLine(batch, rew1, new SimRect(x + 8, y, innerW - 16, 18), mintStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, rew2, new SimRect(x + 8, y, innerW - 16, 18), cyanStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, rew3, new SimRect(x + 8, y, innerW - 16, 18), bodyStyle, SimTextAlign.Left);

            claimBtnRect = new Rectangle(x, cy + ch - 56, innerW, 40);
            DrawButton(batch, claimBtnRect,
                hoverClaim ? new Color(42, 168, 122, 248) : new Color(28, 136, 98, 240),
                new Color(138, 244, 198, 238), 9, "COLLECT REWARDS & RETURN HOME [ENTER]", badgeStyle);
        }

        // ---- Small Helpers (Matching DispatchTriageOverlay cleanly!) ----

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
