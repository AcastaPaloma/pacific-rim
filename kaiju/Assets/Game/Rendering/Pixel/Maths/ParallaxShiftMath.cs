using System;
using System.Collections.Generic;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Maths of the sub-pixel present pass for translation off the reference plane (R-131 section 4, T-176 C11).
    /// Units: texels at the reference plane (one snap grid = 1 texel at the reference depth); rho = zRef / z (1 on the reference plane);
    /// k output pixels per texel. The rig renders from a snapped position S and the present pass shifts the image by whole output pixels.
    /// </summary>
    public static class ParallaxShiftMath
    {
        /// <summary>Whole-output-pixel shift of a surface of depth ratio rho: round(k * rho * remainder). rho = 1 is the uniform shift of the current pass.</summary>
        public static int DepthShiftPx(double rho, double remainderTexels, int scale) => (int)Math.Round(scale * rho * remainderTexels, MidpointRounding.ToEven);

        /// <summary>Snaps a camera coordinate up to a multiple of a layer's grid (ceil, so the remainder is non-positive); gridM must be positive.</summary>
        public static double LayerSnap(double position, double layerGridM)
        {
            if (!(layerGridM > 0)) throw new ArgumentOutOfRangeException(nameof(layerGridM));
            return Math.Ceiling(position / layerGridM) * layerGridM;
        }

        /// <summary>Grid (m) of a layer: one texel at the layer's reference depth, texelM(zRef) * zLayer / zRef.</summary>
        public static double LayerGridM(double referenceTexelM, double referenceDepthM, double layerDepthM) => referenceTexelM * layerDepthM / referenceDepthM;

        public enum Scheme { Baseline, FinerSnap, DepthShift, LayerSnap }

        public const double RefDepthM = 664.874;
        public const double NearDepthM = 124.2, ReferenceClassDepthM = 663.0, FarDepthM = 10353.0;
        public const int K = 4;
        const double Speed = 0.0902, Start = 0.6153;
        const int Frames = 180, Phases = 16;

        /// <summary>Output x of the rendered edge minus the commanded motion, over the T1 translation sequence; max over frames for one edge phase.</summary>
        static double MaxError(Scheme scheme, double rho, double rhoLayer, int fine, double phi)
        {
            double x0 = 0, c0 = 0, worst = 0;
            for (int i = 0; i < Frames; i++)
            {
                double c = Speed * i + Start, S, u; double shift;
                switch (scheme)
                {
                    case Scheme.FinerSnap: S = Math.Ceiling(c * fine) / fine; u = rho * (0 - S) + phi; shift = Math.Round(K * (c - S), MidpointRounding.ToEven); break;
                    case Scheme.DepthShift: S = Math.Ceiling(c); u = rho * (0 - S) + phi; shift = DepthShiftPx(rho, c - S, K); break;
                    case Scheme.LayerSnap: S = LayerSnap(c, 1.0 / rhoLayer); u = rho * (0 - S) + phi; shift = DepthShiftPx(rhoLayer, c - S, K); break;
                    default: S = Math.Ceiling(c); u = rho * (0 - S) + phi; shift = Math.Round(K * (c - S), MidpointRounding.ToEven); break;
                }
                // numpy rounds half to even, like MidpointRounding.ToEven.
                double x = K * Math.Round(u, MidpointRounding.ToEven) - shift, cmd = K * rho * (0 - c);
                if (i == 0) { x0 = x; c0 = cmd; }
                worst = Math.Max(worst, Math.Abs((x - x0) - (cmd - c0)));
            }
            return worst;
        }

        /// <summary>Worst and median (over 16 edge phases) of the max error in output pixels for a surface at depth z; rhoLayer is the layer's reference ratio (LayerSnap only).</summary>
        public static void Simulate(Scheme scheme, double surfaceDepthM, double layerDepthM, out double worstPx, out double medianPx, int fine = 1)
        {
            double rho = RefDepthM / surfaceDepthM, rhoLayer = RefDepthM / layerDepthM;
            var e = new List<double>();
            for (int p = 0; p < Phases; p++) e.Add(MaxError(scheme, rho, rhoLayer, fine, (p + 0.37) / Phases));
            e.Sort();
            worstPx = e[e.Count - 1]; medianPx = 0.5 * (e[Phases / 2 - 1] + e[Phases / 2]);
        }

        /// <summary>The worst figure over every surface depth in [zLo, zHi] (log spaced, 90 samples) when N log-spaced layers cover the range: the any-depth number R-131 4.2 reports beside the gate.</summary>
        public static double AnyDepthWorstPx(int layers, double zLo = 30, double zHi = 30000)
        {
            var rl = new double[layers];
            for (int i = 0; i < layers; i++) rl[i] = RefDepthM / Math.Exp(Math.Log(zLo) + (Math.Log(zHi) - Math.Log(zLo)) * (layers == 1 ? 0.5 : i / (double)(layers - 1)));
            double worst = 0;
            for (int s = 0; s < 90; s++)
            {
                double z = Math.Exp(Math.Log(zLo) + (Math.Log(zHi) - Math.Log(zLo)) * s / 89.0), rho = RefDepthM / z, best = rl[0];
                foreach (double r in rl) if (Math.Abs(Math.Log(r / rho)) < Math.Abs(Math.Log(best / rho))) best = r;
                Simulate(Scheme.LayerSnap, z, RefDepthM / best, out double w, out _);
                worst = Math.Max(worst, w);
            }
            return worst;
        }
    }
}
