using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Game.Control;
using Game.Control.Contracts;
using Game.Core;
using NUnit.Framework;

namespace Game.Control.Tests
{
    sealed class FixedPilot : IPilotSource
    {
        public PilotInput Input; public int Reads;
        public PilotInput Read() { Reads++; return Input; }
    }

    /// <summary>Lives only in the test assembly: proves a new arbiter is one new class and nothing else changes (D-003, MODULARITY 4.1 "Later").</summary>
    sealed class MeanArbiter : IIntentArbiter
    {
        public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
        {
            var f = new IntentFrame(); Vector3 sum = Vector3.Zero; float effort = 0;
            foreach (var i in inputs) { if (!i.valid) continue; f.move = i.move; sum += i.target; effort += i.effort; f.pilots++; }
            if (f.pilots > 0) { f.target = sum / f.pilots; f.effort = effort / f.pilots; }
            return f;
        }
    }

    public class ControlContractTests
    {
        [Test]
        public void Contract_HasExactlyTheModularity41Types()
        {
            var asm = typeof(IPilotSource).Assembly;
            Assert.That(typeof(IPilotSource).GetMethod("Read").ReturnType, Is.EqualTo(typeof(PilotInput)));
            Assert.That(typeof(IntentFrame).IsValueType, Is.True);
            Assert.That(typeof(IntentFrame).GetField("move").FieldType, Is.EqualTo(typeof(MoveId)));
            Assert.That(typeof(IntentFrame).GetField("target").FieldType, Is.EqualTo(typeof(Vector3)));
            Assert.That(typeof(IntentFrame).GetField("effort").FieldType, Is.EqualTo(typeof(float)));
            var merge = typeof(IIntentArbiter).GetMethod("Merge");
            Assert.That(merge.ReturnType, Is.EqualTo(typeof(IntentFrame)));
            Assert.That(merge.GetParameters().Single().ParameterType, Is.EqualTo(typeof(IReadOnlyList<PilotInput>)));
            Assert.That(typeof(PilotSeat).IsClass, Is.True);
            Assert.That(typeof(PilotSeat).GetField("pilots").FieldType, Is.EqualTo(typeof(List<IPilotSource>)));
            Assert.That(typeof(PilotSeat).GetField("arbiter").FieldType, Is.EqualTo(typeof(IIntentArbiter)));
            Assert.That(asm.GetName().Name, Is.EqualTo("Game.Control.Contracts"));
        }

        [Test]
        public void SinglePilotArbiter_IsTheOnlyShippedArbiter()
        {
            var arbiters = typeof(SinglePilotArbiter).Assembly.GetTypes().Where(t => typeof(IIntentArbiter).IsAssignableFrom(t) && !t.IsInterface).ToArray();
            Assert.That(arbiters, Is.EqualTo(new[] { typeof(SinglePilotArbiter) }));
        }

        [Test]
        public void SinglePilotArbiter_PassesTheInputStraightThrough()
        {
            var pilot = new FixedPilot { Input = new PilotInput { move = MoveId.Walk, target = new Vector3(1, 0, 200), effort = 0.6f, valid = true } };
            var seat = new PilotSeat(new SinglePilotArbiter(), pilot);
            var f = seat.Read();
            Assert.That(f.move, Is.EqualTo(MoveId.Walk)); Assert.That(f.target, Is.EqualTo(new Vector3(1, 0, 200))); Assert.That(f.effort, Is.EqualTo(0.6f)); Assert.That(f.pilots, Is.EqualTo(1));
            Assert.That(pilot.Reads, Is.EqualTo(1), "one read per pilot per step");
            Assert.That(new PilotSeat(new SinglePilotArbiter()).Read().move, Is.EqualTo(MoveId.Stand), "an empty seat stands");
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Seat_TakesAnyNumberOfPilots_AndASwappedArbiter(int pilots)
        {
            var seat = new PilotSeat { arbiter = new MeanArbiter() };
            for (int i = 0; i < pilots; i++) seat.pilots.Add(new FixedPilot { Input = new PilotInput { move = MoveId.Walk, target = new Vector3(0, 0, 100 * (i + 1)), effort = 0.2f * (i + 1), valid = true } });
            var f = seat.Read();
            Assert.That(f.pilots, Is.EqualTo(pilots));
            Assert.That(f.target.Z, Is.EqualTo(100f * (pilots + 1) / 2f).Within(1e-3f));
            Assert.That(f.effort, Is.EqualTo(0.2f * (pilots + 1) / 2f).Within(1e-5f));
        }
    }

    public class PilotSourceTests
    {
        [Test]
        public void ScriptedPilot_PlaysItsTimelineByClockStep()
        {
            var clock = new SimulationClock(1.0 / 60.0);
            var p = new ScriptedPilotSource(clock).At(0, MoveId.Stand, default, 0).At(1.0, MoveId.Walk, new Vector3(0, 0, 500), 0.8f).At(2.0, MoveId.Stop, default, 0);
            Assert.That(p.Read().move, Is.EqualTo(MoveId.Stand));
            for (int i = 0; i < 59; i++) clock.Advance();
            Assert.That(p.Read().move, Is.EqualTo(MoveId.Stand));
            clock.Advance();
            Assert.That(p.Read().move, Is.EqualTo(MoveId.Walk)); Assert.That(p.Read().effort, Is.EqualTo(0.8f));
            for (int i = 0; i < 60; i++) clock.Advance();
            Assert.That(p.Read().move, Is.EqualTo(MoveId.Stop));
            Assert.Throws<ArgumentException>(() => p.At(1.5, MoveId.Walk, default, 1));
        }

        [Test]
        public void StickMapping_GivesATargetPointAndEffort_NeverAJoint()
        {
            var t = new ControlTuning { schema = ControlTuning.Schema, stick_dead_zone = 0.15, target_distance_m = 400, stop_button_emergency_hold_s = 0.5 };
            var rest = StickMapping.Map(0.05f, 0.05f, false, false, new Vector3(10, 0, 20), 0, t);
            Assert.That(rest.move, Is.EqualTo(MoveId.Stop));
            var fwd = StickMapping.Map(0, 1, false, false, new Vector3(10, 0, 20), 0, t);
            Assert.That(fwd.move, Is.EqualTo(MoveId.Walk)); Assert.That(fwd.effort, Is.EqualTo(1f).Within(1e-5f)); Assert.That(fwd.target.Z, Is.EqualTo(420f).Within(1e-3f)); Assert.That(fwd.target.X, Is.EqualTo(10f).Within(1e-3f));
            var right = StickMapping.Map(0, 1, false, false, Vector3.Zero, (float)(Math.PI / 2), t);
            Assert.That(right.target.X, Is.EqualTo(400f).Within(1e-2f), "stick forward with the view turned 90 degrees clockwise walks toward +x");
            Assert.That(StickMapping.Map(0, 1, true, true, Vector3.Zero, 0, t).move, Is.EqualTo(MoveId.EmergencyStop));
            Assert.That(StickMapping.Map(0, 0.575f, false, false, Vector3.Zero, 0, t).effort, Is.EqualTo(0.5f).Within(1e-3f));
        }
    }
}
