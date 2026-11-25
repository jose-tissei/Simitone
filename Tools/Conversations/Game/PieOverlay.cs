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
                screenW = batch.GraphicsDevice.Viewport.Width; screenH = batch.GraphicsDevice.Viewport.Height;
                if (buttons.Count == 0) { LastState = "new slots=0"; return; }
                var first = buttons[0] as UIButton;
                if (first == null) { LastState = "native (unexpected button)"; return; }
                if (!ReferenceEquals(first, laidOut) || buttons.Count != laidCount) { Arrange(buttons, hasParent); laidOut = first; laidCount = buttons.Count; }
                originY = pie.Y;
                DrawOrbit(pie.X, pie.Y, hasParent);
                hoverCaption = null;
                int drawn = 0;
                for (int i = 0; i < buttons.Count; i++)
                {
                    var b = buttons[i] as UIButton;
                    if (b == null) continue;
                    bool back = hasParent && i == buttons.Count - 1;
                    string cap; if (!captions.TryGetValue(b, out cap)) cap = b.Caption;
                    DrawSlot(pie.X + b.X, pie.Y + b.Y, b.Width, b.Size.Y, cap, back);
                    drawn++;
                }
                if (hoverCaption != null) DrawTooltip(hoverCaption, TargetName(pie), hoverX, hoverY, hoverW, hoverH);
                LastState = "new slots=" + drawn;
            }
            catch (Exception e) { LastState = "error " + e.Message; }
        }

        static string Str(object o, string name) { var v = Prop(o, name); return v == null ? "" : v.ToString(); }

        object laidOut; int laidCount;
        readonly Dictionary<object, string> captions = new Dictionary<object, string>();   // the native captions, which we blank so long ones do not poke out past our slots
        const float SlotH = 48;
        float ringRy = 128;

        // Moves the native buttons onto the design's orbit around the head (PieStyle.Orbit) and sizes each to its label, so the click areas are the slots we draw.
        void Arrange(IList buttons, bool hasParent)
        {
            int n = buttons.Count - (hasParent ? 1 : 0);
            var widths = new float[n]; float backW = 0; int k = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i] as UIButton; if (b == null) continue;
                bool back = hasParent && i == buttons.Count - 1;
                string cap = b.Caption;
                captions[b] = cap;
                float tw, th; painter.MeasureText(back ? PieStyle.CleanLabel(cap) : PieStyle.ShortLabel(cap), TextSize, false, out tw, out th);
                float w = (float)Math.Ceiling(Math.Max(PieStyle.SlotWidth(tw, IconSize, 0, !back && PieStyle.IsSubmenu(cap)), 96));
                b.Width = w;
                b.Caption = "";
                if (back) backW = w; else widths[k++] = w;
            }
            float[] cx, cy;
            PieStyle.Orbit(widths, SlotH, hasParent, backW, 235, 128, 10, out cx, out cy);
            ringRy = n > 0 ? -cy[0] : 128;
            k = 0;
            for (int i = 0; i < buttons.Count; i++)
            {
                var b = buttons[i] as UIButton; if (b == null) continue;
                bool back = hasParent && i == buttons.Count - 1;
                float x = back ? 0 : cx[k], y = back ? 0 : cy[k];
                if (!back) k++;
                b.X = x - b.Width / 2; b.Y = y - b.Size.Y / 2;
            }
        }

        // the design's dashed ellipse through the slots
        void DrawOrbit(float ox, float oy, bool hasBack)
        {
            float ry = ringRy, rx = ry * 235f / 128f;
            var dash = new Rgba(255, 255, 255, 150);
            // dashes made of round dots (anti-aliased by the surface), 4 px apart along the curve: three dots on, two off
            double prevX = rx, prevY = 0, acc = 0; int idx = 0;
            for (int i = 1; i <= 1440; i++)
            {
                double a = i * Math.PI / 720, x = Math.Cos(a) * rx, y = Math.Sin(a) * ry;
                acc += Math.Sqrt((x - prevX) * (x - prevX) + (y - prevY) * (y - prevY)); prevX = x; prevY = y;
                if (acc < 4) continue;
                acc -= 4; idx++;
                if (idx % 5 < 3) painter.FillRect(ox + (float)x - 2, oy + (float)y - 2, 4, 4, dash, 2);
            }
        }

        float originY;
        float screenW = 1920, screenH = 1080;
        string hoverCaption; float hoverX, hoverY, hoverW, hoverH;

        // name of the Sim under the pie ("" for objects)
        static string TargetName(UIElement pie)
        {
            var obj = Prop(pie, "m_Obj");
            if (obj == null || obj.GetType().Name.IndexOf("Avatar", StringComparison.OrdinalIgnoreCase) < 0) return "";
            return Str(obj, "Name");
        }

        // the design's tooltip: dark glass, 16 px text, under the hovered slot and kept 32 px inside the screen
        void DrawTooltip(string caption, string who, float sx, float sy, float sw, float sh)
        {
            string text = PieStyle.Tooltip(caption, who);
            if (string.IsNullOrEmpty(text)) return;
            float tw, th; painter.MeasureText(text, 16, false, out tw, out th);
            float w = tw + 28, h = th + 16;
            float sc = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float vw = screenW / sc, vh = screenH / sc;
            float x = Math.Max(32, Math.Min(sx + sw / 2 - w / 2, vw - 32 - w)), y = sy + sh / 2 < originY ? Math.Max(32, sy - h - 14) : Math.Min(sy + sh + 14, vh - 32 - h);   // above slots in the top half, below the others, so it stays out of the ring
            painter.FillRect(x - 2, y - 2, w + 4, h + 4, new Rgba(255, 255, 255, 36), 14);
            painter.FillRect(x, y, w, h, Token("balloon-bg", new Rgba(10, 20, 24, 242)), 12);
            painter.DrawText(text, x + 14, y + 8, 16, false, Token("text", new Rgba(234, 248, 246)));
        }

        void DrawSlot(float nx, float ny, float nw, float nh, string caption, bool back)
        {
            bool sub = !back && PieStyle.IsSubmenu(caption);
            string full = PieStyle.CleanLabel(caption);
            string label = back ? full : PieStyle.ShortLabel(caption);
            float tw, th; painter.MeasureText(label, TextSize, false, out tw, out th);
            bool hover = mouseX >= nx && mouseX <= nx + nw && mouseY >= ny && mouseY <= ny + nh;
            float grow = hover ? 1.08f : 1f;                      // the design's "on" slot is scaled up 8%
            float w = nw * grow, h = SlotH * grow;
            float x = nx + nw / 2 - w / 2, y = ny + nh / 2 - h / 2;

            var white = Token("pie-rim", new Rgba(255, 255, 255));
            var fa = Token("pie-a", new Rgba(38, 88, 200)); var fb = Token("pie-b", new Rgba(18, 58, 143));
            var lip = Token("pie-edge", new Rgba(10, 37, 97));
            var ink = white;
            float lipH = 4, lift = 0;
            if (hover) { hoverCaption = back ? null : caption; hoverX = x; hoverY = y; hoverW = w; hoverH = h; }
            if (hover) { fa = new Rgba(180, 255, 230); fb = Token("accent-2", new Rgba(0, 217, 139)); ink = Token("on-accent", new Rgba(3, 48, 42)); lipH = 7; lift = 3; }   // mint "on" slot: lifted, so its lip shows more instead of the design's 1.5 degree tilt (which looks jagged here)
            y -= lift;
            // the design's slot: soft drop shadow, a hard lip under the pill, a 3 px white border, then the blue (or mint) face
            painter.FillRect(x + 2, y + lipH + 3, w - 4, h, new Rgba(0, 0, 0, 40), h / 2);
            painter.FillRect(x + 5, y + lipH + 6, w - 10, h, new Rgba(0, 0, 0, 30), h / 2);
            painter.FillRect(x, y + lipH, w, h, lip, h / 2);
            painter.FillRect(x, y, w, h, white, h / 2);
            painter.FillGradient(x + 3, y + 3, w - 6, h - 6, fa, fb, false, h / 2 - 3);
            float cx = x + 14 * grow;
            painter.DrawIcon(Icons.Find(back ? "left" : PieStyle.IconFor(full)), cx, y + (h - IconSize) / 2, IconSize, ink);
            painter.DrawText(label, cx + IconSize + 10, y + (h - th) / 2, TextSize, false, ink);
            if (sub) painter.DrawIcon(Icons.Find("right"), x + w - 14 - 14, y + (h - 14) / 2, 14, ink);   // "..." marks a submenu
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
