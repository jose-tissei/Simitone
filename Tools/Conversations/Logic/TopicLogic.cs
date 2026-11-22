using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // Talk subjects (P2). The game scores a topic by the listener's interest value (person data 46..53, 0..1000) in the
    // script `SHD do I like topic?`; here the player is offered subjects and the choice decides the result.
    public static class TopicLogic
    {
        // the eight interests the game stores per Sim, in person-data order 46..53
        public static readonly string[] Interests =
        {
            "Travel and Toys", "Violence and Aliens", "Politics and Pets", "Sixties and School", "Weather", "Sports", "Music", "Outdoors",
        };

        public class Subject
        {
            public int Interest;        // index into Interests
            public string Title { get { return Interests[Interest]; } }
        }

        // interest 0..1000: how the listener feels about a subject
        public static int Liking(short interest) { return interest >= 600 ? 2 : (interest >= 300 ? 1 : 0); }   // 2 loves, 1 neutral, 0 dislikes

        // Up to three subjects: one the speaker is keen on, one the listener is keen on, one other.
        // Deterministic for a given rng so tests can pin it.
        public static List<Subject> Offer(short[] speakerInterests, short[] listenerInterests, Random rng)
        {
            var picks = new List<int>();
            Action<short[]> addTop = arr =>
            {
                var order = Enumerable.Range(0, 8).OrderByDescending(i => arr[i]).ThenBy(i => rng.Next()).ToList();
                foreach (var i in order) if (!picks.Contains(i)) { picks.Add(i); return; }
            };
            addTop(speakerInterests);
            addTop(listenerInterests);
            var rest = Enumerable.Range(0, 8).Where(i => !picks.Contains(i)).OrderBy(i => rng.Next()).ToList();
            if (rest.Count > 0) picks.Add(rest[0]);
            return picks.Select(i => new Subject { Interest = i }).ToList();
        }

        // the game's own rule: liked topic raises the relationship, a disliked one lowers it (outcome 3 / 1);
        // neutral gives the middle result
        public static int OutcomeFor(short listenerInterest)
        {
            int l = Liking(listenerInterest);
            return l == 2 ? 3 : (l == 1 ? 2 : 1);
        }
    }
}
