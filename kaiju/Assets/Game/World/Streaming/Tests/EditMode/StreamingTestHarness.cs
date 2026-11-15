// Shared helpers of the World.Streaming tests: an instant loader that answers the scheduler's plan, and a recorder of bus events.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.World.Geo;

namespace Game.World.Streaming.Tests
{
    sealed class NullErrors : ICoreErrorSink { public void Report(CoreError error) { } }

    sealed class EventLog : IEventHandler<TileResident>, IEventHandler<TileEvicted>, IEventHandler<ClusterOriginChanged>
    {
        public int Resident, Evicted, OriginChanges;
        public readonly List<TileResident> ResidentEvents = new List<TileResident>();
        public void Handle(in TileResident e) { Resident++; ResidentEvents.Add(e); }
        public void Handle(in TileEvicted e) { Evicted++; }
        public void Handle(in ClusterOriginChanged e) { OriginChanges++; }
    }

    /// <summary>Scheduler + instant loader. Every requested key completes (with its estimate times Scale) and every eviction is released within the same step.</summary>
    sealed class Harness
    {
        public readonly StreamingConfig Cfg;
        public readonly ITileCostModel Cost;
        public readonly StreamingScheduler Sched;
        public readonly StreamingPlan Plan = new StreamingPlan();
        public readonly EventBus Bus;
        public readonly EventLog Log = new EventLog();
        public readonly Dictionary<ResidencyKey, int> Loads = new Dictionary<ResidencyKey, int>();
        public readonly Dictionary<ResidencyKey, int> Evictions = new Dictionary<ResidencyKey, int>();
        public double ActualScale = 1.0;
        /// <summary>When false the harness does not answer loads (the test completes them by hand).</summary>
        public bool AutoComplete = true;
        public long MaxGpu, MaxCpu;
        public double Time;

        public Harness(StreamingConfig cfg = null, ITileCostModel cost = null)
        {
            Cfg = cfg ?? new StreamingConfig();
            Cost = cost ?? new DefaultTileCostModel();
            Bus = new EventBus(new NullErrors(), 100000);
            Bus.Subscribe<TileResident>(Log); Bus.Subscribe<TileEvicted>(Log); Bus.Subscribe<ClusterOriginChanged>(Log);
            Sched = new StreamingScheduler(Cfg, Cost, Bus);
        }

        public StreamingPlan Step(double dt, params FocusSample[] foci)
        {
            Sched.Tick(dt, foci, Plan);
            Time += dt;
            for (int i = 0; i < Plan.Evict.Count; i++)
            {
                var k = Plan.Evict[i];
                Evictions[k] = (Evictions.TryGetValue(k, out int e) ? e : 0) + 1;
                Sched.Released(k);
            }
            for (int i = 0; i < Plan.Load.Count; i++)
            {
                var r = Plan.Load[i];
                Loads[r.Key] = (Loads.TryGetValue(r.Key, out int l) ? l : 0) + 1;
                if (AutoComplete) Sched.Completed(r.Key, (long)(r.EstGpuBytes * ActualScale), (long)(r.EstCpuBytes * ActualScale));
            }
            MaxGpu = Math.Max(MaxGpu, Sched.GpuBytesInUse); MaxCpu = Math.Max(MaxCpu, Sched.CpuBytesInUse);
            return Plan;
        }

        public static FocusSample Camera(int id, double x, double z, float vx = 0, float vz = 0, double y = 50)
            => new FocusSample { Id = id, Kind = FocusKind.Camera, X = x, Y = y, Z = z, Vx = vx, Vz = vz };

        public static FocusSample Body(int id, double x, double z, float vx = 0, float vz = 0, double y = 10, bool render = false)
            => new FocusSample { Id = id, Kind = FocusKind.Body, X = x, Y = y, Z = z, Vx = vx, Vz = vz, ReachM = 100f, NeedsPhysics = true, NeedsRender = render };

        public int ResidentOfKind(ResidencyKind kind)
        {
            int c = 0;
            foreach (var k in AllKeysEverLoaded()) if (k.Kind == kind && Sched.GetState(k) == TileState.Resident) c++;
            return c;
        }

        public IEnumerable<ResidencyKey> AllKeysEverLoaded() { return Loads.Keys; }

        /// <summary>Distance from a point to a key's footprint (horizontal).</summary>
        public static double DistXZ(ResidencyKey key, double x, double z)
        {
            key.Bounds(out double x0, out double z0, out double x1, out double z1);
            double dx = Math.Max(Math.Max(x0 - x, x - x1), 0.0), dz = Math.Max(Math.Max(z0 - z, z - z1), 0.0);
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
