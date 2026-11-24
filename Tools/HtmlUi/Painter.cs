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
        // two-colour gradient, top to bottom (or left to right when horizontal)
        void FillGradient(float x, float y, float w, float h, Rgba from, Rgba to, bool horizontal, float radius);
        void StrokeRect(float x, float y, float w, float h, Rgba color, float thickness, float radius);
        void DrawText(string text, float x, float y, float size, bool bold, Rgba color);
        void MeasureText(string text, float size, bool bold, out float w, out float h);
        // cell of the 8 x 8 icon sheet (docs/ui-redesign/skins/icons.png), tinted with color
        void DrawIcon(int cell, float x, float y, float size, Rgba color);
    }

    // order of the icon sheet, from design-tokens.json "iconSheet.order"
    public static class Icons
    {
        public static readonly string[] Order = {
            "save","home","quit","eye","cube","grid","chat","text","bulb","pie","gear","close","search","plus","minus","play",
            "ff","fff","pause","heart","people","person","star","gift","thought","job","mask","left","right","check","alert","coin",
            "rotate","layers","hammer","sofa","bed","chair","wall","brush","floor","roof","book","news","hand","note","kiss","mail",
            "food","bolt","drop","sun","moon","wc","smile","diamond","lock","trash","pencil","filter","sort","clock","cart","info" };

        public static int Find(string name) { return Array.IndexOf(Order, name); }
    }
}
