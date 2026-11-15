// geo_lod@v1 render tiles L1..L4 behind LevelRoutingReader (T-111b). Reads `<root>/L{n}/{ii}_{jj}/` baked by Pipelines/geo/bake_lod.py:
// terrain.r16 -> (journal craters) -> RTIN -> terrain chunks, b1/b2 .lodmesh -> one GeoMeshData per 256 m chunk (journal removals dropped by
// index range), water.png -> merged water quads. Engine-free, runs on workers. Meshes are tile-local metres like the L0 reader's.
// Reading never relaxes a threshold or cap: the terrain RTIN threshold is the manifest's nominal one unless the caller overrides it.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Game.Core;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    public sealed class GeoLodOptions
    {
        /// <summary>Terrain RTIN threshold in metres: null = the manifest's nominal `rtin_threshold_m` (0.1 x 2^n); a value overrides it for every tile (measurement and budget experiments).</summary>
        public double? TerrainThresholdM;
        /// <summary>Use the manifest's `rtin_threshold_for_cap_m` (smallest ladder threshold that fits 150 k) where it is larger than the nominal. Off by default: the threshold is the lead's decision.</summary>
        public bool UseCapThreshold;
        /// <summary>RTIN chunk edge in cells (the tile is meshed as (2048 / this)^2 independent chunks).</summary>
        public int TerrainChunkCells = 256;
        public double WaterLevelNavd88 = 0.969;
        public int WaterChunkCells = 256;
        /// <summary>Force every border node of a border that has a same-level neighbour on disk, so neighbours agree on the shared edge (no cracks).</summary>
        public bool ForceSharedBorders = true;
        /// <summary>Validate a tile folder (manifest, hashes) the first time it is read.</summary>
        public bool Validate = true;
        /// <summary>Keep the mesh heights at every lattice node in `LodTileResult.TerrainRenderHeights` (tests and error measurement; 16 MB per tile).</summary>
        public bool KeepTerrainHeights;
    }

    /// <summary>What one LOD tile read produced, for measurement and tests.</summary>
    public sealed class LodTileResult
    {
        public GeoTileId Tile;
        public readonly List<GeoMeshData> Meshes = new List<GeoMeshData>();
        public double TerrainThresholdM;
        public int TerrainTriangles, B1Triangles, B2Triangles, WaterTriangles;
        public int RemovedBuildingsHidden, TerrainOpsApplied, TerrainNodesChanged;
        public double TerrainMaxErrorM, TerrainRmsErrorM;
        public float[] TerrainRenderHeights;
    }

    public sealed class GeoLodTileReader : ITileReader
    {
        readonly string _root;
        readonly StreamingJournal _journal;
        readonly GeoLodOptions _opt;
        readonly ConcurrentDictionary<string, bool> _validated = new ConcurrentDictionary<string, bool>();

        public GeoLodTileReader(string lodRoot, StreamingJournal journal = null, GeoLodOptions options = null)
        { _root = lodRoot ?? throw new ArgumentNullException(nameof(lodRoot)); _journal = journal; _opt = options ?? new GeoLodOptions(); }

        /// <summary>Registers one reader for L1..L4 on the router.</summary>
        public static GeoLodTileReader RegisterAll(LevelRoutingReader router, string lodRoot, StreamingJournal journal = null, GeoLodOptions options = null)
        {
            var r = new GeoLodTileReader(lodRoot, journal, options);
            for (int n = 1; n <= LodSpec.MaxLevel; n++) router.Register(n, r);
            return r;
        }

        public bool Exists(GeoTileId id) => id.Level >= 1 && id.Level <= LodSpec.MaxLevel && File.Exists(Path.Combine(LodSpec.TileFolder(_root, id), "manifest.json"));

        public TilePayload Read(ResidencyKey key, CancellationToken ct)
        {
            if (key.Tile.Level < 1 || key.Tile.Level > LodSpec.MaxLevel) throw new NotSupportedException("GeoLodTileReader reads levels 1.." + LodSpec.MaxLevel + " only");
            if (key.Kind != ResidencyKind.Render) throw new NotSupportedException("pyramid levels have no physics chunks");
            var r = Load(key.Tile, ct);
            var p = new TilePayload { Key = key };
            long v = 0, i = 0;
            foreach (var m in r.Meshes) { p.Meshes.Add(m); v += m.VertexCount; i += m.Indices.Length; }
            p.GpuBytes = v * 32 + i * 4;
            p.CpuBytes = p.GpuBytes;
            return p;
        }

        public LodTileResult Load(GeoTileId id, CancellationToken ct = default)
        {
            var dir = LodSpec.TileFolder(_root, id);
            if (!File.Exists(Path.Combine(dir, "manifest.json"))) throw new FileNotFoundException("no geo_lod@v1 tile " + id, dir);
            if (_opt.Validate && !_validated.ContainsKey(dir))
            {
                var problems = LodSpec.Validate(dir, id);
                if (problems.Count > 0) throw new InvalidDataException("geo_lod@v1 tile " + id + " invalid: " + string.Join("; ", problems));
                _validated[dir] = true;
            }
            var man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))));
            var res = new LodTileResult { Tile = id };
            var removed = RemovedForTile(id, out var ops);
            ct.ThrowIfCancellationRequested();
            Terrain(dir, id, man, ops, res, ct);
            ct.ThrowIfCancellationRequested();
            var forms = GeoJson.Obj(GeoJson.Get(man, "buildings"));
            foreach (var form in LodSpec.FormsOfLevel(id.Level))
            {
                var fo = GeoJson.Obj(GeoJson.Get(forms, "form_" + form));
                if (fo == null) continue;
                Form(dir, id, form, GeoJson.Str(fo, "file"), GeoJson.Str(fo, "index"), removed, res);
                ct.ThrowIfCancellationRequested();
            }
            Water(dir, id, res);
            return res;
        }

        // ---- journal (T-110) over the level: every contributing L0 tile's removals and the craters deep enough for this level ----

        HashSet<BuildingId> RemovedForTile(GeoTileId id, out List<TerrainOp> ops)
        {
            ops = null;
            if (_journal == null) return null;
            int f = 1 << id.Level;
            HashSet<BuildingId> removed = null;
            for (int b = id.J * f; b < (id.J + 1) * f; b++)
                for (int a = id.I * f; a < (id.I + 1) * f; a++)
                {
                    var snap = _journal.Snapshot(GeoTileId.From(0, a, b));
                    if (snap.IsEmpty) continue;
                    if (snap.Removed.Count > 0) { if (removed == null) removed = new HashSet<BuildingId>(); foreach (var r in snap.Removed) removed.Add(r); }
                    foreach (var op in snap.Ops)
                    {
                        if (!op.AffectsLevel(id.Level)) continue;
                        if (ops == null) ops = new List<TerrainOp>();
                        if (!ops.Contains(op)) ops.Add(op);        // one crater is recorded in every L0 tile its disc overlaps
                    }
                }
            return removed;
        }

        // ---- terrain ----

        void Terrain(string dir, GeoTileId id, IReadOnlyDictionary<string, object> man, List<TerrainOp> ops, LodTileResult res, CancellationToken ct)
        {
            var t = GeoJson.Obj(GeoJson.Get(man, "terrain"));
            int n = (int)GeoJson.Num(t, "samples", LodSpec.Samples);
            double sp = GeoJson.Num(t, "spacing_m");
            var hf = GeoHeightField.FromR16(File.ReadAllBytes(Path.Combine(dir, GeoJson.Str(t, "file"))), n, sp, GeoJson.Num(t, "h_min"), GeoJson.Num(t, "h_max"));
            if (ops != null && ops.Count > 0)
            {
                res.TerrainOpsApplied = ops.Count;
                res.TerrainNodesChanged = TerrainOps.Apply(hf.Data, n, sp, id.OriginX, id.OriginZ, ops);
            }
            double nominal = GeoJson.Num(t, "rtin_threshold_m"), thr = nominal;
            if (_opt.TerrainThresholdM.HasValue) thr = _opt.TerrainThresholdM.Value;
            else if (_opt.UseCapThreshold) { double c = GeoJson.Num(t, "rtin_threshold_for_cap_m", nominal); if (c > thr) thr = c; }
            res.TerrainThresholdM = thr;
            bool[] force = null;
            if (_opt.ForceSharedBorders)
            {
                // N, E, S, W (GeoDirection order); a border is forced when the neighbour of the same level is baked
                force = new bool[4];
                force[0] = Exists(Neighbour(id, 0, 1)); force[1] = Exists(Neighbour(id, 1, 0)); force[2] = Exists(Neighbour(id, 0, -1)); force[3] = Exists(Neighbour(id, -1, 0));
                if (!force[0] && !force[1] && !force[2] && !force[3]) force = null;
            }
            ct.ThrowIfCancellationRequested();
            var rt = RtinMesher.Build(hf.Data, n, sp, (float)thr, _opt.TerrainChunkCells, "terrain", force);
            res.Meshes.AddRange(rt.Chunks);
            res.TerrainTriangles = rt.Triangles;
            res.TerrainMaxErrorM = rt.MaxErrorM;
            res.TerrainRmsErrorM = rt.RmsErrorM;
            if (_opt.KeepTerrainHeights) res.TerrainRenderHeights = rt.RenderHeights;
        }

        static GeoTileId Neighbour(GeoTileId id, int di, int dj)
        {
            int i = id.I + di, j = id.J + dj;
            return i < 0 || j < 0 ? id : GeoTileId.From(id.Level, i, j);
        }

        // ---- buildings (b1 / b2) ----

        void Form(string dir, GeoTileId id, string form, string meshFile, string indexFile, HashSet<BuildingId> removed, LodTileResult res)
        {
            var mesh = LodMesh.Decode(File.ReadAllBytes(Path.Combine(dir, meshFile)));
            var index = LodBuildingIndex.Parse(File.ReadAllText(Path.Combine(dir, indexFile)));
            // removed rows: the building itself or its parent structure (a structure's parts go with it, as in GeoTileModel.CombineChunk)
            HashSet<int> hide = null;
            if (removed != null && removed.Count > 0)
                for (int k = 0; k < index.Bids.Count; k++)
                {
                    if ((TryBid(index.Bids[k], out var b) && removed.Contains(b)) || (index.PartOf[k] != null && TryBid(index.PartOf[k], out var p) && removed.Contains(p))
                        || AllMembersRemoved(index.Members[k], index.MemberPartOf[k], removed))
                    { if (hide == null) hide = new HashSet<int>(); hide.Add(k); }
                }
            double ox = id.OriginX, oz = id.OriginZ;
            var byChunk = new List<LodRange>[mesh.Chunks.Length];
            if (hide != null)
                foreach (var r in mesh.Ranges)
                    if (hide.Contains(r.BidIndex)) { (byChunk[r.ChunkIndex] ?? (byChunk[r.ChunkIndex] = new List<LodRange>())).Add(r); res.RemovedBuildingsHidden++; }
            int tris = 0;
            for (int ci = 0; ci < mesh.Chunks.Length; ci++)
            {
                var c = mesh.Chunks[ci];
                var cut = byChunk[ci];
                var keep = new bool[c.IndexCount];
                for (int k = 0; k < keep.Length; k++) keep[k] = true;
                if (cut != null) foreach (var r in cut) for (int k = r.FirstIndex; k < r.FirstIndex + r.IndexCount; k++) keep[k] = false;
                int kept = 0;
                for (int k = 0; k < keep.Length; k++) if (keep[k]) kept++;
                if (kept == 0) continue;
                // compact the vertices when something was cut, else keep the chunk's vertex window as is
                var remap = new int[c.VertexCount];
                for (int k = 0; k < remap.Length; k++) remap[k] = cut == null ? k : -1;
                var idx = new int[kept];
                int w = 0, nv = cut == null ? c.VertexCount : 0;
                for (int k = 0; k < keep.Length; k++)
                {
                    if (!keep[k]) continue;
                    int lv = mesh.Indices[c.IndexFirst + k];
                    if (remap[lv] < 0) remap[lv] = nv++;
                    idx[w++] = remap[lv];
                }
                var pos = new float[nv * 3]; var nrm = new float[nv * 3];
                for (int lv = 0; lv < c.VertexCount; lv++)
                {
                    int to = remap[lv];
                    if (to < 0) continue;
                    int g = c.VertexFirst + lv;
                    pos[3 * to] = (float)(mesh.Positions[3 * g] - ox); pos[3 * to + 1] = (float)mesh.Positions[3 * g + 1]; pos[3 * to + 2] = (float)(mesh.Positions[3 * g + 2] - oz);
                    nrm[3 * to] = mesh.Normals[3 * g]; nrm[3 * to + 1] = mesh.Normals[3 * g + 1]; nrm[3 * to + 2] = mesh.Normals[3 * g + 2];
                }
                res.Meshes.Add(new GeoMeshData
                {
                    Layer = "buildings", Material = "building", Name = form + "_" + c.Cx + "_" + c.Cz, Positions = pos, Normals = nrm, Indices = idx, ChunkX = c.Cx, ChunkZ = c.Cz,
                });
                tris += kept / 3;
            }
            if (form == "b1") res.B1Triangles += tris; else res.B2Triangles += tris;
        }

        /// <summary>A merged block (T-108d) stands for several buildings: it goes once all of them, each by its own id or its parent structure, are removed.</summary>
        static bool AllMembersRemoved(string[] members, string[] memberPartOf, HashSet<BuildingId> removed)
        {
            if (members == null || members.Length == 0) return false;
            for (int m = 0; m < members.Length; m++)
            {
                bool gone = (TryBid(members[m], out var b) && removed.Contains(b))
                    || (memberPartOf != null && memberPartOf[m] != null && TryBid(memberPartOf[m], out var p) && removed.Contains(p));
                if (!gone) return false;
            }
            return true;
        }

        static bool TryBid(string bid, out BuildingId id)
        {
            id = default;
            try { id = BuildingsLayer.ParseBid(bid); return true; }
            catch (FormatException) { return false; }        // landmark:* and other non-OSM ids have no journal entry
        }

        // ---- water ----

        void Water(string dir, GeoTileId id, LodTileResult res)
        {
            var pngPath = Path.Combine(dir, "water.png");
            if (!File.Exists(pngPath)) return;
            var px = PngGray8.Decode(File.ReadAllBytes(pngPath), out int w, out int h);
            double cell = id.SizeM / w;
            int chunk = _opt.WaterChunkCells;
            if (w % chunk != 0 || h % chunk != 0) chunk = w;
            float y = (float)_opt.WaterLevelNavd88;
            for (int cz = 0; cz < h / chunk; cz++)
                for (int cx = 0; cx < w / chunk; cx++)
                {
                    var pos = new List<float>(); var idx = new List<int>();
                    // runs of wet cells per row, merged vertically while the same run repeats (greedy rectangles)
                    var open = new Dictionary<(int, int), int>();
                    var closed = new List<(int c0, int c1, int r0, int r1)>();
                    for (int r = 0; r <= chunk; r++)
                    {
                        var runs = new List<(int, int)>();
                        if (r < chunk)
                        {
                            int pr = h - 1 - (cz * chunk + r);          // cell row from the south; png row 0 = north
                            int c = 0;
                            while (c < chunk)
                            {
                                if (px[pr * w + cx * chunk + c] < 128) { c++; continue; }
                                int c0 = c;
                                while (c < chunk && px[pr * w + cx * chunk + c] >= 128) c++;
                                runs.Add((c0, c));
                            }
                        }
                        var still = new HashSet<(int, int)>(runs);
                        foreach (var kv in new List<KeyValuePair<(int, int), int>>(open))
                            if (!still.Contains(kv.Key)) { closed.Add((kv.Key.Item1, kv.Key.Item2, kv.Value, r)); open.Remove(kv.Key); }
                        foreach (var run in runs) if (!open.ContainsKey(run)) open[run] = r;
                    }
                    closed.Sort();
                    foreach (var q in closed)
                    {
                        float x0 = (float)((cx * chunk + q.c0) * cell), x1 = (float)((cx * chunk + q.c1) * cell);
                        float z0 = (float)((cz * chunk + q.r0) * cell), z1 = (float)((cz * chunk + q.r1) * cell);
                        int b = pos.Count / 3;
                        pos.Add(x0); pos.Add(y); pos.Add(z0); pos.Add(x0); pos.Add(y); pos.Add(z1); pos.Add(x1); pos.Add(y); pos.Add(z1); pos.Add(x1); pos.Add(y); pos.Add(z0);
                        idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
                    }
                    if (idx.Count == 0) continue;
                    var nrm = new float[pos.Count];
                    for (int k = 1; k < nrm.Length; k += 3) nrm[k] = 1f;
                    res.Meshes.Add(new GeoMeshData { Layer = "water", Material = "water", Name = "water_" + cx + "_" + cz, Positions = pos.ToArray(), Normals = nrm, Indices = idx.ToArray(), ChunkX = cx, ChunkZ = cz });
                    res.WaterTriangles += idx.Count / 3;
                }
        }
    }
}
