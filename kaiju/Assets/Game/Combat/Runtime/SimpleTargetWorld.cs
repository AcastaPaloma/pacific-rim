// Engine-free target volumes for tests and the headless harness: axis-aligned boxes (buildings) and vertical capsules (kaiju, mechs).
// Swept-sphere queries are exact for faces and cylinder sides; box edges and corners are treated as sharp (conservative, a hit
// slightly earlier than a rounded edge). Deterministic: targets are tested in insertion order, ties keep the first.
using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Combat
{
    public sealed class SimpleTargetWorld : ICombatTargetQuery
    {
        private struct Box { public CVec3 Min, Max; public BuildingId Id; public double MassKg; }
        private struct Capsule { public CVec3 Base; public double Radius, Height; public EntityId Id; public TargetKind Kind; public double MassKg; public CVec3 Velocity; }

        private readonly List<Box> _boxes = new List<Box>();
        private readonly List<Capsule> _caps = new List<Capsule>();

        public void AddBuilding(BuildingId id, CVec3 min, CVec3 max, double massKg) { _boxes.Add(new Box { Id = id, Min = min, Max = max, MassKg = massKg }); }

        /// <summary>Vertical capsule: axis from <paramref name="baseCentre"/> up by <paramref name="height"/>, end spheres included inside the height.</summary>
        public void AddCapsule(EntityId id, TargetKind kind, CVec3 baseCentre, double radius, double height, double massKg, CVec3 velocity = default)
        {
            _caps.Add(new Capsule { Id = id, Kind = kind, Base = baseCentre, Radius = radius, Height = height, MassKg = massKg, Velocity = velocity });
        }

        public bool SweepSphere(in CVec3 from, in CVec3 to, double radius, EntityId ignore, out SweepHit hit)
        {
            hit = default;
            for (int i = 0; i < _boxes.Count; i++)
            {
                SweepHit h;
                if (SweepBox(_boxes[i], from, to, radius, out h) && (!hit.Hit || h.T < hit.T)) hit = h;
            }
            for (int i = 0; i < _caps.Count; i++)
            {
                if (_caps[i].Id == ignore) continue;
                SweepHit h;
                if (SweepCapsule(_caps[i], from, to, radius, out h) && (!hit.Hit || h.T < hit.T)) hit = h;
            }
            return hit.Hit;
        }

        private static bool SweepBox(Box b, CVec3 a, CVec3 c, double r, out SweepHit hit)
        {
            hit = default;
            double[] lo = { b.Min.X - r, b.Min.Y - r, b.Min.Z - r }, hi = { b.Max.X + r, b.Max.Y + r, b.Max.Z + r };
            double[] o = { a.X, a.Y, a.Z }, d = { c.X - a.X, c.Y - a.Y, c.Z - a.Z };
            double t0 = 0, t1 = 1; int axis = -1; double sign = 0;
            bool inside = true;
            for (int k = 0; k < 3; k++)
            {
                if (d[k] == 0)
                {
                    if (o[k] < lo[k] || o[k] > hi[k]) return false;
                    continue;
                }
                double ta = (lo[k] - o[k]) / d[k], tb = (hi[k] - o[k]) / d[k];
                double near = Math.Min(ta, tb), far = Math.Max(ta, tb);
                if (near > t0) { t0 = near; axis = k; sign = d[k] > 0 ? -1 : 1; }
                if (far < t1) t1 = far;
                if (t0 > t1) return false;
            }
            for (int k = 0; k < 3; k++) if (o[k] <= lo[k] || o[k] >= hi[k]) inside = false;
            CVec3 n;
            if (inside || axis < 0)
            {
                // Start inside the inflated box: push out along the axis of least penetration.
                double best = double.MaxValue; int bk = 0; double bs = 1;
                for (int k = 0; k < 3; k++)
                {
                    double p0 = o[k] - lo[k], p1 = hi[k] - o[k];
                    if (p0 < best) { best = p0; bk = k; bs = -1; }
                    if (p1 < best) { best = p1; bk = k; bs = 1; }
                }
                n = Axis(bk, bs); t0 = 0;
            }
            else n = Axis(axis, sign);
            CVec3 at = a + (c - a) * t0;
            hit = new SweepHit
            {
                Hit = true, T = t0, Normal = n, Kind = TargetKind.Building, Building = b.Id, TargetMassKg = b.MassKg,
                Point = new CVec3(Clamp(at.X, b.Min.X, b.Max.X), Clamp(at.Y, b.Min.Y, b.Max.Y), Clamp(at.Z, b.Min.Z, b.Max.Z)),
            };
            return true;
        }

        private static CVec3 Axis(int k, double s) => k == 0 ? new CVec3(s, 0, 0) : k == 1 ? new CVec3(0, s, 0) : new CVec3(0, 0, s);
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        private static bool SweepCapsule(Capsule cp, CVec3 a, CVec3 c, double r, out SweepHit hit)
        {
            hit = default;
            double R = cp.Radius + r;
            double y0 = cp.Base.Y + cp.Radius, y1 = cp.Base.Y + cp.Height - cp.Radius; // axis segment
            if (y1 < y0) { y0 = y1 = cp.Base.Y + 0.5 * cp.Height; }
            CVec3 d = c - a;
            double best = double.MaxValue;
            // Side: infinite cylinder in x,z restricted to the axis segment.
            double ox = a.X - cp.Base.X, oz = a.Z - cp.Base.Z;
            double A = d.X * d.X + d.Z * d.Z, B = 2 * (ox * d.X + oz * d.Z), C = ox * ox + oz * oz - R * R;
            if (C <= 0 && a.Y >= y0 && a.Y <= y1) best = 0;
            else if (A > 0)
            {
                double disc = B * B - 4 * A * C;
                if (disc >= 0)
                {
                    double t = (-B - Math.Sqrt(disc)) / (2 * A);
                    if (t >= 0 && t <= 1) { double y = a.Y + d.Y * t; if (y >= y0 && y <= y1) best = t; }
                }
            }
            for (int s = 0; s < 2; s++)
            {
                double cy = s == 0 ? y0 : y1;
                double t;
                if (SphereT(a, d, new CVec3(cp.Base.X, cy, cp.Base.Z), R, out t) && t < best) best = t;
            }
            if (best == double.MaxValue) return false;
            CVec3 at = a + d * best;
            double ay = at.Y < y0 ? y0 : at.Y > y1 ? y1 : at.Y;
            CVec3 axisPt = new CVec3(cp.Base.X, ay, cp.Base.Z);
            CVec3 n = (at - axisPt).Normalized(CVec3.Up);
            hit = new SweepHit { Hit = true, T = best, Normal = n, Point = axisPt + n * cp.Radius, Kind = cp.Kind, Entity = cp.Id, TargetMassKg = cp.MassKg, TargetVelocity = cp.Velocity };
            return true;
        }

        private static bool SphereT(CVec3 o, CVec3 d, CVec3 ctr, double R, out double t)
        {
            t = 0;
            CVec3 m = o - ctr;
            double A = CVec3.Dot(d, d), B = 2 * CVec3.Dot(m, d), C = CVec3.Dot(m, m) - R * R;
            if (C <= 0) { t = 0; return true; }
            if (!(A > 0)) return false;
            double disc = B * B - 4 * A * C;
            if (disc < 0) return false;
            t = (-B - Math.Sqrt(disc)) / (2 * A);
            return t >= 0 && t <= 1;
        }
    }
}
