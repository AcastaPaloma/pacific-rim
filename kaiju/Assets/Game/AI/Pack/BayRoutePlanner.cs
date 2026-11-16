// T-115c: Bay route planner for packs (R-103 6.1). Grid A* on a depth / soft-ground cost field, string-pulled, cached per (species height, start cell, goal cell).
// The cost field is an interface: the Track 1 bake (T-102 depth raster + liquefaction softness) plugs in through an adapter in Composition; until it exists the
// m007_bay harness supplies a synthetic field (water ellipse, shelf, deep channel). Pure C#, no engine types.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.AI.Pack
{
    /// <summary>
    /// Regular grid of traversability inputs over a local East-North metre frame (cell (ix, iy) covers [OriginX + ix*CellM, +CellM) x [OriginY + iy*CellM, +CellM)).
    /// Implemented by the Track 1 bake adapter (250 m depth/soft-ground raster, R-103 6.1) and by the synthetic Bay of the m007_bay harness.
    /// </summary>
    public interface IBayCostField
    {
        float OriginX { get; }
        float OriginY { get; }
        float CellM { get; }
        int Nx { get; }
        int Ny { get; }
        /// <summary>Water depth in metres at the cell (0 on land).</summary>
        float DepthM(int ix, int iy);
        /// <summary>Soft-ground factor 0..1 (liquefiable fill / Bay mud; R-103 sec. 7), 0 on firm ground.</summary>
        float Softness(int ix, int iy);
    }

    /// <summary>
    /// A* route planner on an <see cref="IBayCostField"/>. A cell is blocked for a species when depth > <see cref="DepthOverHeight"/> x height (R-103 3.2 Assumed rule);
    /// other cells cost 1 + <see cref="SoftCost"/> x softness per metre. Not thread safe: use one instance per run (the field itself is read only and may be shared).
    /// </summary>
    public sealed class GridRoutePlanner : IRoutePlanner
    {
        public const float DefaultDepthOverHeight = 0.4f, DefaultSoftCost = 1.5f;
        public readonly float DepthOverHeight, SoftCost;
        /// <summary>Number of A* searches run (tests and the cost report).</summary>
        public int Searches { get; private set; }
        /// <summary>T-124c profile (read-only, no behaviour effect): wall ms of the A* searches (total, longest single, count over 5 ms). Reset with <see cref="ResetSearchProfile"/>.</summary>
        public double SearchMsTotal { get; private set; }
        public double SearchMsMax { get; private set; }
        public int SearchesOver5Ms { get; private set; }
        public void ResetSearchProfile() { SearchMsTotal = 0; SearchMsMax = 0; SearchesOver5Ms = 0; }

        private readonly IBayCostField _f;
        private readonly int _nx, _ny;
        private readonly float _ox, _oy, _cell;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private int _gen;
        private readonly Heap _open = new Heap();
        private readonly Dictionary<Key, Route> _cache = new Dictionary<Key, Route>();

        private readonly struct Key : IEquatable<Key>
        {
            public readonly int H, Start, Goal;
            public Key(int h, int s, int g) { H = h; Start = s; Goal = g; }
            public bool Equals(Key o) => H == o.H && Start == o.Start && Goal == o.Goal;
            public override bool Equals(object o) => o is Key k && Equals(k);
            public override int GetHashCode() => (H * 73856093) ^ (Start * 19349663) ^ (Goal * 83492791);
        }

        private sealed class Route { public Float2[] Pts; public float Length; }   // Pts null = no path; Length is the soft-weighted effective length

        public GridRoutePlanner(IBayCostField field, float depthOverHeight = DefaultDepthOverHeight, float softCost = DefaultSoftCost)
        {
            _f = field; DepthOverHeight = depthOverHeight; SoftCost = softCost;
            _nx = field.Nx; _ny = field.Ny; _ox = field.OriginX; _oy = field.OriginY; _cell = field.CellM;
            _g = new float[_nx * _ny]; _parent = new int[_nx * _ny]; _stamp = new int[_nx * _ny];
        }

        // ------------------------------------------------------------------ cell queries

        private bool InGrid(Float2 p) => p.X >= _ox && p.Y >= _oy && p.X < _ox + _nx * _cell && p.Y < _oy + _ny * _cell;
        private int CellOf(Float2 p) { int ix = (int)((p.X - _ox) / _cell), iy = (int)((p.Y - _oy) / _cell); return iy * _nx + ix; }
        private Float2 Centre(int c) => new Float2(_ox + (c % _nx + 0.5f) * _cell, _oy + (c / _nx + 0.5f) * _cell);
        private bool Blocked(int c, float limitM) => _f.DepthM(c % _nx, c / _nx) > limitM;
        private bool BlockedAt(float x, float y, float limitM)
        {
            int ix = (int)((x - _ox) / _cell), iy = (int)((y - _oy) / _cell);
            return ix >= 0 && iy >= 0 && ix < _nx && iy < _ny && _f.DepthM(ix, iy) > limitM;
        }
        private float Mult(int c) => 1f + SoftCost * _f.Softness(c % _nx, c / _nx);

        /// <summary>Soft-weighted length of the straight segment, or NaN when it crosses a blocked cell. Samples every 0.2 cell.</summary>
        private float SegmentCost(Float2 a, Float2 b, float limitM)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return 0f;
            int n = Math.Max(1, (int)MathF.Ceiling(len / (0.2f * _cell)));
            float sum = 0f;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                var p = new Float2(a.X + dx * t, a.Y + dy * t);
                if (!InGrid(p)) { sum += 1f; continue; }
                int c = CellOf(p);
                if (Blocked(c, limitM)) return float.NaN;
                // 0.15 cell clearance (not applied in the first half cell, where the segment starts inside the mover's own cell): a segment may not clip the corner of a blocked cell (a kaiju stops at the first blocked cell it enters)
                float along = t * len, m = 0.15f * _cell;
                if (along >= 0.5f * _cell && (BlockedAt(p.X + m, p.Y, limitM) || BlockedAt(p.X - m, p.Y, limitM) || BlockedAt(p.X, p.Y + m, limitM) || BlockedAt(p.X, p.Y - m, limitM))) return float.NaN;
                sum += Mult(c);
            }
            return len * sum / (n + 1);
        }

        // A goal inside blocked water maps to a shore cell: among the passable cells of the nearest three rings, the one minimising (distance to goal + distance from the start).
        private int NearestPassable(int goal, float limitM, int from)
        {
            if (!Blocked(goal, limitM)) return goal;
            int gx = goal % _nx, gy = goal / _nx, fx = from % _nx, fy = from / _nx;
            int best = -1; float bd = float.MaxValue; int firstRing = -1;
            for (int r = 1; r < Math.Max(_nx, _ny); r++)
            {
                if (firstRing >= 0 && r > firstRing + 2) break;
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int x = gx + dx, y = gy + dy;
                        if (x < 0 || y < 0 || x >= _nx || y >= _ny) continue;
                        int c = y * _nx + x;
                        if (Blocked(c, limitM)) continue;
                        if (firstRing < 0) firstRing = r;
                        float d = MathF.Sqrt(dx * dx + dy * dy) + MathF.Sqrt((x - fx) * (x - fx) + (y - fy) * (y - fy));
                        if (d < bd) { bd = d; best = c; }
                    }
            }
            return best;
        }

        // ------------------------------------------------------------------ IRoutePlanner

        public float PathLength(Float2 from, Float2 to, in SpeciesRole species)
        {
            if (!InGrid(from) || !InGrid(to)) return Straight(from, to);
            Route r = Plan(from, ref to, species, out bool direct);
            if (r == null || r.Pts == null) return float.PositiveInfinity;
            if (direct) return r.Length;
            return Straight(from, Centre(CellOf(from))) + r.Length;
        }

        public Float2 NextWaypoint(Float2 from, Float2 to, in SpeciesRole species)
        {
            if (!InGrid(from) || !InGrid(to)) return to;
            Route r = Plan(from, ref to, species, out bool direct);
            if (direct || r == null || r.Pts == null) return to;
            float limit = DepthOverHeight * species.HeightM;
            for (int i = r.Pts.Length - 1; i >= 0; i--)
            {
                float c = SegmentCost(from, r.Pts[i], limit);
                if (!float.IsNaN(c)) return r.Pts[i];
            }
            // nothing on the route is visible from this exact spot (the route starts at the centre of the cell): walk to the cell centre first
            Float2 centre = Centre(CellOf(from));
            return Straight(from, centre) > 2f ? centre : r.Pts[0];
        }

        private static float Straight(Float2 a, Float2 b) { float dx = b.X - a.X, dy = b.Y - a.Y; return MathF.Sqrt(dx * dx + dy * dy); }

        // Returns a cached or fresh route; `direct` = the straight segment is clear and about as cheap as the straight length (route then holds that segment's cost).
        private Route Plan(Float2 from, ref Float2 to, in SpeciesRole sp, out bool direct)
        {
            float limit = DepthOverHeight * sp.HeightM;
            int goal = NearestPassable(CellOf(to), limit, CellOf(from));
            if (goal < 0) { direct = false; return null; }
            if (goal != CellOf(to)) to = Centre(goal);
            float straight = Straight(from, to);
            float sc = SegmentCost(from, to, limit);
            if (!float.IsNaN(sc) && sc <= 1.05f * straight + 1f) { direct = true; return new Route { Pts = new[] { to }, Length = sc }; }
            direct = false;
            var key = new Key((int)sp.HeightM, CellOf(from), goal);
            if (_cache.TryGetValue(key, out Route hit)) return hit;
            if (_cache.Count > 4096) _cache.Clear();
            long st0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Route route = Search(CellOf(from), goal, limit, to);
            double sms = (System.Diagnostics.Stopwatch.GetTimestamp() - st0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            SearchMsTotal += sms; if (sms > SearchMsMax) SearchMsMax = sms; if (sms > 5.0) SearchesOver5Ms++;
            _cache[key] = route;
            return route;
        }

        // ------------------------------------------------------------------ A*

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 }, Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        private Route Search(int start, int goal, float limit, Float2 goalPos)
        {
            Searches++;
            _gen++;
            _open.Clear();
            int gx = goal % _nx, gy = goal / _nx;
            _g[start] = 0f; _parent[start] = -1; _stamp[start] = _gen;
            _open.Push(start, Heur(start % _nx, start / _nx, gx, gy));
            bool found = false;
            while (_open.Count > 0)
            {
                int cur = _open.Pop(out float fcur);
                if (cur == goal) { found = true; break; }
                int cx = cur % _nx, cy = cur / _nx;
                if (fcur > _g[cur] + Heur(cx, cy, gx, gy) + 1e-3f) continue;   // stale entry
                float mc = Mult(cur);
                for (int k = 0; k < 8; k++)
                {
                    int x = cx + Dx[k], y = cy + Dy[k];
                    if (x < 0 || y < 0 || x >= _nx || y >= _ny) continue;
                    int nb = y * _nx + x;
                    if (Blocked(nb, limit)) continue;
                    float step = k < 4 ? 1f : 1.41421356f;
                    if (k >= 4 && (Blocked(cy * _nx + x, limit) || Blocked(y * _nx + cx, limit))) continue;   // no corner cutting
                    float ng = _g[cur] + step * 0.5f * (mc + Mult(nb));
                    if (_stamp[nb] != _gen || ng < _g[nb] - 1e-5f)
                    {
                        _stamp[nb] = _gen; _g[nb] = ng; _parent[nb] = cur;
                        _open.Push(nb, ng + Heur(x, y, gx, gy));
                    }
                }
            }
            if (!found) return new Route { Pts = null, Length = float.PositiveInfinity };
            var cells = new List<int>();
            for (int c = goal; c >= 0; c = _parent[c]) cells.Add(c);
            cells.Reverse();
            var pts = new List<Float2>(cells.Count);
            for (int i = 1; i < cells.Count; i++) pts.Add(Centre(cells[i]));
            if (pts.Count == 0) pts.Add(goalPos);
            // the goal itself is the exact requested position (or the nearest passable cell centre)
            pts[pts.Count - 1] = goalPos;
            // string pulling: from each anchor jump to the farthest vertex that is visible and no more expensive than the grid path over that stretch
            var smooth = new List<Float2>();
            Float2 anchor = Centre(start);
            int idx = 0;
            float total = 0f;
            while (idx < pts.Count)
            {
                int far = idx; float farCost = SegmentCost(anchor, pts[idx], limit);
                if (float.IsNaN(farCost)) farCost = Straight(anchor, pts[idx]) * 1.5f;
                float gridCost = 0f; Float2 prev = anchor;
                for (int j = idx; j < pts.Count; j++)
                {
                    float seg = SegmentCost(prev, pts[j], limit);
                    gridCost += float.IsNaN(seg) ? Straight(prev, pts[j]) : seg;
                    prev = pts[j];
                    float sc = SegmentCost(anchor, pts[j], limit);
                    if (!float.IsNaN(sc) && sc <= gridCost * 1.02f + 1f) { far = j; farCost = sc; }
                }
                smooth.Add(pts[far]); total += farCost; anchor = pts[far]; idx = far + 1;
            }
            return new Route { Pts = smooth.ToArray(), Length = total };
        }

        private static float Heur(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy);   // octile at the minimum multiplier 1
        }

        private sealed class Heap
        {
            private int[] _id = new int[256]; private float[] _key = new float[256];
            public int Count { get; private set; }
            public void Clear() { Count = 0; }
            public void Push(int id, float key)
            {
                if (Count == _id.Length) { Array.Resize(ref _id, Count * 2); Array.Resize(ref _key, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_key[p] <= key) break;
                    _id[i] = _id[p]; _key[i] = _key[p]; i = p;
                }
                _id[i] = id; _key[i] = key;
            }
            public int Pop(out float key)
            {
                int top = _id[0]; key = _key[0];
                int last = --Count;
                if (last > 0)
                {
                    int id = _id[last]; float k = _key[last]; int i = 0;
                    while (true)
                    {
                        int c = 2 * i + 1; if (c >= last) break;
                        if (c + 1 < last && _key[c + 1] < _key[c]) c++;
                        if (_key[c] >= k) break;
                        _id[i] = _id[c]; _key[i] = _key[c]; i = c;
                    }
                    _id[i] = id; _key[i] = k;
                }
                return top;
            }
        }
    }
}
