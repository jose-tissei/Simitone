using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

// Integration tests: run the real SpriteCardsPatch against the real Simitone release and check the patched
// assemblies. No Sims game files are needed. Set SIMITONE_APP to an extracted SimitoneWindows.zip; without it
// the tests are skipped (CI downloads the release and sets it).
//   SIMITONE_APP        extracted release directory (required)
//   SPRITECARDS_DLL     built hook assembly   (default ../SpriteCards/out/SpriteCards.dll)
//   SPRITECARDSPATCH    built patcher dll     (default ../SpriteCardsPatch/out/SpriteCardsPatch.dll)
public class PatchFixture : IDisposable
{
    public string App = Environment.GetEnvironmentVariable("SIMITONE_APP");
    public string Hook, Patcher, Out, Log;
    public int ExitCode;
    public Dictionary<string, string> OriginalHashes = new Dictionary<string, string>();

    static string Find(string env, string rel)
    {
        var v = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrEmpty(v)) return v;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, rel);
            if (File.Exists(p)) return p;
        }
        return Path.Combine("..", rel);
    }

    public PatchFixture()
    {
        if (string.IsNullOrEmpty(App) || !Directory.Exists(App)) return;
        Hook = Find("SPRITECARDS_DLL", Path.Combine("SpriteCards", "out", "SpriteCards.dll"));
        Patcher = Find("SPRITECARDSPATCH", Path.Combine("SpriteCardsPatch", "out", "SpriteCardsPatch.dll"));
        foreach (var f in Targets) OriginalHashes[f] = Hash(Path.Combine(App, f));
        Out = Path.Combine(Path.GetTempPath(), "spritecards-patch-" + Guid.NewGuid().ToString("N"));
        (ExitCode, Log) = RunPatcher(App, Out);
    }

    public static readonly string[] Targets = { "FSO.LotView.dll", "Simitone.Client.dll", "Simitone.Windows.exe" };

    public (int, string) RunPatcher(string appDir, string outDir)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{Patcher}\" \"{appDir}\" \"{Hook}\" \"{outDir}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using (var p = Process.Start(psi))
        {
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, text);
        }
    }

    public static string Hash(string path)
    {
        using (var s = File.OpenRead(path)) return Convert.ToHexString(SHA256.HashData(s));
    }

    public void Dispose() { try { if (Out != null) Directory.Delete(Out, true); } catch { } }
}

public class PatchTests : IClassFixture<PatchFixture>
{
    readonly PatchFixture f;
    public PatchTests(PatchFixture fixture) { f = fixture; }

    void NeedsRelease() { Skip.If(string.IsNullOrEmpty(f.App) || !Directory.Exists(f.App), "SIMITONE_APP is not set"); }

    ModuleDefinition Open(string file)
    {
        var r = new DefaultAssemblyResolver();
        r.AddSearchDirectory(f.Out); r.AddSearchDirectory(f.App); r.AddSearchDirectory(Path.Combine(f.App, "Monogame", "Windows"));
        return ModuleDefinition.ReadModule(Path.Combine(f.Out, file), new ReaderParameters { AssemblyResolver = r });
    }

    static TypeDefinition Type(ModuleDefinition m, string name) =>
        m.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == name);

    static IEnumerable<MethodReference> CallsTo(MethodDefinition m, string typeName) =>
        m.Body.Instructions.Where(i => i.Operand is MethodReference r && r.DeclaringType.Name == typeName).Select(i => (MethodReference)i.Operand);

    [SkippableFact]
    public void Patcher_Succeeds_AndWritesThePatchedAssemblies()
    {
        NeedsRelease();
        Assert.True(f.ExitCode == 0, f.Log);
        foreach (var t in PatchFixture.Targets) Assert.True(File.Exists(Path.Combine(f.Out, t)), t + " was not written\n" + f.Log);
    }

    [SkippableFact]
    public void Patcher_DoesNotTouchTheOriginals()
    {
        NeedsRelease();
        foreach (var t in PatchFixture.Targets) Assert.Equal(f.OriginalHashes[t], PatchFixture.Hash(Path.Combine(f.App, t)));
    }

    [SkippableFact]
    public void ObjectComponent_DrawEntryPoints_CallApplyFirst()
    {
        NeedsRelease();
        using (var m = Open("FSO.LotView.dll"))
        {
            var oc = Type(m, "ObjectComponent");
            foreach (var name in new[] { "DoDraw", "Draw", "Update", "Preload" })
            {
                var method = oc.Methods.Single(x => x.Name == name && x.Parameters.Count > 0 && x.Parameters.Last().ParameterType.Name == "WorldState");
                var first = method.Body.Instructions.Take(4).Select(i => i.Operand).OfType<MethodReference>().FirstOrDefault();
                Assert.True(first != null && first.Name == "Apply" && first.DeclaringType.Name == "SpriteCards", name + " does not start with SpriteCards.Apply");
            }
        }
    }

    [SkippableFact]
    public void SpriteBatch_DrawImmediate_AlwaysRebindsSpriteIndices()
    {
        NeedsRelease();
        using (var m = Open("FSO.LotView.dll"))
        {
            var method = Type(m, "_2DWorldBatch").Methods.Single(x => x.Name == "DrawImmediate" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.Name == "_2DStandaloneSprite");
            Assert.DoesNotContain(method.Body.Instructions, i => (i.OpCode == OpCodes.Call && i.Operand is MethodReference r && r.Name == "get_DirectX")
                                                              || (i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference fr && fr.Name == "DirectX"));
        }
    }

    [SkippableFact]
    public void OptionsMenu_HasFiveNewEntries_AndIsDecorated()
    {
        NeedsRelease();
        using (var m = Open("Simitone.Client.dll"))
        {
            var method = Type(m, "UIMainPanel").Methods.Single(x => x.Name == "Switcher_OnCategorySelect");
            var calls = CallsTo(method, "SpriteCards").Select(r => r.Name).ToList();
            Assert.Equal(5, calls.Count(n => n == "Label"));
            Assert.Single(calls, n => n == "Decorate");
            var handlers = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldftn).Select(i => ((MethodReference)i.Operand).Name).ToList();
            foreach (var h in new[] { "Menu2D", "MenuList", "MenuArch", "MenuFull3D", "MenuReload" }) Assert.Contains(h, handlers);
            var arrays = method.Body.Instructions.Where(i => i.OpCode == OpCodes.Newarr && ((TypeReference)i.Operand).Name == "UICatFunc").ToList();
            Assert.Single(arrays);
            var size = arrays[0].Previous;
            Assert.Equal(8, Convert.ToInt32(size.OpCode == OpCodes.Ldc_I4_8 ? 8 : size.Operand));
        }
    }

    [SkippableFact]
    public void LotControl_ShowPieMenu_StartsWithCtrlClickHook()
    {
        NeedsRelease();
        using (var m = Open("Simitone.Client.dll"))
        {
            var method = Type(m, "UILotControl").Methods.Single(x => x.Name == "ShowPieMenu" && x.Parameters.Count == 2);
            var first = method.Body.Instructions.Take(6).Select(i => i.Operand).OfType<MethodReference>().FirstOrDefault();
            Assert.True(first != null && first.Name == "CtrlClick", "ShowPieMenu does not start with SpriteCards.CtrlClick");
        }
    }

    [SkippableFact]
    public void Main_CallsStartup_Before3DIsDecided()
    {
        NeedsRelease();
        using (var m = Open("Simitone.Windows.exe"))
        {
            var main = Type(m, "Program").Methods.Single(x => x.Name == "Main");
            var ins = main.Body.Instructions.ToList();
            var startup = ins.FindIndex(i => i.Operand is MethodReference r && r.Name == "Startup" && r.DeclaringType.Name == "SpriteCards");
            var link = ins.FindIndex(i => i.Operand is MethodReference r && r.Name == "Link");
            Assert.True(startup >= 0, "SpriteCards.Startup is not called");
            Assert.True(startup < link, "Startup must run before MonogameLinker.Link");
        }
    }

    [SkippableFact]
    public void EveryInjectedCall_ResolvesToARealHookMethod()
    {
        NeedsRelease();
        var hook = AssemblyDefinition.ReadAssembly(f.Hook).MainModule.GetType("SpriteCards");
        foreach (var file in PatchFixture.Targets)
            using (var m = Open(file))
                foreach (var method in m.GetTypes().SelectMany(t => t.Methods).Where(x => x.HasBody))
                    foreach (var r in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().Where(r => r.DeclaringType.Name == "SpriteCards"))
                        Assert.True(hook.Methods.Any(h => h.Name == r.Name && h.Parameters.Count == r.Parameters.Count && h.IsPublic && h.IsStatic),
                                    $"{file}: {method.FullName} calls missing hook {r.FullName}");
    }

    [SkippableFact]
    public void PatchedAssemblies_AreStructurallyValid()
    {
        NeedsRelease();
        foreach (var file in PatchFixture.Targets)
            using (var m = Open(file))
                foreach (var method in m.GetTypes().SelectMany(t => t.Methods).Where(x => x.HasBody))
                    Assert.NotEmpty(method.Body.Instructions); // forces every body to decode
    }

    [SkippableFact]
    public void Patching_IsIdempotent()
    {
        NeedsRelease();
        // a second app dir whose three target files are already patched
        var app2 = Path.Combine(Path.GetTempPath(), "spritecards-app2-" + Guid.NewGuid().ToString("N"));
        var out2 = Path.Combine(Path.GetTempPath(), "spritecards-out2-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(app2);
            foreach (var file in Directory.GetFiles(f.App)) File.Copy(file, Path.Combine(app2, Path.GetFileName(file)), true);
            var mono = Path.Combine(f.App, "Monogame", "Windows");
            if (Directory.Exists(mono))
            {
                Directory.CreateDirectory(Path.Combine(app2, "Monogame", "Windows"));
                foreach (var file in Directory.GetFiles(mono)) File.Copy(file, Path.Combine(app2, "Monogame", "Windows", Path.GetFileName(file)), true);
            }
            foreach (var t in PatchFixture.Targets) File.Copy(Path.Combine(f.Out, t), Path.Combine(app2, t), true);

            var (code, log) = f.RunPatcher(app2, out2);
            Assert.True(code == 0, log);
            Assert.False(Directory.Exists(out2) && Directory.GetFiles(out2).Length > 0, "second run rewrote files:\n" + log);
        }
        finally { try { Directory.Delete(app2, true); Directory.Delete(out2, true); } catch { } }
    }
}
