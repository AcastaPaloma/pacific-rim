using System;

namespace Game.Physics.Body
{
    /// <summary>R-005 section 3.2: k = I w_n^2, d = 2 zeta I w_n. Gains follow mass and inertia; nothing is hand-set per mech.</summary>
    public static class GainRules
    {
        public static double Stiffness(double inertia, double hz) { double w = 2 * Math.PI * hz; return inertia * w * w; }
        public static double Damping(double inertia, double hz, double zeta) => 2 * zeta * inertia * 2 * Math.PI * hz;
    }

    /// <summary>R-001 section 5 contact definition: begins at F_n >= F_on, ends at F_n &lt; F_off.</summary>
    public struct ContactHysteresis
    {
        public bool InContact;
        /// <summary>Returns +1 on contact begin, -1 on contact end, 0 otherwise.</summary>
        public int Update(double fn, double fOn, double fOff)
        {
            if (!InContact && fn >= fOn && fn > 0) { InContact = true; return 1; }
            if (InContact && fn < fOff) { InContact = false; return -1; }
            return 0;
        }
    }
}
