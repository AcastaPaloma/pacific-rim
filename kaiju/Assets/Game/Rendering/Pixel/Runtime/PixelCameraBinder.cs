using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        [NonSerialized] public bool controlContinuousTone;
        /// <summary>Capture-only (T-182): the present stage writes the log-coded eye depth of the texel it shows instead of its colour, so output-resolution edge positions are measured from the shipped present path.</summary>
        [NonSerialized] public bool controlDepthOutput;

        PixelPipelineSettings m_Settings;
        PixelFlagState m_Flags;
        IPixelStabilisationSource m_Source;
        Camera m_Camera;
        RenderTexture m_CameraTarget, m_Colour, m_Depth;
        RTHandle m_ColourHandle, m_DepthHandle;
        PixelLayout m_Layout;
        PixelStabilisationHints m_Hints;
        bool m_HasHints, m_Prepared, m_SourceSet;

        // ---- layer slabs (T-181d, MODULE.md "Layer slabs") ----
        // The layered path renders the world camera once per slab: BeginSlab moves it to the slab's snapped position, narrows its clip range and
        // redirects the Store stage to the slab's own colour / depth targets; CompositeInternal then builds the unshifted internal composite that the capture
        // tool and the metrics read, and the present stage composites the three slabs again at their own shifts.
        PixelSlab[] m_Slabs;
        int[] m_BoundaryBands = { -1, -1 };
        bool m_Layered;
        int m_ActiveSlab = -1;
        readonly RenderTexture[] m_SlabColour = new RenderTexture[PixelLayerPlan.Count], m_SlabDepth = new RenderTexture[PixelLayerPlan.Count];
        readonly RTHandle[] m_SlabColourHandle = new RTHandle[PixelLayerPlan.Count], m_SlabDepthHandle = new RTHandle[PixelLayerPlan.Count];
        readonly HazeTuning m_PlanHaze = new HazeTuning();
        Vector3 m_SavedPosition; float m_SavedNear, m_SavedFar; Matrix4x4 m_SavedProjection; bool m_SavedShadows;
        Material m_CompositeMaterial;
        string m_LayerReason;
        // T-196 (flag layerSharedPhase): one output-pixel lattice phase for the three slab shifts, resolved once per prepared layered frame.
        readonly PixelSharedPhase m_SharedPhase = new PixelSharedPhase();
        readonly int[] m_SharedShifts = new int[2 * PixelLayerPlan.Count];
        bool m_SharedApplied, m_WasLayered;

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

        /// <summary>True for this frame when the hints carried three valid layers, layerSnap is on and the slab plan was built (rendered and presented layered).</summary>
        public bool Layered => m_Layered;
        public PixelSlab[] Slabs => m_Slabs;
        public int[] BoundaryBands => m_BoundaryBands;
        /// <summary>Why the layered path was not used this frame (null when it was, or when the hints simply carry no layers).</summary>
        public string LayerRejection => m_LayerReason;
        public int ActiveSlab => m_ActiveSlab;
        /// <summary>Targets the Store stage writes: the composite targets, or the active slab's while a slab renders.</summary>
        public RTHandle StoreColourHandle => m_ActiveSlab >= 0 ? m_SlabColourHandle[m_ActiveSlab] : m_ColourHandle;
        public RTHandle StoreDepthHandle => m_ActiveSlab >= 0 ? m_SlabDepthHandle[m_ActiveSlab] : m_DepthHandle;
        public RTHandle SlabColourHandle(int slab) => m_SlabColourHandle[slab];
        public RTHandle SlabDepthHandle(int slab) => m_SlabDepthHandle[slab];

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
            // T-196: a shared lattice phase moves a slab up to 1.5 px further than its own remainder, so the slab shifts need two texels of guard band.
            if (Flags.IsOn(PixelFlags.LayerSnap) && Flags.IsOn(PixelFlags.LayerSharedPhase)) layout.Guard = Mathf.Max(layout.Guard, 2);
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
            PrepareLayers();
        }

        /// <summary>
        /// Decides whether this frame renders layered: layerSnap on, three valid layers (grids one texel at their reference depths), a buildable slab plan.
        /// Anything else renders the v1 single-snap path; a rejection is counted in <see cref="PixelLog.LayerHintsRejected"/> and never silently layers with a wrong grid.
        /// </summary>
        void PrepareLayers()
        {
            m_WasLayered = m_Layered;
            m_Layered = false; m_LayerReason = null; m_Slabs = null; m_SharedApplied = false;
            if (!m_HasHints || !Flags.IsOn(PixelFlags.LayerSnap) || m_Hints.LayerCount == 0) return;
            var cam = Camera;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanH = tanV * m_Layout.Width / m_Layout.Height;
            string reason;
            if (!PixelLayerHints.TryValidate(m_Hints, tanV, m_Layout.Height, out reason)) { if (reason != null) Reject(reason); return; }
            bool haze = StageEnabled(PixelStageId.Haze);
            if (haze && Flags.IsOn(PixelFlags.HazeFarLift) && profile != null && profile.hazeFarStartM > 0f) PixelLog.WarnOnce("hazeFarLift: slab boundaries follow the unlifted haze bands");
            if (haze) { var p = profile; m_PlanHaze.HazeStartM = p.hazeStartM; m_PlanHaze.HazeFullM = p.hazeFullM; m_PlanHaze.HazeBands = p.HazeBandCount; m_PlanHaze.HazeCurveGamma = p.hazeCurveGamma; }
            var plan = new PixelLayerPlanSettings { CameraNearM = cam.nearClipPlane, CameraFarM = cam.farClipPlane };
            if (!PixelLayerPlan.TryBuild(PixelLayerHints.ReferenceDepths(m_Hints), haze ? m_PlanHaze : null, tanH, tanV, plan, m_BoundaryBands, out var slabs, out var bands, out reason)) { Reject(reason); return; }
            m_Slabs = slabs; m_BoundaryBands = bands;
            EnsureSlabTargets();
            m_Layered = true;
            ResolveSharedPhase();
        }

        /// <summary>
        /// T-196: with layerSharedPhase (and subPixelPresent) on, the three slab shifts are resolved on one lattice phase (<see cref="PixelSharedPhase"/>); a cut on the hints or on
        /// any layer, or a frame after a non-layered one, restarts the per-shot reference. Off: nothing is kept and the slabs round their own remainders (T-181d).
        /// </summary>
        void ResolveSharedPhase()
        {
            if (!Flags.IsOn(PixelFlags.LayerSharedPhase) || !Flags.IsOn(PixelFlags.SubPixelPresent)) { m_SharedPhase.Reset(); return; }
            bool cut = m_Hints.Cut || m_Hints.LayerNear.Cut || m_Hints.LayerMain.Cut || m_Hints.LayerFar.Cut || !m_WasLayered;
            m_SharedPhase.Resolve(m_Hints, m_Layout.Scale, m_Layout.Guard * m_Layout.Scale, cut, m_SharedShifts);
            m_SharedApplied = true;
        }

        /// <summary>True for this frame when the slab shifts share one lattice phase (layerSharedPhase on and the frame is layered).</summary>
        public bool SharedPhaseApplied => m_SharedApplied;

        void Reject(string reason) { m_LayerReason = reason; PixelLog.LayerHintsRejected++; PixelLog.WarnOnce("layerSnap: " + reason + "; rendering the single-snap path"); }

        /// <summary>
        /// Moves the world camera to slab <paramref name="slab"/> (its snapped position, its clip range, shadows on the near slab only) and redirects the Store stage to the slab's
        /// targets. The caller renders the camera, then calls <see cref="EndSlabs"/> after the last slab. Returns false when this frame is not layered.
        /// </summary>
        public bool BeginSlab(int slab)
        {
            if (!m_Layered || m_ActiveSlab >= 0 || slab < 0 || slab >= PixelLayerPlan.Count) return false;
            var cam = Camera; var t = cam.transform;
            m_SavedPosition = t.position; m_SavedNear = cam.nearClipPlane; m_SavedFar = cam.farClipPlane; m_SavedProjection = cam.projectionMatrix;
            var h = PixelLayerHints.Get(m_Hints, slab); var sl = m_Slabs[slab];
            t.position = m_SavedPosition + t.right * h.OffsetRight + t.up * h.OffsetUp;
            cam.nearClipPlane = (float)sl.ClipNearM; cam.farClipPlane = (float)sl.ClipFarM;
            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * m_Layout.TargetHeight / m_Layout.Height;
            cam.projectionMatrix = Matrix4x4.Perspective(2f * Mathf.Atan(tanV) * Mathf.Rad2Deg, m_Layout.TargetWidth / (float)m_Layout.TargetHeight, cam.nearClipPlane, cam.farClipPlane);
            // The pixel URP asset's shadow distance (50 m) lies inside the near slab: only that slab renders shadows (MODULE.md; asserted in the tests).
            var data = cam.GetUniversalAdditionalCameraData();
            m_SavedShadows = data.renderShadows; data.renderShadows = m_SavedShadows && slab == PixelLayerPlan.Near;
            m_ActiveSlab = slab;
            return true;
        }

        /// <summary>Restores the camera after the slabs (position, clip range, projection, shadows).</summary>
        public void EndSlabs()
        {
            if (m_ActiveSlab < 0) return;
            var cam = Camera;
            cam.transform.position = m_SavedPosition;
            cam.nearClipPlane = m_SavedNear; cam.farClipPlane = m_SavedFar; cam.projectionMatrix = m_SavedProjection;
            cam.GetUniversalAdditionalCameraData().renderShadows = m_SavedShadows;
            m_ActiveSlab = -1;
        }

        /// <summary>Present shift (whole output pixels) of a slab: round(own remainder * scale), limited to the guard band; zero when sub-pixel compensation is off. The main slab's equals <see cref="PresentShift"/>.</summary>
        public Vector2Int SlabShift(int slab)
        {
            if (!m_HasHints || !m_Layered || !Flags.IsOn(PixelFlags.SubPixelPresent)) return Vector2Int.zero;
            if (m_SharedApplied) return new Vector2Int(m_SharedShifts[2 * slab], m_SharedShifts[2 * slab + 1]);
            PixelLayerHints.ShiftOutputPx(PixelLayerHints.Get(m_Hints, slab), m_Layout.Scale, m_Layout.Guard, out int x, out int y);
            return new Vector2Int(x, y);
        }

        /// <summary>The <c>layers</c> sidecar member for this frame (<see cref="PixelLayerSidecar"/>), or null when the frame is not layered.</summary>
        public string LayersJson()
        {
            if (!m_Layered) return null;
            var s = new int[6];
            for (int i = 0; i < PixelLayerPlan.Count; i++) { var v = SlabShift(i); s[2 * i] = v.x; s[2 * i + 1] = v.y; }
            return PixelLayerSidecar.Build(m_Hints, m_Slabs, m_BoundaryBands, s, m_Layout.Scale, m_Layout.Guard, Flags.IsOn(PixelFlags.SubPixelPresent), Flags.IsOn(PixelFlags.DepthShift), m_SharedApplied);
        }

        /// <summary>
        /// Builds the unshifted internal composite (colour with palette index, eye depth) from the three slab targets with the present stage's composite rule at zero shift.
        /// Each slab's snapped position is within half its own texel of the rendered pose, so the composite is registered to under half a texel of the single render.
        /// </summary>
        public void CompositeInternal()
        {
            if (!m_Layered) return;
            if (m_CompositeMaterial == null)
            {
                var shader = Shader.Find("Hidden/Game/Pixel/Composite") ?? throw new InvalidOperationException("shader Hidden/Game/Pixel/Composite not found (add it to Always Included Shaders for players)");
                m_CompositeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            var m = m_CompositeMaterial;
            for (int i = 0; i < PixelLayerPlan.Count; i++) { m.SetTexture(PixelIds.SlabColour[i], m_SlabColour[i]); m.SetTexture(PixelIds.SlabDepth[i], m_SlabDepth[i]); }
            Vector2 tan = TargetTanHalfFov();
            m.SetVector(PixelIds.SlabShift2, new Vector4(0, 0, (float)m_Slabs[1].OwnFromM, (float)m_Slabs[2].OwnFromM));
            m.SetVector(PixelIds.SlabInfo, new Vector4(m_Layout.TargetWidth, m_Layout.TargetHeight, tan.x, tan.y));
            var previous = RenderTexture.active;
            Graphics.SetRenderTarget(m_Colour); m.SetPass(0); Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            Graphics.SetRenderTarget(m_Depth); m.SetPass(1); Graphics.DrawProceduralNow(MeshTopology.Triangles, 3);
            RenderTexture.active = previous;
        }

        void EnsureSlabTargets()
        {
            int tw = m_Layout.TargetWidth, th = m_Layout.TargetHeight;
            if (m_SlabColour[0] != null && m_SlabColour[0].width == tw && m_SlabColour[0].height == th) return;
            ReleaseSlabTargets();
            for (int i = 0; i < PixelLayerPlan.Count; i++)
            {
                m_SlabColour[i] = new RenderTexture(tw, th, 0, GraphicsFormat.R8G8B8A8_UNorm) { name = "PixelSlabColour" + i, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                m_SlabColour[i].Create();
                m_SlabDepth[i] = new RenderTexture(tw, th, 0, GraphicsFormat.R32_SFloat) { name = "PixelSlabDepth" + i, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                m_SlabDepth[i].Create();
                m_SlabColourHandle[i] = RTHandles.Alloc(m_SlabColour[i]); m_SlabDepthHandle[i] = RTHandles.Alloc(m_SlabDepth[i]);
            }
        }

        void ReleaseSlabTargets()
        {
            for (int i = 0; i < PixelLayerPlan.Count; i++)
            {
                m_SlabColourHandle[i]?.Release(); m_SlabDepthHandle[i]?.Release(); m_SlabColourHandle[i] = null; m_SlabDepthHandle[i] = null;
                Release(ref m_SlabColour[i]); Release(ref m_SlabDepth[i]);
            }
        }

        /// <summary>Tan of half the field of view of the whole camera target (guard band included).</summary>
        public Vector2 TargetTanHalfFov()
        {
            float tanV = Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / m_Layout.Height;
            return new Vector2(tanV * m_Layout.TargetWidth, tanV * m_Layout.TargetHeight);
        }

        /// <summary>Present shift in whole output pixels: round(sub-texel remainder * scale), limited to the guard band. With a shared slab phase (T-196) the main slab's resolved shift, which every slab equals modulo the scale.</summary>
        public Vector2Int PresentShift()
        {
            if (!m_HasHints || !Flags.IsOn(PixelFlags.SubPixelPresent)) return Vector2Int.zero;
            if (m_SharedApplied) return SlabShift(PixelLayerPlan.Main);
            int limit = m_Layout.Guard * m_Layout.Scale;
            return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(m_Hints.SubTexelX * m_Layout.Scale), -limit, limit),
                                  Mathf.Clamp(Mathf.RoundToInt(m_Hints.SubTexelY * m_Layout.Scale), -limit, limit));
        }

        /// <summary>
        /// Depth-aware present shift (T-180): x = reference depth (m), y/z = remainder (texels, unrounded), w = clamp (output pixels). Zero w means off:
        /// the flag is off, there are no hints or reference depth, subPixelPresent is off, or layerSnap is on (which ignores depthShift, one warning).
        /// </summary>
        public Vector4 PresentDepthShift()
        {
            if (!m_HasHints || !Flags.IsOn(PixelFlags.DepthShift) || !Flags.IsOn(PixelFlags.SubPixelPresent) || !(m_Hints.ReferenceDepth > 0f)) return Vector4.zero;
            if (Flags.IsOn(PixelFlags.LayerSnap)) { PixelLog.WarnOnce("depthShift ignored: layerSnap is on"); return Vector4.zero; }
            return new Vector4(m_Hints.ReferenceDepth, m_Hints.SubTexelX, m_Hints.SubTexelY, m_Layout.Guard * m_Layout.Scale);
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
            ReleaseSlabTargets(); m_Layered = false; m_ActiveSlab = -1;
            if (m_CompositeMaterial != null) { if (Application.isPlaying) Destroy(m_CompositeMaterial); else DestroyImmediate(m_CompositeMaterial); m_CompositeMaterial = null; }
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
