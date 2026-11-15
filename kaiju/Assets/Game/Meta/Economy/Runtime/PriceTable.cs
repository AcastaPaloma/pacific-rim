using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Economy
{
    /// <summary>Installed price of one part class (R-104 section 2). Data, not code: each row carries its source and an Assumed flag.</summary>
    public sealed class PriceClass
    {
        public string Id; public long UsdPerTonne; public string Source; public bool Assumed;
    }

    public sealed class ChassisComposition
    {
        public string Id;
        /// <summary>Tonnes by class id (long, whole tonnes).</summary>
        public SortedDictionary<string, long> Tonnes = new SortedDictionary<string, long>(StringComparer.Ordinal);
    }

    public sealed class FixedAssetValue
    {
        public string Id; public long Usd; public string Source; public bool Assumed;
    }

    /// <summary>
    /// economy_prices@v1: class prices, chassis compositions, contract and repair parameters, replacement-cost catalogue. Parsed from JSON text;
    /// everything validated up front (FormatException). All arithmetic downstream is integer.
    /// </summary>
    public sealed class PriceTable
    {
        public SortedDictionary<string, PriceClass> Classes = new SortedDictionary<string, PriceClass>(StringComparer.Ordinal);
        public SortedDictionary<string, ChassisComposition> Chassis = new SortedDictionary<string, ChassisComposition>(StringComparer.Ordinal);
        public SortedDictionary<string, FixedAssetValue> FixedAssets = new SortedDictionary<string, FixedAssetValue>(StringComparer.Ordinal);

        public int FeeBps, AdvanceBps, FailureFloorBps, CapBps, SellBackBps, RepairOverheadBps, UpkeepBps;
        public string CapReferenceChassis, FloorChassis;
        public long GuaranteedMinPayoutUsd, BuildingUsdPerM2;
        public int[] LossBps = new int[5];       // None, Slight, Moderate, Extensive, Complete
        public int[] RepairCapBps = new int[3];  // Light, Moderate, Heavy
        public int IntactBps, LightBps, ModerateBps; // integrity-fraction thresholds in bps

        public static PriceTable Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("economy_prices: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("economy_prices: root must be an object");
            var schema = root.Get("schema");
            if (schema == null || schema.Kind != JsonKind.String || schema.Text != EconomyContractVersions.PricesSchema)
                throw new FormatException("economy_prices: schema must be " + EconomyContractVersions.PricesSchema);
            var t = new PriceTable();

            foreach (var c in Items(root, "classes"))
            {
                var pc = new PriceClass { Id = Str(c, "id"), UsdPerTonne = Int(c, "usd_per_tonne"), Source = Str(c, "source"), Assumed = Bool(c, "assumed") };
                if (pc.Id.Length == 0 || pc.UsdPerTonne <= 0) throw new FormatException("economy_prices: bad class " + pc.Id);
                if (!t.Classes.TryAdd2(pc.Id, pc)) throw new FormatException("economy_prices: duplicate class " + pc.Id);
            }
            foreach (var c in Items(root, "chassis"))
            {
                var ch = new ChassisComposition { Id = Str(c, "id") };
                var tonnes = c.Get("tonnes");
                if (tonnes == null || tonnes.Kind != JsonKind.Object) throw new FormatException("economy_prices: chassis " + ch.Id + " lacks tonnes");
                foreach (var m in tonnes.Members)
                {
                    if (!t.Classes.ContainsKey(m.Key)) throw new FormatException("economy_prices: chassis " + ch.Id + " uses unknown class " + m.Key);
                    if (!m.Value.TryGetInt64(out long v) || v < 0) throw new FormatException("economy_prices: chassis " + ch.Id + " bad tonnes for " + m.Key);
                    ch.Tonnes[m.Key] = v;
                }
                if (!t.Chassis.TryAdd2(ch.Id, ch)) throw new FormatException("economy_prices: duplicate chassis " + ch.Id);
            }
            foreach (var c in Items(root, "fixed_assets"))
            {
                var a = new FixedAssetValue { Id = Str(c, "id"), Usd = Int(c, "usd"), Source = Str(c, "source"), Assumed = Bool(c, "assumed") };
                if (a.Usd <= 0 || !t.FixedAssets.TryAdd2(a.Id, a)) throw new FormatException("economy_prices: bad or duplicate fixed asset " + a.Id);
            }
            var p = root.Get("parameters");
            if (p == null || p.Kind != JsonKind.Object) throw new FormatException("economy_prices: parameters missing");
            t.FeeBps = Bps(p, "fee_bps"); t.AdvanceBps = Bps(p, "advance_bps"); t.FailureFloorBps = Bps(p, "failure_floor_saved_bps");
            t.CapBps = Bps(p, "cap_bps_of_reference"); t.SellBackBps = Bps(p, "sellback_bps"); t.UpkeepBps = Bps(p, "upkeep_bps_per_sortie");
            t.RepairOverheadBps = (int)Int(p, "repair_overhead_bps");
            if (t.RepairOverheadBps < 10000 || t.RepairOverheadBps > 100000) throw new FormatException("economy_prices: repair_overhead_bps out of range");
            t.CapReferenceChassis = Str(p, "cap_reference_chassis"); t.FloorChassis = Str(p, "floor_chassis");
            if (!t.Chassis.ContainsKey(t.CapReferenceChassis) || !t.Chassis.ContainsKey(t.FloorChassis)) throw new FormatException("economy_prices: reference or floor chassis unknown");
            t.GuaranteedMinPayoutUsd = Int(p, "guaranteed_min_payout_usd"); t.BuildingUsdPerM2 = Int(p, "building_usd_per_m2");
            if (t.GuaranteedMinPayoutUsd < 0 || t.BuildingUsdPerM2 <= 0) throw new FormatException("economy_prices: bad guaranteed minimum or building rate");
            var lb = p.Get("loss_bps");
            if (lb == null || lb.Kind != JsonKind.Array || lb.Items.Count != 5) throw new FormatException("economy_prices: loss_bps needs 5 entries");
            for (int i = 0; i < 5; i++)
            {
                if (!lb.Items[i].TryGetInt64(out long v) || v < 0 || v > 10000 || (i > 0 && v < t.LossBps[i - 1])) throw new FormatException("economy_prices: loss_bps must be ascending within 0..10000");
                t.LossBps[i] = (int)v;
            }
            var rc = p.Get("repair_cap_bps");
            if (rc == null || rc.Kind != JsonKind.Array || rc.Items.Count != 3) throw new FormatException("economy_prices: repair_cap_bps needs 3 entries");
            for (int i = 0; i < 3; i++)
            {
                if (!rc.Items[i].TryGetInt64(out long v) || v < 0 || v > 10000) throw new FormatException("economy_prices: repair_cap_bps out of range");
                t.RepairCapBps[i] = (int)v;
            }
            t.IntactBps = Bps(p, "intact_min_integrity_bps"); t.LightBps = Bps(p, "light_min_integrity_bps"); t.ModerateBps = Bps(p, "moderate_min_integrity_bps");
            if (!(t.ModerateBps < t.LightBps && t.LightBps < t.IntactBps)) throw new FormatException("economy_prices: integrity thresholds must ascend");
            return t;
        }

        // ---- price function: mass x class index

        public PriceQuote QuotePart(string classId, long massKg)
        {
            if (massKg < 0) throw new ArgumentOutOfRangeException(nameof(massKg));
            if (!Classes.TryGetValue(classId ?? "", out var c)) throw new ArgumentException("Unknown price class '" + classId + "'");
            return new PriceQuote(c.Id, massKg, c.UsdPerTonne, MulDivRound(massKg, c.UsdPerTonne, 1000), c.Assumed, c.Source);
        }

        public long ChassisPriceUsd(string chassisId)
        {
            if (!Chassis.TryGetValue(chassisId ?? "", out var ch)) throw new ArgumentException("Unknown chassis '" + chassisId + "'");
            long sum = 0;
            foreach (var kv in ch.Tonnes) sum = checked(sum + checked(kv.Value * Classes[kv.Key].UsdPerTonne));
            return sum;
        }

        public long ChassisMassTonnes(string chassisId)
        {
            if (!Chassis.TryGetValue(chassisId ?? "", out var ch)) throw new ArgumentException("Unknown chassis '" + chassisId + "'");
            long m = 0; foreach (var kv in ch.Tonnes) m += kv.Value; return m;
        }

        /// <summary>Contract payout cap: CapBps of the reference chassis price.</summary>
        public long PayoutCapUsd => MulDivFloor(ChassisPriceUsd(CapReferenceChassis), CapBps, 10000);

        public long SellBackUsd(long priceUsd) => MulDivFloor(priceUsd, SellBackBps, 10000);

        public long UpkeepPerSortieUsd(long mechPriceUsd) => MulDivRound(mechPriceUsd, UpkeepBps, 10000);

        /// <summary>Replacement cost of a building: footprint (m2) x floors x USD per m2 of gross floor area.</summary>
        public long BuildingValueUsd(long footprintM2, int floors)
        {
            if (footprintM2 <= 0 || floors <= 0) throw new ArgumentOutOfRangeException(nameof(footprintM2));
            return checked(footprintM2 * floors * BuildingUsdPerM2);
        }

        // ---- contract payout (R-104 section 3)

        public int LossRatioBps(AssetDamageState s) => LossBps[(int)s];

        public long LossUsd(ContractAsset[] assets, AssetDamageState[] states)
        {
            if (assets.Length != states.Length) throw new ArgumentException("states must align with assets");
            long l = 0;
            for (int i = 0; i < assets.Length; i++) l += MulDivFloor(assets[i].ReplacementUsd, LossRatioBps(states[i]), 10000);
            return l;
        }

        /// <summary>Payout for a protected value V and loss L: 0 when the saved share is below the failure floor, else min(fee x (V - L), cap).</summary>
        public long Payout(long valueUsd, long lossUsd)
        {
            if (valueUsd <= 0 || lossUsd < 0 || lossUsd > valueUsd) throw new ArgumentOutOfRangeException(nameof(lossUsd));
            long saved = valueUsd - lossUsd;
            // saved/V < floor  <=>  saved*10000 < floor*V   (exact integer comparison)
            if (checked(saved * 10000) < checked((long)FailureFloorBps * valueUsd)) return 0;
            return Math.Min(MulDivFloor(saved, FeeBps, 10000), PayoutCapUsd);
        }

        public bool Failed(long valueUsd, long lossUsd) => Payout(valueUsd, lossUsd) == 0 && checked((valueUsd - lossUsd) * 10000) < checked((long)FailureFloorBps * valueUsd);

        /// <summary>Payout of an undamaged contract: the number shown at offer time.</summary>
        public long ExpectedPayout(long valueUsd) => Payout(valueUsd, 0);

        public long AdvanceUsd(long valueUsd) => MulDivFloor(ExpectedPayout(valueUsd), AdvanceBps, 10000);

        // ---- repair (R-104 section 4)

        /// <summary>Repair cost of one part: overhead x (1 - I) x price, capped per state; replaced (price) at I = 0; free when intact (I >= 0.95).</summary>
        public long RepairCostUsd(long partPriceUsd, double integrityFraction)
        {
            if (partPriceUsd < 0) throw new ArgumentOutOfRangeException(nameof(partPriceUsd));
            if (double.IsNaN(integrityFraction) || integrityFraction < 0 || integrityFraction > 1) throw new ArgumentOutOfRangeException(nameof(integrityFraction));
            long iBps = (long)Math.Floor(integrityFraction * 10000.0 + 0.5);
            if (iBps >= IntactBps) return 0;
            if (iBps == 0) return partPriceUsd;
            long lossBps = 10000 - iBps;
            long raw = MulDivFloor(partPriceUsd, lossBps * RepairOverheadBps, 100000000L);
            int capBps = iBps >= LightBps ? RepairCapBps[0] : iBps >= ModerateBps ? RepairCapBps[1] : RepairCapBps[2];
            return Math.Min(raw, MulDivFloor(partPriceUsd, capBps, 10000));
        }

        // ---- integer helpers (a*b is checked; b/c exact rounding policy is explicit)

        public static long MulDivFloor(long a, long b, long c) => checked(a * b) / c;
        public static long MulDivRound(long a, long b, long c) => (checked(a * b) * 2 + c) / (2 * c);

        // ---- json helpers
        static IEnumerable<JsonValue> Items(JsonValue o, string key)
        {
            var a = o.Get(key);
            if (a == null || a.Kind != JsonKind.Array || a.Items.Count == 0) throw new FormatException("economy_prices: '" + key + "' must be a non-empty array");
            return a.Items;
        }
        static string Str(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.String) throw new FormatException("economy_prices: missing string '" + key + "'");
            return v.Text;
        }
        static long Int(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || !v.TryGetInt64(out long n)) throw new FormatException("economy_prices: missing integer '" + key + "'");
            return n;
        }
        static bool Bool(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Bool) throw new FormatException("economy_prices: missing bool '" + key + "'");
            return v.Bool;
        }
        static int Bps(JsonValue o, string key)
        {
            long n = Int(o, key);
            if (n < 0 || n > 10000) throw new FormatException("economy_prices: '" + key + "' out of 0..10000 bps");
            return (int)n;
        }
    }

    static class DictExt
    {
        public static bool TryAdd2<K, V>(this SortedDictionary<K, V> d, K k, V v) { if (d.ContainsKey(k)) return false; d.Add(k, v); return true; }
    }
}
