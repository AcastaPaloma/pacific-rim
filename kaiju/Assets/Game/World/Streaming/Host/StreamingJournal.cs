// Destruction persistence across evict/reload (T-110, R-101 section 7, F-043): a per-L0-tile journal of removed BuildingIds (a set) and an
// ordered list of terrain craters. It is updated at removal time (so nothing depends on a tile being resident), flushed to a durable store when
// the scheduler decides to evict the tile (TileEvicted, before the host releases meshes), saved as section `streaming` v1, and replayed when the
// tile is built again (removed buildings omitted before meshing and collider cooking; craters applied to the decoded heights before RTIN).
// Engine-free. Per-building damage state stays in destruction@v1 (IDamageModel); this journal exists even with the destruction module off.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Core;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    /// <summary>One crater: centre in BayTM float64, radius and depth in metres, seed for the deterministic rim texture.</summary>
    public readonly struct TerrainOp : IEquatable<TerrainOp>
    {
        public readonly double X, Z, Radius, Depth;
        public readonly uint Seed;
        public TerrainOp(double x, double z, double radius, double depth, uint seed)
        {
            if (!(radius > 0) || double.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!(depth > 0) || double.IsInfinity(depth)) throw new ArgumentOutOfRangeException(nameof(depth));
            if (double.IsNaN(x) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(z)) throw new ArgumentOutOfRangeException(nameof(x));
            X = x; Z = z; Radius = radius; Depth = depth; Seed = seed;
        }
        public bool Equals(TerrainOp o) => X == o.X && Z == o.Z && Radius == o.Radius && Depth == o.Depth && Seed == o.Seed;
        public override bool Equals(object obj) => obj is TerrainOp o && Equals(o);
        public override int GetHashCode() => (X.GetHashCode() * 397 ^ Z.GetHashCode()) * 397 ^ (int)Seed;
        /// <summary>R-101 section 7: an edit applies to a pyramid level only when its depth reaches that level's error bound (0.1 x 2^n m).</summary>
        public bool AffectsLevel(int level) => Depth >= (level <= 0 ? 0.0 : 0.1 * Math.Pow(2.0, level));
    }

    /// <summary>Deterministic crater replay on a height lattice (row = z south first, col = x). Same ops in the same order give identical heights.</summary>
    public static class TerrainOps
    {
        /// <summary>Lowers heights by depth x (1 - t^2)^2 x (0.95 + 0.1 u), t = distance / radius, u in [0,1) a hash of (seed, node). Returns the nodes changed.</summary>
        public static int Apply(float[] heights, int samples, double spacing, double originX, double originZ, IReadOnlyList<TerrainOp> ops)
        {
            if (ops == null || ops.Count == 0) return 0;
            int changed = 0;
            for (int k = 0; k < ops.Count; k++)
            {
                var op = ops[k];
                int c0 = Math.Max(0, (int)Math.Floor((op.X - op.Radius - originX) / spacing));
                int c1 = Math.Min(samples - 1, (int)Math.Ceiling((op.X + op.Radius - originX) / spacing));
                int r0 = Math.Max(0, (int)Math.Floor((op.Z - op.Radius - originZ) / spacing));
                int r1 = Math.Min(samples - 1, (int)Math.Ceiling((op.Z + op.Radius - originZ) / spacing));
                double r2 = op.Radius * op.Radius;
                for (int r = r0; r <= r1; r++)
                {
                    double dz = originZ + r * spacing - op.Z;
                    for (int c = c0; c <= c1; c++)
                    {
                        double dx = originX + c * spacing - op.X;
                        double d2 = dx * dx + dz * dz;
                        if (d2 >= r2) continue;
                        double t2 = d2 / r2, w = (1.0 - t2) * (1.0 - t2);
                        // jitter keyed on the world node (not the tile-local index) so both tiles sharing a border node agree
                        long gx = (long)Math.Round((originX + c * spacing) / spacing), gz = (long)Math.Round((originZ + r * spacing) / spacing);
                        double u = Hash(op.Seed, gx, gz) / 4294967296.0;
                        heights[r * samples + c] -= (float)(op.Depth * w * (0.95 + 0.1 * u));
                        changed++;
                    }
                }
            }
            return changed;
        }

        static uint Hash(uint seed, long a, long b)
        {
            ulong h = 0x9E3779B97F4A7C15UL ^ seed;
            h = (h ^ (ulong)a) * 0xBF58476D1CE4E5B9UL; h ^= h >> 31;
            h = (h ^ (ulong)b) * 0x94D049BB133111EBUL; h ^= h >> 29;
            return (uint)(h >> 16);
        }
    }

    /// <summary>Immutable copy of one tile's journal (what a worker thread reads while the main thread keeps recording).</summary>
    public sealed class TileJournalSnapshot
    {
        public static readonly TileJournalSnapshot Empty = new TileJournalSnapshot(new HashSet<BuildingId>(), new TerrainOp[0], 0);
        public readonly HashSet<BuildingId> Removed;
        public readonly IReadOnlyList<TerrainOp> Ops;
        /// <summary>Counts every change of this tile (ops and removals); a reader's cached model is stale when it differs.</summary>
        public readonly int Version;
        /// <summary>Identity of the crater list (count and order-sensitive hash): equal keys give identical terrain.</summary>
        public readonly int OpsKey;
        internal TileJournalSnapshot(HashSet<BuildingId> removed, TerrainOp[] ops, int version)
        {
            Removed = removed; Ops = ops; Version = version;
            int h = ops.Length;
            foreach (var o in ops) h = h * 31 + o.GetHashCode();
            OpsKey = h;
        }
        public bool IsEmpty => Removed.Count == 0 && Ops.Count == 0;
    }

    /// <summary>Durable home of flushed tile journals. One opaque text payload per L0 tile.</summary>
    public interface IJournalStore
    {
        void Write(GeoTileId tile, string payload);
        bool TryRead(GeoTileId tile, out string payload);
        IEnumerable<GeoTileId> Tiles();
    }

    public sealed class MemoryJournalStore : IJournalStore
    {
        readonly SortedDictionary<string, string> _data = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public int Writes { get; private set; }
        public void Write(GeoTileId tile, string payload) { _data[tile.ToString()] = payload; Writes++; }
        public bool TryRead(GeoTileId tile, out string payload) => _data.TryGetValue(tile.ToString(), out payload);
        public IEnumerable<GeoTileId> Tiles() { foreach (var k in _data.Keys) if (GeoTileId.TryParse(k, out var id)) yield return id; }
    }

    /// <summary>One file per tile (`L0_30_36.journal`), written to a temp name and moved into place, so a crash leaves the old or the new file whole.</summary>
    public sealed class FileJournalStore : IJournalStore
    {
        readonly string _dir;
        public FileJournalStore(string directory) { _dir = directory ?? throw new ArgumentNullException(nameof(directory)); Directory.CreateDirectory(_dir); }
        string PathOf(GeoTileId t) => Path.Combine(_dir, t.ToString() + ".journal");
        public void Write(GeoTileId tile, string payload)
        {
            var path = PathOf(tile); var tmp = path + ".tmp";
            File.WriteAllText(tmp, payload, new UTF8Encoding(false));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        public bool TryRead(GeoTileId tile, out string payload)
        {
            var path = PathOf(tile);
            if (!File.Exists(path)) { payload = null; return false; }
            payload = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }
        public IEnumerable<GeoTileId> Tiles()
        {
            var names = new List<string>(Directory.GetFiles(_dir, "*.journal"));
            names.Sort(StringComparer.Ordinal);
            foreach (var n in names) if (GeoTileId.TryParse(Path.GetFileNameWithoutExtension(n), out var id)) yield return id;
        }
    }

    /// <summary>Text codec of one tile journal: `removed:` sorted ids joined by `;`, then `op:` lines with float64 bits in hex (exact round trip).</summary>
    public static class JournalCodec
    {
        public static string Encode(IReadOnlyCollection<BuildingId> removed, IReadOnlyList<TerrainOp> ops)
        {
            var ids = new List<BuildingId>(removed); ids.Sort();
            var sb = new StringBuilder("journal@1\nremoved:");
            for (int i = 0; i < ids.Count; i++) { if (i > 0) sb.Append(';'); sb.Append(ids[i].ToString()); }
            sb.Append('\n');
            foreach (var op in ops)
                sb.Append("op:").Append(Bits(op.X)).Append(',').Append(Bits(op.Z)).Append(',').Append(Bits(op.Radius)).Append(',').Append(Bits(op.Depth)).Append(',')
                  .Append(op.Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            return sb.ToString();
        }

        public static void Decode(string text, out List<BuildingId> removed, out List<TerrainOp> ops)
        {
            removed = new List<BuildingId>(); ops = new List<TerrainOp>();
            var lines = text.Split('\n');
            if (lines.Length == 0 || lines[0] != "journal@1") throw new FormatException("journal: bad header");
            bool sawRemoved = false;
            for (int i = 1; i < lines.Length; i++)
            {
                var l = lines[i];
                if (l.Length == 0) continue;
                if (l.StartsWith("removed:", StringComparison.Ordinal))
                {
                    if (sawRemoved) throw new FormatException("journal: two removed lines");
                    sawRemoved = true;
                    var body = l.Substring(8);
                    if (body.Length == 0) continue;
                    foreach (var s in body.Split(';'))
                    {
                        if (!BuildingId.TryParse(s, out var id) || !id.IsValid || id.ToString() != s) throw new FormatException("journal: bad building id '" + s + "'");
                        removed.Add(id);
                    }
                }
                else if (l.StartsWith("op:", StringComparison.Ordinal))
                {
                    var f = l.Substring(3).Split(',');
                    if (f.Length != 5 || !uint.TryParse(f[4], NumberStyles.None, CultureInfo.InvariantCulture, out var seed)) throw new FormatException("journal: bad op '" + l + "'");
                    try { ops.Add(new TerrainOp(Unbits(f[0]), Unbits(f[1]), Unbits(f[2]), Unbits(f[3]), seed)); }
                    catch (ArgumentException e) { throw new FormatException("journal: bad op values: " + e.Message); }
                }
                else throw new FormatException("journal: unknown line '" + l + "'");
            }
            if (!sawRemoved) throw new FormatException("journal: no removed line");
        }

        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("x16", CultureInfo.InvariantCulture);
        static double Unbits(string s)
        {
            if (s.Length != 16 || !long.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var b)) throw new FormatException("journal: bad float64 bits '" + s + "'");
            return BitConverter.Int64BitsToDouble(b);
        }
    }

    /// <summary>
    /// Per-L0-tile destruction journal. Thread-safe: the main thread records and flushes, worker threads take snapshots.
    /// Removals and craters are recorded for any tile, resident or not. A crater is recorded in every L0 tile its disc overlaps, so seams agree.
    /// </summary>
    public sealed class StreamingJournal :
        IEventHandler<TileEvicted>, IEventHandler<GeoBuildingRemoved>
    {
        sealed class Entry
        {
            public readonly SortedSet<BuildingId> Removed = new SortedSet<BuildingId>();
            public readonly List<TerrainOp> Ops = new List<TerrainOp>();
            public int Version;
            public bool Dirty;
            public TileJournalSnapshot Cached;
        }

        readonly object _lock = new object();
        readonly SortedDictionary<string, Entry> _tiles = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, GeoTileId> _ids = new Dictionary<string, GeoTileId>(StringComparer.Ordinal);
        readonly IJournalStore _store;
        IEventBus _bus;
        Func<BuildingId, GeoTileId?> _locate;

        public StreamingJournal(IJournalStore store = null) { _store = store; }

        /// <summary>Flushes done (one per dirty tile at eviction or by FlushAll).</summary>
        public int FlushCount { get; private set; }
        /// <summary>Removals that arrived for a building whose tile the locator could not name (dropped, counted).</summary>
        public int UnlocatedRemovals { get; private set; }
        /// <summary>Fired on the recording thread with the tile whose journal changed (readers drop cached models).</summary>
        public event Action<GeoTileId> Changed;

        public int TileCount { get { lock (_lock) return _tiles.Count; } }
        public int RemovedCount { get { lock (_lock) { int n = 0; foreach (var e in _tiles.Values) n += e.Removed.Count; return n; } } }

        // ---- recording ----

        /// <summary>Records a removed building in its L0 tile. Idempotent; true when it was new.</summary>
        public bool RecordRemoval(GeoTileId tile, BuildingId id)
        {
            if (tile.Level != 0) throw new ArgumentException("journals are kept per L0 tile", nameof(tile));
            if (!id.IsValid) throw new ArgumentException("invalid building id", nameof(id));
            bool added;
            lock (_lock)
            {
                var e = Get(tile, true);
                added = e.Removed.Add(id);
                if (added) Touch(e);
            }
            if (added) Changed?.Invoke(tile);
            return added;
        }

        /// <summary>Records a crater in every L0 tile its disc overlaps (order is preserved per tile).</summary>
        public int RecordTerrainOp(in TerrainOp op)
        {
            double s = GeoTileId.BaseSizeM;
            int i0 = (int)Math.Floor((op.X - op.Radius) / s), i1 = (int)Math.Floor((op.X + op.Radius) / s);
            int j0 = (int)Math.Floor((op.Z - op.Radius) / s), j1 = (int)Math.Floor((op.Z + op.Radius) / s);
            var touched = new List<GeoTileId>();
            lock (_lock)
            {
                for (int j = Math.Max(0, j0); j <= j1; j++)
                    for (int i = Math.Max(0, i0); i <= i1; i++)
                    {
                        var t = GeoTileId.From(0, i, j);
                        var e = Get(t, true);
                        e.Ops.Add(op);
                        Touch(e);
                        touched.Add(t);
                    }
            }
            foreach (var t in touched) Changed?.Invoke(t);
            return touched.Count;
        }

        static void Touch(Entry e) { e.Version++; e.Dirty = true; e.Cached = null; }

        Entry Get(GeoTileId tile, bool create)
        {
            var key = tile.ToString();
            if (_tiles.TryGetValue(key, out var e)) return e;
            if (!create) return null;
            e = new Entry();
            _tiles[key] = e; _ids[key] = tile;
            return e;
        }

        // ---- reading ----

        public TileJournalSnapshot Snapshot(GeoTileId tile)
        {
            lock (_lock)
            {
                var e = Get(tile, false);
                if (e == null) return TileJournalSnapshot.Empty;
                if (e.Cached == null) e.Cached = new TileJournalSnapshot(new HashSet<BuildingId>(e.Removed), e.Ops.ToArray(), e.Version);
                return e.Cached;
            }
        }

        public bool IsRemoved(GeoTileId tile, BuildingId id) { lock (_lock) { var e = Get(tile, false); return e != null && e.Removed.Contains(id); } }
        public bool IsDirty(GeoTileId tile) { lock (_lock) { var e = Get(tile, false); return e != null && e.Dirty; } }

        // ---- flush (TileEvicted) ----

        /// <summary>Writes the tile's journal to the store when it changed since the last flush. Call before the host releases the tile's meshes.</summary>
        public bool Flush(GeoTileId tile)
        {
            string payload;
            lock (_lock)
            {
                var e = Get(tile, false);
                if (e == null || !e.Dirty) return false;
                payload = JournalCodec.Encode(e.Removed, e.Ops);
                e.Dirty = false;
            }
            try { _store?.Write(tile, payload); }
            catch { lock (_lock) { var e = Get(tile, false); if (e != null) e.Dirty = true; } throw; }
            FlushCount++;
            return true;
        }

        public int FlushAll()
        {
            var ids = new List<GeoTileId>();
            lock (_lock) foreach (var kv in _tiles) if (kv.Value.Dirty) ids.Add(_ids[kv.Key]);
            int n = 0;
            foreach (var t in ids) if (Flush(t)) n++;
            return n;
        }

        /// <summary>Loads every tile in the store (start-up recovery). Existing in-memory entries are replaced. Throws FormatException on a bad payload before changing anything.</summary>
        public int LoadFromStore()
        {
            if (_store == null) return 0;
            var loaded = new List<KeyValuePair<GeoTileId, string>>();
            foreach (var t in _store.Tiles()) if (_store.TryRead(t, out var p)) loaded.Add(new KeyValuePair<GeoTileId, string>(t, p));
            var parsed = new List<(GeoTileId, List<BuildingId>, List<TerrainOp>)>();
            foreach (var kv in loaded) { JournalCodec.Decode(kv.Value, out var r, out var o); parsed.Add((kv.Key, r, o)); }
            var changed = new List<GeoTileId>();
            lock (_lock)
            {
                foreach (var (t, r, o) in parsed)
                {
                    var e = Get(t, true);
                    e.Removed.Clear(); e.Ops.Clear();
                    foreach (var id in r) e.Removed.Add(id);
                    e.Ops.AddRange(o);
                    e.Version++; e.Dirty = false; e.Cached = null;
                    changed.Add(t);
                }
            }
            foreach (var t in changed) Changed?.Invoke(t);
            return changed.Count;
        }

        // ---- bus ----

        /// <summary>Subscribes: TileEvicted (L0) flushes that tile; GeoBuildingRemoved is recorded in the tile the locator names. locate may be null (then only eviction is wired).</summary>
        public void Attach(IEventBus bus, Func<BuildingId, GeoTileId?> locate = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (_bus != null) throw new InvalidOperationException("journal already attached");
            _bus = bus; _locate = locate;
            bus.Subscribe<TileEvicted>(this);
            if (locate != null) bus.Subscribe<GeoBuildingRemoved>(this);
        }

        public void Detach()
        {
            if (_bus == null) return;
            _bus.Unsubscribe<TileEvicted>(this);
            _bus.Unsubscribe<GeoBuildingRemoved>(this);
            _bus = null; _locate = null;
        }

        void IEventHandler<TileEvicted>.Handle(in TileEvicted e)
        {
            if (e.Tile.Level == 0) Flush(e.Tile);
        }

        void IEventHandler<GeoBuildingRemoved>.Handle(in GeoBuildingRemoved e)
        {
            var t = _locate?.Invoke(e.Building);
            if (t.HasValue) RecordRemoval(t.Value, e.Building); else UnlocatedRemovals++;
        }

        // ---- save section ----

        internal void WriteTo(SaveSection section)
        {
            lock (_lock)
            {
                foreach (var kv in _tiles)
                {
                    var e = kv.Value;
                    if (e.Removed.Count == 0 && e.Ops.Count == 0) continue;
                    section.Add(kv.Key).Set(StreamingSaveParticipant.FieldJournal, SaveValue.Of(JournalCodec.Encode(e.Removed, e.Ops)));
                }
            }
        }

        internal void ReplaceAll(List<(GeoTileId, List<BuildingId>, List<TerrainOp>)> parsed)
        {
            var old = new List<GeoTileId>(); var now = new List<GeoTileId>();
            lock (_lock)
            {
                foreach (var kv in _tiles) old.Add(_ids[kv.Key]);
                foreach (var kv in _tiles) { kv.Value.Removed.Clear(); kv.Value.Ops.Clear(); kv.Value.Version++; kv.Value.Cached = null; kv.Value.Dirty = false; }
                foreach (var (t, r, o) in parsed)
                {
                    var e = Get(t, true);
                    foreach (var id in r) e.Removed.Add(id);
                    e.Ops.AddRange(o);
                    e.Version++; e.Dirty = true; e.Cached = null;
                    now.Add(t);
                }
            }
            foreach (var t in old) Changed?.Invoke(t);
            foreach (var t in now) if (!old.Contains(t)) Changed?.Invoke(t);
        }
    }

    /// <summary>Section `streaming`, version 1: one record per tile with a non-empty journal (key = tile id, field `journal` = JournalCodec text). Read validates every record first.</summary>
    public sealed class StreamingSaveParticipant : ISaveParticipant
    {
        public const string Section = "streaming";
        public const int Version = 1;
        public const string FieldJournal = "journal";
        readonly StreamingJournal _journal;
        public StreamingSaveParticipant(StreamingJournal journal) { _journal = journal ?? throw new ArgumentNullException(nameof(journal)); }
        public string SectionName => Section;
        public int SectionVersion => Version;
        public void Write(SaveSection section) => _journal.WriteTo(section);

        public void Read(SaveSection section)
        {
            var parsed = new List<(GeoTileId, List<BuildingId>, List<TerrainOp>)>();
            foreach (var rec in section.Records)
            {
                if (!GeoTileId.TryParse(rec.Key, out var tile) || tile.Level != 0 || tile.ToString() != rec.Key) throw new FormatException("streaming: bad tile key '" + rec.Key + "'");
                if (!rec.TryGet(FieldJournal, out var v) || v.Kind != SaveValueKind.String) throw new FormatException("streaming: " + rec.Key + " lacks String 'journal'");
                JournalCodec.Decode(v.AsString, out var removed, out var ops);
                parsed.Add((tile, removed, ops));
            }
            _journal.ReplaceAll(parsed);
        }
    }

    /// <summary>Replay helpers shared by the tile readers (L0 now; the geo_lod@v1 reader drops per-building ranges the same way).</summary>
    public static class JournalReplay
    {
        /// <summary>Height edit for GeoTileContext.HeightEdit: replays the tile's craters in order. Null journal or no ops gives null (no edit).</summary>
        public static Action<float[], int, double, double, double> HeightEdit(TileJournalSnapshot snap)
        {
            if (snap == null || snap.Ops.Count == 0) return null;
            return (h, n, sp, ox, oz) => TerrainOps.Apply(h, n, sp, ox, oz, snap.Ops);
        }
    }
}
