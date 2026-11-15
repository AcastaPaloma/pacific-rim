// T-113/T-114 follow-ups: composition of the pack layer and the emergence sequence, plus the Units.Kaiju -> Pack species adapter. Engine-free.
using System;
using System.Collections.Generic;
using Game.AI;
using Game.AI.Pack;
using Game.Core;
using Game.Units.Kaiju.Contracts;
using Game.World.Emergence;
using Game.World.Seismic;

namespace Game.Composition
{
    /// <summary>
    /// Builds the pack layer's <see cref="SpeciesRole"/> from a Units.Kaiju <see cref="KaijuSpeciesSpec"/> (R-103 Q-NEW-a: the spec replaces the
    /// R-103 0.2 fallback numbers). Mapping: SpeciesId = mass class + 1 (Strider 1, Breacher 2, Bastion 3, the numbering of
    /// <c>EmergenceSpecies</c>); cruise and charge speeds = gait cruise/max speed; reach = longest attack reach; standoff = 7 x reach (R-103 0.2);
    /// role fits = the spec's `role_affinity_*`; deep water = floats or swims. HpRel has no spec field (mass would overstate Bastion 16 x), so it
    /// keeps the R-103 0.2 weight of the mass class (0.5 / 1.0 / 2.5, Assumed).
    /// </summary>
    public static class KaijuSpeciesRoles
    {
        public const float StandoffReachFactor = 7f;
        private static readonly float[] HpRelByClass = { 0.5f, 1.0f, 2.5f };

        public static SpeciesRole From(KaijuSpeciesSpec s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            float reach = 0f;
            foreach (var a in s.Attacks) if (a.ReachM != null && a.ReachM.Value > reach) reach = (float)a.ReachM.Value;
            if (reach <= 0f) reach = 0.5f * (float)s.Geometry.HeightM.Value;   // no attack data: R-103 0.2 rule 0.5 H
            int cls = (int)s.MassClass;
            bool deep = (s.Water != null && s.Water.Floats != null && s.Water.Floats.Value > 0.5) || (s.Stance != null && s.Stance.SwimSpeedMaxMs != null && s.Stance.SwimSpeedMaxMs.Value > 0.0);
            return new SpeciesRole(cls + 1, (float)s.Geometry.HeightM.Value, (float)s.Gait.CruiseSpeedMs.Value, (float)s.Gait.MaxSpeedMs.Value,
                reach, StandoffReachFactor * reach, HpRelByClass[Math.Max(0, Math.Min(2, cls))],
                (float)s.Pack.Anchor.Value, (float)s.Pack.Flanker.Value, (float)s.Pack.Bait.Value, (float)s.Pack.Raider.Value, deep);
        }

        /// <summary>SpeciesRole by SpeciesId (1..3). Species missing from the catalog fall back to the R-103 defaults.</summary>
        public static SpeciesRole[] Table(IKaijuSpeciesCatalog catalog)
        {
            var t = new[] { default(SpeciesRole), SpeciesRole.Strider, SpeciesRole.Breacher, SpeciesRole.Bastion };
            if (catalog == null) return t;
            foreach (var spec in catalog.All)
            {
                var r = From(spec);
                if (r.SpeciesId >= 1 && r.SpeciesId <= 3) t[r.SpeciesId] = r;
            }
            return t;
        }
    }

    /// <summary>Result of <see cref="PackInstaller.Install"/>. Null members: the flag is off or a prerequisite is missing (reported).</summary>
    public sealed class PackHandle
    {
        public PackBrainFactory Factory { get; internal set; }
        public PackDirectory Directory { get; internal set; }
        /// <summary>SpeciesRole per SpeciesId (index 0 unused), from the Units.Kaiju catalog when registered, else the R-103 defaults.</summary>
        public SpeciesRole[] Species { get; internal set; }
    }

    public static class PackInstaller
    {
        /// <summary>Needs the scripted brain factory as the inner brain (kaiju.scriptedBrain on). Null when ai.pack is off (nothing registered).</summary>
        public static PackHandle Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors)
        {
            if (registry == null || flags == null || !flags.IsOn(PackFlags.Pack)) return null;
            if (!registry.TryResolve(out IKaijuBrainFactory inner))
            {
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Pack] ai.pack is on but no IKaijuBrainFactory is registered (turn kaiju.scriptedBrain on); pack brains not installed."));
                return null;
            }
            registry.TryResolve(out IKaijuSpeciesCatalog catalog);
            var h = new PackHandle { Species = KaijuSpeciesRoles.Table(catalog) };
            h.Factory = PackModule.Install(registry, flags, inner);
            if (h.Factory == null) return null;
            registry.TryResolve(out PackDirectory dir);
            h.Directory = dir;
            return h;
        }
    }

    /// <summary>Adapts World.Seismic's dissolved fault systems to <see cref="IEmergenceTraceSource"/> (Emergence never references the Seismic runtime).</summary>
    public sealed class SeismicTraceSource : IEmergenceTraceSource
    {
        private readonly FaultTraceSet _faults;
        private readonly Dictionary<uint, EmergenceTrace> _cache = new Dictionary<uint, EmergenceTrace>();
        public SeismicTraceSource(FaultTraceSet faults) { _faults = faults ?? throw new ArgumentNullException(nameof(faults)); }

        public bool TryGet(uint faultId, out EmergenceTrace trace)
        {
            if (_cache.TryGetValue(faultId, out trace)) return true;
            FaultSystem s = _faults.FindSystem(faultId);
            if (s == null) return false;
            trace = new EmergenceTrace(s.FaultId, s.X, s.Z, s.ArcM, s.Bridge);
            _cache[faultId] = trace;
            return true;
        }
    }

    public static class EmergenceInstaller
    {
        /// <summary>Null when emergence.enabled is off, the seismic director is missing, or the bus/allocator are missing (reported when seismic is absent).</summary>
        public static EmergenceDirector Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors, QuakeDirector seismic, IEmergenceTargets targets = null, EmergenceOptions options = null)
        {
            if (registry == null || flags == null || !flags.IsOn(EmergenceFlags.Enabled)) return null;
            if (seismic == null)
            {
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Emergence] emergence.enabled is on but World.Seismic is not installed (seismic.enabled); emergence not installed."));
                return null;
            }
            return EmergenceModule.Install(registry, flags, new SeismicTraceSource(seismic.Faults), targets, options);
        }
    }
}
