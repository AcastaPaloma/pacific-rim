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
            public bool Joined, Refused;
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

        public NetLog Log { get; }
        public int SnapshotIntervalSteps { get; }
        public IntentHostBuffer Intents => _buffer;
        /// <summary>Slots in use, the host's own (index 0) included.</summary>
        public int OccupiedSlots { get; private set; } = 1;

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

        /// <summary>Drains every connection: handshakes are answered, intents buffered. Then logs drops and per-second bandwidth.</summary>
        public void Poll(long step)
        {
            foreach (var c in _conns)
            {
                if (c.Refused) continue;
                while (c.Link.TryReceive(out var ch, out var data))
                {
                    if (ch == NetChannel.Reliable) { if (!c.Joined) HandleHello(c, data, step); }
                    else if (c.Joined) _buffer.Receive(data, c.Slot);
                }
            }
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

        private void HandleHello(Conn c, byte[] data, long step)
        {
            HandshakeResult r;
            if (!NetCodec.TryDecode(data, out HandshakeHello hello)) r = null;
            else r = _validator.Evaluate(hello, OccupiedSlots, step, _snapshotSource(step));
            if (r == null || !r.Accepted)
            {
                var refuse = r?.Refuse ?? new HandshakeRefuse { Code = RefuseCode.Malformed, Details = new[] { "malformed hello" } };
                c.Refused = true;
                c.Link.Send(NetChannel.Reliable, NetCodec.Encode(refuse));
                Log.Add(step, NetLogKind.HandshakeRefuse, a: (long)refuse.Code, text: string.Join(" | ", refuse.Details));
                return; // the link is left open so the Refuse can drain; the caller closes it
            }
            c.Slot = r.Accept.Slot; c.Joined = true; OccupiedSlots++;
            _buffer.RegisterSlot(c.Slot);
            c.Link.Send(NetChannel.Reliable, NetCodec.Encode(r.Accept));
            Log.Add(step, NetLogKind.HandshakeAccept, c.Slot.Index, c.Slot.Index, text: string.Join(" | ", r.Accept.Warnings));
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

    public enum ClientState { Idle, Joining, Joined, Refused }

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

        public void Poll(long step)
        {
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
