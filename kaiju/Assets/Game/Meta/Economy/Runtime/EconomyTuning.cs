using System;
using Game.Core;

namespace Game.Meta.Economy
{
    /// <summary>
    /// economy_tuning@v1 (T-123): the campaign-simulation overlay on economy_prices@v1. Each value carries a source and an Assumed flag and replaces the base value of the
    /// same name (<see cref="ApplyTo"/>). FloorLossLevyBps is new in T-123: the share of the floor chassis price that the treasury is billed when a city reserve hull is
    /// lost (<see cref="FloorLossLevyUsd"/>; the bridge pays it through <c>PayUpkeep</c>, so a shortfall becomes Arrears and the free grant is never blocked).
    /// </summary>
    public sealed class EconomyTuning
    {
        public const string Schema = "economy_tuning@v1";
        public int FeeBps, AdvanceBps, FloorLossLevyBps;
        public bool FeeAssumed, AdvanceAssumed, LevyAssumed;

        public static EconomyTuning Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("economy_tuning: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("economy_tuning: root must be an object");
            var schema = root.Get("schema");
            if (schema == null || schema.Kind != JsonKind.String || schema.Text != Schema) throw new FormatException("economy_tuning: schema must be " + Schema);
            var p = root.Get("parameters");
            if (p == null || p.Kind != JsonKind.Object) throw new FormatException("economy_tuning: parameters missing");
            var t = new EconomyTuning();
            t.FeeBps = Bps(p, "fee_bps", 10000, out t.FeeAssumed);
            t.AdvanceBps = Bps(p, "advance_bps", 10000, out t.AdvanceAssumed);
            t.FloorLossLevyBps = Bps(p, "floor_loss_levy_bps", 5000, out t.LevyAssumed);
            if (t.FeeBps < 1) throw new FormatException("economy_tuning: fee_bps must be positive");
            return t;
        }

        /// <summary>Replaces the fee and the advance of the table. The table is not otherwise changed.</summary>
        public void ApplyTo(PriceTable prices)
        {
            if (prices == null) throw new ArgumentNullException(nameof(prices));
            prices.FeeBps = FeeBps; prices.AdvanceBps = AdvanceBps;
        }

        /// <summary>The bill for losing a floor hull: price of the floor chassis x levy.</summary>
        public long FloorLossLevyUsd(PriceTable prices) => PriceTable.MulDivFloor(prices.ChassisPriceUsd(prices.FloorChassis), FloorLossLevyBps, 10000);

        static int Bps(JsonValue p, string key, int max, out bool assumed)
        {
            var o = p.Get(key);
            if (o == null || o.Kind != JsonKind.Object) throw new FormatException("economy_tuning: missing parameter '" + key + "'");
            var v = o.Get("value"); var a = o.Get("assumed"); var s = o.Get("source");
            if (v == null || !v.TryGetInt64(out long n) || n < 0 || n > max) throw new FormatException("economy_tuning: '" + key + "' out of range 0.." + max);
            if (a == null || a.Kind != JsonKind.Bool) throw new FormatException("economy_tuning: '" + key + "' lacks the assumed flag");
            if (s == null || s.Kind != JsonKind.String || s.Text.Length == 0) throw new FormatException("economy_tuning: '" + key + "' lacks a source");
            assumed = a.Bool;
            return (int)n;
        }
    }
}
