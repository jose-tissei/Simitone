using System;
using System.IO;
using System.Reflection;

// Bridge to the optional render library (Render.dll: anti-aliasing and lighting passes), like ConvoLoader. When Render.dll is
// not installed every member here does nothing, so the Options entry is simply not built.
static class RenderLoader
{
    static Type post;
    static bool looked;

    public static bool Installed { get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Render.dll")); } }

    static void Look()
    {
        if (looked) return;
        looked = true;
        try
        {
            if (!Installed) return;
            post = Assembly.Load("Render").GetType("Simitone.Render.PostAA");
        }
        catch (Exception e) { try { File.AppendAllText(Path.Combine("Content", "spritecards.log"), "# render load failed: " + e.Message + Environment.NewLine); } catch { } }
    }

    // text of the current anti-aliasing mode, e.g. "Off" or "FXAA"
    public static string Label
    {
        get { try { Look(); return post == null ? "Off" : (string)post.GetProperty("ModeLabel").GetValue(null, null); } catch { return "Off"; } }
    }

    public static bool AaOn
    {
        get { try { Look(); return post != null && (bool)post.GetProperty("AaOn").GetValue(null, null); } catch { return false; } }
    }

    public static string LightLabel
    {
        get { try { Look(); return post == null ? "Off" : (string)post.GetProperty("LightLabel").GetValue(null, null); } catch { return "Off"; } }
    }

    public static bool LightOn
    {
        get { try { Look(); return post != null && (bool)post.GetProperty("LightOn").GetValue(null, null); } catch { return false; } }
    }

    public static void CycleLight()
    {
        try { Look(); if (post != null) post.GetMethod("CycleLight").Invoke(null, null); } catch { }
    }

    public static void CycleAa()
    {
        try { Look(); if (post != null) post.GetMethod("Cycle").Invoke(null, null); } catch { }
    }
}
