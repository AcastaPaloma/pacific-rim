using System;
using System.Collections.Generic;

namespace Game.Rendering.Pixel
{
    public enum PaletteRole { Ramp = 0, Body = 1, Accent = 2 }

    /// <summary>One district profile of a palette_set@v1 file (research/R-002 §3). Hex strings are authoritative.</summary>
    public sealed class PaletteProfileData
    {
        public string Id;
        public readonly List<Rgb8> Colours = new List<Rgb8>();
        public readonly List<PaletteRole> Roles = new List<PaletteRole>();
        /// <summary>For ramp colours the ramp position (0 = near-dark), otherwise -1.</summary>
        public readonly List<int> RampSteps = new List<int>();
        /// <summary>ramp_uniform_L: the same curve at uniform L* steps (the renderer's haze ramp, M-012).</summary>
        public readonly List<Rgb8> RampUniform = new List<Rgb8>();
        public int AccentIndex = -1;
        public string Sha256;
    }

    /// <summary>Reads palette_set@v1 (schema in research/R-002.md §3).</summary>
    public static class PaletteSetReader
    {
        public static List<PaletteProfileData> Read(string json)
        {
            var root = MiniJson.Parse(json) as MiniJson.Obj ?? throw new FormatException("palette set: root is not an object");
            if (!root.Has("schema") || root.Str("schema") != PixelContractVersions.PaletteSet)
                throw new FormatException("palette set: schema must be " + PixelContractVersions.PaletteSet);
            var profiles = root.Object("profiles");
            var result = new List<PaletteProfileData>();
            foreach (string id in profiles.Order)
            {
                var src = profiles.Object(id);
                var p = new PaletteProfileData { Id = id };
                foreach (object entry in src.Array("colours"))
                {
                    var c = (MiniJson.Obj)entry;
                    string role = c.Str("role");
                    p.Colours.Add(Rgb8.FromHex(c.Str("hex").ToLowerInvariant()));
                    if (role.StartsWith("ramp_", StringComparison.Ordinal)) { p.Roles.Add(PaletteRole.Ramp); p.RampSteps.Add(int.Parse(role.Substring(5))); }
                    else if (role == "accent") { p.Roles.Add(PaletteRole.Accent); p.RampSteps.Add(-1); if (p.AccentIndex < 0) p.AccentIndex = p.Colours.Count - 1; }
                    else if (role == "body") { p.Roles.Add(PaletteRole.Body); p.RampSteps.Add(-1); }
                    else throw new FormatException("palette set: profile '" + id + "' has unknown role '" + role + "'");
                }
                if (p.Colours.Count == 0) throw new FormatException("palette set: profile '" + id + "' has no colours");
                if (src.Has("ramp_uniform_L"))
                    foreach (object entry in src.Array("ramp_uniform_L")) p.RampUniform.Add(Rgb8.FromHex(((MiniJson.Obj)entry).Str("hex").ToLowerInvariant()));
                if (p.RampUniform.Count < 2)
                {
                    // No uniform ramp in the data: fall back to the declared ramp colours in ramp order.
                    var ramp = new SortedDictionary<int, Rgb8>();
                    for (int i = 0; i < p.Colours.Count; i++) if (p.Roles[i] == PaletteRole.Ramp) ramp[p.RampSteps[i]] = p.Colours[i];
                    p.RampUniform.Clear(); p.RampUniform.AddRange(ramp.Values);
                }
                if (p.RampUniform.Count < 2) throw new FormatException("palette set: profile '" + id + "' has no ramp");
                p.Sha256 = ColourMath.PaletteSha256(p.Colours);
                result.Add(p);
            }
            return result;
        }

        /// <summary>M-012: coefficient of variation of the L* steps between consecutive ramp colours.</summary>
        public static double RampStepCv(IReadOnlyList<Rgb8> ramp)
        {
            if (ramp.Count < 3) return 0;
            var steps = new double[ramp.Count - 1]; double mean = 0;
            for (int i = 0; i < steps.Length; i++) { steps[i] = ColourMath.ToLab(ramp[i + 1]).X - ColourMath.ToLab(ramp[i]).X; mean += steps[i]; }
            mean /= steps.Length;
            if (Math.Abs(mean) < 1e-9) return double.PositiveInfinity;
            double var = 0;
            foreach (double s in steps) var += (s - mean) * (s - mean);
            return Math.Sqrt(var / steps.Length) / Math.Abs(mean);
        }
    }
}
