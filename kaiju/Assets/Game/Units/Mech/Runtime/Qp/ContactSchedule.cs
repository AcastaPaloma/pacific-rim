using System;

namespace Game.Units.Mech.Qp
{
    /// <summary>Schedule and contact-logic options (defaults = the prototype's DEFAULT in Tools/GaitOpt/qp_proto/controller_fid.py; tuned in 9.5).</summary>
    public struct ContactScheduleOptions
    {
        public double OnBw, OffBw;          // contact on/off hysteresis as a fraction of body weight
        public double TOut, TIn, TBlend;    // fmax ramp-out before scheduled unload, fmax rise after first contact (1 body weight per TIn), swing-task blend after lift-off
        public double TdMinFrac;            // a landing earlier than this fraction of the step time is not a touchdown (no re-anchor)
        public double LateVz;               // m/s (reference units): swing target descends at this rate while touchdown is late
        public double FCap;                 // fmax cap in body weights
        public double ZHold;                // a scheduled-loaded point still below this height (m) stays 'in contact' through a force dropout
        public bool HoldSched;
        public double MinAirFrac, XoffMax;  // step 9.5k (qp_td_air_frac, qp_td_xoff_max): a touchdown needs the swing leg continuously unloaded for MinAirFrac x ts, and a re-anchor offset within XoffMax (m) of the current one (0 = off)
        public bool HeelOnly;               // step 9.5k (qp_td_heel): only a heel contact of the swing leg is a touchdown (a toe scuff is not: it stays in the contact set but does not swap the legs or re-anchor)
        public double MissedAfterS;         // late time after which the touchdown counts as missed
        public static ContactScheduleOptions Default => new ContactScheduleOptions
        {
            OnBw = 0.03, OffBw = 0.01, TOut = 0.3, TIn = 0.08, TBlend = 0.2, TdMinFrac = 0.6, LateVz = 2.0, FCap = 4.0, ZHold = 0.05, HoldSched = true, MissedAfterS = 0.5
        };
    }

    /// <summary>
    /// Contact schedule, measured contact set and touchdown handling of the sagittal whole-body QP (port of controller_fid.py lines 143-176, 205-234).
    /// Points are 2*leg + {0 heel, 1 toe}; legs alternate by <see cref="Parity"/> (the swing leg). Engine-free, no allocation after construction.
    /// </summary>
    public sealed class ContactSchedule
    {
        public const int PointCount = 4;
        readonly bool[] _flags;             // [node * 4 + point], scheduled loaded
        readonly int _n2; readonly double _h, _ts;
        public ContactScheduleOptions Opt;
        // state
        public int Parity;                  // leg that swings in the current step
        public double TAnchor, Xoff;        // time of the last touchdown (re-anchor), x offset of the reference to the measured stance foot
        public readonly bool[] Inc = new bool[PointCount];
        public readonly double[] TIn = new double[PointCount], FIn = new double[PointCount];   // time and force (N) at first contact
        public readonly double[] TRel = new double[2];                                        // time the leg last became fully unloaded
        public int TouchdownCount;
        public bool LastCallReanchored;

        /// <param name="flags">scheduled loaded flags, n2 nodes x 4 points, row-major (node-major).</param>
        public ContactSchedule(bool[] flags, int n2, double stepNodeSeconds, double stepSeconds, ContactScheduleOptions opt)
        { _flags = flags; _n2 = n2; _h = stepNodeSeconds; _ts = stepSeconds; Opt = opt; Reset(0); }

        public void Reset(int startParity)
        {
            Parity = startParity; TAnchor = 0; Xoff = 0; TouchdownCount = 0; LastCallReanchored = false;
            for (int i = 0; i < PointCount; i++) { Inc[i] = false; TIn[i] = 0; FIn[i] = 0; }
            TRel[0] = TRel[1] = -10.0;
        }

        /// <summary>Step-time guard: the reference never advances past ts - 0.3 h.</summary>
        double Clamp => _ts - 0.3 * _h;
        /// <summary>Reference time inside the two-step cycle for the clock <paramref name="t"/>.</summary>
        public double RefTime(double t) => Parity * _ts + Math.Min(t - TAnchor, Clamp);
        /// <summary>Seconds past the end of the step without a touchdown.</summary>
        public double Late(double t) => Math.Max(0.0, t - TAnchor - Clamp);
        public bool Missed(double t) => Late(t) > Opt.MissedAfterS;
        /// <summary>Descent (reference units) to subtract from the swing foot's target while the touchdown is late.</summary>
        public double LateDrop(double t) => Opt.LateVz * Late(t);

        /// <summary>Scheduled unload time (>= tref) of a point from the node flags; tref itself if unloaded now; tref + 1e9 if always loaded.</summary>
        public double TOff(int point, double tref)
        {
            int k0 = (int)Math.Ceiling(tref / _h - 1e-9);
            for (int k = k0; k < k0 + _n2; k++) if (!_flags[((k % _n2) + _n2) % _n2 * PointCount + point]) return k * _h;
            return tref + 1e9;
        }

        /// <summary>Scheduled loaded flag of a point at reference time t (node of the two-step cycle).</summary>
        public bool Scheduled(int point, double tref)
        {
            int k = (int)Math.Floor(tref / _h + 1e-9);
            return _flags[((k % _n2) + _n2) % _n2 * PointCount + point];
        }

        /// <summary>Measured contact with hysteresis. <paramref name="fnN"/> per point normal force in N, <paramref name="pointZ"/> per point height above ground (m), <paramref name="weightN"/> = m g.</summary>
        public void UpdateContact(double t, double weightN, double[] fnN, double[] pointZ)
        {
            double trefNow = RefTime(t);
            for (int i = 0; i < PointCount; i++)
            {
                if (!Inc[i] && fnN[i] > Opt.OnBw * weightN) { Inc[i] = true; TIn[i] = t; FIn[i] = fnN[i]; }
                else if (Inc[i] && fnN[i] < Opt.OffBw * weightN)
                {
                    // a scheduled-loaded point still at the ground stays in contact (a release needs the schedule or a real lift)
                    if (Opt.HoldSched && TOff(i, trefNow) > trefNow + Opt.TOut && pointZ[i] < Opt.ZHold) continue;
                    Inc[i] = false;
                    if (!Inc[2 * (i / 2) + (1 - i % 2)]) TRel[i / 2] = t;
                }
            }
        }

        /// <summary>
        /// Touchdown test, once per tick after <see cref="UpdateContact"/>. A point of the swing leg that made contact after the anchor, once the step is older than
        /// TdMinFrac x ts, swaps the legs and re-anchors the clock (<see cref="Xoff"/> = measured x of that point minus the reference x of it at the new step start).
        /// Earlier contact is early: it stays in <see cref="Inc"/> (fmax rises) but does not re-anchor. Late and missed touchdowns hold the phase (see <see cref="Late"/>).
        /// </summary>
        /// <param name="pointX">measured world forward position of each point.</param>
        /// <param name="refPointX">reference forward position of each point at the start of the new step (parity already swapped), Xoff excluded.</param>
        public bool TryTouchdown(double t, double[] pointX, double[] refPointX)
        {
            LastCallReanchored = false;
            double tau = t - TAnchor;
            if (tau <= Opt.TdMinFrac * _ts) return false;
            int landed = -1;
            for (int i = 2 * Parity; i <= (Opt.HeelOnly ? 2 * Parity : 2 * Parity + 1); i++) if (Inc[i] && TIn[i] > TAnchor + 1e-9) { landed = i; break; }
            if (landed < 0) return false;
            if (Opt.MinAirFrac > 0 && t - TRel[Parity] < Opt.MinAirFrac * _ts) return false;
            if (Opt.XoffMax > 0 && Math.Abs(pointX[landed] - refPointX[landed] - Xoff) > Opt.XoffMax) return false;
            Parity = 1 - Parity; TAnchor = t; TouchdownCount++; LastCallReanchored = true;
            Xoff = pointX[landed] - refPointX[landed];
            return true;
        }

        /// <summary>Upper force bound of a contact point in body weights: ramps to 0 over TOut before the scheduled unload, and rises 1 per TIn after first contact.</summary>
        public double Fmax(int point, double t, double tref, double weightN)
        {
            double ramp = Math.Min(1.0, Math.Max(0.0, (TOff(point, tref) - tref) / Opt.TOut));
            return Math.Min(Opt.FCap * ramp, FIn[point] / weightN + (t - TIn[point]) / Opt.TIn);
        }

        /// <summary>Swing-task weight of a leg in [0, 1]: blends in over TBlend after the leg's measured lift-off; the stance leg is 1.</summary>
        public double SwingBlend(int leg, double t) => leg == Parity ? Math.Min(1.0, Math.Max(0.0, (t - TRel[leg]) / Opt.TBlend)) : 1.0;
    }
}
