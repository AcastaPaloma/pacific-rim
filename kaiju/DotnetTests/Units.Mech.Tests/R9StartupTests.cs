using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>T-005 round 9: the stance-side start-up flags (qp_r9_gate, qp_r9_launch) are off by default and the node helper agrees with the round-7 one.</summary>
    public class R9StartupTests
    {
        [Test]
        public void R9FlagsDefaultOff()
        {
            var t = new WalkTuning(); var o = SagittalQpOptions.From(t);
            Assert.AreEqual(0.0, t.qp_r9_gate); Assert.AreEqual(0.0, t.qp_r9_launch);
            Assert.AreEqual(0.0, o.R9Gate); Assert.AreEqual(0.0, o.R9Launch);
            t.qp_plan = 0; t.qp_r9_gate = 0.6; t.qp_r9_launch = 1.0; var o2 = SagittalQpOptions.From(t);
            Assert.AreEqual(0.0, o2.R9Gate, "gate is ignored without qp_plan"); Assert.AreEqual(0.0, o2.R9Launch, "launch is ignored without qp_plan");
            t.qp_plan = 1; var o3 = SagittalQpOptions.From(t);
            Assert.AreEqual(0.6, o3.R9Gate); Assert.AreEqual(1.0, o3.R9Launch);
        }

        [Test]
        public void NodeDcmOffsetAtMatchesStepStartHelper()
        {
            var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_qp.json"); d.Validate();
            var qr = new QpReference(d.rows[0], 0.01, true); double om = 0.49;
            for (int par = 0; par < 2; par++) for (int pt = 0; pt < 4; pt++)
                Assert.AreEqual(qr.NodeDcmOffset(par, pt, om), qr.NodeDcmOffsetAt(par * qr.N, pt, om), 1e-12);
            Assert.AreNotEqual(qr.NodeDcmOffsetAt(0, 0, om), qr.NodeDcmOffsetAt(8, 0, om), "the offset changes along the step");
        }
    }
}
