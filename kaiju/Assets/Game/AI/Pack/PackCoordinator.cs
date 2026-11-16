using System;
using Game.Core;

namespace Game.AI.Pack
{
    /// <summary>
    /// Per-pack coordinator (R-103 7), ticked by the host at about 1 Hz: shared board, goal selection (hunt vs infrastructure raid with
    /// hysteresis), role allocation (<see cref="RoleAllocator"/>), focus claim and per-member <see cref="PackDirective"/>.
    /// Deterministic for (seed, inputs): all iteration in EntityId order, no wall clock, no allocation after construction.
    /// Members that are dead or in Retreat/Withdrawn leave the allocation (forced reallocation, exempt from the thrash count).
    /// </summary>
    public sealed class PackCoordinator : IPackDirectiveSource
    {
        public const int MaxMembers = RoleAllocator.MaxMembers;

        private readonly IInfrastructureSource _infra;
        private readonly IRoutePlanner _route;
        private readonly RoleAllocator _alloc = new RoleAllocator();
        public readonly PackBoard Board = new PackBoard();

        private PackConfig _cfg = PackConfig.Default();
        private ulong _seed;
        private float _flankSide = 1f;

        // eligible members of this tick, sorted by EntityId
        private readonly PackMemberState[] _el = new PackMemberState[MaxMembers];
        private int _n;
        // role memory keyed by id (previous tick)
        private readonly EntityId[] _prevId = new EntityId[MaxMembers];
        private readonly PackRole[] _prevRole = new PackRole[MaxMembers];
        private readonly double[] _prevSince = new double[MaxMembers];
        private int _prevN;
        private readonly PackRole[] _curRole = new PackRole[MaxMembers];
        private readonly double[] _curSince = new double[MaxMembers];
        private readonly PackRole[] _newRole = new PackRole[MaxMembers];
        // directives
        private readonly EntityId[] _dirId = new EntityId[MaxMembers];
        private readonly PackDirective[] _dir = new PackDirective[MaxMembers];
        private int _dirN;

        private double _goalSince = double.NegativeInfinity, _focusLastClaim;
        private bool _anchorLatched, _huntBlocked;
        private bool _started;
        // hunt geometry latch and commit gate
        private float _baseAng; private EntityId _baseFocus; private bool _baseSet;
        private bool _gateOpen, _baitGo, _advance; private double _gateOpenAt; private double _stageNearSince = double.NegativeInfinity; private double _gateNearSince = double.NegativeInfinity, _baitSince = double.NegativeInfinity; private EntityId _gateFocus;
        private readonly EntityId[] _delayId = new EntityId[MaxMembers]; private readonly float[] _delayS = new float[MaxMembers]; private int _delayN; private float _joinExtraS;   // T-113c launch delays fixed at the gate

        /// <summary>Ablation switches of the M-007@v2 controls (R-103 5.3). Defaults are the candidate; frozen per run.</summary>
        public PackControl Control = PackControl.None;

        public PackGoal Goal { get; private set; }
        public EntityId FocusTargetId { get; private set; }
        public EntityId RaidTargetId { get; private set; }
        public int EligibleCount => _n;
        /// <summary>Largest angular separation (rad) among the hunt slots in use; flank commit needs >= PackConfig.FlankSpreadMinRad.</summary>
        public float EngagedSpreadRad { get; private set; }
        public float HuntUtility { get; private set; }
        public float RaidUtility { get; private set; }
        public PackStats Stats;
        public static Action<string> DebugSink;

        public PackCoordinator(IInfrastructureSource infra = null, IRoutePlanner route = null)
        {
            _infra = infra;
            _route = route ?? new StraightLineRoutePlanner();
        }

        public void Reset(ulong seed, in PackConfig cfg)
        {
            _cfg = cfg; _seed = seed;
            _flankSide = (Mix(seed) & 1UL) == 0UL ? 1f : -1f;
            Board.Clear();
            _n = 0; _prevN = 0; _dirN = 0;
            Goal = PackGoal.None; FocusTargetId = default; RaidTargetId = default;
            _goalSince = double.NegativeInfinity; _anchorLatched = false; _huntBlocked = false; _started = false;
            EngagedSpreadRad = 0f; HuntUtility = 0f; RaidUtility = 0f;
            _baseSet = false; _gateOpen = false; _baitGo = false; _advance = false; _stageNearSince = double.NegativeInfinity; _gateNearSince = double.NegativeInfinity; _baitSince = double.NegativeInfinity; _gateFocus = default;
            Stats = default;
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public bool TryGetDirective(EntityId id, out PackDirective d)
        {
            for (int i = 0; i < _dirN; i++) if (_dirId[i] == id) { d = _dir[i]; return d.Valid; }
            d = default; return false;
        }

        public PackRole RoleOf(EntityId id) => TryGetDirective(id, out var d) ? d.Role : PackRole.Free;

        /// <summary>One coordinator tick. members may be in any order and may include dead ones; at most MaxMembers eligible are used.</summary>
        public void Update(double now, PackMemberState[] members, int count)
        {
            Stats.Ticks++;
            // 1. eligible set sorted by EntityId
            _n = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly PackMemberState m = ref members[i];
                if (!m.Alive || !m.Id.IsValid || m.State == BrainState.Retreat || m.State == BrainState.Withdrawn) continue;
                if (_n >= MaxMembers) break;
                int j = _n++;
                while (j > 0 && _el[j - 1].Id.Value > m.Id.Value) { _el[j] = _el[j - 1]; j--; }
                _el[j] = m;
            }
            Board.Prune(now, _cfg.BoardStalenessSeconds);
            bool membershipChanged = _started && SetChanged();
            _dirN = 0;
            if (_n == 0) { Goal = PackGoal.None; SavePrev(now); _started = true; return; }

            float cx = 0, cy = 0, strength = 0, vmin = float.MaxValue;
            for (int i = 0; i < _n; i++)
            {
                cx += _el[i].Pos.X; cy += _el[i].Pos.Y;
                strength += _el[i].Species.HpRel * _el[i].HpFrac;
                if (_el[i].Species.CruiseMps < vmin) vmin = _el[i].Species.CruiseMps;
            }
            Float2 centroid = new Float2(cx / _n, cy / _n);
            if (strength < 1e-3f) strength = 1e-3f;

            float bestAnchor = 0f;
            for (int i = 0; i < _n; i++) if (_el[i].Species.FitAnchor > bestAnchor) bestAnchor = _el[i].Species.FitAnchor;
            // covering rule (R-103 4.3): a pack that fielded an Anchor and can no longer cover it (best fit < AnchorMinFit) does not hunt
            _huntBlocked = _anchorLatched && bestAnchor < _cfg.AnchorMinFit;

            UpdateClaims(now);
            PackGoal prevGoal = Goal;
            SelectGoal(now, centroid, strength, vmin);
            bool goalChanged = _started && Goal != prevGoal;
            if (goalChanged) Stats.GoalSwitches++;

            // 2. roles
            for (int i = 0; i < _n; i++) { _curRole[i] = PrevRole(_el[i].Id, out _curSince[i]); }
            Float2 goalPoint = GoalPoint(now, centroid);
            if (Goal == PackGoal.Regroup || Goal == PackGoal.None)
            {
                for (int i = 0; i < _n; i++) _newRole[i] = PackRole.Free;
            }
            else
            {
                _alloc.Allocate(Goal, _el, _n, _curRole, _curSince, goalPoint, now, _cfg, _newRole);
                if (Control != PackControl.None) ApplyControl(now);
            }
            if (Goal == PackGoal.Hunt && _n >= 2 && bestAnchor >= _cfg.AnchorMinFit) _anchorLatched = true;
            bool forced = membershipChanged || goalChanged || !_started;
            bool any = false;
            for (int i = 0; i < _n; i++)
            {
                if (_newRole[i] != _curRole[i])
                {
                    any = true;
                    Stats.RoleChanges++;
                    if (forced || _curRole[i] == PackRole.Free) Stats.ForcedRoleChanges++;
                    _curSince[i] = now;
                }
            }
            if (any) Stats.Reallocations++;
            for (int i = 0; i < _n; i++) _curRole[i] = _newRole[i];

            BuildDirectives(now, centroid);
            SavePrev(now);
            _started = true;
        }

        // ------------------------------------------------------------------ controls (R-103 5.3)

        private double _randomRoleAt = double.NegativeInfinity;
        private ulong _randomState;

        private void ApplyControl(double now)
        {
            switch (Control)
            {
                case PackControl.NoRoles:   // roles by species only, no auction, no bait
                    for (int i = 0; i < _n; i++)
                        _newRole[i] = Goal == PackGoal.Raid ? PackRole.Raider : (_el[i].Species.FitAnchor >= 0.5f ? PackRole.Anchor : PackRole.Flanker);
                    break;
                case PackControl.NoBait:    // Bait slots go to Flanker
                    for (int i = 0; i < _n; i++) if (_newRole[i] == PackRole.Bait) _newRole[i] = PackRole.Flanker;
                    break;
                case PackControl.RandomRole: // uniform every 20 s, held between draws
                    if (now - _randomRoleAt >= 20.0 || _randomRoleAt == double.NegativeInfinity)
                    {
                        _randomRoleAt = now;
                        for (int i = 0; i < _n; i++)
                        {
                            _randomState = Mix(_randomState + _seed + (ulong)i * 7919UL + (ulong)now);
                            _newRole[i] = (PackRole)(1 + (int)(_randomState % 4UL));
                        }
                    }
                    else for (int i = 0; i < _n; i++) _newRole[i] = _curRole[i] == PackRole.Free ? PackRole.Flanker : _curRole[i];
                    break;
            }
        }

        // ------------------------------------------------------------------ membership memory

        private bool SetChanged()
        {
            if (_prevN != _n) return true;
            for (int i = 0; i < _n; i++) if (_prevId[i] != _el[i].Id) return true;
            return false;
        }

        private PackRole PrevRole(EntityId id, out double since)
        {
            for (int i = 0; i < _prevN; i++) if (_prevId[i] == id) { since = _prevSince[i]; return _prevRole[i]; }
            since = double.NegativeInfinity; return PackRole.Free;
        }

        private void SavePrev(double now)
        {
            _prevN = _n;
            for (int i = 0; i < _n; i++) { _prevId[i] = _el[i].Id; _prevRole[i] = _curRole[i]; _prevSince[i] = _curSince[i]; }
        }

        // ------------------------------------------------------------------ claims and focus

        private void UpdateClaims(double now)
        {
            for (int b = 0; b < Board.Count; b++)
            {
                int c = 0;
                for (int i = 0; i < _n; i++) if (_el[i].TargetId == Board[b].Id) c++;
                Board.SetClaims(b, c);
            }
        }

        private void SelectFocus(double now, Float2 centroid)
        {
            int cur = FocusTargetId.IsValid ? Board.IndexOf(FocusTargetId) : -1;
            if (cur >= 0)
            {
                if (Board[cur].Claims > 0) _focusLastClaim = now;
                if (Board[cur].Alive && (Board[cur].Claims > 0 || now - _focusLastClaim <= _cfg.FocusClaimExpirySeconds)) return;
            }
            int best = -1; float bestScore = float.NegativeInfinity;
            for (int b = 0; b < Board.Count; b++)
            {
                if (!Board[b].Alive) continue;
                Float2 p = Board.ExtrapolatedPos(b, now);
                float dx = p.X - centroid.X, dy = p.Y - centroid.Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float score = Board[b].Claims * 1000f + (1f / (1f + d / 1000f)) * (2f - Board[b].HpFrac);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            FocusTargetId = best >= 0 ? Board[best].Id : default;
            _focusLastClaim = now;
        }

        // ------------------------------------------------------------------ goal selection (R-103 3.3)

        private float ClassWeight(byte c)
        {
            switch (c)
            {
                case 0: return _cfg.WeightCrane;
                case 1: return _cfg.WeightPower;
                case 2: return _cfg.WeightBridge;
                default: return _cfg.WeightAirport;
            }
        }

        // Pack-level path distance for goal choice: the most restricting member (tallest) walks the route; straight line unless RoutePlanning.
        private float PathDist(Float2 a, Float2 b)
        {
            if (!_cfg.RoutePlanning || _n == 0) return Dist(a, b);
            int w = 0;
            for (int i = 1; i < _n; i++) if (_el[i].Species.HeightM > _el[w].Species.HeightM) w = i;
            float d = _route.PathLength(a, b, _el[w].Species);
            return float.IsInfinity(d) ? 1e7f : d;   // unreachable for the tallest member: a very long way, not a NaN utility
        }

        private float TransitDist(int i)
        {
            if (!_cfg.RoutePlanning) return Dist(_el[i].Pos, _dir[i].SlotPos);
            float d = _route.PathLength(_el[i].Pos, _dir[i].SlotPos, _el[i].Species);
            return float.IsInfinity(d) ? Dist(_el[i].Pos, _dir[i].SlotPos) : d;
        }

        private float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }

        private void SelectGoal(double now, Float2 centroid, float strength, float speed)
        {
            if (Control == PackControl.NearestInfra) { SelectGoalNearest(now, centroid); return; }
            // hunt utility over the aged board
            float uh = 0f; bool haveMechs = Board.Count > 0;
            if (haveMechs)
            {
                float value = 0f, enemy = 0f, nearest = float.MaxValue;
                for (int b = 0; b < Board.Count; b++)
                {
                    value += _cfg.MechValue * Board[b].HpFrac;
                    enemy += _cfg.MechStrength * Board[b].HpFrac;
                    float d = Dist(centroid, Board.ExtrapolatedPos(b, now));
                    if (d < nearest) nearest = d;
                }
                float p = strength / (strength + enemy);
                float risk = (1f - p) * strength * _cfg.RiskScale;
                float t = nearest / speed;
                float h = _cfg.HuntHandlingBaseSeconds * enemy / strength;
                uh = (value * p - risk) / (t + h);
            }
            HuntUtility = uh;

            // raid utility: best infrastructure target; stickiness on the current one
            int bestInfra = -1; float ur = float.NegativeInfinity; float urCur = float.NegativeInfinity; int curInfra = -1;
            int raiders = Math.Max(1, _n / 2);
            int ic = _infra != null ? _infra.Count : 0;
            for (int k = 0; k < ic; k++)
            {
                InfraTarget it = _infra.Get(k);
                if (!it.Alive) continue;
                float enemyNear = 0f;
                for (int b = 0; b < Board.Count; b++)
                    if (Dist(it.Pos, Board.ExtrapolatedPos(b, now)) <= _cfg.DefenseRadiusM) enemyNear += _cfg.MechStrength * Board[b].HpFrac;
                float p = strength / (strength + enemyNear);
                float risk = (1f - p) * strength * _cfg.RiskScale;
                float t = PathDist(centroid, it.Pos) / speed;
                float h = it.Hp / (_cfg.RaidDamageRate * raiders);
                float u = (ClassWeight(it.Class) * p - risk) / (t + h);
                if (it.Id == RaidTargetId) { curInfra = k; urCur = u; }
                if (u > ur || (u == ur && bestInfra >= 0 && it.Id.Value < _infra.Get(bestInfra).Id.Value)) { ur = u; bestInfra = k; }
            }
            RaidUtility = bestInfra >= 0 ? ur : 0f;
            if (bestInfra >= 0)
            {
                // keep the current raid target unless the best beats it by the switch ratio
                if (curInfra >= 0 && curInfra != bestInfra && !(ur > Thresh(urCur))) { bestInfra = curInfra; ur = urCur; RaidUtility = ur; }
            }

            // choose goal
            PackGoal want;
            float uWant;
            bool canHunt = haveMechs && !_huntBlocked;
            bool canRaid = bestInfra >= 0;
            if (!canHunt && !canRaid) { want = haveMechs ? PackGoal.Regroup : PackGoal.None; uWant = 0f; }
            else if (!canRaid) { want = uh > 0f ? PackGoal.Hunt : PackGoal.Regroup; uWant = uh; }
            else if (!canHunt) { want = PackGoal.Raid; uWant = ur; }
            else if (uh <= 0f && ur <= 0f) { want = PackGoal.Regroup; uWant = 0f; }
            else if (ur > uh) { want = PackGoal.Raid; uWant = ur; }
            else { want = PackGoal.Hunt; uWant = uh; }

            PackGoal cur = Goal;
            bool curValid = cur == PackGoal.Hunt ? canHunt
                          : cur == PackGoal.Raid ? (RaidTargetId.IsValid && InfraAlive(RaidTargetId))
                          : cur == PackGoal.Regroup ? (now - _goalSince < _cfg.RegroupMinSeconds)
                          : false;
            bool change;
            if (want == cur) change = false;
            else if (!curValid) change = true;
            else
            {
                float uCur = cur == PackGoal.Hunt ? uh : cur == PackGoal.Raid ? urCur : 0f;
                if (cur == PackGoal.Raid && urCur == float.NegativeInfinity) uCur = ur;
                change = now - _goalSince >= _cfg.GoalDwellSeconds && uWant > Thresh(uCur);
            }
            if (change)
            {
                Goal = want; _goalSince = now;
                if (want == PackGoal.Hunt) FocusTargetId = default;
            }
            else if (!_started) { _goalSince = now; }

            if (Goal == PackGoal.Raid && bestInfra >= 0) RaidTargetId = _infra.Get(bestInfra).Id;
            if (Goal == PackGoal.Hunt || (Goal == PackGoal.Raid && haveMechs)) SelectFocus(now, centroid);
            else if (!haveMechs) FocusTargetId = default;
        }

        // nearestinfra@1 (R-103 5.3): goal = nearest living F-044 target, no utility, no hysteresis; mechs only when no infrastructure is left.
        private void SelectGoalNearest(double now, Float2 centroid)
        {
            int best = -1; float bd = float.MaxValue;
            int ic = _infra != null ? _infra.Count : 0;
            for (int k = 0; k < ic; k++)
            {
                InfraTarget it = _infra.Get(k);
                if (!it.Alive) continue;
                float d = PathDist(centroid, it.Pos);
                if (d < bd || (d == bd && best >= 0 && it.Id.Value < _infra.Get(best).Id.Value)) { bd = d; best = k; }
            }
            PackGoal want = best >= 0 ? PackGoal.Raid : (Board.Count > 0 ? PackGoal.Hunt : PackGoal.None);
            if (want != Goal) { Goal = want; _goalSince = now; if (want == PackGoal.Hunt) FocusTargetId = default; }
            else if (!_started) _goalSince = now;
            if (Goal == PackGoal.Raid) RaidTargetId = _infra.Get(best).Id;
            HuntUtility = 0f; RaidUtility = 0f;
            if (Board.Count > 0 && Goal != PackGoal.None) SelectFocus(now, centroid); else FocusTargetId = default;
        }

        private float Thresh(float uCur) => uCur > 0f ? uCur * _cfg.GoalSwitchRatio : uCur + (_cfg.GoalSwitchRatio - 1f) * Math.Abs(uCur);

        private bool InfraAlive(EntityId id)
        {
            int ic = _infra != null ? _infra.Count : 0;
            for (int k = 0; k < ic; k++) { InfraTarget it = _infra.Get(k); if (it.Id == id) return it.Alive; }
            return false;
        }

        private bool TryInfra(EntityId id, out InfraTarget t)
        {
            int ic = _infra != null ? _infra.Count : 0;
            for (int k = 0; k < ic; k++) { t = _infra.Get(k); if (t.Id == id) return true; }
            t = default; return false;
        }

        // ------------------------------------------------------------------ directives

        private Float2 GoalPoint(double now, Float2 centroid)
        {
            if (Goal == PackGoal.Raid && TryInfra(RaidTargetId, out var it)) return it.Pos;
            int f = FocusTargetId.IsValid ? Board.IndexOf(FocusTargetId) : -1;
            if (f >= 0) return Board.ExtrapolatedPos(f, now);
            return centroid;
        }

        private void BuildDirectives(double now, Float2 centroid)
        {
            // rally point for Regroup: healthiest eligible member (fit-independent), lower id on ties
            int rally = 0;
            for (int i = 1; i < _n; i++) if (_el[i].HpFrac > _el[rally].HpFrac + 1e-6f) rally = i;

            // hunt geometry
            int fi = FocusTargetId.IsValid ? Board.IndexOf(FocusTargetId) : -1;
            bool haveFocus = fi >= 0;
            Float2 tp = haveFocus ? Board.ExtrapolatedPos(fi, now) : centroid;
            // Bay: a raiding pack escorts its Raiders at the raid target instead of splitting off to chase a mech it cannot take alone
            InfraTarget raidT = default;
            bool haveRaid = Goal == PackGoal.Raid && TryInfra(RaidTargetId, out raidT);
            bool raidGeo = _cfg.BayTactics && haveRaid;
            if (raidGeo) { tp = raidT.Pos; haveFocus = false; }
            float ux = centroid.X - tp.X, uy = centroid.Y - tp.Y;
            float ul = MathF.Sqrt(ux * ux + uy * uy);
            if (ul < 1f) { ux = 1f; uy = 0f; ul = 1f; }
            ux /= ul; uy /= ul;
            float baseAng = MathF.Atan2(uy, ux);
            // latch the approach bearing: while the pack is far it tracks; inside LatchReleaseM it freezes, so the slots stop rotating
            // around the target when the members themselves move the centroid (feedback orbit)
            if (haveFocus && _cfg.BayTactics)
            {
                if (!_baseSet || _baseFocus != FocusTargetId || ul > _cfg.LatchReleaseM) { _baseAng = baseAng; _baseFocus = FocusTargetId; _baseSet = true; }
                baseAng = _baseAng;
            }
            else _baseSet = false;

            int flankIdx = 0, anchorIdx = 0; float minAng = 0f, maxAng = 0f; int slotsUsed = 0;
            int nFlank = 0, nAnchor = 0, nBait = 0;
            for (int i = 0; i < _n; i++) { if (_curRole[i] == PackRole.Flanker) nFlank++; else if (_curRole[i] == PackRole.Anchor) nAnchor++; else if (_curRole[i] == PackRole.Bait) nBait++; }
            float holdR = _cfg.BaitBandFrac * _cfg.MechWeaponRangeM + 40f;   // Anchor/Flanker never stand nearer to a mech than the Bait does
            // T-113b bait-first: strikers on one side of the axis outside weapon range, the Bait wide on the other side (every striker >= 90 deg from it)
            bool baitFirst = _cfg.BayTactics && _cfg.BaitFirst && Goal == PackGoal.Hunt && haveFocus && nBait > 0 && nAnchor + nFlank > 0;
            int nStrike = nAnchor + nFlank, strikeIdx = 0;
            _dirN = _n;
            for (int i = 0; i < _n; i++)
            {
                ref readonly PackMemberState m = ref _el[i];
                PackDirective d = default;
                d.Valid = true; d.Goal = Goal; d.Role = _curRole[i];
                d.FocusTargetId = Goal == PackGoal.Raid && d.Role == PackRole.Raider ? RaidTargetId : FocusTargetId;
                if (baitFirst && (d.Role == PackRole.Anchor || d.Role == PackRole.Flanker || d.Role == PackRole.Bait))
                {
                    float ang, r;
                    if (d.Role == PackRole.Bait)
                    {
                        ang = -_flankSide * _cfg.BaitWideOffsetRad;
                        r = StageScale(m.Species, _baitGo ? _cfg.BaitBandFrac * _cfg.MechWeaponRangeM : _cfg.MechWeaponRangeM * 1.05f);
                        d.AllowMelee = false;
                    }
                    else
                    {
                        // anchors take the slots nearest the axis, flankers the wide ones (striker order = EntityId order within each role)
                        int j = d.Role == PackRole.Anchor ? AnchorOrdinal(i) : nAnchor + FlankOrdinal(i);
                        float u = nStrike > 1 ? (float)j / (nStrike - 1) : (d.Role == PackRole.Flanker ? 1f : 0f);
                        ang = _flankSide * (_cfg.StrikeArcMinRad + u * (_cfg.StrikeArcMaxRad - _cfg.StrikeArcMinRad));
                        // outside the mech's weapon range (only the Bait is shot before the strike); T-113b iteration 3 also held outside the member's own
                        // engagement radius and was rejected on data (C1 0.85 -> 0.49, P4 bs 0.60 -> 0.04): tasks/T-113b.progress.md
                        float hr = Math.Max(SlotRadius(m.Species, holdR), _cfg.MechWeaponRangeM + _cfg.HoldOutsideMarginM);
                        if (_cfg.AnchorHoldM > 0f && d.Role == PackRole.Anchor) hr = Math.Max(_cfg.MechWeaponRangeM + _cfg.HoldOutsideMarginM, Math.Min(hr, _cfg.AnchorHoldM));
                        r = StageScale(m.Species, hr);
                        d.CommitRangeM = hr + 80f;
                        d.AllowMelee = true;
                        strikeIdx++;
                    }
                    d.SlotPos = At(tp, baseAng + ang, r);
                    Track(ang, ref minAng, ref maxAng, ref slotsUsed);
                    d.RoutePos = _route.NextWaypoint(m.Pos, d.SlotPos, m.Species);
                    _dirId[i] = m.Id; _dir[i] = d;
                    continue;
                }
                switch (d.Role)
                {
                    case PackRole.Anchor:
                        if (Goal == PackGoal.Raid && haveRaid && (raidGeo || !haveFocus))
                        {
                            // escort: stand between the pack and the raid target at standoff
                            float ex = centroid.X - raidT.Pos.X, ey = centroid.Y - raidT.Pos.Y;
                            float el = Math.Max(1f, MathF.Sqrt(ex * ex + ey * ey));
                            d.SlotPos = new Float2(raidT.Pos.X + ex / el * m.Species.StandoffM, raidT.Pos.Y + ey / el * m.Species.StandoffM);
                            d.FocusTargetId = RaidTargetId;
                        }
                        else
                        {
                            // no flanker: Anchors are the wings; one flanker: Anchors stand off the opposite shoulder; two or more: centre, then either side of it.
                            // Every combination keeps the engaged spread above 90 degrees with the Bait on the shoulder opposite the first flanker.
                            int ai = anchorIdx++;
                            float off = !_cfg.BayTactics ? 0f : nFlank == 0 ? (ai == 0 ? _flankSide * _cfg.WingOffsetRad : ai == 1 ? -_flankSide * _cfg.WingOffsetRad : 0f)
                                      : nFlank == 1 ? -_flankSide * _cfg.AnchorOffsetRad * (1 + ai)
                                      : (ai == 0 ? 0f : (ai % 2 == 1 ? _flankSide : -_flankSide) * 0.35f);
                            d.SlotPos = At(tp, baseAng + off, StageScale(m.Species, SlotRadius(m.Species, holdR)));
                            Track(off, ref minAng, ref maxAng, ref slotsUsed);
                        }
                        d.AllowMelee = true;
                        break;
                    case PackRole.Flanker:
                    {
                        float side = (flankIdx++ % 2 == 0) ? _flankSide : -_flankSide;
                        float a = baseAng + side * _cfg.WingOffsetRad;
                        d.SlotPos = At(tp, a, StageScale(m.Species, SlotRadius(m.Species, holdR)));
                        d.AllowMelee = true;
                        Track(side * _cfg.WingOffsetRad, ref minAng, ref maxAng, ref slotsUsed);
                        break;
                    }
                    case PackRole.Bait:
                        // the Bait stages just outside mech range until every holder is on its slot, then steps into the band to draw the lock
                        d.SlotPos = At(tp, baseAng - (_cfg.BayTactics ? _flankSide * _cfg.BaitOffsetRad : 0f), StageScale(m.Species, _baitGo || !_cfg.BayTactics ? _cfg.BaitBandFrac * _cfg.MechWeaponRangeM : _cfg.MechWeaponRangeM * 1.05f));
                        d.AllowMelee = false;
                        if (_cfg.BayTactics) Track(-_flankSide * _cfg.BaitOffsetRad, ref minAng, ref maxAng, ref slotsUsed);
                        break;
                    case PackRole.Raider:
                        if (haveRaid)
                        {
                            d.SlotPos = raidT.Pos;
                            d.RoutePos = _route.NextWaypoint(m.Pos, raidT.Pos, m.Species);
                        }
                        d.AllowMelee = false;
                        break;
                    default: // Free (Regroup / None)
                        d.SlotPos = _el[rally].Pos;
                        d.RoutePos = _el[rally].Pos;
                        d.AllowMelee = false;
                        break;
                }
                if (d.Role != PackRole.Raider && d.Role != PackRole.Free) d.RoutePos = _route.NextWaypoint(m.Pos, d.SlotPos, m.Species);
                _dirId[i] = m.Id; _dir[i] = d;
            }
            EngagedSpreadRad = slotsUsed >= 2 ? maxAng - minAng : 0f;
            if (_cfg.BayTactics) UpdateGate(now, tp, haveFocus && Goal == PackGoal.Hunt, baitFirst);
            if (_cfg.BayTactics && _cfg.PaceMinFrac > 0f && Goal == PackGoal.Hunt && haveFocus && !_advance)
            {
                // synchronised arrival: every member walks so that it reaches its (stage) slot when the slowest member does
                float tmax = 1f;
                for (int i = 0; i < _n; i++) { float t = TransitDist(i) / Math.Max(1f, _el[i].Species.CruiseMps); if (t > tmax) tmax = t; }
                for (int i = 0; i < _n; i++)
                {
                    float t = TransitDist(i) / Math.Max(1f, _el[i].Species.CruiseMps);
                    _dir[i].PaceFrac = Math.Min(1f, Math.Max(_cfg.PaceMinFrac, t / tmax));
                }
            }
            for (int i = 0; i < _n; i++)
            {
                _dir[i].CommitReady = !_cfg.BayTactics || Goal != PackGoal.Hunt || _gateOpen;
                _dir[i].GateOpenAt = _gateOpenAt; _dir[i].BaitFirst = baitFirst;
                _dir[i].BaitGo = _baitGo;
                _dir[i].BaitJoin = _cfg.BayTactics && _cfg.BaitJoinDelaySeconds > 0f && Goal == PackGoal.Hunt && _gateOpen && now - _gateOpenAt >= (baitFirst && _cfg.AnchorLead ? _joinExtraS : _cfg.BaitJoinDelaySeconds);
                if (baitFirst && _cfg.AnchorLead && _gateOpen) for (int q = 0; q < _delayN; q++) if (_delayId[q] == _dirId[i]) { _dir[i].CommitDelayS = _delayS[q]; break; }
            }
            if (DebugSink != null && Goal == PackGoal.Hunt)
                for (int i = 0; i < _n; i++)
                    DebugSink($"t={now:F0} id={_dirId[i].Value} {_dir[i].Role} d_tgt={Dist(_el[i].Pos, tp):F0} d_slot={Dist(_el[i].Pos, _dir[i].SlotPos):F0} gate={_gateOpen} adv={_advance} baitGo={_baitGo} near={_gateNearSince:F0} bait={_baitSince:F0}");
        }

        // Commit gate (hunt): Anchor and Flanker hold their slots until every one of them is within tolerance of its slot and the Bait (if any)
        // has been inside its band long enough to draw the lock, or until GateTimeoutSeconds after the first member came near. Reset by a new focus.
        private void UpdateGate(double now, Float2 tp, bool hunting, bool baitFirst = false)
        {
            if (!hunting) { _gateOpen = false; _baitGo = false; _advance = false; _stageNearSince = double.NegativeInfinity; _gateNearSince = double.NegativeInfinity; _baitSince = double.NegativeInfinity; _gateFocus = default; return; }
            if (_gateFocus != FocusTargetId) { _gateFocus = FocusTargetId; _gateOpen = false; _baitGo = false; _advance = false; _stageNearSince = double.NegativeInfinity; _gateNearSince = double.NegativeInfinity; _baitSince = double.NegativeInfinity; }
            if (_gateOpen) { _baitGo = !(baitFirst && _cfg.BaitTimed) || AnchorEtaToRange(tp) <= _cfg.BaitLeadS; return; }
            if (!_advance)
            {
                // ring up: advance when every member holds its (wide) stage slot, or StageTimeoutSeconds after all of them are within twice the stage radius
                bool allStaged = true, allClose = true;
                for (int i = 0; i < _n; i++)
                {
                    ref readonly PackMemberState m = ref _el[i];
                    float rs = Dist(tp, _dir[i].SlotPos);
                    if (Dist(m.Pos, _dir[i].SlotPos) > Math.Max(_cfg.SlotTolMinM, _cfg.SlotTolFrac * rs * 0.5f)) allStaged = false;
                    if (Dist(m.Pos, tp) > 2f * rs) allClose = false;
                }
                if (allClose && double.IsNegativeInfinity(_stageNearSince)) _stageNearSince = now;
                if (allStaged || (!double.IsNegativeInfinity(_stageNearSince) && now - _stageNearSince >= _cfg.StageTimeoutSeconds)) _advance = true;
                _baitGo = false;
                return;
            }
            bool timedLead = baitFirst && _cfg.BaitTimed;   // T-113c: the Bait's draw is timed against the Anchor's run, after the gate
            bool allHeld = true, holdersNear = true, allNear = true, haveBait = false, baitDrawing = false, haveHolder = false;
            float band = _cfg.BaitBandFrac * _cfg.MechWeaponRangeM;
            for (int i = 0; i < _n; i++)
            {
                ref readonly PackMemberState m = ref _el[i];
                float dt = Dist(m.Pos, tp);
                PackRole r = _dir[i].Role;
                if (r == PackRole.Flanker || r == PackRole.Anchor)   // the heavy Anchor holds a looser tolerance (it closes slowly and charges from further out)
                {
                    haveHolder = true;
                    // bait-first: every striker (the Anchor too) must really be on its hold slot before the Bait steps in and draws the lock
                    float tol = Math.Max(_cfg.SlotTolMinM, _cfg.SlotTolFrac * m.Species.StandoffM) * (r == PackRole.Anchor && !(baitFirst && _cfg.StrictAnchorTol) ? _cfg.AnchorTolMul : 1f);
                    float ds = Dist(m.Pos, _dir[i].SlotPos);
                    if (ds > tol) allHeld = false;
                    if (ds > _cfg.BaitStepInM) holdersNear = false;
                }
                else if (r == PackRole.Bait)
                {
                    haveBait = true;
                    if (dt <= band * 1.15f) baitDrawing = true;
                }
                if (dt > SlotRadius(m.Species, band + 40f) * _cfg.CommitRangeFracFor(r)) allNear = false;
            }
            if (allNear && double.IsNegativeInfinity(_gateNearSince)) _gateNearSince = now;
            if (haveBait)
            {
                if (baitDrawing) { if (double.IsNegativeInfinity(_baitSince)) _baitSince = now; }
                else _baitSince = double.NegativeInfinity;
            }
            _baitGo = timedLead ? false : (!haveHolder || allHeld || holdersNear);
            bool baitOk = !haveBait || (!double.IsNegativeInfinity(_baitSince) && now - _baitSince >= _cfg.BaitDrawSeconds);
            bool ready = timedLead ? (!haveHolder || allHeld) : haveHolder ? allHeld && baitOk : baitOk;
            bool timeout = !double.IsNegativeInfinity(_gateNearSince) && now - _gateNearSince >= _cfg.GateTimeoutSeconds;
            if ((allNear && ready) || timeout) { _gateOpen = true; _gateOpenAt = now; if (baitFirst && _cfg.AnchorLead) ComputeLaunchDelays(tp); }
        }

        // T-113c: seconds until the nearest Anchor reaches the mech's weapon range (running at charge speed); 0 when there is none (the Bait then goes at once).
        private float AnchorEtaToRange(Float2 tp)
        {
            float best = float.MaxValue; bool any = false;
            for (int i = 0; i < _n; i++)
                if (_curRole[i] == PackRole.Anchor) { any = true; float e = Math.Max(0f, Dist(_el[i].Pos, tp) - _cfg.MechWeaponRangeM) / Math.Max(1f, _el[i].Species.ChargeMps); if (e < best) best = e; }
            return any ? best : 0f;
        }

        // T-113c: time for a kaiju at rest to cover d metres to melee reach at charge speed with a constant acceleration (the harness and body model).
        private float Eta(float d, float v)
        {
            float a = Math.Max(0.1f, _cfg.AccelMps2), dAcc = v * v / (2f * a);
            return d <= 0f ? 0f : d <= dAcc ? MathF.Sqrt(2f * d / a) : v / a + (d - dAcc) / v;
        }

        private void ComputeLaunchDelays(Float2 tp)
        {
            float tmax = 0f;
            for (int i = 0; i < _n; i++)
            {
                PackRole r = _dir[i].Role;
                if (r != PackRole.Anchor && r != PackRole.Flanker && r != PackRole.Bait) continue;
                float e = Eta(Dist(_el[i].Pos, tp) - _el[i].Species.MeleeReachM, _el[i].Species.ChargeMps);
                if (r != PackRole.Bait && e > tmax) tmax = e;
            }
            _delayN = 0; _joinExtraS = 0f;
            for (int i = 0; i < _n; i++)
            {
                PackRole r = _dir[i].Role;
                if (r != PackRole.Anchor && r != PackRole.Flanker) continue;
                float e = Eta(Dist(_el[i].Pos, tp) - _el[i].Species.MeleeReachM, _el[i].Species.ChargeMps);
                float dl = Math.Max(0f, tmax - e + (e < tmax - 0.5f ? _cfg.AnchorLeadLagS : 0f));
                _delayId[_delayN] = _el[i].Id; _delayS[_delayN] = dl; _delayN++;
                if (dl > _joinExtraS) _joinExtraS = dl;
            }
        }

        // Engagement slot radius: the species standoff, never nearer to a mech than the Bait, and always inside the metric's engagement radius
        // (5 x body height: 300 / 500 / 800 m, R-103 5.1) so a member holding its slot counts as engaged.
        private float SlotRadius(in SpeciesRole sp, float holdR) => !_cfg.BayTactics ? sp.StandoffM : Math.Min(Math.Max(sp.StandoffM, holdR), 0.95f * 5f * sp.HeightM);

        // Before the advance every slot sits on a wider ring (StageFrac x, and outside the member's engagement radius): the pack rings up, then engages together.
        private float StageScale(in SpeciesRole sp, float r) => _advance || !_cfg.BayTactics ? r : Math.Max(r * _cfg.StageFrac, 1.15f * 5f * sp.HeightM);

        private int AnchorOrdinal(int i) { int k = 0; for (int j = 0; j < i; j++) if (_curRole[j] == PackRole.Anchor) k++; return k; }
        private int FlankOrdinal(int i) { int k = 0; for (int j = 0; j < i; j++) if (_curRole[j] == PackRole.Flanker) k++; return k; }

        private static void Track(float ang, ref float min, ref float max, ref int n)
        {
            if (n == 0) { min = max = ang; } else { if (ang < min) min = ang; if (ang > max) max = ang; }
            n++;
        }

        private static Float2 At(Float2 centre, float angle, float radius)
            => new Float2(centre.X + MathF.Cos(angle) * radius, centre.Y + MathF.Sin(angle) * radius);
    }
}
