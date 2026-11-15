using System.Collections.Generic;
using NVec3 = System.Numerics.Vector3;
using Game.Control.Contracts;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Net.ControlBridge.Unity.Tests
{
    /// <summary>T-030 step 2 / T-031 in Unity: per-mech behaviours on GameObjects, one host and one client in one process, stepped by hand.</summary>
    public class MechSeatBehaviourTests
    {
        private sealed class FirstValid : IIntentArbiter
        {
            public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
            {
                for (int i = 0; i < inputs.Count; i++) if (inputs[i].valid) return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
                return default(IntentFrame);
            }
        }
        private sealed class Pilot : IPilotSource { public long N; public PilotInput Read() => new PilotInput { move = MoveId.Walk, target = new NVec3(N, 0, -N), effort = 0.5f, valid = true }; }

        [Test]
        public void HostBehaviour_And_ClientBehaviour_Loopback_InOneProcess()
        {
            var flags = new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", true), new KeyValuePair<string, bool>(SeatBridgeFlags.Seat, true) });
            var host = new NetHostSession(new HandshakeValidator(flags, 0.02, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
            LoopbackTransport.CreatePair(out var h, out var c);
            var hs = new SimulatedLinkTransport(h, 1, new LinkConditions()); var cs = new SimulatedLinkTransport(c, 2, new LinkConditions());
            host.AddConnection(hs);
            var client = new NetClientSession(cs, flags, 0.02, "1.0", new NetLog(), 50);
            client.Join();
            long tick = 0;
            for (int i = 0; i < 400 && client.State != ClientState.Joined; i++)
            {
                tick++; hs.Advance(tick); cs.Advance(tick); host.Poll(tick / 20); client.Poll(tick / 20);
            }
            Assert.AreEqual(ClientState.Joined, client.State);

            var pump = new HostSeatPump(host);
            var hostMech = new MechSeat(flags, 0, new FirstValid());
            pump.Assign(client.Slot, hostMech);
            var pilot = new Pilot();
            var clientMech = new MechSeat(flags, 0, new FirstValid(), pilot);
            var link = new ClientSeatLink(flags, client, pilot);

            var go1 = new GameObject("hostMech"); var go2 = new GameObject("clientMech");
            try
            {
                var hb = go1.AddComponent<MechSeatBehaviour>(); hb.ConfigureHost(hostMech, pump);
                var cb = go2.AddComponent<MechSeatBehaviour>(); cb.ConfigureClient(clientMech, link);
                double stamp = 0; int received = 0;
                hb.IntentReady += f => { if (f.pilots == 1) received++; };
                for (int k = 0; k < 60; k++)
                {
                    for (int i = 0; i < 20; i++) { tick++; hs.Advance(tick); cs.Advance(tick); }
                    stamp += 0.02; pilot.N = k;
                    hb.Tick(stamp); cb.Tick(stamp);
                }
                Assert.Greater(received, 50);
                Assert.AreEqual(MoveId.Walk, hb.Frame.move);
                Assert.AreEqual(0.5f, hb.Frame.effort, IntentCodec.EffortMaxError);
                Assert.AreEqual(1, pump.Step > 0 ? 1 : 0);
            }
            finally { Object.DestroyImmediate(go1); Object.DestroyImmediate(go2); }
        }

        [Test]
        public void Behaviour_IsInert_WhenFlagOff()
        {
            var off = FeatureFlagSnapshot.AllOff;
            var go = new GameObject("m");
            try
            {
                var b = go.AddComponent<MechSeatBehaviour>();
                var host = new NetHostSession(new HandshakeValidator(off, 0.02, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
                var pump = new HostSeatPump(host);
                b.ConfigureHost(new MechSeat(off, 0, new FirstValid()), pump);
                b.Tick(0.02);
                Assert.AreEqual(-1, pump.Step, "pump never advanced");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
