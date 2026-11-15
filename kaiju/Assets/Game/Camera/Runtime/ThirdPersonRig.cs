using System;
using Game.Core;
using Game.Rendering.Pixel;
using UnityEngine;

namespace Game.Cameras
{
    /// <summary>
    /// Third-person rig (D-006, MODULARITY §4.2). Follows an <see cref="ICameraTarget"/> at a distance scaled to the
    /// target's height, or renders a scripted pose (capture sequences). Every wanted pose goes through
    /// <see cref="CameraStabiliser"/>: the camera renders from the texel-snapped pose and the rig offers the remainder
    /// to the pixel pipeline as <see cref="IPixelStabilisationSource"/> (contract in Rendering.Pixel.Contracts, so the
    /// pipeline never references this module). Camera impulses arrive as <see cref="CameraShakeImpulse"/> events on
    /// the Core bus. The rig depends on contracts only: it finds the pixel grid through <see cref="IPixelGridSource"/>
    /// on the same GameObject.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class ThirdPersonRig : MonoBehaviour, ICameraRig, IPixelStabilisationSource, IEventHandler<CameraShakeImpulse>
    {
        public const string Id = "ThirdPersonRig";

        [Tooltip("Assets/Game/Config/Camera/ThirdPersonRig.json")]
        public TextAsset settingsJson;

        ThirdPersonRigSettings m_Settings;
        CameraImpulse m_Impulse;
        ICameraTarget m_Target;
        IPixelGridSource m_GridSource;
        IEventBus m_Bus;
        Camera m_Camera;
        bool m_SnapPosition, m_QuantiseRotation, m_ImpulseEnabled;
        bool m_Scripted, m_Cut = true, m_HasFocus, m_HasHints;
        CameraPose m_ScriptedPose, m_Wanted, m_Rendered;
        float m_ScriptedReferenceDepth;
        double m_FocusX, m_FocusY, m_FocusZ;
        float m_OrbitYawDeg, m_ReferenceDepth;
        PixelStabilisationHints m_Hints;
        SnapState m_Snap;
        LayerSnapState m_LayerSnap;
        bool m_LayerSnapOn;
        float m_LayerMainDepth, m_LayerFarDepth;   // 0 = take the settings value

        public string RigId => Id;
        public CameraPose WantedPose => m_Wanted;
        public CameraPose RenderedPose => m_Rendered;
        public bool SnapPosition => m_SnapPosition;
        public bool QuantiseRotation => m_QuantiseRotation;
        public float ReferenceDepth => m_ReferenceDepth;
        /// <summary>True when the rig fills the v2 layer hints (flag rendering.pixel.layerSnap).</summary>
        public bool LayerSnap => m_LayerSnapOn;
        public CameraImpulse Impulse => m_Impulse ?? (m_Impulse = new CameraImpulse(Settings.Impulse));

        public ThirdPersonRigSettings Settings
        {
            get
            {
                if (m_Settings == null) m_Settings = settingsJson != null ? ThirdPersonRigSettings.FromJson(settingsJson.text) : new ThirdPersonRigSettings();
                return m_Settings;
            }
        }

        /// <summary>Sets the tuning values and which stabilisers and couplings are on (from feature flags, by the composition root).</summary>
        public void Configure(ThirdPersonRigSettings settings, bool snapPosition, bool quantiseRotation, bool footfallImpulse)
        {
            m_Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            m_Impulse = new CameraImpulse(settings.Impulse);
            m_SnapPosition = snapPosition; m_QuantiseRotation = quantiseRotation; m_ImpulseEnabled = footfallImpulse;
        }

        /// <summary>Turns layer snap (hints v2) on or off. Off is the default and leaves the v1 path untouched. Turning it off or on restarts every lattice.</summary>
        public void ConfigureLayers(bool layerSnap) { if (layerSnap != m_LayerSnapOn) m_LayerSnap = default; m_LayerSnapOn = layerSnap; }

        /// <summary>Overrides the main and far slab reference depths (capture sequences, for example 663.0 for the reference-class edge); 0 restores the settings values.</summary>
        public void SetScriptedLayerReferences(float mainDepth, float farDepth) { m_LayerMainDepth = mainDepth; m_LayerFarDepth = farDepth; }

        /// <summary>Play Mode wiring through the Core registry: flags, the event bus subscription, and registration as the active rig.</summary>
        public void Bind(IServiceRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (registry.TryResolve<IFeatureFlags>(out var flags))
                { Configure(Settings, flags.IsOn(CameraFlags.SnapPosition), flags.IsOn(CameraFlags.QuantiseRotation), flags.IsOn(CameraFlags.FootfallImpulse)); ConfigureLayers(flags.IsOn(PixelFlags.LayerSnap)); }
            Subscribe(registry.Resolve<IEventBus>());
            if (!registry.IsRegistered<ICameraRig>()) registry.Register<ICameraRig>(this);
            if (!registry.IsRegistered<IPixelStabilisationSource>()) registry.Register<IPixelStabilisationSource>(this);
        }

        /// <summary>Subscribes to camera impulse events on a bus (event contract only).</summary>
        public void Subscribe(IEventBus bus)
        {
            Unsubscribe();
            m_Bus = bus ?? throw new ArgumentNullException(nameof(bus));
            m_Bus.Subscribe(this);
        }

        public void Unsubscribe()
        {
            if (m_Bus != null) m_Bus.Unsubscribe(this);
            m_Bus = null;
        }

        public void SetTarget(ICameraTarget target) { m_Target = target; }
        public void SetGridSource(IPixelGridSource source) { m_GridSource = source; }
        public void Cut() { m_Cut = true; }
        public void SetOrbitYaw(float yawDeg) { m_OrbitYawDeg = yawDeg; }
        public float OrbitYawDeg => m_OrbitYawDeg;

        /// <summary>Renders this pose instead of following the target (capture sequences). The reference depth is the plane the position snap locks.</summary>
        public void SetScriptedPose(CameraPose pose, float referenceDepth) { m_Scripted = true; m_ScriptedPose = pose; m_ScriptedReferenceDepth = referenceDepth; }
        public void ClearScriptedPose() { m_Scripted = false; }

        public void Handle(in CameraShakeImpulse evt)
        {
            if (!m_ImpulseEnabled) return;
            Vector3 p = transform.position;
            float dx = evt.OriginX - p.x, dy = evt.OriginY - p.y, dz = evt.OriginZ - p.z;
            Impulse.Add(evt.ImpulseNs, Mathf.Sqrt(dx * dx + dy * dy + dz * dz));
        }

        public bool TryGetHints(out PixelStabilisationHints hints) { hints = m_Hints; return m_HasHints; }

        /// <summary>Advances the rig by <paramref name="dt"/> seconds and places the camera. LateUpdate calls it in Play Mode; the capture tool calls it with a fixed step.</summary>
        public void Tick(float dt)
        {
            if (m_Camera == null) m_Camera = GetComponent<Camera>();
            if (m_GridSource == null) m_GridSource = GetComponent<IPixelGridSource>();
            var s = Settings;
            bool cut = m_Cut; m_Cut = false;

            PixelGrid grid = default;
            bool hasGrid = m_GridSource != null && m_GridSource.TryGetGrid(out grid);
            if (hasGrid)
            {
                // Square texels: the vertical field of view follows from the horizontal one and the internal aspect.
                float aspect = grid.InternalWidth / (float)grid.InternalHeight;
                m_Camera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(s.horizontalFovDeg * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
                hasGrid = m_GridSource.TryGetGrid(out grid);
            }

            if (m_Scripted) { m_Wanted = m_ScriptedPose; m_ReferenceDepth = m_ScriptedReferenceDepth; }
            else if (m_Target != null && m_Target.TryGetTarget(out CameraVec3 ground, out float height))
            {
                double fx = ground.X, fy = ground.Y + height * s.focusHeightFraction, fz = ground.Z;
                if (cut || !m_HasFocus) { m_FocusX = fx; m_FocusY = fy; m_FocusZ = fz; m_HasFocus = true; }
                else
                {
                    m_FocusX = RigFraming.Approach(m_FocusX, fx, s.followLagSeconds, dt);
                    m_FocusY = RigFraming.Approach(m_FocusY, fy, s.followLagSeconds, dt);
                    m_FocusZ = RigFraming.Approach(m_FocusZ, fz, s.followLagSeconds, dt);
                }
                float distance = height * s.distanceInTargetHeights;
                m_Wanted = RigFraming.Orbit(new CameraVec3((float)m_FocusX, (float)m_FocusY, (float)m_FocusZ), m_OrbitYawDeg, s.pitchDeg, distance);
                m_ReferenceDepth = Mathf.Max(s.minReferenceDepthM, distance);
            }
            else
            {
                Vector3 e = transform.eulerAngles;
                m_Wanted = new CameraPose { Position = new CameraVec3(transform.position.x, transform.position.y, transform.position.z), YawDeg = e.y, PitchDeg = -Mathf.DeltaAngle(0f, e.x) };
                m_ReferenceDepth = s.minReferenceDepthM;
            }

            var wanted = m_Wanted;
            if (m_ImpulseEnabled)
            {
                if (cut) Impulse.Reset(); else Impulse.Advance(dt);
                wanted.Position.Y += Impulse.OffsetMetres; wanted.PitchDeg += Impulse.OffsetDegrees;
            }

            if (hasGrid)
            {
                var settings = new StabiliserSettings { SnapPosition = m_SnapPosition, QuantiseRotation = m_QuantiseRotation, ReferenceDepth = m_ReferenceDepth };
                StabilisedPose result;
                var layers = new LayerSettings
                {
                    SnapPosition = m_SnapPosition, QuantiseRotation = m_QuantiseRotation, NearDepth = m_ReferenceDepth,
                    MainDepth = m_LayerMainDepth > 0f ? m_LayerMainDepth : s.skylineDepthM, FarDepth = m_LayerFarDepth > 0f ? m_LayerFarDepth : s.farDepthM,
                };
                if (m_LayerSnapOn && CameraLayerStabiliser.TryStabilise(wanted, grid, layers, cut, ref m_LayerSnap, out result)) m_Snap = default;
                else result = CameraStabiliser.Stabilise(wanted, grid, settings, cut, ref m_Snap);
                m_Rendered = result.Rendered; m_Hints = result.Hints; m_HasHints = true;
            }
            else { m_Rendered = wanted; m_Hints = default; m_HasHints = false; m_Snap = default; m_LayerSnap = default; }

            transform.SetPositionAndRotation(new Vector3(m_Rendered.Position.X, m_Rendered.Position.Y, m_Rendered.Position.Z), Quaternion.Euler(-m_Rendered.PitchDeg, m_Rendered.YawDeg, 0f));
        }

        void LateUpdate() { if (Application.isPlaying) Tick(Time.deltaTime); }
        void OnDisable() { Unsubscribe(); m_HasHints = false; }
    }

    /// <summary>A simple target for scenes and tests: follows a Transform's position with a fixed height.</summary>
    public sealed class TransformCameraTarget : ICameraTarget
    {
        readonly Transform m_Transform; readonly float m_Height;
        public TransformCameraTarget(Transform transform, float heightMetres) { m_Transform = transform; m_Height = heightMetres; }
        public bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres)
        {
            heightMetres = m_Height;
            if (m_Transform == null) { groundPosition = default; return false; }
            Vector3 p = m_Transform.position; groundPosition = new CameraVec3(p.x, p.y, p.z);
            return true;
        }
    }
}
