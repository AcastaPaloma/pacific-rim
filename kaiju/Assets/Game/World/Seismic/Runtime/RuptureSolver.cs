// Rupture on the mapped trace + emergence vertex (R-022 sections 1, 2, 4; T-027 step 2). Pure and deterministic: the same fault system,
// scenario and district references give the same result. Every rupture vertex is a vertex of the fault system polyline (M-053 distance 0).
// Slip is zero on bridged segments (they are not mapped trace). If the system is shorter than the scaled SRL, Mw is reduced via
// RuptureScaling.MwFromSrlKm; the trace is never stretched.
using System;
using Game.Core;

namespace Game.World.Seismic
{
    /// <summary>Reference positions of the two districts in BayTM metres. Inputs: the Geo district boundaries are not read here; a point stands for each district (Assumed).</summary>
    public struct DistrictRefs
    {
        public double GGX, GGZ, SoMaX, SoMaZ;
        /// <summary>true: the emergence vertex is the one nearest the GG reference; false: nearest the SoMa reference.</summary>
        public bool TargetGG;
    }

    public sealed class RuptureResult
    {
        public FaultSystem System;
        public uint EventId, Seed;
        public double MwRequested, Mw;
        /// <summary>True when the system was shorter than the scaled SRL and Mw was reduced.</summary>
        public bool ShortenedToSystem;
        public int HypoVertex, VertexStart, VertexEnd;
        public ushort SectionIdx;
        public SlipSense SlipSense;
        /// <summary>Arc length of the ruptured vertex range, km (what M-054 uses).</summary>
        public double SrlKm;
        public double MdM, AdM;
        public double StartS, EndS, RuptureSpeedMs;
        /// <summary>Slip, m, per vertex of [VertexStart, VertexEnd] (index 0 = VertexStart). Bridged segments carry no slip (zero in the mean), whatever the end vertices hold.</summary>
        public double[] Slip;
        /// <summary>Mean slip over the mapped length of the range (bridges count as zero), m; equals AdM within 1 %.</summary>
        public double MeanSlipM;
        public double PeakSlipM;

        /// <summary>Seconds after the origin at which the rupture front reaches the vertex (bilateral from the hypocentre vertex).</summary>
        /// <summary>
        /// Slip carried by segment <paramref name="segment"/> (vertex segment to segment + 1), m: 0 on a bridged gap and outside the ruptured range,
        /// else the mean of its end vertices. Use this (or MeanSlipM), not Slip[], for anything that needs offset on the ground: Slip[] keeps the
        /// profile value at a mapped chain end that touches a bridge (that vertex is mapped trace; the bridge beside it is not). DEF-M1ds-4.
        /// </summary>
        public double SegmentSlip(int segment)
        {
            if (segment < VertexStart || segment >= VertexEnd || System.Bridge[segment]) return 0.0;
            return 0.5 * (Slip[segment - VertexStart] + Slip[segment - VertexStart + 1]);
        }

        public double VertexTimeS(int vertex) { return Math.Abs(System.ArcM[vertex] - System.ArcM[HypoVertex]) / RuptureSpeedMs; }
    }

    public struct EmergenceChoice
    {
        public bool Found;
        public int VertexIdx;
        public double X, Z, DistToGGm, DistToSoMaM, TimeS;
    }

    public static class RuptureSolver
    {
        /// <summary>Peak-to-noise amplitude of the seeded slip variation (Assumed).</summary>
        public const double SlipNoiseAmplitude = 0.15;

        public static RuptureResult Solve(FaultSystem sys, QuakeScenario sc)
        {
            double mwReq = sc.Mw > 0 ? sc.Mw : SeismicAssumptions.DefaultMw;
            double speed = sc.RuptureSpeedMs > 0 ? sc.RuptureSpeedMs : SeismicAssumptions.RuptureSpeedMs;
            double mw = mwReq;
            RuptureSize size = RuptureScaling.SizeFromMw(mw);
            double srlKm = sc.SrlKmOverride > 0 ? sc.SrlKmOverride : size.SrlKm;
            double sysKm = sys.LengthM / 1000.0;
            bool shortened = false;
            if (srlKm > sysKm)
            {
                srlKm = sysKm; mw = RuptureScaling.MwFromSrlKm(sysKm); shortened = true;
                size = RuptureScaling.SizeFromMw(mw);
            }
            double lenM = srlKm * 1000.0;

            int h = NearestVertex(sys, sc.HypoX, sc.HypoZ);
            double total = sys.LengthM;
            double a = sys.ArcM[h] - lenM / 2.0;
            if (a < 0) a = 0;
            if (a + lenM > total) a = Math.Max(0, total - lenM);
            int start = NearestArc(sys, a);
            int end = NearestArc(sys, sys.ArcM[start] + lenM);
            if (end <= start) end = Math.Min(sys.VertexCount - 1, start + 1);
            if (h < start) start = h;
            if (h > end) end = h;

            var r = new RuptureResult
            {
                System = sys, EventId = sc.EventId, Seed = sc.Seed, MwRequested = mwReq, Mw = mw, ShortenedToSystem = shortened,
                HypoVertex = h, VertexStart = start, VertexEnd = end, SectionIdx = sys.SectionOfVertex[h],
                SlipSense = sc.SlipSense != 0 ? (SlipSense)sc.SlipSense : sys.SlipSense,
                SrlKm = sys.ArcBetweenM(start, end) / 1000.0,
                MdM = size.MdM, AdM = size.AdM, RuptureSpeedMs = speed, StartS = 0,
            };
            r.EndS = Math.Max(sys.ArcM[h] - sys.ArcM[start], sys.ArcM[end] - sys.ArcM[h]) / speed;
            r.Slip = SlipProfile(sys, start, end, r.MdM, r.AdM, sc.Seed);
            r.PeakSlipM = 0; for (int i = 0; i < r.Slip.Length; i++) r.PeakSlipM = Math.Max(r.PeakSlipM, r.Slip[i]);
            r.MeanSlipM = MeanSlip(sys, start, r.Slip);
            return r;
        }

        /// <summary>Emergence vertex: inside the ruptured interval, not touching a bridged segment, nearest the target district reference (ties: lowest index).</summary>
        public static EmergenceChoice ChooseEmergence(RuptureResult r, DistrictRefs d)
        {
            FaultSystem sys = r.System;
            double tx = d.TargetGG ? d.GGX : d.SoMaX, tz = d.TargetGG ? d.GGZ : d.SoMaZ;
            int best = -1; double bd = double.MaxValue;
            for (int v = r.VertexStart; v <= r.VertexEnd; v++)
            {
                if (sys.TouchesBridge(v)) continue;
                double dd = Dist(sys.X[v], sys.Z[v], tx, tz);
                if (dd < bd) { bd = dd; best = v; }
            }
            var c = new EmergenceChoice();
            if (best < 0) return c;
            c.Found = true; c.VertexIdx = best; c.X = sys.X[best]; c.Z = sys.Z[best];
            c.DistToGGm = Dist(c.X, c.Z, d.GGX, d.GGZ); c.DistToSoMaM = Dist(c.X, c.Z, d.SoMaX, d.SoMaZ);
            c.TimeS = r.EndS + SeismicAssumptions.ShakingAfterRuptureS + SeismicAssumptions.EmergenceAfterShakingS;
            return c;
        }

        /// <summary>
        /// Elliptical taper (1 - u^2)^p over the range (u in [-1,1] by arc), times seeded smooth variation, scaled so the peak is mdM; p is solved so
        /// the arc-weighted mean (bridged segments count zero) equals adM. If adM/mdM is above what the flattest shape reaches, the flattest shape is used.
        /// Regenerable from (system, range, mdM, adM, seed).
        /// </summary>
        public static double[] SlipProfile(FaultSystem sys, int start, int end, double mdM, double adM, uint seed)
        {
            int n = end - start + 1;
            var u = new double[n]; var noise = new double[n];
            double s0 = sys.ArcM[start], len = sys.ArcM[end] - s0;
            ulong st = 0x9E3779B97F4A7C15UL ^ seed;
            double[] ph = new double[3];
            for (int k = 0; k < 3; k++) ph[k] = 2 * Math.PI * Next01(ref st);
            for (int i = 0; i < n; i++)
            {
                double f = len > 0 ? (sys.ArcM[start + i] - s0) / len : 0.5;
                u[i] = 2 * f - 1;
                double w = 0.5 * Math.Sin(2 * Math.PI * 2 * f + ph[0]) + 0.3 * Math.Sin(2 * Math.PI * 5 * f + ph[1]) + 0.2 * Math.Sin(2 * Math.PI * 11 * f + ph[2]);
                noise[i] = 1 + SlipNoiseAmplitude * w;
            }
            double target = adM / mdM;
            double lo = 0, hi = 40;
            var g = new double[n];
            Shape(u, noise, lo, g); double rLo = Ratio(sys, start, g);
            double p;
            if (rLo <= target) p = lo;
            else
            {
                for (int it = 0; it < 100; it++)
                {
                    double mid = 0.5 * (lo + hi);
                    Shape(u, noise, mid, g);
                    if (Ratio(sys, start, g) > target) lo = mid; else hi = mid;
                }
                p = 0.5 * (lo + hi);
            }
            Shape(u, noise, p, g);
            double mx = 0; for (int i = 0; i < n; i++) mx = Math.Max(mx, g[i]);
            var slip = new double[n];
            for (int i = 0; i < n; i++) slip[i] = mdM * g[i] / mx;
            return slip;
        }

        static void Shape(double[] u, double[] noise, double p, double[] g)
        {
            for (int i = 0; i < u.Length; i++)
            {
                double e = 1 - u[i] * u[i]; if (e < 0) e = 0;
                g[i] = (p == 0 ? 1.0 : Math.Pow(e, p)) * noise[i];
            }
        }

        static double Ratio(FaultSystem sys, int start, double[] g)
        {
            double mx = 0; for (int i = 0; i < g.Length; i++) mx = Math.Max(mx, g[i]);
            return MeanSlip(sys, start, g) / mx;
        }

        /// <summary>Arc-weighted mean of the per-vertex slip over the range (trapezoid; bridged segments contribute zero, their length still counts).</summary>
        public static double MeanSlip(FaultSystem sys, int start, double[] slip)
        {
            double sum = 0, len = 0;
            for (int i = 0; i + 1 < slip.Length; i++)
            {
                double l = sys.ArcM[start + i + 1] - sys.ArcM[start + i];
                len += l;
                if (!sys.Bridge[start + i]) sum += l * 0.5 * (slip[i] + slip[i + 1]);
            }
            return len > 0 ? sum / len : 0;
        }

        static double Next01(ref ulong s)
        {
            s ^= s << 13; s ^= s >> 7; s ^= s << 17;   // xorshift64
            return (s >> 11) * (1.0 / 9007199254740992.0);
        }

        static int NearestVertex(FaultSystem sys, double x, double z)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < sys.VertexCount; i++) { double d = Dist(sys.X[i], sys.Z[i], x, z); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        static int NearestArc(FaultSystem sys, double arc)
        {
            int k = Array.BinarySearch(sys.ArcM, arc);
            if (k >= 0) return k;
            int hi = ~k;
            if (hi <= 0) return 0;
            if (hi >= sys.VertexCount) return sys.VertexCount - 1;
            return arc - sys.ArcM[hi - 1] <= sys.ArcM[hi] - arc ? hi - 1 : hi;
        }

        static double Dist(double x0, double z0, double x1, double z1) { double dx = x1 - x0, dz = z1 - z0; return Math.Sqrt(dx * dx + dz * dz); }
    }

    /// <summary>Receives the sequence events. The Core event bus adapts to this (step 3).</summary>
    public interface ISeismicEventSink
    {
        void Publish<T>(T e) where T : unmanaged, IEvent;
    }

    /// <summary>Runs the quake sequence: QuakeBegan, FaultRupture, EmergencePoint, in that order, once per EventId.</summary>
    public sealed class QuakeSequence
    {
        readonly FaultTraceSet _faults;
        readonly System.Collections.Generic.HashSet<uint> _done = new System.Collections.Generic.HashSet<uint>();
        public QuakeSequence(FaultTraceSet faults) { _faults = faults; }

        /// <summary>Returns the rupture result, or null when the EventId already ran or the FaultId is unknown (nothing is emitted then).</summary>
        public RuptureResult Run(QuakeScenario sc, DistrictRefs districts, ISeismicEventSink sink, double originTimeS = 0)
        {
            if (_done.Contains(sc.EventId)) return null;
            FaultSystem sys = _faults.FindSystem(sc.FaultId);
            if (sys == null) return null;
            _done.Add(sc.EventId);
            RuptureResult r = RuptureSolver.Solve(sys, sc);
            sink.Publish(new QuakeBegan
            {
                EventId = sc.EventId, Mw = (float)r.Mw, OriginTimeS = originTimeS, HypoX = sc.HypoX, HypoZ = sc.HypoZ,
                HypoDepthM = sc.HypoDepthM > 0 ? sc.HypoDepthM : SeismicAssumptions.HypoDepthM,
                FaultId = sys.FaultId, RuptureSpeedMs = (float)r.RuptureSpeedMs, Seed = sc.Seed,
            });
            sink.Publish(new FaultRupture
            {
                EventId = sc.EventId, FaultId = sys.FaultId, SectionIdx = r.SectionIdx, VertexStart = r.VertexStart, VertexEnd = r.VertexEnd,
                SrlKm = (float)r.SrlKm, MdM = (float)r.MdM, AdM = (float)r.AdM, SlipSense = (byte)r.SlipSense,
                StartS = (float)r.StartS, EndS = (float)r.EndS,
            });
            EmergenceChoice c = RuptureSolver.ChooseEmergence(r, districts);
            if (c.Found)
                sink.Publish(new EmergencePoint
                {
                    EventId = sc.EventId, X = c.X, Z = c.Z, HeightM = 0f, FaultId = sys.FaultId, VertexIdx = c.VertexIdx,
                    TimeS = (float)c.TimeS, DistToGGm = (float)c.DistToGGm, DistToSoMaM = (float)c.DistToSoMaM,
                });
            return r;
        }
    }
}
