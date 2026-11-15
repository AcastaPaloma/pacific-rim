// Engine-free spawn planner of the M1 slice (T-039c, DEF-M1slice-f1). The first player build put the mech at the SoMa tile centre and the third-person
// rig 248 m behind an 80 m body, which is inside a block of 100-190 m towers: every frame showed one wall. The planner picks the mech spot and the
// orbit yaw so that the camera sits in clear air, the line from camera to mech is not blocked by a tall building, and as much city as possible
// stands behind the mech inside the view cone.
using System;
using System.Collections.Generic;

namespace Game.Slice
{
    public readonly struct SliceFootprint
    {
        public readonly double X, Z, Radius, Height;
        public SliceFootprint(double x, double z, double radius, double height) { X = x; Z = z; Radius = radius; Height = height; }
    }

    public sealed class SliceSpawnPlan
    {
        public double MechX, MechZ, YawDeg, GroundM;
        public int Blockers, VisibleBuildings;
        public double Score;
    }

    public static class SliceSpawnPlanner
    {
        public const double ClearMarginM = 12.0;          // a building closer than radius + this to the camera, the mech or the sight line blocks
        public const double MinBlockerHeightM = 15.0;     // lower buildings do not block a camera 27 m up
        public const double MinGroundM = 1.0;             // the mech stands on land, not in the bay
        public const double ViewHalfAngleDeg = 30.0;      // horizontal half field of view counted for "city behind the mech"
        public const double ViewFarM = 900.0;
        public const double WalkM = 72.0;                 // the autopilot walks the mech straight ahead; the sight line must stay clear all the way (checked every 24 m)

        /// <param name="groundAt">terrain height (m) at a point, NaN when unknown</param>
        /// <param name="camDistM">orbit distance of the rig (target height x 3.1)</param>
        /// <param name="camAboveGroundM">camera height above the mech's ground (the rig looks up a little, so it sits below the 0.55 H focus)</param>
        public static bool TryPlan(IReadOnlyList<SliceFootprint> buildings, Func<double, double, double> groundAt, double centreX, double centreZ, double searchHalfM, double stepM,
            double camDistM, double camAboveGroundM, int yawSteps, out SliceSpawnPlan plan)
        {
            plan = null;
            if (buildings == null || groundAt == null || stepM <= 0 || yawSteps < 1) return false;
            const double Cell = 128.0;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < buildings.Count; i++)
            {
                long k = Key((int)Math.Floor(buildings[i].X / Cell), (int)Math.Floor(buildings[i].Z / Cell));
                if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                l.Add(i);
            }
            SliceSpawnPlan best = null;
            for (double x = centreX - searchHalfM; x <= centreX + searchHalfM; x += stepM)
                for (double z = centreZ - searchHalfM; z <= centreZ + searchHalfM; z += stepM)
                {
                    double g0 = groundAt(x, z);
                    if (double.IsNaN(g0) || g0 < MinGroundM) continue;
                    if (Inside(buildings, grid, Cell, x, z, ClearMarginM, 0.0)) continue;   // the mech itself must stand in the open
                    for (int yi = 0; yi < yawSteps; yi++)
                    {
                        double yaw = 360.0 * yi / yawSteps;
                        var p = Evaluate(buildings, grid, Cell, groundAt, x, z, g0, yaw, camDistM, camAboveGroundM);
                        if (best == null || p.Score > best.Score) best = p;
                    }
                }
            plan = best;
            return best != null;
        }

        static long Key(int cx, int cz) { return ((long)cx << 32) ^ (uint)cz; }

        static bool Inside(IReadOnlyList<SliceFootprint> b, Dictionary<long, List<int>> grid, double cell, double x, double z, double margin, double minHeight)
        {
            int cx = (int)Math.Floor(x / cell), cz = (int)Math.Floor(z / cell);
            for (int i = cx - 2; i <= cx + 2; i++)
                for (int j = cz - 2; j <= cz + 2; j++)
                    if (grid.TryGetValue(Key(i, j), out var l))
                        foreach (int bi in l)
                        {
                            var f = b[bi]; if (f.Height < minHeight) continue;
                            double dx = f.X - x, dz = f.Z - z;
                            if (dx * dx + dz * dz < (f.Radius + margin) * (f.Radius + margin)) return true;
                        }
            return false;
        }

        static SliceSpawnPlan Evaluate(IReadOnlyList<SliceFootprint> b, Dictionary<long, List<int>> grid, double cell, Func<double, double, double> groundAt,
            double mx, double mz, double g0, double yawDeg, double camDist, double camAbove)
        {
            double yaw = yawDeg * Math.PI / 180.0, fx = Math.Sin(yaw), fz = Math.Cos(yaw);   // forward on the ground plane (camera yaw 0 looks +z)
            double cx = mx - fx * camDist, cz = mz - fz * camDist;
            int blockers = 0;
            // sight line camera -> mech, sampled every 12 m, at the start and after the mech has walked 24, 48 and 72 m ahead (camera following);
            // a tall building near a sample blocks it (the camera sample itself catches "camera inside a building")
            int samples = Math.Max(2, (int)(camDist / 12.0));
            var counted = new HashSet<int>();
            for (double walk = 0.0; walk <= WalkM + 1e-9; walk += 24.0)
            {
                double ox = fx * walk, oz = fz * walk;
                for (int s = 0; s <= samples; s++)
                {
                    double t = (double)s / samples, sx = cx + ox + (mx - cx) * t, sz = cz + oz + (mz - cz) * t, alt = camAbove + (0.55 * 80.0 - camAbove) * t;
                    int gx = (int)Math.Floor(sx / cell), gz = (int)Math.Floor(sz / cell);
                    for (int i = gx - 1; i <= gx + 1; i++)
                        for (int j = gz - 1; j <= gz + 1; j++)
                            if (grid.TryGetValue(Key(i, j), out var l))
                                foreach (int bi in l)
                                {
                                    var f = b[bi]; if (f.Height < MinBlockerHeightM) continue;
                                    double dx = f.X - sx, dz = f.Z - sz;
                                    if (dx * dx + dz * dz >= (f.Radius + ClearMarginM) * (f.Radius + ClearMarginM)) continue;
                                    double gb = groundAt(f.X, f.Z); if (double.IsNaN(gb)) gb = 0.0;
                                    if (gb + f.Height > g0 + alt && counted.Add(bi)) blockers++;
                                }
                }
            }
            // city behind the mech: buildings inside the view cone beyond the mech
            int visible = 0; double cosHalf = Math.Cos(ViewHalfAngleDeg * Math.PI / 180.0);
            int rx0 = (int)Math.Floor((cx - ViewFarM) / cell), rx1 = (int)Math.Floor((cx + ViewFarM) / cell), rz0 = (int)Math.Floor((cz - ViewFarM) / cell), rz1 = (int)Math.Floor((cz + ViewFarM) / cell);
            for (int i = rx0; i <= rx1; i++)
                for (int j = rz0; j <= rz1; j++)
                    if (grid.TryGetValue(Key(i, j), out var l))
                        foreach (int bi in l)
                        {
                            var f = b[bi]; if (f.Height < 20.0) continue;
                            double dx = f.X - cx, dz = f.Z - cz, d = Math.Sqrt(dx * dx + dz * dz);
                            if (d < camDist || d > ViewFarM) continue;
                            if ((dx * fx + dz * fz) / d >= cosHalf) visible++;
                        }
            return new SliceSpawnPlan { MechX = mx, MechZ = mz, YawDeg = yawDeg, GroundM = g0, Blockers = blockers, VisibleBuildings = visible, Score = Math.Min(visible, 150) - 1000.0 * blockers };
        }
    }
}
