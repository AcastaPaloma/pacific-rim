using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public class ClockTests
    {
        public static string CommittedSettingsPath => ProjectPaths.Of("Assets", "Game", "Config", "CoreSettings.json");

        // C26: step from config; 64-bit counter; no drift.
        [Test]
        public void Clock_StepComesFromCommittedConfig()
        {
            var settings = CoreSettings.Parse(File.ReadAllText(CommittedSettingsPath), CommittedSettingsPath);
            // Value is Assumed (0.02 s); research/R-005 R2 recommends 1/60 s. Changing it is a config edit only.
            Assert.That(settings.FixedStepSeconds, Is.GreaterThan(0).And.LessThanOrEqualTo(0.1));
            Evidence.Line("bench.txt", "committed fixedStepSeconds=" + settings.FixedStepSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " maxEventsPerCascade=" + settings.MaxEventsPerCascade);
            Assert.That(settings.MaxEventsPerCascade, Is.GreaterThan(0));
            var clock = new SimulationClock(settings.FixedStepSeconds);
            Assert.That(clock.StepSeconds, Is.EqualTo(settings.FixedStepSeconds));
            Assert.That(typeof(ISimulationClock).GetProperty("Step").PropertyType, Is.EqualTo(typeof(long)));
            Assert.Throws<FormatException>(() => CoreSettings.Parse("{\"schema\":1,\"fixedStepSeconds\":\"0,02\",\"maxEventsPerCascade\":10}", "bad"));
            Assert.Throws<FormatException>(() => CoreSettings.Parse("{\"schema\":1,\"fixedStepSeconds\":0.02", "truncated"));
        }

        [Test]
        public void Clock_NoDriftAfter10MSteps()
        {
            var clock = new SimulationClock(0.02);
            float naive = 0f; double naiveDouble = 0;
            for (long i = 0; i < 10000000L; i++) { clock.Advance(); naive += 0.02f; naiveDouble += 0.02; }
            Assert.That(clock.Step, Is.EqualTo(10000000L));
            Assert.That(clock.TimeSeconds, Is.EqualTo(10000000L * 0.02));
            Assert.That(Math.Abs(clock.TimeSeconds - 200000.0), Is.LessThan(1e-9));
            Assert.That(clock.TimeAt(clock.Step), Is.EqualTo(clock.TimeSeconds));
            Evidence.Line("bench.txt", "clock 1e7 steps: derived=" + clock.TimeSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " naiveFloatSum=" + naive.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " naiveDoubleSum=" + naiveDouble.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var resumed = new SimulationClock(0.02, long.MaxValue / 2);
            Assert.That(resumed.Step, Is.EqualTo(long.MaxValue / 2), "64-bit counter");
        }

        [Test]
        public void Clock_FixedStepCheck_DetectsMismatch()
        {
            Assert.That(FixedStepCheck.Matches(0.02f, 0.02), Is.True);
            Assert.That(FixedStepCheck.Matches(1f / 60f, 1.0 / 60.0), Is.True);
            Assert.That(FixedStepCheck.Matches(0.0166667f, 0.02), Is.False);
            Assert.That(FixedStepCheck.Matches(0.01f, 0.02), Is.False);
        }

        // C27: tests drive the clock by hand; it never moves on its own.
        [Test]
        public void Clock_ManualStepping_DrivesBus()
        {
            var clock = new SimulationClock(0.02);
            var bus = new EventBus(new CollectingErrorSink(), 100);
            long lastSeen = -1;
            bus.Subscribe(new Handler<StepEvent>(e => lastSeen = clock.Step));
            Assert.That(clock.Step, Is.EqualTo(0));
            System.Threading.Thread.Sleep(20);
            Assert.That(clock.Step, Is.EqualTo(0), "no wall clock behind the step");
            for (int i = 0; i < 3; i++) { clock.Advance(); bus.Publish(new StepEvent { Step = clock.Step }); }
            Assert.That(lastSeen, Is.EqualTo(3));
        }

        // C28: a Core-only simulation replayed from the same state gives byte-identical traces.
        public const string GoldenReplayTraceHash = "CE7CBB4259A6A39B";

        private sealed class Agent : IEventHandler<StepEvent>, IEventHandler<PingEvent>
        {
            private readonly IEventBus _bus; private readonly ISimulationClock _clock; private readonly StringBuilder _trace;
            private readonly EntityId _id; private readonly bool _chatty; private SplitMix64 _rng;
            public Agent(IEventBus bus, ISimulationClock clock, StringBuilder trace, EntityId id, bool chatty)
            { _bus = bus; _clock = clock; _trace = trace; _id = id; _chatty = chatty; _rng = new SplitMix64(id.Value); }
            public void Handle(in StepEvent e)
            {
                if (_rng.NextInt(4) == 0) _bus.Publish(new PingEvent((int)_id.Value * 1000 + (int)(_clock.Step % 1000)));
                _trace.Append(_id).Append('@').Append(e.Step).Append(';');
            }
            public void Handle(in PingEvent e)
            {
                _trace.Append(_id).Append("<-").Append(e.Value).Append(';');
                if (_chatty && e.Value % 7 == 0) _bus.Publish(new PongEvent(e.Value));
            }
        }

        public static string RunReplay(int steps)
        {
            var errors = new CollectingErrorSink();
            var flags = FeatureFlagLoader.Parse("{\"schema\":1,\"flags\":{\"sim.chatty\":true}}", "replay", errors);
            using (var core = new CoreServices(new CoreSettings(0.02, 10000), flags, errors))
            {
                var trace = new StringBuilder();
                var pongs = new TraceHandler("pong", trace);
                core.Bus.Subscribe<PongEvent>(pongs);
                var agents = new Agent[6];
                for (int i = 0; i < agents.Length; i++)
                {
                    agents[i] = new Agent(core.Bus, core.Clock, trace, core.EntityIds.Allocate(), core.Flags.IsOn("sim.chatty") && i % 2 == 0);
                    core.Bus.Subscribe<StepEvent>(agents[i]);
                    core.Bus.Subscribe<PingEvent>(agents[i]);
                }
                for (int s = 0; s < steps; s++)
                {
                    core.Clock.Advance();
                    core.Bus.Publish(new StepEvent { Step = core.Clock.Step });
                    if (s == steps / 2) core.Bus.Unsubscribe<PingEvent>(agents[1]);
                }
                Assert.That(errors.Count, Is.EqualTo(0));
                return trace.ToString();
            }
        }

        [Test]
        public void Core_StepReplay_Deterministic()
        {
            string a = RunReplay(1000);
            string b = RunReplay(1000);
            Assert.That(Encoding.UTF8.GetBytes(b), Is.EqualTo(Encoding.UTF8.GetBytes(a)));
            string hash = StableHash.Hex(StableHash.Fnv1a64(a));
            Evidence.Line("trace-hashes.txt", "replay1000 " + hash + " length=" + a.Length);
            Assert.That(hash, Is.EqualTo(GoldenReplayTraceHash));
        }
    }
}
