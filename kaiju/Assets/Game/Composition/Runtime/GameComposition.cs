// The game's composition root (T-039): the only place that knows every module and installs it into the Core registry.
// Each module keeps its own default-off flag; with every flag off nothing is registered and no data is read.
using System;
using Game.AI;
using Game.Hangar;
using Game.Units.Placeholder;
using Game.Core;
using Game.Core.Unity;
using Game.World.Destruction;
using Game.World.Geo.Unity;
using Game.World.Seismic;
using UnityEngine;

namespace Game.Composition
{
    /// <summary>Data the root cannot know itself. Providers run only when the owning flag is on.</summary>
    public sealed class GameCompositionInputs
    {
        /// <summary>Baked fault traces (FaultTraceSet.Build over PiecesFromGeo). Null result with seismic.enabled on = reported, not installed.</summary>
        public Func<FaultTraceSet> Faults;
        public Func<DistrictRefs> Districts;
        /// <summary>Vs30 override for the Destruction/Seismic bridge; null = the Seismic default.</summary>
        public Func<double, double, double> Vs30At;
        /// <summary>Simulation step counter for the bridge; null = the bridge's own default.</summary>
        public Func<long> Step;
        /// <summary>Role, transport and snapshot source for the co-op install (net.coop + net.seatBridge); null with those flags on = reported, not installed.</summary>
        public NetCoopInputs Net;
        /// <summary>Limits for placeholder bodies (units.placeholderBody); null = PlaceholderLimits.Default().</summary>
        public PlaceholderLimits? PlaceholderBodyLimits;
    }

    /// <summary>What an install pass created. A null member means that module's flag is off (or its input was missing).</summary>
    public sealed class GameCompositionResult
    {
        public GeoWorldService Geo;
        public DamageModel Destruction;
        /// <summary>destruction@v1 save section owner; set when Destruction installed AND a SaveSystem was supplied.</summary>
        public DestructionSaveParticipant DestructionSave;
        public QuakeDirector Seismic;
        public SeismicShakingBridge ShakingBridge;
        public IKaijuBrainFactory Brain;
        public NetCoopHandle Net;
        public PlaceholderBodyFactory Placeholder;
        /// <summary>Hangar stub (hangar.stub AND units.placeholderBody on); registered with the SaveSystem when one was supplied.</summary>
        public HangarService Hangar;
        public CombatHandle Combat;
    }

    public static class GameComposition
    {
        /// <summary>Set before Play Mode (a bootstrap or test); null = no data providers, so seismic stays uninstalled even when its flag is on.</summary>
        public static GameCompositionInputs Inputs { get; set; }

        /// <summary>Installs every module whose flag is on. Order: Geo, Destruction, Seismic (needs the bus), the shaking bridge (needs both), the brain, the co-op net session (net.coop + net.seatBridge), the placeholder body factory (units.placeholderBody).</summary>
        public static GameCompositionResult Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors, GameCompositionInputs inputs = null, SaveSystem saves = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            inputs = inputs ?? Inputs;
            var r = new GameCompositionResult();
            r.Geo = GeoModule.Install(registry, flags);
            r.Destruction = DestructionModule.Install(registry, flags);
            r.DestructionSave = DestructionSaveInstaller.Install(r.Destruction, saves);

            if (flags.IsOn(SeismicFlags.Enabled))
            {
                FaultTraceSet faults = inputs?.Faults?.Invoke();
                if (faults == null)
                    errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Composition] seismic.enabled is on but no fault traces were supplied (GameComposition.Inputs.Faults); Seismic not installed."));
                else
                    r.Seismic = SeismicModule.Install(registry, flags, faults, inputs.Districts != null ? inputs.Districts() : default(DistrictRefs), null);
            }

            if (r.Destruction != null && r.Seismic != null)
            {
                IEventBus bus; registry.TryResolve(out bus);
                r.ShakingBridge = SeismicShakingBridge.TryCreate(flags, bus, r.Destruction, r.Seismic.Field, inputs?.Vs30At, inputs?.Step);
                if (r.ShakingBridge != null) registry.Register<SeismicShakingBridge>(r.ShakingBridge);
            }

            r.Brain = ScriptedBrainModule.Install(registry, flags);
            r.Net = NetCoopInstaller.Install(registry, flags, errors, inputs?.Net);
            r.Placeholder = PlaceholderInstaller.Install(registry, flags, inputs?.PlaceholderBodyLimits);
            if (r.Placeholder != null)
            {
                r.Hangar = HangarModule.Install(registry, flags, r.Geo);
                if (r.Hangar != null) saves?.Register(r.Hangar);
            }
            r.Combat = CombatInstaller.Install(registry, flags, errors);
            return r;
        }
    }

    /// <summary>Runs <see cref="GameComposition.Install"/> once per Play session, right after Core boots.</summary>
    internal static class GameCompositionBoot
    {
        /// <summary>The last install result of this session (null before boot).</summary>
        internal static GameCompositionResult Last;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            Last = null;
            CoreRuntime.Booted -= OnBooted;
            CoreRuntime.Booted += OnBooted;
        }

        private static void OnBooted(CoreRoot root)
        {
            Last = GameComposition.Install(root.Registry, root.Services.Flags, root.Errors, null, root.SaveSystem);
        }
    }
}
