using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Physics.Body.PlayTests
{
    public class BodyPlayTests
    {
        const double Dt = 1.0 / 60.0;
        static MechFrameData Frame() => JsonUtility.FromJson<MechFrameData>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/mech_frame_balanced.json")));
        static BodyTuning Tuning() => JsonUtility.FromJson<BodyTuning>(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Mech/body_tuning.json")));
        string _dir;
        [SetUp] public void SetUp() { _dir = Path.Combine(Application.temporaryCachePath, "body_playtests"); Directory.CreateDirectory(Path.Combine(_dir, "physics")); }

        /// <summary>A failed assertion must not leave bodies in the scene for the next test.</summary>
        [TearDown]
        public void DestroyLeftovers()
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("mech_") || go.name == "ground" || go.name.StartsWith("r1_") || go.name == "impact_box" || go.name == "strip_capture") UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Physics.simulationMode = SimulationMode.FixedUpdate; UnityEngine.Physics.gravity = new Vector3(0, -9.81f, 0);
        }

        /// <summary>R-005 risk R1 (bring-up step 1): one revolute link of known inertia. Measured engine behaviour the body adapter relies on.</summary>
        [Test]
        public void SingleJoint_DriveUnitsAndSign_AreAsTheAdapterAssumes()
        {
            var mode = UnityEngine.Physics.simulationMode; var gravity = UnityEngine.Physics.gravity;
            UnityEngine.Physics.simulationMode = SimulationMode.Script; UnityEngine.Physics.gravity = Vector3.zero;
            var rootGo = new GameObject("r1_root");
            try
            {
                var root = rootGo.AddComponent<ArticulationBody>(); root.immovable = true;
                var childGo = new GameObject("r1_child"); childGo.transform.SetParent(rootGo.transform, false);
                var c = childGo.AddComponent<ArticulationBody>(); c.jointType = ArticulationJointType.RevoluteJoint; c.anchorPosition = Vector3.zero; c.anchorRotation = Quaternion.identity; c.twistLock = ArticulationDofLock.FreeMotion;
                const float m = 1e3f, len = 10f, ic = 1e4f; float inertia = ic + m * len * len;
                c.mass = m; c.automaticCenterOfMass = false; c.automaticInertiaTensor = false; c.centerOfMass = new Vector3(0, -len, 0); c.inertiaTensor = Vector3.one * ic; c.inertiaTensorRotation = Quaternion.identity; c.linearDamping = 0; c.angularDamping = 0; c.jointFriction = 0;
                var d = c.xDrive; d.driveType = ArticulationDriveType.Force; d.stiffness = inertia * Mathf.Pow(2 * Mathf.PI * 0.5f, 2); d.damping = 0; d.forceLimit = float.MaxValue; d.target = 10f; c.xDrive = d;
                float max = -1; int maxStep = 0;
                for (int i = 1; i <= 120; i++) { UnityEngine.Physics.Simulate((float)Dt); if (c.jointPosition[0] > max) { max = c.jointPosition[0]; maxStep = i; } }
                Assert.That(maxStep * Dt, Is.InRange(0.88, 1.05), "stiffness is N m per radian: half period about 1.0 s for k = I (2 pi 0.5 Hz)^2 (0.13 s if it were per degree). Measured 1.000 s with the PGS solver and 0.933 s with TGS.");
                Assert.That(max, Is.EqualTo(20 * Mathf.Deg2Rad).Within(0.03f), "target is in degrees, jointPosition in radians");
                d.damping = 2 * inertia * Mathf.PI; c.xDrive = d;
                for (int i = 0; i < 600; i++) UnityEngine.Physics.Simulate((float)Dt);
                c.transform.localRotation.ToAngleAxis(out float angle, out Vector3 axis);
                Assert.That(angle * Mathf.Sign(axis.x), Is.EqualTo(10f).Within(0.1f), "a positive target is a positive Unity rotation about the anchor x axis");
                d.stiffness = 0; d.damping = inertia * 5f; d.targetVelocity = 0.5f; d.target = 0; c.xDrive = d;
                for (int i = 0; i < 300; i++) UnityEngine.Physics.Simulate((float)Dt);
                Assert.That(c.jointVelocity[0], Is.EqualTo(0.5f * Mathf.Deg2Rad).Within(1e-3f), "measured: targetVelocity is in degrees per second (the scripting reference says rad/s)");
                d.damping = 0; d.targetVelocity = 0; c.xDrive = d; c.jointVelocity = new ArticulationReducedSpace(0f); c.jointForce = new ArticulationReducedSpace(inertia * 0.1f);
                for (int i = 0; i < 60; i++) UnityEngine.Physics.Simulate((float)Dt);
                Assert.That(c.jointVelocity[0], Is.EqualTo(0.1f).Within(1e-3f), "jointForce is N m, positive along the axis, and persists until rewritten");
            }
            finally { Object.DestroyImmediate(rootGo); UnityEngine.Physics.simulationMode = mode; UnityEngine.Physics.gravity = gravity; }
        }

        [Test]
        public void Body_IsBuiltFromConfig_MassesAndSettingsReadBack()
        {
            var r = TruthScenes.UnitSanity(Frame(), Tuning(), Dt, _dir, "test", null);
            var v = new Dictionary<string, double>(); foreach (var kv in r.Values) v[kv.Key] = kv.Value;
            Assert.That(v["mass_sum_rel_error"], Is.LessThan(1e-3)); Assert.That(v["link_mass_worst_rel_error"], Is.LessThan(1e-3));
            Assert.That(v["gravity_y"], Is.EqualTo(-9.81).Within(1e-6)); Assert.That(v["max_damping_readback"], Is.EqualTo(0));
            Assert.That(v["density_sec4_kg_m3"], Is.InRange(1000, 2500)); Assert.That(v["dof_count"], Is.EqualTo(23)); Assert.That(v["link_count"], Is.EqualTo(15));
        }

        [Test]
        public void PassiveDrop_LosesNoMoreEnergyThanTheIntegratorBound()
        {
            var r = TruthScenes.Drop(Frame(), Tuning(), Dt, _dir, "test", null);
            var v = new Dictionary<string, double>(); foreach (var kv in r.Values) v[kv.Key] = kv.Value;
            Assert.That(v["energy_loss_rel_of_released"], Is.LessThanOrEqualTo(v["threshold_loss"]), "M-006.2: loss <= dt/t + 0.5 %");
            Assert.That(v["energy_gain_worst_rel"], Is.LessThanOrEqualTo(0.001), "M-006.2: never a gain above 0.1 %");
        }

        [Test]
        public void FootContact_IsPublishedOncePerContact_InTheContactStep()
        {
            var tuning = Tuning();
            using (var core = new CoreServices(new CoreSettings(Dt, 10000), FeatureFlagSnapshot.AllOff, new CollectingErrorSink()))
            using (var world = new BodyWorld(tuning, Dt, true, core.Bus))
            {
                var ground = BodyWorld.CreateGround(tuning);
                var probe = new Probe(); core.Bus.Subscribe<FootContact>(probe); core.Bus.Subscribe<FootLiftOff>(probe); core.Bus.Subscribe<FootLoaded>(probe);
                var body = new ArticulatedBody(world, Frame(), tuning, EntityId.FromValue(7), core.Bus, new BodyBuildOptions { LockAllJoints = true, Visuals = false, Position = new Vector3(0, 0.5f, 0) });
                // the body state is the reference: every false -> true change of a foot's contact flag must have exactly one event, in that same step
                var begins = new List<KeyValuePair<int, long>>(); int ends = 0; var was = new bool[body.FootCount];
                for (long step = 1; step <= 240; step++)
                {
                    world.Simulate(step);
                    for (int f = 0; f < body.FootCount; f++) { body.GetFoot(f, out var fs); if (fs.Contact && !was[f]) begins.Add(new KeyValuePair<int, long>(f, step)); if (!fs.Contact && was[f]) ends++; was[f] = fs.Contact; }
                }
                Assert.That(begins.Count, Is.GreaterThanOrEqualTo(body.FootCount));
                Assert.That(probe.Contacts.Count, Is.EqualTo(begins.Count), "exactly one event per contact: no misses, no duplicates while a foot stays loaded");
                for (int i = 0; i < begins.Count; i++)
                {
                    var e = probe.Contacts[i];
                    Assert.That(e.Unit, Is.EqualTo(EntityId.FromValue(7))); Assert.That(e.Foot, Is.EqualTo(begins[i].Key)); Assert.That(e.Step, Is.EqualTo(begins[i].Value), "published in the step in which the state first shows contact");
                    Assert.That(e.NormalForceN, Is.GreaterThanOrEqualTo((float)body.FOnN)); Assert.That(e.PressurePa, Is.EqualTo(e.NormalForceN / 150f).Within(1f)); Assert.That(e.Normal.Y, Is.GreaterThan(0.9f));
                }
                Assert.That(probe.Contacts[0].ApproachVelocity.Y, Is.LessThan(0f), "the first contact carries the approach velocity");
                Assert.That(probe.LiftOffs, Is.EqualTo(ends), "lift-off is an event too, one per contact end");
                // the follow-up event carries what the contact really did: its peak force and the impulse of its loading window
                Assert.That(probe.Loaded.Count, Is.EqualTo(begins.Count), "one FootLoaded per contact");
                foreach (var l in probe.Loaded)
                {
                    var first = probe.Contacts.Find(c => c.ContactId == l.ContactId);
                    Assert.That(l.Foot, Is.EqualTo(first.Foot)); Assert.That(l.PeakNormalForceN, Is.GreaterThanOrEqualTo(first.NormalForceN)); Assert.That(l.ImpulseNs, Is.GreaterThanOrEqualTo(first.VerticalImpulseNs)); Assert.That(l.Step, Is.GreaterThanOrEqualTo(first.Step));
                }
                Assert.That(body.JointSpeedClampCount, Is.EqualTo(0), "a rigid mech has no joint at the speed guard");
                world.DestroyBody(body); Object.DestroyImmediate(ground);
            }
        }

        /// <summary>Documents the measured PhysX limit behind body_tuning.sim_mass_unit_kg (evidence/T-005/probes): in kilograms the 10,000 t body does not rest on the ground.</summary>
        [Test]
        public void MassUnit_InKilogramsTheBodySinks_InTheConfiguredUnitItRests()
        {
            foreach (bool kilograms in new[] { true, false })
            {
                var tuning = Tuning(); if (kilograms) tuning.sim_mass_unit_kg = 1.0;
                using (var world = new BodyWorld(tuning, Dt, true, null))
                {
                    var ground = BodyWorld.CreateGround(tuning);
                    var body = new ArticulatedBody(world, Frame(), tuning, EntityId.FromValue(1), null, new BodyBuildOptions { LockAllJoints = true, Visuals = false });
                    for (long step = 1; step <= 300; step++) world.Simulate(step);
                    body.GetLink(0, out var pelvis);
                    if (kilograms) Assert.That(pelvis.Position.Y, Is.LessThan(38.5f), "with link masses of 1e5..3e6 PhysX units the feet sink through rigid ground");
                    else Assert.That(pelvis.Position.Y, Is.EqualTo(39.0f).Within(0.05f), "in the configured unit the rigid mech rests on its soles");
                    world.DestroyBody(body); Object.DestroyImmediate(ground);
                }
            }
        }

        sealed class Probe : IEventHandler<FootContact>, IEventHandler<FootLiftOff>, IEventHandler<FootLoaded>
        {
            public readonly List<FootContact> Contacts = new List<FootContact>(); public readonly List<FootLoaded> Loaded = new List<FootLoaded>(); public int LiftOffs;
            public void Handle(in FootLoaded e) => Loaded.Add(e);
            public void Handle(in FootContact e) => Contacts.Add(e);
            public void Handle(in FootLiftOff e) => LiftOffs++;
        }
    }
}
