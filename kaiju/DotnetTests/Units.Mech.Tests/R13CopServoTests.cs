using NUnit.Framework;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 13: the rear-CoP servo flags are off by default (control stays bit-identical).</summary>
    public class R13CopServoTests
    {
        [Test]
        public void FlagsDefaultOff()
        {
            var t = new WalkTuning();
            Assert.AreEqual(0.0, t.qp_r13_copservo);
            Assert.AreEqual(1.0, t.qp_r13_sign);
            Assert.Greater(t.qp_r13_m, 0.0);
            Assert.GreaterOrEqual(t.qp_r13_kd, 0.0);
        }
    }
}
