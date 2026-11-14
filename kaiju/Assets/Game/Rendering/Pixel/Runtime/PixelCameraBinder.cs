using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Game.Rendering.Pixel
{
    /// <summary>Resolution layout of one frame: the visible internal image, its guard band and the camera target.</summary>
    public struct PixelLayout
    {
        public int OutputWidth, OutputHeight;
        public int Scale;        // integer output pixels per internal texel (k)
        public int Width, Height; // visible internal resolution
        public int Guard;        // texels of guard band on each side, so a sub-pixel shift never shows the edge
        public int Supersample;  // camera target pixels per internal texel (1 when the Downsample stage is off)
        public int TargetWidth => Width + 2 * Guard;
        public int TargetHeight => Height + 2 * Guard;
    }

    /// <summary>
    /// Stage 0 of the pipeline (R-003 §10): a component on the world camera. It owns the low-resolution camera target
    /// and the two persistent internal targets (colour with palette index, linear depth), widens the projection to
    /// cover the guard band, holds the active PaletteProfile, settings and flag state, and pulls the stabilisation
    /// hints from whatever rig provides them. The renderer features find it through <see cref="TryGet"/>; a camera
    /// without a binder is not touched by the pixel pipeline.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(10000)]
    public sealed class PixelCameraBinder : MonoBehaviour, IPixelGridSource
    {
        static readonly Dictionary<Camera, PixelCameraBinder> s_Binders = new Dictionary<Camera, PixelCameraBinder>();

        [Tooltip("Generated asset catalog: palette profiles, settings file and the dedicated URP asset.")]
        public PixelPipelineCatalog catalog;
        [Tooltip("Active palette profile (district/time).")]
        public PaletteProfile profile;
        [Tooltip("Output resolution the internal resolution is derived from.")]
        public Vector2Int outputSize = new Vector2Int(1920, 1080);

        /// <summary>Negative controls for the metric evidence; never set by game code.</summary>
        [NonSerialized] public bool controlReversedHaze;
        [NonSerialized] public int controlTemporalNoiseSeed;

        PixelPipelineSettings m_Settings;
        PixelFlagState m_Flags;
        IPixelStabilisationSource m_Source;
        Camera m_Camera;
        RenderTexture m_CameraTarget, m_Colour, m_Depth;
        RTHandle m_ColourHandle, m_DepthHandle;
        PixelLayout m_Layout;
        PixelStabilisationHints m_Hints;
        bool m_HasHints, m_Prepared, m_SourceSet;

        public static bool TryGet(Camera camera, out PixelCameraBinder binder) => s_Binders.TryGetValue(camera, out binder) && binder != null && binder.isActiveAndEnabled;

        public Camera Camera => m_Camera != null ? m_Camera : (m_Camera = GetComponent<Camera>());
        public PixelLayout Layout => m_Layout;
        public bool HasHints => m_HasHints;
        public PixelStabilisationHints Hints => m_Hints;
        public RenderTexture InternalColour => m_Colour;
        public RenderTexture InternalDepth => m_Depth;
        public RTHandle InternalColourHandle => m_ColourHandle;
        public RTHandle InternalDepthHandle => m_DepthHandle;
        public bool IsPrepared => m_Prepared && m_Colour != null;

        public PixelPipelineSettings Settings
        {
            get
            {
                if (m_Settings == null) m_Settings = catalog != null && catalog.settingsJson != null ? catalog.LoadSettings() : new PixelPipelineSettings();
                return m_Settings;
            }
        }

        public PixelFlagState Flags => m_Flags ?? (m_Flags = PixelFlagState.FromNames(Array.Empty<string>()));

        /// <summary>Sets the tuning values and the effective flag state (session flags plus a configuration's enables).</summary>
        public void Configure(PixelPipelineSettings settings, PixelFlagState flags)
        {
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_Flags = flags ?? throw new ArgumentNullException(nameof(flags));
        }

        /// <summary>The rig (or anything else) that provides stabilisation hints; null renders unsnapped.</summary>
        public void SetStabilisationSource(IPixelStabilisationSource source) { m_Source = source; m_SourceSet = true; }

        public bool PipelineEnabled => Flags.IsOn(PixelFlags.Enabled);

        public bool StageEnabled(PixelStageId stage)
        {
            if (!m_Prepared || !Flags.StageEnabled(stage)) return false;
            if (stage == PixelStageId.Quantise || stage == PixelStageId.Haze) return profile != null && profile.IsBaked;
            return true;
        }

        public PixelLayout ComputeLayout()
        {
            var s = Settings;
            var layout = new PixelLayout { OutputWidth = Mathf.Max(1, outputSize.x), OutputHeight = Mathf.Max(1, outputSize.y), Guard = s.guardTexels };
            layout.Scale = Flags.IsOn(PixelFlags.LowRes) ? PixelScaleRule.ChooseScale(layout.OutputHeight, s.targetLines, s.minScale, s.maxScale) : 1;
            PixelScaleRule.InternalSize(layout.OutputWidth, layout.OutputHeight, layout.Scale, out layout.Width, out layout.Height);
            layout.Supersample = Flags.IsOn(PixelFlags.Supersample) ? Mathf.Max(1, s.supersample) : 1;
            return layout;
        }

        public bool TryGetGrid(out PixelGrid grid)
        {
            var layout = ComputeLayout();
            float tanV = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            grid = new PixelGrid
            {
                InternalWidth = layout.Width, InternalHeight = layout.Height, Scale = layout.Scale,
                TanHalfVerticalFov = tanV, TanHalfHorizontalFov = tanV * layout.Width / layout.Height,
                TexelAngle = (float)PixelScaleRule.TexelAngle(tanV, layout.Height),
            };
            return PipelineEnabled;
        }

        /// <summary>
        /// Call once per frame after the rig has placed the camera and before the camera renders (LateUpdate does it in
        /// Play Mode; the capture tool calls it directly): sizes the targets, sets the padded projection, pulls hints.
        /// </summary>
        public void PrepareFrame()
        {
            var cam = Camera;
            if (!PipelineEnabled) { ReleaseTargets(); m_Prepared = false; return; }
            m_Layout = ComputeLayout();
            EnsureTargets();
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * m_Layout.TargetHeight / m_Layout.Height;
            cam.projectionMatrix = Matrix4x4.Perspective(2f * Mathf.Atan(tanV) * Mathf.Rad2Deg, m_Layout.TargetWidth / (float)m_Layout.TargetHeight, cam.nearClipPlane, cam.farClipPlane);
            cam.targetTexture = m_CameraTarget;
            if (m_Source == null && !m_SourceSet) m_Source = GetComponent<IPixelStabilisationSource>();   // a rig on the same camera, found by contract
            m_HasHints = m_Source != null && m_Source.TryGetHints(out m_Hints);
            if (!m_HasHints) m_Hints = default;
            m_Prepared = true;
        }

        /// <summary>Tan of half the field of view of the whole camera target (guard band included).</summary>
        public Vector2 TargetTanHalfFov()
        {
            float tanV = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / m_Layout.Height;
            return new Vector2(tanV * m_Layout.TargetWidth, tanV * m_Layout.TargetHeight);
        }

        /// <summary>Present shift in whole output pixels: round(sub-texel remainder * scale), limited to the guard band.</summary>
        public Vector2Int PresentShift()
        {
            if (!m_HasHints || !Flags.IsOn(PixelFlags.SubPixelPresent)) return Vector2Int.zero;
            int limit = m_Layout.Guard * m_Layout.Scale;
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(m_Hints.SubTexelX * m_Layout.Scale), -limit, limit),
                                  Mathf.Clamp(Mathf.RoundToInt(m_Hints.SubTexelY * m_Layout.Scale), -limit, limit));
        }

        void EnsureTargets()
        {
            int tw = m_Layout.TargetWidth, th = m_Layout.TargetHeight, ss = m_Layout.Supersample;
            if (m_CameraTarget != null && m_CameraTarget.width == tw * ss && m_CameraTarget.height == th * ss && m_Colour != null && m_Colour.width == tw && m_Colour.height == th) return;
            ReleaseTargets();
            m_CameraTarget = new RenderTexture(tw * ss, th * ss, 32, GraphicsFormat.R8G8B8A8_SRGB) { name = "PixelCameraTarget", filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            m_CameraTarget.Create();
            m_Colour = new RenderTexture(tw, th, 0, GraphicsFormat.R8G8B8A8_UNorm) { name = "PixelInternalColour", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            m_Colour.Create();
            m_Depth = new RenderTexture(tw, th, 0, GraphicsFormat.R32_SFloat) { name = "PixelInternalDepth", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            m_Depth.Create();
            m_ColourHandle = RTHandles.Alloc(m_Colour);
            m_DepthHandle = RTHandles.Alloc(m_Depth);
        }

        void ReleaseTargets()
        {
            var cam = Camera;
            if (cam != null && m_CameraTarget != null && cam.targetTexture == m_CameraTarget) { cam.targetTexture = null; cam.ResetProjectionMatrix(); }
            m_ColourHandle?.Release(); m_DepthHandle?.Release(); m_ColourHandle = null; m_DepthHandle = null;
            Release(ref m_CameraTarget); Release(ref m_Colour); Release(ref m_Depth);
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            if (Application.isPlaying) Destroy(rt); else DestroyImmediate(rt);
            rt = null;
        }

        void OnEnable() { s_Binders[Camera] = this; }

        void OnDisable()
        {
            if (m_Camera != null && s_Binders.TryGetValue(m_Camera, out var b) && b == this) s_Binders.Remove(m_Camera);
            ReleaseTargets(); m_Prepared = false;
        }

        void LateUpdate() { if (Application.isPlaying) PrepareFrame(); }
    }
}
