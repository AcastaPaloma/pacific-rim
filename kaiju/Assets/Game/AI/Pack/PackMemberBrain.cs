using System;
using Game.Core;

namespace Game.AI.Pack
{
    /// <summary>Where a member brain reads its directive (the <see cref="PackCoordinator"/>, or a test double).</summary>
    public interface IPackDirectiveSource
    {
        bool TryGetDirective(EntityId id, out PackDirective directive);
    }

    /// <summary>
    /// Wraps any <see cref="IKaijuBrain"/> (R-103 7): when the inner brain is not directed, its intent is post-processed per role
    /// (Bait kites at the band and never melees, Anchor slows after reaching its slot, Raider transits to the route point and fights only in
    /// self-defence, Flanker steers to its flank slot until the inner brain charges, Free regroups). Without a directive the inner brain is unchanged.
    /// No allocation per decision; deterministic.
    /// </summary>
    public sealed class PackMemberBrain : IDirectedBrain
    {
        private readonly IKaijuBrain _inner;
        private readonly IPackDirectiveSource _src;
        private readonly PackConfig _pcfg;
        private BrainConfig _cfg = BrainConfig.Default();
        private bool _baitRetreating;   // latched once the Bait fell below BaitRetreatHp: it leaves the fight instead of flapping in and out of the allocation

        public PackMemberBrain(IKaijuBrain inner, IPackDirectiveSource src, in PackConfig pcfg)
        {
            _inner = inner; _src = src; _pcfg = pcfg;
        }

        public int ContractVersion => _inner.ContractVersion;

        public void Reset(ulong seed, in BrainConfig cfg) { _cfg = cfg; _baitRetreating = false; _inner.Reset(seed, cfg); }

        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            if (_src != null && _src.TryGetDirective(obs.Self.Id, out PackDirective d)) Decide(obs, d, ref intent);
            else { _inner.Decide(obs, ref intent); if (_baitRetreating && _pcfg.BayTactics) LeaveFight(obs, default, ref intent); }
        }

        public void Decide(in BrainObservation obs, in PackDirective dIn, ref BrainIntent intent)
        {
            if (_inner is IDirectedBrain directed) { directed.Decide(obs, dIn, ref intent); return; }
            _inner.Decide(obs, ref intent);
            if (!dIn.Valid) return;
            PackDirective d = dIn;
            // T-113c timed strike: a striker whose launch delay has not run out still holds its slot (the gate is closed for it)
            if (d.CommitDelayS > 0f && d.CommitReady && d.Role != PackRole.Bait && obs.TimeSeconds < d.GateOpenAt + d.CommitDelayS) d.CommitReady = false;
            if (_baitRetreating && _pcfg.BayTactics) { LeaveFight(obs, d, ref intent); return; }
            Float2 me = obs.Self.Pos;
            switch (d.Role)
            {
                case PackRole.Raider: PostRaider(obs, d, ref intent); break;
                case PackRole.Bait: PostBait(obs, d, ref intent); break;
                case PackRole.Anchor: PostAnchor(obs, d, me, ref intent); break;
                case PackRole.Flanker: PostFlanker(obs, d, me, ref intent); break;
                default: if (d.Goal == PackGoal.Regroup) PostRegroup(d, me, ref intent); break;
            }
            if (_pcfg.AvoidOtherMechsM > 0f && d.Goal == PackGoal.Hunt && d.Role != PackRole.Raider) AvoidOtherMechs(obs, d, ref intent);
        }

        /// <summary>
        /// T-113b: a member that is not charging steers around a seen mech other than the pack focus (it would otherwise walk through that mech's
        /// weapon range on its way to the slot and be shot by a mech nobody is fighting). Own sight only; the focus itself is never avoided.
        /// </summary>
        private void AvoidOtherMechs(in BrainObservation obs, in PackDirective d, ref BrainIntent intent)
        {
            if (intent.State == BrainState.Charge || intent.Action == BrainAction.Melee || intent.SpeedFrac <= 0f) return;
            Float2 me = obs.Self.Pos;
            int k = -1; float dk = _pcfg.AvoidOtherMechsM;
            for (int i = 0; i < obs.TargetCount; i++)
            {
                if (!obs.Targets[i].Alive || obs.Targets[i].Id == d.FocusTargetId) continue;
                float q = Dist(me, obs.Targets[i].Pos);
                if (q < dk) { dk = q; k = i; }
            }
            if (k < 0) return;
            float r0 = _pcfg.MechWeaponRangeM;
            float w = Math.Min(3f, Math.Max(0f, 2f * (_pcfg.AvoidOtherMechsM - dk) / Math.Max(1f, _pcfg.AvoidOtherMechsM - r0)));
            float away = Dir(obs.Targets[k].Pos, me);
            float vx = MathF.Cos(intent.DesiredHeadingRad) + w * MathF.Cos(away), vy = MathF.Sin(intent.DesiredHeadingRad) + w * MathF.Sin(away);
            if (vx * vx + vy * vy > 1e-6f) intent.DesiredHeadingRad = MathF.Atan2(vy, vx);
            if (dk < r0 * 1.1f) intent.SpeedFrac = 1f;   // inside or at its weapon range: get out at full speed
        }

        /// <summary>
        /// T-113b pincer (bait-first strike): once the strikers charge, the Bait keeps its distance to the focus at the nearest charging striker's distance
        /// +/- BaitSwingM, alternating every BaitSwingSeconds (nearer first: it holds the turret while the strikers close). The nearest threat therefore
        /// alternates between bearings >= 90 deg apart. Returns false when the pincer does not apply.
        /// </summary>
        private bool Pincer(in BrainObservation obs, in PackDirective d, Float2 tp, float dist, ref BrainIntent intent)
        {
            if (!_pcfg.BayTactics || !d.BaitFirst || !d.CommitReady || d.Goal != PackGoal.Hunt || _pcfg.BaitSwingM <= 0f) return false;
            float ds = float.MaxValue;
            for (int i = 0; i < obs.AllyCount; i++)
            {
                if (!obs.Allies[i].Alive || obs.Allies[i].State != (byte)BrainState.Charge || obs.Allies[i].TargetId != d.FocusTargetId) continue;
                float q = Dist(obs.Allies[i].Pos, tp);
                if (q < ds) ds = q;
            }
            float band = _pcfg.BaitBandFrac * _pcfg.MechWeaponRangeM;
            bool nearer = ((long)Math.Floor((obs.TimeSeconds - d.GateOpenAt) / _pcfg.BaitSwingSeconds) & 1L) == 0L;
            float want = ds == float.MaxValue ? band : ds + (nearer ? -_pcfg.BaitSwingM : _pcfg.BaitSwingM);
            want = Math.Max(want, 0.8f * _cfg.MeleeReachM);
            float toT = Dir(obs.Self.Pos, tp);
            intent.TargetId = d.FocusTargetId; intent.DesiredHeadingRad = toT;
            if (dist > want) { intent.SpeedFrac = _cfg.SpeedCharge; intent.State = BrainState.Charge; }
            else { intent.SpeedFrac = 0f; intent.State = BrainState.Flank; }
            intent.Action = dist <= _cfg.MeleeReachM ? BrainAction.Melee : BrainAction.None;
            return true;
        }

        private static float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }
        private static float Dir(Float2 a, Float2 b) => MathF.Atan2(b.Y - a.Y, b.X - a.X);
        private static float Wrap(float a)
        {
            const float tau = 6.2831855f;
            while (a > MathF.PI) a -= tau;
            while (a <= -MathF.PI) a += tau;
            return a;
        }

        private static bool FindTarget(in BrainObservation obs, EntityId id, out Float2 pos)
        {
            for (int i = 0; i < obs.TargetCount; i++)
                if (obs.Targets[i].Id == id && obs.Targets[i].Alive) { pos = obs.Targets[i].Pos; return true; }
            pos = default; return false;
        }

        private void PostRaider(in BrainObservation obs, in PackDirective d, ref BrainIntent intent)
        {
            // self-defence: keep the inner melee when a live target is inside reach
            if (intent.Action == BrainAction.Melee) return;
            for (int i = 0; i < obs.TargetCount; i++)
                if (obs.Targets[i].Alive && Dist(obs.Self.Pos, obs.Targets[i].Pos) <= _cfg.MeleeReachM * 1.5f) return;
            intent.DesiredHeadingRad = Dir(obs.Self.Pos, d.RoutePos);
            intent.SpeedFrac = 1f;
            intent.Action = BrainAction.None;
            intent.State = BrainState.Approach;
            intent.TargetId = d.FocusTargetId;
        }

        private void PostBait(in BrainObservation obs, in PackDirective d, ref BrainIntent intent)
        {
            intent.Action = BrainAction.None;
            Float2 me = obs.Self.Pos;
            if (!FindTarget(obs, d.FocusTargetId, out Float2 tp))
            {
                // target known to the pack board but not in this member's sight yet: walk to the slot (Bay-scale transit), else keep the inner behaviour
                if (d.Valid && Dist(me, d.SlotPos) > 60f)
                {
                    intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, _cfg.SpeedApproach); intent.State = BrainState.Approach;
                    if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
                }
                return;
            }
            float band = _pcfg.BaitBandFrac * _pcfg.MechWeaponRangeM;
            float dist = Dist(me, tp);
            float toT = Dir(me, tp);
            intent.TargetId = d.FocusTargetId;
            if (!_baitRetreating && obs.Self.HpFrac >= _pcfg.BaitRetreatHp && Pincer(obs, d, tp, dist, ref intent)) return;
            if (d.BaitJoin && !_baitRetreating && obs.Self.HpFrac >= _pcfg.BaitRetreatHp)
            {
                // bait and strike: the lock is drawn and the strike is under way, the Bait joins it
                intent.DesiredHeadingRad = toT; intent.SpeedFrac = _cfg.SpeedCharge; intent.State = BrainState.Charge;
                intent.Action = dist <= _cfg.MeleeReachM ? BrainAction.Melee : BrainAction.None;
                return;
            }
            if (obs.Self.HpFrac < _pcfg.BaitRetreatHp) _baitRetreating = true;
            if (_baitRetreating) { LeaveFight(obs, d, ref intent); return; }
            bool timed = _pcfg.BaitTimed && d.BaitFirst && d.Goal == PackGoal.Hunt;
            if (timed && !d.BaitGo)
            {
                // T-113c: wait outside weapon range (a patrolling mech drifts onto the stage ring) until the Anchor is BaitLeadS from it
                float keep = _pcfg.MechWeaponRangeM * 1.08f;
                if (dist < keep) { intent.DesiredHeadingRad = Wrap(toT + MathF.PI); intent.SpeedFrac = 1f; intent.State = BrainState.Flank; }
                else if (Dist(me, d.SlotPos) > 40f) { intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, _cfg.SpeedApproach); intent.State = BrainState.Approach; }
                else { intent.SpeedFrac = 0f; intent.DesiredHeadingRad = toT; intent.State = BrainState.Flank; }
                return;
            }
            if (!timed && _pcfg.BayTactics && d.CommitReady && d.Goal == PackGoal.Hunt && !d.BaitJoin && _pcfg.BaitJoinDelaySeconds > 0f)
            {
                // the lock is drawn and the strike is on its way: slip out of the fire until it is time to join (the mechs retarget to the nearer strikers)
                float safe = _pcfg.MechWeaponRangeM * 1.35f;
                if (dist < safe) { intent.DesiredHeadingRad = Wrap(toT + MathF.PI); intent.SpeedFrac = 1f; intent.State = BrainState.Flank; }
                else { intent.SpeedFrac = 0f; intent.DesiredHeadingRad = toT; intent.State = BrainState.Flank; }
                return;
            }
            if (dist < band * 1.05f)
            {
                // kite tangentially, side fixed by the slot's side of the approach line so it does not flip
                float cross = (d.SlotPos.X - tp.X) * (me.Y - tp.Y) - (d.SlotPos.Y - tp.Y) * (me.X - tp.X);
                float side = cross >= 0f ? 1f : -1f;
                float outward = dist < band * 0.9f ? 0.6f : 0f; // angle away from the target when inside the band
                intent.DesiredHeadingRad = Wrap(toT + side * (MathF.PI * 0.5f + outward));
                intent.SpeedFrac = _cfg.SpeedFlank; intent.State = BrainState.Flank;
            }
            else
            {
                intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, _cfg.SpeedApproach); intent.State = BrainState.Approach;
            }
        }

        /// <summary>
        /// Hunt commit (R-103 4.2): Anchor and Flanker hold their slot while the gate is closed (an inner Charge is suppressed) and charge the focus target together
        /// when it opens. Retreat of the inner brain always wins.
        /// </summary>
        private bool HoldOrCommit(in BrainObservation obs, in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            if (!_pcfg.BayTactics) return false;
            if (intent.State == BrainState.Retreat || intent.State == BrainState.Withdrawn) return true;
            if (d.Goal != PackGoal.Hunt || !d.FocusTargetId.IsValid) return false;
            if (d.CommitReady)
            {
                if (FindTarget(obs, d.FocusTargetId, out Float2 tp))
                {
                    float dist = Dist(me, tp);
                    // within the commit range only: a straggler still in transit keeps walking to its slot
                    if (dist <= Math.Max(_pcfg.CommitRangeFrac * _stand(obs), d.CommitRangeM))
                    {
                        intent.DesiredHeadingRad = Dir(me, tp); intent.SpeedFrac = _cfg.SpeedCharge; intent.State = BrainState.Charge; intent.TargetId = d.FocusTargetId;
                        intent.Action = dist <= _cfg.MeleeReachM ? BrainAction.Melee : BrainAction.None;
                        return true;
                    }
                }
                return false;
            }
            return false;
        }

        private float _stand(in BrainObservation obs) => _cfg.StandoffM;

        /// <summary>A Bait that fell below its retreat hp leaves the fight for good (latched, whatever role it is given afterwards): away from the focus, then still.</summary>
        private void LeaveFight(in BrainObservation obs, in PackDirective d, ref BrainIntent intent)
        {
            Float2 me = obs.Self.Pos;
            intent.Action = BrainAction.None;
            int near = -1; float dn = 700f;
            for (int i = 0; i < obs.TargetCount; i++)
            {
                if (!obs.Targets[i].Alive) continue;
                float dd = Dist(me, obs.Targets[i].Pos);
                if (dd <= dn) { dn = dd; near = i; }
            }
            if (near >= 0) { intent.DesiredHeadingRad = Wrap(Dir(me, obs.Targets[near].Pos) + MathF.PI); intent.SpeedFrac = _cfg.SpeedRetreat; intent.State = BrainState.Retreat; }
            else { intent.SpeedFrac = 0f; intent.State = BrainState.Withdrawn; }
        }

        /// <summary>Bay-scale transit: beyond <see cref="PackConfig.TransitDistanceM"/> from the slot a member walks at full cruise speed instead of its tactical approach speed.</summary>
        /// <summary>Transit aim point: the planner's next waypoint while the slot is far (RoutePlanning), the slot itself on the last stretch.</summary>
        private Float2 Via(Float2 me, in PackDirective d) => _pcfg.RoutePlanning && Dist(me, d.SlotPos) > _pcfg.TransitDistanceM ? d.RoutePos : d.SlotPos;

        private float TransitSpeed(Float2 me, in PackDirective d, float tactical) => Dist(me, d.SlotPos) > _pcfg.TransitDistanceM ? (d.PaceFrac > 0f ? d.PaceFrac : 1f) : Math.Max(tactical, _pcfg.SlotApproachFrac);

        private void PostAnchor(in BrainObservation obs, in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            if (HoldOrCommit(obs, d, me, ref intent)) return;
            if (HoldSlot(obs, d, me, ref intent, _cfg.SpeedApproach, BrainState.Approach)) return;
            if (intent.State == BrainState.Charge) return;
            if (Dist(me, d.SlotPos) > 60f)
            {
                intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, _cfg.SpeedApproach); intent.State = BrainState.Approach;
                if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
            }
            else intent.SpeedFrac *= _pcfg.AnchorSpeedFrac;
        }

        private void PostFlanker(in BrainObservation obs, in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            if (HoldOrCommit(obs, d, me, ref intent)) return;
            if (HoldSlot(obs, d, me, ref intent, _cfg.SpeedFlank, BrainState.Flank)) return;
            if (intent.State == BrainState.Charge || intent.State == BrainState.Retreat) return;
            if (Dist(me, d.SlotPos) > 60f)
            {
                intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, _cfg.SpeedFlank); intent.State = BrainState.Flank;
                if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
            }
        }

        /// <summary>Hunt with the gate closed: walk to the slot (full speed far away), then stand on it facing the target. Returns false outside a hunt or once the gate is open.</summary>
        private bool HoldSlot(in BrainObservation obs, in PackDirective d, Float2 me, ref BrainIntent intent, float tactical, BrainState label)
        {
            if (!_pcfg.BayTactics || d.Goal != PackGoal.Hunt || d.CommitReady || !d.FocusTargetId.IsValid) return false;
            intent.Action = BrainAction.None; intent.TargetId = d.FocusTargetId; intent.State = label;
            if (Dist(me, d.SlotPos) > 40f) { intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = TransitSpeed(me, d, tactical); }
            else
            {
                intent.SpeedFrac = 0f;
                intent.DesiredHeadingRad = FindTarget(obs, d.FocusTargetId, out Float2 tp) ? Dir(me, tp) : Dir(me, d.SlotPos);
            }
            return true;
        }

        private void PostRegroup(in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            intent.Action = BrainAction.None;
            if (Dist(me, d.SlotPos) > 100f)
            {
                intent.DesiredHeadingRad = Dir(me, Via(me, d)); intent.SpeedFrac = _cfg.SpeedRetreat; intent.State = BrainState.Retreat;
            }
            else { intent.SpeedFrac = 0f; intent.State = BrainState.Idle; }
        }
    }

    /// <summary>Makes pack-directed brains (BrainId <c>pack@1</c>) around an inner factory. Registered by a module only while <see cref="PackFlags.Pack"/> is on.</summary>
    public sealed class PackBrainFactory : IKaijuBrainFactory
    {
        private readonly IKaijuBrainFactory _inner;
        private readonly IPackDirectiveSource _src;
        private readonly PackConfig _pcfg;

        public PackBrainFactory(IKaijuBrainFactory inner, IPackDirectiveSource src, in PackConfig pcfg)
        {
            _inner = inner; _src = src; _pcfg = pcfg;
        }

        public string BrainId => PackFlags.BrainId;
        public IKaijuBrain Create() => new PackMemberBrain(_inner.Create(), _src, _pcfg);
    }
}
