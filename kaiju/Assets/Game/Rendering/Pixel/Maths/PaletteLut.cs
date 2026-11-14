using System;
using System.Collections.Generic;

namespace Game.Rendering.Pixel
{
    /// <summary>Inputs of a LUT bake: everything comes from a PaletteProfile, nothing is hard-coded.</summary>
    public sealed class PaletteLutInput
    {
        /// <summary>The profile's colour list in declared order (index = palette index).</summary>
        public IReadOnlyList<Rgb8> Colours;
        /// <summary>The haze ramp from near-dark to far-light (R-002 ramp_uniform_L); scene value 0..1 maps along it.</summary>
        public IReadOnlyList<Rgb8> ValueRamp;
        /// <summary>Index of the accent colour in <see cref="Colours"/>, or -1.</summary>
        public int AccentIndex = -1;
        /// <summary>LUT edge length (cells per channel); R-003 recommends 32.</summary>
        public int Size = 32;
        /// <summary>Ramp position = clamp(ValueBias + ValueScale * pow(L* / 100, ValueGamma), 0, 1).</summary>
        public double ValueScale = 1.0, ValueBias = 0.0, ValueGamma = 1.0;
        /// <summary>Scene chroma (CIELAB C*) at which a colour starts and finishes being pulled to the accent.</summary>
        public double AccentChromaStart = 25.0, AccentChromaFull = 60.0;
        /// <summary>Penalty on mixing distant colours, as a fraction of their OKLab distance (Yliluoma).</summary>
        public double PairPenalty = 0.25;
        /// <summary>How many nearest palette colours are considered as mix partners.</summary>
        public int Candidates = 5;
        /// <summary>Weight of the OKLab a, b axes against L in every distance (1 = plain OKLab). Above 1, an entry of the right hue beats an off-hue entry of slightly closer lightness.</summary>
        public double ChromaWeight = 1.0;
        /// <summary>
        /// Value ladder for neutral scene tones. When LadderMinStepL &gt; 0, neutral cells (scene chroma below
        /// AccentChromaStart) may only use the ladder: the palette entries whose hue lies on the value ramp (a*, b* within
        /// LadderHueTolerance of the ramp colour of the same lightness), thinned so that two rungs are at least
        /// LadderMinStepL apart in L*, and only neighbouring rungs are mixed. With a ladder the grade maps scene value
        /// linearly onto the rungs (value 0 = bottom rung, 1 = top rung) instead of onto the uniform ramp, so equal
        /// steps of haze move a surface one rung at a time: two haze bands differ by a visible value step, and a seam
        /// never mixes colours of another hue.
        /// </summary>
        public double LadderMinStepL = 0.0, LadderHueTolerance = 12.0;
        /// <summary>Lightness (L*) of the ladder's top rung; 0 = the light end of the value ramp. A higher value extends the ladder to a lighter on-hue entry (more value range for a low-contrast palette).</summary>
        public double LadderTopL = 0.0;
    }

    /// <summary>
    /// Bakes the palette LUT (R-003 §3–4). Each cell of the size^3 table, addressed by an sRGB-encoded scene colour,
    /// holds two palette indices and a mix ratio: the scene colour is first graded onto the profile's value ramp
    /// (and pulled to the accent when saturated), then the best pair of palette colours and their linear-light mix
    /// ratio are found in OKLab. The runtime picks one of the two by the dither threshold, so every output pixel is an
    /// exact palette colour. Layout: 4 bytes per cell (index a, index b, ratio of b * 255, 255); cell (r, g, b) is at
    /// ((b * size + g) * size + r) * 4. Index a is always the darker colour so the pattern never inverts between cells.
    /// </summary>
    public static class PaletteLutBaker
    {
        public const int BytesPerCell = 4;

        public static byte[] Bake(PaletteLutInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Colours == null || input.Colours.Count == 0) throw new ArgumentException("palette is empty");
            if (input.Colours.Count > 255) throw new ArgumentException("at most 255 palette colours (index 255 means not quantised)");
            if (input.ValueRamp == null || input.ValueRamp.Count < 2) throw new ArgumentException("value ramp needs at least two colours");
            if (input.Size < 2 || input.Size > 64) throw new ArgumentOutOfRangeException(nameof(input.Size));

            int n = input.Colours.Count, size = input.Size;
            var linear = new Vec3d[n]; var ok = new Vec3d[n]; var lab = new Vec3d[n];
            for (int i = 0; i < n; i++)
            {
                linear[i] = ColourMath.ToLinear(input.Colours[i]);
                ok[i] = Weighted(ColourMath.LinearToOklab(linear[i]), input.ChromaWeight);
                lab[i] = ColourMath.LinearToLab(linear[i]);
            }
            var rampLab = ColourMath.ToLab(input.ValueRamp);
            bool hasAccent = input.AccentIndex >= 0 && input.AccentIndex < n;
            int k = Math.Min(Math.Max(1, input.Candidates), n);
            var candidates = new int[k]; var candidateDistance = new double[k];
            int[] ladder = Ladder(input, lab, rampLab);            // palette indices of the rungs, dark to light (empty = no ladder)
            var rung = new int[n]; for (int i = 0; i < n; i++) rung[i] = -1;
            for (int i = 0; i < ladder.Length; i++) rung[ladder[i]] = i;
            if (ladder.Length >= 2)
            {
                // The grade follows the rungs: scene value 0..1 runs linearly over the rung colours.
                var rungLab = new Vec3d[ladder.Length];
                for (int i = 0; i < ladder.Length; i++) rungLab[i] = lab[ladder[i]];
                rampLab = rungLab;
            }

            var lut = new byte[size * size * size * BytesPerCell];
            for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                    for (int r = 0; r < size; r++)
                    {
                        var srgb = new Vec3d((r + 0.5) / size, (g + 0.5) / size, (b + 0.5) / size);
                        Vec3d sceneLab = ColourMath.LinearToLab(ColourMath.SrgbToLinear(srgb));
                        bool neutral = ladder.Length >= 2 && Math.Sqrt(sceneLab.Y * sceneLab.Y + sceneLab.Z * sceneLab.Z) < input.AccentChromaStart;
                        Vec3d targetLab = Grade(sceneLab, rampLab, hasAccent ? lab[input.AccentIndex] : default, hasAccent, input);
                        Vec3d targetLinear = ColourMath.LabToLinear(targetLab);
                        Vec3d targetOk = Weighted(ColourMath.LinearToOklab(new Vec3d(ColourMath.Clamp01(targetLinear.X), ColourMath.Clamp01(targetLinear.Y), ColourMath.Clamp01(targetLinear.Z))), input.ChromaWeight);

                        NearestK(ok, targetOk, candidates, candidateDistance, neutral ? rung : null);
                        int bestA = candidates[0], bestB = candidates[0]; double bestT = 0, bestErr = candidateDistance[0];
                        for (int i = 0; i < k; i++)
                            for (int j = i + 1; j < k; j++)
                            {
                                int ia = candidates[i], ib = candidates[j];
                                if (ia < 0 || ib < 0) continue;
                                if (neutral && Math.Abs(rung[ia] - rung[ib]) != 1) continue;   // only neighbouring rungs mix
                                double t = ProjectRatio(linear[ia], linear[ib], targetLinear);
                                if (t <= 0 || t >= 1) continue;
                                double err = Vec3d.Distance(Weighted(ColourMath.LinearToOklab(Vec3d.Lerp(linear[ia], linear[ib], t)), input.ChromaWeight), targetOk)
                                             + input.PairPenalty * Vec3d.Distance(ok[ia], ok[ib]);
                                if (err < bestErr) { bestErr = err; bestA = ia; bestB = ib; bestT = t; }
                            }
                        // Darker colour first (ties broken by index) so the ratio always means "share of the lighter colour".
                        if (bestA != bestB && (lab[bestB].X < lab[bestA].X || (lab[bestB].X == lab[bestA].X && bestB < bestA)))
                        { int tmp = bestA; bestA = bestB; bestB = tmp; bestT = 1.0 - bestT; }
                        int o = ((b * size + g) * size + r) * BytesPerCell;
                        lut[o] = (byte)bestA; lut[o + 1] = (byte)bestB;
                        lut[o + 2] = (byte)Math.Min(255, Math.Max(0, (int)Math.Floor(bestT * 255.0 + 0.5)));
                        lut[o + 3] = 255;
                    }
            return lut;
        }

        static Vec3d Weighted(Vec3d oklab, double chromaWeight) => new Vec3d(oklab.X, oklab.Y * chromaWeight, oklab.Z * chromaWeight);

        /// <summary>The graded target of a scene colour in CIELAB: its place on the value ramp, pulled to the accent by chroma.</summary>
        public static Vec3d Grade(Vec3d sceneLab, IReadOnlyList<Vec3d> rampLab, Vec3d accentLab, bool hasAccent, PaletteLutInput input)
        {
            double v = ColourMath.Clamp01(sceneLab.X / 100.0);
            double p = ColourMath.Clamp01(input.ValueBias + input.ValueScale * Math.Pow(v, input.ValueGamma));
            Vec3d target = SampleRamp(rampLab, p);
            if (hasAccent)
            {
                double chroma = Math.Sqrt(sceneLab.Y * sceneLab.Y + sceneLab.Z * sceneLab.Z);
                double w = ColourMath.Clamp01((chroma - input.AccentChromaStart) / Math.Max(1e-6, input.AccentChromaFull - input.AccentChromaStart));
                w = w * w * (3 - 2 * w);
                target = Vec3d.Lerp(target, accentLab, w);
            }
            return target;
        }

        public static Vec3d SampleRamp(IReadOnlyList<Vec3d> ramp, double p)
        {
            double f = ColourMath.Clamp01(p) * (ramp.Count - 1);
            int i = Math.Min((int)Math.Floor(f), ramp.Count - 2);
            return Vec3d.Lerp(ramp[i], ramp[i + 1], f - i);
        }

        /// <summary>Ratio t in [0, 1] of the point on segment a..b closest to <paramref name="c"/> (linear light).</summary>
        static double ProjectRatio(Vec3d a, Vec3d b, Vec3d c)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double len2 = dx * dx + dy * dy + dz * dz;
            if (len2 <= 0) return 0;
            return ColourMath.Clamp01(((c.X - a.X) * dx + (c.Y - a.Y) * dy + (c.Z - a.Z) * dz) / len2);
        }

        /// <summary>The rungs of the value ladder as palette indices from dark to light; empty when the ladder is off.</summary>
        public static int[] Ladder(PaletteLutInput input, IReadOnlyList<Vec3d> paletteLab, IReadOnlyList<Vec3d> rampLab)
        {
            if (!(input.LadderMinStepL > 0)) return new int[0];
            var onHue = new List<int>();
            for (int i = 0; i < paletteLab.Count; i++)
            {
                if (i == input.AccentIndex) continue;
                // The ramp colour of the same lightness (the ramp is monotone in L*); beyond its ends, the end colour.
                Vec3d c = paletteLab[i], onRamp = rampLab[0];
                if (c.X >= rampLab[rampLab.Count - 1].X) onRamp = rampLab[rampLab.Count - 1];
                else
                    for (int j = 0; j + 1 < rampLab.Count; j++)
                        if (c.X >= rampLab[j].X && c.X <= rampLab[j + 1].X)
                        { double t = (c.X - rampLab[j].X) / Math.Max(1e-9, rampLab[j + 1].X - rampLab[j].X); onRamp = Vec3d.Lerp(rampLab[j], rampLab[j + 1], t); break; }
                double da = c.Y - onRamp.Y, db = c.Z - onRamp.Z;
                if (Math.Sqrt(da * da + db * db) <= input.LadderHueTolerance) onHue.Add(i);
            }
            if (onHue.Count < 2) return new int[0];
            // Ends: the entries nearest to the dark end of the ramp and to the top lightness (the ramp's light end unless LadderTopL says otherwise).
            double topL = input.LadderTopL > 0 ? input.LadderTopL : rampLab[rampLab.Count - 1].X;
            int bottom = onHue[0], top = onHue[0];
            foreach (int i in onHue)
            {
                if (Math.Abs(paletteLab[i].X - rampLab[0].X) < Math.Abs(paletteLab[bottom].X - rampLab[0].X)) bottom = i;
                if (Math.Abs(paletteLab[i].X - topL) < Math.Abs(paletteLab[top].X - topL)) top = i;
            }
            onHue.Sort((x, y) => { int cmp = paletteLab[x].X.CompareTo(paletteLab[y].X); return cmp != 0 ? cmp : x.CompareTo(y); });
            var rungs = new List<int> { bottom };
            foreach (int i in onHue)
            {
                double l = paletteLab[i].X;
                if (l <= paletteLab[bottom].X || l >= paletteLab[top].X) continue;
                if (l - paletteLab[rungs[rungs.Count - 1]].X >= input.LadderMinStepL && paletteLab[top].X - l >= input.LadderMinStepL) rungs.Add(i);
            }
            if (top != bottom) rungs.Add(top);
            return rungs.ToArray();
        }

        static void NearestK(Vec3d[] ok, Vec3d target, int[] indices, double[] distances, int[] rung = null)
        {
            int k = indices.Length;
            for (int i = 0; i < k; i++) { indices[i] = -1; distances[i] = double.MaxValue; }
            for (int c = 0; c < ok.Length; c++)
            {
                if (rung != null && rung[c] < 0) continue;   // neutral cells use the ladder only
                double d = Vec3d.Distance(ok[c], target);
                int pos = k;
                while (pos > 0 && d < distances[pos - 1]) pos--;   // strict: the earlier index wins ties
                if (pos >= k) continue;
                for (int m = k - 1; m > pos; m--) { indices[m] = indices[m - 1]; distances[m] = distances[m - 1]; }
                indices[pos] = c; distances[pos] = d;
            }
        }

        /// <summary>Reads a LUT cell the way the shader does: cell = min(floor(srgb * size), size - 1).</summary>
        public static void Lookup(byte[] lut, int size, double sr, double sg, double sb, out int indexA, out int indexB, out double ratioB)
        {
            int r = Cell(sr, size), g = Cell(sg, size), b = Cell(sb, size);
            int o = ((b * size + g) * size + r) * BytesPerCell;
            indexA = lut[o]; indexB = lut[o + 1]; ratioB = lut[o + 2] / 255.0;
        }

        static int Cell(double v, int size) => Math.Min(size - 1, Math.Max(0, (int)Math.Floor(ColourMath.Clamp01(v) * size)));

        /// <summary>The index the quantiser outputs for a cell: b when threshold &lt; shaped ratio, else a (PixelQuantise.shader).</summary>
        public static int Select(int indexA, int indexB, double ratioB, double threshold, double spread)
        {
            double shaped = spread <= 1e-6 ? (ratioB > 0.5 ? 1.0 : 0.0) : ColourMath.Clamp01((ratioB - 0.5) / spread + 0.5);
            return threshold < shaped ? indexB : indexA;
        }
    }
}
