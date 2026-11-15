using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    public enum IntentSource : byte
    {
        /// <summary>A new intent arrived since the previous host step.</summary>
        Fresh = 0,
        /// <summary>Nothing new; the last intent is repeated (at most <see cref="IntentHostBuffer.HoldSteps"/> steps).</summary>
        Held = 1,
        /// <summary>Hold expired or nothing ever arrived: the neutral (all-zero) payload.</summary>
        Neutral = 2,
    }

    /// <summary>
    /// Implemented later by the Control seat (T-005, Q-100); Net never interprets the payload.
    /// Called once per registered slot per host step, in ascending <see cref="PlayerSlot"/> order.
    /// </summary>
    public interface IIntentSink
    {
        void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source);
    }

    /// <summary>
    /// Host-side intent buffer. Accepts only sequences newer than the last accepted for the slot (so duplicates and
    /// reordered older packets are dropped, wrap-safe), keeps the newest accepted intent per slot, and at each host
    /// step hands one intent per registered slot to the sink: fresh, else held for 100 ms (5 Core steps at 0.02 s),
    /// else neutral. Delivery order is by slot index only, never arrival order.
    /// </summary>
    public sealed class IntentHostBuffer
    {
        public const double HoldSeconds = 0.1;
        public const double CoreStepSeconds = 0.02;
        public const int HoldSteps = 5; // = HoldSeconds / CoreStepSeconds, fixed so float rounding cannot change it

        private sealed class SlotState
        {
            public bool HasPending; public IntentPayload Pending;
            public bool HasLast; public IntentPayload Last; public long LastFreshStep;
        }

        private readonly StaleSequenceFilter _filter = new StaleSequenceFilter();
        private readonly SortedDictionary<int, SlotState> _slots = new SortedDictionary<int, SlotState>();
        private readonly IntentPacket[] _scratch = new IntentPacket[IntentBundle.MaxPackets];

        public int DroppedStale => _filter.DroppedCount;
        public int DroppedUnknownSlot { get; private set; }
        public int DroppedMalformed { get; private set; }
        /// <summary>Accepted intents replaced by a newer one before a host step took them.</summary>
        public int Superseded { get; private set; }
        public int HeldSteps { get; private set; }
        public int NeutralSteps { get; private set; }

        public void RegisterSlot(PlayerSlot slot)
        {
            if (!slot.IsValid) throw new ArgumentException("Invalid slot", nameof(slot));
            if (!_slots.ContainsKey(slot.Index)) _slots[slot.Index] = new SlotState();
        }

        public void UnregisterSlot(PlayerSlot slot) { _slots.Remove(slot.Index); _filter.Reset(slot); }

        /// <summary>Offers one packet. True if it became the slot's pending intent.</summary>
        public bool Accept(in IntentPacket p)
        {
            if (!_slots.TryGetValue(p.Slot.Index, out var st)) { DroppedUnknownSlot++; return false; }
            if (!_filter.Accept(p)) return false;
            if (st.HasPending) Superseded++;
            st.HasPending = true; st.Pending = p.Payload;
            return true;
        }

        /// <summary>
        /// Decodes one unreliable datagram and accepts its packets oldest first. Packets whose slot is not
        /// <paramref name="expectedSlot"/> (the slot bound to the connection it came from) are dropped.
        /// </summary>
        public void Receive(byte[] datagram, PlayerSlot expectedSlot)
        {
            if (!IntentBundle.TryDecode(datagram, _scratch, out int n)) { DroppedMalformed++; return; }
            for (int i = n - 1; i >= 0; i--)
            {
                if (_scratch[i].Slot != expectedSlot) { DroppedUnknownSlot++; continue; }
                Accept(_scratch[i]);
            }
        }

        /// <summary>Drains everything queued on the transport bound to <paramref name="slot"/>: unreliable datagrams go to <see cref="Receive"/>, reliable ones to <paramref name="onReliable"/> (dropped if null).</summary>
        public void Pump(ITransport transport, PlayerSlot slot, Action<byte[]> onReliable = null)
        {
            while (transport.TryReceive(out var ch, out var data))
            {
                if (ch == NetChannel.Unreliable) Receive(data, slot);
                else onReliable?.Invoke(data);
            }
        }

        /// <summary>Start of host step <paramref name="hostStep"/>: delivers one intent per registered slot, ascending.</summary>
        public void Deliver(long hostStep, IIntentSink sink)
        {
            foreach (var kv in _slots)
            {
                var st = kv.Value;
                var slot = PlayerSlot.FromIndex(kv.Key);
                if (st.HasPending)
                {
                    st.HasPending = false; st.Last = st.Pending; st.HasLast = true; st.LastFreshStep = hostStep;
                    sink.ApplyIntent(slot, st.Last, IntentSource.Fresh);
                }
                else if (st.HasLast && hostStep - st.LastFreshStep <= HoldSteps)
                {
                    HeldSteps++;
                    sink.ApplyIntent(slot, st.Last, IntentSource.Held);
                }
                else
                {
                    NeutralSteps++;
                    sink.ApplyIntent(slot, default(IntentPayload), IntentSource.Neutral);
                }
            }
        }
    }
}
