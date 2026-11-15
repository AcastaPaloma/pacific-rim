using System;
using Game.Core;

namespace Game.AI.Policy
{
    /// <summary>
    /// Turns a factored <see cref="PolicyAction"/> into a <see cref="BrainIntent"/> (R-028 section 3). Deterministic geometry only:
    /// the network chooses whom and which primitive, the decoder computes heading / speed. Invalid choices are sanitised
    /// (unusable target -> none; target-needing maneuver without a target -> Hold), so a policy can never target an entity
    /// that is neither visible nor remembered (M-007 C4 by construction). The reason code is derived from facts, never from
    /// the network, so C3 (switching) measures the policy honestly.
    /// </summary>
    public static class ActionDecoder
    {
        public static void Decode(in PolicyAction a, in BrainObservation obs, in BrainConfig cfg, in PolicyConfig pcfg,
                                  ObservationEncoder enc, ref PolicyMemory mem, ref BrainIntent intent)
        {
            SelfObs self = obs.Self;
            double now = obs.TimeSeconds;

            // ---- resolve target ----
            int slot = a.Target - 1;
            int src = -1; ulong tid = 0;
            if (slot >= 0 && slot < PolicyLayout.NT && enc.TargetSlotUsable[slot]) { src = enc.TargetSlotSrc[slot]; tid = enc.TargetSlotIds[slot]; }
            Maneuver m = a.Maneuver >= 0 && a.Maneuver < PolicyLayout.NManeuvers ? (Maneuver)a.Maneuver : Maneuver.Hold;

            ulong prev = mem.CurrentTargetId;
            int prevSlot = enc.SlotOf(prev);
            bool prevUsable = prevSlot >= 0 && enc.TargetSlotUsable[prevSlot];
            // no silent drop: releasing a usable target to "none" is allowed only when moving away (Retreat / Regroup)
            if (tid == 0 && prevUsable && m != Maneuver.Retreat && m != Maneuver.Regroup)
            {
                tid = prev; src = enc.TargetSlotSrc[prevSlot];
            }
            if (tid == 0 && PolicyLayout.NeedsTarget(m)) m = Maneuver.Hold;

            // ---- reason (facts only) ----
            ReasonCode reason = ReasonCode.None;
            if (tid != prev)
            {
                if (tid != 0 && prev != 0) reason = prevUsable ? ReasonCode.RetargetVoluntary : ReasonCode.RetargetForced;
                else if (tid != 0) // from none: a re-pick of a different target soon after a voluntary drop is still a voluntary switch
                    reason = mem.LastDroppedId != 0 && mem.LastDroppedId != tid && now - mem.LastDropTime < cfg.RetargetMinSeconds
                        ? ReasonCode.RetargetVoluntary : ReasonCode.TargetAcquired;
                else if (prevUsable) { mem.LastDroppedId = prev; mem.LastDropTime = now; }
                else reason = ReasonCode.TargetLost;
                if (tid != 0) mem.LastSwitchTime = now;
            }
            else if (m == Maneuver.Retreat && self.HpFrac < cfg.RetreatHpFrac && mem.LastManeuver != (int)Maneuver.Retreat) reason = ReasonCode.LowHp;

            // ---- intent ----
            float speed = pcfg.Tier(a.Speed);
            Float2 tp = src >= 0 ? obs.Targets[src].Pos : self.Pos;
            float d = src >= 0 ? PolicyMath.Dist(self.Pos, tp) : 0f;
            intent.Action = BrainAction.None;
            intent.TargetId = tid != 0 ? EntityId.FromValue(tid) : default;
            switch (m)
            {
                case Maneuver.Approach:
                    intent.DesiredHeadingRad = PolicyMath.Dir(self.Pos, tp);
                    intent.SpeedFrac = d <= cfg.StandoffM ? 0f : speed;
                    intent.State = BrainState.Approach;
                    break;
                case Maneuver.FlankLeft:
                case Maneuver.FlankRight:
                {
                    float side = m == Maneuver.FlankLeft ? 1f : -1f;
                    float phi = PolicyMath.Dir(tp, self.Pos) + side * pcfg.FlankStepRad;
                    Float2 w = new Float2(tp.X + cfg.StandoffM * MathF.Cos(phi), tp.Y + cfg.StandoffM * MathF.Sin(phi));
                    intent.DesiredHeadingRad = PolicyMath.Dir(self.Pos, w);
                    intent.SpeedFrac = speed;
                    intent.State = BrainState.Flank;
                    break;
                }
                case Maneuver.Charge:
                    intent.DesiredHeadingRad = PolicyMath.Dir(self.Pos, tp);
                    intent.SpeedFrac = speed;
                    intent.State = BrainState.Charge;
                    if (d <= cfg.MeleeReachM) intent.Action = BrainAction.Melee;
                    break;
                case Maneuver.Retreat:
                {
                    int ti = NearestVisibleThreat(obs, cfg, self);
                    intent.DesiredHeadingRad = ti >= 0 ? PolicyMath.Dir(obs.Targets[ti].Pos, self.Pos) : self.HeadingRad;
                    intent.SpeedFrac = speed;
                    intent.State = BrainState.Retreat;
                    break;
                }
                case Maneuver.Regroup:
                {
                    int n = 0; float cx = 0f, cy = 0f;
                    for (int i = 0; i < obs.AllyCount; i++)
                    {
                        ref readonly AllyObs al = ref obs.Allies[i];
                        if (!al.Alive || PolicyMath.Dist(self.Pos, al.Pos) > cfg.AllyAwarenessM) continue;
                        n++; cx += al.Pos.X; cy += al.Pos.Y;
                    }
                    Float2 c = n > 0 ? new Float2(cx / n, cy / n) : self.Pos;
                    bool there = n == 0 || PolicyMath.Dist(self.Pos, c) <= pcfg.RegroupRadiusM;
                    intent.DesiredHeadingRad = there ? (src >= 0 ? PolicyMath.Dir(self.Pos, tp) : self.HeadingRad) : PolicyMath.Dir(self.Pos, c);
                    intent.SpeedFrac = there ? 0f : speed;
                    intent.State = tid != 0 ? BrainState.Approach : BrainState.Idle;
                    break;
                }
                default: // Hold
                    intent.DesiredHeadingRad = src >= 0 ? PolicyMath.Dir(self.Pos, tp) : self.HeadingRad;
                    intent.SpeedFrac = 0f;
                    intent.State = tid != 0 ? BrainState.Approach : BrainState.Idle;
                    m = Maneuver.Hold;
                    break;
            }
            intent.Reason = reason;
            mem.CurrentTargetId = tid;
            mem.LastManeuver = (int)m;
        }

        private static int NearestVisibleThreat(in BrainObservation obs, in BrainConfig cfg, in SelfObs self)
        {
            int best = -1; float bd = float.PositiveInfinity;
            for (int i = 0; i < obs.TargetCount; i++)
            {
                ref readonly TargetObs t = ref obs.Targets[i];
                if (!t.Alive || !t.Visible) continue;
                float d = PolicyMath.Dist(self.Pos, t.Pos);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }
    }
}
