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

        /// <summary>C20 last clause, at the M-006.8 tolerance. Measured 2026-11-14: 2.3 cm (2 mechs) and 4.6 cm (4 mechs) after 20 s: this test is expected to fail until that is resolved (see evidence/T-005/EVIDENCE.md, "not met").</summary>
        [TestCase(2)]
        [TestCase(4)]
        [Category("KnownFailing")]
        public void NMechs_MechAIsUnchangedWithinOneCentimetre_WhetherOthersExistOrNot(int count)
        {
            var cfg = Config(); NVec3 alone, together;
            using (var sim = new MechSimulation(cfg.Body, Dt)) alone = RunGroup(1, out _, out _, sim);
            using (var sim = new MechSimulation(cfg.Body, Dt)) together = RunGroup(count, out _, out _, sim);
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
