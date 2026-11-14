using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Rendering.Pixel.Editor
{
    /// <summary>
    /// Makes a render pipeline asset active for the duration of an Editor tool or test and restores the previous one.
    /// The previous asset is remembered by asset path as well as by reference: opening a scene unloads unused assets,
    /// which would turn a plain reference into null and leave ProjectSettings/QualitySettings.asset pointing at nothing.
    /// </summary>
    public sealed class PixelPipelineScope : IDisposable
    {
        // Static: keeps the previous assets reachable for the unused-asset scan while scopes are open (scopes may nest).
        static readonly System.Collections.Generic.List<RenderPipelineAsset> s_KeepAlive = new System.Collections.Generic.List<RenderPipelineAsset>();
        readonly RenderPipelineAsset m_Previous;
        readonly string m_PreviousPath;
        readonly bool m_HadPrevious;
        bool m_Disposed;

        public PixelPipelineScope(RenderPipelineAsset pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            m_Previous = QualitySettings.renderPipeline;
            m_HadPrevious = m_Previous != null;
            m_PreviousPath = m_HadPrevious ? AssetDatabase.GetAssetPath(m_Previous) : null;
            if (m_HadPrevious) s_KeepAlive.Add(m_Previous);
            QualitySettings.renderPipeline = pipeline;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            RenderPipelineAsset previous = m_Previous;
            if (m_HadPrevious && previous == null && !string.IsNullOrEmpty(m_PreviousPath))
                previous = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(m_PreviousPath);
            if (m_HadPrevious && previous == null)
                Debug.LogError("[Pixel] could not restore the previous render pipeline asset (" + m_PreviousPath + ")");
            QualitySettings.renderPipeline = previous;
            if (m_HadPrevious) s_KeepAlive.Remove(m_Previous);
        }
    }
}
