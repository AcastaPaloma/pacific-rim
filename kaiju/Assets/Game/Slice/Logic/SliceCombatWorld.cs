// T-189: target volumes for the slice's weapons (TEMPORARY, engine-free, deterministic). Buildings are vertical cylinders from the
// footprint index (no per-building colliders in the slice, cost), the kaiju is one vertical cylinder, the ground comes from a terrain sampler.
// Implements the Combat query contract so the real Combat module (T-040) sweeps against it; replace with the physics adapter when T-005 lands.
using System;
using System.Collections.Generic;
using Game.Combat;
using Game.Core;

namespace Game.Slice
{
    public sealed class SliceCombatWorld : ICombatTargetQuery
    {
        /// <summary>ASSUMED: hollow-concrete bulk density used for the building mass the contact law sees (kg per m^3 of footprint x height).</summary>
        public const double BuildingDensityKgM3 = 250.0;
        public const double MinBuildingMassKg = 2e5, MaxBuildingMassKg = 5e8;
        const double Cell = 64.0, MaxCylinderRadius = 120.0;
        const int MaxCellSpan = 48;

        struct Cyl { public BuildingId Id; public double X, Z, R, Height; }
        readonly Dictionary<long, List<Cyl>> _cells = new Dictionary<long, List<Cyl>>();
        readonly Func<double, double, double> _ground;
        readonly List<Cyl> _scratch = new List<Cyl>();
        readonly HashSet<BuildingId> _removed = new HashSet<BuildingId>();
        public int BuildingCount { get; private set; }

        // the kaiju stand-in: one cylinder whose base follows the terrain
        bool _kaijuOn; EntityId _kaijuId; double _kx, _kz, _kBase, _kR, _kH, _kMass; CVec3 _kVel;

        public SliceCombatWorld(Func<double, double, double> groundAt) { _ground = groundAt ?? throw new ArgumentNullException(nameof(groundAt)); }

        static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }
        static int C(double v) { return (int)Math.Floor(v / Cell); }

        public void AddBuilding(BuildingId id, double x, double z, double radiusM, double heightM)
        {
            long k = Key(C(x), C(z));
            if (!_cells.TryGetValue(k, out var l)) _cells[k] = l = new List<Cyl>();
            l.Add(new Cyl { Id = id, X = x, Z = z, R = Math.Min(Math.Max(radiusM, 3.0), MaxCylinderRadius), Height = Math.Max(heightM, 3.0) });
            BuildingCount++;
        }

        /// <summary>A building that was removed from the scene (Complete) stops being a target.</summary>
        public void RemoveBuilding(BuildingId id) { _removed.Add(id); }

        public static double BuildingMassKg(double radiusM, double heightM)
        {
            double m = BuildingDensityKgM3 * Math.PI * radiusM * radiusM * heightM;
            return Math.Min(MaxBuildingMassKg, Math.Max(MinBuildingMassKg, m));
        }

        public void SetKaiju(EntityId id, double x, double z, double baseY, double radiusM, double heightM, double massKg, CVec3 velocity)
        { _kaijuOn = true; _kaijuId = id; _kx = x; _kz = z; _kBase = baseY; _kR = radiusM; _kH = heightM; _kMass = massKg; _kVel = velocity; }
        public void ClearKaiju() { _kaijuOn = false; }

        public bool SweepSphere(in CVec3 from, in CVec3 to, double radius, EntityId ignore, out SweepHit hit)
        {
            hit = default;
            if (!from.IsFinite || !to.IsFinite) return false;
            double r = Math.Max(radius, 0.0);
            // candidates from the grid cells under the inflated segment box
            double minX = Math.Min(from.X, to.X) - r - MaxCylinderRadius, maxX = Math.Max(from.X, to.X) + r + MaxCylinderRadius;
            double minZ = Math.Min(from.Z, to.Z) - r - MaxCylinderRadius, maxZ = Math.Max(from.Z, to.Z) + r + MaxCylinderRadius;
            int c0 = C(minX), c1 = C(maxX), d0 = C(minZ), d1 = C(maxZ);
            if (c1 - c0 > MaxCellSpan) c1 = c0 + MaxCellSpan;
            if (d1 - d0 > MaxCellSpan) d1 = d0 + MaxCellSpan;
            for (int i = c0; i <= c1; i++)
                for (int j = d0; j <= d1; j++)
                    if (_cells.TryGetValue(Key(i, j), out var l))
                        for (int n = 0; n < l.Count; n++)
                        {
                            var b = l[n];
                            if (_removed.Contains(b.Id)) continue;
                            double gy = _ground(b.X, b.Z);
                            if (SweepCylinder(from, to, r, b.X, b.Z, b.R, gy - 2.0, gy + b.Height, out var h))
                            {
                                if (!hit.Hit || h.T < hit.T)
                                {
                                    h.Kind = TargetKind.Building; h.Building = b.Id; h.TargetMassKg = BuildingMassKg(b.R, b.Height); h.TargetVelocity = CVec3.Zero;
                                    hit = h;
                                }
                            }
                        }
            if (_kaijuOn && _kaijuId != ignore && SweepCylinder(from, to, r, _kx, _kz, _kR, _kBase, _kBase + _kH, out var kh) && (!hit.Hit || kh.T < hit.T))
            {
                kh.Kind = TargetKind.Kaiju; kh.Entity = _kaijuId; kh.TargetMassKg = _kMass; kh.TargetVelocity = _kVel; hit = kh;
            }
            // terrain: sample along the segment (4 samples is enough for the <= 25 m steps a shell or arm tip makes per fixed step)
            const int Samples = 4;
            double prevT = 0, prevD = from.Y - r - _ground(from.X, from.Z);
            if (prevD <= 0 && (!hit.Hit || 0.0 < hit.T)) { hit = GroundHit(from, to, 0.0); }
            else
                for (int s = 1; s <= Samples; s++)
                {
                    double t = s / (double)Samples;
                    double px = from.X + (to.X - from.X) * t, py = from.Y + (to.Y - from.Y) * t, pz = from.Z + (to.Z - from.Z) * t;
                    double d = py - r - _ground(px, pz);
                    if (d <= 0)
                    {
                        double tt = prevD > 0 ? prevT + (t - prevT) * (prevD / (prevD - d)) : t;
                        if (!hit.Hit || tt < hit.T) hit = GroundHit(from, to, tt);
                        break;
                    }
                    prevT = t; prevD = d;
                }
            return hit.Hit;
        }

        SweepHit GroundHit(in CVec3 a, in CVec3 b, double t)
        {
            var p = a + (b - a) * t;
            return new SweepHit { Hit = true, T = t, Point = new CVec3(p.X, _ground(p.X, p.Z), p.Z), Normal = CVec3.Up, Kind = TargetKind.Ground };
        }

        /// <summary>First contact of a sphere (radius r) moving a -> c with a vertical cylinder (axis x,z; y in [y0, y1]); the cylinder's end caps are treated as flat.</summary>
        static bool SweepCylinder(in CVec3 a, in CVec3 c, double r, double cx, double cz, double cr, double y0, double y1, out SweepHit hit)
        {
            hit = default;
            double dx = c.X - a.X, dy = c.Y - a.Y, dz = c.Z - a.Z;
            double ox = a.X - cx, oz = a.Z - cz, R = cr + r;
            // horizontal interval where |o + t d|_xz <= R
            double A = dx * dx + dz * dz, B = 2 * (ox * dx + oz * dz), Cc = ox * ox + oz * oz - R * R;
            double t0, t1;
            if (A < 1e-12) { if (Cc > 0) return false; t0 = 0; t1 = 1; }
            else
            {
                double disc = B * B - 4 * A * Cc;
                if (disc < 0) return false;
                double sq = Math.Sqrt(disc);
                t0 = (-B - sq) / (2 * A); t1 = (-B + sq) / (2 * A);
            }
            // vertical interval where y in [y0 - r, y1 + r]
            double lo = y0 - r, hi = y1 + r, v0, v1;
            if (Math.Abs(dy) < 1e-12) { if (a.Y < lo || a.Y > hi) return false; v0 = 0; v1 = 1; }
            else
            {
                double u = (lo - a.Y) / dy, w = (hi - a.Y) / dy;
                v0 = Math.Min(u, w); v1 = Math.Max(u, w);
            }
            double t = Math.Max(Math.Max(t0, v0), 0.0), tEnd = Math.Min(Math.Min(t1, v1), 1.0);
            if (t > tEnd) return false;
            double px = a.X + dx * t, py = a.Y + dy * t, pz = a.Z + dz * t;
            double nx = px - cx, nz = pz - cz, nl = Math.Sqrt(nx * nx + nz * nz);
            CVec3 n;
            if (dy < 0 && v0 > t0 && t >= v0 - 1e-12) n = CVec3.Up;      // entered through the top cap
            else n = nl > 1e-9 ? new CVec3(nx / nl, 0, nz / nl) : CVec3.Up;
            hit = new SweepHit { Hit = true, T = t, Point = new CVec3(px, py, pz), Normal = n };
            return true;
        }
    }
}
