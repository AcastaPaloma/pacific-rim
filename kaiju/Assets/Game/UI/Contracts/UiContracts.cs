using System;

namespace Game.UI
{
    /// <summary>Versions of the UI module's contracts (T-149, R-122 stage 0).</summary>
    public static class UiContractVersions
    {
        public const int LimitsView = 1;
        public const int AccessibilitySettings = 1;
    }

    /// <summary>Feature-flag names (Assets/Game/Config/FeatureFlags/UI.json). All ship false.</summary>
    public static class UiFlags
    {
        public const string Hud = "ui.hud";
        /// <summary>T-150: use the sha-checked hud_colours table instead of the embedded palettes.</summary>
        public const string HudData = "ui.hud_data";
        /// <summary>T-153: load and save settings@v1.</summary>
        public const string Settings = "ui.settings";
    }

    /// <summary>Which D-019 limits a provider actually has state for. Only ammo exists at runtime today (R-122 section 1).</summary>
    [Flags]
    public enum LimitsAvailability
    {
        None = 0,
        Power = 1,
        Heat = 2,
        Ammo = 4,
        Strain = 8,
    }

    /// <summary>Heat state ladder; thresholds in <see cref="LimitsThresholds"/>.</summary>
    public enum HeatState { Nominal = 0, Warm = 1, Overheat = 2, Lockout = 3 }

    /// <summary>Pilot strain ladder; same thresholds as heat.</summary>
    public enum StrainState { Calm = 0, Elevated = 1, Critical = 2, Overload = 3 }

    /// <summary>Assumed thresholds (R-122 4.1), to be tuned with D-019 numbers (Q-008).</summary>
    public static class LimitsThresholds
    {
        public const float Warm = 0.6f;
        public const float Overheat = 0.85f;
        public const float Lockout = 1.0f;

        /// <summary>Ladder index 0..3 for a fraction (clamped to 0..1).</summary>
        public static int Rung(float frac)
        {
            if (!(frac > 0f)) return 0; // also NaN
            if (frac >= Lockout) return 3;
            if (frac >= Overheat) return 2;
            if (frac >= Warm) return 1;
            return 0;
        }

        public static HeatState Heat(float frac) { return (HeatState)Rung(frac); }
        public static StrainState Strain(float frac) { return (StrainState)Rung(frac); }
    }

    /// <summary>
    /// Read-only snapshot of one mech's D-019 limits for one frame. The HUD reads only this, never
    /// simulation internals. Fractions are 0..1 and are clamped by <see cref="Sanitised"/>.
    /// </summary>
    public readonly struct MechLimitsView
    {
        public readonly int SchemaVersion;
        public readonly LimitsAvailability Available;
        public readonly float PowerFrac;
        public readonly float PowerPeakFrac;
        public readonly float HeatFrac;
        public readonly HeatState Heat;
        public readonly int AmmoCount;
        public readonly int AmmoMax;
        public readonly float StrainFrac;
        public readonly StrainState Strain;

        public MechLimitsView(LimitsAvailability available, float powerFrac, float powerPeakFrac, float heatFrac,
            int ammoCount, int ammoMax, float strainFrac)
        {
            SchemaVersion = UiContractVersions.LimitsView;
            Available = available;
            PowerFrac = Clamp01(powerFrac);
            PowerPeakFrac = Clamp01(powerPeakFrac);
            HeatFrac = Clamp01(heatFrac);
            Heat = LimitsThresholds.Heat(HeatFrac);
            AmmoMax = ammoMax < 0 ? 0 : ammoMax;
            AmmoCount = ammoCount < 0 ? 0 : (ammoCount > AmmoMax ? AmmoMax : ammoCount);
            StrainFrac = Clamp01(strainFrac);
            Strain = LimitsThresholds.Strain(StrainFrac);
        }

        /// <summary>Ammo-only view: the only limit with real runtime state today.</summary>
        public static MechLimitsView AmmoOnly(int count, int max)
        {
            return new MechLimitsView(LimitsAvailability.Ammo, 0f, 0f, 0f, count, max, 0f);
        }

        public bool Has(LimitsAvailability a) { return (Available & a) == a; }

        /// <summary>True when every field is already inside its legal range (always true for constructed values).</summary>
        public bool IsValid
        {
            get
            {
                return SchemaVersion == UiContractVersions.LimitsView
                    && InRange(PowerFrac) && InRange(PowerPeakFrac) && InRange(HeatFrac) && InRange(StrainFrac)
                    && AmmoCount >= 0 && AmmoCount <= AmmoMax;
            }
        }

        static bool InRange(float f) { return f >= 0f && f <= 1f; }
        static float Clamp01(float f) { return !(f > 0f) ? 0f : (f > 1f ? 1f : f); }
    }

    /// <summary>Source of limits views. Implementations: ScriptedLimitsProvider (tests, capture); real providers later (T-152, T-155).</summary>
    public interface ILimitsProvider
    {
        /// <summary>False when the provider knows nothing about this mech (HUD hides the block).</summary>
        bool TryGetLimits(int mechId, out MechLimitsView view);
    }

    public enum TextScaleStep { Small = 0, Medium = 1, Large = 2 }

    /// <summary>Accessibility part of the future settings@v1 (R-122 4.3-4.5). Defaults per R-122: text S, shake and flash 100 %.</summary>
    public readonly struct AccessibilitySettings
    {
        public readonly int SchemaVersion;
        public readonly TextScaleStep TextScale;
        public readonly bool HighContrast;
        public readonly bool ReduceBlink;
        public readonly float ShakeGain;
        public readonly float FlashGain;

        public AccessibilitySettings(TextScaleStep textScale, bool highContrast, bool reduceBlink, float shakeGain, float flashGain)
        {
            SchemaVersion = UiContractVersions.AccessibilitySettings;
            TextScale = textScale;
            HighContrast = highContrast;
            ReduceBlink = reduceBlink;
            ShakeGain = Clamp01(shakeGain);
            FlashGain = Clamp01(flashGain);
        }

        public static AccessibilitySettings Default
        {
            get { return new AccessibilitySettings(TextScaleStep.Small, false, false, 1f, 1f); }
        }

        static float Clamp01(float f) { return !(f > 0f) ? 0f : (f > 1f ? 1f : f); }
    }
}
