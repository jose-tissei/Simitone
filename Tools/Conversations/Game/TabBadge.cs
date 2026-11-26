using System;
using System.Collections.Generic;
using System.Reflection;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using Microsoft.Xna.Framework;
using Simitone.HtmlUi;

namespace Simitone.Conversations
{
    // The unread badge on the Memory tab of the Live panel's tab rail (docs/ui-redesign screen map: "labelled and shorter tab rail with unread
    // badge on Memory"). The rail is the game's own control: this overlay finds the Memory button through the rail (UICategorySwitcher) and
    // draws a small count over it: on the button in the open rail, and on the rail's main button while another tab is showing.
    // "Unread" = News stories of the Sim being played that the News tab has not shown yet.
    public class TabBadge : UIElement
    {
        const int MemoryCategory = 5;
        static TabBadge instance;
        static readonly Dictionary<int, int> seen = new Dictionary<int, int>();   // neighbour id -> stories the News tab has shown
        public static string LastState = "idle";

        HtmlSurface painter;
        Dictionary<string, string> tokens;
        int unread; long nextCount;

        public static void MarkSeen(int simId, int total) { if (simId > 0) lock (seen) seen[simId] = total; }

        static int SeenOf(int simId) { int n; lock (seen) return seen.TryGetValue(simId, out n) ? n : 0; }

        // from Boot.OnTick: keeps the overlay on the current screen
        public static void Ensure()
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null) instance = new TabBadge();
                if (instance.Parent != screen) { if (instance.Parent != null) instance.Parent.Remove(instance); screen.Add(instance); }
                else if (!IsLast(screen)) { screen.Remove(instance); screen.Add(instance); }   // the game adds its panels later: stay on top of them
            }
            catch (Exception e) { Boot.Log("tab badge: " + e.Message); }
        }

        static PropertyInfo childrenProp;

        static bool IsLast(UIContainer screen)
        {
            if (childrenProp == null) childrenProp = typeof(UIContainer).GetProperty("Children", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var kids = childrenProp == null ? null : childrenProp.GetValue(screen, null) as System.Collections.IList;
            return kids == null || kids.Count == 0 || ReferenceEquals(kids[kids.Count - 1], instance);
        }

        static object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name); if (p != null) return p.GetValue(o, null);
            var f = o.GetType().GetField(name); return f != null ? f.GetValue(o) : null;
        }

        static object Switcher()
        {
            var screen = UIScreen.Current;
            var frontend = Prop(screen, "Frontend");
            var main = Prop(frontend, "MainPanel") ?? Prop(screen, "MainPanel");
            return Prop(main, "Switcher");
        }

        static VMAvatar ActiveAvatar()
        {
            var screen = UIScreen.Current;
            var lot = Prop(screen, "LotControl") ?? Prop(Prop(screen, "Frontend"), "LotControl");
            return Prop(lot, "ActiveEntity") as VMAvatar;
        }

        void RefreshCount()
        {
            unread = 0;
            var av = ActiveAvatar(); var vm = ParallelSocialDriver.CurrentVm;
            if (av == null || vm == null || !Boot.GossipEnabled) return;
            int id = av.GetPersonData(VMPersonDataVariable.NeighborId);
            if (id <= 0) return;
            unread = TabBadgeLogic.Unread(GossipView.NewsOf(Boot.Gossip, id, GossipDirector.Today(vm)).Count, SeenOf(id));
        }

        // where a control sits on the screen: its own position plus its parents' (they are not scaled)
        static Vector2 Absolute(UIElement e)
        {
            var p = Vector2.Zero;
            for (UIElement c = e; c != null && !(c is UIScreen); c = c.Parent as UIElement) p += c.Position;
            return p;
        }

        public override void Draw(UISpriteBatch batch)
        {
            LastState = "idle";
            try
            {
                if (!Boot.Enabled || SimMemoryMenu.IsOpen || TalkSheetMenu.IsOpen || SimsOptionsMenu.IsOpen) { LastState = "hidden (modal or off)"; return; }
                if (--nextCount <= 0) { nextCount = 30; RefreshCount(); }
                if (unread <= 0) { LastState = "no unread"; return; }
                var sw = Switcher(); if (sw == null) { LastState = "no switcher"; return; }
                var cats = Prop(sw, "Categories") as System.Collections.IList;
                var btns = Prop(sw, "CatSwitchButtons") as System.Collections.IList;
                var main = Prop(sw, "MainButton") as UIElement;
                if (cats == null || btns == null || main == null) { LastState = "rail not ready"; return; }
                int active = (int)Prop(sw, "ActiveCategory");
                int expandIdx = -1;
                for (int i = 0, k = 0; i < cats.Count; i++)
                {
                    int id = (int)Prop(cats[i], "ID");
                    if (id == MemoryCategory) { if (k < btns.Count) expandIdx = k; break; }
                    if (id != active || UIScreen.Current.ScreenHeight >= 720) k++;
                }
                if (painter == null) { painter = new HtmlSurface(this, "badge.none"); LoadTokens(); }
                painter.BeginDraw(batch);
                var expanded = (float)Prop(sw, "CategoryExpand") > 0.9f;
                if (expanded && expandIdx >= 0) { var b = (UIElement)btns[expandIdx]; if (b.Visible) { Paint(Absolute(b) + new Vector2(44, -2)); LastState = "badge on the Memory tab"; } }
                else if (active != MemoryCategory) { Paint(Absolute(main) + new Vector2(44, -2)); LastState = "badge on the rail button"; }
            }
            catch (Exception e) { LastState = "error: " + e.Message; }
        }

        void Paint(Vector2 at)
        {
            string t = TabBadgeLogic.Label(unread);
            float tw, th; painter.MeasureText(t, 16, true, out tw, out th);
            float w = Math.Max(26, tw + 14), h = 26;
            painter.FillRect(at.X - 2, at.Y - 2, w + 4, h + 4, Token("panel", new Rgba(10, 20, 24)), (h + 4) / 2);   // ring, so it reads on any icon
            painter.FillRect(at.X, at.Y, w, h, Token("bad", new Rgba(255, 90, 90)), h / 2);
            painter.DrawText(t, at.X + (w - tw) / 2, at.Y + (h - th) / 2, 16, true, new Rgba(255, 255, 255));
        }

        Rgba Token(string name, Rgba fallback)
        {
            string v;
            return tokens != null && tokens.TryGetValue(name, out v) ? HtmlView.ParseColor(v) : fallback;
        }

        void LoadTokens()
        {
            try { tokens = ThemeTokens.Build(System.IO.File.ReadAllText(System.IO.Path.Combine(Boot.GameDir, "Content", "ui", "design-tokens.json")), "night"); }
            catch { tokens = null; }
        }
    }
}
