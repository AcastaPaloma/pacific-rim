using System;
using System.Collections.Generic;
using Game.Control.Contracts;
using Game.Core;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Unity;
using Game.Units.Mech.Unity;
using UnityEngine;

namespace Game.Units.Mech.Scenarios
{
    /// <summary>
    /// Headless composition root for scenarios and tests: its own Core services (bus, clock, IDs), one physics world, any number of mechs.
    /// Step order: every unit's PreStep (intent, control) at step n; one PhysX step; FootContact events are published during it with Step = n + 1; clock advances; PostStep(n + 1) logs.
    /// </summary>
    public sealed class MechSimulation : IDisposable
    {
        public CoreServices Core { get; }
        public BodyWorld World { get; }
        public List<MechUnit> Units { get; } = new List<MechUnit>();
        public GameObject Ground { get; }
        public long Step => Core.Clock.Step;
        public double Time => Core.Clock.TimeSeconds;
        public double StepSeconds => Core.Clock.StepSeconds;

        public MechSimulation(BodyTuning tuning, double stepSeconds, bool ground = true, float slopeDeg = 0)
        {
            var flags = new FeatureFlagSnapshot(new[] { new KeyValuePair<string, bool>(MechConfig.FlagEnabled, true), new KeyValuePair<string, bool>(MechConfig.FlagBodyEnabled, true), new KeyValuePair<string, bool>(MechConfig.FlagGaitLog, true), new KeyValuePair<string, bool>(MechConfig.FlagPhysicsLog, true) });
            Core = new CoreServices(new CoreSettings(stepSeconds, 10000), flags, new CollectingErrorSink());
            World = new BodyWorld(tuning, stepSeconds, true, Core.Bus);
            if (ground) Ground = BodyWorld.CreateGround(tuning, 5000f, 0f, slopeDeg);
        }

        public MechUnit Spawn(MechConfig config, PilotSeat seat, Vector3 position, float yawDeg = 0, bool visuals = true)
        {
            var unit = new MechUnit(World, config, Core.EntityIds.Allocate(), Core.Bus, seat, new BodySpawn { Position = new System.Numerics.Vector3(position.x, position.y, position.z), Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yawDeg * Mathf.Deg2Rad), Visuals = visuals });
            unit.ClockStep = () => Core.Clock.Step + 1;
            Units.Add(unit);
            return unit;
        }

        public void StepOnce()
        {
            long n = Core.Clock.Step;
            foreach (var u in Units) u.PreStep(n);
            World.Simulate(n + 1);
            Core.Clock.Advance();
            foreach (var u in Units) u.PostStep(n + 1);
        }

        public void Run(double seconds, Action<MechSimulation> eachStep = null)
        {
            long end = Core.Clock.Step + (long)Math.Round(seconds / StepSeconds);
            while (Core.Clock.Step < end) { StepOnce(); eachStep?.Invoke(this); }
        }

        public void Dispose()
        {
            foreach (var u in Units) u.Dispose();
            if (Ground != null) UnityEngine.Object.DestroyImmediate(Ground);
            World.Dispose(); Core.Dispose();
        }
    }
}
