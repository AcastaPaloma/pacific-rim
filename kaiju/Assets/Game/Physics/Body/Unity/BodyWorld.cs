using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Qp;
using Unity.Collections;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Physics.Body.Unity
{
    /// <summary>
    /// One per simulation context: routes PhysX contact events to the bodies that own the colliders, and owns scene-level physics setup.
    /// Instance, not static, so several contexts (tests, N mechs) never share state.
    /// </summary>
    public sealed class BodyWorld : IBodyWorld, IDisposable
    {
        readonly Dictionary<Collider, KeyValuePair<ArticulatedBody, int>> _owners = new Dictionary<Collider, KeyValuePair<ArticulatedBody, int>>();
        readonly List<ArticulatedBody> _bodies = new List<ArticulatedBody>();
        readonly SimulationMode _previousMode;
        readonly Vector3 _previousGravity;
        readonly Dictionary<IArticulatedBody, PhysicsLogWriter> _logs = new Dictionary<IArticulatedBody, PhysicsLogWriter>();
        readonly IEventBus _bus;
        readonly UnityEngine.SceneManagement.Scene _isolated;
        /// <summary>Round 17 (default: invalid scene = old behaviour, bit-identical). Headless rollout tooling (ScenarioCli -isolated) sets a preview scene here per rollout: bodies and ground are then created in it and <see cref="Simulate"/> steps its own PhysicsScene, so a rollout starts from a fresh PhysX scene and its result no longer depends on the rollouts that ran before it in the same process (r16: +-3 s per seed by batch order).</summary>
        public static UnityEngine.SceneManagement.Scene IsolatedScene;
        public static void Place(GameObject go) { if (IsolatedScene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, IsolatedScene); }
        public double StepSeconds { get; }
        public SimMassUnit MassUnit { get; }
        readonly IFeatureFlags _flags;
        public IReadOnlyList<ArticulatedBody> Bodies => _bodies;
        /// <summary>Project-level engine facts for log headers (solver type, determinism): set by the composition root, which can read project settings.</summary>
        public string EngineInfoJson = "\"solver_type\":\"unknown\"";

        /// <param name="scripted">True: the caller advances physics with <see cref="Simulate"/> (headless scenarios, tests). False: Unity's FixedUpdate does.</param>
        public BodyWorld(BodyTuning tuning, double stepSeconds, bool scripted, IEventBus bus, IFeatureFlags flags = null)
        {
            _isolated = IsolatedScene;             StepSeconds = stepSeconds; _bus = bus; _flags = flags; MassUnit = new SimMassUnit(tuning.sim_mass_unit_kg);
            _previousMode = UnityEngine.Physics.simulationMode; _previousGravity = UnityEngine.Physics.gravity;
            UnityEngine.Physics.gravity = new Vector3(0, -(float)tuning.gravity_mps2, 0);
            if (scripted) UnityEngine.Physics.simulationMode = SimulationMode.Script;
            UnityEngine.Physics.ContactEvent += OnContact;
        }

        public void Dispose()
        {
            UnityEngine.Physics.ContactEvent -= OnContact;
            UnityEngine.Physics.simulationMode = _previousMode; UnityEngine.Physics.gravity = _previousGravity;
            foreach (var w in _logs.Values) w.Dispose();
            _logs.Clear(); _owners.Clear(); _bodies.Clear();
        }

        public IArticulatedBody CreateBody(MechFrameData frame, BodyTuning tuning, EntityId unitId, BodySpawn spawn)
        {
            // module switch (MODULARITY rule 5): a world given a flag snapshot creates bodies only when physics.body.enabled is on. Truth scenes and probes pass no snapshot.
            if (_flags != null && !_flags.IsOn(BodyContractVersions.FlagEnabled)) throw new InvalidOperationException("feature flag " + BodyContractVersions.FlagEnabled + " is off: Physics.Body creates no bodies");
            var o = new BodyBuildOptions { Position = new Vector3(spawn.Position.X, spawn.Position.Y, spawn.Position.Z), Rotation = new Quaternion(spawn.Rotation.X, spawn.Rotation.Y, spawn.Rotation.Z, spawn.Rotation.W), LockAllJoints = spawn.LockAllJoints, Visuals = spawn.Visuals, Name = spawn.Name };
            return new ArticulatedBody(this, frame, tuning, unitId, _bus, o);
        }
        public IQpSolver CreateQpSolver(int maxN, int maxEq, int maxIn) => new GoldfarbIdnaniQp(maxN, maxEq, maxIn);
        public void DestroyBody(IArticulatedBody body)
        {
            var b = (ArticulatedBody)body;
            StopPhysicsLog(body); _bodies.Remove(b);
            var dead = new List<Collider>();
            foreach (var kv in _owners) if (kv.Value.Key == b) dead.Add(kv.Key);
            foreach (var c in dead) _owners.Remove(c);
            if (b.Root != null) UnityEngine.Object.DestroyImmediate(b.Root);
        }
        public void StartPhysicsLog(IArticulatedBody body, string path, string scene, string commit)
        {
            if (_flags != null && !_flags.IsOn(BodyContractVersions.FlagPhysicsLog)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var w = new PhysicsLogWriter(new StreamWriter(path), body, scene, StepSeconds);
            w.WriteHeader(scene, commit, Application.unityVersion, ((ArticulatedBody)body).DescribeEngineJson(EngineInfoJson));
            w.WriteStep(0);
            _logs[body] = w;
        }
        public void LogEvent(IArticulatedBody body, long step, string type, string extraJsonBody) { if (_logs.TryGetValue(body, out var w)) w.WriteEvent(step, type, extraJsonBody); }
        public void StopPhysicsLog(IArticulatedBody body) { if (_logs.TryGetValue(body, out var w)) { w.Dispose(); _logs.Remove(body); } }
        internal void LogFootEvent(ArticulatedBody body, long step, string type, string extra) => LogEvent(body, step, type, extra);

        internal void Register(ArticulatedBody body) => _bodies.Add(body);
        internal void Register(Collider c, ArticulatedBody body, int link) => _owners[c] = new KeyValuePair<ArticulatedBody, int>(body, link);

        /// <summary>Scripted mode: flush every body's drives and forces, advance PhysX one fixed step, then let bodies read the result. Returns after contacts of this step are known.</summary>
        public void Simulate(long stepAfter)
        {
            foreach (var b in _bodies) b.BeforeStep();
            if (_isolated.IsValid()) _isolated.GetPhysicsScene().Simulate((float)StepSeconds); else UnityEngine.Physics.Simulate((float)StepSeconds);
            foreach (var b in _bodies) { b.AfterStep(stepAfter); if (_logs.TryGetValue(b, out var w)) { w.WriteStep(stepAfter); b.FlushLogEvents(stepAfter); } }
        }

        void OnContact(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            for (int h = 0; h < headers.Length; h++)
            {
                var header = headers[h];
                for (int p = 0; p < header.pairCount; p++)
                {
                    ref readonly var pair = ref header.GetContactPair(p);
                    var ca = pair.collider; var cb = pair.otherCollider;
                    if (ca == null || cb == null) continue; // a collider destroyed since the contact was generated
                    bool a = _owners.TryGetValue(ca, out var oa), b = _owners.TryGetValue(cb, out var ob);
                    if (!a && !b) continue;
                    if (a && b && oa.Key == ob.Key) continue;
                    for (int k = 0; k < pair.contactCount; k++)
                    {
                        ref readonly var pt = ref pair.GetContactPoint(k);
                        if (pt.impulse.sqrMagnitude <= 0f) continue; // speculative points carry no impulse
                        if (a) oa.Key.AccumulateContact(oa.Value, pt.position, pt.impulse);
                        if (b) ob.Key.AccumulateContact(ob.Value, pt.position, -pt.impulse);
                    }
                }
            }
        }

        /// <summary>Rigid flat ground (M0): a static box whose top face is the plane y = height, restitution 0, friction from tuning.</summary>
        public static GameObject CreateGround(BodyTuning t, float halfExtent = 5000f, float height = 0f, float slopeDeg = 0f)
        {
            var go = new GameObject("ground");
            Place(go);
            var c = go.AddComponent<BoxCollider>();
            c.size = new Vector3(halfExtent * 2, 20f, halfExtent * 2);
            go.transform.rotation = Quaternion.Euler(-slopeDeg, 0, 0);
            go.transform.position = new Vector3(0, height, 0) - go.transform.up * 10f;
            c.material = FootMaterial(t);
            return go;
        }

        public static PhysicsMaterial FootMaterial(BodyTuning t) => new PhysicsMaterial("mech_contact")
        {
            staticFriction = (float)t.friction_static, dynamicFriction = (float)t.friction_dynamic, bounciness = (float)t.restitution,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }
}
