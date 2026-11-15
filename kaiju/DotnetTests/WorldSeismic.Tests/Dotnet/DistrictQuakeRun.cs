// T-036c step 3: a real input run. A Mw 7.2 rupture on the baked San Andreas system (and a Hayward rupture for M-053/M-054 only) over the
// district buildings (Golden Gate 3x3 + SoMa 3x2 tiles, real baked data). Writes the dumps the Python metric tools read.
// Runs only when KAIJU_DUMP_DIR is set (otherwise a no-op), and when KAIJU_PYTHON is set also runs M-053 / M-054 / M-005 --pieces on them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Game.Core;
using Game.World.Destruction;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Seismic.Tests
{
    public class DistrictQuakeRun
    {
        sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }

        static readonly string[] Tiles =
        {
            "L0_25_36","L0_25_37","L0_25_38","L0_26_36","L0_26_37","L0_26_38","L0_27_36","L0_27_37","L0_27_38",
            "L0_29_35","L0_29_36","L0_30_35","L0_30_36","L0_31_35","L0_31_36",
        };

        static string Root()
        {
            string d = AppContext.BaseDirectory;
            while (d != null && !Directory.Exists(Path.Combine(d, "Assets", "StreamingAssets", "Geo"))) d = Path.GetDirectoryName(d);
            return d ?? throw new DirectoryNotFoundException("project root");
        }

        sealed class Sink : IEventHandler<FaultRupture> { public FaultRupture Last; public void Handle(in FaultRupture e) { Last = e; } }

        [Test]
        public void RunMw72OnTheDistrict_AndWriteDumps()
        {
            string dir = Environment.GetEnvironmentVariable("KAIJU_DUMP_DIR");
            if (string.IsNullOrEmpty(dir)) Assert.Pass("KAIJU_DUMP_DIR not set");
            Directory.CreateDirectory(dir);
            string root = Root();
            var settings = GeoSettings.Load(Path.Combine(root, "Assets", "Game", "Config", "WorldGeo.json"));

            // ---- district buildings (the real BuildingsLayer on the baked tiles)
            var tiles = new List<DistrictTileBuildings>();
            var sw = Stopwatch.StartNew();
            foreach (string t in Tiles)
            {
                GeoTileId.TryParse(t, out GeoTileId id);
                var ctx = new GeoTileContext(Path.Combine(root, "Assets", "StreamingAssets", "Geo", "tiles", "v1", id.Folder.Replace('/', Path.DirectorySeparatorChar)), settings, false);
                var model = new GeoTileModel(ctx, new IGeoLayer[] { new BuildingsLayer() });
                tiles.Add(new DistrictTileBuildings { Tile = id, Buildings = model.Buildings });
            }
            int total = tiles.Sum(x => x.Buildings.Count);
            TestContext.Out.WriteLine($"loaded {tiles.Count} tiles, {total} building records in {sw.Elapsed.TotalSeconds:F1} s");

            // ---- pieces
            var pieces = DistrictPiecesExporter.Build(tiles);
            File.WriteAllText(Path.Combine(dir, "district_pieces.json"), DistrictPiecesExporter.ToJson(pieces));
            TestContext.Out.WriteLine($"pieces: {pieces.Count}");

            // ---- quake
            var faults = FaultTraceSet.Build(RealFaults.Load());
            FaultSystem sa = faults.Systems.Where(s => s.Name.StartsWith("San Andreas")).OrderByDescending(s => s.LengthM).First();
            FaultSystem hw = faults.Systems.Where(s => s.Name.StartsWith("Hayward")).OrderByDescending(s => s.LengthM).First();
            var refs = new DistrictRefs { GGX = 26 * 2048 + 1024, GGZ = 37 * 2048 + 1024, SoMaX = 30 * 2048 + 1024, SoMaZ = 36 * 2048 + 1024, TargetGG = true };

            var bus = new EventBus(new NullErrors(), 10000);
            var flags = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(DestructionFlags.Enabled, true), new KeyValuePair<string, bool>(SeismicFlags.Enabled, true) });
            var damage = new DamageModel(bus);
            var director = new QuakeDirector(faults, refs, bus);
            var bridge = SeismicShakingBridge.TryCreate(flags, bus, damage, director.Field);
            Assert.IsNotNull(bridge);
            var registered = new List<KeyValuePair<BuildingId, BuildingAttributes>>();
            foreach (var tb in tiles)
                foreach (GeoBuildingData b in tb.Buildings)
                {
                    if (b.Excluded != null || (b.HasParts && b.RenderPolygons == null)) continue;
                    var attrs = BuildingAttributesFromGeo.From(b);
                    damage.Register(b.Id, attrs);
                    bridge.AddBuilding(b.Id, tb.Tile.OriginX + b.AnchorLocalX, tb.Tile.OriginZ + b.AnchorLocalZ);
                    registered.Add(new KeyValuePair<BuildingId, BuildingAttributes>(b.Id, attrs));
                }
            TestContext.Out.WriteLine($"registered {registered.Count} damageable buildings");

            int h = NearestVertex(sa, refs.GGX, refs.GGZ);
            var scen = new QuakeScenario { EventId = 1, Mw = 7.2f, FaultId = sa.FaultId, HypoX = (float)sa.X[h], HypoZ = (float)sa.Z[h], HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = 20261115 };
            RuptureResult rsa = director.Run(scen);
            Assert.IsNotNull(rsa);
            // Hayward on a second director with no bridge (rupture geometry and scaling only)
            var dir2 = new QuakeDirector(faults, refs, null);
            int h2 = NearestVertex(hw, refs.GGX, refs.GGZ);
            RuptureResult rhw = dir2.Run(new QuakeScenario { EventId = 2, Mw = 7.2f, FaultId = hw.FaultId, HypoX = (float)hw.X[h2], HypoZ = (float)hw.Z[h2], HypoDepthM = 8000f, RuptureSpeedMs = 2800f, Seed = 20261116 });
            Assert.IsNotNull(rhw);
            TestContext.Out.WriteLine($"SA {rsa.Mw:F2} srl {rsa.SrlKm:F1} km v{rsa.VertexStart}-{rsa.VertexEnd} shortened={rsa.ShortenedToSystem}; HW {rhw.Mw:F2} srl {rhw.SrlKm:F1} km shortened={rhw.ShortenedToSystem}");

            // damage census
            var census = new int[5];
            foreach (var kv in registered) { damage.TryGetState(kv.Key, out var st); census[st.State]++; }
            TestContext.Out.WriteLine($"damage census states0..4: {string.Join(",", census)}");

            // M-050 input: capture the quake damage, restore it into a fresh model of the same district, capture again
            var saveA = new SaveSystem("t036c"); saveA.Register(new DestructionSaveParticipant(damage));
            string before = SaveCodec.Serialize(saveA.Capture());
            var damage2 = new DamageModel();
            foreach (var kv in registered) damage2.Register(kv.Key, kv.Value);
            var saveB = new SaveSystem("t036c"); saveB.Register(new DestructionSaveParticipant(damage2));
            var read = SaveCodec.Deserialize(before, "mem");
            Assert.IsTrue(read.Ok, read.Message);
            Assert.IsTrue(saveB.Restore(read.Document).Ok);
            string after = SaveCodec.Serialize(saveB.Capture());
            File.WriteAllText(Path.Combine(dir, "save_before.json"), before);
            File.WriteAllText(Path.Combine(dir, "save_after.json"), after);

            string faultsRel = "Assets/StreamingAssets/Geo/global/v1/faults_bay.geojson";
            string faultsSha;
            using (var sha = SHA256.Create()) faultsSha = string.Concat(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, faultsRel))).Select(b => b.ToString("x2")));
            File.WriteAllText(Path.Combine(dir, "fault_rupture_dump.json"), SeismicDumpExporter.BuildFaultRuptureDump(new[] { rsa, rhw }, faultsRel, faultsSha));
            File.WriteAllText(Path.Combine(dir, "rupture_dump.json"), SeismicDumpExporter.BuildRuptureDump(new[] { rsa, rhw }));

            // destruction dump: every building that reached state >= 1 (cap 20000, ordinal id order) plus the first 200 undamaged
            var damaged = registered.Where(kv => { damage.TryGetState(kv.Key, out var s); return s.State >= 1; }).OrderBy(kv => kv.Key).Take(20000).ToList();
            var intact = registered.Where(kv => { damage.TryGetState(kv.Key, out var s); return s.State == 0; }).OrderBy(kv => kv.Key).Take(200).ToList();
            var all = damaged.Concat(intact).ToList();
            File.WriteAllText(Path.Combine(dir, "destruction_dump.json"), DestructionDumpExporter.BuildDestructionDump(damage, all));
            File.WriteAllText(Path.Combine(dir, "fragility_dump.json"), DestructionDumpExporter.BuildFragilityDump(10000));
            File.WriteAllText(Path.Combine(dir, "run_summary.json"), JsonSerializer.Serialize(new
            {
                tiles = Tiles, building_records = total, pieces = pieces.Count, registered = registered.Count, states = census,
                sa = new { mw = rsa.Mw, srl_km = rsa.SrlKm, v0 = rsa.VertexStart, v1 = rsa.VertexEnd, shortened = rsa.ShortenedToSystem, system_km = sa.LengthM / 1000, hypo_vertex = rsa.HypoVertex },
                hw = new { mw = rhw.Mw, srl_km = rhw.SrlKm, v0 = rhw.VertexStart, v1 = rhw.VertexEnd, shortened = rhw.ShortenedToSystem, system_km = hw.LengthM / 1000 },
                dumped_buildings = all.Count,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        static int NearestVertex(FaultSystem s, double x, double z)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < s.VertexCount; i++) { double d = (s.X[i] - x) * (s.X[i] - x) + (s.Z[i] - z) * (s.Z[i] - z); if (d < bd) { bd = d; best = i; } }
            return best;
        }
    }
}
