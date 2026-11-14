using System;
using System.Collections.Generic;
using Game.Control;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Scenarios;
using Game.Units.Mech.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = Game.Core.EntityId;
using NVec3 = System.Numerics.Vector3;

namespace Game.Units.Mech.PlayTests
{
    /// <summary>Arbiter that exists only in this test assembly (C19): the mean of all valid pilots.</summary>
    sealed class MeanArbiter : IIntentArbiter
    {
        public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
        {
            var f = new IntentFrame(); NVec3 sum = NVec3.Zero; float effort = 0;
            foreach (var i in inputs) { if (!i.valid) continue; f.move = i.move; sum += i.target; effort += i.effort; f.pilots++; }
            if (f.pilots > 0) { f.target = sum / f.pilots; f.effort = effort / f.pilots; }
            return f;
        }
    }

    public class MechPlayTests
    {
        const double Dt = 1.0 / 60.0;
        static MechConfig Config() => MechConfig.LoadDefault();
        static PilotSeat Seat(MechSimulation sim, Action<ScriptedPilotSource> script) { var p = new ScriptedPilotSource(sim.Core.Clock); script(p); return new PilotSeat(new SinglePilotArbiter(), p); }

        /// <summary>A failed assertion must not leave bodies in the scene for the next test.</summary>
        [TearDown]
        public void DestroyLeftovers()
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("mech_") || go.name == "ground" || go.name.StartsWith("r1_") || go.name == "impact_box" || go.name == "strip_capture") UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Physics.simulationMode = SimulationMode.FixedUpdate; UnityEngine.Physics.gravity = new Vector3(0, -9.81f, 0);
        }

        [Test]
        public void FeatureFlags_SwitchTheModulesOff_AsShipped()
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt, true, 0, MechSimulation.RunFlags(mech: false)))
                Assert.Throws<InvalidOperationException>(() => sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false), "units.mech.enabled off: no mech is assembled");
            using (var sim = new MechSimulation(cfg.Body, Dt, true, 0, MechSimulation.RunFlags(body: false)))
                Assert.Throws<InvalidOperationException>(() => sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false), "physics.body.enabled off: no body is created");
            // the shipped files: both off
            var errors = new CollectingErrorSink();
            var shipped = FeatureFlagLoader.LoadFolder(System.IO.Path.Combine(Application.dataPath, "Game/Config/FeatureFlags"), errors);
            Assert.That(shipped.IsOn(MechConfig.FlagEnabled), Is.False); Assert.That(shipped.IsOn(MechConfig.FlagBodyEnabled), Is.False);
            using (var sim = new MechSimulation(cfg.Body, Dt, true, 0, shipped))
                Assert.Throws<InvalidOperationException>(() => sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false));
            // logs are flags too: with the gait-log flag off a run writes no gait file
            string dir = System.IO.Path.Combine(Application.temporaryCachePath, "flag_test_" + Guid.NewGuid().ToString("N"));
            using (var sim = new MechSimulation(cfg.Body, Dt, true, 0, MechSimulation.RunFlags(gaitLog: false, physicsLog: false)))
            {
                var unit = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false);
                unit.StartLogs(dir, "x", "test", 0.2); sim.Run(0.5); unit.CloseLogs();
            }
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(dir, "gait", "x.gait.jsonl")), Is.False); Assert.That(System.IO.File.Exists(System.IO.Path.Combine(dir, "physics", "x.physics.jsonl")), Is.False);
        }

        [Test]
        public void Stand_IsStable_AndUsesNoExternalForce()
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var unit = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false);
                sim.Run(10); var com10 = unit.Body.Com; sim.Run(10);
                Assert.That(unit.Phase, Is.EqualTo(LocoPhase.Stand));
                Assert.That((unit.Body.Com - com10).Length(), Is.LessThan(0.05f), "R-005 bring-up step 3: CoM drift <= 5 cm");
                Assert.That(unit.Body.ExternalWorkJ, Is.EqualTo(0)); Assert.That(unit.Body.NonFootContacts, Is.EqualTo(0));
                double fn = 0; for (int f = 0; f < unit.Body.FootCount; f++) { unit.Body.GetFoot(f, out var fs); fn += fs.NormalForceN; Assert.That(fs.Contact, Is.True); }
                Assert.That(fn, Is.EqualTo(unit.Body.TotalMassKg * 9.81).Within(0.02 * unit.Body.TotalMassKg * 9.81), "the feet carry the weight");
            }
        }

        [Test]
        public void Walk_ComesFromIntentOnly_MovesForward_AndEveryFootfallEventArrivesInItsStep()
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var unit = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0).At(2, MoveId.Walk, new NVec3(0, 0, 3000), 0.8f)), Vector3.zero, 0, false);
                int events = 0, late = 0; unit.FootfallProbe = (e, receivedAt) => { events++; if (receivedAt != e.Step) late++; Assert.That(e.Unit, Is.EqualTo(unit.Id)); };
                sim.Run(40);
                Assert.That(unit.Phase, Is.Not.EqualTo(LocoPhase.Falling).And.Not.EqualTo(LocoPhase.Fallen), "walks without falling");
                Assert.That(unit.Body.Com.Z, Is.GreaterThan(60f), "it covers ground");
                Assert.That(unit.Body.ExternalWorkJ, Is.EqualTo(0), "C10: no external force in a locomotion run");
                Assert.That(events, Is.GreaterThanOrEqualTo(8)); Assert.That(late, Is.EqualTo(0), "F-009: delivered in the contact step");
                Assert.That(unit.Command.Speed, Is.LessThan(unit.Command.RequestedSpeed), "the command is limited and the log says by what");
                Assert.That(unit.Command.Causes, Is.Not.EqualTo(LimitCause.None));
            }
        }

        static NVec3 RunGroup(int count, out List<MechUnit> unitsOut, out Dictionary<EntityId, int> perUnit, MechSimulation sim)
        {
            var cfg = Config(); var units = new List<MechUnit>(); var counts = new Dictionary<EntityId, int>();
            for (int i = 0; i < count; i++)
            {
                int k = i;
                Action<ScriptedPilotSource> script = i == 0 ? (Action<ScriptedPilotSource>)(p => p.At(0, MoveId.Stand, default, 0).At(2, MoveId.Walk, new NVec3(0, 0, 3000), 0.8f)) : (p => p.At(0, MoveId.Stand, default, 0).At(1 + k, MoveId.Turn, new NVec3(5000, 0, 0), 0));
                var u = sim.Spawn(cfg, Seat(sim, script), new Vector3(300f * i, 0, 0), 0, false);
                u.FootfallProbe = (e, at) => { counts[e.Unit] = counts.TryGetValue(e.Unit, out int n) ? n + 1 : 1; };
                units.Add(u);
            }
            sim.Run(20);
            unitsOut = units; perUnit = counts;
            return units[0].Body.Com;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void NMechs_EachHasItsOwnSeatConfigAndIds_AndAllOperate(int count)
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                RunGroup(count, out var units, out var perUnit, sim);
                Assert.That(new HashSet<EntityId>(units.ConvertAll(u => u.Id)).Count, Is.EqualTo(count), "distinct unit IDs");
                Assert.That(units[0].Body.Com.Z, Is.GreaterThan(20f), "mech A walks");
                for (int i = 0; i < count; i++)
                {
                    var u = units[i];
                    Assert.That(perUnit.ContainsKey(u.Id), Is.True, "every unit publishes its own footfalls, tagged with its ID");
                    Assert.That(u.Phase, Is.Not.EqualTo(LocoPhase.Fallen).And.Not.EqualTo(LocoPhase.Falling));
                    int feetInFrame = 0; foreach (var seg in cfg.Frame.segments) if (seg.kind == "foot") feetInFrame++;
                    Assert.That(u.Body.FootCount, Is.EqualTo(feetInFrame), "foot count comes from the frame");
                    for (int j = i + 1; j < count; j++) { Assert.That(ReferenceEquals(u.Seat, units[j].Seat), Is.False); Assert.That(ReferenceEquals(u.Body, units[j].Body), Is.False); }
                }
            }
        }

        /// <summary>C20 last clause, at the M-006.8 tolerance. Not met: a few centimetres after 20 s (values in evidence/T-005/EVIDENCE.md). The onset is written to the test output.</summary>
        [TestCase(2)]
        [TestCase(4)]
        [Category("KnownFailing")]
        public void NMechs_MechAIsUnchangedWithinOneCentimetre_WhetherOthersExistOrNot(int count)
        {
            var cfg = Config(); NVec3 alone, together;
            using (var sim = new MechSimulation(cfg.Body, Dt)) alone = RunGroup(1, out _, out _, sim);
            using (var sim = new MechSimulation(cfg.Body, Dt)) together = RunGroup(count, out _, out _, sim);
            // diagnostic for DEF-T005-arch-2 / physics-9: when does mech A first differ, and is the other mech's controller involved?
            var a1 = new List<NVec3>(); var a2 = new List<NVec3>(); var a3 = new List<NVec3>();
            Action<ScriptedPilotSource> walk = p => p.At(0, MoveId.Stand, default, 0).At(2, MoveId.Walk, new NVec3(0, 0, 3000), 0.8f);
            using (var sim = new MechSimulation(cfg.Body, Dt)) { var a = sim.Spawn(cfg, Seat(sim, walk), Vector3.zero, 0, false); for (int i = 0; i < 600; i++) { sim.StepOnce(); a1.Add(a.Body.Com); } }
            using (var sim = new MechSimulation(cfg.Body, Dt)) { var a = sim.Spawn(cfg, Seat(sim, walk), Vector3.zero, 0, false); sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), new Vector3(300f, 0, 0), 0, false); for (int i = 0; i < 600; i++) { sim.StepOnce(); a2.Add(a.Body.Com); } }
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                // the second body is a rigid, uncontrolled mech (all joints locked) resting 300 m away: no controller, no events of interest
                var a = sim.Spawn(cfg, Seat(sim, walk), Vector3.zero, 0, false);
                var inert = sim.World.CreateBody(cfg.Frame, cfg.Body, sim.Core.EntityIds.Allocate(), new BodySpawn { Position = new NVec3(300, 0, 0), LockAllJoints = true, Visuals = false });
                for (int i = 0; i < 600; i++) { sim.StepOnce(); a3.Add(a.Body.Com); }
                sim.World.DestroyBody(inert);
            }
            // a rigid mech dropped from 5 m, 300 m away: no controller at all, but its contacts with the shared ground begin while A walks
            var a4 = new List<NVec3>(); long dropContact = -1;
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var a = sim.Spawn(cfg, Seat(sim, walk), Vector3.zero, 0, false);
                var dropped = sim.World.CreateBody(cfg.Frame, cfg.Body, sim.Core.EntityIds.Allocate(), new BodySpawn { Position = new NVec3(300, 60, 0), LockAllJoints = true, Visuals = false });
                for (int i = 0; i < 600; i++) { sim.StepOnce(); a4.Add(a.Body.Com); dropped.GetFoot(0, out var df); if (dropContact < 0 && df.NormalForceN > 0) dropContact = i + 1; }
                sim.World.DestroyBody(dropped);
            }
            int onsetDrop = -1; for (int i = 0; i < 600; i++) if (onsetDrop < 0 && (a1[i] - a4[i]).Length() > 0) onsetDrop = i + 1;
            TestContext.WriteLine("C20 diagnostic: alone vs with a rigid mech dropped 300 m away (no controller): the dropped body first touches the ground at step " + dropContact + ", mech A first differs at step " + onsetDrop + ", difference at 10 s " + (a1[599] - a4[599]).Length().ToString("E2") + " m");
            // a second controlled mech that turns on the spot from t = 2 s: when does A first differ, and what was B doing in that step?
            var a5 = new List<NVec3>(); var bPhase = new List<LocoPhase>(); var bFeet = new List<int>(); var bClamp = new List<int>();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var a = sim.Spawn(cfg, Seat(sim, walk), Vector3.zero, 0, false);
                var b2 = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0).At(2, MoveId.Turn, new NVec3(5000, 0, 0), 0)), new Vector3(300f, 0, 0), 0, false);
                for (int i = 0; i < 600; i++) { sim.StepOnce(); a5.Add(a.Body.Com); bPhase.Add(b2.Phase); b2.Body.GetFoot(0, out var f0); b2.Body.GetFoot(1, out var f1); bFeet.Add((f0.Contact ? 1 : 0) + (f1.Contact ? 2 : 0)); bClamp.Add(b2.Body.JointSpeedClampCount); }
            }
            int onsetTurn = -1; for (int i = 0; i < 600; i++) if (onsetTurn < 0 && (a1[i] - a5[i]).Length() > 0) onsetTurn = i;
            int firstLift = bFeet.FindIndex(x => x != 3), firstShift = bPhase.FindIndex(x => x != LocoPhase.Stand);
            TestContext.WriteLine("C20 diagnostic: alone vs with a mech turning on the spot 300 m away: B leaves Stand at step " + (firstShift + 1) + ", B's first foot lift at step " + (firstLift + 1) + ", mech A first differs at step " + (onsetTurn + 1) + (onsetTurn >= 0 ? " (B phase " + bPhase[onsetTurn] + ", B feet-in-contact mask " + bFeet[onsetTurn] + ", previous step mask " + bFeet[Math.Max(0, onsetTurn - 1)] + ", B clamp count " + bClamp[onsetTurn] + ")" : "") + ", difference at 10 s " + (a1[599] - a5[599]).Length().ToString("E2") + " m");
            int onsetStanding = -1, onsetInert = -1;
            for (int i = 0; i < 600; i++) { if (onsetStanding < 0 && (a1[i] - a2[i]).Length() > 0) onsetStanding = i + 1; if (onsetInert < 0 && (a1[i] - a3[i]).Length() > 0) onsetInert = i + 1; }
            TestContext.WriteLine("C20 diagnostic: mech A alone vs with a standing controlled mech 300 m away: first differing step " + onsetStanding + ", difference at 2 s " + (a1[119] - a2[119]).Length().ToString("E2") + " m, at 10 s " + (a1[599] - a2[599]).Length().ToString("E2") + " m; alone vs with an inert rigid mech (no controller): first differing step " + onsetInert + ", at 2 s " + (a1[119] - a3[119]).Length().ToString("E2") + " m, at 10 s " + (a1[599] - a3[599]).Length().ToString("E2") + " m; after 20 s with " + count + " mechs: " + (together - alone).Length().ToString("E2") + " m");
            Assert.That((together - alone).Length(), Is.LessThan(0.01f), "mech A's centre of mass after 20 s, alone against in company");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void ArbiterSwap_SeveralPilotsDriveOneMech_ThroughATestOnlyArbiter(int pilots)
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var seat = new PilotSeat { arbiter = new MeanArbiter() };
                // each pilot asks for a different point; the mean is straight ahead at (0, 0, 2000)
                for (int i = 0; i < pilots; i++) seat.pilots.Add(new ScriptedPilotSource(sim.Core.Clock).At(0, MoveId.Stand, default, 0).At(2, MoveId.Walk, new NVec3(600f * (i - (pilots - 1) / 2f), 0, 2000), 0.8f));
                var unit = sim.Spawn(cfg, seat, Vector3.zero, 0, false);
                sim.Run(30);
                Assert.That(unit.Intent.pilots, Is.EqualTo(pilots));
                Assert.That(unit.Intent.target.X, Is.EqualTo(0f).Within(1e-3f));
                Assert.That(unit.Body.Com.Z, Is.GreaterThan(30f)); Assert.That(Math.Abs(unit.Body.Com.X), Is.LessThan(15f), "it walks toward the merged target");
                Assert.That(unit.Phase, Is.Not.EqualTo(LocoPhase.Fallen));
            }
        }

        [Test]
        public void LargeShove_EndsInAPhysicalFall_WithFallEvents_AndNoNumericalBlowUp()
        {
            var cfg = Config();
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var unit = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false);
                var probe = new FallProbe(); sim.Core.Bus.Subscribe<MechFallStarted>(probe); sim.Core.Bus.Subscribe<MechFallEnded>(probe);
                double maxSpeed = 0;
                long steps = (long)Math.Round(40 / Dt);
                for (long i = 0; i < steps; i++)
                {
                    if (sim.Time >= 5 && sim.Time < 7) unit.Body.AddExternalForce(unit.Body.FindLink("pelvis"), new NVec3(0, 0, (float)(0.6 * unit.Body.TotalMassKg * 9.81)), unit.Body.Com);
                    sim.StepOnce();
                    for (int l = 0; l < unit.Body.LinkCount; l++) { unit.Body.GetLink(l, out var s); Assert.That(float.IsNaN(s.Position.X + s.Velocity.X), Is.False); maxSpeed = Math.Max(maxSpeed, s.Velocity.Length()); }
                }
                Assert.That(probe.Started, Is.EqualTo(1), "one fall_start"); Assert.That(probe.Ended, Is.EqualTo(1), "it comes to rest: one fall_end");
                Assert.That(unit.Phase, Is.EqualTo(LocoPhase.Fallen)); Assert.That(unit.Body.Com.Y, Is.LessThan(15f));
                Assert.That(maxSpeed, Is.LessThan(60), "M-006.7: link speed <= 60 m/s");
                Assert.That(unit.Body.ExternalWorkJ, Is.GreaterThan(0), "the shove is a logged external force");
            }
        }

        [TestCase(76.0, 6.0e6)]
        [TestCase(88.0, 1.5e7)]
        public void MassAndHeightCorners_LoadByConfigOnly_AndStand(double height, double mass)
        {
            var cfg = Config().ScaledTo(height, mass);
            using (var sim = new MechSimulation(cfg.Body, Dt))
            {
                var unit = sim.Spawn(cfg, Seat(sim, p => p.At(0, MoveId.Stand, default, 0)), Vector3.zero, 0, false);
                Assert.That(unit.Body.TotalMassKg, Is.EqualTo(mass).Within(1e-3 * mass), "M-006.1 at the corner");
                sim.Run(10);
                Assert.That(unit.Phase, Is.EqualTo(LocoPhase.Stand)); Assert.That(unit.Body.NonFootContacts, Is.EqualTo(0));
            }
        }

        sealed class FallProbe : IEventHandler<MechFallStarted>, IEventHandler<MechFallEnded>
        {
            public int Started, Ended;
            public void Handle(in MechFallStarted e) => Started++;
            public void Handle(in MechFallEnded e) => Ended++;
        }
    }
}
