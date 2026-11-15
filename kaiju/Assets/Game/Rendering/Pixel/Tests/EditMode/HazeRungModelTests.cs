using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>
    /// R-131 / T-176: the engine-free haze + ladder model (HazeRungModel), the committed tuning and the C11 shift maths
    /// (ParallaxShiftMath) against the offline Python model's numbers (research/R-131: golden_haze.json, out_recommend.json, out_c11_sim.json).
    /// </summary>
    public class HazeRungModelTests
    {
        static readonly string[] Ids = { "warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson" };

        static string R131(string file) => Path.Combine(PixelTestPaths.ProjectRoot(), "research", "R-131", file);
        static string TuningJson() => File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Config", "Rendering", "PaletteProfileTuning.json"));
        static HazeRungModel Model(string id) => new HazeRungModel(PixelTestPaths.Profile(id), HazeTuning.Parse(TuningJson(), id));
        static MiniJson.Obj Golden() => (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(R131("golden_haze.json")));

        [Test]
        public void RungLightness_MatchesTheOfflineModel_ForAllFiveProfiles()
        {
            var g = Golden().Object("profiles");
            foreach (string id in Ids)
            {
                var m = Model(id); var want = g.Object(id).Array("rungL");
                Assert.AreEqual(want.Count, m.RungL.Length, id + " rung count");
                for (int i = 0; i < want.Count; i++) Assert.AreEqual((double)want[i], m.RungL[i], 0.1, id + " rung " + i);
            }
            // The captured rung values of R-131 section 2.
            CollectionAssert.AreEqual(new[] { 17.3, 22.9, 31.9, 39.6, 46.5, 53.9, 65.7 }, Round1(Model("warm_rust_sepia").RungL));
            CollectionAssert.AreEqual(new[] { 9.9, 19.0, 27.2, 36.4, 49.7 }, Round1(Model("slate_crimson").RungL));
        }

        static double[] Round1(double[] v) { var r = new double[v.Length]; for (int i = 0; i < v.Length; i++) r[i] = Math.Round(v[i], 1); return r; }

        [Test]
        public void CommittedTuning_CarriesTheR131Recommendation()
        {
            var rec = (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(R131("out_recommend.json")));
            foreach (string id in Ids)
            {
                var t = HazeTuning.Parse(TuningJson(), id); var row = rec.Object(id);
                if (!row.Has("params")) continue;                       // teal and lavender stay unchanged
                var p = row.Object("params");
                Assert.AreEqual(p.Num("start"), t.HazeStartM, 1e-9, id); Assert.AreEqual(p.Num("full"), t.HazeFullM, 1e-9, id);
                Assert.AreEqual((int)p.Num("B"), t.HazeBands, id); Assert.AreEqual(p.Num("gamma"), t.HazeCurveGamma, 1e-9, id);
            }
            Assert.AreEqual(64, HazeTuning.Parse(TuningJson(), "warm_rust_sepia").HazeBands);
            Assert.AreEqual(38400, HazeTuning.Parse(TuningJson(), "warm_amber_haze").HazeFullM, 1e-9);
            Assert.AreEqual(0.72, HazeTuning.Parse(TuningJson(), "slate_crimson").HazeCurveGamma, 1e-9);
            Assert.AreEqual(20000, HazeTuning.Parse(TuningJson(), "slate_teal").HazeFullM, 1e-9, "teal unchanged");
            Assert.AreEqual(20000, HazeTuning.Parse(TuningJson(), "lavender_peach").HazeFullM, 1e-9, "lavender unchanged");
        }

        /// <summary>T-178: a later tuning edit that breaks the model's prediction fails here (golden values rendered by research/R-131/golden_haze.py).</summary>
        [Test]
        public void GreyAlbedoRenders_MatchTheOfflineModel_Within05L()
        {
            var g = Golden(); var d = g.Array("distances"); var profiles = g.Object("profiles");
            foreach (string id in Ids)
            {
                var m = Model(id); var ground = profiles.Object(id).Array("ground024"); var box = profiles.Object(id).Array("box0272");
                Assert.AreEqual((int)profiles.Object(id).Num("bands"), m.Bands, id);
                for (int i = 0; i < d.Count; i++)
                {
                    Assert.AreEqual((double)ground[i], m.RenderGrey(0.24, (double)d[i]), 0.5, id + " ground at " + d[i]);
                    Assert.AreEqual((double)box[i], m.RenderGrey(0.272, (double)d[i]), 0.5, id + " box at " + d[i]);
                }
            }
        }

        [Test]
        public void RecordedPredictions_AreAllGatesPassing_ForTheCommittedTuning()
        {
            var rec = (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(R131("out_recommend.json")));
            foreach (string id in Ids)
            {
                var row = rec.Object(id); var r = row.Has("recommended") ? row.Object("recommended") : row.Object("baseline");
                Assert.AreEqual(r.Object("c7").Num("far_pass_min_over_albedo"), r.Object("c7").Num("far_n"), id + " C7 1 km in every albedo scenario");
                Assert.AreEqual(4, r.Object("c7").Num("near_pass_min"), id + " C7 150 m");
                Assert.AreEqual(12, r.Object("c6").Num("lighter"), id + " C6");
                Assert.IsTrue((bool)r.Object("c1")["pass"], id + " c1"); Assert.IsTrue((bool)r.Object("c2")["pass"], id + " c2");
                Assert.GreaterOrEqual(r.Object("c7").Num("far_min"), 4.0, id + " dL_ring3 margin");
            }
        }

        [Test]
        public void GroundAlbedo_ClimbsAtMostOneRungPerBandStep_AndNeverFalls()
        {
            foreach (string id in Ids)
            {
                var m = Model(id); var t = HazeTuning.Parse(TuningJson(), id);
                int prev = m.RungAt(0.24, t.HazeStartM * 0.5);
                for (int b = 1; b <= t.HazeBands - 1; b++)
                {
                    double d0 = HazeMath.BandStart(b, t.HazeStartM, t.HazeFullM, t.HazeBands) * 0.999, d1 = d0 * 1.002;
                    // levels are given by gamma: skip the analytic inverse and just walk the distance finely.
                    int a = m.RungAt(0.24, d0), c = m.RungAt(0.24, d1);
                    Assert.LessOrEqual(Math.Abs(c - a), 1, id + " band " + b); Assert.GreaterOrEqual(c, a - 0, id + " never falls at band " + b);
                }
                double last = m.RenderGrey(0.24, 10);
                for (double dist = 20; dist < 60000; dist *= 1.05) { double l = m.RenderGrey(0.24, dist); Assert.GreaterOrEqual(l, last - 1e-9, id + " at " + dist); last = l; }
                // monotone in albedo at a fixed distance
                for (double dist = 100; dist < 30000; dist *= 3) { double lo = -1; for (double a = 0.05; a <= 0.95; a += 0.05) { double l = m.RenderGrey(a, dist); Assert.GreaterOrEqual(l, lo - 1e-9, id + " albedo " + a + " at " + dist); lo = l; } }
            }
        }
    }

    public class ParallaxShiftMathTests
    {
        static MiniJson.Obj Sim() => (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "research", "R-131", "out_c11_sim.json")));
        const double Tol = 0.5;

        static void Check(MiniJson.Obj row, string cls, ParallaxShiftMath.Scheme s, double z, double zLayer, int fine = 1)
        {
            ParallaxShiftMath.Simulate(s, z, zLayer, out double worst, out double median, fine);
            var want = row.Array(cls);
            Assert.AreEqual((double)want[0], worst, Tol, cls + " worst"); Assert.AreEqual((double)want[1], median, Tol, cls + " median");
        }

        [Test]
        public void SingleSnap_UniformShift_ReproducesTheBaseline()
        {
            var r = Sim().Object("baseline_uniform_shift");
            Check(r, "near", ParallaxShiftMath.Scheme.Baseline, ParallaxShiftMath.NearDepthM, ParallaxShiftMath.RefDepthM);
            Check(r, "reference", ParallaxShiftMath.Scheme.Baseline, ParallaxShiftMath.ReferenceClassDepthM, ParallaxShiftMath.RefDepthM);
            Check(r, "far", ParallaxShiftMath.Scheme.Baseline, ParallaxShiftMath.FarDepthM, ParallaxShiftMath.RefDepthM);
        }

        [Test]
        public void DepthAwareShift_RemovesTheParallaxTermOnly()
        {
            var r = Sim().Object("depth_aware_shift_single_snap");
            Check(r, "near", ParallaxShiftMath.Scheme.DepthShift, ParallaxShiftMath.NearDepthM, ParallaxShiftMath.RefDepthM);
            Check(r, "reference", ParallaxShiftMath.Scheme.DepthShift, ParallaxShiftMath.ReferenceClassDepthM, ParallaxShiftMath.RefDepthM);
            Check(r, "far", ParallaxShiftMath.Scheme.DepthShift, ParallaxShiftMath.FarDepthM, ParallaxShiftMath.RefDepthM);
            ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.DepthShift, ParallaxShiftMath.NearDepthM, 0, out double w, out _);
            Assert.Greater(w, 1.0, "step A alone does not reach 1 px (R-131 4.4)");
        }

        [Test]
        public void ThreeLayerSlabsAtTheClassDepths_ReachOnePixel()
        {
            var r = Sim().Object("layers_exact_at_class_depths(3)");
            foreach (var (cls, z) in new[] { ("near", ParallaxShiftMath.NearDepthM), ("reference", ParallaxShiftMath.ReferenceClassDepthM), ("far", ParallaxShiftMath.FarDepthM) })
            {
                Check(r, cls, ParallaxShiftMath.Scheme.LayerSnap, z, z);
                ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, z, z, out double w, out _);
                Assert.LessOrEqual(w, 1.0, cls + " within the C11 gate at its slab depth");
            }
        }

        [Test]
        public void AnyDepth_StaysNearFivePixels_SoQ260StaysOpen()
        {
            var r = Sim().Object("layers_log_spaced");
            foreach (int n in new[] { 12, 20 })
                Assert.AreEqual(r.Object("N=" + n).Num("worst_px_any_depth"), ParallaxShiftMath.AnyDepthWorstPx(n), Tol, "N=" + n);
            Assert.Greater(ParallaxShiftMath.AnyDepthWorstPx(20), 1.0, "C11 is not reachable for continuous depth by layering (R-131 4.3)");
        }

        [Test]
        public void ShiftAndSnap_Basics()
        {
            Assert.AreEqual(-3, ParallaxShiftMath.DepthShiftPx(1.0, -0.75, 4));
            Assert.AreEqual(-16, ParallaxShiftMath.DepthShiftPx(5.35, -0.75, 4));
            Assert.AreEqual(0.0, ParallaxShiftMath.LayerSnap(0.0, 2.0)); Assert.AreEqual(2.0, ParallaxShiftMath.LayerSnap(0.1, 2.0)); Assert.AreEqual(2.0, ParallaxShiftMath.LayerSnap(2.0, 2.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ParallaxShiftMath.LayerSnap(1, 0));
            Assert.AreEqual(1.5994 * 124.2 / 664.874, ParallaxShiftMath.LayerGridM(1.5994, 664.874, 124.2), 1e-9);
        }
    }
}
