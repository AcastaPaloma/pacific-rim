using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class EventBusTests
    {
        private CollectingErrorSink _errors;
        private EventBus _bus;

        [SetUp]
        public void SetUp()
        {
            _errors = new CollectingErrorSink();
            _bus = new EventBus(_errors, 10000);
        }

        // C7: every event of step N, nested ones included, is delivered before the outermost Publish returns.
        [Test]
        public void Bus_DeliversAllEventsWithinPublishingStep()
        {
            var clock = new SimulationClock(0.02);
            var seen = new List<string>();
            var pingSeen = new Handler<PingEvent>(e => { seen.Add("ping" + e.Value + "@" + clock.Step); _bus.Publish(new PongEvent(e.Value)); });
            var pongSeen = new Handler<PongEvent>(e => seen.Add("pong" + e.Value + "@" + clock.Step));
            _bus.Subscribe(pingSeen);
            _bus.Subscribe(pongSeen);
            for (int step = 0; step < 5; step++)
            {
                _bus.Publish(new PingEvent(step));
                Assert.That(seen, Has.Member("ping" + step + "@" + step));
                Assert.That(seen, Has.Member("pong" + step + "@" + step), "nested event must arrive in the same step");
                clock.Advance();
            }
            Assert.That(seen.Count, Is.EqualTo(10));
        }

        // C8: same subscriptions + publishes give the identical trace, 100 times, in every process and runner.
        public const string GoldenDispatchTraceHash = "6F72236CAACE7678";

        public static string RunDispatchScenario()
        {
            var trace = new StringBuilder();
            var bus = new EventBus(new CollectingErrorSink(), 10000);
            var handlers = new TraceHandler[12];
            for (int i = 0; i < handlers.Length; i++)
            {
                handlers[i] = new TraceHandler("h" + i, trace);
                if (i % 3 != 2) bus.Subscribe<PingEvent>(handlers[i]);
                if (i % 2 == 0) bus.Subscribe<PongEvent>(handlers[i]);
            }
            var relay = new Handler<PingEvent>(e => { if (e.Value % 2 == 0) bus.Publish(new PongEvent(e.Value * 10)); });
            bus.Subscribe(relay);
            var rng = new SplitMix64(42);
            for (int n = 0; n < 200; n++)
            {
                int k = rng.NextInt(handlers.Length);
                switch (rng.NextInt(4))
                {
                    case 0: bus.Publish(new PingEvent(n)); break;
                    case 1: bus.Publish(new PongEvent(n)); break;
                    case 2: if (!bus.Unsubscribe<PingEvent>(handlers[k])) bus.Subscribe<PingEvent>(handlers[k]); break;
                    default: if (!bus.Unsubscribe<PongEvent>(handlers[k])) bus.Subscribe<PongEvent>(handlers[k]); break;
                }
            }
            return trace.ToString();
        }

        [Test]
        public void Bus_DispatchTrace_IsReproducible()
        {
            string first = RunDispatchScenario();
            for (int i = 0; i < 100; i++) Assert.That(RunDispatchScenario(), Is.EqualTo(first), "repeat " + i);
            string hash = StableHash.Hex(StableHash.Fnv1a64(first));
            Evidence.Line("trace-hashes.txt", "dispatch " + hash + " length=" + first.Length);
            Assert.That(hash, Is.EqualTo(GoldenDispatchTraceHash), "trace differs from the committed golden hash");
        }

        // C9: re-entrancy semantics.
        [Test]
        public void Bus_PublishFromHandler_IsQueuedFifoAfterCurrentEvent()
        {
            var trace = new StringBuilder();
            var a = new TraceHandler("a", trace);
            var b = new TraceHandler("b", trace);
            var relay = new Handler<PingEvent>(e => { if (e.Value == 1) { _bus.Publish(new PongEvent(1)); _bus.Publish(new PingEvent(2)); _bus.Publish(new PongEvent(2)); } });
            _bus.Subscribe<PingEvent>(a);
            _bus.Subscribe(relay);
            _bus.Subscribe<PingEvent>(b);
            _bus.Subscribe<PongEvent>(b);
            _bus.Subscribe<PongEvent>(a);
            _bus.Publish(new PingEvent(1));
            Assert.That(trace.ToString(), Is.EqualTo("a:ping1;b:ping1;b:pong1;a:pong1;a:ping2;b:ping2;b:pong2;a:pong2;"));
        }

        [Test]
        public void Bus_UnsubscribeOtherDuringDispatch_RemovedGetsNothingMore()
        {
            Handler<PingEvent> victim = null;
            var killer = new Handler<PingEvent>(e => _bus.Unsubscribe(victim));
            victim = new Handler<PingEvent>(null);
            var last = new Handler<PingEvent>(null);
            _bus.Subscribe(killer);
            _bus.Subscribe(victim);
            _bus.Subscribe(last);
            _bus.Publish(new PingEvent(1));
            _bus.Publish(new PingEvent(2));
            Assert.That(victim.Calls, Is.EqualTo(0));
            Assert.That(killer.Calls, Is.EqualTo(2));
            Assert.That(last.Calls, Is.EqualTo(2));
            Assert.That(_bus.SubscriberCount<PingEvent>(), Is.EqualTo(2));
        }

        [Test]
        public void Bus_UnsubscribeSelfDuringDispatch_IsSafe()
        {
            Handler<PingEvent> self = null;
            self = new Handler<PingEvent>(e => _bus.Unsubscribe(self));
            var after = new Handler<PingEvent>(null);
            _bus.Subscribe(self);
            _bus.Subscribe(after);
            _bus.Publish(new PingEvent(1));
            _bus.Publish(new PingEvent(2));
            Assert.That(self.Calls, Is.EqualTo(1));
            Assert.That(after.Calls, Is.EqualTo(2));
            Assert.That(_errors.Count, Is.EqualTo(0));
        }

        [Test]
        public void Bus_SubscribeDuringDispatch_StartsWithNextPublishedEvent()
        {
            var lateSeen = new List<int>();
            var late = new Handler<PongEvent>(e => lateSeen.Add(e.Value));
            var adder = new Handler<PingEvent>(e =>
            {
                _bus.Publish(new PongEvent(1));      // queued before the subscription: not delivered to late
                _bus.Subscribe(late);
                _bus.Publish(new PongEvent(2));      // published after: delivered
            });
            var lateOnPing = new Handler<PingEvent>(null);
            var addPingSubscriber = new Handler<PingEvent>(e => { if (e.Value == 1) _bus.Subscribe(lateOnPing); });
            _bus.Subscribe(adder);
            _bus.Subscribe(addPingSubscriber);
            _bus.Publish(new PingEvent(1));
            Assert.That(lateSeen, Is.EqualTo(new[] { 2 }));
            Assert.That(lateOnPing.Calls, Is.EqualTo(0), "added during ping 1, so ping 1 is not delivered to it");
            _bus.Unsubscribe(adder);
            _bus.Publish(new PingEvent(2));
            Assert.That(lateOnPing.Calls, Is.EqualTo(1));
        }

        [Test]
        public void Bus_DoubleSubscribe_Throws()
        {
            var h = new Handler<PingEvent>(null);
            _bus.Subscribe(h);
            Assert.Throws<InvalidOperationException>(() => _bus.Subscribe(h));
        }

        // C10: a publish cycle hits the configured cap and fails loudly, naming the event types.
        [Test]
        public void Bus_PublishCycle_FailsLoudlyNamingEventTypes()
        {
            var bus = new EventBus(_errors, 100);
            var ping = new Handler<PingEvent>(e => bus.Publish(new PongEvent(e.Value + 1)));
            var pong = new Handler<PongEvent>(e => bus.Publish(new PingEvent(e.Value + 1)));
            bus.Subscribe(ping);
            bus.Subscribe(pong);
            var ex = Assert.Throws<EventCascadeLimitException>(() => bus.Publish(new PingEvent(0)));
            StringAssert.Contains("PingEvent", ex.Message);
            StringAssert.Contains("PongEvent", ex.Message);
            StringAssert.Contains("100", ex.Message);
            Assert.That(_errors.CountOf(CoreErrorKind.CascadeLimit), Is.EqualTo(1));
            Assert.That(ping.Calls + pong.Calls, Is.EqualTo(100));
            // The bus is usable afterwards.
            bus.Unsubscribe(pong);
            bus.Publish(new PingEvent(7));
            Assert.That(ping.Calls, Is.EqualTo(51));
        }

        // C11: a throwing handler is reported (type + subscriber), counted, and the others still run.
        [Test]
        public void Bus_ThrowingHandler_OthersStillReceive_ErrorReported()
        {
            var before = new Handler<PingEvent>(null);
            var thrower = new Handler<PingEvent>(e => throw new InvalidOperationException("boom " + e.Value));
            var after = new Handler<PingEvent>(null);
            var pong = new Handler<PongEvent>(null);
            _bus.Subscribe(before);
            _bus.Subscribe(thrower);
            _bus.Subscribe(after);
            _bus.Subscribe(pong);
            _bus.Publish(new PingEvent(1));
            _bus.Publish(new PongEvent(1));
            _bus.Publish(new PingEvent(2));
            Assert.That(before.Calls, Is.EqualTo(2));
            Assert.That(after.Calls, Is.EqualTo(2));
            Assert.That(pong.Calls, Is.EqualTo(1));
            Assert.That(_bus.HandlerExceptionCount, Is.EqualTo(2));
            Assert.That(_errors.CountOf(CoreErrorKind.HandlerException), Is.EqualTo(2));
            var err = _errors.Errors[0];
            Assert.That(err.EventType, Is.EqualTo(typeof(PingEvent)));
            StringAssert.Contains(typeof(Handler<PingEvent>).FullName, err.Subscriber);
            Assert.That(err.Exception.Message, Is.EqualTo("boom 1"));
        }

        // C13: typed isolation, and every IEvent type in the process is an unmanaged struct.
        [Test]
        public void Bus_TypedIsolation()
        {
            var ping = new Handler<PingEvent>(null);
            var pong = new Handler<PongEvent>(null);
            _bus.Subscribe(ping);
            _bus.Subscribe(pong);
            _bus.Publish(new PingEvent(1));
            _bus.Publish(new PingEvent(2));
            Assert.That(ping.Calls, Is.EqualTo(2));
            Assert.That(pong.Calls, Is.EqualTo(0));
        }

#pragma warning disable 0649 // fields exist only to be inspected by reflection
        private struct HoldsReference { public int X; public string Name; }
        private struct Nested { public long A; public HoldsReference Inner; }
        private struct AllValues { public long A; public double B; public BuildingId Id; }
#pragma warning restore 0649

        public static bool IsUnmanaged(Type t)
        {
            if (!t.IsValueType) return false;
            if (t.IsPrimitive || t.IsEnum || t.IsPointer) return true;
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!IsUnmanaged(f.FieldType)) return false;
            return true;
        }

        [Test]
        public void EventTypes_AreUnmanagedStructs()
        {
            Assert.That(IsUnmanaged(typeof(HoldsReference)), Is.False, "checker must catch a string field");
            Assert.That(IsUnmanaged(typeof(Nested)), Is.False, "checker must catch a nested reference");
            Assert.That(IsUnmanaged(typeof(AllValues)), Is.True);
            int found = 0;
            var bad = new List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (var t in types)
                {
                    if (t == null || t.IsInterface || !typeof(IEvent).IsAssignableFrom(t)) continue;
                    found++;
                    if (!IsUnmanaged(t)) bad.Add(t.FullName);
                }
            }
            Assert.That(found, Is.GreaterThanOrEqualTo(4));
            Assert.That(bad, Is.Empty, "event types must be unmanaged structs");
        }

        [Test]
        public void Bus_PublishFromAnotherThread_FailsLoudly()
        {
            Exception caught = null;
            var t = new Thread(() => { try { _bus.Publish(new PingEvent(1)); } catch (Exception ex) { caught = ex; } });
            t.Start();
            t.Join();
            Assert.That(caught, Is.InstanceOf<InvalidOperationException>());
            Assert.That(_errors.CountOf(CoreErrorKind.WrongThread), Is.EqualTo(1));
        }
    }
}
