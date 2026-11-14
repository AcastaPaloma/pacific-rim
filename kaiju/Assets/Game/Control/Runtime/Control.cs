using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core.Contracts;

namespace Game.Control
{
    /// <summary>The M0 arbiter (MODULARITY 4.1 "Now"): the first valid pilot's input passes straight through.</summary>
    public sealed class SinglePilotArbiter : IIntentArbiter
    {
        public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
        {
            for (int i = 0; i < inputs.Count; i++)
                if (inputs[i].valid) return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
            return new IntentFrame { move = MoveId.Stand, pilots = 0 };
        }
    }

    /// <summary>A pilot that plays a fixed timeline of intents keyed by simulation step (tests, scenarios, replays). Deterministic: reads only the Core clock.</summary>
    public sealed class ScriptedPilotSource : IPilotSource
    {
        struct Entry { public long Step; public PilotInput Input; }
        readonly List<Entry> _timeline = new List<Entry>();
        readonly ISimulationClock _clock;
        int _cursor = -1;

        public ScriptedPilotSource(ISimulationClock clock) { _clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        public ScriptedPilotSource At(double seconds, MoveId move, Vector3 target, float effort)
        {
            long step = (long)Math.Round(seconds / _clock.StepSeconds);
            if (_timeline.Count > 0 && step < _timeline[_timeline.Count - 1].Step) throw new ArgumentException("timeline entries must be added in time order");
            _timeline.Add(new Entry { Step = step, Input = new PilotInput { move = move, target = target, effort = effort, valid = true } });
            return this;
        }

        public PilotInput Read()
        {
            long now = _clock.Step;
            while (_cursor + 1 < _timeline.Count && _timeline[_cursor + 1].Step <= now) _cursor++;
            return _cursor < 0 ? new PilotInput { move = MoveId.Stand, valid = true } : _timeline[_cursor].Input;
        }
    }

    /// <summary>Data spec control_tuning@v1 (Assets/Game/Config/Control/control_tuning.json).</summary>
    [Serializable]
    public sealed class ControlTuning
    {
        public const string Schema = "control_tuning@v1";
        public string schema;
        public double stick_dead_zone, target_distance_m, stop_button_emergency_hold_s;
    }

    /// <summary>Engine-free mapping from a stick sample to a pilot input, so the device source stays a thin adapter and the mapping is testable.</summary>
    public static class StickMapping
    {
        /// <param name="stickX">right is positive</param><param name="stickY">forward is positive</param>
        /// <param name="origin">where the mech is</param><param name="viewYaw">heading the stick is relative to (camera yaw), rad</param>
        public static PilotInput Map(float stickX, float stickY, bool stop, bool emergency, Vector3 origin, float viewYaw, ControlTuning t)
        {
            if (emergency) return new PilotInput { move = MoveId.EmergencyStop, target = origin, valid = true };
            float mag = (float)Math.Sqrt(stickX * stickX + stickY * stickY);
            if (stop || mag <= t.stick_dead_zone) return new PilotInput { move = MoveId.Stop, target = origin, valid = true };
            float effort = Math.Min(1f, (mag - (float)t.stick_dead_zone) / (1f - (float)t.stick_dead_zone));
            float s = (float)Math.Sin(viewYaw), c = (float)Math.Cos(viewYaw);
            var dir = new Vector3(stickX * c + stickY * s, 0, -stickX * s + stickY * c) / mag;
            return new PilotInput { move = MoveId.Walk, target = origin + dir * (float)t.target_distance_m, effort = effort, valid = true };
        }
    }
}
