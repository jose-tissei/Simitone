// Shims so the vendored Yoga.Net sources (written for net8+) compile for the game's .NET Framework runtime.
using System.Collections.Generic;

namespace System
{
    public struct HashCode
    {
        int h;
        public void Add<T>(T v) { h = unchecked(h * 31 + (v == null ? 0 : EqualityComparer<T>.Default.GetHashCode(v))); }
        public int ToHashCode() { return h; }
        public static int Combine<A, B>(A a, B b) { var x = new HashCode(); x.Add(a); x.Add(b); return x.h; }
        public static int Combine<A, B, C, D, E>(A a, B b, C c, D d, E e) { var x = new HashCode(); x.Add(a); x.Add(b); x.Add(c); x.Add(d); x.Add(e); return x.h; }
        public static int Combine<A, B, C, D, E, F>(A a, B b, C c, D d, E e, F f) { var x = new HashCode(); x.Add(a); x.Add(b); x.Add(c); x.Add(d); x.Add(e); x.Add(f); return x.h; }
        public static int Combine<A>(A a) { var x = new HashCode(); x.Add(a); return x.h; }
    }

    public static class Compat
    {
        public static unsafe float Int32BitsToSingle(int v) { return *(float*)&v; }
        public static unsafe int SingleToInt32Bits(float v) { return *(int*)&v; }
    }
}

namespace System.Numerics
{
    public static class BitOperations
    {
        public static int LeadingZeroCount(uint v)
        {
            if (v == 0) return 32;
            int n = 0;
            while ((v & 0x80000000u) == 0) { n++; v <<= 1; }
            return n;
        }
    }
}
