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
    public void TextureGet_GuardsAgainstATextureThatCouldNotBeLoaded()
    {
        NeedsRelease();
        using (var m = Open("FSO.Content.dll"))
        {
            var get = m.GetType("FSO.Content.Model.AbstractTextureRef").Methods.Single(x => x.Name == "Get" && x.HasBody);
            var ins = get.Body.Instructions.ToList();
            var call = ins.Single(i => i.Operand is MethodReference r && r.Name == "OrPlaceholder" && r.DeclaringType.Name == "Fixes");
            int k = ins.IndexOf(call);
            Assert.Equal(OpCodes.Stfld, ins[k + 1].OpCode);                                  // the placeholder is stored back into _Instance
            Assert.Equal("_Instance", ((FieldReference)ins[k + 1].Operand).Name);
            // the guard runs before the first use of the DirectX flag that follows the loading code (and so before the Tag is written)
            var tagSet = ins.First(i => i.Operand is MethodReference r2 && r2.Name == "set_Tag");
            Assert.True(ins.IndexOf(tagSet) > k, "the Tag is written before the guard");
        }
    }

    [SkippableFact]
    public void DirPassOffset_AsksTheDirectionalShadingHook()
    {
        NeedsRelease();
        using (var m = Open("FSO.LotView.dll"))
        {
            var getter = m.GetType("FSO.LotView.WorldConfig").Methods.Single(x => x.Name == "get_DirPassOffset" && x.HasBody);
            var ins = getter.Body.Instructions;
            Assert.Equal(OpCodes.Ldarg_0, ins[0].OpCode);                                   // the AdvancedLighting flag is still loaded first
            var call = ins[2];
            Assert.True(call.Operand is MethodReference r && r.Name == "DirPass" && r.DeclaringType.Name == "Normals", "the getter does not call Normals.DirPass");
            Assert.Equal(OpCodes.Ret, ins[3].OpCode);                                       // and its answer is returned
        }
    }

    [SkippableFact]
    public void TheLibrary_EmbedsTheCascadesEffect_AsMgfxVersion8()
    {
        NeedsRelease();
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Render.dll")))
        {
            var bytes = lib.Resources.OfType<EmbeddedResource>().Single(x => x.Name == "Cascades.mgfxo").GetResourceData();
            Assert.Equal("MGFX", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(8, bytes[4]);
        }
    }

    [SkippableFact]
    public void TheShippedWorldSpriteEffect_HasTheThirdPass_AndTheGameFileHasTheOldTwo()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Render", "Shaders", "World2D.xnb");
        Skip.IfNot(File.Exists(shipped), "World2D.xnb not found");
        NeedsRelease();
        var mine = File.ReadAllBytes(shipped);
        var original = File.ReadAllBytes(Path.Combine(f.App, "Content", "DX", "Effects", "2DWorldBatch.xnb"));
        int Count(byte[] b, string word) { var t = System.Text.Encoding.ASCII.GetString(b); int n = 0, i = 0; while ((i = t.IndexOf(word, i, StringComparison.Ordinal)) >= 0) { n++; i += word.Length; } return n; }
        Assert.Equal(0, Count(original, "dirLighting"));
        Assert.True(Count(mine, "dirLighting") >= 1, "the replacement effect has no dirLighting pass");
        Assert.Equal(Count(original, "advLighting"), Count(mine, "advLighting"));   // everything else is kept
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
