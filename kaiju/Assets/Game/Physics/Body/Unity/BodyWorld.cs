using System;
using System.Collections.Generic;
using Game.Physics.Body.Contracts;
using Unity.Collections;
using UnityEngine;

namespace Game.Physics.Body.Unity
{
    /// <summary>
    /// One per simulation context: routes PhysX contact events to the bodies that own the colliders, and owns scene-level physics setup.
    /// Instance, not static, so several contexts (tests, N mechs) never share state.
    /// </summary>
    public sealed class BodyWorld : IDisposable
    {
        readonly Dictionary<Collider, KeyValuePair<ArticulatedBody, int>> _owners = new Dictionary<Collider, KeyValuePair<ArticulatedBody, int>>();
        readonly List<ArticulatedBody> _bodies = new List<ArticulatedBody>();
        readonly SimulationMode _previousMode;
        readonly Vector3 _previousGravity;
        public double StepSeconds { get; }
        public IReadOnlyList<ArticulatedBody> Bodies => _bodies;

        /// <param name="scripted">True: the caller advances physics with <see cref="Simulate"/> (headless scenarios, tests). False: Unity's FixedUpdate does.</param>
        public BodyWorld(BodyTuning tuning, double stepSeconds, bool scripted)
        {
            StepSeconds = stepSeconds;
            _previousMode = UnityEngine.Physics.simulationMode; _previousGravity = UnityEngine.Physics.gravity;
            UnityEngine.Physics.gravity = new Vector3(0, -(float)tuning.gravity_mps2, 0);
            if (scripted) UnityEngine.Physics.simulationMode = SimulationMode.Script;
            UnityEngine.Physics.ContactEvent += OnContact;
        }

        public void Dispose()
        {
            UnityEngine.Physics.ContactEvent -= OnContact;
            UnityEngine.Physics.simulationMode = _previousMode; UnityEngine.Physics.gravity = _previousGravity;
            _owners.Clear(); _bodies.Clear();
        }

        internal void Register(ArticulatedBody body) => _bodies.Add(body);
        internal void Register(Collider c, ArticulatedBody body, int link) => _owners[c] = new KeyValuePair<ArticulatedBody, int>(body, link);

        /// <summary>Scripted mode: flush every body's drives and forces, advance PhysX one fixed step, then let bodies read the result. Returns after contacts of this step are known.</summary>
        public void Simulate(long stepAfter)
        {
            foreach (var b in _bodies) b.BeforeStep();
            UnityEngine.Physics.Simulate((float)StepSeconds);
            foreach (var b in _bodies) b.AfterStep(stepAfter);
        }

        void OnContact(PhysicsScene scene, NativeArray<ContactPairHeader>.ReadOnly headers)
        {
            for (int h = 0; h < headers.Length; h++)
            {
                var header = headers[h];
                for (int p = 0; p < header.pairCount; p++)
                {
                    ref readonly var pair = ref header.GetContactPair(p);
                    bool a = _owners.TryGetValue(pair.collider, out var oa), b = _owners.TryGetValue(pair.otherCollider, out var ob);
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
