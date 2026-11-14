using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Game.Rendering.Pixel
{
    /// <summary>
    /// Marks the present camera (culling mask Nothing, present renderer): it shows the internal image of
    /// <see cref="source"/> at integer scale. UI belongs on this camera, at output resolution.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class PixelPresentCamera : MonoBehaviour
    {
        static readonly Dictionary<Camera, PixelPresentCamera> s_Cameras = new Dictionary<Camera, PixelPresentCamera>();
        public PixelCameraBinder source;
        /// <summary>Negative control for the metric evidence (a filtered upscale); never set by game code.</summary>
        [NonSerialized] public bool controlBilinear;
        Camera m_Camera;

        public static bool TryGet(Camera camera, out PixelPresentCamera present) => s_Cameras.TryGetValue(camera, out present) && present != null && present.isActiveAndEnabled;
        void OnEnable() { m_Camera = GetComponent<Camera>(); s_Cameras[m_Camera] = this; }
        void OnDisable() { if (m_Camera != null && s_Cameras.TryGetValue(m_Camera, out var p) && p == this) s_Cameras.Remove(m_Camera); }
    }
}
