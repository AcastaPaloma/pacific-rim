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
using Game.Cameras;
using Game.Composition;
using Game.Core;
using Game.Core.Unity;
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
        public TextAsset cameraSettings;
        public PixelPipelineCatalog pixelCatalog;
        public string paletteProfileId = "warm_amber_haze";
        public Material kaijuMaterial, mechMaterial, impactorMaterial;

        /// <summary>Smoke mode: run this many frames (after warm-up) with a scripted sequence, write evidence, then quit. Set by -sliceSmoke N or a test.</summary>
        public static int SmokeFrames;
        public static string SmokeOut;
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
        readonly SliceBuildingIndex _index = new SliceBuildingIndex();
        readonly Queue<BuildingId> _removals = new Queue<BuildingId>();
        readonly List<SliceTarget> _targets = new List<SliceTarget>();
        readonly int[] _stateCounts = new int[5];
        int _removedCount, _damagedEvents, _quakes, _impacts, _nextEventId = 1;
        Camera _cam; ThirdPersonRig _rig; PixelCameraBinder _binder;
        Transform _kaijuT, _mechT; SliceKaijuAgent _kaiju; SliceTarget _mech;
        Transform _impactor; Vector3 _impactVel; BuildingId _impactTarget; Vector3 _impactAim;
        readonly StringBuilder _log = new StringBuilder();
        string _lastMsg = "";
        double _loadSeconds; int _tileCount, _buildingCount, _landmarkColliders;
        // smoke
        int _frame; readonly List<float> _ms = new List<float>(); readonly List<double> _draw = new List<double>(); ProfilerRecorder _drawRec;
        public bool AutoPilot;

        double OriginX => _builder.FloatingOriginX; double OriginZ => _builder.FloatingOriginZ;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            Instance = this;
            string sm = Arg("-sliceSmoke"); if (sm != null && int.TryParse(sm, out int n)) SmokeFrames = n;
            string so = Arg("-sliceSmokeOut"); if (so != null) SmokeOut = so;
            if (!CoreRuntime.IsBooted) { Debug.LogError("[Slice] Core is not booted"); yield break; }
            _root = CoreRuntime.Root;
            _flags = FeatureFlagLoader.Parse(sliceFlags != null ? sliceFlags.text : null, "slice_m1.flags.json", _root.Errors);
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
            _builder = new GeoTileSetBuilder(set, GgRef, GeoTileBuilder.LoadSettings()) { BuildingColliders = false, TerrainCollider = true };
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
            _comp = GameComposition.Install(_root.Registry, _flags, _root.Errors, inputs);
            Log("install: geo=" + (_comp.Geo != null) + " destruction=" + (_comp.Destruction != null) + " seismic=" + (_comp.Seismic != null)
                + " bridge=" + (_comp.ShakingBridge != null) + " brain=" + (_comp.Brain != null));
            if (_comp.Geo == null || _comp.Destruction == null) { Debug.LogError("[Slice] composition did not install Geo/Destruction: " + _lastMsg); yield break; }
            foreach (var inst in _builder.Instances) _comp.Geo.Add(inst);
            _bus.Subscribe<BuildingDamaged>(this); _bus.Subscribe<EmergencePoint>(this);
            RegisterBuildings();
            yield return null;

            // 3. actors, camera, light
            SpawnActors();
            SetupCamera();
            _loadSeconds = Time.realtimeSinceStartupAsDouble - t0;
            Log("ready in " + _loadSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s: tiles " + _tileCount + ", buildings " + _buildingCount + ", landmark colliders " + _landmarkColliders);
            Debug.Log("[Slice] " + _lastMsg);
            Ready = true;
            AutoPilot = SmokeFrames > 0;
            if (SmokeFrames > 0) _drawRec = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        }

        FaultTraceSet BuildFaults()
        {
            var pieces = new List<FaultPiece>();
            foreach (var inst in _builder.Instances)
            {
                var layer = inst.Model.Layer("faults");
                if (layer != null) pieces.AddRange(FaultTraceSet.PiecesFromGeo(layer.Faults, inst.Model.Context.OriginX, inst.Model.Context.OriginZ));
            }
            return FaultTraceSet.Build(pieces);
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
            _rig.SetTarget(new TransformCameraTarget(_mechT, 12f));
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
            // TEMPORARY: a capsule mech body (12 m) moved by WASD until Units.Placeholder lands, and a 60 m capsule kaiju stand-in.
            float mx = 1024f, mz = 1024f;
            var mech = Prim(PrimitiveType.Capsule, "SliceTempMech", mechMaterial, new Vector3(6f, 6f, 6f));
            _mechT = mech.transform; _mechT.position = new Vector3(mx, Ground(mx, mz) + 6f, mz);
            _mech = new SliceTarget { Id = EntityId.FromValue(101), X = mx, Y = mz };
            _targets.Add(_mech);
            var brain = _root.Registry.Resolve<IKaijuBrainFactory>().Create();
            float kx = mx - 700f, kz = mz + 200f;
            var kj = Prim(PrimitiveType.Capsule, "SliceTempKaiju", kaijuMaterial, new Vector3(25f, 30f, 25f));
            _kaijuT = kj.transform;
            _kaiju = new SliceKaijuAgent(brain, EntityId.FromValue(1), 7, kx, kz, 0f, (float)_root.Services.Settings.FixedStepSeconds);
            PlaceKaiju();
        }

        void PlaceKaiju() { _kaijuT.SetPositionAndRotation(new Vector3(_kaiju.X, Ground(_kaiju.X, _kaiju.Y) + 30f, _kaiju.Y), Quaternion.Euler(0f, 90f - _kaiju.HeadingRad * Mathf.Rad2Deg, 0f)); }

        void FixedUpdate()
        {
            if (!Ready) return;
            var clock = _root.Services.Clock;
            // kaiju step
            var hit = _kaiju.Step(clock.Step, clock.TimeSeconds, (float)clock.StepSeconds, _targets);
            if (hit != null) { Log("kaiju melee hit; mech hp " + hit.Hp.ToString("F2", CultureInfo.InvariantCulture)); if (!hit.Alive) { hit.Hp = 1f; hit.Alive = true; Log("mech respawned"); } }
            PlaceKaiju();
        }

        void Update()
        {
            if (!Ready) return;
            float dt = Time.deltaTime;
            MoveMech(dt);
            MoveImpactor(dt);
            for (int i = 0; i < 3 && _removals.Count > 0; i++) { var id = _removals.Dequeue(); if (!_comp.Geo.IsRemoved(id)) { _comp.Geo.RemoveBuilding(id); _removedCount++; } }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.qKey.wasPressedThisFrame) TriggerQuake();
                if (kb.iKey.wasPressedThisFrame) TriggerImpact();
            }
            if (_binder != null && (_binder.outputSize.x != Screen.width || _binder.outputSize.y != Screen.height)) _binder.outputSize = new Vector2Int(Screen.width, Screen.height);
            if (SmokeFrames > 0 && !SmokeDone) SmokeTick();
        }

        void MoveMech(float dt)
        {
            Vector2 mv = Vector2.zero;
            if (AutoPilot)
            {
                float t = (float)_root.Services.Clock.TimeSeconds;
                mv = new Vector2(Mathf.Sin(t * 0.2f), Mathf.Cos(t * 0.2f) * 0.5f);   // slow loop so the kaiju has something to chase
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
            if (!SliceQuakePlan.TryPlan(_comp.Seismic.Faults, (uint)_nextEventId, 1234u + (uint)_nextEventId, out var sc)) { Log("quake: no mapped fault in the district"); return false; }
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
            var aim = new Vector3(p.x + 150f, 0f, p.z + 60f);
            if (!_index.TryNearest(aim.x + OriginX, aim.z + OriginZ, out _impactTarget, out double ax, out double az)) { Log("impact: no buildings"); return false; }
            aim = new Vector3((float)(ax - OriginX), 0f, (float)(az - OriginZ)); aim.y = Ground(aim.x, aim.z) + 5f;
            _impactAim = aim;
            var start = aim + new Vector3(-500f, 400f, -150f);
            _impactVel = (aim - start).normalized * (float)ImpactSpeedMs;
            _impactor = Prim(PrimitiveType.Sphere, "SliceImpactor", impactorMaterial, new Vector3(10f, 10f, 10f)).transform;
            _impactor.position = start;
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
            Log("impact " + (e / 1e9).ToString("F1", CultureInfo.InvariantCulture) + " GJ on " + _impactTarget + ": state " + ch.PrevState + " -> " + ch.Current.State + " (thresholds GJ " + (s1 / 1e9).ToString("F2", CultureInfo.InvariantCulture) + "/" + (s2 / 1e9).ToString("F2", CultureInfo.InvariantCulture) + "/" + (s3 / 1e9).ToString("F2", CultureInfo.InvariantCulture) + "/" + (s4 / 1e9).ToString("F2", CultureInfo.InvariantCulture) + ")");
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

        // ---- smoke ----
        const int Warmup = 60, QuakeAt = 120, ImpactAt = 240;
        void SmokeTick()
        {
            _frame++;
            if (_frame == QuakeAt) TriggerQuake();
            if (_frame == ImpactAt) TriggerImpact();
            if (_frame > Warmup) { _ms.Add(Time.unscaledDeltaTime * 1000f); _draw.Add(_drawRec.Valid ? _drawRec.LastValue : -1); }
            if (_frame >= Warmup + SmokeFrames) FinishSmoke();
        }

        static double Pct(List<double> v, double p) { var s = new List<double>(v); s.Sort(); return s.Count == 0 ? 0 : s[Math.Min(s.Count - 1, (int)(p * s.Count))]; }

        void FinishSmoke()
        {
            SmokeDone = true;
            var ms = new List<double>(); foreach (var f in _ms) ms.Add(f);
            double sum = 0; foreach (var f in ms) sum += f;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"task\": \"T-039b\", \"scene\": \"M1Slice\", \"platform\": \"").Append(Application.platform).Append("\", \"development_build\": ").Append(Debug.isDebugBuild ? "true" : "false").Append(",\n");
            sb.Append("  \"graphics_device\": \"").Append(SystemInfo.graphicsDeviceName).Append("\", \"resolution\": \"").Append(Screen.width).Append("x").Append(Screen.height).Append("\",\n");
            sb.Append("  \"pixel_pipeline\": ").Append(_binder != null ? "true" : "false").Append(", \"frames_recorded\": ").Append(ms.Count).Append(", \"warmup_frames\": ").Append(Warmup).Append(",\n");
            sb.Append("  \"frame_ms\": { \"mean\": ").Append(F(ms.Count > 0 ? sum / ms.Count : 0)).Append(", \"median\": ").Append(F(Pct(ms, 0.5))).Append(", \"p95\": ").Append(F(Pct(ms, 0.95))).Append(", \"max\": ").Append(F(Pct(ms, 1.0))).Append(" },\n");
            sb.Append("  \"draw_calls\": { \"median\": ").Append(F(Pct(_draw, 0.5))).Append(", \"max\": ").Append(F(Pct(_draw, 1.0))).Append(" },\n");
            sb.Append("  \"load_seconds\": ").Append(F(_loadSeconds)).Append(", \"tiles\": ").Append(_tileCount).Append(", \"buildings_registered\": ").Append(_buildingCount).Append(", \"landmark_colliders\": ").Append(_landmarkColliders).Append(",\n");
            sb.Append("  \"quakes\": ").Append(_quakes).Append(", \"impacts\": ").Append(_impacts).Append(", \"building_damaged_events\": ").Append(_damagedEvents).Append(", \"state_counts\": [");
            for (int i = 0; i < _stateCounts.Length; i++) sb.Append(i > 0 ? ", " : "").Append(_stateCounts[i]);
            sb.Append("], \"buildings_removed_from_scene\": ").Append(_removedCount).Append(",\n");
            sb.Append("  \"kaiju\": { \"decisions\": ").Append(_kaiju.Decisions).Append(", \"melee_hits\": ").Append(_kaiju.Hits).Append(", \"state\": \"").Append(_kaiju.State).Append("\", \"distance_to_mech_m\": ").Append(F(Mathf.Sqrt((_mech.X - _kaiju.X) * (_mech.X - _kaiju.X) + (_mech.Y - _kaiju.Y) * (_mech.Y - _kaiju.Y)))).Append(" },\n");
            sb.Append("  \"log\": \"").Append(_log.ToString().Replace("\\", "/").Replace("\"", "'").Replace("\r", "").Replace("\n", " | ")).Append("\"\n}\n");
            SmokeJson = sb.ToString();
            Debug.Log("[SliceSmoke] " + SmokeJson.Replace("\n", " "));
            if (!string.IsNullOrEmpty(SmokeOut))
            {
                try { Directory.CreateDirectory(Path.GetDirectoryName(SmokeOut)); File.WriteAllText(SmokeOut, SmokeJson); }
                catch (Exception ex) { Debug.LogError("[SliceSmoke] write failed: " + ex.Message); }
            }
            _drawRec.Dispose();
            if (!Application.isEditor) Application.Quit(0);
        }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // ---- HUD ----
        void OnGUI()
        {
            if (!Ready) { GUI.Label(new Rect(10, 10, 600, 24), "M1 slice: loading..."); return; }
            var s = "M1 slice (TEMPORARY stand-ins)  " + (1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f)).ToString("F0") + " fps   buildings destroyed " + _stateCounts[4] + "  damaged events " + _damagedEvents
                + "\nWASD move   Q quake   I impactor   kaiju " + _kaiju.State + " d=" + Mathf.Sqrt((_mech.X - _kaiju.X) * (_mech.X - _kaiju.X) + (_mech.Y - _kaiju.Y) * (_mech.Y - _kaiju.Y)).ToString("F0") + " m   mech hp " + _mech.Hp.ToString("F2")
                + "\n" + _lastMsg;
            GUI.Label(new Rect(10, 10, 900, 80), s);
        }

        void OnDestroy() { if (_bus != null) { _bus.Unsubscribe<BuildingDamaged>(this); _bus.Unsubscribe<EmergencePoint>(this); } if (_drawRec.Valid) _drawRec.Dispose(); if (Instance == this) Instance = null; }
    }
}
