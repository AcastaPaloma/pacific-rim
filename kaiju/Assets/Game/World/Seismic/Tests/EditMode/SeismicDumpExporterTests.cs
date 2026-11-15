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
                for (int v = run[0]; v < run[1]; v++) { Assert.IsFalse(s.Bridge[v]); Assert.IsFalse(s.Chord[v]); Assert.AreEqual(s.SectionOfVertex[run[0]], s.SectionOfVertex[v + 1]); covered++; }
            }
            int mapped = 0; for (int v = r.VertexStart; v < r.VertexEnd; v++) if (!s.Bridge[v] && !s.Chord[v]) mapped++;
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
        public void JoinChord_IsFlagged_AndSplitsTheRun_ButStaysRuptured()
        {
            // one section made of two pieces 20 m apart (joined within the 50 m tolerance): the chord is not on any baked piece
            var set = FaultTraceSet.Build(new[] { Piece("A section", "11", 0, 30000, 0), Piece("A section", "11", 30020, 60000, 0) });
            FaultSystem s = set.Systems[0];
            int chords = s.Chord.Count(c => c);
            Assert.AreEqual(1, chords);
            Assert.AreEqual(0, s.BridgeCount);
            int c0 = Array.IndexOf(s.Chord, true);
            Assert.AreEqual(20.0, s.ArcM[c0 + 1] - s.ArcM[c0], 1e-9);
            RuptureResult r = Rupture(s, 30000, 1, 6.9);
            Assert.Less(r.VertexStart, c0); Assert.Greater(r.VertexEnd, c0 + 1);
            Assert.AreEqual(2, SeismicDumpExporter.Runs(r).Count);
            foreach (int[] run in SeismicDumpExporter.Runs(r)) Assert.IsFalse(run[0] <= c0 && run[1] > c0, "no run crosses the chord");
            Assert.Greater(r.SegmentSlip(c0), 0.0, "the chord is still ruptured (shaking and slip account)");
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

        [Test]
        public void PiecesFromGeo_CarriesTheBakedFaultId_IntoTheSystemAndTheDump()
        {
            var g = new Game.World.Geo.GeoFaultData { Id = "qf:1:1", FaultId = "42", FaultName = "Geo fault zone", Section = "North section", SlipSense = "Right lateral" };
            var l = new List<double>(); for (double x = 0; x <= 40000; x += 500) { l.Add(x - 61440); l.Add(10 - 73728); }
            g.Lines.Add(l.ToArray());
            var set = FaultTraceSet.Build(FaultTraceSet.PiecesFromGeo(new[] { g }, 61440, 73728));
            FaultSystem s = set.Systems[0];
            CollectionAssert.AreEqual(new[] { "42" }, s.SectionBakedIds);
            RuptureResult r = RuptureSolver.Solve(s, new QuakeScenario { EventId = 1, Mw = 6.9f, FaultId = s.FaultId, HypoX = 20000, HypoZ = 10, Seed = 1 });
            StringAssert.Contains("\"fault_id\":\"42\"", SeismicDumpExporter.BuildFaultRuptureDump(new[] { r }));
        }
    }
}
