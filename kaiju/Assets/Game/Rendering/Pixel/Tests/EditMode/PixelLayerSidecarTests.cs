using System;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-182: the <c>layers</c> sidecar member and the per-layer present shift (engine-free; the capture tool writes exactly this text).</summary>
    public class PixelLayerSidecarTests
    {
        const int H = 272;
        static readonly double TanV = Math.Tan(37.5 * Math.PI / 180) * 270 / 480;
        static readonly double TanH = TanV * 480 / 272;

        static PixelLayerHint Layer(double depth, float rx = 0, float ry = 0, float sx = 0, float sy = 0) =>
            new PixelLayerHint { ReferenceDepth = (float)depth, Grid = (float)PixelLayerPlan.GridM(depth, TanV, H), OffsetRight = rx, OffsetUp = ry, SubTexelX = sx, SubTexelY = sy };

        static PixelStabilisationHints Hints(double mainDepth = 664.874)
        {
            var main = Layer(mainDepth, 0, 0, 0.3f, -0.2f);
            return new PixelStabilisationHints
            {
                ReferenceDepth = main.ReferenceDepth, PositionGrid = main.Grid, SubTexelX = 0.3f, SubTexelY = -0.2f, PixelScale = 4,
                LayerCount = 3, LayerNear = Layer(124.2, 0.01f, -0.02f, 0.4f, 0.1f), LayerMain = main, LayerFar = Layer(10353, 1.5f, 0.5f, 0.9f, 0.2f),
            };
        }

        static (PixelSlab[] slabs, int[] bands) Plan(in PixelStabilisationHints h)
        {
            Assert.IsTrue(PixelLayerPlan.TryBuild(PixelLayerHints.ReferenceDepths(h), null, TanH, TanV, null, null, out var slabs, out var bands, out string reason), reason);
            return (slabs, bands);
        }

        [Test]
        public void ShiftOutputPx_RoundsTheOwnRemainderAndClampsToTheGuard()
        {
            var l = Layer(124.2, 0, 0, 0.4f, -0.6f);
            PixelLayerHints.ShiftOutputPx(l, 4, 1, out int sx, out int sy);
            Assert.AreEqual(2, sx); Assert.AreEqual(-2, sy);   // round(1.6) = 2, round(-2.4) = -2
            var far = Layer(10353, 0, 0, 5f, -5f);
            PixelLayerHints.ShiftOutputPx(far, 4, 1, out sx, out sy);
            Assert.AreEqual(4, sx); Assert.AreEqual(-4, sy);   // clamped to guard * scale
            PixelLayerHints.ShiftOutputPx(Layer(664.874, 0, 0, 0.125f, 0.375f), 4, 1, out sx, out sy);
            Assert.AreEqual(0, sx); Assert.AreEqual(2, sy);    // ties to even: 0.5 -> 0, 1.5 -> 2
        }

        [Test]
        public void MainShift_EqualsTheV1PresentShift()
        {
            var h = Hints();
            PixelLayerHints.ShiftOutputPx(h.LayerMain, 4, 1, out int mx, out int my);
            Assert.AreEqual(Math.Max(-4, Math.Min(4, (int)Math.Round(h.SubTexelX * 4.0))), mx);
            Assert.AreEqual(Math.Max(-4, Math.Min(4, (int)Math.Round(h.SubTexelY * 4.0))), my);
        }

        [Test]
        public void Build_WritesTheVersionedKeyWithReferencesGridsBoundariesBandsAndShifts()
        {
            var h = Hints(); var (slabs, bands) = Plan(h);
            string json = PixelLayerSidecar.Build(h, slabs, bands, new[] { 2, 0, 1, -1, 4, 1 }, 4, 1, true, false);
            Assert.IsInstanceOf<MiniJson.Obj>(MiniJson.Parse(json), "valid JSON");
            var o = (MiniJson.Obj)MiniJson.Parse(json);
            Assert.AreEqual("pixel_layers@v1", o["schema"]);
            Assert.AreEqual(3.0, o["count"]);
            Assert.AreEqual(3, ((System.Collections.Generic.IList<object>)o["references_m"]).Count);
            StringAssert.Contains("\"references_m\": [124.2, 664.874, 10353]", json);
            StringAssert.Contains("\"shift_output_px\": [[2, 0], [1, -1], [4, 1]]", json);
            StringAssert.Contains("\"remainder_own_texels\": [[0.4, 0.1], [0.3, -0.2], [0.9, 0.2]]", json);
            StringAssert.Contains("\"boundaries_radial_m\": [" + slabs[1].OwnFromM.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + ", ", json);
            StringAssert.Contains("\"subpixel_applied\": true", json);
            StringAssert.Contains("\"depth_shift_ignored\": false", json);
            StringAssert.DoesNotContain("Infinity", json);
        }

        [Test]
        public void Build_GridsAreOneTexelAtEachReference()
        {
            var h = Hints(); var (slabs, bands) = Plan(h);
            var o = (MiniJson.Obj)MiniJson.Parse(PixelLayerSidecar.Build(h, slabs, bands, new int[6], 4, 1, false, true));
            var refs = (System.Collections.Generic.IList<object>)o["references_m"]; var grids = (System.Collections.Generic.IList<object>)o["grids_m"];
            for (int i = 0; i < 3; i++) Assert.IsTrue(PixelLayerPlan.GridMatches((double)grids[i], (double)refs[i], TanV, H), "slab " + i);
            Assert.AreEqual(true, o["depth_shift_ignored"]);
        }

        [Test]
        public void Build_RejectsWrongShapes()
        {
            var h = Hints(); var (slabs, bands) = Plan(h);
            Assert.Throws<ArgumentException>(() => PixelLayerSidecar.Build(h, new PixelSlab[2], bands, new int[6], 4, 1, true, false));
            Assert.Throws<ArgumentException>(() => PixelLayerSidecar.Build(h, slabs, bands, new int[4], 4, 1, true, false));
        }

        [Test]
        public void Build_IsDeterministic_AndBothMainDepthsPlanFromTheSameBoundaries()
        {
            var a = Hints(664.874); var b = Hints(663.0);
            var (sa, ba) = Plan(a); var (sb, bb) = Plan(b);
            Assert.AreEqual(PixelLayerSidecar.Build(a, sa, ba, new int[6], 4, 1, true, false), PixelLayerSidecar.Build(a, sa, ba, new int[6], 4, 1, true, false));
            Assert.AreEqual(PixelLayerPlan.Near, 0); Assert.IsTrue(sb[1].ReferenceDepthM == 663.0 && sa[1].ReferenceDepthM == (float)664.874);
        }
    }
}
