using NUnit.Framework;

namespace Game.Rendering.Pixel.Tests
{
    /// <summary>T-197b: colour / brightness profile of the destruction cue (engine-free maths).</summary>
    public class DestructionFxLookTests
    {
        // warm_amber_haze-like ascending value ramp (luma), 0..1
        static readonly float[] Ramp = { 0.04f, 0.09f, 0.16f, 0.27f, 0.38f, 0.50f, 0.62f, 0.74f, 0.86f, 0.95f };
        const float TypicalBackground = 0.38f;   // mid-ramp: city / sky haze behind a collapse

        [Test]
        public void Contrast_IsSymmetric_AndOneForEqual()
        {
            Assert.AreEqual(1f, DestructionFxLook.Contrast(0.3f, 0.3f), 1e-6f);
            Assert.AreEqual(DestructionFxLook.Contrast(0.9f, 0.2f), DestructionFxLook.Contrast(0.2f, 0.9f), 1e-6f);
        }

        [Test]
        public void FlashPhase_StepsDownBrightness_ThenOff()
        {
            Assert.AreEqual(DestructionFxLook.PhaseLightest, DestructionFxLook.FlashPhase(0f));
            Assert.AreEqual(DestructionFxLook.PhaseLight, DestructionFxLook.FlashPhase(0.25f));
            Assert.AreEqual(DestructionFxLook.PhaseMid, DestructionFxLook.FlashPhase(0.4f));
            Assert.AreEqual(DestructionFxLook.PhaseOff, DestructionFxLook.FlashPhase(0.6f));
        }

        [Test]
        public void FlashLightestAndLight_MeetMinContrast_AgainstTypicalBackground()
        {
            int a = DestructionFxLook.PickContrasting(Ramp, Ramp.Length - 1, TypicalBackground, DestructionFxLook.MinContrast);
            int b = DestructionFxLook.PickContrasting(Ramp, Ramp.Length - 2, TypicalBackground, DestructionFxLook.MinContrast);
            Assert.GreaterOrEqual(DestructionFxLook.Contrast(Ramp[a], TypicalBackground), DestructionFxLook.MinContrast);
            Assert.GreaterOrEqual(DestructionFxLook.Contrast(Ramp[b], TypicalBackground), DestructionFxLook.MinContrast);
            Assert.AreEqual(Ramp.Length - 1, a);   // already contrasting: the preferred entry is kept
        }

        [Test]
        public void DustDark_MeetsMinContrast_AgainstTypicalBackground()
        {
            int d = DestructionFxLook.PickContrasting(Ramp, 1, TypicalBackground, DestructionFxLook.MinContrast);
            Assert.GreaterOrEqual(DestructionFxLook.Contrast(Ramp[d], TypicalBackground), DestructionFxLook.MinContrast);
        }

        [Test]
        public void Pick_WhenPreferredTooCloseToBackground_MovesAwayFromIt()
        {
            int i = DestructionFxLook.PickContrasting(Ramp, 4, TypicalBackground, DestructionFxLook.MinContrast);
            Assert.AreNotEqual(4, i);
            Assert.GreaterOrEqual(DestructionFxLook.Contrast(Ramp[i], TypicalBackground), DestructionFxLook.MinContrast);
        }

        [Test]
        public void Pick_WhenNoEntryReachesMin_ReturnsHighestContrast()
        {
            var flat = new[] { 0.40f, 0.42f, 0.44f };
            Assert.AreEqual(2, DestructionFxLook.PickContrasting(flat, 1, 0.38f, 5f));
        }

        [Test]
        public void Pick_IsDeterministic_AndRejectsEmptyRamp()
        {
            Assert.AreEqual(DestructionFxLook.PickContrasting(Ramp, 3, 0.5f, 1.4f), DestructionFxLook.PickContrasting(Ramp, 3, 0.5f, 1.4f));
            Assert.Throws<System.ArgumentException>(() => DestructionFxLook.PickContrasting(new float[0], 0, 0.5f, 1.4f));
        }

        [Test]
        public void EmissionGain_IsUnity_SoColoursStayOnTheRamp()
        {
            for (int p = 0; p <= DestructionFxLook.PhaseOff; p++) Assert.AreEqual(1f, DestructionFxLook.EmissionGain(p));
        }
    }
}
