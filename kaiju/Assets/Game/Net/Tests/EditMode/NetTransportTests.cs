using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    public class NetTransportTests
    {
        private static byte[] B(int v) => new[] { (byte)v, (byte)(v >> 8) };
        private static int V(byte[] b) => b[0] | (b[1] << 8);

        private static List<string> Drain(ITransport t, long step)
        {
            var l = new List<string>();
            while (t.TryReceive(out var ch, out var p)) l.Add(step + ":" + (ch == NetChannel.Reliable ? "R" : "U") + V(p));
            return l;
        }

        [Test]
        public void Loopback_DeliversInSendOrderPerChannel_AndCopiesPayload()
        {
            LoopbackTransport.CreatePair(out var a, out var b);
            var buf = B(1);
            a.Send(NetChannel.Reliable, buf);
            buf[0] = 99;
            a.Send(NetChannel.Unreliable, B(2));
            a.Send(NetChannel.Reliable, B(3));
            CollectionAssert.AreEqual(new[] { "0:R1", "0:U2", "0:R3" }, Drain(b, 0));
            Assert.IsEmpty(Drain(a, 0));
        }

        [Test]
        public void Loopback_CloseStopsSends()
        {
            LoopbackTransport.CreatePair(out var a, out var b);
            a.Close();
            Assert.IsFalse(a.IsConnected);
            Assert.IsFalse(b.IsConnected);
            Assert.IsFalse(b.Send(NetChannel.Reliable, B(1)));
        }

        // Sends 40 unreliable + 10 reliable messages one per step, records what arrives and when.
        private static string Run(ulong seed, LinkConditions c)
        {
            LoopbackTransport.CreatePair(out var a, out var b);
            var link = new SimulatedLinkTransport(a, seed, c);
            var log = new List<string>();
            for (long step = 0; step < 80; step++)
            {
                link.Advance(step);
                if (step < 40) link.Send(NetChannel.Unreliable, B((int)step));
                if (step < 10) link.Send(NetChannel.Reliable, B(1000 + (int)step));
                log.AddRange(Drain(b, step));
            }
            return string.Join(",", log) + "|dropped=" + link.DroppedCount;
        }

        [Test]
        public void Link_SameSeedSameOutcome_DifferentSeedDiffers()
        {
            var c = new LinkConditions { DelaySteps = 3, JitterSteps = 4, UnreliableLoss = 0.25 };
            Assert.AreEqual(Run(42, c), Run(42, c));
            Assert.AreNotEqual(Run(42, c), Run(43, c));
        }

        [Test]
        public void Link_LossOnlyHitsUnreliable_AndRoughlyMatchesRate()
        {
            var c = new LinkConditions { DelaySteps = 1, UnreliableLoss = 0.5 };
            string r = Run(7, c);
            int reliable = 0; foreach (var s in r.Split('|')[0].Split(',')) if (s.Contains(":R")) reliable++;
            Assert.AreEqual(10, reliable);
            int dropped = int.Parse(r.Split('=')[1]);
            Assert.That(dropped, Is.InRange(8, 32));
        }

        [Test]
        public void Link_DelayIsExactInSteps_NoSleeps()
        {
            LoopbackTransport.CreatePair(out var a, out var b);
            var link = new SimulatedLinkTransport(a, 1, new LinkConditions { DelaySteps = 5 });
            link.Advance(10);
            link.Send(NetChannel.Reliable, B(1));
            for (long s = 10; s < 15; s++) { link.Advance(s); Assert.IsEmpty(Drain(b, s), "step " + s); }
            link.Advance(15);
            CollectionAssert.AreEqual(new[] { "15:R1" }, Drain(b, 15));
        }

        [Test]
        public void Link_ReliableStaysOrderedUnderJitter()
        {
            LoopbackTransport.CreatePair(out var a, out var b);
            var link = new SimulatedLinkTransport(a, 99, new LinkConditions { DelaySteps = 1, JitterSteps = 20 });
            for (int i = 0; i < 50; i++) { link.Advance(i); link.Send(NetChannel.Reliable, B(i)); }
            link.Advance(200);
            int expect = 0;
            while (b.TryReceive(out _, out var p)) Assert.AreEqual(expect++, V(p));
            Assert.AreEqual(50, expect);
        }

        [Test]
        public void Link_StepCannotGoBackwards()
        {
            LoopbackTransport.CreatePair(out var a, out _);
            var link = new SimulatedLinkTransport(a, 1, default);
            link.Advance(5);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => link.Advance(4));
        }
    }

    public class NetIntentTests
    {
        private static IntentPacket P(int slot, uint seq, long step = 0) =>
            new IntentPacket(PlayerSlot.FromIndex(slot), step, seq, 0, new IntentPayload(seq, 0));

        [Test]
        public void StaleSequence_DropsDuplicatesAndOlder_PerSlot()
        {
            var f = new StaleSequenceFilter();
            Assert.IsTrue(f.Accept(P(1, 5)));
            Assert.IsFalse(f.Accept(P(1, 5)), "duplicate");
            Assert.IsFalse(f.Accept(P(1, 4)), "older");
            Assert.IsTrue(f.Accept(P(1, 7)), "gap is fine (loss)");
            Assert.IsFalse(f.Accept(P(1, 6)), "late after a newer one");
            Assert.IsTrue(f.Accept(P(2, 1)), "other slot is independent");
            Assert.AreEqual(3, f.DroppedCount);
        }

        [Test]
        public void StaleSequence_HandlesWrapAround()
        {
            var f = new StaleSequenceFilter();
            Assert.IsTrue(f.Accept(P(0, uint.MaxValue - 1)));
            Assert.IsTrue(f.Accept(P(0, uint.MaxValue)));
            Assert.IsTrue(f.Accept(P(0, 0)));
            Assert.IsFalse(f.Accept(P(0, uint.MaxValue)));
        }

        [Test]
        public void IntentPacket_RoundTripsAndRejectsBadLength()
        {
            var p = new IntentPacket(PlayerSlot.FromIndex(3), 123456789012L, 77, 123456789000L, new IntentPayload(0xDEADBEEFUL, 0x0123456789ABCDEFUL));
            var bytes = NetCodec.Encode(p);
            Assert.AreEqual(NetCodec.IntentPacketBytes, bytes.Length);
            Assert.IsTrue(NetCodec.TryDecode(bytes, out IntentPacket q));
            Assert.AreEqual(p.Slot, q.Slot); Assert.AreEqual(p.ClientStep, q.ClientStep); Assert.AreEqual(p.Sequence, q.Sequence);
            Assert.AreEqual(p.LastHostStepSeen, q.LastHostStepSeen);
            Assert.AreEqual(p.Payload.Lo, q.Payload.Lo); Assert.AreEqual(p.Payload.Hi, q.Payload.Hi);
            Assert.IsFalse(NetCodec.TryDecode(new byte[bytes.Length - 1], out IntentPacket _));
        }

        [Test]
        public void Intents_OverLossyLink_StaleOnesAreDropped()
        {
            LoopbackTransport.CreatePair(out var client, out var host);
            var link = new SimulatedLinkTransport(client, 5, new LinkConditions { DelaySteps = 1, JitterSteps = 6 });
            var filter = new StaleSequenceFilter();
            var applied = new List<uint>();
            for (uint s = 1; s <= 60; s++)
            {
                link.Advance(s);
                link.Send(NetChannel.Unreliable, NetCodec.Encode(P(1, s, s)));
                while (host.TryReceive(out _, out var bytes))
                    if (NetCodec.TryDecode(bytes, out IntentPacket p) && filter.Accept(p)) applied.Add(p.Sequence);
            }
            link.Advance(100);
            while (host.TryReceive(out _, out var bytes))
                if (NetCodec.TryDecode(bytes, out IntentPacket p) && filter.Accept(p)) applied.Add(p.Sequence);
            for (int i = 1; i < applied.Count; i++) Assert.Greater(applied[i], applied[i - 1]);
            Assert.Greater(filter.DroppedCount, 0, "jitter should have reordered at least one packet");
        }
    }
}
