using System;
using Game.Core;

namespace Game.Net
{
    /// <summary>
    /// Unreliable wire message: u8 count (1..3) then that many IntentPacket@v1 records, newest first. A sender puts
    /// the current intent and up to 2 previous ones in each packet so a single lost datagram costs nothing.
    /// </summary>
    public static class IntentBundle
    {
        public const int Repeats = 2;
        public const int MaxPackets = Repeats + 1;

        public static byte[] Encode(ReadOnlySpan<IntentPacket> newestFirst)
        {
            if (newestFirst.Length < 1 || newestFirst.Length > MaxPackets) throw new ArgumentOutOfRangeException(nameof(newestFirst));
            var buf = new byte[1 + newestFirst.Length * NetCodec.IntentPacketBytes];
            buf[0] = (byte)newestFirst.Length;
            for (int i = 0; i < newestFirst.Length; i++)
            {
                var one = NetCodec.Encode(newestFirst[i]);
                Buffer.BlockCopy(one, 0, buf, 1 + i * NetCodec.IntentPacketBytes, one.Length);
            }
            return buf;
        }

        /// <summary>Decodes into <paramref name="newestFirst"/> (length >= MaxPackets). False on any malformation.</summary>
        public static bool TryDecode(byte[] data, IntentPacket[] newestFirst, out int count)
        {
            count = 0;
            if (data == null || data.Length < 1) return false;
            int n = data[0];
            if (n < 1 || n > MaxPackets || data.Length != 1 + n * NetCodec.IntentPacketBytes) return false;
            for (int i = 0; i < n; i++)
            {
                var one = new byte[NetCodec.IntentPacketBytes];
                Buffer.BlockCopy(data, 1 + i * NetCodec.IntentPacketBytes, one, 0, one.Length);
                if (!NetCodec.TryDecode(one, out newestFirst[i])) return false;
            }
            count = n;
            return true;
        }
    }
}
