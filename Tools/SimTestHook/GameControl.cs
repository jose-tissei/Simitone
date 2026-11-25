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

    public static readonly HashSet<string> Verbs = new HashSet<string> { "sims", "menu", "interact", "rel", "dialog", "answer", "speed", "global", "time", "objs", "attr", "persona", "setrel", "motives", "focus", "convo", "aa", "render", "gotolot", "families", "playhouse", "lotinfo", "alerts", "dismiss", "camera", "lotname", "state", "autodialog" };

    // called on the game thread, once per frame
    public static void Drain()
    {
        AutoTick();
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
                catch (Exception e) { var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e; result = "err " + inner.GetType().Name + ": " + inner.Message + " @ " + (inner.StackTrace ?? "").Split((char)10).FirstOrDefault(); }
                done.Set();
            });
        // the game thread only runs when the game window is updating
        return done.Wait(5000) ? result : "err the game thread did not respond (is the game loading, or minimised?)";
    }

    static object Neighborhood()
    {
        var contentType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Content.Content", false)).First(x => x != null);
        var content = contentType.GetMethod("Get", Type.EmptyTypes).Invoke(null, null);
        return contentType.GetField("Neighborhood").GetValue(content);
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
        var vm = (p[0] == "state" || p[0] == "autodialog" || p[0] == "convo" || p[0] == "lotname" || p[0] == "families" || p[0] == "playhouse" || p[0] == "alerts" || p[0] == "dismiss") ? null : GetVM();   // convo commands also work on the neighbourhood map
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
            case "persona":
                // persona <sim> nice 900 outgoing 100 playful 500 active generous neat: personality points 0-1000 (test aid for videos)
                var pa = Find(vm, p[1]);
                for (int i = 2; i + 1 < p.Length; i += 2)
                {
                    VMPersonDataVariable pv;
                    switch (p[i].ToLowerInvariant())
                    {
                        case "nice": pv = VMPersonDataVariable.NicePersonality; break;
                        case "outgoing": pv = VMPersonDataVariable.OutgoingPersonality; break;
                        case "playful": pv = VMPersonDataVariable.PlayfulPersonality; break;
                        case "active": pv = VMPersonDataVariable.ActivePersonality; break;
                        case "generous": pv = VMPersonDataVariable.GenerousPersonality; break;
                        case "neat": pv = VMPersonDataVariable.NeatPersonality; break;
                        default: throw new Exception("trait is nice|outgoing|playful|active|generous|neat");
                    }
                    pa.SetPersonData(pv, short.Parse(p[i + 1]));
                }
                return "ok " + pa.Name + " nice=" + pa.GetPersonData(VMPersonDataVariable.NicePersonality) + " outgoing=" + pa.GetPersonData(VMPersonDataVariable.OutgoingPersonality) + " playful=" + pa.GetPersonData(VMPersonDataVariable.PlayfulPersonality) + " active=" + pa.GetPersonData(VMPersonDataVariable.ActivePersonality) + " generous=" + pa.GetPersonData(VMPersonDataVariable.GenerousPersonality) + " neat=" + pa.GetPersonData(VMPersonDataVariable.NeatPersonality) + " mood=" + pa.GetMotiveData(VMMotive.Mood) + " social=" + pa.GetMotiveData(VMMotive.Social);
            case "focus":
            {
                // focus <sim> [<sim2>]: centre the camera on a Sim (or between two), for filming
                var fa = Find(vm, p[1]); var fb = p.Length > 2 ? Find(vm, p[2]) : fa;
                float fx = (fa.Position.TileX + fb.Position.TileX) / 2f + 0.5f, fy = (fa.Position.TileY + fb.Position.TileY) / 2f + 0.5f;
                vm.Context.World.State.CenterTile = new Microsoft.Xna.Framework.Vector2(fx, fy);
                return "ok center=" + fx + "," + fy;
            }
            case "motives":
                // motives <sim> <value -100..100>: sets every need to that value (a bad mood for a video)
                var ma = Find(vm, p[1]);
                foreach (VMMotive mv in Enum.GetValues(typeof(VMMotive))) { if (mv == VMMotive.Mood) continue; try { ma.SetMotiveData(mv, short.Parse(p[2])); } catch { } }
                return "ok " + ma.Name + " mood=" + ma.GetMotiveData(VMMotive.Mood);
            case "setrel":
                // setrel <from> <to> <short-term> <long-term>: one direction (test aid for videos)
                var sf = Find(vm, p[1]); var sto = Find(vm, p[2]);
                var snb = FSO.Content.Content.Get().Neighborhood;
                var sn = snb.GetNeighborByID(sf.GetPersonData(VMPersonDataVariable.NeighborId));
                var tid2 = (int)sto.GetPersonData(VMPersonDataVariable.NeighborId);
                List<short> rv;
                if (!sn.Relationships.TryGetValue(tid2, out rv) || rv == null) { rv = new List<short> { 0, 0, 0, 0 }; sn.Relationships[tid2] = rv; }
                while (rv.Count < 4) rv.Add(0);
                rv[0] = short.Parse(p[3]); rv[2] = short.Parse(p[4]);
                return "ok " + One(sf, sto);
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
            case "render":
                // render status | aa off|fxaa : the anti-aliasing / lighting passes (Render.dll), by reflection
                var rt = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Simitone.Render.PostAA", false)).FirstOrDefault(t => t != null);
                if (rt == null) throw new Exception("Render.dll is not loaded (it loads on the first drawn frame)");
                return "ok " + (string)rt.GetMethod("Command").Invoke(null, new object[] { p.Skip(1).ToArray() });
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
            case "lotinfo":
            {
                // lotinfo: what is loaded now: objects, Sims, and whether a blocking dialog is up
                int avatars = vm.Entities.Count(e => e is VMAvatar), objects = vm.Entities.Count - avatars;
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                var down = screen.GetType().GetField("Downtown")?.GetValue(screen);
                return "ok objects=" + objects + " sims=" + avatars + " downtown=" + down + " size=" + vm.Context.Architecture.Width + "x" + vm.Context.Architecture.Height + " hour=" + vm.Context.Clock.Hours;
            }
            case "state": return State();
            case "autodialog":
                // autodialog on [speed] | off: answers every dialog the game puts up (help hints, event pop-ups) and keeps the speed; a dialog the VM waits on
                // freezes the simulation for good otherwise, and closing it on screen does not answer it
                AutoDialog = p.Length > 1 && p[1] == "on"; AutoSpeed = p.Length > 2 ? int.Parse(p[2]) : 0;
                return "ok autodialog=" + AutoDialog + " speed=" + AutoSpeed;
            case "alerts":
            case "dismiss":
            {
                if (p[0] == "dismiss") { int n = CloseAlerts(); bool ans = AnswerWaiting(); return "ok closed " + n + (ans ? " and answered the dialog the game was waiting on" : ""); }
                // alerts: the text of every alert box on screen ("title: message"); dismiss: closes them (what pressing OK does)
                var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
                var facade = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Client.GameFacade", false)).First(x => x != null);
                var screens = (facade.GetField("Screens", any)?.GetValue(null)) ?? facade.GetProperty("Screens", any)?.GetValue(null, null);
                var found = new List<string>(); var toClose = new List<object>();
                foreach (var f in screens.GetType().GetFields(any))
                {
                    var en = f.GetValue(screens) as System.Collections.IEnumerable;
                    if (en == null || f.FieldType == typeof(string)) continue;
                    foreach (var item in en)
                    {
                        var dlg = item?.GetType().GetField("Dialog")?.GetValue(item) ?? item;
                        var opt = dlg?.GetType().GetField("m_Options", any)?.GetValue(dlg);
                        if (opt == null) continue;
                        found.Add(Convert.ToString(opt.GetType().GetField("Title")?.GetValue(opt) ?? opt.GetType().GetProperty("Title")?.GetValue(opt, null)) + ": " +
                                  Convert.ToString(opt.GetType().GetField("Message")?.GetValue(opt) ?? opt.GetType().GetProperty("Message")?.GetValue(opt, null)));
                        toClose.Add(dlg);
                    }
                }
                if (p[0] == "dismiss")
                {
                    var uiScreen = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Client.UI.Framework.UIScreen", false)).First(x => x != null);
                    foreach (var d in toClose) uiScreen.GetMethod("RemoveDialog").Invoke(null, new object[] { d });
                    return "ok closed " + toClose.Count;
                }
                return "ok " + found.Count + (found.Count > 0 ? " | " + string.Join(" || ", found.Select(x => x.Replace((char)10, (char)32).Replace((char)13, (char)32))) : "");
            }
            case "families":
            {
                // families: the houses that have a family living in them, e.g. "1 3 7 21"
                var neigh = Neighborhood();
                var gm = neigh.GetType().GetMethod("GetFamilyForHouse");
                var houses = new List<string>();
                for (int h = 1; h < 100; h++) if (gm.Invoke(neigh, new object[] { (short)h }) != null) houses.Add(h.ToString());
                return "ok " + string.Join(" ", houses);
            }
            case "playhouse":
            {
                // playhouse <n>: what choosing "play" on that house in the neighbourhood does (sets the active family, loads the lot with its Sims)
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                var m = screen.GetType().GetMethod("PlayHouse");
                // the game's own "play" removes the neighbourhood switcher (the Create-a-Sim and Downtown/Vacation/Studio/Magic buttons) together with the
                // neighbourhood panel; passing null left those buttons drawn over the lot, which is not what a player ever sees
                object switcher = null;
                var kids = typeof(FSO.Client.UI.Framework.UIContainer).GetProperty("Children", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(screen) as System.Collections.IEnumerable;
                if (kids != null) foreach (var k in kids) if (k.GetType().Name == "UINeighbourhoodSwitcher") { switcher = k; break; }
                m.Invoke(screen, new object[] { short.Parse(p[1]), switcher });
                return "ok playing house " + p[1];
            }
            case "gotolot":
            {
                // gotolot <house number>: the same lot switch the Hole in the Ground (and other travel objects) cause: the game screen's VMLotSwitch(lotId)
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                var fam = screen.GetType().GetField("ActiveFamily", any)?.GetValue(screen);
                if (fam == null)
                {
                    // no family lives on this lot (a vacant lot), and the game's own switch assumes one: do what it does for a lot downtown instead
                    // (mark it as downtown, then clean up the old world and load the new one through InitializeLot, which clears the texture cache)
                    var contentType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Content.Content", false)).First(x => x != null);
                    var content = contentType.GetMethod("Get", Type.EmptyTypes).Invoke(null, null);
                    var neigh = contentType.GetField("Neighborhood").GetValue(content);
                    var path = (string)neigh.GetType().GetMethod("GetHousePath").Invoke(neigh, new object[] { int.Parse(p[1]) });
                    screen.GetType().GetField("Downtown").SetValue(screen, true);
                    screen.GetType().GetMethod("InitializeLot", new[] { typeof(string), typeof(bool) }).Invoke(screen, new object[] { path, false });
                    return "ok loaded lot " + p[1] + " directly: " + path;
                }
                var m = screen.GetType().GetMethod("VMLotSwitch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (m == null) throw new Exception("this screen has no VMLotSwitch");
                {
                    // what the Hole in the Ground's primitive (ChangeToLotInTemp0) records before it asks for the switch: the objects of the lot
                    // downtown read it back (the Sim to spawn, the family, whether the whole family travels), and fail with a null reference without it
                    var gm = GetVM();
                    var ct = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Content.Content", false)).First(x => x != null);
                    var nb = ct.GetField("Neighborhood").GetValue(ct.GetMethod("Get", Type.EmptyTypes).Invoke(null, null));
                    var state = nb.GetType().GetField("GameState")?.GetValue(nb) ?? nb.GetType().GetProperty("GameState").GetValue(nb, null);
                    var st = state.GetType();
                    uint target = uint.Parse(p[1]);
                    var avatar = gm.GetObjectById(gm.GetGlobalValue(3)) as VMAvatar ?? Avatars(gm).FirstOrDefault();
                    st.GetField("ActiveFamily").SetValue(state, gm.TS1State.CurrentFamily);
                    if (avatar != null) st.GetField("DowntownSimGUID").SetValue(state, avatar.Object.OBJ.GUID);
                    st.GetField("LotTransitInfo").SetValue(state, (target >= 40 && target < 50) ? (short)1 : gm.GetGlobalValue(34));
                }
                m.Invoke(screen, new object[] { uint.Parse(p[1]) });
                return "ok lot switch requested: " + p[1];
            }
            case "lotname":
            {
                // lotname <n>: the name and description the game gives house n (e.g. in the neighbourhood and magic town lists)
                var ct2 = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Content.Content", false)).First(x => x != null);
                var nb2 = ct2.GetField("Neighborhood").GetValue(ct2.GetMethod("Get", Type.EmptyTypes).Invoke(null, null));
                var nd = nb2.GetType().GetMethod("GetHouseNameDesc").Invoke(nb2, new object[] { int.Parse(p[1]) });
                return "ok " + nd.GetType().GetProperty("Item1").GetValue(nd, null) + " | " + ((string)nd.GetType().GetProperty("Item2").GetValue(nd, null)).Replace((char)10, (char)32).Replace((char)13, (char)32);
            }
            case "camera":
            {
                // camera: where the world camera looks, whether the world is drawn, and where the controlled Sim stands
                var w = vm.Context.World;
                // camera <x> <y>: look at that tile (a lot with no Sim on it keeps the camera at its corner)
                if (p.Length > 2) w.State.CenterTile = new Microsoft.Xna.Framework.Vector2(float.Parse(p[1]), float.Parse(p[2]));
                var c = vm.GetObjectById(vm.GetGlobalValue(3));
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                var lc = screen.GetType().GetProperty("LotControl")?.GetValue(screen, null) as FSO.Client.UI.Framework.UIElement;
                return "ok center=" + w.State.CenterTile + " zoom=" + w.State.Zoom + " worldVisible=" + w.Visible + " lotControlVisible=" + lc?.Visible +
                       " sim=" + (c == null ? "none" : c.Position + " lvl " + c.Position.Level) + " speed=" + vm.SpeedMultiplier + " cutaway=" + w.State.Level;
            }
            case "time":
                // time [hours [minutes]]: read the clock, or set it (the light follows on the next simulation ticks)
                if (p.Length > 1) { vm.Context.Clock.Hours = int.Parse(p[1]); vm.Context.Clock.Minutes = p.Length > 2 ? int.Parse(p[2]) : 0; }
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

    public static bool AutoDialog; public static int AutoSpeed; static int autoFrame;

    // once every few frames, on the game thread
    static void AutoTick()
    {
        if (!AutoDialog || ++autoFrame % 6 != 0) return;
        try
        {
            CloseAlerts();
            AnswerWaiting();
            var vm = GetVM();
            if (AutoSpeed > 0 && vm.SpeedMultiplier <= 0 && vm.GlobalBlockingDialog == null) { vm.SpeedMultiplier = AutoSpeed; vm.LastSpeedMultiplier = 0; }
        }
        catch { }
    }

    // answers the dialog the simulation is waiting on (the first choice)
    static bool AnswerWaiting()
    {
        try
        {
            var vm = GetVM();
            var owner = vm.GlobalBlockingDialog;
            if (owner == null || !(owner.Thread.BlockingState is VMDialogResult)) return false;
            vm.SendCommand(new VMNetDialogResponseCmd { ResponseCode = 0, ResponseText = "" });
            return true;
        }
        catch { return false; }
    }

    // closes the alert boxes shown on screen (what pressing OK does to the box)
    static int CloseAlerts()
    {
        var any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var facade = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Client.GameFacade", false)).First(x => x != null);
        var screens = (facade.GetField("Screens", any)?.GetValue(null)) ?? facade.GetProperty("Screens", any)?.GetValue(null, null);
        var toClose = new List<object>();
        foreach (var f in screens.GetType().GetFields(any))
        {
            var en = f.GetValue(screens) as System.Collections.IEnumerable;
            if (en == null || f.FieldType == typeof(string)) continue;
            foreach (var item in en)
            {
                var dlg = item?.GetType().GetField("Dialog")?.GetValue(item) ?? item;
                if (dlg?.GetType().GetField("m_Options", any)?.GetValue(dlg) != null) toClose.Add(dlg);
            }
        }
        var uiScreen = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("FSO.Client.UI.Framework.UIScreen", false)).First(x => x != null);
        foreach (var d in toClose) uiScreen.GetMethod("RemoveDialog").Invoke(null, new object[] { d });
        return toClose.Count;
    }

    static string J(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\""; }

    // one line of JSON with everything worth knowing about the game right now
    static string State()
    {
        var sb = new System.Text.StringBuilder("ok {");
        var screen = FSO.Client.UI.Framework.UIScreen.Current;
        sb.Append("\"screen\":" + J(screen == null ? null : screen.GetType().Name));
        VM vm = null; try { vm = GetVM(); } catch { }
        sb.Append(",\"lot\":" + (vm != null ? "true" : "false") + ",\"autodialog\":" + (AutoDialog ? "true" : "false"));
        if (vm != null)
        {
            var clk = vm.Context.Clock;
            sb.Append(",\"time\":" + J(clk.Hours.ToString("00") + ":" + clk.Minutes.ToString("00")) + ",\"day\":" + clk.DayOfMonth + ",\"speed\":" + vm.SpeedMultiplier);
            var owner = vm.GlobalBlockingDialog;
            sb.Append(",\"blockedBy\":" + (owner == null ? "null" : J(owner.Name + (owner.Thread.BlockingState is VMDialogResult ? " (waiting for an answer)" : ""))));
            var w = vm.Context.World;
            var active = vm.GetObjectById(vm.GetGlobalValue(3));
            sb.Append(",\"camera\":{\"x\":" + w.State.CenterTile.X.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + w.State.CenterTile.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"worldVisible\":" + (w.Visible ? "true" : "false") + "}");
            sb.Append(",\"sims\":[");
            bool first = true;
            foreach (var a in Avatars(vm))
            {
                if (!first) sb.Append(","); first = false;
                bool away = a.Position == FSO.LotView.Model.LotTilePos.OUT_OF_WORLD;
                var q = a.Thread?.Queue;
                sb.Append("{\"id\":" + a.ObjectID + ",\"name\":" + J(a.Name) + ",\"active\":" + (ReferenceEquals(a, active) ? "true" : "false") + ",\"away\":" + (away ? "true" : "false") +
                          ",\"pos\":" + (away ? "null" : "[" + a.Position.TileX + "," + a.Position.TileY + "," + a.Position.Level + "]") +
                          ",\"age\":" + a.GetPersonData(VMPersonDataVariable.PersonsAge) +
                          ",\"queue\":" + (q == null ? 0 : q.Count) + ",\"action\":" + J(q != null && q.Count > 0 ? q[0].Name : null) + ",\"priority\":" + (q != null && q.Count > 0 ? q[0].Priority : 0) +
                          ",\"social\":" + a.GetMotiveData(VMMotive.Social) + ",\"mood\":" + a.GetMotiveData(VMMotive.Mood) + ",\"energy\":" + a.GetMotiveData(VMMotive.Energy) + ",\"hunger\":" + a.GetMotiveData(VMMotive.Hunger) + "}");
            }
            sb.Append("]");
        }
        return sb.Append("}").ToString();
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
        // a trailing "auto" queues the action the way a Sim's own free will does (autonomous priority) instead of as the player's click
        bool auto = name.EndsWith(" auto", StringComparison.OrdinalIgnoreCase);
        if (auto) name = name.Substring(0, name.Length - 5);
        var items = PieMenu(vm, actor, target);
        var exact = items.Where(i => string.Equals(Norm(i.Name), Norm(name), StringComparison.OrdinalIgnoreCase)).ToList();
        var found = exact.Count > 0 ? exact : items.Where(i => Norm(i.Name).IndexOf(Norm(name), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (found.Count == 0) throw new Exception("no interaction '" + name + "' available from " + actor.Name + " to " + target.Name);
        if (found.Count > 1) throw new Exception("'" + name + "' is ambiguous: " + string.Join(", ", found.Select(i => Norm(i.Name))));
        var act = found[0];
        var cmd = new VMNetInteractionCmd
        {
            Interaction = act.ID,
            ActorUID = actor.PersistID,
            CallerID = actor.ObjectID,
            CalleeID = target.ObjectID,
            Param0 = act.Param0,
            Global = act.Global
        };
        if (auto)
        {
            var cp = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("Simitone.Conversations.ConvoPrimitive", false)).FirstOrDefault(x => x != null);
            cp?.GetMethod("ClearAsked")?.Invoke(null, new object[] { actor, target });   // the menu lookup above is not a player click
            cmd.Execute(vm, actor);
            foreach (var q in actor.Thread.Queue) if (q.Callee == target) q.Priority = (short)FSO.SimAntics.Engine.VMQueuePriority.Autonomous;
            return "ok queued " + Norm(act.Name) + " as an autonomous action (" + actor.Name + " -> " + target.Name + ")";
        }
        vm.SendCommand(cmd);
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
