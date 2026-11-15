using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    public class IntentBufferTests
    {
        private sealed class Rec : IIntentSink
        {
            public readonly List<string> Log = new List<string>();
            public readonly List<(int slot, ulong v, IntentSource src)> Calls = new List<(int, ulong, IntentSource)>();
            public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
            {
                Calls.Add((slot.Index, payload.Lo, source));
                Log.Add(slot.Index + ":" + payload.Lo + ":" + source);
            }
        }

        private static PlayerSlot S(int i) => PlayerSlot.FromIndex(i);
        private static IntentPacket P(int slot, uint seq, ulong v) => new IntentPacket(S(slot), seq, seq, 0, new IntentPayload(v, 0));

        private static IntentHostBuffer Host(params int[] slots)
        {
            var h = new IntentHostBuffer();
            foreach (var s in slots) h.RegisterSlot(S(s));
            return h;
        }

        private static string Step(IntentHostBuffer h, long step)
        {
            var r = new Rec();
            h.Deliver(step, r);
            return string.Join(",", r.Log);
        }

        [Test]
        public void HoldConstantsMatchCoreStep()
        {
            Assert.AreEqual(IntentHostBuffer.HoldSteps, (int)System.Math.Round(IntentHostBuffer.HoldSeconds / IntentHostBuffer.CoreStepSeconds));
        }

        [Test]
        public void OutOfOrderAndDuplicates_AppliedOnce_InSequenceOrder()
        {
            var h = Host(1);
            var applied = new List<ulong>();
            // arrival order: 2, 1 (late), 2 (dup), 3, 3 (dup), 5, 4 (late), 6
            uint[] arrivals = { 2, 1, 2, 3, 3, 5, 4, 6 };
            long step = 0;
            foreach (var s in arrivals)
            {
                h.Accept(P(1, s, s * 10));
                var r = new Rec(); h.Deliver(step++, r);
                if (r.Calls[0].src == IntentSource.Fresh) applied.Add(r.Calls[0].v);
            }
            CollectionAssert.AreEqual(new ulong[] { 20, 30, 50, 60 }, applied);
            Assert.AreEqual(4, h.DroppedStale); // 1, dup 2, dup 3, 4
        }

        [Test]
        public void SameStepBurst_DeliversNewestOnce_AndLateOlderIsDropped()
        {
            var h = Host(1);
            h.Accept(P(1, 7, 70)); h.Accept(P(1, 9, 90)); h.Accept(P(1, 8, 80));
            Assert.AreEqual("1:90:Fresh", Step(h, 0));
            Assert.AreEqual(1, h.Superseded);
            Assert.AreEqual(1, h.DroppedStale);
            Assert.AreEqual("1:90:Held", Step(h, 1));
        }

        [Test]
        public void SequenceWrapAround_IsAccepted()
        {
            var h = Host(0);
            h.Accept(P(0, uint.MaxValue, 1));
            Assert.AreEqual("0:1:Fresh", Step(h, 0));
            h.Accept(P(0, 0, 2));
            Assert.AreEqual("0:2:Fresh", Step(h, 1));
            h.Accept(P(0, uint.MaxValue, 1));
            Assert.AreEqual("0:2:Held", Step(h, 2));
        }

        [Test]
        public void Hold100ms_ThenNeutral()
        {
            var h = Host(0);
            Assert.AreEqual("0:0:Neutral", Step(h, 0), "nothing ever arrived");
            h.Accept(P(0, 1, 42));
            Assert.AreEqual("0:42:Fresh", Step(h, 10));
            for (long s = 11; s <= 15; s++) Assert.AreEqual("0:42:Held", Step(h, s), "step " + s);
            Assert.AreEqual("0:0:Neutral", Step(h, 16));
            Assert.AreEqual("0:0:Neutral", Step(h, 40));
            h.Accept(P(0, 2, 7));
            Assert.AreEqual("0:7:Fresh", Step(h, 41));
            Assert.AreEqual("0:7:Held", Step(h, 42));
            Assert.AreEqual(5 + 1, h.HeldSteps);
            Assert.AreEqual(1 + 1 + 1, h.NeutralSteps);
        }

        [Test]
        public void SlotOrder_IsAscending_WhateverTheArrivalOrder()
        {
            var perms = new[] { new[] { 0, 1, 2 }, new[] { 2, 1, 0 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 } };
            string expected = null;
            foreach (var perm in perms)
            {
                var h = Host(2, 0, 1); // registration order also scrambled
                foreach (var s in perm) h.Accept(P(s, 1, (ulong)(100 + s)));
                string got = Step(h, 0);
                Assert.AreEqual("0:100:Fresh,1:101:Fresh,2:102:Fresh", got);
                expected = expected ?? got;
                Assert.AreEqual(expected, got);
            }
        }

        [Test]
        public void SilentSlot_StillGetsNeutral_InOrder()
        {
            var h = Host(0, 1);
            h.Accept(P(1, 1, 5));
            Assert.AreEqual("0:0:Neutral,1:5:Fresh", Step(h, 0));
        }

        [Test]
        public void UnknownSlot_AndMalformed_AreDropped()
        {
            var h = Host(0);
            Assert.IsFalse(h.Accept(P(3, 1, 1)));
            h.Receive(new byte[] { 3, 1, 2 }, S(0));
            h.Receive(new byte[0], S(0));
            h.Receive(null, S(0));
            Assert.AreEqual(1, h.DroppedUnknownSlot);
            Assert.AreEqual(3, h.DroppedMalformed);
            Assert.AreEqual("0:0:Neutral", Step(h, 0));
        }

        [Test]
        public void Sender_RepeatsPrevious2_AndHostReceivesBundle()
        {
            LoopbackTransport.CreatePair(out var c, out var hostEnd);
            var sender = new IntentSender(S(1), c);
            var buf = new IntentPacket[3];
            var counts = new List<int>();
            for (int i = 1; i <= 4; i++)
            {
                Assert.IsTrue(sender.Send(i, 0, new IntentPayload((ulong)i, 0)));
                Assert.IsTrue(hostEnd.TryReceive(out var ch, out var data));
                Assert.AreEqual(NetChannel.Unreliable, ch);
                Assert.IsTrue(IntentBundle.TryDecode(data, buf, out int n));
                counts.Add(n);
                Assert.AreEqual((uint)i, buf[0].Sequence);
                if (n > 1) Assert.AreEqual((uint)(i - 1), buf[1].Sequence);
                if (n > 2) { Assert.AreEqual((uint)(i - 2), buf[2].Sequence); Assert.AreEqual((ulong)(i - 2), buf[2].Payload.Lo); }
                Assert.AreEqual(S(1), buf[0].Slot);
                Assert.AreEqual(i, buf[0].ClientStep);
            }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 3 }, counts);
        }

        [Test]
        public void Pump_RoutesUnreliableToBuffer_AndOtherSlotIsRejected()
        {
            LoopbackTransport.CreatePair(out var c, out var hostEnd);
            var h = Host(1, 2);
            new IntentSender(S(1), c).Send(1, 0, new IntentPayload(11, 0));
            new IntentSender(S(2), c).Send(1, 0, new IntentPayload(22, 0)); // spoof: this connection is bound to slot 1
            c.Send(NetChannel.Reliable, new byte[] { 9 });
            int reliable = 0;
            h.Pump(hostEnd, S(1), d => reliable++);
            Assert.AreEqual(1, reliable);
            Assert.AreEqual("1:11:Fresh,2:0:Neutral", Step(h, 0));
        }

        private struct Rng
        {
            private ulong _s;
            public Rng(ulong seed) { _s = seed; }
            public double Next()
            {
                ulong z = (_s += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
                return (z >> 11) * (1.0 / (1UL << 53));
            }
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(0xC0FFEEUL)]
        public void TwoPercentLoss_WithRepeats_NoGapLongerThanOneStep(ulong seed)
        {
            // Sender 60 packets/s (every 5 units), host 50 steps/s (every 6 units), unit = 1/300 s. 120 s.
            // Each datagram is lost with p = 0.02 and arrives 1 unit later (before the host step of that unit).
            LoopbackTransport.CreatePair(out var clientEnd, out var hostEnd);
            var sender = new IntentSender(S(0), clientEnd);
            var h = Host(0);
            var rng = new Rng(seed);
            var inFlight = new Queue<(int due, byte[] data)>();
            int steps = 0, gap = 0, maxGap = 0, lost = 0, sent = 0;
            var sink = new Rec();
            var learned = new HashSet<uint>();
            var tmp = new IntentPacket[3];
            for (int u = 0; u < 300 * 120; u++)
            {
                if (u % 5 == 0)
                {
                    sender.Send(sent, 0, new IntentPayload((ulong)(sent + 1), 0));
                    hostEnd.TryReceive(out _, out var data);
                    sent++;
                    if (rng.Next() < 0.02) lost++; else inFlight.Enqueue((u + 1, data));
                }
                while (inFlight.Count > 0 && inFlight.Peek().due <= u)
                {
                    var d = inFlight.Dequeue().data;
                    if (IntentBundle.TryDecode(d, tmp, out int n)) for (int i = 0; i < n; i++) learned.Add(tmp[i].Sequence);
                    h.Receive(d, S(0));
                }
                if (u % 6 == 0)
                {
                    sink.Calls.Clear();
                    h.Deliver(steps++, sink);
                    if (sink.Calls[0].src == IntentSource.Fresh) gap = 0;
                    else { gap++; if (gap > maxGap) maxGap = gap; }
                }
            }
            Assert.Greater(lost, 100, "loss actually exercised");
            // Gap in what the host learned: client intents (sequences) absent from every datagram that arrived.
            // A single lost datagram is always covered by the 2 repeats, so a gap needs 3 losses in a row.
            int run = 0, maxLearnGap = 0;
            for (uint q = 1; q <= (uint)sent - 2; q++)
            {
                if (learned.Contains(q)) run = 0; else { run++; if (run > maxLearnGap) maxLearnGap = run; }
            }
            Assert.LessOrEqual(maxLearnGap, 1);
            // Host steps with no fresh intent (held): 50 Hz steps vs 60 Hz packets, so one lost datagram can empty
            // a step window; two such steps in a row need two separate losses. Seeded, so deterministic.
            Assert.LessOrEqual(maxGap, 2);
            Assert.AreEqual(1, h.NeutralSteps, "only step 0, before the first arrival");
        }
    }
}
