using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Roster;

namespace Game.Meta.Roster.Tests
{
    static class Kit
    {
        public static string Root()
        {
            var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Assets", "Game", "Config", "FeatureFlags", "Meta.Roster.json"))) d = d.Parent;
            Assert.IsNotNull(d, "repo root not found");
            return d.FullName;
        }
        public static PriceTable Prices() => PriceTable.Parse(File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "Economy", "economy_prices@v1.json")));
        public static EventBus NewBus() => new EventBus(new CollectingErrorSink(), 1000);
        public static readonly PlayerSlot S0 = PlayerSlot.FromIndex(0), S1 = PlayerSlot.FromIndex(1);

        public sealed class Rng
        {
            ulong _s; public Rng(ulong seed) { _s = seed; }
            public ulong Next() { unchecked { _s += 0x9E3779B97F4A7C15UL; ulong z = _s; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return z ^ (z >> 31); } }
            public int Below(int n) => (int)(Next() % (ulong)n);
            public double Unit() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        }

        public sealed class Collect : IEventHandler<MechLost>
        {
            public readonly List<MechLost> Seen = new List<MechLost>();
            public void Handle(in MechLost e) { Seen.Add(e); }
        }

        /// <summary>A damage record with explicit parts, as the body adapter would present them.</summary>
        public sealed class Record : IBodyDamageRecord
        {
            public readonly List<PartDamage> Parts = new List<PartDamage>();
            public Record(PlayerSlot slot, params (string id, double integrity)[] parts)
            { Slot = slot; foreach (var p in parts) Parts.Add(new PartDamage { PartId = p.id, Integrity = p.integrity, MaxIntegrity = 100 }); }
            public PlayerSlot Slot { get; }
            public void CopyParts(List<PartDamage> into) { into.Clear(); into.AddRange(Parts); }
            public void ApplyParts(IReadOnlyList<PartDamage> parts) { Parts.Clear(); foreach (var p in parts) Parts.Add(p); }
        }

        public static Record Body(PlayerSlot slot, double hp) => new Record(slot, ("body", hp));
        public static Record Full(PlayerSlot slot, double core, double limb) => new Record(slot, ("cockpit_torso", core), ("reactor", 100), ("arm_l", limb), ("arm_r", limb));

        public static RosterService New(out EconomyService eco, EventBus bus = null, long funds = 0, EntityIdAllocator alloc = null)
        {
            eco = new EconomyService(Prices(), null, funds);
            return new RosterService(eco, alloc ?? new EntityIdAllocator(), bus);
        }
    }
}
