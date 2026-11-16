using NUnit.Framework;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 15: the stance half-width and legacy-reference flags are off by default (frame untouched, control bit-identical); the v7s10 suffix is selected only by qp_ref_v5 above 9.5.</summary>
    public class R15StanceTests
    {
        [Test]
        public void FlagsDefaultOff()
        {
            var t = new WalkTuning();
            Assert.AreEqual(0.0, t.body_r15_stance_m);
            Assert.AreEqual(0.0, t.qp_r15_ref);
        }
        [Test]
        public void SuffixOnlyAboveNine()
        {
            Assert.AreEqual("v5", GaitReferenceData.V5Suffix(1));
            Assert.AreEqual("v6g", GaitReferenceData.V5Suffix(9));
            Assert.AreEqual("v7s10", GaitReferenceData.V5Suffix(10));
        }
    }
}
