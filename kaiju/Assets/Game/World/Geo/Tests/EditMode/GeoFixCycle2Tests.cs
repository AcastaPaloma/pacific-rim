// Fix cycle 2 (T-003): config is the single source of truth; non-surface features and part heights (engine-free).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoFixCycle2Tests
    {
        static string ConfigPath => Path.Combine(GeoTestData.ProjectRoot(), "Assets", "Game", "Config", "WorldGeo.json");

        [Test]
        public void Config_HasEveryRequiredKey()
        {
            var d = GeoSettings.Load(ConfigPath);
            foreach (var k in GeoSettings.RequiredKeys) Assert.IsTrue(d.ContainsKey(k), k);
            Assert.AreEqual(0.1, d[GeoSettingKeys.TerrainMaxErrorM], 1e-12);
        }

        [Test]
        public void Config_MissingKey_FailsLoudly()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "worldgeo_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(tmp, File.ReadAllText(ConfigPath).Replace("\"terrainMaxErrorM\"", "\"terrainMaxErrorM_renamed\""));
            Assert.Throws<InvalidDataException>(() => GeoSettings.Load(tmp));
            Assert.Throws<FileNotFoundException>(() => GeoSettings.Load(tmp + ".absent"));
        }

        [Test]
        public void Importer_WithoutSettings_RefusesInsteadOfUsingCodeDefaults()
        {
            var ctx = new GeoTileContext(GeoTestData.M0Dir, new Dictionary<string, double>(), verifyHashes: false);
            Assert.Throws<InvalidDataException>(() => new TerrainLayer().Build(ctx));
        }

        [Test]
        public void UndergroundStationsAndShip_AreRecordsWithoutGeometry()
        {
            var m = GeoTestData.M0();
            foreach (var bid in new[] { "osm:w550652205", "osm:w184956226", "osm:w773030142", "osm:w281243626" })
            {
                var b = m.ById[BuildingsLayer.ParseBid(bid)];
                Assert.IsNotNull(b.Excluded, bid);
                Assert.IsNull(b.Mesh, bid + " must not be extruded");
            }
            var station = BuildingsLayer.ParseBid("osm:w550652205");
            if (m.PartsOf.TryGetValue(station, out var parts))
                Assert.IsTrue(parts.All(p => p.Mesh == null && p.Excluded != null));
            Assert.AreEqual(m.Buildings.Count(b => b.Excluded != null), (int)m.Stats["buildings.excluded_not_extruded"]);
        }

        [Test]
        public void FallbackPartHeights_NeverExceedTheirStructure()
        {
            var m = GeoTestData.M0();
            int over = 0;
            foreach (var b in m.Buildings)
            {
                if (!b.PartOf.IsValid || b.HeightSource == "osm_height") continue;
                var parent = m.ById[b.PartOf];
                if ((parent.HeightSource == "osm_height" || parent.HeightSource == "datasf") && b.HeightM > parent.HeightM + 1e-6) over++;
            }
            Assert.AreEqual(0, over);
            Assert.Greater(m.Buildings.Count(b => b.HeightCappedToParent), 0);
        }

        [Test]
        public void NoBuildingFloats_WallsReachBelowTheLowestGround()
        {
            foreach (var b in GeoTestData.M0().Buildings)
            {
                if (b.Mesh == null || b.MinHeightM > 0) continue;
                float minY = float.MaxValue;
                for (int i = 1; i < b.Mesh.Positions.Length; i += 3) minY = Math.Min(minY, b.Mesh.Positions[i]);
                Assert.LessOrEqual(minY, b.GroundMinNavd88M + 1e-3, b.Bid);
            }
        }
    }
}
