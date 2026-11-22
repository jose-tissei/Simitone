using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.SimAntics;
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

        void Add(VM vm, VMEntity speaker, string speakerName, string line, VMEntity listener, string listenerName, string reply)
        {
            lines.Clear();
            lines.Add(new Entry { Who = speakerName, Text = "“" + line + "”", Entity = speaker, Vm = vm, Born = clock });
            // the answer appears a moment later, as in a real exchange
            lines.Add(new Entry { Who = listenerName, Text = "“" + reply + "”", Entity = listener, Vm = vm, Born = clock + 1.6 });
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            clock += 1.0 / 60.0;
            if (lines.Count == 0) return;
            if (clock - lines[lines.Count - 1].Born > Seconds) lines.Clear();
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
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float screenW = batch.GraphicsDevice.Viewport.Width / scale;
            float screenH = batch.GraphicsDevice.Viewport.Height / scale;

            var placed = new List<BalloonLayout.Box>();
            float stripY = screenH - 24;
            foreach (var l in lines)
            {
                if (clock < l.Born) continue;
                var wrapped = BalloonLayout.Wrap(l.Text, MaxTextWidth, s => text.MeasureString(s).X);
                float tw = 0;
                foreach (var w in wrapped) tw = Math.Max(tw, text.MeasureString(w).X);
                float lh = 20, w2 = tw + 24, h = 26 + wrapped.Count * lh;

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

                DrawLocalTexture(batch, px, null, new Vector2(box.X, box.Y), new Vector2(box.W, box.H), new Color(20, 20, 30, 215));
                DrawLocalTexture(batch, px, null, new Vector2(box.X, box.Y), new Vector2(box.W, 2), new Color(255, 230, 120, 230));   // accent edge
                if (over)
                {
                    // a small tail pointing down at the Sim
                    float cx = Math.Max(box.X + 10, Math.Min(anchor.Value.X, box.X + box.W - 10));
                    for (int i = 0; i < 6; i++)
                        DrawLocalTexture(batch, px, null, new Vector2(cx - (6 - i), box.Y + box.H + i), new Vector2((6 - i) * 2, 1), new Color(20, 20, 30, 215));
                }
                DrawLocalString(batch, l.Who, new Vector2(box.X + 12, box.Y + 5), name);
                float ty = box.Y + 22;
                foreach (var w in wrapped) { DrawLocalString(batch, w, new Vector2(box.X + 12, ty), text); ty += lh; }
            }
        }
    }
}
