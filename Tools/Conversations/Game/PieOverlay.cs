using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using Simitone.HtmlUi;

namespace Simitone.Conversations
{
    // The redesigned pie menu. The game's own UIPieMenu stays alive underneath and keeps doing everything (hover, categories, the back
    // button, sounds, sending the interaction, closing); this element only draws the design's slots on top of the native buttons, at the
    // same places and at least as large, so a click lands on the native button exactly as before. If anything about the native menu
    // is not as expected the overlay draws nothing and the native pie shows as usual.
    public class PieOverlay : UIElement
    {
        static PieOverlay instance;
        public static bool Enabled = true;      // `simtool convo pie native|new`
        public static string LastState = "idle";

        HtmlSurface painter;
        Dictionary<string, string> tokens;
        FieldInfo pieField, buttonsField, currentField;
        PropertyInfo childrenProp, nameProp, parentProp;
        float mouseX, mouseY;
        const float IconSize = 20, TextSize = 18;

        // called from the game tick (Boot.OnTick): makes sure the overlay is on the current screen
        public static void Ensure()
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null) instance = new PieOverlay();
                if (instance.Parent != screen) { if (instance.Parent != null) instance.Parent.Remove(instance); screen.Add(instance); }
            }
            catch (Exception e) { Boot.Log("pie overlay: " + e.Message); }
        }

        // the live native pie menu (UIPieMenu) or null
        object NativePie()
        {
            var screen = UIScreen.Current;
            object lot = Prop(screen, "LotControl");
            if (lot == null) { var fe = Prop(screen, "Frontend"); lot = Prop(fe, "LotControl"); }
            if (lot == null) return null;
            if (pieField == null) pieField = lot.GetType().GetField("PieMenu", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return pieField == null ? null : pieField.GetValue(lot);
        }

        static object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name); if (p != null) return p.GetValue(o, null);
            var f = o.GetType().GetField(name); return f != null ? f.GetValue(o) : null;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            float sc = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            mouseX = state.MouseState.X / sc; mouseY = state.MouseState.Y / sc;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Enabled) { LastState = "native"; return; }
            try
            {
                var pie = NativePie() as UIElement;
                if (pie == null) { LastState = "idle"; return; }
                if (painter == null) { painter = new HtmlSurface(this, "pie.none"); LoadTokens(); }
                if (buttonsField == null)
                {
                    buttonsField = pie.GetType().GetField("m_PieButtons");
                    currentField = pie.GetType().GetField("m_CurrentItem");
                }
                var buttons = buttonsField == null ? null : buttonsField.GetValue(pie) as IList;
                var current = currentField == null ? null : currentField.GetValue(pie);
                if (buttons == null || current == null) { LastState = "native (unexpected pie)"; return; }
                string currentName = Str(current, "Name");
                bool hasParent = Prop(current, "Parent") != null;

                painter.BeginDraw(batch);
                if (buttons.Count == 0) { LastState = "new slots=0"; return; }
                var first = buttons[0] as UIButton;
                if (first == null) { LastState = "native (unexpected button)"; return; }
                if (!ReferenceEquals(first, laidOut) || buttons.Count != laidCount) { Arrange(buttons, hasParent); laidOut = first; laidCount = buttons.Count; }
                int drawn = 0;
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i] as UIButton;
                    if (b == null) continue;
                    bool back = hasParent && i == buttons.Count - 1;
                    DrawSlot(pie.X + b.X, pie.Y + b.Y, b.Width, b.Size.Y, b.Caption, back);
                    drawn++;
                }
                LastState = "new slots=" + drawn;
            }
            catch (Exception e) { LastState = "error " + e.Message; }
        }

        static string Str(object o, string name) { var v = Prop(o, name); return v == null ? "" : v.ToString(); }

        object laidOut; int laidCount;
        const float SlotH = 36, Gutter = 10, Hub = 54;

        // Moves the native buttons into the harmonious layout (PieStyle.Layout) and gives them one common width, so the click areas are the slots we draw.
        void Arrange(IList buttons, bool hasParent)
        {
            int n = buttons.Count - (hasParent ? 1 : 0);
            float w = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i] as UIButton; if (b == null) continue;
                bool back = hasParent && i == buttons.Count - 1;
                float tw, th; painter.MeasureText(back ? PieStyle.CleanLabel(b.Caption) : PieStyle.ShortLabel(b.Caption), TextSize, false, out tw, out th);
                w = Math.Max(w, PieStyle.SlotWidth(tw, IconSize, 0, !back && PieStyle.IsSubmenu(b.Caption)));
            }
            w = (float)Math.Ceiling(Math.Max(w, 120));
            float[] cx, cy; float bx, by;
            PieStyle.Layout(n, hasParent, w, SlotH, Hub, Gutter, out cx, out cy, out bx, out by);
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i] as UIButton; if (b == null) continue;
                b.Width = w;
                bool back = hasParent && i == buttons.Count - 1;
                float x = back ? bx : cx[i], y = back ? by : cy[i];
                b.X = x - w / 2; b.Y = y - b.Size.Y / 2;
            }
        }

        void DrawSlot(float nx, float ny, float nw, float nh, string caption, bool back)
        {
            bool sub = !back && PieStyle.IsSubmenu(caption);
            string full = PieStyle.CleanLabel(caption);
            string label = back ? full : PieStyle.ShortLabel(caption);
            float tw, th; painter.MeasureText(label, TextSize, false, out tw, out th);
            float w = nw, h = SlotH;
            float x = nx, y = ny + nh / 2 - h / 2;
            bool hover = mouseX >= nx && mouseX <= nx + nw && mouseY >= ny && mouseY <= ny + nh;

            var a = Token(hover ? "info" : "pie-a", new Rgba(38, 88, 200)); var bcol = Token("pie-b", new Rgba(18, 58, 143));
            var edge = Token("pie-edge", new Rgba(10, 37, 97)); var rim = Token("pie-rim", new Rgba(255, 255, 255));
            painter.FillRect(x - 1, y - 1, w + 2, h + 2, hover ? rim : edge, h / 2 + 1);   // edge ring (white when hovered, so it is not colour alone: it also grows)
            painter.FillGradient(x, y, w, h, a, bcol, false, h / 2);
            float cx = x + 12;
            painter.DrawIcon(Icons.Find(back ? "left" : PieStyle.IconFor(full)), cx, y + (h - IconSize) / 2, IconSize, rim);
            painter.DrawText(label, cx + IconSize + 8, y + (h - th) / 2, TextSize, false, rim);
            if (sub) painter.DrawIcon(Icons.Find("right"), x + w - 12 - 14, y + (h - 14) / 2, 14, rim);   // "..." marks a submenu
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
