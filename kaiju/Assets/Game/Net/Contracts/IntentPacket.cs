using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Opaque fixed-size intent payload. The Control seat contract (T-005) is not on this branch, so Net
    /// carries 16 bytes it does not interpret; the seat adapter packs and unpacks them.
    /// </summary>
    public readonly struct IntentPayload
    {
        public const int Size = 16;
        public readonly ulong Lo;
        public readonly ulong Hi;
        public IntentPayload(ulong lo, ulong hi) { Lo = lo; Hi = hi; }
    }

    /// <summary>Client -> host, unreliable, one per Core step (IntentPacket@v1).</summary>
    public readonly struct IntentPacket
    {
        public readonly PlayerSlot Slot;
        /// <summary>Client Core step n at which the intent was sampled.</summary>
        public readonly long ClientStep;
        /// <summary>Per-slot increasing sequence s; the host drops s &lt;= last accepted.</summary>
        public readonly uint Sequence;
        /// <summary>Last host Core step h the client has seen.</summary>
        public readonly long LastHostStepSeen;
        public readonly IntentPayload Payload;

        public IntentPacket(PlayerSlot slot, long clientStep, uint sequence, long lastHostStepSeen, IntentPayload payload)
        {
            Slot = slot; ClientStep = clientStep; Sequence = sequence; LastHostStepSeen = lastHostStepSeen; Payload = payload;
        }
    }
}
