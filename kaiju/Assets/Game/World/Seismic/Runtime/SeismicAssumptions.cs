// Named constants of World.Seismic that R-022 marks Assumed (no source retrieved) or open. Changing one changes
// SeismicContractVersions.ModelRevision. Vs30 is open question Q-NEW-c (no USGS Vs30 grid yet).
namespace Game.World.Seismic
{
    public static class SeismicAssumptions
    {
        /// <summary>Assumed: rock site, also the BSSA14 reference velocity (R-022 section 3, Q-NEW-c). m/s.</summary>
        public const float Vs30RockMs = 760f;
        /// <summary>Assumed: soft fill / SoMa waterfront (R-022 section 3, Q-NEW-c). m/s.</summary>
        public const float Vs30FillMs = 270f;

        /// <summary>M1 default scenario magnitude (R-022 section 2, chosen; inside the WC94 range 5.6-8.1).</summary>
        public const float DefaultMw = 7.2f;
        /// <summary>Assumed: about 0.8 x shear speed (R-022 section 4). m/s.</summary>
        public const float RuptureSpeedMs = 2800f;
        /// <summary>Assumed standard values (R-022 section 4). m/s.</summary>
        public const float PWaveSpeedMs = 6000f;
        public const float SWaveSpeedMs = 3500f;
        /// <summary>Assumed hypocentre depth, m.</summary>
        public const float HypoDepthM = 8000f;
        /// <summary>Assumed: shaking ends this long after the rupture ends, s.</summary>
        public const float ShakingAfterRuptureS = 10f;
        /// <summary>Assumed: EmergencePoint fires this long after shaking ends, s.</summary>
        public const float EmergenceAfterShakingS = 5f;
    }
}
