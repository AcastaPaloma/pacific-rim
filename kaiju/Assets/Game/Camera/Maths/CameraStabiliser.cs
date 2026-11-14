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

    /// <summary>The snapped position carried from frame to frame (the only history the stabiliser keeps).</summary>
    public struct SnapState
    {
        public bool Valid;
        public double X, Y, Z;
    }

    /// <summary>
    /// Texel snapping for a perspective camera (GAME_BREAKDOWN §9; R-003 §7). Rotation is quantised to whole texel
    /// angles (a pure function of the wanted rotation). The position is snapped incrementally: the rendered position
    /// stays where it is until the wanted position has moved more than half a texel (at the reference depth) along the
    /// camera's right or up axis, then it steps by whole texels along that axis; the forward axis is continuous.
    /// Stepping from the previous snapped position, instead of rounding to a lattice fixed in the camera frame, keeps
    /// the camera exactly still while it only turns (a lattice in the camera frame turns with the camera and makes the
    /// position jump on every rotation step, which measured as yaw shimmer). A cut restarts from the wanted position,
    /// so the first frame after a cut is identical to a static frame of the same pose. The remainders are returned in
    /// texels so the present stage can shift the image by whole output pixels.
    /// </summary>
    public static class CameraStabiliser
    {
        /// <summary>Dither origins are reduced modulo this (a multiple of every mask size) to stay exact in a float.</summary>
        public const int DitherOriginPeriod = 1024;

        public static StabilisedPose Stabilise(CameraPose wanted, PixelGrid grid, StabiliserSettings settings, bool cut, ref SnapState state)
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
            double wx = wanted.Position.X, wy = wanted.Position.Y, wz = wanted.Position.Z;
            double gridSize = 0;
            if (settings.SnapPosition && settings.ReferenceDepth > 0 && grid.InternalHeight > 0)
            {
                gridSize = 2.0 * settings.ReferenceDepth * grid.TanHalfVerticalFov / grid.InternalHeight;
                if (cut || !state.Valid) { state.X = wx; state.Y = wy; state.Z = wz; state.Valid = true; }
                else
                {
                    double dx = wx - state.X, dy = wy - state.Y, dz = wz - state.Z;
                    double alongRight = dx * right[0] + dy * right[1] + dz * right[2];
                    double alongUp = dx * up[0] + dy * up[1] + dz * up[2];
                    double alongForward = dx * forward[0] + dy * forward[1] + dz * forward[2];
                    double stepRight = Math.Floor(alongRight / gridSize + 0.5) * gridSize, stepUp = Math.Floor(alongUp / gridSize + 0.5) * gridSize;
                    state.X += right[0] * stepRight + up[0] * stepUp + forward[0] * alongForward;
                    state.Y += right[1] * stepRight + up[1] * stepUp + forward[1] * alongForward;
                    state.Z += right[2] * stepRight + up[2] * stepUp + forward[2] * alongForward;
                    subX += (alongRight - stepRight) / gridSize; subY += (alongUp - stepUp) / gridSize;
                }
                wx = state.X; wy = state.Y; wz = state.Z;
            }
            else state.Valid = false;

            var result = new StabilisedPose();
            result.Rendered.Position = new CameraVec3((float)wx, (float)wy, (float)wz);
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

    }
}
