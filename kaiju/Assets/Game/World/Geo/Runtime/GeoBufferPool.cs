// T-124f: small thread-safe pool for the big scratch arrays of the tile build (RTIN error / mask / render-height lattices, decoded heights, raw r16 bytes).
// A (2^k+1)^2 lattice is 16.8 MB; one tile read used to allocate about ten such buffers, which is what drives the collector while tiles stream.
// Only buffers of one exact length are reused; at most MaxKept are kept per type, so the pool never holds more than a few lattices. Output never depends on it:
// Rent(len, clear: true) returns a zeroed array, Rent(len, clear: false) is for buffers the caller overwrites completely.
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Game.World.Geo
{
    public static class GeoBufferPool<T>
    {
        public const int MaxKept = 3;
        /// <summary>Arrays below this many elements are not pooled (small ones are cheap to allocate).</summary>
        public const int MinLength = 1 << 16;
        static readonly ConcurrentStack<T[]> Kept = new ConcurrentStack<T[]>();
        static int _count;
        /// <summary>Counters for tests and the allocation bench: fresh allocations and reuses.</summary>
        public static long Allocated, Reused;

        public static T[] Rent(int length, bool clear)
        {
            if (length >= MinLength)
                while (Kept.TryPop(out var a))
                {
                    Interlocked.Decrement(ref _count);
                    if (a.Length != length) continue;      // another lattice size: drop it
                    if (clear) Array.Clear(a, 0, a.Length);
                    Interlocked.Increment(ref Reused);
                    return a;
                }
            Interlocked.Increment(ref Allocated);
            return new T[length];
        }

        public static void Return(T[] a)
        {
            if (a == null || a.Length < MinLength) return;
            if (Interlocked.Increment(ref _count) > MaxKept) { Interlocked.Decrement(ref _count); return; }
            Kept.Push(a);
        }
    }
}
