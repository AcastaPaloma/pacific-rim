using System;
using System.Collections.Generic;

namespace Game.Meta.Refit
{
    /// <summary>Everything the section 9 checks compute from one chassis and one part per slot. Deterministic float64 (+ - * / sqrt, atan2 only for the tilt margin).</summary>
    public sealed class RefitEvaluation
    {
        public long TotalMassKg;
        public double ComX, ComY, ComZ;
        public double[] Inertia = new double[6];
        public double PowerRatedW, WalkW, PowerPeakW;
        public double HeatGenW, HeatRejectW, HeatNeedJ, HeatStorageJ;
        /// <summary>Per slot: torque demand and rated torque (N m); NaN for a slot without an actuator.</summary>
        public double[] TorqueDemandNm, TorqueRatedNm;
        public double TiltMinDeg;
        public readonly List<ConstraintMargin> Margins = new List<ConstraintMargin>();
    }

    /// <summary>
    /// R-104 section 9 as code. One build = one part per slot of a chassis. Constants (g, 0.2 MW per tonne of reactor, passive heat, coolant, 2000 N m/kg actuators,
    /// derate, limits) come from chassis@v1; this class holds only the formulas:
    ///  mass: M = sum m_i; cap data (never above p_g A_foot / g).
    ///  CoM: sum m_i c_i / M with c_i = anchor + (mirrored) offset; inertia about the CoM by the parallel-axis theorem with box own-inertia m/12 (b^2 + c^2).
    ///  power: rated = W/kg x reactor-class mass; peak = walking C_T M g v + continuous part demands + the largest pulse; need peak - rated &lt;= buffer / window.
    ///  heat: sustained sum of part heat &lt;= passive + radiator W/kg x radiator mass; burst (sum of burst heat - rejection)+ x profile &lt;= coolant mass x c x dT.
    ///  torque: per actuated slot, demand = sum over moved parts of m g r + (m r^2 + m L^2 / 12) alpha (r = pivot to part CoM, L = largest box side, alpha per chassis),
    ///  rated = N m/kg x derate x actuator (drive-class) mass x (1 + bonus); ratio rated / demand >= 1.
    ///  tilt: atan2(w - |x|, y) of the CoM with the stance half-width w, intact and after losing each listed limb, minimum >= limit.
    /// </summary>
    public static class RefitPhysics
    {
        public const double G = 9.81;

        public static RefitEvaluation Evaluate(ChassisDef ch, PartDef[] parts)
        {
            int n = ch.Slots.Length;
            var e = new RefitEvaluation();
            var m = new double[n]; var cx = new double[n]; var cy = new double[n]; var cz = new double[n];
            long total = 0;
            for (int i = 0; i < n; i++)
            {
                var s = ch.Slots[i]; var p = parts[i];
                m[i] = p.MassKg; total += p.MassKg;
                cx[i] = s.AnchorX + (s.Mirror ? -p.ComX : p.ComX); cy[i] = s.AnchorY + p.ComY; cz[i] = s.AnchorZ + p.ComZ;
            }
            double M = total;
            e.TotalMassKg = total;
            double sx = 0, sy = 0, sz = 0;
            for (int i = 0; i < n; i++) { sx += m[i] * cx[i]; sy += m[i] * cy[i]; sz += m[i] * cz[i]; }
            e.ComX = sx / M; e.ComY = sy / M; e.ComZ = sz / M;

            double ixx = 0, iyy = 0, izz = 0, ixy = 0, ixz = 0, iyz = 0;
            for (int i = 0; i < n; i++)
            {
                var p = parts[i];
                double dx = cx[i] - e.ComX, dy = cy[i] - e.ComY, dz = cz[i] - e.ComZ, k = m[i] / 12.0;
                ixx += k * (p.SizeY * p.SizeY + p.SizeZ * p.SizeZ) + m[i] * (dy * dy + dz * dz);
                iyy += k * (p.SizeX * p.SizeX + p.SizeZ * p.SizeZ) + m[i] * (dx * dx + dz * dz);
                izz += k * (p.SizeX * p.SizeX + p.SizeY * p.SizeY) + m[i] * (dx * dx + dy * dy);
                ixy -= m[i] * dx * dy; ixz -= m[i] * dx * dz; iyz -= m[i] * dy * dz;
            }
            e.Inertia[0] = ixx; e.Inertia[1] = iyy; e.Inertia[2] = izz; e.Inertia[3] = ixy; e.Inertia[4] = ixz; e.Inertia[5] = iyz;

            // power
            long reactorKg = 0; double cont = 0, pulse = 0;
            for (int i = 0; i < n; i++)
            {
                var p = parts[i];
                if (p.CompositionKg.TryGetValue("reactor", out long rk)) reactorKg += rk;
                if (p.PowerPulse) { if (p.PowerPeakW > pulse) pulse = p.PowerPeakW; } else cont += p.PowerPeakW;
            }
            e.PowerRatedW = ch.ReactorWPerKg * reactorKg;
            e.WalkW = ch.Cot * M * G * ch.WalkSpeedMs;
            e.PowerPeakW = e.WalkW + cont + pulse;

            // heat
            double qgen = 0, qpeak = 0; long radKg = 0, coolKg = 0;
            for (int i = 0; i < n; i++) { var p = parts[i]; qgen += p.HeatW; qpeak += p.HeatPeakW; radKg += p.RadiatorKg; coolKg += p.CoolantKg; }
            e.HeatGenW = qgen;
            e.HeatRejectW = ch.PassiveHeatW + ch.RadiatorWPerKg * radKg;
            e.HeatNeedJ = Math.Max(0.0, qpeak - e.HeatRejectW) * ch.BurstS;
            e.HeatStorageJ = coolKg * ch.CoolantJPerKgK * ch.CoolantDeltaK;

            // torque
            e.TorqueDemandNm = new double[n]; e.TorqueRatedNm = new double[n];
            double worstRatio = double.PositiveInfinity; int worstSlot = -1;
            for (int i = 0; i < n; i++)
            {
                e.TorqueDemandNm[i] = double.NaN; e.TorqueRatedNm[i] = double.NaN;
                var s = ch.Slots[i];
                if (!s.Actuated) continue;
                double stat = 0, dyn = 0;
                foreach (int k in ch.Moved[i])
                {
                    double dx = cx[k] - s.PivotX, dy = cy[k] - s.PivotY, dz = cz[k] - s.PivotZ;
                    double r2 = dx * dx + dy * dy + dz * dz, r = Math.Sqrt(r2);
                    var pk = parts[k];
                    double side = Math.Max(pk.SizeX, Math.Max(pk.SizeY, pk.SizeZ));
                    stat += m[k] * G * r;
                    dyn += (m[k] * r2 + m[k] * side * side / 12.0) * ch.AlphaRadS2;
                }
                double demand = stat + dyn;
                long drive = parts[i].CompositionKg.TryGetValue("drive", out long dk) ? dk : 0;
                double rated = ch.TorqueNmPerKg * drive * ch.TorqueDerate * (1.0 + parts[i].TorqueBonusBps / 10000.0);
                e.TorqueDemandNm[i] = demand; e.TorqueRatedNm[i] = rated;
                double ratio = demand > 0 ? rated / demand : double.PositiveInfinity;
                if (ratio < worstRatio) { worstRatio = ratio; worstSlot = i; }
            }

            // tilt margin (intact and after losing each limb)
            double w = ch.StanceHalfWidthM;
            double tilt = Math.Atan2(w - Math.Abs(e.ComX), e.ComY) * (180.0 / Math.PI);
            foreach (var limb in ch.Limbs)
            {
                var lost = new bool[n];
                foreach (int k in limb.Value) lost[k] = true;
                double mk = 0, xk = 0, yk = 0;
                for (int i = 0; i < n; i++) { if (lost[i]) continue; mk += m[i]; xk += m[i] * cx[i]; yk += m[i] * cy[i]; }
                double t = Math.Atan2(w - Math.Abs(xk / mk), yk / mk) * (180.0 / Math.PI);
                if (t < tilt) tilt = t;
            }
            e.TiltMinDeg = tilt;

            // margins, in check order
            double h = ch.FrameHeightM;
            Add(e, RefitViolation.Mass, "", M, ch.MassCapKg, ch.MassCapKg - M, M <= ch.MassCapKg);
            double excess = e.PowerPeakW - e.PowerRatedW, allow = ch.BufferJ / ch.BufferWindowS;
            Add(e, RefitViolation.Power, "", excess, allow, allow - excess, excess <= allow);
            Add(e, RefitViolation.HeatRejection, "", e.HeatGenW, e.HeatRejectW, e.HeatRejectW - e.HeatGenW, e.HeatGenW <= e.HeatRejectW);
            Add(e, RefitViolation.HeatStorage, "", e.HeatNeedJ, e.HeatStorageJ, e.HeatStorageJ - e.HeatNeedJ, e.HeatNeedJ <= e.HeatStorageJ);
            if (worstSlot >= 0) Add(e, RefitViolation.Torque, ch.Slots[worstSlot].Name, worstRatio, 1.0, worstRatio - 1.0, worstRatio >= 1.0);
            else Add(e, RefitViolation.Torque, "", double.PositiveInfinity, 1.0, double.PositiveInfinity, true);
            double lo = ch.ComHeightLoFrac * h, hi = ch.ComHeightHiFrac * h;
            Add(e, RefitViolation.ComHeight, "", e.ComY, lo, Math.Min(e.ComY - lo, hi - e.ComY), e.ComY >= lo && e.ComY <= hi);
            Add(e, RefitViolation.ComLateral, "", Math.Abs(e.ComX), ch.ComLateralM, ch.ComLateralM - Math.Abs(e.ComX), Math.Abs(e.ComX) <= ch.ComLateralM);
            Add(e, RefitViolation.ComForeAft, "", e.ComZ, ch.ComForeAftLoM, Math.Min(e.ComZ - ch.ComForeAftLoM, ch.ComForeAftHiM - e.ComZ), e.ComZ >= ch.ComForeAftLoM && e.ComZ <= ch.ComForeAftHiM);
            Add(e, RefitViolation.TiltMargin, "", tilt, ch.TiltMinDeg, tilt - ch.TiltMinDeg, tilt >= ch.TiltMinDeg);
            return e;
        }

        static void Add(RefitEvaluation e, RefitViolation kind, string subject, double value, double limit, double margin, bool ok)
            => e.Margins.Add(new ConstraintMargin { Kind = kind, Subject = subject, Value = value, Limit = limit, Margin = margin, Ok = ok });

        /// <summary>MechBuildSpec@v1 from an evaluation. The hash is FNV-1a 64 over the chassis id, slot names, part ids, masses and the float64 bits of the CoM and inertia.</summary>
        public static MechBuildSpec ToSpec(ChassisDef ch, PartDef[] parts, RefitEvaluation e)
        {
            int n = ch.Slots.Length;
            var spec = new MechBuildSpec
            {
                Chassis = ch.Id, SlotNames = new string[n], PartIds = new string[n], PartMassKg = new long[n], TorqueLimitNm = new double[n],
                TotalMassKg = e.TotalMassKg, ComX = e.ComX, ComY = e.ComY, ComZ = e.ComZ, Inertia = (double[])e.Inertia.Clone(),
                PowerRatedW = e.PowerRatedW, PowerPeakW = e.PowerPeakW, HeatRejectionW = e.HeatRejectW, HeatStorageJ = e.HeatStorageJ,
            };
            ulong h = 14695981039346656037UL;
            h = Mix(h, ch.Id);
            for (int i = 0; i < n; i++)
            {
                spec.SlotNames[i] = ch.Slots[i].Name; spec.PartIds[i] = parts[i].Id; spec.PartMassKg[i] = parts[i].MassKg;
                spec.TorqueLimitNm[i] = double.IsNaN(e.TorqueRatedNm[i]) ? 0.0 : e.TorqueRatedNm[i];
                h = Mix(h, ch.Slots[i].Name); h = Mix(h, parts[i].Id); h = Mix(h, (ulong)parts[i].MassKg);
            }
            h = Mix(h, (ulong)e.TotalMassKg);
            h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(e.ComX)); h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(e.ComY)); h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(e.ComZ));
            for (int i = 0; i < 6; i++) h = Mix(h, (ulong)BitConverter.DoubleToInt64Bits(e.Inertia[i]));
            long hash = unchecked((long)h);
            spec.Hash = hash == 0 ? 1 : hash;   // 0 is "no spec yet" in the roster
            return spec;
        }

        static ulong Mix(ulong h, string s)
        {
            unchecked
            {
                foreach (char c in s) { h = (h ^ (c & 0xFFu)) * 1099511628211UL; h = (h ^ (ulong)(c >> 8)) * 1099511628211UL; }
                return (h ^ 0xFFu) * 1099511628211UL;   // terminator, so "ab","c" differs from "a","bc"
            }
        }

        static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++) { h = (h ^ (v & 0xFFu)) * 1099511628211UL; v >>= 8; }
                return h;
            }
        }
    }
}
