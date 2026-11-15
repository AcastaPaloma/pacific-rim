using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Ordered-dither masks as data (reconciled spec, readings table: Bayer 4x4 is the v1 default and at least one
    /// alternative is selectable without code). A mask is a square of ranks 0..n*n-1; the shader compares
    /// (rank + 0.5) / (n*n) with the mix ratio.
    /// </summary>
    public static class DitherMasks
    {
        /// <summary>Recursive Bayer index matrix (Bayer 1973). <paramref name="size"/> must be a power of two. Row-major, [y * size + x].</summary>
        public static int[] Bayer(int size)
        {
            if (size < 1 || (size & (size - 1)) != 0) throw new ArgumentException("Bayer size must be a power of two", nameof(size));
            var m = new int[] { 0 };
            for (int n = 1; n < size; n *= 2)
            {
                var next = new int[4 * n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int v = 4 * m[y * n + x];
                        next[y * 2 * n + x] = v;                 // top-left     4M
                        next[y * 2 * n + x + n] = v + 2;         // top-right    4M+2
                        next[(y + n) * 2 * n + x] = v + 3;       // bottom-left  4M+3
                        next[(y + n) * 2 * n + x + n] = v + 1;   // bottom-right 4M+1
                    }
                m = next;
            }
            return m;
        }

        /// <summary>
        /// Clustered-dot mask: two dot centres per tile on a 45 degree screen; thresholds grow outward from each centre,
        /// so mixes form clumps of pixels instead of a fine checker (closer to the clustered transitions R-002 measures).
        /// </summary>
        public static int[] ClusteredDot(int size)
        {
            if (size < 2 || size % 2 != 0) throw new ArgumentException("clustered-dot size must be even", nameof(size));
            int n = size * size, half = size / 2;
            var score = new double[n];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance to the nearest of the two dot centres on the torus; the second half of the range fills the gaps.
                    double d0 = TorusDistance(x + 0.5, y + 0.5, half * 0.5, half * 0.5, size);
                    double d1 = TorusDistance(x + 0.5, y + 0.5, half * 1.5, half * 1.5, size);
                    double d = Math.Min(d0, d1);
                    // Tie-break deterministically by angle and position so the ranking is a strict order.
                    score[y * size + x] = d + 1e-6 * (y * size + x);
                }
            return RanksOf(score);
        }

        static double TorusDistance(double x, double y, double cx, double cy, int size)
        {
            double dx = Math.Abs(x - cx), dy = Math.Abs(y - cy);
            dx = Math.Min(dx, size - dx); dy = Math.Min(dy, size - dy);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Blue-noise mask by the void-and-cluster idea (Ulichney 1993), simplified: points are added one at a time at
        /// the largest void of a Gaussian energy field on the torus. Deterministic for a given size and seed.
        /// </summary>
        public static int[] BlueNoise(int size, int seed)
        {
            if (size < 2) throw new ArgumentOutOfRangeException(nameof(size));
            int n = size * size;
            var energy = new double[n];
            var rank = new int[n];
            for (int i = 0; i < n; i++) rank[i] = -1;
            const double sigma = 1.9;
            int radius = Math.Min(size / 2, 6);
            var kernel = new double[(2 * radius + 1) * (2 * radius + 1)];
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    kernel[(dy + radius) * (2 * radius + 1) + dx + radius] = Math.Exp(-(dx * dx + dy * dy) / (2 * sigma * sigma));
            // A seeded, tiny per-cell bias breaks the symmetry of the empty field without any random generator state.
            uint s = (uint)seed * 2654435761u + 12345u;
            for (int i = 0; i < n; i++) { s = s * 1664525u + 1013904223u; energy[i] = (s >> 8) / 16777216.0 * 1e-3; }
            for (int r = 0; r < n; r++)
            {
                int best = -1; double bestE = double.MaxValue;
                for (int i = 0; i < n; i++)
                    if (rank[i] < 0 && energy[i] < bestE) { bestE = energy[i]; best = i; }
                rank[best] = r;
                int bx = best % size, by = best / size;
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int x = ((bx + dx) % size + size) % size, y = ((by + dy) % size + size) % size;
                        energy[y * size + x] += kernel[(dy + radius) * (2 * radius + 1) + dx + radius];
                    }
            }
            return rank;
        }

        static int[] RanksOf(double[] score)
        {
            int n = score.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort((double[])score.Clone(), order);
            var rank = new int[n];
            for (int r = 0; r < n; r++) rank[order[r]] = r;
            return rank;
        }

        /// <summary>Thresholds as bytes: round(255 * (rank + 0.5) / count). The shader reads (byte + 0.5) / 256.</summary>
        public static byte[] ToThresholdBytes(int[] ranks)
        {
            var bytes = new byte[ranks.Length];
            for (int i = 0; i < ranks.Length; i++)
                bytes[i] = (byte)Math.Min(255, (int)Math.Floor(256.0 * (ranks[i] + 0.5) / ranks.Length));
            return bytes;
        }

        /// <summary>Builds a mask by id: "bayer2", "bayer4", "bayer8", "cluster8", "bluenoise16", "bluenoise32" (any valid size).</summary>
        public static int[] Create(string id, out int size)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (id.StartsWith("bayer", StringComparison.Ordinal)) { size = int.Parse(id.Substring(5)); return Bayer(size); }
            if (id.StartsWith("cluster", StringComparison.Ordinal)) { size = int.Parse(id.Substring(7)); return ClusteredDot(size); }
            if (id.StartsWith("bluenoise", StringComparison.Ordinal)) { size = int.Parse(id.Substring(9)); return BlueNoise(size, 1); }
            throw new ArgumentException("unknown dither mask '" + id + "'", nameof(id));
        }
    }
}
