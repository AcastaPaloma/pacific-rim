using System;
using System.Collections.Generic;
using System.IO;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Qp;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Units.Mech.Unity
{
    /// <summary>The three config files of one mech (frame, body tuning, walk tuning). Other sizes are the same files scaled by the R-005 section 1.4 rule: data only.</summary>
    public sealed class MechConfig
    {
        public MechFrameData Frame; public BodyTuning Body; public WalkTuning Walk;
        public const string FlagEnabled = MechContractVersions.FlagEnabled, FlagGaitLog = MechContractVersions.FlagGaitLog, FlagBodyEnabled = BodyContractVersions.FlagEnabled, FlagPhysicsLog = BodyContractVersions.FlagPhysicsLog;

        public static MechConfig FromJson(string frameJson, string bodyJson, string walkJson)
        {
            var c = new MechConfig { Frame = JsonUtility.FromJson<MechFrameData>(frameJson), Body = JsonUtility.FromJson<BodyTuning>(bodyJson), Walk = JsonUtility.FromJson<WalkTuning>(walkJson) };
            MechFrameOps.Validate(c.Frame);
            if (c.Body.schema != BodyTuning.Schema) throw new InvalidOperationException("body tuning schema '" + c.Body.schema + "' is not " + BodyTuning.Schema);
            if (c.Walk.schema != WalkTuning.Schema) throw new InvalidOperationException("walk tuning schema '" + c.Walk.schema + "' is not " + WalkTuning.Schema);
            return c;
        }
        /// <summary>Editor and headless runs: read the committed config files.</summary>
        public static MechConfig LoadDefault(string frameFile = "mech_frame_balanced.json")
        {
            string dir = Path.Combine(Application.dataPath, "Game/Config/Mech");
            return FromJson(File.ReadAllText(Path.Combine(dir, frameFile)), File.ReadAllText(Path.Combine(dir, "body_tuning.json")), File.ReadAllText(Path.Combine(dir, "walk_tuning.json")));
        }
        /// <summary>The optimised reference (gait_reference_opt@v1) when this walk tuning asks for imp = 2, else null. A missing or invalid file throws.</summary>
        public GaitReferenceData GaitReferenceFor()
        {
            string file = Walk.imp >= 2.5 && Walk.qp_engage_speed_frac > 0 ? GaitReferenceData.FileFor(2.0) : GaitReferenceData.FileFor(Walk.imp);   // imp >= 2.5 (the whole-body QP) reads gait_reference_qp.json (@v2), imp 2 gait_reference_opt.json (@v1); an imp 3 hand-off start (step 9.5) runs the imp 2 pipeline on the @v1 file first and passes the @v2 file as QpReferenceFor
            if (file == null) return null;
            string path = Path.Combine(Application.dataPath, "Game/Config/Mech", file);
            var d = JsonUtility.FromJson<GaitReferenceData>(File.ReadAllText(path)); d.Validate(); return d;
        }
        /// <summary>The @v2 reference of the imp = 3 QP when the imp 2 pipeline runs on the @v1 file before the hand-off (qp_engage_speed_frac > 0), else null.</summary>
        public GaitReferenceData QpReferenceFor()
        {
            if (!(Walk.imp >= 2.5 && Walk.qp_engage_speed_frac > 0)) return null;
            var d = JsonUtility.FromJson<GaitReferenceData>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech", Walk.qp_ref_v5 > 0.5 ? GaitReferenceData.MainFileV5For(Walk.qp_ref_v5) : Walk.qp_ref_v4 > 0.5 ? GaitReferenceData.MainFileV4 : GaitReferenceData.FileFor(Walk.imp)))); d.Validate();
            if (Walk.qp_ladder > 0.5)
            {
                // step 9.5b speed ladder: the low-Froude @v2 rows first, the Fr 0.10 row last (rows sorted by Froude)
                var lad = JsonUtility.FromJson<GaitReferenceData>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech", Walk.qp_ref_v5 > 0.5 ? GaitReferenceData.LadderFileV5For(Walk.qp_ref_v5) : Walk.qp_ref_v4 > 0.5 ? GaitReferenceData.LadderFileV4 : GaitReferenceData.LadderFile))); lad.Validate();
                var all = new GaitRefRow[lad.rows.Length + d.rows.Length]; lad.rows.CopyTo(all, 0); d.rows.CopyTo(all, lad.rows.Length); d.rows = all; d.Validate();
            }
            return d;
        }
        public MechConfig ScaledTo(double heightM, double massKg) => new MechConfig { Frame = MechFrameOps.Scale(Frame, heightM, massKg), Body = Body.ScaledFor(massKg, heightM), Walk = Walk };
        public MechConfig WithBody(Action<BodyTuning> edit) { var b = JsonUtility.FromJson<BodyTuning>(JsonUtility.ToJson(Body)); edit(b); return new MechConfig { Frame = Frame, Body = b, Walk = Walk }; }
        /// <summary>Round 7 body experiments (walk-tuning fields exp_*, default 0 = off; the default body_tuning / frame files are never edited): exp_ankle_limit_nm replaces the ankle joint force limit, exp_toe_m trims the foot at the toe to this lever from the ankle (the foot box shortens, its mass is kept, its inertia follows the box).</summary>
        public MechConfig WithExperiments()
        {
            var c = this;
            if (Walk.exp_ankle_limit_nm > 0) c = c.WithBody(b => { foreach (var k in b.joint_kinds) if (k.kind == "ankle") k.force_limit_nm = Walk.exp_ankle_limit_nm; });
            if (Walk.exp_toe_m > 0 && Walk.exp_toe_m < Frame.foot_sole.toe_z_m)
            {
                var f = JsonUtility.FromJson<MechFrameData>(JsonUtility.ToJson(Frame)); double toe = Walk.exp_toe_m, heel = f.foot_sole.heel_z_m, d = toe - heel;
                f.foot_sole.area_m2 *= d / f.foot_sole.length_m; f.foot_sole.length_m = d; f.foot_sole.toe_z_m = toe;
                foreach (var sg in f.segments)
                    if (sg.kind == "foot")
                    {
                        double d0 = sg.depth_z_m, w = sg.width_x_m, h = sg.height_y_m, m = sg.mass_kg;
                        sg.depth_z_m = d; sg.length_m = d; sg.distal_end[2] = toe; sg.com[2] = 0.5 * (heel + toe) + f.foot_sole.ankle_z_m * 0.0;
                        sg.hull_volume_m3 *= d / d0; sg.inertia_principal_kgm2 = new[] { m * (h * h + d * d) / 12.0, m * (w * w + d * d) / 12.0, m * (w * w + h * h) / 12.0 };
                    }
                c = new MechConfig { Frame = f, Body = c.Body, Walk = c.Walk };
            }
            return c;
        }
        public MechConfig WithWalk(Action<WalkTuning> edit) { var w = JsonUtility.FromJson<WalkTuning>(JsonUtility.ToJson(Walk)); edit(w); return new MechConfig { Frame = Frame, Body = Body, Walk = w }; }
    }

    /// <summary>
    /// The mech assembly (Units.Mech): frame + articulated body + pilot seat + locomotion controller, one instance per mech with its own config, seat, logs and IDs.
    /// The unit never reads a device: it reads its seat.
    /// </summary>
    public sealed class MechUnit : IMechUnit, IEventHandler<FootContact>, IEventHandler<FootLiftOff>, IEventHandler<MechFallStarted>, IEventHandler<MechFallEnded>, IDisposable
    {
        readonly IEventBus _bus;
        readonly IFeatureFlags _flags;
        readonly IBodyWorld _world;
        readonly double _dt;
        readonly List<FootContact> _contacts = new List<FootContact>();
        readonly List<FootLiftOff> _liftoffs = new List<FootLiftOff>();
        readonly List<string> _physEvents = new List<string>();
        GaitLogWriter _gait;

        public EntityId Id { get; }
        public PilotSeat Seat { get; }
        public IArticulatedBody Body { get; }
        public LocomotionController Controller { get; }
        public LocoPhase Phase => Controller.Phase;
        public LocoCommand Command => Controller.Command;
        public IntentFrame Intent { get; private set; }
        public MechConfig Config { get; }
        /// <summary>Stands in for consumers of F-009 (camera, audio): called once per FootContact in the publishing step. Arguments: event, step at which it was received.</summary>
        public Action<FootContact, long> FootfallProbe;
        public Func<long> ClockStep;

        public MechUnit(IBodyWorld world, MechConfig config, EntityId id, IEventBus bus, PilotSeat seat, BodySpawn spawn, IFeatureFlags flags)
        {
            // module switch (MODULARITY rule 5): no mech is assembled while units.mech.enabled is off
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            if (!flags.IsOn(MechConfig.FlagEnabled)) throw new InvalidOperationException("feature flag " + MechConfig.FlagEnabled + " is off: Units.Mech assembles no mech");
            Config = config; Id = id; _bus = bus; Seat = seat; _world = world; _dt = world.StepSeconds;
            Body = world.CreateBody(config.Frame, config.Body, id, spawn);
            Controller = new LocomotionController(Body, config.Walk, _dt, bus, config.GaitReferenceFor(), LocomotionController.QpWanted(config.Walk) ? world.CreateQpSolver(SagittalQpController.MaxNx, 3, SagittalQpController.MaxIn) : null, config.QpReferenceFor());
            // walk_mode 2 plants its feet on the terrain under them: a downward ray at the foot xz, ignoring the mech's own articulation links (flat ground at y = 0 where the ray hits nothing)
            if (config.Walk.walk_mode > 1.5) Controller.GroundHeight = (x, z) =>
            {
                float top = (float)Body.Com.Y + 200f; float best = float.NegativeInfinity;
                foreach (var h in UnityEngine.Physics.RaycastAll(new Vector3(x, top, z), Vector3.down, top + 500f, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.GetComponentInParent<ArticulationBody>() == null && h.point.y > best) best = h.point.y;
                return float.IsNegativeInfinity(best) ? 0f : best;
            };
            bus.Subscribe<FootContact>(this); bus.Subscribe<FootLiftOff>(this); bus.Subscribe<MechFallStarted>(this); bus.Subscribe<MechFallEnded>(this);
        }

        public void StartLogs(string directory, string capture, string commit, double cruiseFroude, bool physicsLog = true)
        {
            if (physicsLog) _world.StartPhysicsLog(Body, Path.Combine(directory, "physics", capture + ".physics.jsonl"), capture, commit);
            if (!_flags.IsOn(MechConfig.FlagGaitLog)) return;
            Directory.CreateDirectory(Path.Combine(directory, "gait"));
            _gait = new GaitLogWriter(new StreamWriter(Path.Combine(directory, "gait", capture + ".gait.jsonl")), Body, _dt);
            _gait.WriteHeader(capture, commit, Controller.Limits, cruiseFroude, "FootfallProbe (test subscriber)", "ft is a momentum-balance estimate split by fn in double support; cop and ctl are extra fields; record n is the state after n physics steps");
            _gait.WriteStep(0, Intent, Controller.Command, Controller.DebugState);
        }

        /// <summary>Before the physics step: read the seat, run the controller. This is the only place intent enters the mech.</summary>
        public void PreStep(long step)
        {
            Intent = Seat.Read();
            Controller.Update(step, Intent);
        }

        /// <summary>After the physics step: write the records of step <paramref name="step"/> and the events that were published during it.</summary>
        public void PostStep(long step)
        {
            _gait?.WriteStep(step, Intent, Controller.Command, Controller.DebugState);
            foreach (var c in _contacts)
            {
                _gait?.ContactBegin(c);
                _gait?.CameraImpulse(step, c.Foot, c.ContactId, Config.Walk.footfall_probe_gain_m * Math.Pow(c.VerticalImpulseNs / (Body.TotalMassKg * Body.Tuning.gravity_mps2 * _dt), Config.Walk.footfall_probe_exponent), c.Step);
            }
            foreach (var l in _liftoffs) _gait?.ContactEnd(l);
            foreach (var e in _physEvents) _world.LogEvent(Body, step, e, null); // fall_start / fall_end belong to physics_log@v1; gait_log@v1 has no such event type
            _contacts.Clear(); _liftoffs.Clear(); _physEvents.Clear();
        }

        public void Handle(in FootContact e) { if (e.Unit != Id) return; _contacts.Add(e); FootfallProbe?.Invoke(e, ClockStep != null ? ClockStep() : e.Step); }
        public void Handle(in FootLiftOff e) { if (e.Unit == Id) _liftoffs.Add(e); }
        public void Handle(in MechFallStarted e) { if (e.Unit == Id) _physEvents.Add("fall_start"); }
        public void Handle(in MechFallEnded e) { if (e.Unit == Id) _physEvents.Add("fall_end"); }

        public void CloseLogs() { _gait?.Dispose(); _gait = null; _world.StopPhysicsLog(Body); }
        public void Dispose()
        {
            CloseLogs();
            _world.DestroyBody(Body);
            _bus.Unsubscribe<FootContact>(this); _bus.Unsubscribe<FootLiftOff>(this); _bus.Unsubscribe<MechFallStarted>(this); _bus.Unsubscribe<MechFallEnded>(this);
        }
    }
}
