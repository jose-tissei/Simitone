using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Simitone.Conversations
{
    // What changed in a listener's mind when they heard a claim.
    public class HearResult
    {
        public bool IsNew;                                   // the listener had never heard of this fact
        public List<Slot> Filled = new List<Slot>();         // slots that went from unknown/forgotten/doubt to something better
        public List<Slot> Conflicts = new List<Slot>();      // the listener was sure of a different value
        public bool NothingNew { get { return !IsNew && Filled.Count == 0 && Conflicts.Count == 0; } }
    }

    public enum Reaction { None = 0, Surprised, KnewIt, Doubtful, FilledGap }

    // The truth of the neighbourhood (facts) and what every Sim thinks happened (minds). Pure data and rules, no game types:
    // the game side feeds it events and Sim ids, the tests drive it directly.
    //
    // Thread-safe through one lock, like SocialMemory: planning runs off the game thread and the VM tick thread records events.
    public class GossipBook
    {
        public const int MaxBeliefsPerSim = 24;
        public const int KeepDays = 14;
        public const uint DefaultForgetPpm = 40000;      // chance per slot per game day to forget it
        public const uint DefaultDoubtDropPpm = 120000;  // chance per day that a doubt is remembered as certain

        readonly object gate = new object();
        [JsonProperty("nextFact")] int nextFact = 1;
        [JsonProperty("facts")] Dictionary<int, Fact> facts = new Dictionary<int, Fact>();
        [JsonProperty("minds")] Dictionary<int, Mind> minds = new Dictionary<int, Mind>();
        [JsonProperty("names")] Dictionary<int, string> names = new Dictionary<int, string>();

        public object Gate { get { return gate; } }
        public int FactCount { get { lock (gate) return facts.Count; } }

        // ---- names (so lines and the memory window can say "Goran" instead of a number)

        public void SetName(int simId, string name)
        {
            if (simId <= 0 || string.IsNullOrWhiteSpace(name)) return;
            lock (gate) names[simId] = name.Trim();
        }

        public string NameOf(int simId)
        {
            lock (gate)
            {
                string n;
                return names.TryGetValue(simId, out n) ? n : "someone";
            }
        }

        public bool KnownSim(int simId) { lock (gate) return names.ContainsKey(simId); }

        public List<int> SimIds() { lock (gate) return names.Keys.OrderBy(k => k).ToList(); }

        // ---- facts

        public Fact GetFact(int factId)
        {
            lock (gate)
            {
                Fact f;
                return facts.TryGetValue(factId, out f) ? f : null;
            }
        }

        // Records that something happened. The same event on the same day is not recorded twice.
        public Fact AddFact(FactKind kind, string label, int who, int with, int amount, int day)
        {
            lock (gate)
            {
                foreach (var old in facts.Values)
                    if (old.Kind == kind && old.Label == (label ?? "") && old.Get(Slot.Who) == who && old.Get(Slot.With) == (with > 0 ? with : FactRules.NoValue)
                        && old.Get(Slot.When) == day && (!FactRules.Carries(kind, Slot.Amount) || old.Get(Slot.Amount) == amount))
                        return old;
                var f = new Fact { Id = nextFact++, Kind = kind, Label = label ?? "" };
                f.Slots[(int)Slot.Who] = who;
                if (FactRules.Carries(kind, Slot.With) && with > 0) f.Slots[(int)Slot.With] = with;
                if (FactRules.Carries(kind, Slot.Amount)) f.Slots[(int)Slot.Amount] = amount;
                f.Slots[(int)Slot.When] = day;
                facts[f.Id] = f;
                return f;
            }
        }

        // ---- minds

        public Mind GetMind(int simId)
        {
            lock (gate)
            {
                Mind m;
                if (!minds.TryGetValue(simId, out m)) { m = new Mind(); minds[simId] = m; }
                return m;
            }
        }

        // a copy that is safe to read from another thread while the game thread keeps updating the book
        public Mind SnapshotMind(int simId)
        {
            lock (gate)
            {
                Mind m;
                if (!minds.TryGetValue(simId, out m)) return new Mind();
                return JsonConvert.DeserializeObject<Mind>(JsonConvert.SerializeObject(m));
            }
        }

        public int BeliefCount(int simId) { lock (gate) { Mind m; return minds.TryGetValue(simId, out m) ? m.Beliefs.Count : 0; } }

        // The Sim saw it happen: every slot the fact carries is known for sure, first hand.
        public void Witness(int simId, int factId, long tick)
        {
            lock (gate)
            {
                Fact f;
                if (simId <= 0 || !facts.TryGetValue(factId, out f)) return;
                var mind = GetMind(simId);
                var b = FindOrAdd(mind, f, 0, tick);
                foreach (var s in FactRules.AllSlots)
                {
                    if (!f.Has(s)) continue;
                    var sb = b.Get(s);
                    sb.State = SlotState.Sure; sb.Value = f.Get(s); sb.Source = 0;
                }
                b.ToldBy = 0;   // even if they heard it first, they have seen it now
                Trim(mind);
            }
        }

        static Belief FindOrAdd(Mind mind, Fact f, int toldBy, long tick)
        {
            var b = mind.Find(f.Id);
            if (b != null) return b;
            b = new Belief { FactId = f.Id, Kind = f.Kind, Label = f.Label, ToldBy = toldBy, HeardTick = tick };
            mind.Beliefs.Add(b);
            return b;
        }

        void Trim(Mind mind)
        {
            while (mind.Beliefs.Count > MaxBeliefsPerSim) mind.Beliefs.RemoveAt(0);   // the oldest is forgotten first
        }

        // The listener hears a claim. Same rules as Rumor Village's memory: an unknown or forgotten slot takes the value (as a
        // doubt if the teller said it was a guess), a doubt becomes sure when told as fact, and a slot the listener is sure about
        // is never overwritten (a different value is reported as a conflict instead).
        public HearResult Hear(int listener, int teller, Claim claim, long tick)
        {
            var result = new HearResult();
            lock (gate)
            {
                Fact f;
                if (listener <= 0 || claim == null || !facts.TryGetValue(claim.FactId, out f)) return result;
                var mind = GetMind(listener);
                result.IsNew = mind.Find(f.Id) == null;
                var b = FindOrAdd(mind, f, teller, tick);
                foreach (var s in FactRules.AllSlots)
                {
                    if (!claim.Has(s) || !FactRules.Carries(f.Kind, s)) continue;
                    int value = claim.Values[(int)s];
                    bool guess = claim.Guess[(int)s];
                    var sb = b.Get(s);
                    switch (sb.State)
                    {
                        case SlotState.Unknown:
                        case SlotState.Forgotten:
                            sb.State = guess ? SlotState.Doubt : SlotState.Sure;
                            break;
                        case SlotState.Doubt:
                            if (guess) { if (sb.Value != value) { result.Conflicts.Add(s); } continue; }
                            sb.State = SlotState.Sure;
                            break;
                        case SlotState.Sure:
                            if (sb.Value != value) result.Conflicts.Add(s);
                            continue;
                    }
                    sb.Value = value; sb.Source = teller;
                    result.Filled.Add(s);
                }
                Trim(mind);
            }
            return result;
        }

        public void MarkTold(int teller, int factId, int listener)
        {
            lock (gate)
            {
                Mind m;
                if (!minds.TryGetValue(teller, out m)) return;
                var b = m.Find(factId);
                if (b == null) return;
                int i = b.ToldTo.BinarySearch(listener);
                if (i < 0) b.ToldTo.Insert(~i, listener);
            }
        }

        // Time passes: a slot may be forgotten (and its old value erased, so it can never leak back), and a doubt may harden into
        // "sure" because the Sim stops remembering it was only a guess. Call once per game day per Sim.
        public int Forget(int simId, ulong seed, long tick, uint forgetPpm, uint doubtDropPpm)
        {
            int changed = 0;
            lock (gate)
            {
                Mind m;
                if (!minds.TryGetValue(simId, out m)) return 0;
                var rng = new GossipRng(GossipRng.Derive(seed, tick, simId, 1));
                foreach (var b in m.Beliefs)
                    foreach (var s in FactRules.AllSlots)
                    {
                        var sb = b.Get(s);
                        if (sb.State != SlotState.Sure && sb.State != SlotState.Doubt) continue;
                        if (sb.Source == 0 && sb.State == SlotState.Sure && b.FirstHand && s == Slot.Who) continue;   // you never forget who it was you saw
                        if (rng.Chance(forgetPpm)) { sb.State = SlotState.Forgotten; sb.Value = FactRules.NoValue; sb.Source = 0; changed++; }
                        else if (sb.State == SlotState.Doubt && rng.Chance(doubtDropPpm)) { sb.State = SlotState.Sure; changed++; }
                    }
            }
            return changed;
        }

        // Drops facts nobody is likely to talk about any more, and beliefs about them.
        public void Prune(int today)
        {
            lock (gate)
            {
                var old = facts.Values.Where(f => f.Has(Slot.When) && today - f.Get(Slot.When) > KeepDays).Select(f => f.Id).ToList();
                if (old.Count == 0) return;
                foreach (var id in old) facts.Remove(id);
                foreach (var m in minds.Values) m.Beliefs.RemoveAll(b => old.Contains(b.FactId));
            }
        }

        // A private copy holding every fact and only the given Sims' minds, to play a conversation on without touching this book.
        public GossipBook CloneFor(params int[] simIds)
        {
            lock (gate)
            {
                var c = new GossipBook { nextFact = nextFact };
                foreach (var kv in facts) c.facts[kv.Key] = JsonConvert.DeserializeObject<Fact>(JsonConvert.SerializeObject(kv.Value));
                foreach (var kv in names) c.names[kv.Key] = kv.Value;
                foreach (var id in simIds)
                {
                    Mind m;
                    c.minds[id] = minds.TryGetValue(id, out m) ? JsonConvert.DeserializeObject<Mind>(JsonConvert.SerializeObject(m)) : new Mind();
                }
                return c;
            }
        }

        // Puts a played conversation into the real book: the listeners hear what was said and the tellers remember telling it.
        public void Apply(GossipTranscript transcript)
        {
            if (transcript == null) return;
            foreach (var e in transcript.Effects)
            {
                Hear(e.Listener, e.Teller, e.Claim, transcript.Tick);
                MarkTold(e.Teller, e.Claim.FactId, e.Listener);
            }
        }

        // ---- everything the memory window needs

        public List<Belief> BeliefsOf(int simId)
        {
            lock (gate)
            {
                Mind m;
                if (!minds.TryGetValue(simId, out m)) return new List<Belief>();
                return m.Beliefs.Select(b => JsonConvert.DeserializeObject<Belief>(JsonConvert.SerializeObject(b))).ToList();
            }
        }

        // ---- saving

        public void Save(string path)
        {
            string json;
            lock (gate) json = JsonConvert.SerializeObject(this, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static GossipBook Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new GossipBook();
                var b = JsonConvert.DeserializeObject<GossipBook>(File.ReadAllText(path));
                return b ?? new GossipBook();
            }
            catch { return new GossipBook(); }
        }
    }
}
