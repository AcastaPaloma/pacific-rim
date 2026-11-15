using System;
using UnityEngine;

namespace Game.Cameras
{
    /// <summary>Tuning values of the third-person rig, read from Assets/Game/Config/Camera/ThirdPersonRig.json (schema 1). All Assumed, pending the feel panel.</summary>
    [Serializable]
    public sealed class ThirdPersonRigSettings
    {
        public int schema = CameraContractVersions.RigSettings;
        public float horizontalFovDeg = 60f;
        public float distanceInTargetHeights = 3.1f;   // an 80 m target then fills about half the frame height
        public float focusHeightFraction = 0.55f;      // the rig looks at this fraction of the target's height
        public float pitchDeg = 4f;                    // looking slightly up: low horizon, large sky
        public float followLagSeconds = 0.6f;
        public float minReferenceDepthM = 20f;
        public float skylineDepthM = 664.874f;         // layerSnap main slab reference depth (T1 skyline design depth)
        public float farDepthM = 10353f;               // layerSnap far slab reference depth (T1 far design depth)
        public float nearEdgeOffsetInTargetHeights = 0f; // layerSnap near slab depth = focus depth minus this many target heights (the target's nearest silhouette, T-187); 0 = focus depth
        public float impulseMetresPerMegaNs = 0.015f;
        public float impulseDegreesPerMegaNs = 0.012f;
        public float impulseMaxMetres = 0.5f;
        public float impulseMaxDegrees = 0.5f;
        public float impulseDecaySeconds = 1.2f;
        public float impulseFrequencyHz = 2f;
        public float impulseFalloffDistanceM = 300f;

        public static ThirdPersonRigSettings FromJson(string json)
        {
            var s = JsonUtility.FromJson<ThirdPersonRigSettings>(json);
            if (s != null)
                // Code defaults duplicate the config; a missing or misspelt key would silently fall back to them (DEF-T004-arch-5).
                foreach (var f in typeof(ThirdPersonRigSettings).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    if (json.IndexOf("\"" + f.Name + "\"", StringComparison.Ordinal) < 0)
                        Debug.LogWarning("ThirdPersonRig.json: key '" + f.Name + "' is missing; using the code default " + f.GetValue(s));
            if (s == null || s.schema != CameraContractVersions.RigSettings) throw new FormatException("ThirdPersonRig.json: schema must be " + CameraContractVersions.RigSettings);
            if (s.horizontalFovDeg <= 1f || s.horizontalFovDeg >= 170f || s.distanceInTargetHeights <= 0f) throw new FormatException("ThirdPersonRig.json: value out of range");
            return s;
        }

        public ImpulseSettings Impulse => new ImpulseSettings
        {
            MetresPerMegaNs = impulseMetresPerMegaNs, DegreesPerMegaNs = impulseDegreesPerMegaNs, MaxMetres = impulseMaxMetres, MaxDegrees = impulseMaxDegrees,
            DecaySeconds = impulseDecaySeconds, FrequencyHz = impulseFrequencyHz, FalloffDistance = impulseFalloffDistanceM,
        };
    }
}
