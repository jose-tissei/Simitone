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
    // Minimal translucent floating HUD for Law Enforcement Level 3 ("Patrol Officer"):
    // Real-Lot Live Beat Patrol (DISP-06). Leaves the real Sims 1 lot 100% visible and interactive
    // so the player pans, zooms, and clicks suspects/objects with Simitone's native Pie Menu.
    public sealed class BeatPatrolOverlay : SimUiElement
    {
        static BeatPatrolOverlay instance;
        static bool isOpen;

        readonly TextStyle titleStyle;
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

        bool hoverStart, hoverSkip, hoverEnd, hoverClaim;
        bool prevLeftDown, prevSpaceDown, prevEnterDown, prevEscDown;

        public static bool IsOpen { get { return isOpen && instance != null && instance.Parent != null; } }

        BeatPatrolOverlay()
        {
            titleStyle = SimUiTokens.MakeStyle(17, SimUiTokens.DialogTitle, true);
            subStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextMuted, true);
            bodyStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            smallStyle = SimUiTokens.MakeStyle(11, SimUiTokens.TextSubtle, true);
            badgeStyle = SimUiTokens.MakeStyle(11, Color.White, true);
            cyanStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            mintStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Mint, true);
            goldStyle = SimUiTokens.MakeStyle(12, new Color(246, 206, 94), true);
            redStyle = SimUiTokens.MakeStyle(12, new Color(244, 118, 118), true);
            gradeStyle = SimUiTokens.MakeStyle(36, Color.White, true);
        }

        public static void Open()
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null) instance = new BeatPatrolOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                isOpen = true;
                instance.prevLeftDown = true;
                instance.prevSpaceDown = true;
                instance.prevEnterDown = true;
                instance.prevEscDown = true;
            }
            catch (Exception e) { Boot.Log("beat-patrol overlay open error: " + e.Message); }
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
            try { BeatPatrolDriver.PreDrawFrame(); }
            catch (Exception e) { Boot.Log("beat-patrol PreDraw error: " + e.Message); }
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen) return;

            BeatPatrolDriver.TickAlertSweep();

            var game = BeatPatrolDriver.Game;
            if (game == null || !BeatPatrolDriver.IsPatrolLotLive)
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

            bool spaceDown = kb.IsKeyDown(Keys.Space);
            bool enterDown = kb.IsKeyDown(Keys.Enter);
            bool escDown = kb.IsKeyDown(Keys.Escape);
            bool spacePressed = spaceDown && !prevSpaceDown;
            bool enterPressed = enterDown && !prevEnterDown;
            bool escPressed = escDown && !prevEscDown;
            prevSpaceDown = spaceDown;
            prevEnterDown = enterDown;
            prevEscDown = escDown;

            if (game.IsBriefing)
            {
                hoverStart = startBtnRect.Contains(mx, my);
                hoverSkip = skipBtnRect.Contains(mx, my);
                hoverEnd = false;
                hoverClaim = false;

                if (spacePressed || enterPressed || (justClicked && hoverStart))
                    BeatPatrolDriver.BeginActivePatrolFromBriefing();
                else if (escPressed || (justClicked && hoverSkip))
                    BeatPatrolDriver.RequestReturnHome(false);
            }
            else if (game.IsFinished && BeatPatrolDriver.FinalOutcome != null)
            {
                hoverClaim = claimBtnRect.Contains(mx, my);
                hoverStart = hoverSkip = hoverEnd = false;

                if (spacePressed || enterPressed || escPressed || (justClicked && hoverClaim))
                    BeatPatrolDriver.RequestReturnHome(true);
            }
            else
            {
                hoverEnd = endShiftBtnRect.Contains(mx, my);
                hoverStart = hoverSkip = hoverClaim = false;

                if (justClicked && hoverEnd)
                    BeatPatrolDriver.HandleScriptedAction("end");
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!isOpen) return;
            var game = BeatPatrolDriver.Game;
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

                if (game.IsBriefing)
                    DrawBriefingCard(batch, px, sw, sh, game);
                else if (game.IsFinished && BeatPatrolDriver.FinalOutcome != null)
                    DrawScorecard(batch, px, sw, sh, game, BeatPatrolDriver.FinalOutcome);
                else
                    DrawTopPatrolHud(batch, px, sw, sh, game);
            }
            catch (Exception e)
            {
                Boot.Log("beat-patrol draw error: " + e.GetType().Name + ": " + e.Message);
            }
        }

        void DrawBriefingCard(UISpriteBatch batch, Texture2D px, float sw, float sh, BeatPatrolGame game)
        {
            int cw = (int)Math.Min(432f, sw - 48f);
            int ch = 314;
            int cx = 24;
            int cy = (int)Math.Max(24f, Math.Min(sh - ch - 24f, (sh - ch) * 0.42f));
            var rect = new Rectangle(cx, cy, cw, ch);

            DrawCard(batch, rect, new Color(14, 24, 38, 232), new Color(76, 152, 224, 195), 14);

            int x = cx + 18;
            int innerW = cw - 36;
            int y = cy + 16;

            var pill = new Rectangle(x, y, innerW, 22);
            DrawPill(batch, pill, new Color(36, 98, 178, 225), 6);
            DrawFittedLine(batch, "LAW ENFORCEMENT  |  LVL 3 PATROL OFFICER", new SimRect(x + 10, y + 2, innerW - 20, 18), badgeStyle, SimTextAlign.Left);
            y += 30;

            DrawFittedLine(batch, "REAL-LOT BEAT PATROL", new SimRect(x, y, innerW, 22), titleStyle, SimTextAlign.Left);
            y += 24;

            DrawFittedLine(batch, game.SectorDisplayName, new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 24;

            FillRect(batch, px, x, y, innerW, 1f, new Color(90, 140, 200, 95));
            y += 12;

            DrawFittedLine(batch, "- Squad Car parked at curb (Officer inside).", new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, "- Pan & zoom the real lot to spot 3 active calls:", new SimRect(x, y, innerW, 18), bodyStyle, SimTextAlign.Left);
            y += 20;
            DrawFittedLine(batch, "  1. Prowling Burglar -> SNEAK UP & APPREHEND!", new SimRect(x, y, innerW, 18), mintStyle, SimTextAlign.Left);
            y += 20;
            DrawFittedLine(batch, "  2. Loud Boombox -> Warn (+Rel) or Ticket (+$)", new SimRect(x, y, innerW, 18), goldStyle, SimTextAlign.Left);
            y += 20;
            DrawFittedLine(batch, "  3. Trash Hazard -> Inspect & Log (+Logic XP)", new SimRect(x, y, innerW, 18), cyanStyle, SimTextAlign.Left);
            y += 24;
            DrawFittedLine(batch, "Click suspects or hazards directly for Patrol Pie Menus!", new SimRect(x, y, innerW, 16), subStyle, SimTextAlign.Left);
            y += 28;

            int btnW = (innerW - 12) / 2;
            startBtnRect = new Rectangle(x, y, btnW + 24, 36);
            skipBtnRect = new Rectangle(x + btnW + 36, y, btnW - 24, 36);

            DrawPill(batch, startBtnRect, hoverStart ? new Color(56, 152, 238, 250) : new Color(36, 122, 208, 240), 8);
            DrawFittedLine(batch, "START PATROL [SPACE]", ToSimRect(startBtnRect), badgeStyle, SimTextAlign.Center);

            DrawPill(batch, skipBtnRect, hoverSkip ? new Color(82, 96, 118, 235) : new Color(54, 66, 86, 220), 8);
            DrawFittedLine(batch, "RETURN [ESC]", ToSimRect(skipBtnRect), badgeStyle, SimTextAlign.Center);
        }

        void DrawTopPatrolHud(UISpriteBatch batch, Texture2D px, float sw, float sh, BeatPatrolGame game)
        {
            int barW = (int)Math.Min(860f, sw - 32f);
            int barH = 68;
            int bx = (int)((sw - barW) * 0.5f);
            int by = 14;
            var rect = new Rectangle(bx, by, barW, barH);

            DrawCard(batch, rect, new Color(14, 24, 38, 226), new Color(72, 146, 218, 180), 12);

            int x = bx + 14;
            int y1 = by + 9;
            DrawFittedLine(batch, game.SectorDisplayName, new SimRect(x, y1, 290, 18), cyanStyle, SimTextAlign.Left);

            // Officer state badge
            string stateLabel;
            Color stateBg;
            var mode = BeatPatrolDriver.MotionMode;
            if (mode == OfficerMotionMode.InCruiser)
            {
                stateLabel = "IN CRUISER";
                stateBg = new Color(38, 108, 182, 235);
            }
            else if (mode == OfficerMotionMode.MovingToTarget && BeatPatrolDriver.LastActiveClipName == "patrol_sneak_loop")
            {
                stateLabel = "SNEAKING!";
                stateBg = new Color(42, 156, 112, 245);
            }
            else if (mode == OfficerMotionMode.PerformingAction)
            {
                stateLabel = "RESPONDING";
                stateBg = new Color(204, 138, 36, 245);
            }
            else if (mode == OfficerMotionMode.EscortingToCruiser)
            {
                stateLabel = "ESCORT TO CAR";
                stateBg = new Color(44, 134, 214, 248);
            }
            else
            {
                stateLabel = "ON FOOT";
                stateBg = new Color(64, 96, 144, 230);
            }

            var stateRect = new Rectangle(bx + barW / 2 - 108, y1 - 2, 132, 22);
            DrawPill(batch, stateRect, stateBg, 6);
            DrawFittedLine(batch, stateLabel, ToSimRect(stateRect), badgeStyle, SimTextAlign.Center);

            int rem = (int)Math.Ceiling(game.RemainingSeconds);
            string clockStr = string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", rem / 60, rem % 60);
            string stats = "CALLS " + game.ResolvedRequiredCount + "/" + game.RequiredCount +
                           "  |  SCORE " + game.Score +
                           "  |  " + clockStr;
            DrawFittedLine(batch, stats, new SimRect(bx + barW - 340, y1, 232, 18), badgeStyle, SimTextAlign.Right);

            endShiftBtnRect = new Rectangle(bx + barW - 96, by + 7, 84, 24);
            DrawPill(batch, endShiftBtnRect, hoverEnd ? new Color(186, 78, 72, 245) : new Color(138, 56, 56, 225), 6);
            DrawFittedLine(batch, "END SHIFT", ToSimRect(endShiftBtnRect), badgeStyle, SimTextAlign.Center);

            // Bottom row: Radio hint or latest callout
            int y2 = by + 37;
            string line;
            TextStyle lineStyle = bodyStyle;
            if (mode == OfficerMotionMode.EscortingToCruiser)
            {
                line = "Escorting apprehended Burglar back to the parked Police Squad Car...";
                lineStyle = cyanStyle;
            }
            else
            {
                line = !string.IsNullOrWhiteSpace(game.LastCallout) && game.Score > 0
                    ? game.LastCallout
                    : game.ActiveRadioHint();

                if (game.Score > 0)
                {
                    if (game.LastRating == PatrolActionRating.Perfect) lineStyle = mintStyle;
                    else if (game.LastRating == PatrolActionRating.Good) lineStyle = goldStyle;
                    else if (game.LastRating == PatrolActionRating.Miss) lineStyle = redStyle;
                }
            }
            DrawFittedLine(batch, line, new SimRect(x, y2, barW - 28, 20), lineStyle, SimTextAlign.Left);
        }

        void DrawScorecard(UISpriteBatch batch, Texture2D px, float sw, float sh, BeatPatrolGame game, BeatPatrolOutcome outcome)
        {
            int cw = (int)Math.Min(560f, sw - 48f);
            int ch = 368;
            int cx = (int)((sw - cw) * 0.5f);
            int cy = (int)((sh - ch) * 0.5f);
            var rect = new Rectangle(cx, cy, cw, ch);

            DrawCard(batch, rect, new Color(14, 22, 36, 244), new Color(84, 168, 236, 215), 16);

            int x = cx + 22;
            int innerW = cw - 44;
            int y = cy + 18;

            Color gradeBg = outcome.Grade == "S" ? new Color(46, 174, 126, 248)
                          : outcome.Grade == "A" ? new Color(54, 134, 218, 248)
                          : outcome.Grade == "B" ? new Color(214, 156, 46, 248)
                          : new Color(162, 78, 72, 245);
            var gradeBox = new Rectangle(cx + cw - 94, y, 72, 68);
            DrawPill(batch, gradeBox, gradeBg, 12);
            DrawFittedLine(batch, outcome.Grade, ToSimRect(gradeBox), gradeStyle, SimTextAlign.Center);

            DrawFittedLine(batch, "SIMCITY PD  |  BEAT PATROL REPORT", new SimRect(x, y, innerW - 86, 18), cyanStyle, SimTextAlign.Left);
            y += 22;
            DrawFittedLine(batch, game.SectorDisplayName, new SimRect(x, y, innerW - 86, 22), titleStyle, SimTextAlign.Left);
            y += 24;
            DrawFittedLine(batch, "Officer " + game.OfficerName + "  |  Calls " + outcome.ResolvedTargets + "/" + outcome.TotalTargets + "  |  Score " + outcome.TotalPoints, new SimRect(x, y, innerW - 86, 18), subStyle, SimTextAlign.Left);
            y += 32;

            FillRect(batch, px, x, y, innerW, 1f, new Color(92, 140, 200, 95));
            y += 12;

            for (int i = 0; i < game.Incidents.Count; i++)
            {
                var inc = game.Incidents[i];
                if (!inc.IsRequiredTarget && !inc.Resolved) continue;
                string status = inc.Resolved ? inc.Rating.ToString().ToUpperInvariant() : "UNRESOLVED";
                TextStyle stStyle = !inc.Resolved ? subStyle
                                  : inc.Rating == PatrolActionRating.Perfect ? mintStyle
                                  : inc.Rating == PatrolActionRating.Good ? goldStyle
                                  : redStyle;

                DrawFittedLine(batch, "- " + inc.Title + " (" + inc.ActorName + ")", new SimRect(x, y, innerW - 165, 20), bodyStyle, SimTextAlign.Left);
                string ptsStr = status + (inc.Resolved ? " (" + (inc.PointsAwarded >= 0 ? "+" : "") + inc.PointsAwarded + ")" : "");
                DrawFittedLine(batch, ptsStr, new SimRect(cx + cw - 182, y, 160, 20), stStyle, SimTextAlign.Right);
                y += 24;
            }

            y += 8;
            FillRect(batch, px, x, y, innerW, 1f, new Color(92, 140, 200, 95));
            y += 12;

            var r = outcome.Reward;
            string rewardLine = "REWARDS:  +$" + r.BonusSimoleons +
                                "  |  Perf +" + r.JobPerformanceDelta +
                                "  |  Logic +" + r.LogicXpDelta + " XP" +
                                "  |  Charisma +" + r.CharismaXpDelta + " XP";
            DrawFittedLine(batch, rewardLine, new SimRect(x, y, innerW, 20), mintStyle, SimTextAlign.Left);
            y += 24;

            if (outcome.RelationshipDeltas.Count > 0)
            {
                string relText = "COMMUNITY: ";
                for (int i = 0; i < outcome.RelationshipDeltas.Count; i++)
                {
                    var rd = outcome.RelationshipDeltas[i];
                    if (i > 0) relText += "  |  ";
                    relText += rd.SimName + " (" + (rd.DailyDelta > 0 ? "+" : "") + rd.DailyDelta + " Rel)";
                }
                DrawFittedLine(batch, relText, new SimRect(x, y, innerW, 20), goldStyle, SimTextAlign.Left);
                y += 24;
            }

            claimBtnRect = new Rectangle(cx + (cw - 280) / 2, cy + ch - 54, 280, 38);
            DrawPill(batch, claimBtnRect, hoverClaim ? new Color(56, 178, 132, 250) : new Color(38, 146, 108, 242), 9);
            DrawFittedLine(batch, "CLAIM & RETURN HOME [SPACE]", ToSimRect(claimBtnRect), badgeStyle, SimTextAlign.Center);
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
