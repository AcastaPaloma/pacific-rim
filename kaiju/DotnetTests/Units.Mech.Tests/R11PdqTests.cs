using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 11: the QP-consistent PD retarget is off by default (legacy imp-2 PD targets, bit-identical control) and its gain scale defaults to 1.</summary>
    public class R11PdqTests
    {
        [Test]
        public void R11FlagsDefaultOff()
        {
            var t = new WalkTuning();
            Assert.AreEqual(0.0, t.qp_r11_pdq); Assert.AreEqual(1.0, t.qp_r11_pdq_k); Assert.AreEqual(0.0, t.qp_r11_pdq_mode); Assert.AreEqual(0.0, t.qp_r11_ds); Assert.AreEqual(0, Game.Units.Mech.Qp.SagittalQpOptions.From(t).R11Ds);
            t.qp_plan = 0; t.qp_r11_ds = 1; Assert.AreEqual(0, Game.Units.Mech.Qp.SagittalQpOptions.From(t).R11Ds, "needs qp_plan");
            t.qp_plan = 1; Assert.AreEqual(1, Game.Units.Mech.Qp.SagittalQpOptions.From(t).R11Ds);
        }
    }
}
