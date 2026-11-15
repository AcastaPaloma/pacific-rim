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

        public PackMemberBrain(IKaijuBrain inner, IPackDirectiveSource src, in PackConfig pcfg)
        {
            _inner = inner; _src = src; _pcfg = pcfg;
        }

        public int ContractVersion => _inner.ContractVersion;

        public void Reset(ulong seed, in BrainConfig cfg) { _cfg = cfg; _inner.Reset(seed, cfg); }

        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            if (_src != null && _src.TryGetDirective(obs.Self.Id, out PackDirective d)) Decide(obs, d, ref intent);
            else _inner.Decide(obs, ref intent);
        }

        public void Decide(in BrainObservation obs, in PackDirective d, ref BrainIntent intent)
        {
            if (_inner is IDirectedBrain directed) { directed.Decide(obs, d, ref intent); return; }
            _inner.Decide(obs, ref intent);
            if (!d.Valid) return;
            Float2 me = obs.Self.Pos;
            switch (d.Role)
            {
                case PackRole.Raider: PostRaider(obs, d, ref intent); break;
                case PackRole.Bait: PostBait(obs, d, ref intent); break;
                case PackRole.Anchor: PostAnchor(d, me, ref intent); break;
                case PackRole.Flanker: PostFlanker(d, me, ref intent); break;
                default: if (d.Goal == PackGoal.Regroup) PostRegroup(d, me, ref intent); break;
            }
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
                    intent.DesiredHeadingRad = Dir(me, d.SlotPos); intent.SpeedFrac = TransitSpeed(me, d.SlotPos, _cfg.SpeedApproach); intent.State = BrainState.Approach;
                    if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
                }
                return;
            }
            float band = _pcfg.BaitBandFrac * _pcfg.MechWeaponRangeM;
            float dist = Dist(me, tp);
            float toT = Dir(me, tp);
            intent.TargetId = d.FocusTargetId;
            if (obs.Self.HpFrac < _pcfg.BaitRetreatHp)
            {
                intent.DesiredHeadingRad = Wrap(toT + MathF.PI); intent.SpeedFrac = _cfg.SpeedRetreat; intent.State = BrainState.Retreat; return;
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
                intent.DesiredHeadingRad = Dir(me, d.SlotPos); intent.SpeedFrac = _cfg.SpeedApproach; intent.State = BrainState.Approach;
            }
        }

        /// <summary>Bay-scale transit: beyond <see cref="PackConfig.TransitDistanceM"/> from the slot a member walks at full cruise speed instead of its tactical approach speed.</summary>
        private float TransitSpeed(Float2 me, Float2 slot, float tactical) => Dist(me, slot) > _pcfg.TransitDistanceM ? 1f : tactical;

        private void PostAnchor(in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            if (intent.State == BrainState.Charge) return;
            if (Dist(me, d.SlotPos) > 60f)
            {
                intent.DesiredHeadingRad = Dir(me, d.SlotPos); intent.SpeedFrac = TransitSpeed(me, d.SlotPos, _cfg.SpeedApproach); intent.State = BrainState.Approach;
                if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
            }
            else intent.SpeedFrac *= _pcfg.AnchorSpeedFrac;
        }

        private void PostFlanker(in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            if (intent.State == BrainState.Charge || intent.State == BrainState.Retreat) return;
            if (Dist(me, d.SlotPos) > 60f)
            {
                intent.DesiredHeadingRad = Dir(me, d.SlotPos); intent.SpeedFrac = TransitSpeed(me, d.SlotPos, _cfg.SpeedFlank); intent.State = BrainState.Flank;
                if (d.FocusTargetId.IsValid) intent.TargetId = d.FocusTargetId;
            }
        }

        private void PostRegroup(in PackDirective d, Float2 me, ref BrainIntent intent)
        {
            intent.Action = BrainAction.None;
            if (Dist(me, d.SlotPos) > 100f)
            {
                intent.DesiredHeadingRad = Dir(me, d.SlotPos); intent.SpeedFrac = _cfg.SpeedRetreat; intent.State = BrainState.Retreat;
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
