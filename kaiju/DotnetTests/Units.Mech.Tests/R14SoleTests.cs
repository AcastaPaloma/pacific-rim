using NUnit.Framework;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 14: the body sole variant is off by default (frame untouched, control bit-identical).</summary>
    public class R14SoleTests
    {
        [Test]
        public void FlagsDefaultOff()
        {
            var t = new WalkTuning();
            Assert.AreEqual(0.0, t.body_r14_sole_m);
            Assert.AreEqual(0.0, t.body_r14_width_m);
        }
    }
}
