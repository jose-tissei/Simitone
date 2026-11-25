using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json.Linq;
using Simitone.HtmlUi;

namespace Simitone.Conversations
{
    // Shows one HTML page (Content/ui/<page>.html + sims-ui.css) on an owner UIElement, filled with data through the Template syntax.
    // Re-reads the files when they change, rebuilds when the data changes, and reports clicks as data-action names.
    public class HtmlSurface : IPainter
    {
        public const float FontScale = 0.70f;      // the game font runs about 1.4 times larger than the same CSS px size
        readonly UIElement owner;
        readonly string page;
        public string Theme = "night";
        public event Action<string> Action;
        public HtmlView View { get { return view; } }
        HtmlView view;
        string template, css, tokensJson;
        DateTime stamp;
        double watch;
        string dataKey;
        IDictionary<string, object> data = new Dictionary<string, object>();
        float viewW, viewH;
        bool prevLeft = true;
        Dictionary<string, string> tokens;
        UISpriteBatch batch;
        Texture2D px, icons;
        readonly Dictionary<int, TextStyle> styles = new Dictionary<int, TextStyle>();
        public string Error;

        static string UiDir { get { return Path.Combine(Boot.GameDir, "Content", "ui"); } }

        public HtmlSurface(UIElement owner, string page) { this.owner = owner; this.page = page; }

        public void SetData(IDictionary<string, object> d, string key)
        {
            if (key == dataKey && view != null) return;
            dataKey = key; data = d; view = null;
        }

        // true when the point is inside the first element of the page (the dialog); a click outside closes the window
        public bool PointInFirstBox(float x, float y)
        {
            if (view == null || view.Root == null || view.Root.Children.Count == 0) return true;
            var b = view.Root.Children[0];
            return x >= b.X && y >= b.Y && x < b.X + b.W && y < b.Y + b.H;
        }

        public void Invalidate() { view = null; }
        public void IgnoreCurrentClick() { prevLeft = true; }

        string[] Files() { return new[] { Path.Combine(UiDir, page), Path.Combine(UiDir, "sims-ui.css"), Path.Combine(UiDir, "design-tokens.json") }; }

        DateTime Stamp() { return Files().Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max(); }

        void Build()
        {
            try
            {
                var files = Files();
                template = File.ReadAllText(files[0]);
                css = File.Exists(files[1]) ? File.ReadAllText(files[1]) : "";
                tokensJson = File.ReadAllText(files[2]);
                tokens = ThemeTokens.Build(tokensJson, Theme);
                string body = Template.Render(template, data);
                view = HtmlView.FromHtml("<style>" + css + "</style>" + body, this, tokens);
                view.Action += (a, box) => { if (Action != null) Action(a); };
                view.Layout(viewW, viewH);
                stamp = Stamp();
                Error = null;
            }
            catch (Exception e) { Error = e.Message; Boot.Log("html surface " + page + ": " + e); view = null; stamp = Stamp(); }
        }

        public void Update(UpdateState state)
        {
            watch += 1.0 / 60.0;
            if (watch > 0.5) { watch = 0; if (Stamp() != stamp) view = null; }
            if (view == null) return;
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float mx = state.MouseState.X / scale, my = state.MouseState.Y / scale;
            view.Hover(mx, my);
            bool left = state.MouseState.LeftButton == ButtonState.Pressed;
            if (left && !prevLeft) view.Click(mx, my);
            prevLeft = left;
        }

        // for overlays that paint shapes and text directly (speech balloons) without a page: call before FillRect / StrokeRect / DrawText
        public void BeginDraw(UISpriteBatch b)
        {
            batch = b;
            px = TextureGenerator.GetPxWhite(b.GraphicsDevice);
        }

        public void Draw(UISpriteBatch b)
        {
            batch = b;
            px = TextureGenerator.GetPxWhite(b.GraphicsDevice);
            float scale = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            float w = b.GraphicsDevice.Viewport.Width / scale, h = b.GraphicsDevice.Viewport.Height / scale;
            if (w != viewW || h != viewH) { viewW = w; viewH = h; view = null; }
            if (view == null) Build();
            if (view == null) return;
            view.Paint(this);
        }

        // ---- IPainter ----
        // the game blends with premultiplied alpha
        static Color C(Rgba c) { return Color.FromNonPremultiplied(c.R, c.G, c.B, c.A); }
        // Optional rotation of everything drawn until ClearRotation (the engine's sprite batch rotates textures): every piece is moved
        // round the pivot and drawn turned by the same angle, so a rounded slot turns as one shape. Text is placed glyph by glyph along
        // the turned baseline (glyphs themselves stay upright, which is exact enough for the few degrees the design asks for).
        float rotCos = 1, rotSin = 0, rotX, rotY, rotRad;
        bool rotating;
        public void SetRotation(float pivotX, float pivotY, float radians) { rotating = Math.Abs(radians) > 1e-4f; rotX = pivotX; rotY = pivotY; rotRad = radians; rotCos = (float)Math.Cos(radians); rotSin = (float)Math.Sin(radians); }
        public void ClearRotation() { rotating = false; }
        Vector2 Turn(float x, float y)
        {
            float dx = x - rotX, dy = y - rotY;
            return new Vector2(rotX + dx * rotCos - dy * rotSin, rotY + dx * rotSin + dy * rotCos);
        }

        void Rect(float x, float y, float w, float h, Color c)
        {
            if (w <= 0 || h <= 0) return;
            if (!rotating) { owner.DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), c); return; }
            owner.DrawLocalTexture(batch, px, null, Turn(x, y), new Vector2(w + 1.6f, h + 1.6f), c, rotRad);   // a little larger: the engine snaps each piece to whole pixels, which would leave hairline gaps
        }

        // rounded rectangle as a centre block plus one strip per corner row; colorAt(t) gives the colour at relative height t (0 top .. 1 bottom)
        void Rounded(float x, float y, float w, float h, float r, Func<float, Color> colorAt)
        {
            r = Math.Min(r, Math.Min(w, h) / 2);
            if (r < 1.5f) { Rect(x, y, w, h, colorAt(0.5f)); return; }
            int step = r > 12 ? 2 : 1;
            for (float i = 0; i < r; i += step)
            {
                float dy = r - (i + step * 0.5f);
                float inset = r - (float)Math.Sqrt(Math.Max(0, r * r - dy * dy));
                float rowH = Math.Min(step, r - i);
                Rect(x + inset, y + i, w - 2 * inset, rowH, colorAt((i + rowH / 2) / h));
                Rect(x + inset, y + h - i - rowH, w - 2 * inset, rowH, colorAt(1 - (i + rowH / 2) / h));
            }
            // the middle part, banded when it is a gradient
            float midY = y + r, midH = h - 2 * r;
            var top = colorAt(r / h); var bot = colorAt(1 - r / h);
            if (top == bot) Rect(x, midY, w, midH, top);
            else
            {
                int bands = Math.Max(1, Math.Min(24, (int)(midH / 3)));
                for (int k = 0; k < bands; k++)
                    Rect(x, midY + midH * k / bands, w, midH / bands + 0.5f, colorAt((r + midH * (k + 0.5f) / bands) / h));
            }
        }

        public void FillRect(float x, float y, float w, float h, Rgba color, float radius)
        {
            var c = C(color);
            Rounded(x, y, w, h, radius, t => c);
        }

        public void FillGradient(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float radius)
        {
            var a = C(from); var b = C(to);
            if (horizontal)
            {
                int n = Math.Max(2, Math.Min(48, (int)(w / 12)));
                for (int k = 0; k < n; k++) Rect(x + w * k / n, y, w / n + 0.5f, h, Color.Lerp(a, b, (k + 0.5f) / n));
            }
            else Rounded(x, y, w, h, radius, t => Color.Lerp(a, b, Math.Max(0, Math.Min(1, t))));
        }

        public void StrokeRect(float x, float y, float w, float h, Rgba color, float t, float radius)
        {
            var c = C(color);
            float r = Math.Min(radius, Math.Min(w, h) / 2);
            if (r < 1.5f) { Rect(x, y, w, t, c); Rect(x, y + h - t, w, t, c); Rect(x, y + t, t, h - 2 * t, c); Rect(x + w - t, y + t, t, h - 2 * t, c); return; }
            // rounded ring: one row at a time through the corner zones, two bars in between
            float ri = Math.Max(0, r - t);
            for (float i = 0; i < r; i++)
            {
                float dy = r - (i + 0.5f);
                float outer = r - (float)Math.Sqrt(Math.Max(0, r * r - dy * dy));
                float innerSq = ri * ri - dy * dy;
                float inner = i < t || innerSq <= 0 ? -1 : r - (float)Math.Sqrt(innerSq);
                foreach (float ry in new[] { y + i, y + h - i - 1 })
                {
                    if (inner < 0) Rect(x + outer, ry, w - 2 * outer, 1, c);
                    else { Rect(x + outer, ry, inner - outer + 0.5f, 1, c); Rect(x + w - inner - 0.5f, ry, inner - outer + 0.5f, 1, c); }
                }
            }
            float midH = h - 2 * r;
            Rect(x, y + r, t, midH, c); Rect(x + w - t, y + r, t, midH, c);
        }

        TextStyle Style(float size, Rgba color)
        {
            int key = ((int)size << 24) ^ (color.R << 16) ^ (color.G << 8) ^ color.B;
            TextStyle s;
            if (!styles.TryGetValue(key, out s))
            {
                s = TextStyle.DefaultLabel.Clone();
                s.Size = Math.Max(8, (int)Math.Round(size * FontScale));
                s.Color = new Color(color.R, color.G, color.B);
                s.Shadow = false;
                styles[key] = s;
            }
            return s;
        }

        public void DrawText(string text, float x, float y, float size, bool bold, Rgba color)
        {
            var st = Style(size, color);
            if (!rotating) { owner.DrawLocalString(batch, text, new Vector2(x, y), st); return; }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ') continue;
                float before = i == 0 ? 0 : st.MeasureString(text.Substring(0, i)).X;
                owner.DrawLocalString(batch, text[i].ToString(), Turn(x + before, y), st);
            }
        }

        public void MeasureText(string text, float size, bool bold, out float w, out float h)
        {
            var m = Style(size, new Rgba(255, 255, 255)).MeasureString(text);
            w = m.X; h = m.Y;
        }

        public void DrawIcon(int cell, float x, float y, float size, Rgba color)
        {
            if (icons == null)
            {
                try
                {
                    using (var fs = File.OpenRead(Path.Combine(UiDir, "icons.png"))) icons = Texture2D.FromStream(batch.GraphicsDevice, fs);
                    var px4 = new Color[icons.Width * icons.Height];
                    icons.GetData(px4);
                    for (int i = 0; i < px4.Length; i++) px4[i] = Color.FromNonPremultiplied(px4[i].R, px4[i].G, px4[i].B, px4[i].A);
                    icons.SetData(px4);
                }
                catch { icons = px; }
            }
            if (icons == px) return;
            int cellPx = icons.Width / 8;
            var src = new Rectangle((cell % 8) * cellPx, (cell / 8) * cellPx, cellPx, cellPx);
            var scale = new Vector2(size / cellPx, size / cellPx);
            if (!rotating) { owner.DrawLocalTexture(batch, icons, src, new Vector2(x, y), scale, C(color)); return; }
            owner.DrawLocalTexture(batch, icons, src, Turn(x + size / 2, y + size / 2), scale, C(color), rotRad, new Vector2(cellPx / 2f, cellPx / 2f));
        }
    }
}
