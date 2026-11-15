using System.Collections.Generic;
using System.IO;
using Game.AI;
using Game.Core;
using Game.Core.Unity;
using Game.World.Destruction;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class CompositionTests
    {
        private sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        private static FeatureFlagSnapshot Flags(params string[] on)
        {
            var l = new List<KeyValuePair<string, bool>>();
            foreach (var n in new[] { "world.geo.enabled", "destruction.enabled", "seismic.enabled", "kaiju.scriptedBrain" })
                l.Add(new KeyValuePair<string, bool>(n, System.Array.IndexOf(on, n) >= 0));
            return new FeatureFlagSnapshot(l);
        }

        private static GameCompositionInputs Inputs() => new GameCompositionInputs
        {
            Faults = () => FaultTraceSet.Build(new[] { new FaultPiece { FaultName = "Test", Section = "A", Linetype = "L", SlipSense = "RL", Xz = new double[] { 0, 0, 10000, 0, 20000, 0 } } }),
            Districts = () => new DistrictRefs { GGX = 0, GGZ = 0, SoMaX = 20000, SoMaZ = 0 },
        };

        [Test]
        public void AllFlagsOff_RegistersNothing()
        {
            var reg = new ServiceRegistry();
            var r = GameComposition.Install(reg, Flags(), new Errors(), Inputs());
            Assert.That(r.Geo, Is.Null); Assert.That(r.Destruction, Is.Null); Assert.That(r.Seismic, Is.Null);
            Assert.That(r.ShakingBridge, Is.Null); Assert.That(r.Brain, Is.Null);
            Assert.That(reg.IsRegistered<IDamageModel>(), Is.False);
            Assert.That(reg.IsRegistered<IShakingField>(), Is.False);
            Assert.That(reg.IsRegistered<IKaijuBrainFactory>(), Is.False);
        }

        [Test]
        public void Install_DestructionOn_RegistersSaveParticipant_FlagOffRegistersNone()
        {
            var saves = new SaveSystem("test");
            var on = GameComposition.Install(new ServiceRegistry(), Flags("destruction.enabled"), new Errors(), Inputs(), saves);
            Assert.That(on.DestructionSave, Is.Not.Null);
            Assert.That(saves.Capture().TryGetSection(DestructionSaveParticipant.Section, out _), Is.True);

            var saves2 = new SaveSystem("test");
            var off = GameComposition.Install(new ServiceRegistry(), Flags(), new Errors(), Inputs(), saves2);
            Assert.That(off.DestructionSave, Is.Null);
            Assert.That(saves2.Capture().TryGetSection(DestructionSaveParticipant.Section, out _), Is.False);
        }

        [Test]
        public void AllFlagsOn_InstallsEveryModule()
        {
            var reg = new ServiceRegistry();
            var bus = new EventBus(new Errors(), 256);
            reg.Register<IEventBus>(bus);
            var flags = Flags("world.geo.enabled", "destruction.enabled", "seismic.enabled", "kaiju.scriptedBrain");
            var r = GameComposition.Install(reg, flags, new Errors(), Inputs());
            Assert.That(r.Geo, Is.Not.Null); Assert.That(r.Destruction, Is.Not.Null); Assert.That(r.Seismic, Is.Not.Null);
            Assert.That(r.ShakingBridge, Is.Not.Null); Assert.That(r.Brain, Is.Not.Null);
            Assert.That(reg.Resolve<IDamageModel>(), Is.SameAs(r.Destruction));
            Assert.That(reg.Resolve<IShakingField>(), Is.SameAs(r.Seismic.Field));
            Assert.That(reg.Resolve<IKaijuBrainFactory>().Create(), Is.InstanceOf<ScriptedKaijuBrain>());
        }

        [Test]
        public void SeismicOn_WithoutFaultInput_IsReportedNotInstalled()
        {
            var reg = new ServiceRegistry();
            var errors = new Errors();
            var r = GameComposition.Install(reg, Flags("seismic.enabled"), errors, new GameCompositionInputs());
            Assert.That(r.Seismic, Is.Null);
            Assert.That(errors.List.Count, Is.EqualTo(1));
            Assert.That(reg.IsRegistered<QuakeDirector>(), Is.False);
        }

        [Test]
        public void SeismicWithoutDestruction_HasNoBridge()
        {
            var reg = new ServiceRegistry();
            var r = GameComposition.Install(reg, Flags("seismic.enabled"), new Errors(), Inputs());
            Assert.That(r.Seismic, Is.Not.Null); Assert.That(r.ShakingBridge, Is.Null);
        }

        [Test]
        public void CommittedConfig_BootedRoot_InstallsNothing()
        {
            var root = CoreRoot.Create(CoreConfigAsset.Load(), Path.GetTempPath(), "test");
            try
            {
                var r = GameComposition.Install(root.Registry, root.Services.Flags, root.Errors, Inputs());
                Assert.That(r.Geo, Is.Null); Assert.That(r.Destruction, Is.Null); Assert.That(r.Seismic, Is.Null); Assert.That(r.Brain, Is.Null);
            }
            finally { root.Dispose(); }
        }
    }
}
