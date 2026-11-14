// T-006 step 2: shot planning for the style frame. Pure analytic geometry (no randomness, no time): the hero camera and the
// three alternative framings are placed from the mech, the tallest tower and the tile's own water and ground; the placeholder
// props (true-size boxes) are placed so each lands 1-3 internal pixels in the hero. Writes shot_params.json and placeholder_props.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Cameras;
using UnityEngine;

namespace Game.Tools.StyleFrame
{
    /// <summary>Terrain and water lookups in scene (tile-local) metres, supplied by the builder from the loaded tile.</summary>
    public sealed class FrameGround
    {
        public Func<double, double, bool> IsWater;
        public Func<double, double, double> Data, Render;   // seabed/ground (data) and rendered surface height, NAVD88 m
        public double Level, TileSize = 2048.0;
        public double Depth(double x, double z) => Level - Data(x, z);
        public bool Inside(double x, double z, double margin) => x >= margin && z >= margin && x <= TileSize - margin && z <= TileSize - margin;
        public bool ShoreLand(double x, double z, double reach) =>
            !IsWater(x, z) && (IsWater(x + reach, z) || IsWater(x - reach, z) || IsWater(x, z + reach) || IsWater(x, z - reach));
        /// <summary>Distance to the nearest non-water point (or the tile edge) in 16 directions, in 4 m rings; maxR + 1 when clear.</summary>
        public double LandClearance(double x, double z, double maxR)
        {
            for (double r = 4; r <= maxR; r += 4)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8, px = x + r * Math.Cos(a), pz = z + r * Math.Sin(a);
                    if (!Inside(px, pz, 1) || !IsWater(px, pz)) return r;
                }
            return maxR + 1;
        }
    }

    public sealed class MechInfo { public double X, Z, YWater, YTop, YSole, YawDeg, HeightM, WaterDepthM; public Vector3 ScenePosition; }
    public sealed class TowerInfo { public double X, Z, Top; public string Bid; }

    /// <summary>Pinhole model of the world camera at the internal grid: 60 degrees horizontal, 16:9, pitch up positive (rig convention).</summary>
    public sealed class CamModel
    {
        public static readonly double TanH = Math.Tan(StyleFrameShots.HorizontalFovDeg * 0.5 * Math.PI / 180.0);
        public static readonly double TanV = TanH * 9.0 / 16.0;
        public static double VerticalFovDeg => 2.0 * Math.Atan(TanV) * 180.0 / Math.PI;
        readonly double m_X, m_Y, m_Z, fx, fy, fz, rx, rz, ux, uy, uz;

        public CamModel(double x, double y, double z, double yawDeg, double pitchDeg)
        {
            m_X = x; m_Y = y; m_Z = z;
            double yw = yawDeg * Math.PI / 180.0, p = pitchDeg * Math.PI / 180.0;
            fx = Math.Sin(yw) * Math.Cos(p); fy = Math.Sin(p); fz = Math.Cos(yw) * Math.Cos(p);
            rx = Math.Cos(yw); rz = -Math.Sin(yw);
            ux = fy * rz; uy = fz * rx - fx * rz; uz = -fy * rx;
        }

        public bool Project(double wx, double wy, double wz, out double ndcX, out double ndcY, out double depth)
        {
            double vx = wx - m_X, vy = wy - m_Y, vz = wz - m_Z;
            depth = vx * fx + vy * fy + vz * fz;
            ndcX = ndcY = 0;
            if (depth < 0.5) return false;
            double x = vx * rx + vz * rz, y = vx * ux + vy * uy + vz * uz;
            ndcX = x / (depth * TanH); ndcY = y / (depth * TanV);
            return true;
        }
    }

    public sealed class ShotDef
    {
        public string Name;
        public double X, Y, Z, YawDeg, PitchDeg, ReferenceDepth, DistanceToMech, HeightAboveSurface;
        public string Anchor;
        public double MechFractionVisible, MechFractionFull80, HorizonRowFromTop, MechBaseRowFromTop, MechTopRowFromTop;
        public double TowerOffsetFromMechDeg, TowerTopRowFromTop, TowerTopColFromLeft, CompassAzimuthFromMechDeg;
        public bool TowerInFrame;
        public CameraPose Pose => new CameraPose { Position = new CameraVec3((float)X, (float)Y, (float)Z), YawDeg = (float)YawDeg, PitchDeg = (float)PitchDeg };
        public CamModel Model => new CamModel(X, Y, Z, YawDeg, PitchDeg);
    }

    public sealed class PropDef
    {
        public string Name, Kind, Surface, Note;
        public Vector3 Size;          // w (x), h (y), d (z) metres
        public double X, Y, Z, YawDeg, Value;
        public bool Placed;
    }

    public sealed class ShotPlan
    {
        public ShotDef Hero, Closer, Wider, Waterfront;
        public List<PropDef> Props = new List<PropDef>();
        public MechInfo Mech; public TowerInfo Tower;
        public List<string> Warnings = new List<string>();
        public ShotDef[] All => new[] { Hero, Closer, Wider, Waterfront };
        public ShotDef[] Alternatives => new[] { Closer, Wider, Waterfront };
    }

    public static class StyleFrameShots
    {
        public const double HorizontalFovDeg = 60.0;        // Config/Camera/ThirdPersonRig.json horizontalFovDeg
        public const double SunFromAzimuthDeg = 195.0, SunElevationDeg = 15.0;   // compass bearing of the sun (SSW), low
        public static float SunTravelYawDeg => (float)((SunFromAzimuthDeg + 180.0) % 360.0);
        public const string PropsPath = "evidence/T-006/placeholder_props.json", ShotsPath = "evidence/T-006/shot_params.json";
        public static ShotPlan LastPlan;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly int[][] Internal = { new[] { 480, 270, 4 }, new[] { 512, 288, 5 } };   // 1080p and 1440p internal grids, integer scales
        static readonly string[] ResNames = { "1080p", "1440p" };

        // ------------------------------------------------------------------ framing maths
        static double Deg(double rad) => rad * 180.0 / Math.PI;
        static double Wrap(double d) { d %= 360.0; if (d > 180) d -= 360; if (d < -180) d += 360; return d; }
        static double Bearing(double fx, double fz, double tx, double tz) => Deg(Math.Atan2(tx - fx, tz - fz));

        /// <summary>Ground distance D at which a target spanning y in [yb, yt] seen from height h fills the frame between ndc rows nb and nt (pitch follows).</summary>
        static double SolveDistance(double h, double yb, double yt, double nb, double nt, out double pitchRad)
        {
            double T = CamModel.TanV, lo = 40, hi = 6000;
            for (int i = 0; i < 100; i++)
            {
                double mid = 0.5 * (lo + hi);
                double eb = Math.Atan((yb - h) / mid), et = Math.Atan((yt - h) / mid), p = eb - Math.Atan(nb * T);
                double g = Math.Tan(et - p) - Math.Tan(eb - p) - (nt - nb) * T;
                if (g > 0) lo = mid; else hi = mid;
            }
            double d = 0.5 * (lo + hi);
            pitchRad = Math.Atan((yb - h) / d) - Math.Atan(nb * T);
            return d;
        }

        static double PickAzimuth(FrameGround g, MechInfo m, TowerInfo t, double D, double h, double wantOffsetDeg, double thMin, double thMax, double camXMax, out double offset, List<string> warnings, string name)
        {
            double best = double.MaxValue, bestTh = 90, bestOff = 0; bool found = false;
            for (double th = thMin; th <= thMax + 1e-9; th += 0.25)
            {
                double rad = th * Math.PI / 180.0, cx = m.X + D * Math.Sin(rad), cz = m.Z + D * Math.Cos(rad);
                if (!g.Inside(cx, cz, 80) || cx > camXMax || !g.IsWater(cx, cz) || g.Depth(cx, cz) < 1.5) continue;
                double yaw = Bearing(cx, cz, m.X, m.Z), off = Wrap(Bearing(cx, cz, t.X, t.Z) - yaw);
                double key = Math.Abs(off - wantOffsetDeg) + 0.02 * Math.Abs(th - 90.0);
                if (key < best) { best = key; bestTh = th; bestOff = off; found = true; }
            }
            if (!found) warnings.Add("shot " + name + ": no camera azimuth over water satisfied the constraints; using azimuth 90");
            offset = bestOff;
            return bestTh;
        }

        static ShotDef MakeShot(string name, double cx, double cy, double cz, MechInfo m, TowerInfo t, double nb, double heightAbove, double azimuth)
        {
            double D = Math.Sqrt((m.X - cx) * (m.X - cx) + (m.Z - cz) * (m.Z - cz));
            double yaw = Bearing(cx, cz, m.X, m.Z);
            double eb = Math.Atan((m.YWater - cy) / D), pitch = eb - Math.Atan(nb * CamModel.TanV);
            var s = new ShotDef { Name = name, X = cx, Y = cy, Z = cz, YawDeg = yaw, PitchDeg = Deg(pitch), ReferenceDepth = D, DistanceToMech = D, HeightAboveSurface = heightAbove, CompassAzimuthFromMechDeg = azimuth };
            var cam = s.Model;
            cam.Project(m.X, m.YWater, m.Z, out _, out double nyB, out _);
            cam.Project(m.X, m.YTop, m.Z, out _, out double nyT, out _);
            cam.Project(m.X, m.YSole, m.Z, out _, out double nyS, out _);
            s.MechFractionVisible = (nyT - nyB) * 0.5; s.MechFractionFull80 = (nyT - nyS) * 0.5;
            s.MechBaseRowFromTop = (1 - nyB) * 0.5; s.MechTopRowFromTop = (1 - nyT) * 0.5;
            s.HorizonRowFromTop = (1 + Math.Tan(pitch) / CamModel.TanV) * 0.5;   // el = 0 ray: ndc = tan(-p)/T
            s.TowerOffsetFromMechDeg = Wrap(Bearing(cx, cz, t.X, t.Z) - yaw);
            bool ok = cam.Project(t.X, t.Top, t.Z, out double nx, out double ny, out _);
            s.TowerInFrame = ok && Math.Abs(nx) <= 1 && Math.Abs(ny) <= 1;
            s.TowerTopColFromLeft = (1 + nx) * 0.5; s.TowerTopRowFromTop = (1 - ny) * 0.5;
            return s;
        }

        /// <summary>A camera over open water east of the mech: distance from the framing solve, azimuth chosen so the tallest tower stands left of the mech.</summary>
        static ShotDef OverWater(FrameGround g, MechInfo m, TowerInfo t, string name, double fraction, double nb, double heightAbove, double wantOffset, double camXMax, List<string> warnings)
        {
            double h = g.Level + heightAbove;
            double D = SolveDistance(h, m.YWater, m.YTop, nb, nb + 2 * fraction, out _);
            double th = PickAzimuth(g, m, t, D, h, wantOffset, 40, 140, camXMax, out _, warnings, name);
            double rad = th * Math.PI / 180.0;
            return MakeShot(name, m.X + D * Math.Sin(rad), h, m.Z + D * Math.Cos(rad), m, t, nb, heightAbove, th);
        }

        static ShotDef Waterfront(FrameGround g, MechInfo m, TowerInfo t, List<string> warnings)
        {
            const double fraction = 0.32, nb = -0.35;
            double D0 = SolveDistance(g.Level + 5.0, m.YWater, m.YTop, nb, nb + 2 * fraction, out _);
            for (int pass = 0; pass < 2; pass++)
            {
                double tol = pass == 0 ? 25 : 90, sector = pass == 0 ? 20 : 40, best = double.MaxValue, bx = 0, bz = 0;
                for (double z = m.Z - D0 - 120; z <= m.Z + D0 + 120; z += 4)
                    for (double x = m.X - D0 - 120; x <= m.X; x += 4)
                    {
                        if (!g.Inside(x, z, 80) || !g.ShoreLand(x, z, 6) || g.Render(x, z) < g.Level + 0.5) continue;
                        double d = Math.Sqrt((m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z)), b = Bearing(x, z, m.X, m.Z);
                        if (Math.Abs(d - D0) > tol || Math.Abs(b - 90) > sector) continue;
                        double key = Math.Abs(d - D0) + 0.5 * Math.Abs(b - 90);
                        if (key < best) { best = key; bx = x; bz = z; }
                    }
                if (best < double.MaxValue)
                {
                    if (pass == 1) warnings.Add("waterfront: relaxed search (distance +/-90 m, +/-40 deg of east) was needed");
                    double gy = Math.Max(g.Render(bx, bz), g.Level);
                    return MakeShot("waterfront", bx, gy + 3.0, bz, m, t, nb, 3.0, Deg(Math.Atan2(bx - m.X, bz - m.Z)));
                }
            }
            throw new InvalidOperationException("no shore position found for the waterfront framing");
        }

        // ------------------------------------------------------------------ props
        static PropDef Box(string name, string kind, Vector3 size, double value) => new PropDef { Name = name, Kind = kind, Size = size, Value = value };

        static bool FindShore(FrameGround g, ShotDef cam, double bearingRel, double dMin, double dMax, out double x, out double z)
        {
            double a = (cam.YawDeg + bearingRel) * Math.PI / 180.0, sx = Math.Sin(a), sz = Math.Cos(a);
            for (double r = dMin; r <= dMax; r += 2)
            {
                double px = cam.X + r * sx, pz = cam.Z + r * sz;
                if (!g.Inside(px, pz, 4)) break;
                if (!g.IsWater(px, pz) && g.Render(px, pz) > g.Level + 0.3)
                {
                    double ix = px + 5 * sx, iz = pz + 5 * sz;     // five metres inland
                    if (!g.IsWater(ix, iz)) { x = ix; z = iz; return true; }
                }
            }
            x = z = 0; return false;
        }

        static bool FindWater(FrameGround g, ShotDef cam, double dMin, double dMax, double clearance, out double x, out double z, out double bearing)
        {
            for (int k = 0; k <= 10; k++)
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    double b = sign * (6 + k);
                    double a = (cam.YawDeg + b) * Math.PI / 180.0;
                    for (double r = dMin; r <= dMax; r += 4)
                    {
                        double px = cam.X + r * Math.Sin(a), pz = cam.Z + r * Math.Cos(a);
                        if (!g.Inside(px, pz, 20) || !g.IsWater(px, pz)) break;   // shore reached on this ray
                        if (g.Depth(px, pz) >= 1.2 && g.LandClearance(px, pz, clearance) > clearance) { x = px; z = pz; bearing = b; return true; }
                    }
                }
            x = z = bearing = 0; return false;
        }

        static void PlaceProps(FrameGround g, ShotPlan plan)
        {
            var hero = plan.Hero;
            double[] figureBearings = { -13.0, -8.5, 9.5 };
            for (int i = 0; i < 3; i++)
            {
                var p = Box("figure_" + (i + 1), "figure 1.8 m", new Vector3(0.5f, 1.8f, 0.4f), 0.04);
                p.Surface = "land (shoreline)";
                if (FindShore(g, hero, figureBearings[i], 300, 520, out p.X, out p.Z)) { p.Y = Math.Max(g.Render(p.X, p.Z), g.Level); p.Placed = true; }
                else plan.Warnings.Add(p.Name + ": no shore ground found on the ray " + figureBearings[i].ToString("F1", Inv) + " deg from the hero view axis");
                plan.Props.Add(p);
            }
            var car = Box("car_1", "car 4.5 x 1.5 x 1.8 m (length along local z, bow toward the camera)", new Vector3(1.8f, 1.5f, 4.5f), 0.92);
            car.Surface = "land (shoreline)";
            if (FindShore(g, hero, 14.0, 300, 520, out car.X, out car.Z)) { car.Y = Math.Max(g.Render(car.X, car.Z), g.Level); car.Placed = true; }
            else plan.Warnings.Add("car_1: no shore ground found");
            plan.Props.Add(car);
            var boat = Box("boat_1", "small boat hull 8 x 1.5 x 2.4 m (length along local z, bow toward the camera)", new Vector3(2.4f, 1.5f, 8.0f), 0.92);
            boat.Surface = "water";
            if (FindWater(g, hero, 360, 560, 14, out boat.X, out boat.Z, out double bb)) { boat.Y = g.Level - 0.4; boat.Placed = true; boat.Note = "bearing " + bb.ToString("F0", Inv) + " deg from the view axis; hull sits 0.4 m below the water line"; }
            else plan.Warnings.Add("boat_1: no open water 360-560 m from the hero camera");
            plan.Props.Add(boat);
            foreach (var p in plan.Props)
                if (p.Placed) p.YawDeg = Bearing(p.X, p.Z, hero.X, hero.Z);   // local +z (bow, front) toward the hero camera
        }

        static Mesh MakeBox(string name, Vector3 size)
        {
            float x = size.x * 0.5f, z = size.z * 0.5f, h = size.y;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), nn) < 0) { var tmp = b; b = d; d = tmp; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(nn);
                t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
            Face(new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(x, 0, z), Vector3.right);
            Face(new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(-x, h, z), new Vector3(-x, 0, z), Vector3.left);
            Face(new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, h, z), new Vector3(-x, h, z), Vector3.up);
            Face(new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z), Vector3.down);
            Face(new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(x, h, z), new Vector3(x, 0, z), Vector3.forward);
            Face(new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(x, 0, -z), Vector3.back);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Creates the prop objects under one root, all at scale 1 (the box meshes carry the true size). Pivot = centre of the base.</summary>
        public static void AddProps(ShotPlan plan, string shaderName)
        {
            var root = new GameObject("PlaceholderProps");
            var shader = Shader.Find(shaderName) ?? throw new InvalidOperationException("shader not found: " + shaderName);
            foreach (var p in plan.Props)
            {
                if (!p.Placed) continue;
                var go = new GameObject(p.Name);
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(new Vector3((float)p.X, (float)p.Y, (float)p.Z), Quaternion.Euler(0f, (float)p.YawDeg, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = MakeBox(p.Name + "_box", p.Size);
                var r = go.AddComponent<MeshRenderer>();
                var mat = new Material(shader) { name = "StyleFrameProp_" + p.Name };
                mat.SetFloat("_Value", (float)p.Value); mat.SetVector("_TintSrgb", new Vector4(1, 1, 1, 0)); mat.SetFloat("_Unlit", 1f);
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            }
        }

        // ------------------------------------------------------------------ plan
        public static ShotPlan Plan(FrameGround g, MechInfo m, TowerInfo t)
        {
            var plan = new ShotPlan { Mech = m, Tower = t };
            const double camXMax = 2000;
            //                         name        mech height fraction (visible part), base row (ndc), height above water, wanted tower offset
            plan.Hero = OverWater(g, m, t, "hero", 0.47, -0.45, 12, -9, camXMax, plan.Warnings);
            plan.Closer = OverWater(g, m, t, "closer", 0.54, -0.52, 8, -7, camXMax, plan.Warnings);
            plan.Wider = OverWater(g, m, t, "wider", 0.26, -0.30, 22, -6, camXMax, plan.Warnings);
            plan.Waterfront = Waterfront(g, m, t, plan.Warnings);
            PlaceProps(g, plan);
            if (plan.Hero.MechFractionFull80 < 0.40 || plan.Hero.MechFractionFull80 > 0.60) plan.Warnings.Add("hero mech height fraction (80 m) " + plan.Hero.MechFractionFull80.ToString("F3", Inv) + " is outside 0.40-0.60");
            if (!plan.Hero.TowerInFrame) plan.Warnings.Add("hero: the tallest tower is not inside the frame");
            LastPlan = plan;
            return plan;
        }

        // ------------------------------------------------------------------ json
        static string N(double v) => v.ToString("0.####", Inv);
        static string Q(string s) => StyleFrameScene.Str(s);
        static string V3(double x, double y, double z) => "[" + N(x) + ", " + N(y) + ", " + N(z) + "]";

        sealed class Px { public double W, H, Max; public bool InFrame, Visible; }

        static Px Footprint(ShotDef shot, PropDef p, int iw, int ih)
        {
            var cam = shot.Model; var px = new Px { Visible = true };
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            double yw = p.YawDeg * Math.PI / 180.0, c = Math.Cos(yw), s = Math.Sin(yw);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = 0; sy <= 1; sy++)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        double lx = sx * p.Size.x * 0.5, lz = sz * p.Size.z * 0.5;
                        double wx = p.X + lx * c + lz * s, wz = p.Z - lx * s + lz * c, wy = p.Y + sy * p.Size.y;
                        if (!cam.Project(wx, wy, wz, out double nx, out double ny, out _)) { px.Visible = false; continue; }
                        minX = Math.Min(minX, nx); maxX = Math.Max(maxX, nx); minY = Math.Min(minY, ny); maxY = Math.Max(maxY, ny);
                    }
            if (!px.Visible) return px;
            px.W = (maxX - minX) * 0.5 * iw; px.H = (maxY - minY) * 0.5 * ih; px.Max = Math.Max(px.W, px.H);
            px.InFrame = (minX + maxX) * 0.5 >= -1 && (minX + maxX) * 0.5 <= 1 && (minY + maxY) * 0.5 >= -1 && (minY + maxY) * 0.5 <= 1;
            return px;
        }

        static string PxJson(Px p) => p.Visible
            ? "{\"width_px\": " + N(p.W) + ", \"height_px\": " + N(p.H) + ", \"max_dim_px\": " + N(p.Max) + ", \"in_frame\": " + (p.InFrame ? "true" : "false") + ", \"in_1_to_3_px\": " + (p.Max >= 1.0 && p.Max <= 3.0 ? "true" : "false") + "}"
            : "{\"visible\": false}";

        public static void WriteProps(ShotPlan plan, double originX, double originZ, List<string> warnings)
        {
            var j = new StringBuilder();
            j.Append("{\n \"schema\": \"style_frame_props@v1\",\n");
            j.Append(" \"note\": \"Placeholders for real assets: plain box meshes (scale 1, size carried by the mesh, pivot at the centre of the base), unlit flat value, no texture, no detail. Positions are the centre of the base on the ground or water surface (BayTM x,z; y NAVD88 m).\",\n");
            j.Append(" \"internal_grids\": {\"1080p\": [480, 270, 4], \"1440p\": [512, 288, 5]},\n");
            j.Append(" \"pixel_method\": \"analytic pinhole projection of the 8 box corners (60 deg horizontal, 16:9) from the planned pose, before the rig's sub-texel snap; max_dim_px = larger of projected width and height; 'figure' height 1.8 m is the 1-3 px criterion, its 0.5 m width is below 1 px by geometry\",\n");
            j.Append(" \"hero_camera_baytm\": ").Append(V3(plan.Hero.X + originX, plan.Hero.Y, plan.Hero.Z + originZ)).Append(",\n \"props\": [");
            bool first = true;
            foreach (var p in plan.Props)
            {
                j.Append(first ? "\n" : ",\n"); first = false;
                j.Append("  {\"name\": ").Append(Q(p.Name)).Append(", \"kind\": ").Append(Q(p.Kind)).Append(", \"primitive\": \"box mesh (plain box), scale 1\"");
                j.Append(", \"size_m\": [").Append(N(p.Size.x)).Append(", ").Append(N(p.Size.y)).Append(", ").Append(N(p.Size.z)).Append("], \"size_axes\": \"x width, y height, z length/depth\"");
                j.Append(", \"material\": {\"shader\": \"Game/StyleTest/Surface\", \"value\": ").Append(N(p.Value)).Append(", \"unlit\": true}, \"surface\": ").Append(Q(p.Surface)).Append(", \"placed\": ").Append(p.Placed ? "true" : "false");
                if (!p.Placed) { j.Append(", \"note\": \"no position found\"}"); continue; }
                j.Append(", \"position_baytm\": ").Append(V3(p.X + originX, p.Y, p.Z + originZ)).Append(", \"position_scene\": ").Append(V3(p.X, p.Y, p.Z)).Append(", \"yaw_deg\": ").Append(N(p.YawDeg));
                double dx = p.X - plan.Hero.X, dz = p.Z - plan.Hero.Z;
                j.Append(", \"distance_from_hero_camera_m\": ").Append(N(Math.Sqrt(dx * dx + dz * dz))).Append(", \"bearing_from_hero_view_axis_deg\": ").Append(N(Wrap(Bearing(plan.Hero.X, plan.Hero.Z, p.X, p.Z) - plan.Hero.YawDeg)));
                var hero1080 = Footprint(plan.Hero, p, Internal[0][0], Internal[0][1]); var hero1440 = Footprint(plan.Hero, p, Internal[1][0], Internal[1][1]);
                j.Append(", \"expected_internal_px\": {\"hero_1080p\": ").Append(PxJson(hero1080)).Append(", \"hero_1440p\": ").Append(PxJson(hero1440)).Append("}");
                j.Append(", \"expected_internal_px_other_framings_1080p\": {");
                for (int k = 0; k < 3; k++) j.Append(k > 0 ? ", " : "").Append(Q(plan.Alternatives[k].Name)).Append(": ").Append(PxJson(Footprint(plan.Alternatives[k], p, Internal[0][0], Internal[0][1])));
                j.Append("}");
                if (!hero1080.Visible || !hero1080.InFrame || hero1080.Max < 1.0 || hero1080.Max > 3.0 || !hero1440.Visible || hero1440.Max > 3.0)
                { string w = p.Name + ": hero footprint outside 1-3 internal px or outside the frame"; warnings.Add(w); j.Append(", \"warning\": ").Append(Q(w)); }
                if (p.Note != null) j.Append(", \"note\": ").Append(Q(p.Note));
                j.Append("}");
            }
            j.Append("\n ],\n \"warnings\": [").Append(string.Join(", ", plan.Warnings.ConvertAll(Q))).Append("]\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(PropsPath));
            File.WriteAllText(PropsPath, j.ToString());
        }

        public static void WriteShots(ShotPlan plan, double originX, double originZ, string profile, string paletteSha, string configuration, List<string> flagsOn, string tile)
        {
            var m = plan.Mech; var t = plan.Tower;
            var j = new StringBuilder();
            j.Append("{\n \"schema\": \"style_frame_shots@v1\",\n \"commit\": \"placeholder\",\n");
            j.Append(" \"commit_note\": \"the git commit is recorded in every capture sidecar (git rev-parse HEAD at capture time); this file stays free of it so it is identical across runs\",\n");
            j.Append(" \"tile\": ").Append(Q(tile)).Append(", \"floating_origin_baytm\": [").Append(N(originX)).Append(", ").Append(N(originZ)).Append("],\n");
            j.Append(" \"profile\": ").Append(Q(profile)).Append(", \"palette_sha256\": ").Append(Q(paletteSha)).Append(", \"configuration\": ").Append(Q(configuration)).Append(",\n");
            j.Append(" \"flags_on\": [").Append(string.Join(", ", flagsOn.ConvertAll(Q))).Append("],\n");
            j.Append(" \"camera_rig\": \"ThirdPersonRig on PixelWorldCamera, scripted pose (SetScriptedPose) so the rig snaps rotation and position to the texel grid; pose = position (BayTM x,z; y NAVD88 m), yaw deg (0 = +z/north, 90 = +x/east), pitch deg (up positive)\",\n");
            j.Append(" \"field_of_view\": {\"horizontal_deg\": ").Append(N(HorizontalFovDeg)).Append(", \"vertical_deg\": ").Append(N(CamModel.VerticalFovDeg)).Append(", \"aspect\": \"16:9\"},\n");
            j.Append(" \"internal_grids\": {\"1080p\": {\"internal\": [480, 270], \"scale\": 4}, \"1440p\": {\"internal\": [512, 288], \"scale\": 5}},\n");
            j.Append(" \"light\": {\"kind\": \"one fixed directional sun, no shadows\", \"sun_from_compass_azimuth_deg\": ").Append(N(SunFromAzimuthDeg)).Append(", \"sun_elevation_deg\": ").Append(N(SunElevationDeg)).Append(", \"light_euler_deg\": [").Append(N(SunElevationDeg)).Append(", ").Append(N(SunTravelYawDeg)).Append(", 0]},\n");
            j.Append(" \"mech\": {\"position_baytm\": ").Append(V3(m.ScenePosition.x + originX, m.ScenePosition.y, m.ScenePosition.z + originZ)).Append(", \"bounds_centre_baytm_xz\": [").Append(N(m.X + originX)).Append(", ").Append(N(m.Z + originZ)).Append("], \"yaw_deg\": ").Append(N(m.YawDeg));
            j.Append(", \"height_m\": ").Append(N(m.HeightM)).Append(", \"water_level_navd88_m\": ").Append(N(m.YWater)).Append(", \"top_navd88_m\": ").Append(N(m.YTop)).Append(", \"sole_navd88_m\": ").Append(N(m.YSole)).Append(", \"water_depth_at_mech_m\": ").Append(N(m.WaterDepthM)).Append("},\n");
            j.Append(" \"tallest_tower\": {\"bid\": ").Append(Q(t.Bid)).Append(", \"anchor_baytm\": [").Append(N(t.X + originX)).Append(", ").Append(N(t.Z + originZ)).Append("], \"top_navd88_m\": ").Append(N(t.Top)).Append("},\n");
            j.Append(" \"framings\": [");
            for (int i = 0; i < 4; i++)
            {
                var s = plan.All[i];
                j.Append(i > 0 ? ",\n" : "\n").Append("  {\"name\": ").Append(Q(s.Name));
                j.Append(", \"camera_baytm\": ").Append(V3(s.X + originX, s.Y, s.Z + originZ)).Append(", \"camera_scene\": ").Append(V3(s.X, s.Y, s.Z));
                j.Append(", \"yaw_deg\": ").Append(N(s.YawDeg)).Append(", \"pitch_deg\": ").Append(N(s.PitchDeg)).Append(", \"height_above_surface_m\": ").Append(N(s.HeightAboveSurface));
                j.Append(", \"reference_depth_m\": ").Append(N(s.ReferenceDepth)).Append(", \"distance_to_mech_m\": ").Append(N(s.DistanceToMech)).Append(", \"compass_azimuth_of_camera_from_mech_deg\": ").Append(N(s.CompassAzimuthFromMechDeg));
                j.Append(", \"mech_height_fraction_of_frame\": {\"visible_above_waterline\": ").Append(N(s.MechFractionVisible)).Append(", \"full_80m_incl_submerged\": ").Append(N(s.MechFractionFull80)).Append(", \"method\": \"analytic projection of waterline and mech top; the C1 measure is on the rendered mask\"}");
                j.Append(", \"rows_from_top_fraction\": {\"horizon\": ").Append(N(s.HorizonRowFromTop)).Append(", \"mech_base_waterline\": ").Append(N(s.MechBaseRowFromTop)).Append(", \"mech_top\": ").Append(N(s.MechTopRowFromTop)).Append("}");
                j.Append(", \"horizon_row_1080p_internal\": ").Append(N(s.HorizonRowFromTop * 270)).Append(", \"horizon_row_1440p_internal\": ").Append(N(s.HorizonRowFromTop * 288));
                j.Append(", \"tallest_tower\": {\"bearing_offset_from_view_axis_deg\": ").Append(N(s.TowerOffsetFromMechDeg)).Append(", \"top_col_from_left\": ").Append(N(s.TowerTopColFromLeft)).Append(", \"top_row_from_top\": ").Append(N(s.TowerTopRowFromTop)).Append(", \"in_frame\": ").Append(s.TowerInFrame ? "true" : "false").Append("}}");
            }
            j.Append("\n ],\n \"warnings\": [").Append(string.Join(", ", plan.Warnings.ConvertAll(Q))).Append("]\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ShotsPath));
            File.WriteAllText(ShotsPath, j.ToString());
        }
    }
}
