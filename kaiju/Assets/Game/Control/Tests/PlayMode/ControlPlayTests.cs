using Game.Control;
using Game.Control.Contracts;
using Game.Control.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Game.Control.PlayTests
{
    public class ControlPlayTests
    {
        static readonly Game.Core.FeatureFlagSnapshot On = new Game.Core.FeatureFlagSnapshot(new[] { new System.Collections.Generic.KeyValuePair<string, bool>(DevicePilotSource.Flag, true) });
        static ControlTuning Tuning() => JsonUtility.FromJson<ControlTuning>(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Game/Config/Control/control_tuning.json")));

        [Test]
        public void DevicePilot_IsAnIPilotSource_ThatTurnsTheStickIntoATargetAndEffort()
        {
            Vector2 stick = Vector2.zero; bool stop = false;
            IPilotSource source = new DevicePilotSource(On, Tuning(), () => new System.Numerics.Vector3(5, 0, 5), () => 0f, () => stick, () => stop);
            var seat = new PilotSeat(new SinglePilotArbiter(), source);
            Assert.That(seat.Read().move, Is.EqualTo(MoveId.Stop), "stick at rest: come to rest");
            stick = new Vector2(0, 1);
            var f = seat.Read();
            Assert.That(f.move, Is.EqualTo(MoveId.Walk)); Assert.That(f.effort, Is.GreaterThan(0.9f)); Assert.That(f.target.Z, Is.GreaterThan(300f)); Assert.That(f.target.X, Is.EqualTo(5f).Within(1f));
            stop = true;
            Assert.That(seat.Read().move, Is.EqualTo(MoveId.Stop));
            // interchangeable: the same seat works with a scripted source in the same slot
            seat.pilots[0] = new ScriptedPilotSource(new Game.Core.SimulationClock(1.0 / 60)).At(0, MoveId.Turn, new System.Numerics.Vector3(9, 0, 0), 0);
            Assert.That(seat.Read().move, Is.EqualTo(MoveId.Turn));
        }

        [Test]
        public void DevicePilot_WithTheRealInputSystemDevices_ReadsWithoutADevicePresent()
        {
            IPilotSource source = new DevicePilotSource(On, Tuning(), () => default, () => 0f);
            Assert.That(source.Read().valid, Is.True, "no gamepad, no keys pressed: a valid 'come to rest' input, never an exception");
        }

        [Test]
        public void DevicePilot_RefusesATuningFileOfAnotherSchema()
        {
            var t = Tuning(); t.schema = "control_tuning@v2";
            Assert.Throws<System.InvalidOperationException>(() => new DevicePilotSource(On, t, () => default, () => 0f));
        }

        [Test]
        public void DevicePilot_IsNotCreated_WhileItsFlagIsOff_AsShipped()
        {
            Assert.Throws<System.InvalidOperationException>(() => new DevicePilotSource(Game.Core.FeatureFlagSnapshot.AllOff, Tuning(), () => default, () => 0f));
            string shipped = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Game/Config/FeatureFlags/Control.json"));
            var flags = Game.Core.FeatureFlagLoader.Parse(shipped, "Control.json", new Game.Core.CollectingErrorSink());
            Assert.That(flags.IsOn(DevicePilotSource.Flag), Is.False, "ships off");
            Assert.Throws<System.InvalidOperationException>(() => new DevicePilotSource(flags, Tuning(), () => default, () => 0f));
        }
    }
}
