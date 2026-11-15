using System;
using System.IO;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>
    /// T-181a: the engine-free slab plan (PixelLayerPlan) and the C11 lock tolerance, against the independent Python mirror
    /// evidence/T-181/layer_plan.py -> layer_plan.json. Design note: Assets/Game/Rendering/Pixel/MODULE.md, "Layer slabs".
    /// </summary>
    public class PixelLayerPlanTests
    {
        static readonly string[] Ids = { "warm_rust_sepia", "warm_amber_haze", "slate_teal", "lavender_peach", "slate_crimson" };
        static readonly double[] Refs = { 124.2, 664.874, 10353.0 };
        static readonly double TanH = Math.Tan(37.5 * Math.PI / 180), TanV = TanH * 270 / 480;

        static string TuningJson() => File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Config", "Rendering", "PaletteProfileTuning.json"));
        static HazeTuning Haze(string id) => HazeTuning.Parse(TuningJson(), id);
        static MiniJson.Obj Golden() => (MiniJson.Obj)MiniJson.Parse(File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "evidence", "T-181", "layer_plan.json")));

        static PixelSlab[] Build(HazeTuning h, out int[] bands, int[] previous = null, double[] refs = null)
        {
            Assert.IsTrue(PixelLayerPlan.TryBuild(refs ?? Refs, h, TanH, TanV, new PixelLayerPlanSettings(), previous, out var slabs, out bands, out string reason), reason);
            return slabs;
        }

        [Test]
        public void BandStart_WithGamma_IsTheInverseOfLevel()
        {
            foreach (double g in new[] { 1.0, 0.72, 1.4 })
                for (int b = 1; b < 24; b++)
                {
                    double d = HazeMath.BandStart(b, 155.4, 16800, 24, g);
                    Assert.AreEqual(b - 1, HazeMath.Band(HazeMath.Level(d * 0.9999, 155.4, 16800, 24, g), 24), "gamma " + g + " band " + b + " below");
                    Assert.AreEqual(b, HazeMath.Band(HazeMath.Level(d * 1.0001, 155.4, 16800, 24, g), 24), "gamma " + g + " band " + b + " above");
                }
            Assert.AreEqual(HazeMath.BandStart(5, 60, 20000, 8), HazeMath.BandStart(5, 60, 20000, 8, 1.0), 1e-9, "the four-argument form is gamma 1");
            Assert.Throws<ArgumentOutOfRangeException>(() => HazeMath.BandStart(1, 60, 20000, 8, 0));
        }

        [Test]
        public void SlabBoundaries_SitOnHazeBandEdges_InAllFiveProfiles_AndMatchTheMirror()
        {
            var g = Golden().Object("plan");
            foreach (string id in Ids)
            {
                var h = Haze(id);
                var slabs = Build(h, out int[] bands);
                var rows = g.Array(id);
                for (int b = 0; b < 2; b++)
                {
                    var row = (MiniJson.Obj)rows[b];
                    double e = slabs[b + 1].OwnFromM;
                    Assert.AreEqual(row.Num("boundary_m"), e, 1e-6 * e, id + " boundary " + b);
                    Assert.AreEqual((int)row.Num("band"), bands[b], id + " band " + b);
                    Assert.Greater(bands[b], 0, id + ": boundary " + b + " is a band edge");
                    // The slab seam is a band seam: just inside the lower slab the band is one less than just inside the upper one.
                    Assert.AreEqual(bands[b] - 1, HazeMath.Band(HazeMath.Level(e * 0.9999, h.HazeStartM, h.HazeFullM, h.HazeBands, h.HazeCurveGamma), h.HazeBands), id);
                    Assert.AreEqual(bands[b], HazeMath.Band(HazeMath.Level(e * 1.0001, h.HazeStartM, h.HazeFullM, h.HazeBands, h.HazeCurveGamma), h.HazeBands), id);
                }
            }
        }

        [Test]
        public void Slabs_PartitionRadialDistance_OwnTheirReferenceDepthAtEveryPixel_AndClipCoversOwnership()
        {
            double ray = PixelLayerPlan.CornerRayLength(TanH, TanV);
            foreach (string id in Ids)
            {
                var slabs = Build(Haze(id), out _);
                Assert.AreEqual(0.0, slabs[0].OwnFromM); Assert.IsTrue(double.IsPositiveInfinity(slabs[2].OwnToM));
                for (int i = 0; i < 2; i++) Assert.AreEqual(slabs[i].OwnToM, slabs[i + 1].OwnFromM, id + ": contiguous");
                for (int i = 0; i < 3; i++)
                {
                    var s = slabs[i];
                    Assert.AreEqual(i, PixelLayerPlan.Owner(s.ReferenceDepthM, slabs), id + " slab " + i + " owns its reference at the centre");
                    Assert.AreEqual(i, PixelLayerPlan.Owner(s.ReferenceDepthM * ray, slabs), id + " slab " + i + " owns its reference at the corner");
                    // Radial r in [from, to) has eye depth in [from / ray, to): the clip range must contain it.
                    if (i > 0) Assert.LessOrEqual(s.ClipNearM, s.OwnFromM / ray, id + " clip near " + i);
                    else Assert.AreEqual(new PixelLayerPlanSettings().CameraNearM, s.ClipNearM, id + ": the near slab starts at the camera near plane");
                    if (i < 2) Assert.GreaterOrEqual(s.ClipFarM, s.OwnToM, id + " clip far " + i);
                }
                for (double r = 1; r < 40000; r *= 1.07)
                {
                    int owners = 0; for (int i = 0; i < 3; i++) if (r >= slabs[i].OwnFromM && r < slabs[i].OwnToM) owners++;
                    Assert.AreEqual(1, owners, id + ": one owner at " + r);
                }
            }
        }

        [Test]
        public void HazeOff_PutsBoundariesAtTheGeometricMiddleOfTheAdmissibleRange()
        {
            var slabs = Build(null, out int[] bands);
            double ray = PixelLayerPlan.CornerRayLength(TanH, TanV);
            for (int b = 0; b < 2; b++)
            {
                Assert.AreEqual(-1, bands[b]);
                double lo = Refs[b] * ray * 1.1, hi = Refs[b + 1] / 1.1;
                Assert.AreEqual(Math.Sqrt(lo * hi), slabs[b + 1].OwnFromM, 1e-9);
            }
        }

        [Test]
        public void IllegalReferenceDepths_AreRejectedWithAReason()
        {
            var s = new PixelLayerPlanSettings();
            Assert.IsFalse(PixelLayerPlan.TryBuild(new[] { 124.2, 150.0, 10353.0 }, Haze("slate_teal"), TanH, TanV, s, null, out _, out _, out string r1));
            StringAssert.Contains("too close", r1);
            Assert.IsFalse(PixelLayerPlan.TryBuild(new[] { 664.874, 124.2, 10353.0 }, null, TanH, TanV, s, null, out _, out _, out string r2));
            StringAssert.Contains("increase", r2);
            Assert.IsFalse(PixelLayerPlan.TryBuild(new[] { 124.2, 664.874 }, null, TanH, TanV, s, null, out _, out _, out _));
            Assert.IsFalse(PixelLayerPlan.TryBuild(new[] { 124.2, 664.874, 90000.0 }, null, TanH, TanV, s, null, out _, out _, out _), "beyond the camera far plane");
        }

        [Test]
        public void Hysteresis_KeepsTheBandEdge_WhileTheReferenceDriftsALittle()
        {
            var h = Haze("warm_rust_sepia");     // 64 bands: about 10 % of distance per band
            Build(h, out int[] bands);
            // Drift the main reference until the best near/main edge changes; the previous edge must hold for a while.
            int changedAt = -1, heldUntil = -1;
            for (int step = 1; step < 200; step++)
            {
                double main = Refs[1] * Math.Pow(1.002, step);
                Build(h, out int[] fresh, null, new[] { Refs[0], main, Refs[2] });
                Build(h, out int[] held, bands, new[] { Refs[0], main, Refs[2] });
                if (changedAt < 0 && fresh[0] != bands[0]) changedAt = step;
                if (heldUntil < 0 && held[0] != bands[0]) heldUntil = step;
            }
            Assert.Greater(changedAt, 0, "the drift moves the best edge");
            Assert.Greater(heldUntil, changedAt, "hysteresis holds the previous edge past the point where it stops being the best");
            // A previous edge outside the admissible range is never kept.
            Build(h, out int[] far, new[] { 1, bands[1] });
            Assert.AreEqual(bands[0], far[0]);
        }

        [Test]
        public void Composite_OwnerWins_GapTakesTheNearestCoveredSample_NothingCoveredIsSky()
        {
            var slabs = Build(Haze("slate_teal"), out _);
            double e1 = slabs[1].OwnFromM, e2 = slabs[2].OwnFromM;
            Assert.AreEqual(0, PixelLayerPlan.Composite(slabs, new[] { true, true, true }, new[] { 124.2, 700, 12000 }), "near surface in front");
            Assert.AreEqual(1, PixelLayerPlan.Composite(slabs, new[] { true, true, true }, new[] { e1 * 1.05, 700, 12000 }), "near slab's overlap sample is not its own: main owns");
            Assert.AreEqual(2, PixelLayerPlan.Composite(slabs, new[] { false, false, true }, new[] { 0, 0, 1e30 }), "sky");
            // Gap: the near sample is beyond its range and the main sample (another shift) is before its range.
            Assert.AreEqual(1, PixelLayerPlan.Composite(slabs, new[] { true, true, false }, new[] { e1 * 1.03, e1 * 0.97, 0 }), "gap: nearest covered sample (main, 0.97 e1)");
            Assert.AreEqual(2, PixelLayerPlan.Composite(slabs, new[] { false, false, false }, new[] { 0.0, 0, 0 }));
            Assert.AreEqual(2, PixelLayerPlan.Owner(e2, slabs));
        }

        [Test]
        public void Grid_IsOneTexelAtTheReferenceDepth_AndAMismatchedGridIsRejected()
        {
            double g = PixelLayerPlan.GridM(664.874, TanV, 270);
            Assert.AreEqual(2 * 664.874 * TanV / 270, g, 1e-12);
            Assert.IsTrue(PixelLayerPlan.GridMatches(g, 664.874, TanV, 270));
            Assert.IsTrue(PixelLayerPlan.GridMatches(g * (1 + 0.5e-4), 664.874, TanV, 270));
            Assert.IsFalse(PixelLayerPlan.GridMatches(g * 1.0025, 664.874, TanV, 270), "0.25 % off breaks the lock (C11 tolerance test)");
        }

        /// <summary>
        /// The C11 lock only holds when the slab reference depth equals the surface depth: the model's worst phase jumps to about 4 px at 0.1-0.25 % mismatch
        /// for near and reference (a whole-texel tie flip) while far, which snaps only about once in the sequence, stays below 1 px over +-1 %.
        /// This is why the rig must supply exact depths and why the 3-slab result is not an any-depth result (Q-260 stays open).
        /// </summary>
        [Test]
        public void C11Lock_IsExactOnly_AtTheSlabReferenceDepth_MatchingTheMirror()
        {
            var tol = Golden().Object("c11_lock_tolerance");
            var depths = new[] { ("near", ParallaxShiftMath.NearDepthM), ("reference", ParallaxShiftMath.ReferenceClassDepthM), ("far", ParallaxShiftMath.FarDepthM) };
            foreach (var (cls, z) in depths)
                foreach (var o in tol.Array(cls))
                {
                    var row = (MiniJson.Obj)o; double e = row.Num("rel_error");
                    ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, z, z * (1 + e), out double w, out double m);
                    var want = row.Array("layer");
                    Assert.AreEqual((double)want[0], w, 0.5, cls + " worst at " + e); Assert.AreEqual((double)want[1], m, 0.5, cls + " median at " + e);
                }
            foreach (var (cls, z) in depths)
            {
                ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, z, z, out double exact, out _);
                Assert.LessOrEqual(exact, 1.0, cls + " exact");
            }
            foreach (double e in new[] { -0.0025, 0.0025, 0.02 })
            {
                ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, ParallaxShiftMath.NearDepthM, ParallaxShiftMath.NearDepthM * (1 + e), out double w, out _);
                Assert.Greater(w, 1.0, "near slab " + e + " off its surface fails the gate");
            }
            foreach (double e in new[] { -0.01, -0.0025, 0.0025, 0.01 })
            {
                ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, ParallaxShiftMath.FarDepthM, ParallaxShiftMath.FarDepthM * (1 + e), out double w, out _);
                Assert.LessOrEqual(w, 1.0, "far slab tolerant at " + e);
            }
            // As configured in T1 the main slab is at the rig depth 664.874 m, not the reference-class edge at 663.0 m: the worst phase is the 4.8 px tie flip.
            var rig = tol.Array("reference_at_rig_depth");
            ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.LayerSnap, ParallaxShiftMath.ReferenceClassDepthM, ParallaxShiftMath.RefDepthM, out double rw, out double rm);
            Assert.AreEqual((double)rig[0], rw, 0.5); Assert.AreEqual((double)rig[1], rm, 0.5);
        }
    }
}
