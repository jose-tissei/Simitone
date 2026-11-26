using System;
using System.IO;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // Shows any page from Content/ui as a full-screen overlay (`simtool convo htmlui <page.html>`), mainly for trying designs.
    // The real windows (Sim Memory) use HtmlSurface directly and fill the page with data.
    public class HtmlPanel : UIElement
    {
        static HtmlPanel instance;
        HtmlSurface surface;
        string page;
        bool prevEsc = true;
        public static string LastAction = "";

        public static bool IsOpen { get { return ModalHost.IsShown(instance); } }

        public static string Open(string file)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null) return "no screen";
                if (!File.Exists(Path.Combine(Boot.GameDir, "Content", "ui", file))) return "missing " + file;
                if (instance == null) instance = new HtmlPanel();
                instance.page = file;
                instance.surface = new HtmlSurface(instance, file);
                instance.surface.Action += a => { LastAction = a; if (a == "close") Close(); };
                ModalHost.Show(instance);
                instance.surface.IgnoreCurrentClick(); instance.prevEsc = true;
                return Status();
            }
            catch (Exception e) { Boot.Log("htmlui open failed: " + e); return "error: " + e.Message; }
        }

        public static string Close()
        {
            ModalHost.Detach(instance);
            return "closed";
        }

        public static string Status()
        {
            return IsOpen ? "open=True page=" + instance.page + " error=" + instance.surface.Error + " last=" + LastAction : "open=False last=" + LastAction;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!IsOpen) return;
            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEsc) { prevEsc = true; Close(); return; }
            prevEsc = esc;
            surface.Update(state);
        }

        public override void Draw(UISpriteBatch b)
        {
            if (IsOpen) surface.Draw(b);
        }
    }
}
