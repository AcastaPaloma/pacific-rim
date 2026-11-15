// World.Seismic contracts (layer L2): engine-free event structs and the scenario input for the quake -> fault rupture -> emergence
// sequence (F-020, D-014, Q-030). Numbers and field lists: research/R-022.md section 4. All events are unmanaged (IDs, no managed refs);
// the fault polyline stays in the World.Geo faults layer and is referenced by FaultId + vertex range.
using Game.Core;

namespace Game.World.Seismic
{
    public static class SeismicContractVersions
    {
        /// <summary>Event shapes (QuakeBegan, FaultRupture, EmergencePoint) and QuakeScenario.</summary>
        public const int EventApi = 1;
        /// <summary>Version of the scaling coefficients and timing constants (R-022 sections 2 and 4).</summary>
        public const int ModelRevision = 1;
    }

    /// <summary>Fault slip sense, from the baked `slip_sense` field. Append only; the byte is published.</summary>
    public enum SlipSense : byte
    {
        Unknown = 0,
        RightLateral = 1,
        LeftLateral = 2,
        Normal = 3,
        Reverse = 4,
    }

    /// <summary>Input of one quake. Everything the sequence needs; the fault is chosen by baked `fault_id` (no fictional traces, Q-030).</summary>
    public struct QuakeScenario
    {
        public uint EventId;
        /// <summary>Moment magnitude (M1 default 7.2).</summary>
        public float Mw;
        /// <summary>Id of the dissolved trace (stable hash of baked `fault_id`/`fault_name`+`section`; the faults layer owns the mapping).</summary>
        public uint FaultId;
        public ushort SectionIdx;
        /// <summary><see cref="SlipSense"/>.</summary>
        public byte SlipSense;
        /// <summary>Hypocentre in BayTM metres (float; floating-origin shifts are not applied here).</summary>
        public float HypoX, HypoZ;
        /// <summary>Hypocentre depth, m (Assumed 8000, R-022 section 4).</summary>
        public float HypoDepthM;
        /// <summary>Rupture speed, m/s (Assumed 2800).</summary>
        public float RuptureSpeedMs;
        /// <summary>Surface rupture length override in km; 0 = from Mw by the WC94 relation (set for the HayWired Hayward scenario).</summary>
        public float SrlKmOverride;
        /// <summary>Seeds the tapered slip profile and any shaking draw.</summary>
        public uint Seed;
    }

    /// <summary>t = 0 of the sequence; the shaking field is defined from Mw and the planned rupture. Nothing is damaged until the S wave arrives.</summary>
    public struct QuakeBegan : IEvent
    {
        public uint EventId;
        public float Mw;
        /// <summary>Simulation time of the origin, s.</summary>
        public double OriginTimeS;
        public float HypoX, HypoZ, HypoDepthM;
        public uint FaultId;
        public float RuptureSpeedMs;
        public uint Seed;
    }

    /// <summary>Slip starts at the hypocentre vertex and propagates along the dissolved trace. Published after <see cref="QuakeBegan"/>.</summary>
    public struct FaultRupture : IEvent
    {
        public uint EventId;
        public uint FaultId;
        public ushort SectionIdx;
        /// <summary>Ruptured vertex range on the dissolved trace (faults layer).</summary>
        public int VertexStart, VertexEnd;
        /// <summary>Surface rupture length, km.</summary>
        public float SrlKm;
        /// <summary>Maximum and average surface displacement, m. The slip profile is regenerated from these and the seed.</summary>
        public float MdM, AdM;
        /// <summary><see cref="Seismic.SlipSense"/>.</summary>
        public byte SlipSense;
        /// <summary>Rupture start and end, simulation s from QuakeBegan.</summary>
        public float StartS, EndS;
    }

    /// <summary>Where and when the kaiju rises. Fires at most once per EventId.</summary>
    public struct EmergencePoint : IEvent
    {
        public uint EventId;
        public double X, Z;
        public float HeightM;
        public uint FaultId;
        public int VertexIdx;
        public float TimeS;
        public float DistToGGm, DistToSoMaM;
    }
}
