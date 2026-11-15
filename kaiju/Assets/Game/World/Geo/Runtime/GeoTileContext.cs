// Loads one baked geo_tile@v1 folder: refuses incomplete manifests, decodes terrain.r16 and water.png.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Game.World.Geo
{
    /// <summary>Full-resolution data surface (r16) plus the rendered LOD surface (set by the terrain layer).</summary>
    public sealed class GeoHeightField : IGeoHeightField
    {
        public readonly float[] Data;        // [row = z (south first)][col = x], NAVD88 m
        public float[] Render;               // same lattice, heights of the rendered mesh at the nodes
        public int Samples { get; }
        public double Spacing { get; }
        public double HMin, HMax;

        public GeoHeightField(float[] data, int samples, double spacing) { Data = data; Samples = samples; Spacing = spacing; }

        public static GeoHeightField FromR16(byte[] raw, int samples, double spacing, double hMin, double hMax)
        {
            if (raw.Length != samples * samples * 2) throw new InvalidDataException("terrain.r16 has " + raw.Length + " bytes, expected " + samples * samples * 2);
            var h = new float[samples * samples];
            double k = (hMax - hMin) / 65535.0;
            for (int i = 0; i < h.Length; i++) h[i] = (float)(hMin + (raw[2 * i] | (raw[2 * i + 1] << 8)) * k);
            return new GeoHeightField(h, samples, spacing) { HMin = hMin, HMax = hMax };
        }

        static double Bilinear(float[] a, int n, double spacing, double x, double z)
        {
            double fx = x / spacing, fz = z / spacing;
            if (fx < 0) fx = 0; if (fz < 0) fz = 0;
            if (fx > n - 1) fx = n - 1; if (fz > n - 1) fz = n - 1;
            int c = Math.Min((int)fx, n - 2), r = Math.Min((int)fz, n - 2);
            double tx = fx - c, tz = fz - r;
            double h00 = a[r * n + c], h10 = a[r * n + c + 1], h01 = a[(r + 1) * n + c], h11 = a[(r + 1) * n + c + 1];
            return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
        }

        public double SampleData(double x, double z) => Bilinear(Data, Samples, Spacing, x, z);
        public double SampleRender(double x, double z) => Bilinear(Render ?? Data, Samples, Spacing, x, z);
    }

    public sealed class GeoTileContext : IGeoTileContext
    {
        readonly string _dir;
        readonly Dictionary<string, IReadOnlyDictionary<string, object>> _layers = new Dictionary<string, IReadOnlyDictionary<string, object>>(StringComparer.Ordinal);
        readonly IReadOnlyDictionary<string, double> _settings;

        public IReadOnlyDictionary<string, object> Manifest { get; }
        public GeoTileId Tile { get; }
        public double OriginX { get; }
        public double OriginZ { get; }
        public double WaterLevelNavd88 { get; }
        public IGeoHeightField Terrain => Heights;
        public GeoHeightField Heights { get; internal set; }
        public bool[] WaterCells { get; internal set; }
        /// <summary>Shared-border policy when the tile has neighbours in a set (null = single tile, output unchanged). Set before building the model.</summary>
        public GeoEdgePolicy Edge { get; set; }

        /// <summary>Opens and validates a tile folder; throws GeoTileRejectedException on any manifest problem.</summary>
        public GeoTileContext(string tileDirectory, IReadOnlyDictionary<string, double> settings = null, bool verifyHashes = true)
        {
            _dir = tileDirectory;
            _settings = settings ?? new Dictionary<string, double>();
            var path = Path.Combine(tileDirectory, "manifest.json");
            if (!File.Exists(path)) throw new GeoTileRejectedException(tileDirectory, new[] { "manifest.json missing" });
            IReadOnlyDictionary<string, object> man;
            try { man = GeoJson.Obj(GeoJson.Parse(File.ReadAllText(path))); }
            catch (FormatException e) { throw new GeoTileRejectedException(tileDirectory, new[] { "manifest.json malformed: " + e.Message }); }
            var problems = GeoManifestValidator.Validate(man, verifyHashes ? (Func<string, byte[]>)ReadBytes : null);
            if (problems.Count > 0) throw new GeoTileRejectedException(GeoJson.Str(man, "tile") ?? tileDirectory, problems);
            Manifest = man;
            if (!GeoTileId.TryParse(GeoJson.Str(man, "tile"), out var id)) throw new GeoTileRejectedException(tileDirectory, new[] { "bad tile id" });
            Tile = id;
            var origin = GeoJson.Arr(GeoJson.Get(man, "origin_baytm_m"));
            OriginX = (double)origin[0];
            OriginZ = (double)origin[1];
            if (OriginX != id.OriginX || OriginZ != id.OriginZ)
                throw new GeoTileRejectedException(id.ToString(), new[] { "origin_baytm_m does not match the tile index" });
            WaterLevelNavd88 = GeoJson.Num(man, "water_level_navd88_m");
            foreach (var lo in GeoJson.Arr(GeoJson.Get(man, "layers")))
            {
                var L = GeoJson.Obj(lo);
                _layers[GeoJson.Str(L, "layer")] = L;
            }
        }

        public string ReadText(string file) => File.ReadAllText(Path.Combine(_dir, file));
        public byte[] ReadBytes(string file) => File.ReadAllBytes(Path.Combine(_dir, file));
        public IReadOnlyDictionary<string, object> ManifestLayer(string layer) => _layers.TryGetValue(layer, out var l) ? l : null;
        public double Setting(string key, double fallback) => _settings.TryGetValue(key, out var v) ? v : fallback;
    }

    /// <summary>Decoder for the 8-bit greyscale, non-interlaced PNGs the bake writes (water.png).</summary>
    public static class PngGray8
    {
        public static byte[] Decode(byte[] png, out int width, out int height)
        {
            if (png.Length < 8 || png[0] != 0x89 || png[1] != (byte)'P') throw new InvalidDataException("not a PNG");
            int p = 8;
            width = height = 0;
            var idat = new MemoryStream();
            while (p < png.Length)
            {
                int len = (png[p] << 24) | (png[p + 1] << 16) | (png[p + 2] << 8) | png[p + 3];
                string type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
                int d = p + 8;
                if (type == "IHDR")
                {
                    width = (png[d] << 24) | (png[d + 1] << 16) | (png[d + 2] << 8) | png[d + 3];
                    height = (png[d + 4] << 24) | (png[d + 5] << 16) | (png[d + 6] << 8) | png[d + 7];
                    if (png[d + 8] != 8 || png[d + 9] != 0 || png[d + 12] != 0) throw new InvalidDataException("PNG must be 8-bit greyscale, non-interlaced");
                }
                else if (type == "IDAT") idat.Write(png, d, len);
                else if (type == "IEND") break;
                p = d + len + 4;
            }
            var z = idat.ToArray();
            var raw = new MemoryStream();
            using (var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 2), CompressionMode.Decompress)) ds.CopyTo(raw);
            var r = raw.ToArray();
            var o = new byte[width * height];
            int stride = width + 1;
            for (int y = 0; y < height; y++)
            {
                int f = r[y * stride];
                for (int x = 0; x < width; x++)
                {
                    int a = x > 0 ? o[y * width + x - 1] : 0;
                    int b = y > 0 ? o[(y - 1) * width + x] : 0;
                    int c = x > 0 && y > 0 ? o[(y - 1) * width + x - 1] : 0;
                    int v = r[y * stride + 1 + x];
                    switch (f)
                    {
                        case 1: v += a; break;
                        case 2: v += b; break;
                        case 3: v += (a + b) >> 1; break;
                        case 4: { int pp = a + b - c, pa = Math.Abs(pp - a), pb = Math.Abs(pp - b), pc = Math.Abs(pp - c); v += pa <= pb && pa <= pc ? a : (pb <= pc ? b : c); break; }
                    }
                    o[y * width + x] = (byte)v;
                }
            }
            return o;
        }
    }
}
