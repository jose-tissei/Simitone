using System;
using System.Collections.Generic;
using System.IO;

namespace Simitone.Render
{
    public enum AaMode { Off = 0, Fxaa = 1 }

    // The user's render choices, kept in Documents\Simitone\render.profile as simple "key=value" lines. No game types here, so it is unit tested.
    public class RenderSettings
    {
        public AaMode Aa = AaMode.Off;

        public static AaMode ParseAa(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "fxaa": case "1": case "on": return AaMode.Fxaa;
                default: return AaMode.Off;
            }
        }

        public static string AaName(AaMode m) { return m == AaMode.Fxaa ? "fxaa" : "off"; }

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
            }
            return s;
        }

        public string Serialize() { return "aa=" + AaName(Aa) + Environment.NewLine; }

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
