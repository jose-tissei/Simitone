using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: InputHookPatch <appDir> <SimTestHook.dll> <outDir>
// Rewrites calls in the game's own assemblies so that
//   Mouse.GetState()    -> SimTestHook.Mouse()
//   Keyboard.GetState() -> SimTestHook.Keyboard()
//   Game.get_IsActive   -> SimTestHook.IsActive(Game)
// Patched copies are written to outDir (originals untouched). Idempotent: already-patched calls
// no longer match.
var appDir = args[0]; var hookPath = args[1]; var outDir = args[2];
Directory.CreateDirectory(outDir);
var hookAsm = AssemblyDefinition.ReadAssembly(hookPath);
var hookType = hookAsm.MainModule.GetType("SimTestHook");
MethodDefinition Hook(string n) => hookType.Methods.First(m => m.Name == n);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(appDir);
resolver.AddSearchDirectory(Path.Combine(appDir, "Monogame", "Windows"));

static MethodDefinition SafeResolve(MethodReference r) { try { return r.Resolve(); } catch { return null; } }
int total = 0;
foreach (var file in Directory.GetFiles(appDir).Where(f => (Path.GetFileName(f).StartsWith("FSO.") || Path.GetFileName(f).StartsWith("Simitone.")) && (f.EndsWith(".dll") || f.EndsWith(".exe"))))
{
    ModuleDefinition mod;
    try { mod = ModuleDefinition.ReadModule(file, new ReaderParameters { AssemblyResolver = resolver }); } catch { continue; }
    int n = 0;
    MethodReference mouse = null, kb = null, active = null;
    foreach (var type in mod.GetTypes())
        foreach (var m in type.Methods.Where(m => m.HasBody))
            foreach (var ins in m.Body.Instructions)
            {
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                if (ins.Operand is not MethodReference mr) continue;
                var dt = mr.DeclaringType.FullName;
                if (dt == "Microsoft.Xna.Framework.Input.Mouse" && mr.Name == "GetState") { mouse ??= mod.ImportReference(Hook("Mouse")); ins.Operand = mouse; ins.OpCode = OpCodes.Call; n++; }
                else if (dt == "Microsoft.Xna.Framework.Input.Keyboard" && mr.Name == "GetState" && mr.Parameters.Count == 0) { kb ??= mod.ImportReference(Hook("Keyboard")); ins.Operand = kb; ins.OpCode = OpCodes.Call; n++; }
                else if (mr.Name == "get_IsActive" && (dt == "Microsoft.Xna.Framework.Game" || SafeResolve(mr)?.DeclaringType.FullName == "Microsoft.Xna.Framework.Game")) { active ??= mod.ImportReference(Hook("IsActive")); ins.Operand = active; ins.OpCode = OpCodes.Call; n++; }
            }
    if (n == 0) { mod.Dispose(); continue; }
    var outPath = Path.Combine(outDir, Path.GetFileName(file));
    mod.Write(outPath);
    Console.WriteLine($"{Path.GetFileName(file)}: {n} call(s) rewritten");
    total += n;
}
Console.WriteLine(total == 0 ? "nothing to patch (already patched?)" : $"done: {total} call(s)");
return 0;
