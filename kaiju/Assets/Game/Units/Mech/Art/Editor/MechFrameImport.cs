using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Units.Mech.Art.Editor
{
    /// <summary>
    /// Imports the Balanced mech greybox (T-007) and builds its prefab, materials and part map.
    /// Idempotent: a second run leaves every file byte-identical (git diff empty).
    ///
    ///   unity run &lt;kaiju&gt; --no-tail --log-file &lt;log&gt; --timeout 900 -- -executeMethod Game.Units.Mech.Art.Editor.MechFrameImport.Run
    ///
    /// Source of the model: art/models/mech_frame_balanced/mech_frame_balanced.fbx (built by
    /// Pipelines/blender/mech_frame_balanced/build_greybox.py). Dimensions: Assets/Game/Config/Mech/mech_frame_balanced.json.
    /// </summary>
    public static class MechFrameImport
    {
        public const string ArtDir = "Assets/Game/Units/Mech/Art";
        public const string ModelPath = ArtDir + "/Models/mech_frame_balanced.fbx";
        public const string PrefabPath = ArtDir + "/Prefabs/MechFrameBalanced.prefab";
        public const string MapPath = ArtDir + "/Data/MechFrameBalanced_PartMap.asset";
        public const string MaterialDir = ArtDir + "/Materials";
        public const string ConfigPath = "Assets/Game/Config/Mech/mech_frame_balanced.json";
        public const string PartPrefix = "MechBalanced_";
        public const string SocketPrefix = "MechBalanced_socket_";
        public static readonly string[] Roles = { "primary", "secondary", "joint", "accent" };

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static string SourceFbx => Path.Combine(ProjectRoot, "art/models/mech_frame_balanced/mech_frame_balanced.fbx");
        static string PaletteJson => Path.Combine(ProjectRoot, "Pipelines/blender/mech_frame_balanced/palette_roles.json");
        public static string EvidenceDir => Path.Combine(ProjectRoot, "evidence/T-007");

        [MenuItem("Game/Mech/Rebuild Balanced Art (T-007)")]
        public static void Run()
        {
            try
            {
                Build();
                Debug.Log("[MechFrameImport] done");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        public static void Build()
        {
            foreach (var d in new[] { "Models", "Materials", "Prefabs", "Data" })
                EnsureFolder(ArtDir + "/" + d);

            CopyModel();
            var mats = BuildMaterials();
            ConfigureImporter(mats);
            var cfg = MechFrameConfig.Parse(AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigPath).text);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new InvalidOperationException("model did not import: " + ModelPath);
            var map = BuildMap(cfg, model);
            BuildPrefab(model, map);
            AssetDatabase.SaveAssets();
            WriteHierarchyListing();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void CopyModel()
        {
            var dst = Path.Combine(ProjectRoot, ModelPath);
            var src = File.ReadAllBytes(SourceFbx);
            if (!File.Exists(dst) || !File.ReadAllBytes(dst).SequenceEqual(src))
            {
                File.WriteAllBytes(dst, src);
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
            }
        }

        [Serializable] class RoleJson { public string hex; public string source; }
        [Serializable] class RolesJson { public RoleJson primary, secondary, joint, accent; }
        [Serializable] class PaletteFile { public string schema; public string id; public RolesJson roles; }

        static Dictionary<string, Material> BuildMaterials()
        {
            var pal = JsonUtility.FromJson<PaletteFile>(File.ReadAllText(PaletteJson));
            var hex = new Dictionary<string, string>
            {
                { "primary", pal.roles.primary.hex }, { "secondary", pal.roles.secondary.hex },
                { "joint", pal.roles.joint.hex }, { "accent", pal.roles.accent.hex },
            };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader not found");
            var mats = new Dictionary<string, Material>();
            foreach (var role in Roles)
            {
                var path = $"{MaterialDir}/mech_{role}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader) { name = "mech_" + role };
                    AssetDatabase.CreateAsset(m, path);
                }
                ColorUtility.TryParseHtmlString(hex[role], out var c);
                bool changed = m.shader != shader || m.GetColor("_BaseColor") != c
                               || !Mathf.Approximately(m.GetFloat("_Smoothness"), 0.1f) || !Mathf.Approximately(m.GetFloat("_Metallic"), 0f);
                if (changed)
                {
                    m.shader = shader;
                    m.SetColor("_BaseColor", c);
                    m.SetColor("_Color", c);
                    m.SetFloat("_Smoothness", 0.1f);
                    m.SetFloat("_Metallic", 0f);
                    EditorUtility.SetDirty(m);
                }
                mats[role] = m;
            }
            return mats;
        }

        static void ConfigureImporter(Dictionary<string, Material> mats)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            bool dirty = false;
            void Set<T>(Func<T> get, Action<T> set, T value)
            {
                if (!EqualityComparer<T>.Default.Equals(get(), value)) { set(value); dirty = true; }
            }
            Set(() => imp.globalScale, v => imp.globalScale = v, 1f);
            Set(() => imp.useFileScale, v => imp.useFileScale = v, true);
            Set(() => imp.bakeAxisConversion, v => imp.bakeAxisConversion = v, true);
            Set(() => imp.preserveHierarchy, v => imp.preserveHierarchy = v, true);
            Set(() => imp.sortHierarchyByName, v => imp.sortHierarchyByName = v, false);
            Set(() => imp.importAnimation, v => imp.importAnimation = v, false);
            Set(() => imp.animationType, v => imp.animationType = v, ModelImporterAnimationType.None);
            Set(() => imp.importBlendShapes, v => imp.importBlendShapes = v, false);
            Set(() => imp.importCameras, v => imp.importCameras = v, false);
            Set(() => imp.importLights, v => imp.importLights = v, false);
            Set(() => imp.importVisibility, v => imp.importVisibility = v, false);
            Set(() => imp.addCollider, v => imp.addCollider = v, false);
            Set(() => imp.meshCompression, v => imp.meshCompression = v, ModelImporterMeshCompression.Off);
            Set(() => imp.isReadable, v => imp.isReadable = v, false);
            // faceted normals recomputed from the triangle winding after the Z-up axis bake (flat-shaded greybox)
            Set(() => imp.importNormals, v => imp.importNormals = v, ModelImporterNormals.Calculate);
            Set(() => imp.normalSmoothingAngle, v => imp.normalSmoothingAngle = v, 0f);
            Set(() => imp.importTangents, v => imp.importTangents = v, ModelImporterTangents.None);
            Set(() => imp.weldVertices, v => imp.weldVertices = v, true);
            Set(() => imp.materialImportMode, v => imp.materialImportMode = v, ModelImporterMaterialImportMode.ImportStandard);
            Set(() => imp.materialLocation, v => imp.materialLocation = v, ModelImporterMaterialLocation.InPrefab);
            var remaps = imp.GetExternalObjectMap();
            foreach (var role in Roles)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "mech_" + role);
                if (!remaps.TryGetValue(id, out var cur) || cur != mats[role]) { imp.AddRemap(id, mats[role]); dirty = true; }
            }
            if (dirty) imp.SaveAndReimport();
        }

        static MechPartMap BuildMap(MechFrameConfig cfg, GameObject model)
        {
            var map = AssetDatabase.LoadAssetAtPath<MechPartMap>(MapPath);
            bool isNew = map == null;
            if (isNew) map = ScriptableObject.CreateInstance<MechPartMap>();
            var root = model.transform;
            var all = root.GetComponentsInChildren<Transform>(true);
            var parts = new List<MechPartMap.Part>();
            foreach (var seg in cfg.segments)
            {
                var name = PartPrefix + seg.id;
                var t = all.FirstOrDefault(x => x.name == name);
                if (t == null) throw new InvalidOperationException("model has no part " + name);
                parts.Add(new MechPartMap.Part
                {
                    segmentId = seg.id, partName = name, parentSegmentId = seg.IsRoot ? "" : seg.parent,
                    path = MechPartMap.PathOf(root, t),
                });
            }
            var sockets = new List<MechPartMap.Socket>();
            foreach (var t in all.Where(x => x.name.StartsWith(SocketPrefix)).OrderBy(x => x.name, StringComparer.Ordinal))
            {
                var rest = t.name.Substring(SocketPrefix.Length);           // e.g. hardpoint_L
                var us = rest.LastIndexOf('_');
                sockets.Add(new MechPartMap.Socket
                {
                    socketName = t.name, kind = rest.Substring(0, us), side = rest.Substring(us + 1),
                    parentSegmentId = t.parent.name.Substring(PartPrefix.Length), path = MechPartMap.PathOf(root, t),
                });
            }
            var cfgAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigPath);
            bool same = !isNew && map.frameId == cfg.id && map.frameConfig == cfgAsset && map.schema == MechFrameConfig.SchemaV1
                        && map.parts.SequenceEqual(parts) && map.sockets.SequenceEqual(sockets);
            if (!same)
            {
                map.schema = MechFrameConfig.SchemaV1;
                map.frameId = cfg.id;
                map.frameConfig = cfgAsset;
                map.parts = parts;
                map.sockets = sockets;
                if (isNew) AssetDatabase.CreateAsset(map, MapPath);
                EditorUtility.SetDirty(map);
            }
            return map;
        }

        /// <summary>Socket orientation convention: +Z points the way a mounted part extends.</summary>
        public static Quaternion SocketRotation(string kind, string side)
        {
            float s = side == "L" ? -1f : 1f;
            switch (kind)
            {
                case "shoulder":
                case "hardpoint": return Quaternion.LookRotation(new Vector3(s, 0, 0), Vector3.up);
                default: return Quaternion.LookRotation(Vector3.down, Vector3.forward);   // hip, elbow
            }
        }

        static void BuildPrefab(GameObject model, MechPartMap map)
        {
            GameObject root;
            bool exists = File.Exists(Path.Combine(ProjectRoot, PrefabPath));
            root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                foreach (var s in map.sockets)
                {
                    var t = root.transform.Find(s.path);
                    var q = SocketRotation(s.kind, s.side);
                    if (Quaternion.Angle(t.localRotation, q) > 1e-3f) t.localRotation = q;
                }
                var art = root.GetComponent<MechArtRoot>();
                if (art == null) art = root.AddComponent<MechArtRoot>();
                if (art.partMap != map) art.partMap = map;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static int TriangleCount(Mesh m)
        {
            long n = 0;
            for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return (int)(n / 3);
        }

        static void WriteHierarchyListing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var sb = new StringBuilder();
            sb.AppendLine($"# {PrefabPath} (written by MechFrameImport.Run; local position m, local euler deg, lossy scale)");
            void Walk(Transform t, int depth)
            {
                var p = t.localPosition; var e = t.localEulerAngles; var sc = t.lossyScale;
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<MeshRenderer>();
                string mesh = mf ? $" mesh={mf.sharedMesh.name} tris={TriangleCount(mf.sharedMesh)}" : "";
                string mats = mr ? " mats=" + string.Join(",", mr.sharedMaterials.Select(m => m ? m.name : "null")) : "";
                sb.AppendLine($"{new string(' ', depth * 2)}{t.name} pos=({p.x:F3},{p.y:F3},{p.z:F3}) rot=({e.x:F1},{e.y:F1},{e.z:F1}) scale=({sc.x:F3},{sc.y:F3},{sc.z:F3}){mesh}{mats}");
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
            }
            Walk(prefab.transform, 0);
            Directory.CreateDirectory(EvidenceDir);
            File.WriteAllText(Path.Combine(EvidenceDir, "unity_hierarchy.txt"), sb.ToString());
        }
    }
}
