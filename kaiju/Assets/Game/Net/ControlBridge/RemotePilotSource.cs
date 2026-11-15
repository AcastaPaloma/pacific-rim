using System;
using System.Collections.Generic;
using Game.Control.Contracts;
using Game.Core;

namespace Game.Net.ControlBridge
{
    /// <summary>
    /// Host side: the pilot of one remote slot. Register it as (part of) the <see cref="IIntentSink"/> passed to
    /// NetHostSession.Step; Net calls ApplyIntent once per host step with fresh / held / neutral (hold limit and
    /// neutral fall-back are IntentHostBuffer's: <see cref="IntentHostBuffer.HoldSteps"/> steps, then neutral).
    /// Read() returns the input decoded at the latest host step (same value for repeated reads within a step).
    /// Neutral decodes to <c>valid = false</c> (the arbiter ignores it); set <see cref="NeutralAsStop"/> to emit
    /// a valid Stop instead (R-023 4.3 "stop command"). Before the first step Read() is the plain neutral (valid = false).
    /// </summary>
    public sealed class RemotePilotSource : IPilotSource, IIntentSink
    {
        public PlayerSlot Slot { get; }
        public bool NeutralAsStop { get; set; }
        public int Fresh { get; private set; }
        public int Held { get; private set; }
        public int Neutral { get; private set; }
        public IntentSource LastSource { get; private set; } = IntentSource.Neutral;
        private PilotInput _current;

        public RemotePilotSource(PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("Invalid slot", nameof(slot));
            Slot = slot;
        }

        public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
        {
            if (slot != Slot) return;
            LastSource = source;
            switch (source)
            {
                case IntentSource.Fresh: Fresh++; break;
                case IntentSource.Held: Held++; break;
                default: Neutral++; break;
            }
            _current = source == IntentSource.Neutral ? NeutralInput() : IntentCodec.Decode(payload);
        }

        public PilotInput Read() => _current;

        private PilotInput NeutralInput() =>
            NeutralAsStop ? new PilotInput { move = MoveId.Stop, valid = true } : default(PilotInput);
    }

    /// <summary>One IIntentSink in front of several per-slot sinks (a host seat per remote slot).</summary>
    public sealed class IntentSinkFanout : IIntentSink
    {
        private readonly List<IIntentSink> _sinks = new List<IIntentSink>();
        public void Add(IIntentSink sink) => _sinks.Add(sink);
        public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
        {
            for (int i = 0; i < _sinks.Count; i++) _sinks[i].ApplyIntent(slot, payload, source);
        }
    }
}
