using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Meta.Economy;

namespace Game.Meta.Economy.Tests
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
        public static string PricesText() => File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Economy", "economy_prices@v1.json"));
        public static PriceTable Prices() => PriceTable.Parse(PricesText());
        public static EventBus NewBus() => new EventBus(new CollectingErrorSink(), 1000);

        public static List<ContractAsset> Pool(PriceTable t)
        {
            var p = new List<ContractAsset>();
            foreach (var kv in t.FixedAssets) p.Add(new ContractAsset(kv.Key, kv.Value.Usd));
            for (int i = 0; i < 12; i++) p.Add(new ContractAsset("bld_" + i, t.BuildingValueUsd(900 + 350 * i, 4 + i)));
            return p;
        }

        public sealed class Rng
        {
            ulong _s; public Rng(ulong seed) { _s = seed; }
            public ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public int Below(int n) => (int)(Next() % (ulong)n);
            public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        }

        public sealed class Collect : IEventHandler<ContractCompleted>
        {
            public readonly List<ContractCompleted> Seen = new List<ContractCompleted>();
            public void Handle(in ContractCompleted e) { Seen.Add(e); }
        }

        public static AssetDamageState[] States(ContractInfo c, AssetDamageState s)
        {
            var a = new AssetDamageState[c.Assets.Length]; for (int i = 0; i < a.Length; i++) a[i] = s; return a;
        }
    }
}
