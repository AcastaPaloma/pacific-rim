using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Salvage;

namespace Game.Meta.Salvage.Tests
{
    static class Kit
    {
        public static string Root()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Meta.json"))) d = d.Parent;
            Assert.IsNotNull(d, "repo root not found");
            return d.FullName;
        }
        public static string YieldsText() => File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Salvage", "salvage_yields@v1.json"));
        public static YieldsTable Yields() => YieldsTable.Parse(YieldsText());
        public static PriceTable Prices() => PriceTable.Parse(File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Economy", "economy_prices@v1.json")));
        public static EventBus NewBus() => new EventBus(new CollectingErrorSink(), 1000);

        public sealed class Rng
        {
            ulong _s; public Rng(ulong seed) { _s = seed; }
            public ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public int Below(int n) => (int)(Next() % (ulong)n);
            public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        }

        public sealed class Collect : IEventHandler<SalvageRecovered>
        {
            public readonly List<SalvageRecovered> Seen = new List<SalvageRecovered>();
            public void Handle(in SalvageRecovered e) { Seen.Add(e); }
        }

        static readonly string[] MechClasses = { "structure", "armour", "drive", "reactor", "titanium", "composite", "weapons" };

        /// <summary>Balanced mech wreck: masses and prices from the economy table, integrity and absorbed energy from the rng.</summary>
        public static SalvageInput Mech(long id, SalvageLocation loc, Rng r, PriceTable t, double fall = 392.0)
        {
            var parts = new List<SalvagePart>();
            long build = 0;
            foreach (var c in MechClasses)
            {
                long tonnes = t.Chassis["balanced"].Tonnes.TryGetValue(c, out long x) ? x : 0;
                if (tonnes == 0) continue;
                double mass = tonnes * 1000.0;
                long price = t.QuotePart(c, tonnes * 1000L).PriceUsd;
                build += price;
                double integ = r.Unit();
                double hit = r.Unit() < 0.3 ? r.Unit() * 50_000.0 * mass : 0.0;   // focused hits of up to 50 kJ/kg on some parts
                parts.Add(new SalvagePart("p_" + c, c, mass, hit, integ * 100.0, 100.0, price));
            }
            return new SalvageInput { WreckId = id, SourceKind = SalvageSourceKind.Mech, Location = loc, FallEnergyJPerKg = fall, BuildPriceUsd = build, Parts = parts.ToArray() };
        }

        public static SalvageInput Kaiju(long id, SalvageLocation loc, YieldsTable y, double lengthM, double energyJPerKg = 0.0)
        {
            var parts = new List<SalvagePart>();
            foreach (var c in new[] { "bone", "hide", "tendon" })
            {
                double m = y.KaijuTissueMassKg(lengthM, c);
                parts.Add(new SalvagePart("k_" + c, c, m, energyJPerKg * m, 0, 1, 0));
            }
            return new SalvageInput { WreckId = id, SourceKind = SalvageSourceKind.Kaiju, Location = loc, FallEnergyJPerKg = 0, Parts = parts.ToArray() };
        }

        public static SalvageInput Building(long id, SalvageLocation loc, double debrisKg, double steelShare)
        {
            return new SalvageInput
            {
                WreckId = id, SourceKind = SalvageSourceKind.Building, Location = loc,
                Parts = new[] { new SalvagePart("b_steel", "debris_steel", debrisKg * steelShare, 0, 0, 1, 0), new SalvagePart("b_other", "debris_other", debrisKg * (1 - steelShare), 0, 0, 1, 0) }
                    .Where(p => p.MassKg > 0).ToArray(),
            };
        }

        public static SalvageService New(out EconomyService eco, EventBus bus = null, long funds = 0)
        {
            eco = new EconomyService(Prices(), null, funds);
            return new SalvageService(Yields(), eco, bus);
        }
    }

    public class SalvageTests
    {
        // ---------------------------------------------------------------- data file and flag

        [Test] public void Flag_FileDefaultsOff()
        {
            string t = File.ReadAllText(Path.Combine(Kit.Root(), "Assets", "Game", "Config", "FeatureFlags", "Meta.json"));
            StringAssert.Contains("\"meta.salvage\": false", t);
            var snap = FeatureFlagLoader.Parse(t, "Meta.json", new CollectingErrorSink());
            Assert.IsFalse(snap.IsOn(SalvageFlags.Salvage));
        }

        [Test] public void Install_OnlyWhenFlagOnAndEconomyPresent()
        {
            var y = Kit.Yields(); var eco = new EconomyService(Kit.Prices());
            var reg = new ServiceRegistry();
            Assert.IsNull(SalvageModule.Install(reg, FeatureFlagSnapshot.AllOff, y, eco));
            Assert.IsFalse(reg.IsRegistered<ISalvageService>());
            var on = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(SalvageFlags.Salvage, true) });
            Assert.IsNull(SalvageModule.Install(reg, on, y, null));
            var svc = SalvageModule.Install(reg, on, y, eco);
            Assert.IsNotNull(svc); Assert.AreSame(svc, reg.Resolve<ISalvageService>());
        }

        [Test] public void YieldsFile_EveryRowHasSourceAndIsValidated()
        {
            var y = Kit.Yields();
            Assert.AreEqual(12, y.Classes.Count);
            foreach (var c in y.Classes.Values) Assert.IsNotEmpty(c.Source, c.Id);
            string t = Kit.YieldsText();
            Assert.Throws<FormatException>(() => YieldsTable.Parse(t.Replace("salvage_yields@v1", "salvage_yields@v2")));
            Assert.Throws<FormatException>(() => YieldsTable.Parse(t.Replace("\"eta_bps\": 9000", "\"eta_bps\": 20000")));
            Assert.Throws<FormatException>(() => YieldsTable.Parse(t.Replace("\"eta_bps\": 7000", "\"eta_bps\": 9500")));   // efficiency may not rise with depth
            Assert.Throws<FormatException>(() => YieldsTable.Parse("{"));
        }

        // ---------------------------------------------------------------- fractions match the data file (R-104 5.1, Erratum)

        [Test] public void Fractions_MatchTheDataFileHeadline()
        {
            var y = Kit.Yields();
            double land = y.RecoveredFraction(y.HeadlineFallEnergy, SalvageLocation.Land), bay = y.RecoveredFraction(y.HeadlineFallEnergy, SalvageLocation.ShallowBay),
                   deep = y.RecoveredFraction(y.HeadlineFallEnergy, SalvageLocation.DeepChannel);
            Assert.AreEqual(y.HeadlineLand, land, y.HeadlineTolerance);
            Assert.AreEqual(y.HeadlineShallow, bay, y.HeadlineTolerance);
            Assert.AreEqual(y.HeadlineDeep, deep, y.HeadlineTolerance);
            // the corrected numbers, not the old 0.60 / 0.30
            Assert.AreEqual(0.865, land, 0.001); Assert.AreEqual(0.673, bay, 0.001); Assert.AreEqual(0.337, deep, 0.001);
            Assert.Greater(Math.Abs(bay - 0.60), 0.05); Assert.Greater(Math.Abs(deep - 0.30), 0.03);
            Assert.AreEqual(0.0384, y.FragmentedFraction(392.0), 0.0002);
        }

        [Test] public void Fractions_ThroughAssess_ForAWholeWreck()
        {
            var svc = Kit.New(out _); var t = Kit.Prices(); var y = svc.Yields;
            var locs = new[] { SalvageLocation.Land, SalvageLocation.ShallowBay, SalvageLocation.DeepChannel }; var want = new[] { y.HeadlineLand, y.HeadlineShallow, y.HeadlineDeep };
            for (int i = 0; i < 3; i++)
            {
                var inp = Kit.Mech(1 + i, locs[i], new Kit.Rng(1), t);
                var clean = new SalvageInput { WreckId = inp.WreckId, SourceKind = inp.SourceKind, Location = inp.Location, FallEnergyJPerKg = 392.0, BuildPriceUsd = inp.BuildPriceUsd,
                    Parts = inp.Parts.Select(p => new SalvagePart(p.PartId, p.ClassId, p.MassKg, 0, p.Integrity, p.MaxIntegrity, p.PriceUsd)).ToArray() };
                var r = svc.Assess(clean);
                Assert.AreEqual(want[i], r.RecoveredKg / r.AvailableKg, y.HeadlineTolerance, locs[i].ToString());
                Assert.AreEqual(y.RecoveredFraction(392.0, locs[i]), r.RecoveredKg / r.AvailableKg, 1e-12);
            }
        }

        [Test] public void FocusedHitsFragmentMore_AndDeeperIsWorse()
        {
            var y = Kit.Yields();
            Assert.Greater(y.FragmentedFraction(50_000), y.FragmentedFraction(10_000)); Assert.Greater(y.FragmentedFraction(10_000), 0.6);
            Assert.AreEqual(0.0, y.FragmentedFraction(0)); Assert.AreEqual(0.0, y.FragmentedFraction(-5)); Assert.AreEqual(1.0, y.FragmentedFraction(double.PositiveInfinity));
            Assert.Greater(y.RecoveredFraction(0, SalvageLocation.Land), y.RecoveredFraction(0, SalvageLocation.ShallowBay));
            Assert.Greater(y.RecoveredFraction(0, SalvageLocation.ShallowBay), y.RecoveredFraction(0, SalvageLocation.DeepChannel));
        }

        // ---------------------------------------------------------------- mass conservation

        [Test] public void MassConservation_RecoveredNeverExceedsAvailable_300RandomWrecks()
        {
            var svc = Kit.New(out _); var t = Kit.Prices(); var y = svc.Yields; var r = new Kit.Rng(20261115);
            for (int i = 0; i < 300; i++)
            {
                var loc = (SalvageLocation)r.Below(3); SalvageInput inp;
                switch (i % 3)
                {
                    case 0: inp = Kit.Mech(i + 1, loc, r, t, r.Unit() * 2000); break;
                    case 1: inp = Kit.Kaiju(i + 1, loc, y, 20 + r.Unit() * 160, r.Unit() * 30_000); break;
                    default: inp = Kit.Building(i + 1, loc, 1000 + r.Unit() * 1e8, r.Unit()); break;
                }
                var res = svc.Assess(inp);
                double sa = 0, sr = 0;
                foreach (var p in res.Parts)
                {
                    Assert.LessOrEqual(p.RecoveredKg, p.AvailableKg, inp.SourceKind + " " + p.PartId); Assert.GreaterOrEqual(p.RecoveredKg, 0);
                    Assert.GreaterOrEqual(p.FragmentedFraction, 0); Assert.LessOrEqual(p.FragmentedFraction, 1);
                    sa += p.AvailableKg; sr += p.RecoveredKg;
                }
                Assert.AreEqual(sa, res.AvailableKg); Assert.AreEqual(sr, res.RecoveredKg); Assert.LessOrEqual(res.RecoveredKg, res.AvailableKg);
                Assert.LessOrEqual(res.CreditUsd, res.RawCreditUsd);
                if (inp.SourceKind == SalvageSourceKind.Kaiju) Assert.LessOrEqual(res.RecoveredKg, y.ThroughputBudgetKg + 1e-6);
            }
        }

        [Test] public void Assess_IsPure_AndDeterministic()
        {
            var svc = Kit.New(out var eco, null, 5); var t = Kit.Prices();
            var inp = Kit.Mech(7, SalvageLocation.ShallowBay, new Kit.Rng(3), t);
            var a = svc.Assess(inp); var b = svc.Assess(inp);
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.RecoveredKg), BitConverter.DoubleToInt64Bits(b.RecoveredKg)); Assert.AreEqual(a.CreditUsd, b.CreditUsd);
            Assert.AreEqual(5, eco.Funds); Assert.IsFalse(svc.IsClaimed(7)); Assert.AreEqual(0, svc.InventoryKg("structure"));
        }

        // ---------------------------------------------------------------- credit and the wreck-credit cap

        [Test] public void ReuseVersusScrap_ByIntegrityAndClass()
        {
            var svc = Kit.New(out _);
            SalvagePart P(string id, string cls, double integ) => new SalvagePart(id, cls, 100_000, 0, integ, 1.0, 50_000_000);
            var inp = new SalvageInput { WreckId = 1, SourceKind = SalvageSourceKind.Mech, Location = SalvageLocation.Land, FallEnergyJPerKg = 0, BuildPriceUsd = 10_000_000_000L,
                Parts = new[] { P("a", "structure", 0.7), P("b", "structure", 0.2), P("c", "weapons", 0.2), P("d", "weapons", 0.7), P("e", "titanium", 0.0) } };
            var r = svc.Assess(inp); var by = r.Parts.ToDictionary(p => p.PartId);
            double frac = 0.9;   // no fall: land efficiency only
            Assert.IsTrue(by["a"].Reused); Assert.AreEqual((long)Math.Floor(50_000_000.0 * 0.6 * 0.7 * frac), by["a"].CreditUsd);
            Assert.IsFalse(by["b"].Reused); Assert.AreEqual((long)Math.Floor(100_000 * frac / 1000.0 * 294), by["b"].CreditUsd);   // steel scrap $294/t
            Assert.AreEqual(0, by["c"].CreditUsd);                                                                                // weapons are reuse-only
            Assert.IsTrue(by["d"].Reused); Assert.Greater(by["d"].CreditUsd, 0);
            Assert.AreEqual((long)Math.Floor(100_000 * frac / 1000.0 * 8000), by["e"].CreditUsd);                                // titanium scrap
        }

        [Test] public void CreditCap_TwelvePercentOfBuildPrice_IsEnforcedAndPaid()
        {
            var svc = Kit.New(out var eco); var t = Kit.Prices();
            var inp = Kit.Mech(1, SalvageLocation.Land, new Kit.Rng(1), t);
            // an intact wreck with no fall energy: the raw reuse value would be far above 12%
            var intact = new SalvageInput { WreckId = 1, SourceKind = SalvageSourceKind.Mech, Location = SalvageLocation.Land, FallEnergyJPerKg = 0, BuildPriceUsd = inp.BuildPriceUsd,
                Parts = inp.Parts.Select(p => new SalvagePart(p.PartId, p.ClassId, p.MassKg, 0, 100, 100, p.PriceUsd)).ToArray() };
            long cap = inp.BuildPriceUsd * 1200 / 10000;
            var r = svc.Assess(intact);
            Assert.Greater(r.RawCreditUsd, cap, "uncapped intact wreck exceeds the cap"); Assert.IsTrue(r.CapApplied); Assert.AreEqual(cap, r.CreditUsd);
            var c = svc.Claim(intact, 5);
            Assert.AreEqual(cap, c.CreditUsd); Assert.AreEqual(cap, eco.Funds); Assert.AreEqual(cap, svc.TotalCreditUsd);
            Assert.LessOrEqual(eco.Funds, inp.BuildPriceUsd * 0.12 + 1);   // losing a mech always costs at least 88% of its price
        }

        [Test] public void CreditCap_NeverExceeded_AcrossRandomMechWrecks()
        {
            var svc = Kit.New(out var eco); var t = Kit.Prices(); var r = new Kit.Rng(99);
            long total = 0;
            for (int i = 0; i < 200; i++)
            {
                var inp = Kit.Mech(i + 1, (SalvageLocation)r.Below(3), r, t, r.Unit() * 600);
                long cap = inp.BuildPriceUsd * 1200 / 10000;
                var res = svc.Assess(inp);
                Assert.LessOrEqual(res.CreditUsd, cap); Assert.AreEqual(res.RawCreditUsd > cap, res.CapApplied);
                total += svc.Claim(inp, i).CreditUsd;
            }
            Assert.AreEqual(total, eco.Funds); Assert.AreEqual(total, svc.TotalCreditUsd); Assert.AreEqual(eco.TotalCredited, eco.Funds + eco.TotalDebited);
        }

        [Test] public void Credit_GoesToEconomyAsSalvageCredit_AndCollectsArrears()
        {
            var svc = Kit.New(out var eco); var t = Kit.Prices();
            eco.PayUpkeep(1_000_000, 1);   // no funds: becomes arrears
            Assert.AreEqual(1_000_000, eco.Arrears);
            var inp = Kit.Mech(1, SalvageLocation.Land, new Kit.Rng(4), t);
            var c = svc.Claim(inp, 2);
            var tx = new List<KeyValuePair<long, long>>(); var rs = new List<TransactionReason>(); eco.CopyTransactions(tx, rs);
            Assert.Contains(TransactionReason.SalvageCredit, rs);
            Assert.Greater(c.CreditUsd, 1_000_000);
            Assert.AreEqual(0, eco.Arrears); Assert.AreEqual(c.CreditUsd - 1_000_000, eco.Funds);
        }

        // ---------------------------------------------------------------- claim, event

        [Test] public void Claim_IsIdempotentByWreckId_OneEventOneCredit()
        {
            var bus = Kit.NewBus(); var col = new Kit.Collect(); bus.Subscribe(col);
            var svc = Kit.New(out var eco, bus); var t = Kit.Prices();
            var inp = Kit.Mech(42, SalvageLocation.ShallowBay, new Kit.Rng(8), t);
            var a = svc.Claim(inp, 1); long funds = eco.Funds; double inv = svc.InventoryKg("structure");
            var b = svc.Claim(inp, 2); var c = svc.Claim(inp, 3);
            Assert.IsFalse(a.Replay); Assert.IsTrue(b.Replay); Assert.IsTrue(c.Replay);
            Assert.AreEqual(a.CreditUsd, b.CreditUsd); Assert.AreEqual(a.RecoveredKg, b.RecoveredKg); Assert.IsNull(b.Result);
            Assert.AreEqual(funds, eco.Funds); Assert.AreEqual(inv, svc.InventoryKg("structure")); Assert.AreEqual(1, svc.WreckCount);
            Assert.AreEqual(1, col.Seen.Count);
            var e = col.Seen[0];
            Assert.AreEqual(42L, e.WreckId); Assert.AreEqual((byte)SalvageSourceKind.Mech, e.SourceKind); Assert.AreEqual((byte)SalvageLocation.ShallowBay, e.Location);
            Assert.AreEqual(a.RecoveredKg, e.RecoveredKg); Assert.AreEqual(a.CreditUsd, e.CreditUsd);
        }

        [Test] public void InvalidInputs_AreRejectedAndChangeNothing()
        {
            var svc = Kit.New(out var eco); var t = Kit.Prices(); var good = Kit.Mech(1, SalvageLocation.Land, new Kit.Rng(2), t);
            SalvageInput With(Action<SalvageInput> f) { var i = new SalvageInput { WreckId = good.WreckId, SourceKind = good.SourceKind, Location = good.Location, FallEnergyJPerKg = good.FallEnergyJPerKg, BuildPriceUsd = good.BuildPriceUsd, Parts = good.Parts.ToArray() }; f(i); return i; }
            var bad = new List<SalvageInput>
            {
                With(i => i.WreckId = 0), With(i => i.Parts = new SalvagePart[0]), With(i => i.Parts = null), With(i => i.BuildPriceUsd = 0),
                With(i => i.FallEnergyJPerKg = -1), With(i => i.FallEnergyJPerKg = double.NaN), With(i => i.Location = (SalvageLocation)9), With(i => i.SourceKind = (SalvageSourceKind)9),
                With(i => i.SourceKind = SalvageSourceKind.Kaiju),   // mech classes in a kaiju wreck
                With(i => i.Parts = new[] { good.Parts[0], good.Parts[0] }),
                With(i => i.Parts = new[] { new SalvagePart("x", "unobtainium", 1, 0, 1, 1, 0) }),
                With(i => i.Parts = new[] { new SalvagePart("x", "structure", 0, 0, 1, 1, 0) }),
                With(i => i.Parts = new[] { new SalvagePart("x", "structure", 1, -1, 1, 1, 0) }),
                With(i => i.Parts = new[] { new SalvagePart("x", "structure", 1, 0, 1, 0, 0) }),
                With(i => i.Parts = new[] { new SalvagePart("", "structure", 1, 0, 1, 1, 0) }),
            };
            foreach (var b in bad) { Assert.Throws<ArgumentException>(() => svc.Claim(b, 1)); }
            Assert.AreEqual(0, svc.WreckCount); Assert.AreEqual(0, eco.Funds);
            Assert.Throws<ArgumentNullException>(() => svc.Assess(null));
        }

        // ---------------------------------------------------------------- kaiju tissue and rare parts (R-104 5.4)

        [Test] public void Kaiju_CarcassMass_MatchesTheSpeciesTable()
        {
            var y = Kit.Yields();
            double Total(double l) => y.KaijuTissueMassKg(l, "bone") + y.KaijuTissueMassKg(l, "hide") + y.KaijuTissueMassKg(l, "tendon");
            // R-104 table: 2,700 t / 12,600 t / 56,600 t carcass; the three tissue classes are 0.53 of it
            Assert.AreEqual(2_700_000 * 0.53, Total(60), 2_700_000 * 0.53 * 0.02);
            Assert.AreEqual(12_600_000 * 0.53, Total(100), 12_600_000 * 0.53 * 0.02);
            Assert.AreEqual(56_600_000 * 0.53, Total(165), 56_600_000 * 0.53 * 0.02);
            Assert.AreEqual(56_600_000 * 0.12, y.KaijuTissueMassKg(165, "bone"), 56_600_000 * 0.12 * 0.02);
            Assert.Throws<ArgumentException>(() => y.KaijuTissueMassKg(100, "structure"));
        }

        [Test] public void Kaiju_HeavyBoneIsThroughputLimited_AndUnlocksRareSets()
        {
            var svc = Kit.New(out var eco); var y = svc.Yields;
            var res = svc.Assess(Kit.Kaiju(1, SalvageLocation.Land, y, 165));
            var bone = res.Parts.First(p => p.PartId == "k_bone");
            Assert.AreEqual(340_000, bone.RecoveredKg, 340_000 * 0.02, "about 340 t of bone (R-104 5.4)");
            Assert.AreEqual(y.KaijuTissueMassKg(165, "bone") * 0.05, bone.RecoveredKg, 1e-6);
            Assert.AreEqual(18_000_000L, bone.CreditUsd, 18_000_000 * 0.03, "about $18M");
            Assert.LessOrEqual(res.RecoveredKg, y.ThroughputBudgetKg);
            var c = svc.Claim(Kit.Kaiju(1, SalvageLocation.Land, y, 165), 1);
            long sets = svc.RareAvailable("osteoderm_plate");
            Assert.AreEqual((long)Math.Floor(bone.RecoveredKg / 25_000), sets); Assert.GreaterOrEqual(sets, 10);
            Assert.IsTrue(svc.TryConsumeRare("osteoderm_plate", 2)); Assert.AreEqual(sets - 2, svc.RareAvailable("osteoderm_plate"));
            Assert.IsFalse(svc.TryConsumeRare("osteoderm_plate", sets)); Assert.AreEqual(sets - 2, svc.RareAvailable("osteoderm_plate"));
            Assert.IsFalse(svc.TryConsumeRare("no_such_part", 1)); Assert.AreEqual(0, svc.RareAvailable("no_such_part"));
            Assert.AreEqual(c.CreditUsd, eco.Funds);
            Assert.AreEqual(0, res.Parts.First(p => p.PartId == "k_hide").CreditUsd, "hide and tendon carry no sourced cash value");
        }

        [Test] public void Kaiju_RareSetsAccumulateAcrossKills_AndNeverDropWhenConsumed()
        {
            var svc = Kit.New(out _); var y = svc.Yields;
            svc.Claim(Kit.Kaiju(1, SalvageLocation.Land, y, 60), 1);
            long s1 = svc.RareAvailable("osteoderm_plate");
            Assert.AreEqual((long)Math.Floor(svc.InventoryKg("bone") / 25_000), s1);
            svc.TryConsumeRare("osteoderm_plate", s1);
            for (int i = 2; i <= 6; i++) svc.Claim(Kit.Kaiju(i, SalvageLocation.Land, y, 60), i);
            long earnedTotal = (long)Math.Floor(svc.InventoryKg("bone") / 25_000);
            Assert.AreEqual(earnedTotal - s1, svc.RareAvailable("osteoderm_plate"));
        }

        [Test] public void Kaiju_DamageEnergyReducesTissue_AndDeepWaterReducesMore()
        {
            var svc = Kit.New(out _); var y = svc.Yields;
            double clean = svc.Assess(Kit.Kaiju(1, SalvageLocation.Land, y, 100)).RecoveredKg;
            double hurt = svc.Assess(Kit.Kaiju(1, SalvageLocation.Land, y, 100, 40_000)).RecoveredKg;
            double deep = svc.Assess(Kit.Kaiju(1, SalvageLocation.DeepChannel, y, 100)).RecoveredKg;
            Assert.Less(hurt, clean); Assert.LessOrEqual(deep, clean);
        }

        // ---------------------------------------------------------------- building debris (R-104 5.3)

        [Test] public void BuildingDebris_PaidAsSteelScrapOnly()
        {
            var svc = Kit.New(out _);
            var r = svc.Assess(Kit.Building(1, SalvageLocation.Land, 100_000_000.0, 1.0));   // a 100,000 t collapse, all steel
            Assert.AreEqual(0.9 * 100_000_000.0, r.RecoveredKg, 1e-3);
            Assert.AreEqual((long)Math.Floor(90_000.0 * 294), r.CreditUsd); Assert.AreEqual(26_460_000L, r.CreditUsd);
            var mixed = svc.Assess(Kit.Building(2, SalvageLocation.ShallowBay, 1_000_000.0, 0.25));
            Assert.AreEqual(0.7 * 1_000_000.0, mixed.RecoveredKg, 1e-6); Assert.AreEqual((long)Math.Floor(0.7 * 250_000 / 1000.0 * 294), mixed.CreditUsd);
            Assert.IsFalse(mixed.CapApplied);
        }

        [Test] public void BuildingDebris_IsNotAFarmingRoute_TinyNextToABuildPrice()
        {
            var svc = Kit.New(out _); var p = Kit.Prices();
            long collapse = svc.Assess(Kit.Building(1, SalvageLocation.Land, 100_000_000.0, 1.0)).CreditUsd;
            Assert.Less(collapse, p.ChassisPriceUsd("balanced") / 10);
        }
    }
}
