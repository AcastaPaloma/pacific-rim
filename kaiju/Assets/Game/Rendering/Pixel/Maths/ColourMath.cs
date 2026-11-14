using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Game.Rendering.Pixel
{
    /// <summary>An 8-bit sRGB colour. The hex string of a palette entry is authoritative (R-002 §3).</summary>
    public readonly struct Rgb8 : IEquatable<Rgb8>
    {
        public readonly byte R, G, B;
        public Rgb8(byte r, byte g, byte b) { R = r; G = g; B = b; }

        public static Rgb8 FromHex(string hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#') throw new FormatException("expected #rrggbb, got '" + hex + "'");
            return new Rgb8(Byte(hex, 1), Byte(hex, 3), Byte(hex, 5));
        }

        static byte Byte(string s, int i) => byte.Parse(s.Substring(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

        /// <summary>Lowercase #rrggbb.</summary>
        public string ToHex() => "#" + R.ToString("x2", CultureInfo.InvariantCulture) + G.ToString("x2", CultureInfo.InvariantCulture) + B.ToString("x2", CultureInfo.InvariantCulture);
        public bool Equals(Rgb8 other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is Rgb8 o && Equals(o);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
        public override string ToString() => ToHex();
    }

    /// <summary>A colour as three doubles: linear RGB, CIELAB (L*, a*, b*) or OKLab (L, a, b) depending on context.</summary>
    public readonly struct Vec3d
    {
        public readonly double X, Y, Z;
        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => new Vec3d(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        public static double Distance(Vec3d a, Vec3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    /// <summary>
    /// Engine-free colour maths: sRGB, CIELAB (D65/2°), OKLab, CIEDE2000 (Sharma, Wu, Dalal 2005), nearest palette
    /// colour and the canonical palette hash. M-003 measures adherence in CIEDE2000; the LUT maps in OKLab (R-003 §3).
    /// </summary>
    public static class ColourMath
    {
        public static double SrgbToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        public static double LinearToSrgb(double c) => c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;

        public static Vec3d ToLinear(Rgb8 c) => new Vec3d(SrgbToLinear(c.R / 255.0), SrgbToLinear(c.G / 255.0), SrgbToLinear(c.B / 255.0));
        public static Vec3d SrgbToLinear(Vec3d srgb) => new Vec3d(SrgbToLinear(srgb.X), SrgbToLinear(srgb.Y), SrgbToLinear(srgb.Z));

        public static Rgb8 ToRgb8(Vec3d linear) => new Rgb8(ToByte(LinearToSrgb(Clamp01(linear.X))), ToByte(LinearToSrgb(Clamp01(linear.Y))), ToByte(LinearToSrgb(Clamp01(linear.Z))));
        static byte ToByte(double v) => (byte)Math.Min(255, Math.Max(0, (int)Math.Floor(v * 255.0 + 0.5)));
        public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        // D65 reference white, sRGB primaries (IEC 61966-2-1).
        const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

        public static Vec3d LinearToLab(Vec3d c)
        {
            double x = 0.4124564 * c.X + 0.3575761 * c.Y + 0.1804375 * c.Z;
            double y = 0.2126729 * c.X + 0.7151522 * c.Y + 0.0721750 * c.Z;
            double z = 0.0193339 * c.X + 0.1191920 * c.Y + 0.9503041 * c.Z;
            double fx = LabF(x / Xn), fy = LabF(y / Yn), fz = LabF(z / Zn);
            return new Vec3d(116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
        }

        public static Vec3d LabToLinear(Vec3d lab)
        {
            double fy = (lab.X + 16.0) / 116.0, fx = fy + lab.Y / 500.0, fz = fy - lab.Z / 200.0;
            double x = Xn * LabFInv(fx), y = Yn * LabFInv(fy), z = Zn * LabFInv(fz);
            return new Vec3d(
                3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
                -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
                0.0556434 * x - 0.2040259 * y + 1.0572252 * z);
        }

        static double LabF(double t) => t > 216.0 / 24389.0 ? Math.Pow(t, 1.0 / 3.0) : (24389.0 / 27.0 * t + 16.0) / 116.0;
        static double LabFInv(double f) { double f3 = f * f * f; return f3 > 216.0 / 24389.0 ? f3 : (116.0 * f - 16.0) / (24389.0 / 27.0); }

        public static Vec3d ToLab(Rgb8 c) => LinearToLab(ToLinear(c));

        /// <summary>OKLab (Ottosson 2020) from linear sRGB.</summary>
        public static Vec3d LinearToOklab(Vec3d c)
        {
            double l = 0.4122214708 * c.X + 0.5363325363 * c.Y + 0.0514459929 * c.Z;
            double m = 0.2119034982 * c.X + 0.6806995451 * c.Y + 0.1073969566 * c.Z;
            double s = 0.0883024619 * c.X + 0.2817188376 * c.Y + 0.6299787005 * c.Z;
            l = Cbrt(l); m = Cbrt(m); s = Cbrt(s);
            return new Vec3d(
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
        }

        static double Cbrt(double v) => v < 0 ? -Math.Pow(-v, 1.0 / 3.0) : Math.Pow(v, 1.0 / 3.0);

        /// <summary>CIEDE2000 colour difference between two CIELAB colours (kL = kC = kH = 1).</summary>
        public static double DeltaE2000(Vec3d lab1, Vec3d lab2)
        {
            double L1 = lab1.X, a1 = lab1.Y, b1 = lab1.Z, L2 = lab2.X, a2 = lab2.Y, b2 = lab2.Z;
            double c1 = Math.Sqrt(a1 * a1 + b1 * b1), c2 = Math.Sqrt(a2 * a2 + b2 * b2);
            double cBar = (c1 + c2) / 2.0;
            double cBar7 = Math.Pow(cBar, 7);
            double g = 0.5 * (1.0 - Math.Sqrt(cBar7 / (cBar7 + 6103515625.0))); // 25^7
            double a1p = (1.0 + g) * a1, a2p = (1.0 + g) * a2;
            double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
            double h1p = Hue(b1, a1p), h2p = Hue(b2, a2p);
            double dLp = L2 - L1, dCp = c2p - c1p;
            double dhp;
            if (c1p * c2p == 0) dhp = 0;
            else
            {
                dhp = h2p - h1p;
                if (dhp > 180) dhp -= 360; else if (dhp < -180) dhp += 360;
            }
            double dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dhp / 2.0));
            double lBarP = (L1 + L2) / 2.0, cBarP = (c1p + c2p) / 2.0;
            double hBarP;
            if (c1p * c2p == 0) hBarP = h1p + h2p;
            else if (Math.Abs(h1p - h2p) <= 180) hBarP = (h1p + h2p) / 2.0;
            else hBarP = (h1p + h2p < 360) ? (h1p + h2p + 360) / 2.0 : (h1p + h2p - 360) / 2.0;
            double t = 1.0 - 0.17 * Math.Cos(Rad(hBarP - 30)) + 0.24 * Math.Cos(Rad(2 * hBarP)) + 0.32 * Math.Cos(Rad(3 * hBarP + 6)) - 0.20 * Math.Cos(Rad(4 * hBarP - 63));
            double dTheta = 30.0 * Math.Exp(-Math.Pow((hBarP - 275) / 25.0, 2));
            double cBarP7 = Math.Pow(cBarP, 7);
            double rc = 2.0 * Math.Sqrt(cBarP7 / (cBarP7 + 6103515625.0));
            double l50 = (lBarP - 50) * (lBarP - 50);
            double sl = 1.0 + 0.015 * l50 / Math.Sqrt(20.0 + l50);
            double sc = 1.0 + 0.045 * cBarP;
            double sh = 1.0 + 0.015 * cBarP * t;
            double rt = -Math.Sin(Rad(2 * dTheta)) * rc;
            double tl = dLp / sl, tc = dCp / sc, th = dHp / sh;
            return Math.Sqrt(tl * tl + tc * tc + th * th + rt * tc * th);
        }

        static double Hue(double b, double ap)
        {
            if (b == 0 && ap == 0) return 0;
            double h = Math.Atan2(b, ap) * 180.0 / Math.PI;
            return h < 0 ? h + 360 : h;
        }

        static double Rad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Index of the palette colour with the smallest CIEDE2000 distance to <paramref name="lab"/> (first wins on ties).</summary>
        public static int NearestIndex(IReadOnlyList<Vec3d> paletteLab, Vec3d lab, out double deltaE)
        {
            int best = -1; deltaE = double.MaxValue;
            for (int i = 0; i < paletteLab.Count; i++)
            {
                double d = DeltaE2000(paletteLab[i], lab);
                if (d < deltaE) { deltaE = d; best = i; }
            }
            return best;
        }

        public static Vec3d[] ToLab(IReadOnlyList<Rgb8> colours)
        {
            var lab = new Vec3d[colours.Count];
            for (int i = 0; i < colours.Count; i++) lab[i] = ToLab(colours[i]);
            return lab;
        }

        /// <summary>
        /// Canonical palette hash (orchestrator rule): SHA-256 of the UTF-8 bytes of the colour list as compact JSON,
        /// lowercase "#rrggbb" strings in declared order with no spaces, e.g. ["#33291c","#46331f"].
        /// </summary>
        public static string PaletteSha256(IReadOnlyList<Rgb8> colours)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < colours.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(colours[i].ToHex()).Append('"');
            }
            sb.Append(']');
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }
    }
}
