using System;
using System.Collections.Generic;
using System.IO;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.HtmlUi;

namespace Simitone.Conversations
{
    // Proof of concept: a full-screen overlay whose look comes from an HTML + CSS file (Content/ui/*.html) instead of hand-coded draw calls.
    // HtmlView (AngleSharp for markup/CSS, Yoga for flexbox) lays the page out; this class only paints boxes and text through the game's UISpriteBatch
    // and feeds mouse input in. The file is re-read when it changes on disk, so a page can be edited while the game runs.
    public class HtmlPanel : UIElement, IPainter
    {
        static HtmlPanel instance;
        string path;
        DateTime loadedAt;
        double watchTimer;
        HtmlView view;
        float viewW, viewH;
        bool prevLeft = true, prevEsc = true;
        UISpriteBatch batch;
        Texture2D px;
        readonly Dictionary<int, TextStyle> styles = new Dictionary<int, TextStyle>();
        public static string LastAction = "";

        public static bool IsOpen { get { return instance != null && instance.Parent != null; } }

        public static string Open(string file)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (instance == null) instance = new HtmlPanel();
                instance.path = Path.IsPathRooted(file) ? file : Path.Combine(Boot.GameDir, "Content", "ui", file);
                if (!File.Exists(instance.path)) return "missing " + instance.path;
                instance.Load();
                if (instance.Parent != screen) { if (instance.Parent != null) instance.Parent.Remove(instance); screen.Add(instance); }
                instance.prevLeft = true; instance.prevEsc = true;
                return Status();
            }
            catch (Exception e) { Boot.Log("htmlui open failed: " + e); return "error: " + e.Message; }
        }

        public static string Close()
        {
            if (instance != null && instance.Parent != null) instance.Parent.Remove(instance);
            return "closed";
        }

        public static string Status()
        {
            return IsOpen ? "open=True file=" + instance.path + " boxes=" + Count(instance.view.Root) + " last=" + LastAction : "open=False last=" + LastAction;
        }

        static int Count(Box b) { if (b == null) return 0; int n = 1; foreach (var c in b.Children) n += Count(c); return n; }

        void Load()
        {
            view = HtmlView.FromHtml(File.ReadAllText(path), this);
            view.Action += (action, box) =>
            {
                LastAction = action;
                if (action == "close") Close();
                else if (action == "news") view.SetText("title", "Bob's News");
                else if (action.StartsWith("tab-")) view.SetText("title", action.Substring(4) + "'s Sim Memory");
            };
            loadedAt = File.GetLastWriteTimeUtc(path);
            viewW = 0;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!IsOpen) return;
            watchTimer += 1.0 / 60.0;
            if (watchTimer > 0.5)
            {
                watchTimer = 0;
                try { if (File.GetLastWriteTimeUtc(path) != loadedAt) Load(); } catch { }
            }
            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEsc) { prevEsc = true; Close(); return; }
            prevEsc = esc;
            if (view == null || viewW == 0) return;
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float mx = state.MouseState.X / scale, my = state.MouseState.Y / scale;
            view.Hover(mx, my);
            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeft) view.Click(mx, my);
            prevLeft = left;
        }

        public override void Draw(UISpriteBatch b)
        {
            if (!IsOpen || view == null) return;
            batch = b;
            px = TextureGenerator.GetPxWhite(b.GraphicsDevice);
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float w = b.GraphicsDevice.Viewport.Width / scale, h = b.GraphicsDevice.Viewport.Height / scale;
            if (w != viewW || h != viewH || view.Root == null) { viewW = w; viewH = h; view.Layout(w, h); }
            view.Paint(this);
        }

        // ---- IPainter ----
        static Color C(Rgba c) { return new Color(c.R, c.G, c.B, c.A); }

        public void FillRect(float x, float y, float w, float h, Rgba color, float radius)
        {
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), C(color));
        }

        public void StrokeRect(float x, float y, float w, float h, Rgba color, float t, float radius)
        {
            var c = C(color);
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, t), c);
            DrawLocalTexture(batch, px, null, new Vector2(x, y + h - t), new Vector2(w, t), c);
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(t, h), c);
            DrawLocalTexture(batch, px, null, new Vector2(x + w - t, y), new Vector2(t, h), c);
        }

        TextStyle Style(float size, Rgba color)
        {
            int key = ((int)size << 24) ^ (color.R << 16) ^ (color.G << 8) ^ color.B;
            TextStyle s;
            if (!styles.TryGetValue(key, out s))
            {
                s = TextStyle.DefaultLabel.Clone();
                s.Size = Math.Max(8, (int)Math.Round(size * 0.82f)); // the game font runs larger than the same CSS px size
                s.Color = new Color(color.R, color.G, color.B);
                s.Shadow = true;
                styles[key] = s;
            }
            return s;
        }

        public void DrawText(string text, float x, float y, float size, bool bold, Rgba color)
        {
            DrawLocalString(batch, text, new Vector2(x, y), Style(size, color));
        }

        public void MeasureText(string text, float size, bool bold, out float w, out float h)
        {
            var m = Style(size, new Rgba(255, 255, 255)).MeasureString(text);
            w = m.X; h = m.Y;
        }
    }
}
