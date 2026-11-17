// T-194e: no building render-mesh horizontal face may be coplanar with the terrain under it (depth tie / z-fight source).
using System;
using System.Linq;
using NUnit.Framework;

namespace Game.World.Geo.Tests
{
    public class GeoBuildingTerrainCoplanarTests
    {
        /// <summary>A face counts as coplanar with the terrain when every vertex is within this of the rendered terrain surface (RTIN error is about 0.2 m, depth precision far finer).</summary>
        const double EpsM = 0.05;

        static void Scan(GeoTileModel m, bool bottoms, out int faces, out double minMaxDev)
        {
            faces = 0; minMaxDev = double.MaxValue;
            foreach (var b in m.Buildings)
            {
                if (b.Mesh == null) continue;
                var P = b.Mesh.Positions; var I = b.Mesh.Indices; var N = b.Mesh.Normals;
                for (int k = 0; k < I.Length; k += 3)
                {
                    float ny = N[3 * I[k] + 1];
                    if (Math.Abs(ny) < 0.99f || (ny < 0) != bottoms) continue;
                    faces++;
                    double dmax = 0;
                    for (int j = 0; j < 3; j++)
                    {
                        int v = I[k + j];
                        dmax = Math.Max(dmax, Math.Abs(P[3 * v + 1] - m.Heights.SampleRender(P[3 * v], P[3 * v + 2])));
                    }
                    minMaxDev = Math.Min(minMaxDev, dmax);
                }
            }
        }

        [Test]
        public void FootprintBottomFaces_AreNeverCoplanarWithTerrain()
        {
            Scan(GeoTestData.M0(), true, out int n, out double dev);
            Assert.Greater(n, 1000, "scanned the real tile's bottom faces");
            Assert.Greater(dev, EpsM, "a building bottom face lies within " + EpsM + " m of the terrain surface (depth tie)");
        }

        [Test]
        public void RoofFaces_AreNeverCoplanarWithTerrain()
        {
            Scan(GeoTestData.M0(), false, out int n, out double dev);
            Assert.Greater(n, 1000);
            Assert.Greater(dev, EpsM, "a flat roof face lies within " + EpsM + " m of the terrain surface (depth tie)");
        }

        [Test]
        public void ExtrusionBottom_IsAtLeastTheSkirtBelowGroundMin_UnlessMinHeight()
        {
            double skirt = GeoTestData.Settings()[GeoSettingKeys.BuildingSkirtM];
            var m = GeoTestData.M0();
            int checkedN = 0;
            foreach (var b in m.Buildings.Where(x => x.Mesh != null && x.MinHeightM <= 0))
            {
                float lo = float.MaxValue;
                for (int i = 0; i < b.Mesh.VertexCount; i++) lo = Math.Min(lo, b.Mesh.Positions[3 * i + 1]);
                Assert.LessOrEqual(lo, Math.Min(b.BaseNavd88M, b.GroundMinNavd88M) - skirt + 1e-3, b.Bid);
                checkedN++;
            }
            Assert.Greater(checkedN, 1000);
        }
    }
}
