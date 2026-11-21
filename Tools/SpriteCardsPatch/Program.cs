using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: SpriteCardsPatch <appDir> <SpriteCards.dll> <outDir>
// Patches (copies written to outDir, originals untouched; each patch is idempotent):
//  1. FSO.LotView.dll   ObjectComponent.{DoDraw,Draw,Update,Preload} call SpriteCards.Apply(this, world),
//                       and _2DWorldBatch.DrawImmediate(sprite) always re-binds the sprite quad indices.
//  2. Simitone.Client.dll  the in-lot Options menu gets four extra entries (camera / object modes).
//  3. Simitone.Windows.exe Main calls SpriteCards.Startup(args): 3D is on by default, "-2d" turns it off.
var appDir = args[0]; var hookPath = args[1]; var outDir = args[2];
Directory.CreateDirectory(outDir);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(appDir);
resolver.AddSearchDirectory(Path.Combine(appDir, "Monogame", "Windows"));
var hook = AssemblyDefinition.ReadAssembly(hookPath);
var hookType = hook.MainModule.GetType("SpriteCards");
MethodDefinition Hook(string name, int paramCount = -1) => hookType.Methods.First(m => m.Name == name && (paramCount < 0 || m.Parameters.Count == paramCount));

ModuleDefinition Open(string file) => ModuleDefinition.ReadModule(Path.Combine(appDir, file), new ReaderParameters { AssemblyResolver = resolver });
int total = 0;

// ---------------------------------------------------------------- 1. FSO.LotView.dll
{
    var mod = Open("FSO.LotView.dll");
    int n = 0;
    var oc = mod.GetType("FSO.LotView.Components.ObjectComponent");
    var applyRef = mod.ImportReference(Hook("Apply"));
    string[] targets = { "DoDraw", "Draw", "Update", "Preload" };
    foreach (var m in oc.Methods.Where(m => m.HasBody && targets.Contains(m.Name) && m.Parameters.Count > 0 && m.Parameters.Last().ParameterType.Name == "WorldState"))
    {
        if (m.Body.Instructions.Take(4).Any(i => i.Operand is MethodReference mr && mr.Name == "Apply")) continue;
        var il = m.Body.GetILProcessor();
        var first = m.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg, m.Parameters.Last())); // the WorldState argument
        il.InsertBefore(first, il.Create(OpCodes.Call, applyRef));
        Console.WriteLine("patched " + m.FullName);
        n++;
    }
    // DrawImmediate only re-binds the sprite quad index buffer when !FSOEnvironment.DirectX, assuming nothing
    // else changed it. A mesh draw replaces it, so later sprites were drawn with the wrong triangles.
    var batch = mod.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == "_2DWorldBatch");
    foreach (var m in batch.Methods.Where(m => m.Name == "DrawImmediate" && m.HasBody && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "_2DStandaloneSprite"))
    {
        var load = m.Body.Instructions.FirstOrDefault(i => (i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "get_DirectX")
                                                         || (i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "DirectX"));
        if (load == null) { Console.WriteLine("DrawImmediate already patched or DirectX check not found"); continue; }
        load.OpCode = OpCodes.Ldc_I4_0; load.Operand = null;
        Console.WriteLine("patched " + m.FullName + " (always re-bind sprite indices)");
        n++;
    }
    if (n > 0) { mod.Write(Path.Combine(outDir, "FSO.LotView.dll")); total += n; }
}

// ---------------------------------------------------------------- 2. Simitone.Client.dll (Options menu)
{
    var mod = Open("Simitone.Client.dll");
    var panel = mod.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == "UIMainPanel");
    var m = panel.Methods.First(x => x.Name == "Switcher_OnCategorySelect");
    var ins = m.Body.Instructions;
    if (ins.Any(i => i.Operand is MethodReference r && r.Name == "Decorate" && r.DeclaringType.Name == "SpriteCards"))
        Console.WriteLine("options menu already patched");
    else
    {
        // the OPTIONS case: ... ldc.i4.3 ; newarr UICatFunc ; (3 entries) ; newobj UIButtonSubpanel::.ctor
        int ctorIdx = ins.ToList().FindIndex(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && r.DeclaringType.Name == "UIButtonSubpanel");
        int arrIdx = ins.ToList().FindLastIndex(ctorIdx, i => i.OpCode == OpCodes.Newarr && ((TypeReference)i.Operand).Name == "UICatFunc");
        if (ctorIdx < 0 || arrIdx < 1) throw new Exception("options menu array not found");
        var sizeIns = ins[arrIdx - 1];
        int oldSize = sizeIns.OpCode == OpCodes.Ldc_I4_3 ? 3 : sizeIns.OpCode == OpCodes.Ldc_I4_S ? (sbyte)sizeIns.Operand : (int)sizeIns.Operand;
        var catFuncCtor = ins.First(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && r.DeclaringType.Name == "UICatFunc").Operand as MethodReference;
        var actionCtor = ins.First(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r && r.DeclaringType.Name == "Action" && r.Parameters.Count == 2).Operand as MethodReference;
        var labelRef = mod.ImportReference(Hook("Label"));
        // (label id, icon, menu action)
        var entries = new (int id, string icon, string action)[]
        {
            (0, "opt_cam_2d.png", "Menu2D"),
            (1, "opt_cam_fixed.png", "MenuFixed"),
            (2, "opt_cam_classic.png", "MenuClassic"),
            (3, "opt_cam_free.png", "MenuFree"),
            (4, "opt_cam_reload.png", "MenuReload"),
        };
        sizeIns.OpCode = OpCodes.Ldc_I4; sizeIns.Operand = oldSize + entries.Length;
        var il = m.Body.GetILProcessor();
        var anchor = ins[ctorIdx];
        for (int i = 0; i < entries.Length; i++)
        {
            il.InsertBefore(anchor, il.Create(OpCodes.Dup));
            il.InsertBefore(anchor, il.Create(OpCodes.Ldc_I4, oldSize + i));
            il.InsertBefore(anchor, il.Create(OpCodes.Ldc_I4, entries[i].id));
            il.InsertBefore(anchor, il.Create(OpCodes.Call, labelRef));
            il.InsertBefore(anchor, il.Create(OpCodes.Ldstr, entries[i].icon));
            il.InsertBefore(anchor, il.Create(OpCodes.Ldnull));
            il.InsertBefore(anchor, il.Create(OpCodes.Ldftn, mod.ImportReference(Hook(entries[i].action))));
            il.InsertBefore(anchor, il.Create(OpCodes.Newobj, actionCtor));
            il.InsertBefore(anchor, il.Create(OpCodes.Newobj, catFuncCtor));
            il.InsertBefore(anchor, il.Create(OpCodes.Stelem_Ref));
        }
        // after "newobj UIButtonSubpanel" the new panel is on the stack: Decorate(panel) returns it (and colours the active button)
        var decorateRef = mod.ImportReference(Hook("Decorate"));
        var decorateCall = il.Create(OpCodes.Call, decorateRef);
        il.InsertAfter(anchor, decorateCall);
        // Decorate returns object; the consumer expects the subpanel type, so cast back
        il.InsertAfter(decorateCall, il.Create(OpCodes.Castclass, ((MethodReference)anchor.Operand).DeclaringType));
        Console.WriteLine($"patched {m.FullName}: options menu {oldSize} -> {oldSize + entries.Length} entries");
        mod.Write(Path.Combine(outDir, "Simitone.Client.dll")); total++;
    }
}

// ---------------------------------------------------------------- 3. Simitone.Windows.exe (3D on by default)
{
    var mod = Open("Simitone.Windows.exe");
    var program = mod.Types.First(t => t.Name == "Program");
    var main = program.Methods.First(x => x.Name == "Main");
    var ins = main.Body.Instructions;
    if (ins.Any(i => i.Operand is MethodReference r && r.Name == "Startup" && r.DeclaringType.Name == "SpriteCards"))
        Console.WriteLine("startup already patched");
    else
    {
        // right before MonogameLinker.Link(...), after the command line has been parsed
        var link = ins.First(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "Link" && r.DeclaringType.Name == "MonogameLinker");
        // arguments of Link are already being pushed; insert before the instruction that starts the call expression:
        // walk back to the statement start (first instruction after the previous stloc/stsfld/pop that leaves an empty stack)
        var il = main.Body.GetILProcessor();
        var startRef = mod.ImportReference(Hook("Startup"));
        var argsParam = main.Parameters.First(p => p.ParameterType.IsArray);
        // Link(useDX) is "ldloc useDX; call Link; stloc useDX": its statement begins one instruction before the call
        var stmtStart = link.Previous;
        il.InsertBefore(stmtStart, il.Create(OpCodes.Ldarg, argsParam));
        il.InsertBefore(stmtStart, il.Create(OpCodes.Call, startRef));
        Console.WriteLine("patched " + main.FullName + ": SpriteCards.Startup(args)");
        mod.Write(Path.Combine(outDir, "Simitone.Windows.exe")); total++;
    }
}

Console.WriteLine(total == 0 ? "nothing to patch" : $"done: {total} assembly(ies) written to {outDir}");
return 0;
