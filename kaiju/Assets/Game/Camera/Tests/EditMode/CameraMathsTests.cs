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

        static StabilisedPose Fresh(CameraPose pose, StabiliserSettings settings, bool cut = false) { var state = new SnapState(); return CameraStabiliser.Stabilise(pose, Grid(), settings, cut, ref state); }

        [Test]
        public void TexelGrid_MatchesTheCaptureProtocolNumbers()
        {
            var g = Grid();
            Assert.AreEqual(0.1378, g.TexelAngle * 180.0 / Math.PI, 2e-4, "degrees per internal texel at the screen centre");
            Assert.AreEqual(1.4434, Fresh(Pose(0, 70, 0, 0, 0), Both).Hints.PositionGrid, 1e-3, "one texel at the 600 m reference depth, in metres");
        }

        [Test]
        public void ACut_RendersTheWantedPosition_SoItEqualsAStaticFrame()
        {
            var a = Pose(0, 70, -150, 0, 3); var b = Pose(127.3f, 1.7f, -109.8f, 31.7f, 7.3f);
            var state = new SnapState();
            for (int i = 0; i < 40; i++) { a.Position.X += 0.4f; a.YawDeg += 0.07f; CameraStabiliser.Stabilise(a, Grid(), Both, i == 0, ref state); }
            var cut = CameraStabiliser.Stabilise(b, Grid(), Both, true, ref state);
            var held = CameraStabiliser.Stabilise(b, Grid(), Both, false, ref state);
            var fresh = Fresh(b, Both, true);
            foreach (var r in new[] { held, fresh })
            {
                Assert.AreEqual(cut.Rendered.Position.X, r.Rendered.Position.X); Assert.AreEqual(cut.Rendered.Position.Y, r.Rendered.Position.Y); Assert.AreEqual(cut.Rendered.Position.Z, r.Rendered.Position.Z);
                Assert.AreEqual(cut.Rendered.YawDeg, r.Rendered.YawDeg); Assert.AreEqual(cut.Rendered.PitchDeg, r.Rendered.PitchDeg);
                Assert.AreEqual(cut.Hints.SubTexelX, r.Hints.SubTexelX, 1e-6); Assert.AreEqual(cut.Hints.SubTexelY, r.Hints.SubTexelY, 1e-6);
                Assert.AreEqual(cut.Hints.DitherOriginX, r.Hints.DitherOriginX); Assert.AreEqual(cut.Hints.DitherOriginY, r.Hints.DitherOriginY);
            }
            Assert.AreEqual(b.Position.X, cut.Rendered.Position.X, 1e-4, "a cut starts from the wanted position: no carried remainder");
            Assert.IsTrue(cut.Hints.Cut); Assert.IsFalse(held.Hints.Cut);
        }

        [Test]
        public void LateralTranslation_RendersInWholeTexelJumps_AndTheRemainderRestoresTheMotion()
        {
            var g = Grid(); float grid = Fresh(Pose(0, 70, 0, 0, 0), Both).Hints.PositionGrid;
            var state = new SnapState(); float previousX = float.NaN; int jumps = 0;
            for (int i = 0; i < 200; i++)
            {
                float x = i * 0.1f * grid;   // 0.1 internal px per frame at the reference depth (sequence T1)
                var r = CameraStabiliser.Stabilise(Pose(x, 70, 0, 0, 0), g, Both, i == 0, ref state);
                Assert.AreEqual(0.0, Math.IEEERemainder(r.Rendered.Position.X, grid), 1e-3, "rendered position moves in whole texels from where it started");
                Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelX), 0.5 + 1e-3);
                Assert.AreEqual(x, r.Rendered.Position.X + r.Hints.SubTexelX * grid, 2e-3, "rendered position plus remainder is the wanted position");
                Assert.AreEqual(0f, r.Rendered.Position.Z, 1e-4, "no drift along the forward axis");
                if (!float.IsNaN(previousX) && r.Rendered.Position.X != previousX) { jumps++; Assert.AreEqual(grid, r.Rendered.Position.X - previousX, 1e-3); }
                previousX = r.Rendered.Position.X;
            }
            Assert.AreEqual(20, jumps, 1, "one jump per ten frames");
        }

        [Test]
        public void TurningOnTheSpot_NeverMovesTheCamera()
        {
            var g = Grid(); double thetaDeg = g.TexelAngle * 180.0 / Math.PI; var state = new SnapState();
            var first = CameraStabiliser.Stabilise(Pose(12.34f, 70.2f, -150.7f, 0, 3), g, Both, true, ref state);
            for (int i = 1; i < 400; i++)
            {
                var r = CameraStabiliser.Stabilise(Pose(12.34f, 70.2f, -150.7f, (float)(i * 0.25 * thetaDeg), 3), g, Both, false, ref state);
                Assert.AreEqual(first.Rendered.Position.X, r.Rendered.Position.X); Assert.AreEqual(first.Rendered.Position.Y, r.Rendered.Position.Y); Assert.AreEqual(first.Rendered.Position.Z, r.Rendered.Position.Z);
            }
        }

        [Test]
public void ForwardMotion_IsContinuous_AtAnyHeading()
        {
            var g = Grid(); var state = new SnapState();
            float yawDeg = (float)(218 * g.TexelAngle * 180.0 / Math.PI);   // about 30 degrees, a whole number of rotation steps
            double yaw = 218 * (double)g.TexelAngle, sin = Math.Sin(yaw), cos = Math.Cos(yaw);
            CameraStabiliser.Stabilise(Pose(0, 70, 0, yawDeg, 0), g, Both, true, ref state);
            for (int i = 1; i < 100; i++)
            {
                float d = i * 0.37f;   // along the camera's forward axis: the rendered position follows exactly
                var r = CameraStabiliser.Stabilise(Pose((float)(sin * d), 70, (float)(cos * d), yawDeg, 0), g, Both, false, ref state);
                Assert.AreEqual(sin * d, r.Rendered.Position.X, 2e-3); Assert.AreEqual(cos * d, r.Rendered.Position.Z, 2e-3);
                Assert.AreEqual(0f, r.Hints.SubTexelX, 5e-3);
            }
        }

        [Test]
        public void Yaw_TurnsInWholeTexelSteps_AndTheDitherOriginFollows()
        {
            var g = Grid(); double thetaDeg = g.TexelAngle * 180.0 / Math.PI;
            int previousOrigin = -1; float previousYaw = float.NaN; var state = new SnapState();
            for (int i = 0; i < 100; i++)
            {
                float yaw = (float)(i * 0.25 * thetaDeg);   // sequence Y1
                var r = CameraStabiliser.Stabilise(Pose(0, 70, 0, yaw, 0), g, Both, i == 0, ref state);
                Assert.AreEqual(0.0, Math.IEEERemainder(r.Rendered.YawDeg, thetaDeg), 1e-4);
                Assert.AreEqual(yaw, r.Rendered.YawDeg + r.Hints.SubTexelX * thetaDeg, 1e-4);
                if (previousOrigin >= 0 && r.Rendered.YawDeg != previousYaw) Assert.AreEqual(previousOrigin + 1, r.Hints.DitherOriginX, "one texel of pattern per step to the right");
                previousOrigin = r.Hints.DitherOriginX; previousYaw = r.Rendered.YawDeg;
            }
            var up = Fresh(Pose(0, 70, 0, 0, (float)(3 * thetaDeg)), Both);
            Assert.AreEqual(3, up.Hints.DitherOriginY); Assert.AreEqual(3 * thetaDeg, up.Rendered.PitchDeg, 1e-4);
            var left = Fresh(Pose(0, 70, 0, (float)(-2 * thetaDeg), 0), Both);
            Assert.AreEqual(CameraStabiliser.DitherOriginPeriod - 2, left.Hints.DitherOriginX, "origins wrap without going negative");
        }

        [Test]
        public void WithStabilisersOff_ThePoseIsUntouched()
        {
            var wanted = Pose(12.34f, 70.2f, -150.7f, 23.4f, -3.3f);
            var r = Fresh(wanted, new StabiliserSettings { ReferenceDepth = 600f });
            Assert.AreEqual(wanted.Position.X, r.Rendered.Position.X, 1e-4); Assert.AreEqual(wanted.Position.Y, r.Rendered.Position.Y, 1e-4); Assert.AreEqual(wanted.Position.Z, r.Rendered.Position.Z, 1e-4);
            Assert.AreEqual(wanted.YawDeg, r.Rendered.YawDeg, 1e-5); Assert.AreEqual(0f, r.Hints.SubTexelX); Assert.AreEqual(0f, r.Hints.SubTexelY);
            Assert.AreEqual(0, r.Hints.DitherOriginX); Assert.AreEqual(0f, r.Hints.PositionGrid); Assert.AreEqual(0f, r.Hints.RotationStep);
        }

        [Test]
        public void Remainders_StayInsideOneTexel_AlongAnyPath()
        {
            var g = Grid(); uint s = 99; var state = new SnapState();
            float x = 0, y = 70, z = 0, yaw = 0, pitch = 0;
            for (int i = 0; i < 4000; i++)
            {
                x += (Next(ref s) - 0.5f) * 3f; y += (Next(ref s) - 0.5f) * 1f; z += (Next(ref s) - 0.5f) * 3f; yaw += (Next(ref s) - 0.5f) * 0.6f; pitch = Math.Max(-30, Math.Min(30, pitch + (Next(ref s) - 0.5f) * 0.3f));
                var r = CameraStabiliser.Stabilise(Pose(x, y, z, yaw, pitch), g, Both, i == 0, ref state);
                Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelX), 1.0 + 0.02); Assert.LessOrEqual(Math.Abs(r.Hints.SubTexelY), 1.0 + 0.02);
                double moved = Math.Sqrt(Math.Pow(r.Rendered.Position.X - x, 2) + Math.Pow(r.Rendered.Position.Y - y, 2) + Math.Pow(r.Rendered.Position.Z - z, 2));
                Assert.LessOrEqual(moved, r.Hints.PositionGrid * 0.7072 + 2e-3, "the rendered camera stays within half a texel per axis of the wanted one");
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
    /// <summary>Layer snap (T-181c): the rig side of hints v2.</summary>
    public class CameraLayerStabiliserTests
    {
        static PixelGrid Grid()
        {
            double tanH = Math.Tan(30.0 * Math.PI / 180.0), tanV = tanH * 270.0 / 480.0;
            return new PixelGrid { InternalWidth = 480, InternalHeight = 270, Scale = 4, TanHalfHorizontalFov = (float)tanH, TanHalfVerticalFov = (float)tanV, TexelAngle = (float)PixelScaleRule.TexelAngle(tanV, 270) };
        }

        static LayerSettings T1(float main = 664.874f) => new LayerSettings { SnapPosition = true, QuantiseRotation = true, NearDepth = 124.2f, MainDepth = main, FarDepth = 10353f };
        static CameraPose Pose(float x, float y, float z, float yaw, float pitch) => new CameraPose { Position = new CameraVec3(x, y, z), YawDeg = yaw, PitchDeg = pitch };
        static float TexelAt(float depth) => (float)(2.0 * depth * Grid().TanHalfVerticalFov / Grid().InternalHeight);

        static CameraPose PathAt(int i) => Pose(0.37f * i, 70f + 0.011f * i, -150f + 0.21f * i, 5f + 0.031f * i, 3f + 0.004f * i);

        [Test]
        public void MainLayer_EqualsTheV1Stabiliser_AtTheMainDepth_BitForBit()
        {
            var layered = new LayerSnapState(); var plain = new SnapState();
            var plainSettings = new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 664.874f };
            for (int i = 0; i < 300; i++)
            {
                var wanted = PathAt(i);
                Assert.IsTrue(CameraLayerStabiliser.TryStabilise(wanted, Grid(), T1(), i == 0, ref layered, out var r));
                var v1 = CameraStabiliser.Stabilise(wanted, Grid(), plainSettings, i == 0, ref plain);
                Assert.AreEqual(v1.Rendered.Position.X, r.Rendered.Position.X); Assert.AreEqual(v1.Rendered.Position.Y, r.Rendered.Position.Y); Assert.AreEqual(v1.Rendered.Position.Z, r.Rendered.Position.Z);
                Assert.AreEqual(v1.Rendered.YawDeg, r.Rendered.YawDeg); Assert.AreEqual(v1.Rendered.PitchDeg, r.Rendered.PitchDeg);
                Assert.AreEqual(v1.Hints.SubTexelX, r.Hints.SubTexelX); Assert.AreEqual(v1.Hints.SubTexelY, r.Hints.SubTexelY);
                Assert.AreEqual(v1.Hints.PositionGrid, r.Hints.PositionGrid); Assert.AreEqual(v1.Hints.ReferenceDepth, r.Hints.ReferenceDepth);
                Assert.AreEqual(v1.Hints.DitherOriginX, r.Hints.DitherOriginX); Assert.AreEqual(v1.Hints.DitherOriginY, r.Hints.DitherOriginY);
                Assert.AreEqual(v1.Hints.Cut, r.Hints.Cut);
                Assert.AreEqual(3, r.Hints.LayerCount);
                Assert.AreEqual(0f, r.Hints.LayerMain.OffsetRight); Assert.AreEqual(0f, r.Hints.LayerMain.OffsetUp);
                Assert.AreEqual(r.Hints.SubTexelX, r.Hints.LayerMain.SubTexelX); Assert.AreEqual(r.Hints.SubTexelY, r.Hints.LayerMain.SubTexelY);
                Assert.AreEqual(r.Hints.ReferenceDepth, r.Hints.LayerMain.ReferenceDepth); Assert.AreEqual(r.Hints.PositionGrid, r.Hints.LayerMain.Grid);
            }
        }

        [Test]
        public void EachLayer_GridIsOneTexelAtItsOwnDepth_AndRemaindersStayInsideOneTexel()
        {
            var st = new LayerSnapState();
            for (int i = 0; i < 400; i++)
            {
                Assert.IsTrue(CameraLayerStabiliser.TryStabilise(PathAt(i), Grid(), T1(), i == 0, ref st, out var r));
                var h = r.Hints;
                foreach (var l in new[] { h.LayerNear, h.LayerMain, h.LayerFar })
                {
                    Assert.AreEqual(TexelAt(l.ReferenceDepth), l.Grid, TexelAt(l.ReferenceDepth) * 1e-5, "grid is one texel at the layer's depth");
                    Assert.LessOrEqual(Math.Abs(l.SubTexelX), 1.0f); Assert.LessOrEqual(Math.Abs(l.SubTexelY), 1.0f);
                }
                Assert.AreEqual(124.2f, h.LayerNear.ReferenceDepth); Assert.AreEqual(664.874f, h.LayerMain.ReferenceDepth); Assert.AreEqual(10353f, h.LayerFar.ReferenceDepth);
            }
        }

        [Test]
        public void LayerOffsets_StayWithinHalfTheirOwnGridPlusHalfTheMainGrid()
        {
            var st = new LayerSnapState();
            for (int i = 0; i < 600; i++)
            {
                CameraLayerStabiliser.TryStabilise(PathAt(i), Grid(), T1(), i == 0, ref st, out var r);
                var h = r.Hints;
                foreach (var l in new[] { h.LayerNear, h.LayerFar })
                {
                    float lim = 0.5f * (l.Grid + h.PositionGrid) * 1.0001f + 1e-4f;
                    Assert.LessOrEqual(Math.Abs(l.OffsetRight), lim, "right offset at step " + i); Assert.LessOrEqual(Math.Abs(l.OffsetUp), lim, "up offset at step " + i);
                }
            }
        }

        [Test]
        public void LayerPositionPlusOffset_ReproducesTheLayersOwnSnappedPosition_AndLayersAreIndependentLattices()
        {
            // Pure lateral motion with no rotation: every layer's snapped x is its own whole-texel lattice, so the offset is the difference of the two lattices.
            var st = new LayerSnapState(); var near = new SnapState(); var far = new SnapState();
            var settings = T1();
            for (int i = 0; i < 400; i++)
            {
                var wanted = Pose(0.173f * i, 70f, -150f, 0f, 0f);
                CameraLayerStabiliser.TryStabilise(wanted, Grid(), settings, i == 0, ref st, out var r);
                var n = CameraStabiliser.Stabilise(wanted, Grid(), new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 124.2f }, i == 0, ref near);
                var f = CameraStabiliser.Stabilise(wanted, Grid(), new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 10353f }, i == 0, ref far);
                Assert.AreEqual(n.Rendered.Position.X - r.Rendered.Position.X, r.Hints.LayerNear.OffsetRight, 2e-4, "near offset at " + i);
                Assert.AreEqual(f.Rendered.Position.X - r.Rendered.Position.X, r.Hints.LayerFar.OffsetRight, 2e-4, "far offset at " + i);
                Assert.AreEqual(n.Hints.SubTexelX, r.Hints.LayerNear.SubTexelX); Assert.AreEqual(f.Hints.SubTexelX, r.Hints.LayerFar.SubTexelX);
                Assert.AreEqual(0f, r.Hints.LayerNear.OffsetUp, 1e-4); Assert.AreEqual(0f, r.Hints.LayerFar.OffsetUp, 1e-4);
            }
        }

        [Test]
        public void AMovingCamera_NearLayerSnapsInSmallerSteps_ThanMainThanFar()
        {
            var st = new LayerSnapState(); int nearSteps = 0, mainSteps = 0, farSteps = 0;
            float px = 0, pn = 0, pf = 0;
            for (int i = 0; i < 1000; i++)
            {
                CameraLayerStabiliser.TryStabilise(Pose(0.05f * i, 70f, -150f, 0f, 0f), Grid(), T1(), i == 0, ref st, out var r);
                float x = r.Rendered.Position.X, n = x + r.Hints.LayerNear.OffsetRight, f = x + r.Hints.LayerFar.OffsetRight;
                if (i > 0) { if (x != px) mainSteps++; if (n != pn) nearSteps++; if (f != pf) farSteps++; }
                px = x; pn = n; pf = f;
            }
            Assert.Greater(nearSteps, mainSteps); Assert.Greater(mainSteps, farSteps); Assert.GreaterOrEqual(farSteps, 1);
        }

        [Test]
        public void ALayerCut_FiresOnlyForTheLayerWhoseReferenceDepthChanged_AndAGlobalCutFiresAll()
        {
            var st = new LayerSnapState();
            CameraLayerStabiliser.TryStabilise(PathAt(0), Grid(), T1(), true, ref st, out var r);
            Assert.IsTrue(r.Hints.LayerNear.Cut && r.Hints.LayerMain.Cut && r.Hints.LayerFar.Cut && r.Hints.Cut, "first frame restarts every lattice");
            CameraLayerStabiliser.TryStabilise(PathAt(1), Grid(), T1(), false, ref st, out r);
            Assert.IsFalse(r.Hints.LayerNear.Cut || r.Hints.LayerMain.Cut || r.Hints.LayerFar.Cut || r.Hints.Cut);
            var s = T1(); s.NearDepth = 124.2f * 1.001f;   // focus depth drifted by 0.1 %: more than the grid tolerance
            CameraLayerStabiliser.TryStabilise(PathAt(2), Grid(), s, false, ref st, out r);
            Assert.IsTrue(r.Hints.LayerNear.Cut); Assert.IsFalse(r.Hints.LayerMain.Cut); Assert.IsFalse(r.Hints.LayerFar.Cut); Assert.IsFalse(r.Hints.Cut, "v1 Cut stays the main layer's");
            s.NearDepth = 124.2f * 1.001f * 1.00001f;       // 1e-5 relative: inside the tolerance
            CameraLayerStabiliser.TryStabilise(PathAt(3), Grid(), s, false, ref st, out r);
            Assert.IsFalse(r.Hints.LayerNear.Cut);
            CameraLayerStabiliser.TryStabilise(PathAt(4), Grid(), s, true, ref st, out r);
            Assert.IsTrue(r.Hints.LayerNear.Cut && r.Hints.LayerMain.Cut && r.Hints.LayerFar.Cut && r.Hints.Cut);
        }

        [Test]
        public void ACutLayer_RestartsFromTheWantedPosition_SoItsOffsetIsZeroAlongTheForwardFreeAxes()
        {
            var st = new LayerSnapState();
            for (int i = 0; i < 80; i++) CameraLayerStabiliser.TryStabilise(Pose(0.4f * i, 70f, -150f, 0f, 0f), Grid(), T1(), i == 0, ref st, out _);
            CameraLayerStabiliser.TryStabilise(Pose(500f, 90f, 10f, 20f, 2f), Grid(), T1(), true, ref st, out var r);
            Assert.AreEqual(500f, r.Rendered.Position.X, 1e-3); Assert.AreEqual(90f, r.Rendered.Position.Y, 1e-3);
            Assert.AreEqual(0f, r.Hints.LayerNear.OffsetRight, 1e-3); Assert.AreEqual(0f, r.Hints.LayerFar.OffsetUp, 1e-3);
        }

        [Test]
        public void UnusableLayering_ReturnsFalse_AndClearsTheState()
        {
            var st = new LayerSnapState();
            CameraLayerStabiliser.TryStabilise(PathAt(0), Grid(), T1(), true, ref st, out _);
            Assert.IsTrue(st.Active);
            var bad = T1(); bad.NearDepth = 700f;   // near not closer than main
            Assert.IsFalse(CameraLayerStabiliser.TryStabilise(PathAt(1), Grid(), bad, false, ref st, out var r)); Assert.AreEqual(0, r.Hints.LayerCount); Assert.IsFalse(st.Active);
            var noSnap = T1(); noSnap.SnapPosition = false;
            Assert.IsFalse(CameraLayerStabiliser.TryStabilise(PathAt(1), Grid(), noSnap, false, ref st, out _));
            Assert.IsFalse(CameraLayerStabiliser.TryStabilise(PathAt(1), default, T1(), false, ref st, out _), "no grid");
            var nan = T1(); nan.FarDepth = float.NaN;
            Assert.IsFalse(CameraLayerStabiliser.TryStabilise(PathAt(1), Grid(), nan, false, ref st, out _));
            // After a failure the next good frame restarts every lattice.
            Assert.IsTrue(CameraLayerStabiliser.TryStabilise(PathAt(2), Grid(), T1(), false, ref st, out r));
            Assert.IsTrue(r.Hints.LayerNear.Cut && r.Hints.LayerFar.Cut);
        }

        [Test]
        public void Rotation_IsSharedByAllLayers_AndTurningInPlaceMovesNoLayer()
        {
            var st = new LayerSnapState();
            CameraLayerStabiliser.TryStabilise(Pose(10f, 70f, -150f, 0f, 3f), Grid(), T1(), true, ref st, out var a);
            for (int i = 1; i <= 60; i++)
            {
                CameraLayerStabiliser.TryStabilise(Pose(10f, 70f, -150f, 0.05f * i, 3f), Grid(), T1(), false, ref st, out var r);
                Assert.AreEqual(a.Rendered.Position.X, r.Rendered.Position.X, 1e-4); Assert.AreEqual(0f, r.Hints.LayerNear.OffsetRight, 1e-3); Assert.AreEqual(0f, r.Hints.LayerFar.OffsetRight, 1e-3);
            }
        }

        [Test]
        public void TheLayeredCall_AllocatesNoMoreThanThreePlainStabiliserCalls()
        {
            var st = new LayerSnapState(); var near = new SnapState(); var main = new SnapState(); var far = new SnapState();
            var s = T1();
            for (int i = 0; i < 20; i++) { CameraLayerStabiliser.TryStabilise(PathAt(i), Grid(), s, i == 0, ref st, out _); }
            var g = Grid();
            long GcLayered() { long b = GC.GetAllocatedBytesForCurrentThread(); for (int i = 20; i < 220; i++) CameraLayerStabiliser.TryStabilise(PathAt(i), g, s, false, ref st, out _); return GC.GetAllocatedBytesForCurrentThread() - b; }
            long GcPlain()
            {
                long b = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 20; i < 220; i++)
                {
                    CameraStabiliser.Stabilise(PathAt(i), g, new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 124.2f }, false, ref near);
                    CameraStabiliser.Stabilise(PathAt(i), g, new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 664.874f }, false, ref main);
                    CameraStabiliser.Stabilise(PathAt(i), g, new StabiliserSettings { SnapPosition = true, QuantiseRotation = true, ReferenceDepth = 10353f }, false, ref far);
                }
                return GC.GetAllocatedBytesForCurrentThread() - b;
            }
            GcLayered(); GcPlain();
            Assert.LessOrEqual(GcLayered(), GcPlain(), "the wrapper itself allocates nothing beyond the three Stabilise calls");
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
                StringAssert.DoesNotContain("GUID:", text, file + ": reference assemblies by name");
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
        public void CameraFlagFile_AllFalse_AndMatchesCameraFlagsAll()
        {
            string text = File.ReadAllText(Path.Combine(Root(), "Assets", "Game", "Config", "FeatureFlags", "Camera.json"));
            var found = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "\"(camera\\.[^\"]+)\"\\s*:\\s*(\\w+)"))
            {
                found.Add(m.Groups[1].Value);
                Assert.AreEqual("false", m.Groups[2].Value, m.Groups[1].Value + " must ship false");
            }
            CollectionAssert.AreEquivalent(CameraFlags.All, found, "Camera.json keys drifted from CameraFlags.All");
            StringAssert.DoesNotContain("true", text);
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
