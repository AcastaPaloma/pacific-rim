using System;
using Game.Control.Contracts;
using UnityEngine;
using UnityEngine.InputSystem;
using NVec3 = System.Numerics.Vector3;

namespace Game.Control.Unity
{
    /// <summary>
    /// A local player's devices (Input System gamepad or keyboard) as an <see cref="IPilotSource"/>. Interchangeable with ScriptedPilotSource: the seat cannot tell them apart.
    /// The mech position and view heading come from delegates supplied by the composition root, so this class knows no body and no camera.
    /// </summary>
    public sealed class DevicePilotSource : IPilotSource
    {
        readonly ControlTuning _tuning;
        readonly Func<NVec3> _origin;
        readonly Func<float> _viewYaw;
        readonly Func<Vector2> _stick;
        readonly Func<bool> _stop;
        double _stopHeld;

        /// <param name="stick">where the stick sample comes from; default: the current gamepad's left stick plus WASD</param>
        /// <param name="stop">the stop button; default: gamepad east button or the space bar</param>
        public DevicePilotSource(ControlTuning tuning, Func<NVec3> origin, Func<float> viewYaw, Func<Vector2> stick = null, Func<bool> stop = null)
        {
            if (tuning == null || tuning.schema != ControlTuning.Schema) throw new InvalidOperationException("control tuning schema is not " + ControlTuning.Schema);
            _tuning = tuning; _origin = origin; _viewYaw = viewYaw; _stick = stick ?? ReadDevices; _stop = stop ?? ReadStopDevices;
        }

        static Vector2 ReadDevices()
        {
            var pad = Gamepad.current; var keys = Keyboard.current; Vector2 stick = Vector2.zero;
            if (pad != null) stick = pad.leftStick.ReadValue();
            if (keys != null)
            {
                if (keys.wKey.isPressed) stick.y += 1; if (keys.sKey.isPressed) stick.y -= 1;
                if (keys.dKey.isPressed) stick.x += 1; if (keys.aKey.isPressed) stick.x -= 1;
            }
            return Vector2.ClampMagnitude(stick, 1f);
        }
        static bool ReadStopDevices() => (Gamepad.current != null && Gamepad.current.buttonEast.isPressed) || (Keyboard.current != null && Keyboard.current.spaceKey.isPressed);

        public static ControlTuning LoadTuning(TextAsset json) => JsonUtility.FromJson<ControlTuning>(json.text);

        public PilotInput Read()
        {
            Vector2 stick = _stick(); bool stop = _stop();
            _stopHeld = stop ? _stopHeld + Time.fixedDeltaTime : 0;
            return StickMapping.Map(stick.x, stick.y, stop, _stopHeld >= _tuning.stop_button_emergency_hold_s, _origin(), _viewYaw(), _tuning);
        }
    }
}
