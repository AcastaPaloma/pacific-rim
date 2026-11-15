using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Thin host wrapper over HandshakeValidator, IntentHostBuffer and SnapshotCodec. No clock of its own: the caller
    /// passes a step to <see cref="Poll"/> and <see cref="Step"/> (Core step in the game; log records are keyed by it).
    /// Poll may be called as often as the transport is advanced, Step once per host step.
    /// </summary>
    public sealed class NetHostSession
    {
        private sealed class Conn
        {
            public NetMeter Link;
            public PlayerSlot Slot;
            public bool Joined, Refused, Dead;
            public long LastRxStep, RefusedStep;
        }

        private sealed class LoggingSink : IIntentSink
        {
            public IIntentSink Inner; public NetLog Log; public long Step;
            public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
            {
                if (source == IntentSource.Held) Log.Add(Step, NetLogKind.IntentHold, slot.Index);
                else if (source == IntentSource.Neutral) Log.Add(Step, NetLogKind.IntentNeutral, slot.Index);
                Inner.ApplyIntent(slot, payload, source);
            }
        }

        private readonly HandshakeValidator _validator;
        private readonly Func<long, StateSnapshot> _snapshotSource;
        private readonly List<Conn> _conns = new List<Conn>();
        private readonly IntentHostBuffer _buffer = new IntentHostBuffer();
        private readonly LoggingSink _sink = new LoggingSink();
        private readonly int _stepsPerSecond;
        private long _second;
        private int _lastStale, _lastOther;

        private readonly SortedSet<int> _used = new SortedSet<int>();

        public NetLog Log { get; }
        /// <summary>A joined client silent for more than this many steps loses its slot (0 = never). Clients send every step, so 250 (5 s at 50 Hz) is generous.</summary>
        public int SlotTimeoutSteps { get; set; }
        /// <summary>A refused connection is closed after this many steps (the Refuse has long since drained).</summary>
        public int RefusedLingerSteps { get; set; } = 100;
        public event Action<PlayerSlot> SlotJoined;
        public event Action<PlayerSlot> SlotReleased;
        public int SnapshotIntervalSteps { get; }
        public IntentHostBuffer Intents => _buffer;
        /// <summary>Slots in use, the host's own (index 0) included.</summary>
        public int OccupiedSlots => 1 + _used.Count;
        public bool IsOccupied(PlayerSlot slot) => _used.Contains(slot.Index);

        public NetHostSession(HandshakeValidator validator, Func<long, StateSnapshot> snapshotSource, NetLog log,
                              int snapshotIntervalSteps = 2, int stepsPerSecond = 50)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
            Log = log ?? throw new ArgumentNullException(nameof(log));
            SnapshotIntervalSteps = snapshotIntervalSteps; _stepsPerSecond = stepsPerSecond;
        }

        /// <summary>Adds one client connection (not yet joined). Returns its meter, the transport as the session sees it.</summary>
        public NetMeter AddConnection(ITransport transport)
        {
            var c = new Conn { Link = new NetMeter(transport) };
            _conns.Add(c);
            return c.Link;
        }

        /// <summary>
        /// Drains every connection: handshakes are answered, intents buffered. Then frees the slots of clients that left, whose link
        /// died or that went silent past <see cref="SlotTimeoutSteps"/>, and logs drops and per-second bandwidth.
        /// </summary>
        public void Poll(long step)
        {
            List<Conn> gone = null;
            foreach (var c in _conns)
            {
                if (c.Refused)
                {
                    if (c.RefusedStep == 0) c.RefusedStep = step + 1;
                    if (!c.Link.IsConnected || step + 1 - c.RefusedStep > RefusedLingerSteps)
                    {
                        if (c.Link.IsConnected) c.Link.Close();
                        (gone ?? (gone = new List<Conn>())).Add(c);
                    }
                    continue;
                }
                if (c.LastRxStep == 0) c.LastRxStep = step + 1;
                bool left = false;
                while (c.Link.TryReceive(out var ch, out var data))
                {
                    c.LastRxStep = step + 1;
                    if (ch == NetChannel.Reliable)
                    {
                        if (!c.Joined) HandleHello(c, data, step);
                        else if (data.Length > 0 && data[0] == NetCodec.MsgLeave) { left = true; break; }
                    }
                    else if (c.Joined) _buffer.Receive(data, c.Slot);
                }
                if (!c.Joined) continue;
                long silent = step + 1 - c.LastRxStep;
                if (left) { Free(c, 0, silent, step); (gone ?? (gone = new List<Conn>())).Add(c); }
                else if (!c.Link.IsConnected) { Free(c, 2, silent, step); (gone ?? (gone = new List<Conn>())).Add(c); }
                else if (SlotTimeoutSteps > 0 && silent > SlotTimeoutSteps) { Free(c, 1, silent, step); (gone ?? (gone = new List<Conn>())).Add(c); }
            }
            if (gone != null) foreach (var c in gone) _conns.Remove(c);
            int stale = _buffer.DroppedStale, other = _buffer.DroppedUnknownSlot + _buffer.DroppedMalformed;
            if (stale != _lastStale) { Log.Add(step, NetLogKind.IntentDropStale, a: stale - _lastStale); _lastStale = stale; }
            if (other != _lastOther) { Log.Add(step, NetLogKind.IntentDropOther, a: other - _lastOther); _lastOther = other; }
            if (_stepsPerSecond > 0 && step / _stepsPerSecond > _second)
            {
                _second = step / _stepsPerSecond;
                foreach (var c in _conns)
                {
                    if (!c.Joined) continue;
                    c.Link.TakeWindow(out long sb, out long sp, out long rb, out long rp);
                    Log.Add(step, NetLogKind.Bandwidth, c.Slot.Index, sb, rb, sp, rp);
                }
            }
        }

        /// <summary>Host-initiated release (kick, or the game saw the player leave): frees the slot and closes the link. False if the slot is not in use.</summary>
        public bool Release(PlayerSlot slot, long step)
        {
            foreach (var c in _conns)
            {
                if (!c.Joined || c.Slot != slot) continue;
                Free(c, 0, 0, step); c.Link.Close(); _conns.Remove(c);
                return true;
            }
            return false;
        }

        private void Free(Conn c, int reason, long silentSteps, long step)
        {
            c.Joined = false; c.Dead = true;
            _used.Remove(c.Slot.Index);
            _buffer.UnregisterSlot(c.Slot);
            c.Link.Close();
            Log.Add(step, NetLogKind.SlotRelease, c.Slot.Index, reason, silentSteps);
            SlotReleased?.Invoke(c.Slot);
        }

        private int LowestFree()
        {
            int max = _validator.MaxSlots;
            for (int i = 1; i < max; i++) if (!_used.Contains(i)) return i;
            return -1;
        }

        private void HandleHello(Conn c, byte[] data, long step)
        {
            HandshakeResult r;
            if (!NetCodec.TryDecode(data, out HandshakeHello hello)) r = null;
            else r = _validator.Evaluate(hello, OccupiedSlots, step, _snapshotSource(step), LowestFree());
            if (r == null || !r.Accepted)
            {
                var refuse = r?.Refuse ?? new HandshakeRefuse { Code = RefuseCode.Malformed, Details = new[] { "malformed hello" } };
                c.Refused = true;
                c.Link.Send(NetChannel.Reliable, NetCodec.Encode(refuse));
                Log.Add(step, NetLogKind.HandshakeRefuse, a: (long)refuse.Code, text: string.Join(" | ", refuse.Details));
                return; // the link is left open so the Refuse can drain; the caller closes it
            }
            c.Slot = r.Accept.Slot; c.Joined = true; _used.Add(c.Slot.Index);
            _buffer.RegisterSlot(c.Slot);
            c.Link.Send(NetChannel.Reliable, NetCodec.Encode(r.Accept));
            Log.Add(step, NetLogKind.HandshakeAccept, c.Slot.Index, c.Slot.Index, text: string.Join(" | ", r.Accept.Warnings));
            SlotJoined?.Invoke(c.Slot);
        }

        /// <summary>One host step: hands one intent per joined slot to <paramref name="sink"/>, then sends a snapshot to every joined client on every <see cref="SnapshotIntervalSteps"/>th step.</summary>
        public void Step(long hostStep, IIntentSink sink)
        {
            _sink.Inner = sink; _sink.Log = Log; _sink.Step = hostStep;
            _buffer.Deliver(hostStep, _sink);
            if (SnapshotIntervalSteps > 0 && hostStep % SnapshotIntervalSteps == 0)
            {
                byte[] bytes = null;
                foreach (var c in _conns)
                {
                    if (!c.Joined) continue;
                    if (bytes == null) bytes = SnapshotCodec.Encode(_snapshotSource(hostStep));
                    c.Link.Send(NetChannel.Unreliable, bytes);
                }
            }
        }
    }

    public enum ClientState { Idle, Joining, Joined, Refused, /* the host link died or Leave() was called */ Lost }

    /// <summary>Thin client wrapper: handshake, intent sender, snapshot receive and interpolation, logging.</summary>
    public sealed class NetClientSession
    {
        private readonly NetMeter _link;
        private readonly HandshakeHello _hello;
        private readonly SnapshotInterpolator _interp;
        private readonly int _stepsPerSecond;
        private IntentSender _sender;
        private long _second;
        private bool _wasStale;

        public NetLog Log { get; }
        public ClientState State { get; private set; } = ClientState.Idle;
        public PlayerSlot Slot { get; private set; }
        public HandshakeRefuse Refusal { get; private set; }
        public long LastHostStepSeen { get; private set; }
        public SnapshotInterpolator Interpolator => _interp;
        public NetMeter Link => _link;

        public NetClientSession(ITransport transport, FeatureFlagSnapshot flags, double stepSeconds, string buildVersion, NetLog log,
                                int stepsPerSecond = 50, int snapshotIntervalSteps = 2)
        {
            _link = new NetMeter(transport);
            _hello = NetContracts.BuildHello(flags, stepSeconds, buildVersion);
            Log = log ?? throw new ArgumentNullException(nameof(log));
            _interp = new SnapshotInterpolator(stepSeconds, snapshotIntervalSteps);
            _stepsPerSecond = stepsPerSecond;
        }

        public void Join()
        {
            if (State != ClientState.Idle) return;
            State = ClientState.Joining;
            _link.Send(NetChannel.Reliable, NetCodec.Encode(_hello));
        }

        /// <summary>Tells the host this client is leaving (its slot is freed at once) and closes the link. Join again with a new session on a new transport.</summary>
        public void Leave(long step)
        {
            if (State == ClientState.Joined) _link.Send(NetChannel.Reliable, NetCodec.EncodeLeave());
            _link.Close();
            State = ClientState.Lost;
        }

        public void Poll(long step)
        {
            if (State == ClientState.Lost) return;
            while (_link.TryReceive(out var ch, out var data))
            {
                if (ch == NetChannel.Reliable)
                {
                    if (State != ClientState.Joining || data.Length == 0) continue;
                    if (data[0] == NetCodec.MsgAccept && NetCodec.TryDecode(data, out HandshakeAccept a))
                    {
                        Slot = a.Slot; State = ClientState.Joined;
                        _sender = new IntentSender(a.Slot, _link);
                        LastHostStepSeen = a.HostStep;
                        _interp.Receive(step, a.FirstSnapshot);
                        Log.Add(step, NetLogKind.HandshakeAccept, a.Slot.Index, a.Slot.Index, text: string.Join(" | ", a.Warnings));
                    }
                    else if (data[0] == NetCodec.MsgRefuse && NetCodec.TryDecode(data, out HandshakeRefuse f))
                    {
                        Refusal = f; State = ClientState.Refused;
                        Log.Add(step, NetLogKind.HandshakeRefuse, a: (long)f.Code, text: string.Join(" | ", f.Details));
                    }
                }
                else if (State == ClientState.Joined && SnapshotCodec.TryDecode(data, out var snap))
                {
                    if (snap.HostStep > LastHostStepSeen) LastHostStepSeen = snap.HostStep;
                    _interp.Receive(step, snap);
                }
            }
            if ((State == ClientState.Joined || State == ClientState.Joining) && !_link.IsConnected)
            {
                Log.Add(step, NetLogKind.LinkLost, Slot.IsValid ? Slot.Index : -1, step - LastHostStepSeen);
                State = ClientState.Lost;
                return;
            }
            if (_stepsPerSecond > 0 && step / _stepsPerSecond > _second)
            {
                _second = step / _stepsPerSecond;
                if (State == ClientState.Joined)
                {
                    _link.TakeWindow(out long sb, out long sp, out long rb, out long rp);
                    Log.Add(step, NetLogKind.Bandwidth, Slot.Index, rb, sb, rp, sp);
                }
            }
        }

        /// <summary>Sends the intent sampled at <paramref name="clientStep"/>. False before the join is accepted.</summary>
        public bool SendIntent(long clientStep, IntentPayload payload) =>
            _sender != null && _sender.Send(clientStep, LastHostStepSeen, payload);

        /// <summary>Advances the interpolator and logs stale transitions (keyed by the floor of <paramref name="clientStep"/>).</summary>
        public void Update(double clientStep)
        {
            if (State != ClientState.Joined) return;
            _interp.Update(clientStep);
            if (_interp.IsStale != _wasStale)
            {
                _wasStale = _interp.IsStale;
                Log.Add((long)Math.Floor(clientStep), NetLogKind.SnapshotStale, Slot.Index, _wasStale ? 1 : 0);
            }
        }
    }
}
