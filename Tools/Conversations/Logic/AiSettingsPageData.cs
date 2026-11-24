using System.Collections.Generic;

namespace Simitone.Conversations
{
    // The data the Options "Sims AI" page (ui/sims_options.html) is filled with. Pure, so it is unit tested.
    public static class AiSettingsPageData
    {
        // layaState: "up", "paused" or "off"
        public static Dictionary<string, object> Build(bool convo, bool gossip, bool simpleMenu, bool day, string layaState)
        {
            string text = layaState == "up" ? "Running: Sims can think about what to say"
                : layaState == "paused" ? "Paused: Sims use the built-in lines for now"
                : "Not running: Sims use the built-in lines";
            return new Dictionary<string, object>
            {
                { "convo", convo }, { "gossip", gossip }, { "simple", simpleMenu }, { "day", day },
                { "layaUp", layaState == "up" }, { "layaText", text },
            };
        }

        // the action names the page raises (data-action) -> which setting they flip; null for anything else
        public static string SettingOf(string action)
        {
            switch (action)
            {
                case "toggle-convo": return "convo";
                case "toggle-gossip": return "gossip";
                case "toggle-menu": return "menu";
                case "toggle-theme": return "theme";
                default: return null;
            }
        }
    }
}
