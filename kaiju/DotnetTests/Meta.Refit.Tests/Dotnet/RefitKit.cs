using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;
using Game.Meta.Refit;
using Game.Meta.Salvage;

namespace Game.Meta.Refit.Tests
{
    static class Kit
    {
        public static string Root()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Meta.Refit.json"))) d = d.Parent;
            Assert.IsNotNull(d, "repo root not found");
            return d.FullName;
        }
        public static string PartsText() => File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Refit", "parts@v1.json"));
        public static string ChassisText() => File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Refit", "chassis@v1.json"));
        public static PriceTable Prices() => PriceTable.Parse(File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Economy", "economy_prices@v1.json")));
        public static PartCatalog Parts() => PartCatalog.Parse(PartsText());
        public static ChassisCatalog Chassis(PartCatalog p) => ChassisCatalog.Parse(ChassisText(), p);
        public static EventBus NewBus() => new EventBus(new CollectingErrorSink(), 1000);
        public static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0), S1 = PlayerSlot.FromIndex(1);

        public static ClassPricing Pricing(PriceTable t)
        {
            var d = new Dictionary<string, long>();
            foreach (var kv in t.Classes) d[kv.Key] = kv.Value.UsdPerTonne;
            return new ClassPricing(d, t.SellBackBps);
        }

        public sealed class Rng
        {
            ulong _s; public Rng(ulong seed) { _s = seed; }
            public ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public int Below(int n) => (int)(Next() % (ulong)n);
            public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>A salvage stub holding rare sets; the real service is exercised by its own module's tests.</summary>
        public sealed class Rare : ISalvageService
        {
            public readonly Dictionary<string, long> Sets = new Dictionary<string, long>();
            public SalvageResult Assess(SalvageInput input) => throw new NotSupportedException();
            public SalvageClaim Claim(SalvageInput input, long step) => throw new NotSupportedException();
            public bool IsClaimed(long wreckId) => false;
            public double InventoryKg(string classId) => 0;
            public long RareAvailable(string id) => Sets.TryGetValue(id, out long n) ? n : 0;
            public bool TryConsumeRare(string id, long count) { if (RareAvailable(id) < count) return false; Sets[id] -= count; return true; }
            public long TotalCreditUsd => 0;
        }

        public sealed class Gate : IRefitGate
        {
            public bool Open = true;
            public bool CanRefit(PlayerSlot slot, EntityId mech) => Open;
        }

        public sealed class Rig
        {
            public PriceTable Prices; public PartCatalog Parts; public ChassisCatalog Chassis; public ClassPricing Pricing; public EconomyService Eco; public RefitService Svc;
            public Rare Salvage = new Rare(); public Gate Gate = new Gate(); public EventBus Bus = NewBus();
        }

        public static Rig NewRig(long funds = 0, bool withBus = true)
        {
            var r = new Rig { Prices = Prices(), Parts = Parts() };
            r.Chassis = Chassis(r.Parts); r.Pricing = Pricing(r.Prices);
            r.Eco = new EconomyService(r.Prices, null, funds);
            r.Svc = new RefitService(r.Parts, r.Chassis, r.Pricing, r.Eco, withBus ? r.Bus : null, r.Salvage, r.Gate);
            return r;
        }

        public static Dictionary<string, string> Build(IReadOnlyDictionary<string, string> b) { var d = new Dictionary<string, string>(); foreach (var kv in b) d[kv.Key] = kv.Value; return d; }
        public static Dictionary<string, string> With(IReadOnlyDictionary<string, string> b, params (string slot, string part)[] over) { var d = Build(b); foreach (var o in over) d[o.slot] = o.part; return d; }
    }
}
