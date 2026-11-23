using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Render
{
    // Entry points the patched game calls directly.
    public static class Boot
    {
        // Replaces the game's call to GraphicsDeviceManager.set_SynchronizeWithVerticalRetrace in the SimitoneGame constructor, which runs
        // before the graphics device is created. The shipped game never asks for a graphics profile, so it runs at the Reach profile
        // (Direct3D feature level 9_3), which the SMAA shaders cannot use. Ask for HiDef when the adapter supports it, as the original
        // FreeSO client did. `hidef=off` in render.profile keeps the old behaviour.
        public static void SetVsync(GraphicsDeviceManager graphics, bool vsync)
        {
            graphics.SynchronizeWithVerticalRetrace = vsync;
            try
            {
                if (RenderSettings.Load().HiDef && GraphicsAdapter.DefaultAdapter.IsProfileSupported(GraphicsProfile.HiDef))
                {
                    graphics.GraphicsProfile = GraphicsProfile.HiDef;
                    PostAA.Log("requested the HiDef graphics profile");
                }
            }
            catch (System.Exception e) { PostAA.Log("HiDef request failed: " + e.Message); }
            // The anti-aliasing passes draw into other render targets in the middle of the frame; switching back to the screen would
            // throw away what the game already drew on it (its clear colour, which the world blends over). Keep the screen contents.
            try
            {
                graphics.PreparingDeviceSettings += (s, e) => e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            }
            catch (System.Exception e) { PostAA.Log("preserve-contents request failed: " + e.Message); }
        }
    }
}
