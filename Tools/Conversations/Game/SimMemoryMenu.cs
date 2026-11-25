using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
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
        Rectangle panelRect, closeRect, newsRect;
        bool showNews;                // the right-hand side shows the news the selected Sim has heard instead of what they know about another Sim

        // the redesigned look: Content/ui/sim_memory.html filled with data (see docs/ui-redesign). Falls back to the hand-drawn window when the page is missing.
        HtmlSurface surface;
        bool htmlDirty = true;
        string htmlKey = "";
        bool UseHtml { get { return surface != null && File.Exists(Path.Combine(Boot.GameDir, "Content", "ui", "sim_memory.html")) && surface.Error == null; } }

        string selectedSpeakerKey = "";
        string selectedSpeakerName = "";
        string selectedListenerKey = "";
        int scrollOffset;
        readonly System.Text.StringBuilder search = new System.Text.StringBuilder();   // typed into the Known Sims search field
        int totalKnown;
        string newsFilter = "all"; bool newsAll;   // the News tab: which stories, and whether all of them are listed
        SimMemorySnapshot snapshot = new SimMemorySnapshot();
        double refreshTimer;
        bool prevLeftDown, prevEscDown;
        int prevScrollWheel;

        public static bool IsOpen { get { return isOpen && instance != null; } }

        // simtool: show the news side (true) or the dossier side (false) of an open menu
        public static string SetNews(bool on) { if (instance == null) return "closed"; instance.showNews = on; instance.htmlDirty = true; return Status(); }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open(null);
        }

        public static string Open(string speakerQuery = null)
        {
            try
            {
                if (SimsOptionsMenu.IsOpen) SimsOptionsMenu.Close();   // one modal at a time (the redesign's rule)
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
            ListenForMouse(new Microsoft.Xna.Framework.Rectangle(0, 0, 4000, 3000), (type, st) => { });   // modal: the world underneath must not get these clicks
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
            surface = new HtmlSurface(this, "sim_memory.html");
            surface.Action += OnPageAction;
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
            htmlDirty = true;
            speakers.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string activeKey = null, activeName = null;
            HashSet<string> familyKeys = null;

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
                    var family = FamilyGuids(screen);
                    foreach (var av in vm.Entities.OfType<VMAvatar>().Where(a => !a.Dead && !a.IsPet && (family != null ? family.Contains(av_guid(a)) : a.GetPersonData(VMPersonDataVariable.PersonsAge) >= 18)))
                    {
                        var k = KeyOf(av);
                        if (seen.Add(k))
                            speakers.Add(new SpeakerIdentity { Key = k, Name = SafeName(av.Name), KnownCount = Boot.Memory.GetKnownSims(k).Count });
                    }
                    if (family != null) familyKeys = new HashSet<string>(speakers.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
                }
            }
            catch { }

            // 2. Add any other Sims present in persisted SocialMemory
            foreach (var sp in Boot.Memory.GetAllSpeakers())
            {
                if (familyKeys != null && !familyKeys.Contains(sp.Key)) continue;   // the household only: other Sims' memories are not shown
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
            totalKnown = snapshot.KnownSims.Count;
            if (search.Length > 0) snapshot.KnownSims.RemoveAll(k => !SimMemoryPageData.NameMatches(k.ListenerName, search.ToString()));
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

        static uint av_guid(VMAvatar a) { try { return a.Object.OBJ.GUID; } catch { return 0; } }

        // GUIDs of the Sims in the household being played (FAMI.FamilyGUIDs of the screen's ActiveFamily); null when it cannot be read
        static HashSet<uint> FamilyGuids(object screen)
        {
            try
            {
                var fam = screen == null ? null : screen.GetType().GetField("ActiveFamily", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(screen);
                if (fam == null) fam = screen.GetType().GetProperty("ActiveFamily", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(screen, null);
                var guids = fam == null ? null : fam.GetType().GetField("FamilyGUIDs").GetValue(fam) as uint[];
                return guids == null || guids.Length == 0 ? null : new HashSet<uint>(guids);
            }
            catch { return null; }
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
                refreshTimer = 0; htmlDirty = true;
                RefreshData(null, false);
            }

            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEscDown)
            {
                prevEscDown = true;
                if (search.Length > 0) { search.Clear(); scrollOffset = 0; RefreshData(null, false); return; }   // Esc clears the search first, then closes
                Close();
                return;
            }
            if (UseHtml && !showNews) TypeIntoSearch(state);
            prevEscDown = esc;

            if (UseHtml)
            {
                float sc = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
                bool pressed = state.MouseState.LeftButton == ButtonState.Pressed;
                if (pressed && !prevLeftDown && !surface.PointInFirstBox(state.MouseState.X / sc, state.MouseState.Y / sc)) { prevLeftDown = true; Close(); return; }
                prevLeftDown = pressed;
                int wheelNow = state.MouseState.ScrollWheelValue;
                if (wheelNow != prevScrollWheel && surface.PointInFirstBox(state.MouseState.X / sc, state.MouseState.Y / sc))
                {
                    // the Known Sims list shows only the rows that fit: the wheel moves the first row shown
                    int max = Math.Max(0, (snapshot == null ? 0 : snapshot.KnownSims.Count) - 1);
                    scrollOffset = wheelNow < prevScrollWheel ? Math.Min(max, scrollOffset + 1) : Math.Max(0, scrollOffset - 1);
                    htmlDirty = true;
                }
                prevScrollWheel = wheelNow;
                surface.Update(state);
                return;
            }

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
                if (newsRect.Width > 0 && newsRect.Contains(mx, my)) { prevLeftDown = true; showNews = !showNews; return; }
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
            if (UseHtml) { DrawHtml(batch); if (UseHtml) return; }
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

            // News / Dossier switch, left of the close button
            int speakerId = SpeakerNeighborId();
            var vmNow = ParallelSocialDriver.CurrentVm;
            var newsList = speakerId > 0 && vmNow != null ? GossipView.NewsOf(Boot.Gossip, speakerId, GossipDirector.Today(vmNow)) : new List<NewsEntry>();
            string newsLabel = showNews ? "Dossier" : "News (" + newsList.Count + ")";
            float nw = Math.Max(78f, bodyStyle.MeasureString(newsLabel).X + 24f);
            newsRect = new Rectangle((int)(px0 + pw - 46 - 10 - nw), (int)(py0 + 14), (int)nw, 28);
            DrawLocalTexture(batch, px, null, new Vector2(newsRect.X, newsRect.Y), new Vector2(newsRect.Width, newsRect.Height), showNews ? new Color(46, 98, 128, 245) : new Color(34, 48, 68, 230));
            DrawLocalString(batch, newsLabel, new Vector2(newsRect.X + 12, newsRect.Y + 4), showNews ? goldStyle : bodyStyle);

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

            if (showNews)
            {
                DrawNews(batch, px, newsList, rightX + 18, bodyY + 14, rightW - 36, bodyH - 30);
                DrawLocalString(batch, "Press Esc or click outside to close", new Vector2(px0 + pw - 245, py0 + ph - 22), mutedStyle);
                return;
            }

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
            string relText = "Daily " + Signed(card.LastStr) + "  |  Lifetime " + Signed(card.LastLtr) + "  (" + card.PositiveCount + " great / " + card.NeutralCount + " ok / " + card.NegativeCount + " awkward)";
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

        List<NewsEntry> CurrentNews()
        {
            int speakerId = SpeakerNeighborId();
            var vmNow = ParallelSocialDriver.CurrentVm;
            return speakerId > 0 && vmNow != null ? GossipView.NewsOf(Boot.Gossip, speakerId, GossipDirector.Today(vmNow)) : new List<NewsEntry>();
        }

        void DrawHtml(UISpriteBatch batch)
        {
            if (surface.Theme != Boot.Theme) { surface.Theme = Boot.Theme; surface.Invalidate(); }
            if (htmlDirty)
            {
                htmlDirty = false;
                var data = SimMemoryPageData.Build(snapshot, speakers, selectedSpeakerKey, selectedListenerKey, showNews, CurrentNews(), selectedSpeakerName, newsFilter, newsAll, search.ToString(), totalKnown);
                var known = data["known"] as List<object>;
                if (known != null && scrollOffset > 0) data["known"] = known.Skip(Math.Min(scrollOffset, known.Count)).ToList();
                string key = Newtonsoft.Json.JsonConvert.SerializeObject(data);
                if (key != htmlKey) { htmlKey = key; surface.SetData(data, key); }
            }
            surface.Draw(batch);
        }

        // keys typed while the Dossier is open go to the search field (the game's own text routine: letters, Backspace, Delete, Ctrl+Backspace)
        void TypeIntoSearch(UpdateState state)
        {
            try
            {
                int before = search.Length; string was = search.ToString();
                var r = state.InputManager.ApplyKeyboardInput(search, state, -1, -1, true);
                // a keyboard that only reports key presses (no text events, like the test hook) still types letters, digits and space
                bool noText = state.FrameTextInput == null || state.FrameTextInput.Count == 0;
                if (noText && search.ToString() == was)
                {
                    bool shift = state.KeyboardState.IsKeyDown(Keys.LeftShift) || state.KeyboardState.IsKeyDown(Keys.RightShift);
                    foreach (var k in state.NewKeys)
                        if ((k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || k == Keys.Space)
                        {
                            char c = FSO.Common.Rendering.Framework.IO.InputManager.TranslateChar(k, shift, state.KeyboardState.CapsLock, false);
                            if (c != 0) search.Append(c);
                        }
                }
                if (r == null) return;
                search.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                if (search.Length > 40) search.Length = 40;
                if (search.ToString() != was) { scrollOffset = 0; htmlDirty = true; RefreshData(null, false); }
            }
            catch (Exception e) { Boot.Log("search typing: " + e.Message); }
        }

        void OnPageAction(string action)
        {
            htmlDirty = true;
            if (action == "close") { Close(); return; }
            if (action == "switch") { showNews = !showNews; return; }
            if (action == "more") { newsAll = true; return; }
            if (action.StartsWith("filter-")) { newsFilter = action.Substring(7); newsAll = false; return; }
            int n;
            if (action.StartsWith("speaker-") && int.TryParse(action.Substring(8), out n) && n < speakers.Count)
            {
                selectedSpeakerKey = speakers[n].Key; selectedSpeakerName = speakers[n].Name;
                selectedListenerKey = ""; scrollOffset = 0; RefreshData(null, false);
            }
            else if (action.StartsWith("pick-") && int.TryParse(action.Substring(5), out n) && n + scrollOffset < snapshot.KnownSims.Count)
                selectedListenerKey = snapshot.KnownSims[n + scrollOffset].ListenerKey;
        }

        // "n:48" -> 48 (the key SocialMemory uses for a Sim with a neighbour number)
        int SpeakerNeighborId()
        {
            int id;
            if (selectedSpeakerKey != null && selectedSpeakerKey.StartsWith("n:") && int.TryParse(selectedSpeakerKey.Substring(2), out id)) return id;
            return 0;
        }

        // the news this Sim has heard or seen: what it was, how sure they are, where it came from, who they passed it on to
        void DrawNews(UISpriteBatch batch, Texture2D px, List<NewsEntry> news, float x, float y, float w, float h)
        {
            DrawLocalString(batch, selectedSpeakerName + "'s news", new Vector2(x, y), titleStyle);
            DrawLocalString(batch, "What " + selectedSpeakerName + " saw or heard about other Sims, newest first", new Vector2(x, y + 26), subtitleStyle);
            float cy = y + 56;
            if (news.Count == 0)
            {
                DrawWrapped(batch, "Nothing yet. News is picked up when something happens in the same room (a promotion, a new skill point, new friends, a kiss, a fight, a gift) and spreads when Sims chat.", x, cy, w, bodyStyle);
                return;
            }
            float bottom = y + h;
            int shown = 0;
            foreach (var n in news)
            {
                var lines = BalloonLayout.Wrap(n.Text, w - 16, s => bodyStyle.MeasureString(s).X);
                float rowH = 10 + lines.Take(2).Count() * 19 + 20;
                if (cy + rowH > bottom) break;
                DrawLocalTexture(batch, px, null, new Vector2(x, cy), new Vector2(w, rowH - 4), new Color(24, 32, 46, 200));
                var accent = n.Fuzzy ? new Color(235, 170, 90, 255) : (n.Certainty == "saw it" ? new Color(255, 224, 120, 255) : new Color(90, 210, 170, 255));
                DrawLocalTexture(batch, px, null, new Vector2(x, cy), new Vector2(4, rowH - 4), accent);
                float ty = cy + 5;
                foreach (var line in lines.Take(2)) { DrawLocalString(batch, line, new Vector2(x + 12, ty), bodyStyle); ty += 19; }
                string meta = n.Detail + (n.Differs ? " | differs from what really happened" : "");
                DrawLocalString(batch, meta, new Vector2(x + 12, ty), n.Differs ? redStyle : mutedStyle);
                cy += rowH; shown++;
            }
            if (shown < news.Count) DrawLocalString(batch, "+ " + (news.Count - shown) + " more", new Vector2(x + 12, cy), mutedStyle);
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
