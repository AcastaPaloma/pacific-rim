// Engine-free pixel check of the slice smoke (T-039c, DEF-M1slice-f1). The smoke captures the presented frame at several times and
// asks three questions of the pixels, so a blank, flat or frozen render can never pass on counters alone:
//   1. non-background share: how many pixels are NOT a colour of the sky-only reference frame (same camera, nothing drawn but the sky/haze);
//   2. richness: a capture needs at least MinDistinctColours colours and no single colour above MaxDominantShare (a flat wall or a flat sky fails even though it is "not the reference sky");
//   3. pairwise difference: every two captures differ in at least one pixel (the scene moves: kaiju, mech, quake shake, destruction).
// Pixels are packed 0xRRGGBB, row-major, top row first. HUD rectangles are ignored.
using System;
using System.Collections.Generic;

namespace Game.Slice
{
    public readonly struct PixelRect
    {
        public readonly int X0, Y0, X1, Y1;   // [X0,X1) x [Y0,Y1), top-left origin
        public PixelRect(int x0, int y0, int x1, int y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public bool Contains(int x, int y) { return x >= X0 && x < X1 && y >= Y0 && y < Y1; }
    }

    public sealed class PixelCheckVerdict
    {
        public double Threshold;
        public double[] NonBackgroundShare = Array.Empty<double>();
        public int[] DistinctColours = Array.Empty<int>();
        public double[] DominantShare = Array.Empty<double>();
        public double MaxDominant;
        public int MinDistinct;
        public bool RichnessPass;
        public double[,] PairDiffShare = new double[0, 0];
        public double MinShare, MinPairDiffShare;
        public int BackgroundColours;
        public bool SharesPass, PairsPass, Pass;
    }

    public static class SlicePixelCheck
    {
        /// <summary>Per-capture floor of non-background pixels (Assumed: a third-person view of a dense district fills well over a fifth of the frame with terrain, buildings and actors).</summary>
        public const double MinNonBackgroundShare = 0.20;
        /// <summary>Richness floors (Assumed; set after the first rendered captures: the broken frames of DEF-M1slice-f1 had 2 colours and a 0.995 dominant share, rendered city frames have 5-10 colours and at most 0.8 in the follow view).</summary>
        public const int MinDistinctColours = 5;
        public const double MaxDominantShare = 0.90;

        public static uint Pack(byte r, byte g, byte b) { return ((uint)r << 16) | ((uint)g << 8) | b; }

        static bool Ignored(PixelRect[] ignore, int x, int y)
        {
            if (ignore == null) return false;
            for (int i = 0; i < ignore.Length; i++) if (ignore[i].Contains(x, y)) return true;
            return false;
        }

        /// <summary>The distinct colours of the sky-only reference frame, outside the ignored rectangles.</summary>
        public static HashSet<uint> BackgroundColours(uint[] sky, int w, int h, PixelRect[] ignore)
        {
            var set = new HashSet<uint>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (!Ignored(ignore, x, y)) set.Add(sky[y * w + x]);
            return set;
        }

        /// <summary>Share of the non-ignored pixels whose colour is not in <paramref name="background"/>; also reports the frame's distinct colour count.</summary>
        public static double NonBackgroundShare(uint[] frame, int w, int h, ISet<uint> background, PixelRect[] ignore, out int distinctColours, out double dominantShare)
        {
            long total = 0, non = 0; var seen = new Dictionary<uint, long>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (Ignored(ignore, x, y)) continue;
                    uint c = frame[y * w + x]; total++;
                    seen.TryGetValue(c, out long n); seen[c] = n + 1;
                    if (!background.Contains(c)) non++;
                }
            distinctColours = seen.Count;
            long top = 0; foreach (var kv in seen) if (kv.Value > top) top = kv.Value;
            dominantShare = total == 0 ? 1.0 : (double)top / total;
            return total == 0 ? 0.0 : (double)non / total;
        }

        /// <summary>Share of the non-ignored pixels that differ between two captures.</summary>
        public static double DiffShare(uint[] a, uint[] b, int w, int h, PixelRect[] ignore)
        {
            long total = 0, diff = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (Ignored(ignore, x, y)) continue;
                    total++; if (a[y * w + x] != b[y * w + x]) diff++;
                }
            return total == 0 ? 0.0 : (double)diff / total;
        }

        /// <summary>The verdict over a sky-only reference and the timed captures: every share >= threshold and every pairwise difference > 0.</summary>
        public static PixelCheckVerdict Evaluate(uint[] sky, IReadOnlyList<uint[]> frames, int w, int h, PixelRect[] ignore, double threshold)
        {
            var bg = BackgroundColours(sky, w, h, ignore);
            int n = frames.Count;
            var v = new PixelCheckVerdict { Threshold = threshold, BackgroundColours = bg.Count, NonBackgroundShare = new double[n], DistinctColours = new int[n], PairDiffShare = new double[n, n], DominantShare = new double[n], MinDistinct = int.MaxValue };
            v.MinShare = n == 0 ? 0.0 : double.MaxValue; v.MinPairDiffShare = n < 2 ? 0.0 : double.MaxValue;
            for (int i = 0; i < n; i++)
            {
                v.NonBackgroundShare[i] = NonBackgroundShare(frames[i], w, h, bg, ignore, out v.DistinctColours[i], out v.DominantShare[i]);
                if (v.NonBackgroundShare[i] < v.MinShare) v.MinShare = v.NonBackgroundShare[i];
                if (v.DistinctColours[i] < v.MinDistinct) v.MinDistinct = v.DistinctColours[i];
                if (v.DominantShare[i] > v.MaxDominant) v.MaxDominant = v.DominantShare[i];
            }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    double d = DiffShare(frames[i], frames[j], w, h, ignore);
                    v.PairDiffShare[i, j] = v.PairDiffShare[j, i] = d;
                    if (d < v.MinPairDiffShare) v.MinPairDiffShare = d;
                }
            if (n == 0) v.MinDistinct = 0;
            v.SharesPass = n > 0 && v.MinShare >= threshold;
            v.RichnessPass = n > 0 && v.MinDistinct >= MinDistinctColours && v.MaxDominant <= MaxDominantShare;
            v.PairsPass = n >= 2 && v.MinPairDiffShare > 0.0;
            v.Pass = v.SharesPass && v.RichnessPass && v.PairsPass;
            return v;
        }
    }
}
