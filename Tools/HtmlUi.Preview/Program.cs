using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Simitone.Conversations;
using Simitone.HtmlUi;

namespace Simitone.HtmlUi.Preview
{
    // Renders a page through the same HtmlView, Template and design tokens the game uses, with GDI+, so designs can be checked without starting the game.
    // usage: HtmlUi.Preview page.html out.png [--size 1920 1080] [--theme night|day] [--data data.json] [--bg screenshot.png] [--hover x y]
    //   the page's folder must hold sims-ui.css; design-tokens.json is taken from there or from docs/ui-redesign
    static class Program
    {
        static int Main(string[] a)
        {
            if (a.Length < 2) { Console.WriteLine("usage: HtmlUi.Preview page.html out.png [--size w h] [--theme night|day] [--data data.json] [--bg shot.png] [--hover x y]"); return 1; }
            int w = 1920, h = 1080; string theme = "night", dataFile = null, bg = null; float hx = -1, hy = -1;
            for (int i = 2; i < a.Length; i++)
            {
                if (a[i] == "--size") { w = int.Parse(a[++i]); h = int.Parse(a[++i]); }
                else if (a[i] == "--theme") theme = a[++i];
                else if (a[i] == "--data") dataFile = a[++i];
                else if (a[i] == "--bg") bg = a[++i];
                else if (a[i] == "--hover") { hx = float.Parse(a[++i]); hy = float.Parse(a[++i]); }
            }
            string dir = Path.GetDirectoryName(Path.GetFullPath(a[0]));
            string tokensPath = Path.Combine(dir, "design-tokens.json");
            if (!File.Exists(tokensPath)) tokensPath = FindUp(dir, Path.Combine("docs", "ui-redesign", "design-tokens.json"));
            string cssPath = Path.Combine(dir, "sims-ui.css");
            string iconPath = Path.Combine(dir, "icons.png");
            if (!File.Exists(iconPath)) iconPath = FindUp(dir, Path.Combine("docs", "ui-redesign", "skins", "icons.png"));

            var tokens = ThemeTokens.Build(File.ReadAllText(tokensPath), theme);
            var data = dataFile == null ? new Dictionary<string, object>() : (Dictionary<string, object>)ToObj(JToken.Parse(File.ReadAllText(dataFile)));
            string body = Template.Render(File.ReadAllText(a[0]), data);
            string css = File.Exists(cssPath) ? File.ReadAllText(cssPath) : "";

            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(40, 60, 40));
                if (bg != null) using (var img = Image.FromFile(bg)) g.DrawImage(img, 0, 0, w, h);
                var painter = new GdiPainter(g, iconPath != null && File.Exists(iconPath) ? new Bitmap(iconPath) : null);
                var view = HtmlView.FromHtml("<style>" + css + "</style>" + body, painter, tokens);
                view.Layout(w, h);
                if (hx >= 0) view.Hover(hx, hy);
                if (Environment.GetEnvironmentVariable("HTMLUI_DUMP") != null) Dump(view.Root, 0);
                view.Paint(painter);
                bmp.Save(a[1], System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }

        static void Dump(Box b, int depth)
        {
            if (b.Tag == "#text") return;
            Console.WriteLine(new string(' ', depth * 2) + b.Tag + "." + (b.Element == null ? "" : b.Element.ClassName) + " y=" + b.Y + " h=" + b.H + " w=" + b.W);
            foreach (var c in b.Children) Dump(c, depth + 1);
        }

        static string FindUp(string dir, string rel)
        {
            for (var d = new DirectoryInfo(dir); d != null; d = d.Parent) { var p = Path.Combine(d.FullName, rel); if (File.Exists(p)) return p; }
            return null;
        }

        static object ToObj(JToken t)
        {
            if (t is JObject) return ((JObject)t).Properties().ToDictionary(p => p.Name, p => ToObj(p.Value));
            if (t is JArray) return t.Select(ToObj).ToList();
            if (t.Type == JTokenType.Boolean) return (bool)t;
            if (t.Type == JTokenType.Integer) return (int)t;
            if (t.Type == JTokenType.Null) return null;
            return (string)t;
        }
    }

    class GdiPainter : IPainter
    {
        readonly Graphics g;
        readonly Bitmap icons;
        public GdiPainter(Graphics g, Bitmap icons) { this.g = g; this.icons = icons; }
        static Color C(Rgba c) { return Color.FromArgb(c.A, c.R, c.G, c.B); }

        static GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            var p = new GraphicsPath();
            if (r <= 0) { p.AddRectangle(new RectangleF(x, y, w, h)); return p; }
            r = Math.Min(r, Math.Min(w, h) / 2);
            p.AddArc(x, y, r * 2, r * 2, 180, 90); p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90); p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure(); return p;
        }

        static GraphicsPath Round2(float x, float y, float w, float h, float rt, float rb)
        {
            var p = new GraphicsPath();
            rt = Math.Min(rt, Math.Min(w, h) / 2); rb = Math.Min(rb, Math.Min(w, h) / 2);
            if (rt > 0) { p.AddArc(x, y, rt * 2, rt * 2, 180, 90); p.AddArc(x + w - rt * 2, y, rt * 2, rt * 2, 270, 90); }
            else { p.AddLine(x, y, x + w, y); }
            if (rb > 0) { p.AddArc(x + w - rb * 2, y + h - rb * 2, rb * 2, rb * 2, 0, 90); p.AddArc(x, y + h - rb * 2, rb * 2, rb * 2, 90, 90); }
            else { p.AddLine(x + w, y + h, x, y + h); }
            p.CloseFigure(); return p;
        }

        public void FillRectCorners(float x, float y, float w, float h, Rgba c, float rt, float rb)
        { using (var b = new SolidBrush(C(c))) using (var p = Round2(x, y, w, h, rt, rb)) g.FillPath(b, p); }

        public void FillGradientCorners(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float rt, float rb)
        {
            if (w < 1 || h < 1) return;
            using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), C(from), C(to), horizontal ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical))
            using (var p = Round2(x, y, w, h, rt, rb)) g.FillPath(b, p);
        }

        public void FillRect(float x, float y, float w, float h, Rgba c, float r)
        { using (var b = new SolidBrush(C(c))) using (var p = Round(x, y, w, h, r)) g.FillPath(b, p); }

        public void FillGradient(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float r)
        {
            if (w < 1 || h < 1) return;
            using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), C(from), C(to), horizontal ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical))
            using (var p = Round(x, y, w, h, r)) g.FillPath(b, p);
        }

        public void StrokeRect(float x, float y, float w, float h, Rgba c, float t, float r)
        { using (var pen = new Pen(C(c), t)) using (var p = Round(x + t / 2, y + t / 2, w - t, h - t, r)) g.DrawPath(pen, p); }

        // the game font runs larger than the same CSS size, so the preview shrinks it by the same factor
        Font F(float size, bool bold) { return new Font("Montserrat", Math.Max(6f, size * 0.75f * 0.93f), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }

        public void DrawText(string s, float x, float y, float size, bool bold, Rgba c)
        { using (var f = F(size, bold)) using (var b = new SolidBrush(C(c))) g.DrawString(s, f, b, x, y, StringFormat.GenericTypographic); }

        public void MeasureText(string s, float size, bool bold, out float w, out float h)
        { using (var f = F(size, bold)) { var m = g.MeasureString(s, f, 100000, StringFormat.GenericTypographic); w = m.Width; h = m.Height; } }

        public void DrawIcon(int cell, float x, float y, float size, Rgba c)
        {
            if (icons == null) return;
            int cp = icons.Width / 8;
            var cm = new System.Drawing.Imaging.ColorMatrix(new[] {
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, c.A / 255f, 0 }, new float[] { c.R / 255f, c.G / 255f, c.B / 255f, 0, 1 } });
            using (var ia = new System.Drawing.Imaging.ImageAttributes())
            {
                ia.SetColorMatrix(cm);
                g.DrawImage(icons, new Rectangle((int)x, (int)y, (int)size, (int)size), (cell % 8) * cp, (cell / 8) * cp, cp, cp, GraphicsUnit.Pixel, ia);
            }
        }
    }
}
