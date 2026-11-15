using System;

namespace Game.Net
{
    /// <summary>
    /// Counts bytes and packets at the <see cref="ITransport"/> boundary (payload bytes only; an assumed per-packet
    /// overhead is added by whoever reports). Wrap the outermost transport an endpoint talks to.
    /// </summary>
    public sealed class NetMeter : ITransport
    {
        private readonly ITransport _inner;
        public long SentBytes, SentPackets, ReceivedBytes, ReceivedPackets;
        private long _s0, _sp0, _r0, _rp0;

        public NetMeter(ITransport inner) { _inner = inner ?? throw new ArgumentNullException(nameof(inner)); }

        public bool IsConnected => _inner.IsConnected;

        public bool Send(NetChannel channel, ReadOnlySpan<byte> payload)
        {
            bool ok = _inner.Send(channel, payload);
            if (ok) { SentBytes += payload.Length; SentPackets++; }
            return ok;
        }

        public bool TryReceive(out NetChannel channel, out byte[] payload)
        {
            if (!_inner.TryReceive(out channel, out payload)) return false;
            ReceivedBytes += payload.Length; ReceivedPackets++;
            return true;
        }

        public void Close() => _inner.Close();

        /// <summary>Returns the counts since the previous call and starts a new window.</summary>
        public void TakeWindow(out long sentBytes, out long sentPackets, out long receivedBytes, out long receivedPackets)
        {
            sentBytes = SentBytes - _s0; sentPackets = SentPackets - _sp0;
            receivedBytes = ReceivedBytes - _r0; receivedPackets = ReceivedPackets - _rp0;
            _s0 = SentBytes; _sp0 = SentPackets; _r0 = ReceivedBytes; _rp0 = ReceivedPackets;
        }
    }
}
