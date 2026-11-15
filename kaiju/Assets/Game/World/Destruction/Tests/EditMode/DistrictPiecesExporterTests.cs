// T-036c: district_pieces@v1 writer core: clipped areas are exact on shapes with known answers, pieces of a split building add up
// to its footprint, holes are subtracted, output is deterministic and carries the schema fields.
using System;
using System.Collections.Generic;
using Game.World.Geo;
using NUnit.Framework;

namespace Game.World.Destruction.Tests
{
    public class DistrictPiecesExporterTests
    {
        static List<List<double[]>> Rect(double x0, double z0, double x1, double z1, params double[][] holes)
        {
            var poly = new List<double[]> { new[] { x0, z0, x1, z0, x1, z1, x0, z1 } };
            foreach (var h in holes) poly.Add(h);
            return new List<List<double[]>> { poly };
        }

        static GeoBuildingData B(string bid, List<List<double[]>> polys, double h = 10, double b = 3) =>
            new GeoBuildingData { Bid = bid, Polygons = polys, HeightM = h, BaseNavd88M = b };

        static readonly GeoTileId T00 = GeoTileId.From(0, 5, 5), T10 = GeoTileId.From(0, 6, 5);

        [Test]
        public void ClippedArea_RectangleCrossingTheEdge_SplitsExactly()
        {
            // tile-local x 2000..2100 in tile (5,5): global x = 5*2048 + 2000 .. 2100; the edge to tile 6 is at local x 2048
            var p = Rect(2000, 100, 2100, 140);
            double ox = T00.OriginX, oz = T00.OriginZ;
            double west = DistrictPiecesExporter.ClippedAreaM2(p, ox, oz, T00.OriginX, T00.OriginZ, T00.OriginX + 2048, T00.OriginZ + 2048);
            double east = DistrictPiecesExporter.ClippedAreaM2(p, ox, oz, T10.OriginX, T10.OriginZ, T10.OriginX + 2048, T10.OriginZ + 2048);
            Assert.AreEqual(48 * 40, west, 1e-9);
            Assert.AreEqual(52 * 40, east, 1e-9);
        }

        [Test]
        public void ClippedArea_ConcaveRing_AndHole()
        {
            // L shape of area 3 (unit squares) in a 2 x 2 box, clipped to x <= 1 -> area 2
            var l = new List<List<double[]>> { new List<double[]> { new double[] { 0, 0, 2, 0, 2, 1, 1, 1, 1, 2, 0, 2 } } };
            Assert.AreEqual(2.0, DistrictPiecesExporter.ClippedAreaM2(l, 0, 0, -10, -10, 1, 10), 1e-12);
            Assert.AreEqual(3.0, DistrictPiecesExporter.ClippedAreaM2(l, 0, 0, -10, -10, 10, 10), 1e-12);
            // 10 x 10 square with a 2 x 2 hole fully inside the clip window
            var h = Rect(0, 0, 10, 10, new double[] { 4, 4, 4, 6, 6, 6, 6, 4 });
            Assert.AreEqual(96.0, DistrictPiecesExporter.ClippedAreaM2(h, 0, 0, -1, -1, 11, 11), 1e-12);
            // window cuts the hole in half: clipped square 5 x 10 = 50 minus hole half 2 = 48
            Assert.AreEqual(48.0, DistrictPiecesExporter.ClippedAreaM2(h, 0, 0, 0, 0, 5, 10), 1e-12);
            // window outside the footprint
            Assert.AreEqual(0.0, DistrictPiecesExporter.ClippedAreaM2(h, 0, 0, 20, 20, 30, 30), 0.0);
        }

        [Test]
        public void Build_PiecesOfASplitBuildingSumToTheFootprint_AndAreDeterministic()
        {
            var cross = B("osm:w1", Rect(2000, 100, 2100, 140), 12.5, 4.25);
            var inside = B("osm:w2", Rect(100, 100, 120, 130), 7, 1);
            var tiles = new List<DistrictTileBuildings>
            {
                new DistrictTileBuildings { Tile = T00, Buildings = new[] { cross, inside } },
                new DistrictTileBuildings { Tile = T10, Buildings = new GeoBuildingData[0] },
            };
            var a = DistrictPiecesExporter.Build(tiles);
            Assert.AreEqual(3, a.Count);                                      // two pieces of w1, one of w2
            double sum = 0; int n1 = 0;
            foreach (var p in a) if (p.Bid == "osm:w1") { sum += p.AreaM2; n1++; Assert.AreEqual(12.5, p.HeightM); Assert.AreEqual(4.25, p.BaseNavd88M); }
            Assert.AreEqual(2, n1); Assert.AreEqual(100 * 40, sum, 1e-9);
            string j = DistrictPiecesExporter.ToJson(a);
            Assert.AreEqual(j, DistrictPiecesExporter.ToJson(DistrictPiecesExporter.Build(tiles)));
            StringAssert.StartsWith("{\"format\":\"district_pieces\",\"schema\":1,\"pieces\":[", j);
            StringAssert.Contains("\"tile\":\"L0_05_05\"", j); StringAssert.Contains("\"tile\":\"L0_06_05\"", j);
            StringAssert.Contains("\"base_navd88_m\":4.25", j);
        }

        [Test]
        public void Build_BuildingOutsideEverySquare_GivesNoPiece_AndRejectsEmpty()
        {
            var far = B("osm:w3", Rect(3000, 100, 3100, 140));              // local x beyond the owner's 2048 and beyond tile 6's? 5*2048+3000 -> inside tile 6 square
            var tiles = new List<DistrictTileBuildings> { new DistrictTileBuildings { Tile = T00, Buildings = new[] { far } } };
            Assert.AreEqual(0, DistrictPiecesExporter.Build(tiles).Count);   // tile 6 is not in the district list
            Assert.Throws<ArgumentException>(() => DistrictPiecesExporter.Build(new List<DistrictTileBuildings>()));
            Assert.Throws<ArgumentException>(() => DistrictPiecesExporter.ToJson(new List<DistrictPiece>()));
        }
    }
}
