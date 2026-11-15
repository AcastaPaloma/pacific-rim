using System;
using Game.Rendering.Pixel;

namespace Game.Cameras
{
    /// <summary>Reference depths (eye depth, m) of the three layer-snap slabs: near &lt; main &lt; far, all positive (T-181c).</summary>
    public struct LayerSettings
    {
        public bool SnapPosition, QuantiseRotation;
        public float NearDepth, MainDepth, FarDepth;
    }

    /// <summary>The three snapped positions carried frame to frame, plus the depths they were snapped at (to detect a lattice restart).</summary>
    public struct LayerSnapState
    {
        public bool Active;
        public SnapState Near, Main, Far;
        public float NearDepth, MainDepth, FarDepth;
    }

    /// <summary>
    /// Layer-snap hints (hints v2, T-181c): <see cref="CameraStabiliser.Stabilise"/> once per layer with its own <see cref="SnapState"/>.
    /// The main layer is the rendered pose and fills the v1 fields unchanged; near and far report the offset of their own snapped position
    /// from it along the rendered right and up axes, and their own remainder and grid. Rotation quantisation is a pure function of the wanted
    /// rotation, so all three share one rotation and one dither origin.
    /// </summary>
    public static class CameraLayerStabiliser
    {
        /// <summary>Relative change of a layer's reference depth that restarts its lattice (equals PixelLayerPlan.GridTolerance; Camera does not reference Pixel.Maths).</summary>
        public const double GridTolerance = 1e-4;
        /// <summary>Number of layers (near, main, far); equals PixelLayerPlan.Count.</summary>
        public const int LayerCount = 3;

        /// <summary>
        /// False when layering is not possible (position snap off, depths not positive and increasing, no grid): <paramref name="result"/> is then default
        /// and <paramref name="state"/> is cleared so that the next layered frame restarts every lattice. The caller falls back to the v1 path.
        /// </summary>
        public static bool TryStabilise(CameraPose wanted, PixelGrid grid, in LayerSettings s, bool cut, ref LayerSnapState state, out StabilisedPose result)
        {
            result = default;
            if (!s.SnapPosition || grid.InternalHeight <= 0 || !(s.NearDepth > 0) || !(s.MainDepth > s.NearDepth) || !(s.FarDepth > s.MainDepth)
                || float.IsNaN(s.FarDepth) || float.IsInfinity(s.FarDepth))
            { state = default; return false; }

            bool first = !state.Active;
            bool cutNear = cut || first || Changed(state.NearDepth, s.NearDepth);
            bool cutMain = cut || first || Changed(state.MainDepth, s.MainDepth);
            bool cutFar = cut || first || Changed(state.FarDepth, s.FarDepth);

            StabilisedPose main = CameraStabiliser.Stabilise(wanted, grid, Settings(s, s.MainDepth), cutMain, ref state.Main);
            StabilisedPose near = CameraStabiliser.Stabilise(wanted, grid, Settings(s, s.NearDepth), cutNear, ref state.Near);
            StabilisedPose far = CameraStabiliser.Stabilise(wanted, grid, Settings(s, s.FarDepth), cutFar, ref state.Far);
            state.Active = true; state.NearDepth = s.NearDepth; state.MainDepth = s.MainDepth; state.FarDepth = s.FarDepth;

            // Rendered right / up axes, from the main (rendered) rotation; the three poses share it.
            double yaw = main.Rendered.YawDeg * Math.PI / 180.0, pitch = main.Rendered.PitchDeg * Math.PI / 180.0;
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw), sp = Math.Sin(pitch), cp = Math.Cos(pitch);
            double rx = cy, rz = -sy;                                   // right = (cy, 0, -sy)
            double fx = sy * cp, fy = sp, fz = cy * cp;                 // forward
            double ux = fy * rz, uy = fz * rx - fx * rz, uz = -fy * rx; // up = forward x right (see CameraStabiliser.Basis)

            result = main;
            PixelStabilisationHints h = main.Hints;
            h.LayerCount = LayerCount;
            h.LayerMain = new PixelLayerHint
            {
                ReferenceDepth = h.ReferenceDepth, Grid = h.PositionGrid, OffsetRight = 0f, OffsetUp = 0f,
                SubTexelX = h.SubTexelX, SubTexelY = h.SubTexelY, Cut = cutMain,
            };
            h.LayerNear = Layer(near.Hints, state.Near, state.Main, rx, rz, ux, uy, uz, cutNear);
            h.LayerFar = Layer(far.Hints, state.Far, state.Main, rx, rz, ux, uy, uz, cutFar);
            result.Hints = h;
            return true;
        }

        static PixelLayerHint Layer(in PixelStabilisationHints own, in SnapState p, in SnapState main, double rx, double rz, double ux, double uy, double uz, bool cut)
        {
            double dx = p.X - main.X, dy = p.Y - main.Y, dz = p.Z - main.Z;
            return new PixelLayerHint
            {
                ReferenceDepth = own.ReferenceDepth, Grid = own.PositionGrid,
                OffsetRight = (float)(dx * rx + dz * rz), OffsetUp = (float)(dx * ux + dy * uy + dz * uz),
                SubTexelX = own.SubTexelX, SubTexelY = own.SubTexelY, Cut = cut,
            };
        }

        /// <summary>
        /// Reference depth of the near layer (T-187): the eye depth of the near subject's nearest silhouette, not of its focus point. The C11 lock is exact or nothing (T-181), so a near
        /// layer snapped at the focus depth (124.2 m) while the edge a viewer tracks is 0.7 m closer (123.49 m) flips a whole texel once in a few tens of snap steps (T-184: 4.58 px).
        /// <paramref name="edgeOffsetM"/> is how far the nearest silhouette lies in front of the focus depth; a non-positive offset keeps the focus depth.
        /// Never below <paramref name="minDepthM"/>; the caller's layer check (near &lt; main) still applies.
        /// </summary>
        public static float NearEdgeDepth(float focusDepthM, float edgeOffsetM, float minDepthM = 1f)
        {
            if (!(edgeOffsetM > 0f) || float.IsInfinity(edgeOffsetM)) return focusDepthM;
            return Math.Min(focusDepthM, Math.Max(minDepthM, focusDepthM - edgeOffsetM));
        }

        /// <summary>
        /// Eye depth (m, along the pose's forward axis) of the nearest point of a world-space box around a subject: the centre's depth minus the box's support along the forward axis.
        /// Planner-side helper for a subject whose bounds are known (the capture tool, a later mech bounds provider).
        /// </summary>
        public static double SubjectNearEdgeDepth(CameraPose pose, CameraVec3 centre, CameraVec3 halfExtents)
        {
            double yaw = pose.YawDeg * Math.PI / 180.0, pitch = pose.PitchDeg * Math.PI / 180.0;
            double fx = Math.Sin(yaw) * Math.Cos(pitch), fy = Math.Sin(pitch), fz = Math.Cos(yaw) * Math.Cos(pitch);
            double centreDepth = (centre.X - pose.Position.X) * fx + (centre.Y - pose.Position.Y) * fy + (centre.Z - pose.Position.Z) * fz;
            return centreDepth - (Math.Abs(fx) * halfExtents.X + Math.Abs(fy) * halfExtents.Y + Math.Abs(fz) * halfExtents.Z);
        }

        static StabiliserSettings Settings(in LayerSettings s, float depth) => new StabiliserSettings { SnapPosition = true, QuantiseRotation = s.QuantiseRotation, ReferenceDepth = depth };

        static bool Changed(float previous, float now) => Math.Abs(now / (double)previous - 1.0) > GridTolerance;
    }
}
