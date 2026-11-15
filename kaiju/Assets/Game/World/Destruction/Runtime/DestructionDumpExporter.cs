// Metric dump exporters (T-036 step 3/4): `destruction_dump@v1` (M-052, schema in Tools/Metrics/kaiju_metrics/destruction/m052.py)
// and `fragility_dump@v1` (M-051, schema in m051.py). Pure, engine-free, deterministic: same inputs give the same string.
// Both are read-only views of DamageModel; neither changes a state. Numbers are written with "R" so they round-trip.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;

namespace Game.World.Destruction
{
    public static class DestructionDumpExporter
    {
        /// <summary>The five M-052 archetypes (R-021 §4): house, mid-rise, tower, wide low warehouse, slender tower.</summary>
        public static readonly BuildingAttributes[] Archetypes =
        {
            new BuildingAttributes { HeightM = 7, Levels = 2, FootprintM2 = 100, Residential = true },
            new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 },
            new BuildingAttributes { HeightM = 160, Levels = 40, FootprintM2 = 1500 },
            new BuildingAttributes { HeightM = 8, Levels = 2, FootprintM2 = 10000 },
            new BuildingAttributes { HeightM = 120, Levels = 30, FootprintM2 = 150 },
        };
        static readonly string[] ArchetypeNames = { "house", "mid-rise", "tower", "wide-warehouse", "slender-tower" };

        public static string HazusName(HazusType t)
        {
            switch (t)
            {
                case HazusType.W1: return "W1";
                case HazusType.C2L: return "C2.L";
                case HazusType.C2M: return "C2.M";
                default: return "C2.H";
            }
        }

        static string Num(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Not finite: " + v);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// M-052 dump of the given buildings (they must be registered in <paramref name="model"/> with these attributes,
        /// in the order given) plus the two-step vs direct transition checks for the five archetypes.
        /// </summary>
        public static string BuildDestructionDump(DamageModel model, IReadOnlyList<KeyValuePair<BuildingId, BuildingAttributes>> buildings)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (buildings == null || buildings.Count == 0) throw new ArgumentException("An empty dump proves nothing", nameof(buildings));
            var sb = new StringBuilder(1 << 12);
            sb.Append("{\"format\":\"destruction_dump\",\"schema\":1,\"model_rev\":")
              .Append(DestructionContractVersions.ModelRevision.ToString(CultureInfo.InvariantCulture)).Append(",\"buildings\":[");
            for (int i = 0; i < buildings.Count; i++)
            {
                var id = buildings[i].Key;
                var attrs = buildings[i].Value;
                if (!model.TryGetState(id, out var st) || !model.TryGetDebrisAccount(id, out var acc))
                    throw new ArgumentException("Building not registered: " + id, nameof(buildings));
                var cap = CapacityModel.Compute(id, attrs);
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(id.ToString()).Append("\",\"class\":\"").Append(cap.Rise.ToString())
                  .Append("\",\"H\":").Append(Num(attrs.HeightM > 0 ? attrs.HeightM : DamageParameters.MetresPerLevel))
                  .Append(",\"T\":").Append(Num(cap.PeriodS))
                  .Append(",\"state\":").Append(st.State.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"e_cum_j\":").Append(Num(st.CumulativeEnergyJ))
                  .Append(",\"debris_kg\":").Append(Num(st.DebrisKg))
                  .Append(",\"m0_kg\":").Append(Num(acc.MassKg))
                  .Append(",\"m_standing_kg\":").Append(Num(acc.StandingKg))
                  .Append(",\"piece_count\":").Append(acc.PieceCount.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"piece_mass_sum_kg\":").Append(Num(acc.PieceMassSumKg));
                if (st.State >= 3)
                {
                    // W_ext is not part of the C# balance (DebrisAccount.ExternalWorkJ is informational), so it is exported as 0.
                    sb.Append(",\"energy\":{\"dpe_j\":").Append(Num(acc.PotentialEnergyJ))
                      .Append(",\"w_ext_j\":0,\"e_frac_j\":").Append(Num(acc.FractureJ))
                      .Append(",\"e_ground_j\":").Append(Num(acc.GroundJ))
                      .Append(",\"ke_ej_j\":").Append(Num(acc.EjectaJ)).Append('}');
                }
                sb.Append('}');
            }
            sb.Append("],\"transition_checks\":[");
            for (int a = 0; a < Archetypes.Length; a++)
            {
                if (a > 0) sb.Append(',');
                var id = BuildingId.From(BuildingSource.OsmWay, 9_000_000UL + (ulong)a);
                var direct = Totals(id, Archetypes[a], false);
                var two = Totals(id, Archetypes[a], true);
                sb.Append("{\"archetype\":\"").Append(ArchetypeNames[a]).Append("\",\"direct\":").Append(direct)
                  .Append(",\"two_step\":").Append(two).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Drives a fresh model to state 4 directly (1->4) or via state 3 (1->3->4) and reports the final totals.
        static string Totals(BuildingId id, BuildingAttributes attrs, bool viaThree)
        {
            var m = new DamageModel();
            m.Register(id, attrs);
            m.TryGetThresholds(id, out _, out _, out double e3, out double e4);
            if (viaThree) m.Apply(id, e3 * 1.0001, DamageCause.Impact, default, 1);
            m.TryGetState(id, out var s);
            m.Apply(id, e4 * 1.0001 - s.CumulativeEnergyJ, DamageCause.Impact, default, 2);
            m.TryGetState(id, out s);
            if (s.State != 4) throw new InvalidOperationException("Archetype did not reach state 4");
            m.TryGetDebrisAccount(id, out var acc);
            return "{\"debris_kg\":" + Num(s.DebrisKg) + ",\"e_frac_j\":" + Num(acc.FractureJ) + ",\"dpe_j\":" + Num(acc.PotentialEnergyJ) + "}";
        }

        /// <summary>The M-051 classes: A house (W1), B mid-rise (C2.M), C tower (C2.H), Moderate-Code era (YearBuilt 0).</summary>
        public static readonly BuildingAttributes[] FragilityClasses =
        {
            new BuildingAttributes { HeightM = 7, Levels = 2, FootprintM2 = 100, Residential = true },
            new BuildingAttributes { HeightM = 25, Levels = 6, FootprintM2 = 600 },
            new BuildingAttributes { HeightM = 160, Levels = 40, FootprintM2 = 1500 },
        };

        public static readonly double[] PgaGridG = { 0.05, 0.1, 0.2, 0.4, 0.8, 1.6 };

        /// <summary>
        /// M-051 dump (R-021 §4 M-051, R-026 C-3): per class, <paramref name="n"/> distinct IDs, a fresh model per PGA grid
        /// point (shaking route, event index 1) and per E_del point (energy route, 13 log points 0.1 E_1 .. 10 E_4); the
        /// grid is run twice to prove bit equality; state_decreases counts buildings whose state fell as intensity rose.
        /// </summary>
        public static string BuildFragilityDump(int n)
        {
            if (n < 1000) throw new ArgumentOutOfRangeException(nameof(n), "M-051 needs N >= 1000 (10,000 for the binomial tolerance)");
            var sb = new StringBuilder(1 << 14);
            sb.Append("{\"format\":\"fragility_dump\",\"schema\":1,\"n\":").Append(n.ToString(CultureInfo.InvariantCulture)).Append(",\"classes\":[");
            for (int c = 0; c < FragilityClasses.Length; c++)
            {
                var attrs = FragilityClasses[c];
                var cap0 = CapacityModel.Compute(BuildingId.From(BuildingSource.OsmWay, 1UL), attrs);
                var egrid = new double[13];
                double lo = 0.1 * cap0.E1, hi = 10.0 * cap0.E4;
                for (int i = 0; i < 13; i++) egrid[i] = lo * Math.Pow(hi / lo, i / 12.0);

                var runA = RunGrid(attrs, n, egrid, out int decreases);
                var runB = RunGrid(attrs, n, egrid, out _);
                bool same = runA.Hash == runB.Hash;

                if (c > 0) sb.Append(',');
                sb.Append("{\"class\":\"").Append(cap0.Rise.ToString()).Append("\",\"hazus_type\":\"").Append(HazusName(cap0.Type))
                  .Append("\",\"H\":").Append(Num(attrs.HeightM)).Append(",\"T\":").Append(Num(cap0.PeriodS))
                  .Append(",\"theta_k\":[");
                for (int k = 1; k <= 4; k++) sb.Append(k > 1 ? "," : "").Append(Num(DamageParameters.DriftMedian(cap0.Type, k, cap0.Era)));
                sb.Append("],\"beta\":").Append(Num(DamageParameters.ShakingBetaCapacity))
                  .Append(",\"beta_tot\":").Append(Num(DamageParameters.ShakingBetaTotal))
                  .Append(",\"e_k_j\":[").Append(Num(cap0.E1)).Append(',').Append(Num(cap0.E2)).Append(',').Append(Num(cap0.E3)).Append(',').Append(Num(cap0.E4))
                  .Append("],\"beta_e\":").Append(Num(DamageParameters.BetaE)).Append(",\"pga_points\":[");
                for (int p = 0; p < PgaGridG.Length; p++)
                    sb.Append(p > 0 ? "," : "").Append("{\"pga_g\":").Append(Num(PgaGridG[p])).Append(",\"counts\":").Append(Counts(runA.Pga[p])).Append('}');
                sb.Append("],\"energy_points\":[");
                for (int p = 0; p < egrid.Length; p++)
                    sb.Append(p > 0 ? "," : "").Append("{\"e_del_j\":").Append(Num(egrid[p])).Append(",\"counts\":").Append(Counts(runA.Energy[p])).Append('}');
                sb.Append("],\"state_decreases\":").Append(decreases.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"repeat_identical\":").Append(same ? "true" : "false").Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        sealed class GridRun
        {
            public int[][] Pga, Energy;
            public ulong Hash;
        }

        static string Counts(int[] ge)
        {
            return "[" + ge[1].ToString(CultureInfo.InvariantCulture) + "," + ge[2].ToString(CultureInfo.InvariantCulture) + ","
                 + ge[3].ToString(CultureInfo.InvariantCulture) + "," + ge[4].ToString(CultureInfo.InvariantCulture) + "]";
        }

        // Counts of state >= k (index 1..4) per grid point; a fresh model for each point.
        static GridRun RunGrid(BuildingAttributes attrs, int n, double[] egrid, out int decreases)
        {
            var r = new GridRun { Pga = new int[PgaGridG.Length][], Energy = new int[egrid.Length][], Hash = 14695981039346656037UL };
            var prev = new byte[n];
            decreases = 0;
            for (int p = 0; p < PgaGridG.Length; p++)
            {
                var m = new DamageModel();
                var cnt = new int[5];
                for (int i = 0; i < n; i++)
                {
                    var id = BuildingId.From(BuildingSource.OsmWay, 1_000_000UL + (ulong)i);
                    m.Register(id, attrs);
                    m.ApplyShaking(id, PgaGridG[p], DamageParameters.FirstEventIndex, 1);
                    m.TryGetState(id, out var s);
                    if (s.State < prev[i]) decreases++;
                    prev[i] = s.State;
                    for (int k = 1; k <= s.State; k++) cnt[k]++;
                    r.Hash = (r.Hash ^ s.State) * 1099511628211UL;
                }
                r.Pga[p] = cnt;
            }
            Array.Clear(prev, 0, n);
            for (int p = 0; p < egrid.Length; p++)
            {
                var m = new DamageModel();
                var cnt = new int[5];
                for (int i = 0; i < n; i++)
                {
                    var id = BuildingId.From(BuildingSource.OsmWay, 1_000_000UL + (ulong)i);
                    m.Register(id, attrs);
                    m.Apply(id, egrid[p], DamageCause.Impact, default, 1);
                    m.TryGetState(id, out var s);
                    if (s.State < prev[i]) decreases++;
                    prev[i] = s.State;
                    for (int k = 1; k <= s.State; k++) cnt[k]++;
                    r.Hash = (r.Hash ^ s.State) * 1099511628211UL;
                }
                r.Energy[p] = cnt;
            }
            return r;
        }
    }
}
