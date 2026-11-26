using System;
using System.Linq;
using System.Reflection;
using FSO.Client.UI.Framework;

namespace Simitone.Conversations
{
    // What the full Options page needs from the rest of the game: the view mode, anti-aliasing and lighting (SpriteCards.dll, which the
    // installer puts next to this mod) and the game's own Save / Neighborhood / Quit. All by reflection and all optional: when something
    // is not there (a plain game, or the tests) the page simply hides that group or the call does nothing.
    public static class GameBridge
    {
        const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static Type Find(string name)
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name != "SpriteCards") continue;
                    var t = a.GetTypes().FirstOrDefault(x => x.Name == name);
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }

        static object Call(Type t, string method)
        {
            var m = t == null ? null : t.GetMethod(method, Any);
            return m == null ? null : m.Invoke(null, null);
        }

        public static bool HasView { get { return Find("SpriteCards") != null; } }

        // "2d" | "list" | "arch" | "full3d" (ids of ModeLogic.Modes); "" when unknown
        public static string ViewMode
        {
            get
            {
                try
                {
                    var t = Find("SpriteCards");
                    var f = t == null ? null : t.GetField("Current", Any);
                    var mode = f == null ? null : f.GetValue(null);
                    var id = mode == null ? null : mode.GetType().GetField("Id").GetValue(mode);
                    return id as string ?? "";
                }
                catch { return ""; }
            }
        }

        public static void SetView(string id)
        {
            string method = id == "2d" ? "Menu2D" : id == "list" ? "MenuList" : id == "arch" ? "MenuArch" : id == "full3d" ? "MenuFull3D" : null;
            if (method != null) Try(() => Call(Find("SpriteCards"), method));
        }

        public static void ReloadList() { Try(() => Call(Find("SpriteCards"), "MenuReload")); }

        public static bool AaOn { get { return Flag("AaOn"); } }
        public static bool LightOn { get { return Flag("LightOn"); } }
        public static void ToggleAa() { Try(() => Call(Find("SpriteCards"), "MenuAntiAlias")); }
        public static void ToggleLight() { Try(() => Call(Find("SpriteCards"), "MenuLighting")); }

        static bool Flag(string name)
        {
            try
            {
                var t = Find("RenderLoader");
                var p = t == null ? null : t.GetProperty(name, Any);
                return p != null && (bool)p.GetValue(null, null);
            }
            catch { return false; }
        }

        // the game screen's own methods; each of the last two asks "save first?" itself
        public static void Save() { OnScreen("Save"); }
        public static void BackToNeighborhood() { OnScreen("ReturnToNeighbourhood"); }
        public static void Quit() { OnScreen("CloseAttempt"); }

        // The Options button only fires when the bottom panel CHANGES to Options mode. After the page is closed the panel would stay in
        // Options mode (and the game stays paused), so the next click on the button did nothing: back to Live mode when the page closes.
        public static void BackToLiveMode()
        {
            Try(() =>
            {
                var screen = UIScreen.Current;
                var frontend = Member(screen, "Frontend");
                var main = Member(frontend, "MainPanel") ?? Member(screen, "MainPanel");
                if (main == null) return null;
                var modeField = main.GetType().GetField("Mode");
                var setMode = main.GetType().GetMethod("SetMode");
                if (modeField == null || setMode == null) return null;
                var live = Enum.ToObject(modeField.FieldType, 0);
                if (!Equals(modeField.GetValue(main), live)) setMode.Invoke(main, new[] { live });
                return null;
            });
        }

        static object Member(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name, Any); if (p != null) return p.GetValue(o, null);
            var f = o.GetType().GetField(name, Any); return f != null ? f.GetValue(o) : null;
        }

        static void OnScreen(string method)
        {
            Try(() =>
            {
                var screen = UIScreen.Current;
                var m = screen == null ? null : screen.GetType().GetMethod(method, Any);
                if (m != null) m.Invoke(screen, null);
                return null;
            });
        }

        static void Try(Func<object> f)
        {
            try { f(); } catch (Exception e) { Boot.Log("game bridge: " + e.Message); }
        }
    }
}
