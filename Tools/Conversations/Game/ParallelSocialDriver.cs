using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;

namespace Simitone.Conversations
{
    // Autonomous parallel room-conversation driver.
    // Hooked at VM.InternalTick via Boot.OnTick(VM). Scans Sims in the same room without touching VMThread.Queue,
    // triggers when Social <= 60% (<= 20 on -100..+100), runs Laya interaction & AI topic planning off the game thread,
    // and drains completed plans on the VM tick to show speech balloons and update Social, STR/LTR, and SocialMemory.
    public static class ParallelSocialDriver
    {
        const int ScanIntervalTicks = 45;         // ~1.5 s of game time between room scans
        const int SimCooldownTicks = 300;         // ~10 s of game time per Sim between parallel exchanges
        const int PairCooldownTicks = 360;        // ~12 s of game time per pair

        class CompletedExchange
        {
            public VM Vm;
            public short SpeakerId, ListenerId;
            public RawSim SpeakerRaw, ListenerRaw;
            public ParallelSocialPlan Plan;
        }

        static readonly object gate = new object();
        static readonly Queue<CompletedExchange> completed = new Queue<CompletedExchange>();
        static readonly HashSet<short> inFlight = new HashSet<short>();
        static readonly Dictionary<short, long> simNextTick = new Dictionary<short, long>();
        static readonly Dictionary<string, long> pairNextTick = new Dictionary<string, long>();
        static readonly Random rng = new Random();
        static VM lastVm;
        static long tickCount;
        static int totalExchanges;
        static int savesPending;

        public static string LastParallelDecision = "";
        public static int TotalExchanges { get { return totalExchanges; } }

        public static void OnTick(VM vm)
        {
            if (vm == null || !vm.Ready) return;
            if (!ReferenceEquals(vm, lastVm))
            {
                ResetForLot(vm);
            }

            if (!Boot.Enabled) return;
            if (vm.SpeedMultiplier <= 0 || vm.GlobalBlockingDialog != null) return;

            tickCount++;
            DrainCompleted(vm);

            if (tickCount % ScanIntervalTicks != 0) return;
            ScanAndDispatch(vm);
        }

        static void ResetForLot(VM vm)
        {
            lock (gate)
            {
                lastVm = vm;
                tickCount = 0;
                completed.Clear();
                inFlight.Clear();
                simNextTick.Clear();
                pairNextTick.Clear();
            }
        }

        static bool IsOutsideRoom(VM vm, ushort roomId)
        {
            try
            {
                var arch = vm.Context?.Architecture;
                var data = arch?.RoomData;
                if (roomId == 0 || data == null || roomId >= data.Count) return true;
                return data[roomId].IsOutside;
            }
            catch { return true; }
        }

        static bool IsEligibleSim(VMAvatar a)
        {
            if (a == null || a.Dead || a.IsPet) return false;
            if (a.Position == LotTilePos.OUT_OF_WORLD) return false;
            if (a.GetPersonData(VMPersonDataVariable.PersonsAge) < 18) return false;
            // SleepState > 40 indicates a sleeping Sim
            if (a.GetMotiveData(VMMotive.SleepState) > 40) return false;
            // Skip Sims currently engaged in a dedicated scripted social interaction
            if (a.GetPersonData(VMPersonDataVariable.SocialTargetID) > 0) return false;
            return true;
        }

        static bool SameRoom(VM vm, VMAvatar a, VMAvatar b)
        {
            var rA = vm.Context.GetObjectRoom(a);
            var rB = vm.Context.GetObjectRoom(b);
            bool outA = IsOutsideRoom(vm, rA);
            bool outB = IsOutsideRoom(vm, rB);
            return ParallelSocialPlanner.IsSameRoom(rA, outA, a.Position.TileX, a.Position.TileY, rB, outB, b.Position.TileX, b.Position.TileY);
        }

        static void ScanAndDispatch(VM vm)
        {
            var avatars = vm.Entities.OfType<VMAvatar>().Where(IsEligibleSim).ToList();
            if (avatars.Count < 2) return;

            foreach (var speaker in avatars)
            {
                short social = speaker.GetMotiveData(VMMotive.Social);
                if (!ParallelSocialPlanner.ShouldTriggerSocial(social)) continue;
                if (IsCoolingDownOrBusy(speaker.ObjectID)) continue;

                // Find eligible listeners in the same room
                var roomMates = new List<VMAvatar>();
                foreach (var candidate in avatars)
                {
                    if (ReferenceEquals(candidate, speaker)) continue;
                    if (IsCoolingDownOrBusy(candidate.ObjectID)) continue;
                    if (!SameRoom(vm, speaker, candidate)) continue;
                    var pKey = PairTickKey(speaker.ObjectID, candidate.ObjectID);
                    long nextPair;
                    lock (gate) { if (pairNextTick.TryGetValue(pKey, out nextPair) && tickCount < nextPair) continue; }
                    roomMates.Add(candidate);
                }
                if (roomMates.Count == 0) continue;

                // Prefer a roommate who isn't in negative-loop backoff (prefer higher STR or lower social)
                VMAvatar chosen = null;
                foreach (var mate in roomMates.OrderByDescending(m => ReadStr(vm, speaker, m)))
                {
                    var rsCheck = ConvoPrimitive.Read(speaker, mate, vm);
                    var rlCheck = ConvoPrimitive.Read(mate, speaker, vm);
                    if (Boot.Memory.ConsumeBackoff(rsCheck, rlCheck))
                    {
                        SetPairCooldown(speaker.ObjectID, mate.ObjectID, PairCooldownTicks);
                        continue;
                    }
                    chosen = mate;
                    break;
                }
                if (chosen == null) continue;

                Dispatch(vm, speaker, chosen);
                return;   // at most one new parallel conversation dispatched per scan tick
            }
        }

        public static string ForceTrigger(string speakerQuery, string listenerQuery)
        {
            var vm = lastVm;
            if (vm == null || !vm.Ready) throw new Exception("no lot is active yet");
            var avatars = vm.Entities.OfType<VMAvatar>().ToList();
            Func<string, VMAvatar> find = q =>
                avatars.FirstOrDefault(a => (a.Name ?? "").IndexOf(q ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
            var sp = string.IsNullOrEmpty(speakerQuery) ? avatars.FirstOrDefault(IsEligibleSim) : find(speakerQuery);
            var ls = string.IsNullOrEmpty(listenerQuery) ? avatars.FirstOrDefault(a => !ReferenceEquals(a, sp) && IsEligibleSim(a)) : find(listenerQuery);
            if (sp == null || ls == null) throw new Exception("could not find both Sims on the lot");
            Dispatch(vm, sp, ls);
            return "dispatched parallel social (" + sp.Name + " -> " + ls.Name + ")";
        }

        static void Dispatch(VM vm, VMAvatar speaker, VMAvatar listener)
        {
            var rs = ConvoPrimitive.Read(speaker, listener, vm);
            var rl = ConvoPrimitive.Read(listener, speaker, vm);
            short spId = speaker.ObjectID, lsId = listener.ObjectID;

            lock (gate)
            {
                inFlight.Add(spId);
                inFlight.Add(lsId);
                simNextTick[spId] = tickCount + SimCooldownTicks;
                simNextTick[lsId] = tickCount + SimCooldownTicks;
                pairNextTick[PairTickKey(spId, lsId)] = tickCount + PairCooldownTicks;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var localRng = new Random(Guid.NewGuid().GetHashCode());
                    var plan = ParallelSocialPlanner.Plan(rs, rl, Boot.Laya, Boot.Bank, Boot.Subjects, Boot.Memory, localRng);
                    lock (gate)
                    {
                        completed.Enqueue(new CompletedExchange
                        {
                            Vm = vm,
                            SpeakerId = spId,
                            ListenerId = lsId,
                            SpeakerRaw = rs,
                            ListenerRaw = rl,
                            Plan = plan,
                        });
                    }
                }
                catch (Exception e)
                {
                    Boot.Log("parallel social worker error: " + e.GetType().Name + ": " + e.Message);
                    lock (gate) { inFlight.Remove(spId); inFlight.Remove(lsId); }
                }
            });
        }

        static void DrainCompleted(VM vm)
        {
            while (true)
            {
                CompletedExchange ex;
                lock (gate)
                {
                    if (completed.Count == 0) return;
                    ex = completed.Dequeue();
                    inFlight.Remove(ex.SpeakerId);
                    inFlight.Remove(ex.ListenerId);
                }
                if (!ReferenceEquals(ex.Vm, vm) || ex.Plan == null) continue;
                try { ApplyExchange(vm, ex); }
                catch (Exception e) { Boot.Log("parallel social apply error: " + e.GetType().Name + ": " + e.Message); }
            }
        }

        static void ApplyExchange(VM vm, CompletedExchange ex)
        {
            var speaker = vm.GetObjectById(ex.SpeakerId) as VMAvatar;
            var listener = vm.GetObjectById(ex.ListenerId) as VMAvatar;
            if (!IsEligibleSim(speaker) || !IsEligibleSim(listener)) return;
            if (!SameRoom(vm, speaker, listener)) return;

            var plan = ex.Plan;
            // Apply Social motive deltas without disturbing either Sim's active interaction queue
            AddMotive(speaker, VMMotive.Social, plan.SpeakerSocialDelta);
            AddMotive(listener, VMMotive.Social, plan.ListenerSocialDelta);

            // Apply STR and LTR deltas in both directions
            AddRelationship(vm, speaker, listener, plan.SpeakerStrDelta, plan.SpeakerLtrDelta);
            AddRelationship(vm, listener, speaker, plan.ListenerStrDelta, plan.ListenerLtrDelta);

            // Record outcome in SocialMemory so both Sims learn about each other and remember successes/failures
            int interestIdx = plan.Subject != null ? plan.Subject.Interest : -1;
            string subjId = plan.Subject != null ? plan.Subject.Id : null;
            string subjTitle = plan.Subject != null ? plan.Subject.Title : null;
            // Refresh STR/LTR on raw snapshots after applying deltas so memory displays up-to-date relationship values
            ReadRelationship(vm, speaker, listener, out ex.SpeakerRaw.StrToTarget, out ex.SpeakerRaw.LtrToTarget);
            ReadRelationship(vm, listener, speaker, out ex.ListenerRaw.StrToTarget, out ex.ListenerRaw.LtrToTarget);
            Boot.Memory.Record(ex.SpeakerRaw, ex.ListenerRaw, plan.Interaction, plan.Tone, subjId, plan.Quality, interestIdx, plan.DiscoveredSkill, subjTitle, plan.ListenerTemperament);
            var speakerSkill = plan.Subject != null && plan.Subject.Skill != SkillKind.None ? plan.Subject.Skill : SkillKind.None;
            Boot.Memory.RecordReciprocal(ex.ListenerRaw, ex.SpeakerRaw, plan.Interaction, plan.Quality, interestIdx, speakerSkill, subjId, subjTitle);

            Interlocked.Increment(ref totalExchanges);
            LastParallelDecision = plan.Summary(ex.SpeakerRaw.Name, ex.ListenerRaw.Name);
            Boot.LastDecision = LastParallelDecision;
            Boot.Log("decision: " + LastParallelDecision + " | \"" + plan.SpeakerLine + "\" / \"" + plan.ReplyLine + "\"");

            ConvoOverlay.Show(vm, speaker, ex.SpeakerRaw.Name, plan.SpeakerLine, listener, ex.ListenerRaw.Name, plan.ReplyLine);

            if (++savesPending >= 2)
            {
                savesPending = 0;
                Boot.SaveMemoryAsync();
            }
        }

        static bool IsCoolingDownOrBusy(short id)
        {
            lock (gate)
            {
                if (inFlight.Contains(id)) return true;
                long until;
                return simNextTick.TryGetValue(id, out until) && tickCount < until;
            }
        }

        static void SetPairCooldown(short a, short b, int ticks)
        {
            lock (gate) pairNextTick[PairTickKey(a, b)] = tickCount + ticks;
        }

        static string PairTickKey(short a, short b) { return a < b ? a + ":" + b : b + ":" + a; }

        static void AddMotive(VMAvatar a, VMMotive motive, short delta)
        {
            if (delta == 0) return;
            int next = Math.Max(-100, Math.Min(100, a.GetMotiveData(motive) + delta));
            a.SetMotiveData(motive, (short)next);
        }

        public static short ReadStr(VM vm, VMAvatar from, VMAvatar to)
        {
            short str, ltr;
            ReadRelationship(vm, from, to, out str, out ltr);
            return str;
        }

        public static void ReadRelationship(VM vm, VMAvatar from, VMAvatar to, out short str, out short ltr)
        {
            str = 0; ltr = 0;
            if (from == null || to == null) return;
            try
            {
                if (vm != null && vm.TS1)
                {
                    var nb = FSO.Content.Content.Get().Neighborhood;
                    var f = nb?.GetNeighborByID(from.GetPersonData(VMPersonDataVariable.NeighborId));
                    var tid = (int)to.GetPersonData(VMPersonDataVariable.NeighborId);
                    List<short> v;
                    if (f != null && f.Relationships != null && f.Relationships.TryGetValue(tid, out v) && v != null)
                    {
                        if (v.Count > 0) str = v[0];
                        if (v.Count > 2) ltr = v[2];
                        return;
                    }
                }
                List<short> mo;
                if (from.MeToObject != null && from.MeToObject.TryGetValue((ushort)to.ObjectID, out mo) && mo != null)
                {
                    if (mo.Count > 0) str = mo[0];
                    if (mo.Count > 2) ltr = mo[2];
                }
            }
            catch { }
        }

        static void AddRelationship(VM vm, VMAvatar from, VMAvatar to, short strDelta, short ltrDelta)
        {
            if (strDelta == 0 && ltrDelta == 0) return;
            try
            {
                if (vm != null && vm.TS1)
                {
                    var nb = FSO.Content.Content.Get().Neighborhood;
                    var f = nb?.GetNeighborByID(from.GetPersonData(VMPersonDataVariable.NeighborId));
                    var tid = (int)to.GetPersonData(VMPersonDataVariable.NeighborId);
                    if (f != null && f.Relationships != null && tid > 0)
                    {
                        List<short> v;
                        if (!f.Relationships.TryGetValue(tid, out v) || v == null) { v = new List<short>(); f.Relationships[tid] = v; }
                        ApplyRelList(v, strDelta, ltrDelta);
                        return;
                    }
                }
                if (from.MeToObject != null)
                {
                    var key = (ushort)to.ObjectID;
                    List<short> v;
                    if (!from.MeToObject.TryGetValue(key, out v) || v == null) { v = new List<short>(); from.MeToObject[key] = v; }
                    ApplyRelList(v, strDelta, ltrDelta);
                    to.MayHaveRelToMe?.Add((ushort)from.ObjectID);
                }
            }
            catch (Exception e) { Boot.Log("rel update error: " + e.Message); }
        }

        static void ApplyRelList(List<short> v, short strDelta, short ltrDelta)
        {
            while (v.Count < 4) v.Add(0);
            int nextStr = Math.Max(-100, Math.Min(100, v[0] + strDelta));
            v[0] = (short)nextStr;
            if (nextStr >= 70) v[1] = 1;
            else if (nextStr < 70 && v[1] != 0) v[1] = 0;

            int nextLtr = Math.Max(-100, Math.Min(100, v[2] + ltrDelta));
            v[2] = (short)nextLtr;
            if (nextLtr >= 70) v[3] = 1;
            else if (nextLtr < 70 && v[3] != 0) v[3] = 0;
        }
    }
}
