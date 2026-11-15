using System;
using System.Collections.Generic;
using System.Text;
using Game.Core;
using Game.Physics.Body.Contracts;
using UnityEngine;
using EntityId = Game.Core.EntityId;
using NQuat = System.Numerics.Quaternion;
using NVec3 = System.Numerics.Vector3;

namespace Game.Physics.Body.Unity
{
    public sealed class BodyBuildOptions
    {
        public Vector3 Position = Vector3.zero;          // world position of the frame origin (ground, midway between the feet)
        public Quaternion Rotation = Quaternion.identity;
        public bool LockAllJoints;                       // M-006.2 / .5: rigid mech
        public bool ImmovableRoot;                       // truth scenes only (pendulum): the root link is fixed in the world
        public string[] FreeJoints;                      // truth scenes only: joints left free and unlimited although LockAllJoints is set
        public bool Visuals = true;
        public string Name = "mech";
    }

    /// <summary>
    /// F-001: the articulated body, built only from mech_frame@v1 + body_tuning@v1 (R-005 section 3.4 recipe). Implements <see cref="IArticulatedBody"/>.
    /// Engine units stay inside this class: PhysX mass unit = tuning.sim_mass_unit_kg, drive targets in degrees and deg/s (measured, see MODULE.md).
    /// </summary>
    public sealed class ArticulatedBody : IArticulatedBody
    {
        readonly BodyWorld _world;
        readonly IEventBus _bus;
        readonly double _unit, _dt;
        readonly MechFrameData _frame;
        readonly BodyTuning _tuning;
        readonly ArticulationBody[] _ab;
        readonly Transform[] _tf;
        readonly double[] _massKg;
        readonly int[] _parent;
        readonly LinkState[] _state;
        readonly int[] _linkDofStart, _linkDofCount;
        // DOFs
        DofInfo[] _dof;
        double[] _q, _qd, _tauDrive, _tauFf, _tauFfApplied, _target, _targetVel, _k, _d;
        // feet
        readonly int[] _footLink;
        readonly FootState[] _foot;
        readonly ContactHysteresis[] _hyst;
        readonly Vector3[] _footImpulse, _footMoment, _footPrevVel;
        readonly float[] _footImpulseAbs;
        readonly int[] _footPoints;
        readonly int[] _linkFoot;
        readonly bool[] _linkTouched;
        long _nextContactId = 1;
        readonly List<KeyValuePair<string, string>> _logEvents = new List<KeyValuePair<string, string>>();
        readonly Vector3 _soleCentreLocal;
        // external forces
        struct Ext { public int Link; public Vector3 Force, Point, VelBefore; }
        readonly List<Ext> _ext = new List<Ext>();
        Vector3 _prevComVel, _extSum;
        bool _hasPrev;

        public GameObject Root { get; }
        public EntityId UnitId { get; }
        public MechFrameData Frame => _frame;
        public BodyTuning Tuning => _tuning;
        public int LinkCount => _ab.Length;
        public int DofCount => _dof.Length;
        public int FootCount => _footLink.Length;
        public double TotalMassKg { get; private set; }
        public NVec3 Com { get; private set; }
        public NVec3 ComVelocity { get; private set; }
        public double SoleAreaM2 => _frame.foot_sole.area_m2;
        public double ExternalForceMagnitudeN { get; private set; }
        public double ExternalWorkJ { get; private set; }
        public int NonFootContacts { get; private set; }
        public int JointSpeedClampCount { get; private set; }
        public bool DofSpeedClamped(int dof) => _clamped[dof];
        bool[] _clamped; float[] _dofSpeedCap;
        // loading window per foot (FootLoaded)
        float[] _loadPeak, _loadImpulse; int[] _loadSteps; long[] _loadPeakStep; bool[] _loadOpen; int _loadWindowSteps;
        /// <summary>Steps x links in which the engine's DOF count differed from the config's (feed-forward skipped). Expected: only before the first physics step.</summary>
        public int DofCountMismatches { get; private set; }
        public double FOnN => _tuning.contact_on_bw * TotalMassKg * _tuning.gravity_mps2;
        public double FOffN => _tuning.contact_off_bw * TotalMassKg * _tuning.gravity_mps2;

        public ArticulatedBody(BodyWorld world, MechFrameData frame, BodyTuning tuning, EntityId unitId, IEventBus bus, BodyBuildOptions opt)
        {
            MechFrameOps.Validate(frame);
            if (tuning == null || tuning.schema != BodyTuning.Schema) throw new InvalidOperationException("body tuning schema is not " + BodyTuning.Schema);
            _world = world; _frame = frame; _tuning = tuning; UnitId = unitId; _bus = bus; _unit = world.MassUnit.KilogramsPerUnit; _dt = world.StepSeconds;
            if (Math.Abs(tuning.sim_mass_unit_kg - _unit) > 1e-9 * _unit) throw new InvalidOperationException("body tuning mass unit " + tuning.sim_mass_unit_kg + " kg differs from the physics world's " + _unit + " kg: every body in a scene must use one unit");
            int n = frame.segments.Length;
            _ab = new ArticulationBody[n]; _tf = new Transform[n]; _massKg = new double[n]; _parent = new int[n]; _state = new LinkState[n];
            _linkDofStart = new int[n]; _linkDofCount = new int[n]; _linkFoot = new int[n]; _linkTouched = new bool[n];
            var fs = frame.foot_sole;
            _soleCentreLocal = new Vector3(0, -(float)fs.ankle_height_m, (float)((fs.heel_z_m + fs.toe_z_m) * 0.5 - fs.ankle_z_m));

            Root = new GameObject(opt.Name + "_" + unitId);
            Root.transform.SetPositionAndRotation(opt.Position, opt.Rotation);
            var material = BodyWorld.FootMaterial(tuning);
            var dofs = new List<DofInfo>();
            var feet = new List<int>();
            // parents first: segments are listed root first, children after their parents in mech_frame@v1; resolve by repeated passes to be safe
            var built = new bool[n];
            for (int pass = 0, done = 0; done < n; pass++)
            {
                if (pass > n) throw new InvalidOperationException("mech frame segment hierarchy has a cycle");
                for (int i = 0; i < n; i++)
                {
                    if (built[i]) continue;
                    var s = frame.segments[i];
                    int p = string.IsNullOrEmpty(s.parent) ? -1 : MechFrameOps.Find(frame, s.parent);
                    if (p >= 0 && !built[p]) continue;
                    BuildLink(i, p, s, opt, material, dofs, feet);
                    built[i] = true; done++;
                }
            }
            _dof = dofs.ToArray();
            int nd = _dof.Length;
            _q = new double[nd]; _qd = new double[nd]; _tauDrive = new double[nd]; _tauFf = new double[nd]; _tauFfApplied = new double[nd];
            _target = new double[nd]; _targetVel = new double[nd]; _k = new double[nd]; _d = new double[nd];
            _clamped = new bool[nd]; _dofSpeedCap = new float[nd];
            for (int d = 0; d < nd; d++) _dofSpeedCap[d] = _ab[_dof[d].ChildLink].maxJointVelocity;
            _footLink = feet.ToArray();
            int nf = _footLink.Length;
            _foot = new FootState[nf]; _hyst = new ContactHysteresis[nf]; _footImpulse = new Vector3[nf]; _footMoment = new Vector3[nf]; _footPrevVel = new Vector3[nf];
            _footImpulseAbs = new float[nf]; _footPoints = new int[nf];
            _loadPeak = new float[nf]; _loadImpulse = new float[nf]; _loadSteps = new int[nf]; _loadPeakStep = new long[nf]; _loadOpen = new bool[nf];
            double legLength = MechFrameOps.LegLength(frame);
            _loadWindowSteps = Math.Max(1, (int)Math.Round(tuning.contact_load_window_tstar * Math.Sqrt(legLength / Math.Max(1e-6, tuning.gravity_mps2 > 0 ? tuning.gravity_mps2 : 9.81)) / _dt));
            for (int i = 0; i < n; i++) _linkFoot[i] = -1;
            for (int f = 0; f < nf; f++) _linkFoot[_footLink[f]] = f;
            foreach (double m in _massKg) TotalMassKg += m;
            world.Register(this);
            UnityEngine.Physics.SyncTransforms();
            ReadState();
        }

        void BuildLink(int i, int p, SegmentData s, BodyBuildOptions opt, PhysicsMaterial material, List<DofInfo> dofs, List<int> feet)
        {
            var go = new GameObject(s.id);
            Vector3 prox = V(s.proximal_joint);
            if (p < 0) { go.transform.SetParent(Root.transform, false); go.transform.localPosition = prox; }
            else { go.transform.SetParent(_tf[p], false); go.transform.localPosition = prox - V(_frame.segments[p].proximal_joint); }
            _tf[i] = go.transform; _parent[i] = p; _massKg[i] = s.mass_kg;

            // collider: the segment's envelope box (feet: the sole slab centred on the foot CoM), R-005 table 1.3
            Vector3 centre = (s.kind == "foot" ? V(s.com) : (V(s.proximal_joint) + V(s.distal_end)) * 0.5f) - prox;
            var box = go.AddComponent<BoxCollider>();
            box.center = centre; box.size = new Vector3((float)s.width_x_m, (float)s.height_y_m, (float)s.depth_z_m);
            box.material = material; box.contactOffset = (float)_tuning.contact_offset_m; box.providesContacts = true;
            _world.Register(box, this, i);
            if (s.kind == "foot") feet.Add(i);
            if (opt.Visuals) AddVisual(go.transform, s, centre, box.size);

            var ab = go.AddComponent<ArticulationBody>();
            _ab[i] = ab;
            ab.automaticCenterOfMass = false; ab.automaticInertiaTensor = false;
            ab.mass = (float)(s.mass_kg / _unit);
            ab.centerOfMass = V(s.com) - prox;
            ab.inertiaTensor = new Vector3((float)(s.inertia_principal_kgm2[0] / _unit), (float)(s.inertia_principal_kgm2[1] / _unit), (float)(s.inertia_principal_kgm2[2] / _unit));
            ab.inertiaTensorRotation = Quaternion.identity;
            ab.useGravity = true;
            ab.linearDamping = (float)_tuning.linear_damping; ab.angularDamping = (float)_tuning.angular_damping; ab.jointFriction = (float)_tuning.joint_friction;
            ab.solverIterations = _tuning.solver_iterations; ab.solverVelocityIterations = _tuning.solver_velocity_iterations;
            ab.sleepThreshold = (float)_tuning.sleep_threshold; ab.maxDepenetrationVelocity = (float)_tuning.max_depenetration_mps;
            ab.maxLinearVelocity = (float)_tuning.max_linear_velocity_mps; ab.maxAngularVelocity = (float)_tuning.max_angular_velocity_rps;
            bool ccd = _tuning.continuous_collision_kinds != null && Array.IndexOf(_tuning.continuous_collision_kinds, s.kind) >= 0;
            ab.collisionDetectionMode = ccd ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.Discrete;
            _linkDofStart[i] = dofs.Count;
            if (p < 0) { ab.immovable = opt.ImmovableRoot; return; }

            var joint = MechFrameOps.JointOfChild(_frame, s.id) ?? throw new InvalidOperationException("segment " + s.id + " has no joint to its parent");
            ab.anchorPosition = Vector3.zero; ab.anchorRotation = Quaternion.identity; ab.matchAnchors = true;
            string kind = MechFrameOps.JointKind(joint.id);
            var kt = _tuning.Kind(kind);
            var active = new DofData[3];
            bool free = opt.FreeJoints != null && Array.IndexOf(opt.FreeJoints, joint.id) >= 0;
            if (!opt.LockAllJoints || free) foreach (var d in joint.dofs) if (d.m0 == "active") active[MechFrameOps.AxisIndex(d)] = d;
            int count = (active[0] != null ? 1 : 0) + (active[1] != null ? 1 : 0) + (active[2] != null ? 1 : 0);
            bool spherical = joint.unity_joint_type.StartsWith("Spherical", StringComparison.Ordinal), revolute = joint.unity_joint_type.StartsWith("Revolute", StringComparison.Ordinal);
            if (count == 0 || (!spherical && !revolute)) { ab.jointType = ArticulationJointType.FixedJoint; return; }
            if (kt == null) throw new InvalidOperationException("body tuning has no joint kind '" + kind + "' for joint " + joint.id);
            ab.jointType = spherical ? ArticulationJointType.SphericalJoint : ArticulationJointType.RevoluteJoint;
            float vmax = (float)(kind == "hip" || kind == "knee" || kind == "ankle" ? _tuning.leg_max_joint_velocity_rps : kind == "shoulder" || kind == "elbow" ? _tuning.arm_max_joint_velocity_rps : _tuning.body_max_joint_velocity_rps);
            ab.maxJointVelocity = vmax;
            for (int a = 0; a < 3; a++)
            {
                var d = active[a];
                var lockMode = d != null ? (free ? ArticulationDofLock.FreeMotion : ArticulationDofLock.LimitedMotion) : ArticulationDofLock.LockedMotion;
                if (a == 0) ab.twistLock = lockMode; else if (!spherical) continue; else if (a == 1) ab.swingYLock = lockMode; else ab.swingZLock = lockMode;
                if (d == null) continue;
                double lo = d.rom_deg[0] * d.unity_sign, hi = d.rom_deg[1] * d.unity_sign;
                if (lo > hi) { double t = lo; lo = hi; hi = t; }
                var drive = GetDrive(ab, a);
                drive.driveType = ArticulationDriveType.Force; drive.lowerLimit = (float)lo; drive.upperLimit = (float)hi;
                drive.stiffness = 0; drive.damping = 0; drive.target = 0; drive.targetVelocity = 0; drive.forceLimit = (float)(kt.force_limit_nm / _unit);
                SetDrive(ab, a, drive);
                dofs.Add(new DofInfo { JointId = joint.id, Name = d.name, Kind = kind, ChildLink = i, AxisIndex = a, Sign = d.unity_sign, LowerRad = d.rom_deg[0] * Mathf.Deg2Rad, UpperRad = d.rom_deg[1] * Mathf.Deg2Rad, ForceLimitNm = kt.force_limit_nm });
            }
            _linkDofCount[i] = dofs.Count - _linkDofStart[i];
        }

        static void AddVisual(Transform parent, SegmentData s, Vector3 centre, Vector3 size)
        {
            var v = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(v.GetComponent<Collider>());
            v.name = "visual_" + s.id; v.transform.SetParent(parent, false); v.transform.localPosition = centre; v.transform.localScale = size;
        }

        static ArticulationDrive GetDrive(ArticulationBody ab, int axis) => axis == 0 ? ab.xDrive : axis == 1 ? ab.yDrive : ab.zDrive;
        static void SetDrive(ArticulationBody ab, int axis, ArticulationDrive d) { if (axis == 0) ab.xDrive = d; else if (axis == 1) ab.yDrive = d; else ab.zDrive = d; }
        static Vector3 V(double[] a) => new Vector3((float)a[0], (float)a[1], (float)a[2]);
        static NVec3 N(Vector3 v) => new NVec3(v.x, v.y, v.z);
        static Vector3 U(NVec3 v) => new Vector3(v.X, v.Y, v.Z);
        static NQuat N(Quaternion q) => new NQuat(q.x, q.y, q.z, q.w);

        // ---- IArticulatedBody: structure ----
        public string LinkId(int link) => _frame.segments[link].id;
        public int FindLink(string id) => MechFrameOps.Find(_frame, id);
        public int LinkParent(int link) => _parent[link];
        public double LinkMassKg(int link) => _massKg[link];
        public void GetLink(int link, out LinkState state) => state = _state[link];
        public DofInfo GetDof(int dof) => _dof[dof];
        public int FindDof(string jointId, string dofName)
        {
            for (int d = 0; d < _dof.Length; d++) if (_dof[d].JointId == jointId && _dof[d].Name == dofName) return d;
            return -1;
        }
        public double DofPosition(int dof) => _q[dof];
        public double DofVelocity(int dof) => _qd[dof];
        public NVec3 DofAxisWorld(int dof)
        {
            var info = _dof[dof];
            // measured: joint forces of a spherical joint act about the child link's joint-frame axes
            Vector3 local = info.AxisIndex == 0 ? Vector3.right : info.AxisIndex == 1 ? Vector3.up : Vector3.forward;
            return N(_tf[info.ChildLink].rotation * local) * info.Sign;
        }
        public void SetDofDrive(int dof, double targetRad, double targetVelocityRps, double stiffness, double damping)
        { _target[dof] = targetRad; _targetVel[dof] = targetVelocityRps; _k[dof] = stiffness; _d[dof] = damping; }
        public void SetDofFeedForward(int dof, double torqueNm) => _tauFf[dof] = torqueNm;
        public double DofInertia(int dof, bool stanceSide)
        {
            var info = _dof[dof]; var joint = MechFrameOps.JointOfChild(_frame, _frame.segments[info.ChildLink].id);
            return stanceSide ? MechFrameOps.ProximalInertia(_frame, joint, info.AxisIndex) : MechFrameOps.DistalInertia(_frame, joint, info.AxisIndex);
        }
        public void GetDofGains(int dof, bool stanceSide, out double stiffness, out double damping)
        {
            var kt = _tuning.Kind(_dof[dof].Kind); double inertia = DofInertia(dof, stanceSide), hz = stanceSide ? kt.stance_hz : kt.swing_hz;
            stiffness = GainRules.Stiffness(inertia, hz); damping = GainRules.Damping(inertia, hz, _tuning.damping_ratio);
        }
        public double DofDriveTorque(int dof) => _tauDrive[dof];
        public double DofAppliedTorque(int dof) => _tauDrive[dof] + _tauFfApplied[dof];

        // ---- inverse dynamics from the engine's own model (verified against the 6000.6.9f1 scripting API: GetJointGravityForces, GetJointCoriolisCentrifugalForces, GetJointForcesForAcceleration, GetDofStartIndices) ----
        readonly List<float> _idGravity = new List<float>(), _idCoriolis = new List<float>(); readonly List<int> _idStart = new List<int>();
        bool _idGravityFresh, _idCoriolisFresh, _idStartFresh;
        int IdIndex(int dof)
        {
            if (!_idStartFresh) { _ab[0].GetDofStartIndices(_idStart); _idStartFresh = true; }
            int link = _dof[dof].ChildLink, index = _ab[link].index;
            return index >= 0 && index < _idStart.Count ? _idStart[index] + dof - _linkDofStart[link] : -1;
        }
        public double DofGravityTorque(int dof)
        {
            if (!_idGravityFresh) { _ab[0].GetJointGravityForces(_idGravity); _idGravityFresh = true; }
            int i = IdIndex(dof);
            return i >= 0 && i < _idGravity.Count ? _dof[dof].Sign * _idGravity[i] * _unit : 0;
        }
        public double DofCoriolisTorque(int dof)
        {
            if (!_idCoriolisFresh) { _ab[0].GetJointCoriolisCentrifugalForces(_idCoriolis); _idCoriolisFresh = true; }
            int i = IdIndex(dof);
            return i >= 0 && i < _idCoriolis.Count ? _dof[dof].Sign * _idCoriolis[i] * _unit : 0;
        }
        public double DofTorqueForAcceleration(int dof, double accelerationRps2)
        {
            var info = _dof[dof]; var ab = _ab[info.ChildLink];
            int slots = ab.dofCount, k = dof - _linkDofStart[info.ChildLink];
            if (slots <= k) return 0;
            var acc = slots == 1 ? new ArticulationReducedSpace(0f) : slots == 2 ? new ArticulationReducedSpace(0f, 0f) : new ArticulationReducedSpace(0f, 0f, 0f);
            acc[k] = (float)(info.Sign * accelerationRps2);
            var force = ab.GetJointForcesForAcceleration(acc);
            return force.dofCount > k ? info.Sign * force[k] * _unit : 0;
        }
        // ---- whole-body model (contract v3). Verified against the 6000.6.9f1 scripting API: ArticulationBody.GetDenseJacobian(ref ArticulationJacobian) returns the element count, rows = 6 x articulation bodies (3 linear, 3 angular), columns = all joint DOFs (+6 when not immovable); ArticulationJacobian(int nRows, int nCols), indexer [row, col]; GetJointGravityForces / GetJointCoriolisCentrifugalForces(List<float>) return the total DOF count. Column order of the base block and frame are probed in step 9.5. ----
        ArticulationJacobian _jacobian; int _jacRows, _jacCols;
        int BiasOffset()
        {
            // bias vectors and Jacobian columns share one index space when the bias list carries the 6 base coordinates; otherwise the base block comes first
            if (!_idGravityFresh) { _ab[0].GetJointGravityForces(_idGravity); _idGravityFresh = true; }
            return ModelColumns - _idGravity.Count;
        }
        /// <summary>Measured (step 9.4c, 6000.6.9f1): the engine's reduced-coordinate count (bias length = Jacobian columns = 6 base + the slots of every link, 3 per spherical joint even with a locked axis), not the active DOF count; the extra slots are inert.</summary>
        public int ModelColumns { get { if (!_idGravityFresh) { _ab[0].GetJointGravityForces(_idGravity); _idGravityFresh = true; } return _idGravity.Count > 0 ? _idGravity.Count : _dof.Length + (_ab[0].immovable ? 0 : 6); } }
        public int ModelColumnOfDof(int dof) { int i = IdIndex(dof); return i < 0 ? -1 : i + BiasOffset(); }
        public int ModelBaseColumn(int k) => _ab[0].immovable ? -1 : k < 0 || k > 5 ? -1 : k;
        public bool GetBodyJacobian(double[] jac, out int rows, out int cols)
        {
            rows = _ab.Length * 6; cols = ModelColumns;
            if (jac == null || jac.Length < rows * cols) return false;
            if (_jacobian.elements == null || _jacRows != rows || _jacCols != cols) { _jacobian = new ArticulationJacobian(rows, cols); _jacRows = rows; _jacCols = cols; }
            if (_ab[0].GetDenseJacobian(ref _jacobian) <= 0) return false;
            rows = _jacobian.rows; cols = _jacobian.columns;
            if (jac.Length < rows * cols) return false;
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) jac[r * cols + c] = _jacobian[r, c];
            for (int d = 0; d < _dof.Length; d++)
            {
                int c = ModelColumnOfDof(d);
                if (c < 0 || c >= cols || _dof[d].Sign >= 0) continue;
                for (int r = 0; r < rows; r++) jac[r * cols + c] = -jac[r * cols + c];
            }
            return true;
        }
        public void GetBiasForce(double[] h)
        {
            if (!_idGravityFresh) { _ab[0].GetJointGravityForces(_idGravity); _idGravityFresh = true; }
            if (!_idCoriolisFresh) { _ab[0].GetJointCoriolisCentrifugalForces(_idCoriolis); _idCoriolisFresh = true; }
            int n = Math.Min(h.Length, Math.Min(_idGravity.Count, _idCoriolis.Count));
            for (int i = 0; i < n; i++) h[i] = (_idGravity[i] + _idCoriolis[i]) * _unit;
            for (int d = 0; d < _dof.Length; d++) { int c = ModelColumnOfDof(d); if (c >= 0 && c < n && _dof[d].Sign < 0) h[c] = -h[c]; }
        }
        public string FootId(int foot) => _frame.segments[_footLink[foot]].side ?? _frame.segments[_footLink[foot]].id;
        public void GetFoot(int foot, out FootState state) => state = _foot[foot];
        public Transform PartTransform(string segmentId) { int i = FindLink(segmentId); return i < 0 ? null : _tf[i]; }
        public ArticulationBody EngineLink(int link) => _ab[link];

        public void AddExternalForce(int link, NVec3 forceN, NVec3 worldPoint) => _ext.Add(new Ext { Link = link, Force = U(forceN), Point = U(worldPoint) });

        // ---- stepping ----
        internal void BeforeStep()
        {
            for (int i = 0; i < _ab.Length; i++)
            {
                int n = _linkDofCount[i];
                if (n == 0) continue;
                var ab = _ab[i];
                // measured (edit mode and play mode): the reduced coordinates of a link are its unlocked axes in the order twist, swing y, swing z (compact),
                // although ArticulationBody.dofCount reads 3 for a spherical joint with one axis locked. Before the engine has created the articulation the count reads 0.
                int slots = ab.dofCount;
                if (slots < n) { DofCountMismatches++; continue; }
                var jf = slots == 1 ? new ArticulationReducedSpace(0f) : slots == 2 ? new ArticulationReducedSpace(0f, 0f) : new ArticulationReducedSpace(0f, 0f, 0f);
                for (int k = 0; k < n; k++)
                {
                    int d = _linkDofStart[i] + k;
                    var info = _dof[d];
                    double ff = Math.Max(-info.ForceLimitNm, Math.Min(info.ForceLimitNm, _tauFf[d]));
                    if (double.IsNaN(ff)) ff = 0;
                    _tauFfApplied[d] = ff;
                    var drive = GetDrive(ab, info.AxisIndex);
                    drive.stiffness = (float)(_k[d] / _unit); drive.damping = (float)(_d[d] / _unit);
                    drive.target = (float)(info.Sign * _target[d] * Mathf.Rad2Deg);
                    drive.targetVelocity = (float)(info.Sign * _targetVel[d] * Mathf.Rad2Deg); // measured: deg/s, not rad/s as documented
                    drive.forceLimit = (float)(Math.Max(0, info.ForceLimitNm - Math.Abs(ff)) / _unit);
                    SetDrive(ab, info.AxisIndex, drive);
                    jf[k] = (float)(info.Sign * ff / _unit);
                }
                ab.jointForce = jf; // persistent in PhysX: rewritten every step
            }
            _extSum = Vector3.zero; double mag = 0;
            for (int e = 0; e < _ext.Count; e++)
            {
                var x = _ext[e];
                x.VelBefore = _ab[x.Link].GetPointVelocity(x.Point); _ext[e] = x;
                _ab[x.Link].AddForceAtPosition(x.Force / (float)_unit, x.Point, ForceMode.Force);
                _extSum += x.Force; mag += x.Force.magnitude;
            }
            ExternalForceMagnitudeN = mag;
            for (int f = 0; f < _footLink.Length; f++) { _footImpulse[f] = Vector3.zero; _footMoment[f] = Vector3.zero; _footImpulseAbs[f] = 0; _footPoints[f] = 0; _footPrevVel[f] = U(_foot[f].Velocity); }
            Array.Clear(_linkTouched, 0, _linkTouched.Length);
        }

        internal void AccumulateContact(int link, Vector3 position, Vector3 impulseOnBody)
        {
            _linkTouched[link] = true;
            int f = _linkFoot[link];
            if (f < 0) return;
            float a = impulseOnBody.magnitude;
            _footImpulse[f] += impulseOnBody; _footMoment[f] += position * a; _footImpulseAbs[f] += a; _footPoints[f]++;
        }

        internal void AfterStep(long step)
        {
            // external work with the mean of the point velocity before and after the step
            for (int e = 0; e < _ext.Count; e++) { var x = _ext[e]; ExternalWorkJ += Vector3.Dot(x.Force, (x.VelBefore + _ab[x.Link].GetPointVelocity(x.Point)) * 0.5f) * _dt; }
            _ext.Clear();
            ReadState();
            float dt = (float)_dt, unit = (float)_unit;
            // tangential ground force: momentum balance on the whole body, split between loaded feet by normal force
            Vector3 comVel = U(ComVelocity);
            Vector3 groundTotal = _hasPrev ? (comVel - _prevComVel) * (float)(TotalMassKg / _dt) - _extSum - new Vector3(0, -(float)(TotalMassKg * _tuning.gravity_mps2), 0) : Vector3.zero;
            _prevComVel = comVel; _hasPrev = true;
            float fnSum = 0;
            for (int f = 0; f < _footLink.Length; f++) fnSum += _footImpulseAbs[f];
            int nonFoot = 0;
            for (int i = 0; i < _linkTouched.Length; i++) if (_linkTouched[i] && _linkFoot[i] < 0) nonFoot++;
            NonFootContacts = nonFoot;
            double fOn = FOnN, fOff = FOffN;
            for (int f = 0; f < _footLink.Length; f++)
            {
                ref FootState fs = ref _foot[f];
                Vector3 imp = _footImpulse[f] * unit;
                float fn = _footImpulseAbs[f] * unit / dt;
                fs.NormalForceN = fn; fs.Points = _footPoints[f];
                fs.Normal = fn > 0 ? N(imp.normalized) : new NVec3(0, 1, 0);
                fs.Cop = fn > 0 ? N(_footMoment[f] / _footImpulseAbs[f]) : fs.SoleCentre;
                Vector3 tangential = fnSum > 0 && nonFoot == 0 ? groundTotal * (_footImpulseAbs[f] / fnSum) : Vector3.zero;
                tangential -= U(fs.Normal) * Vector3.Dot(tangential, U(fs.Normal));
                fs.TangentialForceN = N(tangential);
                int edge = _hyst[f].Update(fn, fOn, fOff);
                fs.Contact = _hyst[f].InContact;
                if (edge > 0)
                {
                    fs.ContactId = _nextContactId++;
                    _loadOpen[f] = true; _loadPeak[f] = 0; _loadImpulse[f] = 0; _loadSteps[f] = 0;
                    _logEvents.Add(new KeyValuePair<string, string>("footfall", "\"foot\":\"" + FootId(f) + "\",\"contact_id\":" + fs.ContactId + ",\"t_contact\":" + JsonLine.ArrayOf(new[] { step * _dt }).Trim('[', ']') + ",\"cop\":" + JsonLine.VecOf(fs.Cop) + ",\"Jn\":" + JsonLine.ArrayOf(new double[] { fn * dt }).Trim('[', ']') + ",\"peak_Fn\":" + JsonLine.ArrayOf(new double[] { fn }).Trim('[', ']') + ",\"pressure_kpa\":" + JsonLine.ArrayOf(new[] { fn / SoleAreaM2 / 1000.0 }).Trim('[', ']') + ",\"v_approach\":" + JsonLine.VecOf(N(_footPrevVel[f]))));
                    _bus?.Publish(new FootContact { Unit = UnitId, Foot = f, Step = step, ContactId = fs.ContactId, Position = fs.Cop, Normal = fs.Normal, VerticalImpulseNs = fn * dt, NormalForceN = fn, PressurePa = (float)(fn / SoleAreaM2), ApproachVelocity = N(_footPrevVel[f]) });
                }
                else if (edge < 0)
                {
                    _logEvents.Add(new KeyValuePair<string, string>("liftoff", "\"foot\":\"" + FootId(f) + "\",\"contact_id\":" + fs.ContactId));
                    _bus?.Publish(new FootLiftOff { Unit = UnitId, Foot = f, Step = step, ContactId = fs.ContactId });
                }
                if (_loadOpen[f])
                {
                    if (fs.Contact) { _loadImpulse[f] += fn * dt; _loadSteps[f]++; if (fn > _loadPeak[f]) { _loadPeak[f] = fn; _loadPeakStep[f] = step; } }
                    if (!fs.Contact || _loadSteps[f] >= _loadWindowSteps)
                    {
                        _loadOpen[f] = false;
                        _logEvents.Add(new KeyValuePair<string, string>("footfall_loaded", "\"foot\":\"" + FootId(f) + "\",\"contact_id\":" + fs.ContactId + ",\"peak_Fn\":" + JsonLine.Text(_loadPeak[f]) + ",\"Jn_window\":" + JsonLine.Text(_loadImpulse[f]) + ",\"window_s\":" + JsonLine.Text(_loadSteps[f] * _dt) + ",\"peak_step\":" + _loadPeakStep[f]));
                        _bus?.Publish(new FootLoaded { Unit = UnitId, Foot = f, Step = step, ContactId = fs.ContactId, PeakNormalForceN = _loadPeak[f], ImpulseNs = _loadImpulse[f], WindowSeconds = (float)(_loadSteps[f] * _dt), PeakStep = _loadPeakStep[f] });
                    }
                }
            }
        }

        internal void FlushLogEvents(long step) { foreach (var e in _logEvents) _world.LogFootEvent(this, step, e.Key, e.Value); _logEvents.Clear(); }

        void ReadState()
        {
            _idGravityFresh = false; _idCoriolisFresh = false;
            double mx = 0, my = 0, mz = 0, vx = 0, vy = 0, vz = 0;
            for (int i = 0; i < _ab.Length; i++)
            {
                var ab = _ab[i]; var tf = _tf[i];
                Vector3 com = ab.worldCenterOfMass, v = ab.linearVelocity;
                _state[i] = new LinkState { Position = N(tf.position), Rotation = N(tf.rotation), Com = N(com), Velocity = N(v), AngularVelocity = N(ab.angularVelocity) };
                double m = _massKg[i];
                mx += m * com.x; my += m * com.y; mz += m * com.z; vx += m * v.x; vy += m * v.y; vz += m * v.z;
                int n = _linkDofCount[i];
                if (n == 0) continue;
                var jp = ab.jointPosition; var jv = ab.jointVelocity; var df = ab.driveForce;
                if (ab.dofCount == 0) continue;
                for (int k = 0; k < n; k++)
                {
                    int d = _linkDofStart[i] + k; int sign = _dof[d].Sign;
                    _q[d] = sign * jp[k]; _qd[d] = sign * jv[k]; _tauDrive[d] = sign * df[k] * _unit;
                    bool atCap = _dofSpeedCap[d] > 0 && Math.Abs(jv[k]) >= 0.999f * _dofSpeedCap[d];
                    _clamped[d] = atCap; if (atCap) JointSpeedClampCount++;
                }
            }
            double M = TotalMassKg > 0 ? TotalMassKg : 1;
            if (TotalMassKg <= 0) { M = 0; foreach (double m in _massKg) M += m; }
            Com = new NVec3((float)(mx / M), (float)(my / M), (float)(mz / M));
            ComVelocity = new NVec3((float)(vx / M), (float)(vy / M), (float)(vz / M));
            for (int f = 0; f < _footLink.Length; f++)
            {
                int link = _footLink[f];
                Vector3 centre = _tf[link].TransformPoint(_soleCentreLocal);
                _foot[f].Link = link; _foot[f].SoleCentre = N(centre); _foot[f].Rotation = N(_tf[link].rotation); _foot[f].Velocity = N(_ab[link].GetPointVelocity(centre));
            }
        }

        /// <summary>Engine read-back for the physics_log@v1 header (M-006.1: damping, limits, drives as the engine reports them).</summary>
        public string DescribeEngineJson(string solverInfo)
        {
            var sb = new StringBuilder("{");
            sb.Append(solverInfo).Append(",\"links\":{");
            for (int i = 0; i < _ab.Length; i++)
            {
                var ab = _ab[i]; var box = ab.GetComponent<BoxCollider>();
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(LinkId(i)).Append("\":{\"mass_kg\":"); JsonLine.Num(sb, ab.mass * _unit);
                sb.Append(",\"linear_damping\":"); JsonLine.Num(sb, ab.linearDamping); sb.Append(",\"angular_damping\":"); JsonLine.Num(sb, ab.angularDamping);
                sb.Append(",\"joint_friction\":"); JsonLine.Num(sb, ab.jointFriction); sb.Append(",\"max_depenetration\":"); JsonLine.Num(sb, ab.maxDepenetrationVelocity);
                sb.Append(",\"max_joint_velocity\":"); JsonLine.Num(sb, ab.maxJointVelocity); sb.Append(",\"sleep_threshold\":"); JsonLine.Num(sb, ab.sleepThreshold);
                sb.Append(",\"contact_offset\":"); JsonLine.Num(sb, box.contactOffset); sb.Append(",\"solver_iterations\":[").Append(ab.solverIterations).Append(',').Append(ab.solverVelocityIterations).Append("]");
                sb.Append(",\"collider_volume_m3\":"); JsonLine.Num(sb, box.size.x * box.size.y * box.size.z); sb.Append(",\"joint_type\":\"").Append(ab.isRoot ? "Root" : ab.jointType.ToString()).Append("\"}");
            }
            sb.Append("},\"drives\":{");
            for (int d = 0; d < _dof.Length; d++)
            {
                var info = _dof[d]; var drive = GetDrive(_ab[info.ChildLink], info.AxisIndex);
                if (d > 0) sb.Append(',');
                sb.Append('"').Append(info.JointId).Append('.').Append(info.Name).Append("\":{\"k\":"); JsonLine.Num(sb, drive.stiffness * _unit); sb.Append(",\"d\":"); JsonLine.Num(sb, drive.damping * _unit);
                sb.Append(",\"forceLimit\":"); JsonLine.Num(sb, info.ForceLimitNm); sb.Append(",\"driveType\":\"").Append(drive.driveType).Append("\",\"lower_deg\":"); JsonLine.Num(sb, drive.lowerLimit); sb.Append(",\"upper_deg\":"); JsonLine.Num(sb, drive.upperLimit); sb.Append('}');
            }
            return sb.Append("}}").ToString();
        }
    }
}
