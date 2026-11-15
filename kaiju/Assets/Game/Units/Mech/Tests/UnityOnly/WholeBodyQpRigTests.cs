using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;
using UnityEngine;

namespace Game.Units.Mech.UnityTests
{
    /// <summary>
    /// T-005 r4 step 9.4d: WholeBodyModel and WholeBodyQpDriver on the real mech rig, at node 0 of the reference row (gait_reference_qp.json), against the engine-derived quantities of the step 9.4c-2 method
    /// (M = sum J' diag(m, I_world) J from the engine Jacobian rows at EngineLink(l).index * 6; point velocities from ArticulationBody.GetPointVelocity). Tolerances fixed before the first run.
    /// </summary>
    public partial class WholeBodyProbeTests
    {
        // M_ss: the driver and the engine-derived matrix use the same Jacobian, rotations and COMs; float32 conversions only.
        const double TolMss = 1e-5;
        // point velocity: J_p qd_S against the engine point velocity; the soft ankle lock leaks 3-7 % of an ankle slot velocity into the inert slot (eLin up to 3.3e-3 over all links in step4c), so 2 % of the largest point speed.
        const double TolPointVel = 2e-2;
        // sole points of the pose against the planar reference points of node 0 (leg 42 m): 0.25 m.
        const double TolPose = 0.25;

        static WalkTuning QpWalk() => JsonUtility.FromJson<WalkTuning>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/walk_tuning_qp.json")));
        static GaitReferenceData QpReference() { var d = JsonUtility.FromJson<GaitReferenceData>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/gait_reference_qp.json"))); d.Validate(); return d; }

        /// <summary>The six DOFs of planar leg A (left) then B, and their feet, the way LocomotionController picks them.</summary>
        static void QpDofs(Rig r, out int[] dofs, out int[] feet)
        {
            var b = r.Body; dofs = new int[6]; feet = new int[2];
            string[] side = { "L", "R" };
            for (int leg = 0; leg < 2; leg++)
            {
                dofs[3 * leg] = b.FindDof("hip_" + side[leg], "flexion"); dofs[3 * leg + 1] = b.FindDof("knee_" + side[leg], "flexion"); dofs[3 * leg + 2] = b.FindDof("ankle_" + side[leg], "plantarflexion");
                Assert.That(dofs[3 * leg] >= 0 && dofs[3 * leg + 1] >= 0 && dofs[3 * leg + 2] >= 0, Is.True, "sagittal DOFs of the " + side[leg] + " leg");
                feet[leg] = -1; for (int f = 0; f < b.FootCount; f++) if (b.FootId(f) == side[leg]) feet[leg] = f;
                Assert.That(feet[leg], Is.GreaterThanOrEqualTo(0), "foot " + side[leg]);
            }
        }

        /// <summary>Node 0 of the reference row on the rig: joint angles from q9 (hip = a + th, knee = a - b, ankle = b + f), pelvis pitched by th about the lateral axis with the hip joints at (x, z) of q9. Velocities zero.</summary>
        static void PlaceAtNode0(Rig r, GaitRefNode n, int[] dofs)
        {
            var b = r.Body; double[] q = n.q9; double th = q[2]; var qa = new double[b.DofCount];
            for (int leg = 0; leg < 2; leg++)
            {
                int o = 3 + 3 * leg; double a = q[o], bb = q[o + 1], f = q[o + 2];
                qa[dofs[3 * leg]] = a + th; qa[dofs[3 * leg + 1]] = a - bb; qa[dofs[3 * leg + 2]] = bb + f;
            }
            // pelvis origin -> hip joint offset in the rest pose (sagittal part): frame data, joint hip_L minus pelvis proximal joint
            var fr = r.Frame; double[] pj = fr.segments[0].proximal_joint, hp = null; foreach (var j in fr.joints) if (j.id == "hip_L") hp = j.position;
            var off = new Vector3(0, (float)(hp[1] - pj[1]), (float)(hp[2] - pj[2])); var rot = Quaternion.Euler((float)(th * Mathf.Rad2Deg), 0, 0);
            var root = new Vector3(0, (float)q[1], (float)q[0]) - rot * off;
            r.SetPose(qa, root, rot);
        }

        static Rig NewRigAtNode0(bool gravity, bool colliders, out WholeBodyQpDriver drv, out GaitRefNode node0, out int[] dofs, out GameObject ground)
        {
            var reference = QpReference(); node0 = reference.rows[0].nodes[0]; ground = null;
            var r = new Rig(gravity); QpDofs(r, out dofs, out var feet);
            if (!colliders) NoColliders(r); else ground = BodyWorld_Ground(r);
            PlaceAtNode0(r, node0, dofs); r.Step();
            drv = new WholeBodyQpDriver(r.Body, QpWalk(), reference.rows[0], r.World.CreateQpSolver(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn), dofs, feet, Dt);
            return r;
        }
        static GameObject BodyWorld_Ground(Rig r) => Game.Physics.Body.Unity.BodyWorld.CreateGround(Tuning(), 5000f, 0f, 0f);

        [Test]
        public void RigAtNode0_MssAndPointJacobians_AgreeWithTheEngineDerivedOnes()
        {
            using (var r = NewRigAtNode0(false, false, out var drv, out var node0, out var dofs, out _))
            {
                var b = r.Body; var rng = new System.Random(7);
                // sagittal velocities only: root COM (y, z), pitch rate about +x, the six leg joints
                var qd = new double[b.DofCount]; foreach (int d in dofs) qd[d] = (rng.NextDouble() * 2 - 1) * 0.05;
                r.SetJointVelocities(qd); var root = b.EngineLink(0); root.linearVelocity = new Vector3(0, (float)(rng.NextDouble() - 0.5), 1.0f); root.angularVelocity = new Vector3(0.02f, 0, 0);
                r.Step();
                var jac = Jacobian(r, out int cols); var M = MassMatrix(r, jac, cols);
                drv.Apply(0, 0, 0);   // the QP may or may not be feasible in free fall; only the model matters here
                var cs = new int[9]; cs[0] = b.ModelBaseColumn(2); cs[1] = b.ModelBaseColumn(1); cs[2] = b.ModelBaseColumn(3); for (int j = 0; j < 6; j++) cs[3 + j] = b.ModelColumnOfDof(dofs[j]);
                var mss = drv.Model.Mss; double worst = 0; string where = "";
                for (int i = 0; i < 9; i++) for (int j = 0; j < 9; j++)
                {
                    double e = Math.Abs(mss[i * 9 + j] - M[cs[i], cs[j]]) / Math.Sqrt(Math.Abs(M[cs[i], cs[i]] * M[cs[j], cs[j]]));
                    if (e > worst) { worst = e; where = i + "," + j; }
                }
                // engine point velocity of the four sole points against J_p qd_S
                var qS = new double[9]; b.GetLink(0, out var rl); qS[0] = rl.Velocity.Z; qS[1] = rl.Velocity.Y; qS[2] = rl.AngularVelocity.X; for (int j = 0; j < 6; j++) qS[3 + j] = b.DofVelocity(dofs[j]);
                double worstV = 0, vmax = 0; var sb = new StringBuilder();
                var jp = drv.Model.Jp; var pp = drv.Model.PointPos;
                for (int p = 0; p < 4; p++)
                {
                    int link = drv.Model.PointLink[p]; var ab = b.EngineLink(link); var v = ab.GetPointVelocity(new Vector3((float)pp[3 * p], (float)pp[3 * p + 1], (float)pp[3 * p + 2]));
                    var pred = new double[3]; for (int k = 0; k < 3; k++) for (int s = 0; s < 9; s++) pred[k] += jp[p * 27 + k * 9 + s] * qS[s];
                    double err = Math.Sqrt(Sq(pred[1] - v.y) + Sq(pred[2] - v.z)); worstV = Math.Max(worstV, err); vmax = Math.Max(vmax, Math.Sqrt(Sq(v.y) + Sq(v.z)));
                    sb.Append("\n  point " + p + ": engine (y,z) = (" + v.y.ToString("F4") + ", " + v.z.ToString("F4") + ") model (" + pred[1].ToString("F4") + ", " + pred[2].ToString("F4") + ") x-velocity " + v.x.ToString("E1") + " vs " + pred[0].ToString("E1"));
                }
                Log("rig node 0 vs engine: worst M_ss entry error (relative to sqrt(Mii Mjj)) " + worst.ToString("E2") + " at " + where + "; worst point velocity error " + worstV.ToString("E2") + " m/s of max speed " + vmax.ToString("F3") + " m/s (" + (worstV / vmax).ToString("E2") + ")" + sb);
                Assert.That(worst, Is.LessThan(TolMss), "M_ss of WholeBodyModel against the engine-derived mass matrix, worst entry " + where);
                Assert.That(worstV / vmax, Is.LessThan(TolPointVel), "J_p qd_S against the engine point velocity");
            }
        }

        [Test]
        public void RigAtNode0_DriverApply_ProducesFiniteTorquesWithinTheLimits_AndTheQpIsFeasible()
        {
            GameObject ground;
            using (var r = NewRigAtNode0(true, true, out var drv, out var node0, out var dofs, out ground))
            {
                try
                {
                    var b = r.Body; bool ok = drv.Apply(0, 0, 0); var c = drv.Controller; var model = drv.Model; var pp = model.PointPos; var sb = new StringBuilder();
                    double worstPose = 0;
                    for (int k = 0; k < 4; k++) { double ex = pp[3 * k + 2] - node0.pts[2 * k], ez = pp[3 * k + 1] - node0.pts[2 * k + 1]; worstPose = Math.Max(worstPose, Math.Sqrt(ex * ex + ez * ez)); sb.Append("\n  sole point " + k + ": rig (x,z) = (" + pp[3 * k + 2].ToString("F3") + ", " + pp[3 * k + 1].ToString("F3") + ") reference (" + node0.pts[2 * k].ToString("F3") + ", " + node0.pts[2 * k + 1].ToString("F3") + ")"); }
                    var tau0 = new double[] { node0.swing.hip_tau_nm, node0.swing.knee_tau_nm, node0.swing.ankle_tau_nm, node0.stance.hip_tau_nm, node0.stance.knee_tau_nm, node0.stance.ankle_tau_nm };
                    for (int j = 0; j < 6; j++) sb.Append("\n  dof " + j + " (" + b.GetDof(dofs[j]).JointId + "): QP tau " + c.Tau[j].ToString("E3") + " limit " + b.GetDof(dofs[j]).ForceLimitNm.ToString("E2") + " reference tau " + tau0[j].ToString("E3"));
                    Log("rig node 0 Apply: ok=" + ok + " status=" + c.LastStatus + " iterations=" + c.LastIterations + " worst sole-point offset to the reference " + worstPose.ToString("F3") + " m" + sb);
                    Assert.That(c.LastStatus, Is.EqualTo(QpStatus.Optimal), "the QP is feasible at the reference pose"); Assert.That(ok, Is.True);
                    for (int j = 0; j < 6; j++)
                    {
                        Assert.That(double.IsFinite(c.Tau[j]), Is.True, "tau " + j + " finite");
                        Assert.That(Math.Abs(c.Tau[j]), Is.LessThanOrEqualTo(b.GetDof(dofs[j]).ForceLimitNm * (1 + 1e-9)), "tau " + j + " within force_limit_nm");
                    }
                    Assert.That(worstPose, Is.LessThan(TolPose), "sole points of the rig pose against the reference points of node 0 (m)");
                }
                finally { if (ground != null) UnityEngine.Object.DestroyImmediate(ground); }
            }
        }
    }
}
