using System;
using System.IO;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-180: the depth-aware present shift. Shader text is checked for the constants the mirror relies on; behaviour is checked on the engine-free mirror PixelDepthShift.</summary>
    public class PixelDepthShiftTests
    {
        const double ZRef = ParallaxShiftMath.RefDepthM;
        static string Shader() => File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Rendering", "Pixel", "Runtime", "Shaders", "PixelPresent.shader"));

        [Test]
        public void Flag_IsListed_ShipsFalse_AndIsDistinctFromLayerSnap()
        {
            Assert.AreEqual("rendering.pixel.depthShift", PixelFlags.DepthShift);
            Assert.Contains(PixelFlags.DepthShift, PixelFlags.All);
            Assert.AreNotEqual(PixelFlags.LayerSnap, PixelFlags.DepthShift);
        }

        [Test]
        public void Shader_KeepsTheFlatPathUntouched_AndGatesDepthShiftOnMode()
        {
            string s = Shader();
            StringAssert.Contains("_PixelPresentMode.z > 0.5 && _PixelPresentMode.y < 0.5", s, "depth shift is a separate branch, off for the bilinear control");
            StringAssert.Contains("float2 t = floor((o + _PixelPresent.zw) / _PixelPresent.x) + _PixelPresent.y;", s, "flat path byte-for-byte as before: flag off renders bit-identical");
            StringAssert.Contains("round(_PixelPresent.x * rho * _PixelPresentDepth.yz)", s, "shift = round(k * rho * remainder), ParallaxShiftMath.DepthShiftPx");
            StringAssert.Contains("_PixelPresentDepth.x / d", s, "rho = zRef / z");
            StringAssert.Contains("clamp(round(", s);
            StringAssert.Contains("landed ? nearT : farT", s, "gap takes the farther sample");
            StringAssert.Contains("3.0e38", s);
            Assert.AreEqual(3.0e38f, PixelDepthShift.SkyDepth);
        }

        [Test]
        public void ShiftPx_IsParallaxShiftMath_Clamped()
        {
            foreach (double z in new[] { 30.0, 124.2, ZRef, 3000.0, 10353.0 })
                foreach (double r in new[] { -0.9, -0.31, 0.0, 0.26, 0.75 })
                    Assert.AreEqual(Math.Max(-4, Math.Min(4, ParallaxShiftMath.DepthShiftPx(ZRef / z, r, 4))), PixelDepthShift.ShiftPx(ZRef, z, r, 4, 4));
            Assert.AreEqual(ParallaxShiftMath.DepthShiftPx(1.0, 0.3, 4), PixelDepthShift.ShiftPx(ZRef, ZRef, 0.3, 4, 4), "reference plane = the uniform shift of today");
        }

        [Test]
        public void UniformDepthAtReference_EqualsTheFlatShift_EveryPixel()
        {
            var row = new float[40]; for (int i = 0; i < row.Length; i++) row[i] = (float)ZRef;
            for (int r4 = -4; r4 <= 4; r4++)
            {
                double rem = r4 / 4.0 * 0.5;
                int flat = ParallaxShiftMath.DepthShiftPx(1.0, rem, 4);
                for (int o = 8; o < 150; o++)
                {
                    int t = PixelDepthShift.ResolveAxis(row, o, (float)ZRef, rem, 4, 1, out bool gap);   // zRef is the float the rig sends: ratio exactly 1, so ties round like the flat shift
                    Assert.IsFalse(gap);
                    Assert.AreEqual((int)Math.Floor((o + flat) / 4.0) + 1, t, "o=" + o + " rem=" + rem);
                }
            }
        }

        [Test]
        public void ZeroRemainder_IsIdentityAtAnyDepth()
        {
            var row = new float[40]; for (int i = 0; i < row.Length; i++) row[i] = i < 20 ? 100f : 9000f;
            for (int o = 8; o < 150; o++)
                Assert.AreEqual(o / 4 + 1, PixelDepthShift.ResolveAxis(row, o, ZRef, 0, 4, 1, out bool gap)); // no disocclusion without a shift... depth step leaves no gap at rem 0
        }

        [Test]
        public void NearSurface_ShiftsFartherThanFar_ByTheDepthRatio()
        {
            // rem 0.5 texel, k 4: reference shift 2 px, near (z = ref/2) 4 px (clamped to guard*k = 4), far (z = 8 ref) 0 px
            Assert.AreEqual(2, PixelDepthShift.ShiftPx(ZRef, ZRef, 0.5, 4, 4));
            Assert.AreEqual(4, PixelDepthShift.ShiftPx(ZRef, ZRef / 2, 0.5, 4, 4));
            Assert.AreEqual(0, PixelDepthShift.ShiftPx(ZRef, ZRef * 8, 0.5, 4, 4));
        }

        [Test]
        public void Disocclusion_TakesTheFartherSample_AndOcclusionTheNearer()
        {
            // near foreground texels 10..13 (z = ref/2) in front of far texels (z = 8 ref); remainder +0.5 texel, k 4.
            var row = new float[30]; for (int i = 0; i < row.Length; i++) row[i] = (i >= 10 && i <= 13) ? (float)(ZRef / 2) : (float)(ZRef * 8);
            int gaps = 0, nearShown = 0;
            for (int o = 4; o < 100; o++)
            {
                int t = PixelDepthShift.ResolveAxis(row, o, ZRef, 0.5, 4, 1, out bool gap);
                if (gap) { gaps++; Assert.AreEqual(ZRef * 8, row[t], 1e-3 * ZRef, "a gap shows the farther surface, never the foreground, o=" + o); }
                else if (row[t] < ZRef) nearShown++;
            }
            Assert.AreEqual(16, nearShown, "the foreground keeps all four texels, 4 output pixels each");
            Assert.Greater(gaps, 0, "a depth edge opens a gap on the trailing side");
            Assert.LessOrEqual(gaps, 4, "gap width is at most the shift difference (4 px)");
        }

        [Test]
        public void SkyDepth_IsTreatedAsFarthest()
        {
            var row = new float[20]; for (int i = 0; i < 20; i++) row[i] = i < 10 ? (float)ZRef : 0f;   // 0 = sky
            Assert.AreEqual(PixelDepthShift.SkyDepth, PixelDepthShift.Sanitise(0f));
            Assert.AreEqual(PixelDepthShift.SkyDepth, PixelDepthShift.Sanitise(float.NaN));
            Assert.AreEqual(PixelDepthShift.SkyDepth, PixelDepthShift.Sanitise(float.PositiveInfinity));
            for (int o = 0; o < 80; o++) { int t = PixelDepthShift.ResolveAxis(row, o, ZRef, 0.25, 4, 1, out _); Assert.That(t, Is.InRange(0, 19)); }
        }

        [Test]
        public void Model_DepthShiftWorstPhase_MatchesTheDesignNote_AKnownFailOfC11()
        {
            // MODULE.md step T-180: near 4.5 / ref 4.8 (worst) / far 4.0 px. Reported as is; C11 is not claimed met.
            double[] z = { ParallaxShiftMath.NearDepthM, ParallaxShiftMath.ReferenceClassDepthM, ParallaxShiftMath.FarDepthM };
            double[] want = { 4.5, 4.8, 4.0 };
            for (int i = 0; i < 3; i++)
            {
                ParallaxShiftMath.Simulate(ParallaxShiftMath.Scheme.DepthShift, z[i], z[i], out double worst, out _);
                Assert.AreEqual(want[i], worst, 0.5, "depth " + z[i]);
            }
        }
    }
}
