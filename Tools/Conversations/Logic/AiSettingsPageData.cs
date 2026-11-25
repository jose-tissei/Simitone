using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // The data the Options "Sims AI" page (ui/sims_options.html) is filled with. Pure, so it is unit tested.
    public static class AiSettingsPageData
    {
        // layaState: "up", "paused" or "off"
        public static Dictionary<string, object> Build(bool convo, bool gossip, bool simpleMenu, bool day, string layaState, string viewMode = "", bool aa = false, bool light = false, bool hasView = false)
        {
            string text = layaState == "up" ? "Running: Sims can think about what to say"
                : layaState == "paused" ? "Paused: Sims use the built-in lines for now"
                : "Not running: Sims use the built-in lines";
            return new Dictionary<string, object>
            {
                { "convo", convo }, { "gossip", gossip }, { "simple", simpleMenu }, { "day", day },
                { "layaUp", layaState == "up" }, { "layaText", text },
                { "hasView", hasView }, { "aa", aa }, { "light", light },
                { "tiles", ViewTiles.Select(t => (object)new Dictionary<string, object> { { "id", t[0] }, { "label", t[1] }, { "icon", t[2] }, { "on", t[0] == viewMode } }).ToList() },
            };
        }

        // the four ways to draw the world (ids of SpriteCards' ModeLogic.Modes), with the icon the design gives each
        static readonly string[][] ViewTiles =
        {
            new[] { "2d", "2D mode", "grid" }, new[] { "list", "3D + 2D list", "cube" },
            new[] { "arch", "3D arch + 2D objects", "home" }, new[] { "full3d", "Full 3D", "eye" },
        };

        // "view-arch" -> "arch"; null for anything else
        public static string ViewOf(string action) { return action != null && action.StartsWith("view-") ? action.Substring(5) : null; }

        // the action names the page raises (data-action) -> which setting they flip; null for anything else
        public static string SettingOf(string action)
        {
            switch (action)
            {
                case "toggle-convo": return "convo";
                case "toggle-gossip": return "gossip";
                case "toggle-menu": return "menu";
                case "toggle-theme": return "theme";
                case "toggle-aa": return "aa";
                case "toggle-light": return "light";
                case "save": case "hood": case "reload": case "quit": return action;
                default: return null;
            }
        }
    }
}
