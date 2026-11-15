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

        public PackGoal Goal { get; private set; }
        public EntityId FocusTargetId { get; private set; }
        public EntityId RaidTargetId { get; private set; }
        public int EligibleCount => _n;
        /// <summary>Largest angular separation (rad) among the hunt slots in use; flank commit needs >= PackConfig.FlankSpreadMinRad.</summary>
        public float EngagedSpreadRad { get; private set; }
        public float HuntUtility { get; private set; }
        public float RaidUtility { get; private set; }
        public PackStats Stats;

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

        private float Dist(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }

        private void SelectGoal(double now, Float2 centroid, float strength, float speed)
        {
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
                float t = Dist(centroid, it.Pos) / speed;
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
            float ux = centroid.X - tp.X, uy = centroid.Y - tp.Y;
            float ul = MathF.Sqrt(ux * ux + uy * uy);
            if (ul < 1f) { ux = 1f; uy = 0f; ul = 1f; }
            ux /= ul; uy /= ul;
            float baseAng = MathF.Atan2(uy, ux);
            InfraTarget raidT = default;
            bool haveRaid = Goal == PackGoal.Raid && TryInfra(RaidTargetId, out raidT);

            int flankIdx = 0; float minAng = 0f, maxAng = 0f; int slotsUsed = 0;
            _dirN = _n;
            for (int i = 0; i < _n; i++)
            {
                ref readonly PackMemberState m = ref _el[i];
                PackDirective d = default;
                d.Valid = true; d.Goal = Goal; d.Role = _curRole[i];
                d.FocusTargetId = Goal == PackGoal.Raid && d.Role == PackRole.Raider ? RaidTargetId : FocusTargetId;
                switch (d.Role)
                {
                    case PackRole.Anchor:
                        if (Goal == PackGoal.Raid && haveRaid && !haveFocus)
                        {
                            // escort: stand between the pack and the raid target at standoff
                            float ex = centroid.X - raidT.Pos.X, ey = centroid.Y - raidT.Pos.Y;
                            float el = Math.Max(1f, MathF.Sqrt(ex * ex + ey * ey));
                            d.SlotPos = new Float2(raidT.Pos.X + ex / el * m.Species.StandoffM, raidT.Pos.Y + ey / el * m.Species.StandoffM);
                            d.FocusTargetId = RaidTargetId;
                        }
                        else d.SlotPos = At(tp, baseAng, m.Species.StandoffM);
                        d.AllowMelee = true;
                        Track(baseAng, ref minAng, ref maxAng, ref slotsUsed);
                        break;
                    case PackRole.Flanker:
                    {
                        float side = (flankIdx++ % 2 == 0) ? _flankSide : -_flankSide;
                        float a = baseAng + side * _cfg.WingOffsetRad;
                        d.SlotPos = At(tp, a, m.Species.StandoffM);
                        d.AllowMelee = true;
                        Track(side * _cfg.WingOffsetRad, ref minAng, ref maxAng, ref slotsUsed);
                        break;
                    }
                    case PackRole.Bait:
                        d.SlotPos = At(tp, baseAng, _cfg.BaitBandFrac * _cfg.MechWeaponRangeM);
                        d.AllowMelee = false;
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
        }

        private static void Track(float ang, ref float min, ref float max, ref int n)
        {
            if (n == 0) { min = max = ang; } else { if (ang < min) min = ang; if (ang > max) max = ang; }
            n++;
        }

        private static Float2 At(Float2 centre, float angle, float radius)
            => new Float2(centre.X + MathF.Cos(angle) * radius, centre.Y + MathF.Sin(angle) * radius);
    }
}
