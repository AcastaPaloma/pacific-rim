using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Game.UI
{
    /// <summary>Colour-vision-deficiency simulation and dE: same maths as Tools/ui/hud_colours.py (Machado 2009, severity 1.0, linear RGB; CIE76 in L*a*b*, D65).</summary>
    public static class CvdMath
    {
        public enum Kind { Normal = 0, Protan = 1, Deutan = 2, Tritan = 3 }

        static readonly double[][] Protan = { new[] { 0.152286, 1.052583, -0.204868 }, new[] { 0.114503, 0.786281, 0.099216 }, new[] { -0.003882, -0.048116, 1.051998 } };
        static readonly double[][] Deutan = { new[] { 0.367322, 0.860646, -0.227968 }, new[] { 0.280085, 0.672501, 0.047413 }, new[] { -0.011820, 0.042940, 0.968881 } };
        static readonly double[][] Tritan = { new[] { 1.255528, -0.076749, -0.178779 }, new[] { -0.078411, 0.930809, 0.147602 }, new[] { 0.004733, 0.691367, 0.303900 } };

        static double Lin(double c8)
        {
            double c = c8 / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Enc(double l)
        {
            if (l < 0) l = 0; else if (l > 1) l = 1;
            return (l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055) * 255.0;
        }

        /// <summary>Simulated colour as float RGB 0..255 (not rounded, like the Python tool).</summary>
        public static void Simulate(Rgb c, Kind kind, out double r, out double g, out double b)
        {
            double lr = Lin(c.R), lg = Lin(c.G), lb = Lin(c.B);
            if (kind == Kind.Normal) { r = c.R; g = c.G; b = c.B; return; }
            var m = kind == Kind.Protan ? Protan : (kind == Kind.Deutan ? Deutan : Tritan);
            r = Enc(m[0][0] * lr + m[0][1] * lg + m[0][2] * lb);
            g = Enc(m[1][0] * lr + m[1][1] * lg + m[1][2] * lb);
            b = Enc(m[2][0] * lr + m[2][1] * lg + m[2][2] * lb);
        }

        static void Lab(double r8, double g8, double b8, out double L, out double a, out double bb)
        {
            double r = Lin(r8), g = Lin(g8), b = Lin(b8);
            double x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
            double y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
            double fx = F(x), fy = F(y), fz = F(z);
            L = 116 * fy - 16; a = 500 * (fx - fy); bb = 200 * (fy - fz);
        }

        static double F(double t) { return t > 216.0 / 24389.0 ? Math.Pow(t, 1.0 / 3.0) : (24389.0 / 27.0 * t + 16) / 116; }

        public static double DeltaE76(Rgb a, Rgb b, Kind kind)
        {
            double r1, g1, b1, r2, g2, b2;
            Simulate(a, kind, out r1, out g1, out b1);
            Simulate(b, kind, out r2, out g2, out b2);
            double L1, a1, c1, L2, a2, c2;
            Lab(r1, g1, b1, out L1, out a1, out c1);
            Lab(r2, g2, b2, out L2, out a2, out c2);
            double dl = L1 - L2, da = a1 - a2, db = c1 - c2;
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        /// <summary>Minimum dE76 over normal + protan + deutan + tritan.</summary>
        public static double WorstDeltaE(Rgb a, Rgb b)
        {
            double w = double.MaxValue;
            for (int k = 0; k < 4; k++) { double d = DeltaE76(a, b, (Kind)k); if (d < w) w = d; }
            return w;
        }

        /// <summary>Minimum worst-case dE over all pairs of the colours (what M-NEW-a measures per profile).</summary>
        public static double MinPairwiseWorstDeltaE(IList<Rgb> colours)
        {
            double w = double.MaxValue;
            for (int i = 0; i < colours.Count; i++)
                for (int j = i + 1; j < colours.Count; j++)
                { double d = WorstDeltaE(colours[i], colours[j]); if (d < w) w = d; }
            return w;
        }
    }

    public sealed class HudColourEntry
    {
        public string Profile;
        public Rgb Panel, Text;
        public Rgb[] Fills;
        public string ProfileSha256;
        public double FillsMinDe;
    }

    public sealed class HudColourTableException : Exception
    {
        public HudColourTableException(string m) : base(m) { }
    }

    /// <summary>
    /// The committed per-profile `hud_colours` table (Assets/Game/Config/UI/hud_colours.json), sha-checked like the profiles:
    /// `table_sha256` covers panel, text, fills and each profile's own sha. Replaces the embedded <see cref="HudPalette.All"/> when the
    /// `ui.hud_data` flag is on. Built and verified against the profile assets by Tools/ui/hud_colours.py.
    /// </summary>
    public sealed class HudColourTable
    {
        public readonly IReadOnlyList<HudColourEntry> Entries;
        public readonly string TableSha256;

        HudColourTable(List<HudColourEntry> e, string sha) { Entries = e; TableSha256 = sha; }

        public static string ComputeSha(IEnumerable<HudColourEntry> entries)
        {
            var list = new List<HudColourEntry>(entries);
            list.Sort((a, b) => string.CompareOrdinal(a.Profile, b.Profile));
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (i > 0) sb.Append('\n');
                sb.Append(e.Profile).Append(':').Append(e.Panel).Append(':').Append(e.Text).Append(':');
                for (int f = 0; f < e.Fills.Length; f++) { if (f > 0) sb.Append(','); sb.Append(e.Fills[f]); }
                sb.Append(':').Append(e.ProfileSha256);
            }
            using (var h = SHA256.Create())
            {
                var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++) hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }

        /// <summary>Parses and sha-verifies. Throws <see cref="HudColourTableException"/> on any mismatch (callers fall back to the embedded palette).</summary>
        public static HudColourTable Parse(string json)
        {
            object root;
            if (!MiniJson.TryParse(json, out root)) throw new HudColourTableException("not valid json");
            var o = MiniJson.Obj(root);
            if (o == null || MiniJson.Int(o, "schema", 0) != 1) throw new HudColourTableException("unsupported schema");
            var profiles = MiniJson.Obj(MiniJson.Get(o, "profiles"));
            if (profiles == null || profiles.Count == 0) throw new HudColourTableException("no profiles");
            var list = new List<HudColourEntry>();
            foreach (var kv in profiles)
            {
                var p = MiniJson.Obj(kv.Value);
                if (p == null) throw new HudColourTableException("profile " + kv.Key + " is not an object");
                var fills = MiniJson.Arr(MiniJson.Get(p, "fills"));
                if (fills == null || fills.Count == 0) throw new HudColourTableException("profile " + kv.Key + " has no fills");
                var e = new HudColourEntry { Profile = kv.Key, Panel = Hex(MiniJson.Str(p, "panel", null), kv.Key), Text = Hex(MiniJson.Str(p, "text", null), kv.Key), Fills = new Rgb[fills.Count], ProfileSha256 = MiniJson.Str(p, "profile_sha256", "") };
                for (int i = 0; i < fills.Count; i++) e.Fills[i] = Hex(fills[i] as string, kv.Key);
                object d = MiniJson.Get(p, "fills_min_de");
                e.FillsMinDe = d is double ? (double)d : double.NaN;
                list.Add(e);
            }
            string want = MiniJson.Str(o, "table_sha256", "");
            string got = ComputeSha(list);
            if (!string.Equals(want, got, StringComparison.Ordinal)) throw new HudColourTableException("table sha mismatch: file " + want + " computed " + got);
            return new HudColourTable(list, got);
        }

        static Rgb Hex(string s, string profile)
        {
            if (s == null || s.Length != 6) throw new HudColourTableException("bad colour in " + profile);
            int v;
            if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v))
                throw new HudColourTableException("bad colour '" + s + "' in " + profile);
            return Rgb.FromHex(v);
        }

        public HudPalette Find(string profile)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Profile == profile) return new HudPalette(profile, Entries[i].Panel, Entries[i].Text, Entries[i].Fills);
            return null;
        }

        /// <summary>Flag-gated palette choice: table when <paramref name="flagOn"/> and the table has the profile, else the embedded one.</summary>
        public static HudPalette Resolve(string profile, bool flagOn, HudColourTable table)
        {
            if (flagOn && table != null) { var p = table.Find(profile); if (p != null) return p; }
            return HudPalette.Find(profile);
        }
    }
}
