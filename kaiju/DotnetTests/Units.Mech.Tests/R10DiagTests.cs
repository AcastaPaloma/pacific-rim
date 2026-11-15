using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 10: the per-step diagnostics flag and the touchdown-timing flag are off by default and need qp_plan.</summary>
    public class R10DiagTests
    {
        [Test]
        public void R10FlagsDefaultOff()
        {
            var t = new WalkTuning(); var o = SagittalQpOptions.From(t);
            Assert.AreEqual(0.0, t.qp_r10_diag); Assert.IsFalse(o.R10Diag); Assert.AreEqual(0.0, o.R10Fp); Assert.AreEqual(0.0, t.qp_r10_fp);
            t.qp_plan = 0; t.qp_r10_diag = 1; Assert.IsFalse(SagittalQpOptions.From(t).R10Diag, "diagnostics are ignored without qp_plan");
            t.qp_plan = 1; Assert.IsTrue(SagittalQpOptions.From(t).R10Diag);
        }
    }
}
