using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.ControlBridge.Tests
{
    /// <summary>T-030 step 2 (seat shell) and T-031 (two clients, two mechs, one process, simulated links), engine-free.</summary>
    public class MechSeatBridgeTests
    {
        private const int TicksPerStep = 20;
        private const double StepS = 0.02;

        private sealed class FirstValidArbiter : IIntentArbiter
        {
            public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
            {
                for (int i = 0; i < inputs.Count; i++)
                    if (inputs[i].valid) return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
                return default(IntentFrame);
            }
        }

        private sealed class FuncPilot : IPilotSource
        {
            public Func<PilotInput> Next; public PilotInput Read() => Next();
        }

        private static FeatureFlagSnapshot Flags(bool seat) => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        {
            new KeyValuePair<string, bool>("net.coop", true),
            new KeyValuePair<string, bool>(SeatBridgeFlags.Seat, seat),
        });

        private static PilotInput Script(int client, long n) => new PilotInput
        {
            move = (MoveId)(1 + (int)((n + client) % 5)),
            target = new Vector3(client * 100 + n * 0.5f, 0f, -n),
            effort = (n % 11) / 10f,
            valid = true,
        };

        [Test]
        public void FlagOff_SeatIsLocalOnly_RemoteRefused_ClientLinkSilent()
        {
            var off = Flags(false);
            var local = new FuncPilot { Next = () => Script(0, 1) };
            var seat = new MechSeat(off, 0, new FirstValidArbiter(), local);
            Assert.IsFalse(seat.BridgeOn);
            Assert.Throws<InvalidOperationException>(() => seat.AddRemote(PlayerSlot.FromIndex(1)));
            Assert.AreEqual(Script(0, 1).move, seat.Read().move, "local pilot still works");
            LoopbackTransport.CreatePair(out var h, out var c);
            var client = new NetClientSession(c, off, StepS, "1.0", new NetLog(), 50);
            var link = new ClientSeatLink(off, client, local);
            Assert.IsFalse(link.Advance(0.02));
            Assert.AreEqual(0, link.Sent);
        }

        [Test]
        public void PumpAdvance_IsIdempotentPerStamp()
        {
            var on = Flags(true);
            var host = new NetHostSession(new HandshakeValidator(on, StepS, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
            var pump = new HostSeatPump(host);
            var a = new MechSeat(on, 0, new FirstValidArbiter()); var b = new MechSeat(on, 1, new FirstValidArbiter());
            pump.AddMech(a); pump.AddMech(b);
            Assert.IsTrue(pump.Advance(0.02)); Assert.IsFalse(pump.Advance(0.02)); Assert.IsTrue(pump.Advance(0.04));
            Assert.AreEqual(1, pump.Step);
            pump.Assign(PlayerSlot.FromIndex(1), a);
            Assert.Throws<InvalidOperationException>(() => pump.Assign(PlayerSlot.FromIndex(1), b));
            Assert.AreSame(a, pump.MechOf(PlayerSlot.FromIndex(1)));
        }

        // ---- T-031: host + two clients; client 1 -> mech A (with host's own pilot behind it), client 2 -> mech B ----------------------

        private sealed class Peer
        {
            public NetClientSession Client; public SimulatedLinkTransport HostSide, ClientSide;
            public FuncPilot Pilot = new FuncPilot(); public ClientSeatLink Link; public int Index;
        }

        private sealed class World
        {
            public NetHostSession Host; public HostSeatPump Pump; public MechSeat MechA, MechB;
            public readonly List<Peer> Peers = new List<Peer>();
            public long Tick; public double Stamp;
            public FeatureFlagSnapshot Flags;

            public World(int rttMs)
            {
                Flags = MechSeatBridgeTests.Flags(true);
                Host = new NetHostSession(new HandshakeValidator(Flags, StepS, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
                Pump = new HostSeatPump(Host);
                MechA = new MechSeat(Flags, 0, new FirstValidArbiter());
                MechB = new MechSeat(Flags, 1, new FirstValidArbiter());
                Pump.AddMech(MechA); Pump.AddMech(MechB);
                for (int i = 1; i <= 2; i++)
                {
                    var p = new Peer { Index = i };
                    LoopbackTransport.CreatePair(out var h, out var c);
                    var cond = new LinkConditions { DelaySteps = rttMs / 2 };
                    p.HostSide = new SimulatedLinkTransport(h, (ulong)(10 * i), cond); p.ClientSide = new SimulatedLinkTransport(c, (ulong)(10 * i + 1), cond);
                    Host.AddConnection(p.HostSide);
                    p.Client = new NetClientSession(p.ClientSide, Flags, StepS, "1.0", new NetLog(), 50);
                    p.Client.Join();
                    Peers.Add(p);
                }
            }

            private void AdvanceLinks()
            {
                Tick++;
                foreach (var p in Peers) { p.HostSide.Advance(Tick); p.ClientSide.Advance(Tick); }
            }

            public void Join()
            {
                // joins ride the ordinary client Poll; run raw polls until both are Joined
                while (Peers.Exists(p => p.Client.State != ClientState.Joined))
                {
                    AdvanceLinks();
                    Host.Poll(Tick / TicksPerStep);
                    foreach (var p in Peers) p.Client.Poll(Tick / TicksPerStep);
                    Assert.Less(Tick, 4000, "join");
                }
                // assignment from the handshake slot: client 1 -> mech A, client 2 -> mech B
                Pump.Assign(Peers[0].Client.Slot, MechA);
                Pump.Assign(Peers[1].Client.Slot, MechB);
                foreach (var p in Peers) p.Link = new ClientSeatLink(Flags, p.Client, p.Pilot);
            }

            public void RunStep(bool clientsSend)
            {
                for (int i = 0; i < TicksPerStep; i++) AdvanceLinks();
                Stamp += StepS;
                Pump.Advance(Stamp);
                if (clientsSend) foreach (var p in Peers) p.Link.Advance(Stamp);
                else foreach (var p in Peers) p.Client.Poll(Tick / TicksPerStep);
            }
        }

        [TestCase(0, 2)]
        [TestCase(100, 5)]
        public void TwoClients_EachReachesOwnMech_InOrder(int rttMs, int maxLag)
        {
            var w = new World(rttMs); w.Join();
            Assert.AreNotEqual(w.Peers[0].Client.Slot, w.Peers[1].Client.Slot);
            long n = 0;
            foreach (var p in w.Peers) { var pp = p; pp.Pilot.Next = () => Script(pp.Index, n); }
            var outA = new List<IntentFrame>(); var outB = new List<IntentFrame>();
            for (int k = 0; k < 150; k++)
            {
                w.RunStep(true);
                n++;
                outA.Add(w.MechA.LastIntent); outB.Add(w.MechB.LastIntent);
            }
            AssertStream(outA, 1, maxLag); AssertStream(outB, 2, maxLag);
            Assert.AreEqual(0, w.MechA.Remotes[0].Held); Assert.AreEqual(0, w.MechB.Remotes[0].Held);
        }

        private static void AssertStream(List<IntentFrame> outs, int client, int maxLag)
        {
            // pilots is 1 from the first fresh frame on; the frame k then equals script(client, k - first)
            int first = outs.FindIndex(f => f.pilots == 1);
            Assert.GreaterOrEqual(first, 0); Assert.LessOrEqual(first, maxLag + 1);
            for (int k = first; k < outs.Count; k++)
            {
                var e = Script(client, k - first);
                Assert.AreEqual(e.move, outs[k].move, "client " + client + " step " + k);
                Assert.AreEqual(e.target, outs[k].target);
                Assert.AreEqual(e.effort, outs[k].effort, IntentCodec.EffortMaxError);
            }
        }

        [Test]
        public void HostLocalPilot_WinsFirstValid_WhileRemoteStillTracked()
        {
            var w = new World(0); w.Join();
            var host = new FuncPilot { Next = () => new PilotInput { move = MoveId.Stomp, valid = true, effort = 1f } };
            w.MechA.Seat.pilots.Insert(0, host);
            long n = 0;
            w.Peers[0].Pilot.Next = () => Script(1, n); w.Peers[1].Pilot.Next = () => Script(2, n);
            for (int k = 0; k < 20; k++) { w.RunStep(true); n++; }
            Assert.AreEqual(MoveId.Stomp, w.MechA.LastIntent.move);
            Assert.Greater(w.MechA.Remotes[0].Fresh, 10, "remote slot still replicated");
            Assert.AreNotEqual(MoveId.Stomp, w.MechB.LastIntent.move);
        }

        [Test]
        public void UnassignedSlot_Dropped_NoThrow()
        {
            var on = Flags(true);
            var host = new NetHostSession(new HandshakeValidator(on, StepS, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
            var pump = new HostSeatPump(host);
            pump.ApplyIntent(PlayerSlot.FromIndex(3), default(IntentPayload), IntentSource.Fresh);
            Assert.IsNull(pump.MechOf(PlayerSlot.FromIndex(3)));
        }
    }
}
