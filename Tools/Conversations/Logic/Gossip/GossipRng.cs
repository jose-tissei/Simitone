using System;

namespace Simitone.Conversations
{
    // A small deterministic random generator (SplitMix64). Gossip never uses System.Random, so the same seed always replays the same
    // conversations: that is what makes the frozen scenario tests possible.
    public struct GossipRng
    {
        ulong state;

        public GossipRng(ulong seed) { state = seed; }

        public const uint Ppm = 1000000;   // probabilities are parts per million

        public static ulong Derive(ulong seed, long tick, int id, int stream)
        {
            ulong x = seed ^ 0x9E3779B97F4A7C15UL;
            x = Mix(x + (ulong)tick * 0xBF58476D1CE4E5B9UL);
            x = Mix(x + (ulong)(uint)id * 0x94D049BB133111EBUL);
            x = Mix(x + (ulong)(uint)stream * 0x2545F4914F6CDD1DUL);
            return x;
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong Next()
        {
            state += 0x9E3779B97F4A7C15UL;
            return Mix(state);
        }

        // 0 .. n-1
        public int Below(int n) { return n <= 1 ? 0 : (int)(Next() % (ulong)n); }

        // a..b inclusive
        public int Range(int a, int b) { return a + Below(b - a + 1); }

        public bool Chance(uint ppm) { return (uint)(Next() % Ppm) < ppm; }
    }
}
