using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

// Integration tests for the AI conversations part of the patcher (4th argument = Conversations.dll), run against the
// real release. Skipped when SIMITONE_APP is not set.
//   CONVERSATIONS_DLL   built Conversations.dll   (default ../Conversations/out/Conversations.dll)
public class ConversationsFixture : IDisposable
{
    public string App = Environment.GetEnvironmentVariable("SIMITONE_APP");
    public string Hook, Patcher, Convo, Out, Log;
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

    public ConversationsFixture()
    {
        if (string.IsNullOrEmpty(App) || !Directory.Exists(App)) return;
        Hook = Find("SPRITECARDS_DLL", Path.Combine("SpriteCards", "out", "SpriteCards.dll"));
        Patcher = Find("SPRITECARDSPATCH", Path.Combine("SpriteCardsPatch", "out", "SpriteCardsPatch.dll"));
        Convo = Find("CONVERSATIONS_DLL", Path.Combine("Conversations", "out", "Conversations.dll"));
        Out = Path.Combine(Path.GetTempPath(), "convo-patch-" + Guid.NewGuid().ToString("N"));
        (ExitCode, Log) = Run(App, Out);
    }

    public (int, string) Run(string appDir, string outDir)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{Patcher}\" \"{appDir}\" \"{Hook}\" \"{outDir}\" \"{Convo}\"")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using (var p = Process.Start(psi))
        {
            var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, text);
        }
    }

    public void Dispose() { try { if (Out != null) Directory.Delete(Out, true); } catch { } }
}

public class ConversationsPatchTests : IClassFixture<ConversationsFixture>
{
    readonly ConversationsFixture f;
    public ConversationsPatchTests(ConversationsFixture fixture) { f = fixture; }

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
        Assert.True(File.Exists(Path.Combine(f.Out, "FSO.SimAntics.dll")), "FSO.SimAntics.dll was not written\n" + f.Log);
        Assert.True(File.Exists(Path.Combine(f.Out, "Conversations.dll")), "Conversations.dll was not copied\n" + f.Log);
    }

    [SkippableFact]
    public void VMContextConstructor_CallsTheHook_BeforeEveryReturn()
    {
        NeedsRelease();
        using (var m = Open("FSO.SimAntics.dll"))
        {
            var ctor = m.GetType("FSO.SimAntics.VMContext").Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count == 2 && x.Parameters[0].ParameterType.Name == "World");
            var rets = ctor.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            Assert.NotEmpty(rets);
            foreach (var ret in rets)
            {
                var call = ret.Previous;
                Assert.True(call.Operand is MethodReference r && r.Name == "OnContextReady" && r.DeclaringType.Name == "Boot", "no hook call before a return");
                Assert.Equal(OpCodes.Ldarg_0, call.Previous.OpCode);   // passes "this"
            }
        }
    }

    [SkippableFact]
    public void TheInjectedCall_ResolvesToARealMethodWithTheRightSignature()
    {
        NeedsRelease();
        using (var m = Open("FSO.SimAntics.dll"))
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Conversations.dll")))
        {
            var ctor = m.GetType("FSO.SimAntics.VMContext").Methods.Single(x => x.IsConstructor && !x.IsStatic && x.Parameters.Count == 2 && x.Parameters[0].ParameterType.Name == "World");
            var call = (MethodReference)ctor.Body.Instructions.First(i => i.Operand is MethodReference r && r.Name == "OnContextReady").Operand;
            var real = lib.GetType("Simitone.Conversations.Boot").Methods.Single(x => x.Name == "OnContextReady");
            Assert.True(real.IsStatic && real.IsPublic);
            Assert.Equal(call.Parameters.Count, real.Parameters.Count);
            Assert.Equal("VMContext", real.Parameters[0].ParameterType.Name);
        }
    }

    [SkippableFact]
    public void VMInternalTick_CallsBootOnTick_BeforeEveryReturn()
    {
        NeedsRelease();
        using (var m = Open("FSO.SimAntics.dll"))
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Conversations.dll")))
        {
            var tick = m.GetType("FSO.SimAntics.VM").Methods.Single(x => x.Name == "InternalTick" && x.Parameters.Count == 1);
            var rets = tick.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            Assert.NotEmpty(rets);
            foreach (var ret in rets)
            {
                var call = ret.Previous;
                Assert.True(call.Operand is MethodReference r && r.Name == "OnTick" && r.DeclaringType.Name == "Boot", "no OnTick hook call before a return");
                Assert.Equal(OpCodes.Ldarg_0, call.Previous.OpCode);
            }
            var real = lib.GetType("Simitone.Conversations.Boot").Methods.Single(x => x.Name == "OnTick");
            Assert.True(real.IsStatic && real.IsPublic);
            Assert.Single(real.Parameters);
            Assert.Equal("VM", real.Parameters[0].ParameterType.Name);
        }
    }

    [SkippableFact]
    public void VMEntityGetPieMenu_CallsBootFilterPieMenu_BeforeEveryReturn()
    {
        NeedsRelease();
        using (var m = Open("FSO.SimAntics.dll"))
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Conversations.dll")))
        {
            var pie = m.GetType("FSO.SimAntics.VMEntity").Methods.Single(x => x.Name == "GetPieMenu" && x.Parameters.Count == 4);
            var rets = pie.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
            Assert.NotEmpty(rets);
            foreach (var ret in rets)
            {
                var call = ret.Previous;
                Assert.True(call.Operand is MethodReference r && r.Name == "FilterPieMenu" && r.DeclaringType.Name == "Boot", "no FilterPieMenu hook call before a return");
                Assert.Equal(OpCodes.Ldarg_2, call.Previous.OpCode);
                Assert.Equal(OpCodes.Ldarg_1, call.Previous.Previous.OpCode);
                Assert.Equal(OpCodes.Ldarg_0, call.Previous.Previous.Previous.OpCode);
            }
            var real = lib.GetType("Simitone.Conversations.Boot").Methods.Single(x => x.Name == "FilterPieMenu");
            Assert.True(real.IsStatic && real.IsPublic);
            Assert.Equal(4, real.Parameters.Count);
            Assert.Equal("VMEntity", real.Parameters[1].ParameterType.Name);
            Assert.Equal("VM", real.Parameters[2].ParameterType.Name);
            Assert.Equal("VMEntity", real.Parameters[3].ParameterType.Name);
        }
    }

    [SkippableFact]
    public void EarlyStartupClass_MentionsNoGameTypes()
    {
        NeedsRelease();
        // it runs before MonoGame is linked, so merely loading it must not need any game assembly
        using (var lib = ModuleDefinition.ReadModule(Path.Combine(f.Out, "Conversations.dll")))
        {
            var early = lib.GetType("Simitone.Conversations.Early");
            Assert.NotNull(early);
            var types = early.Methods.SelectMany(x => x.Parameters.Select(p => p.ParameterType).Append(x.ReturnType)).Select(t => t.Scope?.Name ?? "").Distinct();
            Assert.DoesNotContain(types, s => s.StartsWith("FSO.") || s.StartsWith("MonoGame"));
        }
    }

    // the pie menu mode button needs both libraries (its slot follows the anti-aliasing and lighting buttons), so this one patches with both
    [SkippableFact]
    public void OptionsMenu_WithBothLibraries_HasThePieMenuModeButton()
    {
        NeedsRelease();
        var render = Environment.GetEnvironmentVariable("RENDER_DLL");
        Skip.If(string.IsNullOrEmpty(render) || !File.Exists(render), "RENDER_DLL is not set");
        var outDir = Path.Combine(Path.GetTempPath(), "convo-render-patch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{f.Patcher}\" \"{f.App}\" \"{f.Hook}\" \"{outDir}\" \"{f.Convo}\" \"{render}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();
                Assert.True(p.ExitCode == 0, text);
            }
            using (var m = ModuleDefinition.ReadModule(Path.Combine(outDir, "Simitone.Client.dll")))
            {
                var panel = m.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == "UIMainPanel");
                var sw = panel.Methods.First(x => x.Name == "Switcher_OnCategorySelect");
                foreach (var h in new[] { "MenuConversations", "MenuAntiAlias", "MenuLighting", "MenuPieMode" })
                    Assert.Contains(sw.Body.Instructions, i => i.Operand is MethodReference r && r.Name == h);
                Assert.Contains(sw.Body.Instructions, i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "opt_pie.png");
            }
        }
        finally { try { Directory.Delete(outDir, true); } catch { } }
    }

    [SkippableFact]
    public void OptionsMenu_AndLiveSimPanel_GetConversationsAndSimMemoryHooks()
    {
        NeedsRelease();
        using (var m = Open("Simitone.Client.dll"))
        {
            var panel = m.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes)).First(t => t.Name == "UIMainPanel");
            var sw = panel.Methods.First(x => x.Name == "Switcher_OnCategorySelect");
            var ctor = panel.Methods.First(x => x.IsConstructor && !x.IsStatic && x.HasBody);
            // Options keeps AI conversations toggle only (Sim Memory lives in the selected Sim Live panel)
            Assert.Contains(sw.Body.Instructions, i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "opt_convo.png");
            Assert.Contains(sw.Body.Instructions, i => i.Operand is MethodReference r && r.Name == "MenuConversations");
            Assert.DoesNotContain(sw.Body.Instructions, i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == "opt_memory.png");
            // Live Sim panel gets Sim Memory category (ID 5) + subpanel resolver
            Assert.Contains(ctor.Body.Instructions, i => i.Operand is MethodReference r && r.Name == "InitLiveCategories");
            Assert.Contains(sw.Body.Instructions, i => i.Operand is MethodReference r && r.Name == "ResolveSubpanel");
        }
    }

    [SkippableFact]
    public void ThePatchedAssembliesAreStructurallyValid()
    {
        NeedsRelease();
        using (var m = Open("FSO.SimAntics.dll"))
            foreach (var method in m.GetTypes().SelectMany(t => t.Methods).Where(x => x.HasBody)) Assert.NotEmpty(method.Body.Instructions);
    }

    [SkippableFact]
    public void Patching_IsIdempotent()
    {
        NeedsRelease();
        var app2 = Path.Combine(Path.GetTempPath(), "convo-app2-" + Guid.NewGuid().ToString("N"));
        var out2 = Path.Combine(Path.GetTempPath(), "convo-out2-" + Guid.NewGuid().ToString("N"));
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
            foreach (var file in Directory.GetFiles(f.Out)) File.Copy(file, Path.Combine(app2, Path.GetFileName(file)), true);   // already patched
            var (code, log) = f.Run(app2, out2);
            Assert.True(code == 0, log);
            Assert.False(Directory.Exists(out2) && Directory.GetFiles(out2).Length > 0, "second run rewrote files:\n" + log);
        }
        finally { try { Directory.Delete(app2, true); Directory.Delete(out2, true); } catch { } }
    }
}
