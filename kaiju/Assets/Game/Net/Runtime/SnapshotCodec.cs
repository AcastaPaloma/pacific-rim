using System;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Deterministic binary codec for <see cref="StateSnapshot"/> (StateSnapshot@v1). Little-endian, full precision:
    /// every float is written as its raw IEEE-754 bits, so a round trip is bit-exact (NaN payloads and -0 included)
    /// and the same snapshot always encodes to the same bytes. No quantisation and no delta: at the M-057 estimate
    /// (~40 B per body, 25 Hz) the stream is small, and a lossy codec would break the bit-exact requirement.
    /// Layout: u8 version | i64 hostStep | i32 count | count x (u64 id, 8 x f32). Entities strictly ascending by id.
    /// </summary>
    public static class SnapshotCodec
    {
        public const int HeaderBytes = 1 + 8 + 4;
        public const int EntityBytes = 8 + 8 * 4;

        public static int EncodedSize(StateSnapshot s) => HeaderBytes + s.Entities.Length * EntityBytes;

        public static byte[] Encode(StateSnapshot s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var w = new ByteWriter();
            w.U8((byte)NetContractVersions.StateSnapshot);
            w.I64(s.HostStep);
            w.I32(s.Entities.Length);
            foreach (var e in s.Entities)
            {
                w.U64(e.Id.Value);
                w.F32(e.PosX); w.F32(e.PosY); w.F32(e.PosZ);
                w.F32(e.VelX); w.F32(e.VelY); w.F32(e.VelZ);
                w.F32(e.Yaw); w.F32(e.YawRate);
            }
            return w.ToArray();
        }

        /// <summary>Decodes a snapshot. Rejects wrong version, truncation, trailing bytes, and ids that are not strictly ascending.</summary>
        public static bool TryDecode(byte[] data, out StateSnapshot snapshot)
        {
            snapshot = null;
            var r = new ByteReader(data);
            if (r.U8() != NetContractVersions.StateSnapshot) return false;
            var s = new StateSnapshot { HostStep = r.I64() };
            int n = r.Count(EntityBytes);
            var list = new EntityState[n];
            ulong prev = 0;
            for (int i = 0; i < n; i++)
            {
                ulong id = r.U64();
                if (i > 0 && id <= prev) return false;
                prev = id;
                list[i] = new EntityState(EntityId.FromValue(id), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32(), r.F32());
            }
            if (r.Failed || r.Remaining != 0) return false;
            s.Entities = list;
            snapshot = s;
            return true;
        }
    }
}
