// IInfrastructureRegistry implementation: records by id, a uniform grid for spatial queries, damage forwarded to IDamageModel.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Destruction;

namespace Game.World.Infrastructure
{
    public sealed class InfraRegistry : IInfrastructureRegistry
    {
        /// <summary>Grid cell edge, m (Assumed; power lines are indexed along their length so long objects cost cells, not scans).</summary>
        public const double CellM = 512.0;

        readonly Dictionary<InfraId, InfraRecord> _records = new Dictionary<InfraId, InfraRecord>();
        readonly Dictionary<long, List<InfraId>> _grid = new Dictionary<long, List<InfraId>>();
        readonly Dictionary<string, List<InfraId>> _tiles = new Dictionary<string, List<InfraId>>(StringComparer.Ordinal);
        readonly int[] _counts = new int[11];
        readonly IDamageModel _damage;

        /// <param name="damage">Optional. Without it the registry answers spatial queries only (every state is None, Apply is unknown).</param>
        public InfraRegistry(IDamageModel damage = null) { _damage = damage; }

        public int Count => _records.Count;
        public int CountOf(InfraKind kind) => (int)kind < _counts.Length ? _counts[(int)kind] : 0;
        public bool TryGet(InfraId id, out InfraRecord record) => _records.TryGetValue(id, out record);

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        static int Cell(double v) => (int)Math.Floor(v / CellM);

        /// <summary>Adds (or replaces) the records of one tile and registers them with the damage model. Idempotent per id; damage of a known id is kept.</summary>
        public int AddTile(string tileId, IReadOnlyList<InfraRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            tileId = tileId ?? "";
            RemoveTile(tileId);
            var ids = new List<InfraId>(records.Count);
            foreach (var r in records)
            {
                if (!r.Id.IsValid) throw new ArgumentException("record without a valid id", nameof(records));
                r.Tile = tileId;
                if (_records.TryGetValue(r.Id, out var old)) Unindex(old);
                _records[r.Id] = r;
                Index(r);
                ids.Add(r.Id);
                _damage?.Register(r.Id.ToBuildingId(), InfraDamageProfile.AttributesOf(r));
            }
            _tiles[tileId] = ids;
            return ids.Count;
        }

        /// <summary>Drops a tile's records (tile unloaded). Their damage stays in the damage model (and in its save), so a reload sees it again.</summary>
        public int RemoveTile(string tileId)
        {
            tileId = tileId ?? "";
            if (!_tiles.TryGetValue(tileId, out var ids)) return 0;
            int n = 0;
            foreach (var id in ids)
                if (_records.TryGetValue(id, out var r) && r.Tile == tileId) { Unindex(r); _records.Remove(id); n++; }
            _tiles.Remove(tileId);
            return n;
        }

        void Index(InfraRecord r)
        {
            _counts[(int)r.Id.Kind]++;
            foreach (long k in CellsOf(r)) { if (!_grid.TryGetValue(k, out var l)) _grid[k] = l = new List<InfraId>(); l.Add(r.Id); }
        }

        void Unindex(InfraRecord r)
        {
            _counts[(int)r.Id.Kind]--;
            foreach (long k in CellsOf(r)) if (_grid.TryGetValue(k, out var l)) { l.Remove(r.Id); if (l.Count == 0) _grid.Remove(k); }
        }

        static IEnumerable<long> CellsOf(InfraRecord r)
        {
            var seen = new HashSet<long>();
            var p = r.Points;
            if (p.Length == 2 || r.IsArea)
            {
                for (int cx = Cell(r.MinX); cx <= Cell(r.MaxX); cx++) for (int cz = Cell(r.MinZ); cz <= Cell(r.MaxZ); cz++) seen.Add(Key(cx, cz));
                if (p.Length == 2) { seen.Clear(); seen.Add(Key(Cell(p[0]), Cell(p[1]))); }
            }
            else
            {
                seen.Add(Key(Cell(p[0]), Cell(p[1])));
                for (int i = 2; i < p.Length; i += 2)
                {
                    double ax = p[i - 2], az = p[i - 1], bx = p[i], bz = p[i + 1];
                    int steps = Math.Max(1, (int)Math.Ceiling(InfraGeometry.Hyp(bx - ax, bz - az) / (CellM * 0.5)));
                    for (int s = 0; s <= steps; s++) { double t = (double)s / steps; seen.Add(Key(Cell(ax + t * (bx - ax)), Cell(az + t * (bz - az)))); }
                }
            }
            return seen;
        }

        public void QueryRadius(double x, double z, double radiusM, uint kindMask, List<InfraRecord> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int start = into.Count;
            var seen = new HashSet<InfraId>();
            // A long line's grid cells are exact only to 0.5 cell, so widen the cell window by one cell.
            for (int cx = Cell(x - radiusM) - 1; cx <= Cell(x + radiusM) + 1; cx++)
                for (int cz = Cell(z - radiusM) - 1; cz <= Cell(z + radiusM) + 1; cz++)
                    if (_grid.TryGetValue(Key(cx, cz), out var l))
                        foreach (var id in l)
                        {
                            if ((InfraKinds.Mask(id.Kind) & kindMask) == 0 || !seen.Add(id)) continue;
                            var r = _records[id];
                            if (InfraGeometry.Distance(r, x, z) <= radiusM) into.Add(r);
                        }
            into.Sort(start, into.Count - start, new DistanceOrder(x, z));
        }

        sealed class DistanceOrder : IComparer<InfraRecord>
        {
            readonly double _x, _z;
            public DistanceOrder(double x, double z) { _x = x; _z = z; }
            public int Compare(InfraRecord a, InfraRecord b)
            {
                int c = InfraGeometry.Distance(a, _x, _z).CompareTo(InfraGeometry.Distance(b, _x, _z));
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            }
        }

        public bool TryNearest(double x, double z, uint kindMask, double maxRadiusM, out InfraRecord record, out double distanceM)
        {
            record = null; distanceM = double.MaxValue;
            var scratch = new List<InfraRecord>();
            maxRadiusM = Math.Min(maxRadiusM, 1e6);
            for (double r = CellM; ; r *= 2)
            {
                double rr = Math.Min(r, maxRadiusM);
                scratch.Clear();
                QueryRadius(x, z, rr, kindMask, scratch);
                if (scratch.Count > 0) { record = scratch[0]; distanceM = InfraGeometry.Distance(record, x, z); return true; }
                if (rr >= maxRadiusM) return false;
            }
        }

        public DamageState StateOf(InfraId id)
        {
            if (_damage == null || !_records.ContainsKey(id)) return DamageState.None;
            return _damage.TryGetState(id.ToBuildingId(), out var s) ? (DamageState)s.State : DamageState.None;
        }

        public bool IsOperational(InfraId id) => _records.ContainsKey(id) && StateOf(id) != DamageState.Complete;

        public InfraDamageChange Apply(InfraId id, double energyJ, DamageCause cause, EntityId source, long step)
        {
            if (_damage == null || !_records.ContainsKey(id)) return default;
            DamageChange c = _damage.Apply(id.ToBuildingId(), energyJ, cause, source, step);
            return new InfraDamageChange
            {
                Known = c.Known, Changed = c.Changed, Prev = (DamageState)c.PrevState,
                Current = (DamageState)c.Current.State, CumulativeEnergyJ = c.Current.CumulativeEnergyJ,
            };
        }
    }
}
