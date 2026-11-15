using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.ControlBridge.Tests
{
    /// <summary>T-030c: the seat pump follows slot release and rejoin (the remote pilot leaves the seat; a rejoin is assigned again by policy).</summary>
    public class PumpSlotLifecycleTests
    {
        private const double StepS = 0.02;

        private sealed class FirstValid : IIntentArbiter
        {
            public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
            {
                for (int i = 0; i < inputs.Count; i++)
                    if (inputs[i].valid) return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
                return default(IntentFrame);
            }
        }
        private sealed class FuncPilot : IPilotSource { public PilotInput Value; public PilotInput Read() => Value; }

        private static FeatureFlagSnapshot Flags() => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        { new KeyValuePair<string, bool>("net.coop", true), new KeyValuePair<string, bool>(SeatBridgeFlags.Seat, true) });

        [Test]
        public void ReleaseUnassigns_SeatFallsBackToLocal_RejoinIsReassigned()
        {
            var flags = Flags();
            var host = new NetHostSession(new HandshakeValidator(flags, StepS, "1.0", 4), s => new StateSnapshot { HostStep = s }, new NetLog());
            var pump = new HostSeatPump(host);
            var idleLocal = new FuncPilot(); // valid = false: the host player is not steering
            var mech = new MechSeat(flags, 0, new FirstValid(), idleLocal);
            pump.AddMech(mech);
            pump.AssignPolicy = slot => mech;
            double stamp = 0;

            NetClientSession Join(out ClientSeatLink link, out FuncPilot pilot)
            {
                LoopbackTransport.CreatePair(out var h, out var c);
                host.AddConnection(h);
                var cl = new NetClientSession(c, flags, StepS, "1.0", new NetLog(), 50);
                pilot = new FuncPilot { Value = new PilotInput { move = MoveId.Walk, target = new Vector3(5, 0, 5), effort = 1f, valid = true } };
                link = new ClientSeatLink(flags, cl, pilot);
                cl.Join();
                return cl;
            }
            void Run(int n, ClientSeatLink link) { for (int i = 0; i < n; i++) { stamp += StepS; pump.Advance(stamp); link?.Advance(stamp); } }

            var cl1 = Join(out var link1, out _);
            Run(12, link1);
            Assert.AreEqual(ClientState.Joined, cl1.State);
            Assert.AreSame(mech, pump.MechOf(cl1.Slot), "the policy assigned the joined slot");
            Assert.AreEqual(1, mech.Remotes.Count);
            Assert.AreEqual(MoveId.Walk, mech.LastIntent.move);
            var slot = cl1.Slot;

            cl1.Leave(0);
            Run(2, null);
            Assert.IsNull(pump.MechOf(slot), "released slot is unassigned");
            Assert.AreEqual(0, mech.Remotes.Count, "its pilot left the seat");
            Assert.AreEqual(0, mech.LastIntent.pilots, "nothing steers the mech any more");
            Assert.AreEqual(MoveId.Stand, mech.LastIntent.move);

            var cl2 = Join(out var link2, out _);
            Run(12, link2);
            Assert.AreEqual(slot, cl2.Slot, "same slot again");
            Assert.AreSame(mech, pump.MechOf(cl2.Slot));
            Assert.AreEqual(1, mech.Remotes.Count, "exactly one remote pilot, not two");
            Assert.AreEqual(MoveId.Walk, mech.LastIntent.move);
        }
    }
}
