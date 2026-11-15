using System.Collections.Generic;

namespace Game.Units.Kaiju.Contracts
{
    public static class KaijuContractVersions
    {
        public const string SpeciesSchema = "KaijuSpeciesSpec@v1";
        public const string TissueSchema = "kaiju_tissue@v1";
    }

    public static class KaijuFlags
    {
        public const string Species = "units.kaiju.species";
    }

    /// <summary>Provenance of a number (R-102 section 10): cited, derived from other fields, or an Assumed design value.</summary>
    public enum SpecStatus : byte { Sourced = 0, Derived = 1, Assumed = 2 }

    public enum MassClass : byte { Light = 0, Mid = 1, Heavy = 2 }

    public enum AttackKind : byte { Bite, Rake, Swipe, TailSweep, Stomp, Slam, Ram, Grapple, Rush }

    /// <summary>One numeric field: value in SI base units plus unit label, source (citation or derivation) and status.</summary>
    public sealed class SpecNumber
    {
        public double Value; public string Unit; public string Source; public SpecStatus Status;
        public static implicit operator double(SpecNumber n) => n.Value;
    }

    public sealed class Ellipsoid { public SpecNumber A, B, C; }

    /// <summary>Truncated cone: length L, radius R0 at the root/top, R1 at the tip/bottom.</summary>
    public sealed class Frustum { public SpecNumber L, R0, R1; }

    public sealed class SpecGeometry
    {
        public SpecNumber HeightM, HipHeightM, NLimbs, ExtraVolumeM3, TrackWidthM, FootAreaM2, FootRadiusM, ComHeightM;
        public Ellipsoid Trunk, Head;
        public Frustum Neck, Tail, LimbUpper, LimbLower;
    }

    public sealed class SpecMass
    {
        public SpecNumber RhoEff, VolumeM3, MassKg, InertiaYaw;
        public Ellipsoid ElephantTrunk, ElephantHead;
        public Frustum ElephantNeck, ElephantTail, ElephantLimbUpper, ElephantLimbLower;
        public SpecNumber ElephantExtraM3, ElephantVolumeM3, ElephantMassMinKg, ElephantMassMaxKg;
    }

    public sealed class SpecMaterial { public string TissueRef; public SpecNumber BoneYieldPa, MuscleIsometricPa, MuscleUtilisationMax, BoneSfWalkMin, BoneSfChargeMin; }

    public sealed class SpecLimbModel { public SpecNumber PhiStatic, PhiPeak, KA, KB, EFrac, CortexQ, Ema, BoneAreaShare, MuscleAreaShare, LimbAreaM2; }

    public sealed class SpecDamage { public SpecNumber PhiOneLimbLost, PhiTwoLimbsLost, FrCrawl; }

    public sealed class SpecGait
    {
        public SpecNumber FrCruise, FrBandMin, FrBandMax, FrChargeCap, DutyMin, DutyMax, DutyCruise, StrideK, StrideExp;
        public SpecNumber StartStridesMin, StartStridesMax, StopStridesMin, StopStridesMax;
        public SpecNumber YawRateMaxDps, YawAccelMaxDps2, LateralGMax, PowerWPerKg, ResistC, AccelCapG;
        public SpecNumber CruiseSpeedMs, MaxSpeedMs, StridePeriodCruiseS, StridePeriodChargeS, AccelPowerCruiseMs2;
    }

    public sealed class SpecStance { public SpecNumber RearModeAllowed, RearTimeMaxS, RearPhi, RearFr, SwimSpeedMaxMs; }

    public sealed class SpecAttack
    {
        public string Id; public AttackKind Kind;
        public SpecNumber MEffKg, VTipMs, MuscleMassKg, WorkJPerKg, ElasticAmp, DropHeightM, TargetMassKg, Restitution, HoldForceN, MuscleAreaM2;
        public SpecNumber ECapJ, ImpulseNs, CooldownS, ReachM;
    }

    public sealed class SpecWater { public SpecNumber RhoW, Floats, DraftFraction, BellyClearanceM; }

    public sealed class SpecSoil { public SpecNumber StaticPressurePa, PeakPressurePa, SoilUltimatePa, Sinks; }

    public sealed class SpecPack { public SpecNumber Anchor, Flanker, Bait, Raider, RangeMinM, RangeMaxM, MaxPackSizeHint; }

    public sealed class SpecRenderHints { public string SilhouetteId, PaletteId; public string[] PxFeatures; }

    public sealed class SpecProvenance { public string[] Citations, AssumedFields; public string DerivationVersion; }

    /// <summary>KaijuSpeciesSpec@v1 (R-102 section 10). Pure data: no engine types, no shape data (art owns shapes).</summary>
    public sealed class KaijuSpeciesSpec
    {
        public string Id, DesignCodename; public MassClass MassClass;
        public SpecGeometry Geometry; public SpecMass Mass; public SpecMaterial Material; public SpecLimbModel LimbModel; public SpecDamage Damage;
        public SpecGait Gait; public SpecStance Stance; public List<SpecAttack> Attacks = new List<SpecAttack>();
        public SpecWater Water; public SpecSoil Soil; public SpecPack Pack; public SpecRenderHints RenderHints; public SpecProvenance Provenance;
    }

    /// <summary>kaiju_tissue@v1: the shared Assumed numbers (changed in one place) and the real values they are compared with.</summary>
    public sealed class KaijuTissue
    {
        public SpecNumber BoneYieldPa, MuscleIsometricPa, RealBoneYieldPa, RealMuscleIsometricPa; public string[] AssumedFields;
    }

    public interface IKaijuSpeciesCatalog
    {
        IReadOnlyList<KaijuSpeciesSpec> All { get; }
        KaijuTissue Tissue { get; }
        bool TryGet(string id, out KaijuSpeciesSpec spec);
    }
}
