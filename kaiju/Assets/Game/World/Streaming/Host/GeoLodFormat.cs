// geo_lod@v1 on the C# side (T-111b): the validator mirror of `kaiju_geo/lod.py` `validate_level_tile` and the `.lodmesh` decoder
// (mirror of `read_lodmesh`). Engine-free, no dependency on the streaming host: usable from tools and tests.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.World.Geo;

namespace Game.World.Streaming.Host
{
    /// <summary>The geo_lod@v1 contract: constants and the tile validator (same checks as `lod.validate_level_tile`).</summary>
    public static class LodSpec
    {
        public const string Spec = "geo_lod@v1";
        public const int MaxLevel = 4;
        public const int Samples = 2049;
        public const double ChunkM = 256.0;
        /// <summary>Forms present per level (R-101 section 4): L1 B1; L2 B1 + B2; L3, L4 B2.</summary>
        public static string[] FormsOfLevel(int level) => level == 1 ? new[] { "b1" } : level == 2 ? new[] { "b1", "b2" } : level == 3 || level == 4 ? new[] { "b2" } : new string[0];

        public static readonly string[] TopFields =
        {
            "spec", "tile", "level", "i", "j", "bounds_baytm", "source_level", "source_fingerprint", "terrain", "buildings", "water",
            "errors", "stats", "chunk_m", "files", "attribution", "licence_ok",
        };

        public static string TileFolder(string root, GeoTileId id) => Path.Combine(root, "L" + id.Level, id.I.ToString("00") + "_" + id.J.ToString("00"));

        /// <summary>Problems of the tile folder (empty = valid): completeness, errors present, file hashes, form files listed, level/i/j match.</summary>
        public static List<string> Validate(string tileDir, GeoTileId? expect = null, bool checkHashes = true)
        {
            var errs = new List<string>();
            var mp = Path.Combine(tileDir, "manifest.json");
            if (!File.Exists(mp)) { errs.Add("manifest.json missing"); return errs; }
            IReadOnlyDictionary<string, object> man;
            try { man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(mp))); }
            catch (Exception e) { errs.Add("manifest.json unreadable: " + e.Message); return errs; }
            if (man == null) { errs.Add("manifest.json is not an object"); return errs; }
            foreach (var k in TopFields)
            {
                var v = GeoJson.Get(man, k);
                if (!man.ContainsKey(k) || IsEmpty(v))
                {
                    if (k == "licence_ok" && v is bool bl && !bl) continue;
                    if (k == "attribution" && v is List<object> al && al.Count == 0) continue;
                    errs.Add("missing top-level field '" + k + "'");
                }
            }
            if (GeoJson.Str(man, "spec") != Spec) errs.Add("spec is '" + GeoJson.Str(man, "spec") + "', expected '" + Spec + "'");
            var e2 = GeoJson.Obj(GeoJson.Get(man, "errors"));
            Need(errs, e2, "terrain", "max_m", "rms_m", "p99_m");
            Need(errs, e2, "buildings", "footprint_max_m", "footprint_rms_m", "height_max_m");
            var files = GeoJson.Obj(GeoJson.Get(man, "files"));
            if (files != null)
                foreach (var kv in files)
                {
                    var p = Path.Combine(tileDir, kv.Key);
                    if (!File.Exists(p)) errs.Add("file " + kv.Key + " missing");
                    else if (checkHashes && Sha256Hex(File.ReadAllBytes(p)) != (kv.Value as string)) errs.Add("sha256 mismatch for " + kv.Key);
                }
            var forms = GeoJson.Obj(GeoJson.Get(man, "buildings"));
            if (forms != null)
                foreach (var f in forms.Values)
                {
                    var fo = GeoJson.Obj(f);
                    foreach (var k in new[] { "file", "index" })
                        if (files == null || !files.ContainsKey(GeoJson.Str(fo, k) ?? "")) errs.Add("form file " + GeoJson.Str(fo, k) + " not in files");
                }
            if (expect.HasValue)
            {
                var id = expect.Value;
                if ((int)GeoJson.Num(man, "level", -1) != id.Level || (int)GeoJson.Num(man, "i", -1) != id.I || (int)GeoJson.Num(man, "j", -1) != id.J)
                    errs.Add("manifest names tile L" + GeoJson.Num(man, "level") + "_" + GeoJson.Num(man, "i") + "_" + GeoJson.Num(man, "j") + ", expected " + id);
            }
            var t = GeoJson.Obj(GeoJson.Get(man, "terrain"));
            if (t != null)
            {
                if ((int)GeoJson.Num(t, "samples", -1) != Samples) errs.Add("terrain.samples is not " + Samples);
                foreach (var k in new[] { "h_min", "h_max", "spacing_m", "rtin_threshold_m" })
                    if (double.IsNaN(GeoJson.Num(t, k))) errs.Add("terrain." + k + " missing");
            }
            return errs;
        }

        /// <summary>The source fingerprint check (`source_fingerprint` = sha256 over the sorted sha256 of the contributing L0 manifests joined by newlines).</summary>
        public static string SourceFingerprint(string l0TilesRoot, IEnumerable<GeoTileId> l0Tiles)
        {
            var shas = new List<string>();
            foreach (var t in l0Tiles) shas.Add(Sha256Hex(File.ReadAllBytes(Path.Combine(GeoTileSetLoader.TileDirectory(l0TilesRoot, t), "manifest.json"))));
            shas.Sort(StringComparer.Ordinal);
            return Sha256Hex(Encoding.UTF8.GetBytes(string.Join("\n", shas)));
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var h = SHA256.Create())
            {
                var d = h.ComputeHash(data);
                var sb = new StringBuilder(d.Length * 2);
                foreach (var b in d) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static bool IsEmpty(object v) => v == null || (v is string s && s.Length == 0) || (v is List<object> l && l.Count == 0) || (v is Dictionary<string, object> d && d.Count == 0);

        static void Need(List<string> errs, IReadOnlyDictionary<string, object> e, string part, params string[] keys)
        {
            var p = GeoJson.Obj(GeoJson.Get(e, part));
            foreach (var k in keys) if (p == null || !p.ContainsKey(k)) errs.Add("errors." + part + "." + k + " missing");
        }
    }

    /// <summary>One 256 m chunk of a .lodmesh: AABB (tile world coordinates, BayTM), vertex and index windows.</summary>
    public struct LodChunk
    {
        public int Cx, Cz;
        public float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public int VertexFirst, VertexCount, IndexFirst, IndexCount;
    }

    /// <summary>One building's index window inside a chunk (indices are chunk-local).</summary>
    public struct LodRange { public int BidIndex, ChunkIndex, FirstIndex, IndexCount; }

    /// <summary>Decoded `.lodmesh` (R-101 section 8.2): positions as BayTM metres (float64 from the chunk-quantised u16), oct-decoded unit normals, chunk-local u16 indices.</summary>
    public sealed class LodMesh
    {
        public int VertexCount, IndexCount;
        public LodChunk[] Chunks;
        public LodRange[] Ranges;
        public double[] Positions;      // 3 per vertex
        public float[] Normals;         // 3 per vertex
        public ushort[] Indices;

        public static LodMesh Decode(byte[] data)
        {
            if (data == null || data.Length < 32) throw new InvalidDataException("lodmesh too short");
            if (data[0] != (byte)'K' || data[1] != (byte)'L' || data[2] != (byte)'O' || data[3] != (byte)'D') throw new InvalidDataException("not a KLOD file");
            uint U(int o) => BitConverter.ToUInt32(data, o);
            if (U(4) != 1) throw new InvalidDataException("unsupported lodmesh version " + U(4));
            int vc = (int)U(8), ic = (int)U(12), cc = (int)U(16), rc = (int)U(20);
            var m = new LodMesh { VertexCount = vc, IndexCount = ic, Chunks = new LodChunk[cc], Ranges = new LodRange[rc] };
            int off = 32;
            long need = 32L + 48L * cc + 16L * rc;
            if (data.Length < need) throw new InvalidDataException("lodmesh truncated (tables)");
            for (int k = 0; k < cc; k++, off += 48)
                m.Chunks[k] = new LodChunk
                {
                    Cx = BitConverter.ToInt32(data, off), Cz = BitConverter.ToInt32(data, off + 4),
                    MinX = BitConverter.ToSingle(data, off + 8), MinY = BitConverter.ToSingle(data, off + 12), MinZ = BitConverter.ToSingle(data, off + 16),
                    MaxX = BitConverter.ToSingle(data, off + 20), MaxY = BitConverter.ToSingle(data, off + 24), MaxZ = BitConverter.ToSingle(data, off + 28),
                    VertexFirst = (int)U(off + 32), VertexCount = (int)U(off + 36), IndexFirst = (int)U(off + 40), IndexCount = (int)U(off + 44),
                };
            for (int k = 0; k < rc; k++, off += 16)
                m.Ranges[k] = new LodRange { BidIndex = (int)U(off), ChunkIndex = (int)U(off + 4), FirstIndex = (int)U(off + 8), IndexCount = (int)U(off + 12) };
            long posBytes = (long)vc * 6, nrmBytes = (long)vc * 2, idxBytes = (long)ic * 2;
            long total = off + posBytes + Pad(posBytes) + nrmBytes + Pad(nrmBytes) + idxBytes + Pad(idxBytes);
            if (data.Length < total) throw new InvalidDataException("lodmesh truncated (arrays)");
            int posOff = off;
            int nrmOff = posOff + (int)(posBytes + Pad(posBytes));
            int idxOff = nrmOff + (int)(nrmBytes + Pad(nrmBytes));
            m.Positions = new double[vc * 3];
            m.Normals = new float[vc * 3];
            foreach (var c in m.Chunks)
            {
                if (c.VertexFirst < 0 || c.VertexFirst + c.VertexCount > vc || c.IndexFirst < 0 || c.IndexFirst + c.IndexCount > ic) throw new InvalidDataException("chunk window outside the arrays");
                double lx = c.MinX, ly = c.MinY, lz = c.MinZ;
                double sx = (double)c.MaxX - c.MinX, sy = (double)c.MaxY - c.MinY, sz = (double)c.MaxZ - c.MinZ;
                if (!(sx > 0)) sx = 1.0; if (!(sy > 0)) sy = 1.0; if (!(sz > 0)) sz = 1.0;
                for (int v = c.VertexFirst; v < c.VertexFirst + c.VertexCount; v++)
                {
                    m.Positions[3 * v] = lx + BitConverter.ToUInt16(data, posOff + 6 * v) / 65535.0 * sx;
                    m.Positions[3 * v + 1] = ly + BitConverter.ToUInt16(data, posOff + 6 * v + 2) / 65535.0 * sy;
                    m.Positions[3 * v + 2] = lz + BitConverter.ToUInt16(data, posOff + 6 * v + 4) / 65535.0 * sz;
                }
            }
            for (int v = 0; v < vc; v++) OctDecode((sbyte)data[nrmOff + 2 * v], (sbyte)data[nrmOff + 2 * v + 1], m.Normals, 3 * v);
            m.Indices = new ushort[ic];
            for (int k = 0; k < ic; k++) m.Indices[k] = BitConverter.ToUInt16(data, idxOff + 2 * k);
            foreach (var r in m.Ranges)
            {
                if (r.ChunkIndex < 0 || r.ChunkIndex >= cc) throw new InvalidDataException("range names a missing chunk");
                if (r.FirstIndex < 0 || r.FirstIndex + r.IndexCount > m.Chunks[r.ChunkIndex].IndexCount) throw new InvalidDataException("range outside its chunk");
            }
            return m;
        }

        static long Pad(long bytes) => (4 - bytes % 4) % 4;

        /// <summary>Mirror of `oct_decode` (int8 x 2 per vertex).</summary>
        public static void OctDecode(sbyte qx, sbyte qy, float[] dst, int at)
        {
            double x = qx / 127.0, y = qy / 127.0;
            double z = 1 - Math.Abs(x) - Math.Abs(y);
            double t = Math.Max(-z, 0.0);
            x -= x >= 0 ? t : -t;
            y -= y >= 0 ? t : -t;
            double len = Math.Sqrt(x * x + y * y + z * z);
            if (len < 1e-12) len = 1e-12;
            dst[at] = (float)(x / len); dst[at + 1] = (float)(y / len); dst[at + 2] = (float)(z / len);
        }

        /// <summary>The encoder of `MeshBuilder.encode` for tests and tools: chunks sorted by (cx, cz), ranges as given (bidIndex, chunkIndex into the sorted chunk list, first, count).</summary>
        public static byte[] Encode(IList<(int cx, int cz, double[] pos, float[] nrm, ushort[] idx)> chunks, IList<LodRange> ranges)
        {
            var ordered = new List<(int cx, int cz, double[] pos, float[] nrm, ushort[] idx)>(chunks);
            ordered.Sort((a, b) => a.cx != b.cx ? a.cx.CompareTo(b.cx) : a.cz.CompareTo(b.cz));
            int vc = 0, ic = 0;
            foreach (var c in ordered) { vc += c.pos.Length / 3; ic += c.idx.Length; }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new byte[] { (byte)'K', (byte)'L', (byte)'O', (byte)'D' });
                w.Write(1u); w.Write((uint)vc); w.Write((uint)ic); w.Write((uint)ordered.Count); w.Write((uint)ranges.Count); w.Write(0u); w.Write(0u);
                var pos = new MemoryStream(); var nrm = new MemoryStream(); var idx = new MemoryStream();
                int vf = 0, idf = 0;
                foreach (var c in ordered)
                {
                    int n = c.pos.Length / 3;
                    float[] lo = { float.MaxValue, float.MaxValue, float.MaxValue }, hi = { float.MinValue, float.MinValue, float.MinValue };
                    for (int v = 0; v < n; v++)
                        for (int a = 0; a < 3; a++) { float f = (float)c.pos[3 * v + a]; if (f < lo[a]) lo[a] = f; if (f > hi[a]) hi[a] = f; }
                    w.Write(c.cx); w.Write(c.cz);
                    for (int a = 0; a < 3; a++) w.Write(lo[a]);
                    for (int a = 0; a < 3; a++) w.Write(hi[a]);
                    w.Write((uint)vf); w.Write((uint)n); w.Write((uint)idf); w.Write((uint)c.idx.Length);
                    for (int v = 0; v < n; v++)
                        for (int a = 0; a < 3; a++)
                        {
                            double span = (double)hi[a] - lo[a]; if (!(span > 0)) span = 1.0;
                            double q = Math.Round((c.pos[3 * v + a] - lo[a]) / span * 65535.0);
                            pos.Write(BitConverter.GetBytes((ushort)Math.Max(0, Math.Min(65535, q))), 0, 2);
                        }
                    for (int v = 0; v < n; v++) { OctEncode(c.nrm[3 * v], c.nrm[3 * v + 1], c.nrm[3 * v + 2], out sbyte ox, out sbyte oy); nrm.WriteByte((byte)ox); nrm.WriteByte((byte)oy); }
                    foreach (var i in c.idx) idx.Write(BitConverter.GetBytes(i), 0, 2);
                    vf += n; idf += c.idx.Length;
                }
                foreach (var r in ranges) { w.Write((uint)r.BidIndex); w.Write((uint)r.ChunkIndex); w.Write((uint)r.FirstIndex); w.Write((uint)r.IndexCount); }
                foreach (var s in new[] { pos, nrm, idx })
                {
                    var b = s.ToArray(); w.Write(b);
                    for (long p = Pad(b.Length); p > 0; p--) w.Write((byte)0);
                }
                return ms.ToArray();
            }
        }

        public static void OctEncode(double x, double y, double z, out sbyte ox, out sbyte oy)
        {
            double s = Math.Abs(x) + Math.Abs(y) + Math.Abs(z); if (s < 1e-12) s = 1e-12;
            x /= s; y /= s; z /= s;
            double px = x, py = y;
            if (z < 0) { px = (1 - Math.Abs(y)) * (x >= 0 ? 1.0 : -1.0); py = (1 - Math.Abs(x)) * (y >= 0 ? 1.0 : -1.0); }
            ox = (sbyte)Math.Max(-127, Math.Min(127, Math.Round(px * 127.0))); oy = (sbyte)Math.Max(-127, Math.Min(127, Math.Round(py * 127.0)));
        }
    }

    /// <summary>`b1.index.json` / `b2.index.json`: the sorted building rows a range's BidIndex points into.</summary>
    public sealed class LodBuildingIndex
    {
        public readonly List<string> Bids = new List<string>();
        public readonly List<string> PartOf = new List<string>();     // parent structure bid or null, parallel to Bids
        /// <summary>T-108d merged blocks (bake_lod.py@7): the building ids a block row stands for and, parallel, each member's parent structure (or null);
        /// null for an ordinary row. A block is hidden once every member (or its parent) is removed.</summary>
        public readonly List<string[]> Members = new List<string[]>();
        public readonly List<string[]> MemberPartOf = new List<string[]>();

        public static LodBuildingIndex Parse(string json)
        {
            var root = GeoJson.Obj(GeoJson.Parse(json));
            var idx = new LodBuildingIndex();
            var rows = GeoJson.Arr(GeoJson.Get(root, "buildings"));
            if (rows == null) throw new InvalidDataException("index.json has no 'buildings'");
            foreach (var r in rows)
            {
                var o = GeoJson.Obj(r);
                idx.Bids.Add(GeoJson.Str(o, "bid") ?? throw new InvalidDataException("index row without bid"));
                idx.PartOf.Add(GeoJson.Str(o, "part_of"));
                var mem = GeoJson.Arr(GeoJson.Get(o, "members"));
                if (mem == null || mem.Count == 0) { idx.Members.Add(null); idx.MemberPartOf.Add(null); continue; }
                var ms = new string[mem.Count]; var mp = new string[mem.Count];
                var mpArr = GeoJson.Arr(GeoJson.Get(o, "member_part_of"));
                for (int k = 0; k < mem.Count; k++)
                {
                    ms[k] = mem[k] as string;
                    mp[k] = mpArr != null && k < mpArr.Count ? mpArr[k] as string : null;
                }
                idx.Members.Add(ms); idx.MemberPartOf.Add(mp);
            }
            return idx;
        }
    }
}
