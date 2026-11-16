using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// T-196 (flag <c>rendering.pixel.layerSharedPhase</c>, ships false): present shifts of the three layer slabs on ONE output-pixel lattice, so the layered
    /// frame is a single k x k block grid at one declared offset (M-003.d-iv). Every slab's shift is congruent to one shared phase p modulo k; the slabs differ
    /// only by whole texels. Per axis the phase is chosen each frame to minimise the largest deviation of any slab from its own ideal shift
    /// (remainder * k, real), measured against that slab's deviation on the first frame after a cut (so a constant per-shot offset costs nothing).
    /// <para>Bound (MODULE.md "Shared grid phase", evidence/T-196): when two slabs' ideal shifts drift apart (lateral translation, two reference depths) their
    /// difference sweeps every residue mod k; at a difference of k/2 with a half-pixel fraction no integer phase does better than 1.5 output px for one of them.
    /// So this lattice costs C11 at least 1.5 px against the 1 px gate; it is a measured option for the style panel, not a pass.</para>
    /// Engine-free and allocation-free per frame; the binder resolves once per prepared frame.
    /// </summary>
    public sealed class PixelSharedPhase
    {
        readonly double[] m_TauX = new double[PixelLayerPlan.Count], m_TauY = new double[PixelLayerPlan.Count];
        readonly double[] m_IdealX = new double[PixelLayerPlan.Count], m_IdealY = new double[PixelLayerPlan.Count];
        readonly int[] m_Out = new int[PixelLayerPlan.Count];
        bool m_Started;

        /// <summary>Largest |deviation| (output px) of any slab from its target on the last resolved frame, per axis.</summary>
        public double LastWorstX { get; private set; }
        public double LastWorstY { get; private set; }
        public bool Started => m_Started;

        /// <summary>Forgets the per-shot reference (next frame is treated as a cut).</summary>
        public void Reset() { m_Started = false; LastWorstX = LastWorstY = 0; }

        /// <summary>
        /// Resolves one frame. <paramref name="hints"/> carry the three layers (validated by the caller); <paramref name="cut"/> restarts the per-shot reference;
        /// <paramref name="limitPx"/> is the guard band in output pixels. Writes near / main / far as x, y pairs into <paramref name="shiftsXY"/> (length 6).
        /// </summary>
        public void Resolve(in PixelStabilisationHints hints, int scale, int limitPx, bool cut, int[] shiftsXY)
        {
            if (shiftsXY == null || shiftsXY.Length != 2 * PixelLayerPlan.Count) throw new ArgumentException("need three x, y shift pairs", nameof(shiftsXY));
            if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));
            for (int i = 0; i < PixelLayerPlan.Count; i++)
            {
                PixelLayerHint l = PixelLayerHints.Get(hints, i);
                m_IdealX[i] = l.SubTexelX * (double)scale; m_IdealY[i] = l.SubTexelY * (double)scale;
            }
            if (cut || !m_Started)
            {
                Start(m_IdealX, m_TauX, scale, limitPx);
                Start(m_IdealY, m_TauY, scale, limitPx);
                m_Started = true;
            }
            LastWorstX = Axis(m_IdealX, m_TauX, scale, limitPx, m_Out);
            for (int i = 0; i < PixelLayerPlan.Count; i++) shiftsXY[2 * i] = m_Out[i];
            LastWorstY = Axis(m_IdealY, m_TauY, scale, limitPx, m_Out);
            for (int i = 0; i < PixelLayerPlan.Count; i++) shiftsXY[2 * i + 1] = m_Out[i];
        }

        /// <summary>First frame of a shot: the phase that minimises the largest absolute deviation; each slab's deviation then becomes its per-shot reference.</summary>
        void Start(double[] ideal, double[] tau, int k, int limit)
        {
            for (int i = 0; i < tau.Length; i++) tau[i] = 0;
            Axis(ideal, tau, k, limit, m_Out);
            for (int i = 0; i < tau.Length; i++) tau[i] = ideal[i] - m_Out[i];
        }

        /// <summary>
        /// One axis: over every integer phase p in [-limit, limit], each slab takes the shift congruent to p (mod k) nearest its target ideal - tau inside the guard band;
        /// the phase with the smallest largest deviation wins (ties: smaller sum of squares, then the phase nearest the main slab's rounded target, then the smaller p).
        /// Returns the winning largest deviation.
        /// </summary>
        static double Axis(double[] ideal, double[] tau, int k, int limit, int[] outShift)
        {
            double bestMax = double.PositiveInfinity, bestSq = double.PositiveInfinity; int bestP = 0, bestTie = int.MaxValue;
            int mainRound = (int)Math.Round(ideal[PixelLayerPlan.Main] - tau[PixelLayerPlan.Main], MidpointRounding.ToEven);
            int lo = -Math.Max(limit, k - 1), hi = Math.Max(limit, k - 1);
            for (int p = lo; p <= hi; p++)
            {
                double mx = 0, sq = 0;
                for (int i = 0; i < ideal.Length; i++)
                {
                    double target = ideal[i] - tau[i];
                    double e = Math.Abs(Nearest(target, p, k, limit) - target);
                    mx = Math.Max(mx, e); sq += e * e;
                }
                int tie = Math.Abs(p - mainRound);
                if (mx < bestMax - 1e-9 || (Math.Abs(mx - bestMax) <= 1e-9 && (sq < bestSq - 1e-9 || (Math.Abs(sq - bestSq) <= 1e-9 && tie < bestTie))))
                { bestMax = mx; bestSq = sq; bestP = p; bestTie = tie; }
            }
            for (int i = 0; i < ideal.Length; i++) outShift[i] = Nearest(ideal[i] - tau[i], bestP, k, limit);
            return bestMax;
        }

        /// <summary>The integer congruent to p modulo k nearest to target, inside [-limit, limit] when such a value exists (else the nearest one).</summary>
        public static int Nearest(double target, int p, int k, int limit)
        {
            int m = (int)Math.Round((target - p) / k, MidpointRounding.ToEven);
            int s = p + k * m;
            while (s > limit && s - k >= -limit) s -= k;
            while (s < -limit && s + k <= limit) s += k;
            return s;
        }

        /// <summary>Shift value modulo k in [0, k).</summary>
        public static int Mod(int v, int k) => ((v % k) + k) % k;
    }
}
