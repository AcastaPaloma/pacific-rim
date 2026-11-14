using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public interface ITestService { int Id { get; } }
    public interface IOtherService { }

    public sealed class TestService : ITestService, IOtherService, IDisposable
    {
        private readonly List<string> _log;
        public TestService(int id, List<string> log = null) { Id = id; _log = log; }
        public int Id { get; }
        public int DisposeCalls;
        public void Dispose() { DisposeCalls++; _log?.Add("dispose" + Id); }
    }

    public sealed class AlternativeService : ITestService { public int Id => 99; }

    public class RegistryTests
    {
        // C15
        [Test]
        public void Registry_ResolveMissing_NamesContract()
        {
            var reg = new ServiceRegistry();
            var ex = Assert.Throws<ServiceNotRegisteredException>(() => reg.Resolve<ITestService>());
            StringAssert.Contains(typeof(ITestService).FullName, ex.Message);
            Assert.That(reg.TryResolve<ITestService>(out var none), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void Registry_DuplicateRegistration_FailsNamingBothImplementations()
        {
            var reg = new ServiceRegistry();
            reg.Register<ITestService>(new TestService(1));
            var ex = Assert.Throws<DuplicateServiceException>(() => reg.Register<ITestService>(new AlternativeService()));
            StringAssert.Contains(typeof(ITestService).FullName, ex.Message);
            StringAssert.Contains(typeof(TestService).FullName, ex.Message);
            StringAssert.Contains(typeof(AlternativeService).FullName, ex.Message);
            Assert.That(reg.Resolve<ITestService>().Id, Is.EqualTo(1), "the first registration is kept");
        }

        [Test]
        public void Registry_RegisterResolveByContract()
        {
            var reg = new ServiceRegistry();
            var svc = new TestService(5);
            reg.Register<ITestService>(svc);
            reg.Register<IOtherService>(svc);
            Assert.That(reg.Resolve<ITestService>(), Is.SameAs(svc));
            Assert.That(reg.Resolve<IOtherService>(), Is.SameAs(svc));
            Assert.That(reg.IsRegistered<ITestService>(), Is.True);
        }

        // C16: two registries and two buses in one process are independent.
        [Test]
        public void Registry_And_Bus_InstancesAreIndependent()
        {
            var r1 = new ServiceRegistry(); var r2 = new ServiceRegistry();
            r1.Register<ITestService>(new TestService(1));
            Assert.That(r2.IsRegistered<ITestService>(), Is.False);
            r2.Register<ITestService>(new TestService(2));
            Assert.That(r1.Resolve<ITestService>().Id, Is.EqualTo(1));

            var b1 = new EventBus(new CollectingErrorSink(), 100); var b2 = new EventBus(new CollectingErrorSink(), 100);
            var h1 = new Handler<PingEvent>(null); var h2 = new Handler<PingEvent>(null);
            b1.Subscribe(h1); b2.Subscribe(h2);
            b1.Publish(new PingEvent(1));
            Assert.That(h1.Calls, Is.EqualTo(1));
            Assert.That(h2.Calls, Is.EqualTo(0));
        }

        // C17
        [Test]
        public void Registry_Teardown_DisposesReverseOrderOnce()
        {
            var log = new List<string>();
            var reg = new ServiceRegistry();
            var a = new TestService(1, log); var b = new TestService(2, log); var c = new TestService(3, log);
            reg.Register<ITestService>(a);
            reg.Register<IOtherService>(b);
            reg.Register(c);
            reg.Register<IDisposable>(a); // same instance under a second contract: still disposed once
            reg.Dispose();
            reg.Dispose();
            Assert.That(log, Is.EqualTo(new[] { "dispose1", "dispose3", "dispose2" }));
            Assert.That(a.DisposeCalls + b.DisposeCalls + c.DisposeCalls, Is.EqualTo(3));
            Assert.Throws<ObjectDisposedException>(() => reg.Register<ITestService>(new AlternativeService()));
        }

        [Test]
        public void CoreServices_RegistersCoreContracts()
        {
            using (var core = new CoreServices(new CoreSettings(0.02, 1000), FeatureFlagSnapshot.AllOff, new CollectingErrorSink()))
            {
                Assert.That(core.Registry.Resolve<IEventBus>(), Is.SameAs(core.Bus));
                Assert.That(core.Registry.Resolve<ISimulationClock>(), Is.SameAs(core.Clock));
                Assert.That(core.Registry.Resolve<IFeatureFlags>(), Is.SameAs(core.Flags));
                Assert.That(core.Registry.Resolve<EntityIdAllocator>(), Is.SameAs(core.EntityIds));
                Assert.That(core.Registry.Resolve<ICoreErrorSink>(), Is.SameAs(core.Errors));
            }
        }
    }
}
