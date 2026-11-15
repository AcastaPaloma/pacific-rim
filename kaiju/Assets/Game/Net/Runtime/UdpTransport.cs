using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Game.Net
{
    /// <summary>
    /// Real-socket <see cref="ITransport"/> over UDP (T-030c; the Steam adapter of F-063 replaces it later). One datagram per message:
    /// <c>[kind u8][body]</c>, kind 0 = unreliable payload, 1 = reliable payload with <c>[seq u32]</c> (ordered, resent until acked, duplicates dropped),
    /// 2 = ack <c>[seq u32]</c>, 3 = disconnect. Non-blocking: nothing here sleeps. Retransmission uses its own <see cref="Stopwatch"/> (a transport may have a clock;
    /// the sessions above it never do). Payloads must fit one datagram (<see cref="MaxPayload"/>).
    /// </summary>
    public sealed class UdpPeer : ITransport
    {
        public const int MaxPayload = 1200;
        private const byte KUnreliable = 0, KReliable = 1, KAck = 2, KDisconnect = 3;
        private const long ResendTicks = 60; // ms
        private struct Msg { public NetChannel Channel; public byte[] Data; }
        private sealed class Unacked { public uint Seq; public byte[] Datagram; public long SentMs; }

        private readonly Socket _sock;
        private readonly EndPoint _remote;
        private readonly bool _ownsSocket;
        private readonly Queue<Msg> _in = new Queue<Msg>();
        private readonly List<Unacked> _unacked = new List<Unacked>();
        private readonly SortedDictionary<uint, byte[]> _early = new SortedDictionary<uint, byte[]>();
        private readonly byte[] _rx = new byte[2048];
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly UdpHostListener _listener;
        private uint _sendSeq = 1, _expect = 1;
        private bool _closed, _remoteClosed;

        public long DatagramsSent, DatagramsReceived, Resends;

        internal UdpPeer(Socket sock, EndPoint remote, bool ownsSocket, UdpHostListener listener)
        { _sock = sock; _remote = remote; _ownsSocket = ownsSocket; _listener = listener; }

        /// <summary>Client side: a socket on an ephemeral port aimed at the host. Nothing is sent until the first Send.</summary>
        public static UdpPeer Connect(string host, int port)
        {
            var addr = Dns.GetHostAddresses(host)[0];
            var s = new Socket(addr.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            s.Bind(new IPEndPoint(addr.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0));
            return new UdpPeer(s, new IPEndPoint(addr, port), true, null);
        }

        public IPEndPoint LocalEndPoint => (IPEndPoint)_sock.LocalEndPoint;
        public bool IsConnected => !_closed && !_remoteClosed;
        public int UnackedCount => _unacked.Count;

        public bool Send(NetChannel channel, ReadOnlySpan<byte> payload)
        {
            if (!IsConnected) return false;
            if (payload.Length > MaxPayload) throw new ArgumentException("payload larger than one datagram", nameof(payload));
            byte[] d;
            if (channel == NetChannel.Reliable)
            {
                d = new byte[payload.Length + 5];
                d[0] = KReliable; WriteU32(d, 1, _sendSeq);
                payload.CopyTo(new Span<byte>(d, 5, payload.Length));
                _unacked.Add(new Unacked { Seq = _sendSeq, Datagram = d, SentMs = _clock.ElapsedMilliseconds });
                _sendSeq++;
            }
            else
            {
                d = new byte[payload.Length + 1];
                d[0] = KUnreliable;
                payload.CopyTo(new Span<byte>(d, 1, payload.Length));
            }
            Raw(d);
            return true;
        }

        public bool TryReceive(out NetChannel channel, out byte[] payload)
        {
            if (!_closed) { _listener?.Pump(); if (_listener == null) PumpOwn(); Resend(); }
            if (_in.Count == 0) { channel = default; payload = null; return false; }
            var m = _in.Dequeue(); channel = m.Channel; payload = m.Data; return true;
        }

        public void Close()
        {
            if (_closed) return;
            if (!_remoteClosed) { try { Raw(new byte[] { KDisconnect }); Raw(new byte[] { KDisconnect }); } catch (SocketException) { } }
            _closed = true;
            if (_ownsSocket) _sock.Dispose();
            _listener?.Forget(this);
        }

        internal EndPoint Remote => _remote;

        private void Raw(byte[] d)
        {
            try { _sock.SendTo(d, _remote); DatagramsSent++; } catch (SocketException) { }
        }

        private void Resend()
        {
            if (_unacked.Count == 0) return;
            long now = _clock.ElapsedMilliseconds;
            foreach (var u in _unacked)
                if (now - u.SentMs >= ResendTicks) { u.SentMs = now; Resends++; Raw(u.Datagram); }
        }

        private void PumpOwn()
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_sock.Available > 0)
            {
                int n;
                try { n = _sock.ReceiveFrom(_rx, ref from); } catch (SocketException) { return; }
                OnDatagram(_rx, n);
            }
        }

        internal void OnDatagram(byte[] buf, int n)
        {
            if (n < 1 || _closed) return;
            DatagramsReceived++;
            switch (buf[0])
            {
                case KUnreliable:
                    _in.Enqueue(new Msg { Channel = NetChannel.Unreliable, Data = Slice(buf, 1, n - 1) });
                    break;
                case KReliable:
                {
                    if (n < 5) return;
                    uint seq = ReadU32(buf, 1);
                    var ack = new byte[5]; ack[0] = KAck; WriteU32(ack, 1, seq); Raw(ack);
                    if (unchecked((int)(seq - _expect)) < 0) return;           // already delivered: duplicate
                    if (seq == _expect)
                    {
                        _in.Enqueue(new Msg { Channel = NetChannel.Reliable, Data = Slice(buf, 5, n - 5) }); _expect++;
                        while (_early.TryGetValue(_expect, out var e)) { _early.Remove(_expect); _in.Enqueue(new Msg { Channel = NetChannel.Reliable, Data = e }); _expect++; }
                    }
                    else if (!_early.ContainsKey(seq)) _early[seq] = Slice(buf, 5, n - 5);
                    break;
                }
                case KAck:
                    if (n >= 5) { uint s = ReadU32(buf, 1); _unacked.RemoveAll(u => u.Seq == s); }
                    break;
                case KDisconnect:
                    _remoteClosed = true;
                    break;
            }
        }

        private static byte[] Slice(byte[] b, int off, int len) { var r = new byte[len]; Buffer.BlockCopy(b, off, r, 0, len); return r; }
        private static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        private static uint ReadU32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
    }

    /// <summary>
    /// Host side: one bound UDP socket shared by every client. Each new remote endpoint becomes a <see cref="UdpPeer"/> (fetch it with
    /// <see cref="TryAccept"/> and give it to <c>NetHostSession.AddConnection</c>). Call <see cref="Pump"/> (or any peer's TryReceive) to read the socket.
    /// A remote that disconnected and sends again from the same endpoint starts a fresh peer.
    /// </summary>
    public sealed class UdpHostListener : IDisposable
    {
        private readonly Socket _sock;
        private readonly Dictionary<string, UdpPeer> _peers = new Dictionary<string, UdpPeer>();
        private readonly Queue<UdpPeer> _accepted = new Queue<UdpPeer>();
        private readonly byte[] _rx = new byte[2048];
        public int Port => ((IPEndPoint)_sock.LocalEndPoint).Port;
        public long UnknownDatagrams;

        public UdpHostListener(int port, bool loopbackOnly = true)
        {
            _sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            _sock.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port));
        }

        public bool TryAccept(out UdpPeer peer)
        {
            Pump();
            if (_accepted.Count == 0) { peer = null; return false; }
            peer = _accepted.Dequeue(); return true;
        }

        public void Pump()
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            while (_sock.Available > 0)
            {
                int n;
                try { n = _sock.ReceiveFrom(_rx, ref from); } catch (SocketException) { return; }
                string key = from.ToString();
                if (!_peers.TryGetValue(key, out var p))
                {
                    if (n < 1 || _rx[0] == 3 || _rx[0] == 2) { UnknownDatagrams++; continue; } // a stray disconnect/ack from a forgotten peer
                    p = new UdpPeer(_sock, new IPEndPoint(((IPEndPoint)from).Address, ((IPEndPoint)from).Port), false, this);
                    _peers[key] = p; _accepted.Enqueue(p);
                }
                p.OnDatagram(_rx, n);
            }
        }

        internal void Forget(UdpPeer p) => _peers.Remove(p.Remote.ToString());

        public void Dispose() => _sock.Dispose();
    }
}
