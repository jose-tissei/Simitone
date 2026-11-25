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
        // switching the graphics mode (the Options view tiles) recreates the graphics device and disposes every texture made before:
        // drop the cached masks and icon sheets so they are built again on the new device
        void DropStaleTextures()
        {
            bool stale = cornerTex.Values.Any(t => t.IsDisposed) || ringTex.Values.Any(t => t.IsDisposed) || tailTex.Values.Any(t => t.IsDisposed)
                || iconSheets.Values.Any(t => t.IsDisposed) || (icons != null && icons != px && icons.IsDisposed);
            if (!stale) return;
            cornerTex.Clear(); ringTex.Clear(); tailTex.Clear(); iconSheets.Clear(); icons = null;
        }

        public void BeginDraw(UISpriteBatch b)
        {
            batch = b;
            px = TextureGenerator.GetPxWhite(b.GraphicsDevice);
            DropStaleTextures();
        }

        public void Draw(UISpriteBatch b)
        {
            batch = b;
            px = TextureGenerator.GetPxWhite(b.GraphicsDevice);
            DropStaleTextures();
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

        // Anti-aliased corners: for a radius r a small texture (2r x r) holds the top-left and top-right corner coverage, computed once per radius.
        // Rows of it are drawn tinted, so a gradient can still change colour row by row and the edge is smooth instead of stair-stepped.
        readonly Dictionary<int, Texture2D> cornerTex = new Dictionary<int, Texture2D>();
        Texture2D Corners(int r)
        {
            Texture2D t;
            if (cornerTex.TryGetValue(r, out t)) return t;
            t = new Texture2D(batch.GraphicsDevice, 2 * r, r);
            var data = new Color[2 * r * r];
            for (int py = 0; py < r; py++)
                for (int px2 = 0; px2 < 2 * r; px2++)
                {
                    float dx = r - (px2 + 0.5f), dy = r - (py + 0.5f);   // vector to the corner circle's centre (left half; the right half is mirrored below)
                    float d = r - (float)Math.Sqrt(dx * dx + dy * dy);   // distance inside the circle edge, in pixels
                    byte a = (byte)(255 * Math.Max(0, Math.Min(1, d + 0.5f)));
                    data[py * 2 * r + px2] = new Color(a, a, a, a);     // premultiplied white
                }
            // right half mirrors the left one: pixel (r + k) takes coverage of left pixel (r - 1 - k)
            for (int py = 0; py < r; py++)
                for (int k = 0; k < r; k++) data[py * 2 * r + r + k] = data[py * 2 * r + (r - 1 - k)];
            t.SetData(data);
            cornerTex[r] = t;
            return t;
        }

        void Piece(Texture2D tex, int sx, int sy, int sw, float dx, float dy, Color c)
        {
            var src = new Rectangle(sx, sy, sw, 1);
            if (!rotating) owner.DrawLocalTexture(batch, tex, src, new Vector2(dx, dy), Vector2.One, c);
            else owner.DrawLocalTexture(batch, tex, src, Turn(dx, dy), new Vector2(1.02f, 1.6f), c, rotRad);
        }

        // rounded rectangle: smooth corner rows at the top and bottom (radii may differ), a centre block between;
        // colorAt(t) gives the colour at relative height t (0 top .. 1 bottom)
        void Rounded(float x, float y, float w, float h, float radiusTop, float radiusBottom, Func<float, Color> colorAt)
        {
            if (!rotating) { x = (float)Math.Round(x); y = (float)Math.Round(y); w = (float)Math.Round(w); h = (float)Math.Round(h); }
            int rt = (int)Math.Round(Math.Min(radiusTop, Math.Min(w, h) / 2)), rb = (int)Math.Round(Math.Min(radiusBottom, Math.Min(w, h) / 2));
            if (rt < 2) rt = 0;
            if (rb < 2) rb = 0;
            for (int i = 0; i < rt; i++)
            {
                var ct = colorAt((i + 0.5f) / h);
                var tex = Corners(rt);
                Piece(tex, 0, i, rt, x, y + i, ct); Piece(tex, rt, i, rt, x + w - rt, y + i, ct);
                Rect(x + rt, y + i, w - 2 * rt, 1, ct);
            }
            for (int i = 0; i < rb; i++)
            {
                var cb = colorAt(1 - (i + 0.5f) / h);
                var tex = Corners(rb);
                Piece(tex, 0, i, rb, x, y + h - 1 - i, cb); Piece(tex, rb, i, rb, x + w - rb, y + h - 1 - i, cb);
                Rect(x + rb, y + h - 1 - i, w - 2 * rb, 1, cb);
            }
            // the middle part, banded when it is a gradient
            float midY = y + rt, midH = h - rt - rb;
            if (midH <= 0) return;
            var top = colorAt(rt / h); var bot = colorAt(1 - rb / h);
            if (top == bot) Rect(x, midY, w, midH, top);
            else
            {
                int bands = Math.Max(1, Math.Min(24, (int)(midH / 3)));
                for (int k = 0; k < bands; k++)
                    Rect(x, midY + midH * k / bands, w, midH / bands + 0.5f, colorAt((rt + midH * (k + 0.5f) / bands) / h));
            }
        }

        public void FillRect(float x, float y, float w, float h, Rgba color, float radius)
        {
            var c = C(color);
            Rounded(x, y, w, h, radius, radius, t => c);
        }

        public void FillRectCorners(float x, float y, float w, float h, Rgba color, float radiusTop, float radiusBottom)
        {
            var c = C(color);
            Rounded(x, y, w, h, radiusTop, radiusBottom, t => c);
        }

        public void FillGradientCorners(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float radiusTop, float radiusBottom)
        {
            var a = C(from); var b = C(to);
            if (horizontal) FillGradient(x, y, w, h, from, to, true, radiusTop);
            else Rounded(x, y, w, h, radiusTop, radiusBottom, t => Color.Lerp(a, b, Math.Max(0, Math.Min(1, t))));
        }

        public void FillGradient(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float radius)
        {
            var a = C(from); var b = C(to);
            if (horizontal)
            {
                int n = Math.Max(2, Math.Min(48, (int)(w / 12)));
                for (int k = 0; k < n; k++) Rect(x + w * k / n, y, w / n + 0.5f, h, Color.Lerp(a, b, (k + 0.5f) / n));
            }
            else Rounded(x, y, w, h, radius, radius, t => Color.Lerp(a, b, Math.Max(0, Math.Min(1, t))));
        }

        // Anti-aliased ring corners (outer circle minus inner circle) for radius r and thickness t: a 2r x r mask like Corners()
        readonly Dictionary<int, Texture2D> ringTex = new Dictionary<int, Texture2D>();
        Texture2D RingCorners(int r, int t)
        {
            int key = r * 1000 + t;
            Texture2D tex;
            if (ringTex.TryGetValue(key, out tex)) return tex;
            tex = new Texture2D(batch.GraphicsDevice, 2 * r, r);
            var data = new Color[2 * r * r];
            for (int py = 0; py < r; py++)
                for (int k = 0; k < r; k++)
                {
                    float dx = r - (k + 0.5f), dy = r - (py + 0.5f);
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float outer = Math.Max(0, Math.Min(1, r - dist + 0.5f));        // inside the outer circle
                    float inner = Math.Max(0, Math.Min(1, (r - t) - dist + 0.5f));  // inside the inner circle (the hole)
                    byte a = (byte)(255 * Math.Max(0, outer - inner));
                    var c = new Color(a, a, a, a);
                    data[py * 2 * r + k] = c; data[py * 2 * r + 2 * r - 1 - k] = c;   // right corner mirrors the left
                }
            tex.SetData(data);
            ringTex[key] = tex;
            return tex;
        }

        public void StrokeRect(float x, float y, float w, float h, Rgba color, float thickness, float radius)
        {
            var c = C(color);
            if (!rotating) { x = (float)Math.Round(x); y = (float)Math.Round(y); w = (float)Math.Round(w); h = (float)Math.Round(h); }
            int t = Math.Max(1, (int)Math.Round(thickness));
            int r = (int)Math.Round(Math.Min(radius, Math.Min(w, h) / 2));
            if (r < 2) { Rect(x, y, w, t, c); Rect(x, y + h - t, w, t, c); Rect(x, y + t, t, h - 2 * t, c); Rect(x + w - t, y + t, t, h - 2 * t, c); return; }
            t = Math.Min(t, r);
            var tex = RingCorners(r, t);
            for (int i = 0; i < r; i++)
            {
                Piece(tex, 0, i, r, x, y + i, c); Piece(tex, r, i, r, x + w - r, y + i, c);
                Piece(tex, 0, i, r, x, y + h - 1 - i, c); Piece(tex, r, i, r, x + w - r, y + h - 1 - i, c);
                if (i < t) { Rect(x + r, y + i, w - 2 * r, 1, c); Rect(x + r, y + h - 1 - i, w - 2 * r, 1, c); }   // top and bottom bars between the corners
            }
            float midH = h - 2 * r;
            Rect(x, y + r, t, midH, c); Rect(x + w - t, y + r, t, midH, c);
        }

        // A small anti-aliased triangle pointing down (apex at the bottom centre): the speech balloon's tail.
        readonly Dictionary<int, Texture2D> tailTex = new Dictionary<int, Texture2D>();
        public void FillTail(float cx, float y, int halfWidth, int height, Rgba color)
        {
            int key = halfWidth * 1000 + height;
            Texture2D tex;
            if (!tailTex.TryGetValue(key, out tex))
            {
                int w = halfWidth * 2;
                tex = new Texture2D(batch.GraphicsDevice, w, height);
                var data = new Color[w * height];
                for (int py = 0; py < height; py++)
                    for (int pxl = 0; pxl < w; pxl++)
                    {
                        int hit = 0;
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                            {
                                float fx = pxl + (sx + 0.5f) / 4 - halfWidth, fy = py + (sy + 0.5f) / 4;
                                if (Math.Abs(fx) <= halfWidth * (1 - fy / height)) hit++;
                            }
                        byte a = (byte)(255 * hit / 16);
                        data[py * w + pxl] = new Color(a, a, a, a);
                    }
                tex.SetData(data);
                tailTex[key] = tex;
            }
            var col = C(color);
            float dx = (float)Math.Round(cx) - halfWidth, dy = (float)Math.Round(y);
            owner.DrawLocalTexture(batch, tex, null, new Vector2(dx, dy), Vector2.One, col);
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

        int iconCell, iconW; Color[] iconPixels;
        readonly Dictionary<int, Texture2D> iconSheets = new Dictionary<int, Texture2D>();

        // the 8 x 8 icon sheet reduced so that each cell is sz x sz pixels (area average of premultiplied pixels = smooth, no jaggies)
        Texture2D IconSheetAt(int sz)
        {
            Texture2D t;
            if (iconSheets.TryGetValue(sz, out t)) return t;
            if (sz == iconCell) { iconSheets[sz] = icons; return icons; }
            int W = 8 * sz;
            var dst = new Color[W * W];
            float k = (float)iconCell / sz;
            for (int cy = 0; cy < 8; cy++)
                for (int cx = 0; cx < 8; cx++)
                    for (int py = 0; py < sz; py++)
                        for (int pxl = 0; pxl < sz; pxl++)
                        {
                            float x0 = pxl * k, x1 = x0 + k, y0 = py * k, y1 = y0 + k;
                            float r = 0, g = 0, b = 0, a = 0, wsum = 0;
                            for (int sy = (int)y0; sy < Math.Min(iconCell, (int)Math.Ceiling(y1)); sy++)
                            {
                                float wy = Math.Min(sy + 1, y1) - Math.Max(sy, y0);
                                for (int sx = (int)x0; sx < Math.Min(iconCell, (int)Math.Ceiling(x1)); sx++)
                                {
                                    float w = wy * (Math.Min(sx + 1, x1) - Math.Max(sx, x0));
                                    var c = iconPixels[(cy * iconCell + sy) * iconW + cx * iconCell + sx];
                                    r += c.R * w; g += c.G * w; b += c.B * w; a += c.A * w; wsum += w;
                                }
                            }
                            if (wsum > 0) dst[(cy * sz + py) * W + cx * sz + pxl] = new Color((byte)(r / wsum + 0.5f), (byte)(g / wsum + 0.5f), (byte)(b / wsum + 0.5f), (byte)(a / wsum + 0.5f));
                        }
            t = new Texture2D(batch.GraphicsDevice, W, W);
            t.SetData(dst);
            iconSheets[sz] = t;
            return t;
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
                    iconCell = icons.Width / 8; iconW = icons.Width;
                    for (int i = 0; i < px4.Length; i++) px4[i] = Color.FromNonPremultiplied(px4[i].R, px4[i].G, px4[i].B, px4[i].A);
                    icons.SetData(px4); iconPixels = px4;
                }
                catch { icons = px; }
            }
            if (icons == px) return;
            // the sheet's cells are much larger than the icon on screen, and the sampler would just pick single pixels (jagged):
            // average the source pixels down to the size actually drawn, once per size
            int sz = Math.Max(4, (int)Math.Round(size));
            var sheet = IconSheetAt(sz);
            var src = new Rectangle((cell % 8) * sz, (cell / 8) * sz, sz, sz);
            if (!rotating) { owner.DrawLocalTexture(batch, sheet, src, new Vector2(x + (size - sz) / 2, y + (size - sz) / 2), Vector2.One, C(color)); return; }
            owner.DrawLocalTexture(batch, sheet, src, Turn(x + size / 2, y + size / 2), Vector2.One, C(color), rotRad, new Vector2(sz / 2f, sz / 2f));
        }
    }
}
