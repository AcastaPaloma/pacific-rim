using System;
using System.Collections.Generic;
using Game.Net;

namespace Game.Platform.Null
{
    /// <summary>
    /// In-process lossless <see cref="ITransport"/> pair (contract v1). Delivers in order on both channels, never
    /// drops, no clock. Closing either end disconnects both; already-queued messages stay receivable.
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        readonly Queue<(NetChannel, byte[])> _inbox = new Queue<(NetChannel, byte[])>();
        LoopbackTransport _peer;
        bool _closed;

        LoopbackTransport() { }


        public static (LoopbackTransport, LoopbackTransport) CreatePair()
        {
            var a = new LoopbackTransport(); var b = new LoopbackTransport();
            a._peer = b; b._peer = a;
            return (a, b);
        }

        public bool IsConnected => !_closed && !_peer._closed;

        public bool Send(NetChannel channel, ReadOnlySpan<byte> payload)
        {
            if (!IsConnected) return false;
            _peer._inbox.Enqueue((channel, payload.ToArray()));
            return true;
        }

        public bool TryReceive(out NetChannel channel, out byte[] data)
        {
            if (_inbox.Count == 0) { channel = NetChannel.Reliable; data = null; return false; }
            var m = _inbox.Dequeue();
            channel = m.Item1; data = m.Item2;
            return true;
        }

        public void Close() { _closed = true; }
    }
}
