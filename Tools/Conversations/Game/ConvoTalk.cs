using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;

namespace Simitone.Conversations
{
    // Talk subjects (P2). The Talk social has no outcome step; it picks what to talk about in the routine
    // "select topic I like." (the topic number goes into attribute 2 of the social object, 0..7 = the eight interests).
    // When a Talk routine is first entered, the first instruction of that routine is replaced by this primitive: for the
    // Sim the player controls it offers three subjects and stores the chosen one; otherwise it runs the original instruction.
    // The game then scores the topic by the listener's interest exactly as before, so a subject the listener likes helps.
    public class ConvoTalkOperand : VMPrimitiveOperand
    {
        public ushort OriginalOpcode;
        public VMPrimitiveOperand OriginalOperand;
        public void Read(byte[] bytes) { }
        public void Write(byte[] bytes) { }
    }

    public class ConvoTalkPrimitive : VMPrimitiveHandler
    {
        public const ushort Opcode = 202;
        // the topic routine has two names across the game files (the Hot Date version used by the running Talk is the second)
        static readonly string[] RoutineLabels = { "select topic i like.", "shd select topic i like" };

        class Pending
        {
            public RawSim Speaker, Listener;
            public TraitProfile ListenerProfile, SpeakerProfile;
            public List<TopicLogic.Subject> Subjects;
        }

        static readonly ConditionalWeakTable<VMThread, Pending> pending = new ConditionalWeakTable<VMThread, Pending>();
        static readonly HashSet<VMRoutine> inspected = new HashSet<VMRoutine>();
        static readonly Random rng = new Random();

        // called (from the patched VMThread.ExecuteSubRoutine) before any subroutine runs: finds the topic routine of a Talk script
        public static void OnSubRoutine(VMStackFrame frame, ushort opcode)
        {
            if (opcode < 4096) return;
            try
            {
                var owner = frame.CodeOwner;
                var routine = (owner == null ? null : (opcode >= 8192 ? owner.Resource.SemiGlobal?.GetRoutine(opcode) : owner.Resource.GetRoutine(opcode))) as VMRoutine;
                if (routine == null) return;
                lock (inspected) { if (!inspected.Add(routine)) return; }
                var label = routine.Chunk == null ? null : routine.Chunk.ChunkLabel;
                if (label == null || Array.IndexOf(RoutineLabels, label.Trim().ToLowerInvariant()) < 0) return;
                var first = routine.Instructions[0];
                if (first.Opcode == Opcode || first.Opcode >= 256 || VMContext.Primitives[first.Opcode] == null) return;
                first.Operand = new ConvoTalkOperand { OriginalOpcode = first.Opcode, OriginalOperand = first.Operand };
                first.Opcode = Opcode;
                Boot.Log("patched the Talk subject choice in " + (owner.Resource.MainIff == null ? "?" : owner.Resource.MainIff.Filename));
            }
            catch (Exception e) { Boot.Log("talk hook: " + e.GetType().Name + ": " + e.Message); }
        }

        public override VMPrimitiveExitCode Execute(VMStackFrame ctx, VMPrimitiveOperand operand)
        {
            var op = (ConvoTalkOperand)operand;
            try
            {
                var r = Run(ctx, op, out bool handled);
                if (handled) return r;
            }
            catch (Exception e)
            {
                Boot.Log("error in the Talk subject step (continuing normally): " + e.GetType().Name + ": " + e.Message);
                try { pending.Remove(ctx.Thread); } catch { }
            }
            return Original(ctx, op);
        }

        static VMPrimitiveExitCode Original(VMStackFrame ctx, ConvoTalkOperand op)
        {
            return VMContext.Primitives[op.OriginalOpcode].GetHandler().Execute(ctx, op.OriginalOperand);
        }

        static VMPrimitiveExitCode Run(VMStackFrame ctx, ConvoTalkOperand op, out bool handled)
        {
            handled = true;
            var thread = ctx.Thread;
            Pending p;
            if (!pending.TryGetValue(thread, out p))
            {
                handled = false;
                if (!Boot.Enabled) return VMPrimitiveExitCode.GOTO_TRUE;
                var speaker = ctx.Caller as VMAvatar;
                var social = ctx.StackObject;
                if (speaker == null || social == null) return VMPrimitiveExitCode.GOTO_TRUE;
                var listener = ctx.VM.GetObjectById(social.GetAttribute(0)) as VMAvatar;
                if (listener == null || speaker.IsPet || listener.IsPet) return VMPrimitiveExitCode.GOTO_TRUE;
                if (!ConvoPrimitive.PlayerControls(speaker) && Boot.AutoTone < 0) return VMPrimitiveExitCode.GOTO_TRUE;

                var rs = ConvoPrimitive.Read(speaker); var rl = ConvoPrimitive.Read(listener);
                var ps = TraitProfile.From(rs); var pl = TraitProfile.From(rl);
                if (ps.IsChild || pl.IsChild) return VMPrimitiveExitCode.GOTO_TRUE;
                Boot.Laya.Prefetch(pl);
                p = new Pending { Speaker = rs, Listener = rl, SpeakerProfile = ps, ListenerProfile = pl, Subjects = TopicLogic.Offer(rs.Interests, rl.Interests, rng) };

                if (Boot.AutoTone >= 0) { Finish(ctx, p, p.Subjects[Math.Min(p.Subjects.Count - 1, Boot.AutoTone)]); handled = true; return VMPrimitiveExitCode.RETURN_TRUE; }
                ConvoPrimitive.ShowChoice(ctx, "Talk", "What do you want to talk to " + pl.Name + " about?", p.Subjects.Select(s => s.Title).ToArray());
                pending.Add(thread, p);
                handled = true;
                return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
            }

            var dr = thread.BlockingState as VMDialogResult;
            if (dr == null) { pending.Remove(thread); handled = false; return VMPrimitiveExitCode.GOTO_TRUE; }
            if (!dr.Responded && dr.WaitTime <= dr.Timeout) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
            thread.BlockingState = null;
            pending.Remove(thread);
            var chosen = p.Subjects[dr.Responded ? Math.Min(p.Subjects.Count - 1, (int)dr.ResponseCode) : 0];
            Finish(ctx, p, chosen);
            return VMPrimitiveExitCode.RETURN_TRUE;   // the routine's job (choosing a topic) is done
        }

        static void Finish(VMStackFrame ctx, Pending p, TopicLogic.Subject s)
        {
            var social = ctx.StackObject;
            social.SetAttribute(2, (short)s.Interest);   // the topic, as the game itself stores it

            var temper = Boot.Laya.CachedTemperament(p.ListenerProfile);
            int liking = TopicLogic.Liking(p.Listener.Interests[s.Interest]);
            int quality = liking == 2 ? 3 : (liking == 1 ? 2 : 1);
            var tone = p.Speaker.Playful > 650 ? Tone.Playful : (p.Speaker.Outgoing > 650 && p.Speaker.Nice < 400 ? Tone.Bold : Tone.Gentle);
            var line = DialogueBank.Fill(Boot.Bank.SpeakerLine("talk", tone, rng), p.SpeakerProfile.Name, p.ListenerProfile.Name, p.ListenerProfile.JobTitle, s.Title);
            var reply = DialogueBank.Fill(Boot.Bank.ReplyLine("talk", quality, temper, rng), p.SpeakerProfile.Name, p.ListenerProfile.Name, p.ListenerProfile.JobTitle, s.Title);
            Boot.LastDecision = "talk about " + s.Title + " listener-interest=" + p.Listener.Interests[s.Interest] + " liking=" + liking + " temperament=" + temper +
                                " (" + p.SpeakerProfile.Name + " -> " + p.ListenerProfile.Name + ")";
            Boot.Log("decision: " + Boot.LastDecision + " | \"" + line + "\" / \"" + reply + "\"");
            ConvoOverlay.Show(ctx.VM, ctx.Caller, p.SpeakerProfile.Name, line, ctx.VM.GetObjectById(social.GetAttribute(0)), p.ListenerProfile.Name, reply);
        }
    }
}
