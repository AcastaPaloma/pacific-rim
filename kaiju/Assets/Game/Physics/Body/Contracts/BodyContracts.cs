using System.Numerics;
using Game.Core;
using Game.Physics.Body.Qp;

namespace Game.Physics.Body.Contracts
{
    public static class BodyContractVersions
    {
        public const int ArticulatedBody = 3;   // 3: whole-body model queries (Jacobian, bias force; step 9.4 of T-005); 2: inverse-dynamics queries
        public const int FootContactEvent = 1;
        public const string PhysicsLog = "physics_log@v1";
        public const string FlagEnabled = "physics.body.enabled", FlagPhysicsLog = "physics.body.physicsLog";
    }

    /// <summary>F-009: published on the Core bus in the simulation step in which the foot's normal force first reaches F_on. One per contact.</summary>
    public struct FootContact : IEvent
    {
        public EntityId Unit;
        public int Foot;
        public long Step;
        public long ContactId;
        public Vector3 Position;          // centre of pressure, world, m
        public Vector3 Normal;            // force-weighted contact normal
        public float VerticalImpulseNs;   // normal impulse of the contact step
        public float NormalForceN;
        public float PressurePa;          // normal force / sole area
        public Vector3 ApproachVelocity;  // foot velocity at the start of the contact step, m/s
    }

    /// <summary>
    /// Follow-up to <see cref="FootContact"/> with the same ContactId, published once the contact's loading window (body_tuning.contact_load_window_tstar, R-001 section 6: 0.10 T_nom)
    /// has passed or the contact has ended: the peak normal force and the normal impulse of that window. Consumers that scale an effect with how hard the foot came down use this one.
    /// </summary>
    public struct FootLoaded : IEvent
    {
        public EntityId Unit;
        public int Foot;
        public long Step;
        public long ContactId;
        public float PeakNormalForceN;
        public float ImpulseNs;        // integral of the normal force over the window
        public float WindowSeconds;
        public long PeakStep;
    }

    /// <summary>
    /// The one place the PhysX mass unit is declared (body_tuning.sim_mass_unit_kg). PhysX cannot hold these bodies in kilograms (see MODULE.md), so every body in a physics scene is created
    /// with masses in this unit and every force, torque, impulse, stiffness and damping handed to or read from the engine goes through these conversions. Accelerations, lengths and times are unchanged.
    /// Any module that creates a Rigidbody or ArticulationBody, or applies a force with the engine API, must use the world's <see cref="IBodyWorld.MassUnit"/>; mixing units in one scene is a physics error.
    /// </summary>
    public readonly struct SimMassUnit
    {
        public readonly double KilogramsPerUnit;
        public SimMassUnit(double kilogramsPerUnit) { if (!(kilogramsPerUnit > 0)) throw new System.ArgumentOutOfRangeException(nameof(kilogramsPerUnit)); KilogramsPerUnit = kilogramsPerUnit; }
        /// <summary>kg, N, N m, N s, N/m, N m/rad, N m s/rad, kg m2: SI to engine.</summary>
        public double ToEngine(double si) => si / KilogramsPerUnit;
        public double ToSi(double engine) => engine * KilogramsPerUnit;
    }

    /// <summary>Published in the step in which the foot's normal force falls below F_off.</summary>
    public struct FootLiftOff : IEvent
    {
        public EntityId Unit;
        public int Foot;
        public long Step;
        public long ContactId;
    }

    public struct LinkState
    {
        public Vector3 Position;          // link origin = its proximal joint, world
        public Quaternion Rotation;
        public Vector3 Com;               // world
        public Vector3 Velocity;          // of the centre of mass
        public Vector3 AngularVelocity;
    }

    public struct DofInfo
    {
        public string JointId, Name, Kind;
        public int ChildLink;
        public int AxisIndex;             // 0 twist (x), 1 swing y, 2 swing z in the joint frame
        public int Sign;                  // +1/-1: anatomical positive = Sign * engine rotation about the axis
        public double LowerRad, UpperRad; // anatomical
        public double ForceLimitNm;
    }

    public struct FootState
    {
        public int Link;
        public bool Contact;              // F_on / F_off hysteresis
        public long ContactId;
        public double NormalForceN;       // summed over the last step
        public Vector3 TangentialForceN;  // estimate (see MODULE.md): momentum balance, split by normal force in double support
        public Vector3 Cop;               // world; valid when NormalForceN > 0
        public Vector3 Normal;
        public int Points;
        public Vector3 SoleCentre;        // world position of the sole centre (foot-frame origin of gait_log@v1)
        public Quaternion Rotation;
        public Vector3 Velocity;          // of the sole centre
    }

    /// <summary>
    /// The only way other modules touch a physical body. SI units and anatomical joint signs everywhere; the implementation owns engine units.
    /// Honest physics: there is no method that writes a pose or a velocity. Motion comes from joint drives, joint torques and logged external forces.
    /// </summary>
    public interface IArticulatedBody
    {
        EntityId UnitId { get; }
        MechFrameData Frame { get; }
        BodyTuning Tuning { get; }

        int LinkCount { get; }
        string LinkId(int link);
        int FindLink(string id);
        int LinkParent(int link);
        double LinkMassKg(int link);
        void GetLink(int link, out LinkState state);

        int DofCount { get; }
        DofInfo GetDof(int dof);
        int FindDof(string jointId, string dofName);
        double DofPosition(int dof);
        double DofVelocity(int dof);
        /// <summary>World axis about which a positive anatomical torque of this DOF acts on the child link.</summary>
        Vector3 DofAxisWorld(int dof);
        void SetDofDrive(int dof, double targetRad, double targetVelocityRps, double stiffnessNmPerRad, double dampingNmsPerRad);
        /// <summary>Extra joint torque for the next step (gravity and model feed-forward). Clamped with the drive to the DOF's force limit.</summary>
        void SetDofFeedForward(int dof, double torqueNm);
        /// <summary>Effective inertia about the DOF axis in the rest pose: the distal side (a free-swinging limb) or, for <paramref name="stanceSide"/>, everything else (the body above a planted limb).</summary>
        double DofInertia(int dof, bool stanceSide);
        /// <summary>R-005 section 3.2 gain rule (k = I w^2, d = 2 zeta I w) on that inertia with the tuning's frequency for the joint kind. Gains follow mass and inertia; none is hand-set.</summary>
        void GetDofGains(int dof, bool stanceSide, out double stiffnessNmPerRad, out double dampingNmsPerRad);
        double DofDriveTorque(int dof);
        double DofAppliedTorque(int dof);
        /// <summary>Inverse dynamics from the body's own model in its present pose (engine: ArticulationBody.GetJointGravityForces): the joint torque that counteracts gravity on this DOF. Contact forces are not part of it.</summary>
        double DofGravityTorque(int dof);
        /// <summary>Inverse dynamics (engine: GetJointCoriolisCentrifugalForces): the joint torque that counteracts the Coriolis and centrifugal forces at the present joint velocities.</summary>
        double DofCoriolisTorque(int dof);
        /// <summary>Inverse dynamics (engine: GetJointForcesForAcceleration on the DOF's link): the joint torque that gives this DOF the acceleration (rad/s2) in the present pose. Gravity, velocity terms, drives and contacts are not part of it.</summary>
        double DofTorqueForAcceleration(int dof, double accelerationRps2);

        double TotalMassKg { get; }
        Vector3 Com { get; }
        Vector3 ComVelocity { get; }

        int FootCount { get; }
        string FootId(int foot);
        void GetFoot(int foot, out FootState state);
        double SoleAreaM2 { get; }

        /// <summary>A real external force for the next step only (shoves, impulses). Its magnitude and work are logged; locomotion captures must show zero.</summary>
        void AddExternalForce(int link, Vector3 forceN, Vector3 worldPoint);
        double ExternalForceMagnitudeN { get; }
        double ExternalWorkJ { get; }
        /// <summary>Count of non-foot links that touched the ground in the last step (fall detection, M-006.5).</summary>
        int NonFootContacts { get; }
        /// <summary>Running count of (step, DOF) pairs at which a joint moved at the engine's speed guard (max joint velocity, R-005 R11). The guard is an explosion stop, not a motion limit: it should stay 0 outside falls.</summary>
        int JointSpeedClampCount { get; }
        bool DofSpeedClamped(int dof);

        // ---- v3: whole-body model (the engine exposes no mass matrix; the controller builds it from Jacobians, masses and inertias) ----
        /// <summary>Column count of the Jacobian and length of the bias vector: the engine's reduced coordinates (6 base columns when the root is not immovable, then the slots of every link; a locked axis of a spherical joint keeps an inert slot, so this can exceed DofCount + 6). Valid once the articulation exists (after the first step).</summary>
        int ModelColumns { get; }
        /// <summary>Model column of a body DOF (anatomical sign, same index space as <see cref="GetBiasForce"/>).</summary>
        int ModelColumnOfDof(int dof);
        /// <summary>Model column of base coordinate k (0..5, engine order; engine frame and sign, unprobed until step 9.5).</summary>
        int ModelBaseColumn(int k);
        /// <summary>Dense Jacobian, row-major, rows x cols, written into <paramref name="jac"/> (length >= rows*cols). Rows are 6 per link: row of link i = (engine articulation index of i) * 6, 3 linear then 3 angular. DOF columns use anatomical signs; base columns are raw. False when the buffer is too small or the engine returns nothing.</summary>
        bool GetBodyJacobian(double[] jac, out int rows, out int cols);
        /// <summary>Bias force h = gravity + Coriolis/centrifugal joint forces (counteracting forces as the engine reports them), SI (N, N m), model-column order, DOF entries in anatomical sign.</summary>
        void GetBiasForce(double[] h);
    }

    /// <summary>Where and how a body is created. Rotation is a world rotation of the whole frame (identity = facing +z).</summary>
    public sealed class BodySpawn
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.Identity;
        public bool LockAllJoints;
        public bool Visuals = true;
        public string Name = "mech";
    }

    /// <summary>
    /// The physics context bodies live in (one per simulation; registered in the Core service registry by the composition root).
    /// Units create their body here, so no unit module references the body implementation.
    /// </summary>
    public interface IBodyWorld
    {
        double StepSeconds { get; }
        SimMassUnit MassUnit { get; }
        IArticulatedBody CreateBody(MechFrameData frame, BodyTuning tuning, EntityId unitId, BodySpawn spawn);
        void DestroyBody(IArticulatedBody body);
        /// <summary>Starts a physics_log@v1 file for the body; a record is written after every step.</summary>
        void StartPhysicsLog(IArticulatedBody body, string path, string scene, string commit);
        void LogEvent(IArticulatedBody body, long step, string type, string extraJsonBody);
        void StopPhysicsLog(IArticulatedBody body);
        /// <summary>A preallocated dense QP solver (no allocation per solve), so unit modules need not reference the solver implementation.</summary>
        IQpSolver CreateQpSolver(int maxN, int maxEq, int maxIn);
    }
}
