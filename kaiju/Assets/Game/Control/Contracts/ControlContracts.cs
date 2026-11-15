using System.Collections.Generic;
using System.Numerics;

namespace Game.Control.Contracts
{
    public static class ControlContractVersions { public const int Intent = 1; }

    /// <summary>What a pilot can ask for (D-013: the move and where it lands, never a joint). Append-only: values are logged.</summary>
    public enum MoveId
    {
        Stand = 0,
        /// <summary>Walk toward <c>target</c> and stop there; <c>effort</c> in [0,1] scales the speed.</summary>
        Walk = 1,
        /// <summary>Turn on the spot to face <c>target</c>.</summary>
        Turn = 2,
        /// <summary>Come to rest by stepping, at the normal braking rate.</summary>
        Stop = 3,
        /// <summary>Come to rest as fast as the limits allow.</summary>
        EmergencyStop = 4,
        /// <summary>Lift the foot nearest <c>target</c> and bring it down hard on the spot.</summary>
        Stomp = 5
        // later tasks: Brace, GetUp, Strike... (F-003, F-005)
    }

    /// <summary>One pilot's raw request for this step. A device or a remote player produces it; nothing downstream reads a device.</summary>
    public struct PilotInput
    {
        public MoveId move;
        public Vector3 target;   // world point, m
        public float effort;     // 0..1 (values outside are limited downstream and the clamp is logged)
        public bool valid;       // false: this pilot has nothing to say this step
    }

    /// <summary>MODULARITY 4.1. The merged request the unit controller receives.</summary>
    public struct IntentFrame
    {
        public MoveId move;
        public Vector3 target;
        public float effort;
        public int pilots;       // how many valid inputs were merged
    }

    /// <summary>MODULARITY 4.1: a device or a remote player.</summary>
    public interface IPilotSource { PilotInput Read(); }

    /// <summary>MODULARITY 4.1: merges the seat's pilot inputs into one intent. New arbiters are new classes; nothing else changes.</summary>
    public interface IIntentArbiter { IntentFrame Merge(IReadOnlyList<PilotInput> inputs); }

    /// <summary>MODULARITY 4.1: one per mech. Any number of pilots (D-003).</summary>
    public class PilotSeat
    {
        public List<IPilotSource> pilots = new List<IPilotSource>();
        public IIntentArbiter arbiter;
        readonly List<PilotInput> _inputs = new List<PilotInput>();

        public PilotSeat() { }
        public PilotSeat(IIntentArbiter arbiter, params IPilotSource[] sources) { this.arbiter = arbiter; pilots.AddRange(sources); }

        /// <summary>Reads every pilot once and merges. Call once per simulation step.</summary>
        public IntentFrame Read()
        {
            _inputs.Clear();
            for (int i = 0; i < pilots.Count; i++) _inputs.Add(pilots[i].Read());
            return arbiter.Merge(_inputs);
        }
    }
}
