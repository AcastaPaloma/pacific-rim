using System;
using System.IO;
using Game.Rendering.Pixel;
using NUnit.Framework;

namespace Game.Cameras.Tests
{
    /// <summary>Engine-free tests, run by Unity EditMode and by `dotnet test DotnetTests/Camera.Tests`.</summary>
    public class CameraStabiliserTests
    {
        // 480x270 internal, 60 degree horizontal field of view (R-002 §5 capture protocol).
        static PixelGrid Grid()
        {
            double tanH = Math.Tan(30.0 * Math.PI / 180.0), tanV = tanH * 270.0 / 480.0;
            return new PixelGrid { InternalWidth = 480, InternalHeight = 270, Scale = 4, TanHalfHorizontalFov = (float)tanH, TanHalfVerticalFov = (float)tanV, TexelAngle = (float)PixelScaleRule.TexelAngle(tanV, 270) };
        }

        static readonly StabiliserSettings Both = new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 600f };
        static CameraPose Pose(float x, float y, float z, float yaw, float pitch) => new CameraPose { Position = new CameraVec3(x, y, z), YawDeg = yaw, PitchDeg = pitch };

        [Test]
        public void TexelGrid_MatchesTheCaptureProtocolNumbers()
        {
            var g = Grid();
            Assert.AreEqual(0.1378, g.TexelAngle * 180.0 / Math.PI, 2e-4, "degrees per internal texel at the screen centre");
            var r = CameraStabiliser.Stabilise(Pose(0, 70, 0, 0, 0), g, Both, false);
            Assert.AreEqual(1.4434, r.Hints.PositionGrid, 1e-3, "one texel at the 600 m reference depth, in metres");
        }

        [Test]
        public void Stabilise_IsAPureFunction_SoACutEqualsAStaticFrame()
        {
            var wanted = Pose(12.34f, 70.2f, -150.7f, 23.4f, -3.3f);
            var a = CameraStabiliser.Stabilise(wanted, Grid(), Both, false);
            var b = CameraStabiliser.Stabilise(wanted, Grid(), Both, true);
            Assert.AreEqual(a.Rendered.Position.X, b.Rendered.Position.X); Assert.AreEqual(a.Rendered.Position.Y, b.Rendered.Position.Y); Assert.AreEqual(a.Rendered.Position.Z, b.Rendered.Position.Z);
            Assert.AreEqual(a.Rendered.YawDeg, b.Rendered.YawDeg); Assert.AreEqual(a.Rendered.PitchDeg, b.Rendered.PitchDeg);
            Assert.AreEqual(a.Hints.SubTexelX, b.Hints.SubTexelX); Assert.AreEqual(a.Hints.DitherOriginX, b.Hints.DitherOriginX); Assert.AreEqual(a.Hints.DitherOriginY, b.Hints.DitherOriginY);
            Assert.IsFalse(a.Hints.Cut); Assert.IsTrue(b.Hints.Cut);
        }

        [Test]
        public void LateralTranslation_RendersInWholeTexelJumps_AndTheRemainderRestoresTheMotion()
        {
            var g = Grid(); float grid = CameraStabiliser.Stabilise(Pose(0, 70, 0, 0, 0), g, Both, false).Hints.PositionGrid;
            float previousX = float.NaN; int jumps = 0;
            for (int i = 0; i < 200; i++)
            {
                float x = i * 0.1f * grid;   // 0.1 internal px per frame at the reference depth (sequence T1)
                var r = CameraStabiliser.Stabilise(Pose(x, 70, 0, 0, 0), g, Both, false);
                Assert.AreEqual(0.0, Math.IEEERemainder(r.Rendered.Position.X, grid), 1e-3, "rendered position is on the grid");
                Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelX), 0.5 + 1e-3);
                Assert.AreEqual(x, r.Rendered.Position.X + r.Hints.SubTexelX * grid, 2e-3, "rendered position plus remainder is the wanted position");
                Assert.AreEqual(0f, r.Rendered.Position.Z, 1e-4, "the forward axis is not snapped");
                if (!float.IsNaN(previousX) && r.Rendered.Position.X != previousX) { jumps++; Assert.AreEqual(grid, r.Rendered.Position.X - previousX, 1e-3); }
                previousX = r.Rendered.Position.X;
            }
            Assert.AreEqual(20, jumps, 1, "one jump per ten frames");
        }

        [Test]
        public void Yaw_TurnsInWholeTexelSteps_AndTheDitherOriginFollows()
        {
            var g = Grid(); double thetaDeg = g.TexelAngle * 180.0 / Math.PI;
            int previousOrigin = -1; float previousYaw = float.NaN;
            for (int i = 0; i < 100; i++)
            {
                float yaw = (float)(i * 0.25 * thetaDeg);   // sequence Y1
                var r = CameraStabiliser.Stabilise(Pose(0, 70, 0, yaw, 0), g, Both, false);
                Assert.AreEqual(0.0, Math.IEEERemainder(r.Rendered.YawDeg, thetaDeg), 1e-4);
                Assert.AreEqual(yaw, r.Rendered.YawDeg + r.Hints.SubTexelX * thetaDeg, 1e-4);
                if (previousOrigin >= 0 && r.Rendered.YawDeg != previousYaw) Assert.AreEqual(previousOrigin + 1, r.Hints.DitherOriginX, "one texel of pattern per step to the right");
                previousOrigin = r.Hints.DitherOriginX; previousYaw = r.Rendered.YawDeg;
            }
            var up = CameraStabiliser.Stabilise(Pose(0, 70, 0, 0, (float)(3 * thetaDeg)), g, Both, false);
            Assert.AreEqual(3, up.Hints.DitherOriginY); Assert.AreEqual(3 * thetaDeg, up.Rendered.PitchDeg, 1e-4);
            var left = CameraStabiliser.Stabilise(Pose(0, 70, 0, (float)(-2 * thetaDeg), 0), g, Both, false);
            Assert.AreEqual(CameraStabiliser.DitherOriginPeriod - 2, left.Hints.DitherOriginX, "origins wrap without going negative");
        }

        [Test]
        public void WithStabilisersOff_ThePoseIsUntouched()
        {
            var wanted = Pose(12.34f, 70.2f, -150.7f, 23.4f, -3.3f);
            var r = CameraStabiliser.Stabilise(wanted, Grid(), new StabiliserSettings { ReferenceDepth = 600f }, false);
            Assert.AreEqual(wanted.Position.X, r.Rendered.Position.X, 1e-4); Assert.AreEqual(wanted.Position.Y, r.Rendered.Position.Y, 1e-4); Assert.AreEqual(wanted.Position.Z, r.Rendered.Position.Z, 1e-4);
            Assert.AreEqual(wanted.YawDeg, r.Rendered.YawDeg, 1e-5); Assert.AreEqual(0f, r.Hints.SubTexelX); Assert.AreEqual(0f, r.Hints.SubTexelY);
            Assert.AreEqual(0, r.Hints.DitherOriginX); Assert.AreEqual(0f, r.Hints.PositionGrid); Assert.AreEqual(0f, r.Hints.RotationStep);
        }

        [Test]
        public void Remainders_StayInsideOneTexel_ForAnyPose()
        {
            var g = Grid(); uint s = 99;
            for (int i = 0; i < 2000; i++)
            {
                float x = Next(ref s) * 4000 - 2000, y = Next(ref s) * 400, z = Next(ref s) * 4000 - 2000, yaw = Next(ref s) * 720 - 360, pitch = Next(ref s) * 60 - 30;
                var r = CameraStabiliser.Stabilise(Pose(x, y, z, yaw, pitch), g, Both, false);
                Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelX), 1.0 + 0.02); Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelY), 1.0 + 0.02);
                double moved = Math.Sqrt(Math.Pow(r.Rendered.Position.X - x, 2) + Math.Pow(r.Rendered.Position.Y - y, 2) + Math.Pow(r.Rendered.Position.Z - z, 2));
                Assert.LessOrEqual(moved, r.Hints.PositionGrid * 0.7072 + 2e-3, "the snap moves the camera by at most half a texel per axis");
            }
        }

        static float Next(ref uint s) { s = s * 1664525u + 1013904223u; return (s >> 8) / 16777216f; }

        [Test]
        public void Quaternion_AgreesWithTheBasis()
        {
            double yaw = 0.7, pitch = 0.2;
            CameraStabiliser.Basis(yaw, pitch, out var right, out var up, out var forward);
            CameraStabiliser.ToQuaternion(yaw, pitch, out float x, out float y, out float z, out float w);
            // Rotate +Z by the quaternion: v' = v + 2w(q x v) + 2 q x (q x v)
            double[] f = Rotate(x, y, z, w, 0, 0, 1), r = Rotate(x, y, z, w, 1, 0, 0), u = Rotate(x, y, z, w, 0, 1, 0);
            for (int i = 0; i < 3; i++) { Assert.AreEqual(forward[i], f[i], 1e-6); Assert.AreEqual(right[i], r[i], 1e-6); Assert.AreEqual(up[i], u[i], 1e-6); }
            Assert.Greater(forward[1], 0, "positive pitch looks up");
        }

        static double[] Rotate(double qx, double qy, double qz, double qw, double vx, double vy, double vz)
        {
            double cx = qy * vz - qz * vy, cy = qz * vx - qx * vz, cz = qx * vy - qy * vx;
            double dx = qy * cz - qz * cy, dy = qz * cx - qx * cz, dz = qx * cy - qy * cx;
            return new[] { vx + 2 * (qw * cx + dx), vy + 2 * (qw * cy + dy), vz + 2 * (qw * cz + dz) };
        }
    }

    public class CameraImpulseTests
    {
        static readonly ImpulseSettings Settings = new ImpulseSettings { MetresPerMegaNs = 0.015f, DegreesPerMegaNs = 0.012f, MaxMetres = 0.5f, MaxDegrees = 0.5f, DecaySeconds = 1.2f, FrequencyHz = 2f, FalloffDistance = 300f };

        static float Peak(float impulseNs, float distance, double dt, out float atEnd)
        {
            var imp = new CameraImpulse(Settings); imp.Add(impulseNs, distance);
            float peak = 0;
            for (double t = 0; t < imp.Lifetime + 0.2; t += dt) { imp.Advance(dt); peak = Math.Max(peak, Math.Abs(imp.OffsetMetres)); }
            atEnd = imp.OffsetMetres;
            return peak;
        }

        [Test]
        public void Response_IsProportionalToTheImpulse_UntilTheCap()
        {
            float small = Peak(5e6f, 100f, 1.0 / 240, out _), twice = Peak(1e7f, 100f, 1.0 / 240, out _), huge = Peak(1e12f, 100f, 1.0 / 240, out _);
            Assert.Greater(small, 0.01f);
            Assert.AreEqual(2.0, twice / small, 0.01);
            Assert.LessOrEqual(huge, Settings.MaxMetres + 1e-6f, "bounded");
            Assert.Greater(huge, Settings.MaxMetres * 0.5f);
        }

        [Test]
        public void Response_DecaysToExactlyZero_WithinTheStatedTime()
        {
            var imp = new CameraImpulse(Settings); imp.Add(3e7f, 100f);
            Assert.Less(imp.Lifetime, 3.0, "a 1.2 s decay ends within three seconds");
            double t = 0; for (; t < Settings.DecaySeconds; t += 1.0 / 60) imp.Advance(1.0 / 60);
            Assert.Less(Math.Abs(imp.OffsetMetres), 0.05 * 0.5f + 1e-4, "5% envelope after the decay time");
            for (; t < imp.Lifetime + 0.1; t += 1.0 / 60) imp.Advance(1.0 / 60);
            Assert.AreEqual(0f, imp.OffsetMetres); Assert.AreEqual(0f, imp.OffsetDegrees); Assert.AreEqual(0, imp.ActiveCount);
        }

        [Test]
        public void Response_IsReproducible_AndIndependentOfTheStepSize()
        {
            var a = new CameraImpulse(Settings); var b = new CameraImpulse(Settings); var c = new CameraImpulse(Settings);
            a.Add(2e7f, 150f); b.Add(2e7f, 150f); c.Add(2e7f, 150f);
            for (int i = 0; i < 60; i++)
            {
                a.Advance(1.0 / 60); b.Advance(1.0 / 60); c.Advance(1.0 / 120); c.Advance(1.0 / 120);
                Assert.AreEqual(a.OffsetMetres, b.OffsetMetres); Assert.AreEqual(a.OffsetDegrees, b.OffsetDegrees);
                Assert.AreEqual(a.OffsetMetres, c.OffsetMetres, 1e-6);
            }
        }

        [Test]
        public void Response_FallsOffWithDistance_AndIgnoresBadInput()
        {
            float near = Peak(1e7f, 100f, 1.0 / 240, out _), far = Peak(1e7f, 600f, 1.0 / 240, out _);
            Assert.AreEqual(0.5, far / near, 0.01, "half the amplitude at twice the falloff distance");
            var imp = new CameraImpulse(Settings); imp.Add(-5f, 10f); imp.Add(float.NaN, 10f); imp.Add(float.PositiveInfinity, 10f);
            Assert.AreEqual(0, imp.ActiveCount);
            for (int i = 0; i < 40; i++) imp.Add(1e7f, 10f);   // more events than slots: no growth, still bounded
            imp.Advance(0.1); Assert.LessOrEqual(Math.Abs(imp.OffsetMetres), Settings.MaxMetres);
        }

        [Test]
        public void Orbit_FramesTheFocusFromTheGivenDistance()
        {
            var pose = RigFraming.Orbit(new CameraVec3(10, 44, 20), 30f, 4f, 248f);
            double d = Math.Sqrt(Math.Pow(pose.Position.X - 10, 2) + Math.Pow(pose.Position.Y - 44, 2) + Math.Pow(pose.Position.Z - 20, 2));
            Assert.AreEqual(248.0, d, 1e-2); Assert.Less(pose.Position.Y, 44f, "looking up at the focus from below");
            Assert.AreEqual(5.0, RigFraming.Approach(0, 10, 0.6, 0.6 * Math.Log(2)), 1e-9, "half-way after ln 2 time constants");
            Assert.AreEqual(10.0, RigFraming.Approach(0, 10, 0, 0.016), 1e-12);
        }
    }

    public class CameraArchitectureTests
    {
        static string Root()
        {
            foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
                for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                    if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Game", "Camera"))) return d.FullName;
            throw new DirectoryNotFoundException("project root not found");
        }

        [Test]
        public void Camera_ReferencesNoUnitOrMechCode_AndPixelOnlyThroughContracts()
        {
            string dir = Path.Combine(Root(), "Assets", "Game", "Camera");
            foreach (string file in Directory.GetFiles(dir, "*.asmdef", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                foreach (string banned in new[] { "Game.Units", "Game.Mech", "Game.Combat", "Game.Control", "Game.World", "Game.AI" })
                    StringAssert.DoesNotContain("\"" + banned, text, file);
                if (!file.Contains("Tests")) StringAssert.DoesNotContain("\"Game.Rendering.Pixel\"", text, file + ": only Game.Rendering.Pixel.Contracts is allowed");
            }
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (file.Contains("Tests")) continue;
                StringAssert.DoesNotContain("using Game.Units", text, file); StringAssert.DoesNotContain("FootContact", text.Replace("foot contact", ""), file);
            }
        }

        [Test]
        public void ImpulseEvent_IsAnUnmanagedCoreEvent()
        {
            Assert.IsTrue(typeof(Game.Core.IEvent).IsAssignableFrom(typeof(CameraShakeImpulse)));
            Assert.IsTrue(typeof(CameraShakeImpulse).IsValueType);
            foreach (var f in typeof(CameraShakeImpulse).GetFields()) Assert.IsTrue(f.FieldType.IsValueType, f.Name);
        }
    }
}
