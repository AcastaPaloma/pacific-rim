// T-122: the meta composition (Economy, Salvage, Roster, Refit) over the real data files. Engine-free: runs in dotnet (DotnetTests/Composition.Adapters.Tests) and in Unity EditMode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Composition;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Refit;
using Game.Meta.Roster;
using Game.Meta.Salvage;
using Game.World.Destruction;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.Composition.Tests
{
    public class MetaCompositionTests
    {
        static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0);

        sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        sealed class Record : IBodyDamageRecord
        {
            public readonly List<PartDamage> Parts = new List<PartDamage>();
            public Record(PlayerSlot slot, params (string id, double integrity)[] parts)
            { Slot = slot; foreach (var p in parts) Parts.Add(new PartDamage { PartId = p.id, Integrity = p.integrity, MaxIntegrity = 100 }); }
            public PlayerSlot Slot { get; }
            public void CopyParts(List<PartDamage> into) { into.Clear(); into.AddRange(Parts); }
            public void ApplyParts(IReadOnlyList<PartDamage> parts) { Parts.Clear(); foreach (var p in parts) Parts.Add(p); }
        }

        static string ConfigRoot()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Meta.Refit.json"))) d = d.Parent;
                if (d != null) return Path.Combine(d.FullName, "Assets", "Game", "Config");
            }
            Assert.Fail("repo root not found");
            return null;
        }

        static FeatureFlagSnapshot Flags(bool eco, bool salvage, bool roster, bool refit) => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        {
            new KeyValuePair<string, bool>("meta.economy", eco), new KeyValuePair<string, bool>("meta.salvage", salvage),
            new KeyValuePair<string, bool>("meta.permadeath", roster), new KeyValuePair<string, bool>("meta.refit", refit),
        });

        static FeatureFlagSnapshot AllOn() => Flags(true, true, true, true);

        static IEnumerable<BuildingAssetInfo> Buildings()
        {
            yield return new BuildingAssetInfo("b1", 400, 5);
            yield return BuildingAssetInfo.From(BuildingId.From(BuildingSource.OsmWay, 77), new BuildingAttributes { HeightM = 42.2, Levels = 0, FootprintM2 = 800.4 });
        }

        sealed class World
        {
            public ServiceRegistry Reg = new ServiceRegistry();
            public EventBus Bus; public SaveSystem Saves = new SaveSystem("t122"); public Errors Err = new Errors();
            public HangarService Hangar = new HangarService(HangarSites.CrissyField);
            public MetaHandle Meta;
            public World(FeatureFlagSnapshot flags, bool withHangar = true, long funds = 1_000_000_000_000L)
            {
                Bus = new EventBus(Err, 1000);
                Reg.Register<IEventBus>(Bus);
                Meta = MetaInstaller.Install(Reg, flags, Err, MetaInputs.FromDirectory(ConfigRoot(), funds, Buildings), Saves, withHangar ? Hangar : null);
            }
        }

        [Test]
        public void AllFlagsOff_ReadsNothingRegistersNothing()
        {
            var reg = new ServiceRegistry(); var saves = new SaveSystem("t"); int reads = 0;
            var inputs = new MetaInputs { ReadConfig = p => { reads++; return null; } };
            var meta = MetaInstaller.Install(reg, Flags(false, false, false, false), new Errors(), inputs, saves, null);
            Assert.That(meta, Is.Null); Assert.That(reads, Is.EqualTo(0));
            Assert.That(reg.IsRegistered<IEconomyService>(), Is.False); Assert.That(reg.IsRegistered<ISalvageService>(), Is.False);
            Assert.That(reg.IsRegistered<IRosterService>(), Is.False); Assert.That(reg.IsRegistered<IRefitService>(), Is.False);
            Assert.That(reg.IsRegistered<IAssetValueProvider>(), Is.False);
            var doc = saves.Capture();
            foreach (var s in new[] { "economy", "salvage", "roster", "refit" }) Assert.That(doc.TryGetSection(s, out _), Is.False, s);
        }

        [Test]
        public void EconomyOnly_LoadsPrices_RegistersEconomyAndAssets()
        {
            var w = new World(Flags(true, false, false, false));
            Assert.That(w.Meta.Economy, Is.Not.Null); Assert.That(w.Meta.Salvage, Is.Null); Assert.That(w.Meta.Roster, Is.Null); Assert.That(w.Meta.Refit, Is.Null);
            Assert.That(w.Meta.Bridge, Is.Null);
            Assert.That(w.Meta.Economy.Funds, Is.EqualTo(1_000_000_000_000L));
            Assert.That(w.Reg.IsRegistered<IEconomyService>(), Is.True);
            Assert.That(w.Saves.Capture().TryGetSection("economy", out _), Is.True);
            Assert.That(w.Err.List, Is.Empty);
        }

        [Test]
        public void AllOn_RegistersFourSaveSections()
        {
            var w = new World(AllOn());
            var doc = w.Saves.Capture();
            foreach (var s in new[] { "economy", "salvage", "roster", "refit" }) Assert.That(doc.TryGetSection(s, out _), Is.True, s);
            Assert.That(w.Reg.IsRegistered<ISalvageService>(), Is.True); Assert.That(w.Reg.IsRegistered<IRosterService>(), Is.True);
            Assert.That(w.Reg.IsRegistered<IRefitService>(), Is.True); Assert.That(w.Reg.IsRegistered<MetaFightEndBridge>(), Is.True);
            Assert.That(w.Err.List, Is.Empty);
        }

        [Test]
        public void DependentFlagsWithoutEconomy_AreReportedAndSkipped()
        {
            var reg = new ServiceRegistry(); var err = new Errors();
            var meta = MetaInstaller.Install(reg, Flags(false, true, true, true), err, MetaInputs.FromDirectory(ConfigRoot()), new SaveSystem("t"), null);
            Assert.That(meta, Is.Not.Null); Assert.That(meta.Economy, Is.Null); Assert.That(meta.Roster, Is.Null);
            Assert.That(err.List.Any(e => e.Kind == CoreErrorKind.ConfigMissing && e.Message.Contains("meta.economy")), Is.True);
            Assert.That(reg.IsRegistered<ISalvageService>(), Is.False);
        }

        [Test]
        public void MissingOrMalformedConfig_IsReported_NoThrow()
        {
            var err = new Errors();
            var m1 = MetaInstaller.Install(new ServiceRegistry(), AllOn(), err, new MetaInputs { ReadConfig = p => throw new FileNotFoundException(p) }, null, null);
            Assert.That(m1.Economy, Is.Null); Assert.That(err.List.Any(e => e.Kind == CoreErrorKind.ConfigMissing), Is.True);
            var err2 = new Errors();
            var m2 = MetaInstaller.Install(new ServiceRegistry(), AllOn(), err2, new MetaInputs { ReadConfig = p => "{ not json" }, null, null);
            Assert.That(m2.Economy, Is.Null); Assert.That(err2.List.Any(e => e.Kind == CoreErrorKind.ConfigMalformed), Is.True);
            var err3 = new Errors();
            var m3 = MetaInstaller.Install(new ServiceRegistry(), AllOn(), err3, null, null, null);
            Assert.That(m3.Economy, Is.Null); Assert.That(err3.List.Count, Is.EqualTo(1));
        }

        [Test]
        public void AssetValueProvider_PoolAndValues_FromPriceTable()
        {
            var w = new World(Flags(true, false, false, false));
            var a = w.Meta.Assets; var t = w.Meta.Prices;
            Assert.That(a.TryGetValue("golden_gate_bridge", out long gg), Is.True); Assert.That(gg, Is.EqualTo(1_000_000_000L));
            Assert.That(a.TryGetValue("b1", out long b1), Is.True); Assert.That(b1, Is.EqualTo(400L * 5 * t.BuildingUsdPerM2));
            var b2 = BuildingAssetInfo.From(BuildingId.From(BuildingSource.OsmWay, 77), new BuildingAttributes { HeightM = 42.2, Levels = 0, FootprintM2 = 800.4 });
            Assert.That(b2.Floors, Is.EqualTo(10)); Assert.That(b2.FootprintM2, Is.EqualTo(800L));
            Assert.That(a.TryGetValue("nope", out _), Is.False);
            var pool = new List<ContractAsset>(); a.CopyPool(pool);
            Assert.That(pool.Count, Is.EqualTo(t.FixedAssets.Count + 2));
            var offers = w.Meta.Economy.OfferContracts(9, 0, pool, 3);
            Assert.That(offers.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Pricing_FromEconomyTable_MatchesChassisPrice()
        {
            var w = new World(AllOn());
            var p = MetaInstaller.PricingFrom(w.Meta.Prices);
            foreach (var id in new[] { "balanced", "heavy_brawler", "basic_balanced" })
            {
                var asg = w.Meta.Refit.DefaultBuild(id);
                Assert.That(w.Meta.Refit.PriceOf(id, asg), Is.EqualTo(w.Meta.Prices.ChassisPriceUsd(id)), id);
            }
            Assert.That(p.SellBackUsd(1000), Is.EqualTo(w.Meta.Prices.SellBackUsd(1000)));
        }

        [Test]
        public void CreateMech_RegistersWithRefit_AndStoresSpecHash()
        {
            var w = new World(AllOn());
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            Assert.That(w.Meta.Refit.IsRegistered(m.MechId), Is.True);
            var spec = w.Meta.Refit.Validate("balanced", w.Meta.Refit.DefaultBuild("balanced")).Spec;
            Assert.That(w.Meta.Roster.Get(m.MechId).SpecHash, Is.EqualTo(spec.Hash));
            Assert.That(w.Meta.Roster.Get(m.MechId).SpecHash, Is.Not.EqualTo(0));
        }

        [Test]
        public void RefitGate_NeedsHangarPhase_AndFlyableMechOfSlot()
        {
            var w = new World(AllOn());
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            var asg = Swap(w, m.MechId);
            Assert.That(w.Meta.Refit.Apply(m.MechId, asg, 1).Status, Is.EqualTo(RefitStatus.NotAllowed), "fight phase");
            w.Hangar.EnterHangar(new Record(S0, ("body", 100)));
            var gate = new HangarRefitGate(w.Hangar, w.Meta.Roster);
            Assert.That(gate.CanRefit(S0, m.MechId), Is.True);
            Assert.That(gate.CanRefit(PlayerSlot.FromIndex(1), m.MechId), Is.False, "other slot");
            Assert.That(gate.CanRefit(S0, EntityId.FromValue(99999)), Is.False, "unknown mech");
            Assert.That(new HangarRefitGate(null).CanRefit(S0, m.MechId), Is.False, "no hangar");
        }

        [Test]
        public void ApplyRefit_UpdatesRosterSpecHash_ViaBridgeAndViaEvent()
        {
            var w = new World(AllOn());
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            w.Hangar.EnterHangar(new Record(S0, ("body", 100)));
            long h0 = w.Meta.Roster.Get(m.MechId).SpecHash;
            var asg = Swap(w, m.MechId);
            var res = w.Meta.Bridge.ApplyRefit(m.MechId, asg, 5);
            Assert.That(res.Status, Is.EqualTo(RefitStatus.Applied), res.Report?.Detail);
            Assert.That(w.Meta.Roster.Get(m.MechId).SpecHash, Is.EqualTo(res.Spec.Hash)); Assert.That(res.Spec.Hash, Is.Not.EqualTo(h0));
            // direct service call: the RefitApplied event carries the hash to the roster
            var back = w.Meta.Refit.DefaultBuild("balanced").ToDictionary(k => k.Key, k => k.Value);
            var r2 = w.Meta.Refit.Apply(m.MechId, back, 6);
            Assert.That(r2.Status, Is.EqualTo(RefitStatus.Applied));
            Assert.That(w.Meta.Roster.Get(m.MechId).SpecHash, Is.EqualTo(h0));
        }

        [Test]
        public void FightEnd_Survivor_EntersHangar_NoSalvage()
        {
            var w = new World(AllOn());
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            var r = w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 80), ("cockpit_torso", 90), ("reactor", 100)), 10);
            Assert.That(r.Roster.Lost, Is.False); Assert.That(r.EnteredHangar, Is.True); Assert.That(r.Salvage, Is.Null);
            Assert.That(w.Hangar.GetPhase(S0), Is.EqualTo(HangarPhase.Hangar));
            Assert.That(w.Meta.Roster.Get(m.MechId).Cycles, Is.EqualTo(1));
        }

        [Test]
        public void FightEnd_Loss_ClaimsSalvage_CappedAt12Percent_AndRegistersFloorChassis()
        {
            var w = new World(AllOn(), funds: 0);
            var m = w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            long price = w.Meta.Prices.ChassisPriceUsd("balanced");
            var r = w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 0), ("cockpit_torso", 0), ("reactor", 0)), 20);
            Assert.That(r.Roster.Lost, Is.True); Assert.That(r.EnteredHangar, Is.False);
            Assert.That(r.Salvage, Is.Not.Null); Assert.That(r.Salvage.WreckId, Is.EqualTo(r.Roster.WreckId));
            Assert.That(r.Salvage.Result.CreditUsd, Is.LessThanOrEqualTo(price * 12 / 100));
            Assert.That(w.Meta.Salvage.IsClaimed(r.Roster.WreckId), Is.True);
            Assert.That(w.Meta.Salvage.TotalCreditUsd, Is.EqualTo(r.Salvage.CreditUsd));
            Assert.That(r.Roster.FloorGranted, Is.True);
            Assert.That(r.FloorSpec, Is.Not.Null);
            var floor = w.Meta.Roster.Get(r.Roster.FloorMechId);
            Assert.That(floor.IsFloor, Is.True); Assert.That(floor.SpecHash, Is.EqualTo(r.FloorSpec.Hash));
            Assert.That(w.Meta.Refit.IsRegistered(floor.MechId), Is.True);
            Assert.That(w.Meta.Refit.IsRegistered(m.MechId), Is.True, "the wreck keeps its refit record (no unregister API)");
            Assert.That(w.Meta.Roster.Get(m.MechId).State, Is.EqualTo(MechState.Lost));
        }

        [Test]
        public void FightEnd_Loss_WithoutRefit_UsesChassisComposition()
        {
            var w = new World(Flags(true, true, true, false), funds: 0);
            w.Meta.Bridge.CreateMech(S0, "heavy_brawler", 0);
            var r = w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 0)), 3);
            Assert.That(r.Salvage, Is.Not.Null);
            double tonnes = w.Meta.Prices.ChassisMassTonnes("heavy_brawler");
            Assert.That(r.Salvage.Result.AvailableKg, Is.EqualTo(tonnes * 1000.0).Within(1.0));
            Assert.That(r.FloorSpec, Is.Null);
        }

        [Test]
        public void FightEnd_NoSalvage_NoHangar_StillDecidesTheLoss()
        {
            var w = new World(Flags(true, false, true, false), withHangar: false);
            w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            var r = w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 0)), 3);
            Assert.That(r.Roster.Lost, Is.True); Assert.That(r.Salvage, Is.Null);
            var s = w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 50)), 4);
            Assert.That(s.EnteredHangar, Is.False);
        }

        [Test]
        public void SaveRoundTrip_AfterLossAndRefit_RestoresAllFourSections()
        {
            var w = new World(AllOn());
            w.Meta.Bridge.CreateMech(S0, "balanced", 0);
            w.Meta.Bridge.OnFightEnd(new Record(S0, ("body", 0)), 20);
            var floorMech = w.Meta.Roster.ActiveMech(S0);
            w.Hangar.EnterHangar(new Record(S0, ("body", 100)));
            var doc = w.Saves.Capture();
            var text = SaveCodec.Serialize(doc);
            var loaded = SaveCodec.Deserialize(text, "mem");
            Assert.That(loaded.Ok, Is.True, loaded.Message);

            var w2 = new World(AllOn(), funds: 5);
            var rr = w2.Saves.Restore(loaded.Document);
            Assert.That(rr.Ok, Is.True, rr.Message);
            Assert.That(SaveCodec.Serialize(w2.Saves.Capture()), Is.EqualTo(text));
            Assert.That(w2.Meta.Roster.ActiveMech(S0), Is.EqualTo(floorMech));
            Assert.That(w2.Meta.Economy.Funds, Is.EqualTo(w.Meta.Economy.Funds));
            Assert.That(w2.Meta.Refit.IsRegistered(floorMech), Is.True);
        }

        /// <summary>A valid single-slot swap (any alternative part of the same slot type that the chassis accepts and the gate-free quote accepts).</summary>
        static Dictionary<string, string> Swap(World w, EntityId mech)
        {
            var build = w.Meta.Refit.Get(mech);
            var chassis = w.Meta.Refit.Chassis.Get(build.Chassis);
            foreach (var slot in chassis.Slots)
                foreach (var p in w.Meta.Refit.Parts.All)
                {
                    if (p.SlotType != slot.Type || p.IsRare || p.Id == build.Parts[slot.Name]) continue;
                    var asg = build.Parts.ToDictionary(k => k.Key, k => k.Value);
                    asg[slot.Name] = p.Id;
                    var rep = w.Meta.Refit.Validate(build.Chassis, asg);
                    if (rep.Ok && w.Meta.Refit.PriceOf(build.Chassis, asg) >= 0) return asg;
                }
            Assert.Fail("no valid swap found"); return null;
        }
    }
}
