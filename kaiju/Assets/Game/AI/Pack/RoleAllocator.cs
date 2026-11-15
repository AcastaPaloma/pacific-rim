using System;

namespace Game.AI.Pack
{
    /// <summary>
    /// Role allocation for heterogeneous packs of 1..5 (Q-204, R-103 4.2/4.3). Slots per (goal, size) are role sets; the allocator picks the
    /// member-to-slot assignment that maximises the summed bids (exact search, at most 5! = 120 leaves, no allocation).
    /// bid(i, role) = fit(species_i, role) x (0.5 + 0.5 hp_i) x pos_i x (incumbent bonus | switch cost).
    /// Ties resolve to the lower member index (members are sorted by EntityId ordinal), so the result is deterministic.
    /// </summary>
    public sealed class RoleAllocator
    {
        public const int MaxMembers = 5;
        private const int A = 1 << (int)PackRole.Anchor, F = 1 << (int)PackRole.Flanker, B = 1 << (int)PackRole.Bait, R = 1 << (int)PackRole.Raider;

        // R-103 4.2: hunt n=1 {A|F|B}; n=2 {A|F, F|B}; n=3 {A, F, F|B}; n=4 {A, F, B, A|F}; n=5 {A, F, F, B, A|F} (the flex slot is never a second Bait).
        private static readonly int[][] HuntSlots =
        {
            new int[0],
            new[] { A | F | B },
            new[] { A | F, F | B },
            new[] { A, F, F | B },
            new[] { A, F, B, A | F },
            new[] { A, F, F, B, A | F },
        };
        // R-103 4.2: raid n>=1 {Raider, +Anchor escort for n>=3}.
        private static readonly int[][] RaidSlots =
        {
            new int[0],
            new[] { R },
            new[] { R, R | F },
            new[] { R, A, R | F | B },
            new[] { R, A, R | F, F | B },
            new[] { R, A, R, F, B },
        };

        // scratch (single-threaded use, allocated once)
        private readonly float[,] _bid = new float[MaxMembers, MaxMembers];
        private readonly PackRole[,] _bidRole = new PackRole[MaxMembers, MaxMembers];
        private readonly int[] _cur = new int[MaxMembers];
        private readonly int[] _best = new int[MaxMembers];
        private float _bestTotal;
        private int _n;

        public static int SlotCount(PackGoal goal, int n) => SlotsFor(goal, n).Length;

        private static int[] SlotsFor(PackGoal goal, int n)
        {
            if (n < 0) n = 0; if (n > MaxMembers) n = MaxMembers;
            return goal == PackGoal.Raid ? RaidSlots[n] : HuntSlots[n];
        }

        /// <summary>Slot role mask for diagnostics and tests.</summary>
        public static int SlotMask(PackGoal goal, int n, int slot) => SlotsFor(goal, n)[slot];

        /// <summary>
        /// Allocate. members[0..n) are the eligible members sorted by EntityId; currentRole/roleSince hold each one's incumbent role.
        /// goalPoint is the position the roles are measured against (hunt focus or raid target). Writes outRole[0..n) and returns the total bid.
        /// </summary>
        public float Allocate(PackGoal goal, PackMemberState[] members, int n, PackRole[] currentRole, double[] roleSince,
            Float2 goalPoint, double now, in PackConfig cfg, PackRole[] outRole)
        {
            if (n > MaxMembers) n = MaxMembers;
            _n = n;
            int[] slots = SlotsFor(goal, n);
            for (int i = 0; i < n; i++)
            {
                float dx = members[i].Pos.X - goalPoint.X, dy = members[i].Pos.Y - goalPoint.Y;
                float pos = 1f / (1f + MathF.Sqrt(dx * dx + dy * dy) / cfg.PosScaleM);
                float hp = 0.5f + 0.5f * members[i].HpFrac;
                bool dwelling = currentRole[i] != PackRole.Free && now - roleSince[i] < cfg.RoleDwellSeconds;
                for (int s = 0; s < n; s++)
                {
                    float best = -1f; PackRole bestRole = PackRole.Free;
                    for (int r = (int)PackRole.Anchor; r <= (int)PackRole.Raider; r++)
                    {
                        if ((slots[s] & (1 << r)) == 0) continue;
                        float b = members[i].Species.Fit((PackRole)r) * hp * pos;
                        if (currentRole[i] == (PackRole)r) b *= cfg.BidMargin;
                        else if (dwelling) b *= 1f - cfg.SwitchCost;
                        if (b > best) { best = b; bestRole = (PackRole)r; }
                    }
                    _bid[i, s] = best; _bidRole[i, s] = bestRole;
                }
            }
            _bestTotal = -1f;
            Search(0, 0, 0f);
            for (int i = 0; i < n; i++) outRole[i] = n > 0 ? _bidRole[i, _best[i]] : PackRole.Free;
            return _bestTotal;
        }

        private void Search(int member, int usedMask, float total)
        {
            if (member == _n)
            {
                if (total > _bestTotal + 1e-6f) { _bestTotal = total; for (int i = 0; i < _n; i++) _best[i] = _cur[i]; }
                return;
            }
            for (int s = 0; s < _n; s++)
            {
                if ((usedMask & (1 << s)) != 0) continue;
                _cur[member] = s;
                Search(member + 1, usedMask | (1 << s), total + _bid[member, s]);
            }
        }
    }
}
