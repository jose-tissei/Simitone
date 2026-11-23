using System;
using System.Collections.Generic;
using System.Linq;
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
    // Interactive in-game overlay menu showing everything each Sim has learned about the Sims they know:
    // personality traits, temperament, career, liked/disliked interests, discovered skills, and what approaches work or fail.
    // Opened via Options -> "Sim memory" (or `simtool convo memory-ui open`).
    public class SimMemoryMenu : UIElement
    {
        static SimMemoryMenu instance;
        static bool isOpen;

        readonly TextStyle titleStyle, subtitleStyle, sectionStyle, bodyStyle, mutedStyle, badgeStyle, tabStyle, greenStyle, redStyle, goldStyle;
        readonly List<SpeakerIdentity> speakers = new List<SpeakerIdentity>();
        readonly List<Rectangle> tabRects = new List<Rectangle>();
        readonly List<Rectangle> rowRects = new List<Rectangle>();
        Rectangle panelRect, closeRect;

        string selectedSpeakerKey = "";
        string selectedSpeakerName = "";
        string selectedListenerKey = "";
        int scrollOffset;
        SimMemorySnapshot snapshot = new SimMemorySnapshot();
        double refreshTimer;
        bool prevLeftDown, prevEscDown;
        int prevScrollWheel;

        public static bool IsOpen { get { return isOpen && instance != null; } }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open(null);
        }

        public static string Open(string speakerQuery = null)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new SimMemoryMenu();
                if (instance.Parent != screen)
                {
                    if (instance.Parent != null) instance.Parent.Remove(instance);
                    screen.Add(instance);
                }
                isOpen = true;
                instance.Visible = true;
                // Ignore the mouse/key press that triggered Open() so clicking Options -> Sim memory never instantly closes it
                instance.prevLeftDown = true;
                instance.prevEscDown = true;
                instance.RefreshData(speakerQuery, true);
                return Status();
            }
            catch (Exception e)
            {
                Boot.Log("memory-ui open failed: " + e.Message);
                return "error: " + e.Message;
            }
        }

        public static string Close()
        {
            try
            {
                isOpen = false;
                if (instance != null)
                {
                    instance.Visible = false;
                    if (instance.Parent != null) instance.Parent.Remove(instance);
                }
                return "closed";
            }
            catch { return "closed"; }
        }

        public static string SelectKnown(string query)
        {
            if (!IsOpen) Open(null);
            if (instance == null) return "closed";
            instance.RefreshData(null, false);
            if (!string.IsNullOrWhiteSpace(query))
            {
                var hit = instance.snapshot.KnownSims.FirstOrDefault(k =>
                    (k.ListenerName ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (k.ListenerKey ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) instance.selectedListenerKey = hit.ListenerKey;
            }
            return Status();
        }

        public static string Status()
        {
            if (!IsOpen || instance == null) return "open=False";
            var card = instance.CurrentCard();
            return "open=True speaker=" + instance.selectedSpeakerName +
                   " known=" + instance.snapshot.KnownSims.Count +
                   (card != null ? " selected=[" + SimMemoryViewModel.FormatCardLine(card) + "]" : " selected=none");
        }

        SimMemoryMenu()
        {
            titleStyle = MakeStyle(20, new Color(255, 228, 130));
            subtitleStyle = MakeStyle(13, new Color(185, 198, 214));
            sectionStyle = MakeStyle(13, new Color(130, 214, 232));
            bodyStyle = MakeStyle(14, new Color(240, 244, 248));
            mutedStyle = MakeStyle(13, new Color(152, 164, 180));
            badgeStyle = MakeStyle(12, new Color(245, 248, 252));
            tabStyle = MakeStyle(14, new Color(235, 240, 246));
            greenStyle = MakeStyle(14, new Color(126, 230, 158));
            redStyle = MakeStyle(14, new Color(250, 136, 136));
            goldStyle = MakeStyle(14, new Color(255, 220, 120));
        }

        static TextStyle MakeStyle(int size, Color color)
        {
            var s = TextStyle.DefaultLabel.Clone();
            s.Size = size;
            s.Color = color;
            s.Shadow = true;
            return s;
        }

        void RefreshData(string speakerQuery, bool resetToActiveIfEmpty)
        {
            speakers.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string activeKey = null, activeName = null;

            // 1. Add currently active Sim & lot avatars first so they're always selectable
            try
            {
                var screen = UIScreen.Current;
                var lot = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                var active = lot?.GetType().GetProperty("ActiveEntity")?.GetValue(lot, null) as VMAvatar;
                var vm = screen?.GetType().GetProperty("vm")?.GetValue(screen, null) as VM;

                if (active != null)
                {
                    activeKey = KeyOf(active);
                    activeName = SafeName(active.Name);
                    if (seen.Add(activeKey))
                        speakers.Add(new SpeakerIdentity { Key = activeKey, Name = activeName, KnownCount = Boot.Memory.GetKnownSims(activeKey).Count });
                }
                if (vm != null && vm.Ready)
                {
                    foreach (var av in vm.Entities.OfType<VMAvatar>().Where(a => !a.Dead && !a.IsPet && a.GetPersonData(VMPersonDataVariable.PersonsAge) >= 18))
                    {
                        var k = KeyOf(av);
                        if (seen.Add(k))
                            speakers.Add(new SpeakerIdentity { Key = k, Name = SafeName(av.Name), KnownCount = Boot.Memory.GetKnownSims(k).Count });
                    }
                }
            }
            catch { }

            // 2. Add any other Sims present in persisted SocialMemory
            foreach (var sp in Boot.Memory.GetAllSpeakers())
            {
                if (seen.Add(sp.Key)) speakers.Add(sp);
            }

            // 3. Pick which Sim's memory to display
            if (!string.IsNullOrWhiteSpace(speakerQuery))
            {
                var hit = speakers.FirstOrDefault(s =>
                    (s.Name ?? "").IndexOf(speakerQuery, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (s.Key ?? "").IndexOf(speakerQuery, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) { selectedSpeakerKey = hit.Key; selectedSpeakerName = hit.Name; }
            }
            else if (resetToActiveIfEmpty && !string.IsNullOrEmpty(activeKey))
            {
                selectedSpeakerKey = activeKey;
                selectedSpeakerName = activeName;
            }
            else if (string.IsNullOrEmpty(selectedSpeakerKey) && speakers.Count > 0)
            {
                selectedSpeakerKey = speakers[0].Key;
                selectedSpeakerName = speakers[0].Name;
            }

            if (string.IsNullOrEmpty(selectedSpeakerName))
            {
                var sp = speakers.FirstOrDefault(s => string.Equals(s.Key, selectedSpeakerKey, StringComparison.OrdinalIgnoreCase));
                selectedSpeakerName = sp != null ? sp.Name : "Sim";
            }

            snapshot = SimMemoryViewModel.Build(Boot.Memory, selectedSpeakerKey, selectedSpeakerName, Boot.Subjects);
            if (snapshot.KnownSims.Count == 0)
            {
                selectedListenerKey = "";
                scrollOffset = 0;
            }
            else if (!snapshot.KnownSims.Any(k => string.Equals(k.ListenerKey, selectedListenerKey, StringComparison.OrdinalIgnoreCase)))
            {
                selectedListenerKey = snapshot.KnownSims[0].ListenerKey;
                scrollOffset = 0;
            }
        }

        static string KeyOf(VMAvatar a)
        {
            short nid = a.GetPersonData(VMPersonDataVariable.NeighborId);
            if (nid > 0) return "n:" + nid;
            return "name:" + SafeName(a.Name).ToLowerInvariant();
        }

        static string SafeName(string n) { return string.IsNullOrWhiteSpace(n) ? "Sim" : n.Trim(); }

        SimKnowledgeCard CurrentCard()
        {
            if (snapshot == null || snapshot.KnownSims.Count == 0) return null;
            return snapshot.KnownSims.FirstOrDefault(k => string.Equals(k.ListenerKey, selectedListenerKey, StringComparison.OrdinalIgnoreCase))
                   ?? snapshot.KnownSims[0];
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!isOpen || !Visible) return;
            refreshTimer += 1.0 / 60.0;
            if (refreshTimer >= 0.75)
            {
                refreshTimer = 0;
                RefreshData(null, false);
            }

            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEscDown)
            {
                prevEscDown = true;
                Close();
                return;
            }
            prevEscDown = esc;

            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            int mx = (int)(state.MouseState.X / scale);
            int my = (int)(state.MouseState.Y / scale);
            int wheel = state.MouseState.ScrollWheelValue;
            if (wheel != prevScrollWheel && panelRect.Contains(mx, my))
            {
                if (wheel < prevScrollWheel) scrollOffset = Math.Min(Math.Max(0, snapshot.KnownSims.Count - 6), scrollOffset + 1);
                else scrollOffset = Math.Max(0, scrollOffset - 1);
            }
            prevScrollWheel = wheel;

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (closeRect.Contains(mx, my) || (panelRect.Width > 0 && !panelRect.Contains(mx, my)))
                {
                    prevLeftDown = true;
                    Close();
                    return;
                }
                for (int i = 0; i < tabRects.Count && i < speakers.Count; i++)
                {
                    if (tabRects[i].Contains(mx, my))
                    {
                        selectedSpeakerKey = speakers[i].Key;
                        selectedSpeakerName = speakers[i].Name;
                        selectedListenerKey = "";
                        scrollOffset = 0;
                        RefreshData(null, false);
                        break;
                    }
                }
                for (int i = 0; i < rowRects.Count; i++)
                {
                    int idx = scrollOffset + i;
                    if (idx < snapshot.KnownSims.Count && rowRects[i].Contains(mx, my))
                    {
                        selectedListenerKey = snapshot.KnownSims[idx].ListenerKey;
                        break;
                    }
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
            float screenH = batch.GraphicsDevice.Viewport.Height / scale;

            float pw = Math.Min(860f, screenW - 60f);
            float ph = Math.Min(560f, screenH - 70f);
            float px0 = (screenW - pw) * 0.5f;
            float py0 = (screenH - ph) * 0.5f;
            panelRect = new Rectangle((int)px0, (int)py0, (int)pw, (int)ph);

            // Subtle dim backdrop + main card + gold top accent
            DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(screenW, screenH), new Color(6, 10, 16, 130));
            DrawLocalTexture(batch, px, null, new Vector2(px0 - 2, py0 - 2), new Vector2(pw + 4, ph + 4), new Color(90, 135, 165, 190));
            DrawLocalTexture(batch, px, null, new Vector2(px0, py0), new Vector2(pw, ph), new Color(18, 24, 36, 242));
            DrawLocalTexture(batch, px, null, new Vector2(px0, py0), new Vector2(pw, 3), new Color(255, 224, 120, 240));

            // Header
            DrawLocalString(batch, snapshot.SpeakerName + "'s Sim Memory", new Vector2(px0 + 20, py0 + 14), titleStyle);
            var sub = snapshot.KnownSims.Count == 1
                ? "What " + snapshot.SpeakerName + " has learned about 1 Sim"
                : "What " + snapshot.SpeakerName + " has learned about " + snapshot.KnownSims.Count + " Sims";
            DrawLocalString(batch, sub, new Vector2(px0 + 20, py0 + 40), subtitleStyle);

            // Close button [X]
            closeRect = new Rectangle((int)(px0 + pw - 46), (int)(py0 + 14), 30, 28);
            DrawLocalTexture(batch, px, null, new Vector2(closeRect.X, closeRect.Y), new Vector2(closeRect.Width, closeRect.Height), new Color(68, 44, 54, 230));
            var xSz = bodyStyle.MeasureString("X");
            DrawLocalString(batch, "X", new Vector2(closeRect.X + (closeRect.Width - xSz.X) * 0.5f, closeRect.Y + (closeRect.Height - xSz.Y) * 0.5f), bodyStyle);

            // Speaker switcher tabs
            tabRects.Clear();
            float tx = px0 + 20, ty = py0 + 66;
            for (int i = 0; i < speakers.Count && i < 6; i++)
            {
                var sp = speakers[i];
                bool sel = string.Equals(sp.Key, selectedSpeakerKey, StringComparison.OrdinalIgnoreCase);
                string label = sp.Name + " (" + sp.KnownCount + ")";
                float tw = Math.Max(82f, tabStyle.MeasureString(label).X + 22f);
                if (tx + tw > px0 + pw - 20) break;
                var r = new Rectangle((int)tx, (int)ty, (int)tw, 28);
                tabRects.Add(r);
                DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height),
                    sel ? new Color(46, 98, 128, 245) : new Color(30, 40, 58, 220));
                if (sel) DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y + r.Height - 2), new Vector2(r.Width, 2), new Color(255, 224, 120, 255));
                DrawLocalString(batch, label, new Vector2(r.X + 11, r.Y + 6), sel ? goldStyle : tabStyle);
                tx += tw + 8;
            }

            // Divider line under tabs
            float bodyY = py0 + 104;
            float bodyH = ph - 134;
            DrawLocalTexture(batch, px, null, new Vector2(px0 + 16, bodyY - 4), new Vector2(pw - 32, 1), new Color(70, 95, 120, 160));

            // Left column: Known Sims list
            float leftW = 230f;
            float leftX = px0 + 18;
            DrawLocalTexture(batch, px, null, new Vector2(leftX, bodyY + 4), new Vector2(leftW, bodyH - 8), new Color(13, 18, 28, 215));
            DrawLocalString(batch, "KNOWN SIMS", new Vector2(leftX + 12, bodyY + 12), sectionStyle);

            rowRects.Clear();
            if (snapshot.KnownSims.Count == 0)
            {
                DrawWrapped(batch, "No learned memories yet.", leftX + 12, bodyY + 38, leftW - 24, mutedStyle);
            }
            else
            {
                float ry = bodyY + 36;
                int maxRows = Math.Max(1, (int)((bodyH - 48) / 56));
                for (int i = 0; i < maxRows && scrollOffset + i < snapshot.KnownSims.Count; i++)
                {
                    var item = snapshot.KnownSims[scrollOffset + i];
                    bool sel = string.Equals(item.ListenerKey, selectedListenerKey, StringComparison.OrdinalIgnoreCase);
                    var r = new Rectangle((int)(leftX + 6), (int)ry, (int)(leftW - 12), 50);
                    rowRects.Add(r);
                    DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height),
                        sel ? new Color(40, 74, 102, 240) : new Color(24, 32, 46, 200));
                    if (sel) DrawLocalTexture(batch, px, null, new Vector2(r.X, r.Y), new Vector2(4, r.Height), new Color(255, 224, 120, 255));
                    DrawLocalString(batch, item.ListenerName, new Vector2(r.X + 12, r.Y + 6), sel ? goldStyle : bodyStyle);
                    string meta = item.FamiliarityLabel + " | " + item.KnowledgePercent + "% | " + item.Exchanges + (item.Exchanges == 1 ? " chat" : " chats");
                    DrawLocalString(batch, meta, new Vector2(r.X + 12, r.Y + 26), mutedStyle);
                    ry += 56;
                }
            }

            // Right column: Selected Sim's learned dossier
            float rightX = leftX + leftW + 14;
            float rightW = pw - (rightX - px0) - 18;
            DrawLocalTexture(batch, px, null, new Vector2(rightX, bodyY + 4), new Vector2(rightW, bodyH - 8), new Color(15, 21, 32, 225));

            var card = CurrentCard();
            if (card == null)
            {
                DrawLocalString(batch, "Nothing learned yet", new Vector2(rightX + 20, bodyY + 24), titleStyle);
                DrawWrapped(batch,
                    snapshot.SpeakerName + " hasn't talked with anyone yet. Use Talk or let Sims chat autonomously in the same room - every conversation records traits, interests, skills, temperament, and what approaches work best.",
                    rightX + 20, bodyY + 58, rightW - 40, bodyStyle);
                DrawLocalString(batch, "Press Esc or click outside to close", new Vector2(px0 + pw - 245, py0 + ph - 22), mutedStyle);
                return;
            }

            float cx = rightX + 18;
            float cw = rightW - 36;
            float cy = bodyY + 14;

            // Dossier Header + Familiarity bar
            DrawLocalString(batch, card.ListenerName, new Vector2(cx, cy), titleStyle);
            string relText = "STR " + Signed(card.LastStr) + "   LTR " + Signed(card.LastLtr) + "   (" + card.PositiveCount + " great / " + card.NeutralCount + " ok / " + card.NegativeCount + " awkward)";
            DrawLocalString(batch, relText, new Vector2(cx, cy + 26), subtitleStyle);

            // Knowledge progress bar on top right
            float barW = 155f, barX = rightX + rightW - barW - 18, barY = cy + 6;
            DrawLocalString(batch, card.FamiliarityLabel + " (" + card.KnowledgePercent + "%)", new Vector2(barX, barY), badgeStyle);
            DrawLocalTexture(batch, px, null, new Vector2(barX, barY + 18), new Vector2(barW, 8), new Color(40, 52, 70, 220));
            DrawLocalTexture(batch, px, null, new Vector2(barX, barY + 18), new Vector2(barW * (card.KnowledgePercent / 100f), 8),
                card.IsTense ? new Color(235, 120, 100, 240) : new Color(90, 210, 170, 240));
            cy += 52;

            if (card.IsTense)
            {
                DrawLocalTexture(batch, px, null, new Vector2(cx, cy), new Vector2(cw, 24), new Color(115, 42, 48, 210));
                DrawLocalString(batch, "Recent exchanges were tense - favoring gentle or apologetic approaches.", new Vector2(cx + 8, cy + 4), redStyle);
                cy += 30;
            }

            // Section 1: Personality, Temperament & Career
            cy = DrawSectionHeader(batch, px, "PERSONALITY & PROFILE", cx, cy, cw);
            string traitsLine = card.Traits.Count > 0 ? string.Join(", ", card.Traits) : "Not observed yet (chat more to reveal)";
            cy = DrawKeyValue(batch, "Traits: ", traitsLine, cx, cy, cw, bodyStyle);
            string temperLine = !string.IsNullOrEmpty(card.Temperament) ? card.Temperament : "Still reading social cues";
            cy = DrawKeyValue(batch, "Temperament: ", temperLine, cx, cy, cw, goldStyle);
            if (!string.IsNullOrEmpty(card.Career))
                cy = DrawKeyValue(batch, "Career: ", card.Career, cx, cy, cw, bodyStyle);
            cy += 6;

            // Section 2: Interests & Skills Learned
            cy = DrawSectionHeader(batch, px, "INTERESTS & SKILLS LEARNED", cx, cy, cw);
            if (card.LikedInterests.Count == 0 && card.NeutralInterests.Count == 0 && card.DislikedInterests.Count == 0)
            {
                cy = DrawKeyValue(batch, "Interests: ", "None discovered yet", cx, cy, cw, mutedStyle);
            }
            else
            {
                if (card.LikedInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Likes: ", string.Join(", ", card.LikedInterests), cx, cy, cw, greenStyle);
                if (card.NeutralInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Neutral: ", string.Join(", ", card.NeutralInterests), cx, cy, cw, bodyStyle);
                if (card.DislikedInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Dislikes: ", string.Join(", ", card.DislikedInterests), cx, cy, cw, redStyle);
            }
            string skillsLine = card.Skills.Count > 0 ? string.Join(", ", card.Skills) : "None discovered yet";
            cy = DrawKeyValue(batch, "Skills: ", skillsLine, cx, cy, cw, card.Skills.Count > 0 ? goldStyle : mutedStyle);
            cy += 6;

            // Section 3: What Works / Avoid & Recent Topics
            if (cy < bodyY + bodyH - 64)
            {
                cy = DrawSectionHeader(batch, px, "CONVERSATION LESSONS", cx, cy, cw);
                var works = new List<string>();
                if (card.WorkedInteractions.Count > 0) works.Add(string.Join(", ", card.WorkedInteractions));
                if (card.WorkedTones.Count > 0) works.Add(string.Join(", ", card.WorkedTones) + " tone");
                if (works.Count > 0)
                    cy = DrawKeyValue(batch, "Works well: ", string.Join(" | ", works), cx, cy, cw, greenStyle);

                var avoid = new List<string>();
                if (card.AvoidInteractions.Count > 0) avoid.Add(string.Join(", ", card.AvoidInteractions));
                if (card.AvoidTones.Count > 0) avoid.Add(string.Join(", ", card.AvoidTones) + " tone");
                if (avoid.Count > 0)
                    cy = DrawKeyValue(batch, "Avoid: ", string.Join(" | ", avoid), cx, cy, cw, redStyle);

                if (card.Topics.Count > 0 && cy < bodyY + bodyH - 34)
                {
                    string topicStr = string.Join(" | ", card.Topics.Take(4).Select(t => t.Title + " (" + t.OutcomeLabel + ")"));
                    cy = DrawKeyValue(batch, "Topics: ", topicStr, cx, cy, cw, bodyStyle);
                }
            }

            // Footer hint
            DrawLocalString(batch, "Press Esc or click outside to close", new Vector2(px0 + pw - 245, py0 + ph - 22), mutedStyle);
        }

        float DrawSectionHeader(UISpriteBatch batch, Texture2D px, string label, float x, float y, float w)
        {
            DrawLocalString(batch, label, new Vector2(x, y), sectionStyle);
            DrawLocalTexture(batch, px, null, new Vector2(x, y + 17), new Vector2(w, 1), new Color(65, 92, 118, 130));
            return y + 22;
        }

        float DrawKeyValue(UISpriteBatch batch, string key, string val, float x, float y, float w, TextStyle valStyle)
        {
            float kw = mutedStyle.MeasureString(key).X;
            DrawLocalString(batch, key, new Vector2(x, y), mutedStyle);
            var lines = BalloonLayout.Wrap(val, Math.Max(80f, w - kw), s => valStyle.MeasureString(s).X);
            float curY = y;
            foreach (var line in lines.Take(2))
            {
                DrawLocalString(batch, line, new Vector2(x + kw, curY), valStyle);
                curY += 19;
            }
            return curY + 2;
        }

        float DrawWrapped(UISpriteBatch batch, string text, float x, float y, float w, TextStyle style)
        {
            var lines = BalloonLayout.Wrap(text, Math.Max(80f, w), s => style.MeasureString(s).X);
            foreach (var line in lines)
            {
                DrawLocalString(batch, line, new Vector2(x, y), style);
                y += 20;
            }
            return y;
        }

        static string Signed(short v) { return v > 0 ? "+" + v : v.ToString(); }
    }
}
