using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Meta.Refit
{
    /// <summary>parts@v1: the part catalogue (R-104 section 10.4). Parsed from JSON text and validated up front (FormatException); data, not code.</summary>
    public sealed class PartCatalog : IPartCatalog
    {
        public const int MaxTorqueBonusBps = 1500;   // R-104 section 5.4: actuator fibre capped at +15%
        static readonly string[] Tiers = { "basic", "standard", "advanced", "rare" };

        readonly SortedDictionary<string, PartDef> _byId = new SortedDictionary<string, PartDef>(StringComparer.Ordinal);
        readonly List<PartDef> _all = new List<PartDef>();
        readonly List<string> _classes = new List<string>();
        readonly List<string> _slotTypes = new List<string>();

        public IReadOnlyList<PartDef> All => _all;
        public IReadOnlyList<string> PriceClasses => _classes;
        public IReadOnlyList<string> SlotTypes => _slotTypes;
        public bool HasSlotType(string t) => _slotTypes.Contains(t);
        public PartDef Get(string partId) => partId != null && _byId.TryGetValue(partId, out var p) ? p : null;

        public static PartCatalog Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("parts: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("parts: root must be an object");
            if (RefitJson.Str(root, "schema", "parts") != RefitContractVersions.PartsSchema) throw new FormatException("parts: schema must be " + RefitContractVersions.PartsSchema);
            var c = new PartCatalog();
            foreach (var v in RefitJson.Items(root, "classes", "parts"))
            {
                if (v.Kind != JsonKind.String || v.Text.Length == 0 || c._classes.Contains(v.Text)) throw new FormatException("parts: bad or duplicate class");
                c._classes.Add(v.Text);
            }
            foreach (var v in RefitJson.Items(root, "slot_types", "parts"))
            {
                if (v.Kind != JsonKind.String || v.Text.Length == 0 || c._slotTypes.Contains(v.Text)) throw new FormatException("parts: bad or duplicate slot type");
                c._slotTypes.Add(v.Text);
            }
            foreach (var o in RefitJson.Items(root, "parts", "parts"))
            {
                var p = new PartDef
                {
                    Id = RefitJson.Str(o, "id", "parts"), SlotType = RefitJson.Str(o, "slot_type", "parts"), Tier = RefitJson.Str(o, "tier", "parts"),
                    RarePart = RefitJson.Str(o, "rare_part", "parts"), Source = RefitJson.Str(o, "source", "parts"), Assumed = RefitJson.Bool(o, "assumed", "parts"),
                    Note = RefitJson.Str(o, "note", "parts"),
                };
                string at = "parts: part '" + p.Id + "': ";
                if (p.Id.Length == 0 || c._byId.ContainsKey(p.Id)) throw new FormatException("parts: empty or duplicate part id '" + p.Id + "'");
                if (!c._slotTypes.Contains(p.SlotType)) throw new FormatException(at + "unknown slot type '" + p.SlotType + "'");
                if (Array.IndexOf(Tiers, p.Tier) < 0) throw new FormatException(at + "unknown tier '" + p.Tier + "'");
                if ((p.Tier == "rare") != (p.RarePart.Length > 0)) throw new FormatException(at + "tier rare exactly when a rare part is named");
                var ch = RefitJson.Items(o, "chassis", "parts");
                var list = new List<string>();
                foreach (var cv in ch) { if (cv.Kind != JsonKind.String || cv.Text.Length == 0 || list.Contains(cv.Text)) throw new FormatException(at + "bad chassis list"); list.Add(cv.Text); }
                p.Chassis = list.ToArray();
                var comp = o.Get("composition_kg");
                if (comp == null || comp.Kind != JsonKind.Object || comp.Members.Count == 0) throw new FormatException(at + "composition_kg missing");
                p.CompositionKg = new SortedDictionary<string, long>(StringComparer.Ordinal);
                long mass = 0;
                foreach (var m in comp.Members)
                {
                    if (!c._classes.Contains(m.Key)) throw new FormatException(at + "unknown class '" + m.Key + "'");
                    if (!m.Value.TryGetInt64(out long kg) || kg <= 0) throw new FormatException(at + "bad mass for class " + m.Key);
                    p.CompositionKg[m.Key] = kg; mass = checked(mass + kg);
                }
                p.MassKg = mass;
                var size = RefitJson.Vec3(o, "size_m", "parts");
                if (size[0] <= 0 || size[1] <= 0 || size[2] <= 0) throw new FormatException(at + "size must be positive");
                p.SizeX = size[0]; p.SizeY = size[1]; p.SizeZ = size[2];
                var off = RefitJson.Vec3(o, "com_offset_m", "parts");
                p.ComX = off[0]; p.ComY = off[1]; p.ComZ = off[2];
                p.PowerPeakW = RefitJson.Num(o, "power_peak_w", "parts"); p.PowerPulse = RefitJson.Bool(o, "power_pulse", "parts");
                p.HeatW = RefitJson.Num(o, "heat_w", "parts"); p.HeatPeakW = RefitJson.Num(o, "heat_peak_w", "parts");
                if (p.PowerPeakW < 0 || p.HeatW < 0 || p.HeatPeakW < p.HeatW) throw new FormatException(at + "power and heat must be >= 0 and peak heat >= sustained heat");
                p.RadiatorKg = RefitJson.Int(o, "radiator_kg", "parts"); p.CoolantKg = RefitJson.Int(o, "coolant_kg", "parts");
                if (p.RadiatorKg < 0 || p.CoolantKg < 0 || p.RadiatorKg + p.CoolantKg > p.MassKg) throw new FormatException(at + "radiator and coolant mass must fit inside the part");
                long bonus = RefitJson.Int(o, "torque_bonus_bps", "parts");
                if (bonus < 0 || bonus > MaxTorqueBonusBps) throw new FormatException(at + "torque bonus must be 0.." + MaxTorqueBonusBps + " bps (+15% cap)");
                if (bonus > 0 && !p.CompositionKg.ContainsKey("drive")) throw new FormatException(at + "a torque bonus needs drive-class mass");
                p.TorqueBonusBps = (int)bonus;
                c._byId.Add(p.Id, p);
            }
            c._all.AddRange(c._byId.Values);
            return c;
        }
    }

    /// <summary>chassis@v1: slot geometry and limits per chassis class, cross-checked against the catalogue (R-104 section 9).</summary>
    public sealed class ChassisCatalog : IChassisCatalog
    {
        const double G = 9.81;
        readonly SortedDictionary<string, ChassisDef> _byId = new SortedDictionary<string, ChassisDef>(StringComparer.Ordinal);
        readonly List<ChassisDef> _all = new List<ChassisDef>();
        public IReadOnlyList<ChassisDef> All => _all;
        public ChassisDef Get(string chassisId) => chassisId != null && _byId.TryGetValue(chassisId, out var c) ? c : null;

        public static ChassisCatalog Parse(string json, PartCatalog parts)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            JsonValue root;
            try { root = Json.Parse(json); } catch (JsonParseException e) { throw new FormatException("chassis: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw new FormatException("chassis: root must be an object");
            if (RefitJson.Str(root, "schema", "chassis") != RefitContractVersions.ChassisSchema) throw new FormatException("chassis: schema must be " + RefitContractVersions.ChassisSchema);
            var cat = new ChassisCatalog();
            foreach (var o in RefitJson.Items(root, "chassis", "chassis"))
            {
                var ch = new ChassisDef { Id = RefitJson.Str(o, "id", "chassis"), Label = RefitJson.Str(o, "label", "chassis") };
                string at = "chassis: '" + ch.Id + "': ";
                if (ch.Id.Length == 0 || cat._byId.ContainsKey(ch.Id)) throw new FormatException("chassis: empty or duplicate id '" + ch.Id + "'");
                ch.MassCapKg = RefitJson.Int(o, "mass_cap_kg", "chassis");
                ch.FootAreaM2 = RefitJson.Num(o, "foot_area_m2", "chassis"); ch.GroundPressurePa = RefitJson.Num(o, "ground_pressure_pa", "chassis");
                ch.StanceHalfWidthM = RefitJson.Num(o, "stance_half_width_m", "chassis"); ch.AlphaRadS2 = RefitJson.Num(o, "alpha_rad_s2", "chassis");
                ch.FrameHeightM = RefitJson.Num(o, "frame_height_m", "chassis");
                var fr = RefitJson.Vec2(o, "com_height_frac", "chassis"); ch.ComHeightLoFrac = fr[0]; ch.ComHeightHiFrac = fr[1];
                ch.ComLateralM = RefitJson.Num(o, "com_lateral_m", "chassis");
                var fa = RefitJson.Vec2(o, "com_fore_aft_m", "chassis"); ch.ComForeAftLoM = fa[0]; ch.ComForeAftHiM = fa[1];
                ch.TiltMinDeg = RefitJson.Num(o, "tilt_min_deg", "chassis");
                ch.ReactorWPerKg = RefitJson.Num(o, "reactor_w_per_kg", "chassis"); ch.BufferJ = RefitJson.Num(o, "buffer_j", "chassis");
                ch.BufferWindowS = RefitJson.Num(o, "buffer_window_s", "chassis"); ch.Cot = RefitJson.Num(o, "cot", "chassis"); ch.WalkSpeedMs = RefitJson.Num(o, "walk_speed_ms", "chassis");
                ch.PassiveHeatW = RefitJson.Num(o, "passive_heat_w", "chassis"); ch.RadiatorWPerKg = RefitJson.Num(o, "radiator_w_per_kg", "chassis");
                ch.CoolantJPerKgK = RefitJson.Num(o, "coolant_j_per_kg_k", "chassis"); ch.CoolantDeltaK = RefitJson.Num(o, "coolant_delta_k", "chassis");
                ch.BurstS = RefitJson.Num(o, "burst_s", "chassis"); ch.TorqueNmPerKg = RefitJson.Num(o, "torque_nm_per_kg", "chassis"); ch.TorqueDerate = RefitJson.Num(o, "torque_derate", "chassis");
                if (ch.MassCapKg <= 0 || ch.FootAreaM2 <= 0 || ch.GroundPressurePa <= 0 || ch.StanceHalfWidthM <= 0 || ch.AlphaRadS2 <= 0 || ch.FrameHeightM <= 0
                    || ch.ComHeightLoFrac <= 0 || ch.ComHeightHiFrac <= ch.ComHeightLoFrac || ch.ComLateralM < 0 || ch.ComForeAftHiM < ch.ComForeAftLoM || ch.TiltMinDeg < 0
                    || ch.ReactorWPerKg <= 0 || ch.BufferJ < 0 || ch.BufferWindowS <= 0 || ch.Cot <= 0 || ch.WalkSpeedMs < 0 || ch.PassiveHeatW < 0 || ch.RadiatorWPerKg <= 0
                    || ch.CoolantJPerKgK <= 0 || ch.CoolantDeltaK <= 0 || ch.BurstS <= 0 || ch.TorqueNmPerKg <= 0 || ch.TorqueDerate <= 0 || ch.TorqueDerate > 1)
                    throw new FormatException(at + "a limit is out of range");
                // R-104 section 9: total mass <= p_g A_foot / g (the mass cap is data and may be tighter, never looser)
                if (ch.MassCapKg > ch.GroundPressurePa * ch.FootAreaM2 / G) throw new FormatException(at + "mass cap exceeds the ground-pressure limit p A / g");

                var slotArr = RefitJson.Items(o, "slots", "chassis");
                var slots = new List<SlotDef>();
                foreach (var so in slotArr)
                {
                    var s = new SlotDef { Name = RefitJson.Str(so, "name", "chassis"), Type = RefitJson.Str(so, "type", "chassis"), Parent = RefitJson.Str(so, "parent", "chassis") };
                    if (s.Name.Length == 0 || SlotIndexOf(slots, s.Name) >= 0) throw new FormatException(at + "empty or duplicate slot '" + s.Name + "'");
                    if (!parts.HasSlotType(s.Type)) throw new FormatException(at + "slot " + s.Name + " has unknown type " + s.Type);
                    if (s.Parent.Length > 0) { s.ParentIndex = SlotIndexOf(slots, s.Parent); if (s.ParentIndex < 0) throw new FormatException(at + "slot " + s.Name + " parent must appear earlier"); }
                    else if (slots.Count > 0) throw new FormatException(at + "only the first slot may be the root");
                    var a = RefitJson.Vec3(so, "anchor", "chassis"); s.AnchorX = a[0]; s.AnchorY = a[1]; s.AnchorZ = a[2];
                    s.Mirror = RefitJson.Bool(so, "mirror", "chassis"); s.Actuated = RefitJson.Bool(so, "actuated", "chassis"); s.MovesSelf = RefitJson.Bool(so, "moves_self", "chassis");
                    var pv = so.Get("pivot");
                    if (s.Actuated)
                    {
                        var p3 = RefitJson.Vec3(so, "pivot", "chassis"); s.PivotX = p3[0]; s.PivotY = p3[1]; s.PivotZ = p3[2];
                    }
                    else if (pv != null && pv.Kind != JsonKind.Null) throw new FormatException(at + "slot " + s.Name + " has a pivot but no actuator");
                    slots.Add(s);
                }
                if (slots.Count == 0) throw new FormatException(at + "no slots");
                ch.Slots = slots.ToArray();
                ch.Moved = new int[ch.Slots.Length][];
                for (int i = 0; i < ch.Slots.Length; i++)
                {
                    var moved = new List<int>();
                    if (ch.Slots[i].Actuated)
                    {
                        for (int k = 0; k < ch.Slots.Length; k++)
                        {
                            if (k == i) continue;
                            for (int a = ch.Slots[k].ParentIndex; a >= 0; a = ch.Slots[a].ParentIndex) if (a == i) { moved.Add(k); break; }
                        }
                        if (ch.Slots[i].MovesSelf) moved.Add(i);
                        if (moved.Count == 0) throw new FormatException(at + "actuated slot " + ch.Slots[i].Name + " moves nothing");
                    }
                    ch.Moved[i] = moved.ToArray();
                }
                ch.Limbs = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
                var limbs = o.Get("limbs");
                if (limbs == null || limbs.Kind != JsonKind.Object) throw new FormatException(at + "limbs missing");
                foreach (var lm in limbs.Members)
                {
                    if (lm.Value.Kind != JsonKind.Array || lm.Value.Items.Count == 0) throw new FormatException(at + "limb " + lm.Key + " must be a non-empty array");
                    var idx = new List<int>();
                    foreach (var nm in lm.Value.Items)
                    {
                        int k = nm.Kind == JsonKind.String ? ch.SlotIndex(nm.Text) : -1;
                        if (k < 0 || idx.Contains(k)) throw new FormatException(at + "limb " + lm.Key + " names an unknown or repeated slot");
                        idx.Add(k);
                    }
                    if (idx.Count == ch.Slots.Length) throw new FormatException(at + "a limb cannot be the whole mech");
                    ch.Limbs.Add(lm.Key, idx.ToArray());
                }
                var db = o.Get("default_build");
                if (db == null || db.Kind != JsonKind.Object || db.Members.Count != ch.Slots.Length) throw new FormatException(at + "default_build must name every slot");
                ch.DefaultBuild = new string[ch.Slots.Length];
                for (int i = 0; i < ch.Slots.Length; i++)
                {
                    var pv = db.Get(ch.Slots[i].Name);
                    if (pv == null || pv.Kind != JsonKind.String) throw new FormatException(at + "default_build lacks slot " + ch.Slots[i].Name);
                    var part = parts.Get(pv.Text);
                    if (part == null || part.SlotType != ch.Slots[i].Type || Array.IndexOf(part.Chassis, ch.Id) < 0 || part.IsRare)
                        throw new FormatException(at + "default part " + pv.Text + " does not fit slot " + ch.Slots[i].Name);
                    ch.DefaultBuild[i] = part.Id;
                }
                cat._byId.Add(ch.Id, ch);
            }
            cat._all.AddRange(cat._byId.Values);
            // a part may name only chassis that exist
            foreach (var p in parts.All) foreach (var cid in p.Chassis) if (!cat._byId.ContainsKey(cid)) throw new FormatException("chassis: part " + p.Id + " names unknown chassis " + cid);
            return cat;
        }

        static int SlotIndexOf(List<SlotDef> slots, string name)
        {
            for (int i = 0; i < slots.Count; i++) if (string.Equals(slots[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }
    }

    internal static class RefitJson
    {
        public static IReadOnlyList<JsonValue> Items(JsonValue o, string key, string ctx)
        {
            var a = o.Get(key);
            if (a == null || a.Kind != JsonKind.Array || a.Items.Count == 0) throw new FormatException(ctx + ": '" + key + "' must be a non-empty array");
            return a.Items;
        }
        public static string Str(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.String) throw new FormatException(ctx + ": missing string '" + key + "'");
            return v.Text;
        }
        public static long Int(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || !v.TryGetInt64(out long n)) throw new FormatException(ctx + ": missing integer '" + key + "'");
            return n;
        }
        public static double Num(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || !v.TryGetDouble(out double d) || double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException(ctx + ": missing finite number '" + key + "'");
            return d;
        }
        public static bool Bool(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Bool) throw new FormatException(ctx + ": missing bool '" + key + "'");
            return v.Bool;
        }
        public static double[] Vec3(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Array || v.Items.Count != 3) throw new FormatException(ctx + ": '" + key + "' must be [x, y, z]");
            var r = new double[3];
            for (int i = 0; i < 3; i++) if (!v.Items[i].TryGetDouble(out r[i]) || double.IsNaN(r[i]) || double.IsInfinity(r[i])) throw new FormatException(ctx + ": '" + key + "' has a non-finite entry");
            return r;
        }
        public static double[] Vec2(JsonValue o, string key, string ctx)
        {
            var v = o.Get(key);
            if (v == null || v.Kind != JsonKind.Array || v.Items.Count != 2) throw new FormatException(ctx + ": '" + key + "' must be [lo, hi]");
            var r = new double[2];
            for (int i = 0; i < 2; i++) if (!v.Items[i].TryGetDouble(out r[i]) || double.IsNaN(r[i]) || double.IsInfinity(r[i])) throw new FormatException(ctx + ": '" + key + "' has a non-finite entry");
            return r;
        }
    }
}
