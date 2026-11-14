using System;
using System.Collections.Generic;
using System.IO;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Units.Mech.Unity
{
    /// <summary>The three config files of one mech (frame, body tuning, walk tuning). Other sizes are the same files scaled by the R-005 section 1.4 rule: data only.</summary>
    public sealed class MechConfig
    {
        public MechFrameData Frame; public BodyTuning Body; public WalkTuning Walk;
        public const string FlagEnabled = "units.mech.enabled", FlagGaitLog = "units.mech.gaitLog", FlagBodyEnabled = "physics.body.enabled", FlagPhysicsLog = "physics.body.physicsLog";

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
        public MechConfig ScaledTo(double heightM, double massKg) => new MechConfig { Frame = MechFrameOps.Scale(Frame, heightM, massKg), Body = Body.ScaledFor(massKg, heightM), Walk = Walk };
        public MechConfig WithBody(Action<BodyTuning> edit) { var b = JsonUtility.FromJson<BodyTuning>(JsonUtility.ToJson(Body)); edit(b); return new MechConfig { Frame = Frame, Body = b, Walk = Walk }; }
        public MechConfig WithWalk(Action<WalkTuning> edit) { var w = JsonUtility.FromJson<WalkTuning>(JsonUtility.ToJson(Walk)); edit(w); return new MechConfig { Frame = Frame, Body = Body, Walk = w }; }
    }

    /// <summary>
    /// The mech assembly (Units.Mech): frame + articulated body + pilot seat + locomotion controller, one instance per mech with its own config, seat, logs and IDs.
    /// The unit never reads a device: it reads its seat.
    /// </summary>
    public sealed class MechUnit : IMechUnit, IEventHandler<FootContact>, IEventHandler<FootLiftOff>, IEventHandler<MechFallStarted>, IEventHandler<MechFallEnded>, IDisposable
    {
        readonly IEventBus _bus;
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

        public MechUnit(IBodyWorld world, MechConfig config, EntityId id, IEventBus bus, PilotSeat seat, BodySpawn spawn)
        {
            Config = config; Id = id; _bus = bus; Seat = seat; _world = world; _dt = world.StepSeconds;
            Body = world.CreateBody(config.Frame, config.Body, id, spawn);
            Controller = new LocomotionController(Body, config.Walk, _dt, bus);
            bus.Subscribe<FootContact>(this); bus.Subscribe<FootLiftOff>(this); bus.Subscribe<MechFallStarted>(this); bus.Subscribe<MechFallEnded>(this);
        }

        public void StartLogs(string directory, string capture, string commit, double cruiseFroude, bool physicsLog = true)
        {
            Directory.CreateDirectory(Path.Combine(directory, "gait"));
            _gait = new GaitLogWriter(new StreamWriter(Path.Combine(directory, "gait", capture + ".gait.jsonl")), Body, _dt);
            _gait.WriteHeader(capture, commit, Controller.Limits, cruiseFroude, "FootfallProbe (test subscriber)", "ft is a momentum-balance estimate split by fn in double support; cop and ctl are extra fields; record n is the state after n physics steps");
            _gait.WriteStep(0, Intent, Controller.Command, Controller.DebugState);
            if (physicsLog) _world.StartPhysicsLog(Body, Path.Combine(directory, "physics", capture + ".physics.jsonl"), capture, commit);
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
                _gait?.CameraImpulse(step, c.Foot, c.ContactId, 0.05 * Math.Pow(c.VerticalImpulseNs / (Body.TotalMassKg * Body.Tuning.gravity_mps2 * _dt), 0.75), c.Step);
            }
            foreach (var l in _liftoffs) _gait?.ContactEnd(l);
            foreach (var e in _physEvents) { _world.LogEvent(Body, step, e, null); _gait?.Event(step, e, null); }
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
