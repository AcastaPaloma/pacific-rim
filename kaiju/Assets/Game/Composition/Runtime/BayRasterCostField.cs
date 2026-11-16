// T-115d: Composition adapter that provides the pack layer's IBayCostField (GridRoutePlanner, R-103 6.1) over the T-102 Bay raster.
// The raster `bay_cost@v1` is exported from the baked L3 terrain + water tiles by Pipelines/geo/export_bay_cost.py (256 m cells, BayTM@v1 metres, origin (0, 0)):
// depth = area mean of max(0, -h) over the water nodes of the cell, softness = PROXY (low land <= 3 m NAVD88; the USGS liquefaction layer is not baked yet, Assumed).
// File layout (little endian): 'BCF1', int32 nx, int32 ny, float32 originX, float32 originY, float32 cellM, uint16 depth[ny*nx] (decimetres), uint8 soft[ny*nx] (0..255); row 0 = south.
// Engine-free; also runs in the dotnet harness (Game.AI.PackHarness.Run) as map `m007_bay_real@v1`.
using System;
using System.IO;
using Game.AI.Pack;

namespace Game.Composition
{
    public sealed class BayRasterCostField : IBayCostField
    {
        public const string MapId = "m007_bay_real@v1";
        public const string DefaultFileName = "bay_cost_256m_v1.bin";
        // T-115h: versioned data switch. bay_cost@v1 = depth + low-land softness proxy (evidence/T-115). bay_cost@v2 = same depth, softness from the USGS OFR 2006-1037
        // liquefaction layer inside its coverage, v1 proxy kept in the holes (Pipelines/geo/export_bay_softness.py, evidence/T-115g). Same BCF1 layout, so one reader serves both.
        public const string V1 = "v1";
        public const string V2 = "v2";
        public const string DefaultVersion = V2;

        public static string FileNameFor(string version)
        {
            switch (version)
            {
                case V1: return "bay_cost_256m_v1.bin";
                case V2: return "bay_cost_256m_v2.bin";
                default: throw new ArgumentException("unknown bay_cost version '" + version + "' (v1 | v2)");
            }
        }

        /// <summary>Evidence sub-folder (relative to the project root) that holds the raster of a version.</summary>
        public static string EvidenceFolderFor(string version) => version == V1 ? "T-115" : version == V2 ? "T-115g" : throw new ArgumentException("unknown bay_cost version '" + version + "'");

        /// <summary>Walks up from startDir to the project root (the folder with evidence/) and returns the raster path for a version, or null when it is not there.</summary>
        public static string FindRaster(string startDir, string version)
        {
            var d = new DirectoryInfo(startDir);
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "evidence", EvidenceFolderFor(version), FileNameFor(version));
                if (File.Exists(p)) return p;
                d = d.Parent;
            }
            return null;
        }
        private readonly ushort[] _depthDm;
        private readonly byte[] _soft;

        public float OriginX { get; }
        public float OriginY { get; }
        public float CellM { get; }
        public int Nx { get; }
        public int Ny { get; }

        public BayRasterCostField(int nx, int ny, float originX, float originY, float cellM, ushort[] depthDecimetres, byte[] softness)
        {
            if (nx <= 0 || ny <= 0 || cellM <= 0f) throw new ArgumentException("bad raster dimensions");
            if (depthDecimetres == null || depthDecimetres.Length != nx * ny) throw new ArgumentException("depth size");
            if (softness == null || softness.Length != nx * ny) throw new ArgumentException("softness size");
            Nx = nx; Ny = ny; OriginX = originX; OriginY = originY; CellM = cellM; _depthDm = depthDecimetres; _soft = softness;
        }

        public float DepthM(int ix, int iy) => _depthDm[iy * Nx + ix] * 0.1f;
        public float Softness(int ix, int iy) => _soft[iy * Nx + ix] * (1f / 255f);

        public static BayRasterCostField FromBytes(byte[] b)
        {
            if (b == null || b.Length < 24 || b[0] != (byte)'B' || b[1] != (byte)'C' || b[2] != (byte)'F' || b[3] != (byte)'1') throw new InvalidDataException("not a bay_cost raster (BCF1)");
            int nx = BitConverter.ToInt32(b, 4), ny = BitConverter.ToInt32(b, 8);
            float ox = BitConverter.ToSingle(b, 12), oy = BitConverter.ToSingle(b, 16), cell = BitConverter.ToSingle(b, 20);
            long n = (long)nx * ny;
            if (nx <= 0 || ny <= 0 || n > 64_000_000 || b.Length != 24 + 3 * n) throw new InvalidDataException("bay_cost raster size mismatch");
            var d = new ushort[n]; var s = new byte[n];
            for (long i = 0; i < n; i++) d[i] = BitConverter.ToUInt16(b, (int)(24 + 2 * i));
            Buffer.BlockCopy(b, (int)(24 + 2 * n), s, 0, (int)n);
            return new BayRasterCostField(nx, ny, ox, oy, cell, d, s);
        }

        public static BayRasterCostField Load(string path) => FromBytes(File.ReadAllBytes(path));
    }
}
