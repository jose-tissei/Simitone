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
    public class ConvoOverlay : SimUiElement
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
                if (instance == null) instance = new ConvoOverlay();
                if (instance.Parent != null && instance.Parent != screen)
                    instance.Parent.Remove(instance);
                screen.Add(instance);
                instance.Add(vm, speaker, speakerName, line, listener, listenerName, reply);
            }
            catch (Exception e) { Boot.Log("overlay: " + e.Message); }
        }

        ConvoOverlay()
        {
            name = SimUiTokens.MakeStyle(14, SimUiTokens.Mint, true);
            text = SimUiTokens.MakeStyle(15, SimUiTokens.TextPrimary, true);
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
            lines.Add(new Entry { Who = listenerName, Text = "“" + reply + "”", Entity = listener, Vm = vm, Born = clock + 1.6 });
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
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float screenW, screenH;
            GetScreenSize(batch, out screenW, out screenH);

            var placed = new List<BalloonLayout.Box>();
            float stripY = screenH - 24;
            float maxTextW = Math.Min(MaxTextWidth, Math.Max(140f, screenW - 64f));
            foreach (var l in lines)
            {
                if (clock < l.Born) continue;
                var wrapped = BalloonLayout.Wrap(l.Text, maxTextW, s => text.MeasureString(s).X);
                float tw = name.MeasureString(l.Who).X;
                foreach (var w in wrapped) tw = Math.Max(tw, text.MeasureString(w).X);
                float lh = Math.Max(20f, text.MeasureString("Ag").Y + 2f);
                float w2 = tw + 24f, h = 28f + wrapped.Count * lh;

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
                    box = new BalloonLayout.Box { X = Math.Max(12f, Math.Min(screenW - w2 - 12f, (screenW - w2) * 0.5f + 120f)), Y = stripY, W = w2, H = h };
                }
                placed.Add(box);
                LastPlacement = over ? "over-heads" : "strip";

                var boxRect = new SimRect(box.X, box.Y, box.W, box.H);
                FillRoundedRect(batch, boxRect, SimUiTokens.RadiusCamera, SimUiTokens.DialogBg);
                DrawRoundedBorder(batch, boxRect, SimUiTokens.RadiusCamera, SimUiTokens.InnerBorder, 1);
                FillRect(batch, px, box.X + 8f, box.Y + 1f, Math.Max(0f, box.W - 16f), 2f, SimUiTokens.Mint);   // inset Sims mint accent
                if (over)
                {
                    // a small tail pointing down at the Sim
                    float cx = Math.Max(box.X + 14, Math.Min(anchor.Value.X, box.X + box.W - 14));
                    for (int i = 0; i < 6; i++)
                        DrawLocalTexture(batch, px, null, new Vector2(cx - (6 - i), box.Y + box.H + i), new Vector2((6 - i) * 2, 1), SimUiTokens.DialogBg);
                }
                DrawLocalString(batch, l.Who, new Vector2(box.X + 12, box.Y + 5), name);
                float ty = box.Y + 24;
                foreach (var w in wrapped) { DrawLocalString(batch, w, new Vector2(box.X + 12, ty), text); ty += lh; }
            }
        }
    }
}
