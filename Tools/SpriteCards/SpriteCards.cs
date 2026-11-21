using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FSO.LotView.Components;
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
        var n = NameOf(c);
        Log(n);
        ComponentRenderMode m;
        if (n == null) m = ComponentRenderMode._2D;
        else if (sprites.Contains(n)) m = ComponentRenderMode._2D;
        else if (meshes.Contains(n)) m = ComponentRenderMode._3D;
        else m = defaultMesh ? ComponentRenderMode._3D : ComponentRenderMode._2D;
        c.Mode = m;
    }
}
