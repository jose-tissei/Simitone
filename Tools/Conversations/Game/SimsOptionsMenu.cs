using System;
using System.IO;
using FSO.Client;
using FSO.Common;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Options > Sims AI: switches for the AI features, built from Content/ui/sims_options.html (see docs/ui-redesign).
    // Opened with `simtool convo options` (and from the Options button once that entry is wired).
    public class SimsOptionsMenu : UIElement
    {
        static SimsOptionsMenu instance;
        readonly HtmlSurface surface;
        bool prevEsc = true, prevLeft = true;
        string lastKey = "";

        public static bool IsOpen { get { return instance != null && instance.Parent != null; } }

        SimsOptionsMenu()
        {
            ListenForMouse(new Microsoft.Xna.Framework.Rectangle(0, 0, 4000, 3000), (type, st) => { });   // modal: the world underneath must not get these clicks
            surface = new HtmlSurface(this, "sims_options.html");
            surface.Action += a => Act(a);
        }

        public static string Open()
        {
            try
            {
                if (SimMemoryMenu.IsOpen) SimMemoryMenu.Close();   // one modal at a time (the redesign's rule)
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (!File.Exists(Path.Combine(Boot.GameDir, "Content", "ui", "sims_options.html"))) return "missing sims_options.html";
                if (instance == null) instance = new SimsOptionsMenu();
                if (instance.Parent != screen) { if (instance.Parent != null) instance.Parent.Remove(instance); screen.Add(instance); }
                instance.surface.IgnoreCurrentClick(); instance.prevEsc = true; instance.prevLeft = true;
                instance.lastKey = "";
                return Status();
            }
            catch (Exception e) { Boot.Log("options open failed: " + e); return "error: " + e.Message; }
        }

        public static string Close()
        {
            if (instance != null && instance.Parent != null) instance.Parent.Remove(instance);
            return "closed";
        }

        public static string Status()
        {
            return (IsOpen ? "open=True" : "open=False") + " conversations=" + Boot.Enabled + " gossip=" + Boot.GossipEnabled + " menu=" + (Boot.FullMenu ? "full" : "simplified") + " theme=" + Boot.Theme;
        }

        // one page action (also reachable from simtool as `convo options click toggle-gossip`)
        public static string Act(string action)
        {
            try
            {
                if (action == "close") { Close(); return Status(); }
                switch (AiSettingsPageData.SettingOf(action))
                {
                    case "convo": Boot.Enabled = !Boot.Enabled; break;
                    case "gossip": Boot.GossipEnabled = !Boot.GossipEnabled; break;
                    case "menu": Boot.SetFullMenu(!Boot.FullMenu, true); break;
                    case "theme": Boot.Theme = Boot.Theme == "day" ? "night" : "day"; break;
                }
            }
            catch (Exception e) { Boot.Log("options action " + action + ": " + e.Message); }
            return Status();
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!IsOpen) return;
            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEsc) { prevEsc = true; Close(); return; }
            prevEsc = esc;
            float sc = Math.Max(0.5f, FSOEnvironment.DPIScaleFactor);
            bool pressed = state.MouseState.LeftButton == ButtonState.Pressed;
            if (pressed && !prevLeft && !surface.PointInFirstBox(state.MouseState.X / sc, state.MouseState.Y / sc)) { prevLeft = true; Close(); return; }
            prevLeft = pressed;
            surface.Update(state);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!IsOpen) return;
            string laya = Boot.Laya.Available ? "up" : (Boot.Laya.Configured ? "paused" : "off");
            var data = AiSettingsPageData.Build(Boot.Enabled, Boot.GossipEnabled, !Boot.FullMenu, Boot.Theme == "day", laya);
            string key = Boot.Enabled + "|" + Boot.GossipEnabled + "|" + Boot.FullMenu + "|" + Boot.Theme + "|" + laya;
            surface.Theme = Boot.Theme;
            if (key != lastKey) { lastKey = key; surface.SetData(data, key); surface.Invalidate(); }
            surface.Draw(batch);
        }
    }
}
