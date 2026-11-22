using System;

namespace Simitone.Render
{
    // Directional shading of sprites. The release's world-sprite effect (Shaders/World2D.xnb, built from the engine's own HLSL by
    // Make-WorldEffect.py) has a third pass that rebuilds every sprite pixel's surface normal from the depth sprite and shades it
    // by the direction of the light that reaches it. The patched game asks DirPass which pass to use.
    public static class Normals
    {
        public static long Asks;

        // advanced = the engine's own advanced-lighting flag; the engine already uses pass 1 then, and clamps to the pass count
        public static int DirPass(bool advanced)
        {
            Asks++;
            if (!advanced) return 0;
            try { return PostAA.Settings.Shading && !broken ? 2 : 1; }
            catch (Exception e) { broken = true; PostAA.Log("directional shading turned itself off: " + e.Message); return 1; }
        }

        static bool broken;
        public static void Reset() { broken = false; }
    }
}
