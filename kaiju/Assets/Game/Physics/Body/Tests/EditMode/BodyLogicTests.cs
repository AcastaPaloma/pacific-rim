using System;
using System.IO;
using System.Numerics;
using Game.Physics.Body;
using Game.Physics.Body.Contracts;
using NUnit.Framework;

namespace Game.Physics.Body.Tests
{
    /// <summary>Loads the committed config files in both runners: UnityEngine.JsonUtility under Unity, System.Text.Json under dotnet test.</summary>
    public static class TestConfig
    {
        public static string ConfigDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 12 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(Path.Combine(dir, "Assets", "Game", "Config"))) return Path.Combine(dir, "Assets", "Game", "Config");
            dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 12 && dir != null; i++, dir = Path.GetDirectoryName(dir))
                if (Directory.Exists(Path.Combine(dir, "Assets", "Game", "Config"))) return Path.Combine(dir, "Assets", "Game", "Config");
            throw new DirectoryNotFoundException("Assets/Game/Config not found above " + AppContext.BaseDirectory);
        }
        public static T Load<T>(string relative)
        {
            string json = File.ReadAllText(Path.Combine(ConfigDir(), relative));
#if UNITY_5_3_OR_NEWER
            return UnityEngine.JsonUtility.FromJson<T>(json);
#else
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
#endif
        }
        public static MechFrameData Frame() => Load<MechFrameData>("Mech/mech_frame_balanced.json");
        public static BodyTuning Tuning() => Load<BodyTuning>("Mech/body_tuning.json");
    }

    public class FrameTests
    {
        [Test]
        public void Frame_Loads_Validates_And_MassesSumToTotal()
        {
            var f = TestConfig.Frame();
            MechFrameOps.Validate(f);
            Assert.That(f.segments.Length, Is.EqualTo(15));
            Assert.That(MechFrameOps.TotalMass(f), Is.EqualTo(f.total_mass_kg).Within(1e-3 * f.total_mass_kg), "C12: link masses sum to the config total within 0.1 %");
            var com = MechFrameOps.Com(f);
            Assert.That(com[1], Is.EqualTo(f.com_rest[1]).Within(0.01));
            Assert.That(MechFrameOps.LegLength(f), Is.EqualTo(42.0).Within(1e-6));
            Assert.That(MechFrameOps.FootSegments(f).Count, Is.EqualTo(2));
        }

        [Test]
        public void Frame_UnknownSchema_IsRefused()
        {
            var f = TestConfig.Frame(); f.schema = "mech_frame@v2";
            Assert.Throws<InvalidOperationException>(() => MechFrameOps.Validate(f));
        }

        [TestCase(76.0, 6.0e6)]
        [TestCase(88.0, 1.5e7)]
        [TestCase(80.0, 1.0e7)]
        public void Scale_FollowsR005Rule(double height, double mass)
        {
            var f = TestConfig.Frame(); var g = MechFrameOps.Scale(f, height, mass);
            MechFrameOps.Validate(g);
            double kl = height / 80.0, km = mass / 1e7;
            Assert.That(MechFrameOps.TotalMass(g), Is.EqualTo(mass).Within(1e-6 * mass));
            Assert.That(g.foot_sole.area_m2, Is.EqualTo(150.0 * kl * kl).Within(1e-6));
            Assert.That(g.segments[1].inertia_principal_kgm2[0], Is.EqualTo(f.segments[1].inertia_principal_kgm2[0] * km * kl * kl).Within(1.0));
            Assert.That(g.joints[0].dofs[0].rom_deg[1], Is.EqualTo(f.joints[0].dofs[0].rom_deg[1]), "ROM unchanged");
            Assert.That(f.total_height_m, Is.EqualTo(80.0), "scaling returns a copy");
        }

        [Test]
        public void EffectiveInertias_MatchR005Table()
        {
            var f = TestConfig.Frame();
            Assert.That(MechFrameOps.DistalInertia(f, MechFrameOps.JointOfChild(f, "thigh_L"), 0), Is.EqualTo(1.03e9).Within(0.03e9), "hip flexion, swing leg");
            Assert.That(MechFrameOps.DistalInertia(f, MechFrameOps.JointOfChild(f, "shank_L"), 0), Is.EqualTo(1.90e8).Within(0.05e8), "knee, swing");
            Assert.That(MechFrameOps.DistalInertia(f, MechFrameOps.JointOfChild(f, "torso"), 0), Is.EqualTo(8.41e8).Within(0.2e8), "waist flexion");
            Assert.That(MechFrameOps.ProximalInertia(f, MechFrameOps.JointOfChild(f, "foot_L"), 0), Is.EqualTo(1.85e10).Within(0.1e10), "stance ankle: body above the foot");
        }

        [Test]
        public void GainRule_IsKEqualsIOmegaSquared_AndScalesWithInertia()
        {
            Assert.That(GainRules.Stiffness(1.03e9, 1.0), Is.EqualTo(4.07e10).Within(0.05e10), "R-005 3.2: hip swing at 1 Hz");
            Assert.That(GainRules.Damping(1.03e9, 1.0, 1.0), Is.EqualTo(1.29e10).Within(0.02e10));
            Assert.That(GainRules.Stiffness(2e9, 0.5) / GainRules.Stiffness(1e9, 0.5), Is.EqualTo(2.0).Within(1e-12), "gains follow inertia: no hand tuning per mass");
        }

        [Test]
        public void Tuning_Loads_AndScalesLimitsWithMassTimesHeight()
        {
            var t = TestConfig.Tuning();
            Assert.That(t.schema, Is.EqualTo(BodyTuning.Schema));
            Assert.That(t.linear_damping, Is.EqualTo(0)); Assert.That(t.angular_damping, Is.EqualTo(0)); Assert.That(t.joint_friction, Is.EqualTo(0));
            Assert.That(t.contact_on_bw, Is.EqualTo(0.03)); Assert.That(t.contact_off_bw, Is.EqualTo(0.01));
            var s = t.ScaledFor(1.5e7, 88);
            Assert.That(s.Kind("ankle").force_limit_nm, Is.EqualTo(1.0e9 * 1.5 * 1.1).Within(1e3));
            Assert.That(s.Kind("ankle").swing_hz, Is.EqualTo(1.0 / Math.Sqrt(1.1)).Within(1e-9));
            Assert.That(t.Kind("ankle").force_limit_nm, Is.EqualTo(1.0e9), "the original is untouched");
        }
    }

    public class SmallLogicTests
    {
        [Test]
        public void ContactHysteresis_UsesOnAndOffThresholds()
        {
            var h = new ContactHysteresis();
            Assert.That(h.Update(2.0, 3.0, 1.0), Is.EqualTo(0));
            Assert.That(h.Update(3.0, 3.0, 1.0), Is.EqualTo(1));
            Assert.That(h.Update(1.5, 3.0, 1.0), Is.EqualTo(0), "between the thresholds the contact holds");
            Assert.That(h.InContact, Is.True);
            Assert.That(h.Update(0.99, 3.0, 1.0), Is.EqualTo(-1));
            Assert.That(h.Update(2.9, 3.0, 1.0), Is.EqualTo(0));
        }

        [Test]
        public void RotVec_RoundTrips_AndMatchesAxisAngle()
        {
            var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(30, 10, 10)), 0.5789f);
            Vector3 r = JointMath.RotVec(q);
            Assert.That(r.Length(), Is.EqualTo(0.5789f).Within(1e-5f));
            Assert.That(r.X / r.Y, Is.EqualTo(3f).Within(1e-4f), "measured PhysX behaviour: spherical joint coordinates are the rotation vector");
            var back = JointMath.FromRotVec(r);
            Assert.That(Math.Abs(Quaternion.Dot(q, back)), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(JointMath.Yaw(JointMath.YawRotation(0.7f)), Is.EqualTo(0.7f).Within(1e-6f));
        }

        [Test]
        public void QuaternionProduct_AppliesRightFactorFirst()
        {
            var a = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.6f); var b = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.9f);
            Vector3 v = new Vector3(0.2f, -1f, 0.4f);
            Vector3 viaProduct = Vector3.Transform(v, a * b), stepwise = Vector3.Transform(Vector3.Transform(v, b), a);
            Assert.That((viaProduct - stepwise).Length(), Is.LessThan(1e-5f), "world = parent * local, the convention the leg IK relies on");
        }

        [Test]
        public void JsonLine_WritesInvariantRoundTripNumbers()
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string s = new JsonLine().Begin().Str("rec", "step").Int("n", 3).Num("t", 0.05).Vec("p", new Vector3(1.5f, 0, -2)).Bool("ok", true).Num("bad", double.NaN).End();
                Assert.That(s, Is.EqualTo("{\"rec\":\"step\",\"n\":3,\"t\":0.05,\"p\":[1.5,0,-2],\"ok\":true,\"bad\":null}"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }
    }
}
