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
    // Interactive in-game Simitone-styled overlay showing everything each Sim has learned about the Sims they know:
    // personality traits, temperament, career, liked/disliked interests, discovered skills, and what approaches work or fail.
    public class SimMemoryMenu : SimUiElement
    {
        static SimMemoryMenu instance;
        static bool isOpen;

        readonly TextStyle headerTitleStyle, dossierTitleStyle, subtitleStyle, sectionStyle, bodyStyle, mutedStyle, badgeStyle, tabStyle, mintStyle, redStyle, amberStyle;
        readonly List<SpeakerIdentity> speakers = new List<SpeakerIdentity>();
        readonly List<Rectangle> tabRects = new List<Rectangle>();
        readonly List<Rectangle> rowRects = new List<Rectangle>();
        Rectangle panelRect, closeRect;

        string selectedSpeakerKey = "";
        string selectedSpeakerName = "";
        string selectedListenerKey = "";
        int scrollOffset;
        int maxVisibleRows = 6;
        SimMemorySnapshot snapshot = new SimMemorySnapshot();
        double refreshTimer;
        bool prevLeftDown, prevEscDown;
        int prevScrollWheel;
        bool hoverClose;
        int hoverTab = -1;
        int hoverRow = -1;

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
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                isOpen = true;
                instance.Visible = true;
                // Ignore the mouse/key press that triggered Open() so clicking never instantly closes it
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

        public static string OpenForPair(string speakerQuery, string listenerQuery)
        {
            var res = Open(speakerQuery);
            if (!string.IsNullOrWhiteSpace(listenerQuery))
                return SelectKnown(listenerQuery);
            return res;
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
            headerTitleStyle = SimUiTokens.MakeStyle(20, SimUiTokens.DialogTitle, false);
            dossierTitleStyle = SimUiTokens.MakeStyle(19, SimUiTokens.TextPrimary, true);
            subtitleStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextMuted, true);
            sectionStyle = SimUiTokens.MakeStyle(12, SimUiTokens.Cyan, true);
            bodyStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            mutedStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextMuted, true);
            badgeStyle = SimUiTokens.MakeStyle(12, SimUiTokens.TextPrimary, true);
            tabStyle = SimUiTokens.MakeStyle(13, SimUiTokens.TextPrimary, true);
            mintStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Mint, true);
            redStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Danger, true);
            amberStyle = SimUiTokens.MakeStyle(13, SimUiTokens.Amber, true);
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
                var active = screen?.GetType().GetProperty("SelectedAvatar")?.GetValue(screen, null) as VMAvatar;
                if (active == null)
                {
                    var lot = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                    active = lot?.GetType().GetProperty("ActiveEntity")?.GetValue(lot, null) as VMAvatar;
                }
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

            hoverClose = closeRect.Contains(mx, my);
            hoverTab = -1;
            for (int i = 0; i < tabRects.Count; i++)
                if (tabRects[i].Contains(mx, my)) { hoverTab = i; break; }
            hoverRow = -1;
            for (int i = 0; i < rowRects.Count; i++)
                if (rowRects[i].Contains(mx, my)) { hoverRow = i; break; }

            int wheel = state.MouseState.ScrollWheelValue;
            if (wheel != prevScrollWheel && panelRect.Contains(mx, my))
            {
                int maxScroll = Math.Max(0, snapshot.KnownSims.Count - Math.Max(1, maxVisibleRows));
                if (wheel < prevScrollWheel) scrollOffset = Math.Min(maxScroll, scrollOffset + 1);
                else scrollOffset = Math.Max(0, scrollOffset - 1);
            }
            prevScrollWheel = wheel;

            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeftDown)
            {
                if (hoverClose || (panelRect.Width > 0 && !panelRect.Contains(mx, my)))
                {
                    prevLeftDown = true;
                    Close();
                    return;
                }
                if (hoverTab >= 0 && hoverTab < speakers.Count)
                {
                    selectedSpeakerKey = speakers[hoverTab].Key;
                    selectedSpeakerName = speakers[hoverTab].Name;
                    selectedListenerKey = "";
                    scrollOffset = 0;
                    RefreshData(null, false);
                }
                else if (hoverRow >= 0)
                {
                    int idx = scrollOffset + hoverRow;
                    if (idx < snapshot.KnownSims.Count)
                        selectedListenerKey = snapshot.KnownSims[idx].ListenerKey;
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

            var layout = SimMemoryLayout.Compute(screenW, screenH);
            maxVisibleRows = layout.MaxVisibleRows;
            panelRect = ToXnaRect(layout.Panel);
            closeRect = ToXnaRect(layout.CloseBtn);

            // 1. Native Simitone modal backdrop + gradient title bar + close pill
            DrawDialogShell(batch, layout.Panel, true, screenW, screenH);
            DrawTitleBar(batch, layout.HeaderBar, layout.TitleRect, snapshot.SpeakerName + "'s Sim Memory", headerTitleStyle);
            DrawPillButton(batch, layout.CloseBtn, "X", SimButtonVariant.Close, hoverClose);

            // 2. Subtitle + Speaker switcher pills
            var sub = snapshot.KnownSims.Count == 1
                ? "What " + snapshot.SpeakerName + " has learned about 1 Sim"
                : "What " + snapshot.SpeakerName + " has learned about " + snapshot.KnownSims.Count + " Sims";
            DrawFittedLine(batch, sub, layout.SubtitleRect, subtitleStyle, SimTextAlign.Left);

            tabRects.Clear();
            float tx = layout.TabStripRect.X;
            float tabMaxRight = layout.TabStripRect.Right;
            for (int i = 0; i < speakers.Count && i < 6; i++)
            {
                var sp = speakers[i];
                bool sel = string.Equals(sp.Key, selectedSpeakerKey, StringComparison.OrdinalIgnoreCase);
                string label = sp.Name + " (" + sp.KnownCount + ")";
                float tw = SimLayout.Clamp(tabStyle.MeasureString(label).X + 26f, 84f, 180f);
                if (tx + tw > tabMaxRight) break;
                var tabSimRect = new SimRect(tx, layout.TabStripRect.Y, tw, layout.TabStripRect.Height);
                tabRects.Add(ToXnaRect(tabSimRect));

                if (sel)
                {
                    DrawPillButton(batch, tabSimRect, label, SimButtonVariant.Primary, hoverTab == i);
                }
                else
                {
                    Color bg = hoverTab == i ? SimUiTokens.RowSelectedBg : SimUiTokens.PillDarkBg;
                    DrawPillBadge(batch, tabSimRect, label, bg, tabStyle);
                }
                tx += tw + 8f;
            }

            // 3. Left column: Known Sims glass card
            DrawGlassCard(batch, layout.LeftColumn, SimUiTokens.CardBg, SimUiTokens.TealHighlight * 0.75f);
            DrawFittedLine(batch, "KNOWN SIMS", layout.LeftHeaderRect, sectionStyle, SimTextAlign.Left);

            rowRects.Clear();
            if (snapshot.KnownSims.Count == 0)
            {
                DrawFittedWrapped(batch, "No learned memories yet.", layout.LeftListRect.Deflate(4f, 4f), mutedStyle, 3);
            }
            else
            {
                int maxScroll = Math.Max(0, snapshot.KnownSims.Count - layout.MaxVisibleRows);
                if (scrollOffset > maxScroll) scrollOffset = maxScroll;

                for (int i = 0; i < layout.MaxVisibleRows && scrollOffset + i < snapshot.KnownSims.Count; i++)
                {
                    var item = snapshot.KnownSims[scrollOffset + i];
                    bool sel = string.Equals(item.ListenerKey, selectedListenerKey, StringComparison.OrdinalIgnoreCase);
                    bool hov = hoverRow == i;
                    var r = layout.GetRowRect(i);
                    rowRects.Add(ToXnaRect(r));

                    Color rowBg = sel ? SimUiTokens.RowSelectedBg : (hov ? SimUiTokens.CardBgAlt : SimUiTokens.RowBg);
                    FillRoundedRect(batch, r, SimUiTokens.RadiusControl, rowBg);
                    DrawRoundedBorder(batch, r, SimUiTokens.RadiusControl, sel ? SimUiTokens.Mint * 0.75f : SimUiTokens.InnerBorder * 0.6f, 1);
                    if (sel) FillRoundedRect(batch, new SimRect(r.X + 3f, r.Y + 7f, 4f, Math.Max(8f, r.Height - 14f)), 2, SimUiTokens.Mint);

                    var nameRect = new SimRect(r.X + 12f, r.Y + 5f, r.Width - 20f, 20f);
                    var metaRect = new SimRect(r.X + 12f, r.Y + 25f, r.Width - 20f, 18f);
                    DrawFittedLine(batch, item.ListenerName, nameRect, sel ? mintStyle : bodyStyle, SimTextAlign.Left);
                    string meta = item.FamiliarityLabel + " • " + item.KnowledgePercent + "% • " + item.Exchanges + (item.Exchanges == 1 ? " chat" : " chats");
                    DrawFittedLine(batch, meta, metaRect, mutedStyle, SimTextAlign.Left);
                }
            }

            // 4. Right column: Selected Sim dossier glass card
            DrawGlassCard(batch, layout.RightColumn, SimUiTokens.CardBgAlt, SimUiTokens.Mint * 0.65f);

            var card = CurrentCard();
            if (card == null)
            {
                var emptyInner = layout.RightColumn.Deflate(20f, 20f);
                DrawFittedLine(batch, "Nothing learned yet", new SimRect(emptyInner.X, emptyInner.Y, emptyInner.Width, 26f), dossierTitleStyle, SimTextAlign.Left);
                DrawFittedWrapped(batch,
                    snapshot.SpeakerName + " hasn't talked with anyone yet. Use Talk or let Sims chat autonomously in the same room — every conversation records traits, interests, skills, temperament, and what approaches work best.",
                    new SimRect(emptyInner.X, emptyInner.Y + 34f, emptyInner.Width, emptyInner.Height - 40f),
                    bodyStyle, 6);
                DrawFittedLine(batch, "Press Esc or click outside to close", layout.FooterRect, mutedStyle, SimTextAlign.Right);
                return;
            }

            // Dossier Header + Authentic Simitone Motive Bar for Familiarity
            DrawFittedLine(batch, card.ListenerName, layout.DossierNameRect, dossierTitleStyle, SimTextAlign.Left);
            string relText = "Daily " + Signed(card.LastStr) + "  •  Lifetime " + Signed(card.LastLtr) +
                             "  (" + card.PositiveCount + " great / " + card.NeutralCount + " ok / " + card.NegativeCount + " awkward)";
            DrawFittedLine(batch, relText, layout.DossierRelRect, subtitleStyle, SimTextAlign.Left);

            string famLabel = card.FamiliarityLabel + " (" + card.KnowledgePercent + "%)";
            DrawFittedLine(batch, famLabel, layout.FamiliarityLabelRect, badgeStyle,
                layout.StackedDossierHeader ? SimTextAlign.Left : SimTextAlign.Right);
            DrawMotiveBar(batch, layout.FamiliarityBarRect, card.KnowledgePercent / 100f, card.IsTense);

            // Clamped Dossier Body (strictly bounded by layout.DossierBodyRect.Bottom)
            float cx = layout.DossierBodyRect.X;
            float cw = layout.DossierBodyRect.Width;
            float cy = layout.DossierBodyRect.Y;
            float maxY = layout.DossierBodyRect.Bottom;

            if (card.IsTense && cy + 28f <= maxY)
            {
                var tenseRect = new SimRect(cx, cy, cw, 24f);
                FillRoundedRect(batch, tenseRect, SimUiTokens.RadiusControl, SimUiTokens.DangerBg);
                DrawRoundedBorder(batch, tenseRect, SimUiTokens.RadiusControl, SimUiTokens.Danger * 0.7f, 1);
                DrawFittedLine(batch, "Recent exchanges were tense — favoring gentle or apologetic approaches.",
                    new SimRect(cx + 8f, cy, cw - 16f, 24f), redStyle, SimTextAlign.Left);
                cy += 30f;
            }

            // Section 1: Personality, Temperament & Career
            cy = DrawSectionHeader(batch, px, "PERSONALITY & PROFILE", cx, cy, cw, maxY);
            string traitsLine = card.Traits.Count > 0 ? string.Join(", ", card.Traits) : "Not observed yet (chat more to reveal)";
            cy = DrawKeyValue(batch, "Traits: ", traitsLine, cx, cy, cw, maxY, bodyStyle);
            string temperLine = !string.IsNullOrEmpty(card.Temperament) ? card.Temperament : "Still reading social cues";
            cy = DrawKeyValue(batch, "Temperament: ", temperLine, cx, cy, cw, maxY, amberStyle);
            if (!string.IsNullOrEmpty(card.Career))
                cy = DrawKeyValue(batch, "Career: ", card.Career, cx, cy, cw, maxY, bodyStyle);
            cy += 6f;

            // Section 2: Interests & Skills Learned
            cy = DrawSectionHeader(batch, px, "INTERESTS & SKILLS LEARNED", cx, cy, cw, maxY);
            if (card.LikedInterests.Count == 0 && card.NeutralInterests.Count == 0 && card.DislikedInterests.Count == 0)
            {
                cy = DrawKeyValue(batch, "Interests: ", "None discovered yet", cx, cy, cw, maxY, mutedStyle);
            }
            else
            {
                if (card.LikedInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Likes: ", string.Join(", ", card.LikedInterests), cx, cy, cw, maxY, mintStyle);
                if (card.NeutralInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Neutral: ", string.Join(", ", card.NeutralInterests), cx, cy, cw, maxY, bodyStyle);
                if (card.DislikedInterests.Count > 0)
                    cy = DrawKeyValue(batch, "Dislikes: ", string.Join(", ", card.DislikedInterests), cx, cy, cw, maxY, redStyle);
            }
            string skillsLine = card.Skills.Count > 0 ? string.Join(", ", card.Skills) : "None discovered yet";
            cy = DrawKeyValue(batch, "Skills: ", skillsLine, cx, cy, cw, maxY, card.Skills.Count > 0 ? amberStyle : mutedStyle);
            cy += 6f;

            // Section 3: Conversation Lessons (only drawn when space permits)
            if (cy + 42f <= maxY)
            {
                cy = DrawSectionHeader(batch, px, "CONVERSATION LESSONS", cx, cy, cw, maxY);
                var works = new List<string>();
                if (card.WorkedInteractions.Count > 0) works.Add(string.Join(", ", card.WorkedInteractions));
                if (card.WorkedTones.Count > 0) works.Add(string.Join(", ", card.WorkedTones) + " tone");
                if (works.Count > 0)
                    cy = DrawKeyValue(batch, "Works well: ", string.Join(" • ", works), cx, cy, cw, maxY, mintStyle);

                var avoid = new List<string>();
                if (card.AvoidInteractions.Count > 0) avoid.Add(string.Join(", ", card.AvoidInteractions));
                if (card.AvoidTones.Count > 0) avoid.Add(string.Join(", ", card.AvoidTones) + " tone");
                if (avoid.Count > 0)
                    cy = DrawKeyValue(batch, "Avoid: ", string.Join(" • ", avoid), cx, cy, cw, maxY, redStyle);

                if (card.Topics.Count > 0)
                {
                    string topicStr = string.Join(" • ", card.Topics.Take(4).Select(t => t.Title + " (" + t.OutcomeLabel + ")"));
                    cy = DrawKeyValue(batch, "Topics: ", topicStr, cx, cy, cw, maxY, bodyStyle);
                }
            }

            // 5. Reserved footer strip (measured right-alignment — never overlaps!)
            DrawFittedLine(batch, "Press Esc or click outside to close", layout.FooterRect, mutedStyle, SimTextAlign.Right);
        }

        float DrawSectionHeader(UISpriteBatch batch, Texture2D px, string label, float x, float y, float w, float maxY)
        {
            if (y + 22f > maxY) return maxY;
            DrawFittedLine(batch, label, new SimRect(x, y, w, 17f), sectionStyle, SimTextAlign.Left);
            FillRect(batch, px, x, y + 18f, w, 1f, SimUiTokens.Divider);
            return y + 23f;
        }

        float DrawKeyValue(UISpriteBatch batch, string key, string val, float x, float y, float w, float maxY, TextStyle valStyle)
        {
            float lineH = Math.Max(18f, valStyle.MeasureString("Ag").Y + 2f);
            if (y + lineH > maxY) return maxY;

            float kw = Math.Min(w * 0.42f, mutedStyle.MeasureString(key).X + 4f);
            DrawFittedLine(batch, key, new SimRect(x, y, kw, lineH), mutedStyle, SimTextAlign.Left);

            float valW = Math.Max(40f, w - kw);
            float availH = maxY - y;
            int maxLines = Math.Min(2, Math.Max(1, (int)Math.Floor((availH + 2f) / lineH)));
            float endY = DrawFittedWrapped(batch, val, new SimRect(x + kw, y, valW, maxLines * lineH + 2f), valStyle, maxLines);
            return Math.Max(y + lineH, endY) + 2f;
        }

        static string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }
    }
}
