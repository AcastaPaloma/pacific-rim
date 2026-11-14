using System;
using Game.Rendering.Pixel;

namespace Game.Cameras
{
    public struct StabiliserSettings
    {
        public bool SnapPosition;       // snap the position to the texel grid at the reference depth
        public bool QuantiseRotation;   // turn in whole-texel steps of yaw and pitch
        public float ReferenceDepth;    // m: the plane that position snapping locks (the rig's focus distance)
    }

    public struct StabilisedPose
    {
        public CameraPose Rendered;
        public PixelStabilisationHints Hints;
    }

    /// <summary>
    /// Texel snapping for a perspective camera (GAME_BREAKDOWN §9; R-003 §7). A pure function of the wanted pose and the
    /// pixel grid: no history, so the first frame after a cut is identical to a static frame of the same pose.
    /// Rotation is quantised to whole texel angles; the position is snapped, in the frame of the quantised rotation,
    /// to a grid of one texel at the reference depth (the forward axis stays continuous). The remainders are returned
    /// in texels so the present stage can shift the image by whole output pixels.
    /// </summary>
    public static class CameraStabiliser
    {
        /// <summary>Dither origins are reduced modulo this (a multiple of every mask size) to stay exact in a float.</summary>
        public const int DitherOriginPeriod = 1024;

        public static StabilisedPose Stabilise(CameraPose wanted, PixelGrid grid, StabiliserSettings settings, bool cut)
        {
            double theta = grid.TexelAngle;
            double yaw = wanted.YawDeg * Math.PI / 180.0, pitch = wanted.PitchDeg * Math.PI / 180.0;
            long yawSteps = 0, pitchSteps = 0;
            double renderedYaw = yaw, renderedPitch = pitch, subX = 0, subY = 0;
            if (settings.QuantiseRotation && theta > 0)
            {
                yawSteps = (long)Math.Floor(yaw / theta + 0.5); pitchSteps = (long)Math.Floor(pitch / theta + 0.5);
                renderedYaw = yawSteps * theta; renderedPitch = pitchSteps * theta;
                subX += (yaw - renderedYaw) / theta; subY += (pitch - renderedPitch) / theta;
            }

            Basis(renderedYaw, renderedPitch, out var right, out var up, out var forward);
            double px = Dot(wanted.Position, right), py = Dot(wanted.Position, up), pz = Dot(wanted.Position, forward);
            double gridSize = 0;
            if (settings.SnapPosition && settings.ReferenceDepth > 0 && grid.InternalHeight > 0)
            {
                gridSize = 2.0 * settings.ReferenceDepth * grid.TanHalfVerticalFov / grid.InternalHeight;
                double sx = Math.Floor(px / gridSize + 0.5) * gridSize, sy = Math.Floor(py / gridSize + 0.5) * gridSize;
                subX += (px - sx) / gridSize; subY += (py - sy) / gridSize;
                px = sx; py = sy;
            }

            var result = new StabilisedPose();
            result.Rendered.Position = new CameraVec3(
                (float)(right[0] * px + up[0] * py + forward[0] * pz),
                (float)(right[1] * px + up[1] * py + forward[1] * pz),
                (float)(right[2] * px + up[2] * py + forward[2] * pz));
            result.Rendered.YawDeg = (float)(renderedYaw * 180.0 / Math.PI);
            result.Rendered.PitchDeg = (float)(renderedPitch * 180.0 / Math.PI);

            ToQuaternion(renderedYaw, renderedPitch, out float qx, out float qy, out float qz, out float qw);
            result.Hints = new PixelStabilisationHints
            {
                SnappedX = result.Rendered.Position.X, SnappedY = result.Rendered.Position.Y, SnappedZ = result.Rendered.Position.Z,
                RotationX = qx, RotationY = qy, RotationZ = qz, RotationW = qw,
                SubTexelX = (float)subX, SubTexelY = (float)subY,
                DitherOriginX = Mod(yawSteps), DitherOriginY = Mod(pitchSteps),
                ReferenceDepth = settings.ReferenceDepth, PositionGrid = (float)gridSize,
                RotationStep = settings.QuantiseRotation ? (float)theta : 0f,
                TexelAngle = (float)theta, PixelScale = grid.Scale, Cut = cut,
            };
            return result;
        }

        static int Mod(long steps) => (int)(((steps % DitherOriginPeriod) + DitherOriginPeriod) % DitherOriginPeriod);

        /// <summary>Right, up and forward axes (left-handed, +Y up) for yaw about +Y and pitch up, both in radians.</summary>
        public static void Basis(double yaw, double pitch, out double[] right, out double[] up, out double[] forward)
        {
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw), sp = Math.Sin(pitch), cp = Math.Cos(pitch);
            forward = new[] { sy * cp, sp, cy * cp };
            right = new[] { cy, 0.0, -sy };
            up = new[] { forward[1] * right[2] - forward[2] * right[1], forward[2] * right[0] - forward[0] * right[2], forward[0] * right[1] - forward[1] * right[0] };
        }

        /// <summary>Camera-to-world quaternion of the same rotation (Unity convention: yaw about +Y, then pitch about the local X axis, looking up for positive pitch).</summary>
        public static void ToQuaternion(double yaw, double pitch, out float x, out float y, out float z, out float w)
        {
            double hy = yaw * 0.5, hx = -pitch * 0.5;   // Unity's X rotation is positive looking down
            double sy = Math.Sin(hy), cy = Math.Cos(hy), sx = Math.Sin(hx), cx = Math.Cos(hx);
            x = (float)(cy * sx); y = (float)(sy * cx); z = (float)(-sy * sx); w = (float)(cy * cx);
        }

        static double Dot(CameraVec3 p, double[] axis) => p.X * axis[0] + p.Y * axis[1] + p.Z * axis[2];
    }
}
