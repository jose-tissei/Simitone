using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Render
{
    // SMAA 1x (edge detection, blending weights, neighbourhood blending) over the world render target.
    // The reference shader and its two lookup tables are the MIT-licensed ones from github.com/iryoku/smaa.
    public class SmaaPass
    {
        public static float Threshold = 0.2f;   // edge sensitivity (reference HIGH preset: 0.1; 0.2 keeps texture detail, measured)
        public static int BlendOverride;        // diagnostics: 0 = like the engine, 1 = alpha blend, 2 = opaque
        public static float Contrast = 2f;      // local contrast adaptation (reference: 2)
        Effect fx;
        Texture2D areaTex, searchTex;
        RenderTarget2D edges, weights;
        GraphicsDevice device;

        static byte[] Resource(string name)
        {
            using (var s = typeof(SmaaPass).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("embedded " + name + " is missing");
                var bytes = new byte[s.Length];
                int read = 0; while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                return bytes;
            }
        }

        void Prepare(GraphicsDevice gd, int w, int h)
        {
            if (device != gd)
            {
                device = gd;
                fx = new Effect(gd, Resource("Smaa.mgfxo"));
                // the tables are 8 bits per channel with 2 (area) and 1 (search) channels; stored as RGBA so every backend can read them
                var area = Resource("AreaTex.bin");
                var rgba = new byte[160 * 560 * 4];
                for (int i = 0; i < 160 * 560; i++) { rgba[i * 4] = area[i * 2]; rgba[i * 4 + 1] = area[i * 2 + 1]; rgba[i * 4 + 3] = 255; }
                areaTex = new Texture2D(gd, 160, 560, false, SurfaceFormat.Color); areaTex.SetData(rgba);
                var search = Resource("SearchTex.bin");
                var srgba = new byte[64 * 16 * 4];
                for (int i = 0; i < 64 * 16; i++) { srgba[i * 4] = search[i]; srgba[i * 4 + 3] = 255; }
                searchTex = new Texture2D(gd, 64, 16, false, SurfaceFormat.Color); searchTex.SetData(srgba);
                edges = null; weights = null;
                PostAA.Log("SMAA ready (effect and lookup tables loaded)");
            }
            if (edges == null || edges.Width != w || edges.Height != h || edges.IsDisposed)
            {
                if (edges != null) edges.Dispose();
                if (weights != null) weights.Dispose();
                edges = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);
                weights = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);
            }
        }

        void Pass(GraphicsDevice gd, string technique, VertexBuffer quad)
        {
            fx.CurrentTechnique = fx.Techniques[technique];
            fx.CurrentTechnique.Passes[0].Apply();
            gd.SetVertexBuffer(quad);
            gd.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
        }

        // draws the anti-aliased world into whatever render target is currently bound (the screen)
        public void Draw(GraphicsDevice gd, RenderTarget2D world, VertexBuffer quad, bool opaque)
        {
            Prepare(gd, world.Width, world.Height);
            var previous = gd.GetRenderTargets();
            var viewport = gd.Viewport;
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = RasterizerState.CullNone;
            fx.Parameters["SmaaThreshold"].SetValue(Threshold);
            fx.Parameters["SmaaContrast"].SetValue(Contrast);
            fx.Parameters["SmaaMetrics"].SetValue(new Vector4(1f / world.Width, 1f / world.Height, world.Width, world.Height));
            fx.Parameters["colorTexture"].SetValue(world);
            fx.Parameters["areaTexture"].SetValue(areaTex);
            fx.Parameters["searchTexture"].SetValue(searchTex);

            gd.BlendState = BlendState.Opaque;
            gd.SetRenderTarget(edges);
            gd.Clear(Color.Transparent);
            Pass(gd, "SmaaEdges", quad);

            fx.Parameters["edgesTexture"].SetValue(edges);
            gd.SetRenderTarget(weights);
            gd.Clear(Color.Transparent);
            Pass(gd, "SmaaWeights", quad);

            if (previous == null || previous.Length == 0) gd.SetRenderTarget(null); else gd.SetRenderTargets(previous);
            gd.Viewport = viewport;
            gd.BlendState = (BlendOverride == 1 ? false : BlendOverride == 2 ? true : opaque) ? BlendState.Opaque : BlendState.AlphaBlend;
            gd.DepthStencilState = DepthStencilState.None;
            gd.RasterizerState = RasterizerState.CullNone;
            fx.Parameters["blendTexture"].SetValue(weights);
            Pass(gd, "SmaaBlend", quad);
            // do not leave the world target bound as a texture for whoever draws next
            fx.Parameters["colorTexture"].SetValue((Texture2D)null);
            fx.Parameters["edgesTexture"].SetValue((Texture2D)null);
            fx.Parameters["blendTexture"].SetValue((Texture2D)null);
        }
    }
}
