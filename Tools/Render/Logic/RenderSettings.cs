using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Render
{
    public enum AaMode { Off = 0, Fxaa = 1, Smaa = 2 }
    public enum LightMode { Off = 0, Bounce = 1, Cascades = 2 }

    // The user's render choices, kept in Documents\Simitone\render.profile as simple "key=value" lines. No game types here, so it is unit tested.
    public class RenderSettings
    {
        public AaMode Aa = AaMode.Off;
        public LightMode Light = LightMode.Off;
        public bool Shading;          // directional shading of sprites (needs the world-sprite effect with the third pass)
        public bool HiDef = true;   // ask the graphics device for the HiDef profile (needed by SMAA); off = the shipped Reach behaviour

        public static AaMode ParseAa(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "fxaa": case "1": case "on": return AaMode.Fxaa;
                case "smaa": case "2": return AaMode.Smaa;
                default: return AaMode.Off;
            }
        }

        public static LightMode ParseLight(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "bounce": case "1": case "on": return LightMode.Bounce;
                case "rc": case "cascades": case "2": return LightMode.Cascades;
                default: return LightMode.Off;
            }
        }

        public static string LightName(LightMode m) { return m == LightMode.Bounce ? "bounce" : (m == LightMode.Cascades ? "rc" : "off"); }

        // the order the Options menu button cycles through
        public static AaMode Next(AaMode m) { return m == AaMode.Off ? AaMode.Fxaa : (m == AaMode.Fxaa ? AaMode.Smaa : AaMode.Off); }

        // the Options button "Dynamic lighting": off, or radiance cascades with directional shading together (the bounce prototype and
        // the two halves separately stay available through simtool and render.profile)
        public void CycleDynamicLighting()
        {
            bool on = Light != LightMode.Off || Shading;
            Light = on ? LightMode.Off : LightMode.Cascades;
            Shading = !on;
        }

        public static string AaName(AaMode m) { return m == AaMode.Fxaa ? "fxaa" : (m == AaMode.Smaa ? "smaa" : "off"); }

        public static RenderSettings Parse(IEnumerable<string> lines)
        {
            var s = new RenderSettings();
            foreach (var raw in lines ?? new string[0])
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line.Substring(0, i).Trim().ToLowerInvariant();
                var val = line.Substring(i + 1);
                if (key == "aa") s.Aa = ParseAa(val);
                else if (key == "light") s.Light = ParseLight(val);
                else if (key == "shade") s.Shading = IsOn(val);
                else if (key == "hidef") s.HiDef = !(val.Trim().ToLowerInvariant() == "off" || val.Trim() == "0" || val.Trim().ToLowerInvariant() == "false");
            }
            return s;
        }

        static bool IsOn(string v) { v = (v ?? "").Trim().ToLowerInvariant(); return v == "on" || v == "1" || v == "true"; }

        public string Serialize() { return "aa=" + AaName(Aa) + Environment.NewLine + "light=" + LightName(Light) + Environment.NewLine + "shade=" + (Shading ? "on" : "off") + Environment.NewLine + "hidef=" + (HiDef ? "on" : "off") + Environment.NewLine; }

        public static string ProfilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone", "render.profile"); }
        }

        public static RenderSettings Load(string path = null)
        {
            try { path = path ?? ProfilePath; return File.Exists(path) ? Parse(File.ReadAllLines(path)) : new RenderSettings(); }
            catch { return new RenderSettings(); }
        }

        public void Save(string path = null)
        {
            try { path = path ?? ProfilePath; Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, Serialize()); }
            catch { }
        }
    }
}
