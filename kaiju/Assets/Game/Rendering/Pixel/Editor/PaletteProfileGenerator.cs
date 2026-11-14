using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Rendering.Pixel.Editor
{
    /// <summary>
    /// Generates the PaletteProfile assets from palette_set@v1 (reconciled spec C16). Hex strings are authoritative.
    /// Run: unity run &lt;project&gt; -- -executeMethod Game.Rendering.Pixel.Editor.PaletteProfileGenerator.GenerateAll
    /// </summary>
    public static class PaletteProfileGenerator
    {
        public const string ConfigFolder = "Assets/Game/Config/Rendering";
        public const string PaletteSetPath = ConfigFolder + "/palette_set.json";
        public const string TuningPath = ConfigFolder + "/PaletteProfileTuning.json";
        public const string ProfilesFolder = ConfigFolder + "/PaletteProfiles";

        [MenuItem("Game/Rendering/Pixel/Generate palette profiles")]
        public static void GenerateAll()
        {
            var profiles = Generate();
            AssetDatabase.SaveAssets();
            Debug.Log("[Pixel] generated " + profiles.Count + " palette profiles in " + ProfilesFolder);
        }

        /// <summary>Creates or updates one asset per profile of the palette set and bakes its LUT. Existing assets keep their GUID.</summary>
        public static List<PaletteProfile> Generate()
        {
            PixelPipelineAssets.EnsureFolder(ProfilesFolder);
            string tuning = File.Exists(TuningPath) ? File.ReadAllText(TuningPath) : null;
            var result = new List<PaletteProfile>();
            foreach (PaletteProfileData data in PaletteSetReader.Read(File.ReadAllText(PaletteSetPath)))
            {
                string path = ProfilesFolder + "/" + data.Id + ".asset";
                var profile = AssetDatabase.LoadAssetAtPath<PaletteProfile>(path);
                if (profile == null)
                {
                    profile = ScriptableObject.CreateInstance<PaletteProfile>();
                    AssetDatabase.CreateAsset(profile, path);
                }
                Fill(profile, data, tuning);
                EditorUtility.SetDirty(profile);
                result.Add(profile);
            }
            return result;
        }

        /// <summary>A profile that exists only in memory (tests, the sixth-profile check): same code path, no asset.</summary>
        public static PaletteProfile CreateInMemory(PaletteProfileData data, string tuningJson)
        {
            var profile = ScriptableObject.CreateInstance<PaletteProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            Fill(profile, data, tuningJson);
            return profile;
        }

        static void Fill(PaletteProfile profile, PaletteProfileData data, string tuningJson)
        {
            profile.name = data.Id;
            profile.SetData(data);
            PaletteProfileTuning.Apply(profile, tuningJson);
            profile.Bake();
        }

        [MenuItem("Game/Rendering/Pixel/Bake selected palette profile")]
        public static void BakeSelected()
        {
            foreach (Object o in Selection.objects)
                if (o is PaletteProfile p) { p.Bake(); Debug.Log("[Pixel] baked " + p.profileId + " lut " + p.lutSha256); }
            AssetDatabase.SaveAssets();
        }
    }
}
