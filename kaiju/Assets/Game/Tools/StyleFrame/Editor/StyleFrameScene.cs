// T-006 step 1: builds the style-frame scene by Editor script (never text-edited): tile L0_30_36 through World.Geo,
// the T-007 mech prefab wading in the Bay off the downtown waterfront at true scale, the pixel pipeline enabled with one
// palette profile through config and feature flags only. Run: -executeMethod Game.Tools.StyleFrame.StyleFrameScene.BuildCli
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Game.Cameras;
using Game.Rendering.Pixel;
using Game.Rendering.Pixel.Editor;
using Game.Tools.StyleCapture;
using Game.World.Geo;
using Game.World.Geo.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Tools.StyleFrame
{
    public static class StyleFrameScene
    {
        public const string TileName = "L0_30_36";
        public const string DefaultProfile = "slate_teal";
        public const string ScenePath = "Assets/Game/Tools/StyleFrame/Scenes/StyleFrame.unity";
        public const string MechPrefabPath = "Assets/Game/Units/Mech/Art/Prefabs/MechFrameBalanced.prefab";
        public const string LogPath = "evidence/T-006/build_log.json";
        public const string Configuration = StyleCaptureCli.M0Configuration;

        // Ferry Building and Salesforce Tower (WGS84), used only to place a search window relative to the tallest tower.
        const double FerryLon = -122.39345, FerryLat = 37.79553, TowerLon = -122.39690, TowerLat = 37.78970;
        const double SearchMargin = 60.0;
        const float DefaultMechYawDeg = 270f;   // prefab faces +Z; 270 turns it to face west (-x), toward the city (override: -styleFrameMechYaw)
        const double HeightTolerance = 0.5, MechHeight = 80.0;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ CLI
        static string Arg(string name, string fallback)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        public static void BuildCli()
        {
            int code = 0;
            try
            {
                string profile = Arg("-styleFrameProfile", DefaultProfile);
                Build(profile);
                Debug.Log("[StyleFrame] OK: " + ScenePath + ", log " + LogPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[StyleFrame] FAILED: " + e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        [MenuItem("Game/Tools/Build style-frame scene")]
        public static void BuildMenu() => Build(DefaultProfile);

        // ------------------------------------------------------------------ build
        public static void Build(string profile = DefaultProfile)
        {
            if (!GeoTileId.TryParse(TileName, out var tile)) throw new InvalidOperationException("bad tile id " + TileName);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var warnings = new List<string>();

            // 1. the tile, through the public loader, unmodified. Floating origin = tile corner, so scene = tile-local metres.
            string dir = GeoTileBuilder.TileDirectory(tile);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("tile folder missing: " + dir);
            var inst = GeoTileBuilder.Build(dir, new GeoBuildOptions
            {
                FloatingOriginX = tile.OriginX, FloatingOriginZ = tile.OriginZ,
                Settings = GeoTileBuilder.LoadSettings(), VerifyHashes = true,
            });
            var ctx = inst.Model.Context;
            var hf = ctx.Heights;
            double level = ctx.WaterLevelNavd88;
            Physics.SyncTransforms();

            // 2. hashes of the tile as built (before anything else is added to the scene)
            var tileHashes = HashTile(inst, dir, ctx, warnings);

            // 3. where to stand
            var site = FindSite(inst, level, warnings, out var siteNotes);

            // 4. mech
            float mechYaw = float.Parse(Arg("-styleFrameMechYaw", DefaultMechYawDeg.ToString(Inv)), Inv);
            var mechPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechPrefabPath) ?? throw new FileNotFoundException("mech prefab not found: " + MechPrefabPath);
            var mech = (GameObject)PrefabUtility.InstantiatePrefab(mechPrefab, scene);
            mech.name = "Mech_T007_Rest";
            var stripped = StripDynamics(mech);
            mech.transform.localScale = Vector3.one;
            mech.transform.SetPositionAndRotation(new Vector3((float)site.LocalX, 0f, (float)site.LocalZ), Quaternion.Euler(0f, mechYaw, 0f));
            Physics.SyncTransforms();
            var feet = FindFeet(mech);
            double seabedBelowFeet = double.MinValue; var footSeabed = new double[feet.Length];
            for (int i = 0; i < feet.Length; i++)
            {
                footSeabed[i] = MaxSurface(hf, feet[i].bounds, true);
                seabedBelowFeet = Math.Max(seabedBelowFeet, footSeabed[i]);
            }
            double solePre = feet.Length > 0 ? Math.Min(feet[0].bounds.min.y, feet[feet.Length - 1].bounds.min.y) : MechBounds(mech).min.y;
            mech.transform.position += new Vector3(0f, (float)(seabedBelowFeet - solePre), 0f);
            Physics.SyncTransforms();
            var mb = MechBounds(mech);
            double measuredHeight = mb.size.y;
            if (Math.Abs(measuredHeight - MechHeight) > HeightTolerance)
                throw new InvalidOperationException("mech measured height " + measuredHeight.ToString("F3", Inv) + " m, expected 80 +/- 0.5");
            Vector3 mp = mech.transform.position;
            double footCentreX = 0, footCentreZ = 0;
            foreach (var f in feet) { footCentreX += f.bounds.center.x; footCentreZ += f.bounds.center.z; }
            if (feet.Length > 0) { footCentreX /= feet.Length; footCentreZ /= feet.Length; } else { footCentreX = mp.x; footCentreZ = mp.z; }
            double seabedAtMechData = hf.SampleData(footCentreX, footCentreZ);
            double depthAtMech = level - seabedAtMechData;
            double soleY = feet.Length > 0 ? Math.Min(feet[0].bounds.min.y, feet[feet.Length - 1].bounds.min.y) : mb.min.y;

            // 5. water at the tile's sea level: the tile's own water surface, checked (never moved)
            var water = CheckWater(inst, level);

            // 5b. waterline against the legs (DEF-T006-style-4): water level vs ankle (top of the feet) and mid-shin (centre of the shank renderer)
            double ankleY = Math.Max(feet[0].bounds.max.y, feet[feet.Length - 1].bounds.max.y), shinMidY = double.NaN, shinTopY = double.NaN;
            foreach (var r in mech.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.EndsWith("shank_L", StringComparison.Ordinal)) { shinMidY = r.bounds.center.y; shinTopY = r.bounds.max.y; break; }
            if (double.IsNaN(shinMidY)) { shinTopY = soleY + 0.25 * measuredHeight; shinMidY = 0.5 * (ankleY + shinTopY); }
            string waterlineRelation = level < ankleY ? "below the ankle (feet only wet)" : level < shinMidY ? "between ankle and mid-shin" : level < shinTopY ? "above mid-shin, below the knee" : "above the shank top (knee or higher)";
            string waterlineJson = "{\"water_level_navd88_m\": " + Num(level) + ", \"sole_y_m\": " + Num(soleY) + ", \"ankle_y_m\": " + Num(ankleY) + ", \"mid_shin_y_m\": " + Num(shinMidY) + ", \"shin_top_y_m\": " + Num(shinTopY)
                + ", \"water_above_sole_m\": " + Num(level - soleY) + ", \"water_above_ankle_m\": " + Num(level - ankleY) + ", \"water_above_mid_shin_m\": " + Num(level - shinMidY) + ", \"relation\": " + Str(waterlineRelation)
                + ", \"method\": \"ankle = top of the foot renderer bounds; mid-shin = centre of the MechBalanced_shank_L renderer bounds; scene metres, NAVD88 y\"}";
            Debug.Log("[StyleFrame] waterline: " + waterlineJson);

            // 6. light, cameras, pixel pipeline (config + flags only)
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1f; light.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.Euler((float)StyleFrameShots.SunElevationDeg, StyleFrameShots.SunTravelYawDeg, 0f);   // one fixed low sun, from the side (SSW)
            var pipe = AddPixelCameras(profile, mp, mb, out var flagsOn, out string paletteSha);

            // 6b. shots and placeholder props (analytic; the hero pose is applied to the world camera, captures drive it through the rig)
            var wcn = ctx.WaterCells; int wcells = hf.Samples - 1; double wsp = hf.Spacing;
            var ground = new FrameGround
            {
                Level = level,
                IsWater = (lx, lz) => { int c = (int)Math.Floor(lx / wsp), r = (int)Math.Floor(lz / wsp); return c >= 0 && r >= 0 && c < wcells && r < wcells && wcn[r * wcells + c]; },
                Data = (lx, lz) => hf.SampleData(lx, lz),
                Render = (lx, lz) => hf.SampleRender(lx, lz),
            };
            var mechInfo = new MechInfo { X = mb.center.x, Z = mb.center.z, YWater = level, YTop = mb.max.y, YSole = soleY, YawDeg = mechYaw, HeightM = measuredHeight, WaterDepthM = depthAtMech, ScenePosition = mp };
            var towerInfo = new TowerInfo { X = site.TowerX, Z = site.TowerZ, Top = site.TowerTop, Bid = site.TowerBid };
            foreach (var bd in inst.Model.Buildings) if (bd.Excluded == null && bd.Mesh != null) ground.Buildings.Add(new Vector3((float)bd.AnchorLocalX, (float)(bd.BaseNavd88M + bd.HeightM), (float)bd.AnchorLocalZ));
            var plan = StyleFrameShots.Plan(ground, mechInfo, towerInfo);
            var worldCam = GameObject.Find(pipe).transform;
            worldCam.SetPositionAndRotation(new Vector3((float)plan.Hero.X, (float)plan.Hero.Y, (float)plan.Hero.Z), Quaternion.Euler(-(float)plan.Hero.PitchDeg, (float)plan.Hero.YawDeg, 0f));
            StyleFrameShots.AddProps(plan, StyleTestScenes.SurfaceShader);

            // 7. unit scales
            var nonUnit = FindNonUnitScales();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("could not save " + ScenePath);

            // 8. build log
            var j = new StringBuilder();
            j.Append("{\n \"schema\": \"style_frame_build@v1\",\n");
            j.Append(" \"scene\": ").Append(Str(ScenePath)).Append(",\n \"tile\": ").Append(Str(TileName)).Append(",\n");
            j.Append(" \"tile_origin_baytm\": [").Append(Num(ctx.OriginX)).Append(", ").Append(Num(ctx.OriginZ)).Append("],\n");
            j.Append(" \"floating_origin_baytm\": [").Append(Num(inst.FloatingOriginX)).Append(", ").Append(Num(inst.FloatingOriginZ)).Append("],\n");
            j.Append(" \"mech\": {\n");
            j.Append("  \"prefab\": ").Append(Str(MechPrefabPath)).Append(",\n");
            j.Append("  \"scene_position_m\": [").Append(Num(mp.x)).Append(", ").Append(Num(mp.y)).Append(", ").Append(Num(mp.z)).Append("],\n");
            j.Append("  \"baytm_position_m\": [").Append(Num(mp.x + inst.FloatingOriginX)).Append(", ").Append(Num(mp.y)).Append(", ").Append(Num(mp.z + inst.FloatingOriginZ)).Append("],\n");
            j.Append("  \"foot_centre_baytm\": [").Append(Num(footCentreX + inst.FloatingOriginX)).Append(", ").Append(Num(footCentreZ + inst.FloatingOriginZ)).Append("],\n");
            j.Append("  \"yaw_deg\": ").Append(Num(mechYaw)).Append(",\n  \"scale\": [1, 1, 1],\n");
            j.Append("  \"measured_height_m\": ").Append(Num(measuredHeight)).Append(",\n");
            j.Append("  \"bounds_min\": ").Append(Vec(mb.min)).Append(",\n  \"bounds_max\": ").Append(Vec(mb.max)).Append(",\n");
            j.Append("  \"sole_y_navd88_m\": ").Append(Num(soleY)).Append(",\n");
            j.Append("  \"seabed_under_feet_max_m\": ").Append(Num(seabedBelowFeet)).Append(",\n");
            j.Append("  \"seabed_under_each_foot_m\": [").Append(string.Join(", ", Array.ConvertAll(footSeabed, Num))).Append("],\n");
            j.Append("  \"seabed_at_foot_centre_data_m\": ").Append(Num(seabedAtMechData)).Append(",\n");
            j.Append("  \"water_depth_at_mech_m\": ").Append(Num(depthAtMech)).Append(",\n");
            j.Append("  \"stripped_components\": [").Append(string.Join(", ", stripped.ConvertAll(Str))).Append("]\n },\n");
            j.Append(" \"water\": {\"level_navd88_m\": ").Append(Num(level)).Append(", \"surface_min_y\": ").Append(Num(water.minY)).Append(", \"surface_max_y\": ").Append(Num(water.maxY)).Append(", \"renderers\": ").Append(water.count).Append("},\n");
            j.Append(" \"waterline\": ").Append(waterlineJson).Append(",\n");
            j.Append(" \"waterline\": ").Append(waterlineJson).Append(",\n");
            j.Append(" \"site\": {").Append(siteNotes).Append("},\n");
            j.Append(" \"pixel\": {\"profile\": ").Append(Str(profile)).Append(", \"palette_sha256\": ").Append(Str(paletteSha)).Append(", \"configuration\": ").Append(Str(Configuration));
            j.Append(", \"flags_on\": [").Append(string.Join(", ", flagsOn.ConvertAll(Str))).Append("], \"world_camera\": ").Append(Str(pipe)).Append("},\n");
            j.Append(" \"hashes\": {\n").Append(tileHashes).Append("\n },\n");
            j.Append(" \"non_unit_scales\": [").Append(string.Join(", ", nonUnit.ConvertAll(Str))).Append("],\n");
            j.Append(" \"warnings\": [").Append(string.Join(", ", warnings.ConvertAll(Str))).Append("]\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.WriteAllText(LogPath, j.ToString());
            StyleFrameShots.WriteProps(plan, inst.FloatingOriginX, inst.FloatingOriginZ, plan.Warnings);
            StyleFrameShots.WriteShots(plan, inst.FloatingOriginX, inst.FloatingOriginZ, profile, paletteSha, Configuration, flagsOn, TileName);
            foreach (var w in plan.Warnings) Debug.LogWarning("[StyleFrame] " + w);
            if (nonUnit.Count > 0) throw new InvalidOperationException("non-unit scales found: " + string.Join(", ", nonUnit));
            Debug.Log("[StyleFrame] hero camera BayTM (" + (plan.Hero.X + inst.FloatingOriginX).ToString("F1", Inv) + ", " + plan.Hero.Y.ToString("F1", Inv) + ", " + (plan.Hero.Z + inst.FloatingOriginZ).ToString("F1", Inv) + "), mech height fraction " + plan.Hero.MechFractionFull80.ToString("F3", Inv) + ", horizon row " + plan.Hero.HorizonRowFromTop.ToString("F3", Inv));
            Debug.Log("[StyleFrame] mech at BayTM (" + (mp.x + inst.FloatingOriginX).ToString("F1", Inv) + ", " + (mp.z + inst.FloatingOriginZ).ToString("F1", Inv) + "), depth " + depthAtMech.ToString("F2", Inv) + " m, height " + measuredHeight.ToString("F2", Inv) + " m");
        }

        // ------------------------------------------------------------------ site search
        sealed class Site { public double LocalX, LocalZ, Depth, Offshore, TowerX, TowerZ, TowerTop; public string Pass, TowerBid; }

        static Site FindSite(GeoTileInstance inst, double level, List<string> warnings, out string notes)
        {
            var ctx = inst.Model.Context; var hf = ctx.Heights;
            int n = hf.Samples; int cn = n - 1; double sp = hf.Spacing;
            var water = ctx.WaterCells ?? throw new InvalidOperationException("tile has no water mask");
            bool Water(double lx, double lz)
            {
                int c = (int)Math.Floor(lx / sp), r = (int)Math.Floor(lz / sp);
                return c >= 0 && r >= 0 && c < cn && r < cn && water[r * cn + c];
            }

            // tallest surface building = the tower anchor; Ferry Building placed relative to it (cancels projection bias)
            GeoBuildingData tower = null;
            foreach (var b in inst.Model.Buildings)
                if (b.Excluded == null && b.Mesh != null && (tower == null || b.BaseNavd88M + b.HeightM > tower.BaseNavd88M + tower.HeightM)) tower = b;
            if (tower == null) throw new InvalidOperationException("tile has no buildings");
            double towerTop = tower.BaseNavd88M + tower.HeightM;
            var bounds = ctx.Manifest["bounds_wgs84"] as System.Collections.IEnumerable ?? throw new InvalidOperationException("manifest has no bounds_wgs84");
            var bw = new List<double>(); foreach (var o in bounds) bw.Add(Convert.ToDouble(o, Inv));
            double mx = tile2048 / (bw[2] - bw[0]), mz = tile2048 / (bw[3] - bw[1]);   // m per degree (linear, local use only)
            double fx = tower.AnchorLocalX + (FerryLon - TowerLon) * mx, fz = tower.AnchorLocalZ + (FerryLat - TowerLat) * mz;

            double x0 = Math.Max(SearchMargin, fx + 100), x1 = Math.Min(tile2048 - SearchMargin, fx + 1000);
            double z0 = Math.Max(SearchMargin, fz - 500), z1 = Math.Min(tile2048 - SearchMargin, fz + 300);

            // distance to the nearest land cell (m), two-pass chamfer on the cell grid
            var dist = new float[cn * cn];
            const float Big = 1e9f; float d1 = (float)sp, d2 = (float)(sp * 1.41421356);
            for (int i = 0; i < dist.Length; i++) dist[i] = water[i] ? Big : 0f;
            for (int r = 0; r < cn; r++)
                for (int c = 0; c < cn; c++)
                {
                    int i = r * cn + c; float d = dist[i];
                    if (c > 0) d = Math.Min(d, dist[i - 1] + d1);
                    if (r > 0) { d = Math.Min(d, dist[i - cn] + d1); if (c > 0) d = Math.Min(d, dist[i - cn - 1] + d2); if (c < cn - 1) d = Math.Min(d, dist[i - cn + 1] + d2); }
                    dist[i] = d;
                }
            for (int r = cn - 1; r >= 0; r--)
                for (int c = cn - 1; c >= 0; c--)
                {
                    int i = r * cn + c; float d = dist[i];
                    if (c < cn - 1) d = Math.Min(d, dist[i + 1] + d1);
                    if (r < cn - 1) { d = Math.Min(d, dist[i + cn] + d1); if (c < cn - 1) d = Math.Min(d, dist[i + cn + 1] + d2); if (c > 0) d = Math.Min(d, dist[i + cn - 1] + d2); }
                    dist[i] = d;
                }
            double Off(double lx, double lz) { int c = (int)(lx / sp), r = (int)(lz / sp); return dist[Math.Min(cn - 1, r) * cn + Math.Min(cn - 1, c)]; }

            bool Los(double lx, double lz)
            {
                // terrain-only line of sight from 2 m above the water to the tower top (other buildings ignored)
                double ex = tower.AnchorLocalX, ez = tower.AnchorLocalZ; double dx = ex - lx, dz = ez - lz; double len = Math.Sqrt(dx * dx + dz * dz);
                double y0 = level + 2.0;
                for (double t = 8; t < len - 40; t += 8)
                {
                    double y = y0 + (towerTop - y0) * (t / len);
                    if (hf.SampleData(lx + dx * t / len, lz + dz * t / len) > y) return false;
                }
                return true;
            }

            bool Flat(double lx, double lz, double depth)
            {
                for (int k = 0; k < 8; k++)
                {
                    double a = k * Math.PI / 4, px = lx + 10 * Math.Cos(a), pz = lz + 10 * Math.Sin(a);
                    if (!Water(px, pz)) return false;
                    double d = level - hf.SampleData(px, pz);
                    if (Math.Abs(d - depth) > 0.75) return false;
                }
                return true;
            }

            (double dMin, double dMax, double oMin, double oMax, bool los, string name)[] passes =
            {
                (0.5, 6.0, 300, 800, true, "A: depth 0.5-6 m, 300-800 m offshore, flat, tower in line of sight"),
                (0.5, 6.0, 300, 800, false, "B: as A without line of sight"),
                (0.5, 12.0, 150, 1500, false, "C: depth 0.5-12 m, 150-1500 m offshore, flat"),
                (0.5, 40.0, 100, 2000, false, "D: any flat water 100-2000 m offshore (real depth reported)"),
            };
            foreach (var p in passes)
            {
                Site best = null; double bestKey = double.MaxValue;
                for (double lz = z0; lz <= z1; lz += 4)
                    for (double lx = x0; lx <= x1; lx += 4)
                    {
                        if (!Water(lx, lz)) continue;
                        double depth = level - hf.SampleData(lx, lz);
                        if (depth < p.dMin || depth > p.dMax) continue;
                        double off = Off(lx, lz);
                        if (off < p.oMin || off > p.oMax) continue;
                        if (!Flat(lx, lz, depth)) continue;
                        if (p.los && !Los(lx, lz)) continue;
                        double key = depth + 0.002 * Math.Abs(off - 500);
                        if (key < bestKey) { bestKey = key; best = new Site { LocalX = lx, LocalZ = lz, Depth = depth, Offshore = off, Pass = p.name, TowerX = tower.AnchorLocalX, TowerZ = tower.AnchorLocalZ, TowerTop = towerTop, TowerBid = tower.Bid }; }
                    }
                if (best != null)
                {
                    if (p.name[0] != 'A') warnings.Add("site search fell back to pass " + p.name);
                    notes = "\"search_pass\": " + Str(best.Pass) + ", \"ferry_building_baytm_estimate\": [" + Num(fx + ctx.OriginX) + ", " + Num(fz + ctx.OriginZ) + "], \"tower_anchor_baytm\": ["
                        + Num(tower.AnchorLocalX + ctx.OriginX) + ", " + Num(tower.AnchorLocalZ + ctx.OriginZ) + "], \"tower_bid\": " + Str(tower.Bid) + ", \"tower_top_navd88_m\": " + Num(towerTop)
                        + ", \"window_baytm\": [" + Num(x0 + ctx.OriginX) + ", " + Num(z0 + ctx.OriginZ) + ", " + Num(x1 + ctx.OriginX) + ", " + Num(z1 + ctx.OriginZ) + "], \"chosen_baytm\": ["
                        + Num(best.LocalX + ctx.OriginX) + ", " + Num(best.LocalZ + ctx.OriginZ) + "], \"chosen_depth_m\": " + Num(best.Depth) + ", \"chosen_offshore_m\": " + Num(best.Offshore)
                        + ", \"depth_source\": \"terrain.tif (CUDEM topobathy merged into the baked tile), water level minus SampleData\"";
                    return best;
                }
            }
            throw new InvalidOperationException("no water site found in the search window");
        }

        const double tile2048 = 2048.0;

        // ------------------------------------------------------------------ mech helpers
        static List<string> StripDynamics(GameObject mech)
        {
            var found = new List<Component>();
            foreach (var c in mech.GetComponentsInChildren<Component>(true))
                if (c is Rigidbody || c is ArticulationBody || c is Collider || c is Joint || c is Animator || c is Animation || c is CharacterController) found.Add(c);
            var names = new List<string>();
            if (found.Count == 0) return names;
            PrefabUtility.UnpackPrefabInstance(mech, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // Joints/colliders depend on bodies: destroy dependants first
            found.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            foreach (var c in found) { names.Add(c.GetType().Name + "@" + c.gameObject.name); UnityEngine.Object.DestroyImmediate(c); }
            return names;
        }
        static int Rank(Component c) => c is Joint ? 0 : c is Collider ? 1 : c is Rigidbody || c is ArticulationBody ? 2 : 3;

        static Renderer[] FindFeet(GameObject mech)
        {
            var feet = new List<Renderer>();
            foreach (var r in mech.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.EndsWith("foot_L", StringComparison.Ordinal) || r.gameObject.name.EndsWith("foot_R", StringComparison.Ordinal)) feet.Add(r);
            if (feet.Count != 2) throw new InvalidOperationException("expected 2 foot renderers (MechBalanced_foot_L/R), found " + feet.Count);
            return feet.ToArray();
        }

        static Bounds MechBounds(GameObject go)
        {
            Bounds b = default; bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            if (first) throw new InvalidOperationException("mech has no renderers");
            return b;
        }

        /// <summary>Highest surface under a world-space XZ rectangle (1 m steps). Scene = tile-local metres.</summary>
        static double MaxSurface(GeoHeightField hf, Bounds b, bool render)
        {
            double m = double.MinValue;
            for (double z = b.min.z; z <= b.max.z + 1e-6; z += 1.0)
                for (double x = b.min.x; x <= b.max.x + 1e-6; x += 1.0)
                    m = Math.Max(m, render ? hf.SampleRender(x, z) : hf.SampleData(x, z));
            return m;
        }

        // ------------------------------------------------------------------ water, cameras, scales
        static (double minY, double maxY, int count) CheckWater(GeoTileInstance inst, double level)
        {
            double mn = double.MaxValue, mxv = double.MinValue; int cnt = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.parent == null || r.transform.parent.name != "Water") continue;
                var b = r.bounds; mn = Math.Min(mn, b.min.y); mxv = Math.Max(mxv, b.max.y); cnt++;
            }
            if (cnt == 0) throw new InvalidOperationException("tile has no water renderers");
            if (Math.Abs(mn - level) > 0.01 || Math.Abs(mxv - level) > 0.01)
                throw new InvalidOperationException("water surface y [" + mn.ToString("F3", Inv) + ", " + mxv.ToString("F3", Inv) + "] differs from tile sea level " + level.ToString("F3", Inv));
            return (mn, mxv, cnt);
        }

        static string AddPixelCameras(string profile, Vector3 mechPos, Bounds mechBounds, out List<string> flagsOn, out string paletteSha)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PixelPipelineCatalog>(PixelPipelineInstaller.CatalogPath)
                ?? throw new InvalidOperationException("pixel pipeline assets missing: run PixelPipelineInstaller.InstallAll");
            var prof = catalog.FindProfile(profile) ?? throw new ArgumentException("unknown palette profile " + profile);
            var flags = PixelCaptureRig.LoadFlags(Configuration);
            if (!flags.IsOn(PixelFlags.Enabled)) throw new InvalidOperationException("configuration " + Configuration + " does not enable " + PixelFlags.Enabled);
            flagsOn = new List<string>(flags.OnFlags); flagsOn.Sort(StringComparer.Ordinal);
            paletteSha = prof.paletteSha256;

            // provisional pose for inspection only; the capture jobs of later steps drive the real shot pose
            Vector3 focus = mechBounds.center;
            Vector3 pos = new Vector3(mechPos.x + 420f, mechPos.y + 45f, mechPos.z - 420f);
            var worldGo = new GameObject("PixelWorldCamera");
            worldGo.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focus - pos, Vector3.up));
            var world = worldGo.AddComponent<Camera>();
            world.fieldOfView = StyleTestScenes.VerticalFovDeg(16f / 9f); world.nearClipPlane = StyleTestScenes.NearPlane; world.farClipPlane = StyleTestScenes.FarPlane;
            world.clearFlags = CameraClearFlags.SolidColor; world.backgroundColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            world.allowMSAA = false; world.allowHDR = true; world.depth = -10; world.useOcclusionCulling = false;
            var wd = worldGo.AddComponent<UniversalAdditionalCameraData>();
            wd.SetRenderer(catalog.worldRendererIndex);
            wd.renderPostProcessing = false; wd.antialiasing = AntialiasingMode.None;
            wd.renderShadows = false; wd.requiresDepthTexture = true; wd.requiresColorTexture = false;
            var binder = worldGo.AddComponent<PixelCameraBinder>();
            binder.catalog = catalog; binder.profile = prof;

            var presentGo = new GameObject("PixelPresentCamera");
            var present = presentGo.AddComponent<Camera>();
            present.cullingMask = 0; present.clearFlags = CameraClearFlags.SolidColor; present.backgroundColor = Color.black;
            present.allowMSAA = false; present.allowHDR = false; present.depth = 10; present.useOcclusionCulling = false;
            var pd = presentGo.AddComponent<UniversalAdditionalCameraData>();
            pd.SetRenderer(catalog.presentRendererIndex);
            pd.renderPostProcessing = false; pd.antialiasing = AntialiasingMode.None; pd.renderShadows = false;
            pd.requiresDepthTexture = false; pd.requiresColorTexture = false;
            presentGo.AddComponent<PixelPresentCamera>().source = binder;

            var rig = worldGo.AddComponent<ThirdPersonRig>();
            rig.settingsJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/Camera/ThirdPersonRig.json");
            return worldGo.name;
        }

        static List<string> FindNonUnitScales()
        {
            var bad = new List<string>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var s = t.localScale; var l = t.lossyScale;
                    if (Mathf.Abs(s.x - 1f) > 1e-6f || Mathf.Abs(s.y - 1f) > 1e-6f || Mathf.Abs(s.z - 1f) > 1e-6f
                        || Mathf.Abs(l.x - 1f) > 1e-5f || Mathf.Abs(l.y - 1f) > 1e-5f || Mathf.Abs(l.z - 1f) > 1e-5f)
                        bad.Add(PathOf(t) + " local " + s.ToString("F6") + " lossy " + l.ToString("F6"));
                }
            return bad;
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent != null) { t = t.parent; s = t.name + "/" + s; } return s; }

        // ------------------------------------------------------------------ hashes
        static string HashTile(GeoTileInstance inst, string dir, GeoTileContext ctx, List<string> warnings)
        {
            var sb = new StringBuilder();
            string terrain = HashMeshes(inst, "Terrain", 0f, false), buildings = HashMeshes(inst, "BuildingChunks", 0f, false);
            string seabed = HashMeshes(inst, "Terrain", (float)ctx.WaterLevelNavd88, true);
            string water = HashMeshes(inst, "Water", 0f, false);
            string heights;
            using (var sha = SHA256.Create())
            {
                var data = ctx.Heights.Data; var bytes = new byte[data.Length * 4]; Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
                heights = Hex(sha.ComputeHash(bytes));
                var sea = new List<byte>();
                for (int i = 0; i < data.Length; i++) if (data[i] < ctx.WaterLevelNavd88) sea.AddRange(BitConverter.GetBytes(data[i]));
                sb.Append("  \"seabed_height_samples_below_sea_level_sha256\": ").Append(Str(Hex(sha.ComputeHash(sea.ToArray())))).Append(",\n");
            }
            sb.Append("  \"terrain_mesh_vertices_sha256\": ").Append(Str(terrain)).Append(",\n");
            sb.Append("  \"seabed_mesh_vertices_sha256\": ").Append(Str(seabed)).Append(",\n  \"seabed_definition\": \"terrain-mesh vertices with y < sea level, in mesh order\",\n");
            sb.Append("  \"building_chunk_mesh_vertices_sha256\": ").Append(Str(buildings)).Append(",\n");
            sb.Append("  \"water_mesh_vertices_sha256\": ").Append(Str(water)).Append(",\n");
            sb.Append("  \"heightfield_float32_sha256\": ").Append(Str(heights)).Append(",\n");
            sb.Append("  \"baked_files\": {");
            bool first = true;
            foreach (var layer in new[] { "terrain", "buildings", "water" })
            {
                var m = ctx.ManifestLayer(layer);
                if (m == null) { warnings.Add("manifest has no layer " + layer); continue; }
                string file = m.TryGetValue("file", out var f) ? Convert.ToString(f, Inv) : null;
                string want = m.TryGetValue("sha256", out var s) ? Convert.ToString(s, Inv) : null;
                string got = null;
                if (file != null && File.Exists(Path.Combine(dir, file))) using (var sha = SHA256.Create()) got = Hex(sha.ComputeHash(File.ReadAllBytes(Path.Combine(dir, file))));
                if (got == null || got != want) throw new InvalidOperationException("baked file " + file + " hash " + got + " differs from manifest " + want);
                sb.Append(first ? "" : ",").Append("\n   ").Append(Str(layer)).Append(": {\"file\": ").Append(Str(file)).Append(", \"sha256\": ").Append(Str(got)).Append(", \"manifest_sha256\": ").Append(Str(want)).Append("}");
                first = false;
            }
            sb.Append("\n  }");
            return sb.ToString();
        }

        /// <summary>SHA-256 over the vertex positions (float32 bytes) of every mesh under a named child of the tile root, in hierarchy order.</summary>
        static string HashMeshes(GeoTileInstance inst, string rootName, float belowY, bool onlyBelow)
        {
            var root = inst.transform.Find(rootName) ?? throw new InvalidOperationException("tile has no child " + rootName);
            using (var sha = SHA256.Create())
            {
                var tmp = new List<byte>();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh; if (mesh == null) continue;
                    var v = mesh.vertices;
                    foreach (var p in v)
                    {
                        if (onlyBelow && p.y >= belowY) continue;
                        tmp.AddRange(BitConverter.GetBytes(p.x)); tmp.AddRange(BitConverter.GetBytes(p.y)); tmp.AddRange(BitConverter.GetBytes(p.z));
                    }
                    if (tmp.Count > (1 << 24)) { sha.TransformBlock(tmp.ToArray(), 0, tmp.Count, null, 0); tmp.Clear(); }
                }
                var rest = tmp.ToArray(); sha.TransformFinalBlock(rest, 0, rest.Length);
                return Hex(sha.Hash);
            }
        }

        // ------------------------------------------------------------------ small formatting helpers
        static string Hex(byte[] b) { var s = new StringBuilder(b.Length * 2); foreach (var x in b) s.Append(x.ToString("x2", Inv)); return s.ToString(); }
        internal static string Num(double v) => v.ToString("R", Inv);
        static string Vec(Vector3 v) => "[" + Num(v.x) + ", " + Num(v.y) + ", " + Num(v.z) + "]";
        internal static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s) { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", Inv)); else sb.Append(c); }
            return sb.Append('"').ToString();
        }
    }
}
