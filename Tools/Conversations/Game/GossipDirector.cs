using System;
using System.Collections.Generic;
using System.Linq;
using FSO.SimAntics;
using FSO.SimAntics.Model;

namespace Simitone.Conversations
{
    // The game side of gossip: notices things worth talking about (a skill point, a promotion, two Sims becoming friends, a kiss, a fight,
    // a gift), records them in the GossipBook with every Sim who was in the room as a witness, and lets memories fade as days pass.
    // Runs on the VM tick thread only (called from ParallelSocialDriver); the book itself is thread-safe for the planning thread.
    public static class GossipDirector
    {
        static readonly Dictionary<short, RawSim> lastSeen = new Dictionary<short, RawSim>();
        static readonly Dictionary<string, short> lastStr = new Dictionary<string, short>();
        static int lastDay = -1;
        public static string LastEvent = "";
        public static int EventsRecorded;

        public static void Reset()
        {
            lastSeen.Clear(); lastStr.Clear(); lastDay = -1;
        }

        // the game's calendar has 30-day months; only differences between days matter
        public static int Today(VM vm)
        {
            try { var c = vm.Context.Clock; return Math.Max(0, c.Year * 360 + (c.Month - 1) * 30 + c.DayOfMonth); }
            catch { return 0; }
        }

        static long Tick(VM vm)
        {
            try { return vm.Context.Clock.Ticks; } catch { return 0; }
        }

        // Called every room scan with the Sims that are awake and on the lot.
        public static void Observe(VM vm, List<VMAvatar> avatars)
        {
            if (!Boot.GossipEnabled) return;
            int today = Today(vm);
            if (lastDay >= 0 && today > lastDay) NewDay(today);
            lastDay = today;
            long tick = Tick(vm);
            var book = Boot.Gossip;

            var raws = new Dictionary<VMAvatar, RawSim>();
            foreach (var a in avatars)
            {
                var now = ConvoPrimitive.Read(a);
                if (now.NeighborId <= 0) continue;
                raws[a] = now;
                book.SetName(now.NeighborId, now.Name);
                RawSim before;
                if (lastSeen.TryGetValue(now.NeighborId, out before))
                    foreach (var e in EventDetector.Diff(before, now))
                        Record(vm, today, tick, e, a, null, avatars);
                lastSeen[now.NeighborId] = now;
            }

            // two Sims who were not friends and now are
            foreach (var a in raws.Keys)
                foreach (var b in raws.Keys)
                {
                    if (ReferenceEquals(a, b) || raws[a].NeighborId >= raws[b].NeighborId) continue;   // each pair once
                    short str, ltr;
                    ParallelSocialDriver.ReadRelationship(vm, a, b, out str, out ltr);
                    var key = raws[a].NeighborId + ":" + raws[b].NeighborId;
                    short was;
                    if (lastStr.TryGetValue(key, out was) && EventDetector.BecameFriends(was, str))
                        Record(vm, today, tick, new DetectedEvent { Kind = FactKind.Friendship, NeedsOther = true }, a, b, avatars);
                    lastStr[key] = str;
                }
        }

        // Called when a social interaction has finished (manual or autonomous), with how it went.
        public static void OnInteraction(VM vm, VMAvatar speaker, VMAvatar listener, string interaction, int quality)
        {
            if (!Boot.GossipEnabled || vm == null || speaker == null || listener == null) return;
            var events = EventDetector.FromInteraction(interaction, quality);
            if (events.Count == 0) return;
            int today = Today(vm);
            long tick = Tick(vm);
            var room = vm.Entities.OfType<VMAvatar>().Where(ParallelSocialDriver.IsEligibleSim).ToList();
            foreach (var e in events) Record(vm, today, tick, e, speaker, listener, room);
        }

        static void Record(VM vm, int today, long tick, DetectedEvent e, VMAvatar who, VMAvatar with, List<VMAvatar> everyone)
        {
            short whoId = who.GetPersonData(VMPersonDataVariable.NeighborId);
            short withId = with == null ? (short)0 : with.GetPersonData(VMPersonDataVariable.NeighborId);
            if (whoId <= 0 || (e.NeedsOther && withId <= 0)) return;
            var book = Boot.Gossip;
            book.SetName(whoId, who.Name); if (with != null && withId > 0) book.SetName(withId, with.Name);
            int a = whoId, b = withId;
            if (e.Kind == FactKind.Friendship && a > b) { var t = a; a = b; b = t; }   // friendship is mutual: one fact for the pair
            var fact = book.AddFact(e.Kind, e.Label, a, b, e.Amount, today);
            int witnesses = 0;
            foreach (var other in everyone)
            {
                short id = other.GetPersonData(VMPersonDataVariable.NeighborId);
                if (id <= 0) continue;
                bool involved = id == a || id == b;
                if (!involved && !ParallelSocialDriver.SameRoom(vm, who, other)) continue;
                book.SetName(id, other.Name);
                book.Witness(id, fact.Id, tick); witnesses++;
            }
            EventsRecorded++;
            LastEvent = e.Kind + (string.IsNullOrEmpty(e.Label) ? "" : " " + e.Label) + " " + book.NameOf(a) + (b > 0 ? "+" + book.NameOf(b) : "") + " witnesses=" + witnesses;
            Boot.Log("gossip: new fact #" + fact.Id + " " + LastEvent);
        }

        // A new game day: memories fade a little, very old news is dropped, and the book is saved.
        static void NewDay(int today)
        {
            var book = Boot.Gossip;
            foreach (var id in book.SimIds()) book.Forget(id, 0xC0FFEEUL, today, GossipBook.DefaultForgetPpm, GossipBook.DefaultDoubtDropPpm);
            book.Prune(today);
            Boot.SaveGossipAsync();
        }

        // ---- test commands (simtool convo fact ... / news ...)

        public static string Command(string[] a)
        {
            var vm = ParallelSocialDriver.CurrentVm;
            if (vm == null || !vm.Ready) throw new Exception("no lot is active yet");
            var book = Boot.Gossip;
            var avatars = vm.Entities.OfType<VMAvatar>().ToList();
            Func<string, VMAvatar> find = q => avatars.FirstOrDefault(x => (x.Name ?? "").IndexOf(q ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
            switch (a[0])
            {
                case "fact":
                {
                    // convo fact <promoted|skillgain|friendship|romance|argument|gift> <who> [with]: makes it happen, seen by everyone in the room
                    if (a.Length < 3) throw new Exception("convo fact <kind> <who> [with]");
                    FactKind kind;
                    if (!Enum.TryParse(a[1], true, out kind)) throw new Exception("kind is one of " + string.Join(", ", Enum.GetNames(typeof(FactKind))));
                    // a Sim who is not on the lot can be named by neighbour number: #34=Mortimer (the news is then seen by whoever is on the lot)
                    Func<string, int> absent = tok =>
                    {
                        if (tok == null || !tok.StartsWith("#")) return 0;
                        var parts = tok.Substring(1).Split('=');
                        int id; if (!int.TryParse(parts[0], out id)) return 0;
                        if (parts.Length > 1) book.SetName(id, parts[1]);
                        return id;
                    };
                    int absentWho = absent(a[2]), absentWith = a.Length > 3 ? absent(a[3]) : 0;
                    if (absentWho > 0 || absentWith > 0)
                    {
                        var fk = kind; var present = avatars.Where(ParallelSocialDriver.IsEligibleSim).ToList();
                        var by = a.FirstOrDefault(x => x.StartsWith("by="));   // by=Bob: only Bob saw it
                        if (by != null) present = present.Where(p => (p.Name ?? "").IndexOf(by.Substring(3), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                        int who0 = absentWho > 0 ? absentWho : (find(a[2]) == null ? 0 : find(a[2]).GetPersonData(VMPersonDataVariable.NeighborId));
                        int with0 = absentWith > 0 ? absentWith : (a.Length > 3 && find(a[3]) != null ? find(a[3]).GetPersonData(VMPersonDataVariable.NeighborId) : 0);
                        if (who0 <= 0) throw new Exception("need a Sim");
                        var fact0 = book.AddFact(fk, fk == FactKind.Promoted ? "Recruit" : fk == FactKind.SkillGain ? "cooking" : "", who0, with0, 4, Today(vm));
                        foreach (var p in present) { int pid = p.GetPersonData(VMPersonDataVariable.NeighborId); if (pid > 0) { book.SetName(pid, p.Name); book.Witness(pid, fact0.Id, Tick(vm)); } }
                        EventsRecorded++;
                        LastEvent = fk + " " + book.NameOf(who0) + (with0 > 0 ? "+" + book.NameOf(with0) : "") + " witnesses=" + present.Count + " (absent party)";
                        return "recorded " + LastEvent;
                    }
                    var who = find(a[2]); var with = a.Length > 3 ? find(a[3]) : null;
                    if (who == null) throw new Exception("no Sim called " + a[2]);
                    if (FactRules.Carries(kind, Slot.With) && with == null) throw new Exception("that kind needs a second Sim");
                    var label = kind == FactKind.Promoted ? "Recruit" : kind == FactKind.SkillGain ? "cooking" : "";
                    Record(vm, Today(vm), Tick(vm), new DetectedEvent { Kind = kind, Label = label, Amount = 4, NeedsOther = with != null }, who, with, avatars.Where(ParallelSocialDriver.IsEligibleSim).ToList());
                    return "recorded " + LastEvent;
                }
                case "news":
                {
                    var sim = a.Length > 1 ? find(a[1]) : null;
                    var ids = sim != null ? new List<int> { sim.GetPersonData(VMPersonDataVariable.NeighborId) } : book.SimIds();
                    var lines = new List<string>();
                    foreach (var id in ids)
                        foreach (var n in GossipView.NewsOf(book, id, Today(vm))) lines.Add(book.NameOf(id) + ": " + n.Text + " [" + n.Detail + "]");
                    return lines.Count == 0 ? "no news yet (facts=" + book.FactCount + " recorded=" + EventsRecorded + ")" : string.Join(" | ", lines);
                }
                case "gossipstatus":
                    return "enabled=" + Boot.GossipEnabled + " facts=" + book.FactCount + " sims=" + book.SimIds().Count + " recorded=" + EventsRecorded + " last=[" + LastEvent + "] today=" + Today(vm);
                default:
                    throw new Exception("fact|news|gossipstatus");
            }
        }
    }
}
