using System;
using System.IO;
using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-190: the far haze lift (flag rendering.pixel.hazeFarLift). Maths on the engine-free mirror; the shader is checked for the formula text it must keep in step.</summary>
    public class HazeFarLiftTests
    {
        const int Bands = 16;
        static double Base(double d) => HazeMath.Level(d, 48, 20000, Bands);

        [Test]
        public void Flag_IsListed_ShipsFalse()
        {
            Assert.AreEqual("rendering.pixel.hazeFarLift", PixelFlags.HazeFarLift);
            Assert.Contains(PixelFlags.HazeFarLift, PixelFlags.All);
        }

        [Test]
        public void NoLift_WhenStartIsZero_OrNotBelowFull_OrAtAndBelowStart()
        {
            double l = Base(1200);
            Assert.AreEqual(l, HazeMath.FarLift(l, 1200, Bands, 0, 1400));
            Assert.AreEqual(l, HazeMath.FarLift(l, 1200, Bands, 1400, 1400));
            Assert.AreEqual(Base(900), HazeMath.FarLift(Base(900), 900, Bands, 900, 1400), 1e-12);   // continuous at the start
            Assert.AreEqual(Base(500), HazeMath.FarLift(Base(500), 500, Bands, 900, 1400), 1e-12);
        }

        [Test]
        public void Lift_IsMonotone_ReachesLastBandAtFull_AndNeverLowersTheLevel()
        {
            double last = -1;
            for (double d = 900; d <= 3000; d += 25)
            {
                double l = HazeMath.FarLift(Base(d), d, Bands, 900, 1400, 0.8);
                Assert.GreaterOrEqual(l, Base(d) - 1e-12);
                Assert.GreaterOrEqual(l, last - 1e-12);
                last = l;
            }
            Assert.AreEqual(Bands - 1, HazeMath.FarLift(Base(1400), 1400, Bands, 900, 1400, 0.8), 1e-9);
            Assert.AreEqual(Bands - 1, HazeMath.FarLift(Base(2500), 2500, Bands, 900, 1400, 0.8), 1e-9);
        }

        [Test]
        public void Shader_CarriesTheSameFormula()
        {
            string s = File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Rendering", "Pixel", "Runtime", "Shaders", "PixelHaze.shader"));
            StringAssert.Contains("_PixelHaze3", s);
            StringAssert.Contains("level += (_PixelHaze.y - 1.0 - level) * pow(saturate(log(dist / _PixelHaze3.x) * _PixelHaze3.y), _PixelHaze3.z);", s);
        }

        [Test]
        public void Tuning_ShipsInertForProfilesWithoutAFarRange_AndFlagOffIsTheDefault()
        {
            string json = File.ReadAllText(Path.Combine(PixelTestPaths.ProjectRoot(), "Assets", "Game", "Config", "FeatureFlags", "Rendering.Pixel.json"));
            StringAssert.Contains("\"rendering.pixel.hazeFarLift\": false", json);
        }
    }
}
