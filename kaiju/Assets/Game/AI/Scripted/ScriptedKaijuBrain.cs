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
        private ulong _prevTarget;       // target left by the last switch (switch-back lockout, R-024 rev 3)
        private double _prevLeftAt;
        private double _holdStart;       // centre hold at standoff; <0 = unset
        private double _flankStart;
        private double _flankDeadline;   // absolute time
        private double _assemblyStart;   // wing waiting at standoff for the pack; <0 = unset
        private double _holdExtendedTo;  // centre hold: time of last flanking/unassembled evidence
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
            _lastSwitch = double.NegativeInfinity; _prevTarget = 0; _prevLeftAt = double.NegativeInfinity;
            _holdStart = -1; _assemblyStart = -1; _holdExtendedTo = 0; _flankDeadline = 0; _flankStart = 0; _chargeLastContact = 0; _farSince = -1;
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
            if (float.IsNaN(a) || float.IsInfinity(a)) return 0f;
            if (a > 64f || a < -64f) a -= TwoPi * MathF.Floor(a / TwoPi);
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
            _state = s; _holdStart = -1; _assemblyStart = -1; _flankStart = now; _chargeLastContact = now; _farSince = -1;
        }

        private void Think(in BrainObservation obs, ref BrainIntent intent)
        {
            double now = obs.TimeSeconds;
            SelfObs self = obs.Self;

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
            int liveAllies; bool wing, allyCharging; float side; Float2 centroid;

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
                    // R-024 rev 4b: switching back to the target just left is blocked for SwitchBackLockoutSeconds and, for the same
                    // span again, needs the squared hysteresis ratio, so the lockout is shorter than the metric's 12 s thrash window
                    // (a lockout equal to the window made C3 unable to fail) and a flip right after it still has to win clearly.
                    float needRatio = _cfg.RetargetRatio; bool backBlocked = false;
                    if (best >= 0 && obs.Targets[best].Id.Value == _prevTarget)
                    {
                        double since = now - _prevLeftAt;
                        if (since < _cfg.SwitchBackLockoutSeconds) backBlocked = true;
                        else if (since < 2.0 * _cfg.SwitchBackLockoutSeconds) needRatio = _cfg.RetargetRatio * _cfg.RetargetRatio;
                    }
                    if (best >= 0 && best != cur && !backBlocked && bestScore > needRatio * curScore && now - _lastSwitch >= _cfg.RetargetMinSeconds)
                    {
                        _prevTarget = _target; _prevLeftAt = now;
                        _target = obs.Targets[best].Id.Value; cur = best; _lastSwitch = now;
                        if (_state != BrainState.Approach) Enter(BrainState.Approach, now);
                        reason = ReasonCode.RetargetVoluntary; break;
                    }
                    PackSummary(obs, self, out liveAllies, out wing, out side, out allyCharging, out centroid);
                    reason = StateExits(obs, self, cur, liveAllies, wing, side, allyCharging, centroid, now);
                    break;
            }
            // the coordination group depends on the (possibly new) target: recompute for the intent
            PackSummary(obs, self, out liveAllies, out wing, out side, out allyCharging, out centroid);

            // ---- intent for the (possibly new) state ----
            intent.State = _state; intent.Reason = reason; intent.Action = BrainAction.None;
            switch (_state)
            {
                case BrainState.Approach:
                {
                    Float2 tp = obs.Targets[cur].Pos;
                    bool hold = liveAllies > 0 && Dist(self.Pos, tp) <= _cfg.StandoffM;
                    intent.DesiredHeadingRad = Dir(self.Pos, tp);
                    intent.SpeedFrac = hold ? 0f : (wing ? _cfg.SpeedApproachWing : _cfg.SpeedApproach);
                    intent.TargetId = obs.Targets[cur].Id;
                    break;
                }
                case BrainState.Flank:
                {
                    Float2 tp = obs.Targets[cur].Pos;
                    Float2 w = FlankWaypoint(tp, centroid, side);
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


        // R-024 rev 4: the coordination group of a kaiju is its active pack on the same target: allies that are alive,
        // within ally awareness, in a combat state (Approach/Flank/Charge) and whose public TargetId equals mine. This is
        // the "engaged" set G_T of R-024 3.1 the metric measures. Retreating, withdrawn, idle and other-target allies take
        // no part in roles, centroid, the assembly gate, the engaged spread or the "ally charging" trigger.
        private bool InGroup(in AllyObs a, in SelfObs self)
        {
            if (!a.Alive || _target == 0 || a.TargetId.Value != _target) return false;
            if (a.State != (byte)BrainState.Approach && a.State != (byte)BrainState.Flank && a.State != (byte)BrainState.Charge) return false;
            return Dist(self.Pos, a.Pos) <= _cfg.AllyAwarenessM;
        }

        private void PackSummary(in BrainObservation obs, in SelfObs self, out int liveAllies, out bool wing, out float side,
                                 out bool allyCharging, out Float2 centroid)
        {
            ulong id = self.Id.Value;
            int rank = 0; liveAllies = 0; allyCharging = false;
            float cx = self.Pos.X, cy = self.Pos.Y;
            for (int i = 0; i < obs.AllyCount; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!InGroup(a, self)) continue;
                liveAllies++; cx += a.Pos.X; cy += a.Pos.Y;
                if (a.Id.Value < id) rank++;
                if (a.State == (byte)BrainState.Charge) allyCharging = true;
            }
            centroid = new Float2(cx / (liveAllies + 1), cy / (liveAllies + 1));
            wing = liveAllies > 0 && (rank & 1) == 0;       // even rank = wing, odd = centre
            side = ((rank >> 1) & 1) == 0 ? 1f : -1f;       // wings alternate +1/-1
        }

        private Float2 FlankWaypoint(Float2 tp, Float2 centroid, float side)
        {
            float ang = Dir(tp, centroid) + side * _cfg.WingOffsetRad;
            return new Float2(tp.X + _cfg.StandoffM * MathF.Cos(ang), tp.Y + _cfg.StandoffM * MathF.Sin(ang));
        }

        private bool Assembled(in BrainObservation obs, in SelfObs self, Float2 tp)
        {
            for (int i = 0; i < obs.AllyCount; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!InGroup(a, self)) continue;
                if (Dist(a.Pos, tp) > _cfg.AssemblyRadiusM) return false;
            }
            return true;
        }

        private bool AnyAllyInState(in BrainObservation obs, in SelfObs self, BrainState st)
        {
            for (int i = 0; i < obs.AllyCount; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!InGroup(a, self)) continue;
                if (a.State == (byte)st) return true;
            }
            return false;
        }

        // Minimal arc (rad) containing the bearings from the target of me and every live ally within EngageRadius of it.
        private float EngagedSpread(in BrainObservation obs, in SelfObs self, Float2 tp)
        {
            const float TwoPi = 6.2831853071795864f;
            int n = 0;
            Span<float> b = stackalloc float[BrainContracts.MaxAllies + 1];
            if (Dist(self.Pos, tp) <= _cfg.EngageRadiusM) b[n++] = Dir(tp, self.Pos);
            for (int i = 0; i < obs.AllyCount && n < b.Length; i++)
            {
                ref readonly AllyObs a = ref obs.Allies[i];
                if (!InGroup(a, self)) continue;
                if (Dist(a.Pos, tp) <= _cfg.EngageRadiusM) b[n++] = Dir(tp, a.Pos);
            }
            if (n < 2) return 0f;
            for (int i = 0; i < n; i++) { float v = b[i] % TwoPi; if (v < 0) v += TwoPi; b[i] = v; }
            for (int i = 1; i < n; i++) { float v = b[i]; int j = i - 1; while (j >= 0 && b[j] > v) { b[j + 1] = b[j]; j--; } b[j + 1] = v; }
            float maxGap = b[0] + TwoPi - b[n - 1];
            for (int i = 1; i < n; i++) { float g = b[i] - b[i - 1]; if (g > maxGap) maxGap = g; }
            return TwoPi - maxGap;
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
                {
                    if (d > _cfg.StandoffM) return ReasonCode.None;
                    if (liveAllies == 0 || _cfg.DirectCharge) { Enter(BrainState.Charge, now); return ReasonCode.InStandoffSolo; }
                    bool assembled = Assembled(obs, self, tp);
                    if (wing)
                    {
                        // pack-assembly gate: hold at standoff until every live ally is near the target (or timeout)
                        if (_assemblyStart < 0) _assemblyStart = now;
                        if (allyCharging) { Enter(BrainState.Charge, now); return ReasonCode.AllyCharging; }
                        if (!assembled && now - _assemblyStart < _cfg.AssemblyTimeoutSeconds) return ReasonCode.None;
                        Enter(BrainState.Flank, now);
                        float slot0 = Dir(tp, centroid) + side * _cfg.WingOffsetRad;
                        float arc = d * MathF.Abs(Wrap(Dir(tp, self.Pos) - slot0)) ;
                        float spd = _cfg.SpeedFlank * _cfg.CruiseSpeedMps; if (spd < 0.1f) spd = 0.1f;
                        float t = _cfg.FlankTimeoutMargin * arc / spd + _cfg.FlankTimeoutSlackSeconds;
                        if (t < _cfg.FlankTimeoutSeconds) t = _cfg.FlankTimeoutSeconds;
                        _flankDeadline = now + t;
                        return ReasonCode.InStandoffWing;
                    }
                    if (_holdStart < 0) { _holdStart = now; _holdExtendedTo = now; }
                    if (allyCharging) { Enter(BrainState.Charge, now); return ReasonCode.AllyCharging; }
                    // centre waits while the wings are still assembling or flanking (extends the 6 s hold), capped
                    if (!assembled || AnyAllyInState(obs, self, BrainState.Flank)) _holdExtendedTo = now;
                    if (now - _holdStart >= _cfg.CentreHoldMaxSeconds || now - _holdExtendedTo >= _cfg.CentreHoldTimeoutSeconds)
                    { Enter(BrainState.Charge, now); return ReasonCode.CentreHoldTimeout; }
                    return ReasonCode.None;
                }

                case BrainState.Flank:
                {
                    if (liveAllies == 0) { Enter(BrainState.Charge, now); return ReasonCode.NoAlliesLeft; }
                    if (allyCharging) { Enter(BrainState.Charge, now); return ReasonCode.AllyCharging; }
                    float slot = Dir(tp, centroid) + side * _cfg.WingOffsetRad;
                    float err = MathF.Abs(Wrap(Dir(tp, self.Pos) - slot));
                    if (err <= _cfg.FlankBearingTolRad && EngagedSpread(obs, self, tp) >= _cfg.FlankSpreadMinRad)
                    { Enter(BrainState.Charge, now); return ReasonCode.FlankSlotReached; }
                    if (now >= _flankDeadline) { Enter(BrainState.Charge, now); return ReasonCode.FlankTimeout; }
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
