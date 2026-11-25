using System;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Real-Lot UI Overlay for Law Enforcement Level 6 ("Detective"): Crime Scene Deduction.
    // Renders cleanly over the live 100%-real-TS1-game-assets Manor Parlor Crime Scene (works in 3D & 2D):
    //   - Compact left Case Briefing Dossier leaving the Manor Parlor, 3 Clue Stations & 3 Suspects clear
    //   - Slim Top Detective HUD Bar (Clues 0/3, Questioned 0/3, Score, [E] Case Board, [X] End Shift)
    //   - Pulsing clickable world markers directly above all 3 real Clue objects & 3 real Suspects
    //     (automatically surfacing Logic >= 2 [!] Contradiction badges and [CLEARED] Lab tags!)
    //   - Lower-Band Evidence Board & Interrogation Console (all 3 Suspects + Detective stay visible above!)
    //   - Forensic camera flash pulse, Dramatic Accusation Banner, and S/A/B/C Detective Case Scorecard!
    public sealed class CrimeSceneDeductionOverlay : SimUiElement
    {
        static CrimeSceneDeductionOverlay instance;
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
        Rectangle boardBtnRect;
        Rectangle endShiftBtnRect;
        Rectangle claimBtnRect;

        readonly Rectangle[] clueMarkerRects = new Rectangle[3];
        readonly Rectangle[] suspectMarkerRects = new Rectangle[3];

        Rectangle boardCloseRect;
        Rectangle boardForensicsRect;
        readonly Rectangle[] boardQuestionRects = new Rectangle[3];
        readonly Rectangle[] boardAccuseRects = new Rectangle[3];

        bool hoverStart, hoverSkip, hoverBoardBtn, hoverEnd, hoverClaim;
        bool hoverBoardClose, hoverBoardForensics;
        int hoverClueMarker = -1;
        int hoverSuspectMarker = -1;
        int hoverBoardQuestion = -1;
        int hoverBoardAccuse = -1;

        bool prevLeftDown, prevEnterDown, prevSpaceDown, prevEscDown;
        bool prevKeyE, prevKeyF, prevD1, prevD2, prevD3;
        float uiPulseSec;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        CrimeSceneDeductionOverlay()
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
                if (instance == null) instance = new CrimeSceneDeductionOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                isOpen = true;
                instance.prevLeftDown = true;
                instance.prevEnterDown = true;
                instance.prevSpaceDown = true;
                instance.prevEscDown = true;
            }
            catch (Exception e) { Boot.Log("crime-scene overlay open error: " + e.Message); }
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
            try { CrimeSceneDeductionDriver.PreDrawFrame(); }
            catch (Exception e) { Boot.Log("crime-scene PreDraw error: " + e.Message); }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen) return;

            uiPulseSec += 1f / 60f;
            CrimeSceneDeductionDriver.TickAlertSweep();

            var game = CrimeSceneDeductionDriver.Game;
            if (game == null) return;

            var ms = state.MouseState;
            var ks = state.KeyboardState;
            int mx = ms.X;
            int my = ms.Y;

            bool leftDown = ms.LeftButton == ButtonState.Pressed;
            bool justClicked = leftDown && !prevLeftDown;
            prevLeftDown = leftDown;

            bool enterDown = ks.IsKeyDown(Keys.Enter);
            bool spaceDown = ks.IsKeyDown(Keys.Space);
            bool escDown = ks.IsKeyDown(Keys.Escape);
            bool eDown = ks.IsKeyDown(Keys.E);
            bool fDown = ks.IsKeyDown(Keys.F);
            bool d1Down = ks.IsKeyDown(Keys.D1);
            bool d2Down = ks.IsKeyDown(Keys.D2);
            bool d3Down = ks.IsKeyDown(Keys.D3);

            bool enterPressed = enterDown && !prevEnterDown;
            bool spacePressed = spaceDown && !prevSpaceDown;
            bool escPressed = escDown && !prevEscDown;
            bool ePressed = eDown && !prevKeyE;
            bool fPressed = fDown && !prevKeyF;
            bool d1Pressed = d1Down && !prevD1;
            bool d2Pressed = d2Down && !prevD2;
            bool d3Pressed = d3Down && !prevD3;

            prevEnterDown = enterDown;
            prevSpaceDown = spaceDown;
            prevEscDown = escDown;
            prevKeyE = eDown;
            prevKeyF = fDown;
            prevD1 = d1Down;
            prevD2 = d2Down;
            prevD3 = d3Down;

            hoverStart = hoverSkip = hoverBoardBtn = hoverEnd = hoverClaim = false;
            hoverBoardClose = hoverBoardForensics = false;
            hoverClueMarker = -1;
            hoverSuspectMarker = -1;
            hoverBoardQuestion = -1;
            hoverBoardAccuse = -1;

            if (game.Phase == DetectivePhase.Briefing)
            {
                hoverStart = startBtnRect.Contains(mx, my);
                hoverSkip = skipBtnRect.Contains(mx, my);
                if (enterPressed || spacePressed || (justClicked && hoverStart))
                    CrimeSceneDeductionDriver.BeginInvestigation();
                else if (escPressed || (justClicked && hoverSkip))
                    CrimeSceneDeductionDriver.RequestReturnHome(false);
                return;
            }

            if (CrimeSceneDeductionDriver.ShowScorecardNow)
            {
                hoverClaim = claimBtnRect.Contains(mx, my);
                if (enterPressed || spacePressed || escPressed || (justClicked && hoverClaim))
                    CrimeSceneDeductionDriver.RequestReturnHome(true);
                return;
            }

            if (game.Phase == DetectivePhase.AccusationClimax)
            {
                if (enterPressed || spacePressed)
                    CrimeSceneDeductionDriver.HandleTestAction("scorecard");
                return;
            }

            hoverBoardBtn = boardBtnRect.Contains(mx, my);
            hoverEnd = endShiftBtnRect.Contains(mx, my);

            if (justClicked && hoverEnd)
            {
                CrimeSceneDeductionDriver.RequestReturnHome(false);
                return;
            }
            if (ePressed || (justClicked && hoverBoardBtn))
            {
                CrimeSceneDeductionDriver.ToggleCaseBoard(null);
                return;
            }

            if (CrimeSceneDeductionDriver.ActiveModal == DetectiveModalMode.CaseBoard)
            {
                hoverBoardClose = boardCloseRect.Contains(mx, my);
                hoverBoardForensics = !boardForensicsRect.IsEmpty && boardForensicsRect.Contains(mx, my);
                for (int i = 0; i < 3; i++)
                {
                    if (boardQuestionRects[i].Contains(mx, my)) hoverBoardQuestion = i;
                    if (boardAccuseRects[i].Contains(mx, my)) hoverBoardAccuse = i;
                }

                if (escPressed || (justClicked && hoverBoardClose))
                {
                    CrimeSceneDeductionDriver.ToggleCaseBoard(false);
                    return;
                }
                if ((fPressed || (justClicked && hoverBoardForensics)) && game.ForensicsChargesLeft > 0)
                {
                    CrimeSceneDeductionDriver.TriggerForensicsCrossCheck();
                    return;
                }
                if (justClicked && hoverBoardQuestion >= 0)
                {
                    CrimeSceneDeductionDriver.TriggerQuestionSuspect(hoverBoardQuestion, openBoardAfter: true);
                    return;
                }
                if (justClicked && hoverBoardAccuse >= 0)
                {
                    CrimeSceneDeductionDriver.TriggerAccuseSuspect(hoverBoardAccuse);
                    return;
                }
                if (d1Pressed) { CrimeSceneDeductionDriver.TriggerAccuseSuspect(0); return; }
                if (d2Pressed) { CrimeSceneDeductionDriver.TriggerAccuseSuspect(1); return; }
                if (d3Pressed) { CrimeSceneDeductionDriver.TriggerAccuseSuspect(2); return; }
                return;
            }

            // Live Manor Parlor (Evidence Board closed): click pulsing Clue or Suspect markers!
            for (int i = 0; i < 3; i++)
            {
                if (!clueMarkerRects[i].IsEmpty && clueMarkerRects[i].Contains(mx, my))
                    hoverClueMarker = i;
                if (!suspectMarkerRects[i].IsEmpty && suspectMarkerRects[i].Contains(mx, my))
                    hoverSuspectMarker = i;
            }

            if (justClicked && hoverClueMarker >= 0)
            {
                CrimeSceneDeductionDriver.TriggerInspectClue(hoverClueMarker);
                return;
            }
            if (justClicked && hoverSuspectMarker >= 0)
            {
                CrimeSceneDeductionDriver.TriggerQuestionSuspect(hoverSuspectMarker, openBoardAfter: true);
                return;
            }
            if (fPressed && game.ForensicsChargesLeft > 0)
            {
                CrimeSceneDeductionDriver.TriggerForensicsCrossCheck();
                return;
            }
            if (d1Pressed) { CrimeSceneDeductionDriver.TriggerInspectClue(0); return; }
            if (d2Pressed) { CrimeSceneDeductionDriver.TriggerInspectClue(1); return; }
            if (d3Pressed) { CrimeSceneDeductionDriver.TriggerInspectClue(2); return; }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen) return;
            var game = CrimeSceneDeductionDriver.Game;
            if (game == null) return;

            try
            {
                var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
                float sw, sh;
                GetScreenSize(batch, out sw, out sh);

                startBtnRect = Rectangle.Empty;
                skipBtnRect = Rectangle.Empty;
                boardBtnRect = Rectangle.Empty;
                endShiftBtnRect = Rectangle.Empty;
                claimBtnRect = Rectangle.Empty;
                boardCloseRect = Rectangle.Empty;
                boardForensicsRect = Rectangle.Empty;
                for (int i = 0; i < 3; i++)
                {
                    clueMarkerRects[i] = Rectangle.Empty;
                    suspectMarkerRects[i] = Rectangle.Empty;
                    boardQuestionRects[i] = Rectangle.Empty;
                    boardAccuseRects[i] = Rectangle.Empty;
                }

                // Subtle forensic camera-flash pulse
                float flash = CrimeSceneDeductionDriver.CameraFlashAlpha;
                if (flash > 0.01f)
                    FillRect(batch, px, 0, 0, sw, sh, new Color(255, 252, 236, (int)(flash * 145f)));

                if (game.Phase == DetectivePhase.Briefing)
                {
                    DrawBriefingCard(batch, px, sw, sh, game);
                    return;
                }

                DrawTopDetectiveHudBar(batch, px, sw, game);

                if (CrimeSceneDeductionDriver.ShowScorecardNow)
                {
                    DrawScorecard(batch, px, sw, sh, game, CrimeSceneDeductionDriver.FinalOutcome);
                    return;
                }

                if (game.Phase == DetectivePhase.AccusationClimax)
                {
                    DrawClimaxBanner(batch, px, sw, sh, game);
                    return;
                }

                if (CrimeSceneDeductionDriver.ActiveModal == DetectiveModalMode.CaseBoard)
                {
                    DrawEvidenceBoardConsole(batch, px, sw, sh, game);
                }
                else
                {
                    DrawWorldMarkers(batch, px, sw, sh, game);
                    DrawCompactClueStrip(batch, px, game);
                    DrawBottomFeedbackBar(batch, px, sw, sh, game);
                }
            }
            catch (Exception e)
            {
                Boot.Log("crime-scene draw error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---- Compact Left Briefing Card (Manor Parlor stays wide open!) ----

        void DrawBriefingCard(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game)
        {
            int cw = (int)Math.Min(462f, sw - 48f);
            int ch = 362;
            int cx = 24;
            int cy = (int)Math.Max(24f, Math.Min(sh - ch - 24f, (sh - ch) * 0.42f));

            DrawCard(batch, new Rectangle(cx, cy, cw, ch),
                new Color(14, 20, 32, 240), new Color(230, 178, 76, 220), 14);

            int x = cx + 18;
            int innerW = cw - 36;
            int y = cy + 16;

            DrawPill(batch, new Rectangle(x, y, innerW, 22), new Color(36, 76, 134, 236), 6);
            DrawFittedLine(batch, "LAW ENFORCEMENT  |  LVL 6 DETECTIVE",
                new SimRect(x + 10, y + 2, innerW - 20, 18), badgeStyle, SimTextAlign.Left);
            y += 30;

            DrawFittedLine(batch, game.CaseTitle.ToUpperInvariant(),
                new SimRect(x, y, innerW, 22), titleStyle, SimTextAlign.Left);
            y += 24;

            DrawFittedLine(batch, "Det. " + game.DetectiveName + "  |  Logic " + game.LogicSkill + "  |  Charisma " + game.CharismaSkill + "  |  " + game.VenueName,
                new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 22;

            string suspectList = game.Suspects[0].Name + ", " + game.Suspects[1].Name + ", " + game.Suspects[2].Name;
            DrawFittedLine(batch, "Parlor Suspects: " + suspectList,
                new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 24;

            FillRect(batch, px, x, y, innerW, 1f, new Color(230, 178, 76, 95));
            y += 12;

            DrawFittedLine(batch, "1. Inspect 3 real crime scene stations (Footprint, Motive, Alibi).",
                new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, "2. Click any parlor Suspect to question & log their testimony.",
                new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, game.LogicFlagsContradictions
                ? "3. Logic >= 2 ACTIVE: contradictions auto-highlighted in red!"
                : "3. Compare clues on the [E] Case Board and accuse the culprit!",
                new SimRect(x, y, innerW, 18), game.LogicFlagsContradictions ? mintStyle : bodyStyle, SimTextAlign.Left);
            y += 21;
            DrawFittedLine(batch, game.HasForensicsCrossCheck
                ? "4. Logic >= 5 BONUS: [F] Forensics Cross-Check clears 1 innocent!"
                : "4. Tip: Train Logic >= 5 to unlock Forensics Lab Cross-Check.",
                new SimRect(x, y, innerW, 18), game.HasForensicsCrossCheck ? goldStyle : smallStyle, SimTextAlign.Left);
            y += 30;

            int btnW = (innerW - 12) / 2;
            startBtnRect = new Rectangle(x, y, btnW + 24, 38);
            skipBtnRect = new Rectangle(x + btnW + 34, y, btnW - 22, 38);

            DrawActionBtn(batch, startBtnRect, "BEGIN INVESTIGATION", hoverStart,
                new Color(38, 126, 92, 245), new Color(118, 242, 184, 245));
            DrawActionBtn(batch, skipBtnRect, "SKIP", hoverSkip,
                new Color(52, 58, 76, 232), new Color(150, 162, 188, 195));
        }

        // ---- Slim Top Detective HUD Bar ----

        void DrawTopDetectiveHudBar(UISpriteBatch batch, Texture2D px, float sw, CrimeSceneDeductionGame game)
        {
            int bw = (int)Math.Min(1100f, sw - 28f);
            int bx = (int)((sw - bw) * 0.5f);
            int by = 12;
            int bh = 44;

            DrawCard(batch, new Rectangle(bx, by, bw, bh),
                new Color(14, 20, 32, 236), new Color(230, 178, 76, 200), 10);

            int x = bx + 12;
            DrawPill(batch, new Rectangle(x, by + 10, 134, 24), new Color(36, 76, 134, 240), 6);
            DrawFittedLine(batch, "DETECTIVE L6", new SimRect(x, by + 12, 134, 20), badgeStyle, SimTextAlign.Center);
            x += 144;

            DrawFittedLine(batch, game.CaseTitle, new SimRect(x, by + 12, 276, 20), headerStyle, SimTextAlign.Left);
            x += 284;

            Color clueBg = game.CluesFoundCount == 3 ? new Color(26, 108, 76, 235) : new Color(34, 48, 72, 235);
            DrawPill(batch, new Rectangle(x, by + 9, 104, 26), clueBg, 6);
            DrawFittedLine(batch, "Clues " + game.CluesFoundCount + "/3", new SimRect(x, by + 12, 104, 20), badgeStyle, SimTextAlign.Center);
            x += 112;

            Color qBg = game.SuspectsQuestionedCount >= 2 ? new Color(26, 108, 76, 235) : new Color(34, 48, 72, 235);
            DrawPill(batch, new Rectangle(x, by + 9, 114, 26), qBg, 6);
            DrawFittedLine(batch, "Asked " + game.SuspectsQuestionedCount + "/3", new SimRect(x, by + 12, 114, 20), badgeStyle, SimTextAlign.Center);
            x += 122;

            DrawFittedLine(batch, "Score: " + game.Score, new SimRect(x, by + 12, 104, 20), goldStyle, SimTextAlign.Left);

            // Right-side Case Board & End Shift buttons
            endShiftBtnRect = new Rectangle(bx + bw - 94, by + 8, 84, 28);
            boardBtnRect = new Rectangle(bx + bw - 262, by + 8, 160, 28);

            bool boardOpen = CrimeSceneDeductionDriver.ActiveModal == DetectiveModalMode.CaseBoard;
            DrawActionBtn(batch, boardBtnRect, boardOpen ? "[E] HIDE BOARD" : "[E] CASE BOARD", hoverBoardBtn,
                boardOpen ? new Color(168, 122, 36, 245) : new Color(40, 88, 146, 240),
                new Color(246, 212, 120, 235));
            DrawActionBtn(batch, endShiftBtnRect, "LEAVE", hoverEnd,
                new Color(98, 38, 46, 232), new Color(236, 124, 130, 210));
        }

        // ---- Compact Left Clue Summary Strip (when Case Board modal is closed) ----

        void DrawCompactClueStrip(UISpriteBatch batch, Texture2D px, CrimeSceneDeductionGame game)
        {
            int cw = 376;
            int ch = 154;
            int cx = 16;
            int cy = 68;

            DrawCard(batch, new Rectangle(cx, cy, cw, ch),
                new Color(14, 20, 32, 224), new Color(110, 152, 214, 175), 10);

            int x = cx + 12;
            int y = cy + 10;
            DrawFittedLine(batch, "CASE NOTEBOOK", new SimRect(x, y, cw - 24, 18), goldStyle, SimTextAlign.Left);
            y += 22;

            for (int i = 0; i < 3; i++)
            {
                var c = game.Clues[i];
                string prefix = "[" + (i + 1) + "] " + c.Category.ToString().ToUpperInvariant() + ": ";
                string detail = c.Discovered ? c.ShortTag : "Uninspected";
                DrawFittedLine(batch, prefix + detail, new SimRect(x, y, cw - 24, 18),
                    c.Discovered ? mintStyle : subStyle, SimTextAlign.Left);
                y += 22;
            }

            y += 4;
            FillRect(batch, px, x, y, cw - 24, 1f, new Color(120, 150, 200, 75));
            y += 7;
            string logicLine = game.LogicFlagsContradictions
                ? "Logic " + game.LogicSkill + ": Flags contradictions"
                : "Logic " + game.LogicSkill + ": Compare manually";
            DrawFittedLine(batch, logicLine, new SimRect(x, y, cw - 24, 18), cyanStyle, SimTextAlign.Left);
            y += 19;
            if (game.HasForensicsCrossCheck)
            {
                DrawFittedLine(batch, game.ForensicsChargesLeft > 0
                    ? "[F] Forensics Cross-Check READY"
                    : "Forensics Cross-Check used",
                    new SimRect(x, y, cw - 24, 18),
                    game.ForensicsChargesLeft > 0 ? goldStyle : subStyle, SimTextAlign.Left);
            }
        }

        // ---- Pulsing Clickable World Markers Over Real Clues & Real Suspects ----

        void DrawWorldMarkers(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game)
        {
            float pulse = 0.5f + 0.5f * (float)Math.Sin(uiPulseSec * 4.5f);
            bool boardOpen = CrimeSceneDeductionDriver.ActiveModal == DetectiveModalMode.CaseBoard;
            float maxY = boardOpen ? sh - 285f : sh - 80f;

            // 3 Real Clue Station Markers
            for (int i = 0; i < 3; i++)
            {
                var ent = CrimeSceneDeductionDriver.ClueEntities[i];
                var pt = CrimeSceneDeductionDriver.GetEntityScreenPos(ent, yOffsetPx: -54f);
                if (!pt.HasValue) continue;
                if (pt.Value.X < 60f || pt.Value.X > sw - 60f || pt.Value.Y < 64f || pt.Value.Y > maxY) continue;

                var c = game.Clues[i];
                string label = c.Discovered
                    ? "[OK] " + c.Category.ToString().ToUpperInvariant()
                    : "[" + (i + 1) + "] " + c.Category.ToString().ToUpperInvariant();
                int mw = 148;
                int mh = 26;
                var rect = new Rectangle((int)(pt.Value.X - mw * 0.5f), (int)(pt.Value.Y - mh - 4f * pulse), mw, mh);
                clueMarkerRects[i] = rect;

                bool hov = hoverClueMarker == i;
                Color bg = c.Discovered
                    ? new Color(24, 96, 68, 224)
                    : (hov ? new Color(210, 148, 38, 245) : new Color(156, 102, 24, 232));
                Color bdr = c.Discovered
                    ? new Color(112, 236, 178, 220)
                    : new Color(255, 224, 128, (int)(195 + 60 * pulse));

                DrawCard(batch, rect, bg, bdr, 8);
                DrawFittedLine(batch, label, new SimRect(rect.X + 6, rect.Y + 4, rect.Width - 12, 18), badgeStyle, SimTextAlign.Center);
            }

            // 3 Real Neighborhood Suspect Markers
            for (int i = 0; i < 3; i++)
            {
                var av = CrimeSceneDeductionDriver.SuspectAvatars[i];
                var pt = CrimeSceneDeductionDriver.GetEntityScreenPos(av, yOffsetPx: -94f);
                if (!pt.HasValue) continue;
                if (pt.Value.X < 60f || pt.Value.X > sw - 60f || pt.Value.Y < 64f || pt.Value.Y > maxY) continue;

                var s = game.Suspects[i];
                int lies = game.LogicFlagsContradictions ? game.GetVisibleContradictions(i) : 0;
                string tag;
                Color bg, bdr;

                if (s.EliminatedByForensics)
                {
                    tag = s.Name + " [CLEARED]";
                    bg = new Color(34, 72, 60, 220);
                    bdr = new Color(118, 224, 172, 210);
                }
                else if (lies > 0)
                {
                    tag = s.Name + " [!" + lies + " LIE" + (lies == 1 ? "" : "S") + "]";
                    bg = hoverSuspectMarker == i ? new Color(188, 44, 52, 246) : new Color(142, 34, 42, 236);
                    bdr = new Color(255, 144, 144, (int)(200 + 55 * pulse));
                }
                else if (s.Questioned)
                {
                    tag = s.Name + " [ASKED]";
                    bg = hoverSuspectMarker == i ? new Color(56, 98, 158, 244) : new Color(34, 62, 108, 230);
                    bdr = new Color(138, 198, 255, 220);
                }
                else
                {
                    tag = "ASK " + s.Name.ToUpperInvariant();
                    bg = hoverSuspectMarker == i ? new Color(62, 118, 186, 246) : new Color(32, 68, 122, 234);
                    bdr = new Color(142, 214, 255, (int)(185 + 65 * pulse));
                }

                int mw = 204;
                int mh = 28;
                var rect = new Rectangle((int)(pt.Value.X - mw * 0.5f), (int)(pt.Value.Y - mh - 3f * pulse), mw, mh);
                suspectMarkerRects[i] = rect;

                DrawCard(batch, rect, bg, bdr, 8);
                DrawFittedLine(batch, tag, new SimRect(rect.X + 6, rect.Y + 5, rect.Width - 12, 18), badgeStyle, SimTextAlign.Center);
            }
        }

        // ---- Lower-Band Case Evidence Board & Interrogation Console (Sims unobstructed above!) ----

        void DrawEvidenceBoardConsole(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game)
        {
            int bw = (int)Math.Min(1260f, sw - 24f);
            int bh = 328;
            int bx = (int)((sw - bw) * 0.5f);
            int by = (int)(sh - bh - 12f);

            DrawCard(batch, new Rectangle(bx, by, bw, bh),
                new Color(14, 19, 30, 246), new Color(230, 178, 76, 225), 14);

            int pad = 14;
            int y = by + 10;

            // Header bar with Forensics button & Close button
            DrawFittedLine(batch, "CASE EVIDENCE BOARD  —  " + game.CaseTitle.ToUpperInvariant(),
                new SimRect(bx + pad, y + 2, 620, 20), titleStyle, SimTextAlign.Left);

            boardCloseRect = new Rectangle(bx + bw - pad - 84, y, 84, 24);
            DrawActionBtn(batch, boardCloseRect, "CLOSE", hoverBoardClose,
                new Color(62, 68, 86, 236), new Color(165, 178, 204, 200));

            if (game.HasForensicsCrossCheck)
            {
                boardForensicsRect = new Rectangle(bx + bw - pad - 344, y, 250, 24);
                bool avail = game.ForensicsChargesLeft > 0;
                DrawActionBtn(batch, boardForensicsRect,
                    avail ? "[F] FORENSICS CROSS-CHECK (1)" : "FORENSICS LAB: USED",
                    hoverBoardForensics && avail,
                    avail ? new Color(36, 114, 134, 244) : new Color(46, 52, 66, 210),
                    avail ? new Color(118, 232, 255, 235) : new Color(120, 128, 146, 140));
            }
            y += 30;

            // Row 1: 3 Forensic Clue Chips
            int chipGap = 10;
            int chipW = (bw - pad * 2 - chipGap * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                var c = game.Clues[i];
                int cx = bx + pad + i * (chipW + chipGap);
                var r = new Rectangle(cx, y, chipW, 38);
                DrawCard(batch, r,
                    c.Discovered ? new Color(22, 54, 48, 235) : new Color(28, 34, 48, 225),
                    c.Discovered ? new Color(96, 224, 170, 200) : new Color(102, 116, 142, 145), 8);
                DrawFittedLine(batch, c.Category.ToString().ToUpperInvariant() + " CLUE (" + c.StationLabel + ")",
                    new SimRect(cx + 8, y + 3, chipW - 16, 15), c.Discovered ? mintStyle : subStyle, SimTextAlign.Left);
                DrawFittedLine(batch, c.Discovered ? c.ShortTag : "Not inspected yet — click station in room",
                    new SimRect(cx + 8, y + 19, chipW - 16, 16), c.Discovered ? bodyStyle : smallStyle, SimTextAlign.Left);
            }
            y += 46;

            // Row 2: 3 Suspect Comparison Cards side-by-side (2-line wrapped claims — zero ellipsis!)
            int cardH = bh - (y - by) - 12;
            for (int i = 0; i < 3; i++)
            {
                var s = game.Suspects[i];
                int cx = bx + pad + i * (chipW + chipGap);
                int lies = game.LogicFlagsContradictions ? game.GetVisibleContradictions(i) : 0;

                Color cardBg = s.EliminatedByForensics
                    ? new Color(22, 42, 38, 224)
                    : (lies > 0 ? new Color(56, 22, 30, 240) : new Color(22, 30, 46, 236));
                Color cardBdr = s.EliminatedByForensics
                    ? new Color(98, 214, 162, 195)
                    : (lies > 0 ? new Color(248, 114, 114, 225) : new Color(108, 142, 196, 180));

                DrawCard(batch, new Rectangle(cx, y, chipW, cardH), cardBg, cardBdr, 10);

                int ix = cx + 10;
                int iw = chipW - 20;
                int iy = y + 8;

                // Suspect name + role + status tag
                DrawFittedLine(batch, (i + 1) + ". " + s.Name + " — " + s.RoleHint,
                    new SimRect(ix, iy, iw - 112, 18), headerStyle, SimTextAlign.Left);

                if (s.EliminatedByForensics)
                {
                    DrawPill(batch, new Rectangle(cx + chipW - 92, iy - 1, 82, 19), new Color(34, 118, 84, 240), 5);
                    DrawFittedLine(batch, "CLEARED", new SimRect(cx + chipW - 92, iy + 1, 82, 16), badgeStyle, SimTextAlign.Center);
                }
                else if (lies > 0)
                {
                    DrawPill(batch, new Rectangle(cx + chipW - 114, iy - 1, 104, 19), new Color(188, 42, 52, 245), 5);
                    DrawFittedLine(batch, lies + " CONTRADICT!", new SimRect(cx + chipW - 114, iy + 1, 104, 16), badgeStyle, SimTextAlign.Center);
                }
                iy += 24;

                if (!s.Questioned)
                {
                    DrawFittedLine(batch, "Not questioned yet. Click QUESTION to", new SimRect(ix, iy + 12, iw, 17), subStyle, SimTextAlign.Left);
                    DrawFittedLine(batch, "record their Footprint, Motive & Alibi claims.", new SimRect(ix, iy + 32, iw, 17), smallStyle, SimTextAlign.Left);
                }
                else
                {
                    bool fpLie = game.LogicFlagsContradictions && game.Clues[0].Discovered && s.ContradictsFootprint;
                    bool motLie = game.LogicFlagsContradictions && game.Clues[1].Discovered && s.ContradictsMotive;
                    bool aliLie = game.LogicFlagsContradictions && game.Clues[2].Discovered && s.ContradictsAlibi;

                    iy += DrawWrappedClaim(batch, (fpLie ? "[!] " : "- ") + "Shoes: " + s.FootprintClaim,
                        ix, iy, iw, fpLie ? redStyle : smallStyle);
                    iy += 4;
                    iy += DrawWrappedClaim(batch, (motLie ? "[!] " : "- ") + "Motive: " + s.MotiveClaim,
                        ix, iy, iw, motLie ? redStyle : smallStyle);
                    iy += 4;
                    DrawWrappedClaim(batch, (aliLie ? "[!] " : "- ") + "Alibi: " + s.AlibiClaim,
                        ix, iy, iw, aliLie ? redStyle : smallStyle);
                }

                // Bottom action buttons per suspect
                int btnY = y + cardH - 34;
                int halfW = (iw - 8) / 2;
                boardQuestionRects[i] = new Rectangle(ix, btnY, halfW, 26);
                boardAccuseRects[i] = s.EliminatedByForensics
                    ? Rectangle.Empty
                    : new Rectangle(ix + halfW + 8, btnY, halfW, 26);

                DrawActionBtn(batch, boardQuestionRects[i], s.Questioned ? "RE-ASK" : "QUESTION",
                    hoverBoardQuestion == i,
                    new Color(38, 86, 144, 240), new Color(132, 202, 255, 225));

                if (!s.EliminatedByForensics)
                {
                    DrawActionBtn(batch, boardAccuseRects[i], "[" + (i + 1) + "] ACCUSE!",
                        hoverBoardAccuse == i,
                        lies >= 2 ? new Color(192, 46, 56, 248) : new Color(142, 44, 52, 236),
                        new Color(255, 148, 148, 240));
                }
            }
        }

        int DrawWrappedClaim(UISpriteBatch batch, string text, int x, int y, int maxW, TextStyle style)
        {
            if (string.IsNullOrEmpty(text)) return 16;
            if (style.MeasureString(text).X <= maxW)
            {
                DrawFittedLine(batch, text, new SimRect(x, y, maxW, 16), style, SimTextAlign.Left);
                return 17;
            }
            string[] words = text.Split(' ');
            string line1 = "";
            int idx = 0;
            while (idx < words.Length)
            {
                string cand = line1.Length == 0 ? words[idx] : line1 + " " + words[idx];
                if (style.MeasureString(cand).X <= maxW || line1.Length == 0)
                {
                    line1 = cand;
                    idx++;
                }
                else break;
            }
            string line2 = idx < words.Length ? "   " + string.Join(" ", words, idx, words.Length - idx) : "";
            DrawFittedLine(batch, line1, new SimRect(x, y, maxW, 15), style, SimTextAlign.Left);
            if (line2.Length > 0)
            {
                DrawFittedLine(batch, line2, new SimRect(x, y + 15, maxW, 15), style, SimTextAlign.Left);
                return 31;
            }
            return 17;
        }

        void DrawBottomFeedbackBar(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game)
        {
            if (string.IsNullOrWhiteSpace(game.LastFeedback)) return;
            int bw = (int)Math.Min(780f, sw - 40f);
            int bx = (int)((sw - bw) * 0.5f);
            int by = (int)(sh - 48f);
            bool alert = game.LastFeedback.IndexOf("CONTRADICTION", StringComparison.OrdinalIgnoreCase) >= 0;
            Color bg = alert ? new Color(126, 36, 44, 238) : new Color(16, 42, 62, 236);
            Color bdr = alert ? new Color(252, 136, 136, 230) : new Color(110, 206, 246, 215);
            DrawCard(batch, new Rectangle(bx, by, bw, 34), bg, bdr, 10);
            DrawFittedLine(batch, game.LastFeedback, new SimRect(bx + 14, by + 7, bw - 28, 20), badgeStyle, SimTextAlign.Center);
        }

        // ---- Dramatic Accusation Climax Banner ----

        void DrawClimaxBanner(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game)
        {
            int bw = (int)Math.Min(760f, sw - 48f);
            int bx = (int)((sw - bw) * 0.5f);
            int by = (int)(sh - 94f);
            bool ok = game.SolvedCorrectly;

            DrawCard(batch, new Rectangle(bx, by, bw, 74),
                ok ? new Color(20, 74, 52, 244) : new Color(120, 28, 38, 244),
                ok ? new Color(122, 246, 186, 245) : new Color(252, 128, 128, 245), 12);

            DrawFittedLine(batch, ok ? "CULPRIT EXPOSED — CASE CRACKED!" : "WRONG SUSPECT ACCUSED!",
                new SimRect(bx + 16, by + 10, bw - 32, 24), titleStyle, SimTextAlign.Center);
            DrawFittedLine(batch, game.LastFeedback,
                new SimRect(bx + 16, by + 39, bw - 32, 22), bodyStyle, SimTextAlign.Center);
        }

        // ---- S/A/B/C Detective Case Report Scorecard ----

        void DrawScorecard(UISpriteBatch batch, Texture2D px, float sw, float sh, CrimeSceneDeductionGame game, CrimeSceneOutcome outcome)
        {
            if (outcome == null) return;
            int cw = (int)Math.Min(680f, sw - 48f);
            int ch = 368;
            int cx = (int)((sw - cw) * 0.5f);
            int cy = (int)Math.Max(68f, (sh - ch) * 0.5f);

            DrawCard(batch, new Rectangle(cx, cy, cw, ch),
                new Color(14, 20, 32, 248), new Color(236, 188, 86, 235), 16);

            int x = cx + 24;
            int iw = cw - 48;
            int y = cy + 18;

            DrawPill(batch, new Rectangle(x, y, iw, 24),
                outcome.SolvedCorrectly ? new Color(28, 108, 76, 240) : new Color(138, 42, 52, 240), 6);
            DrawFittedLine(batch, "DETECTIVE CASE REPORT  |  " + outcome.CaseTitle.ToUpperInvariant(),
                new SimRect(x + 12, y + 3, iw - 24, 18), badgeStyle, SimTextAlign.Center);
            y += 34;

            // Grade badge + summary
            Color gradeBg = outcome.Grade == "S" ? new Color(214, 158, 36, 248)
                          : outcome.Grade == "A" ? new Color(40, 146, 104, 246)
                          : outcome.Grade == "B" ? new Color(46, 104, 168, 244)
                          : new Color(132, 54, 62, 242);
            DrawCard(batch, new Rectangle(x, y, 76, 76), gradeBg, Color.White, 12);
            DrawFittedLine(batch, outcome.Grade, new SimRect(x, y + 14, 76, 48), gradeStyle, SimTextAlign.Center);

            DrawFittedLine(batch, outcome.SolvedCorrectly ? "CASE CLOSED — CULPRIT ARRESTED" : "CASE COLD — CULPRIT ESCAPED",
                new SimRect(x + 92, y + 4, iw - 92, 22), titleStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Accused: " + outcome.AccusedName + "   |   True Culprit: " + outcome.TrueCulpritName,
                new SimRect(x + 92, y + 30, iw - 92, 18), goldStyle, SimTextAlign.Left);
            DrawFittedLine(batch, "Clues: " + outcome.CluesFound + "/3   |   Interviews: " + outcome.SuspectsQuestioned + "/3   |   Score: " + outcome.FinalScore,
                new SimRect(x + 92, y + 52, iw - 92, 18), cyanStyle, SimTextAlign.Left);
            y += 92;

            FillRect(batch, px, x, y, iw, 1f, new Color(236, 188, 86, 105));
            y += 14;

            var r = outcome.Reward;
            DrawFittedLine(batch, "SIMANTICS CASE REWARDS (APPLIED ON RETURN HOME)",
                new SimRect(x, y, iw, 18), goldStyle, SimTextAlign.Left);
            y += 24;

            DrawFittedLine(batch, "  + Bonus Pay: §" + r.BonusSimoleons + "    |    Job Performance: +" + r.JobPerformanceDelta,
                new SimRect(x, y, iw, 20), mintStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, "  + Logic XP: +" + r.LogicXpDelta + "    |    Charisma XP: +" + r.CharismaXpDelta,
                new SimRect(x, y, iw, 20), cyanStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, "  + Motives: Fun " + (r.FunDelta >= 0 ? "+" : "") + r.FunDelta + ", Social " + (r.SocialDelta >= 0 ? "+" : "") + r.SocialDelta + ", Energy " + r.EnergyDelta,
                new SimRect(x, y, iw, 20), bodyStyle, SimTextAlign.Left);
            y += 24;
            DrawFittedLine(batch, "  + Unlocked on-lot Pie Menu: \"Question Sim\" (Detective+)",
                new SimRect(x, y, iw, 18), goldStyle, SimTextAlign.Left);
            y += 32;

            claimBtnRect = new Rectangle(x, y, iw, 42);
            DrawActionBtn(batch, claimBtnRect, "CLAIM REWARDS & RETURN HOME", hoverClaim,
                new Color(38, 134, 92, 248), new Color(128, 248, 192, 245));
        }

        void DrawActionBtn(UISpriteBatch batch, Rectangle rect, string text, bool hovered, Color fill, Color border)
        {
            Color f = hovered
                ? new Color(Math.Min(255, fill.R + 26), Math.Min(255, fill.G + 26), Math.Min(255, fill.B + 26), fill.A)
                : fill;
            DrawCard(batch, rect, f, border, 8);
            DrawFittedLine(batch, text, new SimRect(rect.X + 6, rect.Y + (rect.Height - 18) / 2, rect.Width - 12, 18),
                badgeStyle, SimTextAlign.Center);
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
