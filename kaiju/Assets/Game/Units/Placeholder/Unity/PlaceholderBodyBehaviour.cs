// TEMPORARY (T-030c): thin Unity shell around PlaceholderBody. Inert until a composition root calls Configure.
using Game.Cameras;
using Game.Control.Contracts;
using UnityEngine;

namespace Game.Units.Placeholder.Unity
{
    /// <summary>
    /// Drives a capsule transform from a <see cref="PlaceholderBody"/>. The latest <see cref="IntentFrame"/> comes from
    /// <see cref="SetIntent"/> (the composition root wires MechSeatBehaviour.IntentReady to it); FixedUpdate steps the body once and
    /// writes the pose. Also the <see cref="ICameraTarget"/> for the camera rig. No scene wiring; nothing finds it by type.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderBodyBehaviour : MonoBehaviour, ICameraTarget
    {
        public PlaceholderBody Body { get; private set; }
        public IntentFrame Intent { get; private set; }

        public void Configure(PlaceholderBody body)
        {
            Body = body;
            if (body != null) body.Place(transform.position.x, transform.position.y, transform.position.z, transform.eulerAngles.y * Mathf.Deg2Rad);
        }

        public void SetIntent(IntentFrame frame) { Intent = frame; }

        /// <summary>One simulation step (FixedUpdate passes Time.fixedDeltaTime; tests pass their own).</summary>
        public void Tick(float dt)
        {
            if (Body == null) return;
            Body.Step(Intent, dt);
            transform.SetPositionAndRotation(new Vector3(Body.X, Body.Y, Body.Z), Quaternion.Euler(0f, Body.Heading * Mathf.Rad2Deg, 0f));
        }

        public bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres)
        {
            if (Body == null) { groundPosition = default; heightMetres = 0f; return false; }
            return Body.TryGetTarget(out groundPosition, out heightMetres);
        }

        void FixedUpdate() => Tick(Time.fixedDeltaTime);
    }
}
