using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Simitone.HtmlUi;

namespace Simitone.HtmlUi.Preview
{
    // Renders an HTML page through the same HtmlView the game uses, with GDI+, so pages can be designed without starting the game.
    // usage: HtmlUi.Preview page.html out.png [width height] [hoverX hoverY]
    static class Program
    {
        static int Main(string[] a)
        {
            if (a.Length < 2) { Console.WriteLine("usage: HtmlUi.Preview page.html out.png [width height] [hoverX hoverY]"); return 1; }
            int w = a.Length > 3 ? int.Parse(a[2]) : 960, h = a.Length > 3 ? int.Parse(a[3]) : 600;
            using (var bmp = new Bitmap(w, h))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(40, 60, 40));
                var painter = new GdiPainter(g);
                var view = HtmlView.FromHtml(File.ReadAllText(a[0]), painter);
                view.Layout(w, h);
                if (a.Length > 5) view.Hover(float.Parse(a[4]), float.Parse(a[5]));
                view.Paint(painter);
                bmp.Save(a[1], System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }
    }

    class GdiPainter : IPainter
    {
        readonly Graphics g;
        public GdiPainter(Graphics g) { this.g = g; }
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

        public void FillRect(float x, float y, float w, float h, Rgba c, float r)
        { using (var b = new SolidBrush(C(c))) using (var p = Round(x, y, w, h, r)) g.FillPath(b, p); }

        public void StrokeRect(float x, float y, float w, float h, Rgba c, float t, float r)
        { using (var pen = new Pen(C(c), t)) using (var p = Round(x + t / 2, y + t / 2, w - t, h - t, r)) g.DrawPath(pen, p); }

        Font F(float size, bool bold) { return new Font("Segoe UI", size * 0.75f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }

        public void DrawText(string s, float x, float y, float size, bool bold, Rgba c)
        { using (var f = F(size, bold)) using (var b = new SolidBrush(C(c))) g.DrawString(s, f, b, x, y, StringFormat.GenericTypographic); }

        public void MeasureText(string s, float size, bool bold, out float w, out float h)
        { using (var f = F(size, bold)) { var m = g.MeasureString(s, f, 100000, StringFormat.GenericTypographic); w = m.Width; h = m.Height; } }
    }
}
