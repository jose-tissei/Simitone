using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Simitone.Conversations
{
    // Design tokens (docs/ui-redesign/design-tokens.json) as the CSS variables the pages use: var(--card), var(--good-ink), var(--r-card) ...
    public static class ThemeTokens
    {
        static readonly Dictionary<string, string> Special = new Dictionary<string, string>
        {
            { "text.primary", "text" }, { "text.secondary", "text-2" }, { "text.disabled", "text-off" },
            { "accent.accent", "accent" }, { "accent.accent2", "accent-2" }, { "accent.deep", "accent-deep" },
            { "focus.ring", "focus" }, { "focus.gap", "focus-2" },
        };

        public static Dictionary<string, string> Build(string json, string theme)
        {
            var root = JObject.Parse(json);
            var map = new Dictionary<string, string>();
            var t = (JObject)root["themes"][theme];
            foreach (JProperty group in t.Properties().Where(p => p.Value is JObject))
                foreach (JProperty kv in ((JObject)group.Value).Properties())
                {
                    string name;
                    if (!Special.TryGetValue(group.Name + "." + kv.Name, out name)) name = Kebab(kv.Name);
                    map[name] = (string)kv.Value;
                }
            foreach (JProperty kv in ((JObject)root["shared"]["pill"]).Properties()) map["pill-" + Kebab(kv.Name)] = (string)kv.Value;
            foreach (JProperty kv in ((JObject)root["shared"]["iconButton"]).Properties()) map["icon-btn-" + Kebab(kv.Name)] = (string)kv.Value;
            foreach (JProperty kv in ((JObject)root["shared"]["pie"]).Properties()) map["pie-" + Kebab(kv.Name)] = (string)kv.Value;
            foreach (JProperty kv in ((JObject)root["shared"]["balloon"]).Properties()) map["balloon-" + Kebab(kv.Name)] = kv.Value.Type == JTokenType.Integer ? ((int)kv.Value) + "px" : (string)kv.Value;
            int i = 1;
            foreach (var c in root["shared"]["simIdentity"]) map["sim-" + (i++)] = (string)c;
            foreach (JProperty kv in ((JObject)root["spacing"]).Properties()) map["sp-" + kv.Name] = ((int)kv.Value) + "px";
            foreach (JProperty kv in ((JObject)root["radius"]).Properties()) map["r-" + Kebab(kv.Name)] = ((int)kv.Value) + "px";
            // translucent tints of the status colours (CSS color-mix is not available in the game's CSS engine)
            foreach (var s in new[] { "good", "warn", "bad", "info" })
            {
                string hex; if (!map.TryGetValue(s, out hex)) continue;
                map[s + "-a16"] = Rgb(hex, 0.16); map[s + "-a8"] = Rgb(hex, 0.08);
            }
            return map;
        }

        static string Rgb(string hex, double a)
        {
            hex = hex.TrimStart('#');
            return "rgba(" + Convert.ToInt32(hex.Substring(0, 2), 16) + "," + Convert.ToInt32(hex.Substring(2, 2), 16) + "," + Convert.ToInt32(hex.Substring(4, 2), 16) + "," + a.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        static string Kebab(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) { if (char.IsUpper(c)) { sb.Append('-'); sb.Append(char.ToLowerInvariant(c)); } else sb.Append(c); }
            return sb.ToString();
        }
    }
}
