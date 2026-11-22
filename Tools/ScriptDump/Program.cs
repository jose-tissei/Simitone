using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FSO.Files.FAR1;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;

class P
{
    static string[] Scopes = "MyObjAttr,StackObjAttr,TargetObjAttr,MyObject,StackObject,TargetObject,Global,Literal,Temps,Params,StackObjectID,TempByTemp,TreeAdRange,StackObjTemp,MyMotives,StackObjMotives,StackObjSlot,StackObjMotiveByTemp,MyPersonData,StackObjPersonData,MySlot,StackObjDefinition,StackObjAttrByParam,RoomByTemp0,NeighborInStackObj,Local,Tuning,DynSpriteFlag,TreeAdPersonality,TreeAdMin,MyPersonDataByTemp,StackObjPersonDataByTemp,NeighborPersonData,JobData,NeighborhoodData,StackObjFunction,MyTypeAttr,StackObjTypeAttr,NeighborsObjDef,Unused,LocalByTemp,StackObjAttrByTemp,TempXL".Split(',');
    static string[] Ops = "GT,LT,==,+=,-=,=,*=,/=,IsFlagSet,SetFlag,ClearFlag,IncAndLT,%=,&=,>=,<=,!=,DecAndGT,|=,^=,sqrt=".Split(',');
    static Dictionary<int, string> Prims = "0=sleep 1=generic_sims_call 2=expression 3=find_best_interaction 4=grab 5=drop 8=random_number 11=get_distance_to 13=push_interaction 14=find_best_object_for_function 20=run_functional_tree 21=show_string 22=look_towards 23=play_sound 24=old_relationship 26=relationship 29=set_motive_deltas 31=set_to_next 32=test_object_type 36=dialog_private 37=test_sim_interacting_with 38=dialog_global 39=dialog_semiglobal 41=set_balloon_headline 44=animate 45=goto_routing_slot 28=run_tree_by_name 65=find_best_action".Split(' ').ToDictionary(s => int.Parse(s.Split('=')[0]), s => s.Split('=')[1]);
    static string Sc(int s) { return s < Scopes.Length ? Scopes[s] : "scope" + s; }
    static string V(int scope, int data) { return scope == 7 ? data.ToString() : Sc(scope) + "[" + data + "]"; }

    static string Decode(BHAVInstruction ins, Dictionary<int, string> names)
    {
        var o = ins.Operand ?? new byte[8];
        var op = ins.Opcode;
        if (op == 2)
        {
            int l = BitConverter.ToInt16(o, 0), r = BitConverter.ToInt16(o, 2);
            int opr = o[5], lo = o[6], ro = o[7];
            return "EXPR " + V(lo, l) + " " + (opr < Ops.Length ? Ops[opr] : "op" + opr) + " " + V(ro, r) + (o[4] == 0 ? "" : " (signed)");
        }
        if (op == 26 || op == 24)
        {
            string s;
            if (op == 26)
            {
                int relvar = o[0], mode = o[1], flags = o[2], local = o[3], scope = BitConverter.ToUInt16(o, 4), data = BitConverter.ToInt16(o, 6);
                int setm = ((flags >> 2) & 1) | ((flags >> 4) & 2);
                s = "REL[" + new[] { "me->stack", "stack->me", "stack->local", "local->stack" }[mode & 3] + "] var" + relvar + " " + new[] { "GET into", "SET from", "ADD", "?" }[setm & 3] + " " + V(scope, data) + ((flags & 1) != 0 ? " failIfMissing" : "") + ((flags & 2) != 0 ? " useNeighbor" : "") + ((flags & 64) != 0 ? " noClamp" : "") + (mode >= 2 ? " local=" + local : "");
            }
            else
            {
                int gs = o[0], relvar = o[1], par = o[2], mode = o[3], flags = o[4];
                s = "REL(old)[" + new[] { "me->stack", "stack->me", "stack->arg0", "arg0->stack" }[mode & 3] + "] var" + relvar + " " + new[] { "GET into", "SET from", "ADD" }[Math.Min(gs, 2)] + " Params[" + par + "]" + ((flags & 1) != 0 ? " failIfMissing" : "") + ((flags & 2) != 0 ? " useNeighbor" : "");
            }
            return s;
        }
        string n;
        if (Prims.TryGetValue(op, out n)) return n + " [" + BitConverter.ToString(o) + "]";
        if (op >= 256) { string nm; names.TryGetValue(op, out nm); return "CALL " + op + " \"" + nm + "\" [" + BitConverter.ToString(o) + "]"; }
        return "prim" + op + " [" + BitConverter.ToString(o) + "]";
    }

    static string Ptr(int p) { return p == 255 ? "FALSE" : p == 254 ? "TRUE" : p == 253 ? "NEXT" : p.ToString(); }

    static void Main(string[] a)
    {
        IffFile.TargetTS1 = true;
        var root = a[0]; var outp = a[1];
        var sb = new StringBuilder();
        var globalNames = new Dictionary<int, string>();
        var loaded = new List<KeyValuePair<string, IffFile>>();
        foreach (var far in new[] { "GameData/Global/Global.far", "GameData/Objects/Objects.far" })
        {
            var arc = new FAR1Archive(Path.Combine(root, far), false);
            foreach (var e in arc.GetAllEntries())
            {
                if (!e.Key.EndsWith(".iff", StringComparison.OrdinalIgnoreCase)) continue;
                var iff = new IffFile(); iff.Read(new MemoryStream(e.Value));
                loaded.Add(new KeyValuePair<string, IffFile>(e.Key, iff));
                if (e.Key.Equals("Global.iff", StringComparison.OrdinalIgnoreCase))
                    foreach (var b in iff.List<BHAV>() ?? new List<BHAV>()) globalNames[b.ChunkID] = b.ChunkLabel;
            }
        }
        foreach (var kv in loaded)
        {
            var iff = kv.Value;
            var names = new Dictionary<int, string>(globalNames);
            foreach (var b in iff.List<BHAV>() ?? new List<BHAV>()) if (b.ChunkID >= 4096) names[b.ChunkID] = b.ChunkLabel;
            sb.AppendLine("##FILE " + kv.Key);
            foreach (var b in iff.List<BHAV>() ?? new List<BHAV>())
            {
                sb.AppendLine("#BHAV " + b.ChunkID + " \"" + b.ChunkLabel + "\" args=" + b.Args + " locals=" + b.Locals);
                for (int i = 0; i < b.Instructions.Length; i++)
                {
                    var ins = b.Instructions[i];
                    sb.AppendLine("  " + i + ": " + Decode(ins, names) + "   T->" + Ptr(ins.TruePointer) + " F->" + Ptr(ins.FalsePointer));
                }
            }
            foreach (var s in iff.List<STR>() ?? new List<STR>())
            {
                sb.AppendLine("#STR " + s.ChunkID + " \"" + s.ChunkLabel + "\"");
                for (int i = 0; i < s.Length; i++) { var t = s.GetString(i); if (!string.IsNullOrEmpty(t)) sb.AppendLine("  " + i + ": " + t); }
            }
            foreach (var t in iff.List<BCON>() ?? new List<BCON>())
            {
                sb.AppendLine("#BCON " + t.ChunkID + " \"" + t.ChunkLabel + "\" : " + string.Join(",", t.Constants.Select(c => c.ToString())));
            }
            foreach (var t in iff.List<TTAB>() ?? new List<TTAB>())
            {
                sb.AppendLine("#TTAB " + t.ChunkID);
                for (int i = 0; i < t.Interactions.Length; i++)
                { var x = t.Interactions[i]; sb.AppendLine("  " + i + ": action=" + x.ActionFunction + " test=" + x.TestFunction + " ttaIdx=" + x.TTAIndex); }
            }
            foreach (var t in iff.List<TTAs>() ?? new List<TTAs>())
            {
                sb.AppendLine("#TTAs " + t.ChunkID);
                for (int i = 0; i < t.Length; i++) { var s = t.GetString(i); if (!string.IsNullOrEmpty(s)) sb.AppendLine("  " + i + ": " + s); }
            }
        }
        File.WriteAllText(outp, sb.ToString());
        Console.WriteLine("done " + sb.Length);
    }
}
