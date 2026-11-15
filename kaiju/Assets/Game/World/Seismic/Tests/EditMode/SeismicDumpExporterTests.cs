// T-036c: the fault_rupture_dump@v1 / rupture_dump@v1 exporters are deterministic, split ruptures at bridges and sections, and
// regenerate the expected slip from the event fields. (The Python round trip over real faults is in DotnetTests/WorldSeismic.Tests/Dotnet.)
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class SeismicDumpExporterTests
    {
        static FaultPiece Piece(string section, string id, double x0, double x1, double z)
        {
            var l = new List<double>();
            for (double x = x0; x <= x1 + 1e-9; x += 500) { l.Add(x); l.Add(z); }
            return new FaultPiece { FaultName = "Test fault zone", Section = section, BakedFaultId = id, SlipSense = "right lateral", Xz = l.ToArray() };
        }

        // Sections A 0..40 km, B 43..100 km (3 km gap, bridged), C far away (dropped).
        static FaultSystem Sys(bool withIds = true)
        {
            var set = FaultTraceSet.Build(new[] { Piece("A section", withIds ? "11" : null, 0, 40000, 0), Piece("B section", withIds ? "12" : null, 43000, 100000, 0), Piece("C section", withIds ? "13" : null, 110000, 120000, 0) });
            return set.Systems[0];
        }

        static RuptureResult Rupture(FaultSystem s, double hx, uint id = 1, double mw = 7.2)
        {
            return RuptureSolver.Solve(s, new QuakeScenario { EventId = id, Mw = (float)mw, FaultId = s.FaultId, HypoX = (float)hx, HypoZ = 0, Seed = 5 });
        }

        [Test]
        public void Runs_SplitAtBridgesAndSections_AndCoverOnlyMappedSegments()
        {
            FaultSystem s = Sys();
            RuptureResult r = Rupture(s, 41500);                  // centred on the A/B gap
            Assert.Less(r.VertexStart, 80); Assert.Greater(r.VertexEnd, 86);
            var runs = SeismicDumpExporter.Runs(r);
            Assert.GreaterOrEqual(runs.Count, 2);
            int covered = 0;
            foreach (int[] run in runs)
            {
                Assert.Greater(run[1], run[0]);
                for (int v = run[0]; v < run[1]; v++) { Assert.IsFalse(s.Bridge[v]); Assert.AreEqual(s.SectionOfVertex[run[0]], s.SectionOfVertex[v + 1]); covered++; }
            }
            int mapped = 0; for (int v = r.VertexStart; v < r.VertexEnd; v++) if (!s.Bridge[v]) mapped++;
            Assert.AreEqual(mapped, covered, "every mapped segment of the rupture is in exactly one run");
        }

        [Test]
        public void FaultRuptureDump_HasSchemaFields_IsDeterministic_AndNeverNonFinite()
        {
            FaultSystem s = Sys();
            var list = new[] { Rupture(s, 41500), Rupture(s, 70000, 2) };
            string a = SeismicDumpExporter.BuildFaultRuptureDump(list, "Assets/x.geojson", new string('a', 64));
            Assert.AreEqual(a, SeismicDumpExporter.BuildFaultRuptureDump(list, "Assets/x.geojson", new string('a', 64)));
            StringAssert.StartsWith("{\"format\":\"fault_rupture_dump\",\"schema\":1,\"faults_path\":\"Assets/x.geojson\"", a);
            foreach (string k in new[] { "\"event_id\":1", "\"event_id\":2", "\"fault_id\":\"11\"", "\"fault_id\":\"12\"", "\"section\":\"A section\"", "\"vertex_start\"", "\"vertex_end\"", "\"polyline\"", "\"applied_m\"", "\"expected_m\"", "\"unresolved_runs\":[]" })
                StringAssert.Contains(k, a);
            StringAssert.DoesNotContain("NaN", a); StringAssert.DoesNotContain("Infinity", a);
        }

        [Test]
        public void AppliedSlipEqualsRegeneratedExpected_WithinFloatRounding()
        {
            FaultSystem s = Sys();
            RuptureResult r = Rupture(s, 70000);
            double[] exp = RuptureSolver.SlipProfile(s, r.VertexStart, r.VertexEnd, (float)r.MdM, (float)r.AdM, r.Seed);
            double worst = 0;
            for (int i = 0; i < exp.Length; i++) if (exp[i] >= SeismicDumpExporter.MinExpectedSlipM) worst = Math.Max(worst, Math.Abs(r.Slip[i] - exp[i]) / exp[i]);
            Assert.Less(worst, 1e-5);
        }

        [Test]
        public void FaultRuptureDump_RejectsEmpty_AndUnresolvableIds_AndUsesResolver()
        {
            Assert.Throws<ArgumentException>(() => SeismicDumpExporter.BuildFaultRuptureDump(new RuptureResult[0]));
            FaultSystem none = Sys(false);
            Assert.Throws<ArgumentException>(() => SeismicDumpExporter.BuildFaultRuptureDump(new[] { Rupture(none, 70000) }));
            string ok = SeismicDumpExporter.BuildFaultRuptureDump(new[] { Rupture(none, 70000) }, null, null, (sys, sec) => "z" + sec);
            StringAssert.Contains("\"fault_id\":\"z1\"", ok);
            // a section without an id is listed, not silently dropped
            string part = SeismicDumpExporter.BuildFaultRuptureDump(new[] { Rupture(none, 41500) }, null, null, (sys, sec) => sec == 1 ? "b" : null);
            StringAssert.DoesNotContain("\"unresolved_runs\":[]", part);
        }

        [Test]
        public void RuptureDump_CarriesMwSrlAndAppliedSlip()
        {
            FaultSystem s = Sys();
            RuptureResult r = Rupture(s, 70000);
            string d = SeismicDumpExporter.BuildRuptureDump(new[] { r });
            StringAssert.StartsWith("{\"format\":\"rupture_dump\",\"schema\":1,\"ruptures\":[{\"name\":\"Test fault zone Mw7.20 ev1\"", d);
            StringAssert.Contains("\"mw\":" + r.Mw.ToString("R", System.Globalization.CultureInfo.InvariantCulture), d);
            StringAssert.Contains("\"trace_length_km\":" + (s.LengthM / 1000.0).ToString("R", System.Globalization.CultureInfo.InvariantCulture), d);
            Assert.Throws<ArgumentException>(() => SeismicDumpExporter.BuildRuptureDump(null));
        }

        [Test]
        public void SegmentSlip_IsZeroOnBridges_AndEqualsMeanOfVertexSlipElsewhere()
        {
            FaultSystem s = Sys();
            RuptureResult r = Rupture(s, 41500);
            bool sawBridge = false;
            for (int v = r.VertexStart; v < r.VertexEnd; v++)
            {
                double seg = r.SegmentSlip(v);
                if (s.Bridge[v]) { Assert.AreEqual(0.0, seg); sawBridge = true; }
                else Assert.AreEqual(0.5 * (r.Slip[v - r.VertexStart] + r.Slip[v - r.VertexStart + 1]), seg, 1e-12);
            }
            Assert.IsTrue(sawBridge);
            Assert.AreEqual(0.0, r.SegmentSlip(r.VertexEnd));
            Assert.AreEqual(0.0, r.SegmentSlip(r.VertexStart - 1));
        }
    }
}
