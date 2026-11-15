// Per-activity-cluster origin assignment (R-101 section 6, O6). Deterministic, engine-free, allocation-light.
// Rules: two foci link when their Chebyshev (x, z) distance is below ClusterLinkM and the merged AABB extent stays at or below
// ClusterExtentCapM (5120 m). The origin is the tile corner (multiple of OriginGridM) nearest the cluster AABB centre, so a member is at most
// extent/2 + 1024 = 3584 m from it. A cluster keeps its origin until a member passes RebaseM (3072 m) on a quiet step, or ForceRebaseM (3840 m)
// on any step; a rebase only happens when it reduces the largest local coordinate (no thrash at 3584 m).
using System;
using System.Collections.Generic;

namespace Game.World.Streaming
{
    public sealed class ClusterAssigner
    {
        sealed class State { public double OriginX, OriginZ; }

        readonly StreamingConfig _cfg;
        readonly Dictionary<int, State> _clusters = new Dictionary<int, State>();
        readonly Dictionary<int, int> _focusCluster = new Dictionary<int, int>();   // previous tick
        readonly Dictionary<int, int> _nextFocusCluster = new Dictionary<int, int>();
        readonly HashSet<int> _claimed = new HashSet<int>();
        readonly Dictionary<int, int> _votes = new Dictionary<int, int>();
        int _nextClusterId = 1;
        int[] _order = new int[0], _parent = new int[0], _rootOf = new int[0];
        double[] _minX = new double[0], _maxX = new double[0], _minZ = new double[0], _maxZ = new double[0];
        int[] _count = new int[0];

        public ClusterAssigner(StreamingConfig cfg) { _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg)); }

        public int ClusterCount => _clusters.Count;

        /// <summary>Assigns every focus (cameras and bodies) to a cluster. Appends to the two lists in cluster id order / focus id order.</summary>
        /// <param name="quiet">False defers soft rebases (a combat frame); the forced distance still rebases.</param>
        public void Assign(ReadOnlySpan<FocusSample> foci, bool quiet, List<ClusterAssignment> assignments, List<ClusterInfo> infos)
        {
            int n = foci.Length;
            Ensure(n);
            for (int i = 0; i < n; i++) _order[i] = i;
            // insertion sort by focus id (n is small; keeps the order deterministic without a comparer allocation)
            for (int i = 1; i < n; i++)
            {
                int v = _order[i]; int id = foci[v].Id; int j = i - 1;
                while (j >= 0 && foci[_order[j]].Id > id) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = v;
            }
            for (int k = 0; k < n; k++)
            {
                var f = foci[_order[k]];
                _parent[k] = k; _minX[k] = _maxX[k] = f.X; _minZ[k] = _maxZ[k] = f.Z;
            }
            double link = _cfg.ClusterLinkM, cap = _cfg.ClusterExtentCapM;
            for (int a = 0; a < n; a++)
            {
                var fa = foci[_order[a]];
                for (int b = a + 1; b < n; b++)
                {
                    var fb = foci[_order[b]];
                    if (Math.Max(Math.Abs(fa.X - fb.X), Math.Abs(fa.Z - fb.Z)) >= link) continue;
                    int ra = Find(a), rb = Find(b);
                    if (ra == rb) continue;
                    double mnx = Math.Min(_minX[ra], _minX[rb]), mxx = Math.Max(_maxX[ra], _maxX[rb]);
                    double mnz = Math.Min(_minZ[ra], _minZ[rb]), mxz = Math.Max(_maxZ[ra], _maxZ[rb]);
                    if (Math.Max(mxx - mnx, mxz - mnz) > cap) continue;
                    int lo = Math.Min(ra, rb), hi = Math.Max(ra, rb);   // smaller sorted index stays the root
                    _parent[hi] = lo; _minX[lo] = mnx; _maxX[lo] = mxx; _minZ[lo] = mnz; _maxZ[lo] = mxz;
                }
            }
            for (int k = 0; k < n; k++) { _rootOf[k] = Find(k); _count[k] = 0; }
            for (int k = 0; k < n; k++) _count[_rootOf[k]]++;

            _claimed.Clear(); _nextFocusCluster.Clear();
            int firstInfo = infos.Count;
            // components in order of their smallest focus id (= root sorted index)
            for (int root = 0; root < n; root++)
            {
                if (_rootOf[root] != root) continue;
                // vote for the previous cluster most members came from
                _votes.Clear();
                for (int k = 0; k < n; k++)
                    if (_rootOf[k] == root && _focusCluster.TryGetValue(foci[_order[k]].Id, out int pc) && !_claimed.Contains(pc))
                        _votes[pc] = _votes.TryGetValue(pc, out int c) ? c + 1 : 1;
                int cid = 0, best = 0;
                foreach (var kv in _votes)
                    if (kv.Value > best || (kv.Value == best && kv.Key < cid)) { best = kv.Value; cid = kv.Key; }
                bool inherited = best > 0;
                if (!inherited) cid = _nextClusterId++;
                _claimed.Add(cid);

                double cx = 0.5 * (_minX[root] + _maxX[root]), cz = 0.5 * (_minZ[root] + _maxZ[root]);
                double grid = _cfg.OriginGridM;
                double bestOx = Math.Round(cx / grid) * grid, bestOz = Math.Round(cz / grid) * grid;
                double ox, oz, prevX, prevZ;
                bool rebased = false, due = false;
                if (!inherited)
                {
                    ox = bestOx; oz = bestOz; prevX = ox; prevZ = oz;
                    _clusters[cid] = new State { OriginX = ox, OriginZ = oz };
                }
                else
                {
                    var st = _clusters[cid];
                    ox = prevX = st.OriginX; oz = prevZ = st.OriginZ;
                    double cur = MaxLocal(foci, root, ox, oz);
                    bool want = cur > _cfg.ForceRebaseM || (cur > _cfg.RebaseM && quiet);
                    if (cur > _cfg.RebaseM && !quiet && cur <= _cfg.ForceRebaseM) due = true;
                    if (want && (bestOx != ox || bestOz != oz) && MaxLocal(foci, root, bestOx, bestOz) < cur)
                    {
                        ox = bestOx; oz = bestOz; rebased = true; due = false;
                        st.OriginX = ox; st.OriginZ = oz;
                    }
                }
                double ext = Math.Max(_maxX[root] - _minX[root], _maxZ[root] - _minZ[root]);
                infos.Add(new ClusterInfo
                {
                    ClusterId = cid, OriginX = ox, OriginZ = oz, FocusCount = _count[root], ExtentM = ext,
                    Rebased = rebased, PrevOriginX = prevX, PrevOriginZ = prevZ, RebaseDue = due,
                });
                for (int k = 0; k < n; k++)
                {
                    if (_rootOf[k] != root) continue;
                    var f = foci[_order[k]];
                    _nextFocusCluster[f.Id] = cid;
                    assignments.Add(new ClusterAssignment { FocusId = f.Id, ClusterId = cid, LocalX = (float)(f.X - ox), LocalY = (float)f.Y, LocalZ = (float)(f.Z - oz) });
                }
            }
            // drop dead clusters, keep the focus -> cluster map
            _deadScratch.Clear();
            foreach (var kv in _clusters) if (!_claimed.Contains(kv.Key)) _deadScratch.Add(kv.Key);
            for (int i = 0; i < _deadScratch.Count; i++) _clusters.Remove(_deadScratch[i]);
            _focusCluster.Clear();
            foreach (var kv in _nextFocusCluster) _focusCluster[kv.Key] = kv.Value;
            // infos in cluster id order
            if (infos.Count - firstInfo > 1) infos.Sort(firstInfo, infos.Count - firstInfo, ClusterIdComparer.Instance);
        }

        readonly List<int> _deadScratch = new List<int>();

        sealed class ClusterIdComparer : IComparer<ClusterInfo>
        {
            public static readonly ClusterIdComparer Instance = new ClusterIdComparer();
            public int Compare(ClusterInfo a, ClusterInfo b) => a.ClusterId.CompareTo(b.ClusterId);
        }

        double MaxLocal(ReadOnlySpan<FocusSample> foci, int root, double ox, double oz)
        {
            double m = 0;
            for (int k = 0; k < _rootOf.Length && k < foci.Length; k++)
            {
                if (_rootOf[k] != root) continue;
                var f = foci[_order[k]];
                m = Math.Max(m, Math.Max(Math.Abs(f.X - ox), Math.Max(Math.Abs(f.Z - oz), Math.Abs(f.Y))));
            }
            return m;
        }

        int Find(int x)
        {
            while (_parent[x] != x) { _parent[x] = _parent[_parent[x]]; x = _parent[x]; }
            return x;
        }

        void Ensure(int n)
        {
            if (_order.Length >= n) return;
            int c = Math.Max(n, _order.Length * 2);
            _order = new int[c]; _parent = new int[c]; _rootOf = new int[c]; _count = new int[c];
            _minX = new double[c]; _maxX = new double[c]; _minZ = new double[c]; _maxZ = new double[c];
        }
    }
}
