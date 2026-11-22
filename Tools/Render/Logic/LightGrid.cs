using System;

namespace Simitone.Render
{
    // The walls of a lot as a picture the lighting shaders can march rays through. One atlas, laid out exactly like the engine's
    // light map (3 floors wide, 2 high), but at a few pixels per tile: a pixel is 255 where a wall blocks light, 0 elsewhere.
    // No game types here, so it is unit tested; the game side only supplies the wall flags per tile.
    public static class LightGrid
    {
        public const byte TopLeft = 1, TopRight = 2, BottomRight = 4, BottomLeft = 8, DiagH = 16, DiagV = 32;

        public const int AtlasWidth = 3, AtlasHeight = 2;

        // pixels of one floor, padded to a multiple of 16 so every cascade block lies inside a single floor
        public static int FloorPixels(int tiles, int pxPerTile)
        {
            int raw = Math.Max(1, tiles * pxPerTile);
            return (raw + 15) / 16 * 16;
        }

        // flagsAt(floor, tileX, tileY) -> wall flags of that tile (doors already left out). Returns width*height alpha values.
        // TopLeft is the tile's edge at x = tileX (running along y), TopRight the edge at y = tileY (running along x).
        public static byte[] Rasterize(int floors, int tiles, int pxPerTile, Func<int, int, int, int> flagsAt, out int width, out int height)
        {
            int fp = FloorPixels(tiles, pxPerTile);
            width = fp * AtlasWidth;
            height = fp * AtlasHeight;
            var px = new byte[width * height];
            for (int f = 0; f < floors && f < AtlasWidth * AtlasHeight; f++)
            {
                int ox = (f % AtlasWidth) * fp, oy = (f / AtlasWidth) * fp;
                for (int ty = 0; ty < tiles; ty++)
                    for (int tx = 0; tx < tiles; tx++)
                    {
                        int fl = flagsAt(f, tx, ty);
                        if (fl == 0) continue;
                        int x0 = ox + tx * pxPerTile, y0 = oy + ty * pxPerTile, x1 = x0 + pxPerTile, y1 = y0 + pxPerTile;
                        // lines run one pixel past each end so walls that meet at a corner leave no gap
                        if ((fl & TopLeft) != 0) Line(px, width, height, x0, y0, x0, y1);
                        if ((fl & TopRight) != 0) Line(px, width, height, x0, y0, x1, y0);
                        if ((fl & BottomRight) != 0) Line(px, width, height, x1, y0, x1, y1);
                        if ((fl & BottomLeft) != 0) Line(px, width, height, x0, y1, x1, y1);
                        if ((fl & DiagH) != 0) Line(px, width, height, x0, y0, x1, y1);
                        if ((fl & DiagV) != 0) Line(px, width, height, x1, y0, x0, y1);
                    }
            }
            return px;
        }

        // one-pixel line, inclusive at both ends
        static void Line(byte[] px, int w, int h, int x0, int y0, int x1, int y1)
        {
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= n; i++)
            {
                int x = n == 0 ? x0 : x0 + (x1 - x0) * i / n;
                int y = n == 0 ? y0 : y0 + (y1 - y0) * i / n;
                if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = 255;
            }
        }
    }
}
