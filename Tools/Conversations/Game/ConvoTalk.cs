using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Primitives;

namespace Simitone.Conversations
{
    // Talk subject choice (APS-12): ALL talk subjects come from the AI Subject Bank (never hardcoded Sims 1 interest names).
    // When a Talk routine is first entered, its first instruction is replaced by this primitive:
    //   - fetches 3 AI-generated subjects ranked by Laya off the game thread (bounded short wait so frame stays smooth)
    //   - for a player-controlled Sim, shows a 3-button dialog with those AI subjects
    //   - for an autonomous Sim, picks the top Laya/intelligence-ranked AI subject directly
    //   - writes the chosen subject's underlying Sims 1 interest slot (0..7) to social attr 2 so the game scores it natively,
    //     records the outcome into SocialMemory, and displays the AI lines in speech balloons.
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
        public const int MaxLayaWaitTicks = 45;
        static readonly string[] RoutineLabels = { "select topic i like.", "shd select topic i like" };

        class Pending
        {
            public RawSim Speaker, Listener;
            public TraitProfile ListenerProfile, SpeakerProfile;
            public volatile List<AiSubject> Subjects;
            public volatile bool SubjectsReady;
            public bool DialogShown;
            public bool PlayerControlled;
            public int Waits;
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

                var rs = ConvoPrimitive.Read(speaker, listener, ctx.VM);
                var rl = ConvoPrimitive.Read(listener, speaker, ctx.VM);
                var ps = TraitProfile.From(rs);
                var pl = TraitProfile.From(rl);
                if (ps.IsChild || pl.IsChild) return VMPrimitiveExitCode.GOTO_TRUE;

                handled = true;
                Boot.Laya.Prefetch(pl);
                p = new Pending
                {
                    Speaker = rs,
                    Listener = rl,
                    SpeakerProfile = ps,
                    ListenerProfile = pl,
                    PlayerControlled = ConvoPrimitive.PlayerControls(speaker, listener),
                };

                Boot.Log("talk step: " + ps.Name + " -> " + pl.Name + " window=" + p.PlayerControlled + " priority=" + (thread.Queue != null && thread.Queue.Count > 0 ? thread.Queue[0].Priority.ToString() : "-"));
                if (Boot.Laya.Available)
                {
                    // Rank AI subjects via Laya on a worker thread (never blocks a frame)
                    var pRef = p;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { pRef.Subjects = Boot.Subjects.OfferForManualTalk(pRef.Speaker, pRef.Listener, Boot.Laya, Boot.Memory, new Random(Guid.NewGuid().GetHashCode())); }
                        catch { pRef.Subjects = Boot.Subjects.OfferForManualTalk(pRef.Speaker, pRef.Listener, null, Boot.Memory, new Random(Guid.NewGuid().GetHashCode())); }
                        finally { pRef.SubjectsReady = true; }
                    });
                    pending.Add(thread, p);
                    return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                }

                p.Subjects = Boot.Subjects.OfferForManualTalk(rs, rl, null, Boot.Memory, rng);
                p.SubjectsReady = true;
                pending.Add(thread, p);
            }

            // Wait briefly for worker-thread Laya subject ranking to finish (bounded so it never stalls)
            if (!p.SubjectsReady)
            {
                if (++p.Waits < MaxLayaWaitTicks) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                p.Subjects = Boot.Subjects.OfferForManualTalk(p.Speaker, p.Listener, null, Boot.Memory, rng);
                p.SubjectsReady = true;
            }

            var subjects = p.Subjects;
            if (subjects == null || subjects.Count == 0)
            {
                subjects = Boot.Subjects.OfferForManualTalk(p.Speaker, p.Listener, null, Boot.Memory, rng);
                p.Subjects = subjects;
            }

            if (!p.DialogShown)
            {
                if (!p.PlayerControlled && Boot.AutoTone < 0)
                {
                    pending.Remove(thread);
                    Finish(ctx, p, BestForListener(p, subjects));
                    return VMPrimitiveExitCode.RETURN_TRUE;
                }
                if (Boot.AutoTone >= 0)
                {
                    pending.Remove(thread);
                    Finish(ctx, p, subjects[Math.Min(subjects.Count - 1, Boot.AutoTone)]);
                    return VMPrimitiveExitCode.RETURN_TRUE;
                }
                ConvoPrimitive.ShowChoice(ctx, "Talk", "Choose an AI conversation subject with " + p.ListenerProfile.Name + ":", subjects.Select(s => s.Title).ToArray());
                p.DialogShown = true;
                return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
            }

            var dr = thread.BlockingState as VMDialogResult;
            if (dr == null) { pending.Remove(thread); handled = false; return VMPrimitiveExitCode.GOTO_TRUE; }
            if (!dr.Responded && dr.WaitTime <= dr.Timeout) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
            thread.BlockingState = null;
            pending.Remove(thread);
            var chosen = subjects[dr.Responded ? Math.Min(subjects.Count - 1, (int)dr.ResponseCode) : 0];
            Finish(ctx, p, chosen);
            return VMPrimitiveExitCode.RETURN_TRUE;   // the routine's job (choosing a topic) is done
        }

        // a Sim chatting on its own brings up the offered subject the listener is likeliest to enjoy
        static AiSubject BestForListener(Pending p, List<AiSubject> subjects)
        {
            double know = Math.Max(0.4, Math.Min(1.0, (p.Speaker.LtrToTarget + 20) / 120.0));
            return subjects.OrderByDescending(s => AiSubjectBank.ListenerTaste(p.Listener, s, know) + (p.Speaker.Interests[s.Interest] / 1000.0) * 0.2 + rng.NextDouble() * 0.3).First();   // the jitter keeps the same topic from coming up every time
        }

        static void Finish(VMStackFrame ctx, Pending p, AiSubject s)
        {
            var social = ctx.StackObject;
            social.SetAttribute(2, (short)s.Interest);   // maps the AI subject to the game's interest slot (0..7)

            var temper = Boot.Laya.CachedTemperament(p.ListenerProfile);
            int liking = TopicLogic.Liking(p.Listener.Interests[s.Interest]);
            int quality = liking == 2 ? 3 : (liking == 1 ? 2 : 1);
            if (s.Skill != SkillKind.None && p.Listener.SkillValue(s.Skill) >= 400 && p.Speaker.SkillValue(s.Skill) >= 300)
                quality = Math.Min(3, quality + 1);
            var tone = p.Speaker.Playful > 650 ? Tone.Playful : (p.Speaker.Outgoing > 650 && p.Speaker.Nice < 400 ? Tone.Bold : s.ToneHint);
            var line = DialogueBank.Fill(Boot.Bank.SpeakerLine("talk", tone, rng), p.SpeakerProfile.Name, p.ListenerProfile.Name, p.ListenerProfile.JobTitle, s.Title);
            var reply = DialogueBank.Fill(Boot.Bank.ReplyLine("talk", quality, temper, rng), p.SpeakerProfile.Name, p.ListenerProfile.Name, p.ListenerProfile.JobTitle, s.Title);

            Boot.Memory.Record(p.Speaker, p.Listener, "talk", tone, s.Id, quality, s.Interest, s.Skill, s.Title, temper);
            Boot.Memory.RecordReciprocal(p.Listener, p.Speaker, "talk", quality, s.Interest, s.Skill, s.Id, s.Title);
            Boot.SaveMemoryAsync();
            Boot.LastDecision = "talk about " + s.Title + " listener-interest=" + p.Listener.Interests[s.Interest] + " liking=" + liking + " quality=" + quality + " temperament=" + temper +
                                " (" + p.SpeakerProfile.Name + " -> " + p.ListenerProfile.Name + ")";
            Boot.Log("decision: " + Boot.LastDecision + " | \"" + line + "\" / \"" + reply + "\"");
            ConvoOverlay.Show(ctx.VM, ctx.Caller, p.SpeakerProfile.Name, line, ctx.VM.GetObjectById(social.GetAttribute(0)), p.ListenerProfile.Name, reply);
        }
    }
}
