using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Units.Kaiju.Contracts;

namespace Game.Units.Kaiju
{
    /// <summary>Parses KaijuSpeciesSpec@v1 and kaiju_tissue@v1 JSON. Everything is validated structurally up front (FormatException).</summary>
    public static class KaijuSpeciesLoader
    {
        public static KaijuSpeciesSpec Parse(string json)
        {
            var root = Root(json, KaijuContractVersions.SpeciesSchema);
            var s = new KaijuSpeciesSpec { Id = Str(root, "id"), DesignCodename = Str(root, "design_codename") };
            if (!Enum.TryParse(Str(root, "mass_class"), false, out s.MassClass)) throw new FormatException("species: bad mass_class");
            var g = Obj(root, "geometry");
            s.Geometry = new SpecGeometry
            {
                HeightM = Num(g, "height_m"), HipHeightM = Num(g, "hip_height_m"), NLimbs = Num(g, "n_limbs"), ExtraVolumeM3 = Num(g, "extra_volume_m3"),
                TrackWidthM = Num(g, "track_width_m"), FootAreaM2 = Num(g, "foot_area_m2"), FootRadiusM = Num(g, "foot_radius_m"), ComHeightM = Num(g, "com_height_m"),
                Trunk = Ell(g, "trunk"), Head = Ell(g, "head"), Neck = Fru(g, "neck", "R", "r"), Tail = Fru(g, "tail", "R", "r"),
                LimbUpper = Fru(g, "limb_upper", "R_top", "R_bot"), LimbLower = Fru(g, "limb_lower", "R_top", "R_bot"),
            };
            var m = Obj(root, "mass"); var rv = Obj(m, "recipe_validation");
            s.Mass = new SpecMass
            {
                RhoEff = Num(m, "rho_eff_kg_m3"), VolumeM3 = Num(m, "volume_m3"), MassKg = Num(m, "mass_kg"), InertiaYaw = Num(m, "inertia_yaw_kgm2"),
                ElephantTrunk = Ell(rv, "elephant_trunk"), ElephantHead = Ell(rv, "elephant_head"),
                ElephantNeck = Fru(rv, "elephant_neck", "R", "r"), ElephantTail = Fru(rv, "elephant_tail", "R", "r"),
                ElephantLimbUpper = Fru(rv, "elephant_limb_upper", "R_top", "R_bot"), ElephantLimbLower = Fru(rv, "elephant_limb_lower", "R_top", "R_bot"),
                ElephantExtraM3 = Num(rv, "elephant_extra_volume_m3"), ElephantVolumeM3 = Num(rv, "elephant_volume_m3"),
                ElephantMassMinKg = Num(rv, "elephant_mass_min_kg"), ElephantMassMaxKg = Num(rv, "elephant_mass_max_kg"),
            };
            var mt = Obj(root, "material");
            s.Material = new SpecMaterial
            {
                TissueRef = Str(mt, "tissue_ref"), BoneYieldPa = Num(mt, "bone_yield_Pa"), MuscleIsometricPa = Num(mt, "muscle_isometric_Pa"),
                MuscleUtilisationMax = Num(mt, "muscle_utilisation_max"), BoneSfWalkMin = Num(mt, "bone_sf_walk_min"), BoneSfChargeMin = Num(mt, "bone_sf_charge_min"),
            };
            var l = Obj(root, "limb_model");
            s.LimbModel = new SpecLimbModel
            {
                PhiStatic = Num(l, "phi_static"), PhiPeak = Num(l, "phi_peak"), KA = Num(l, "k_a"), KB = Num(l, "k_b"), EFrac = Num(l, "e_frac"), CortexQ = Num(l, "cortex_q"),
                Ema = Num(l, "ema"), BoneAreaShare = Num(l, "bone_area_share"), MuscleAreaShare = Num(l, "muscle_area_share"), LimbAreaM2 = Num(l, "limb_area_m2"),
            };
            var d = Obj(root, "damage");
            s.Damage = new SpecDamage { PhiOneLimbLost = Num(d, "phi_one_limb_lost"), PhiTwoLimbsLost = Num(d, "phi_two_limbs_lost"), FrCrawl = Num(d, "fr_crawl") };
            var ga = Obj(root, "gait");
            s.Gait = new SpecGait
            {
                FrCruise = Num(ga, "fr_cruise"), FrBandMin = Num(ga, "fr_band_min"), FrBandMax = Num(ga, "fr_band_max"), FrChargeCap = Num(ga, "fr_charge_cap"),
                DutyMin = Num(ga, "duty_factor_min"), DutyMax = Num(ga, "duty_factor_max"), DutyCruise = Num(ga, "duty_factor_cruise"),
                StrideK = Num(ga, "stride_k"), StrideExp = Num(ga, "stride_exp"),
                StartStridesMin = Num(ga, "start_strides_min"), StartStridesMax = Num(ga, "start_strides_max"), StopStridesMin = Num(ga, "stop_strides_min"), StopStridesMax = Num(ga, "stop_strides_max"),
                YawRateMaxDps = Num(ga, "yaw_rate_max_dps"), YawAccelMaxDps2 = Num(ga, "yaw_accel_max_dps2"), LateralGMax = Num(ga, "lateral_g_max"),
                PowerWPerKg = Num(ga, "power_w_per_kg"), ResistC = Num(ga, "resist_c"), AccelCapG = Num(ga, "accel_cap_g"),
                CruiseSpeedMs = Num(ga, "cruise_speed_ms"), MaxSpeedMs = Num(ga, "max_speed_ms"),
                StridePeriodCruiseS = Num(ga, "stride_period_cruise_s"), StridePeriodChargeS = Num(ga, "stride_period_charge_s"), AccelPowerCruiseMs2 = Num(ga, "accel_power_cruise_ms2"),
            };
            var st = Obj(root, "stance");
            s.Stance = new SpecStance { RearModeAllowed = Num(st, "rear_mode_allowed"), RearTimeMaxS = Num(st, "rear_time_max_s"), RearPhi = Num(st, "rear_phi"), RearFr = Num(st, "rear_fr"), SwimSpeedMaxMs = Num(st, "swim_speed_max_ms") };
            var at = root.Get("attacks");
            if (at == null || at.Kind != JsonKind.Array || at.Items.Count == 0) throw new FormatException("species: attacks must be a non-empty array");
            foreach (var a in at.Items)
            {
                var sa = new SpecAttack { Id = Str(a, "id") };
                if (!Enum.TryParse(Str(a, "kind"), false, out sa.Kind)) throw new FormatException("species: bad attack kind " + sa.Id);
                sa.MEffKg = Num(a, "m_eff_kg"); sa.VTipMs = Num(a, "v_tip_ms"); sa.MuscleMassKg = Num(a, "muscle_mass_kg"); sa.WorkJPerKg = Num(a, "work_J_kg");
                sa.ElasticAmp = Num(a, "elastic_amp"); sa.DropHeightM = Num(a, "drop_height_m"); sa.TargetMassKg = Num(a, "target_mass_kg"); sa.Restitution = Num(a, "restitution");
                sa.HoldForceN = Num(a, "hold_force_N"); sa.MuscleAreaM2 = Num(a, "muscle_area_m2"); sa.ECapJ = Num(a, "E_cap_J"); sa.ImpulseNs = Num(a, "impulse_Ns");
                sa.CooldownS = Num(a, "cooldown_s"); sa.ReachM = Num(a, "reach_m");
                s.Attacks.Add(sa);
            }
            var w = Obj(root, "water");
            s.Water = new SpecWater { RhoW = Num(w, "rho_w_kg_m3"), Floats = Num(w, "floats"), DraftFraction = Num(w, "draft_fraction"), BellyClearanceM = Num(w, "belly_clearance_m") };
            var so = Obj(root, "soil");
            s.Soil = new SpecSoil { StaticPressurePa = Num(so, "static_pressure_Pa"), PeakPressurePa = Num(so, "peak_pressure_Pa"), SoilUltimatePa = Num(so, "soil_ultimate_Pa"), Sinks = Num(so, "sinks") };
            var p = Obj(root, "pack");
            s.Pack = new SpecPack
            {
                Anchor = Num(p, "role_affinity_anchor"), Flanker = Num(p, "role_affinity_flanker"), Bait = Num(p, "role_affinity_bait"), Raider = Num(p, "role_affinity_raider"),
                RangeMinM = Num(p, "preferred_range_min_m"), RangeMaxM = Num(p, "preferred_range_max_m"), MaxPackSizeHint = Num(p, "max_pack_size_hint"),
            };
            var rh = Obj(root, "render_hints");
            s.RenderHints = new SpecRenderHints { SilhouetteId = Str(rh, "silhouette_id"), PaletteId = Str(rh, "palette_id"), PxFeatures = Strs(rh, "px_features") };
            var pv = Obj(root, "provenance");
            s.Provenance = new SpecProvenance { Citations = Strs(pv, "citations"), AssumedFields = Strs(pv, "assumed_fields"), DerivationVersion = Str(pv, "derivation_version") };
            return s;
        }

        public static KaijuTissue ParseTissue(string json)
        {
            var root = Root(json, KaijuContractVersions.TissueSchema);
            return new KaijuTissue
            {
                BoneYieldPa = Num(root, "bone_yield_Pa"), MuscleIsometricPa = Num(root, "muscle_isometric_Pa"),
                RealBoneYieldPa = Num(root, "real_bone_yield_Pa"), RealMuscleIsometricPa = Num(root, "real_muscle_isometric_Pa"), AssumedFields = Strs(root, "assumed_fields"),
            };
        }

        static JsonValue Root(string json, string schema)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("kaiju spec: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("kaiju spec: root must be an object");
            var sc = root.Get("schema");
            if (sc == null || sc.Kind != JsonKind.String || sc.Text != schema) throw new FormatException("kaiju spec: schema must be " + schema);
            return root;
        }

        static JsonValue Obj(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Object) throw new FormatException("kaiju spec: missing object " + key);
            return v;
        }

        static string Str(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.String) throw new FormatException("kaiju spec: missing string " + key);
            return v.Text;
        }

        static string[] Strs(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Array) throw new FormatException("kaiju spec: missing array " + key);
            var r = new string[v.Items.Count];
            for (int i = 0; i < r.Length; i++)
            {
                if (v.Items[i].Kind != JsonKind.String) throw new FormatException("kaiju spec: non-string in " + key);
                r[i] = v.Items[i].Text;
            }
            return r;
        }

        static SpecNumber Num(JsonValue o, string key)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Object) throw new FormatException("kaiju spec: missing number " + key);
            var val = v.Get("value");
            double d;
            if (val == null || !val.TryGetDouble(out d) || double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("kaiju spec: bad value in " + key);
            var st = v.Get("status"); var src = v.Get("source"); var un = v.Get("unit");
            if (st == null || st.Kind != JsonKind.String || !Enum.TryParse(st.Text, false, out SpecStatus status)) throw new FormatException("kaiju spec: bad status in " + key);
            if (src == null || src.Kind != JsonKind.String || src.Text.Length == 0) throw new FormatException("kaiju spec: missing source in " + key);
            if (un == null || un.Kind != JsonKind.String) throw new FormatException("kaiju spec: missing unit in " + key);
            return new SpecNumber { Value = d, Unit = un.Text, Source = src.Text, Status = status };
        }

        static Ellipsoid Ell(JsonValue o, string key)
        {
            var e = Obj(o, key);
            return new Ellipsoid { A = Num(e, "a"), B = Num(e, "b"), C = Num(e, "c") };
        }

        static Frustum Fru(JsonValue o, string key, string r0, string r1)
        {
            var e = Obj(o, key);
            return new Frustum { L = Num(e, "L"), R0 = Num(e, r0), R1 = Num(e, r1) };
        }
    }

    public sealed class KaijuSpeciesCatalog : IKaijuSpeciesCatalog
    {
        readonly List<KaijuSpeciesSpec> _all;
        public KaijuSpeciesCatalog(IEnumerable<KaijuSpeciesSpec> specs, KaijuTissue tissue)
        {
            _all = new List<KaijuSpeciesSpec>(specs);
            Tissue = tissue;
            for (int i = 0; i < _all.Count; i++)
                for (int j = i + 1; j < _all.Count; j++)
                    if (_all[i].Id == _all[j].Id) throw new FormatException("kaiju catalog: duplicate species " + _all[i].Id);
        }
        public IReadOnlyList<KaijuSpeciesSpec> All => _all;
        public KaijuTissue Tissue { get; }
        public bool TryGet(string id, out KaijuSpeciesSpec spec)
        {
            foreach (var s in _all) if (s.Id == id) { spec = s; return true; }
            spec = null; return false;
        }
    }

    public static class KaijuModule
    {
        /// <summary>Returns the catalog it registered, or null when units.kaiju.species is off (nothing registered).</summary>
        public static KaijuSpeciesCatalog Install(IServiceRegistry registry, IFeatureFlags flags, IEnumerable<KaijuSpeciesSpec> specs, KaijuTissue tissue)
        {
            if (registry == null || flags == null || specs == null || tissue == null || !flags.IsOn(KaijuFlags.Species)) return null;
            var c = new KaijuSpeciesCatalog(specs, tissue);
            registry.Register<IKaijuSpeciesCatalog>(c);
            return c;
        }
    }
}
