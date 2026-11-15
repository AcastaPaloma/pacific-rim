using System;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Client side: one call per sampled intent. Assigns the per-slot increasing sequence, and sends the new intent
    /// plus the previous 2 in a single unreliable datagram (<see cref="IntentBundle"/>).
    /// </summary>
    public sealed class IntentSender
    {
        private readonly PlayerSlot _slot;
        private readonly ITransport _transport;
        private readonly IntentPacket[] _recent = new IntentPacket[IntentBundle.MaxPackets]; // newest first
        private int _count;
        private uint _next;

        public IntentSender(PlayerSlot slot, ITransport transport, uint firstSequence = 1)
        {
            if (!slot.IsValid) throw new ArgumentException("Invalid slot", nameof(slot));
            _slot = slot; _transport = transport; _next = firstSequence;
        }

        public uint LastSequence => unchecked(_next - 1);
        public long SentPackets { get; private set; }

        /// <summary>Samples intent <paramref name="payload"/> at client Core step <paramref name="clientStep"/> and sends. False if the link is closed.</summary>
        public bool Send(long clientStep, long lastHostStepSeen, IntentPayload payload)
        {
            var p = new IntentPacket(_slot, clientStep, _next, lastHostStepSeen, payload);
            _next = unchecked(_next + 1);
            for (int i = IntentBundle.MaxPackets - 1; i > 0; i--) _recent[i] = _recent[i - 1];
            _recent[0] = p;
            if (_count < IntentBundle.MaxPackets) _count++;
            SentPackets++;
            return _transport.Send(NetChannel.Unreliable, IntentBundle.Encode(new ReadOnlySpan<IntentPacket>(_recent, 0, _count)));
        }
    }
}
