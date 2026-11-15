using System;
using System.Collections.Generic;
using Game.Control.Contracts;
using Game.Core;

namespace Game.Net.ControlBridge
{
    /// <summary>Flag names of the seat bridge (T-030 step 2). Everything here is off unless <see cref="Seat"/> is on.</summary>
    public static class SeatBridgeFlags
    {
        public const string Seat = "net.seatBridge";
    }

    /// <summary>
    /// One mech's pilot seat: an arbiter, an optional local device pilot and one <see cref="RemotePilotSource"/> per remote slot
    /// assigned to this mech. Engine-free; the MonoBehaviour (Game.Net.ControlBridge.Unity) is a thin shell around it.
    /// With the flag off the seat has only the local pilot and refuses remote slots.
    /// </summary>
    public sealed class MechSeat
    {
        private readonly IFeatureFlags _flags;
        private readonly List<RemotePilotSource> _remotes = new List<RemotePilotSource>();
        public PilotSeat Seat { get; }
        public int MechIndex { get; }
        public IntentFrame LastIntent { get; private set; }
        public IReadOnlyList<RemotePilotSource> Remotes => _remotes;

        public MechSeat(IFeatureFlags flags, int mechIndex, IIntentArbiter arbiter, IPilotSource local = null)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            if (arbiter == null) throw new ArgumentNullException(nameof(arbiter));
            MechIndex = mechIndex;
            Seat = new PilotSeat(arbiter);
            if (local != null) Seat.pilots.Add(local);
        }

        public bool BridgeOn => _flags.IsOn(SeatBridgeFlags.Seat);

        /// <summary>Gives a remote slot a pilot at this seat. Throws while net.seatBridge is off.</summary>
        public RemotePilotSource AddRemote(PlayerSlot slot, bool neutralAsStop = false)
        {
            if (!BridgeOn) throw new InvalidOperationException("feature flag " + SeatBridgeFlags.Seat + " is off: no remote pilot is created");
            var r = new RemotePilotSource(slot) { NeutralAsStop = neutralAsStop };
            _remotes.Add(r);
            Seat.pilots.Add(r);
            return r;
        }

        /// <summary>Takes a remote slot's pilot off this seat (slot released). False if the slot had none here.</summary>
        public bool RemoveRemote(PlayerSlot slot)
        {
            for (int i = 0; i < _remotes.Count; i++)
            {
                if (_remotes[i].Slot != slot) continue;
                Seat.pilots.Remove(_remotes[i]); _remotes.RemoveAt(i);
                return true;
            }
            return false;
        }

        /// <summary>Once per simulation step, after the host has delivered intents.</summary>
        public IntentFrame Read() => LastIntent = Seat.Read();
    }

    /// <summary>
    /// Host side, whole table: slot to mech assignment and the one sink handed to NetHostSession.Step.
    /// Advance(stamp) is idempotent per stamp, so several per-mech MonoBehaviours can all call it from FixedUpdate and the session steps once.
    /// </summary>
    public sealed class HostSeatPump : IIntentSink
    {
        private readonly NetHostSession _session;
        private readonly List<MechSeat> _mechs = new List<MechSeat>();
        private readonly Dictionary<int, MechSeat> _slotToMech = new Dictionary<int, MechSeat>();
        private readonly Action<long> _beforePoll;
        private double _lastStamp = double.NaN;
        public long Step { get; private set; } = -1;

        /// <param name="beforePoll">link advance for simulated transports (tests); null on a real link</param>
        public HostSeatPump(NetHostSession session, Action<long> beforePoll = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _beforePoll = beforePoll;
            _session.SlotReleased += OnSlotReleased;
            _session.SlotJoined += OnSlotJoined;
        }

        /// <summary>
        /// Decides which mech a newly joined slot pilots (null = leave it unassigned; the game assigns later). Runs on every join, a rejoin
        /// into a freed slot included. Default none.
        /// </summary>
        public Func<PlayerSlot, MechSeat> AssignPolicy { get; set; }

        private void OnSlotJoined(PlayerSlot slot)
        {
            var m = AssignPolicy?.Invoke(slot);
            if (m != null && !_slotToMech.ContainsKey(slot.Index)) Assign(slot, m);
        }

        private void OnSlotReleased(PlayerSlot slot) => Unassign(slot);

        /// <summary>Frees a slot's pilot: its remote source leaves the mech's seat, so the mech falls back to its other pilots. Runs automatically when the session releases the slot.</summary>
        public bool Unassign(PlayerSlot slot)
        {
            if (!_slotToMech.TryGetValue(slot.Index, out var m)) return false;
            _slotToMech.Remove(slot.Index);
            m.RemoveRemote(slot);
            return true;
        }

        public void AddMech(MechSeat mech) { if (!_mechs.Contains(mech)) _mechs.Add(mech); }

        /// <summary>Slot-to-mech assignment (from the handshake's Accept slot). One mech per slot; reassigning moves it.</summary>
        public RemotePilotSource Assign(PlayerSlot slot, MechSeat mech, bool neutralAsStop = false)
        {
            if (!slot.IsValid) throw new ArgumentException("invalid slot", nameof(slot));
            AddMech(mech);
            if (_slotToMech.ContainsKey(slot.Index)) throw new InvalidOperationException("slot " + slot.Index + " is already assigned");
            _slotToMech[slot.Index] = mech;
            return mech.AddRemote(slot, neutralAsStop);
        }

        public MechSeat MechOf(PlayerSlot slot) => _slotToMech.TryGetValue(slot.Index, out var m) ? m : null;

        public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
        {
            if (!_slotToMech.TryGetValue(slot.Index, out var m)) return; // unassigned slot: dropped
            var rs = m.Remotes;
            for (int i = 0; i < rs.Count; i++) rs[i].ApplyIntent(slot, payload, source);
        }

        /// <summary>Steps the session once per distinct stamp (e.g. the fixed time) and reads every seat. False if this stamp was already served.</summary>
        public bool Advance(double stamp)
        {
            if (stamp.Equals(_lastStamp)) return false;
            _lastStamp = stamp;
            Step++;
            _beforePoll?.Invoke(Step);
            _session.Poll(Step);
            _session.Step(Step, this);
            for (int i = 0; i < _mechs.Count; i++) _mechs[i].Read();
            return true;
        }
    }

    /// <summary>Client side: publishes the local pilot once per step after the join. Off flag: never sends.</summary>
    public sealed class ClientSeatLink
    {
        private readonly IFeatureFlags _flags;
        private readonly NetClientSession _client;
        private readonly LocalSeatIntentPublisher _publisher;
        private readonly Action<long> _beforePoll;
        private double _lastStamp = double.NaN;
        public long Step { get; private set; } = -1;
        public long Sent { get; private set; }

        public ClientSeatLink(IFeatureFlags flags, NetClientSession client, IPilotSource local, Action<long> beforePoll = null)
        {
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _publisher = new LocalSeatIntentPublisher(local, client);
            _beforePoll = beforePoll;
        }

        /// <summary>The slot the host assigned (valid once Joined).</summary>
        public PlayerSlot Slot => _client.Slot;
        public bool Joined => _client.State == ClientState.Joined;

        public bool Advance(double stamp)
        {
            if (!_flags.IsOn(SeatBridgeFlags.Seat)) return false;
            if (stamp.Equals(_lastStamp)) return false;
            _lastStamp = stamp;
            Step++;
            _beforePoll?.Invoke(Step);
            _client.Poll(Step);
            if (!Joined) return true;
            if (_publisher.Tick(Step)) Sent++;
            return true;
        }
    }
}
