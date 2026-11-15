// T-024 step 5: Hazus code era from year_built. Rows are from Hazus 6.1 fragility.csv [S9]; cut-offs from USGS ShakeCast Appendix D [S4].
using Game.Core;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class CodeEraTests
    {
        [TestCase(0, CodeEra.ModerateCode)]
        [TestCase(-5, CodeEra.ModerateCode)]
        [TestCase(1872, CodeEra.PreCode)]
        [TestCase(1940, CodeEra.PreCode)]
        [TestCase(1941, CodeEra.ModerateCode)]
        [TestCase(1975, CodeEra.ModerateCode)]
        [TestCase(1976, CodeEra.HighCode)]
        [TestCase(2020, CodeEra.HighCode)]
        public void EraFromYear(int year, CodeEra expected) { Assert.AreEqual(expected, DamageParameters.EraFromYear(year)); }

        [Test]
        public void C2M_PcAndHcRows_MatchR021()
        {
            double[] pc = { 0.0021, 0.0041, 0.0105, 0.0267 }, hc = { 0.0027, 0.0067, 0.02, 0.0533 };
            for (int k = 1; k <= 4; k++)
            {
                Assert.AreEqual(pc[k - 1], DamageParameters.DriftMedian(HazusType.C2M, k, CodeEra.PreCode), 1e-12);
                Assert.AreEqual(hc[k - 1], DamageParameters.DriftMedian(HazusType.C2M, k, CodeEra.HighCode), 1e-12);
            }
        }

        [Test]
        public void TwoArgDriftMedian_IsModerateCode()
        {
            foreach (HazusType t in new[] { HazusType.W1, HazusType.C2L, HazusType.C2M, HazusType.C2H })
                for (int k = 1; k <= 4; k++)
                    Assert.AreEqual(DamageParameters.DriftMedian(t, k), DamageParameters.DriftMedian(t, k, CodeEra.ModerateCode));
        }

        [Test]
        public void EraRows_AreOrdered_PcWeakest_HcStrongest_PerState()
        {
            foreach (HazusType t in new[] { HazusType.W1, HazusType.C2L, HazusType.C2M, HazusType.C2H })
                for (int k = 1; k <= 4; k++)
                {
                    double pc = DamageParameters.DriftMedian(t, k, CodeEra.PreCode), lc = DamageParameters.DriftMedian(t, k, CodeEra.LowCode);
                    double mc = DamageParameters.DriftMedian(t, k, CodeEra.ModerateCode), hc = DamageParameters.DriftMedian(t, k, CodeEra.HighCode);
                    Assert.LessOrEqual(pc, lc); Assert.LessOrEqual(lc, mc); Assert.LessOrEqual(mc, hc);
                }
        }

        [Test]
        public void W1_PreCode_UsesModerateCode_PerShakeCast()
        {
            Assert.AreEqual(CodeEra.ModerateCode, DamageParameters.EffectiveEra(HazusType.W1, CodeEra.PreCode));
            Assert.AreEqual(CodeEra.PreCode, DamageParameters.EffectiveEra(HazusType.C2M, CodeEra.PreCode));
        }

        [Test]
        public void MissingYear_IsBitIdenticalToModerateCode()
        {
            var id = BuildingId.From(BuildingSource.OsmWay, 7);
            var a = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 };
            var b = a; b.YearBuilt = 1960;   // Moderate-Code band
            var ca = CapacityModel.Compute(id, a); var cb = CapacityModel.Compute(id, b);
            Assert.AreEqual(CodeEra.ModerateCode, ca.Era);
            Assert.AreEqual(ca.E1, cb.E1); Assert.AreEqual(ca.S4, cb.S4);
            for (int k = 1; k <= 4; k++)
                Assert.AreEqual(ShakingFragility.Exceedance(a, 0.4, k), ShakingFragility.Exceedance(b, 0.4, k));
        }

        [Test]
        public void Era_ChangesCapacityAndShaking()
        {
            var id = BuildingId.From(BuildingSource.OsmWay, 7);
            var old = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600, YearBuilt = 1920 };
            var mod = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 };
            var neu = new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600, YearBuilt = 2005 };
            Assert.Less(CapacityModel.Compute(id, old).E3, CapacityModel.Compute(id, mod).E3);
            Assert.Greater(CapacityModel.Compute(id, neu).E3, CapacityModel.Compute(id, mod).E3);
            Assert.Greater(ShakingFragility.Exceedance(old, 0.4, 3), ShakingFragility.Exceedance(mod, 0.4, 3));
            Assert.Less(ShakingFragility.Exceedance(neu, 0.4, 3), ShakingFragility.Exceedance(mod, 0.4, 3));
        }
    }
}
