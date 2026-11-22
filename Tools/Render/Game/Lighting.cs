using System;
using FSO.LotView;
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
                case "tune":
                    if (a.Length > 1) TilesReach = float.Parse(a[1], inv);
                    if (a.Length > 2) Gain = float.Parse(a[2], inv);
                    return Status;
                default: throw new Exception("render light status|mode off|bounce|tune <tiles> <gain>");
            }
        }
    }
}
