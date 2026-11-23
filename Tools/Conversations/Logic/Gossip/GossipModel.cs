using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Simitone.Conversations
{
    // What Sims can talk about: things that happened to somebody in the neighbourhood. A fact is the TRUTH of what happened;
    // every Sim holds their own version of it (a Belief), which can be incomplete, forgotten or wrong.
    //
    // Design borrowed from Rumor Village (github.com/manuelgscom/rumor-village, MIT): the engine owns the state and decides what is
    // allowed, nobody ever decides from the text of a speech balloon. Reimplemented here for The Sims' own events.
    public enum FactKind { Promoted = 0, SkillGain, Friendship, Romance, Argument, Gift }

    // The parts of a fact that can be remembered wrongly. Who = whom it is about, With = the other person involved,
    // Amount = a number (skill level), When = the game day. The kind and the label (job title, skill name) are never in doubt.
    public enum Slot { Who = 0, With = 1, Amount = 2, When = 3 }

    public enum SlotState { Unknown = 0, Sure, Doubt, Forgotten }

    public enum GossipAct { Greet = 0, Tell, Ask, NoIdea, Bye, None }

    public static class FactRules
    {
        public const int NoValue = int.MinValue;
        public const int SlotCount = 4;
        public static readonly Slot[] AllSlots = { Slot.Who, Slot.With, Slot.Amount, Slot.When };

        // the slots a kind of fact carries
        public static bool Carries(FactKind kind, Slot slot)
        {
            switch (slot)
            {
                case Slot.Who: return true;
                case Slot.When: return true;
                case Slot.With: return kind != FactKind.Promoted && kind != FactKind.SkillGain;
                case Slot.Amount: return kind == FactKind.SkillGain;
                default: return false;
            }
        }

        public static string Key(FactKind kind) { return kind.ToString().ToLowerInvariant(); }
    }

    // The truth of one thing that happened.
    public class Fact
    {
        [JsonProperty("id")] public int Id;
        [JsonProperty("kind"), JsonConverter(typeof(StringEnumConverter))] public FactKind Kind;
        [JsonProperty("label")] public string Label = "";     // job title, skill name: part of what the fact IS
        [JsonProperty("slots")] public int[] Slots = NewSlots();

        public static int[] NewSlots() { return new[] { FactRules.NoValue, FactRules.NoValue, FactRules.NoValue, FactRules.NoValue }; }

        public int Get(Slot s) { return Slots[(int)s]; }
        public bool Has(Slot s) { return Slots[(int)s] != FactRules.NoValue; }
    }

    public class SlotBelief
    {
        [JsonProperty("state"), JsonConverter(typeof(StringEnumConverter))] public SlotState State = SlotState.Unknown;
        [JsonProperty("value")] public int Value = FactRules.NoValue;   // set exactly when State is Sure or Doubt
        [JsonProperty("source")] public int Source;                      // who said it (0 = the Sim saw it happen)

        public bool HasValue { get { return (State == SlotState.Sure || State == SlotState.Doubt) && Value != FactRules.NoValue; } }
    }

    // One Sim's version of one fact.
    public class Belief
    {
        [JsonProperty("fact")] public int FactId;
        [JsonProperty("kind"), JsonConverter(typeof(StringEnumConverter))] public FactKind Kind;
        [JsonProperty("label")] public string Label = "";
        [JsonProperty("slots")] public SlotBelief[] Slots = NewSlots();
        [JsonProperty("toldBy")] public int ToldBy;                       // who told it first; 0 when the Sim saw it happen
        [JsonProperty("heardTick")] public long HeardTick;
        [JsonProperty("toldTo")] public List<int> ToldTo = new List<int>();   // whom this Sim has told it to (ascending, no repeats)

        public static SlotBelief[] NewSlots() { return new[] { new SlotBelief(), new SlotBelief(), new SlotBelief(), new SlotBelief() }; }

        public SlotBelief Get(Slot s) { return Slots[(int)s]; }
        public bool FirstHand { get { return ToldBy == 0; } }
        public bool HasValue(Slot s) { return Slots[(int)s].HasValue; }
    }

    // Everything one Sim knows, in the order they learned it.
    public class Mind
    {
        [JsonProperty("beliefs")] public List<Belief> Beliefs = new List<Belief>();

        public Belief Find(int factId) { return Beliefs.FirstOrDefault(b => b.FactId == factId); }
    }

    // What a Sim says when telling a fact: a value per slot (NoValue = leaves it out) and which of those values are guesses.
    public class Claim
    {
        public int FactId;
        public int[] Values = Fact.NewSlots();
        public bool[] Guess = new bool[FactRules.SlotCount];

        public bool Has(Slot s) { return Values[(int)s] != FactRules.NoValue; }

        public Claim Clone() { return new Claim { FactId = FactId, Values = (int[])Values.Clone(), Guess = (bool[])Guess.Clone() }; }
    }

    // One thing a Sim does in a conversation.
    public class GossipAction
    {
        public GossipAct Act;
        public int FactId;          // Tell, Ask, NoIdea
        public Claim Claim;         // Tell

        public static GossipAction Of(GossipAct act) { return new GossipAction { Act = act }; }
    }
}
