// M1 slice scene bootstrap (T-039b). Loads the 15-tile district through the composition root with the slice config's flags
// (never the committed ones), then adds TEMPORARY stand-ins: a kinematic kaiju driven by ScriptedKaijuBrain, a temporary mech body,
// a placeholder impactor, a key-triggered quake. Everything here is behind `slice.m1`; nothing runs without it.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.AI;
using Game.Combat;
using Game.Audio.SliceBridge;
using Game.Audio.SliceBridge.Unity;
using Game.Cameras;
using Game.Composition;
using Game.Core;
using Game.Control;
using Game.Control.Contracts;
using Game.Control.Unity;
using Game.Core.Unity;
using Game.Units.Placeholder.Unity;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Unity;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Unity;
using Game.Rendering.Pixel;
using Game.World.Destruction;
using Game.World.Geo;
using Game.World.Geo.Unity;
using Game.World.Seismic;
using Unity.Profiling;
using UnityEngine;
using EntityId = Game.Core.EntityId;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Slice
{
    public sealed class SliceBootstrap : MonoBehaviour, IEventHandler<BuildingDamaged>, IEventHandler<EmergencePoint>
    {
        [Tooltip("Slice flag set (Assets/Game/Slice/Config/slice_m1.flags.json): the only flags this scene turns on.")]
        public TextAsset sliceFlags;
        [Tooltip("T-189 feel-check flag set (Assets/Game/Slice/Config/slice_feelcheck.flags.json): the default; -sliceConfig m1 (or SliceBootstrap.ConfigName = \"m1\") uses sliceFlags instead.")]
        public TextAsset feelcheckFlags;
        [Tooltip("T-200 wake-up demo flag set (Assets/Game/Slice/Config/slice_demo.flags.json): -sliceConfig demo. Adds the physics mech (Units.Mech, walk_mode 2).")]
        public TextAsset demoFlags;
        [Tooltip("Mech config files (Assets/Game/Config/Mech/mech_frame_balanced.json, body_tuning.json, walk_tuning.json): demo only; a player has no Application.dataPath copy.")]
        public TextAsset mechFrameJson, mechBodyJson, mechWalkJson, mechWalkFastJson;   // T-201 overlay: demo walks at ~5.8 m/s; -sliceWalk default keeps 2.67 m/s
        public TextAsset cameraSettings;
        [Tooltip("Assets/Game/Config/Control/control_tuning.json")]
        public TextAsset controlTuning;
        [Tooltip("Assets/Game/Config/WorldGeo.json: Geo reads it from Application.dataPath, which a player does not have, so the scene carries it.")]
        public TextAsset geoSettings;
        public PixelPipelineCatalog pixelCatalog;
        public string paletteProfileId = "warm_amber_haze";
        public Material kaijuMaterial, mechMaterial, impactorMaterial;

        /// <summary>Smoke mode: run this many frames (after warm-up) with a scripted sequence, write evidence, then quit. Set by -sliceSmoke N or a test.</summary>
        public static int SmokeFrames;
        public static string SmokeOut;
        /// <summary>"feelcheck" (default when the scene carries the file) or "m1" (the lean T-039b set). Set by -sliceConfig or a test.</summary>
        public static string ConfigName;
        public static SliceBootstrap Instance { get; private set; }
        public bool Ready { get; private set; }
        public bool SmokeDone { get; private set; }
        public string SmokeJson { get; private set; }

        // tiles: GG i25-27 j36-38 and SoMa i29-31 j35-36 (the M-018 district, 15 tiles)
        static readonly GeoTileId GgRef = GeoTileId.From(0, 26, 37), SomaRef = GeoTileId.From(0, 30, 36);
        public const double ImpactMassKg = 1.5e6, ImpactSpeedMs = 150.0;

        CoreRoot _root;
        FeatureFlagSnapshot _flags;
        GameCompositionResult _comp;
        GeoTileSetBuilder _builder;
        IEventBus _bus;
        SliceCombatWorld _combatWorld; SliceFeelRig _feel; string _configUsed = "m1";
        SliceAudioBehaviour _audio;   // T-188: only when audio.slice is on
        readonly Dictionary<BuildingId, (double x, double z)> _audioPos = new Dictionary<BuildingId, (double x, double z)>();
        readonly SliceBuildingIndex _index = new SliceBuildingIndex();
        readonly List<SliceFootprint> _footprints = new List<SliceFootprint>();   // Unity-frame building footprints for the spawn planner
        float _spawnYawDeg;
        public const float MechHeightM = 80f;   // Units.Placeholder body height (PlaceholderLimits.HeightM): the rig orbits at 3.1 x this
        readonly Queue<BuildingId> _removals = new Queue<BuildingId>();
        readonly List<SliceTarget> _targets = new List<SliceTarget>();
        readonly int[] _stateCounts = new int[5];
        int _removedCount, _damagedEvents, _quakes, _impacts, _nextEventId = 1;
        Camera _cam; ThirdPersonRig _rig; PixelCameraBinder _binder;
        // T-200 demo: the physics mech (Units.Mech, walk_mode 2). _body stays as the TEMPORARY logic proxy for damage, cannon mount and camera target; it is placed on the mech's centre of mass every step.
        BodyWorld _bodyWorld; MechUnit _mechUnit; long _mechStep; GameObject _mechVisRoot; double _mechComTravel; float _mechComX, _mechComZ; bool _mechComSet;
        Transform _kaijuT, _mechT; PlaceholderBodyBehaviour _body; PilotSeat _seat; Func<Vector2> _autoStick; SliceKaijuAgent _kaiju; SliceTarget _mech;
        Transform _impactor; Vector3 _impactVel; BuildingId _impactTarget; Vector3 _impactAim;
        readonly StringBuilder _log = new StringBuilder();
        string _lastMsg = "";
        double _mechTravel; float _mechMinY = float.PositiveInfinity, _mechMaxSpeed; double _mechFellAt = -1; double _mechTraceNext; float _mechMaxY = float.NegativeInfinity; double _mechTeleportM; float _lastMX, _lastMZ; bool _haveLastM;
        double _loadSeconds; int _tileCount, _buildingCount, _landmarkColliders;
        // smoke
        int _frame; readonly List<float> _ms = new List<float>(); readonly List<double> _draw = new List<double>(); ProfilerRecorder _drawRec;
        public bool AutoPilot; float _readyAt;

        double OriginX => _builder.FloatingOriginX; double OriginZ => _builder.FloatingOriginZ;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        /// <summary>Diagnostics only: -sliceFlagOff a,b turns those flags off for this run (e.g. rendering.pixel.haze), to bisect a render problem.</summary>
        static string ApplyFlagOverrides(string json)
        {
            string off = Arg("-sliceFlagOff");
            if (json == null || string.IsNullOrEmpty(off)) return json;
            foreach (var name in off.Split(',')) { json = json.Replace("\"" + name + "\": true", "\"" + name + "\": false"); Debug.Log("[Slice] diagnostic override: " + name + " off"); }
            return json;
        }

        IEnumerator Start()
        {
            Instance = this;
            string sm = Arg("-sliceSmoke"); if (sm != null && int.TryParse(sm, out int n)) SmokeFrames = n;
            string so = Arg("-sliceSmokeOut"); if (so != null) SmokeOut = so;
            if (!CoreRuntime.IsBooted) { Debug.LogError("[Slice] Core is not booted"); yield break; }
            _root = CoreRuntime.Root;
            string cfg = Arg("-sliceConfig") ?? ConfigName ?? "feelcheck";
            var cfgAsset = cfg == "m1" || feelcheckFlags == null ? sliceFlags : feelcheckFlags;
            _configUsed = cfgAsset == feelcheckFlags && feelcheckFlags != null ? "feelcheck" : "m1";
            if (cfg == "demo") { if (demoFlags != null) { cfgAsset = demoFlags; _configUsed = "demo"; } else Debug.LogError("[Slice] -sliceConfig demo but the scene has no demoFlags asset (run SliceEditorTools.BuildScene); using " + _configUsed); }
            _flags = FeatureFlagLoader.Parse(ApplyFlagOverrides(cfgAsset != null ? cfgAsset.text : null), _configUsed == "demo" ? "slice_demo.flags.json" : _configUsed == "feelcheck" ? "slice_feelcheck.flags.json" : "slice_m1.flags.json", _root.Errors);
            if (!_flags.IsOn(SliceFlags.M1)) { Debug.Log("[Slice] slice.m1 is off: nothing loaded"); yield break; }
            _bus = _root.Registry.Resolve<IEventBus>();
            var t0 = Time.realtimeSinceStartupAsDouble;

            SetupPixelPipeline();
            // 1. tiles (staged, one per frame), colliders: terrain + landmarks only (per-building colliders cost minutes; hits use the data index)
            var ids = new List<GeoTileId>();
            for (int i = 25; i <= 27; i++) for (int j = 36; j <= 38; j++) ids.Add(GeoTileId.From(0, i, j));
            for (int i = 29; i <= 31; i++) for (int j = 35; j <= 36; j++) ids.Add(GeoTileId.From(0, i, j));
            var absent = GeoTileSetLoader.Absent(GeoModule.DefaultTilesRoot, ids);
            if (absent.Count > 0) { Debug.LogError("[Slice] tile folders absent under " + GeoModule.DefaultTilesRoot + ": " + string.Join(", ", absent)); yield break; }
            var set = GeoTileSetLoader.Load(GeoModule.DefaultTilesRoot, ids, verifyHashes: false);
            _builder = new GeoTileSetBuilder(set, GgRef, LoadGeoSettings()) { BuildingColliders = false, TerrainCollider = true };
            while (!_builder.Done) { _builder.BuildNext(); yield return null; }
            _builder.ShiftOrigin(SomaRef.OriginX, SomaRef.OriginZ);
            _tileCount = _builder.Instances.Count;
            foreach (var inst in _builder.Instances) if (inst.LandmarksRoot != null) _landmarkColliders += inst.LandmarksRoot.GetComponentsInChildren<Collider>().Length;
            yield return null;

            // 2. composition root with the slice flags
            var inputs = new GameCompositionInputs
            {
                Faults = BuildFaults,
                Districts = () => new DistrictRefs { GGX = GgRef.OriginX + 1024, GGZ = GgRef.OriginZ + 1024, SoMaX = SomaRef.OriginX + 1024, SoMaZ = SomaRef.OriginZ + 1024, TargetGG = false },
                Step = () => _root.Services.Clock.Step,
            };
            _combatWorld = new SliceCombatWorld((x, z) => _comp != null && _comp.Geo != null ? Ground(x, z) : 0.0);
            if (_flags.IsOn(CombatFlags.Cannon) || _flags.IsOn(CombatFlags.Melee)) _root.Registry.Register<ICombatTargetQuery>(_combatWorld);   // T-189: real Combat sweeps the slice's volumes
            _comp = GameComposition.Install(_root.Registry, _flags, _root.Errors, inputs);
            Log("config " + _configUsed + ": install: geo=" + (_comp.Geo != null) + " destruction=" + (_comp.Destruction != null) + " seismic=" + (_comp.Seismic != null)
                + " bridge=" + (_comp.ShakingBridge != null) + " brain=" + (_comp.Brain != null)
                + " combat=" + (_comp.Combat != null) + " hangar=" + (_comp.Hangar != null) + " audio=" + _flags.IsOn(SliceAudioFlags.Slice) + " emergence=" + _flags.IsOn("emergence.enabled"));
            if (_comp.Geo == null || _comp.Destruction == null) { Debug.LogError("[Slice] composition did not install Geo/Destruction: " + _lastMsg); yield break; }
            foreach (var inst in _builder.Instances) _comp.Geo.Add(inst);
            _bus.Subscribe<BuildingDamaged>(this); _bus.Subscribe<EmergencePoint>(this);
            _audio = SliceAudioInstaller.TryInstall(_flags, _bus, LocateForAudio);
            RegisterBuildings();
            yield return null;

            // 3. actors, camera, light
            SpawnActors();
            SetupCamera();
            _loadSeconds = Time.realtimeSinceStartupAsDouble - t0;
            Log("ready in " + _loadSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s: tiles " + _tileCount + ", buildings " + _buildingCount + ", landmark colliders " + _landmarkColliders);
            Debug.Log("[Slice] " + _lastMsg);
            if (Arg("-sliceShaderProbe") != null) SpawnShaderProbe();
            LogGeoDiag();
            Ready = true; _readyAt = Time.timeSinceLevelLoad;
            AutoPilot = SmokeFrames > 0;
            if (SmokeFrames > 0) _drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        }

        /// <summary>Diagnostics (-sliceShaderProbe 1): bright cubes in front of the camera, one per shader, so a capture shows which shaders draw in the player.</summary>
        void SpawnShaderProbe()
        {
            string[] names = { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit", "Sprites/Default", "Hidden/Internal-Colored" };
            for (int i = 0; i < names.Length; i++)
            {
                var sh = Shader.Find(names[i]);
                Debug.Log("[Slice] probe shader '" + names[i] + "': " + (sh == null ? "NOT FOUND" : "found, supported=" + sh.isSupported));
                if (sh == null) continue;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Probe" + i;
                var c = cube.GetComponent<Collider>(); if (c != null) Destroy(c);
                var m = new Material(sh); m.color = new Color(1f, 0.1f + 0.2f * i, 0.1f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
                cube.GetComponent<Renderer>().sharedMaterial = m;
                cube.transform.localScale = new Vector3(20f, 20f, 20f);
                cube.transform.position = new Vector3(1024f - 60f + 30f * i, Ground(1024, 900) + 25f, 880f);
            }
        }

        /// <summary>One line per tile: the first terrain/building renderers' material, shader, mesh and bounds, so a blank player frame shows whether the world was built and what it draws with.</summary>
        void LogGeoDiag()
        {
            foreach (var inst in _builder.Instances)
            {
                int n = 0;
                foreach (var r in inst.Renderers)
                {
                    if (r == null || n >= 3) continue; n++;
                    var mf = r.GetComponent<MeshFilter>(); var m = r.sharedMaterial;
                    Debug.Log("[Slice] geo " + inst.TileName + " renderer '" + r.name + "' enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy + " layer=" + r.gameObject.layer + " mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount + "v" : "none")
                        + " material=" + (m != null ? m.name + " shader=" + (m.shader != null ? m.shader.name + " supported=" + m.shader.isSupported : "null") + " queue=" + m.renderQueue : "NULL") + " bounds=" + r.bounds.center + " " + r.bounds.size);
                }
                if (n == 0) Debug.Log("[Slice] geo " + inst.TileName + " has no renderers");
            }
        }

        /// <summary>The tile faults layers are empty by design in this district (nearest trace 5-8 km away), so the traces come from the baked bay-wide file (BayTM).</summary>
        System.Collections.Generic.IReadOnlyDictionary<string, double> LoadGeoSettings()
        {
            if (!Application.isEditor && geoSettings != null)
            {
                string tmp = Path.Combine(Application.temporaryCachePath, "WorldGeo.json");
                File.WriteAllText(tmp, geoSettings.text);
                return GeoSettings.Load(tmp);
            }
            return GeoTileBuilder.LoadSettings();
        }

        FaultTraceSet BuildFaults()
        {
            // the production loader (T-036c): keeps the baked fault_id and verifies the layer's manifest hash (DEF-M1slice-f4)
            string path = Path.Combine(Application.streamingAssetsPath, "Geo/global/v1/faults_bay.geojson");
            return FaultTraceSet.Build(FaultTraceSet.PiecesFromGeo(GeoFaultsLoader.LoadGlobal(path), 0.0, 0.0));
        }

        bool LocateForAudio(BuildingId id, out double x, out double y, out double z)
        {
            if (_audioPos.TryGetValue(id, out var p)) { x = p.x; z = p.z; y = 10.0; return true; }
            x = y = z = 0; return false;
        }

        void RegisterBuildings()
        {
            var all = new List<KeyValuePair<BuildingId, (GeoBuildingData b, GeoTileInstance t)>>();
            foreach (var inst in _builder.Instances)
                foreach (var b in inst.Model.Buildings)
                    if (b.Excluded == null && b.HeightM > 0 && !b.PartOf.IsValid) all.Add(new KeyValuePair<BuildingId, (GeoBuildingData, GeoTileInstance)>(b.Id, (b, inst)));
            all.Sort((p, q) => p.Key.CompareTo(q.Key));   // sorted ids make the bridge's sorted insert an append
            foreach (var kv in all)
            {
                var b = kv.Value.b; var ctx = kv.Value.t.Model.Context;
                double x = ctx.OriginX + b.AnchorLocalX, z = ctx.OriginZ + b.AnchorLocalZ;
                _comp.Destruction.Register(b.Id, BuildingAttributesFromGeo.From(b));
                _comp.ShakingBridge?.AddBuilding(b.Id, x, z);
                _index.Add(b.Id, x, z, Math.Sqrt(Math.Max(b.AreaM2, 20.0)) * 0.6);
                if (_audio != null) _audioPos[b.Id] = (x, z);
                _combatWorld.AddBuilding(b.Id, x - OriginX, z - OriginZ, Math.Sqrt(Math.Max(b.AreaM2, 20.0)) * 0.6, b.HeightM);
                _footprints.Add(new SliceFootprint(x - OriginX, z - OriginZ, Math.Sqrt(Math.Max(b.AreaM2, 20.0)) * 0.6, b.HeightM));
            }
            _buildingCount = all.Count;
        }

        void SetupPixelPipeline()
        {
            if (!_flags.IsOn(PixelFlags.Enabled) || pixelCatalog == null || pixelCatalog.pipeline == null) return;
            QualitySettings.renderPipeline = pixelCatalog.pipeline;
        }

        void SetupCamera()
        {
            var sun = new GameObject("SliceSun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(42f, 150f, 0f);
            bool pixel = _flags.IsOn(PixelFlags.Enabled) && pixelCatalog != null && pixelCatalog.pipeline != null;
            var go = new GameObject("SliceCamera");
            _cam = go.AddComponent<Camera>();
            _cam.nearClipPlane = 1f; _cam.farClipPlane = 30000f; _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.45f, 0.55f, 0.7f);
            go.AddComponent<AudioListener>();
            if (pixel)
            {
                go.AddComponent<UniversalAdditionalCameraData>().SetRenderer(pixelCatalog.worldRendererIndex);
                _binder = go.AddComponent<PixelCameraBinder>();
                _binder.catalog = pixelCatalog; _binder.profile = pixelCatalog.FindProfile(paletteProfileId);
                _binder.outputSize = new Vector2Int(Screen.width, Screen.height);
                _binder.Configure(pixelCatalog.LoadSettings(), PixelFlagState.From(_flags, null));
            }
            _rig = go.AddComponent<ThirdPersonRig>();
            _rig.settingsJson = cameraSettings;
            _rig.Configure(_rig.Settings, _flags.IsOn(CameraFlags.SnapPosition), _flags.IsOn(CameraFlags.QuantiseRotation), _flags.IsOn(CameraFlags.FootfallImpulse));
            _rig.Subscribe(_bus);
            if (_binder != null) _binder.SetStabilisationSource(_rig);
            _rig.SetOrbitYaw(_spawnYawDeg);
            if (_body != null) _rig.SetTarget(_body); else _rig.SetTarget(new TransformCameraTarget(_mechT, MechHeightM));
            _rig.Cut();
            if (pixel)
            {
                var pg = new GameObject("SlicePresentCamera");
                var pc = pg.AddComponent<Camera>();
                pc.cullingMask = 0; pc.clearFlags = CameraClearFlags.SolidColor; pc.depth = 10;
                pg.AddComponent<UniversalAdditionalCameraData>().SetRenderer(pixelCatalog.presentRendererIndex);
                pg.AddComponent<PixelPresentCamera>().source = _binder;
            }
        }

        float Ground(double ux, double uz)
        {
            double h = _comp.Geo.SampleTerrain(ux + OriginX, uz + OriginZ);
            return double.IsNaN(h) ? 0f : (float)h;
        }

        GameObject Prim(PrimitiveType t, string name, Material m, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; g.transform.localScale = scale;
            var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
            if (m != null) g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        void SpawnActors()
        {
            // TEMPORARY: a capsule mech (80 m, the Units.Placeholder body height) and a 100 m capsule kaiju stand-in.
            // The first build put the mech at the SoMa tile centre, inside a block of 100-190 m towers, and the rig 248 m behind it inside another:
            // every frame was one wall (DEF-M1slice-f1). The planner picks a spot on land with the camera in clear air and the city behind the mech.
            float mx = 1024f, mz = 1024f; _spawnYawDeg = 0f;
            var rs = cameraSettings != null ? Game.Cameras.ThirdPersonRigSettings.FromJson(cameraSettings.text) : new Game.Cameras.ThirdPersonRigSettings();
            double camDist = rs.distanceInTargetHeights * MechHeightM, camAbove = rs.focusHeightFraction * MechHeightM - Math.Sin(rs.pitchDeg * Math.PI / 180.0) * camDist;
            var tp = Time.realtimeSinceStartupAsDouble;
            if (SliceSpawnPlanner.TryPlan(_footprints, (x, z) => _comp.Geo.SampleTerrain(x + OriginX, z + OriginZ), 1024.0, 1024.0, 700.0, 70.0, camDist, camAbove, 12, out var plan))
            {
                mx = (float)plan.MechX; mz = (float)plan.MechZ; _spawnYawDeg = (float)plan.YawDeg;
                Log("spawn plan: mech (" + F(mx) + ", " + F(mz) + ") ground " + F(plan.GroundM) + " m, view yaw " + F(_spawnYawDeg) + " deg, blockers " + plan.Blockers + ", buildings in view " + plan.VisibleBuildings + " (" + F(Time.realtimeSinceStartupAsDouble - tp) + " s)");
            }
            else Log("spawn plan: no clear spot found, using the tile centre");
            float gy = Ground(mx, mz);
            var mech = new GameObject("SliceTempMech");
            var vis = Prim(PrimitiveType.Capsule, "Visual", mechMaterial, new Vector3(16f, MechHeightM * 0.5f, 16f));
            vis.transform.SetParent(mech.transform, false); vis.transform.localPosition = new Vector3(0f, MechHeightM * 0.5f, 0f);
            _mechT = mech.transform; _mechT.SetPositionAndRotation(new Vector3(mx, gy, mz), Quaternion.Euler(0f, _spawnYawDeg, 0f));
            _mech = new SliceTarget { Id = EntityId.FromValue(101), X = mx, Y = mz };
            if (_comp.Placeholder != null)
            {
                // The M1 placeholder body (Units.Placeholder, TEMPORARY too) driven through a pilot seat: device pilot -> single arbiter -> IntentFrame -> body.
                _body = _comp.Placeholder.Create(mech, _mech.Id);
                var tuning = DevicePilotSource.LoadTuning(controlTuning);
                var pilot = new DevicePilotSource(_flags, tuning, () => new System.Numerics.Vector3(_body.Body.X, _body.Body.Y, _body.Body.Z), () => AutoPilot ? _spawnYawDeg * Mathf.Deg2Rad : _cam != null ? _cam.transform.eulerAngles.y * Mathf.Deg2Rad : 0f,   // auto-pilot: fixed view yaw, so the smoke's camera cuts do not turn the stick (T-202)
                    stick: () => AutoPilot ? _autoStick() : ReadKeys());
                _autoStick = () =>
                {
                    float t = (float)_root.Services.Clock.TimeSeconds;
                    if (_mechUnit == null) return new Vector2(0.15f * Mathf.Sin(t * 0.35f), 0.6f);
                    if (Arg("-sliceAp") == "straight") return new Vector2(0f, 0.6f);   // bisect aid: straight on, no terrain steering
                    // T-202: the physics mech falls on slopes steeper than ~4 deg (walk_mode 2 is tuned on flat ground), so the scripted auto-pilot picks the flattest heading within +-60 deg every 0.25 s (documented pilot assist, smoke / auto only)
                    if (t >= _apNext) { _apNext = t + 0.25f; _apDir = PickFlatHeading(); }
                    float cy = _spawnYawDeg * Mathf.Deg2Rad;
                    var d = new Vector2(Mathf.Sin(_apDir), Mathf.Cos(_apDir));
                    return new Vector2(d.x * Mathf.Cos(cy) - d.y * Mathf.Sin(cy), d.x * Mathf.Sin(cy) + d.y * Mathf.Cos(cy)) * 0.6f;
                };
                _seat = new PilotSeat(new SinglePilotArbiter(), pilot);
                if (_configUsed == "demo" && _flags.IsOn(MechConfig.FlagEnabled) && _flags.IsOn(MechConfig.FlagBodyEnabled)) { _capsuleVis = vis; SpawnPhysicsMech(mx, gy, mz, vis, _spawnYawDeg * Mathf.Deg2Rad); }
            }
            _targets.Add(_mech);
            var brain = _root.Registry.Resolve<IKaijuBrainFactory>().Create();
            // the kaiju starts ahead of the camera, beyond the mech and a little to the right, so it walks into the frame
            float fx = Mathf.Sin(_spawnYawDeg * Mathf.Deg2Rad), fz = Mathf.Cos(_spawnYawDeg * Mathf.Deg2Rad);
            float kx = mx + fx * 340f + fz * 90f, kz = mz + fz * 340f - fx * 90f;
            var kj = Prim(PrimitiveType.Capsule, "SliceTempKaiju", kaijuMaterial, new Vector3(30f, KaijuHalfHeightM, 30f));
            _kaijuT = kj.transform;
            _kaiju = new SliceKaijuAgent(brain, EntityId.FromValue(1), 7, kx, kz, Mathf.Atan2(mz - kz, mx - kx), (float)_root.Services.Settings.FixedStepSeconds);
            PlaceKaiju();
            if (_comp.Combat != null || _comp.Hangar != null)
            {
                _feel = new SliceFeelRig(_comp, _bus, _combatWorld, (x, z) => Ground(x, z), Log);
                _feel.Attach(_body, _kaiju, _mech, OriginX, OriginZ);
            }
        }

        /// <summary>T-200: the physics mech (walk_mode 2, assisted walk) driven by the pilot seat, standing where the capsule stood. Falls back to the capsule body, with an error in the log, if the mech cannot be built.</summary>
        void SpawnPhysicsMech(float mx, float gy, float mz, GameObject capsuleVisual, float yawRad)
        {
            try
            {
                MechConfig cfg = mechFrameJson != null && mechBodyJson != null && mechWalkJson != null ? MechConfig.FromJson(mechFrameJson.text, mechBodyJson.text, mechWalkJson.text) : MechConfig.LoadDefault();
                if (mechWalkFastJson != null && Arg("-sliceWalk") != "default") cfg = cfg.WithWalkOverlay(mechWalkFastJson.text);   // T-201 fast profile (last stable rung)
                cfg = cfg.WithWalk(w => w.walk_mode = 2);   // demo only: the shipped walk_tuning.json keeps walk_mode 0
                double dt = _root.Services.Settings.FixedStepSeconds;
                _bodyWorld = new BodyWorld(cfg.Body, dt, true, _bus, _flags);
                var spawn = new BodySpawn { Position = new System.Numerics.Vector3(mx, gy + 0.05f, mz), Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, yawRad), Visuals = true, Name = "SliceMech" };
                _mechUnit = new MechUnit(_bodyWorld, cfg, _mech.Id, _bus, _seat, spawn, _flags);
                _mechVisRoot = ((ArticulatedBody)_mechUnit.Body).Root;
                if (mechMaterial != null) foreach (var r in _mechVisRoot.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mechMaterial;
                capsuleVisual.SetActive(false);
                Log("demo: physics mech spawned (walk_mode " + cfg.Walk.walk_mode + ", mass " + F((float)_mechUnit.Body.TotalMassKg) + " kg) at (" + F(mx) + ", " + F(gy) + ", " + F(mz) + ")");
            }
            catch (Exception ex)
            {
                Debug.LogError("[Slice] demo: physics mech failed, keeping the capsule body: " + ex);
                Log("demo: physics mech FAILED (" + ex.GetType().Name + ": " + ex.Message + "); capsule body kept");
                _mechUnit?.Dispose(); _mechUnit = null; _bodyWorld?.Dispose(); _bodyWorld = null; _mechVisRoot = null; capsuleVisual.SetActive(true);
            }
        }

        /// <summary>One physics step of the demo mech per fixed step: pilot seat read + controller, PhysX, then the logic proxy and the ground anchor follow the centre of mass.</summary>
        void StepPhysicsMech()
        {
            long n = _mechStep;
            _mechUnit.PreStep(n);
            _bodyWorld.Simulate(n + 1);
            _mechStep = n + 1;
            _mechUnit.PostStep(n + 1);
            var com = _mechUnit.Body.Com; var v = _mechUnit.Body.ComVelocity;
            float gy = Ground(com.X, com.Z), hd = _mechUnit.Command.Heading;
            _body.SetIntent(_mechUnit.Intent);
            _body.Body.Place(com.X, gy, com.Z, hd);
            _body.transform.SetPositionAndRotation(new Vector3(com.X, gy, com.Z), Quaternion.Euler(0f, hd * Mathf.Rad2Deg, 0f));
            _mechT.SetPositionAndRotation(new Vector3(com.X, gy, com.Z), Quaternion.Euler(0f, hd * Mathf.Rad2Deg, 0f));
            _mech.X = com.X; _mech.Y = com.Z; _mech.Speed = Mathf.Sqrt(v.X * v.X + v.Z * v.Z); _mech.HeadingRad = Mathf.PI * 0.5f - hd;
            if (_mechComSet) _mechComTravel += Math.Sqrt((com.X - _mechComX) * (com.X - _mechComX) + (com.Z - _mechComZ) * (com.Z - _mechComZ));
            _mechComX = com.X; _mechComZ = com.Z; _mechComSet = true;
            // T-202 diagnostics: where it falls, lowest/highest COM, top COM speed, 1 s trace in the log
            double ts = _root.Services.Clock.TimeSeconds; float sp = Mathf.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (com.Y < _mechMinY) _mechMinY = com.Y; if (com.Y > _mechMaxY) _mechMaxY = com.Y; if (sp > _mechMaxSpeed) _mechMaxSpeed = sp;
            var ph = _mechUnit.Phase; if (ph == LocoPhase.Fallen || ph == LocoPhase.Falling) _mechEverFell = true;
            if (_mechFellAt < 0 && (ph == LocoPhase.Fallen || ph == LocoPhase.Falling)) { _mechFellAt = ts; Log("mech FELL at t=" + F((float)ts) + " s, com (" + F(com.X) + ", " + F(com.Y) + ", " + F(com.Z) + ") ground " + F(gy) + " v (" + F(v.X) + ", " + F(v.Y) + ", " + F(v.Z) + ")"); }
            if (ts >= _mechTraceNext) { _mechTraceNext = ts + (ts > 16.5 && ts < 19.5 ? 0.1 : 1.0); Log("mech t=" + F((float)ts) + " " + ph + " com (" + F(com.X) + ", " + F(com.Y) + ", " + F(com.Z) + ") ground " + F(gy) + " ray " + F(_mechUnit.Controller.GroundHeight != null ? _mechUnit.Controller.GroundHeight(com.X, com.Z) : -999f) + " v " + F(sp) + " hd " + F(hd)); }
            MaybeRespawnPhysicsMech(ts, ph, com.X, com.Z, hd);
        }

        float _apNext, _apDir = float.NaN; float _apBase = float.NaN;
        /// <summary>World heading (rad, 0 = +z) of the flattest of 9 candidates within +-60 deg of the base heading (mech facing at the first call), by max terrain gradient over 10..70 m ahead.</summary>
        float PickFlatHeading()
        {
            if (float.IsNaN(_apBase)) _apBase = _mechUnit.Command.Heading;
            float cx = _mech.X, cz = _mech.Y, best = float.MaxValue, bestH = _apBase;
            for (int k = -4; k <= 4; k++)
            {
                float h = _apBase + k * 15f * Mathf.Deg2Rad, worst = 0f, prev = Ground(cx, cz);
                for (int i = 1; i <= 7; i++) { float g = Ground(cx + Mathf.Sin(h) * 10f * i, cz + Mathf.Cos(h) * 10f * i); worst = Mathf.Max(worst, Mathf.Abs(g - prev) / 10f); prev = g; }
                float score = Mathf.Max(worst, 0.05f) + 0.01f * Mathf.Abs(k);   // gradients under 3 deg count as flat, so it keeps straight on
                if (score < best) { best = score; bestH = h; }
            }
            return bestH;
        }

        GameObject _capsuleVis; int _mechFalls; double _fallenSince = -1; bool _mechEverFell;
        /// <summary>TEMPORARY (T-202): the physics mech still falls after 15 to 30 s of start/stop/turn walking on the city terrain in the player (cause not found, see tasks/T-202.progress.md). The demo stays playable: a mech that has been Falling/Fallen for 2.5 s is rebuilt standing at the same spot and heading. Counted in the smoke JSON as mech_respawns; mech_fell_ever stays true. Off with -sliceNoRespawn.</summary>
        void MaybeRespawnPhysicsMech(double ts, LocoPhase ph, float x, float z, float heading)
        {
            bool down = ph == LocoPhase.Fallen || ph == LocoPhase.Falling;
            if (!down) { _fallenSince = -1; return; }
            if (_fallenSince < 0) _fallenSince = ts;
            if (ts - _fallenSince < 2.5 || Arg("-sliceNoRespawn") != null && Arg("-sliceNoRespawn") != "") return;
            _mechFalls++; _fallenSince = -1; _mechEverFell = true;
            Log("mech respawn " + _mechFalls + " (TEMPORARY, fell at t=" + F((float)ts) + " s) at (" + F(x) + ", " + F(z) + ")");
            try { _mechUnit.Dispose(); } catch (Exception ex) { Debug.LogWarning("[Slice] mech dispose: " + ex.Message); }
            _bodyWorld?.Dispose(); _bodyWorld = null; _mechUnit = null; _mechStep = 0; _mechComSet = false; _apBase = float.NaN; _apNext = 0f;
            if (_mechVisRoot != null) Destroy(_mechVisRoot);
            SpawnPhysicsMech(x, Ground(x, z), z, _capsuleVis, heading);
        }

        void SetActorsActive(bool on)
        {
            _kaijuT.gameObject.SetActive(on); _mechT.gameObject.SetActive(on);
            if (_mechVisRoot != null) foreach (var r in _mechVisRoot.GetComponentsInChildren<Renderer>(true)) r.enabled = on;   // T-202: never SetActive the articulation root (deactivating the ArticulationBody collapsed the mech at the destroy-view capture)
        }

        const float KaijuHalfHeightM = 50f;   // capsule scale y 50 = 100 m tall

        void PlaceKaiju() { _kaijuT.SetPositionAndRotation(new Vector3(_kaiju.X, Ground(_kaiju.X, _kaiju.Y) + KaijuHalfHeightM, _kaiju.Y), Quaternion.Euler(0f, 90f - _kaiju.HeadingRad * Mathf.Rad2Deg, 0f)); }

        static Vector2 ReadKeys()
        {
            var kb = Keyboard.current; if (kb == null) return Vector2.zero;
            return Vector2.ClampMagnitude(new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f)), 1f);
        }

        void FixedUpdate()
        {
            if (!Ready) return;
            if (_mechUnit != null) StepPhysicsMech();
            else if (_body != null)
            {
                _body.SetIntent(_seat.Read());
                var b = _body.Body;
                _mech.X = b.X; _mech.Y = b.Z; _mech.Speed = b.Speed; _mech.HeadingRad = Mathf.PI * 0.5f - b.Heading;
            }
            var clock = _root.Services.Clock;
            // kaiju step
            var hit = _kaiju.Step(clock.Step, clock.TimeSeconds, (float)clock.StepSeconds, _targets);
            if (hit != null && _body != null) { _body.Body.Damage.ApplyDamage(100f); hit.Hp = _body.Body.Damage.Fraction; hit.Alive = true; Log("kaiju melee hit; body hp " + hit.Hp.ToString("F2", CultureInfo.InvariantCulture)); if (_body.Body.Damage.HitPoints <= 0f) { if (_feel == null || !_feel.HangarCycle(HangarGround)) { _body.Body.Damage.RepairFully(); Log("mech repaired (stand-in respawn)"); } } }
            else if (hit != null) { Log("kaiju melee hit; mech hp " + hit.Hp.ToString("F2", CultureInfo.InvariantCulture)); if (!hit.Alive) { hit.Hp = 1f; hit.Alive = true; Log("mech respawned"); } }
            _feel?.FixedStep(clock.StepSeconds, clock.TimeSeconds);
            PlaceKaiju();
        }

        double HangarGround(double bx, double bz) { return _comp.Geo.SampleTerrain(bx, bz); }

        void Update()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;
            MoveMech(dt);
            if (_audio != null && _cam != null) _audio.Tick(_mechT.position, _cam.transform, _root.Services.Clock.Step, OriginX, OriginZ);
            { var mp = _mechT.position; if (_haveLastM) _mechTravel += Math.Sqrt((mp.x - _lastMX) * (mp.x - _lastMX) + (mp.z - _lastMZ) * (mp.z - _lastMZ)); _lastMX = mp.x; _lastMZ = mp.z; _haveLastM = true; }
            MoveImpactor(dt);
            for (int i = 0; i < 3 && _removals.Count > 0; i++) { var id = _removals.Dequeue(); if (!_comp.Geo.IsRemoved(id)) { _comp.Geo.RemoveBuilding(id); _removedCount++; } }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) TriggerQuake();
                if (kb.iKey.wasPressedThisFrame) TriggerImpact();
                if (_feel != null)
                {
                    if (kb.fKey.wasPressedThisFrame) _feel.FireCannon();
                    if (kb.rKey.wasPressedThisFrame) _feel.SwingArm();
                    if (kb.hKey.wasPressedThisFrame) _feel.HangarCycle(HangarGround);
                }
            }
            var ms = Mouse.current; if (_feel != null && ms != null && ms.leftButton.wasPressedThisFrame && !AutoPilot) _feel.FireCannon();
            if (_binder != null && (_binder.outputSize.x != Screen.width || _binder.outputSize.y != Screen.height)) _binder.outputSize = new Vector2Int(Screen.width, Screen.height);
            if (SmokeFrames > 0 && !SmokeDone) SmokeTick();
        }

        void MoveMech(float dt)
        {
            if (_body != null) return;   // the placeholder body moves itself in its FixedUpdate
            Vector2 mv = Vector2.zero;
            if (AutoPilot)
            {
                float t = (float)_root.Services.Clock.TimeSeconds;
                mv = new Vector2(0.15f * Mathf.Sin(t * 0.35f), 0.6f);   // walks on with a gentle weave so the kaiju has something to chase
            }
            else
            {
                var kb = Keyboard.current; if (kb == null) return;
                mv = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            }
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            const float Speed = 14f;
            var p = _mechT.position; p.x += mv.x * Speed * dt; p.z += mv.y * Speed * dt; p.y = Ground(p.x, p.z) + 6f;
            _mechT.position = p;
            _mech.X = p.x; _mech.Y = p.z; _mech.Speed = mv.magnitude * Speed;
            if (mv.sqrMagnitude > 0.01f) _mech.HeadingRad = Mathf.Atan2(mv.y, mv.x);
        }

        // ---- quake ----
        public bool TriggerQuake()
        {
            if (_comp.Seismic == null) { Log("quake: seismic not installed"); return false; }
            if (!SliceQuakePlan.TryPlan(_comp.Seismic.Faults, (uint)_nextEventId, 1234u + (uint)_nextEventId, SomaRef.OriginX + 1024, SomaRef.OriginZ + 1024, out var sc)) { Log("quake: no mapped fault in the district"); return false; }
            _nextEventId++;
            var r = _comp.Seismic.Run(sc, _root.Services.Clock.TimeSeconds);
            if (r == null) { Log("quake: director refused the scenario"); return false; }
            _quakes++;
            _bus.Publish(new CameraShakeImpulse { Source = EntityId.FromValue(900), OriginX = _mechT.position.x, OriginY = _mechT.position.y, OriginZ = _mechT.position.z, ImpulseNs = 4e5f });
            Log("quake Mw " + r.Mw.ToString("F2", CultureInfo.InvariantCulture) + " on " + r.System.Name + ", buildings damaged so far " + _damagedEvents);
            return true;
        }

        // ---- impactor ----
        public bool TriggerImpact()
        {
            if (_impactor != null) return false;
            var p = _mechT.position;
            float yr = _spawnYawDeg * Mathf.Deg2Rad, fx = Mathf.Sin(yr), fz = Mathf.Cos(yr);
            var aim = new Vector3(p.x + fx * 170f + fz * 40f, 0f, p.z + fz * 170f - fx * 40f);
            if (!_index.TryNearest(aim.x + OriginX, aim.z + OriginZ, out _impactTarget, out double ax, out double az)) { Log("impact: no buildings"); return false; }
            aim = new Vector3((float)(ax - OriginX), 0f, (float)(az - OriginZ)); aim.y = Ground(aim.x, aim.z) + 5f;
            _impactAim = aim;
            var start = aim + new Vector3(-fx * 600f - fz * 200f, 350f, -fz * 600f + fx * 200f);   // comes in from behind the camera
            _impactVel = (aim - start).normalized * (float)ImpactSpeedMs;
            _impactor = Prim(PrimitiveType.Sphere, "SliceImpactor", impactorMaterial, new Vector3(10f, 10f, 10f)).transform;
            _impactor.position = start;
            PrepareDestroyView();
            return true;
        }

        void MoveImpactor(float dt)
        {
            if (_impactor == null) return;
            _impactor.position += _impactVel * dt;
            if ((_impactor.position - _impactAim).sqrMagnitude > ImpactSpeedMs * dt * ImpactSpeedMs * dt * 1.5 && Vector3.Dot(_impactAim - _impactor.position, _impactVel) > 0f) return;
            double e = SliceImpact.KineticEnergyJ(ImpactMassKg, ImpactSpeedMs);
            var ch = _comp.Destruction.Apply(_impactTarget, e, DamageCause.Impact, EntityId.FromValue(901), _root.Services.Clock.Step);
            _impacts++;
            _comp.Destruction.TryGetThresholds(_impactTarget, out double s1, out double s2, out double s3, out double s4);
            Log("impact " + (e / 1e6).ToString("F0", CultureInfo.InvariantCulture) + " MJ on " + _impactTarget + ": state " + ch.PrevState + " -> " + ch.Current.State + " (thresholds MJ " + (s1 / 1e6).ToString("F1", CultureInfo.InvariantCulture) + "/" + (s2 / 1e6).ToString("F1", CultureInfo.InvariantCulture) + "/" + (s3 / 1e6).ToString("F1", CultureInfo.InvariantCulture) + "/" + (s4 / 1e6).ToString("F1", CultureInfo.InvariantCulture) + ")");
            _bus.Publish(new CameraShakeImpulse { Source = EntityId.FromValue(901), OriginX = _impactAim.x, OriginY = _impactAim.y, OriginZ = _impactAim.z, ImpulseNs = 8e5f });
            Destroy(_impactor.gameObject); _impactor = null;
        }

        public void Handle(in BuildingDamaged e)
        {
            _damagedEvents++; if (e.State < _stateCounts.Length) _stateCounts[e.State]++;
            if (e.State >= (byte)DamageState.Complete) _removals.Enqueue(e.BuildingId);
        }
        public void Handle(in EmergencePoint e) { Log("emergence at (" + e.X.ToString("F0", CultureInfo.InvariantCulture) + ", " + e.Z.ToString("F0", CultureInfo.InvariantCulture) + ") t=" + e.TimeS.ToString("F0", CultureInfo.InvariantCulture) + " s"); }

        void Log(string s) { _lastMsg = s; _log.AppendLine(s); if (Ready) Debug.Log("[Slice] " + s); }

        // ---- smoke (game-time script: the player runs at ~1000 fps, so frames alone would end it before anything happens) ----
        const int Warmup = 60;
        const float QuakeAtS = 5f, ImpactAtS = 12f, EndS = 20f, SkyRefAtS = 0.5f;
        enum CapView { Follow, Bridge, DestroyAfter }
        struct CapPlan { public string Label; public float T; public CapView View; public CapPlan(string l, float t, CapView v) { Label = l; T = t; View = v; } }
        // six frames from the follow camera (the pairwise-difference series), then the Golden Gate Bridge, then the impacted building from above; the "destroy_before" frame
        // is taken at the moment the impactor launches (same pose as "destroy_after"), so before/after differ only by the removed building.
        static readonly CapPlan[] CapSchedule = {
            new CapPlan("follow", 2f, CapView.Follow), new CapPlan("follow", 4f, CapView.Follow), new CapPlan("follow", 6.5f, CapView.Follow), new CapPlan("follow", 9f, CapView.Follow),
            new CapPlan("follow", 14f, CapView.Follow), new CapPlan("follow", 19f, CapView.Follow), new CapPlan("bridge", 19.4f, CapView.Bridge), new CapPlan("destroy_after", 19.8f, CapView.DestroyAfter) };
        float _nextShotAt, _hangarAtT = -1f; int _swingsDone; bool _duelDone;
        int _captureIdx, _destroyBeforeIdx = -1, _destroyAfterIdx = -1; bool _skyRefRequested, _capturePending, _destroyBeforeDone;
        int _capW, _capH; uint[] _skyRef;
        readonly List<uint[]> _frames = new List<uint[]>(); readonly List<string> _frameFiles = new List<string>(), _frameLabels = new List<string>(); readonly List<float> _frameT = new List<float>();
        string _diagAtCapture = "";
        PixelCheckVerdict _verdict; double _destroyDiff = -1;
        Vector3 _destroyCamPos; Vector3 _destroyLookAt; bool _destroyViewSet;

        /// <summary>HUD text (top-left) and the "Development Build" label (bottom-right) are drawn over the presented frame; the pixel check ignores them.</summary>
        static PixelRect[] HudRects(int w, int h) { return new[] { new PixelRect(0, 0, Math.Min(w, 1120), Math.Min(h, 110)), new PixelRect(Math.Max(0, w - 220), Math.Max(0, h - 40), w, h) }; }

        bool Skip(string what) { var a = Arg("-sliceSkip"); return !string.IsNullOrEmpty(a) && ("," + a + ",").Contains("," + what + ","); }   // T-202 bisect aid: -sliceSkip duel,cannon,swing,impact,quake

        void SmokeTick()
        {
            _frame++;
            float t = Time.timeSinceLevelLoad - _readyAt;
            bool shoot = !string.IsNullOrEmpty(SmokeOut);
            if (_quakes == 0 && t >= QuakeAtS && !Skip("quake")) TriggerQuake();
            if (_impacts == 0 && _impactor == null && t >= ImpactAtS && !Skip("impact")) TriggerImpact();
            if (_feel != null)
            {   // T-189 scripted fight: a cannon round every 2 s from 3 s, arm swings at 8 s and 14 s, the hangar cycle once the captures are done
                if (!_duelDone && t >= 13.5f && !Skip("duel")) { _duelDone = true; _feel.ScriptedDuel(); _nextShotAt = t; }
                if (t >= 3f && t >= _nextShotAt && !Skip("cannon")) { _feel.FireCannon(); _nextShotAt = t + 2f; }
                if (Skip("swing")) { }
                else if (_swingsDone == 0 && t >= 8f && _feel.SwingArm()) _swingsDone++;
                else if (_swingsDone == 1 && t >= 14f && _feel.SwingArm()) _swingsDone++;
                bool capsDone = !shoot || (_captureIdx >= CapSchedule.Length && _destroyBeforeDone && !_capturePending);
                if (_feel.HangarOn && _feel.HangarCycles == 0 && t >= EndS - 0.05f && capsDone && _impacts > 0) { _feel.HangarCycle(HangarGround); _hangarAtT = t; }
            }
            if (shoot && !_capturePending)
            {
                if (!_skyRefRequested && t >= SkyRefAtS) { _skyRefRequested = true; _capturePending = true; StartCoroutine(CaptureFrame(-1, "sky", t)); }
                else if (_skyRef != null && _impactor != null && !_destroyBeforeDone && _destroyViewSet) { _destroyBeforeDone = true; BeginCapture("destroy_before", CapView.DestroyAfter, t); }
                else if (_skyRef != null && _captureIdx < CapSchedule.Length && t >= CapSchedule[_captureIdx].T) { var c = CapSchedule[_captureIdx++]; BeginCapture(c.Label, c.View, t); }
            }
            if (_frame > Warmup) { _ms.Add(Time.unscaledDeltaTime * 1000f); _draw.Add(_drawRec.Valid ? _drawRec.LastValue : -1); }
            bool capturesDone = !shoot || (_captureIdx >= CapSchedule.Length && _destroyBeforeDone && !_capturePending);
            bool feelDone = _feel == null || !_feel.HangarOn || (_feel.HangarCycles > 0 && t >= _hangarAtT + 1.5f);
            if (_frame >= Warmup + SmokeFrames && t >= EndS && _impacts > 0 && capturesDone && feelDone) FinishSmoke();
        }

        void BeginCapture(string label, CapView view, float t)
        {
            if (view == CapView.Bridge) SetBridgeView();
            else if (view == CapView.DestroyAfter)
            {
                SetDestroyView();
                // the before/after pair must differ only by the removed building: hide the moving actors and clear any camera shake
                SetActorsActive(false); _hidActors = true;
                if (_rig != null) _rig.Impulse.Reset();
            }
            _capturePending = true; StartCoroutine(CaptureFrame(_frames.Count, label, t));
        }

        /// <summary>Scripted camera 450 m east of the south tower of the Golden Gate Bridge (landmark 1; the north tower is landmark 2), 250 m up, looking west at the tower top: the bridge is 8 km from the mech, so it gets its own frame.</summary>
        void SetBridgeView()
        {
            if (_rig == null) return;
            double sx = 55248.012 - OriginX, sz = 76751.973 - OriginZ;   // south tower (BayTM -> Unity frame)
            float cx = (float)sx + 450f, cz = (float)sz + 120f, cy = Mathf.Max(250f, Ground(cx, cz) + 80f);
            SetScriptedLook(new Vector3(cx, cy, cz), new Vector3((float)sx, 150f, (float)sz));
            Log("bridge view: camera (" + F(cx) + ", " + F(cy) + ", " + F(cz) + ") looking at the south tower");
        }

        /// <summary>Scripted camera above the impact target (same pose for the before and after frames): 260 m back along the view direction, 90 m above its roof, looking down at it.</summary>
        void SetDestroyView() { if (_destroyViewSet) SetScriptedLook(_destroyCamPos, _destroyLookAt); }

        void SetScriptedLook(Vector3 pos, Vector3 look)
        {
            if (_rig == null) return;
            float dx = look.x - pos.x, dz = look.z - pos.z, horiz = Mathf.Sqrt(dx * dx + dz * dz);
            var pose = new CameraPose { Position = new CameraVec3(pos.x, pos.y, pos.z), YawDeg = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, PitchDeg = Mathf.Atan2(look.y - pos.y, horiz) * Mathf.Rad2Deg };
            _rig.SetScriptedPose(pose, Mathf.Max(100f, horiz)); _rig.Cut();
        }

        bool ClearLineOfSight(Vector3 eye, Vector3 look, int skip)
        {
            const int N = 40;
            for (int i = 0; i < _footprints.Count; i++)
            {
                if (i == skip) continue;
                var f = _footprints[i];
                if (Math.Abs(f.X - eye.x) > 400 && Math.Abs(f.Z - eye.z) > 400) continue;
                if (eye.y < Ground(eye.x, eye.z) + f.Height && (f.X - eye.x) * (f.X - eye.x) + (f.Z - eye.z) * (f.Z - eye.z) < f.Radius * f.Radius) return false;
                for (int j = 1; j < N; j++)
                {
                    float u = j / (float)N; double px = eye.x + (look.x - eye.x) * u, pz = eye.z + (look.z - eye.z) * u, py = eye.y + (look.y - eye.y) * u;
                    double dx = px - f.X, dz = pz - f.Z;
                    if (dx * dx + dz * dz < f.Radius * f.Radius && py < Ground((float)f.X, (float)f.Z) + f.Height) return false;
                }
            }
            return true;
        }

        /// <summary>Called when the impactor launches: fixes the destroy-view pose on the target building.</summary>
        void PrepareDestroyView()
        {
            float yr = _spawnYawDeg * Mathf.Deg2Rad;
            double bestD = double.MaxValue; double h = 40.0; int target = -1;
            for (int i = 0; i < _footprints.Count; i++)
            {
                var f = _footprints[i]; double d = (f.X - _impactAim.x) * (f.X - _impactAim.x) + (f.Z - _impactAim.z) * (f.Z - _impactAim.z);
                if (d < bestD) { bestD = d; h = f.Height; target = i; }
            }
            float gy = Ground(_impactAim.x, _impactAim.z);
            var look = new Vector3(_impactAim.x, gy + (float)h * 0.5f, _impactAim.z);
            // first of: view direction, then 7 other azimuths; 3 distances; the 40 degree look-down whose line of sight to the target is not cut by another building and whose eye is not inside one
            bool found = false; var best = new Vector3(look.x - Mathf.Sin(yr) * 200f, look.y + 170f, look.z - Mathf.Cos(yr) * 200f);
            for (int ring = 0; ring < 3 && !found; ring++)
                for (int k = 0; k < 8 && !found; k++)
                {
                    float az = yr + k * Mathf.PI / 4f, dist = 150f + ring * 100f;
                    var pos = new Vector3(look.x - Mathf.Sin(az) * dist, look.y + dist * 0.85f, look.z - Mathf.Cos(az) * dist);
                    if (ClearLineOfSight(pos, look, target)) { best = pos; found = true; }
                }
            Log("destroy view: " + (found ? "clear line of sight" : "no clear azimuth, default pose") + ", target height " + F((float)h) + " m");
            _destroyCamPos = best; _destroyLookAt = look;
            _destroyViewSet = true;
        }

        /// <summary>Reads the presented frame at the end of the frame (what ScreenCapture shows the player). index -1 is the sky-only reference: the world camera draws nothing for this one frame.</summary>
        IEnumerator CaptureFrame(int index, string label, float tSeconds)
        {
            int savedMask = _cam != null ? _cam.cullingMask : 0;
            if (index < 0 && _cam != null) _cam.cullingMask = 0;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (index < 0 && _cam != null) _cam.cullingMask = savedMask;
            if (tex != null)
            {
                int w = tex.width, h = tex.height; var px = tex.GetPixels32();
                var packed = new uint[w * h];
                for (int y = 0; y < h; y++) { int src = (h - 1 - y) * w; for (int x = 0; x < w; x++) { var c = px[src + x]; packed[y * w + x] = SlicePixelCheck.Pack(c.r, c.g, c.b); } }
                _capW = w; _capH = h;
                if (index < 0) _skyRef = packed;
                else
                {
                    string name = "capture_" + index + "_" + label + "_t" + tSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s.png";
                    _frames.Add(packed); _frameFiles.Add(name); _frameLabels.Add(label); _frameT.Add(tSeconds);
                    if (label == "destroy_before") _destroyBeforeIdx = index; else if (label == "destroy_after") _destroyAfterIdx = index;
                    try { File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(SmokeOut), name), tex.EncodeToPNG()); } catch (Exception ex) { Debug.LogError("[SliceSmoke] capture write failed: " + ex.Message); }
                    _diagAtCapture += (index > 0 ? " | " : "") + label + " t=" + tSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " " + RenderDiag();
                }
                Destroy(tex);
            }
            else { Debug.LogError("[SliceSmoke] CaptureScreenshotAsTexture returned null (index " + index + ")"); if (index >= 0) { _frames.Add(null); _frameFiles.Add("(failed)"); _frameLabels.Add(label); _frameT.Add(tSeconds); } }
            if (_hidActors) { SetActorsActive(true); _hidActors = false; }
            if (index >= 0 && _rig != null && label != "follow" && label != "destroy_before") { _rig.ClearScriptedPose(); _rig.Cut(); }
            else if (label == "destroy_before" && _rig != null) { _rig.ClearScriptedPose(); _rig.Cut(); }
            _capturePending = false;
        }

        /// <summary>Log line per renderer nearest the camera (diagnostics): what the camera is actually in front of.</summary>
        void LogNearestRenderers()
        {
            if (_cam == null || _diagLogged >= 1) return; _diagLogged++;
            var cp = _cam.transform.position; var list = new List<(float d, Renderer r)>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)) list.Add(((r.bounds.ClosestPoint(cp) - cp).magnitude, r));
            list.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < Math.Min(14, list.Count); i++)
            {
                var r = list[i].r; var mf = r.GetComponent<MeshFilter>(); var m = r.sharedMaterial;
                Debug.Log("[Slice] near#" + i + " d=" + F(list[i].d) + " '" + r.name + "' parent=" + (r.transform.parent != null ? r.transform.parent.name : "-") + " visible=" + r.isVisible + " mesh=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount + "v" : "none") + " mat=" + (m != null ? m.name : "NULL") + " bounds=" + r.bounds.center + " " + r.bounds.size + " shadow=" + r.shadowCastingMode);
            }
        }
        int _diagLogged; bool _hidActors;

        /// <summary>Camera, pipeline and renderer state at a capture: the first things to read when a frame is blank.</summary>
        string RenderDiag()
        {
            var sb = new StringBuilder();
            var rp = GraphicsSettings.currentRenderPipeline;
            sb.Append("pipeline=").Append(rp != null ? rp.name : "none");
            if (_cam != null)
            {
                var p = _cam.transform.position; var e = _cam.transform.eulerAngles;
                sb.Append(" cam=(").Append(F(p.x)).Append(",").Append(F(p.y)).Append(",").Append(F(p.z)).Append(") rot=(").Append(F(e.x)).Append(",").Append(F(e.y)).Append(") fov=").Append(F(_cam.fieldOfView))
                  .Append(" clip=").Append(F(_cam.nearClipPlane)).Append("-").Append(F(_cam.farClipPlane)).Append(" target=").Append(_cam.targetTexture != null ? _cam.targetTexture.width + "x" + _cam.targetTexture.height : "screen");
            }
            if (_body != null)
            {
                var it = _body.Intent; var bb = _body.Body;
                sb.Append(" body(speed=").Append(F(bb.Speed)).Append(" heading=").Append(F(bb.Heading)).Append(" move=").Append(it.move).Append(" effort=").Append(F(it.effort)).Append(")");
            }
            sb.Append(" kaiju=(").Append(F(_kaiju.X)).Append(",").Append(F(_kaiju.Y)).Append(") ").Append(_kaiju.State);
            if (_mechT != null) { var m = _mechT.position; sb.Append(" mech=(").Append(F(m.x)).Append(",").Append(F(m.y)).Append(",").Append(F(m.z)).Append(")"); }
            int rends = 0, visible = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude)) { rends++; if (r.isVisible) visible++; }
            sb.Append(" renderers=").Append(rends).Append(" visible=").Append(visible);
            return sb.ToString();
        }

        static double Pct(List<double> v, double p) { var s = new List<double>(v); s.Sort(); return s.Count == 0 ? 0 : s[Math.Min(s.Count - 1, (int)(p * s.Count))]; }

        void FinishSmoke()
        {
            SmokeDone = true;
            _verdict = EvaluatePixels();
            var ms = new List<double>(); foreach (var f in _ms) ms.Add(f);
            double sum = 0; foreach (var f in ms) sum += f;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"task\": \"T-039c\", \"scene\": \"M1Slice\", \"platform\": \"").Append(Application.platform).Append("\", \"development_build\": ").Append(Debug.isDebugBuild ? "true" : "false").Append(",\n");
            sb.Append("  \"graphics_device\": \"").Append(SystemInfo.graphicsDeviceName).Append("\", \"resolution\": \"").Append(Screen.width).Append("x").Append(Screen.height).Append("\",\n");
            sb.Append("  \"pixel_pipeline\": ").Append(_binder != null ? "true" : "false").Append(", \"frames_recorded\": ").Append(ms.Count).Append(", \"warmup_frames\": ").Append(Warmup).Append(",\n");
            sb.Append("  \"frame_ms\": { \"mean\": ").Append(F(ms.Count > 0 ? sum / ms.Count : 0)).Append(", \"median\": ").Append(F(Pct(ms, 0.5))).Append(", \"p95\": ").Append(F(Pct(ms, 0.95))).Append(", \"max\": ").Append(F(Pct(ms, 1.0))).Append(" },\n");
            sb.Append("  \"draw_calls\": { \"median\": ").Append(F(Pct(_draw, 0.5))).Append(", \"max\": ").Append(F(Pct(_draw, 1.0))).Append(" },\n");
            sb.Append("  \"load_seconds\": ").Append(F(_loadSeconds)).Append(", \"tiles\": ").Append(_tileCount).Append(", \"buildings_registered\": ").Append(_buildingCount).Append(", \"landmark_colliders\": ").Append(_landmarkColliders).Append(",\n");
            sb.Append("  \"quakes\": ").Append(_quakes).Append(", \"impacts\": ").Append(_impacts).Append(", \"building_damaged_events\": ").Append(_damagedEvents).Append(", \"state_counts\": [");
            for (int i = 0; i < _stateCounts.Length; i++) sb.Append(i > 0 ? ", " : "").Append(_stateCounts[i]);
            sb.Append("], \"buildings_removed_from_scene\": ").Append(_removedCount).Append(",\n");
            sb.Append("  \"kaiju\": { \"decisions\": ").Append(_kaiju.Decisions).Append(", \"melee_hits\": ").Append(_kaiju.Hits).Append(", \"state\": \"").Append(_kaiju.State).Append("\", \"distance_to_mech_m\": ").Append(F(Mathf.Sqrt((_mech.X - _kaiju.X) * (_mech.X - _kaiju.X) + (_mech.Y - _kaiju.Y) * (_mech.Y - _kaiju.Y)))).Append(" },\n");
            _feel?.AppendJson(sb);
            AppendAudioJson(sb);
            sb.Append("  \"mech_body\": \"").Append(_mechUnit != null ? "physics walk_mode 2" : "placeholder capsule (TEMPORARY)").Append("\", \"mech_phase\": \"").Append(_mechUnit != null ? _mechUnit.Phase.ToString() : "").Append("\", \"mech_respawns\": ").Append(_mechFalls).Append(", \"mech_fell_ever\": ").Append(_mechEverFell ? "true" : "false").Append(", \"mech_fell\": ").Append(_mechUnit != null && (_mechUnit.Phase == LocoPhase.Fallen || _mechUnit.Phase == LocoPhase.Falling) ? "true" : "false").Append(",\n");
            sb.Append("  \"mech_fell_at_s\": ").Append(F((float)_mechFellAt)).Append(", \"mech_min_y\": ").Append(F(_mechMinY)).Append(", \"mech_max_y\": ").Append(F(_mechMaxY)).Append(", \"mech_max_speed\": ").Append(F(_mechMaxSpeed)).Append(", \"mech_com_travel_m\": ").Append(F((float)_mechComTravel)).Append(",\n");
            sb.Append("  \"config\": \"").Append(_configUsed).Append("\", \"hangar_note\": \"").Append(_feel != null ? _feel.SpawnNote.Replace("\"", "'") : "").Append("\",\n");
            AppendPixelCheck(sb);
            sb.Append("  \"mech_travel_m\": ").Append(F(_mechUnit != null ? _mechComTravel : _mechTravel)).Append(",\n");
            sb.Append("  \"render_diag\": \"").Append(_diagAtCapture.Replace("\\", "/").Replace("\"", "'")).Append("\",\n");
            sb.Append("  \"log\": \"").Append(_log.ToString().Replace("\\", "/").Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")).Append("\"\n}\n");
            SmokeJson = sb.ToString();
            Debug.Log("[SliceSmoke] " + SmokeJson.Replace("\n", " "));
            if (!string.IsNullOrEmpty(SmokeOut))
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(SmokeOut)); File.WriteAllText(SmokeOut, SmokeJson); }
                catch (Exception ex) { Debug.LogError("[SliceSmoke] write failed: " + ex.Message); }
            }
            _drawRec.Dispose();
            if (_verdict != null && !_verdict.Pass) Debug.LogError("[SliceSmoke] PIXEL CHECK FAILED: min non-background share " + F(_verdict.MinShare) + " (floor " + F(_verdict.Threshold) + "), min pairwise diff " + F(_verdict.MinPairDiffShare) + ", destruction view diff " + F(_destroyDiff));
            if (!Application.isEditor) Application.Quit(_verdict != null && _verdict.Pass ? 0 : 3);
        }

        PixelCheckVerdict EvaluatePixels()
        {
            if (string.IsNullOrEmpty(SmokeOut) || _skyRef == null) return null;
            var frames = new List<uint[]>(); foreach (var f in _frames) if (f != null) frames.Add(f);
            if (frames.Count != _frames.Count) return new PixelCheckVerdict { Threshold = SlicePixelCheck.MinNonBackgroundShare };   // a failed capture is a failed check
            var rects = HudRects(_capW, _capH);
            var v = SlicePixelCheck.Evaluate(_skyRef, frames, _capW, _capH, rects, SlicePixelCheck.MinNonBackgroundShare);
            // destruction is visible: the same pose before the impactor lands and after the building was removed differs in at least one pixel
            if (_destroyBeforeIdx >= 0 && _destroyAfterIdx >= 0) _destroyDiff = SlicePixelCheck.DiffShare(frames[_destroyBeforeIdx], frames[_destroyAfterIdx], _capW, _capH, rects);
            if (!(_destroyDiff > 0.0)) v.Pass = false;
            return v;
        }

        void AppendPixelCheck(StringBuilder sb)
        {
            if (_verdict == null) { sb.Append("  \"pixel_check\": null,\n"); return; }
            var v = _verdict;
            sb.Append("  \"pixel_check\": { \"pass\": ").Append(v.Pass ? "true" : "false").Append(", \"non_background_share_floor\": ").Append(F(v.Threshold))
              .Append(", \"min_distinct_colours_floor\": ").Append(SlicePixelCheck.MinDistinctColours).Append(", \"max_dominant_share_ceiling\": ").Append(F(SlicePixelCheck.MaxDominantShare)).Append(", \"background_colours\": ").Append(v.BackgroundColours).Append(", \"resolution\": \"").Append(_capW).Append("x").Append(_capH).Append("\", \"hud_rects_ignored\": true,\n");
            sb.Append("    \"captures\": [");
            for (int i = 0; i < v.NonBackgroundShare.Length; i++)
                sb.Append(i > 0 ? ", " : "").Append("{ \"label\": \"").Append(_frameLabels[i]).Append("\", \"t_s\": ").Append(F(_frameT[i])).Append(", \"file\": \"").Append(_frameFiles[i]).Append("\", \"non_background_share\": ").Append(F(v.NonBackgroundShare[i])).Append(", \"distinct_colours\": ").Append(v.DistinctColours[i]).Append(", \"dominant_colour_share\": ").Append(F(v.DominantShare[i])).Append(" }");
            sb.Append("],\n    \"destruction_view_diff_share\": ").Append(F(_destroyDiff)).Append(", \"destruction_visible\": ").Append(_destroyDiff > 0.0 ? "true" : "false").Append(",\n    \"pairs\": [");
            bool first = true; int n = v.NonBackgroundShare.Length;
            for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) { sb.Append(first ? "" : ", ").Append("{ \"a\": ").Append(i).Append(", \"b\": ").Append(j).Append(", \"diff_share\": ").Append(F(v.PairDiffShare[i, j])).Append(" }"); first = false; }
            sb.Append("],\n    \"min_non_background_share\": ").Append(F(v.MinShare)).Append(", \"min_pair_diff_share\": ").Append(F(v.MinPairDiffShare))
              .Append(", \"min_distinct_colours\": ").Append(v.MinDistinct == int.MaxValue ? 0 : v.MinDistinct).Append(", \"max_dominant_share\": ").Append(F(v.MaxDominant))
              .Append(", \"shares_pass\": ").Append(v.SharesPass ? "true" : "false").Append(", \"richness_pass\": ").Append(v.RichnessPass ? "true" : "false").Append(", \"pairs_pass\": ").Append(v.PairsPass ? "true" : "false").Append(" },\n");
        }

        void AppendAudioJson(StringBuilder sb)
        {
            if (_audio == null || _audio.Bridge == null) { sb.Append("  \"audio\": null,\n"); return; }
            var st = _audio.Bridge.Stats;
            sb.Append("  \"audio\": { \"received\": ").Append(st.Received).Append(", \"emitted\": ").Append(st.Emitted).Append(", \"dropped_no_position\": ").Append(st.DroppedNoPosition)
              .Append(", \"dropped_filtered\": ").Append(st.DroppedFiltered).Append(", \"dropped_cap\": ").Append(st.DroppedCap).Append(", \"footfalls\": ").Append(_audio.FootfallCount).Append(" },\n");
        }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // ---- HUD ----
        void OnGUI()
        {
            if (!Ready) { GUI.Label(new Rect(10, 10, 600, 24), "M1 slice: loading..."); return; }
            var s = (_configUsed == "feelcheck" ? "Kaiju feel check (TEMPORARY: capsule mech and kaiju; mech body awaits T-005)  " : "M1 slice (TEMPORARY stand-ins)  ") + (1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f)).ToString("F0") + " fps   buildings destroyed " + _stateCounts[4] + "  damaged events " + _damagedEvents
                + "\n" + (_feel != null ? SliceFeelRig.Help : "WASD move   Q quake   I impactor") + "   kaiju " + _kaiju.State + " d=" + Mathf.Sqrt((_mech.X - _kaiju.X) * (_mech.X - _kaiju.X) + (_mech.Y - _kaiju.Y) * (_mech.Y - _kaiju.Y)).ToString("F0") + " m   mech hp " + _mech.Hp.ToString("F2")
                + (_feel != null ? "\n" + _feel.HudLine() : "") + "\n" + _lastMsg;
            GUI.Label(new Rect(10, 10, 1100, 100), s);
        }

        void OnDestroy() { _feel?.Dispose(); _mechUnit?.Dispose(); _bodyWorld?.Dispose(); _mechUnit = null; _bodyWorld = null; if (_bus != null) { _bus.Unsubscribe<BuildingDamaged>(this); _bus.Unsubscribe<EmergencePoint>(this); } if (_drawRec.Valid) _drawRec.Dispose(); if (Instance == this) Instance = null; }
    }
}
