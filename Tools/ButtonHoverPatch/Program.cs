using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;

// Usage: tool <input FSO.UI.dll> <output dll> <appDir for resolving refs>
var input = args[0]; var output = args[1]; var appDir = args[2];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(appDir);
var asm = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
var ui = asm.MainModule.GetType("FSO.Client.UI.UILayer") ?? asm.MainModule.Types.First(t => t.Name == "UILayer");
foreach (var m in ui.Methods.Where(m => m.HasBody))
{
    var il = m.Body.GetILProcessor();
    var stsfld = m.Body.Instructions.FirstOrDefault(i => i.OpCode == OpCodes.Stsfld && ((FieldReference)i.Operand).Name == "DefaultButton");
    if (stsfld == null) continue;
    Console.WriteLine($"Found DefaultButton init in {m.FullName}");
    var colorCtor = m.Body.Instructions.Where(i => i.OpCode == OpCodes.Newobj && ((MethodReference)i.Operand).DeclaringType.Name == "Color"
        && ((MethodReference)i.Operand).Parameters.Count == 3).Select(i => (MethodReference)i.Operand).First();
    var textStyle = ((FieldReference)stsfld.Operand).DeclaringType.Resolve();
    var highlighted = textStyle.Fields.First(f => f.Name == "HighlightedColor");
    var hRef = asm.MainModule.ImportReference(highlighted);
    // stack top is the TextStyle: dup; new Color(0,0x38,0x7B); stfld HighlightedColor
    il.InsertBefore(stsfld, il.Create(OpCodes.Dup));
    il.InsertBefore(stsfld, il.Create(OpCodes.Ldc_I4, 0x00));
    il.InsertBefore(stsfld, il.Create(OpCodes.Ldc_I4, 0x38));
    il.InsertBefore(stsfld, il.Create(OpCodes.Ldc_I4, 0x7B));
    il.InsertBefore(stsfld, il.Create(OpCodes.Newobj, colorCtor));
    il.InsertBefore(stsfld, il.Create(OpCodes.Stfld, hRef));
    asm.Write(output);
    Console.WriteLine("Patched -> " + output);
    return 0;
}
Console.WriteLine("DefaultButton init not found"); return 1;
