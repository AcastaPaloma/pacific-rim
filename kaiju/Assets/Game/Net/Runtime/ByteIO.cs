using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Net
{
    /// <summary>Little-endian binary writer over a growable buffer.</summary>
    public sealed class ByteWriter
    {
        private byte[] _buf = new byte[64];
        private int _len;
        public int Length => _len;
        public byte[] ToArray() { var a = new byte[_len]; Buffer.BlockCopy(_buf, 0, a, 0, _len); return a; }

        private void Ensure(int n)
        {
            if (_len + n <= _buf.Length) return;
            int cap = Math.Max(_buf.Length * 2, _len + n);
            Array.Resize(ref _buf, cap);
        }
        public void U8(byte v) { Ensure(1); _buf[_len++] = v; }
        public void U32(uint v) { Ensure(4); for (int i = 0; i < 4; i++) _buf[_len++] = (byte)(v >> (8 * i)); }
        public void I32(int v) => U32(unchecked((uint)v));
        public void U64(ulong v) { Ensure(8); for (int i = 0; i < 8; i++) _buf[_len++] = (byte)(v >> (8 * i)); }
        public void I64(long v) => U64(unchecked((ulong)v));
        public void F32(float v) => U32(unchecked((uint)BitConverter.SingleToInt32Bits(v)));
        public void F64(double v) => I64(BitConverter.DoubleToInt64Bits(v));
        public void Bytes(byte[] b) { b = b ?? Array.Empty<byte>(); I32(b.Length); Ensure(b.Length); Buffer.BlockCopy(b, 0, _buf, _len, b.Length); _len += b.Length; }
        public void Str(string s) => Bytes(s == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(s));
    }

    /// <summary>Little-endian reader. Any overrun or bad length sets <see cref="Failed"/> and returns defaults, never throws.</summary>
    public sealed class ByteReader
    {
        private readonly byte[] _b;
        private int _pos;
        public bool Failed { get; private set; }
        public int Remaining => _b.Length - _pos;
        public ByteReader(byte[] data) { _b = data ?? Array.Empty<byte>(); }

        private bool Take(int n)
        {
            if (Failed || n < 0 || _b.Length - _pos < n) { Failed = true; return false; }
            return true;
        }
        public byte U8() => Take(1) ? _b[_pos++] : (byte)0;
        public uint U32() { if (!Take(4)) return 0; uint v = 0; for (int i = 0; i < 4; i++) v |= (uint)_b[_pos++] << (8 * i); return v; }
        public int I32() => unchecked((int)U32());
        public ulong U64() { if (!Take(8)) return 0; ulong v = 0; for (int i = 0; i < 8; i++) v |= (ulong)_b[_pos++] << (8 * i); return v; }
        public long I64() => unchecked((long)U64());
        public float F32() => BitConverter.Int32BitsToSingle(I32());
        public double F64() => BitConverter.Int64BitsToDouble(I64());
        public byte[] Bytes(int maxLen = 1 << 20)
        {
            int n = I32();
            if (n < 0 || n > maxLen || !Take(n)) { Failed = true; return Array.Empty<byte>(); }
            var r = new byte[n]; Buffer.BlockCopy(_b, _pos, r, 0, n); _pos += n; return r;
        }
        public string Str() => Encoding.UTF8.GetString(Bytes());
        /// <summary>A count that must not exceed what the remaining bytes could hold at <paramref name="minBytesEach"/>.</summary>
        public int Count(int minBytesEach)
        {
            int n = I32();
            if (n < 0 || (long)n * minBytesEach > Remaining) { Failed = true; return 0; }
            return n;
        }
    }
}
