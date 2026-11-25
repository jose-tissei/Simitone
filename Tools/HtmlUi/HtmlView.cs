using System.Globalization;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.RenderTree;
using AngleSharp.Dom;
using Facebook.Yoga;
using Node = Facebook.Yoga.Node;

namespace Simitone.HtmlUi
{
    // One laid-out element (or text run) of a page.
    public class Box
    {
        public string Tag, Id, Action;
        public Box Parent;
        public List<Box> Children = new List<Box>();
        public IElement Element;
        public Node Yoga;
        public string Text;            // text run
        public List<string> Lines;     // wrapped text
        public Rgba Background, Color, BorderColor;
        public float BorderWidth, Radius, RadiusBottom, FontSize = 16, LineHeight;
        public bool Bold, Upper, HasGradient, GradHorizontal;
        public bool Clip;              // overflow hidden/auto/scroll: children that do not fully fit inside are not drawn (the painters have no scissor)
        public Rgba GradA, GradB;
        public int Icon = -1;          // cell of the icon sheet, -1 when this is not an icon
        public float X, Y, W, H;       // absolute, in view space
    }

    // Loads an HTML page with <style>, resolves CSS with AngleSharp, lays it out with Yoga (flexbox) and paints through IPainter.
    public class HtmlView
    {
        public Box Root;
        public event Action<string, Box> Action;
        IDocument doc; IStyleCollection styles;
        IPainter measurer;
        Box hovered;
        float viewW, viewH;

        // var(--name) is replaced textually so the CSS engine never has to know about custom properties
        static string ApplyTokens(string html, IDictionary<string, string> tokens)
        {
            return System.Text.RegularExpressions.Regex.Replace(html, @"var\(\s*--([\w-]+)\s*(?:,[^)]*)?\)", m =>
            { string v; return tokens.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value; });
        }

        public static HtmlView FromHtml(string html, IPainter painter, IDictionary<string, string> tokens = null)
        {
            if (tokens != null) html = ApplyTokens(html, tokens);
            var v = new HtmlView { measurer = painter };
            // hover state cannot be asked of a static document, so the rule is rewritten to a class that we toggle ourselves
            html = "<style>html { font-size: 16px; }</style>" + html.Replace(":hover", ".is-hover");
            var ctx = BrowsingContext.New(Configuration.Default.WithCss());
            v.doc = ctx.OpenAsync(r => r.Content(html)).GetAwaiter().GetResult();
            v.styles = v.doc.DefaultView.GetStyleCollection(new DefaultRenderDevice { FontSize = 16, DeviceWidth = 1920, DeviceHeight = 1080 }); return v;
        }

        public void Layout(float w, float h)
        {
            viewW = w; viewH = h;
            Root = Build(doc.Body, null);
            YGNodeStyleAPI.YGNodeStyleSetWidth(Root.Yoga, w);
            YGNodeStyleAPI.YGNodeStyleSetHeight(Root.Yoga, h);
            YGNodeAPI.YGNodeCalculateLayout(Root.Yoga, w, h, YGDirection.LTR);
            Place(Root, 0, 0);
        }

        void Relayout() { Layout(viewW, viewH); }

        public void Paint(IPainter p) { PaintBox(p, Root); }

        // returns true when something changed that needs a repaint
        public bool Hover(float x, float y)
        {
            var hit = Root == null ? null : HitTest(Root, x, y);
            while (hit != null && hit.Action == null) hit = hit.Parent;
            if (hit != null && hovered != null && hit.Element == hovered.Element) return false;
            if (hit == null && hovered == null) return false;
            if (hovered != null) hovered.Element.ClassList.Remove("is-hover");
            hovered = hit;
            if (hovered != null) hovered.Element.ClassList.Add("is-hover");
            Relayout();
            return true;
        }

        public void Click(float x, float y)
        {
            var hit = Root == null ? null : HitTest(Root, x, y);
            while (hit != null && hit.Action == null) hit = hit.Parent;
            if (hit != null && Action != null) Action(hit.Action, hit);
        }

        // text of an element can be replaced at run time (data binding in its simplest form)
        public void SetText(string id, string text)
        {
            var e = doc.GetElementById(id);
            if (e != null) { e.TextContent = text; Relayout(); }
        }

        // ---- build ----
        Box Build(INode n, Box parent)
        {
            var b = new Box { Parent = parent };
            var el = n as IElement;
            if (el == null) return b;
            b.Element = el; b.Tag = el.LocalName; b.Id = el.Id; b.Action = el.GetAttribute("data-action");
            foreach (var cls in el.ClassList) if (cls.StartsWith("i-")) b.Icon = Icons.Find(cls.Substring(2));
            var cs = styles.ComputeDeclarations(el);
            b.Yoga = YGNodeAPI.YGNodeNew();
            ApplyStyle(b, cs, parent);
            if (cs.GetPropertyValue("display") == "none") YGNodeStyleAPI.YGNodeStyleSetDisplay(b.Yoga, YGDisplay.None);
            foreach (var c in el.ChildNodes)
            {
                var t = c as IText;
                if (t != null)
                {
                    var s = System.Text.RegularExpressions.Regex.Replace(t.Data, "\\s+", " ").Trim();
                    if (s.Length == 0) continue;
                    if (b.Upper) s = s.ToUpperInvariant();
                    var tb = new Box { Parent = b, Text = s, Tag = "#text", Color = b.Color, FontSize = b.FontSize, Bold = b.Bold, LineHeight = b.LineHeight, Element = el };
                    tb.Yoga = YGNodeAPI.YGNodeNew();
                    YGNodeStyleAPI.YGNodeStyleSetFlexShrink(tb.Yoga, 1);
                    var capture = tb;
                    YGNodeAPI.YGNodeSetMeasureFunc(tb.Yoga, (node, aw, wm, ah, hm) => MeasureRun(capture, aw, wm));
                    b.Children.Add(tb);
                    YGNodeAPI.YGNodeInsertChild(b.Yoga, tb.Yoga, (nuint)(b.Children.Count - 1));
                }
                else if (c is IElement)
                {
                    var cb = Build(c, b);
                    b.Children.Add(cb);
                    YGNodeAPI.YGNodeInsertChild(b.Yoga, cb.Yoga, (nuint)(b.Children.Count - 1));
                }
            }
            return b;
        }

        YGSize MeasureRun(Box tb, float availW, MeasureMode mode)
        {
            float max = mode == MeasureMode.Undefined ? 1e9f : availW;
            tb.Lines = Wrap(tb.Text, tb.FontSize, tb.Bold, max);
            float w = 0;
            foreach (var l in tb.Lines) { float lw, lh; measurer.MeasureText(l, tb.FontSize, tb.Bold, out lw, out lh); if (lw > w) w = lw; }
            float lineH = tb.LineHeight > 0 ? tb.LineHeight : tb.FontSize * 1.3f;
            return new YGSize { Width = (float)Math.Ceiling(w), Height = lineH * tb.Lines.Count };
        }

        List<string> Wrap(string text, float size, bool bold, float maxW)
        {
            var lines = new List<string>(); var cur = "";
            foreach (var word in text.Split(' '))
            {
                var trial = cur.Length == 0 ? word : cur + " " + word;
                float w, h; measurer.MeasureText(trial, size, bold, out w, out h);
                if (w > maxW && cur.Length > 0) { lines.Add(cur); cur = word; } else cur = trial;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }

        void Place(Box b, float ox, float oy)
        {
            b.X = ox + YGNodeLayoutAPI.YGNodeLayoutGetLeft(b.Yoga);
            b.Y = oy + YGNodeLayoutAPI.YGNodeLayoutGetTop(b.Yoga);
            b.W = YGNodeLayoutAPI.YGNodeLayoutGetWidth(b.Yoga);
            b.H = YGNodeLayoutAPI.YGNodeLayoutGetHeight(b.Yoga);
            foreach (var c in b.Children) Place(c, b.X, b.Y);
        }

        // a child of a clipping box is drawn (and clickable) only when it fits inside completely
        static bool Fits(Box parent, Box child)
        {
            return child.X >= parent.X - 0.5f && child.Y >= parent.Y - 0.5f && child.X + child.W <= parent.X + parent.W + 0.5f && child.Y + child.H <= parent.Y + parent.H + 0.5f;
        }

        static Box HitTest(Box b, float x, float y)
        {
            if (b.Tag == "#text") return null;
            if (x < b.X || y < b.Y || x >= b.X + b.W || y >= b.Y + b.H) return null;
            for (int i = b.Children.Count - 1; i >= 0; i--) { if (b.Clip && !Fits(b, b.Children[i])) continue; var h = HitTest(b.Children[i], x, y); if (h != null) return h; }
            return b;
        }

        void PaintBox(IPainter p, Box b)
        {
            if (b.Tag == "#text")
            {
                float lineH = b.LineHeight > 0 ? b.LineHeight : b.FontSize * 1.3f;
                for (int i = 0; i < b.Lines.Count; i++) p.DrawText(b.Lines[i], b.X, b.Y + i * lineH, b.FontSize, b.Bold, b.Color);
                return;
            }
            bool border = b.BorderWidth > 0 && b.BorderColor.Visible;
            if (b.HasGradient) p.FillGradientCorners(b.X, b.Y, b.W, b.H, b.GradA, b.GradB, b.GradHorizontal, b.Radius, b.RadiusBottom);
            else if (b.Background.Visible) p.FillRectCorners(b.X, b.Y, b.W, b.H, b.Background, b.Radius, b.RadiusBottom);
            if (border) p.StrokeRect(b.X, b.Y, b.W, b.H, b.BorderColor, b.BorderWidth, b.Radius);
            if (b.Icon >= 0) p.DrawIcon(b.Icon, b.X, b.Y, Math.Min(b.W, b.H), b.Color);
            foreach (var c in b.Children) { if (b.Clip && !Fits(b, c)) continue; PaintBox(p, c); }
        }

        // ---- CSS to Yoga ----
        static void ApplyStyle(Box b, ICssStyleDeclaration cs, Box parent)
        {
            var n = b.Yoga;
            YGNodeStyleAPI.YGNodeStyleSetFlexShrink(n, 0);   // containers keep their content size; text runs shrink (set below) so they wrap
            var fd = cs.GetPropertyValue("flex-direction");
            YGNodeStyleAPI.YGNodeStyleSetFlexDirection(n, fd == "column" ? YGFlexDirection.Column
                : fd == "column-reverse" ? YGFlexDirection.ColumnReverse
                : fd == "row-reverse" ? YGFlexDirection.RowReverse : YGFlexDirection.Row);
            if (cs.GetPropertyValue("flex-wrap") == "wrap") YGNodeStyleAPI.YGNodeStyleSetFlexWrap(n, YGWrap.Wrap);
            YGNodeStyleAPI.YGNodeStyleSetJustifyContent(n, Justify(cs.GetPropertyValue("justify-content")));
            var ai = cs.GetPropertyValue("align-items"); if (ai.Length > 0) YGNodeStyleAPI.YGNodeStyleSetAlignItems(n, Align(ai));
            var asf = cs.GetPropertyValue("align-self"); if (asf.Length > 0) YGNodeStyleAPI.YGNodeStyleSetAlignSelf(n, Align(asf));
            if (cs.GetPropertyValue("position") == "absolute") YGNodeStyleAPI.YGNodeStyleSetPositionType(n, YGPositionType.Absolute);
            Dim(cs.GetPropertyValue("width"), (v, pct) => { if (pct) YGNodeStyleAPI.YGNodeStyleSetWidthPercent(n, v); else YGNodeStyleAPI.YGNodeStyleSetWidth(n, v); });
            Dim(cs.GetPropertyValue("height"), (v, pct) => { if (pct) YGNodeStyleAPI.YGNodeStyleSetHeightPercent(n, v); else YGNodeStyleAPI.YGNodeStyleSetHeight(n, v); });
            Dim(cs.GetPropertyValue("min-width"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetMinWidth(n, v));
            Dim(cs.GetPropertyValue("min-height"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetMinHeight(n, v));
            float fg; if (float.TryParse(cs.GetPropertyValue("flex-grow"), NumberStyles.Float, CultureInfo.InvariantCulture, out fg)) YGNodeStyleAPI.YGNodeStyleSetFlexGrow(n, fg);
            float fs; if (float.TryParse(cs.GetPropertyValue("flex-shrink"), NumberStyles.Float, CultureInfo.InvariantCulture, out fs)) YGNodeStyleAPI.YGNodeStyleSetFlexShrink(n, fs);
            Dim(cs.GetPropertyValue("flex-basis"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetFlexBasis(n, v));
            Dim(cs.GetPropertyValue("gap"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetGap(n, YGGutter.All, v));
            Dim(cs.GetPropertyValue("row-gap"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetGap(n, YGGutter.Row, v));
            Dim(cs.GetPropertyValue("column-gap"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetGap(n, YGGutter.Column, v));
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                var edge = side == "top" ? YGEdge.Top : side == "right" ? YGEdge.Right : side == "bottom" ? YGEdge.Bottom : YGEdge.Left;
                Dim(cs.GetPropertyValue("padding-" + side), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetPadding(n, edge, v));
                Dim(cs.GetPropertyValue("margin-" + side), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetMargin(n, edge, v));
                Dim(cs.GetPropertyValue("border-" + side + "-width"), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetBorder(n, edge, v));
                Dim(cs.GetPropertyValue(side), (v, pct) => YGNodeStyleAPI.YGNodeStyleSetPosition(n, edge, v));
            }
            b.Background = Color(cs.GetPropertyValue("background-color"), new Rgba(0, 0, 0, 0));
            b.Color = Color(cs.GetPropertyValue("color"), parent != null ? parent.Color : new Rgba(255, 255, 255));
            b.BorderColor = Color(cs.GetPropertyValue("border-top-color"), new Rgba(0, 0, 0, 0));
            float bw = 0; Dim(cs.GetPropertyValue("border-top-width"), (v, pct) => bw = v); b.BorderWidth = bw;
            if (cs.GetPropertyValue("border-top-style") == "none") b.BorderWidth = 0;
            string ov = cs.GetPropertyValue("overflow"); b.Clip = ov == "hidden" || ov == "auto" || ov == "scroll";
            float rad = 0; Dim(cs.GetPropertyValue("border-top-left-radius"), (v, pct) => rad = v); b.Radius = rad;
            float radB = 0; Dim(cs.GetPropertyValue("border-bottom-left-radius"), (v, pct) => radB = v); b.RadiusBottom = radB;
            float fsz = parent != null ? parent.FontSize : 16; Dim(cs.GetPropertyValue("font-size"), (v, pct) => fsz = v); b.FontSize = fsz;
            var fw = cs.GetPropertyValue("font-weight"); int wv; if (fw.Length == 0 && parent != null) fw = parent.Bold ? "bold" : "";
            b.Bold = fw == "bold" || fw == "bolder" || (int.TryParse(fw, out wv) && wv >= 600);
            float lh = parent != null ? parent.LineHeight : 0; Dim(cs.GetPropertyValue("line-height"), (v, pct) => lh = v); b.LineHeight = lh;
            var tt = cs.GetPropertyValue("text-transform"); b.Upper = tt.Length > 0 ? tt == "uppercase" : (parent != null && parent.Upper);
            var bg = cs.GetPropertyValue("background-image"); if (bg.Length == 0) bg = cs.GetPropertyValue("background");
            if (bg.StartsWith("linear-gradient")) ParseGradient(b, bg);
        }

        // linear-gradient(<angle>?, stop, stop, ...): first and last colour; 90deg or "to right" runs left to right, anything else top to bottom
        static void ParseGradient(Box b, string css)
        {
            string inner = css.Substring(css.IndexOf('(') + 1).TrimEnd(')');
            var parts = new List<string>(); int depth = 0; var cur = new System.Text.StringBuilder();
            foreach (char ch in inner)
            {
                if (ch == '(') depth++; else if (ch == ')') depth--;
                if (ch == ',' && depth == 0) { parts.Add(cur.ToString().Trim()); cur.Clear(); } else cur.Append(ch);
            }
            parts.Add(cur.ToString().Trim());
            bool horizontal = false;
            if (parts[0].EndsWith("deg") || parts[0].StartsWith("to "))
            {
                horizontal = parts[0] == "90deg" || parts[0] == "270deg" || parts[0] == "to right" || parts[0] == "to left";
                parts.RemoveAt(0);
            }
            if (parts.Count < 2) return;
            Func<string, Rgba> col = p => { int sp = p.LastIndexOf(' '); if (sp > 0 && p.EndsWith("%")) p = p.Substring(0, sp); return Color(p, new Rgba(0, 0, 0, 0)); };
            b.GradA = col(parts[0]); b.GradB = col(parts[parts.Count - 1]); b.GradHorizontal = horizontal; b.HasGradient = true;
        }

        static void Dim(string s, Action<float, bool> set)
        {
            if (string.IsNullOrEmpty(s) || s == "auto" || s == "normal" || s == "none") return;
            bool pct = s.EndsWith("%");
            s = s.Replace("px", "").Replace("%", "");
            float v; if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) set(v, pct);
        }

        static YGJustify Justify(string s)
        {
            switch (s)
            {
                case "center": return YGJustify.Center;
                case "flex-end": case "end": return YGJustify.FlexEnd;
                case "space-between": return YGJustify.SpaceBetween;
                case "space-around": return YGJustify.SpaceAround;
                case "space-evenly": return YGJustify.SpaceEvenly;
                default: return YGJustify.FlexStart;
            }
        }

        static YGAlign Align(string s)
        {
            switch (s)
            {
                case "center": return YGAlign.Center;
                case "flex-end": case "end": return YGAlign.FlexEnd;
                case "flex-start": case "start": return YGAlign.FlexStart;
                case "baseline": return YGAlign.Baseline;
                case "stretch": return YGAlign.Stretch;
                default: return YGAlign.Auto;
            }
        }

        public static Rgba ParseColor(string s) { return Color(s, new Rgba(255, 0, 255)); }

        static Rgba Color(string s, Rgba fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s == "transparent") return new Rgba(0, 0, 0, 0);
            if (s.StartsWith("#"))
            {
                string h = s.Substring(1);
                if (h.Length == 3) h = "" + h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
                if (h.Length == 6) h += "ff";
                if (h.Length == 8) return new Rgba(Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16), Convert.ToByte(h.Substring(6, 2), 16));
            }
            if (s.StartsWith("rgb"))
            {
                var parts = s.Substring(s.IndexOf('(') + 1).TrimEnd(')').Split(new[] { ',', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                {
                    float a = 1; if (parts.Length >= 4) float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a);
                    return new Rgba(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), (byte)Math.Round(a * 255));
                }
            }
            return fallback;
        }
    }
}
