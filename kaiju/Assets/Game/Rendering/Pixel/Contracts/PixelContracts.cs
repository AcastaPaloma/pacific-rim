using System;
using System.Collections.Generic;

namespace Game.Rendering.Pixel
{
    /// <summary>Versions of the data contracts this module owns. Bump on any breaking change.</summary>
    public static class PixelContractVersions
    {
        public const int StabilisationHints = 2;   // v2 (T-181b): per-slab layers; a v1 source (LayerCount 0) stays valid
        public const int PaletteProfile = 1;
        public const int PipelineSettings = 1;
        public const string PaletteSet = "palette_set@v1";      // research/R-002
        public const string StyleCapture = "style_capture@v1";  // research/R-002 §6
    }

    /// <summary>The pipeline stages. Each one is a separate URP renderer feature (MODULARITY §4.7).</summary>
    public enum PixelStageId
    {
        Downsample = 0, // box-downsample of the supersampled camera target to the internal resolution
        Dither = 1,     // writes the per-texel ordered-dither threshold (mask + anchoring mode)
        Haze = 2,       // aerial perspective in posterised bands by real radial distance, and the sky gradient
        Outline = 3,    // optional depth-edge outline
        Quantise = 4,   // palette LUT lookup; output is exact palette colours
        Store = 5,      // low-res target: writes the internal colour and linear depth targets
        Present = 6,    // point upscale at integer scale with sub-pixel compensation (present renderer)
    }

    /// <summary>How the dither pattern is anchored (reconciled spec, readings table).</summary>
    public enum PixelDitherAnchor
    {
        Screen = 0,     // fixed to the internal pixel grid
        Direction = 1,  // origin follows the rig's whole-texel rotation steps (direction-locked at the screen centre)
        Hybrid = 2,     // surface-anchored near the camera, direction-locked beyond a distance and in the sky
    }

    /// <summary>Feature-flag names (Assets/Game/Config/FeatureFlags/Rendering.Pixel.json). All ship false.</summary>
    public static class PixelFlags
    {
        public const string Enabled = "rendering.pixel.enabled";
        public const string LowRes = "rendering.pixel.lowRes";
        public const string Supersample = "rendering.pixel.supersample";
        public const string Dither = "rendering.pixel.dither";
        public const string Haze = "rendering.pixel.haze";
        public const string Outline = "rendering.pixel.outline";
        public const string Quantise = "rendering.pixel.quantise";
        public const string SubPixelPresent = "rendering.pixel.subPixelPresent";
        /// <summary>T-181: near / main / far slab cameras, each snapped to its own reference depth. Ships false; ignores depthShift when on.</summary>
        public const string LayerSnap = "rendering.pixel.layerSnap";
        /// <summary>T-180: present shift per surface depth (round(k * zRef / z * remainder)) instead of one uniform shift. Needs subPixelPresent; ignored when layerSnap is on.</summary>
        public const string DepthShift = "rendering.pixel.depthShift";
        /// <summary>T-190: beyond the profile's hazeFarStartM the haze level rises to pure airlight by hazeFarFullM (far lift), so far geometry fades toward the sky and the depth value ladder keeps rising across a real skyline. Ships false; slab boundaries (layerSnap) do not follow it.</summary>
        public const string HazeFarLift = "rendering.pixel.hazeFarLift";

        /// <summary>T-196: with layerSnap, the three slab shifts share one output-pixel lattice phase (one k x k block grid, M-003.d-iv) instead of each rounding its own remainder; costs C11 (at least 1.5 px, PixelSharedPhase). Ships false; a measured option, not enabled in any configuration.</summary>
        public const string LayerSharedPhase = "rendering.pixel.layerSharedPhase";

        /// <summary>T-207: when a building is removed, a short palette-limited dust / debris burst and a collapse flash are drawn as world geometry inside the low-res frame (PixelDestructionFx). Ships false; the player's -sliceDestructionFx 1 turns it on for record mode.</summary>
        public const string DestructionFx = "rendering.pixel.destruction_fx";

        public static readonly string[] All = { Enabled, LowRes, Supersample, Dither, Haze, Outline, Quantise, SubPixelPresent, LayerSnap, DepthShift, HazeFarLift, LayerSharedPhase, DestructionFx };

        /// <summary>The flag that gates a stage, or null for stages that always run when the pipeline is enabled.</summary>
        public static string ForStage(PixelStageId stage)
        {
            switch (stage)
            {
                case PixelStageId.Downsample: return Supersample;
                case PixelStageId.Dither: return Dither;
                case PixelStageId.Haze: return Haze;
                case PixelStageId.Outline: return Outline;
                case PixelStageId.Quantise: return Quantise;
                default: return null;
            }
        }
    }

    /// <summary>
    /// What the pipeline tells a camera rig about the internal pixel grid, so the rig can snap to it.
    /// Angles are radians; distances metres.
    /// </summary>
    public struct PixelGrid
    {
        public int InternalWidth, InternalHeight; // visible internal resolution (without the guard band)
        public int Scale;                          // integer output pixels per internal texel (k)
        public float TexelAngle;                   // angle of one internal texel at the screen centre
        public float TanHalfHorizontalFov, TanHalfVerticalFov; // of the visible (nominal) frustum
    }

    public interface IPixelGridSource
    {
        bool TryGetGrid(out PixelGrid grid);
    }

    /// <summary>
    /// Stabilisation hints, spec v1 (R-003 §10). Produced by the active camera rig, consumed by the pipeline.
    /// The contract lives here (L1) so that Rendering.Pixel never references Camera (L3); the rig provides it.
    /// The rig renders from the snapped pose itself; the pipeline uses the remainder, the dither origin and the cut flag.
    /// </summary>
    public struct PixelStabilisationHints
    {
        /// <summary>Rendered (snapped) camera position, world metres.</summary>
        public float SnappedX, SnappedY, SnappedZ;
        /// <summary>Rendered (quantised) camera rotation, camera-to-world quaternion.</summary>
        public float RotationX, RotationY, RotationZ, RotationW;
        /// <summary>
        /// Remainder between the wanted and the rendered view in internal texels: +X means the wanted view is to the
        /// right of the rendered one, +Y above it. The present stage shifts the image by round(offset * scale) output pixels.
        /// </summary>
        public float SubTexelX, SubTexelY;
        /// <summary>Whole texels accumulated from quantised rotation steps: +X per step of yaw to the right, +Y per step of pitch upward.</summary>
        public int DitherOriginX, DitherOriginY;
        /// <summary>Depth (m) of the plane locked by position snapping (the rig's focus distance).</summary>
        public float ReferenceDepth;
        /// <summary>Size (m) of the position grid at the reference depth; 0 when position snapping is off.</summary>
        public float PositionGrid;
        /// <summary>Rotation step (rad); 0 when rotation quantisation is off.</summary>
        public float RotationStep;
        public float TexelAngle;
        public int PixelScale;
        /// <summary>True on the first frame after a teleport or camera cut: grid, dither origin and history restart.</summary>
        public bool Cut;
        /// <summary>v2 (T-181b): 0 = v1 behaviour (single snap). 3 = LayerNear / LayerMain / LayerFar are valid. No arrays: zero allocation per frame.</summary>
        public int LayerCount;
        public PixelLayerHint LayerNear, LayerMain, LayerFar;
    }

    /// <summary>
    /// One slab of the layer-snap pass (hints v2). The main layer is the rendered pose of the v1 fields (offsets 0, SubTexel and grid equal to the v1 ones);
    /// near and far carry the offset of their own snapped position from the rendered one along the rendered right / up axes (m) and their own remainder in
    /// their own texels. Blittable; Rendering.Pixel never references Camera.
    /// </summary>
    public struct PixelLayerHint
    {
        /// <summary>Eye depth (m) of the plane this slab's snap grid is one texel at.</summary>
        public float ReferenceDepth;
        /// <summary>Position grid (m) at the reference depth: one internal texel there.</summary>
        public float Grid;
        /// <summary>Offset (m) of this slab's snapped position from the rendered pose, along the rendered right and up axes.</summary>
        public float OffsetRight, OffsetUp;
        /// <summary>Remainder in this slab's own texels (+X right, +Y up), as the v1 SubTexel fields.</summary>
        public float SubTexelX, SubTexelY;
        /// <summary>This layer's lattice restarted this frame (its reference depth changed by more than the grid tolerance, or a teleport).</summary>
        public bool Cut;
    }

    public interface IPixelStabilisationSource
    {
        /// <summary>False when the rig offers no hints (for example a cockpit rig): the pipeline then renders unsnapped.</summary>
        bool TryGetHints(out PixelStabilisationHints hints);
    }

    /// <summary>Integer scale rule (reconciled spec C2): k = clamp(round(H / targetLines), kMin, kMax), internal = ceil(output / k).</summary>
    public static class PixelScaleRule
    {
        public static int ChooseScale(int outputHeight, int targetLines, int minScale, int maxScale)
        {
            if (outputHeight <= 0 || targetLines <= 0) throw new ArgumentOutOfRangeException(nameof(outputHeight));
            if (minScale < 1 || maxScale < minScale) throw new ArgumentOutOfRangeException(nameof(minScale));
            int k = (int)Math.Floor(outputHeight / (double)targetLines + 0.5);
            return Math.Min(Math.Max(k, minScale), maxScale);
        }

        public static void InternalSize(int outputWidth, int outputHeight, int scale, out int width, out int height)
        {
            if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));
            width = (outputWidth + scale - 1) / scale;
            height = (outputHeight + scale - 1) / scale;
        }

        /// <summary>Angle (rad) of one internal texel at the screen centre.</summary>
        public static double TexelAngle(double tanHalfVerticalFov, int internalHeight) => Math.Atan(2.0 * tanHalfVerticalFov / internalHeight);
    }

    /// <summary>
    /// Stage-order rules (reconciled spec C15). The installer refuses an illegal order; at run time a stage that finds
    /// itself after a stage it must precede is skipped with a warning, so an illegal order is never silently wrong.
    /// </summary>
    public static class PixelStageOrder
    {
        public static readonly PixelStageId[] Default =
        {
            PixelStageId.Downsample, PixelStageId.Dither, PixelStageId.Haze, PixelStageId.Outline,
            PixelStageId.Quantise, PixelStageId.Store, PixelStageId.Present,
        };

        /// <summary>True when <paramref name="first"/> must run before <paramref name="second"/> whenever both are present.</summary>
        public static bool MustPrecede(PixelStageId first, PixelStageId second, out string reason)
        {
            reason = null;
            if (first == second) return false;
            if (second == PixelStageId.Present) { reason = "Present is the last stage"; return true; }
            if (first == PixelStageId.Present) return false;
            if (second == PixelStageId.Store) { reason = "Store writes the finished internal image, after every colour stage"; return true; }
            if (first == PixelStageId.Store) return false;
            if (first == PixelStageId.Downsample) { reason = "internal-resolution stages run after the downsample"; return true; }
            if (first == PixelStageId.Haze && second == PixelStageId.Quantise) { reason = "haze is applied before palette quantisation"; return true; }
            if (first == PixelStageId.Outline && second == PixelStageId.Quantise) { reason = "outline colours must pass through the palette"; return true; }
            if (first == PixelStageId.Dither && (second == PixelStageId.Haze || second == PixelStageId.Quantise))
            { reason = "the dither threshold must exist before the stages that use it"; return true; }
            return false;
        }

        /// <summary>Returns null when the order is legal, otherwise a message naming the first violation.</summary>
        public static string Validate(IReadOnlyList<PixelStageId> order)
        {
            if (order == null) return "order is null";
            for (int i = 0; i < order.Count; i++)
            {
                if (!Enum.IsDefined(typeof(PixelStageId), order[i])) return "unknown stage " + (int)order[i];
                for (int j = i + 1; j < order.Count; j++)
                {
                    if (order[i] == order[j]) return "stage " + order[i] + " is listed twice";
                    if (MustPrecede(order[j], order[i], out string reason))
                        return order[j] + " must come before " + order[i] + ": " + reason;
                }
            }
            return null;
        }

        /// <summary>Run-time check: may <paramref name="stage"/> run after the stages in <paramref name="executedMask"/> (bit = stage id)?</summary>
        public static bool CanRunAfter(int executedMask, PixelStageId stage, out string reason)
        {
            reason = null;
            if ((executedMask & (1 << (int)stage)) != 0) { reason = stage + " already ran this frame"; return false; }
            for (int s = 0; s <= (int)PixelStageId.Present; s++)
            {
                if ((executedMask & (1 << s)) == 0) continue;
                if (MustPrecede(stage, (PixelStageId)s, out string why))
                { reason = stage + " must come before " + (PixelStageId)s + ": " + why; return false; }
            }
            return true;
        }
    }
}
