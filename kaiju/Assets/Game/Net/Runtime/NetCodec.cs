using System;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Binary encoding of the handshake messages and the intent packet. The snapshot encoding used inside Accept
    /// is a plain full-precision dump; the quantised/delta snapshot codec is T-029 step 2 and replaces it on the
    /// unreliable channel.
    /// </summary>
    public static class NetCodec
    {
        public const byte MsgHello = 1, MsgAccept = 2, MsgRefuse = 3, MsgLeave = 4;
        /// <summary>Client to host, reliable: the client is leaving; the host frees its slot at once.</summary>
        public static byte[] EncodeLeave() => new byte[] { MsgLeave };

        public static byte[] Encode(HandshakeHello h)
        {
            var w = new ByteWriter();
            w.U8(MsgHello);
            w.I32(h.Protocol);
            w.I32(h.Contracts.Length);
            foreach (var c in h.Contracts) { w.Str(c.Name); w.I32(c.Version); }
            w.Str(h.FlagsCanonical); w.Bytes(h.FlagsHash); w.F64(h.FixedStepSeconds); w.Str(h.BuildVersion);
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, out HandshakeHello h)
        {
            h = null;
            var r = new ByteReader(data);
            if (r.U8() != MsgHello) return false;
            var x = new HandshakeHello { Protocol = r.I32() };
            int n = r.Count(8);
            x.Contracts = new ContractVersion[n];
            for (int i = 0; i < n; i++) { string name = r.Str(); x.Contracts[i] = new ContractVersion(name, r.I32()); }
            x.FlagsCanonical = r.Str(); x.FlagsHash = r.Bytes(64); x.FixedStepSeconds = r.F64(); x.BuildVersion = r.Str();
            if (r.Failed || r.Remaining != 0) return false;
            h = x; return true;
        }

        public static byte[] Encode(HandshakeRefuse f)
        {
            var w = new ByteWriter();
            w.U8(MsgRefuse); w.I32((int)f.Code); w.I32(f.Details.Length);
            foreach (var d in f.Details) w.Str(d);
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, out HandshakeRefuse f)
        {
            f = null;
            var r = new ByteReader(data);
            if (r.U8() != MsgRefuse) return false;
            var x = new HandshakeRefuse { Code = (RefuseCode)r.I32() };
            int n = r.Count(4);
            x.Details = new string[n];
            for (int i = 0; i < n; i++) x.Details[i] = r.Str();
            if (r.Failed || r.Remaining != 0) return false;
            f = x; return true;
        }

        public static byte[] Encode(HandshakeAccept a)
        {
            var w = new ByteWriter();
            w.U8(MsgAccept); w.I32(a.Slot.Index); w.I64(a.HostStep); w.F64(a.FixedStepSeconds);
            WriteSnapshot(w, a.FirstSnapshot);
            w.I32(a.Warnings.Length);
            foreach (var s in a.Warnings) w.Str(s);
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, out HandshakeAccept a)
        {
            a = null;
            var r = new ByteReader(data);
            if (r.U8() != MsgAccept) return false;
            int slot = r.I32();
            if (slot < 0 || slot == int.MaxValue) return false;
            var x = new HandshakeAccept { Slot = PlayerSlot.FromIndex(slot), HostStep = r.I64(), FixedStepSeconds = r.F64() };
            x.FirstSnapshot = ReadSnapshot(r);
            if (x.FirstSnapshot == null) return false;
            int n = r.Count(4);
            x.Warnings = new string[n];
            for (int i = 0; i < n; i++) x.Warnings[i] = r.Str();
            if (r.Failed || r.Remaining != 0) return false;
            a = x; return true;
        }

        private static void WriteSnapshot(ByteWriter w, StateSnapshot s) => w.Bytes(SnapshotCodec.Encode(s));

        private static StateSnapshot ReadSnapshot(ByteReader r)
        {
            var bytes = r.Bytes();
            return SnapshotCodec.TryDecode(bytes, out var s) ? s : null;
        }

        public const int IntentPacketBytes = 4 + 8 + 4 + 8 + IntentPayload.Size;

        public static byte[] Encode(in IntentPacket p)
        {
            var w = new ByteWriter();
            w.I32(p.Slot.Index); w.I64(p.ClientStep); w.U32(p.Sequence); w.I64(p.LastHostStepSeen);
            w.U64(p.Payload.Lo); w.U64(p.Payload.Hi);
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, out IntentPacket p)
        {
            p = default;
            if (data == null || data.Length != IntentPacketBytes) return false;
            var r = new ByteReader(data);
            int slot = r.I32();
            if (slot < 0 || slot == int.MaxValue) return false;
            long cs = r.I64(); uint seq = r.U32(); long hs = r.I64();
            p = new IntentPacket(PlayerSlot.FromIndex(slot), cs, seq, hs, new IntentPayload(r.U64(), r.U64()));
            return !r.Failed;
        }
    }
}
