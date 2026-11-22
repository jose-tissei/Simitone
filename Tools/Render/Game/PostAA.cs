using System;
using System.IO;
using System.Reflection;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Render
{
    // Anti-aliasing pass for the world. The patched game calls TryDraw at the very start of PPXDepthEngine.DrawBackbuffer (the
    // blit of the world render target to the screen, before the UI is drawn). When the pass is on it draws the world through
    // the FXAA effect and the original blit is skipped; otherwise it returns false and the game draws as it always did.
    // It never throws into the game: any problem turns the pass off and logs once.
    public static class PostAA
    {
        static RenderSettings settings;
        static Effect fxaa;
        static readonly SmaaPass smaa = new SmaaPass();
        static VertexBuffer quad;
        static GraphicsDevice quadDevice;
        static bool broken, warnedReach;
        public static string LastError = "";
        public static long Draws;

        public static RenderSettings Settings { get { return settings ?? (settings = RenderSettings.Load()); } }

        public static AaMode Mode
        {
            get { return Settings.Aa; }
            set { Settings.Aa = value; Settings.Save(); }
        }

        // for the Options menu: the mode name, whether anything is on, and the next mode in the cycle
        public static string ModeLabel { get { return Mode == AaMode.Fxaa ? "FXAA" : (Mode == AaMode.Smaa ? "SMAA" : "Off"); } }
        public static bool AaOn { get { return Mode != AaMode.Off; } }
        public static void Cycle() { Mode = RenderSettings.Next(Mode); broken = false; }

        // false = draw the world the original way
        public static bool TryDraw(float opacity, float scale)
        {
            try
            {
                if (broken || Settings.Aa == AaMode.Off) return false;
                // zoom transitions and fades use the original blit; the SSAA path (render scale above 1) is the engine's own
                if (opacity < 0.999f || Math.Abs(scale - 1f) > 0.0001f || PPXDepthEngine.SSAA > 1) return false;
                var bb = PPXDepthEngine.GetBackbuffer();
                if (bb == null) return false;
                var gd = bb.GraphicsDevice;
                if (gd == null || gd.IsDisposed) return false;
                Prepare(gd);
                if (fxaa == null) return false;

                var mode = Settings.Aa;
                if (mode == AaMode.Smaa && gd.GraphicsProfile != GraphicsProfile.HiDef)
                {
                    // the SMAA shaders need Direct3D feature level 10 or higher
                    if (!warnedReach) { warnedReach = true; Log("SMAA needs the HiDef graphics profile (device runs " + gd.GraphicsProfile + "); using FXAA instead"); }
                    mode = AaMode.Fxaa;
                }
                if (mode == AaMode.Smaa)
                {
                    smaa.Draw(gd, bb, quad, !PPXDepthEngine.WithOpacity);
                    Draws++;
                    return true;
                }

                gd.BlendState = PPXDepthEngine.WithOpacity ? BlendState.AlphaBlend : BlendState.Opaque;   // exactly what the original blit uses
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;
                fxaa.CurrentTechnique = fxaa.Techniques[0];
                fxaa.Parameters["FxaaTexel"].SetValue(new Vector2(1f / bb.Width, 1f / bb.Height));
                fxaa.Parameters["tex"].SetValue(bb);
                fxaa.CurrentTechnique.Passes[0].Apply();
                gd.SetVertexBuffer(quad);
                gd.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
                Draws++;
                return true;
            }
            catch (Exception e)
            {
                broken = true;
                LastError = e.GetType().Name + ": " + e.Message;
                Log("post AA turned itself off: " + LastError);
                return false;
            }
        }

        static void Prepare(GraphicsDevice gd)
        {
            if (fxaa != null && quad != null && quadDevice == gd) return;
            quadDevice = gd;
            Log("graphics profile: " + gd.GraphicsProfile + ", adapter: " + gd.Adapter.Description);
            using (var s = typeof(PostAA).Assembly.GetManifestResourceStream("Fxaa.mgfxo"))
            {
                if (s == null) throw new FileNotFoundException("embedded Fxaa.mgfxo is missing");
                var bytes = new byte[s.Length];
                int read = 0; while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                fxaa = new Effect(gd, bytes);
            }
            // a full-screen strip; the effect's vertex shader flips v the same way the game's SSAA effect expects
            var verts = new VertexPositionTexture[4];
            verts[0] = new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 0));
            verts[1] = new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 1));
            verts[2] = new VertexPositionTexture(new Vector3(1, -1, 0), new Vector2(1, 0));
            verts[3] = new VertexPositionTexture(new Vector3(1, 1, 0), new Vector2(1, 1));
            quad = new VertexBuffer(gd, typeof(VertexPositionTexture), 4, BufferUsage.None);
            quad.SetData(verts);
            Log("post AA ready (FXAA effect loaded)");
        }

        public static void Log(string msg)
        {
            try
            {
                var dir = Path.GetDirectoryName(typeof(PostAA).Assembly.Location);
                File.AppendAllText(Path.Combine(dir, "Content", "render.log"), DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        // simtool render status | aa off|fxaa
        public static string Command(string[] a)
        {
            var cmd = a.Length > 0 ? a[0] : "status";
            switch (cmd)
            {
                case "status": return "aa=" + RenderSettings.AaName(Mode) + " draws=" + Draws + " broken=" + broken + " err=" + LastError;
                case "aa": Mode = RenderSettings.ParseAa(a.Length > 1 ? a[1] : "off"); broken = false; return "aa=" + RenderSettings.AaName(Mode);
                default: throw new Exception("render status|aa off|fxaa|smaa");
            }
        }
    }
}
