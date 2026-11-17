using System;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// T-197b: engine-free colour / brightness profile of the destruction cue (PixelDestructionFx). The cue is drawn EMISSIVE (base colour black, emission = the
    /// palette-ramp colour), so scene lighting and shading add nothing and the colour entering the pixel pipeline is exactly the ramp colour; haze and the
    /// quantiser then act on a known value. Pure maths so the profile (which ramp entry per phase, minimum luminance contrast against the typical background)
    /// is testable without Unity. No palette or threshold change: only ramp entries and the accent are used.
    /// </summary>
    public static class DestructionFxLook
    {
        /// <summary>Minimum luminance contrast ratio ((hi + 0.05) / (lo + 0.05)) a cue colour must have against the typical background.</summary>
        public const float MinContrast = 1.6f;
        /// <summary>Collapse flash phases by age (s): lightest ramp entry, then light, then mid, then off.</summary>
        public const float FlashLightestUntilS = 0.12f, FlashLightUntilS = 0.26f, FlashMidUntilS = 0.40f;
        public const int PhaseLightest = 0, PhaseLight = 1, PhaseMid = 2, PhaseOff = 3;

        public static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

        public static float Contrast(float lumaA, float lumaB)
        {
            float hi = Math.Max(lumaA, lumaB), lo = Math.Min(lumaA, lumaB);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        public static int FlashPhase(float ageS) => ageS < FlashLightestUntilS ? PhaseLightest : ageS < FlashLightUntilS ? PhaseLight : ageS < FlashMidUntilS ? PhaseMid : PhaseOff;

        /// <summary>
        /// Index into an ascending-luma ramp of the entry for a cue part: the nearest entry to <paramref name="preferred"/> (moving away from the background)
        /// whose contrast against <paramref name="backgroundLuma"/> reaches <paramref name="minContrast"/>; if none does, the entry with the highest contrast.
        /// </summary>
        public static int PickContrasting(float[] rampLuma, int preferred, float backgroundLuma, float minContrast)
        {
            if (rampLuma == null || rampLuma.Length == 0) throw new ArgumentException("empty ramp", nameof(rampLuma));
            int n = rampLuma.Length; preferred = Math.Max(0, Math.Min(n - 1, preferred));
            int best = 0; float bestC = -1f;
            for (int d = 0; d < n; d++)
                for (int s = -1; s <= 1; s += 2)
                {
                    int i = preferred + s * d; if (i < 0 || i >= n) continue;
                    float c = Contrast(rampLuma[i], backgroundLuma);
                    if (c >= minContrast) return i;
                    if (c > bestC) { bestC = c; best = i; }
                }
            return best;
        }

        /// <summary>Emission gain applied to a ramp colour (kept at 1: the pipeline's tone range is LDR, over-driving only clips toward white, off the ramp).</summary>
        public static float EmissionGain(int phase) => 1.0f;
    }
}
