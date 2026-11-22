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
        public static float RcGain = 1.2f;     // the same for radiance cascades
        public static float RcStart = 2f;      // length of the first cascade's rays, in scene pixels (each cascade reaches 4x farther)
        public const int PxPerTile = 4;        // resolution of the cascade scene
        public const int CascadeCount = 3;
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
            if (bp.Walls == null || floor >= bp.Walls.Length) return 0;
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
            int hash = tiles * 31 + floors;
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

            var previous = gd.GetRenderTargets();
            var viewport = gd.Viewport;
            gd.BlendState = BlendState.Opaque;
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = RasterizerState.CullNone;
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
            RcInfo = "scene=" + sw + "x" + sh + " floors=" + floors + " reachPx=" + reach;
            Runs++;
        }

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

        public static string Status
        {
            get { return "light=" + RenderSettings.LightName(PostAA.Settings.Light) + " runs=" + Runs + " reach=" + TilesReach + " gain=" + Gain + " broken=" + broken + " err=" + LastError; }
        }

        public static string Command(string[] a)
        {
            var cmd = a.Length > 0 ? a[0] : "status";
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (cmd)
            {
                case "status": return Status;
                case "mode": PostAA.Settings.Light = RenderSettings.ParseLight(a.Length > 1 ? a[1] : "off"); PostAA.Settings.Save(); broken = false; return Status;
                case "rc":
                    if (a.Length > 1) RcGain = float.Parse(a[1], inv);
                    if (a.Length > 2) RcStart = float.Parse(a[2], inv);
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
