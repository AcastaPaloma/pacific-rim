using System.Collections.Generic;
using UnityEngine;

namespace Game.Core.Unity
{
    /// <summary>
    /// Points at the committed config files in Assets/Game/Config so editor and player load the same bytes
    /// through Resources. Holds no values itself. In the editor the flag file list is re-read from the folder at
    /// every boot and before every build (CoreConfigBuildHook), so adding a module's flag file needs no manual step.
    /// </summary>
    public sealed class CoreConfigAsset : ScriptableObject
    {
        public const string ResourcesName = "GameCoreConfig";
        public const string AssetPath = "Assets/Game/Core/Unity/Resources/" + ResourcesName + ".asset";
        public const string FeatureFlagsFolder = "Assets/Game/Config/FeatureFlags";
        public const string CoreSettingsPath = "Assets/Game/Config/CoreSettings.json";

        [SerializeField] private TextAsset[] featureFlagFiles = new TextAsset[0];
        [SerializeField] private TextAsset coreSettings;

        public string CoreSettingsText => coreSettings != null ? coreSettings.text : null;

        /// <summary>(asset path, JSON text) per flag file; FeatureFlagLoader.Merge orders them.</summary>
        public List<KeyValuePair<string, string>> FeatureFlagSources()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var t in featureFlagFiles)
                if (t != null) list.Add(new KeyValuePair<string, string>(FeatureFlagsFolder + "/" + t.name + ".json", t.text));
            return list;
        }

        public static CoreConfigAsset Load() => Resources.Load<CoreConfigAsset>(ResourcesName);

#if UNITY_EDITOR
        /// <summary>Editor/batch entry point: -executeMethod Game.Core.Unity.CoreConfigAsset.CreateOrUpdate</summary>
        public static void CreateOrUpdate()
        {
            string folder = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                UnityEditor.AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder).Replace('\\', '/'), System.IO.Path.GetFileName(folder));
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<CoreConfigAsset>(AssetPath);
            if (asset == null)
            {
                asset = CreateInstance<CoreConfigAsset>();
                UnityEditor.AssetDatabase.CreateAsset(asset, AssetPath);
            }
            if (asset.RefreshFromFolder()) UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[Core] CoreConfigAsset at " + AssetPath + ": " + asset.featureFlagFiles.Length + " flag file(s)");
        }

        /// <summary>Re-reads the flag folder and the settings file. Returns true when the asset changed.</summary>
        public bool RefreshFromFolder()
        {
            var files = new List<TextAsset>();
            var paths = new List<string>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TextAsset", new[] { FeatureFlagsFolder }))
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".json", System.StringComparison.Ordinal) && System.IO.Path.GetDirectoryName(p).Replace('\\', '/') == FeatureFlagsFolder) paths.Add(p);
            }
            paths.Sort(string.CompareOrdinal);
            foreach (var p in paths) files.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(p));
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(CoreSettingsPath);
            bool changed = settings != coreSettings || files.Count != featureFlagFiles.Length;
            for (int i = 0; !changed && i < files.Count; i++) changed = files[i] != featureFlagFiles[i];
            if (!changed) return false;
            featureFlagFiles = files.ToArray();
            coreSettings = settings;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>Keeps the Resources pointer current before every player build.</summary>
    internal sealed class CoreConfigBuildHook : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => CoreConfigAsset.CreateOrUpdate();
    }
#endif
}
