namespace Simitone.HtmlUi
{
    public struct Rgba
    {
        public byte R, G, B, A;
        public Rgba(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        public bool Visible { get { return A > 0; } }
    }

    // What a host (the game, or the GDI preview tool) must provide to show a page.
    public interface IPainter
    {
        void FillRect(float x, float y, float w, float h, Rgba color, float radius);
        void StrokeRect(float x, float y, float w, float h, Rgba color, float thickness, float radius);
        void DrawText(string text, float x, float y, float size, bool bold, Rgba color);
        void MeasureText(string text, float size, bool bold, out float w, out float h);
    }
}
