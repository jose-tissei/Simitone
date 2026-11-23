using System;
using System.IO;
using System.Reflection;

// Bridge to the optional AI conversations library (Conversations.dll). It lives in its own class because
// SpriteCards itself has static fields of game types: touching SpriteCards from the game's Main (before MonoGame is
// linked) would fail, and this class must be safe to call that early. When Conversations.dll is not installed
// every method here does nothing.
static class ConvoLoader
{
    static Type boot, early;
    static bool looked;

    public static bool Installed { get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Conversations.dll")); } }

    static void Look()
    {
        if (looked) return;
        looked = true;
        try
        {
            if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Conversations.dll"))) return;
            var asm = Assembly.Load("Conversations");   // default load context: the same assembly the patched game code calls into
            boot = asm.GetType("Simitone.Conversations.Boot");
            early = asm.GetType("Simitone.Conversations.Early");
        }
        catch (Exception e) { Diag("conversations load failed: " + e.GetType().Name + ": " + e.Message); }
    }

    static void Diag(string msg)
    {
        try { File.AppendAllText(Path.Combine("Content", "spritecards.log"), "# " + msg + Environment.NewLine); } catch { }
    }

    // from the game's Main
    public static void Start()
    {
        try
        {
            Look();
            if (early != null) early.GetMethod("Startup").Invoke(null, new object[] { AppDomain.CurrentDomain.BaseDirectory });
        }
        catch (Exception e) { Diag("conversations startup failed: " + (e.InnerException ?? e).ToString().Replace("\r\n", " | ")); }
    }

    public static bool Enabled
    {
        get
        {
            try { Look(); return boot != null && (bool)boot.GetProperty("Enabled").GetValue(null, null); } catch { return false; }
        }
    }

    public static void Toggle()
    {
        try { Look(); if (boot != null) boot.GetProperty("Enabled").SetValue(null, !Enabled, null); } catch { }
    }

    public static void OpenMemoryMenu()
    {
        try { Look(); if (boot != null) boot.GetMethod("OpenMemoryMenu").Invoke(null, null); } catch { }
    }

    public static void CloseMemoryMenu()
    {
        try { Look(); if (boot != null) boot.GetMethod("CloseMemoryMenu").Invoke(null, null); } catch { }
    }
}
