using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Engine.Scopes;
using FSO.SimAntics.Engine.Primitives;
using FSO.SimAntics.Entities;
using FSO.SimAntics.Model;
using FSO.SimAntics.Primitives;

namespace Simitone.Conversations
{
    // The conversation step. The game's 18 social scripts (SocialInteractions.iff "<Name> - Person A") all call the
    // global script 397 "enable social outcome choice?" right where they use the outcome. Install() replaces that
    // script's only instruction with this primitive, which always returns FALSE (so the script continues), but first:
    //   1. asks the player which of three lines to say (the game's own dialog machinery, so the game pauses properly)
    //   2. moves the outcome by at most one step according to the receiver's temperament and the chosen tone
    //   3. shows the two lines
    // With the feature off, or for anything it does not understand, it does nothing and the original behaviour remains.
    public class ConvoOperand : VMPrimitiveOperand
    {
        public void Read(byte[] bytes) { }
        public void Write(byte[] bytes) { }
    }

    public class ConvoPrimitive : VMPrimitiveHandler
    {
        public const ushort Opcode = 200;
        const ushort RoutineId = 397;
        public static int InstalledCount;

        static readonly Random rng = new Random();

        class Pending
        {
            public string Key;
            public TraitProfile Speaker, Listener;
            public RawSim RawSpeaker, RawListener;
            public string[] SpeakerLines;     // one per tone
            public Tone Tone;
            public bool Auto;        // an autonomous Sim: waiting for Laya, not for a dialog answer
            public int Waits;
        }

        // an autonomous Sim waits at most this many game ticks for Laya's answer about the receiver (about 3 s of game time)
        public const int MaxAutoWaitTicks = 90;

        static readonly ConditionalWeakTable<VMThread, Pending> pending = new ConditionalWeakTable<VMThread, Pending>();

        public static void Install(VMContext ctx)
        {
            if (ctx == null || ctx.Globals == null) { Boot.Log("install: context not ready"); return; }
            VMContext.AddPrimitive(new VMPrimitiveRegistration(new ConvoPrimitive()) { Opcode = Opcode, Name = "social_conversation", OperandModel = typeof(ConvoOperand) });
            VMContext.AddPrimitive(new VMPrimitiveRegistration(new ConvoApplyPrimitive()) { Opcode = ConvoApplyPrimitive.Opcode, Name = "social_conversation_apply", OperandModel = typeof(ConvoApplyOperand) });
            VMContext.AddPrimitive(new VMPrimitiveRegistration(new ConvoTalkPrimitive()) { Opcode = ConvoTalkPrimitive.Opcode, Name = "social_talk_subject", OperandModel = typeof(ConvoTalkOperand) });
            var routine = ctx.Globals.Resource.GetRoutine(RoutineId) as VMRoutine;
            if (routine == null || routine.Instructions == null || routine.Instructions.Length == 0) { Boot.Log("install: routine " + RoutineId + " not found"); return; }
            var ins = routine.Instructions[0];
            if (ins.Opcode == Opcode) return;   // already installed in this lot
            if (routine.Chunk == null || !(routine.Chunk.ChunkLabel ?? "").Contains("social outcome")) { Boot.Log("install: script 397 is not the expected one (" + routine.Chunk?.ChunkLabel + ")"); return; }
            ins.Opcode = Opcode;
            ins.Operand = new ConvoOperand();
            ins.TruePointer = 255;    // 255 = return FALSE: the caller continues with the outcome in the social object
            ins.FalsePointer = 255;
            InstalledCount++;
            Boot.Log("install: conversation step active in script " + RoutineId);
        }

        public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand operand)
        {
            try { return Run(context); }
            catch (Exception e)
            {
                Boot.Log("error in conversation step (continuing normally): " + e.GetType().Name + ": " + e.Message);
                try { pending.Remove(context.Thread); } catch { }
                return VMPrimitiveExitCode.GOTO_FALSE;
            }
        }

        static string ScriptLabel(VMThread thread)
        {
            // the calling script is the nearest frame below us whose name ends in "- Person A"
            for (int i = thread.Stack.Count - 1; i >= 0; i--)
            {
                var label = thread.Stack[i].Routine?.Chunk?.ChunkLabel;
                if (label != null && label.IndexOf(" - Person", StringComparison.OrdinalIgnoreCase) > 0) return label;
            }
            return null;
        }

        // The dialog is for the Sim the player is controlling. (Queue priorities cannot tell: the social script runs as a pushed
        // interaction with a low priority even when the player asked for it.)
        // And only when the player asked for this: the Sim being played also chats on its own (free will), and then no window should appear.
        // The game builds a Sim's pie menu only when the player points at it (UILotControl) and never for a Sim's own free will,
        // so a pie built for this pair a moment ago means the player is behind the conversation.
        const long AskedWindowMs = 60000;
        static readonly System.Diagnostics.Stopwatch askedClock = System.Diagnostics.Stopwatch.StartNew();
        static readonly Dictionary<int, long> asked = new Dictionary<int, long>();

        static int PairKey(VMEntity a, VMEntity b) { return (a.ObjectID << 16) ^ (ushort)b.ObjectID; }

        public static void MarkAsked(VMEntity caller, VMEntity target)
        {
            if (caller == null || target == null || ReferenceEquals(caller, target)) return;
            lock (asked) asked[PairKey(caller, target)] = askedClock.ElapsedMilliseconds;
        }

        // test aid: forget that the player pointed at this pair (the test hook builds menus too, which would look like a player click)
        public static void ClearAsked(VMEntity caller, VMEntity target) { lock (asked) asked.Remove(PairKey(caller, target)); }

        static bool WasAsked(VMEntity speaker, VMEntity listener)
        {
            if (speaker == null || listener == null) return false;
            long at;
            lock (asked) return asked.TryGetValue(PairKey(speaker, listener), out at) && askedClock.ElapsedMilliseconds - at <= AskedWindowMs;
        }

        public static bool PlayerControls(VMEntity speaker, VMEntity listener)
        {
            if (Boot.TreatAsAutonomous) return false;
            try
            {
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                var lot = screen?.GetType().GetProperty("LotControl")?.GetValue(screen, null);
                var active = lot?.GetType().GetProperty("ActiveEntity")?.GetValue(lot, null);
                return active != null && ReferenceEquals(active, speaker) && WasAsked(speaker, listener);
            }
            catch { return false; }
        }

        static VMPrimitiveExitCode Run(VMStackFrame ctx)
        {
            Boot.Log("step reached: label=" + ScriptLabel(ctx.Thread) + " callerIP=" + (ctx.Thread.Stack.Count > 1 ? ctx.Thread.Stack[ctx.Thread.Stack.Count - 2].InstructionPointer.ToString() : "-") + " socialAttrs=[" + string.Join(",", Enumerable.Range(0, 10).Select(i => ctx.StackObject == null ? 0 : (int)ctx.StackObject.GetAttribute(i))) + "]" + " caller=" + ctx.Caller?.ToString() + " stack=" + ctx.StackObject?.ToString() + " attr9=" + ctx.StackObject?.GetAttribute(9) + " priority=" + (ctx.Thread.Queue != null && ctx.Thread.Queue.Count > 0 ? ctx.Thread.Queue[0].Priority.ToString() : "-"));
            var thread = ctx.Thread;
            var vm = ctx.VM;
            Pending p;

            if (!pending.TryGetValue(thread, out p))
            {
                if (!Boot.Enabled) return VMPrimitiveExitCode.GOTO_FALSE;
                var speaker = ctx.Caller as VMAvatar;
                var social = ctx.StackObject;
                if (speaker == null || social == null) return VMPrimitiveExitCode.GOTO_FALSE;
                var listener = vm.GetObjectById(social.GetAttribute(0)) as VMAvatar;
                if (listener == null || speaker.IsPet || listener.IsPet) return VMPrimitiveExitCode.GOTO_FALSE;

                var key = OutcomeResolver.KeyFromScriptName(ScriptLabel(thread));
                if (key == null) return VMPrimitiveExitCode.GOTO_FALSE;

                var rs = Read(speaker, listener, vm); var rl = Read(listener, speaker, vm);
                var ps = TraitProfile.From(rs); var pl = TraitProfile.From(rl);
                if (ps.IsChild || pl.IsChild) return VMPrimitiveExitCode.GOTO_FALSE;   // children keep the original behaviour
                Boot.Laya.Prefetch(pl);   // by the time the player has chosen, the answer is usually ready

                p = new Pending
                {
                    Key = key, Speaker = ps, Listener = pl, RawSpeaker = rs, RawListener = rl,
                    SpeakerLines = new[]
                    {
                        Boot.Bank.SpeakerLine(key, Tone.Gentle, rng), Boot.Bank.SpeakerLine(key, Tone.Playful, rng), Boot.Bank.SpeakerLine(key, Tone.Bold, rng),
                    },
                };
                for (int i = 0; i < 3; i++) p.SpeakerLines[i] = DialogueBank.Fill(p.SpeakerLines[i], ps.Name, pl.Name, pl.JobTitle);

                Boot.Log("social step: " + key + " " + ps.Name + " -> " + pl.Name + " window=" + PlayerControls(speaker, listener) + " priority=" + (thread.Queue != null && thread.Queue.Count > 0 ? thread.Queue[0].Priority.ToString() : "-"));
                if (Boot.AutoTone >= 0) p.Tone = (Tone)Math.Min(2, Boot.AutoTone);
                else if (PlayerControls(speaker, listener))
                {
                    ShowDialog(ctx, p, key);
                    pending.Add(thread, p);
                    return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                }
                else
                {
                    p.Tone = AutoTone2(rs);
                    // nobody is choosing, so there is no time to classify the receiver while waiting: give Laya a moment
                    if (Boot.Laya.Available && !Boot.Laya.HasTemperament(pl))
                    {
                        p.Auto = true;
                        pending.Add(thread, p);
                        return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                    }
                }
            }
            else if (p.Auto)
            {
                if (Boot.Laya.Available && !Boot.Laya.HasTemperament(p.Listener) && ++p.Waits < MaxAutoWaitTicks) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                pending.Remove(thread);
            }
            else
            {
                var dr = thread.BlockingState as VMDialogResult;
                if (dr == null) { pending.Remove(thread); return VMPrimitiveExitCode.GOTO_FALSE; }   // something else ended the wait
                if (!dr.Responded && dr.WaitTime <= dr.Timeout) return VMPrimitiveExitCode.CONTINUE_NEXT_TICK;
                thread.BlockingState = null;
                p.Tone = dr.Responded && dr.ResponseCode != TalkSheetMenu.NeverMind ? (Tone)Math.Min(2, (int)dr.ResponseCode) : Tone.Gentle;
                pending.Remove(thread);
            }

            Decide(ctx, p);
            return VMPrimitiveExitCode.GOTO_FALSE;
        }

        // autonomous Sims pick a tone from their own personality
        static Tone AutoTone2(RawSim s)
        {
            if (s.Playful > 650) return Tone.Playful;
            if (s.Outgoing > 650 && s.Nice < 400) return Tone.Bold;
            return Tone.Gentle;
        }

        // ---- deciding. Two shapes of social script exist in the game:
        //  (a) the outcome is already in the social object when script 397 is called (attribute 9 > 0): decide right there;
        //  (b) 397 is called first and the script computes the outcome afterwards in "Function: <X> - Decision", then runs
        //      "StackObjAttr[9] = Temps[0]". For (b) the instruction after the decision is swapped for ConvoApplyPrimitive,
        //      which adjusts Temps[0] (the script's outcome) before that assignment runs.
        static readonly ConditionalWeakTable<VMThread, Pending> armed = new ConditionalWeakTable<VMThread, Pending>();

        static void Decide(VMStackFrame ctx, Pending p)
        {
            int script = ctx.StackObject.GetAttribute(9);
            if (script >= 1)
            {
                int outcome = Apply(ctx, p, script);
                if (outcome != script) ctx.StackObject.SetAttribute(9, (short)outcome);
                return;
            }
            // shape (b): remember the choice and hook the instruction that stores the outcome
            try { armed.Remove(ctx.Thread); } catch { }
            armed.Add(ctx.Thread, p);
            if (!ArmApplyStep(ctx)) { Boot.Log("could not find the outcome store after script 397; outcome left to the game (" + p.Key + ")"); armed.Remove(ctx.Thread); }
        }

        // the outcome the game computed -> the outcome we want; also records the decision and shows the lines
        static int Apply(VMStackFrame ctx, Pending p, int script)
        {
            var temper = Boot.Laya.CachedTemperament(p.Listener);
            int outcome = OutcomeResolver.Resolve(script, p.Key, p.Tone, temper);
            if (Boot.ForceOutcome >= 1 && Boot.ForceOutcome <= 3) outcome = Math.Min(Boot.ForceOutcome, OutcomeResolver.MaxOutcome(p.Key));

            int quality = OutcomeResolver.Quality(outcome < 1 ? 2 : outcome, p.Key);
            var reply = DialogueBank.Fill(Boot.Bank.ReplyLine(p.Key, quality, temper, rng), p.Speaker.Name, p.Listener.Name, p.Listener.JobTitle);
            var line = p.SpeakerLines[(int)p.Tone];

            var topLsSkills = p.RawListener != null ? p.RawListener.TopSkills(300) : new SkillKind[0];
            var topSpSkills = p.RawSpeaker != null ? p.RawSpeaker.TopSkills(300) : new SkillKind[0];
            Boot.Memory.Record(p.RawSpeaker, p.RawListener, p.Key, p.Tone, null, quality, -1, topLsSkills.Length > 0 ? topLsSkills[0] : SkillKind.None, null, temper);
            Boot.Memory.RecordReciprocal(p.RawListener, p.RawSpeaker, p.Key, quality, -1, topSpSkills.Length > 0 ? topSpSkills[0] : SkillKind.None);
            Boot.SaveMemoryAsync();

            Boot.LastDecision = p.Key + " " + Names.Label(p.Tone) + " script=" + script + " final=" + outcome + " temperament=" + temper +
                                " (" + p.Speaker.Name + " -> " + p.Listener.Name + ") laya=" + (Boot.Laya.Available ? "up" : "down") + " failures=" + Boot.Laya.Failures + " err=" + Boot.Laya.LastError + " cached=" + Boot.Laya.HasTemperament(p.Listener) + " [" + p.Listener.Summary + "]";
            Boot.Log("decision: " + Boot.LastDecision + " | \"" + line + "\" / \"" + reply + "\"");
            ConvoOverlay.Show(ctx.VM, ctx.Caller, p.Speaker.Name, line, ctx.VM.GetObjectById(ctx.StackObject.GetAttribute(0)), p.Listener.Name, reply);
            try { GossipDirector.OnInteraction(ctx.VM, ctx.Caller as VMAvatar, ctx.VM.GetObjectById(ctx.StackObject.GetAttribute(0)) as VMAvatar, p.Key, quality); } catch (Exception e) { Boot.Log("gossip interaction error: " + e.Message); }
            return outcome;
        }

        // From the CALL to script 397 follow the false/true pointers a few steps to "StackObjAttr[9] = Temps[0]" and swap it.
        static bool ArmApplyStep(VMStackFrame ctx)
        {
            var thread = ctx.Thread;
            if (thread.Stack.Count < 2) return false;
            var callerFrame = thread.Stack[thread.Stack.Count - 2];
            var routine = callerFrame.Routine;
            var call = callerFrame.GetCurrentInstruction();
            int idx = call.FalsePointer;
            for (int hop = 0; hop < 6 && idx >= 0 && idx < routine.Instructions.Length; hop++)
            {
                var ins = routine.Instructions[idx];
                if (ins.Opcode == ConvoApplyPrimitive.Opcode) return true;   // patched earlier
                var ex = ins.Operand as VMExpressionOperand;
                if (ins.Opcode == 2 && ex != null && ex.LhsOwner == VMVariableScope.StackObjectAttributes && ex.LhsData == 9 &&
                    ex.Operator == VMExpressionOperator.Assign && ex.RhsOwner == VMVariableScope.Temps && ex.RhsData == 0)
                {
                    ins.Operand = new ConvoApplyOperand { Original = ins.Operand };
                    ins.Opcode = ConvoApplyPrimitive.Opcode;
                    Boot.Log("patched the outcome store in '" + routine.Chunk?.ChunkLabel + "' at instruction " + idx);
                    return true;
                }
                idx = ins.TruePointer;   // a call returns to the same place whichever way it goes
            }
            return false;
        }

        // the second step: runs in place of "StackObjAttr[9] = Temps[0]"
        public static VMPrimitiveExitCode ApplyStep(VMStackFrame ctx, ConvoApplyOperand op)
        {
            Pending p;
            if (armed.TryGetValue(ctx.Thread, out p))
            {
                armed.Remove(ctx.Thread);
                try { ctx.Thread.TempRegisters[0] = (short)Apply(ctx, p, ctx.Thread.TempRegisters[0]); }
                catch (Exception e) { Boot.Log("error applying the outcome (continuing normally): " + e.Message); }
            }
            // then do exactly what the original instruction did
            return VMContext.Primitives[2].GetHandler().Execute(ctx, op.Original);
        }

        // ---- the three-way dialog, using the engine's own blocking-dialog path (so the clock stops and a click answers it) ----
        // The Talk sheet in place of the engine dialog; the simulation stays blocked on the thread's VMDialogResult as ShowChoice does.
        // False when the sheet cannot be shown: the caller then uses ShowChoice.
        public static bool ShowSheet(VMStackFrame ctx, TalkSheetRequest r)
        {
            var vm = ctx.VM;
            r.Vm = vm; r.Speaker = ctx.Caller;
            if (!TalkSheetMenu.Open(r)) return false;
            ctx.Thread.BlockingState = new VMDialogResult { Type = VMDialogType.YesNoCancel, HasDisplayed = true };
            if (vm.TS1)
            {
                vm.GlobalBlockingDialog = ctx.Caller;
                vm.LastSpeedMultiplier = vm.SpeedMultiplier;
                vm.SpeedMultiplier = -2;
            }
            return true;
        }

        static void ShowDialog(VMStackFrame ctx, Pending p, string key)
        {
            var sheet = new TalkSheetRequest { SpeakerName = p.Speaker.Name, ListenerName = p.Listener.Name, Prompt = Pretty(key), Lines = true };
            for (int i = 0; i < 3; i++) sheet.Rows.Add(new TalkRow { Title = p.SpeakerLines[i], Why = Names.ToneLabels[i] });
            if (ShowSheet(ctx, sheet)) return;
            ShowChoice(ctx, Pretty(key),
                "What do you say to " + p.Listener.Name + "?\n\n" +
                Names.Label(Tone.Gentle) + ": \"" + p.SpeakerLines[0] + "\"\n" +
                Names.Label(Tone.Playful) + ": \"" + p.SpeakerLines[1] + "\"\n" +
                Names.Label(Tone.Bold) + ": \"" + p.SpeakerLines[2] + "\"",
                new[] { Names.Label(Tone.Gentle), Names.Label(Tone.Playful), Names.Label(Tone.Bold) });
        }

        // The engine's own blocking-dialog path with up to three buttons: the clock stops, a click answers it, and the answer
        // (0, 1, 2 = first, second, third button) arrives in the thread's VMDialogResult.
        public static void ShowChoice(VMStackFrame ctx, string title, string message, string[] buttons)
        {
            var vm = ctx.VM;
            var info = new VMDialogInfo
            {
                Block = true,
                Caller = ctx.Caller,
                Icon = vm.GetObjectById(ctx.StackObject.GetAttribute(0)),
                Operand = new VMDialogOperand { Type = VMDialogType.YesNoCancel },
                Title = title,
                Message = message,
                IconName = "",
                Yes = buttons[0], No = buttons.Length > 1 ? buttons[1] : buttons[0], Cancel = buttons.Length > 2 ? buttons[2] : buttons[buttons.Length - 1],
                DialogID = 0xC0DE000000000000UL | (uint)ctx.Caller.ObjectID,
            };
            vm.SignalDialog(info);
            ctx.Thread.BlockingState = new VMDialogResult { Type = VMDialogType.YesNoCancel, HasDisplayed = true };
            if (vm.TS1)
            {
                vm.GlobalBlockingDialog = ctx.Caller;
                vm.LastSpeedMultiplier = vm.SpeedMultiplier;
                vm.SpeedMultiplier = -2;
            }
        }

        static string Pretty(string key)
        {
            switch (key)
            {
                case "backrub": return "Back Rub";
                case "cheerup": return "Cheer Up";
                case "dancewith": return "Dance";
                case "givegift": return "Give Gift";
                default: return char.ToUpperInvariant(key[0]) + key.Substring(1);
            }
        }

        // ---- reading a Sim from the game ----
        public static RawSim Read(VMAvatar a) { return Read(a, null, null); }

        public static RawSim Read(VMAvatar a, VMAvatar target, VM vm)
        {
            var s = new RawSim { Name = a.Name };
            s.NeighborId = a.GetPersonData(VMPersonDataVariable.NeighborId);
            s.Nice = a.GetPersonData(VMPersonDataVariable.NicePersonality);
            s.Active = a.GetPersonData(VMPersonDataVariable.ActivePersonality);
            s.Generous = a.GetPersonData(VMPersonDataVariable.GenerousPersonality);
            s.Playful = a.GetPersonData(VMPersonDataVariable.PlayfulPersonality);
            s.Outgoing = a.GetPersonData(VMPersonDataVariable.OutgoingPersonality);
            s.Neat = a.GetPersonData(VMPersonDataVariable.NeatPersonality);
            s.Cooking = a.GetPersonData(VMPersonDataVariable.CookingSkill);
            s.Charisma = a.GetPersonData(VMPersonDataVariable.CharismaSkill);
            s.Mechanical = a.GetPersonData(VMPersonDataVariable.MechanicalSkill);
            s.Creativity = a.GetPersonData(VMPersonDataVariable.CreativitySkill);
            s.Body = a.GetPersonData(VMPersonDataVariable.BodySkill);
            s.Logic = a.GetPersonData(VMPersonDataVariable.LogicSkill);
            s.Age = a.GetPersonData(VMPersonDataVariable.PersonsAge);
            s.Female = a.GetPersonData(VMPersonDataVariable.Gender) != 0;
            s.Mood = a.GetMotiveData(VMMotive.Mood);
            s.Social = a.GetMotiveData(VMMotive.Social);
            if (target != null && vm != null)
            {
                ParallelSocialDriver.ReadRelationship(vm, a, target, out s.StrToTarget, out s.LtrToTarget);
                ParallelSocialDriver.ReadRelationship(vm, target, a, out s.TargetStrToMe, out s.TargetLtrToMe);
            }
            for (int i = 0; i < 8; i++) s.Interests[i] = a.GetPersonData((VMPersonDataVariable)((int)VMPersonDataVariable.Interest_TravelToys + i));
            s.Interests = TopicLogic.Normalize(s.Interests);
            try
            {
                var jobType = a.GetPersonData(VMPersonDataVariable.JobType);
                var level = a.GetPersonData(VMPersonDataVariable.JobPromotionLevel);
                if (jobType > 0)
                {
                    var job = FSO.Content.Content.Get().Jobs.GetJob((ushort)jobType);
                    if (job != null)
                    {
                        s.Career = job.Name ?? "";
                        if (job.JobLevels != null && level >= 0 && level < job.JobLevels.Length) s.JobTitle = job.JobLevels[level].JobName ?? "";
                    }
                }
            }
            catch { /* unknown job: the Sim is treated as unemployed */ }
            return s;
        }
    }
}

namespace Simitone.Conversations
{
    // replaces "StackObjAttr[9] = Temps[0]" after the game's own outcome decision (see ConvoPrimitive.Decide)
    public class ConvoApplyOperand : VMPrimitiveOperand
    {
        public VMPrimitiveOperand Original;
        public void Read(byte[] bytes) { }
        public void Write(byte[] bytes) { }
    }

    public class ConvoApplyPrimitive : VMPrimitiveHandler
    {
        public const ushort Opcode = 201;

        public override VMPrimitiveExitCode Execute(VMStackFrame context, VMPrimitiveOperand operand)
        {
            return ConvoPrimitive.ApplyStep(context, (ConvoApplyOperand)operand);
        }
    }
}
