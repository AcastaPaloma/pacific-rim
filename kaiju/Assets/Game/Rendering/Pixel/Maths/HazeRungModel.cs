using System;
using System.Collections.Generic;

namespace Game.Rendering.Pixel
{
    /// <summary>The tuning keys the haze and ladder model needs, resolved from PaletteProfileTuning.json (defaults, then the profile).</summary>
    public sealed class HazeTuning
    {
        public double HazeStartM = 50, HazeFullM = 15000, HazeCurveGamma = 1, AirlightValue = 1;
        public int HazeBands = 16;
        public double ValueScale = 1.25, ValueBias = -0.25, ValueGamma = 1;
        public double LadderMinStepL = 2.5, LadderHueTolerance = 12, LadderTopL = 0;
        public double AccentChromaStart = 12, AccentChromaFull = 35;

        public static HazeTuning Parse(string tuningJson, string profileId)
        {
            var t = new HazeTuning();
            var root = MiniJson.Parse(tuningJson) as MiniJson.Obj ?? throw new FormatException("tuning: root is not an object");
            if (root.Has("defaults")) Apply(t, root.Object("defaults"));
            if (root.Has("profiles") && root.Object("profiles").Has(profileId)) Apply(t, root.Object("profiles").Object(profileId));
            return t;
        }

        static void Apply(HazeTuning t, MiniJson.Obj o)
        {
            foreach (string k in o.Order)
            {
                if (!(o[k] is double v)) continue;
                switch (k)
                {
                    case "hazeStartM": t.HazeStartM = v; break;
                    case "hazeFullM": t.HazeFullM = v; break;
                    case "hazeCurveGamma": t.HazeCurveGamma = v; break;
                    case "hazeBands": t.HazeBands = (int)v; break;
                    case "airlightValue": t.AirlightValue = v; break;
                    case "valueScale": t.ValueScale = v; break;
                    case "valueBias": t.ValueBias = v; break;
                    case "valueGamma": t.ValueGamma = v; break;
                    case "ladderMinStepL": t.LadderMinStepL = v; break;
                    case "ladderHueTolerance": t.LadderHueTolerance = v; break;
                    case "ladderTopL": t.LadderTopL = v; break;
                    case "accentChromaStart": t.AccentChromaStart = v; break;
                    case "accentChromaFull": t.AccentChromaFull = v; break;
                }
            }
        }
    }

    /// <summary>
    /// Engine-free model of the neutral path of the pixel pipeline (port of research/R-131/pixmodel.py): a grey scene albedo is hazed
    /// (sRGB lerp toward the airlight by a banded amount, <see cref="HazeMath"/>), graded onto the value ladder (<see cref="PaletteLutBaker.Ladder"/>,
    /// the same mixing of two neighbouring rungs as the LUT bake) and the dither is replaced by the nearest rung (a flat face). It predicts what
    /// a distance does to an albedo, so a tuning edit can be checked without Unity (R-131 3.3, T-176).
    /// </summary>
    public sealed class HazeRungModel
    {
        readonly HazeTuning _t;
        readonly Vec3d[] _rungLab, _rungLin;

        /// <summary>Palette indices of the ladder rungs, dark to light.</summary>
        public int[] Rungs { get; }
        /// <summary>Rung lightness L*, dark to light.</summary>
        public double[] RungL { get; }
        public int Bands => _t.HazeBands;

        public HazeRungModel(PaletteProfileData profile, HazeTuning tuning)
        {
            _t = tuning;
            var input = new PaletteLutInput
            {
                Colours = profile.Colours, ValueRamp = profile.RampUniform, AccentIndex = profile.AccentIndex,
                LadderMinStepL = tuning.LadderMinStepL, LadderHueTolerance = tuning.LadderHueTolerance, LadderTopL = tuning.LadderTopL,
            };
            Rungs = PaletteLutBaker.Ladder(input, ColourMath.ToLab(profile.Colours), ColourMath.ToLab(profile.RampUniform));
            if (Rungs.Length < 2) throw new InvalidOperationException("haze model: profile '" + profile.Id + "' has no value ladder");
            _rungLab = new Vec3d[Rungs.Length]; _rungLin = new Vec3d[Rungs.Length]; RungL = new double[Rungs.Length];
            for (int i = 0; i < Rungs.Length; i++)
            {
                _rungLin[i] = ColourMath.ToLinear(profile.Colours[Rungs[i]]);
                _rungLab[i] = ColourMath.LinearToLab(_rungLin[i]);
                RungL[i] = _rungLab[i].X;
            }
        }

        /// <summary>Continuous haze level at a radial distance (HazeMath.Level with the profile's tuning).</summary>
        public double Level(double distanceM) => HazeMath.Level(distanceM, _t.HazeStartM, _t.HazeFullM, _t.HazeBands, _t.HazeCurveGamma);

        /// <summary>Haze amount in [0, 1] of the (undithered) band at a distance.</summary>
        public double Amount(double distanceM) => HazeMath.Amount(HazeMath.Band(Level(distanceM), _t.HazeBands), _t.HazeBands);

        /// <summary>The sRGB grey value after haze: s0 + (airlight - s0) * amount.</summary>
        public double HazedValue(double albedoSrgb, double distanceM) => albedoSrgb + (_t.AirlightValue - albedoSrgb) * Amount(distanceM);

        /// <summary>L* of the grey sRGB value s (equal channels).</summary>
        public static double GreyL(double s) { double l = ColourMath.SrgbToLinear(ColourMath.Clamp01(s)); return ColourMath.LinearToLab(new Vec3d(l, l, l)).X; }

        /// <summary>Grades a grey sRGB value onto the ladder: the rung index of the nearer of the two mixed neighbours, the mix ratio and the nearest rung L*.</summary>
        public void Quantise(double greySrgb, out int rungIndex, out double ratio, out double nearestL)
        {
            double v = ColourMath.Clamp01(GreyL(ColourMath.Clamp01(greySrgb)) / 100.0);
            double p = ColourMath.Clamp01(_t.ValueBias + _t.ValueScale * Math.Pow(v, _t.ValueGamma));
            int n = Rungs.Length; double f = p * (n - 1);
            int i = Math.Min((int)Math.Floor(f), n - 2); double u = f - i;
            Vec3d tgt = ColourMath.LabToLinear(Vec3d.Lerp(_rungLab[i], _rungLab[i + 1], u));
            Vec3d a = _rungLin[i], b = _rungLin[i + 1];
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z, len2 = dx * dx + dy * dy + dz * dz;
            ratio = ColourMath.Clamp01(((tgt.X - a.X) * dx + (tgt.Y - a.Y) * dy + (tgt.Z - a.Z) * dz) / Math.Max(1e-12, len2));
            rungIndex = i + (ratio > 0.5 ? 1 : 0);
            nearestL = RungL[rungIndex];
        }

        /// <summary>Nearest-rung L* of a grey albedo (sRGB) seen through the haze at a distance.</summary>
        public double RenderGrey(double albedoSrgb, double distanceM)
        {
            Quantise(HazedValue(albedoSrgb, distanceM), out _, out _, out double l);
            return l;
        }

        /// <summary>Rung index of a grey albedo at a distance.</summary>
        public int RungAt(double albedoSrgb, double distanceM)
        {
            Quantise(HazedValue(albedoSrgb, distanceM), out int r, out _, out _);
            return r;
        }
    }
}
