using System;
using System.IO;
using System.Linq;
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
        static TimeSpan? savedStep;
        public static string LastError = "";
        public static long Draws;
        public static int Repeat = 1;   // measurements only: run the pass this many times per frame to make its cost visible
        public static long Calls;     // every world blit, whether or not a pass ran (frame counter for measurements)
        static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public static RenderSettings Settings { get { return settings ?? (settings = RenderSettings.Load()); } }

        public static AaMode Mode
        {
            get { return Settings.Aa; }
            set { Settings.Aa = value; Settings.Save(); }
        }

        // the embedded effect and table files
        internal static byte[] Resource(string name)
        {
            using (var s = typeof(PostAA).Assembly.GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("embedded " + name + " is missing");
                var bytes = new byte[s.Length];
                int read = 0;
                while (read < bytes.Length) { int n = s.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                return bytes;
            }
        }

        // for the Options menu: the mode name, whether anything is on, and the next mode in the cycle
        public static string ModeLabel { get { return Mode == AaMode.Fxaa ? "FXAA" : (Mode == AaMode.Smaa ? "SMAA" : "Off"); } }
        public static bool AaOn { get { return Mode != AaMode.Off; } }
        public static string LightLabel { get { return Settings.Light == LightMode.Off && !Settings.Shading ? "Off" : (Settings.Light == LightMode.Cascades && Settings.Shading ? "On" : "Custom"); } }
        public static bool LightOn { get { return Settings.Light != LightMode.Off || Settings.Shading; } }
        public static void CycleLight() { Settings.CycleDynamicLighting(); Settings.Save(); Lighting.Reset(); Normals.Reset(); }
        public static void Cycle() { Mode = RenderSettings.Next(Mode); broken = false; }

        // false = draw the world the original way
        public static bool TryDraw(float opacity, float scale)
        {
            Calls++;
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
                    for (int r = 0; r < Repeat; r++) smaa.Draw(gd, bb, quad, !PPXDepthEngine.WithOpacity);
                    Draws++;
                    return true;
                }

                gd.BlendState = PPXDepthEngine.WithOpacity ? BlendState.AlphaBlend : BlendState.Opaque;   // exactly what the original blit uses
                gd.DepthStencilState = DepthStencilState.None;
                gd.RasterizerState = RasterizerState.CullNone;
                fxaa.CurrentTechnique = fxaa.Techniques[0];
                fxaa.Parameters["FxaaTexel"].SetValue(new Vector2(1f / bb.Width, 1f / bb.Height));
                fxaa.Parameters["tex"].SetValue(bb);
                for (int r = 0; r < Repeat; r++)
                {
                    fxaa.CurrentTechnique.Passes[0].Apply();
                    gd.SetVertexBuffer(quad);
                    gd.DrawPrimitives(PrimitiveType.TriangleStrip, 0, 2);
                }
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

        // measurements only: with vsync and the fixed time step on, the frame rate is capped and the cost of a pass cannot be seen
        static string Vsync(bool on)
        {
            Type facade = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { facade = asm.GetType("FSO.Client.GameFacade", false); if (facade != null) break; }
            var game = (facade.GetField("Game", BindingFlags.Static | BindingFlags.Public) != null ? facade.GetField("Game", BindingFlags.Static | BindingFlags.Public).GetValue(null) : facade.GetProperty("Game", BindingFlags.Static | BindingFlags.Public).GetValue(null, null)) as Game;
            if (game == null) throw new Exception("no game");
            // the game sets IsFixedTimeStep back every frame, so the cap is lifted through the target frame time instead
            if (savedStep == null) savedStep = game.TargetElapsedTime;
            game.TargetElapsedTime = on ? savedStep.Value : TimeSpan.FromTicks(1000);
            object gfx = null;   // a protected field of the engine's Game base class
            for (var t = game.GetType(); t != null && gfx == null; t = t.BaseType)
            {
                var f = t.GetField("Graphics", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) gfx = f.GetValue(game);
            }
            var dm = (GraphicsDeviceManager)gfx;
            dm.SynchronizeWithVerticalRetrace = on;
            dm.ApplyChanges();
            return "vsync=" + on;
        }

        // simtool render status | aa off|fxaa
        public static string Command(string[] a)
        {
            var cmd = a.Length > 0 ? a[0] : "status";
            switch (cmd)
            {
                case "status": return "aa=" + RenderSettings.AaName(Mode) + " draws=" + Draws + " broken=" + broken + " err=" + LastError;
                case "aa": Mode = RenderSettings.ParseAa(a.Length > 1 ? a[1] : "off"); broken = false; return "aa=" + RenderSettings.AaName(Mode);
                case "repeat": Repeat = Math.Max(1, a.Length > 1 ? int.Parse(a[1]) : 1); return "repeat=" + Repeat;
                case "shade": if (a.Length > 1) { Settings.Shading = a[1] == "on"; Settings.Save(); Normals.Reset(); } return "shade=" + Settings.Shading + " asks=" + Normals.Asks;
                case "light": return Lighting.Command(a.Skip(1).ToArray());
                case "frames": return "calls=" + Calls + " ms=" + clock.ElapsedMilliseconds;
                case "vsync": try { return Vsync(a.Length > 1 && a[1] == "on"); } catch (Exception e) { return "vsync failed: " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split((char)10)[0].Trim(); }
                default: throw new Exception("render status|aa off|fxaa|smaa|frames|vsync on|off");
            }
        }
    }
}
