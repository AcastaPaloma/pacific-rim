// Bus-driven director and composition hook of World.Emergence (MODULARITY rule 5): on every FaultRupture it plans the Bay-wide sequence
// and publishes EmergenceSiteSelected / KaijuEmerged / EmergenceSequenceDone. Registers nothing when emergence.enabled is off.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Seismic;

namespace Game.World.Emergence
{
    public sealed class EmergenceDirector : IEventHandler<QuakeBegan>, IEventHandler<FaultRupture>
    {
        readonly IEventBus _bus;
        readonly IEmergenceTraceSource _traces;
        readonly IEmergenceTargets _targets;
        readonly Func<EntityId> _allocate;
        readonly EmergenceOptions _options;
        readonly Dictionary<uint, QuakeBegan> _began = new Dictionary<uint, QuakeBegan>();
        readonly HashSet<uint> _done = new HashSet<uint>();

        /// <summary>The last plan, for tests and the m007_bay harness (events carry the same numbers).</summary>
        public EmergencePlan LastPlan { get; private set; }
        public int SequencesRun { get; private set; }

        public EmergenceDirector(IEventBus bus, IEmergenceTraceSource traces, IEmergenceTargets targets, Func<EntityId> allocateEntityId, EmergenceOptions options = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _traces = traces ?? throw new ArgumentNullException(nameof(traces));
            _allocate = allocateEntityId ?? throw new ArgumentNullException(nameof(allocateEntityId));
            _targets = targets;
            _options = options ?? new EmergenceOptions();
        }

        public void Handle(in QuakeBegan e) { _began[e.EventId] = e; }

        public void Handle(in FaultRupture e)
        {
            if (_done.Contains(e.EventId)) return;      // at most once per EventId
            _done.Add(e.EventId);
            _began.TryGetValue(e.EventId, out QuakeBegan q);   // missing QuakeBegan: Mw 0, Seed 0 (plan still deterministic)
            if (!_traces.TryGet(e.FaultId, out EmergenceTrace trace)) return;
            var req = new EmergenceRequest
            {
                EventId = e.EventId, Mw = q.Mw, Seed = q.Seed,
                VertexStart = e.VertexStart, VertexEnd = e.VertexEnd, RuptureEndS = e.EndS,
            };
            EmergencePlan plan = EmergencePlanner.Plan(req, trace, _targets, _options);
            LastPlan = plan;
            if (plan == null) return;
            SequencesRun++;
            float first = float.MaxValue, last = 0f;
            int n = 0;
            foreach (PlannedSite s in plan.Sites)
            {
                _bus.Publish(new EmergenceSiteSelected
                {
                    EventId = plan.EventId, SiteIdx = s.SiteIdx, X = s.X, Z = s.Z, FaultId = plan.FaultId, VertexIdx = s.VertexIdx,
                    TimeS = s.TimeS, KaijuCount = (byte)s.Kaiju.Length, DistToTargetM = s.DistToTargetM,
                });
                foreach (PlannedKaiju k in s.Kaiju)
                {
                    _bus.Publish(new KaijuEmerged
                    {
                        EventId = plan.EventId, SiteIdx = s.SiteIdx, Ordinal = (byte)k.Ordinal, SpeciesId = k.SpeciesId, KaijuId = _allocate(),
                        X = k.X, Z = k.Z, TimeS = k.TimeS, HeadingRad = k.HeadingRad,
                    });
                    n++;
                    if (k.TimeS < first) first = k.TimeS;
                    if (k.TimeS > last) last = k.TimeS;
                }
            }
            _bus.Publish(new EmergenceSequenceDone
            {
                EventId = plan.EventId, Sites = plan.Sites.Length, Kaiju = n, SitesRequested = plan.SitesRequested,
                FirstTimeS = n > 0 ? first : 0f, LastTimeS = last,
            });
        }
    }

    public static class EmergenceModule
    {
        /// <summary>
        /// Returns the director it subscribed, or null when the flag is off or a required service is missing (nothing registered, bus
        /// untouched). Needs <see cref="IEventBus"/> and <see cref="EntityIdAllocator"/> in the registry; targets may be null (site choice
        /// then ignores infrastructure).
        /// </summary>
        public static EmergenceDirector Install(IServiceRegistry registry, IFeatureFlags flags, IEmergenceTraceSource traces, IEmergenceTargets targets = null, EmergenceOptions options = null)
        {
            if (registry == null || flags == null || traces == null || !flags.IsOn(EmergenceFlags.Enabled)) return null;
            if (!registry.TryResolve(out IEventBus bus) || !registry.TryResolve(out EntityIdAllocator ids)) return null;
            var director = new EmergenceDirector(bus, traces, targets, ids.Allocate, options);
            bus.Subscribe<QuakeBegan>(director);
            bus.Subscribe<FaultRupture>(director);
            registry.Register(director);
            return director;
        }
    }
}
