using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Core.Unity.Tests
{
    // Unity-only tests (listed in evidence/T-001/test-name-diff.txt): they need the engine, so dotnet cannot run them.
    public class AdapterTests
    {
        private struct ProbeEvent : IEvent { public int V; }
        private sealed class Probe : IEventHandler<ProbeEvent> { public int Calls; public void Handle(in ProbeEvent e) => Calls++; }
        private interface IProbeService { }
        private sealed class ProbeService : IProbeService { }

        [Test]
        public void Adapter_ConfigAsset_PointsAtCommittedFiles()
        {
            var config = CoreConfigAsset.Load();
            Assert.That(config, Is.Not.Null, "Resources/" + CoreConfigAsset.ResourcesName + " missing");
            Assert.That(config.CoreSettingsText, Is.EqualTo(File.ReadAllText(CoreConfigAsset.CoreSettingsPath)));
            var sources = config.FeatureFlagSources();
            Assert.That(sources.Count, Is.EqualTo(Directory.GetFiles(CoreConfigAsset.FeatureFlagsFolder, "*.json").Length));
            foreach (var kv in sources) Assert.That(kv.Value, Is.EqualTo(File.ReadAllText(kv.Key)), kv.Key);
        }

        [Test]
        public void Adapter_CreateRoot_FromCommittedConfig_AllFlagsOff()
        {
            var root = CoreRoot.Create(CoreConfigAsset.Load(), Path.GetTempPath(), "test");
            try
            {
                Assert.That(root.Errors.Count, Is.EqualTo(0));
                Assert.That(root.Services.Flags.Count, Is.GreaterThan(0));
                for (int i = 0; i < root.Services.Flags.Count; i++) Assert.That(root.Services.Flags.ValueAt(i), Is.False, root.Services.Flags.NameAt(i));
                Assert.That(root.Registry.Resolve<ISaveStore>(), Is.SameAs(root.Saves));
                Assert.That(root.Registry.Resolve<IEventBus>(), Is.SameAs(root.Services.Bus));
            }
            finally { root.Dispose(); }
        }

        [Test]
        public void Adapter_ReconcileFixedStep_ReportsMismatch()
        {
            var errors = new CollectingErrorSink();
            Assert.That(CoreRoot.ReconcileFixedStep(0.02f, 0.02, errors), Is.EqualTo(0.02f));
            Assert.That(errors.Count, Is.EqualTo(0));
            Assert.That(CoreRoot.ReconcileFixedStep(1f / 60f, 0.02, errors), Is.EqualTo(0.02f));
            Assert.That(errors.CountOf(CoreErrorKind.FixedStepMismatch), Is.EqualTo(1));
            StringAssert.Contains("CoreSettings.json", errors.Errors[0].Message);
        }

        [Test]
        public void Adapter_LogSink_LogsOncePerSubscriberAndEventType_CountsAll()
        {
            var sink = new UnityLogErrorSink();
            var bus = new EventBus(sink, 100);
            var thrower = new ThrowingProbe();
            bus.Subscribe(thrower);
            LogAssert.Expect(LogType.Error, new Regex("HandlerException.*ThrowingProbe"));
            for (int i = 0; i < 5; i++) bus.Publish(new ProbeEvent { V = i });
            LogAssert.NoUnexpectedReceived();
            Assert.That(sink.Count, Is.EqualTo(5));
            Assert.That(sink.CountOf(CoreErrorKind.HandlerException), Is.EqualTo(5));
        }

        private sealed class ThrowingProbe : IEventHandler<ProbeEvent> { public void Handle(in ProbeEvent e) => throw new System.InvalidOperationException("probe"); }

        // C16: two Play Mode entries with domain reload disabled each boot a fresh Core; nothing leaks between them.
        [UnityTest]
        public IEnumerator Adapter_EnterPlayModeTwice_NoDomainReload_FreshStateEachTime()
        {
            bool savedEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            var savedOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            try
            {
                Assert.That(CoreRuntime.IsBooted, Is.False, "no Core outside Play Mode");
                yield return new EnterPlayMode(false);
                yield return new WaitForFixedUpdate();
                Assert.That(CoreRuntime.IsBooted, Is.True);
                var first = CoreRuntime.Root;
                Assert.That(FixedStepCheck.Matches(Time.fixedDeltaTime, first.Services.Settings.FixedStepSeconds), Is.True);
                first.Services.Bus.Subscribe(new Probe());
                first.Registry.Register<IProbeService>(new ProbeService());
                yield return new WaitForFixedUpdate();
                Assert.That(first.Services.Clock.Step, Is.GreaterThan(0), "clock driven by FixedUpdate");
                Assert.That(first.Errors.Count, Is.EqualTo(0));
                yield return new ExitPlayMode();
                Assert.That(CoreRuntime.IsBooted, Is.False, "shut down on exit");

                yield return new EnterPlayMode(false);
                yield return null;
                Assert.That(CoreRuntime.IsBooted, Is.True);
                var second = CoreRuntime.Root;
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(second.Services.Bus.SubscriberCount<ProbeEvent>(), Is.EqualTo(0));
                Assert.That(second.Registry.IsRegistered<IProbeService>(), Is.False, "no duplicate-registration leak");
                Assert.That(second.Errors.Count, Is.EqualTo(0));
                Assert.That(second.Services.Clock.Step, Is.LessThan(50), "fresh clock");
                Debug.Log("[Core] play mode re-entry: first root " + first.GetHashCode() + ", second root " + second.GetHashCode() + ", domain reload disabled");
                yield return new ExitPlayMode();
                Assert.That(CoreRuntime.IsBooted, Is.False);
            }
            finally
            {
                EditorSettings.enterPlayModeOptionsEnabled = savedEnabled;
                EditorSettings.enterPlayModeOptions = savedOptions;
            }
        }
    }
}
