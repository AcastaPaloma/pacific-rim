using System;
using Game.Core;

namespace Game.AI
{
    /// <summary>
    /// Scripted baseline brain: the R-024 1.4 state machine with 1.5 target selection. Deterministic, no wall clock,
    /// no allocation in Decide. Decides every BrainPeriodSteps (phase = EntityId mod period), holds the last intent otherwise.
    /// </summary>
    public sealed class ScriptedKaijuBrain : IKaijuBrain
    {
        private BrainConfig _cfg = BrainConfig.Default();
        private ulong _seed;
        private bool _started;
        private BrainIntent _held;

        private BrainState _state;
        private bool _withdrawn;
        private ulong _target;           // 0 = none
        private double _lastSwitch;
        private double _holdStart;       // centre hold at standoff; <0 = unset
        private double _flankStart;
        private double _chargeLastContact;
        private double _farSince;        // retreat: time first beyond safe distance; <0 = unset

        public int ContractVersion => BrainContracts.ContractVersion;
        public BrainState State => _state;
        public ulong Seed => _seed;

        public void Reset(ulong seed, in BrainConfig cfg)
        {
            _cfg = cfg;
            if (_cfg.BrainPeriodSteps < 1) _cfg.BrainPeriodSteps = 1;
            _seed = seed; // no randomness is consumed by the v1 state machine; kept for the contract and replay logs
            _started = false;
            _held = default;
            _state = BrainState.Idle; _withdrawn = false; _target = 0;
            _lastSwitch = double.NegativeInfinity;
            _holdStart = -1; _flankStart = 0; _chargeLastContact = 0; _farSince = -1;
        }

        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            ulong id = obs.Self.Id.Value;
            int period = _cfg.BrainPeriodSteps;
            bool due = !_started || (obs.Step % period) == (long)(id % (ulong)period);
            if (!due) { intent = _held; return; }
            _started = true;
            Think(obs, ref intent);
            _held = intent;
        }

        private static float Wrap(float a)
        {
            const float TwoPi = 6.2831853071795864f;
            while (a > MathF.PI) a -= TwoPi;
            while (a <= -MathF.PI) a += TwoPi;
            return a;
        }

        private static float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }
        private static float Dir(Float2 from, Float2 to) => MathF.Atan2(to.Y - from.Y, to.X - from.X);

        private float Score(in BrainObservation obs, in TargetObs t, in SelfObs self)
        {
            float d = Dist(self.Pos, t.Pos);
            float distTerm = 1f - d / _cfg.SightRangeM; if (distTerm < 0f) distTerm = 0f;
            float hp = t.HpFrac < 0f ? 0f : (t.HpFrac > 1f ? 1f : t.HpFrac);
            float facing = MathF.Abs(Wrap(t.HeadingRad - Dir(t.Pos, self.Pos))) <= _cfg.FacingHalfAngleRad ? 1f : 0f;
            int on = 0, live = 0;
            for (int i = 0; i < obs.AllyCount; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!a.Alive || Dist(self.Pos, a.Pos) > _cfg.AllyAwarenessM) continue;
                live++;
                if (a.TargetId.Value == t.Id.Value) on++;
            }
            float focus = (float)on / (live > 1 ? live : 1);
            return _cfg.WeightDistance * distTerm + _cfg.WeightHp * (1f - hp) + _cfg.WeightFacing * facing + _cfg.WeightFocus * focus;
        }

        private bool Usable(in TargetObs t) => t.Alive && t.MemoryAge <= _cfg.MemorySeconds && (t.Visible || t.MemoryAge <= _cfg.MemorySeconds);

        private void Enter(BrainState s, double now)
        {
            _state = s; _holdStart = -1; _flankStart = now; _chargeLastContact = now; _farSince = -1;
        }

        private void Think(in BrainObservation obs, ref BrainIntent intent)
        {
            double now = obs.TimeSeconds;
            SelfObs self = obs.Self;
            ulong id = self.Id.Value;

            // ally summary
            int liveAllies = 0, rank = 0; bool allyCharging = false;
            float cx = self.Pos.X, cy = self.Pos.Y;
            for (int i = 0; i < obs.AllyCount; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!a.Alive || Dist(self.Pos, a.Pos) > _cfg.AllyAwarenessM) continue;
                liveAllies++; cx += a.Pos.X; cy += a.Pos.Y;
                if (a.Id.Value < id) rank++;
                if (a.State == (byte)BrainState.Charge) allyCharging = true;
            }
            Float2 centroid = new Float2(cx / (liveAllies + 1), cy / (liveAllies + 1));
            bool wing = liveAllies > 0 && (rank & 1) == 0;       // even rank = wing, odd = centre
            float side = ((rank >> 1) & 1) == 0 ? 1f : -1f;      // wings alternate +1/-1

            // candidates
            int best = -1, cur = -1; float bestScore = 0f, curScore = 0f;
            for (int i = 0; i < obs.TargetCount; i++)
            {
                ref readonly TargetObs t = ref obs.Targets[i];
                if (!Usable(t)) continue;
                float sc = Score(obs, t, self);
                if (best < 0 || sc > bestScore) { best = i; bestScore = sc; }
                if (_target != 0 && t.Id.Value == _target) { cur = i; curScore = sc; }
            }

            ReasonCode reason = ReasonCode.None;

            // ---- transitions (at most one per decision) ----
            switch (_state)
            {
                case BrainState.Idle:
                case BrainState.Withdrawn:
                    if (!_withdrawn && best >= 0)
                    {
                        _target = obs.Targets[best].Id.Value; cur = best; _lastSwitch = now;
                        Enter(BrainState.Approach, now); reason = ReasonCode.TargetAcquired;
                    }
                    break;

                case BrainState.Retreat:
                {
                    float dn = float.PositiveInfinity;
                    for (int i = 0; i < obs.TargetCount; i++)
                    {
                        ref readonly TargetObs t = ref obs.Targets[i];
                        if (!t.Alive || !t.Visible) continue;
                        float d = Dist(self.Pos, t.Pos); if (d < dn) dn = d;
                    }
                    if (dn > _cfg.RetreatSafeDistanceM)
                    {
                        if (_farSince < 0) _farSince = now;
                        if (now - _farSince >= _cfg.RetreatSafeSeconds)
                        {
                            _withdrawn = true; _target = 0; Enter(BrainState.Withdrawn, now); reason = ReasonCode.Withdrawn;
                        }
                    }
                    else _farSince = -1;
                    break;
                }

                default: // Approach, Flank, Charge
                    if (self.HpFrac < _cfg.RetreatHpFrac)
                    {
                        Enter(BrainState.Retreat, now); reason = ReasonCode.LowHp; break;
                    }
                    if (cur < 0)
                    {
                        // target dead or lost > memory: forced switch (does not count as voluntary) or Idle
                        if (best >= 0)
                        {
                            _target = obs.Targets[best].Id.Value; cur = best; _lastSwitch = now;
                            if (_state != BrainState.Approach) Enter(BrainState.Approach, now);
                            reason = ReasonCode.RetargetForced;
                        }
                        else { _target = 0; Enter(BrainState.Idle, now); reason = ReasonCode.TargetLost; }
                        break;
                    }
                    if (best >= 0 && best != cur && bestScore > _cfg.RetargetRatio * curScore && now - _lastSwitch >= _cfg.RetargetMinSeconds)
                    {
                        _target = obs.Targets[best].Id.Value; cur = best; _lastSwitch = now;
                        if (_state != BrainState.Approach) Enter(BrainState.Approach, now);
                        reason = ReasonCode.RetargetVoluntary; break;
                    }
                    reason = StateExits(obs, self, cur, liveAllies, wing, side, allyCharging, centroid, now);
                    break;
            }

            // ---- intent for the (possibly new) state ----
            intent.State = _state; intent.Reason = reason; intent.Action = BrainAction.None;
            switch (_state)
            {
                case BrainState.Approach:
                {
                    Float2 tp = obs.Targets[cur].Pos;
                    bool hold = liveAllies > 0 && !wing && Dist(self.Pos, tp) <= _cfg.StandoffM;
                    intent.DesiredHeadingRad = Dir(self.Pos, tp);
                    intent.SpeedFrac = hold ? 0f : (wing ? _cfg.SpeedApproachWing : _cfg.SpeedApproach);
                    intent.TargetId = obs.Targets[cur].Id;
                    break;
                }
                case BrainState.Flank:
                {
                    Float2 tp = obs.Targets[cur].Pos;
                    float ang = Dir(tp, centroid) + side * _cfg.WingOffsetRad;
                    Float2 w = new Float2(tp.X + _cfg.StandoffM * MathF.Cos(ang), tp.Y + _cfg.StandoffM * MathF.Sin(ang));
                    intent.DesiredHeadingRad = Dir(self.Pos, w);
                    intent.SpeedFrac = _cfg.SpeedFlank;
                    intent.TargetId = obs.Targets[cur].Id;
                    break;
                }
                case BrainState.Charge:
                {
                    Float2 tp = obs.Targets[cur].Pos;
                    intent.DesiredHeadingRad = Dir(self.Pos, tp);
                    intent.SpeedFrac = _cfg.SpeedCharge;
                    intent.TargetId = obs.Targets[cur].Id;
                    if (Dist(self.Pos, tp) <= _cfg.MeleeReachM) intent.Action = BrainAction.Melee;
                    break;
                }
                case BrainState.Retreat:
                {
                    int nearest = -1; float dn = float.PositiveInfinity;
                    for (int i = 0; i < obs.TargetCount; i++)
                    {
                        ref readonly TargetObs t = ref obs.Targets[i];
                        if (!t.Alive || !t.Visible) continue;
                        float d = Dist(self.Pos, t.Pos); if (d < dn) { dn = d; nearest = i; }
                    }
                    intent.DesiredHeadingRad = nearest >= 0 ? Dir(obs.Targets[nearest].Pos, self.Pos) : self.HeadingRad;
                    intent.SpeedFrac = _cfg.SpeedRetreat;
                    intent.TargetId = default;
                    break;
                }
                default: // Idle, Withdrawn
                    intent.DesiredHeadingRad = self.HeadingRad;
                    intent.SpeedFrac = 0f;
                    intent.TargetId = default;
                    break;
            }
        }

        // Exit rules for Approach / Flank / Charge against a valid current target (index cur).
        private ReasonCode StateExits(in BrainObservation obs, in SelfObs self, int cur, int liveAllies, bool wing, float side,
                                      bool allyCharging, Float2 centroid, double now)
        {
            Float2 tp = obs.Targets[cur].Pos;
            float d = Dist(self.Pos, tp);
            switch (_state)
            {
                case BrainState.Approach:
                    if (d > _cfg.StandoffM) return ReasonCode.None;
                    if (liveAllies == 0) { Enter(BrainState.Charge, now); return ReasonCode.InStandoffSolo; }
                    if (wing) { Enter(BrainState.Flank, now); return ReasonCode.InStandoffWing; }
                    if (_holdStart < 0) _holdStart = now;
                    if (allyCharging) { Enter(BrainState.Charge, now); return ReasonCode.AllyCharging; }
                    if (now - _holdStart >= _cfg.CentreHoldTimeoutSeconds) { Enter(BrainState.Charge, now); return ReasonCode.CentreHoldTimeout; }
                    return ReasonCode.None;

                case BrainState.Flank:
                {
                    if (liveAllies == 0) { Enter(BrainState.Charge, now); return ReasonCode.NoAlliesLeft; }
                    if (allyCharging) { Enter(BrainState.Charge, now); return ReasonCode.AllyCharging; }
                    float slot = Dir(tp, centroid) + side * _cfg.WingOffsetRad;
                    float err = MathF.Abs(Wrap(Dir(tp, self.Pos) - slot));
                    if (err <= _cfg.FlankBearingTolRad) { Enter(BrainState.Charge, now); return ReasonCode.FlankSlotReached; }
                    if (now - _flankStart >= _cfg.FlankTimeoutSeconds) { Enter(BrainState.Charge, now); return ReasonCode.FlankTimeout; }
                    return ReasonCode.None;
                }

                case BrainState.Charge:
                    if (d <= _cfg.MeleeReachM) _chargeLastContact = now;
                    if (now - _chargeLastContact >= _cfg.ChargeNoContactSeconds) { Enter(BrainState.Approach, now); return ReasonCode.ChargeNoContact; }
                    return ReasonCode.None;
            }
            return ReasonCode.None;
        }
    }
}
