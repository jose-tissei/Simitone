using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using Simitone.Client.UI.Panels;

// Display modes for Simitone: how the world is drawn (2D sprites vs 3D meshes) and how the camera behaves.
// Patched into the release assemblies by SpriteCardsPatch; see README.md.
//
// COMPATIBILITY TABLE (every mode is fully described here; nothing else is implicit)
//
//  id      menu name                    engine mode   camera  objects default      list file      per-object override   doors/windows
//  ------  ---------------------------  ------------  ------  -------------------  -------------  --------------------  -------------
//  2d      2D mode                      Full2D        fixed   sprite (engine)      ignored        n/a (no meshes)       sprite
//  list    3D with 2D list              Hybrid2D      fixed   mesh; list = sprite  applies        applies               mesh
//  arch    3D architecture, 2D objects  Hybrid2D      fixed   sprite; list "mesh:" applies*       applies               mesh
//  full3d  Full 3D                      Full3D        free    mesh (engine)        ignored        n/a (no sprites)      mesh
//
//  * in "arch", only lines starting with "mesh:" in the list file matter.
//  The camera is a consequence of the mode (fixed for 2d/list/arch, free for full3d), never chosen separately.
//  Hybrid modes draw objects live (ForceImmediate) so sprites and meshes can be mixed; other modes clear it.
public static class SpriteCards
{
    static readonly ModeDef[] Modes = ModeLogic.Modes;

    // engine graphics mode for each mode id
    static GlobalGraphicsMode EngineOf(ModeDef m)
    {
        switch (m.Id) { case "2d": return GlobalGraphicsMode.Full2D; case "full3d": return GlobalGraphicsMode.Full3D; default: return GlobalGraphicsMode.Hybrid2D; }
    }

    static ModeDef Current = Modes[1];
    static bool profileLoaded;

    static string ProfilePath { get { return Path.Combine(FSOEnvironment.UserDir ?? "", "spritecards.profile"); } }
    static string UserPath { get { return Path.Combine(FSOEnvironment.UserDir ?? "", "spritecards.user.txt"); } }
    static readonly string ListPath = Path.Combine("Content", "spritecards.txt");


    // ---------------------------------------------------------------- list files
    static readonly ModelLists lists = new ModelLists();
    static DateTime listWrite = DateTime.MinValue, userWrite = DateTime.MinValue, lastCheck = DateTime.MinValue;

    static void ReloadFiles(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - lastCheck).TotalSeconds < 2) return;
        lastCheck = now;
        try
        {
            if (File.Exists(ListPath))
            {
                var w = File.GetLastWriteTimeUtc(ListPath);
                if (force || w != listWrite) { listWrite = w; ModeLogic.ParseList(File.ReadAllLines(ListPath), lists); }
            }
            if (File.Exists(UserPath))
            {
                var w = File.GetLastWriteTimeUtc(UserPath);
                if (force || w != userWrite) { userWrite = w; ModeLogic.ParseOverrides(File.ReadAllLines(UserPath), lists); }
            }
        }
        catch { }
    }

    static void SaveUserOverrides()
    {
        try
        {
            File.WriteAllLines(UserPath, ModeLogic.SerializeOverrides(lists.Overrides));
            userWrite = File.GetLastWriteTimeUtc(UserPath);
        }
        catch { }
    }

    // ---------------------------------------------------------------- profile (mode) handling
    static void LoadProfile()
    {
        if (profileLoaded) return;
        profileLoaded = true;
        try { if (File.Exists(ProfilePath)) { var m = ModeLogic.Find(File.ReadAllText(ProfilePath)); if (m != null) Current = m; } } catch { }
    }

    // called from Simitone.Windows Main (patched): 3D is on by default; "-2d" keeps the plain 2D game
    public static void Startup(string[] args)
    {
        bool two = false;
        if (args != null) foreach (var a in args) if (a == "-2d") two = true;
        if (!two) FSOEnvironment.Enable3D = true;
        // the AI conversations feature (Conversations.dll) is optional: when it is not installed nothing changes
        ConvoLoader.Start();
    }

    public static void MenuAntiAlias()
    {
        RenderLoader.CycleAa();
        Refresh();
    }

    public static void MenuLighting()
    {
        RenderLoader.CycleLight();
        Refresh();
    }

    public static void MenuConversations()
    {
        ConvoLoader.Toggle();
        Refresh();
    }

    static void SetMode(ModeDef m)
    {
        Current = m; profileLoaded = true;
        try { File.WriteAllText(ProfilePath, m.Id); } catch { }
        ApplyEngineMode(false);
    }

    // Switch the engine to the mode's graphics mode. Switching between the two hybrid modes needs no
    // rebuild (objects are re-evaluated every frame); reloading forces one through Full3D.
    static void ApplyEngineMode(bool rebuild)
    {
        if (!FSOEnvironment.Enable3D) return;
        if (rebuild && GraphicsModeControl.Mode == EngineOf(Current) && EngineOf(Current) != GlobalGraphicsMode.Full3D)
            GraphicsModeControl.ChangeMode(GlobalGraphicsMode.Full3D);
        GraphicsModeControl.ChangeMode(EngineOf(Current));
    }

    // ---------------------------------------------------------------- Options menu
    // entries: 0 2D mode, 1 3D with 2D list, 2 3D architecture + 2D objects, 3 Full 3D, 4 Reload list, 5 AI conversations (toggle)
    static readonly string[] modeOfEntry = { "2d", "list", "arch", "full3d", null, null, null, null };
    static object menuPanel; // the Options subpanel currently shown

    public static void Menu2D() { SetMode(Modes[0]); Refresh(); }
    public static void MenuList() { SetMode(Modes[1]); Refresh(); }
    public static void MenuArch() { SetMode(Modes[2]); Refresh(); }
    public static void MenuFull3D() { SetMode(Modes[3]); Refresh(); }
    public static void MenuReload() { ReloadFiles(true); ApplyEngineMode(true); }

    public static string Label(int id)
    {
        if (id == 5) return "AI conversations";
        if (id == 6) return "Anti-aliasing: " + RenderLoader.Label;
        if (id == 7) return "Dynamic lighting: " + RenderLoader.LightLabel;
        return id >= 0 && id < Modes.Length ? Modes[id].Label : "Reload list";
    }

    // after the Options subpanel is built: colour the active mode's button green and keep it current
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
            var kids = (System.Collections.IEnumerable)menuPanel.GetType().GetMethod("GetChildren").Invoke(menuPanel, null);
            int i = 0;
            foreach (var k in kids)
            {
                if (k.GetType().Name == "UILabel")
                {
                    // the anti-aliasing button's caption shows the current mode, so keep it current after each click
                    var cap = k.GetType().GetProperty("Caption");
                    var text = cap?.GetValue(k, null) as string;
                    if (text != null && text.StartsWith("Anti-aliasing")) cap.SetValue(k, "Anti-aliasing: " + RenderLoader.Label, null);
                    if (text != null && text.StartsWith("Dynamic lighting")) cap.SetValue(k, "Dynamic lighting: " + RenderLoader.LightLabel, null);
                    continue;
                }
                if (k.GetType().Name != "UICatButton") continue;
                int entry = i - 3; // after Save, Neighborhood, Quit
                // entries 5 (conversations), 6 (anti-aliasing) and 7 (lighting) exist only when their libraries are installed, in that order
                if (entry >= 5 && !ConvoLoader.Installed) entry++;
                var sel = k.GetType().GetProperty("Selected");
                if (sel != null) sel.SetValue(k, entry == 5 ? ConvoLoader.Enabled : entry == 6 ? RenderLoader.AaOn : entry == 7 ? RenderLoader.LightOn : (entry >= 0 && entry < modeOfEntry.Length && modeOfEntry[entry] == Current.Id), null);
                i++;
            }
        }
        catch { }
    }

    // ---------------------------------------------------------------- per-object decision
    static readonly FieldInfo DrawGroupField = typeof(ObjectComponent).GetField("DrawGroup", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly ConditionalWeakTable<object, string> nameCache = new ConditionalWeakTable<object, string>();
    static readonly HashSet<string> seen = new HashSet<string>();

    static void Diag(string msg)
    {
        try { File.AppendAllText(Path.Combine("Content", "spritecards.log"), "# " + msg + Environment.NewLine); } catch { }
    }

    static void Log(string n)
    {
        try { if (n != null && seen.Add(n)) File.AppendAllText(Path.Combine("Content", "spritecards.log"), n + Environment.NewLine); } catch { }
    }

    static string NameOf(ObjectComponent c)
    {
        try
        {
            var dg = DrawGroupField == null ? null : DrawGroupField.GetValue(c);
            if (dg == null) return null;
            string n;
            if (nameCache.TryGetValue(dg, out n)) return n;
            var chunk = (FSO.Files.Formats.IFF.IffChunk)dg;
            n = ((chunk.ChunkParent == null ? "" : chunk.ChunkParent.Filename) ?? "").Replace('.', '_').ToLowerInvariant();
            if (n.EndsWith("_iff")) n = n.Substring(0, n.Length - 4); // "radio_iff" -> "radio"
            nameCache.Add(dg, n);
            return n;
        }
        catch { return null; }
    }

    static bool UsesSprite(string n) { return ModeLogic.UsesSprite(Current, n, lists); }

    // injected at the top of ObjectComponent.{DoDraw,Draw,Update,Preload}
    public static void Apply(ObjectComponent c, WorldState world)
    {
        LoadProfile();
        // hybrid modes draw objects live and in order (needed to mix sprites and meshes); every other mode
        // must clear it or the 2D camera path would stay in its slow immediate mode after leaving a hybrid mode
        if (world != null) world.ForceImmediate = Current.Hybrid;
        ReloadFiles(false);
        var n = NameOf(c);
        Log(n);
        // every mode sets the mode explicitly, so nothing is left over from a previously active mode
        c.Mode = (n != null && Current.Hybrid && !UsesSprite(n)) ? ComponentRenderMode._3D : ComponentRenderMode._2D;
    }

    // ---------------------------------------------------------------- Ctrl+click an object: choose its model
    // injected at the top of UILotControl.ShowPieMenu(pt, state), which runs for a click on the lot;
    // returns true when the click was handled
    public static bool CtrlClick(UILotControl lc, Microsoft.Xna.Framework.Point pt, UpdateState state)
    {
        if (!state.CtrlDown) return false;
        LoadProfile();
        try
        {
            ReloadFiles(false);
            ObjectComponent comp = null;
            // sprites are in the game's object-id picking buffer; meshes are not, so fall back to a ray pick
            var id = lc.World.GetObjectIDAtScreenPos(pt.X, pt.Y, GameFacade.GraphicsDevice);
            if (id > 0)
            {
                var ent = lc.vm.GetObjectById(id);
                if (ent is VMAvatar) return false; // Ctrl+click on a Sim keeps switching control
                comp = ent == null ? null : ent.WorldUI as ObjectComponent;
            }
            if (comp == null) comp = PickMesh(lc.World, pt.X, pt.Y);
            var name = comp == null ? null : NameOf(comp);
            Diag("ctrl+click at " + pt.X + "," + pt.Y + " -> id " + id + ", object " + (name ?? "none"));
            if (name == null) return false;

            if (!Current.Hybrid)
            {
                Show("Object model", "Choosing 2D or 3D per object works in '3D with 2D list' and '3D arch + 2D objects'.\nUse the Options menu to switch.", null, null);
                return true;
            }
            Show("Object model: " + name, "This object is drawn with its " + (UsesSprite(name) ? "2D" : "3D") + " model.\nWhich should it use?",
                () => SetOverride(name, true), () => SetOverride(name, false));
            return true;
        }
        catch (Exception e) { Diag("ctrl+click failed: " + e); return false; }
    }

    static IEnumerable<ObjectComponent> BlueprintObjects(FSO.LotView.World world)
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = world.GetType();
        object bp = null;
        var pi = t.GetProperty("Blueprint", f); if (pi != null) bp = pi.GetValue(world, null);
        if (bp == null) { var fi = t.GetField("Blueprint", f); if (fi != null) bp = fi.GetValue(world); }
        if (bp == null) yield break;
        var objs = bp.GetType().GetField("Objects", f);
        var list = (System.Collections.IEnumerable)(objs != null ? objs.GetValue(bp) : bp.GetType().GetProperty("Objects", f).GetValue(bp, null));
        foreach (var o in list) { var oc = o as ObjectComponent; if (oc != null) yield return oc; }
    }

    // nearest visible object whose 3D bounds the cursor ray hits
    static ObjectComponent PickMesh(FSO.LotView.World world, int x, int y)
    {
        var st = world.State;
        var vp = GameFacade.GraphicsDevice.Viewport;
        var near = vp.Unproject(new Microsoft.Xna.Framework.Vector3(x, y, 0f), st.Projection, st.View, Microsoft.Xna.Framework.Matrix.Identity);
        var far = vp.Unproject(new Microsoft.Xna.Framework.Vector3(x, y, 1f), st.Projection, st.View, Microsoft.Xna.Framework.Matrix.Identity);
        var dir = far - near; dir.Normalize();
        var ray = new Microsoft.Xna.Framework.Ray(near, dir);
        ObjectComponent best = null; float bestD = float.MaxValue;
        foreach (var o in BlueprintObjects(world))
        {
            if (!o.Visible || (o.Mode & ComponentRenderMode._3D) == 0) continue;
            var d = ray.Intersects(o.GetBounds());
            if (d.HasValue && d.Value < bestD) { bestD = d.Value; best = o; }
        }
        return best;
    }

    static void SetOverride(string name, bool sprite)
    {
        lists.Overrides[name] = sprite;
        SaveUserOverrides(); // takes effect on the next frame: Apply runs for every object, every frame
    }

    static void Show(string title, string message, Action use2d, Action use3d)
    {
        // Simitone's own dialog style (the FSO UIAlert has no button graphics in this skin)
        UIMobileAlert alert = null;
        ButtonClickDelegate close = b => { if (alert != null) alert.Close(); };
        UIAlertButton[] buttons;
        if (use2d == null)
            buttons = new[] { new UIAlertButton(UIAlertButtonType.OK, close, "OK") };
        else
            buttons = new[]
            {
                new UIAlertButton(UIAlertButtonType.Yes, b => { use2d(); close(b); }, "Use 2D model"),
                new UIAlertButton(UIAlertButtonType.No, b => { use3d(); close(b); }, "Use 3D model"),
                new UIAlertButton(UIAlertButtonType.Cancel, close, "Cancel"),
            };
        alert = new UIMobileAlert(new UIAlertOptions { Title = title, Message = message, Buttons = buttons });
        UIScreen.GlobalShowDialog(alert, true);
    }
}
