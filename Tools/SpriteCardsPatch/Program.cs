using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: SpriteCardsPatch <appDir> <SpriteCards.dll> <outDir>
// Patches FSO.LotView.dll so ObjectComponent calls SpriteCards.Apply(this) at the top of
// DoDraw / Draw / Update / Preload. Apply sets the per-object sprite-vs-mesh mode.
// Output goes to outDir (original untouched). Idempotent: skips methods already patched.
var appDir = args[0]; var hookPath = args[1]; var outDir = args[2];
Directory.CreateDirectory(outDir);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(appDir);
resolver.AddSearchDirectory(Path.Combine(appDir, "Monogame", "Windows"));
var hook = AssemblyDefinition.ReadAssembly(hookPath);
var apply = hook.MainModule.GetType("SpriteCards").Methods.First(m => m.Name == "Apply");

var lvPath = Path.Combine(appDir, "FSO.LotView.dll");
var mod = ModuleDefinition.ReadModule(lvPath, new ReaderParameters { AssemblyResolver = resolver });
var oc = mod.GetType("FSO.LotView.Components.ObjectComponent");
var applyRef = mod.ImportReference(apply);
string[] targets = { "DoDraw", "Draw", "Update", "Preload" };
int n = 0;
foreach (var m in oc.Methods.Where(m => m.HasBody && targets.Contains(m.Name) && m.Parameters.Count > 0 && m.Parameters.Last().ParameterType.Name == "WorldState"))
{
    var first = m.Body.Instructions[0];
    if (m.Body.Instructions.Take(4).Any(i => i.Operand is MethodReference mr && mr.Name == "Apply")) continue;
    var il = m.Body.GetILProcessor();
    il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
    il.InsertBefore(first, il.Create(OpCodes.Ldarg, m.Parameters.Last())); // the WorldState argument
    il.InsertBefore(first, il.Create(OpCodes.Call, applyRef));
    Console.WriteLine("patched " + m.FullName);
    n++;
}
// _2DWorldBatch.DrawImmediate(_2DStandaloneSprite) only re-binds the sprite quad index buffer when
// !FSOEnvironment.DirectX, assuming nothing else changed it. A mesh draw (Draw3D) replaces the
// index buffer, so later sprites are drawn with the wrong triangles. Make it always re-bind.
var batch = mod.GetType("FSO.LotView.Utils._2DWorldBatch") ?? mod.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == "_2DWorldBatch");
foreach (var m in batch.Methods.Where(m => m.Name == "DrawImmediate" && m.HasBody && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "_2DStandaloneSprite"))
{
    var load = m.Body.Instructions.FirstOrDefault(i => (i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "get_DirectX")
                                                     || (i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == "DirectX"));
    if (load == null) { Console.WriteLine("DirectX check not found in " + m.FullName); continue; }
    load.OpCode = OpCodes.Ldc_I4_0; load.Operand = null;
    Console.WriteLine("patched " + m.FullName + " (always re-bind sprite indices)");
    n++;
}
if (n == 0) { Console.WriteLine("nothing to patch"); return 0; }
mod.Write(Path.Combine(outDir, "FSO.LotView.dll"));
Console.WriteLine($"done: {n} method(s)");
return 0;
