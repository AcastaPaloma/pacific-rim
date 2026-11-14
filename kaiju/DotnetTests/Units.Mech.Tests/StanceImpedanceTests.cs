using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Round 2 step 2: joint-space stance impedance, engine-free pieces (config defaults, gain lookup, CoP range).</summary>
    public class StanceImpedanceTests
    {
        [Test]
        public void ImpOff_ByDefault_AndFlatCopRangeIsTheLegacySole()
        {
            var t = Repo.Walk();
            Assert.That(t.imp, Is.EqualTo(0), "the shipped controller stays on the legacy path");
            Assert.That(t.imp_vf_scale, Is.EqualTo(1.0));
            StanceImpedance.CopRange(FootSupport.Flat, 4.5f, 5f, out float lo, out float hi);
            Assert.That(lo, Is.EqualTo(-4.5f)); Assert.That(hi, Is.EqualTo(4.5f));   // the legacy clamp: +-sole half length less its margin
            StanceImpedance.CopRange(FootSupport.Swing, 4.5f, 5f, out lo, out hi);
            Assert.That(hi, Is.EqualTo(4.5f));
        }

        [Test]
        public void GainLookup_ReturnsConfiguredGains_ForHeelFlatToe()
        {
            var t = Repo.Walk();
            t.imp_hip_hz_heel = 1; t.imp_hip_hz_flat = 2; t.imp_hip_hz_toe = 3; t.imp_hip_zeta_heel = .1; t.imp_hip_zeta_flat = .2; t.imp_hip_zeta_toe = .3;
            t.imp_knee_hz_heel = 4; t.imp_knee_hz_flat = 5; t.imp_knee_hz_toe = 6; t.imp_knee_zeta_heel = .4; t.imp_knee_zeta_flat = .5; t.imp_knee_zeta_toe = .6;
            t.imp_ankle_hz_heel = 7; t.imp_ankle_hz_flat = 8; t.imp_ankle_hz_toe = 9; t.imp_ankle_zeta_heel = .7; t.imp_ankle_zeta_flat = .8; t.imp_ankle_zeta_toe = .9;
            var sup = new[] { FootSupport.Heel, FootSupport.Flat, FootSupport.Toe };
            for (int j = 0; j < 3; j++)
                for (int s = 0; s < 3; s++)
                {
                    StanceImpedance.Gains(t, j, sup[s], out double hz, out double zeta);
                    Assert.That(hz, Is.EqualTo(1 + 3 * j + s), $"hz joint {j} support {sup[s]}");
                    Assert.That(zeta, Is.EqualTo(0.1 * (1 + 3 * j + s)).Within(1e-12), $"zeta joint {j} support {sup[s]}");
                }
            StanceImpedance.Gains(t, StanceImpedance.Knee, FootSupport.Swing, out double h, out _);
            Assert.That(h, Is.EqualTo(5), "a leg the reference has lifted is treated as flat");
            StanceImpedance.PdGains(2, 0.5, 1, out double k, out double c);
            double w = 2 * System.Math.PI * 0.5;
            Assert.That(k, Is.EqualTo(2 * w * w).Within(1e-9)); Assert.That(c, Is.EqualTo(4 * w).Within(1e-9));
        }

        [Test]
        public void Shipped_GainsAreAllPositive()
        {
            var t = Repo.Walk();
            foreach (var s in new[] { FootSupport.Heel, FootSupport.Flat, FootSupport.Toe })
                for (int j = 0; j < 3; j++) { StanceImpedance.Gains(t, j, s, out double hz, out double z); Assert.That(hz, Is.GreaterThan(0)); Assert.That(z, Is.GreaterThan(0)); }
        }

        [Test]
        public void CopRange_ToeReachesToeLine_FlatStaysInside_HeelReachesHeelLine()
        {
            StanceImpedance.CopRange(FootSupport.Toe, 4.5f, 5f, out float lo, out float hi);
            Assert.That(hi, Is.EqualTo(5f), "toe line"); Assert.That(lo, Is.EqualTo(-4.5f));
            StanceImpedance.CopRange(FootSupport.Flat, 4.5f, 5f, out lo, out hi);
            Assert.That(hi, Is.LessThan(5f)); Assert.That(lo, Is.GreaterThan(-5f));
            StanceImpedance.CopRange(FootSupport.Heel, 4.5f, 5f, out lo, out hi);
            Assert.That(lo, Is.EqualTo(-5f), "heel line"); Assert.That(hi, Is.EqualTo(4.5f));
        }
    }
}
