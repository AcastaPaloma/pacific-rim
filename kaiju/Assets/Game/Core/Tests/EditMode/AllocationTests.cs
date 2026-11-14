using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>C12 (zero steady-state allocation) and C14 (throughput baseline, informational).</summary>
    public class AllocationTests
    {
        public const int WarmUp = 1000;
        public const int Publishes = 10000;

        private sealed class Counter<T> : IEventHandler<T> where T : unmanaged, IEvent
        {
            public long Calls;
            public void Handle(in T evt) { Calls++; }
        }

        private sealed class NestedPublisher : IEventHandler<PingEvent>, IEventHandler<BigEvent>
        {
            private readonly IEventBus _bus;
            public NestedPublisher(IEventBus bus) { _bus = bus; }
            public void Handle(in PingEvent e) => _bus.Publish(new PongEvent(e.Value));
            public void Handle(in BigEvent e) => _bus.Publish(new PongEvent((int)e.A));
        }

        [Test]
        public void AllocMeter_DetectsAllocations()
        {
            // Positive control: the meter must see a known allocation, or a "0 bytes" result would mean nothing.
            object[] sink = new object[1];
            var r = AllocMeter.Measure(() => { for (int i = 0; i < 1000; i++) sink[0] = new byte[64]; });
            Evidence.Line("alloc.txt", "control: 1000 x new byte[64] -> " + r);
            Assert.That(r.Bytes >= 0 || r.Count >= 0, Is.True, "no allocation witness in this runtime");
            if (r.Bytes >= 0) Assert.That(r.Bytes, Is.GreaterThanOrEqualTo(64000));
            if (r.Count >= 0) Assert.That(r.Count, Is.GreaterThanOrEqualTo(1000));
        }

        [TestCase(1, false, false)]
        [TestCase(8, false, false)]
        [TestCase(64, false, false)]
        [TestCase(1, true, false)]
        [TestCase(8, true, false)]
        [TestCase(64, true, false)]
        [TestCase(1, false, true)]
        [TestCase(8, false, true)]
        [TestCase(64, false, true)]
        [TestCase(1, true, true)]
        [TestCase(8, true, true)]
        [TestCase(64, true, true)]
        public void Bus_Publish_AllocatesNothing_SteadyState(int subscribers, bool bigEvent, bool nested)
        {
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            var smallHandlers = new List<Counter<PingEvent>>();
            var bigHandlers = new List<Counter<BigEvent>>();
            for (int i = 0; i < subscribers; i++)
            {
                if (bigEvent) { var h = new Counter<BigEvent>(); bigHandlers.Add(h); bus.Subscribe(h); }
                else { var h = new Counter<PingEvent>(); smallHandlers.Add(h); bus.Subscribe(h); }
            }
            var pong = new Counter<PongEvent>();
            if (nested)
            {
                var np = new NestedPublisher(bus);
                if (bigEvent) bus.Subscribe<BigEvent>(np); else bus.Subscribe<PingEvent>(np);
                bus.Subscribe(pong);
            }
            Action run = bigEvent
                ? (Action)(() => { for (int i = 0; i < Publishes; i++) bus.Publish(new BigEvent(i)); })
                : () => { for (int i = 0; i < Publishes; i++) bus.Publish(new PingEvent(i)); };

            for (int i = 0; i < WarmUp; i++) { if (bigEvent) bus.Publish(new BigEvent(i)); else bus.Publish(new PingEvent(i)); }
            var result = AllocMeter.Measure(run);

            string scenario = "publish x" + Publishes + " subs=" + subscribers + " event=" + (bigEvent ? "BigEvent(96B)" : "PingEvent(4B)") + " nested=" + nested;
            Evidence.Line("alloc.txt", scenario + " warmup=" + WarmUp + " -> " + result);
            long delivered = 0;
            foreach (var h in smallHandlers) delivered += h.Calls;
            foreach (var h in bigHandlers) delivered += h.Calls;
            Assert.That(delivered, Is.EqualTo((long)subscribers * (Publishes + WarmUp)));
            if (nested) Assert.That(pong.Calls, Is.EqualTo(Publishes + WarmUp));
            if (result.Bytes >= 0) Assert.That(result.Bytes, Is.EqualTo(0), scenario);
            if (result.Count >= 0) Assert.That(result.Count, Is.EqualTo(0), scenario);
        }

        [Test]
        public void FlagAndClockReads_AllocateNothing()
        {
            var list = new List<KeyValuePair<string, bool>>();
            for (int i = 0; i < 50; i++) list.Add(new KeyValuePair<string, bool>("module" + i + ".feature", i % 2 == 0));
            var flags = new FeatureFlagSnapshot(list);
            var clock = new SimulationClock(0.02);
            bool any = false; double t = 0;
            Action read = () =>
            {
                for (int i = 0; i < Publishes; i++)
                {
                    any ^= flags.IsOn("module17.feature") ^ flags.IsOn("not.listed");
                    clock.Advance();
                    t += clock.TimeSeconds + clock.Step;
                }
            };
            read();
            var r = AllocMeter.Measure(read);
            Evidence.Line("alloc.txt", "flag+clock reads x" + Publishes + " -> " + r);
            if (r.Bytes >= 0) Assert.That(r.Bytes, Is.EqualTo(0));
            if (r.Count >= 0) Assert.That(r.Count, Is.EqualTo(0));
            Assert.That(t, Is.GreaterThan(0));
        }

        // C14: informational baseline; it cannot fail on speed.
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(64)]
        public void Bus_Throughput_Baseline(int subscribers)
        {
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            for (int i = 0; i < subscribers; i++) bus.Subscribe(new Counter<PingEvent>());
            const int n = 200000;
            for (int i = 0; i < 20000; i++) bus.Publish(new PingEvent(i));
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) bus.Publish(new PingEvent(i));
            sw.Stop();
            double nsPerPublish = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            Evidence.Line("bench.txt", "subs=" + subscribers + " ns/publish=" + nsPerPublish.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + " ns/delivery=" + (nsPerPublish / subscribers).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " (n=" + n + ")");
            Assert.That(nsPerPublish, Is.GreaterThan(0));
        }
    
        // DEF-T001-perf-1: a burst deeper than the warm-up. The first burst grows the queue and ring; the second must not allocate.
        private sealed class BurstPublisher : IEventHandler<PingEvent>
        {
            private readonly IEventBus _bus; private readonly int _n;
            public BurstPublisher(IEventBus bus, int n) { _bus = bus; _n = n; }
            public void Handle(in PingEvent e) { for (int i = 0; i < _n; i++) _bus.Publish(new PongEvent(i)); }
        }

        [TestCase(5000)]
        public void Bus_Burst_SecondBurstAllocatesNothing(int burst)
        {
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            bus.Subscribe<PingEvent>(new BurstPublisher(bus, burst));
            var pong = new Counter<PongEvent>();
            bus.Subscribe(pong);
            var first = AllocMeter.Measure(() => bus.Publish(new PingEvent(0)));
            var second = AllocMeter.Measure(() => bus.Publish(new PingEvent(1)));
            string scenario = "burst of " + burst + " nested events in one cascade";
            Evidence.Line("alloc.txt", scenario + ": first burst -> " + first + "; second burst -> " + second);
            Assert.That(pong.Calls, Is.EqualTo(2L * burst));
            if (second.Bytes >= 0) Assert.That(second.Bytes, Is.EqualTo(0), scenario);
            if (second.Count >= 0) Assert.That(second.Count, Is.EqualTo(0), scenario);
        }
    }
}
