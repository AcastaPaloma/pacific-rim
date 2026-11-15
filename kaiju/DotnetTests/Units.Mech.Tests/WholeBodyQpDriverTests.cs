using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Step 9.4d: WholeBodyQpDriver against an IArticulatedBody fake (the real mech frame at its rest pose, planar joints about world x for the six sagittal leg DOFs, Jacobian rows at a PERMUTED engine index, as the engine's rows are not in link order).</summary>
    public class WholeBodyQpDriverTests
    {
        const double Dt = 1.0 / 60.0, G = 9.81;

        /// <summary>Kinematics of the rest pose: link COM velocity = v_root + w x (c - c_root) + sum over the joints above the link of x_hat x (c - p_joint); columns: base 0..5, then the six leg joints.</summary>
        sealed class FakeBody : IArticulatedBody
        {
            public readonly MechFrameData Fr; public readonly BodyTuning Tun; readonly int _n; readonly int[] _parent; readonly double[] _jac; readonly int[] _joint;   // joint link (child) of the six DOFs
            public readonly double[] Ff = new double[6]; public int FfWrites, JacCalls, BiasCalls; public int LastJacLength, LastBiasLength; public readonly List<int> FfOrder = new List<int>(64);
            public double FootNormal; public readonly double[] Limit = { 1.2e9, 8e8, 1e9, 1.2e9, 8e8, 1e9 };
            readonly FootState[] _foot = new FootState[2]; readonly int[] _dofLink = new int[6];
            public const int Cols = 12;
            public int Index(int link) => (link * 4) % _n;       // engine articulation index of a link (4 and 15 are coprime)

            public FakeBody()
            {
                Fr = Repo.Load<MechFrameData>("Mech/mech_frame_balanced.json"); Tun = Repo.Load<BodyTuning>("Mech/body_tuning.json"); Tun.gravity_mps2 = G;
                _n = Fr.segments.Length; _parent = new int[_n];
                for (int i = 0; i < _n; i++) _parent[i] = Fr.segments[i].parent == null ? -1 : FindLink(Fr.segments[i].parent);
                _joint = new[] { FindLink("thigh_L"), FindLink("shank_L"), FindLink("foot_L"), FindLink("thigh_R"), FindLink("shank_R"), FindLink("foot_R") };
                _dofLink = _joint;
                _jac = new double[_n * 6 * Cols];
                int root = 0; var c0 = Fr.segments[root].com;
                for (int i = 0; i < _n; i++)
                {
                    var c = Fr.segments[i].com; int r = Index(i) * 6;
                    double dy = c[1] - c0[1], dz = c[2] - c0[2];
                    for (int k = 0; k < 3; k++) _jac[(r + k) * Cols + k] = 1;
                    // base angular column k: e_k x (c - c_root)
                    double[] d = { c[0] - c0[0], dy, dz };
                    for (int k = 0; k < 3; k++)
                    {
                        var e = new double[3]; e[k] = 1;
                        _jac[r * Cols + 3 + k] = e[1] * d[2] - e[2] * d[1]; _jac[(r + 1) * Cols + 3 + k] = e[2] * d[0] - e[0] * d[2]; _jac[(r + 2) * Cols + 3 + k] = e[0] * d[1] - e[1] * d[0];
                        _jac[(r + 3 + k) * Cols + 3 + k] = 1;
                    }
                    for (int j = 0; j < 6; j++)
                    {
                        if (!Below(i, _joint[j])) continue;
                        var p = Fr.segments[_joint[j]].proximal_joint; double ey = c[1] - p[1], ez = c[2] - p[2];
                        // rotation about +x: x_hat x (c - p) = (0, -(c_z - p_z), (c_y - p_y)); anatomical sign as the engine: hip -1 (flexion = backward about +x), knee and ankle +1
                        double sg = j % 3 == 0 ? -1 : 1;
                        _jac[(r + 1) * Cols + 6 + j] = -sg * ez; _jac[(r + 2) * Cols + 6 + j] = sg * ey; _jac[(r + 3) * Cols + 6 + j] = sg;
                    }
                }
                for (int leg = 0; leg < 2; leg++)
                {
                    int link = FindLink(leg == 0 ? "foot_L" : "foot_R"); var s = Fr.segments[link];
                    _foot[leg] = new FootState { Link = link, Contact = true, Normal = Vector3.UnitY, Points = 4, Rotation = Quaternion.Identity };
                }
            }
            bool Below(int link, int ancestor) { for (int l = link; l >= 0; l = _parent[l]) if (l == ancestor) return true; return false; }

            public void SetLoad(double newtonsPerFoot)
            {
                FootNormal = newtonsPerFoot;
                for (int leg = 0; leg < 2; leg++) { var c = Fr.segments[_foot[leg].Link].com; _foot[leg].NormalForceN = newtonsPerFoot; _foot[leg].Cop = new Vector3((float)c[0], 0f, (float)c[2]); _foot[leg].Contact = newtonsPerFoot > 0; }
            }

            public EntityId UnitId => default; public MechFrameData Frame => Fr; public BodyTuning Tuning => Tun;
            public int LinkCount => _n; public string LinkId(int link) => Fr.segments[link].id;
            public int FindLink(string id) { for (int i = 0; i < Fr.segments.Length; i++) if (Fr.segments[i].id == id) return i; return -1; }
            public int LinkParent(int link) => _parent[link]; public double LinkMassKg(int link) => Fr.segments[link].mass_kg;
            public void GetLink(int link, out LinkState s) { var sg = Fr.segments[link]; s = new LinkState { Position = new Vector3((float)sg.proximal_joint[0], (float)sg.proximal_joint[1], (float)sg.proximal_joint[2]), Rotation = Quaternion.Identity, Com = new Vector3((float)sg.com[0], (float)sg.com[1], (float)sg.com[2]) }; }
            public int DofCount => 6;
            public DofInfo GetDof(int dof) => new DofInfo { ChildLink = _dofLink[dof], Sign = dof % 3 == 0 ? -1 : 1, ForceLimitNm = Limit[dof], LowerRad = -3, UpperRad = 3, JointId = "j" + dof, Name = "n" };
            public int FindDof(string jointId, string dofName) => -1;
            public double DofPosition(int dof) => 0; public double DofVelocity(int dof) => 0;
            public Vector3 DofAxisWorld(int dof) => Vector3.UnitX;
            public void SetDofDrive(int dof, double targetRad, double targetVelocityRps, double stiffnessNmPerRad, double dampingNmsPerRad) { }
            public void SetDofFeedForward(int dof, double torqueNm) { Ff[dof] = torqueNm; FfWrites++; if (FfOrder.Count < 60) FfOrder.Add(dof); }
            public double DofInertia(int dof, bool stanceSide) => throw new NotImplementedException();
            public void GetDofGains(int dof, bool stanceSide, out double k, out double c) => throw new NotImplementedException();
            public double DofDriveTorque(int dof) => 0; public double DofAppliedTorque(int dof) => 0; public double DofGravityTorque(int dof) => 0; public double DofCoriolisTorque(int dof) => 0;
            public double DofTorqueForAcceleration(int dof, double a) => 0;
            public double TotalMassKg => Fr.total_mass_kg; public Vector3 Com => Vector3.Zero; public Vector3 ComVelocity => Vector3.Zero;
            public int FootCount => 2; public string FootId(int foot) => foot == 0 ? "L" : "R"; public void GetFoot(int foot, out FootState state) => state = _foot[foot]; public double SoleAreaM2 => Fr.foot_sole.area_m2;
            public void AddExternalForce(int link, Vector3 f, Vector3 p) { }
            public double ExternalForceMagnitudeN => 0; public double ExternalWorkJ => 0; public int NonFootContacts => 0; public int JointSpeedClampCount => 0; public bool DofSpeedClamped(int dof) => false;
            public bool NoModel; public int ModelColumns => NoModel ? 0 : Cols; public int ModelColumnOfDof(int dof) => 6 + dof; public int ModelBaseColumn(int k) => k; public int LinkJacobianRow(int link) => Index(link) * 6;
            public bool GetBodyJacobian(double[] jac, out int rows, out int cols)
            {
                rows = _n * 6; cols = Cols; JacCalls++; LastJacLength = jac == null ? -1 : jac.Length; if (jac == null || jac.Length < rows * cols) return false;
                Array.Copy(_jac, jac, rows * cols); return true;
            }
            public void GetBiasForce(double[] h)
            {
                BiasCalls++; LastBiasLength = h.Length; Array.Clear(h, 0, h.Length);
                for (int l = 0; l < _n; l++) for (int c = 0; c < Cols; c++) h[c] += Fr.segments[l].mass_kg * G * _jac[(Index(l) * 6 + 1) * Cols + c];
            }
        }

        static GaitReferenceData Ref() { var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_qp.json"); d.Validate(); return d; }
        static WholeBodyQpDriver Make(FakeBody b)
        {
            var tun = Repo.Load<WalkTuning>("Mech/walk_tuning_qp.json");
            return new WholeBodyQpDriver(b, tun, Ref().rows[0], new GoldfarbIdnaniQp(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn), new[] { 0, 1, 2, 3, 4, 5 }, new[] { 0, 1 }, Dt);
        }

        [Test]
        public void FirstApply_SizesBuffersFromTheBody_AndFillsTheControllerInputs()
        {
            var b = new FakeBody(); b.SetLoad(b.TotalMassKg * G / 2); var d = Make(b);
            Assert.That(b.JacCalls, Is.EqualTo(0), "the constructor does not read the engine model (the articulation exists only after the first step)");
            d.Apply(0, 0, 0);
            Assert.That(b.JacCalls, Is.EqualTo(1)); Assert.That(b.BiasCalls, Is.EqualTo(1));
            Assert.That(b.LastJacLength, Is.EqualTo(b.LinkCount * 6 * FakeBody.Cols), "Jacobian buffer = rows x columns"); Assert.That(b.LastBiasLength, Is.EqualTo(FakeBody.Cols), "bias buffer = columns");
            var c = d.Controller; double mt = b.TotalMassKg;
            Assert.That(c.M[0], Is.EqualTo(mt).Within(1e-9 * mt), "M(x,x) = total mass");
            Assert.That(c.M[1 * 9 + 1], Is.EqualTo(mt).Within(1e-9 * mt), "M(z,z) = total mass");
            // weight: h on the up column = +m g
            Assert.That(c.H[1], Is.EqualTo(mt * G).Within(1e-9 * mt * G), "h(z) = +m g (the force that holds the body up)");
            // pitch about the root COM with the thighs held in space (th moves the hip DOFs 1:1): inertia about the root COM of the trunk and arms, plus the leg masses translating with the hip points, from the frame data alone
            var c0 = b.Fr.segments[0].com; double inertia = 0;
            var hip = b.Fr.segments[b.FindLink("thigh_L")].proximal_joint;   // the leg links keep their orientation but translate with the hip point
            foreach (var s in b.Fr.segments)
            {
                bool leg = s.kind == "thigh" || s.kind == "shank" || s.kind == "foot"; var at = leg ? hip : s.com;
                double dy = at[1] - c0[1], dz = at[2] - c0[2]; inertia += s.mass_kg * (dy * dy + dz * dz) + (leg ? 0 : s.inertia_principal_kgm2[0]);
            }
            Assert.That(c.M[2 * 9 + 2], Is.EqualTo(inertia).Within(1e-9 * inertia), "M(th,th) = trunk-and-arms inertia about the root COM: rows of every link read at its OWN engine index");
            for (int i = 0; i < 9; i++) for (int j = 0; j < 9; j++) Assert.That(c.M[i * 9 + j], Is.EqualTo(c.M[j * 9 + i]).Within(1e-9 * Math.Abs(c.M[i * 9 + i]) + 1e-6), "M symmetric " + i + "," + j);
            Assert.That(c.Com[1], Is.EqualTo(b.Fr.com_rest[1]).Within(1e-4), "total COM height");
            Assert.That(c.FnMeas[0] + c.FnMeas[1], Is.EqualTo(b.FootNormal).Within(1e-6 * b.FootNormal), "foot normal force split heel + toe");
            for (int k = 0; k < 4; k++) Assert.That(Math.Abs(c.Pts[2 * k + 1]), Is.LessThan(1e-9), "sole points are on the ground at the rest pose (point " + k + ")");
        }

        [Test]
        public void Apply_WritesTheSixFeedForwardsInDofOrder_FiniteAndWithinTheLimits()
        {
            var b = new FakeBody(); b.SetLoad(b.TotalMassKg * G / 2); var d = Make(b);
            int optimal = 0;
            for (int k = 0; k < 20; k++) { b.FfOrder.Clear(); int w0 = b.FfWrites; if (d.Apply(k * Dt, 0, 0)) optimal++; Assert.That(b.FfWrites - w0, Is.EqualTo(6), "tick " + k); Assert.That(b.FfOrder, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }), "write order"); }
            for (int j = 0; j < 6; j++) { Assert.That(double.IsFinite(b.Ff[j]), Is.True, "dof " + j); Assert.That(Math.Abs(b.Ff[j]), Is.LessThanOrEqualTo(b.Limit[j] * (1 + 1e-9)), "dof " + j + " within the force limit"); Assert.That(b.Ff[j], Is.EqualTo(d.Controller.Tau[j]), "the QP torque is what was written"); }
            Assert.That(d.Ticks, Is.EqualTo(20)); TestContext.WriteLine("optimal solves " + optimal + " of 20; tau " + string.Join(" ", b.Ff));
        }

        [Test]
        public void ApplyAfterWarmUp_AllocatesNothing()
        {
            var b = new FakeBody(); b.SetLoad(b.TotalMassKg * G / 2); var d = Make(b);
            for (int k = 0; k < 30; k++) d.Apply(k * Dt, 0.2, 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 30; k < 230; k++) d.Apply(k * Dt, 0.2, 0);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0), "bytes allocated over 200 Apply calls");
        }

        [Test]
        public void NoEngineModelYet_ReturnsFalseAndWritesNothing()
        {
            var b = new FakeBody { NoModel = true }; var d = Make(b);
            Assert.That(d.Apply(0, 0, 0), Is.False); Assert.That(b.FfWrites, Is.EqualTo(0)); Assert.That(b.JacCalls, Is.EqualTo(0)); Assert.That(d.Ticks, Is.EqualTo(0));
        }
    }
}
