using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.SimAntics;
using Simitone.HtmlUi;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Conversations
{
    // The two lines of a conversation as speech balloons over the Sims' heads, for a few seconds:
    //   Bob:    "Come here, Betty."
    //   Betty:  (a moment later) "Aww, thanks Bob."
    // The Simitone client's own balloons only show pictures, so this is a screen-space element added to the current screen.
    // The balloon follows the Sim (same screen mapping the game's chat balloons use). When a Sim's position cannot be
    // worked out (no lot view, a Sim that left the lot) that line falls back to a small strip at the bottom of the screen.
    public class ConvoOverlay : UIElement
    {
        class Entry { public string Who, Text; public VMEntity Entity; public VM Vm; public double Born; }

        static ConvoOverlay instance;
        public static string LastPlacement = "none";   // "over-heads" when the balloons follow the Sims, "strip" for the bottom fallback (diagnostics)
        readonly List<Entry> lines = new List<Entry>();
        TextStyle name, text;
        double clock;
        const double Seconds = 6.5;
        const float MaxTextWidth = 250;

        public static void Show(VM vm, VMEntity speaker, string speakerName, string line, VMEntity listener, string listenerName, string reply)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                LastPlacement = "none";   // set again when the balloons are drawn
                if (instance == null || instance.Parent == null) { instance = new ConvoOverlay(); screen.Add(instance); }
                instance.Add(vm, speaker, speakerName, line, listener, listenerName, reply);
            }
            catch (Exception e) { Boot.Log("overlay: " + e.Message); }
        }

        ConvoOverlay()
        {
            name = TextStyle.DefaultLabel.Clone(); name.Size = 14; name.Color = new Color(255, 230, 120); name.Shadow = true;
            text = TextStyle.DefaultLabel.Clone(); text.Size = 16; text.Color = Color.White; text.Shadow = true;
        }

        const int MaxLines = 4;

        void Add(VM vm, VMEntity speaker, string speakerName, string line, VMEntity listener, string listenerName, string reply)
        {
            lines.RemoveAll(e =>
                (speaker != null && ReferenceEquals(e.Entity, speaker))
                || (listener != null && ReferenceEquals(e.Entity, listener))
                || clock - e.Born > Seconds);
            while (lines.Count > MaxLines - 2) lines.RemoveAt(0);
            lines.Add(new Entry { Who = speakerName, Text = "“" + line + "”", Entity = speaker, Vm = vm, Born = clock });
            // the answer appears a moment later, as in a real exchange
            if (!string.IsNullOrEmpty(reply)) lines.Add(new Entry { Who = listenerName, Text = "“" + reply + "”", Entity = listener, Vm = vm, Born = clock + 1.6 });
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            clock += 1.0 / 60.0;
            if (lines.Count == 0) return;
            lines.RemoveAll(e => clock - e.Born > Seconds);
        }

        // the point just above a Sim's head, in UI coordinates (the mapping the game's own chat balloons use); null when unknown
        static Vector2? Anchor(Entry e)
        {
            try
            {
                var world = e.Vm.Context.World;
                var st = world.State;
                var ui = e.Entity.WorldUI;
                if (ui == null) return null;
                var scr = ui.GetScreenPos(st);
                var scale = FSOEnvironment.DPIScaleFactor;
                var middle = new Vector2((int)(UIScreen.Current.ScreenWidth / (2 / scale)), (int)(UIScreen.Current.ScreenHeight / (2 / scale)));
                var zoomed = ((scr - middle) * world.BackbufferScale) + middle;
                var off2 = new Vector2(st.WorldSpace.WorldPxWidth, st.WorldSpace.WorldPxHeight);
                off2 = (off2 / st.PreciseZoom - off2) / 2;
                var pt = ((zoomed + new Vector2(0, -60) / (1 << (3 - (int)st.Zoom))) + off2) * st.PreciseZoom / scale;
                return float.IsNaN(pt.X) || float.IsNaN(pt.Y) ? (Vector2?)null : pt;
            }
            catch { return null; }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (lines.Count == 0) return;
            if (painter == null) { painter = new HtmlSurface(this, "balloon.none"); LoadTokens(); }
            painter.BeginDraw(batch);
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float screenW = batch.GraphicsDevice.Viewport.Width / scale;
            float screenH = batch.GraphicsDevice.Viewport.Height / scale;

            var placed = new List<BalloonLayout.Box>();
            float stripY = screenH - 24;
            foreach (var l in lines)
            {
                if (clock < l.Born) continue;
                var wrapped = BalloonLayout.Wrap(l.Text, MaxTextWidth, s => TextW(s, TextSize));
                float tw = TextW(l.Who, NameSize);
                foreach (var w in wrapped) tw = Math.Max(tw, TextW(w, TextSize));
                float lh = 20, w2 = tw + 28, h = 4 + 8 + 20 + wrapped.Count * lh + 8;

                var anchor = Anchor(l);
                BalloonLayout.Box box;
                bool over = anchor.HasValue;
                if (over)
                {
                    box = BalloonLayout.PlaceAbove(anchor.Value.X, anchor.Value.Y, w2, h, screenW, screenH);
                    foreach (var p in placed) box = BalloonLayout.AvoidOverlap(p, box);
                }
                else
                {
                    stripY -= h + 6;
                    box = new BalloonLayout.Box { X = (screenW - w2) / 2 + 120, Y = stripY, W = w2, H = h };
                }
                placed.Add(box);
                LastPlacement = over ? "over-heads" : "strip";

                // the redesign's balloon: dark glass (ignores the theme), a 4 px bar and the speaker's name in the Sim's identity colour, white text
                var ident = Token("sim-" + SimMemoryPageData.SimColor(l.Who), new Rgba(255, 217, 90));
                painter.FillRect(box.X, box.Y, box.W, box.H, BalloonBg, Radius);
                painter.FillRect(box.X + 6, box.Y, box.W - 12, 4, ident, 2);   // top bar, inset so the corners stay round
                if (over)
                {
                    // a small tail pointing down at the Sim
                    float cx = Math.Max(box.X + 14, Math.Min(anchor.Value.X, box.X + box.W - 14));
                    for (int i = 0; i < 8; i++)
                        painter.FillRect(cx - (8 - i), box.Y + box.H + i - 1, (8 - i) * 2, 1, BalloonBg, 0);
                }
                painter.DrawText(l.Who, box.X + 14, box.Y + 10, NameSize, true, ident);
                float ty = box.Y + 32;
                foreach (var w in wrapped) { painter.DrawText(w, box.X + 14, ty, TextSize, false, BalloonText); ty += lh; }
            }
        }

        // ---- redesign painting (tokens from Content/ui/design-tokens.json; dark glass in both themes) ----
        const float TextSize = 20, NameSize = 16, Radius = 14;
        HtmlSurface painter;
        Dictionary<string, string> tokens;
        Rgba BalloonBg, BalloonText;

        float TextW(string s, float size) { float w, h; painter.MeasureText(s, size, false, out w, out h); return w; }

        Rgba Token(string name, Rgba fallback)
        {
            string v;
            return tokens != null && tokens.TryGetValue(name, out v) ? HtmlView.ParseColor(v) : fallback;
        }

        void LoadTokens()
        {
            try { tokens = ThemeTokens.Build(System.IO.File.ReadAllText(System.IO.Path.Combine(Boot.GameDir, "Content", "ui", "design-tokens.json")), "night"); }
            catch { tokens = null; }
            BalloonBg = Token("balloon-bg", new Rgba(10, 20, 24, 242));
            BalloonText = Token("balloon-text", new Rgba(255, 255, 255));
        }
    }
}
