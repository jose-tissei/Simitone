using System;
using System.Collections.Generic;

// Pure decision logic for SpriteCards: which model (2D sprite or 3D mesh) an object gets in each mode, and
// how the list files are parsed. No game types, so it is unit tested directly (see Tools/SpriteCards.Tests).
public sealed class ModeDef
{
    public string Id, Label;
    public bool Hybrid;             // sprites and meshes are mixed live
    public bool ListApplies;        // the default sprite list is honoured
    public bool ObjectsDefaultMesh; // objects are meshes unless listed (only meaningful when ListApplies)
}

public sealed class ModelLists
{
    public readonly HashSet<string> Sprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Meshes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, bool> Overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // true = 2D model
}

public static class ModeLogic
{
    // The compatibility table. See README.md; keep in sync.
    public static readonly ModeDef[] Modes =
    {
        new ModeDef { Id = "2d",     Label = "2D mode",              Hybrid = false },
        new ModeDef { Id = "list",   Label = "3D with 2D list",      Hybrid = true,  ListApplies = true,  ObjectsDefaultMesh = true },
        new ModeDef { Id = "arch",   Label = "3D arch + 2D objects", Hybrid = true,  ListApplies = false, ObjectsDefaultMesh = false },
        new ModeDef { Id = "full3d", Label = "Full 3D",              Hybrid = false },
    };

    // objects whose file name starts with one of these stay meshes in "arch" (3D doors and windows)
    public static readonly string[] ArchMeshPrefixes = { "door", "window" };

    // accepts the ids of earlier builds too (fixed/classic/free)
    public static ModeDef Find(string id)
    {
        if (id == null) return null;
        id = id.Trim();
        if (id == "fixed") id = "list"; else if (id == "classic") id = "arch"; else if (id == "free") id = "full3d";
        foreach (var m in Modes) if (m.Id == id) return m;
        return null;
    }

    // exact name, or a prefix when the entry ends with '*'
    public static bool Matches(HashSet<string> set, string name)
    {
        if (set.Contains(name)) return true;
        foreach (var e in set)
            if (e.EndsWith("*") && name.StartsWith(e.Substring(0, e.Length - 1), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // list file: one name per line, '#' starts a comment, "mesh: name" forces a mesh, obsolete "default ..." lines are ignored
    public static void ParseList(IEnumerable<string> lines, ModelLists into)
    {
        into.Sprites.Clear(); into.Meshes.Clear();
        foreach (var raw in lines)
        {
            var l = raw.Split('#')[0].Trim();
            if (l.Length == 0 || l.StartsWith("default ", StringComparison.OrdinalIgnoreCase)) continue;
            if (l.StartsWith("mesh:", StringComparison.OrdinalIgnoreCase)) into.Meshes.Add(l.Substring(5).Trim());
            else into.Sprites.Add(l);
        }
    }

    // user overrides: "2d name" / "3d name"
    public static void ParseOverrides(IEnumerable<string> lines, ModelLists into)
    {
        into.Overrides.Clear();
        foreach (var raw in lines)
        {
            var p = raw.Split('#')[0].Trim().Split(new[] { ' ' }, 2);
            if (p.Length == 2 && (p[0] == "2d" || p[0] == "3d")) into.Overrides[p[1].Trim()] = p[0] == "2d";
        }
    }

    public static string[] SerializeOverrides(Dictionary<string, bool> overrides)
    {
        var lines = new List<string> { "# per-object model overrides set with Ctrl+click in game: '2d name' or '3d name'" };
        foreach (var kv in overrides) lines.Add((kv.Value ? "2d " : "3d ") + kv.Key);
        return lines.ToArray();
    }

    public static bool IsArchMesh(string name)
    {
        foreach (var p in ArchMeshPrefixes) if (name.StartsWith(p, StringComparison.Ordinal)) return true;
        return false;
    }

    // true when the object is drawn with its 2D sprite in the given mode. Overrides win in hybrid modes.
    public static bool UsesSprite(ModeDef mode, string name, ModelLists lists)
    {
        if (name == null || !mode.Hybrid) return true; // non-hybrid modes are decided by the engine, not per object
        bool o;
        if (lists.Overrides.TryGetValue(name, out o)) return o;
        if (mode.ListApplies)
        {
            if (Matches(lists.Meshes, name)) return false;
            return Matches(lists.Sprites, name) || !mode.ObjectsDefaultMesh;
        }
        return !(IsArchMesh(name) || Matches(lists.Meshes, name)); // arch: sprites except doors/windows and "mesh:" lines
    }
}
