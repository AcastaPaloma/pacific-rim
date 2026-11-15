using System;

namespace Game.Net
{
    public enum NetChannel : byte
    {
        /// <summary>Ordered, lossless. Handshake, join/leave, rare discrete commands.</summary>
        Reliable = 0,
        /// <summary>Unordered, may drop. Intents and snapshots.</summary>
        Unreliable = 1,
    }

    /// <summary>
    /// One endpoint of a point-to-point connection (contract v1). A host holds one per client.
    /// Engine-free and clock-free: any notion of time belongs to the implementation (Steam adapter: its own;
    /// simulated link: the Core step it is advanced to). Payloads are opaque bytes; the transport copies them.
    /// </summary>
    public interface ITransport
    {
        bool IsConnected { get; }
        /// <summary>Queues a copy of the payload. Returns false when the link is closed.</summary>
        bool Send(NetChannel channel, ReadOnlySpan<byte> payload);
        /// <summary>Dequeues the next delivered message, or returns false. Never blocks.</summary>
        bool TryReceive(out NetChannel channel, out byte[] payload);
        void Close();
    }
}
