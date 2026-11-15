using System;
using System.IO;
using Game.Units.Mech;
using Game.Units.Mech.Contracts;
using NUnit.Framework;

namespace Game.Units.Mech.Tests
{
    /// <summary>Round 3 step 8.5: the imp = 2 keys are in the shipped config with neutral defaults, the shipped config keeps imp = 0, and the walk-tuning keys round-trip.</summary>
    public class Imp2ConfigTests
    {
        [Test]
        public void ShippedConfigKeepsImpZeroAndHasTheImp2Keys()
        {
            var t = Repo.Walk();
            Assert.AreEqual(0.0, t.imp);
            Assert.AreEqual(1.0, t.imp2_ff_scale); Assert.AreEqual(0.0, t.imp2_vf_scale);
            Assert.AreEqual(1.0, t.imp2_ff_sign_hip); Assert.AreEqual(1.0, t.imp2_ff_sign_knee); Assert.AreEqual(1.0, t.imp2_ff_sign_ankle);
            string json = File.ReadAllText(Path.Combine(Repo.Root(), "Assets", "Game", "Config", "Mech", "walk_tuning.json"));
            Assert.AreEqual(0.0, t.imp2_phase_sync); Assert.AreEqual(0.0, t.imp2_swing_noclamp); Assert.AreEqual(1.0, t.imp2_swing_gain_scale);
            foreach (var k in new[] { "imp2_phase_sync", "imp2_swing_noclamp", "imp2_swing_gain_scale", "imp2_ff_scale", "imp2_vf_scale", "imp2_ff_sign_hip", "imp2_ff_sign_knee", "imp2_ff_sign_ankle" }) StringAssert.Contains("\"" + k + "\"", json);
        }

        [Test]
        public void TheOptimisedReferenceFileIsLoadableAndValid()
        {
            var d = Repo.Load<GaitReferenceData>("Mech/gait_reference_opt.json");
            Assert.DoesNotThrow(() => d.Validate());
        }

        [Test]
        public void TheScenariosForTheImp2RunsExist()
        {
            string src = File.ReadAllText(Path.Combine(Repo.Root(), "Assets", "Game", "Units", "Mech", "Scenarios", "Scenarios.cs"));
            foreach (var n in new[] { "walk_fr010", "walk_fr015", "walk_fr020" }) StringAssert.Contains("\"" + n + "\"", src);
        }
    }
}
