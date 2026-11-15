using System;

namespace Game.Units.Mech
{
    /// <summary>The sagittal posture of a supporting leg for one hip position: foot pitch about the heel or toe line, knee, shank and thigh angles, ankle angle.</summary>
    public struct StanceSolution
    {
        public double FootPitch;      // rad, positive = heel up about the toe line (toes down), negative = toes up about the heel line
        public double Knee;           // rad, flexion
        public double Thigh, Shank;   // rad from the downward vertical, positive = distal end ahead of the proximal end
        public double AnklePlantar;   // rad, anatomical plantarflexion (= Shank + FootPitch)
        public double LegLength;      // hip to ankle, m
        public double AnkleX, AnkleY; // ankle joint relative to the flat-ankle ground anchor (forward, up), m
        public FootSupport Support;   // Heel, Flat or Toe
        public bool Reachable;        // false when no pitch inside the allowed range lets the leg reach
    }

    /// <summary>
    /// R-008 section 1, the corrected whole-stance reference in state-indexed form (engine-free). A supporting foot does not slide: it pivots about its heel line (heel strike, toes up),
    /// lies flat, or pivots about its toe line (heel rise, push-off). For a hip at horizontal distance <c>x</c> ahead of the foot's flat-ankle anchor and at a given height, the posture
    /// is the smallest foot pitch for which (a) the leg reaches the ankle with at least the minimum knee flexion and (b) the ankle stays inside its range less a margin; the knee, shank
    /// and ankle angles then follow from two-link geometry. The same function serves the time-indexed <see cref="ReferenceGait"/> and the controller's stance-leg impedance targets, so
    /// the joint targets are consistent with where the body really is. It moves nothing by itself.
    /// </summary>
    public static class StancePosture
    {
        /// <summary>Ankle joint relative to the flat-ankle ground anchor for a foot pitch (the heel line or the toe line stays where it was on the ground).</summary>
        public static void AnklePosition(GaitGeometry g, double pitch, out double x, out double y)
        {
            double c = Math.Cos(pitch), s = Math.Sin(pitch), pivot = pitch > 0 ? g.ToeZ : pitch < 0 ? g.HeelZ : 0;
            // the ankle sits at (-pivot, ankle height) from the pivot line when flat; pitching rotates that lever about the line
            x = pivot - pivot * c + g.AnkleHeight * s; y = pivot * s + g.AnkleHeight * c;
        }

        public static double LegLength(GaitGeometry g, double knee) => Math.Sqrt(g.Thigh * g.Thigh + g.Shank * g.Shank + 2 * g.Thigh * g.Shank * Math.Cos(knee));

        /// <summary>d(leg length)/d(knee flexion), m/rad (negative: flexing shortens the leg; zero for a straight leg).</summary>
        public static double LegLengthRate(GaitGeometry g, double knee) { double len = LegLength(g, knee); return len > 0 ? -g.Thigh * g.Shank * Math.Sin(knee) / len : 0; }

        /// <summary>Posture for a given foot pitch, whatever the ranges say (the leg is clamped to its full length if the ankle is out of reach).</summary>
        public static StanceSolution Evaluate(GaitGeometry g, double x, double hipHeight, double pitch)
        {
            var s = new StanceSolution { FootPitch = pitch, Support = pitch > 0 ? FootSupport.Toe : pitch < 0 ? FootSupport.Heel : FootSupport.Flat };
            AnklePosition(g, pitch, out s.AnkleX, out s.AnkleY);
            double dx = s.AnkleX - x, dy = hipHeight - s.AnkleY, d = Math.Sqrt(dx * dx + dy * dy), a = g.Thigh, c = g.Shank;
            s.LegLength = d;
            double len = Math.Max(Math.Abs(a - c) + 1e-9, Math.Min(a + c, d));
            double interior = Math.Acos(Math.Max(-1, Math.Min(1, (a * a + c * c - len * len) / (2 * a * c))));
            double alpha = Math.Acos(Math.Max(-1, Math.Min(1, (a * a + len * len - c * c) / (2 * a * len))));
            s.Knee = Math.PI - interior;
            s.Thigh = Math.Atan2(dx, dy) + alpha;   // knee forward of the hip-ankle line
            s.Shank = s.Thigh - s.Knee;
            s.AnklePlantar = s.Shank + pitch;
            s.Reachable = d <= a + c;
            return s;
        }

        /// <param name="x">hip ahead of the foot's flat-ankle anchor, m (negative: the foot is ahead of the hip)</param>
        /// <param name="hipHeight">hip joint above the ground the foot stands on, m</param>
        /// <param name="kneeMin">smallest knee flexion a supporting leg may have, rad</param>
        /// <param name="ankleLo">lowest ankle angle allowed (dorsiflexion limit plus margin), rad</param>
        /// <param name="ankleHi">highest ankle angle allowed (plantarflexion limit less margin), rad</param>
        /// <param name="heelStrikeMax">largest toes-up pitch about the heel line, rad (positive number)</param>
        /// <param name="toeOffMax">largest heel-up pitch about the toe line, rad</param>
        /// <param name="pitchFloor">heel-up pitch asked for whatever the geometry needs (pre-swing), rad; 0 for none</param>
        public static StanceSolution Solve(GaitGeometry g, double x, double hipHeight, double kneeMin, double ankleLo, double ankleHi, double heelStrikeMax, double toeOffMax, double pitchFloor = 0)
        {
            double reach = LegLength(g, kneeMin);
            // how far a pitch is from being admissible: positive = not yet (leg too short for the distance, or ankle beyond its margin), in metres (1 rad of ankle counts as one leg length)
            Func<double, double> need = f =>
            {
                var e = Evaluate(g, x, hipHeight, f);
                double ankle = f >= 0 ? ankleLo - e.AnklePlantar : e.AnklePlantar - ankleHi;
                return Math.Max(e.LegLength - reach, ankle * (g.Thigh + g.Shank));
            };
            double floor = Math.Max(0, Math.Min(toeOffMax, pitchFloor));
            if (need(floor) <= 0) { var flat = Evaluate(g, x, hipHeight, floor); flat.Reachable = true; return flat; }
            // the foot rolls onto the edge that is nearer the hip: the toe line when the hip is ahead of the ankle, the heel line when it is behind
            bool toe = x >= 0 || floor > 0;
            double lo = toe ? floor : 0, hi = toe ? toeOffMax : -heelStrikeMax;
            if (need(hi) > 0)
            {
                // even the largest pitch does not let the leg reach: take the pitch that comes closest (the leg will hang at full length until the body arrives)
                double best = hi, bestNeed = need(hi);
                for (int i = 0; i <= 16; i++) { double f = lo + (hi - lo) * i / 16.0, n = need(f); if (n < bestNeed) { bestNeed = n; best = f; } }
                var far = Evaluate(g, x, hipHeight, best); far.Reachable = false; return far;
            }
            for (int i = 0; i < 40; i++) { double mid = 0.5 * (lo + hi); if (need(mid) > 0) lo = mid; else hi = mid; }
            var s = Evaluate(g, x, hipHeight, hi); s.Reachable = true; return s;
        }
    }
}
