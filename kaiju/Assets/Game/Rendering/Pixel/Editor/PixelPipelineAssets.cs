using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Rendering.Pixel.Editor
{
    /// <summary>
    /// Low-level asset operations for the pixel pipeline (R-003 §1.3): creates the dedicated URP asset and renderer
    /// assets and edits renderer-feature lists through SerializedObject, keeping m_RendererFeatures and
    /// m_RendererFeatureMap in step. Existing assets keep their GUID; nothing here touches PC_RPAsset.
    /// </summary>
    public static class PixelPipelineAssets
    {
        public readonly struct FeatureSpec
        {
            public readonly Type Type; public readonly string Name; public readonly Action<ScriptableRendererFeature> Configure;
            public FeatureSpec(Type type, string name, Action<ScriptableRendererFeature> configure = null) { Type = type; Name = name; Configure = configure; }
        }

        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetFolder));
        }

        /// <summary>Loads or creates a UniversalRendererData asset and replaces its feature list with <paramref name="features"/> in order.</summary>
        public static UniversalRendererData CreateOrUpdateRenderer(string path, IReadOnlyList<FeatureSpec> features, Action<UniversalRendererData> configure = null)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(data, path);
                ResourceReloader.ReloadAllNullIn(data, UniversalRenderPipelineAsset.packagePath);
            }
            configure?.Invoke(data);
            SetFeatures(data, features);
            return data;
        }

        public static void SetFeatures(ScriptableRendererData data, IReadOnlyList<FeatureSpec> features)
        {
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            // Already the requested list (same types and names in the same order): only re-apply the configuration, so
            // re-running the installer does not rewrite the asset with new local file IDs.
            bool same = list.arraySize == features.Count && map.arraySize == features.Count;
            for (int i = 0; same && i < features.Count; i++)
            {
                var existing = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                same = existing != null && existing.GetType() == features[i].Type && existing.name == features[i].Name;
            }
            if (same)
            {
                for (int i = 0; i < features.Count; i++) features[i].Configure?.Invoke((ScriptableRendererFeature)list.GetArrayElementAtIndex(i).objectReferenceValue);
                return;
            }
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var old = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (old != null) { AssetDatabase.RemoveObjectFromAsset(old); UnityEngine.Object.DestroyImmediate(old, true); }
            }
            list.arraySize = 0; map.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            foreach (var spec in features)
            {
                var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(spec.Type);
                feature.name = spec.Name;
                feature.hideFlags |= HideFlags.HideInHierarchy;
                spec.Configure?.Invoke(feature);
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                so.Update();
                int n = list.arraySize;
                list.arraySize = n + 1; list.GetArrayElementAtIndex(n).objectReferenceValue = feature;
                map.arraySize = n + 1; map.GetArrayElementAtIndex(n).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            data.SetDirty();
            EditorUtility.SetDirty(data);
        }

        /// <summary>Loads or creates the pixel URP asset with the given renderer list (index = renderer index for SetRenderer).</summary>
        public static UniversalRenderPipelineAsset CreateOrUpdatePipeline(string path, IReadOnlyList<ScriptableRendererData> renderers)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderers[0]);
                AssetDatabase.CreateAsset(asset, path);
            }
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            list.arraySize = renderers.Count;
            for (int i = 0; i < renderers.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            so.FindProperty("m_DefaultRendererIndex").intValue = 0;
            // No MSAA, no HDR-dependent post, no shadows by default: the pixel look replaces anti-aliasing (R-003 §1.4).
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_RenderScale").floatValue = 1f;
            so.FindProperty("m_RequireDepthTexture").boolValue = true;
            so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            so.FindProperty("m_SupportsHDR").boolValue = true;
            // 64-bit colour buffer: the gradient mask compares neighbouring texels, which the 10/11-bit float format would stair-step.
            so.FindProperty("m_HDRColorBufferPrecision").intValue = 1;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
