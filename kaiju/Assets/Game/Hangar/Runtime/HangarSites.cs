using System.Collections.Generic;
using Game.World.Geo;

namespace Game.Hangar
{
    /// <summary>Sites chosen by R-025 (research/R-025.md). Positions are *Assumed* until confirmed against baked footprints (Q-NEW-b).</summary>
    public static class HangarSites
    {
        /// <summary>
        /// Crissy Field airfield, Presidio (former Army airfield, 1921; restored 28 acre open airfield, NPS). Open-air pad on the airfield
        /// apron, inside Golden Gate tile L0_27_36. Position: 37.8040 N, 122.4600 W (Assumed airfield centre, +-300 m) transformed
        /// to BayTM@v1 with Pipelines/geo common.lonlat_to_baytm = (56821.48, 75637.64). Heading 90 deg: along the airfield axis (east).
        /// </summary>
        public static readonly HangarSite CrissyField = new HangarSite(
            "crissy_field", "Crissy Field airfield, Presidio", GeoTileId.From(0, 27, 36),
            56821.48, 75637.64, 90.0,
            "research/R-025.md section 2 candidate 1; NPS Crissy Airfield; Presidio NHL nomination");

        /// <summary>Unique, ordinal id order.</summary>
        public static IReadOnlyList<HangarSite> All { get; } = new[] { CrissyField };

        public static bool TryGet(string id, out HangarSite site)
        {
            foreach (var s in All) if (s.Id == id) { site = s; return true; }
            site = null; return false;
        }
    }
}
