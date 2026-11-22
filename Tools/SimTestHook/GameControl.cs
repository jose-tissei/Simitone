using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FSO.SimAntics;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Primitives;

// Test-only game control for the SimTestHook pipe: list Sims, list and queue interactions between two Sims,
// read relationship values, answer the blocking dialog and set the game speed, without any mouse clicks.
// The pipe thread must never touch the VM, so every command is queued and executed on the game thread
// (SimTestHook.Mouse() drains the queue once per frame) and the pipe thread waits for the result.
public static class GameControl
{
    static readonly Queue<Action> jobs = new Queue<Action>();

    public static readonly HashSet<string> Verbs = new HashSet<string> { "sims", "menu", "interact", "rel", "dialog", "answer", "speed", "global", "time", "objs", "attr", "convo", "aa" };

    // called on the game thread, once per frame
    public static void Drain()
    {
        while (true)
        {
            Action a;
            lock (jobs) { if (jobs.Count == 0) return; a = jobs.Dequeue(); }
            a();
        }
    }

    // called on the pipe thread
    public static string Run(string[] p)
    {
        string result = null;
        var done = new ManualResetEventSlim(false);
        lock (jobs)
            jobs.Enqueue(() =>
            {
                try { result = Execute(p); }
                catch (Exception e) { result = "err " + e.GetType().Name + ": " + e.Message; }
                done.Set();
            });
        // the game thread only runs when the game window is updating
        return done.Wait(5000) ? result : "err the game thread did not respond (is the game loading, or minimised?)";
    }

    static VM GetVM()
    {
        var screen = FSO.Client.UI.Framework.UIScreen.Current;
        var prop = screen?.GetType().GetProperty("vm");
        var vm = prop?.GetValue(screen, null) as VM;
        if (vm == null || !vm.Ready) throw new Exception("no lot is loaded (enter a lot first)");
        return vm;
    }

    static string Execute(string[] p)
    {
        var vm = p[0] == "convo" ? null : GetVM();   // convo commands also work on the neighbourhood map
        switch (p[0])
        {
            case "sims": return Sims(vm);
            case "menu": return Menu(vm, Find(vm, p[1]), Find(vm, p[2]));
            case "interact": return Interact(vm, Find(vm, p[1]), Find(vm, p[2]), string.Join(" ", p.Skip(3)));
            case "rel": return Rel(Find(vm, p[1]), Find(vm, p[2]));
            case "dialog": return Dialog(vm);
            case "answer": return Answer(vm, byte.Parse(p[1]));
            case "objs":
                // objs [filter]: objects on the lot (not Sims) whose name contains the filter, with their first attributes
                var flt = p.Length > 1 ? p[1] : "";
                return "ok " + string.Join(" | ", vm.Entities.Where(e => !(e is VMAvatar) && (e.ToString() ?? "").IndexOf(flt, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(40).Select(e => e.ObjectID + ":" + e.ToString() + " attrs[" + string.Join(",", Enumerable.Range(0, 10).Select(i => e.GetAttribute(i))) + "]"));
            case "attr":
                // attr <objectId> <index> [value]: read or write an object attribute
                var eo = vm.GetObjectById(short.Parse(p[1]));
                if (eo == null) throw new Exception("no object " + p[1]);
                if (p.Length > 3) eo.SetAttribute(int.Parse(p[2]), short.Parse(p[3]));
                return "ok attr[" + p[2] + "]=" + eo.GetAttribute(int.Parse(p[2]));
            case "convo":
                // convo status|on|off|force N|tone N|last|log : the AI conversations feature (Conversations.dll), by reflection so
                // the hook works without it
                var boot = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Simitone.Conversations.Boot", false)).FirstOrDefault(t => t != null);
                if (boot == null) throw new Exception("Conversations.dll is not loaded");
                return "ok " + (string)boot.GetMethod("Command").Invoke(null, new object[] { p.Skip(1).ToArray() });
            case "aa":
                // aa [mode]: read or set the engine's anti-aliasing mode (0 off, 1 4x MSAA, 2 2x2 SSAA), by reflection
                var world = vm.Context.World;
                var asm = world.GetType().Assembly;
                var cfg = asm.GetTypes().First(t => t.Name == "WorldConfig").GetField("Current").GetValue(null);
                var aaMember = cfg.GetType().GetField("AA") ?? (System.Reflection.MemberInfo)cfg.GetType().GetProperty("AA");
                var ppx = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("FSO.Common.Utils.PPXDepthEngine", false)).First(t => t != null);
                if (p.Length > 1)
                {
                    if (aaMember is System.Reflection.FieldInfo fi) fi.SetValue(cfg, int.Parse(p[1])); else ((System.Reflection.PropertyInfo)aaMember).SetValue(cfg, int.Parse(p[1]), null);
                    world.GetType().GetMethod("ChangeAAMode").Invoke(world, new object[] { FSO.Client.GameFacade.GraphicsDevice });
                }
                return "ok ssaa=" + ppx.GetField("SSAA").GetValue(null) + " msaa=" + ppx.GetField("MSAA").GetValue(null);
            case "time":
                return "ok " + vm.Context.Clock.Hours.ToString("00") + ":" + vm.Context.Clock.Minutes.ToString("00") + " day " + vm.Context.Clock.DayOfMonth;
            case "global":
                // global <index> [value]: read or set a VM global (e.g. 25 = debug flags; value 6 turns on the social outcome dialog)
                var gi = ushort.Parse(p[1]);
                if (p.Length > 2) vm.SetGlobalValue(gi, short.Parse(p[2]));
                return "ok global[" + gi + "]=" + vm.GetGlobalValue(gi);
            case "speed":
                if (p.Length > 1) { vm.SpeedMultiplier = int.Parse(p[1]); vm.LastSpeedMultiplier = 0; }
                return "ok speed=" + vm.SpeedMultiplier;
        }
        return "err unknown";
    }

    static IEnumerable<VMAvatar> Avatars(VM vm) { return vm.Entities.OfType<VMAvatar>(); }

    static string Label(VMAvatar a)
    {
        // queue = interactions the Sim still has queued or running; 0 means idle
        return a.ObjectID + ":" + a.Name + "(neighbor " + a.GetPersonData(VMPersonDataVariable.NeighborId) + ", queue " + (a.Thread?.Queue?.Count ?? 0) + ")";
    }

    static string Sims(VM vm)
    {
        return "ok " + string.Join(" | ", Avatars(vm).Select(Label)) + " | speed=" + vm.SpeedMultiplier;
    }

    // a Sim is found by object id, or by a case-insensitive part of its name
    static VMAvatar Find(VM vm, string key)
    {
        short id;
        if (short.TryParse(key, out id))
        {
            var byId = vm.GetObjectById(id) as VMAvatar;
            if (byId != null) return byId;
        }
        var matches = Avatars(vm).Where(a => (a.Name ?? "").IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (matches.Count == 1) return matches[0];
        throw new Exception(matches.Count == 0 ? "no Sim matches '" + key + "'" : "'" + key + "' matches several Sims: " + string.Join(", ", matches.Select(Label)));
    }

    static string Norm(string s) { return (s ?? "").Replace('\\', '/').Trim(); }

    static List<VMPieMenuInteraction> PieMenu(VM vm, VMAvatar actor, VMAvatar target)
    {
        return target.GetPieMenu(vm, actor, false, true);
    }

    static string Menu(VM vm, VMAvatar actor, VMAvatar target)
    {
        var items = PieMenu(vm, actor, target);
        return "ok " + actor.Name + " -> " + target.Name + ": " +
            string.Join(" | ", items.Select(i => "#" + i.ID + (i.Global ? "g" : "") + " " + Norm(i.Name) + (i.Param0 != 0 ? " (p" + i.Param0 + ")" : "")));
    }

    static string Interact(VM vm, VMAvatar actor, VMAvatar target, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new Exception("interaction name required (see 'menu')");
        var items = PieMenu(vm, actor, target);
        var exact = items.Where(i => string.Equals(Norm(i.Name), Norm(name), StringComparison.OrdinalIgnoreCase)).ToList();
        var found = exact.Count > 0 ? exact : items.Where(i => Norm(i.Name).IndexOf(Norm(name), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (found.Count == 0) throw new Exception("no interaction '" + name + "' available from " + actor.Name + " to " + target.Name);
        if (found.Count > 1) throw new Exception("'" + name + "' is ambiguous: " + string.Join(", ", found.Select(i => Norm(i.Name))));
        var act = found[0];
        vm.SendCommand(new VMNetInteractionCmd
        {
            Interaction = act.ID,
            ActorUID = actor.PersistID,
            CallerID = actor.ObjectID,
            CalleeID = target.ObjectID,
            Param0 = act.Param0,
            Global = act.Global
        });
        return "ok queued " + Norm(act.Name) + " (" + actor.Name + " -> " + target.Name + ")";
    }

    static string Rel(VMAvatar a, VMAvatar b)
    {
        return "ok " + One(a, b) + " | " + One(b, a);
    }

    static string One(VMAvatar from, VMAvatar to)
    {
        var nb = FSO.Content.Content.Get().Neighborhood;
        var f = nb.GetNeighborByID(from.GetPersonData(VMPersonDataVariable.NeighborId));
        var tid = to.GetPersonData(VMPersonDataVariable.NeighborId);
        List<short> v;
        var vals = (f != null && f.Relationships.TryGetValue(tid, out v)) ? "[" + string.Join(",", v) + "]" : "none";
        // slot 0 = short-term, 1 = its flag, 2 = long-term, 3 = its flag (see docs/architecture/RELATIONSHIPS.md)
        return from.Name + "->" + to.Name + " " + vals;
    }

    static string Dialog(VM vm)
    {
        var owner = vm.GlobalBlockingDialog;
        var waiting = owner != null && owner.Thread.BlockingState is VMDialogResult;
        return "ok dialog=" + (waiting ? "waiting for " + owner.Name : "none") + " speed=" + vm.SpeedMultiplier;
    }

    static string Answer(VM vm, byte code)
    {
        if (vm.GlobalBlockingDialog == null) throw new Exception("no blocking dialog is waiting");
        vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = code, ResponseText = "" });
        return "ok answered " + code;
    }
}
