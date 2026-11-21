using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FSO.LotView.Components;
using FSO.Common;
using FSO.LotView;
using FSO.LotView.Model;

// Per-object choice between the original 2D sprite and a 3D mesh, for the fixed (2D) camera.
// InputHookPatch-style Cecil patch (SpriteCardsPatch) calls Apply(this) at the top of
// ObjectComponent's draw entry points.
//
// Content\spritecards.txt: one IFF name per line (as in MeshReplace file names, e.g. "radio" for
// radio.iff); '#' starts a comment. Objects listed there draw as their original sprite; every other
// object draws as a mesh. First line "default sprite" flips the default (everything sprite, except
// listed objects which become meshes: use "mesh: name").
public static class SpriteCards
{
    static readonly HashSet<string> sprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> meshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static bool defaultMesh = true;
    static DateTime loadedWrite = DateTime.MinValue;
    static DateTime lastCheck = DateTime.MinValue;
    static string path = Path.Combine("Content", "spritecards.txt");

    static readonly FieldInfo DrawGroupField = typeof(ObjectComponent).GetField("DrawGroup", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly ConditionalWeakTable<object, string> nameCache = new ConditionalWeakTable<object, string>();

    // ---- profiles (chosen from the in-game Options menu, remembered in <UserDir>/spritecards.profile)
    //   fixed   : original camera (Hybrid2D), objects are meshes except the list (sprites)
    //   classic : original camera (Hybrid2D), objects are sprites; walls, doors, windows are 3D
    //   free    : free 3D camera (Full3D)
    //   2d      : plain original 2D game (Full2D); 3D is effectively off
    public static string Profile = "fixed";
    static bool profileLoaded;
    static readonly string[] classicMeshPrefixes = { "door", "window" };

    static string ProfilePath { get { return Path.Combine(FSOEnvironment.UserDir ?? "", "spritecards.profile"); } }

    static void LoadProfile()
    {
        if (profileLoaded) return;
        profileLoaded = true;
        try { if (File.Exists(ProfilePath)) { var p = File.ReadAllText(ProfilePath).Trim(); if (p == "fixed" || p == "classic" || p == "free" || p == "2d") Profile = p; } } catch { }
    }

    // called from Simitone.Windows Main (patched): 3D is on by default; "-2d" keeps the plain 2D game
    public static void Startup(string[] args)
    {
        bool two = false;
        if (args != null) foreach (var a in args) if (a == "-2d") two = true;
        if (!two) FSOEnvironment.Enable3D = true;
    }

    public static void SetProfile(string p)
    {
        Profile = p; profileLoaded = true;
        try { File.WriteAllText(ProfilePath, p); } catch { }
        ApplyMode();
    }

    // switch the world to the graphics mode the profile wants; Hybrid2D is rebuilt (via Full3D) so
    // object sprite/mesh choices are re-evaluated
    static void ApplyMode()
    {
        if (!FSOEnvironment.Enable3D) return;
        var want = Profile == "free" ? GlobalGraphicsMode.Full3D : Profile == "2d" ? GlobalGraphicsMode.Full2D : GlobalGraphicsMode.Hybrid2D;
        if (want == GraphicsModeControl.Mode && want != GlobalGraphicsMode.Full3D)
            GraphicsModeControl.ChangeMode(GlobalGraphicsMode.Full3D); // force a rebuild of the lot
        GraphicsModeControl.ChangeMode(want);
    }

    // ---- Options menu: entries are (0 plain 2D, 1 fixed, 2 classic, 3 free, 4 reload). The patched
    // Options menu builds the buttons; Decorate() colours the active profile's button green (the
    // UICatButton "Selected" state) and keeps it up to date when a button is clicked.
    static readonly string[] profileOfEntry = { "2d", "fixed", "classic", "free", null };
    static object menuPanel;           // the Options UIButtonSubpanel currently shown

    public static void Menu2D() { SetProfile("2d"); Refresh(); }
    public static void MenuFixed() { SetProfile("fixed"); Refresh(); }
    public static void MenuClassic() { SetProfile("classic"); Refresh(); }
    public static void MenuFree() { SetProfile("free"); Refresh(); }
    public static void MenuReload() { lastCheck = DateTime.MinValue; loadedWrite = DateTime.MinValue; ApplyMode(); }

    // text on each entry
    public static string Label(int id)
    {
        switch (id)
        {
            case 0: return "Plain 2D";
            case 1: return "Fixed camera";
            case 2: return "Classic objects";
            case 3: return "Free camera";
            default: return "Reload list";
        }
    }

    // called right after the Options subpanel is built (patched into Switcher_OnCategorySelect)
    public static object Decorate(object subpanel)
    {
        menuPanel = subpanel;
        Refresh();
        return subpanel;
    }

    static void Refresh()
    {
        LoadProfile();
        try
        {
            if (menuPanel == null) return;
            var get = menuPanel.GetType().GetMethod("GetChildren");
            var kids = (System.Collections.IEnumerable)get.Invoke(menuPanel, null);
            int i = 0;
            foreach (var k in kids)
            {
                if (k.GetType().Name != "UICatButton") continue;
                int entry = i - 3; // after Save, Neighborhood, Quit
                var sel = k.GetType().GetProperty("Selected");
                if (sel != null) sel.SetValue(k, entry >= 0 && entry < profileOfEntry.Length && profileOfEntry[entry] == Profile, null);
                i++;
            }
        }
        catch { }
    }

    static bool IsClassicMesh(string n)
    {
        foreach (var p in classicMeshPrefixes) if (n.StartsWith(p)) return true;
        return false;
    }

    static void Reload()
    {
        var now = DateTime.UtcNow;
        if ((now - lastCheck).TotalSeconds < 2) return;
        lastCheck = now;
        try
        {
            if (!File.Exists(path)) return;
            var w = File.GetLastWriteTimeUtc(path);
            if (w == loadedWrite) return;
            loadedWrite = w;
            sprites.Clear(); meshes.Clear(); defaultMesh = true;
            foreach (var raw in File.ReadAllLines(path))
            {
                var l = raw.Split('#')[0].Trim();
                if (l.Length == 0) continue;
                if (l.Equals("default sprite", StringComparison.OrdinalIgnoreCase)) defaultMesh = false;
                else if (l.Equals("default mesh", StringComparison.OrdinalIgnoreCase)) defaultMesh = true;
                else if (l.StartsWith("mesh:", StringComparison.OrdinalIgnoreCase)) meshes.Add(l.Substring(5).Trim());
                else sprites.Add(l);
            }
        }
        catch { }
    }

    static readonly HashSet<string> seen = new HashSet<string>();
    static void Log(string n)
    {
        try { if (n != null && seen.Add(n)) File.AppendAllText(Path.Combine("Content", "spritecards.log"), n + Environment.NewLine); } catch { }
    }

    static string NameOf(ObjectComponent c)
    {
        try
        {
            var dg = DrawGroupField?.GetValue(c);
            if (dg == null) return null;
            string n;
            if (nameCache.TryGetValue(dg, out n)) return n;
            var chunk = (FSO.Files.Formats.IFF.IffChunk)dg;
            n = (chunk.ChunkParent?.Filename ?? "").Replace('.', '_').ToLowerInvariant();
            // "radio_iff" -> "radio"
            if (n.EndsWith("_iff")) n = n.Substring(0, n.Length - 4);
            nameCache.Add(dg, n);
            return n;
        }
        catch { return null; }
    }

    public static void Apply(ObjectComponent c, WorldState world)
    {
        // sprites and meshes only mix correctly when objects are drawn live, in order; the default
        // 2D camera path bakes objects into a cached static buffer
        if (world != null) world.ForceImmediate = true;
        Reload();
        LoadProfile();
        if (Profile == "free" || Profile == "2d") return; // free camera / plain 2D: the engine decides itself
        var n = NameOf(c);
        Log(n);
        ComponentRenderMode m;
        if (n == null) m = ComponentRenderMode._2D;
        else if (Profile == "classic")
        {
            // objects are sprites; doors and windows (and anything the list marks "mesh:") are meshes
            m = (meshes.Contains(n) || IsClassicMesh(n)) ? ComponentRenderMode._3D : ComponentRenderMode._2D;
        }
        else if (sprites.Contains(n)) m = ComponentRenderMode._2D;
        else if (meshes.Contains(n)) m = ComponentRenderMode._3D;
        else m = defaultMesh ? ComponentRenderMode._3D : ComponentRenderMode._2D;
        c.Mode = m;
    }
}
