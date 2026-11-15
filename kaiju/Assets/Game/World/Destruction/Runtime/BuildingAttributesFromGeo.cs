// Pure adapter: Geo building data -> the attributes the capacity model reads (R-021 §0). No engine, no state.
using Game.World.Geo;

namespace Game.World.Destruction
{
    public static class BuildingAttributesFromGeo
    {
        /// <summary>
        /// Height = GeoBuildingData.HeightM. Footprint = outer ring minus hole rings of every polygon in
        /// <c>Polygons</c> (the full outline, not RenderPolygons), else <c>AreaM2</c>, else 0.
        /// GeoBuildingData carries no levels and no use, so Levels = 0 (the model then uses round(H / 4.22 m), min 1)
        /// and Residential = false (documented defaults) until geo_export carries building:levels and use.
        /// </summary>
        public static BuildingAttributes From(GeoBuildingData b)
        {
            if (b == null) throw new System.ArgumentNullException(nameof(b));
            double area = PolygonsAreaM2(b);
            if (!(area > 0)) area = b.AreaM2 > 0 ? b.AreaM2 : 0;
            return new BuildingAttributes
            {
                HeightM = b.HeightM > 0 ? b.HeightM : 0,
                Levels = 0,
                FootprintM2 = area,
                Residential = false,
            };
        }

        /// <summary>Shoelace area of rings x0,z0,x1,z1,...: ring 0 is the outer ring, later rings are holes.</summary>
        public static double PolygonsAreaM2(GeoBuildingData b)
        {
            double total = 0;
            if (b.Polygons == null) return 0;
            foreach (var poly in b.Polygons)
            {
                if (poly == null) continue;
                for (int r = 0; r < poly.Count; r++)
                {
                    double a = RingArea(poly[r]);
                    total += r == 0 ? a : -a;
                }
            }
            return total > 0 ? total : 0;
        }

        static double RingArea(double[] ring)
        {
            if (ring == null || ring.Length < 6) return 0;
            int n = ring.Length / 2;
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                s += ring[2 * i] * ring[2 * j + 1] - ring[2 * j] * ring[2 * i + 1];
            }
            return System.Math.Abs(s) * 0.5;
        }
    }
}
