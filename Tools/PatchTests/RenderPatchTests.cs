using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

// Integration tests for the render part of the patcher (5th argument = Render.dll), run against the real release.
// Skipped when SIMITONE_APP is not set.  RENDER_DLL = built Render.dll (default ../Render/out/Render.dll)
public class RenderFixture : IDisposable
{
    public string App = Environment.GetEnvironmentVariable("SIMITONE_APP");
    public string Hook, Patcher, Render, Out, Log;
    public int ExitCode;

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

    public RenderFixture()
    {
        if (string.IsNullOrEmpty(App) || !Directory.Exists(App)) return;
        Hook = Find("SPRITECARDS_DLL", Path.Combine("SpriteCards", "out", "SpriteCards.dll"));
        Patcher = Find("SPRITECARDSPATCH", Path.Combine("SpriteCardsPatch", "out", "SpriteCardsPatch.dll"));
        Render = Find("RENDER_DLL", Path.Combine("Render", "out", "Render.dll"));
        Out = Path.Combine(Path.GetTempPath(), "render-patch-" + Guid.NewGuid().ToString("N"));
        var psi = new ProcessStartInfo("dotnet", $"\"{Patcher}\" \"{App}\" \"{Hook}\" \"{Out}\" \"-\" \"{Render}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using (var p = Process.Start(psi))
        {
            Log = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            ExitCode = p.ExitCode;
        }
    }

    public void Dispose() { try { if (Out != null) Directory.Delete(Out, true); } catch { } }
}

public class RenderPatchTests : IClassFixture<RenderFixture>
{
    readonly RenderFixture f;
    public RenderPatchTests(RenderFixture fixture) { f = fixture; }

    void NeedsRelease() { Skip.If(string.IsNullOrEmpty(f.App) || !Directory.Exists(f.App), "SIMITONE_APP is not set"); }

    ModuleDefinition Open(string file)
    {
        var r = new DefaultAssemblyResolver();
        r.AddSearchDirectory(f.Out); r.AddSearchDirectory(f.App); r.AddSearchDirectory(Path.Combine(f.App, "Monogame", "Windows"));
        return ModuleDefinition.ReadModule(Path.Combine(f.Out, file), new ReaderParameters { AssemblyResolver = r });
    }

    [SkippableFact]
    public void Patcher_Succeeds_AndShipsTheLibrary()
    {
        NeedsRelease();
        Assert.True(f.ExitCode == 0, f.Log);
        Assert.True(File.Exists(Path.Combine(f.Out, "FSO.Common.dll")), "FSO.Common.dll was not written\n" + f.Log);
        Assert.True(File.Exists(Path.Combine(f.Out, "Render.dll")), "Render.dll was not copied\n" + f.Log);
    }

    [SkippableFact]
    public void DrawBackbuffer_StartsWith_IfTryDrawReturn()
    {
        NeedsRelease();
        using (var m = Open("FSO.Common.dll"))
        {
            var blit = m.GetType("FSO.Common.Utils.PPXDepthEngine").Methods.Single(x => x.Name == "DrawBackbuffer" && x.IsStatic);
            var ins = blit.Body.Instructions;
            Assert.Equal(OpCodes.Ldarg_0, ins[0].OpCode);                 // opacity
            Assert.Equal(OpCodes.Ldarg_1, ins[1].OpCode);                 // scale
            Assert.True(ins[2].Operand is MethodReference r && r.Name == "TryDraw" && r.DeclaringType.Name == "PostAA");
            Assert.Equal(OpCodes.Brfalse, ins[3].OpCode);
            Assert.Equal(OpCodes.Ret, ins[4].OpCode);
            Assert.Same(ins[5], ins[3].Operand);                          // false = continue with the original first instruction
        }
    }

    [SkippableFact]
    public void TheInjectedCall_MatchesTheLibrarySignature()
    {
        NeedsRelease();
        using (var m = Open("FSO.Common.dll"))
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Render.dll")))
        {
            var real = lib.GetType("Simitone.Render.PostAA").Methods.Single(x => x.Name == "TryDraw");
            Assert.True(real.IsStatic && real.IsPublic);
            Assert.Equal("Boolean", real.ReturnType.Name);
            Assert.Equal(new[] { "Single", "Single" }, real.Parameters.Select(p => p.ParameterType.Name).ToArray());
        }
    }

    [SkippableFact]
    public void TheLibrary_EmbedsTheFxaaEffect_AsMgfxVersion8()
    {
        NeedsRelease();
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Render.dll")))
        {
            var res = lib.Resources.OfType<EmbeddedResource>().Single(x => x.Name == "Fxaa.mgfxo");
            var bytes = res.GetResourceData();
            Assert.Equal("MGFX", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(8, bytes[4]);   // the format version the game's MonoGame fork reads
        }
    }

    [SkippableFact]
    public void TheLibrary_EmbedsSmaa_AsMgfxVersion8_WithItsLookupTables()
    {
        NeedsRelease();
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Render.dll")))
        {
            byte[] Get(string n) { return lib.Resources.OfType<EmbeddedResource>().Single(x => x.Name == n).GetResourceData(); }
            var fx = Get("Smaa.mgfxo");
            Assert.Equal("MGFX", System.Text.Encoding.ASCII.GetString(fx, 0, 4));
            Assert.Equal(8, fx[4]);
            Assert.Equal(160 * 560 * 2, Get("AreaTex.bin").Length);
            Assert.Equal(64 * 16, Get("SearchTex.bin").Length);
        }
    }

    [SkippableFact]
    public void SimitoneGameConstructor_AsksTheLibraryForTheGraphicsProfile()
    {
        NeedsRelease();
        using (var m = Open("Simitone.Client.dll"))
        {
            var game = m.Types.First(t => t.Name == "SimitoneGame");
            var calls = game.Methods.Where(x => x.IsConstructor && x.HasBody).SelectMany(x => x.Body.Instructions).Select(i => i.Operand).OfType<MethodReference>().ToList();
            Assert.Contains(calls, r => r.Name == "SetVsync" && r.DeclaringType.Name == "Boot");
            Assert.DoesNotContain(calls, r => r.Name == "set_SynchronizeWithVerticalRetrace");   // replaced, not duplicated
        }
    }

    [SkippableFact]
    public void PrepareLighting_EndsWith_TheLightingHook_BeforeEveryReturn()
    {
        NeedsRelease();
        using (var m = Open("FSO.LotView.dll"))
        {
            var prep = m.GetType("FSO.LotView.WorldState").Methods.Single(x => x.Name == "PrepareLighting" && x.HasBody);
            var rets = prep.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            Assert.NotEmpty(rets);
            foreach (var ret in rets)
            {
                Assert.True(ret.Previous.Operand is MethodReference r && r.Name == "AfterPrepare" && r.DeclaringType.Name == "Lighting", "no lighting hook before a return");
                Assert.Equal(OpCodes.Ldarg_0, ret.Previous.Previous.OpCode);   // passes "this"
            }
        }
    }

    [SkippableFact]
    public void TheLibrary_EmbedsTheBounceEffect_AsMgfxVersion8()
    {
        NeedsRelease();
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Render.dll")))
        {
            var bytes = lib.Resources.OfType<EmbeddedResource>().Single(x => x.Name == "Bounce.mgfxo").GetResourceData();
            Assert.Equal("MGFX", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(8, bytes[4]);
        }
    }
}
