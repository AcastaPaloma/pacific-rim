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
        static RenderPipelineAsset s_Previous;   // static: keeps the object reachable for the unused-asset scan
        readonly string m_PreviousPath;
        readonly bool m_HadPrevious;
        bool m_Disposed;

        public PixelPipelineScope(RenderPipelineAsset pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            s_Previous = QualitySettings.renderPipeline;
            m_HadPrevious = s_Previous != null;
            m_PreviousPath = m_HadPrevious ? AssetDatabase.GetAssetPath(s_Previous) : null;
            QualitySettings.renderPipeline = pipeline;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            RenderPipelineAsset previous = s_Previous;
            if (m_HadPrevious && previous == null && !string.IsNullOrEmpty(m_PreviousPath))
                previous = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(m_PreviousPath);
            if (m_HadPrevious && previous == null)
                Debug.LogError("[Pixel] could not restore the previous render pipeline asset (" + m_PreviousPath + ")");
            QualitySettings.renderPipeline = previous;
            s_Previous = null;
        }
    }
}
