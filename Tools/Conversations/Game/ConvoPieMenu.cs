using System;
using System.Collections.Generic;
using System.Linq;
using FSO.SimAntics;
using FSO.SimAntics.Model;

namespace Simitone.Conversations
{
    // Filters the manual Sim-to-Sim pie menu (called from the Cecil hook at VMEntity.GetPieMenu return)
    // so adult-to-adult interactions only show two top-level bubbles: "Talk" and "Romance".
    public static class ConvoPieMenu
    {
        public static List<VMPieMenuInteraction> Filter(List<VMPieMenuInteraction> raw, VMEntity target, VM vm, VMEntity caller)
        {
            try
            {
                if (Boot.Enabled && BeatPatrolDriver.IsActiveOn(vm))
                {
                    var patrolMenu = BeatPatrolDriver.TryBuildPieMenu(target, vm, caller);
                    if (patrolMenu != null) return patrolMenu;
                }
                if (Boot.Enabled && DispatchTriageDriver.IsActiveOn(vm))
                {
                    var triageMenu = DispatchTriageDriver.TryBuildPieMenu(target, vm, caller);
                    if (triageMenu != null) return triageMenu;
                }

                if (raw == null || raw.Count == 0 || !Boot.Enabled || Boot.FullMenu) return raw;
                var targetAv = target as VMAvatar;
                var callerAv = caller as VMAvatar;
                if (targetAv == null || callerAv == null) return raw;

                bool isSelf = ReferenceEquals(targetAv, callerAv);
                bool isAdultHuman = !targetAv.IsPet && !callerAv.IsPet &&
                                    targetAv.GetPersonData(VMPersonDataVariable.PersonsAge) >= 18 &&
                                    callerAv.GetPersonData(VMPersonDataVariable.PersonsAge) >= 18;

                var names = raw.Select(i => i == null ? "" : (i.Name ?? "")).ToList();
                var filtered = PieMenuLogic.Filter(names, true, isAdultHuman, isSelf);

                // fast path: passthrough (same count and identical names in order)
                if (filtered.Count == raw.Count && Enumerable.Range(0, raw.Count).All(idx => filtered[idx].OriginalIndex == idx && filtered[idx].DisplayName == names[idx]))
                    return raw;

                var output = new List<VMPieMenuInteraction>(filtered.Count);
                foreach (var entry in filtered)
                {
                    var src = raw[entry.OriginalIndex];
                    output.Add(new VMPieMenuInteraction
                    {
                        Name = entry.DisplayName,
                        ID = src.ID,
                        Param0 = src.Param0,
                        Entry = src.Entry,
                        Score = src.Score,
                        Callee = src.Callee,
                        Global = src.Global,
                        MotiveAdChanges = src.MotiveAdChanges,
                    });
                }
                return output;
            }
            catch (Exception e)
            {
                Boot.Log("pie menu filter error (returning unmodified menu): " + e.GetType().Name + ": " + e.Message);
                return raw;
            }
        }
    }
}
