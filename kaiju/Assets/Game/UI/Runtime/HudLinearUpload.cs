using System;

namespace Game.UI
{
    /// <summary>
    /// T-154b: colour-exact upload for the linear-colour-space project. IMGUI samples the HUD texture raw while the backbuffer sRGB-encodes on write, which
    /// brightened the palette (031117 -> 0c4955). A half-float texture is never sRGB-decoded on sampling, so it carries sRGB-DECODED values: the hardware
    /// encode on write then returns the original palette byte. Pure and engine-free so the round trip is unit-tested for all 256 byte values.
    /// RGB channels are decoded; alpha stays linear (a/255), so alpha 0 and 255 blend as before.
    /// </summary>
    public static class HudLinearUpload
    {
        /// <summary>sRGB byte -> linear value (IEC 61966-2-1 piecewise curve).</summary>
        public static float Decode(int b)
        {
            float c = b / 255f;
            return c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>Linear value -> nearest sRGB byte (what the GPU does on write to an sRGB target).</summary>
        public static byte Encode(float lin)
        {
            if (!(lin > 0f)) return 0;
            if (lin >= 1f) return 255;
            double c = lin <= 0.0031308f ? lin * 12.92 : 1.055 * Math.Pow(lin, 1.0 / 2.4) - 0.055;
            int v = (int)(c * 255.0 + 0.5);
            return (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        }

        /// <summary>IEEE binary16 bits, round to nearest even, for values in [0,1] (no NaN/inf handling needed).</summary>
        public static ushort ToHalf(float f)
        {
            if (!(f > 0f)) return 0;
            if (f >= 1f) return 0x3C00;
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            int exp = ((bits >> 23) & 0xFF) - 127 + 15;
            int man = bits & 0x7FFFFF;
            if (exp <= 0)
            {
                if (exp < -10) return 0;
                man |= 0x800000;
                int shift = 14 - exp;
                int h = man >> shift;
                int rem = man & ((1 << shift) - 1), half = 1 << (shift - 1);
                if (rem > half || (rem == half && (h & 1) == 1)) h++;
                return (ushort)h;
            }
            int r = (exp << 10) | (man >> 13);
            int low = man & 0x1FFF;
            if (low > 0x1000 || (low == 0x1000 && (r & 1) == 1)) r++;
            return (ushort)r;
        }

        public static float FromHalf(ushort h)
        {
            int exp = (h >> 10) & 0x1F, man = h & 0x3FF;
            if (exp == 0) return (float)(man * Math.Pow(2, -24));
            return (float)((1.0 + man / 1024.0) * Math.Pow(2, exp - 15));
        }

        static readonly ushort[] _lut = BuildLut();
        static ushort[] BuildLut() { var t = new ushort[256]; for (int i = 0; i < 256; i++) t[i] = ToHalf(Decode(i)); return t; }

        /// <summary>Half bits of the decoded value for a byte.</summary>
        public static ushort DecodedHalf(byte b) { return _lut[b]; }

        /// <summary>Converts a top-down RGBA32 byte frame into RGBAHalf bits (4 ushorts per pixel); dst must hold src.Length ushorts.</summary>
        public static void ToHalfRgba(byte[] src, ushort[] dst)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (dst == null || dst.Length < src.Length) throw new ArgumentException("dst too small");
            for (int i = 0; i < src.Length; i += 4)
            {
                dst[i] = _lut[src[i]]; dst[i + 1] = _lut[src[i + 1]]; dst[i + 2] = _lut[src[i + 2]];
                dst[i + 3] = ToHalf(src[i + 3] / 255f);
            }
        }
    }
}
