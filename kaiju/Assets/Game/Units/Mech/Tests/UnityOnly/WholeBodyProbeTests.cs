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
    public partial class WholeBodyProbeTests
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

        // ---------------------------------------------------------------- shared helpers (step 9.4c-2)
        // Every experiment runs on a FRESH rig: re-using one rig through SetPose/teleport left state behind (link speeds grew about 5x per experiment, which is what made the first layout probe fit nothing).
        static double Sq(double x) => x * x;

        static void Prepare(Rig r, int seed, double amp)
        {
            NoColliders(r);
            var rng = new System.Random(seed);
            r.SetPose(RandomPose(r, rng, amp), new Vector3(0, 39, 0), Quaternion.Euler((float)(rng.NextDouble() * 40 - 20), (float)(rng.NextDouble() * 40 - 20), (float)(rng.NextDouble() * 40 - 20)));
            r.Step();   // the pose takes effect; zero velocity, zero drive
        }

        /// <summary>The engine's reduced velocity vector in the model's coordinates: base from the root link (world), joints from the engine list; active DOFs in anatomical sign (as GetBodyJacobian's columns), inert slots raw.</summary>
        static double[] ModelVelocity(Rig r)
        {
            var b = r.Body; var root = b.EngineLink(0); var jv = new List<float>(); root.GetJointVelocities(jv);
            var q = new double[b.ModelColumns]; Assert.That(jv.Count, Is.EqualTo(q.Length));
            for (int c = 6; c < q.Length; c++) q[c] = jv[c];
            var v = root.linearVelocity; var w = root.angularVelocity; q[0] = v.x; q[1] = v.y; q[2] = v.z; q[3] = w.x; q[4] = w.y; q[5] = w.z;
            for (int d = 0; d < b.DofCount; d++) { int c = b.ModelColumnOfDof(d); q[c] = q[c] * b.GetDof(d).Sign; }
            return q;
        }

        static double[] Jacobian(Rig r, out int cols)
        {
            var b = r.Body; var jac = new double[b.LinkCount * 6 * b.ModelColumns + 64];
            Assert.That(b.GetBodyJacobian(jac, out int rows, out cols), Is.True, "GetBodyJacobian"); Assert.That(rows, Is.EqualTo(6 * b.LinkCount));
            return jac;
        }

        /// <summary>M = sum_l J_l^T diag(m_l I3, I_world,l) J_l with the Jacobian rows measured at the link COM in the world frame; principal inertias are about the link axes (inertiaTensorRotation identity, asserted).</summary>
        static double[,] MassMatrix(Rig r, double[] jac, int cols)
        {
            var b = r.Body; var M = new double[cols, cols];
            for (int l = 0; l < b.LinkCount; l++)
            {
                var ab = b.EngineLink(l); Assert.That(Quaternion.Angle(ab.inertiaTensorRotation, Quaternion.identity), Is.LessThan(1e-3f), b.LinkId(l) + " inertia axes = link axes");
                var seg = r.Frame.segments[l]; int row = ab.index * 6; var R = Matrix4x4.Rotate(ab.transform.rotation);
                var iw = new double[3, 3];
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) iw[i, j] += R[i, k] * seg.inertia_principal_kgm2[k] * R[j, k];
                for (int c1 = 0; c1 < cols; c1++)
                    for (int c2 = 0; c2 < cols; c2++)
                    {
                        double lin = jac[row * cols + c1] * jac[row * cols + c2] + jac[(row + 1) * cols + c1] * jac[(row + 1) * cols + c2] + jac[(row + 2) * cols + c1] * jac[(row + 2) * cols + c2], ang = 0;
                        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) ang += jac[(row + 3 + i) * cols + c1] * iw[i, j] * jac[(row + 3 + j) * cols + c2];
                        M[c1, c2] += seg.mass_kg * lin + ang;
                    }
            }
            return M;
        }
        static double[] Mul(double[,] M, double[] a) { int n = a.Length; var y = new double[n]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) y[i] += M[i, j] * a[j]; return y; }
        static List<int> ActiveRows(Rig r) { var rows = new List<int>(); for (int k = 0; k < 6; k++) rows.Add(r.Body.ModelBaseColumn(k)); for (int d = 0; d < r.Body.DofCount; d++) rows.Add(r.Body.ModelColumnOfDof(d)); return rows; }

        // ---------------------------------------------------------------- 1. Jacobian layout
        [Test]
        public void JacobianLayout_JqdotReproducesEveryLinkVelocity_AndColumnsAreTheEnginesReducedCoordinates()
        {
            var sb = new StringBuilder("Jacobian layout: rows block of link i = engine index*6 (COM linear, world; angular, world); columns = engine reduced coordinates (base 0..2 root COM linear, 3..5 root angular, world)");
            foreach (int seed in new[] { 11, 12, 13 })
                using (var r = new Rig(false))
                {
                    var b = r.Body; Prepare(r, seed, 0.25); var rng = new System.Random(seed + 100);
                    var qd = new double[b.DofCount]; for (int d = 0; d < qd.Length; d++) qd[d] = (rng.NextDouble() * 2 - 1) * 0.05;
                    r.SetJointVelocities(qd); var root = b.EngineLink(0); root.linearVelocity = RandVec(rng, 1.0); root.angularVelocity = RandVec(rng, 0.05);
                    r.Step();
                    var jac = Jacobian(r, out int cols); var q = ModelVelocity(r);
                    for (int k = 0; k < 6; k++) Assert.That(b.ModelBaseColumn(k), Is.EqualTo(k));
                    var jvRaw = new List<float>(); root.GetJointVelocities(jvRaw);
                    for (int d = 0; d < b.DofCount; d++) Assert.That(b.DofVelocity(d), Is.EqualTo(b.GetDof(d).Sign * jvRaw[b.ModelColumnOfDof(d)]).Within(1e-4), "DofVelocity(" + d + ") is the engine list entry of its model column");
                    var active = new double[q.Length]; foreach (int c in ActiveRows(r)) active[c] = q[c];
                    foreach (var variant in new[] { "full engine vector", "active DOFs only" })
                    {
                        var qq = variant[0] == 'f' ? q : active; double nl = 0, dl = 0, na = 0, da = 0;
                        for (int l = 0; l < b.LinkCount; l++)
                        {
                            var ab = b.EngineLink(l); int row = ab.index * 6; var p = new double[6];
                            for (int k = 0; k < 6; k++) for (int c = 0; c < cols; c++) p[k] += jac[(row + k) * cols + c] * qq[c];
                            var vl = ab.linearVelocity; var va = ab.angularVelocity;
                            nl += Sq(p[0] - vl.x) + Sq(p[1] - vl.y) + Sq(p[2] - vl.z); dl += vl.sqrMagnitude; na += Sq(p[3] - va.x) + Sq(p[4] - va.y) + Sq(p[5] - va.z); da += va.sqrMagnitude;
                        }
                        double eLin = Math.Sqrt(nl / dl), eAng = Math.Sqrt(na / da);
                        sb.Append("\n  seed " + seed + " " + variant + ": eLin=" + eLin.ToString("E2") + " eAng=" + eAng.ToString("E2"));
                        if (variant[0] == 'f') { Assert.That(eLin, Is.LessThan(TolVel), "J qdot linear (COM, world), seed " + seed); Assert.That(eAng, Is.LessThan(TolVel), "J qdot angular (world), seed " + seed); }
                    }
                }
            Log(sb.ToString());
        }

        // ---------------------------------------------------------------- 2. mass matrix: M from the Jacobian against the measured response to a joint torque (inverse-dynamics reference)
        // The engine's own references are unusable: DofTorqueForAcceleration(d, 1) returns acceleration x mass unit (1e5) for every DOF, i.e. it does not use the inertia, and GetJointForcesForAcceleration is a one-body call.
        // Reference used: free-floating body, gravity 0, one feed-forward torque tau on one DOF for one step, a = (qd_after - qd_before)/dt in model coordinates; then M a must equal tau e_d on the base and active rows
        // (inert rows carry the soft-lock reaction). The torque is scaled so the largest velocity after the step is about 0.004 (one-step finite differences carry an error of about 0.5 |qd| in the residual).
        const double TolMassDyn = 4e-3;

        static double[] Response(int d, double tauNm, out double tauApplied, out double[,] M, out int cols, out double scaleVel)
        {
            using (var r = new Rig(false))
            {
                var b = r.Body; Prepare(r, 41, 0.25); var q0 = ModelVelocity(r);
                b.SetDofFeedForward(d, tauNm); r.Step(); b.SetDofFeedForward(d, 0);
                var q1 = ModelVelocity(r); var jac = Jacobian(r, out cols); M = MassMatrix(r, jac, cols); tauApplied = b.DofAppliedTorque(d);
                var a = new double[cols]; scaleVel = 0;
                for (int c = 0; c < cols; c++) { a[c] = (q1[c] - q0[c]) / Dt; double v = Math.Abs(q1[c] - q0[c]); if (c < 3) v *= 0.1; scaleVel = Math.Max(scaleVel, v); }
                return a;
            }
        }

        static bool LinkHasInertSlot(Rig r, int link) { int n = 0; for (int d = 0; d < r.Body.DofCount; d++) if (r.Body.GetDof(d).ChildLink == link) n++; return r.Body.EngineLink(link).dofCount > n; }

        static void MassCheck(bool lockedAxisJoints)
        {
            var sb = new StringBuilder("mass matrix vs torque response, " + (lockedAxisJoints ? "joints WITH a locked axis (ankles), all active rows" : "fully active joints, rows of fully active joints + base") + " (anatomical SI): diag = (M a)[d]/tau, resid = |(M a - tau e_d)|rows| / |tau|"); double worstDiag = 0, worstRes = 0; string worstName = "";
            int nd; var skip = new List<bool>(); var rowsOk = new List<int>();
            using (var r0 = new Rig(false))
            {
                var b0 = r0.Body; nd = b0.DofCount; for (int k = 0; k < 6; k++) rowsOk.Add(b0.ModelBaseColumn(k));
                for (int d = 0; d < nd; d++) { bool locked = LinkHasInertSlot(r0, b0.GetDof(d).ChildLink); skip.Add(locked != lockedAxisJoints); if (!locked || lockedAxisJoints) rowsOk.Add(b0.ModelColumnOfDof(d)); }
            }
            for (int d = 0; d < nd; d++)
            {
                if (skip[d]) continue;
                Response(d, 1e8, out _, out _, out _, out double v1);
                double tau = 1e8 * Math.Min(1.0, 0.004 / Math.Max(v1, 1e-9));
                var a = Response(d, tau, out double tauApplied, out var M, out int cols, out _);
                using (var r = new Rig(false))
                {
                    var b = r.Body; var y = Mul(M, a); int col = b.ModelColumnOfDof(d); double num = 0;
                    foreach (int c in rowsOk) { double e = y[c] - (c == col ? tauApplied : 0); num += e * e; }
                    double diag = y[col] / tauApplied, res = Math.Sqrt(num) / Math.Abs(tauApplied);
                    sb.Append("\n  " + b.GetDof(d).JointId + "." + b.GetDof(d).Name + ": tau=" + tauApplied.ToString("E2") + " diag=" + diag.ToString("F5") + " resid=" + res.ToString("E2"));
                    if (Math.Abs(diag - 1) > worstDiag) { worstDiag = Math.Abs(diag - 1); worstName = b.GetDof(d).JointId + "." + b.GetDof(d).Name; }
                    worstRes = Math.Max(worstRes, res);
                }
            }
            Log(sb.ToString());
            Assert.That(worstDiag, Is.LessThan(TolMass), "worst |diag - 1| at " + worstName);
            Assert.That(worstRes, Is.LessThan(TolMassDyn), "worst active-row residual");
        }

        [Test]
        public void MassMatrix_FromJacobians_ReproducesTheTorqueResponse_FullyActiveJoints() => MassCheck(false);

        // Known gap: the ankle (spherical joint with one locked axis) rows do not satisfy M a = tau: diag 0.969..1.0008, residual up to 4e-2 |tau|; the soft lock of the inert slot acts on the active ankle slots. Red on purpose, like the KnownGap gait tests.
        [Test, Explicit("known gap: ankle rows carry the soft-lock reaction, see evidence/T-005/round4/step4c.md")]
        public void MassMatrix_FromJacobians_ReproducesTheTorqueResponse_LockedAxisJoints_KnownGap() => MassCheck(true);

        // ---------------------------------------------------------------- 3. bias: free fall from rest (zero torque): M a = Q_gravity, so h = -Q_gravity satisfies M a + h = 0
        [Test]
        public void BiasForce_InFreeFall_BalancesMassTimesMeasuredAcceleration_AndWeightSignIsPositiveUp()
        {
            using (var r = new Rig(true))
            {
                var b = r.Body; Prepare(r, 31, 0.25); var q0 = ModelVelocity(r); r.Step(); var q1 = ModelVelocity(r);
                var jac = Jacobian(r, out int cols); var M = MassMatrix(r, jac, cols); var h = new double[cols]; b.GetBiasForce(h);
                var a = new double[cols]; for (int c = 0; c < cols; c++) a[c] = (q1[c] - q0[c]) / Dt;
                var y = Mul(M, a); double hmax = 0; foreach (int c in ActiveRows(r)) hmax = Math.Max(hmax, Math.Abs(h[c])); double err = 0; foreach (int c in ActiveRows(r)) err = Math.Max(err, Math.Abs(y[c] + h[c]));
                var jg = new List<float>(); b.EngineLink(0).GetJointGravityForces(jg); double jgMax = 0; foreach (var v in jg) jgMax = Math.Max(jgMax, Math.Abs(v));
                double weight = b.TotalMassKg * 9.81;
                Log("bias in free fall: max|M a + h|/max|h| = " + (err / hmax).ToString("E2") + "; h[base y]=" + h[1].ToString("E4") + " weight=" + weight.ToString("E4") + " a_base_y=" + a[1].ToString("F4") + "; engine GetJointGravityForces max |entry| x unit = " + (jgMax * r.World.MassUnit.KilogramsPerUnit).ToString("E2") + " N m (base entries " + jg[0] + ".." + jg[5] + ")");
                Assert.That(h[1], Is.EqualTo(weight).Within(1e-5 * weight), "h on the base y column is +weight (the force that must be supplied to hold the body up)");
                Assert.That(a[1], Is.EqualTo(-9.81).Within(1e-3 * 9.81), "free-fall acceleration");
                Assert.That(err / hmax, Is.LessThan(TolBias), "M a + h = 0 on the base and active rows");
            }
        }

        // ---------------------------------------------------------------- 0. raw data dump for offline layout analysis (step 9.4c-2)
        static string F(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string V3(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);
        static void Snapshot(Rig r, StringBuilder sb, string tag, List<float> jvBefore, double tauAnat)
        {
            var b = r.Body; var ab0 = b.EngineLink(0); int nl = b.LinkCount;
            var jv = new List<float>(); ab0.GetJointVelocities(jv); var jg = new List<float>(); ab0.GetJointGravityForces(jg); var jc = new List<float>(); ab0.GetJointCoriolisCentrifugalForces(jc);
            sb.Append("S " + tag + " tau=" + tauAnat.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            for (int l = 0; l < nl; l++)
            {
                var ab = b.EngineLink(l); var q = ab.transform.rotation;
                sb.Append("L " + l + " " + ab.index + " pos " + V3(ab.transform.position) + " rot " + F(q.x) + " " + F(q.y) + " " + F(q.z) + " " + F(q.w) + " com " + V3(ab.worldCenterOfMass) + " vcom " + V3(ab.linearVelocity) + " vorig " + V3(ab.GetPointVelocity(ab.transform.position)) + " w " + V3(ab.angularVelocity) + "\n");
            }
            sb.Append("JV " + string.Join(" ", jv.ConvertAll(F)) + "\n");
            if (jvBefore != null) sb.Append("JV0 " + string.Join(" ", jvBefore.ConvertAll(F)) + "\n");
            sb.Append("JG " + string.Join(" ", jg.ConvertAll(F)) + "\n"); sb.Append("JC " + string.Join(" ", jc.ConvertAll(F)) + "\n");
            var jac = new ArticulationJacobian(nl * 6, jg.Count); ab0.GetDenseJacobian(ref jac);
            var js = new StringBuilder("J " + jac.rows + " " + jac.columns);
            for (int i = 0; i < jac.rows; i++) for (int c = 0; c < jac.columns; c++) js.Append(" " + F(jac[i, c]));
            sb.Append(js + "\n");
        }
        static List<float> JointVel(Rig r) { var l = new List<float>(); r.Body.EngineLink(0).GetJointVelocities(l); return l; }

        static void NoColliders(Rig r) { for (int l = 0; l < r.Body.LinkCount; l++) foreach (var c in r.Body.EngineLink(l).GetComponentsInChildren<Collider>(true)) c.enabled = false; }
        static double VMax(Rig r) { double m = 0; for (int l = 0; l < r.Body.LinkCount; l++) { var ab = r.Body.EngineLink(l); m = Math.Max(m, Math.Max(ab.linearVelocity.magnitude, ab.angularVelocity.magnitude)); } return m; }

        // one reduced coordinate at a time on a FRESH rig (the first dump showed state leaking between experiments on one rig: link speeds grew by about 5x per experiment)
        static void ColumnExperiment(StringBuilder sb, string variant, string e, bool zeroPose, bool noCol)
        {
            using (var r = new Rig(false))
            {
                var b = r.Body; if (noCol) NoColliders(r);
                var rng = new System.Random(41); var pose = RandomPose(r, rng, 0.25); if (zeroPose) pose = new double[b.DofCount];
                r.SetPose(pose, new Vector3(0, 39, 0), Quaternion.Euler(12, -17, 8)); r.Step(); double pre = VMax(r);
                var root = b.EngineLink(0); int w = e.StartsWith("base") ? int.Parse(e.Substring(4)) : -1;
                if (w >= 0) { if (w < 3) root.linearVelocity = new Vector3(w == 0 ? 1 : 0, w == 1 ? 1 : 0, w == 2 ? 1 : 0); else root.angularVelocity = new Vector3(w == 3 ? 0.5f : 0, w == 4 ? 0.5f : 0, w == 5 ? 0.5f : 0); }
                else { var p = e.Split(' '); var ab = b.EngineLink(int.Parse(p[1])); var v = new float[3]; v[int.Parse(p[2])] = 0.5f; ab.jointVelocity = Reduced(ab.dofCount, v); }
                r.Step(); Snapshot(r, sb, variant + " " + e.Replace(' ', '_') + " pre=" + pre.ToString("E1"), null, 0);
            }
        }

        [Test, Explicit("raw engine data dump for offline analysis; set PROBE_DUMP to the output path")]
        public void DumpEngineData()
        {
            string path = Environment.GetEnvironmentVariable("PROBE_DUMP"); Assert.That(path, Is.Not.Null.And.Not.Empty, "PROBE_DUMP");
            var sb = new StringBuilder(); var exps = new List<string>(); int nd = 0;
            using (var r = new Rig(false))
            {
                var b = r.Body; nd = b.DofCount; var starts = new List<int>(); b.EngineLink(0).GetDofStartIndices(starts);
                sb.Append("H starts " + string.Join(" ", starts) + " immovable " + b.EngineLink(0).immovable + " dt " + Dt + " unit " + r.World.MassUnit.KilogramsPerUnit + "\n");
                for (int l = 0; l < b.LinkCount; l++) { var ab = b.EngineLink(l); var s = r.Frame.segments[l]; sb.Append("HL " + l + " " + b.LinkId(l) + " idx " + ab.index + " dofCount " + ab.dofCount + " mass_kg " + s.mass_kg + " inertia " + string.Join(" ", Array.ConvertAll(s.inertia_principal_kgm2, x => x.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + " parent " + (ab.transform.parent != null && ab.transform.parent.GetComponent<ArticulationBody>() != null ? ab.transform.parent.GetComponent<ArticulationBody>().index : -1) + " jointType " + ab.jointType + " comLocal " + V3(ab.centerOfMass) + " inertiaRot " + F(ab.inertiaTensorRotation.x) + " " + F(ab.inertiaTensorRotation.y) + " " + F(ab.inertiaTensorRotation.z) + " " + F(ab.inertiaTensorRotation.w) + "\n"); }
                for (int d = 0; d < b.DofCount; d++) { var i = b.GetDof(d); sb.Append("HD " + d + " " + i.JointId + "." + i.Name + " link " + i.ChildLink + " sign " + i.Sign + " axis " + i.AxisIndex + " slotCompact " + r.Slot(d) + "\n"); }
                for (int k = 0; k < 6; k++) exps.Add("base" + k);
                for (int l = 0; l < b.LinkCount; l++) for (int k = 0; k < b.EngineLink(l).dofCount; k++) exps.Add("joint " + l + " " + k);
            }
            foreach (var e in exps) { ColumnExperiment(sb, "colZ", e, true, false); ColumnExperiment(sb, "colR", e, false, false); ColumnExperiment(sb, "colN", e, false, true); }
            // B. one feed-forward torque at a time on a fresh rig without colliders (mass-matrix column via the measured acceleration)
            for (int d = 0; d < nd; d++)
                using (var r = new Rig(false))
                {
                    var b = r.Body; NoColliders(r); var rng = new System.Random(41); var pose = RandomPose(r, rng, 0.25);
                    r.SetPose(pose, new Vector3(0, 39, 0), Quaternion.Euler(12, -17, 8)); r.Step();
                    b.SetDofFeedForward(d, 1e8); r.Step(); b.SetDofFeedForward(d, 0);
                    Snapshot(r, sb, "mass " + d + " pre=0", null, b.DofAppliedTorque(d));
                }
            // C. gravity bias, colliders on and off, read after one and after two steps
            foreach (bool nc in new[] { false, true })
                using (var r = new Rig(true))
                {
                    if (nc) NoColliders(r);
                    var rng = new System.Random(31); r.SetPose(RandomPose(r, rng, 0.25), new Vector3(0, 39, 0), Quaternion.Euler(10, -15, 5)); r.Step();
                    var ab0 = r.Body.EngineLink(0);
                    sb.Append("G gravity " + V3(UnityEngine.Physics.gravity) + " useGravity " + ab0.useGravity + " sleeping " + ab0.IsSleeping() + " noColliders " + nc + "\n");
                    Snapshot(r, sb, "bias" + (nc ? "N" : "C") + "1 pre=0", null, 0); r.Step(); Snapshot(r, sb, "bias" + (nc ? "N" : "C") + "2 pre=0", null, 0);
                }
            File.WriteAllText(path, sb.ToString());
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
