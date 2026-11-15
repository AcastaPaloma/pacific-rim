// Pure planner of the Bay-wide emergence sequence (R-103 4.5, T-114). Deterministic for (request, trace, targets, options): own SplitMix64,
// no wall clock, no shared state. Output is a plan (sites and kaiju); the director publishes it as events.
using System;
using System.Collections.Generic;
using Game.World.Emergence;

namespace Game.World.Emergence
{
    /// <summary>All *Assumed* constants of the sequence in one place (R-103 4.5, Q-NEW-b). Changing a default bumps EmergenceContractVersions.ModelRevision.</summary>
    public sealed class EmergenceOptions
    {
        /// <summary>Sites per quake. 1 = a normal quake; the `m007_bay_emerge` scenario asks for 5.</summary>
        public int SiteCount = 1;
        /// <summary>Preferred distance band from a site to the nearest infrastructure cluster (Q-NEW-b: 5-20 km), m.</summary>
        public float BandMinM = 5000f, BandMaxM = 20000f;
        /// <summary>Minimum straight-line distance between two sites, m.</summary>
        public float MinSiteSpacingM = 3000f;
        /// <summary>Kaiju of one cluster are drawn within this arc distance of the site along the rupture (R-103 4.5: 0-4 km), m.</summary>
        public float ClusterSpreadM = 4000f;
        /// <summary>Kaiju times are drawn within this window after the site time (R-103 4.5: 0-120 s), s.</summary>
        public float WindowS = 120f;
        /// <summary>Base time after the rupture ends: shaking 10 s + 5 s, the same constants as World.Seismic's EmergencePoint, s.</summary>
        public float AfterRuptureS = 15f;
        /// <summary>Cluster size range; KMax 0 = derived from Mw (round(2 + 2 (Mw - 6)) clamped to 2..5; below Mw 6 a single kaiju).</summary>
        public int KMin = 1, KMax = 0;
        /// <summary>Minimum straight-line distance of two kaiju of one cluster, m (bodies are 60-160 m tall).</summary>
        public float MinKaijuSpacingM = 250f;

        /// <summary>Species weights per magnitude band (larger event, larger species). Row = band, columns = Strider, Breacher, Bastion. Assumed.</summary>
        public float[] MixMwEdges = { 6.5f, 7.0f, 7.5f };
        public float[][] MixWeights =
        {
            new[] { 0.70f, 0.25f, 0.05f },   // Mw < 6.5
            new[] { 0.50f, 0.35f, 0.15f },   // 6.5 <= Mw < 7.0
            new[] { 0.35f, 0.40f, 0.25f },   // 7.0 <= Mw < 7.5
            new[] { 0.20f, 0.40f, 0.40f },   // Mw >= 7.5
        };

        public int ResolveKMax(float mw)
        {
            int k = KMax > 0 ? KMax : (mw < 6f ? 1 : (int)Math.Round(2.0 + 2.0 * (mw - 6.0), MidpointRounding.AwayFromZero));
            if (k > 5) k = 5;
            return k < KMin ? KMin : k;
        }

        public float[] MixFor(float mw)
        {
            int band = 0;
            while (band < MixMwEdges.Length && mw >= MixMwEdges[band]) band++;
            return MixWeights[Math.Min(band, MixWeights.Length - 1)];
        }
    }

    public struct EmergenceRequest
    {
        public uint EventId;
        public float Mw;
        public uint Seed;
        /// <summary>Ruptured vertex range of the trace (FaultRupture.VertexStart/VertexEnd).</summary>
        public int VertexStart, VertexEnd;
        /// <summary>Rupture end, simulation s from the origin (FaultRupture.EndS).</summary>
        public float RuptureEndS;
    }

    public struct PlannedKaiju
    {
        public int Ordinal;
        public byte SpeciesId;
        public double X, Z;
        public float TimeS, HeadingRad;
    }

    public sealed class PlannedSite
    {
        public int SiteIdx;
        public int VertexIdx;
        public double X, Z;
        public float TimeS, DistToTargetM;
        public PlannedKaiju[] Kaiju;
    }

    public sealed class EmergencePlan
    {
        public uint EventId, FaultId;
        public int SitesRequested;
        public PlannedSite[] Sites = new PlannedSite[0];
        public int KaijuCount { get { int n = 0; foreach (var s in Sites) n += s.Kaiju.Length; return n; } }
    }

    public static class EmergencePlanner
    {
        /// <summary>Returns null when the request has no usable vertex (empty range, or every vertex touches a bridge).</summary>
        public static EmergencePlan Plan(in EmergenceRequest req, EmergenceTrace trace, IEmergenceTargets targets, EmergenceOptions opt)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            opt = opt ?? new EmergenceOptions();
            int vs = Math.Max(0, Math.Min(req.VertexStart, req.VertexEnd));
            int ve = Math.Min(trace.VertexCount - 1, Math.Max(req.VertexStart, req.VertexEnd));
            var rng = new Rng(((ulong)req.Seed << 32) ^ req.EventId ^ 0x9E3779B97F4A7C15UL);

            // candidate vertices: inside the ruptured range, never touching a bridged segment
            var cand = new List<int>();
            for (int v = vs; v <= ve; v++) if (!trace.TouchesBridge(v)) cand.Add(v);
            if (cand.Count == 0) return null;

            float bandMid = 0.5f * (opt.BandMinM + opt.BandMaxM), bandHalf = 0.5f * (opt.BandMaxM - opt.BandMinM);
            var dist = new float[cand.Count];
            var weight = new double[cand.Count];
            bool haveTargets = targets != null && targets.Count > 0;
            for (int i = 0; i < cand.Count; i++)
            {
                int v = cand[i];
                dist[i] = haveTargets ? NearestTarget(targets, trace.X[v], trace.Z[v], out _) : -1f;
                weight[i] = haveTargets ? SiteScore(dist[i], opt.BandMinM, opt.BandMaxM, bandMid, bandHalf) : 1.0;
            }

            var plan = new EmergencePlan { EventId = req.EventId, FaultId = trace.FaultId, SitesRequested = Math.Max(1, opt.SiteCount) };
            var sites = new List<PlannedSite>();
            var chosen = new List<int>();   // indices into cand
            float baseTime = req.RuptureEndS + opt.AfterRuptureS;
            for (int s = 0; s < plan.SitesRequested; s++)
            {
                // weighted draw among candidates that keep the spacing to every chosen site
                double total = 0;
                var elig = new double[cand.Count];
                for (int i = 0; i < cand.Count; i++)
                {
                    if (!SpacingOk(trace, cand[i], chosen, cand, opt.MinSiteSpacingM)) continue;
                    elig[i] = weight[i] * weight[i];
                    total += elig[i];
                }
                if (!(total > 0)) break;
                double pick = rng.NextDouble() * total, acc = 0;
                int sel = -1;
                for (int i = 0; i < cand.Count; i++)
                {
                    if (elig[i] <= 0) continue;
                    acc += elig[i]; sel = i;
                    if (pick < acc) break;
                }
                chosen.Add(sel);
                sites.Add(BuildSite(s, cand[sel], dist[sel], vs, ve, req, trace, targets, opt, baseTime, ref rng));
            }
            plan.Sites = sites.ToArray();
            return plan;
        }

        // In-band candidates score 0.5..1 (1 at the band centre); outside decays from 0.5 over a band width. Always > 0.
        static double SiteScore(float d, float min, float max, float mid, float half)
        {
            if (d >= min && d <= max) return 0.5 + 0.5 * (1.0 - Math.Abs(d - mid) / Math.Max(1f, half));
            float dev = d < min ? min - d : d - max;
            return 0.5 * Math.Exp(-dev / Math.Max(1f, max - min)) * 0.5 + 1e-6;
        }

        static bool SpacingOk(EmergenceTrace t, int v, List<int> chosen, List<int> cand, float minM)
        {
            double m2 = (double)minM * minM;
            for (int k = 0; k < chosen.Count; k++)
            {
                int u = cand[chosen[k]];
                double dx = t.X[v] - t.X[u], dz = t.Z[v] - t.Z[u];
                if (dx * dx + dz * dz < m2) return false;
            }
            return true;
        }

        static float NearestTarget(IEmergenceTargets targets, double x, double z, out int index)
        {
            double best = double.MaxValue; index = -1;
            for (int i = 0; i < targets.Count; i++)
            {
                var g = targets.Get(i);
                double dx = g.X - x, dz = g.Z - z, d2 = dx * dx + dz * dz;
                if (d2 < best) { best = d2; index = i; }
            }
            return (float)Math.Sqrt(best);
        }

        static PlannedSite BuildSite(int siteIdx, int vertex, float dist, int vs, int ve, in EmergenceRequest req, EmergenceTrace t,
            IEmergenceTargets targets, EmergenceOptions opt, float baseTime, ref Rng rng)
        {
            int kMax = opt.ResolveKMax(req.Mw), kMin = Math.Min(opt.KMin, kMax);
            int k = kMin + (int)(rng.NextDouble() * (kMax - kMin + 1));
            if (k > kMax) k = kMax;
            float[] mix = opt.MixFor(req.Mw);

            // mapped run around the site: the vertices reachable from the site without crossing a bridge, limited to the ruptured range
            int lo = vertex, hi = vertex;
            while (lo > vs && !t.Bridge[lo - 1]) lo--;
            while (hi < ve && !t.Bridge[hi]) hi++;
            double arcLo = t.ArcM[lo], arcHi = t.ArcM[hi], arcSite = t.ArcM[vertex];

            var kai = new PlannedKaiju[k];
            var tm = new float[k];
            for (int i = 0; i < k; i++) tm[i] = baseTime + (float)(rng.NextDouble() * opt.WindowS);
            Array.Sort(tm);
            if (k > 0) tm[0] = baseTime;   // the site fires at its time; the others follow inside the window

            double sx = t.X[vertex], sz = t.Z[vertex];
            for (int i = 0; i < k; i++)
            {
                double px = sx, pz = sz;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    double off = (rng.NextDouble() * 2.0 - 1.0) * opt.ClusterSpreadM;
                    double arc = Math.Max(arcLo, Math.Min(arcHi, arcSite + off));
                    PointAtArc(t, lo, hi, arc, out px, out pz);
                    bool ok = true;
                    for (int j = 0; j < i; j++)
                    {
                        double dx = px - kai[j].X, dz = pz - kai[j].Z;
                        if (dx * dx + dz * dz < (double)opt.MinKaijuSpacingM * opt.MinKaijuSpacingM) { ok = false; break; }
                    }
                    if (ok) break;
                }
                float heading = 0f;
                if (targets != null && targets.Count > 0)
                {
                    NearestTarget(targets, px, pz, out int ti);
                    var g = targets.Get(ti);
                    heading = (float)Math.Atan2(g.Z - pz, g.X - px);
                }
                kai[i] = new PlannedKaiju { Ordinal = i, SpeciesId = DrawSpecies(mix, ref rng), X = px, Z = pz, TimeS = tm[i], HeadingRad = heading };
            }
            return new PlannedSite { SiteIdx = siteIdx, VertexIdx = vertex, X = sx, Z = sz, TimeS = baseTime, DistToTargetM = dist, Kaiju = kai };
        }

        static void PointAtArc(EmergenceTrace t, int lo, int hi, double arc, out double x, out double z)
        {
            if (hi <= lo) { x = t.X[lo]; z = t.Z[lo]; return; }
            int a = lo;
            while (a < hi - 1 && t.ArcM[a + 1] < arc) a++;
            double seg = t.ArcM[a + 1] - t.ArcM[a];
            double f = seg > 1e-9 ? (arc - t.ArcM[a]) / seg : 0.0;
            if (f < 0) f = 0; else if (f > 1) f = 1;
            x = t.X[a] + (t.X[a + 1] - t.X[a]) * f;
            z = t.Z[a] + (t.Z[a + 1] - t.Z[a]) * f;
        }

        static byte DrawSpecies(float[] w, ref Rng rng)
        {
            double sum = 0; for (int i = 0; i < w.Length; i++) sum += w[i];
            double r = rng.NextDouble() * sum, acc = 0;
            for (int i = 0; i < w.Length; i++) { acc += w[i]; if (r < acc) return (byte)(i + 1); }
            return (byte)w.Length;
        }

        struct Rng
        {
            ulong _s;
            public Rng(ulong seed) { _s = seed; }
            public ulong Next()
            {
                unchecked
                {
                    ulong z = (_s += 0x9E3779B97F4A7C15UL);
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return z ^ (z >> 31);
                }
            }
            public double NextDouble() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
        }
    }
}
