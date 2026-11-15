// Tile readers behind ITileReader (T-109). L0 comes from the existing geo_tile@v1 folders (GeoTileModel, engine-free, runs on workers).
// L1..L4 are read through a registered reader: the geo_lod@v1 format of T-108 is not settled when this is written, so there is no
// default; a level without a reader fails the request (the scheduler backs off and parks, parent fallback keeps the view covered).
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    /// <summary>Routes a key to the reader registered for its level (render) or to the physics reader.</summary>
    public sealed class LevelRoutingReader : ITileReader
    {
        readonly Dictionary<int, ITileReader> _byLevel = new Dictionary<int, ITileReader>();
        public void Register(int level, ITileReader reader) { _byLevel[level] = reader ?? throw new ArgumentNullException(nameof(reader)); }
        public bool Has(int level) => _byLevel.ContainsKey(level);
        public TilePayload Read(ResidencyKey key, CancellationToken ct)
        {
            if (!_byLevel.TryGetValue(key.Tile.Level, out var r)) throw new NotSupportedException("no reader for level " + key.Tile.Level + " (geo_lod@v1 reader not registered)");
            return r.Read(key, ct);
        }
    }

    /// <summary>L0 render tiles and 256 m physics chunks from baked geo_tile@v1 folders. Models are cached briefly so the 64 chunks of a tile do not rebuild it.</summary>
    public sealed class GeoL0TileReader : ITileReader
    {
        readonly string _tilesRoot;
        readonly IReadOnlyDictionary<string, double> _settings;
        readonly int _cacheSize;
        readonly StreamingJournal _journal;
        readonly ConcurrentDictionary<(GeoTileId, int), Lazy<GeoTileModel>> _models = new ConcurrentDictionary<(GeoTileId, int), Lazy<GeoTileModel>>();
        readonly ConcurrentQueue<(GeoTileId, int)> _order = new ConcurrentQueue<(GeoTileId, int)>();

        /// <summary>journal (T-110, optional): removed buildings are omitted before meshing (render tile and physics chunks alike) and craters are replayed on the decoded heights before RTIN.</summary>
        public GeoL0TileReader(string tilesRoot, IReadOnlyDictionary<string, double> settings, int cacheSize = 3, StreamingJournal journal = null)
        { _tilesRoot = tilesRoot; _settings = settings; _cacheSize = Math.Max(1, cacheSize); _journal = journal; }

        public bool Exists(GeoTileId id) => File.Exists(Path.Combine(GeoTileSetLoader.TileDirectory(_tilesRoot, id), "manifest.json"));

        // the cache key carries the journal's terrain version for the tile: a new crater makes the cached model stale (removals need no rebuild, they hide at CombineChunk)
        GeoTileModel Model(GeoTileId id, TileJournalSnapshot snap)
        {
            int ver = snap.OpsKey;
            var lazy = _models.GetOrAdd((id, ver), k =>
            {
                _order.Enqueue(k);
                while (_order.Count > _cacheSize && _order.TryDequeue(out var old)) _models.TryRemove(old, out _);
                return new Lazy<GeoTileModel>(() =>
                {
                    var ctx = new GeoTileContext(GeoTileSetLoader.TileDirectory(_tilesRoot, k.Item1), _settings, false);
                    ctx.HeightEdit = JournalReplay.HeightEdit(snap);
                    return new GeoTileModel(ctx);
                });
            });
            return lazy.Value;
        }

        public TilePayload Read(ResidencyKey key, CancellationToken ct)
        {
            if (key.Tile.Level != 0) throw new NotSupportedException("GeoL0TileReader reads level 0 only");
            ct.ThrowIfCancellationRequested();
            var snap = _journal != null ? _journal.Snapshot(key.Tile) : TileJournalSnapshot.Empty;
            var hide = snap.Removed.Count > 0 ? snap.Removed : null;
            var m = Model(key.Tile, snap);
            var p = new TilePayload { Key = key };
            if (key.Kind == ResidencyKind.Render)
            {
                foreach (var layer in m.Layers) foreach (var mesh in layer.Meshes) p.Meshes.Add(mesh);
                for (int cz = 0; cz < m.BuildingChunks; cz++) for (int cx = 0; cx < m.BuildingChunks; cx++)
                { var c = m.CombineChunk(cx, cz, hide); if (c.TriangleCount > 0) p.Meshes.Add(c); }
            }
            else
            {
                var terr = m.Layer("terrain");
                if (terr != null) foreach (var mesh in terr.Meshes) if (InChunk(mesh.ChunkX, mesh.ChunkZ, key, m)) p.Meshes.Add(mesh);
                int bx = key.ChunkX * (m.BuildingChunks / ResidencyKey.ChunksPerEdge), bz = key.ChunkZ * (m.BuildingChunks / ResidencyKey.ChunksPerEdge);
                int span = Math.Max(1, m.BuildingChunks / ResidencyKey.ChunksPerEdge);
                for (int dz = 0; dz < span; dz++) for (int dx = 0; dx < span; dx++)
                { var c = m.CombineChunk(bx + dx, bz + dz, hide); if (c.TriangleCount > 0) p.Meshes.Add(c); }
            }
            long v = 0, i = 0;
            foreach (var mesh in p.Meshes) { v += mesh.VertexCount; i += mesh.Indices.Length; }
            p.GpuBytes = key.Kind == ResidencyKind.Render ? v * 32 + i * 4 : 0;
            p.CpuBytes = v * 32 + i * 4;
            return p;
        }

        // terrain meshes carry their own chunk grid (64 chunks of 256 m on a 2048 m tile): same indexing as the physics chunk
        static bool InChunk(int cx, int cz, ResidencyKey key, GeoTileModel m) => cx == key.ChunkX && cz == key.ChunkZ;
    }
}
