// T-021 step 4a: M-015 floating-origin probe. A probe body travels the straight BayTM line Golden Gate centre -> SoMa centre and back
// in 50 m steps over the 15-tile set; the origin rebases by GeoFloatingOrigin + GeoTileSetBuilder.ShiftOrigin.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Game.World.Geo.Tests;
using NUnit.Framework;
using UnityEngine;

namespace Game.World.Geo.Unity.Tests
{
    [Category("M015")]
    public class GeoFloatingOriginTests
    {
        static string TilesRoot => Path.Combine(GeoTestData.ProjectRoot(), "Assets", "StreamingAssets", "Geo", "tiles", "v1");

        static double UlpM(float v)
        {
            float a = Math.Abs(v);
            int bits = BitConverter.SingleToInt32Bits(a);
            return BitConverter.Int32BitsToSingle(bits + 1) - (double)a;
        }

        [Test]
        public void Traversal_GoldenGateToSoMaAndBack_KeepsUnityPositionsSmall()
        {
            var ids = GeoTileSet.Range(0, 25, 27, 36, 38);      // Golden Gate 3 x 3
            ids.AddRange(GeoTileSet.Range(0, 29, 31, 35, 36));  // SoMa 3 x 2
            var absent = GeoTileSetLoader.Absent(TilesRoot, ids);
            if (absent.Count > 0) Assert.Ignore("tile folders absent: " + string.Join(", ", absent));
            var set = GeoTileSetLoader.Load(TilesRoot, ids, verifyHashes: false);
            var gg = GeoTileId.From(0, 26, 37); var soma = GeoTileId.From(0, 30, 36);
            var b = new GeoTileSetBuilder(set, gg, GeoTestData.Settings()) { BuildingColliders = false, TerrainCollider = true };
            try
            {
                b.BuildAll();
                double ax = gg.OriginX + 1024, az = gg.OriginZ + 1024, cx = soma.OriginX + 1024, cz = soma.OriginZ + 1024;
                double len = Math.Sqrt((cx - ax) * (cx - ax) + (cz - az) * (cz - az));
                int steps = (int)Math.Ceiling(len / 50.0);
                double water = b.World.WaterLevelNavd88;

                int samples = 0, shifts = 0, rayCount = 0;
                double maxPos = 0, maxUlp = 0, maxRound = 0, maxRay = 0;
                for (int leg = 0; leg < 2; leg++)
                    for (int k = 0; k <= steps; k++)
                    {
                        double t = (leg == 0 ? k : steps - k) / (double)steps;
                        double bx = ax + (cx - ax) * t, bz = az + (cz - az) * t;
                        GeoFloatingOrigin.Update(b.FloatingOriginX, b.FloatingOriginZ, bx, bz, out var nx, out var nz);
                        if (nx != b.FloatingOriginX || nz != b.FloatingOriginZ) { b.ShiftOrigin(nx, nz); shifts++; }

                        var p = new Vector3((float)(bx - b.FloatingOriginX), 0f, (float)(bz - b.FloatingOriginZ));
                        double mag = Math.Max(Math.Abs(p.x), Math.Abs(p.z));
                        maxPos = Math.Max(maxPos, mag);
                        maxUlp = Math.Max(maxUlp, UlpM((float)mag));
                        b.Instances[0].ToWorld(p, out var wx, out _, out var wz);
                        maxRound = Math.Max(maxRound, Math.Max(Math.Abs(wx - bx), Math.Abs(wz - bz)));

                        double h = b.World.SampleTerrain(bx, bz);
                        if (!double.IsNaN(h) && h > water + 0.5)
                        {
                            var o = new Vector3(p.x, 2000f, p.z);
                            if (Physics.Raycast(o, Vector3.down, out var hit, 5000f))
                            {
                                maxRay = Math.Max(maxRay, Math.Abs(hit.point.y - h));
                                rayCount++;
                            }
                        }
                        samples++;
                    }

                var inv = CultureInfo.InvariantCulture;
                string json = "{\n" +
                    "  \"metric\": \"M-015\",\n" +
                    "  \"route\": \"L0_26_37 centre -> L0_30_36 centre and back\",\n" +
                    "  \"route_length_m\": " + len.ToString("F1", inv) + ",\n" +
                    "  \"step_m\": 50,\n" +
                    "  \"samples\": " + samples + ",\n" +
                    "  \"origin_shifts\": " + shifts + ",\n" +
                    "  \"raycast_samples\": " + rayCount + ",\n" +
                    "  \"max_abs_unity_position_m\": " + maxPos.ToString("F3", inv) + ",\n" +
                    "  \"max_float32_ulp_mm\": " + (maxUlp * 1000).ToString("F4", inv) + ",\n" +
                    "  \"max_roundtrip_error_mm\": " + (maxRound * 1000).ToString("F4", inv) + ",\n" +
                    "  \"max_raycast_vs_sample_m\": " + maxRay.ToString("F4", inv) + "\n}\n";
                var dir = Path.Combine(GeoTestData.ProjectRoot(), "evidence", "T-021");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "m015_floating_origin.json"), json);

                Assert.Greater(shifts, 0, "traversal must rebase at least once");
                Assert.Greater(rayCount, 0, "no on-land raycast samples");
                Assert.LessOrEqual(maxPos, 4096.0, "max |unity position|");
                Assert.LessOrEqual(maxUlp, 0.00049, "float32 ulp (m)");
                Assert.LessOrEqual(maxRound, 0.001, "BayTM round-trip (m)");
                Assert.LessOrEqual(maxRay, 0.05, "raycast vs SampleTerrain (m)");
            }
            finally { UnityEngine.Object.DestroyImmediate(b.Root); }
        }
    }
}
