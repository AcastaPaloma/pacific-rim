using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    /// <summary>T-030c: the UDP transport over real loopback sockets (localhost only, ephemeral ports), then the full handshake, leave and rejoin through it.</summary>
    public class UdpTransportTests
    {
        private static bool Until(Func<bool> cond, int ms = 3000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(1); }
            return cond();
        }

        private static FeatureFlagSnapshot Flags() => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", true) });

        [Test]
        public void ReliableIsOrderedAndComplete_UnreliableArrives()
        {
            using (var l = new UdpHostListener(0))
            {
                var c = UdpPeer.Connect("127.0.0.1", l.Port);
                for (int i = 0; i < 200; i++) c.Send(NetChannel.Reliable, BitConverter.GetBytes(i));
                c.Send(NetChannel.Unreliable, new byte[] { 9, 9 });
                UdpPeer h = null;
                Assert.IsTrue(Until(() => l.TryAccept(out h) || h != null));
                var got = new List<int>(); bool unrel = false;
                Assert.IsTrue(Until(() =>
                {
                    while (h.TryReceive(out var ch, out var d)) { if (ch == NetChannel.Reliable) got.Add(BitConverter.ToInt32(d, 0)); else unrel = d.Length == 2; }
                    c.TryReceive(out _, out _); // lets the client read acks and resend
                    return got.Count == 200;
                }), "all reliable messages delivered");
                for (int i = 0; i < 200; i++) Assert.AreEqual(i, got[i]);
                Assert.IsTrue(unrel);
                c.Close();
            }
        }

        [Test]
        public void ReliableSurvivesLostDatagrams_ByResending()
        {
            using (var l = new UdpHostListener(0))
            {
                var c = UdpPeer.Connect("127.0.0.1", l.Port);
                c.Send(NetChannel.Reliable, new byte[] { 1 });
                UdpPeer h = null;
                Assert.IsTrue(Until(() => l.TryAccept(out h) || h != null));
                // swallow the first copy so the receiver never sees it: drop what arrived
                Assert.IsTrue(Until(() => h.TryReceive(out _, out var d) && d.Length == 1));
                // a second message whose datagram is "lost": send to a peer that is not listening (never delivered), then rely on resend
                c.Send(NetChannel.Reliable, new byte[] { 2 });
                Assert.IsTrue(Until(() => { c.TryReceive(out _, out _); return h.TryReceive(out _, out var d) && d[0] == 2; }));
                Assert.IsTrue(Until(() => { c.TryReceive(out _, out _); return c.UnackedCount == 0; }), "acks clear the resend queue");
                c.Close();
            }
        }

        [Test]
        public void Disconnect_IsSeenByTheOtherSide()
        {
            using (var l = new UdpHostListener(0))
            {
                var c = UdpPeer.Connect("127.0.0.1", l.Port);
                c.Send(NetChannel.Unreliable, new byte[] { 1 });
                UdpPeer h = null;
                Assert.IsTrue(Until(() => l.TryAccept(out h) || h != null));
                c.Close();
                Assert.IsTrue(Until(() => { h.TryReceive(out _, out _); return !h.IsConnected; }));
                Assert.IsFalse(c.Send(NetChannel.Unreliable, new byte[] { 1 }));
            }
        }

        [Test]
        public void HandshakeLeaveAndRejoin_OverRealSockets()
        {
            using (var l = new UdpHostListener(0))
            {
                var hostLog = new NetLog();
                var host = new NetHostSession(new HandshakeValidator(Flags(), 0.02, "1.0", 4),
                    s => new StateSnapshot { HostStep = s, Entities = new[] { new EntityState(EntityId.FromValue(1), s * 0.1f, 0, 0, 5, 0, 0, 0, 0) } }, hostLog);
                long step = 0;
                void HostTick() { while (l.TryAccept(out var p)) host.AddConnection(p); host.Poll(step); host.Step(step, new NullSink()); step++; }

                var c1 = new NetClientSession(UdpPeer.Connect("127.0.0.1", l.Port), Flags(), 0.02, "1.0", new NetLog(), 50);
                c1.Join();
                Assert.IsTrue(Until(() => { HostTick(); c1.Poll(step); return c1.State == ClientState.Joined; }));
                Assert.AreEqual(1, c1.Slot.Index);
                Assert.IsTrue(Until(() => { HostTick(); c1.Poll(step); c1.SendIntent(step, new IntentPayload(1, 2)); return c1.LastHostStepSeen > 5; }), "snapshots flow");

                c1.Leave(step);
                Assert.IsTrue(Until(() => { HostTick(); return host.OccupiedSlots == 1; }), "slot freed after the leave datagram");

                var c2 = new NetClientSession(UdpPeer.Connect("127.0.0.1", l.Port), Flags(), 0.02, "1.0", new NetLog(), 50);
                c2.Join();
                Assert.IsTrue(Until(() => { HostTick(); c2.Poll(step); return c2.State == ClientState.Joined; }));
                Assert.AreEqual(1, c2.Slot.Index, "rejoined into the freed slot");

                // mismatching flags are refused over the wire too
                var bad = new NetClientSession(UdpPeer.Connect("127.0.0.1", l.Port), new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", true), new KeyValuePair<string, bool>("x.y", true) }), 0.02, "1.0", new NetLog(), 50);
                bad.Join();
                Assert.IsTrue(Until(() => { HostTick(); bad.Poll(step); return bad.State == ClientState.Refused; }));
                Assert.IsTrue((bad.Refusal.Code & RefuseCode.FlagsMismatch) != 0);
                Assert.AreEqual(2, host.OccupiedSlots);
            }
        }

        private sealed class NullSink : IIntentSink { public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source) { } }
    }
}
