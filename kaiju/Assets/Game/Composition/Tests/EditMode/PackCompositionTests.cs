// T-113/T-114 follow-ups: Units.Kaiju -> SpeciesRole adapter, ai.pack registration, emergence installer. Engine-free: runs in dotnet (DotnetTests/Composition.Adapters.Tests) and Unity EditMode.
using System;
using System.Collections.Generic;
using System.IO;
using Game.AI;
using Game.AI.Pack;
using Game.Composition;
using Game.Core;
using Game.Units.Kaiju;
using Game.Units.Kaiju.Contracts;
using Game.World.Emergence;
using Game.World.Seismic;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class PackCompositionTests
    {
        sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        sealed class Registry : IServiceRegistry
        {
            readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();
            public void Register<T>(T implementation) where T : class { _map.Add(typeof(T), implementation); }
            public T Resolve<T>() where T : class { return (T)_map[typeof(T)]; }
            public bool TryResolve<T>(out T implementation) where T : class { bool ok = _map.TryGetValue(typeof(T), out var o); implementation = ok ? (T)o : null; return ok; }
            public bool IsRegistered<T>() where T : class { return _map.ContainsKey(typeof(T)); }
            public int Count => _map.Count;
        }

        static IFeatureFlags Flags(params string[] on)
        {
            var l = new List<KeyValuePair<string, bool>>();
            foreach (var f in new[] { PackFlags.Pack, BrainFlags.Scripted, EmergenceFlags.Enabled, SeismicFlags.Enabled }) l.Add(new KeyValuePair<string, bool>(f, Array.IndexOf(on, f) >= 0));
            return new FeatureFlagSnapshot(l);
        }

        static string SpeciesDir()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species"))) d = d.Parent;
                if (d != null) return Path.Combine(d.FullName, "Assets", "Game", "Units", "Kaiju", "species");
            }
            Assert.Fail("repo root not found");
            return null;
        }

        static KaijuSpeciesCatalog Catalog()
        {
            var dir = SpeciesDir();
            var specs = new List<KaijuSpeciesSpec>();
            foreach (var n in new[] { "strider", "breacher", "bastion" }) specs.Add(KaijuSpeciesLoader.Parse(File.ReadAllText(Path.Combine(dir, n + ".json"))));
            return new KaijuSpeciesCatalog(specs, KaijuSpeciesLoader.ParseTissue(File.ReadAllText(Path.Combine(dir, "kaiju_tissue_v1.json"))));
        }

        [Test]
        public void SpeciesRole_FromSpecs_MatchesPackNumberingAndStaysNearTheR103Defaults()
        {
            var table = KaijuSpeciesRoles.Table(Catalog());
            var def = new[] { default(SpeciesRole), SpeciesRole.Strider, SpeciesRole.Breacher, SpeciesRole.Bastion };
            for (int id = 1; id <= 3; id++)
            {
                var r = table[id];
                Assert.AreEqual(id, r.SpeciesId);
                Assert.AreEqual(def[id].HeightM, r.HeightM, 1e-3);
                Assert.AreEqual(def[id].CruiseMps, r.CruiseMps, 0.25f * def[id].CruiseMps, "cruise within 25 % (Q-NEW-a)");
                Assert.AreEqual(def[id].ChargeMps, r.ChargeMps, 0.25f * def[id].ChargeMps, "charge within 25 %");
                Assert.AreEqual(def[id].HpRel, r.HpRel, 1e-6);
                Assert.AreEqual(def[id].CrossesDeepWater, r.CrossesDeepWater);
                Assert.AreEqual(7f * r.MeleeReachM, r.StandoffM, 1e-3);
                Assert.Greater(r.MeleeReachM, 0f);
                foreach (var role in new[] { PackRole.Anchor, PackRole.Flanker, PackRole.Bait, PackRole.Raider })
                    Assert.AreEqual(def[id].Fit(role), r.Fit(role), 0.15f, "role fit of species " + id + " " + role);
            }
            // reach is the longest attack of the spec
            Assert.AreEqual(54f, table[1].MeleeReachM, 1e-3);
            Assert.AreEqual(60f, table[3].MeleeReachM, 1e-3);
            Assert.IsFalse(table[3].CrossesDeepWater);
        }

        [Test]
        public void SpeciesRole_NoCatalog_FallsBackToDefaults()
        {
            var t = KaijuSpeciesRoles.Table(null);
            Assert.AreEqual(SpeciesRole.Strider.StandoffM, t[1].StandoffM);
            Assert.AreEqual(SpeciesRole.Bastion.SpeciesId, t[3].SpeciesId);
        }

        [Test]
        public void Pack_FlagOff_RegistersNothing()
        {
            var reg = new Registry();
            ScriptedBrainModule.Install(reg, Flags(BrainFlags.Scripted));
            int before = reg.Count;
            Assert.IsNull(PackInstaller.Install(reg, Flags(BrainFlags.Scripted), new Errors()));
            Assert.AreEqual(before, reg.Count);
            Assert.IsFalse(reg.IsRegistered<PackBrainFactory>());
        }

        [Test]
        public void Pack_FlagOnWithoutBrain_ReportsAndRegistersNothing()
        {
            var reg = new Registry(); var err = new Errors();
            Assert.IsNull(PackInstaller.Install(reg, Flags(PackFlags.Pack), err));
            Assert.AreEqual(1, err.List.Count);
            Assert.AreEqual(0, reg.Count);
        }

        [Test]
        public void Pack_FlagOn_RegistersFactoryDirectoryAndKeepsTheScriptedSlot()
        {
            var reg = new Registry(); var err = new Errors();
            var flags = Flags(PackFlags.Pack, BrainFlags.Scripted);
            var scripted = ScriptedBrainModule.Install(reg, flags);
            reg.Register<IKaijuSpeciesCatalog>(Catalog());
            var h = PackInstaller.Install(reg, flags, err);
            Assert.IsNotNull(h);
            Assert.AreEqual(0, err.List.Count);
            Assert.AreSame(scripted, reg.Resolve<IKaijuBrainFactory>());
            Assert.AreSame(h.Factory, reg.Resolve<PackBrainFactory>());
            Assert.AreSame(h.Directory, reg.Resolve<PackDirectory>());
            Assert.AreEqual(PackFlags.BrainId, h.Factory.BrainId);
            Assert.IsNotNull(h.Factory.Create());
            Assert.AreEqual(54f, h.Species[1].MeleeReachM, 1e-3, "species roles come from the registered catalog");
        }

        [Test]
        public void PackDirectory_RoutesToTheMembersCoordinator()
        {
            var dir = new PackDirectory();
            var a = EntityId.FromValue(1);
            Assert.IsFalse(dir.TryGetDirective(a, out _));
            var coord = new PackCoordinator();
            dir.Assign(a, coord);
            Assert.AreSame(coord, dir.CoordinatorOf(a));
            Assert.IsTrue(dir.Remove(a));
            Assert.AreEqual(0, dir.MemberCount);
        }

        [Test]
        public void Emergence_FlagOff_Null_FlagOnWithoutSeismic_Reported()
        {
            var reg = new Registry(); var err = new Errors();
            Assert.IsNull(EmergenceInstaller.Install(reg, Flags(), err, null));
            Assert.AreEqual(0, err.List.Count);
            Assert.IsNull(EmergenceInstaller.Install(reg, Flags(EmergenceFlags.Enabled), err, null));
            Assert.AreEqual(1, err.List.Count);
            Assert.AreEqual(0, reg.Count);
        }

        [Test]
        public void Emergence_FlagOnWithSeismic_PlansOnTheRupture()
        {
            var reg = new Registry(); var err = new Errors();
            var bus = new EventBus(err, 10000);
            reg.Register<IEventBus>(bus); reg.Register(new EntityIdAllocator());
            var pts = new List<double>(); for (double x = 0; x <= 60000; x += 500) { pts.Add(x); pts.Add(0); }
            var faults = FaultTraceSet.Build(new List<FaultPiece> { new FaultPiece { FaultName = "Test fault zone", Section = "A", SlipSense = "right lateral", Xz = pts.ToArray() } });
            var quake = new QuakeDirector(faults, new DistrictRefs { GGX = 20000, GGZ = 5000, SoMaX = 40000, SoMaZ = 9000, TargetGG = true }, bus);
            var emerge = EmergenceInstaller.Install(reg, Flags(EmergenceFlags.Enabled, SeismicFlags.Enabled), err, quake, null, new EmergenceOptions { SiteCount = 2 });
            Assert.IsNotNull(emerge);
            var sys = faults.Systems[0];
            quake.Run(new QuakeScenario { EventId = 1, Mw = 6.8f, FaultId = sys.FaultId, HypoX = 30000f, HypoZ = 500f, HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = 3 });
            Assert.IsNotNull(emerge.LastPlan);
            Assert.AreEqual(2, emerge.LastPlan.Sites.Length);
            Assert.AreEqual(0, err.List.Count);
        }
    }
}
