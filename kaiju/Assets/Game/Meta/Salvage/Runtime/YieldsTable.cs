using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Salvage
{
    /// <summary>One material class of salvage_yields@v1.</summary>
    public sealed class MaterialClass
    {
        public string Id, Kind, RarePart, Source;
        public bool Fragments, ReuseOnly, Assumed;
        /// <summary>Scrap price, whole USD per tonne (raw price x scrap bps, integer).</summary>
        public long ScrapUsdPerTonne;
        public long RareMinMassKg;
        public int Priority, PriorityShareBps, TissueShareBps;
    }

    /// <summary>
    /// salvage_yields@v1: recovery efficiency by location, fragmentation scale, reuse and cap parameters, material classes. Parsed from JSON text and
    /// validated up front (FormatException). The fragmentation law and recovery fractions are doubles (physics); every money result is an integer.
    /// </summary>
    public sealed class YieldsTable
    {
        public double ComminutionJPerKg, KaijuDensityKgM3, KaijuVolumeCoeff, KaijuReferenceLengthM, ThroughputKgPerH, WindowH;
        public int ReuseBps, ReuseMinIntegrityBps, CreditCapBps;
        public double[] EtaLocation = new double[3];   // by SalvageLocation
        public SortedDictionary<string, MaterialClass> Classes = new SortedDictionary<string, MaterialClass>(StringComparer.Ordinal);
        public double HeadlineFallEnergy, HeadlineLand, HeadlineShallow, HeadlineDeep, HeadlineTolerance;

        /// <summary>Wreck-credit throughput budget per claim, kg (R-104 5.4: 80 t/h x 48 h).</summary>
        public double ThroughputBudgetKg => ThroughputKgPerH * WindowH;

        static readonly string[] LocationIds = { "land", "shallow_bay", "deep_channel" };

        public static YieldsTable Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("salvage_yields: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("salvage_yields: root must be an object");
            var schema = root.Get("schema");
            if (schema == null || schema.Kind != JsonKind.String || schema.Text != SalvageContractVersions.YieldsSchema)
                throw new FormatException("salvage_yields: schema must be " + SalvageContractVersions.YieldsSchema);
            var t = new YieldsTable();
            var p = Obj(root, "parameters");
            t.ComminutionJPerKg = Pos(p, "comminution_j_per_kg");
            t.ReuseBps = Bps(p, "reuse_bps"); t.ReuseMinIntegrityBps = Bps(p, "reuse_min_integrity_bps"); t.CreditCapBps = Bps(p, "credit_cap_bps_of_build_price");
            t.KaijuDensityKgM3 = Pos(p, "kaiju_density_kg_m3"); t.KaijuVolumeCoeff = Pos(p, "kaiju_volume_m3_coeff"); t.KaijuReferenceLengthM = Pos(p, "kaiju_reference_length_m");
            t.ThroughputKgPerH = Pos(p, "throughput_kg_per_h"); t.WindowH = Pos(p, "window_h");

            var locs = Arr(root, "locations");
            if (locs.Count != LocationIds.Length) throw new FormatException("salvage_yields: locations must list " + string.Join(", ", LocationIds));
            for (int i = 0; i < LocationIds.Length; i++)
            {
                if (Str(locs[i], "id") != LocationIds[i]) throw new FormatException("salvage_yields: location " + i + " must be " + LocationIds[i]);
                t.EtaLocation[i] = Bps(locs[i], "eta_bps") / 10000.0;
                if (t.EtaLocation[i] <= 0) throw new FormatException("salvage_yields: eta must be positive for " + LocationIds[i]);
            }
            if (t.EtaLocation[0] < t.EtaLocation[1] || t.EtaLocation[1] < t.EtaLocation[2]) throw new FormatException("salvage_yields: eta must not increase with depth");

            var h = Obj(root, "headline");
            t.HeadlineFallEnergy = Pos(h, "fall_energy_j_per_kg"); t.HeadlineLand = Pos(h, "land"); t.HeadlineShallow = Pos(h, "shallow_bay");
            t.HeadlineDeep = Pos(h, "deep_channel"); t.HeadlineTolerance = Pos(h, "tolerance");

            foreach (var c in Arr(root, "classes"))
            {
                var m = new MaterialClass
                {
                    Id = Str(c, "id"), Kind = Str(c, "kind"), Fragments = Bool(c, "fragments"), ReuseOnly = Bool(c, "reuse_only"), Assumed = Bool(c, "assumed"),
                    Source = Str(c, "source"), RarePart = Str(c, "rare_part"), RareMinMassKg = Int(c, "rare_min_mass_kg"), Priority = (int)Int(c, "priority"),
                    PriorityShareBps = Bps(c, "priority_share_bps"), TissueShareBps = c.Get("tissue_share_bps") != null ? Bps(c, "tissue_share_bps") : 0,
                };
                long raw = Int(c, "scrap_raw_usd_per_tonne"); int sb = Bps(c, "scrap_bps");
                if (raw < 0) throw new FormatException("salvage_yields: bad scrap price in " + m.Id);
                m.ScrapUsdPerTonne = checked(raw * sb) / 10000;
                if (m.Id.Length == 0 || (m.Kind != "mech" && m.Kind != "tissue" && m.Kind != "debris")) throw new FormatException("salvage_yields: bad class " + m.Id);
                if (m.RareMinMassKg < 0 || (m.RarePart.Length > 0) != (m.RareMinMassKg > 0)) throw new FormatException("salvage_yields: rare part and minimum mass must go together in " + m.Id);
                if (m.Kind == "debris" && m.Fragments) throw new FormatException("salvage_yields: debris cannot fragment (" + m.Id + ")");
                if (m.Kind == "tissue" && m.Priority < 1) throw new FormatException("salvage_yields: tissue class needs a priority >= 1 (" + m.Id + ")");
                if (m.PriorityShareBps == 0) throw new FormatException("salvage_yields: priority_share_bps must be positive in " + m.Id);
                if (!t.Classes.TryAdd2(m.Id, m)) throw new FormatException("salvage_yields: duplicate class " + m.Id);
            }
            var rare = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in t.Classes.Values) if (c.RarePart.Length > 0 && !rare.Add(c.RarePart)) throw new FormatException("salvage_yields: duplicate rare part " + c.RarePart);
            return t;
        }

        /// <summary>f_frag = 1 - exp(-e / e_c) (R-104 5.1), clamped to 0..1. Non-finite or negative energy gives 0.</summary>
        public double FragmentedFraction(double specificEnergyJPerKg)
        {
            if (!(specificEnergyJPerKg > 0) || double.IsInfinity(specificEnergyJPerKg)) return specificEnergyJPerKg > 0 ? 1.0 : 0.0;
            double f = 1.0 - Math.Exp(-specificEnergyJPerKg / ComminutionJPerKg);
            return f < 0 ? 0 : f > 1 ? 1 : f;
        }

        /// <summary>Recovered fraction by mass of a part that absorbed the given specific energy: (1 - f_frag) x eta_loc.</summary>
        public double RecoveredFraction(double specificEnergyJPerKg, SalvageLocation location) => (1.0 - FragmentedFraction(specificEnergyJPerKg)) * EtaLocation[(int)location];

        /// <summary>Kaiju carcass mass of one tissue class for a body of the given length, kg (R-104 5.4: V = 0.07 (l/1.8)^3, rho 1,050).</summary>
        public double KaijuTissueMassKg(double lengthM, string tissueClassId)
        {
            if (!(lengthM > 0)) throw new ArgumentOutOfRangeException(nameof(lengthM));
            if (!Classes.TryGetValue(tissueClassId, out var c) || c.Kind != "tissue") throw new ArgumentException("not a tissue class: " + tissueClassId);
            double r = lengthM / KaijuReferenceLengthM;
            return KaijuVolumeCoeff * r * r * r * KaijuDensityKgM3 * (c.TissueShareBps / 10000.0);
        }

        // ---- json helpers
        static JsonValue Obj(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Object) throw new FormatException("salvage_yields: missing object '" + key + "'");
            return v;
        }
        static List<JsonValue> Arr(JsonValue o, string key)
        {
            var a = o.Get(key);
            if (a == null || a.Kind != JsonKind.Array || a.Items.Count == 0) throw new FormatException("salvage_yields: '" + key + "' must be a non-empty array");
            return a.Items;
        }
        static string Str(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.String) throw new FormatException("salvage_yields: missing string '" + key + "'");
            return v.Text;
        }
        static long Int(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || !v.TryGetInt64(out long n)) throw new FormatException("salvage_yields: missing integer '" + key + "'");
            return n;
        }
        static double Pos(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || !v.TryGetDouble(out double d) || !(d > 0) || double.IsInfinity(d)) throw new FormatException("salvage_yields: '" + key + "' must be a positive number");
            return d;
        }
        static bool Bool(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Bool) throw new FormatException("salvage_yields: missing bool '" + key + "'");
            return v.Bool;
        }
        static int Bps(JsonValue o, string key)
        {
            long n = Int(o, key);
            if (n < 0 || n > 10000) throw new FormatException("salvage_yields: '" + key + "' out of 0..10000 bps");
            return (int)n;
        }
    }

    static class DictExt
    {
        public static bool TryAdd2<K, V>(this SortedDictionary<K, V> d, K k, V v) { if (d.ContainsKey(k)) return false; d.Add(k, v); return true; }
    }
}
