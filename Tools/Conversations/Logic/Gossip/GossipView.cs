using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    public class NewsEntry
    {
        public int FactId;
        public string Text = "";         // "Goran got promoted to Recruit yesterday"
        public string Certainty = "";    // saw it | sure | not sure | fuzzy
        public string Source = "";       // "heard it from Mara"; empty when they saw it themselves (the certainty already says so)
        public string Passed = "";       // "told Bella and Dina"
        public bool Fuzzy;               // some part is only a guess or forgotten
        public bool Differs;             // this Sim's version disagrees with what really happened

        // "not sure | heard it from Mara | told Bella"
        public string Detail { get { return string.Join(" | ", new[] { Certainty, Source, Passed }.Where(s => !string.IsNullOrEmpty(s))); } }
    }

    // What the memory window shows about one Sim's news: plain sentences, how sure they are, where they heard it.
    public static class GossipView
    {
        public static List<NewsEntry> NewsOf(GossipBook book, int simId, int today)
        {
            var list = new List<NewsEntry>();
            if (book == null) return list;
            foreach (var b in book.BeliefsOf(simId).AsEnumerable().Reverse())   // newest first
            {
                var truth = book.GetFact(b.FactId);
                var e = new NewsEntry { FactId = b.FactId, Text = Sentence(book, b, today) };
                bool doubt = false, forgotten = false;
                foreach (var s in FactRules.AllSlots)
                {
                    if (!FactRules.Carries(b.Kind, s)) continue;
                    var sb = b.Get(s);
                    if (sb.State == SlotState.Doubt) doubt = true;
                    if (sb.State == SlotState.Forgotten) forgotten = true;
                    if (truth != null && sb.HasValue && truth.Has(s) && sb.Value != truth.Get(s)) e.Differs = true;
                }
                e.Fuzzy = doubt || forgotten;
                e.Certainty = forgotten ? "fuzzy" : doubt ? "not sure" : b.FirstHand ? "saw it" : "sure";
                e.Source = b.FirstHand ? "" : "heard it from " + book.NameOf(b.ToldBy);
                if (b.ToldTo.Count > 0) e.Passed = "told " + string.Join(", ", b.ToldTo.Select(book.NameOf));
                list.Add(e);
            }
            return list;
        }

        // the sentence from this Sim's own version: a forgotten day is "some day", an unsure one gets "maybe"
        public static string Sentence(GossipBook book, Belief b, int today)
        {
            Func<Slot, string> name = s => b.Get(s).HasValue ? (b.Get(s).State == SlotState.Doubt ? "maybe " : "") + book.NameOf(b.Get(s).Value) : "somebody";
            string when = b.Get(Slot.When).HasValue ? " " + (b.Get(Slot.When).State == SlotState.Doubt ? "maybe " : "") + GossipLines.DayPhrase(today, b.Get(Slot.When).Value) : "";
            string who = name(Slot.Who), with = name(Slot.With);
            string amount = b.Get(Slot.Amount).HasValue ? (b.Get(Slot.Amount).State == SlotState.Doubt ? "maybe " : "") + "level " + b.Get(Slot.Amount).Value : "a higher level";
            switch (b.Kind)
            {
                case FactKind.Promoted: return who + " got promoted" + (string.IsNullOrEmpty(b.Label) ? "" : " to " + b.Label) + when;
                case FactKind.SkillGain: return who + " reached " + amount + " in " + b.Label + when;
                case FactKind.Friendship: return who + " and " + with + " became friends" + when;
                case FactKind.Romance: return who + " and " + with + " got romantic" + when;
                case FactKind.Argument: return who + " and " + with + " had a fight" + when;
                default: return who + " gave " + with + " a gift" + when;
            }
        }
    }
}
