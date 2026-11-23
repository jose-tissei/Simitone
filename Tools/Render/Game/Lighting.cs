using System;
using FSO.LotView;
using FSO.LotView.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Render
{
    // Dynamic lighting additions. The engine bakes the lot's lighting into a plan-view light map (rgb = light intensity, a = with
    // shadows, one tile of the atlas per floor) and hands it to every world shader in WorldState.PrepareLighting. The patched game
    // calls AfterPrepare at the end of that method; with a lighting mode on, the light map is run through a bounce pass and the
    // result replaces the texture the shaders sample. It never throws into the game: a problem turns the pass off and logs once.
    public static class Lighting
    {
        static Effect fx;
        static VertexBuffer quad;
        static RenderTarget2D bounce, combined;
        static GraphicsDevice device;
        static bool broken;
        public static long Runs;
        public static string LastError = "";
        public static float TilesReach = 3f;   // how far light bounces, in tiles
        public static float Gain = 0.6f;       // how much of the gathered light is added
        public static float RcGain = 150f;     // the same for radiance cascades
        public static float EmitScale = 1f;  // radiance of a lamp's glowing disc
        public static float DiscTiles = 3f;      // radius of that disc, in tiles at most
        public static float RcStart = 4f;      // length of the first cascade's rays, in scene pixels (each cascade reaches 4x farther)
        public const int PxPerTile = 8;        // resolution of the cascade scene
        public const int CascadeCount = 3;
        const int MaxLights = 48;
        static int lampCount;
        static Effect rc;
        static GraphicsDevice rcDevice;
        static RenderTarget2D scene;
        static RenderTarget2D[] cascades = new RenderTarget2D[CascadeCount];
        static Texture2D wallTexture;
        static int wallHash, sceneW, sceneH;
        public static string RcInfo = "";

        public static void Reset() { broken = false; }

        public static void AfterPrepare(WorldState state)
        {
            try
            {
                if (broken || state == null || PostAA.Settings.Light == LightMode.Off) return;
                var light = state.Light;
                var map = light == null ? null : light.LightMap;
                if (map == null || map.IsDisposed) return;
                lastMap = map; lastState = state;
                var gd = map.GraphicsDevice;
                if (gd == null || gd.IsDisposed || gd.GraphicsProfile != GraphicsProfile.HiDef) return;   // the effect needs feature level 10
                Prepare(gd, map.Width, map.Height);
                if (PostAA.Settings.Light == LightMode.Cascades) { RunCascades(gd, state, map); return; }

                var previous = gd.GetRenderTargets();
                var viewport = gd.Viewport;
                float pxPerTile = map.Width / 3f / Math.Max(1, light.Blueprint.Width);
                gd.BlendState = BlendState.Opaque;
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;
                fx.Parameters["Texel"].SetValue(new Vector2(1f / map.Width, 1f / map.Height));
                fx.Parameters["PixRadius"].SetValue(TilesReach * pxPerTile);
                fx.Parameters["Gain"].SetValue(Gain);
                fx.Parameters["lightTexture"].SetValue(map);

                gd.SetRenderTarget(bounce);
                Pass("Gather");
                fx.Parameters["bounceTexture"].SetValue(bounce);
                gd.SetRenderTarget(combined);
                Pass("Combine");

                if (previous == null || previous.Length == 0) gd.SetRenderTarget(null); else gd.SetRenderTargets(previous);
                gd.Viewport = viewport;
                fx.Parameters["lightTexture"].SetValue((Texture2D)null);
                fx.Parameters["bounceTexture"].SetValue((Texture2D)null);

                foreach (var e in WorldContent.LightEffects) e.AdvancedLight = combined;
                Runs++;
            }
            catch (Exception ex)
            {
                broken = true;
                LastError = ex.GetType().Name + ": " + ex.Message;
                PostAA.Log("lighting turned itself off: " + LastError);
            }
        }

        // walls of a floor as flags per tile, doors left open
        static int FlagsAt(Blueprint bp, int floor, int x, int y)
        {
            if (ignoreWalls || bp.Walls == null || floor >= bp.Walls.Length) return 0;
            var arr = bp.Walls[floor];
            int off = y * bp.Width + x;
            if (arr == null || off >= arr.Length) return 0;
            var w = arr[off];
            int f = 0;
            if (w.TopLeftSolid) f |= LightGrid.TopLeft;
            if (w.TopRightSolid) f |= LightGrid.TopRight;
            if ((w.Segments & WallSegments.BottomRight) != 0) f |= LightGrid.BottomRight;
            if ((w.Segments & WallSegments.BottomLeft) != 0) f |= LightGrid.BottomLeft;
            if ((w.Segments & WallSegments.HorizontalDiag) != 0) f |= LightGrid.DiagH;
            if ((w.Segments & WallSegments.VerticalDiag) != 0) f |= LightGrid.DiagV;
            return f;
        }

        static void RunCascades(GraphicsDevice gd, WorldState state, RenderTarget2D map)
        {
            var bp = state.Light.Blueprint;
            int tiles = bp.Width - 1;
            int floors = Math.Min(bp.Walls == null ? 0 : bp.Walls.Length, 6);
            int fp = LightGrid.FloorPixels(tiles, PxPerTile);
            int sw = fp * LightGrid.AtlasWidth, sh = fp * LightGrid.AtlasHeight;

            if (rc == null || rcDevice != gd) { rc = new Effect(gd, PostAA.Resource("Cascades.mgfxo")); rcDevice = gd; scene = null; wallTexture = null; PostAA.Log("lighting ready (radiance cascades effect loaded)"); }
            if (scene == null || scene.IsDisposed || sceneW != sw || sceneH != sh)
            {
                DisposeRc();
                scene = new RenderTarget2D(gd, sw, sh, false, SurfaceFormat.Color, DepthFormat.None);
                for (int i = 0; i < CascadeCount; i++) cascades[i] = new RenderTarget2D(gd, sw, sh, false, SurfaceFormat.HalfVector4, DepthFormat.None);
                sceneW = sw; sceneH = sh; wallHash = 0; wallTexture = null;
            }

            // the wall picture only changes when the walls do
            int hash = tiles * 31 + floors + (ignoreWalls ? 7777 : 0);
            for (int f = 0; f < floors; f++)
            {
                var arr = bp.Walls[f];
                if (arr == null) continue;
                for (int i = 0; i < arr.Length; i++) { var w = arr[i]; hash = hash * 31 + (int)w.Segments + (w.TopLeftDoor ? 64 : 0) + (w.TopRightDoor ? 128 : 0); }
            }
            if (wallTexture == null || hash != wallHash)
            {
                int ww, wh;
                var a = LightGrid.Rasterize(floors, tiles, PxPerTile, (f, x, y) => FlagsAt(bp, f, x, y), out ww, out wh);
                var colors = new Color[a.Length];
                for (int i = 0; i < a.Length; i++) colors[i] = new Color(0, 0, 0, (int)a[i]);
                if (wallTexture == null || wallTexture.IsDisposed || wallTexture.Width != ww || wallTexture.Height != wh) wallTexture = new Texture2D(gd, ww, wh, false, SurfaceFormat.Color);
                wallTexture.SetData(colors);
                wallHash = hash;
            }

            // the lamps: point lights of rooms (not the window lights, which carry the sun and moon)
            var posR = new Vector4[MaxLights]; var cols = new Vector4[MaxLights]; int nLights = 0;
            if (bp.Light != null)
                foreach (var room in bp.Light)
                {
                    if (room == null) continue;
                    foreach (var ld in room.Lights)
                    {
                        if (nLights >= MaxLights) break;
                        if (ld.LightType != FSO.LotView.LMap.LightType.ROOM || ld.OutdoorsColor || ld.Level < 0 || ld.Level >= floors) continue;
                        float ox = (ld.Level % LightGrid.AtlasWidth) * fp, oy = (ld.Level / LightGrid.AtlasWidth) * fp;
                        posR[nLights] = new Vector4(ox + ld.LightPos.X / 16f * PxPerTile, oy + ld.LightPos.Y / 16f * PxPerTile, Math.Max(1f, Math.Min(DiscTiles, ld.LightSize / 16f) * PxPerTile), 0);
                        var c = ld.LightColor.ToVector3() * ld.LightIntensity;
                        cols[nLights] = new Vector4(c, 0);
                        nLights++;
                    }
                }

            var previous = gd.GetRenderTargets();
            var viewport = gd.Viewport;
            gd.BlendState = BlendState.Opaque;
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = RasterizerState.CullNone;
            rc.Parameters["LightCount"].SetValue((float)nLights);
            rc.Parameters["LightPosR"].SetValue(posR);
            rc.Parameters["LightColor"].SetValue(cols);
            rc.Parameters["EmitScale"].SetValue(EmitScale);
            rc.Parameters["SceneSize"].SetValue(new Vector2(sw, sh));
            rc.Parameters["FloorPx"].SetValue((float)fp);
            rc.Parameters["Tiles"].SetValue((float)tiles);
            rc.Parameters["PxPerTile"].SetValue((float)PxPerTile);
            rc.Parameters["LightTexel"].SetValue(new Vector2(1f / map.Width, 1f / map.Height));
            rc.Parameters["Gain"].SetValue(RcGain);
            rc.Parameters["lightTexture"].SetValue(map);
            rc.Parameters["wallTexture"].SetValue(wallTexture);

            // cascade i covers rays from L0 (4^i - 1) / 3 to L0 (4^(i+1) - 1) / 3 pixels
            float reach = RcStart * ((float)Math.Pow(4, CascadeCount) - 1) / 3f;
            rc.Parameters["Reach"].SetValue(reach);
            gd.SetRenderTarget(scene);
            RcPass("Scene");
            rc.Parameters["sceneTexture"].SetValue(scene);
            for (int i = CascadeCount - 1; i >= 0; i--)
            {
                rc.Parameters["Block"].SetValue((float)(2 << i));
                rc.Parameters["Start"].SetValue(RcStart * ((float)Math.Pow(4, i) - 1) / 3f);
                rc.Parameters["End"].SetValue(RcStart * ((float)Math.Pow(4, i + 1) - 1) / 3f);
                rc.Parameters["IsTop"].SetValue(i == CascadeCount - 1 ? 1f : 0f);
                if (i < CascadeCount - 1) rc.Parameters["upperTexture"].SetValue(cascades[i + 1]);
                gd.SetRenderTarget(cascades[i]);
                RcPass("Cascade");
            }
            rc.Parameters["cascadeTexture"].SetValue(cascades[0]);
            rc.Parameters["Debug"].SetValue((float)debugView);
            gd.SetRenderTarget(combined);
            RcPass("Combine");

            if (previous == null || previous.Length == 0) gd.SetRenderTarget(null); else gd.SetRenderTargets(previous);
            gd.Viewport = viewport;
            foreach (var n in new[] { "lightTexture", "wallTexture", "sceneTexture", "upperTexture", "cascadeTexture" }) rc.Parameters[n].SetValue((Texture2D)null);

            // simtool render light walls: show the wall picture instead of the light, to check it against the floor plan
            foreach (var e in WorldContent.LightEffects) e.AdvancedLight = combined;
            lampCount = nLights;
            RcInfo = "lamps=" + nLights + " scene=" + sw + "x" + sh + " floors=" + floors + " reachPx=" + reach;
            Runs++;
        }

        static bool ignoreWalls;   // simtool render light walls off: lets light through every wall, to show what the walls change
        static int debugView;   // simtool render light view walls|gather|off

        static void RcPass(string technique)
        {
            rc.CurrentTechnique = rc.Techniques[technique];
            rc.CurrentTechnique.Passes[0].Apply();
            device.SetVertexBuffer(quad);
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }

        static void DisposeRc()
        {
            if (scene != null) scene.Dispose();
            scene = null;
            for (int i = 0; i < CascadeCount; i++) { if (cascades[i] != null) cascades[i].Dispose(); cascades[i] = null; }
        }

        static void Pass(string technique)
        {
            fx.CurrentTechnique = fx.Techniques[technique];
            fx.CurrentTechnique.Passes[0].Apply();
            device.SetVertexBuffer(quad);
            device.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }

        static void Prepare(GraphicsDevice gd, int w, int h)
        {
            if (device != gd)
            {
                device = gd;
                fx = new Effect(gd, PostAA.Resource("Bounce.mgfxo"));
                var verts = new VertexPositionTexture[4];
                verts[0] = new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 0));
                verts[1] = new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 1));
                verts[2] = new VertexPositionTexture(new Vector3(1, -1, 0), new Vector2(1, 0));
                verts[3] = new VertexPositionTexture(new Vector3(1, 1, 0), new Vector2(1, 1));
                quad = new VertexBuffer(gd, typeof(VertexPositionTexture), 4, BufferUsage.None);
                quad.SetData(verts);
                bounce = null; combined = null;
                PostAA.Log("lighting ready (bounce effect loaded)");
            }
            if (bounce == null || bounce.Width != w || bounce.Height != h || bounce.IsDisposed)
            {
                if (bounce != null) bounce.Dispose();
                if (combined != null) combined.Dispose();
                bounce = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);
                combined = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            }
        }

        // simtool render light probe: reads the engine's light map back and prints a coarse grid of floor 0 (r,g,b,a averages, 0-255),
        // to see what the light map really holds (ambient level, lamp spill)
        static string Probe()
        {
            var map = lastMap;
            if (map == null) return "no light map seen yet";
            var data = new Color[map.Width * map.Height];
            map.GetData(data);
            int fw = map.Width / 3, fh = map.Height / 2;
            var sb = new System.Text.StringBuilder("lightmap " + map.Width + "x" + map.Height + " floor0 grid 12x8 (r,g,b,a): | ");
            for (int gy = 0; gy < 8; gy++)
            {
                for (int gx = 0; gx < 12; gx++)
                {
                    long r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int y = gy * fh / 8; y < (gy + 1) * fh / 8; y += 4)
                        for (int x = gx * fw / 12; x < (gx + 1) * fw / 12; x += 4) { var c = data[y * map.Width + x]; r += c.R; g += c.G; b += c.B; a += c.A; n++; }
                    sb.Append(r / n + "," + g / n + "," + b / n + "," + a / n + " ");
                }
                sb.Append(" | ");
            }
            return sb.ToString();
        }

        static WorldState lastState;

        // simtool render light dirprobe: the engine's light direction map (xyz = direction the light travels, w = total intensity), 12x8 grid of floor 0
        static string DirProbe()
        {
            var map = lastState == null ? null : lastState.Light.LightMapDirection;
            if (map == null) return "no direction map (directional lighting off?)";
            var data = new Microsoft.Xna.Framework.Graphics.PackedVector.HalfVector4[map.Width * map.Height];
            map.GetData(data);
            int fw = map.Width / 3, fh = map.Height / 2;
            var sb = new System.Text.StringBuilder("dirmap " + map.Width + "x" + map.Height + " " + map.Format + " (x,y,z,w x100): | ");
            for (int gy = 0; gy < 8; gy++)
            {
                for (int gx = 0; gx < 12; gx++)
                {
                    float x = 0, y = 0, z = 0, w = 0; int n = 0;
                    for (int py = gy * fh / 8; py < (gy + 1) * fh / 8; py += 2)
                        for (int px = gx * fw / 12; px < (gx + 1) * fw / 12; px += 2) { var v = data[py * map.Width + px].ToVector4(); x += v.X; y += v.Y; z += v.Z; w += v.W; n++; }
                    sb.Append((int)(100 * x / n) + "," + (int)(100 * y / n) + "," + (int)(100 * z / n) + "," + (int)(100 * w / n) + " ");
                }
                sb.Append(" | ");
            }
            return sb.ToString();
        }

        // simtool render light wallmap: the wall flags of floor 0 as text over the box that holds any wall (L = top-left edge, R = top-right,
        // B = both, + = other edges or diagonals, o = a door in the way), one row per tile row
        static string WallMap()
        {
            var bp = lastState == null ? null : lastState.Light.Blueprint;
            if (bp == null) return "no lot seen yet";
            int tiles = bp.Width - 1, x0 = tiles, y0 = tiles, x1 = -1, y1 = -1;
            for (int y = 0; y < tiles; y++) for (int x = 0; x < tiles; x++) if (FlagsAt(bp, 0, x, y) != 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            if (x1 < 0) return "no walls on floor 0";
            var sb = new System.Text.StringBuilder("tiles x " + x0 + ".." + x1 + " y " + y0 + ".." + y1 + " | ");
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int f = FlagsAt(bp, 0, x, y);
                    var w = bp.Walls[0][y * bp.Width + x];
                    bool door = w.TopLeftDoor || w.TopRightDoor;
                    sb.Append(door ? 'o' : f == 0 ? '.' : f == LightGrid.TopLeft ? 'L' : f == LightGrid.TopRight ? 'R' : f == (LightGrid.TopLeft | LightGrid.TopRight) ? 'B' : '+');
                }
                sb.Append(" | ");
            }
            return sb.ToString();
        }

        static RenderTarget2D lastMap;

        public static string Status
        {
            get { return "light=" + RenderSettings.LightName(PostAA.Settings.Light) + " " + RcInfo + " runs=" + Runs + " reach=" + TilesReach + " gain=" + Gain + " broken=" + broken + " err=" + LastError; }
        }

        public static string Command(string[] a)
        {
            var cmd = a.Length > 0 ? a[0] : "status";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (cmd)
            {
                case "status": return Status;
                case "mode": PostAA.Settings.Light = RenderSettings.ParseLight(a.Length > 1 ? a[1] : "off"); PostAA.Settings.Save(); broken = false; return Status;
                case "walls": ignoreWalls = a.Length > 1 && a[1] == "off"; return "walls " + (ignoreWalls ? "ignored" : "used");
                case "probe": return Probe();
                case "dirprobe": return DirProbe();
                case "wallmap": return WallMap();
                case "rc":
                    if (a.Length > 1) RcGain = float.Parse(a[1], inv);
                    if (a.Length > 2) RcStart = float.Parse(a[2], inv);
                    if (a.Length > 3) EmitScale = float.Parse(a[3], inv);
                    if (a.Length > 4) DiscTiles = float.Parse(a[4], inv);
                    return Status + " rcGain=" + RcGain + " rcStart=" + RcStart;
                case "view": debugView = a.Length > 1 && a[1] == "walls" ? 1 : (a.Length > 1 && a[1] == "gather" ? 2 : 0); return "view=" + debugView;
                case "tune":
                    if (a.Length > 1) TilesReach = float.Parse(a[1], inv);
                    if (a.Length > 2) Gain = float.Parse(a[2], inv);
                    return Status;
                default: throw new Exception("render light status|mode off|bounce|tune <tiles> <gain>");
            }
        }
    }
}
