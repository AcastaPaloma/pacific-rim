using System;

namespace Game.AI.Policy
{
    /// <summary>
    /// Flattens a <see cref="BrainObservation"/> into the fixed ego-centric vector of <see cref="PolicyLayout"/> (R-028 section 2).
    /// Slots are ordered by (usable/alive first, distance, EntityId), so the encoding does not depend on the order of the input
    /// arrays nor on the absolute EntityId values (ids only break exact distance ties). Ego frame: x forward along the
    /// self heading, y to the left; lengths divided by SightRangeM. No allocation after construction; not thread-safe.
    /// Only observation fields reach the vector, so the policy sees exactly what the scripted brain sees (C4 fairness).
    /// </summary>
    public sealed class ObservationEncoder
    {
        /// <summary>Slot -> EntityId of the target in that slot (0 = empty). Valid after <see cref="Encode"/>.</summary>
        public readonly ulong[] TargetSlotIds = new ulong[PolicyLayout.NT];
        /// <summary>Slot -> index into obs.Targets (-1 = empty).</summary>
        public readonly int[] TargetSlotSrc = new int[PolicyLayout.NT];
        /// <summary>Slot -> usable (alive and visible or remembered within MemorySeconds).</summary>
        public readonly bool[] TargetSlotUsable = new bool[PolicyLayout.NT];
        /// <summary>Ally slot -> index into obs.Allies (-1 = empty).</summary>
        public readonly int[] AllySlotSrc = new int[PolicyLayout.NA];

        private readonly float[] _keyT = new float[PolicyLayout.NT];
        private readonly float[] _keyA = new float[PolicyLayout.NA];

        public static bool Usable(in TargetObs t, in BrainConfig cfg) => t.Alive && (t.Visible || t.MemoryAge <= cfg.MemorySeconds);

        /// <summary>Slot of target id (or -1). Valid after <see cref="Encode"/>.</summary>
        public int SlotOf(ulong id)
        {
            if (id == 0) return -1;
            for (int s = 0; s < PolicyLayout.NT; s++) if (TargetSlotIds[s] == id) return s;
            return -1;
        }

        public void Encode(in BrainObservation obs, in BrainConfig cfg, in PolicyConfig pcfg, in PolicyMemory mem, float[] dst, byte[] mask)
        {
            if (dst == null || dst.Length < PolicyLayout.ObsDim) throw new ArgumentException("dst too small");
            if (mask == null || mask.Length < PolicyLayout.MaskDim) throw new ArgumentException("mask too small");
            Array.Clear(dst, 0, PolicyLayout.ObsDim);
            Array.Clear(mask, 0, PolicyLayout.MaskDim);

            SelfObs self = obs.Self;
            float rs = cfg.SightRangeM > 1f ? cfg.SightRangeM : 1f, inv = 1f / rs, clampE = pcfg.EgoClamp;
            float ch = MathF.Cos(self.HeadingRad), sh = MathF.Sin(self.HeadingRad);

            // ---- target slots: sort indices by (unusable, distance, id), insertion sort over <= 8 entries ----
            int nt = obs.TargetCount < PolicyLayout.NT ? obs.TargetCount : PolicyLayout.NT;
            for (int s = 0; s < PolicyLayout.NT; s++) { TargetSlotIds[s] = 0; TargetSlotSrc[s] = -1; TargetSlotUsable[s] = false; }
            for (int i = 0; i < nt; i++)
            {
                ref readonly TargetObs t = ref obs.Targets[i];
                float key = PolicyMath.Dist(self.Pos, t.Pos) + (Usable(t, cfg) ? 0f : 1e7f);
                int j = i;
                while (j > 0 && Less(key, t.Id.Value, _keyT[j - 1], obs.Targets[TargetSlotSrc[j - 1]].Id.Value))
                {
                    _keyT[j] = _keyT[j - 1]; TargetSlotSrc[j] = TargetSlotSrc[j - 1]; j--;
                }
                _keyT[j] = key; TargetSlotSrc[j] = i;
            }
            for (int s = 0; s < nt; s++)
            {
                ref readonly TargetObs t = ref obs.Targets[TargetSlotSrc[s]];
                TargetSlotIds[s] = t.Id.Value; TargetSlotUsable[s] = Usable(t, cfg);
            }

            // ---- ally slots: sort by (dead, distance, id); only allies inside AllyAwarenessM ----
            int na = 0;
            for (int s = 0; s < PolicyLayout.NA; s++) AllySlotSrc[s] = -1;
            int liveAllies = 0, charging = 0; float cx = 0f, cy = 0f;
            for (int i = 0; i < obs.AllyCount && i < PolicyLayout.NA; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                float d = PolicyMath.Dist(self.Pos, a.Pos);
                if (d > cfg.AllyAwarenessM) continue;
                if (a.Alive)
                {
                    liveAllies++; cx += a.Pos.X - self.Pos.X; cy += a.Pos.Y - self.Pos.Y;
                    if (a.State == (byte)BrainState.Charge) charging++;
                }
                float key = d + (a.Alive ? 0f : 1e7f);
                int j = na;
                while (j > 0 && Less(key, a.Id.Value, _keyA[j - 1], obs.Allies[AllySlotSrc[j - 1]].Id.Value))
                {
                    _keyA[j] = _keyA[j - 1]; AllySlotSrc[j] = AllySlotSrc[j - 1]; j--;
                }
                _keyA[j] = key; AllySlotSrc[j] = i; na++;
            }

            // ---- self block ----
            int usableCount = 0;
            for (int s = 0; s < nt; s++) if (TargetSlotUsable[s]) usableCount++;
            dst[0] = PolicyMath.Clamp(self.HpFrac, 0f, 1f);
            dst[1] = cfg.CruiseSpeedMps > 0f ? PolicyMath.Clamp(self.Speed / cfg.CruiseSpeedMps, 0f, 2f) : 0f;
            dst[2] = pcfg.CooldownNormSeconds > 0f ? PolicyMath.Clamp(self.Cooldowns / pcfg.CooldownNormSeconds, 0f, 1f) : 0f;
            double since = obs.TimeSeconds - mem.LastSwitchTime;
            dst[3] = cfg.RetargetMinSeconds > 0f ? (float)Math.Min(since / cfg.RetargetMinSeconds, 1.0) : 1f;
            dst[4] = liveAllies / (float)PolicyLayout.NA;
            if (liveAllies > 0)
            {
                float mx = cx / liveAllies, my = cy / liveAllies;
                dst[5] = PolicyMath.Clamp((ch * mx + sh * my) * inv, -clampE, clampE);
                dst[6] = PolicyMath.Clamp((-sh * mx + ch * my) * inv, -clampE, clampE);
                dst[7] = charging / (float)liveAllies;
            }
            dst[8] = usableCount / (float)PolicyLayout.NT;
            int curSlot = SlotOf(mem.CurrentTargetId);
            dst[9 + (curSlot >= 0 ? 1 + curSlot : 0)] = 1f;
            int lm = mem.LastManeuver; if (lm < 0 || lm >= PolicyLayout.NManeuvers) lm = 0;
            dst[18 + lm] = 1f;

            // ---- target block ----
            for (int s = 0; s < nt; s++)
            {
                ref readonly TargetObs t = ref obs.Targets[TargetSlotSrc[s]];
                int o = PolicyLayout.TargetsOffset + s * PolicyLayout.TargetDim;
                float dx = t.Pos.X - self.Pos.X, dy = t.Pos.Y - self.Pos.Y, d = MathF.Sqrt(dx * dx + dy * dy);
                float rel = PolicyMath.Wrap(t.HeadingRad - PolicyMath.Dir(t.Pos, self.Pos));
                int on = 0;
                for (int k = 0; k < na; k++)
                {
                    ref readonly AllyObs a = ref obs.Allies[AllySlotSrc[k]];
                    if (a.Alive && a.TargetId.Value == t.Id.Value) on++;
                }
                dst[o + 0] = 1f;
                dst[o + 1] = TargetSlotUsable[s] ? 1f : 0f;
                dst[o + 2] = t.Visible ? 1f : 0f;
                dst[o + 3] = cfg.MemorySeconds > 0f ? PolicyMath.Clamp(t.MemoryAge / cfg.MemorySeconds, 0f, 1f) : 0f;
                dst[o + 4] = PolicyMath.Clamp((ch * dx + sh * dy) * inv, -clampE, clampE);
                dst[o + 5] = PolicyMath.Clamp((-sh * dx + ch * dy) * inv, -clampE, clampE);
                dst[o + 6] = PolicyMath.Clamp(d * inv, 0f, clampE);
                dst[o + 7] = PolicyMath.Clamp(t.HpFrac, 0f, 1f);
                dst[o + 8] = MathF.Sin(rel);
                dst[o + 9] = MathF.Cos(rel);
                dst[o + 10] = MathF.Abs(rel) <= cfg.FacingHalfAngleRad ? 1f : 0f;
                dst[o + 11] = on / (float)(liveAllies > 1 ? liveAllies : 1);
                dst[o + 12] = t.Id.Value == mem.CurrentTargetId && mem.CurrentTargetId != 0 ? 1f : 0f;
                dst[o + 13] = d <= cfg.MeleeReachM ? 1f : 0f;
                dst[o + 14] = d <= cfg.StandoffM ? 1f : 0f;
            }

            // ---- ally block ----
            for (int s = 0; s < na; s++)
            {
                ref readonly AllyObs a = ref obs.Allies[AllySlotSrc[s]];
                int o = PolicyLayout.AlliesOffset + s * PolicyLayout.AllyDim;
                float dx = a.Pos.X - self.Pos.X, dy = a.Pos.Y - self.Pos.Y, d = MathF.Sqrt(dx * dx + dy * dy);
                float rh = PolicyMath.Wrap(a.HeadingRad - self.HeadingRad);
                dst[o + 0] = 1f;
                dst[o + 1] = a.Alive ? 1f : 0f;
                dst[o + 2] = PolicyMath.Clamp((ch * dx + sh * dy) * inv, -clampE, clampE);
                dst[o + 3] = PolicyMath.Clamp((-sh * dx + ch * dy) * inv, -clampE, clampE);
                dst[o + 4] = PolicyMath.Clamp(d * inv, 0f, clampE);
                dst[o + 5] = MathF.Sin(rh);
                dst[o + 6] = MathF.Cos(rh);
                dst[o + 7] = PolicyMath.Clamp(a.HpFrac, 0f, 1f);
                int st = a.State < PolicyLayout.StateOneHot ? a.State : 0;
                dst[o + 8 + st] = 1f;
                ulong at = a.TargetId.Value;
                if (at != 0)
                {
                    int ts = SlotOf(at);
                    if (ts >= 0) dst[o + 14 + ts] = 1f; else dst[o + 22] = 1f;
                    if (at == mem.CurrentTargetId) dst[o + 23] = 1f;
                }
            }

            // ---- action mask (base; the target -> maneuver condition is PolicyLayout.ManeuverAllowed) ----
            mask[PolicyLayout.MaskTargetOffset] = 1;
            for (int s = 0; s < PolicyLayout.NT; s++) mask[PolicyLayout.MaskTargetOffset + 1 + s] = (byte)(TargetSlotUsable[s] ? 1 : 0);
            bool any = usableCount > 0;
            for (int m = 0; m < PolicyLayout.NManeuvers; m++)
                mask[PolicyLayout.MaskManeuverOffset + m] = (byte)(any || !PolicyLayout.NeedsTarget((Maneuver)m) ? 1 : 0);
            for (int sp = 0; sp < PolicyLayout.NSpeeds; sp++) mask[PolicyLayout.MaskSpeedOffset + sp] = 1;
        }

        private static bool Less(float ka, ulong ida, float kb, ulong idb) => ka < kb || (ka == kb && ida < idb);
    }
}
