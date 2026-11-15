// Engine-free World.Seismic tests: run by Unity EditMode and by `dotnet test DotnetTests/WorldSeismic.Tests` (T-027).
using System;
using Game.Core;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class RuptureScalingTests
    {
        static void Rel(Wc94Relation r, double a, double b, double sigma, int n, double min, double max)
        {
            Assert.AreEqual(a, r.A); Assert.AreEqual(b, r.B); Assert.AreEqual(sigma, r.Sigma);
            Assert.AreEqual(n, r.N); Assert.AreEqual(min, r.Min); Assert.AreEqual(max, r.Max);
        }

        [Test]
        public void Coefficients_EqualR022Table()
        {
            Rel(Wc94StrikeSlip.MFromSrl, 5.16, 1.12, 0.28, 43, 1.3, 432);
            Rel(Wc94StrikeSlip.LogSrlFromM, -3.55, 0.74, 0.23, 43, 5.6, 8.1);
            Rel(Wc94StrikeSlip.MFromMd, 6.81, 0.78, 0.29, 43, 0.01, 14.6);
            Rel(Wc94StrikeSlip.LogMdFromM, -7.03, 1.03, 0.34, 43, 5.6, 8.1);
            Rel(Wc94StrikeSlip.MFromAd, 7.04, 0.89, 0.28, 29, 0.05, 8.0);
            Rel(Wc94StrikeSlip.LogAdFromM, -6.32, 0.90, 0.28, 29, 5.6, 8.1);
            Rel(Wc94StrikeSlip.LogWidthFromM, -0.76, 0.27, 0.14, 87, 4.8, 8.1);
            Rel(Wc94StrikeSlip.MFromArea, 3.98, 1.02, 0.23, 83, 3, 5184);
            Assert.AreEqual(0.5, Wc94StrikeSlip.AverageToMaximumRatio);
        }

        [TestCase(7.0, 42.7, 1.51, 0.95, 13.5, 914.0)]
        [TestCase(7.2, 60.0, 2.43, 1.45, 15.3, 1436.0)]
        [TestCase(7.9, 197.7, 12.8, 6.17, 23.6, 6970.0)]
        public void WorkedValues_MatchR022(double mw, double srl, double md, double ad, double w, double area)
        {
            RuptureSize s = RuptureScaling.SizeFromMw(mw);
            Assert.AreEqual(srl, s.SrlKm, srl * 0.01, "SRL");
            Assert.AreEqual(md, s.MdM, md * 0.01, "MD");
            Assert.AreEqual(ad, s.AdM, ad * 0.01, "AD");
            Assert.AreEqual(w, s.WidthKm, w * 0.01, "W");
            Assert.AreEqual(area, s.AreaKm2, area * 0.01, "Area");
        }

        [Test]
        public void Anchors_FromR022()
        {
            // 1906 San Andreas: 477 km -> M 8.15 (R-022 section 2; exact 8.16, R-022 truncates).
            Assert.AreEqual(8.15, RuptureScaling.MwFromSrlKm(477), 0.015);
            // M-054 deviation for the HayWired Hayward length (84 km, Mw 7.0): |dM_L| about 0.31 (a warn).
            // R-022 prints "+0.31"; with dM_L = Mw - M(SRL) as defined in its M-054 it is -0.31 (sign slip in the text, magnitude as stated).
            Assert.AreEqual(-0.31, 7.0 - RuptureScaling.MwFromSrlKm(84), 0.01);
        }

        [Test]
        public void RoundTrip_MagnitudeLengthMagnitude_WithinFloatTolerance()
        {
            // The two SRL fits are independent regressions (not exact inverses): M -> L -> M returns within
            // about one relation sigma (worst case 0.30 at Mw 5.6 for AD), not 1e-6. Same for displacement.
            for (double mw = 5.6; mw <= 8.1; mw += 0.1)
            {
                Assert.AreEqual(mw, RuptureScaling.MwFromSrlKm(RuptureScaling.SrlKmFromMw(mw)), 0.3, "SRL " + mw);
                Assert.AreEqual(mw, RuptureScaling.MwFromMaxDisplacementM(RuptureScaling.MaxDisplacementMFromMw(mw)), 0.35, "MD " + mw);
                Assert.AreEqual(mw, RuptureScaling.MwFromAvgDisplacementM(RuptureScaling.AvgDisplacementMFromMw(mw)), 0.4, "AD " + mw);
                // Area is the same regression both ways: exact inverse.
                Assert.AreEqual(mw, RuptureScaling.MwFromAreaKm2(RuptureScaling.AreaKm2FromMw(mw)), 1e-9, "Area " + mw);
            }
        }

        [Test]
        public void RoundTrip_LengthMagnitudeLength_IsMonotoneAndClose()
        {
            double prev = 0;
            for (double l = 1.3; l <= 432; l *= 1.3)
            {
                double back = RuptureScaling.SrlKmFromMw(RuptureScaling.MwFromSrlKm(l));
                Assert.That(back / l, Is.InRange(0.5, 2.0), "L " + l);
                Assert.Greater(RuptureScaling.MwFromSrlKm(l), prev);
                prev = RuptureScaling.MwFromSrlKm(l);
            }
        }

        [Test]
        public void Ranges()
        {
            Assert.IsTrue(RuptureScaling.MwInRange(7.2));
            Assert.IsFalse(RuptureScaling.MwInRange(5.5));
            Assert.IsFalse(RuptureScaling.MwInRange(8.2));
            Assert.IsFalse(RuptureScaling.IsExtrapolation(7.5));
            Assert.IsTrue(RuptureScaling.IsExtrapolation(7.9));
            Assert.IsTrue(Wc94StrikeSlip.MFromSrl.InRange(60));
            Assert.IsFalse(Wc94StrikeSlip.MFromSrl.InRange(500));
            Assert.AreEqual(0.5 * 2.43, RuptureScaling.AvgFromMaxDisplacementM(2.43), 1e-12);
        }

        [Test]
        public void Assumptions_AreNamedAndAsR022()
        {
            Assert.AreEqual(760f, SeismicAssumptions.Vs30RockMs);
            Assert.AreEqual(270f, SeismicAssumptions.Vs30FillMs);
            Assert.AreEqual(7.2f, SeismicAssumptions.DefaultMw);
            Assert.AreEqual(2800f, SeismicAssumptions.RuptureSpeedMs);
            Assert.AreEqual(6000f, SeismicAssumptions.PWaveSpeedMs);
            Assert.AreEqual(3500f, SeismicAssumptions.SWaveSpeedMs);
            Assert.AreEqual("seismic.enabled", SeismicFlags.Enabled);
        }

        // Compile-time proof: the Core bus only accepts unmanaged IEvent structs.
        static bool IsUnmanaged<T>() where T : unmanaged, IEvent { return true; }

        [Test]
        public void Events_AreUnmanagedBlittableStructs()
        {
            Assert.IsTrue(IsUnmanaged<QuakeBegan>());
            Assert.IsTrue(IsUnmanaged<FaultRupture>());
            Assert.IsTrue(IsUnmanaged<EmergencePoint>());
            foreach (Type t in new[] { typeof(QuakeBegan), typeof(FaultRupture), typeof(EmergencePoint), typeof(QuakeScenario) })
            {
                Assert.IsTrue(t.IsValueType, t.Name);
                Assert.IsFalse(IsReferenceOrContains(t), t.Name + " has a managed field");
            }
        }

        static bool IsReferenceOrContains(Type t)
        {
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                Type ft = f.FieldType;
                if (!ft.IsValueType) return true;
                if (!ft.IsPrimitive && !ft.IsEnum && IsReferenceOrContains(ft)) return true;
            }
            return false;
        }
    }
}
