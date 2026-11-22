using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Conversations
{
    // The two lines of a conversation, drawn as a speech panel above the control panel for a few seconds:
    //   Bob:    "Come here, Betty."
    //   Betty:  "Aww, thanks Bob."
    // The Simitone client has no text-balloon facility (its headline balloons only show pictures), so this is a
    // small screen-space element added to the current screen.
    public class ConvoOverlay : UIElement
    {
        class Entry { public string Who, Text; public double Born; public bool Second; }

        static ConvoOverlay instance;
        readonly List<Entry> lines = new List<Entry>();
        TextStyle name, text;
        double clock;
        const double Seconds = 6.5;

        public static void Show(string speaker, string line, string listener, string reply)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return;
                if (instance == null || instance.Parent == null) { instance = new ConvoOverlay(); screen.Add(instance); }
                instance.Add(speaker, line, listener, reply);
            }
            catch (Exception e) { Boot.Log("overlay: " + e.Message); }
        }

        ConvoOverlay()
        {
            name = TextStyle.DefaultLabel.Clone(); name.Size = 16; name.Color = new Color(255, 230, 120); name.Shadow = true;
            text = TextStyle.DefaultLabel.Clone(); text.Size = 16; text.Color = Color.White; text.Shadow = true;
        }

        void Add(string speaker, string line, string listener, string reply)
        {
            lines.Clear();
            lines.Add(new Entry { Who = speaker, Text = "“" + line + "”", Born = clock });
            // the answer appears a moment later, as in a real exchange
            lines.Add(new Entry { Who = listener, Text = "“" + reply + "”", Born = clock + 1.6, Second = true });
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            clock += 1.0 / 60.0;
            if (lines.Count == 0) return;
            if (clock - lines[lines.Count - 1].Born > Seconds) lines.Clear();
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (lines.Count == 0) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float screenW = batch.GraphicsDevice.Viewport.Width / scale;
            float screenH = batch.GraphicsDevice.Viewport.Height / scale;

            var visible = lines.FindAll(l => clock >= l.Born);
            if (visible.Count == 0) return;
            float w = Math.Min(760, screenW - 360), h = 18 + visible.Count * 30;
            float x = (screenW - w) / 2 + 120, y = screenH - h - 24;
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), new Color(0, 0, 0, 170));
            float ty = y + 10;
            foreach (var l in visible)
            {
                DrawLocalString(batch, l.Who + ":", new Vector2(x + 14, ty), name);
                DrawLocalString(batch, l.Text, new Vector2(x + 14 + 110, ty), text);
                ty += 30;
            }
        }
    }
}
