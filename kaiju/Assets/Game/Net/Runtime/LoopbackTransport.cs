using System;
using System.Collections.Generic;

namespace Game.Net
{
    /// <summary>
    /// In-memory transport pair. Whatever one end sends is immediately receivable at the other, in send order,
    /// on both channels (the two channels keep separate queues; ordering across them is not defined).
    /// </summary>
    public sealed class LoopbackTransport : ITransport
    {
        private readonly Queue<byte[]> _inReliable = new Queue<byte[]>();
        private readonly Queue<byte[]> _inUnreliable = new Queue<byte[]>();
        // Single arrival order across channels so receive order equals send order.
        private readonly Queue<NetChannel> _arrival = new Queue<NetChannel>();
        private LoopbackTransport _peer;
        private bool _closed;

        private LoopbackTransport() { }

        public static void CreatePair(out LoopbackTransport a, out LoopbackTransport b)
        {
            a = new LoopbackTransport(); b = new LoopbackTransport();
            a._peer = b; b._peer = a;
        }

        public bool IsConnected => !_closed && _peer != null && !_peer._closed;

        public bool Send(NetChannel channel, ReadOnlySpan<byte> payload)
        {
            if (!IsConnected) return false;
            _peer.Enqueue(channel, payload.ToArray());
            return true;
        }

        private void Enqueue(NetChannel channel, byte[] data)
        {
            (channel == NetChannel.Reliable ? _inReliable : _inUnreliable).Enqueue(data);
            _arrival.Enqueue(channel);
        }

        public bool TryReceive(out NetChannel channel, out byte[] payload)
        {
            // Messages already delivered stay readable after Close.
            if (_arrival.Count == 0) { channel = default; payload = null; return false; }
            channel = _arrival.Dequeue();
            payload = (channel == NetChannel.Reliable ? _inReliable : _inUnreliable).Dequeue();
            return true;
        }

        public void Close() { _closed = true; }
    }
}
