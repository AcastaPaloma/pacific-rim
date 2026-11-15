// Metric dump exporters of World.Seismic (T-036c): `fault_rupture_dump@v1` (M-053) and `rupture_dump@v1` (M-054). The schemas live in the
// docstrings of Tools/Metrics/kaiju_metrics/destruction/m053.py and m054.py. Pure, engine-free, deterministic: the same results give the same
// string. Read-only views of RuptureResult (the object QuakeDirector.Run returns and the events QuakeBegan / FaultRupture are cut from).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Game.World.Seismic
{
    public static class SeismicDumpExporter
    {
        /// <summary>Slip samples with an expected value below this (m) are not exported: the elliptical taper reaches 0 at the ends and a relative error there means nothing.</summary>
        public const double MinExpectedSlipM = 0.01;

        static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Not finite: " + v);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        /// <summary>
        /// A run is a maximal range of ruptured vertices joined by mapped (non-bridged) segments inside one section: exactly what M-053 can
        /// compare with the dissolved trace of one (fault_id, section). Single-vertex runs have no polyline and are not returned.
        /// </summary>
        public static List<int[]> Runs(RuptureResult r)
        {
            var sys = r.System; var runs = new List<int[]>();
            int a = r.VertexStart;
            for (int v = r.VertexStart; v <= r.VertexEnd; v++)
            {
                bool last = v == r.VertexEnd;
                bool cut = last || sys.Bridge[v] || sys.SectionOfVertex[v + 1] != sys.SectionOfVertex[v];
                if (!cut) continue;
                if (v > a) runs.Add(new[] { a, v });
                a = v + 1;
            }
            return runs;
        }

        /// <summary>
        /// M-053 dump. <paramref name="bakedIdOf"/> maps (system, sectionIdx) to the baked `fault_id` string (default: FaultSystem.SectionBakedIds);
        /// runs without an id are listed in "unresolved_runs" and not exported. Applied slip = RuptureResult.Slip; expected slip is regenerated from the
        /// event fields (float MdM / AdM, seed), so the check proves the profile is regenerable from what the events carry.
        /// </summary>
        public static string BuildFaultRuptureDump(IReadOnlyList<RuptureResult> results, string faultsPath = null, string faultsSha256 = null,
            Func<FaultSystem, int, string> bakedIdOf = null)
        {
            if (results == null || results.Count == 0) throw new ArgumentException("An empty dump proves nothing", nameof(results));
            var sb = new StringBuilder(1 << 16);
            sb.Append("{\"format\":\"fault_rupture_dump\",\"schema\":1");
            if (faultsPath != null) sb.Append(",\"faults_path\":").Append(Str(faultsPath));
            if (faultsSha256 != null) sb.Append(",\"faults_sha256\":").Append(Str(faultsSha256));
            sb.Append(",\"ruptures\":[");
            var unresolved = new List<string>();
            int emitted = 0;
            foreach (RuptureResult r in results)
            {
                var sys = r.System;
                double[] expected = RuptureSolver.SlipProfile(sys, r.VertexStart, r.VertexEnd, (float)r.MdM, (float)r.AdM, r.Seed);
                foreach (int[] run in Runs(r))
                {
                    int sec = sys.SectionOfVertex[run[0]];
                    string id = bakedIdOf != null ? bakedIdOf(sys, sec) : (sys.SectionBakedIds != null ? sys.SectionBakedIds[sec] : null);
                    if (id == null) { unresolved.Add(sys.Name + "|" + sys.Sections[sec] + " v" + run[0] + "-" + run[1]); continue; }
                    if (emitted++ > 0) sb.Append(',');
                    sb.Append("{\"event_id\":").Append(r.EventId.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"fault_id\":").Append(Str(id)).Append(",\"section\":").Append(Str(sys.Sections[sec]))
                      .Append(",\"vertex_start\":").Append(run[0].ToString(CultureInfo.InvariantCulture))
                      .Append(",\"vertex_end\":").Append(run[1].ToString(CultureInfo.InvariantCulture)).Append(",\"polyline\":[");
                    for (int v = run[0]; v <= run[1]; v++)
                        sb.Append(v > run[0] ? "," : "").Append('[').Append(Num(sys.X[v])).Append(',').Append(Num(sys.Z[v])).Append(']');
                    sb.Append("],\"slip\":[");
                    bool first = true;
                    for (int v = run[0]; v <= run[1]; v++)
                    {
                        double e = expected[v - r.VertexStart];
                        if (e < MinExpectedSlipM) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"applied_m\":").Append(Num(r.Slip[v - r.VertexStart])).Append(",\"expected_m\":").Append(Num(e)).Append('}');
                    }
                    sb.Append("]}");
                    if (first) throw new InvalidOperationException("Run with no slip sample above " + MinExpectedSlipM + " m: " + sys.Name);
                }
            }
            if (emitted == 0) throw new ArgumentException("No rupture run has a baked fault_id (first: " + (unresolved.Count > 0 ? unresolved[0] : "none") + ")", nameof(results));
            sb.Append("],\"unresolved_runs\":[");
            for (int i = 0; i < unresolved.Count; i++) sb.Append(i > 0 ? "," : "").Append(Str(unresolved[i]));
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>
        /// M-054 dump: Mw of the rupture (QuakeBegan.Mw before the float cast), SRL = arc length of the ruptured vertices, MD and AD = peak and
        /// mean of the applied slip profile (not the requested scaling values), trace length = the whole fault system.
        /// </summary>
        public static string BuildRuptureDump(IReadOnlyList<RuptureResult> results)
        {
            if (results == null || results.Count == 0) throw new ArgumentException("An empty dump proves nothing", nameof(results));
            var sb = new StringBuilder(1 << 10);
            sb.Append("{\"format\":\"rupture_dump\",\"schema\":1,\"ruptures\":[");
            for (int i = 0; i < results.Count; i++)
            {
                RuptureResult r = results[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Str(r.System.Name + " Mw" + r.Mw.ToString("0.00", CultureInfo.InvariantCulture) + " ev" + r.EventId.ToString(CultureInfo.InvariantCulture)))
                  .Append(",\"event_id\":").Append(r.EventId.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"mw\":").Append(Num(r.Mw)).Append(",\"mw_requested\":").Append(Num(r.MwRequested))
                  .Append(",\"srl_km\":").Append(Num(r.SrlKm)).Append(",\"md_m\":").Append(Num(r.PeakSlipM)).Append(",\"ad_m\":").Append(Num(r.MeanSlipM))
                  .Append(",\"md_scaling_m\":").Append(Num(r.MdM)).Append(",\"ad_scaling_m\":").Append(Num(r.AdM))
                  .Append(",\"trace_length_km\":").Append(Num(r.System.LengthM / 1000.0)).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }
}
