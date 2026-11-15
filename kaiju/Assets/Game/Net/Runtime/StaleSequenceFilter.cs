using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Host-side per-slot stale-packet gate: accepts a packet only if its sequence is newer than the last accepted
    /// one for that slot (serial-number arithmetic, so uint wrap-around is handled). Duplicates and reordered
    /// older packets are dropped.
    /// </summary>
    public sealed class StaleSequenceFilter
    {
        private readonly Dictionary<PlayerSlot, uint> _last = new Dictionary<PlayerSlot, uint>();
        public int DroppedCount { get; private set; }

        public bool Accept(in IntentPacket p)
        {
            if (_last.TryGetValue(p.Slot, out uint last) && unchecked((int)(p.Sequence - last)) <= 0)
            {
                DroppedCount++;
                return false;
            }
            _last[p.Slot] = p.Sequence;
            return true;
        }

        public void Reset(PlayerSlot slot) => _last.Remove(slot);
    }
}
