using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Units.Mech.UnityTests
{
    /// <summary>
    /// T-005 r4 step 9.4c: engine probes for the whole-body QP contract (IArticulatedBody v3). The mech ArticulationBody is built from mech_frame_balanced.json
    /// and body_tuning.json (gravity 0 unless a probe needs it, joints active, free base) and stepped through BodyWorld in edit mode.
    /// Each probe settles one ASSUMED part of plan section 7 risks 1-3: Jacobian layout, mass-matrix diagonal, bias sign, hip/knee/ankle sign mapping.
    /// </summary>
    public class WholeBodyProbeTests
    {
        const double Dt = 1.0 / 60.0;
        // Tolerances, fixed before the first run (not tuned afterwards).
        // J qdot: one engine step of Coriolis drift is about w dt = 0.05 / 60 = 1e-3 of the velocity scale, so 3e-3 relative to the RMS link velocity (about 3x margin).
        const double TolVel = 3e-3;
        // M diagonal: no time stepping; float32 PhysX arithmetic through a 29-DOF chain with inertia ratios up to 1e3 -> 1e-3 relative.
        const double TolMass = 1e-3;
        // gravity bias: static pose, float32; 1e-3 of the largest bias entry.
        const double TolBias = 1e-3;

        static MechFrameData Frame() => JsonUtility.FromJson<MechFrameData>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/mech_frame_balanced.json")));
        static BodyTuning Tuning() => JsonUtility.FromJson<BodyTuning>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/body_tuning.json")));

        sealed class Rig : IDisposable
        {
            public readonly BodyWorld World; public readonly ArticulatedBody Body; public readonly MechFrameData Frame; public readonly GameObject Ground;
            long _step;
            public Rig(bool gravity)
            {
                Frame = WholeBodyProbeTests.Frame(); var t = Tuning(); if (!gravity) t.gravity_mps2 = 0;
                World = new BodyWorld(t, Dt, true, null);
                Body = new ArticulatedBody(World, Frame, t, EntityId.FromValue(1), null, new BodyBuildOptions { Visuals = false, Name = "probe_mech" });
                Step();   // the engine creates the articulation on the first step
            }
            public void Step() { World.Simulate(++_step); }
            public int Slot(int dof) { int s = 0; var c = Body.GetDof(dof).ChildLink; for (int e = 0; e < dof; e++) if (Body.GetDof(e).ChildLink == c) s++; return s; }
            public void SetPose(double[] qAnat, Vector3 rootPos, Quaternion rootRot)
            {
                for (int l = 0; l < Body.LinkCount; l++)
                {
                    var ab = Body.EngineLink(l); int n = ab.dofCount; if (n == 0) continue;
                    var v = new float[3];
                    for (int d = 0; d < Body.DofCount; d++) if (Body.GetDof(d).ChildLink == l) v[Slot(d)] = (float)(Body.GetDof(d).Sign * qAnat[d]);
                    ab.jointPosition = Reduced(n, v); ab.jointVelocity = Reduced(n, new float[3]);
                }
                var root = Body.EngineLink(0); root.TeleportRoot(rootPos, rootRot); root.linearVelocity = Vector3.zero; root.angularVelocity = Vector3.zero;
            }
            public void SetJointVelocities(double[] qdAnat)
            {
                for (int l = 0; l < Body.LinkCount; l++)
                {
                    var ab = Body.EngineLink(l); int n = ab.dofCount; if (n == 0) continue;
                    var v = new float[3];
                    for (int d = 0; d < Body.DofCount; d++) if (Body.GetDof(d).ChildLink == l) v[Slot(d)] = (float)(Body.GetDof(d).Sign * qdAnat[d]);
                    ab.jointVelocity = Reduced(n, v);
                }
            }
            public void Dispose() { World.DestroyBody(Body); World.Dispose(); }
        }

        static ArticulationReducedSpace Reduced(int n, float[] v) => n == 1 ? new ArticulationReducedSpace(v[0]) : n == 2 ? new ArticulationReducedSpace(v[0], v[1]) : new ArticulationReducedSpace(v[0], v[1], v[2]);

        static double[] RandomPose(Rig r, System.Random rng, double amp)
        {
            var q = new double[r.Body.DofCount];
            for (int d = 0; d < q.Length; d++)
            {
                var info = r.Body.GetDof(d); double lo = Math.Max(info.LowerRad, -amp) + 0.03, hi = Math.Min(info.UpperRad, amp) - 0.03;
                q[d] = lo + (hi - lo) * rng.NextDouble();
            }
            return q;
        }
        static Vector3 RandVec(System.Random rng, double a) => new Vector3((float)((rng.NextDouble() * 2 - 1) * a), (float)((rng.NextDouble() * 2 - 1) * a), (float)((rng.NextDouble() * 2 - 1) * a));

        static void Log(string s) { Debug.Log("PROBE " + s); }


        [Test]
        public void JacobianDimensions_AreTheEnginesReducedCoordinates()
        {
            using (var r = new Rig(false))
            {
                var b = r.Body; var ab0 = b.EngineLink(0); r.Step();
                var g = new List<float>(); int gr = ab0.GetJointGravityForces(g); var c = new List<float>(); int cr = ab0.GetJointCoriolisCentrifugalForces(c); var st = new List<int>(); int sr = ab0.GetDofStartIndices(st);
                var sb = new StringBuilder("engine sizes: gravityRet=" + gr + " gravityLen=" + g.Count + " coriolisRet=" + cr + " len=" + c.Count + " startsRet=" + sr + " starts=[" + string.Join(",", st) + "] DofCount(active)=" + b.DofCount + " links=" + b.LinkCount + " immovable=" + ab0.immovable + "\n  per link: ");
                for (int l = 0; l < b.LinkCount; l++) { var ab = b.EngineLink(l); sb.Append(b.LinkId(l) + "(idx" + ab.index + ",dof" + ab.dofCount + ") "); }
                var jac = new double[b.LinkCount * 6 * b.ModelColumns + 64];
                Assert.That(b.GetBodyJacobian(jac, out int rows, out int cols), Is.True);
                sb.Append("\n  GetBodyJacobian -> " + rows + "x" + cols);
                Assert.That(b.ModelColumns, Is.EqualTo(g.Count), "Jacobian columns = bias length = engine reduced coordinates");
                Assert.That(cols, Is.EqualTo(g.Count)); Assert.That(rows, Is.EqualTo(6 * b.LinkCount));
                Assert.That(b.ModelColumns, Is.GreaterThanOrEqualTo(b.DofCount + 6), "inert slots of locked spherical axes make it >= DofCount + 6");
                Log(sb.ToString());
            }
        }

        // ---------------------------------------------------------------- 1. Jacobian layout
        sealed class Snap
        {
            public double[] Jac; public int Rows, Cols; public double[] QdModel; public int[] RowOf; public Quaternion[] LinkRot; public Quaternion RootRot;
            public Vector3[] VCom, VOrig, W, Com, Origin; public Vector3 RootVCom, RootVOrig, RootW;
        }

        static Snap Capture(Rig r, int seed)
        {
            var rng = new System.Random(seed); var b = r.Body;
            r.SetPose(RandomPose(r, rng, 0.25), new Vector3(0, 39, 0), Quaternion.Euler((float)(rng.NextDouble() * 40 - 20), (float)(rng.NextDouble() * 40 - 20), (float)(rng.NextDouble() * 40 - 20)));
            r.Step();   // the pose takes effect; zero velocity, zero gravity, zero drive: it stays
            var qd = new double[b.DofCount]; for (int d = 0; d < qd.Length; d++) qd[d] = (rng.NextDouble() * 2 - 1) * 0.05;
            r.SetJointVelocities(qd);
            var root = b.EngineLink(0); root.linearVelocity = RandVec(rng, 1.0); root.angularVelocity = RandVec(rng, 0.05);
            r.Step();
            var s = new Snap(); int nl = b.LinkCount;
            s.Jac = new double[nl * 6 * b.ModelColumns + 64];
            Assert.That(b.GetBodyJacobian(s.Jac, out s.Rows, out s.Cols), Is.True, "GetBodyJacobian");
            s.QdModel = new double[s.Cols]; s.RowOf = new int[nl]; s.LinkRot = new Quaternion[nl];
            s.VCom = new Vector3[nl]; s.VOrig = new Vector3[nl]; s.W = new Vector3[nl]; s.Com = new Vector3[nl]; s.Origin = new Vector3[nl];
            for (int d = 0; d < b.DofCount; d++) { int c = b.ModelColumnOfDof(d); Assert.That(c, Is.InRange(0, s.Cols - 1), "model column of dof " + d); s.QdModel[c] = b.DofVelocity(d); }
            for (int l = 0; l < nl; l++)
            {
                var ab = b.EngineLink(l); s.RowOf[l] = ab.index * 6; s.LinkRot[l] = ab.transform.rotation; s.Origin[l] = ab.transform.position; s.Com[l] = ab.worldCenterOfMass;
                s.VCom[l] = ab.linearVelocity; s.VOrig[l] = ab.GetPointVelocity(ab.transform.position); s.W[l] = ab.angularVelocity;
            }
            s.RootRot = root.transform.rotation; s.RootVCom = root.linearVelocity; s.RootVOrig = root.GetPointVelocity(root.transform.position); s.RootW = root.angularVelocity;
            return s;
        }

        static readonly string[] RefNames = { "COM", "origin" }, RowFrameNames = { "world", "link-local", "root-local" }, OrderNames = { "lin,ang", "ang,lin" }, BaseFrameNames = { "world", "root-local" }, BaseLinNames = { "rootCOM", "rootOrigin" };

        sealed class Hyp { public int Ref, RowFrame, Order, BaseFrame, BaseLin; public double ELin, EAng; public double Score => Math.Max(ELin, EAng); public override string ToString() => $"ref={RefNames[Ref]} rows={RowFrameNames[RowFrame]} base={OrderNames[Order]} basevec={BaseFrameNames[BaseFrame]} baselin={BaseLinNames[BaseLin]} eLin={ELin:E2} eAng={EAng:E2}"; }

        static Vector3 Frame3(int frame, Quaternion link, Quaternion root, Vector3 worldVec) => frame == 0 ? worldVec : Quaternion.Inverse(frame == 1 ? link : root) * worldVec;

        static List<Hyp> ScoreHypotheses(Rig r, Snap[] snaps)
        {
            var b = r.Body; var list = new List<Hyp>();
            for (int rf = 0; rf < 2; rf++) for (int fr = 0; fr < 3; fr++) for (int od = 0; od < 2; od++) for (int bf = 0; bf < 2; bf++) for (int bl = 0; bl < 2; bl++)
            {
                double nl = 0, dl = 0, na = 0, da = 0;
                foreach (var s in snaps)
                {
                    var qd = (double[])s.QdModel.Clone();
                    Vector3 lin = bl == 0 ? s.RootVCom : s.RootVOrig; if (bf == 1) { lin = Quaternion.Inverse(s.RootRot) * lin; }
                    Vector3 ang = bf == 1 ? Quaternion.Inverse(s.RootRot) * s.RootW : s.RootW;
                    double[] bv = od == 0 ? new double[] { lin.x, lin.y, lin.z, ang.x, ang.y, ang.z } : new double[] { ang.x, ang.y, ang.z, lin.x, lin.y, lin.z };
                    for (int k = 0; k < 6; k++) qd[b.ModelBaseColumn(k)] = bv[k];
                    for (int l = 0; l < b.LinkCount; l++)
                    {
                        int row = s.RowOf[l]; double[] p = new double[6];
                        for (int c = 0; c < s.Cols; c++) { double q = qd[c]; if (q == 0) continue; for (int k = 0; k < 6; k++) p[k] += s.Jac[(row + k) * s.Cols + c] * q; }
                        Vector3 tl = Frame3(fr, s.LinkRot[l], s.RootRot, rf == 0 ? s.VCom[l] : s.VOrig[l]), ta = Frame3(fr, s.LinkRot[l], s.RootRot, s.W[l]);
                        nl += Sq(p[0] - tl.x) + Sq(p[1] - tl.y) + Sq(p[2] - tl.z); dl += tl.sqrMagnitude;
                        na += Sq(p[3] - ta.x) + Sq(p[4] - ta.y) + Sq(p[5] - ta.z); da += ta.sqrMagnitude;
                    }
                }
                list.Add(new Hyp { Ref = rf, RowFrame = fr, Order = od, BaseFrame = bf, BaseLin = bl, ELin = Math.Sqrt(nl / Math.Max(dl, 1e-30)), EAng = Math.Sqrt(na / Math.Max(da, 1e-30)) });
            }
            list.Sort((x, y) => x.Score.CompareTo(y.Score));
            return list;
        }
        static double Sq(double x) => x * x;

        [Test, Explicit("step 9.4c probe still open, see evidence/T-005/round4/step4c.md")]
        public void JacobianLayout_IsSettledByJqdotAgainstLinkVelocities()
        {
            using (var r = new Rig(false))
            {
                var b = r.Body;
                var snaps = new[] { Capture(r, 11), Capture(r, 12), Capture(r, 13) };
                var ab0 = b.EngineLink(0); var starts = new List<int>(); ab0.GetDofStartIndices(starts); var g = new List<float>(); ab0.GetJointGravityForces(g);
                var sb = new StringBuilder("jacobian layout: rows=" + snaps[0].Rows + " cols=" + snaps[0].Cols + " ModelColumns=" + b.ModelColumns + " biasLen=" + g.Count + " immovable=" + ab0.immovable + " dofCount(root)=" + ab0.dofCount + " starts=[" + string.Join(",", starts) + "]\n  linkRow=[");
                for (int l = 0; l < b.LinkCount; l++) sb.Append(b.LinkId(l) + ":" + snaps[0].RowOf[l] / 6 + " ");
                sb.Append("]\n  dofColumns=["); for (int d = 0; d < b.DofCount; d++) sb.Append(b.ModelColumnOfDof(d) + " ");
                sb.Append("] baseColumns=["); for (int k = 0; k < 6; k++) sb.Append(b.ModelBaseColumn(k) + " "); sb.Append("]");
                // diagnostic: consistency of the engine's two velocity readouts, v_origin = v_com + w x (origin - com)
                double worst = 0; foreach (var s in snaps) for (int l = 0; l < b.LinkCount; l++) worst = Math.Max(worst, (s.VOrig[l] - (s.VCom[l] + Vector3.Cross(s.W[l], s.Origin[l] - s.Com[l]))).magnitude / (s.VCom[l].magnitude + 1e-6));
                sb.Append("\n  |v_orig - (v_com + w x (o - c))| worst rel = " + worst.ToString("E2"));
                var hyps = ScoreHypotheses(r, snaps);
                for (int i = 0; i < 6; i++) sb.Append("\n  #" + i + " " + hyps[i]);
                Log(sb.ToString());
                Assert.That(hyps[0].Score, Is.LessThan(TolVel), "no layout hypothesis reproduces the link velocities: " + hyps[0]);
                Assert.That(hyps[1].Score, Is.GreaterThan(10 * hyps[0].Score), "the best hypothesis is not unique: " + hyps[0] + " vs " + hyps[1]);
            }
        }

        // ---------------------------------------------------------------- 2. mass matrix diagonal
        [Test, Explicit("step 9.4c probe still open, see evidence/T-005/round4/step4c.md")]
        public void MassMatrixDiagonal_FromJacobians_MatchesEngineTorqueForUnitAcceleration()
        {
            using (var r = new Rig(false))
            {
                var b = r.Body; var s = Capture(r, 21); int nl = b.LinkCount;
                var engine = new double[b.DofCount]; for (int d = 0; d < engine.Length; d++) engine[d] = b.DofTorqueForAcceleration(d, 1.0);
                var sb = new StringBuilder("mass diagonal (anatomical SI), engine = DofTorqueForAcceleration(d,1):");
                var results = new List<KeyValuePair<double, string>>(); 
                for (int rf = 0; rf < 2; rf++) for (int fr = 0; fr < 3; fr++)
                {
                    double worst = 0; var ratio = new StringBuilder();
                    for (int d = 0; d < b.DofCount; d++)
                    {
                        int c = b.ModelColumnOfDof(d); double m = 0;
                        for (int l = 0; l < nl; l++)
                        {
                            int row = s.RowOf[l]; var seg = r.Frame.segments[l];
                            Vector3 jl = new Vector3((float)s.Jac[row * s.Cols + c], (float)s.Jac[(row + 1) * s.Cols + c], (float)s.Jac[(row + 2) * s.Cols + c]);
                            Vector3 ja = new Vector3((float)s.Jac[(row + 3) * s.Cols + c], (float)s.Jac[(row + 4) * s.Cols + c], (float)s.Jac[(row + 5) * s.Cols + c]);
                            if (fr == 1) { jl = s.LinkRot[l] * jl; ja = s.LinkRot[l] * ja; } else if (fr == 2) { jl = s.RootRot * jl; ja = s.RootRot * ja; }
                            Vector3 vcom = rf == 0 ? jl : jl + Vector3.Cross(ja, s.Com[l] - s.Origin[l]);
                            Vector3 jal = Quaternion.Inverse(s.LinkRot[l]) * ja;
                            double iw = seg.inertia_principal_kgm2[0] * Sq(jal.x) + seg.inertia_principal_kgm2[1] * Sq(jal.y) + seg.inertia_principal_kgm2[2] * Sq(jal.z);
                            m += seg.mass_kg * vcom.sqrMagnitude + iw;
                        }
                        double rel = Math.Abs(m - engine[d]) / Math.Abs(engine[d]); worst = Math.Max(worst, rel);
                        if (d < 12) ratio.Append((m / engine[d]).ToString("F4") + " ");
                    }
                    results.Add(new KeyValuePair<double, string>(worst, "ref=" + RefNames[rf] + " rows=" + RowFrameNames[fr] + " worst rel err=" + worst.ToString("E2") + " ratio(first 12 dofs)= " + ratio));
                }
                results.Sort((x, y) => x.Key.CompareTo(y.Key));
                foreach (var kv in results) sb.Append("\n  " + kv.Value);
                sb.Append("\n  engine diag: "); for (int d = 0; d < engine.Length; d++) sb.Append(b.GetDof(d).JointId + "." + b.GetDof(d).Name + "=" + engine[d].ToString("E3") + " ");
                Log(sb.ToString());
                Assert.That(results[0].Key, Is.LessThan(TolMass), "no (reference point, row frame) hypothesis reproduces the engine's diagonal: " + results[0].Value);
            }
        }

        // ---------------------------------------------------------------- 3. bias sign
        [Test, Explicit("step 9.4c probe still open, see evidence/T-005/round4/step4c.md")]
        public void BiasForce_AtRest_IsMinusTheGravityGeneralisedForce_AndEqualsDofGravityTorque()
        {
            using (var r = new Rig(true))
            {
                var b = r.Body; var rng = new System.Random(31);
                r.SetPose(RandomPose(r, rng, 0.25), new Vector3(0, 39, 0), Quaternion.Euler(10, -15, 5)); r.Step();
                // gravity stays on through the one pose step (the engine bias uses the gravity of the last step; zeroing it made h = 0 in run 3): v = g dt = 0.16 m/s, Coriolis terms are second order
                int nl = b.LinkCount; var jac = new double[nl * 6 * b.ModelColumns + 64]; Assert.That(b.GetBodyJacobian(jac, out int rows, out int cols), Is.True);
                var h = new double[cols]; b.GetBiasForce(h);
                var com = new Vector3[nl]; var org = new Vector3[nl]; var rowOf = new int[nl]; var rot = new Quaternion[nl];
                for (int l = 0; l < nl; l++) { var ab = b.EngineLink(l); rowOf[l] = ab.index * 6; com[l] = ab.worldCenterOfMass; org[l] = ab.transform.position; rot[l] = ab.transform.rotation; }
                // the row convention (reference point, frame) comes from the Jacobian probe; here both reference points are tried and the better one is reported
                var sb = new StringBuilder("bias at rest: cols=" + cols);
                double hmax = 0; for (int c = 0; c < cols; c++) hmax = Math.Max(hmax, Math.Abs(h[c]));
                double bestErr = double.MaxValue; string bestName = "";
                for (int rf = 0; rf < 2; rf++) for (int fr = 0; fr < 3; fr++)
                {
                    double err = 0;
                    for (int c = 0; c < cols; c++)
                    {
                        double q = 0;
                        for (int l = 0; l < nl; l++)
                        {
                            int row = rowOf[l];
                            Vector3 jl = new Vector3((float)jac[row * cols + c], (float)jac[(row + 1) * cols + c], (float)jac[(row + 2) * cols + c]);
                            Vector3 ja = new Vector3((float)jac[(row + 3) * cols + c], (float)jac[(row + 4) * cols + c], (float)jac[(row + 5) * cols + c]);
                            if (fr == 1) { jl = rot[l] * jl; ja = rot[l] * ja; } else if (fr == 2) { jl = rot[0] * jl; ja = rot[0] * ja; }
                            Vector3 vcom = rf == 0 ? jl : jl + Vector3.Cross(ja, com[l] - org[l]);
                            q += r.Frame.segments[l].mass_kg * -9.81 * vcom.y;   // generalised gravity force
                        }
                        err = Math.Max(err, Math.Abs(h[c] + q));
                    }
                    sb.Append("\n  ref=" + RefNames[rf] + " rows=" + RowFrameNames[fr] + " max|h + Q_gravity|/max|h| = " + (err / hmax).ToString("E2"));
                    if (err < bestErr) { bestErr = err; bestName = RefNames[rf] + "/" + RowFrameNames[fr]; }
                }
                double dofErr = 0; for (int d = 0; d < b.DofCount; d++) dofErr = Math.Max(dofErr, Math.Abs(h[b.ModelColumnOfDof(d)] - b.DofGravityTorque(d)));
                sb.Append("\n  best=" + bestName + " relErr=" + (bestErr / hmax).ToString("E2") + "; max|h[col(d)] - DofGravityTorque(d)|/max|h| = " + (dofErr / hmax).ToString("E2") + "; h[base]=");
                for (int k = 0; k < 6; k++) sb.Append(h[b.ModelBaseColumn(k)].ToString("E3") + " ");
                sb.Append(" (total weight " + (b.TotalMassKg * 9.81).ToString("E3") + ")");
                Log(sb.ToString());
                Assert.That(bestErr / hmax, Is.LessThan(TolBias), "h is not minus the gravity generalised force under any row convention");
                Assert.That(dofErr / hmax, Is.LessThan(TolBias), "GetBiasForce DOF entries differ from DofGravityTorque");
            }
        }

        // ---------------------------------------------------------------- 4. sign / DOF mapping of the sagittal leg joints
        [Test]
        public void SagittalLegDofs_FollowThePlanarConvention_PerJointProbe()
        {
            const double th = 0.2; var sb = new StringBuilder("sagittal sign probes (anatomical +" + th + " rad on one DOF, all else 0, gravity 0):");
            foreach (string side in new[] { "L", "R" })
            {
                using (var r = new Rig(false))
                {
                    var b = r.Body; var f = r.Frame;
                    int thigh = b.FindLink("thigh_" + side), shank = b.FindLink("shank_" + side), foot = b.FindLink("foot_" + side);
                    int hip = b.FindDof("hip_" + side, "flexion"), knee = b.FindDof("knee_" + side, "flexion"), ankle = b.FindDof("ankle_" + side, "plantarflexion");
                    Assert.That(new[] { hip, knee, ankle }, Has.All.GreaterThanOrEqualTo(0), "sagittal DOFs exist");
                    Vector3 Pt(int link, double[] p) { var s = f.segments[link]; return b.EngineLink(link).transform.TransformPoint(new Vector3((float)(p[0] - s.proximal_joint[0]), (float)(p[1] - s.proximal_joint[1]), (float)(p[2] - s.proximal_joint[2]))); }
                    double[] toe = { f.segments[foot].proximal_joint[0], f.segments[foot].proximal_joint[1] - f.foot_sole.ankle_height_m, f.foot_sole.toe_z_m };
                    var zero = new double[b.DofCount];
                    void Pose(int dof) { var q = new double[b.DofCount]; if (dof >= 0) q[dof] = th; r.SetPose(q, new Vector3(0, 39, 0), Quaternion.identity); r.Step(); }
                    Pose(-1);
                    Vector3 thighEnd0 = Pt(thigh, f.segments[thigh].distal_end), shankEnd0 = Pt(shank, f.segments[shank].distal_end), toe0 = Pt(foot, toe);
                    Pose(hip); Vector3 thighEnd1 = Pt(thigh, f.segments[thigh].distal_end);
                    Pose(knee); Vector3 shankEnd1 = Pt(shank, f.segments[shank].distal_end);
                    Pose(ankle); Vector3 toe1 = Pt(foot, toe);
                    double lt = f.segments[thigh].proximal_joint[1] - f.segments[thigh].distal_end[1], ls = f.segments[shank].proximal_joint[1] - f.segments[shank].distal_end[1];
                    double dzHip = thighEnd1.z - thighEnd0.z, dzKnee = shankEnd1.z - shankEnd0.z, dyToe = toe1.y - toe0.y;
                    double expHip = lt * Math.Sin(th), expKnee = -ls * Math.Sin(th);
                    double expToe = (-f.foot_sole.ankle_height_m) * Math.Cos(th) - f.foot_sole.toe_z_m * Math.Sin(th) + f.foot_sole.ankle_height_m;
                    sb.Append("\n  " + side + ": hip +flexion: thigh end dz=" + dzHip.ToString("F4") + " (planar a>0 => forward, expect " + expHip.ToString("F4") + "); knee +flexion: shank end dz=" + dzKnee.ToString("F4") + " (planar b=a-knee<0 => back, expect " + expKnee.ToString("F4") + "); ankle +plantar: toe dy=" + dyToe.ToString("F4") + " (planar f>0 => toe down, expect " + expToe.ToString("F4") + ")");
                    Assert.That(dzHip, Is.EqualTo(expHip).Within(0.02 * Math.Abs(expHip)), side + " hip: +flexion = thigh forward = planar a + th with th = 0");
                    Assert.That(dzKnee, Is.EqualTo(expKnee).Within(0.02 * Math.Abs(expKnee)), side + " knee: +flexion = shank backward = planar a - b with a = 0, b < 0");
                    Assert.That(dyToe, Is.EqualTo(expToe).Within(0.02 * Math.Abs(expToe)), side + " ankle: +plantarflexion = toe down = planar b + f with b = 0, f > 0");
                    // torque direction: a positive anatomical feed-forward torque accelerates the DOF positively (one step, gravity 0)
                    foreach (int d in new[] { hip, knee, ankle })
                    {
                        Pose(-1); b.SetDofFeedForward(d, 1e8); r.Step(); b.SetDofFeedForward(d, 0);
                        double qd = b.DofVelocity(d), q = b.DofPosition(d);
                        sb.Append("\n    +1e8 N m on " + b.GetDof(d).JointId + "." + b.GetDof(d).Name + ": qd=" + qd.ToString("E3") + " q=" + q.ToString("E3") + " (free-base 1/M_ii bound " + (1e8 / b.DofTorqueForAcceleration(d, 1.0) * Dt).ToString("E3") + ")");
                        Assert.That(qd, Is.GreaterThan(0), side + " " + b.GetDof(d).JointId + ": positive anatomical torque must give positive anatomical velocity");
                    }
                }
            }
            Log(sb.ToString());
        }
    }
}
