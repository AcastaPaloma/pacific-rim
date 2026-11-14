using Game.Core;

namespace Game.Cameras
{
    /// <summary>Versions of the Camera module's contracts.</summary>
    public static class CameraContractVersions
    {
        public const int Rig = 1;
        public const int ShakeImpulseEvent = 1;
        public const int RigSettings = 1;
    }

    /// <summary>Feature-flag names (Assets/Game/Config/FeatureFlags/Camera.json). All ship false.</summary>
    public static class CameraFlags
    {
        public const string ThirdPersonRig = "camera.thirdPersonRig";
        public const string SnapPosition = "camera.snapPosition";
        public const string QuantiseRotation = "camera.quantiseRotation";
        public const string FootfallImpulse = "camera.footfallImpulse";
        public static readonly string[] All = { ThirdPersonRig, SnapPosition, QuantiseRotation, FootfallImpulse };
    }

    public struct CameraVec3
    {
        public float X, Y, Z;
        public CameraVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
    }

    /// <summary>
    /// A camera pose in world space: position (m), yaw about +Y in degrees (0 looks along +Z, positive turns right)
    /// and pitch in degrees (positive looks up). Yaw is not wrapped: it may exceed 360.
    /// </summary>
    public struct CameraPose
    {
        public CameraVec3 Position;
        public float YawDeg, PitchDeg;
    }

    /// <summary>What a rig follows: a point and a size, supplied by whoever owns the unit (no unit types here).</summary>
    public interface ICameraTarget
    {
        /// <summary>Ground position of the target (m) and its height (m); false when there is nothing to follow.</summary>
        bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres);
    }

    /// <summary>The swappable camera rig (D-006, MODULARITY §4.2): ThirdPersonRig now, a cockpit rig later.</summary>
    public interface ICameraRig
    {
        string RigId { get; }
        /// <summary>The pose the rig wants this frame, before stabilisation.</summary>
        CameraPose WantedPose { get; }
        /// <summary>The pose the camera renders from (snapped to the pixel grid when stabilisation is on).</summary>
        CameraPose RenderedPose { get; }
        void SetTarget(ICameraTarget target);
        /// <summary>Marks a teleport or camera cut: the next frame restarts grid, dither origin and smoothing.</summary>
        void Cut();
    }

    /// <summary>
    /// Camera impulse request (F-009 footfall coupling), published on the Core bus. The Camera module subscribes by
    /// this contract only and knows nothing about mechs: at integration a small adapter turns the mech module's foot
    /// contact event into this one (source entity, world contact point, impulse magnitude in newton-seconds).
    /// </summary>
    public struct CameraShakeImpulse : IEvent
    {
        public EntityId Source;
        public float OriginX, OriginY, OriginZ;   // world position of the contact (m)
        public float ImpulseNs;                    // magnitude of the contact impulse (N·s)
    }
}
