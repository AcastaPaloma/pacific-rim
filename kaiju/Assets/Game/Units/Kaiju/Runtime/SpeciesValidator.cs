using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Units.Kaiju.Contracts;

namespace Game.Units.Kaiju
{
    public sealed class SpecCheckResult
    {
        public string Subject, Measure, Detail; public bool Ok;
        public override string ToString() => Subject + " " + Measure + " " + (Ok ? "PASS" : "FAIL") + " " + Detail;
    }

    /// <summary>Recomputed physics of a species (R-102 sections 2-8). Mirrors Tools/kaiju_spec_check.py.</summary>
    public static class SpeciesPhysics
    {
        public const double G = 9.80665;
        public static double Frustum(Frustum f) => Math.PI * f.L / 3.0 * (f.R0 * f.R0 + f.R0 * f.R1 + f.R1 * f.R1);
        public static double Ellipsoid(Ellipsoid e) => 4.0 / 3.0 * Math.PI * e.A * e.B * e.C;

        public static double Volume(KaijuSpeciesSpec s)
        {
            var g = s.Geometry;
            return Ellipsoid(g.Trunk) + Ellipsoid(g.Head) + Frustum(g.Neck) + Frustum(g.Tail) + g.NLimbs * (Frustum(g.LimbUpper) + Frustum(g.LimbLower)) + g.ExtraVolumeM3;
        }

        public static double ElephantVolume(KaijuSpeciesSpec s)
        {
            var m = s.Mass;
            return Ellipsoid(m.ElephantTrunk) + Ellipsoid(m.ElephantHead) + Frustum(m.ElephantNeck) + Frustum(m.ElephantTail)
                + 4 * (Frustum(m.ElephantLimbUpper) + Frustum(m.ElephantLimbLower)) + m.ElephantExtraM3;
        }

        public struct LimbLoad { public double Area, BoneD, Force, SigmaBone, SigmaMuscle, LimbD; }

        public static LimbLoad Limb(KaijuSpeciesSpec s, double mass, double phi, double fr, double boneDOverride = 0)
        {
            var lm = s.LimbModel;
            double R = 0.5 * (s.Geometry.LimbUpper.R0 + s.Geometry.LimbUpper.R1);
            double area = Math.PI * R * R, q = lm.CortexQ;
            double D = boneDOverride > 0 ? boneDOverride : Math.Sqrt(4.0 * lm.BoneAreaShare * area / (Math.PI * (1 - q * q)));
            double A = Math.PI / 4 * D * D * (1 - q * q), I = Math.PI / 64 * Math.Pow(D, 4) * (1 - Math.Pow(q, 4));
            double F = phi * mass * G * (lm.KA + lm.KB * fr);
            return new LimbLoad
            {
                Area = area, BoneD = D, Force = F, LimbD = 2 * R,
                SigmaBone = F / A + F * lm.EFrac * s.Geometry.HipHeightM * (D / 2) / I,
                SigmaMuscle = F / lm.Ema / (lm.MuscleAreaShare * area),
            };
        }

        public static double Stride(KaijuSpeciesSpec s, double fr) => s.Gait.StrideK * Math.Pow(fr, s.Gait.StrideExp) * Math.Sqrt(s.Geometry.HipHeightM / G);

        public static double RealBoneDiameter(double massKg)
        {
            double cMm = Math.Pow(10.0, (Math.Log10(massKg * 1000.0) + 1.104) / 2.749);
            return cMm / 1000.0 / Math.PI / 2.0;
        }

        /// <summary>Energy bound, impulse and class of an attack. cls: 0 inertial, 1 gravity, 2 rush, 3 static.</summary>
        public static void AttackBound(SpecAttack a, out int cls, out double energy, out double impulse)
        {
            double m = a.MEffKg, v = a.VTipMs;
            switch (a.Kind)
            {
                case AttackKind.Bite: case AttackKind.Rake: case AttackKind.Swipe: case AttackKind.TailSweep: case AttackKind.Ram:
                    cls = 0; energy = Math.Min(0.5 * m * v * v, a.MuscleMassKg * a.WorkJPerKg * a.ElasticAmp); impulse = m * v; break;
                case AttackKind.Stomp: case AttackKind.Slam:
                    cls = 1; energy = m * G * a.DropHeightM; impulse = m * v; break;
                case AttackKind.Rush:
                    {
                        double t = a.TargetMassKg, mu = m * t / (m + t);
                        cls = 2; energy = 0.5 * mu * v * v; impulse = (1 + a.Restitution) * mu * v; break;
                    }
                default: cls = 3; energy = 0; impulse = 0; break;
            }
        }
    }

    /// <summary>M-103 (R-102 section 11.3). Thresholds are constants here; a spec cannot lower them.</summary>
    public static class SpeciesValidator
    {
        public const double MechMassKg = 10000000.0, MechMeleeUpperJ = 460e6;
        public const double RhoMin = 850, RhoMax = 1100, ElephantMin = 3000, ElephantMax = 6000;
        public const double SfWalk = 2.5, SfCharge = 2.0, UtilMax = 0.85, StaticPressureMax = 1.2e6;
        const double G = SpeciesPhysics.G;

        static double Rel(double a, double b) => Math.Abs(a - b) / Math.Max(Math.Abs(b), 1e-300);
        static string F(double v, string fmt = "0.00") => v.ToString(fmt, CultureInfo.InvariantCulture);

        public static double ClassHeight(MassClass c) => c == MassClass.Light ? 60.0 : c == MassClass.Mid ? 100.0 : 160.0;

        public static List<SpecCheckResult> CheckSpecies(KaijuSpeciesSpec s, KaijuTissue tissue)
        {
            var o = new List<SpecCheckResult>();
            Action<string, bool, string> add = (m, ok, d) => o.Add(new SpecCheckResult { Subject = s.Id, Measure = m, Ok = ok, Detail = d });
            var gm = s.Geometry; var g = s.Gait; var lm = s.LimbModel; double l = gm.HipHeightM;

            // M-103.1
            double V = SpeciesPhysics.Volume(s), rho = s.Mass.RhoEff, m = rho * V, md = s.Mass.MassKg, ev = SpeciesPhysics.ElephantVolume(s);
            double iy = m * (gm.Trunk.A * gm.Trunk.A + gm.Trunk.B * gm.Trunk.B) / 5;
            bool ok1 = Rel(m, md) <= 0.01 && Rel(V, s.Mass.VolumeM3) <= 0.01 && rho >= RhoMin && rho <= RhoMax
                && Rel(ev, s.Mass.ElephantVolumeM3) <= 0.01 && ev * 860 >= ElephantMin && ev * 1100 <= ElephantMax
                && s.Mass.ElephantMassMinKg >= ElephantMin && s.Mass.ElephantMassMaxKg <= ElephantMax
                && Math.Abs(gm.HeightM - ClassHeight(s.MassClass)) <= 10.0 && Rel(iy, s.Mass.InertiaYaw) <= 0.01;
            add("M-103.1", ok1, "mass " + F(m / 1e3, "0") + " t (declared " + F(md / 1e3, "0") + " t)");

            // M-103.3 / M-103.4
            double bone = s.Material.BoneYieldPa, mus = s.Material.MuscleIsometricPa;
            double frW = g.FrCruise, frC = g.FrChargeCap;
            var lw = SpeciesPhysics.Limb(s, md, lm.PhiPeak, frW); var lc = SpeciesPhysics.Limb(s, md, lm.PhiPeak, frC);
            double sfW = bone / lw.SigmaBone, sfC = bone / lc.SigmaBone, utC = lc.SigmaMuscle / mus;
            bool declOk = s.Material.BoneSfWalkMin >= SfWalk && s.Material.BoneSfChargeMin >= SfCharge && s.Material.MuscleUtilisationMax <= UtilMax;
            double realD = SpeciesPhysics.RealBoneDiameter(md);
            var lr0 = SpeciesPhysics.Limb(s, md, lm.PhiPeak, frC, realD);
            double realSf = tissue.RealBoneYieldPa / lr0.SigmaBone;
            add("M-103.3", sfW >= SfWalk && sfC >= SfCharge && declOk && realSf < 1.0, "SF walk " + F(sfW) + ", charge " + F(sfC) + "; real-bone SF " + F(realSf) + " (report)");
            bool ok4 = utC <= UtilMax; string rear = "";
            if (s.Stance.RearModeAllowed > 0)
            {
                var lr = SpeciesPhysics.Limb(s, md, s.Stance.RearPhi, s.Stance.RearFr);
                double ur = lr.SigmaMuscle / mus, sfr = bone / lr.SigmaBone;
                ok4 = ok4 && ur <= UtilMax && sfr >= SfCharge && s.Stance.RearTimeMaxS > 0; rear = ", rear util " + F(ur) + " SF " + F(sfr);
            }
            else if (s.Stance.RearTimeMaxS != 0) ok4 = false;
            add("M-103.4", ok4, "muscle util charge " + F(utC) + rear);

            // M-103.5
            bool assumedListed = Array.IndexOf(s.Provenance.AssumedFields, "material.bone_yield_Pa") >= 0 && Array.IndexOf(s.Provenance.AssumedFields, "material.muscle_isometric_Pa") >= 0;
            bool ok5 = bone == tissue.BoneYieldPa.Value && mus == tissue.MuscleIsometricPa.Value && bone / tissue.RealBoneYieldPa <= 3.5 && mus / tissue.RealMuscleIsometricPa <= 25.0
                && assumedListed && s.Material.BoneYieldPa.Status == SpecStatus.Assumed && s.Material.MuscleIsometricPa.Status == SpecStatus.Assumed;
            add("M-103.5", ok5, "tissue " + F(bone / 1e6, "0") + " MPa / " + F(mus / 1e6, "0.0") + " MPa");

            // M-103.6 (spec side; the gait_log@v1 measurement waits on T-117 bodies)
            double tr(double f) => 2.3 * Math.Pow(f, -0.2) * Math.Sqrt(l / G);
            double tc = g.StridePeriodCruiseS, tch = g.StridePeriodChargeS;
            bool ok6 = g.FrCruise >= 0.10 && g.FrCruise <= 0.30 && frW <= frC && frC <= 1.0 && g.FrBandMin == 0.10 && g.FrBandMax == 0.30
                && Math.Abs(tc - tr(frW)) / tr(frW) <= 0.15 && Math.Abs(tch - tr(frC)) / tr(frC) <= 0.15
                && Rel(SpeciesPhysics.Stride(s, frW), tc) <= 1e-6 && Rel(SpeciesPhysics.Stride(s, frC), tch) <= 1e-6
                && g.DutyMin >= 0.55 && g.DutyMax <= 0.90 && g.DutyCruise >= g.DutyMin && g.DutyCruise <= g.DutyMax
                && Rel(g.CruiseSpeedMs, Math.Sqrt(frW * G * l)) <= 0.01 && Rel(g.MaxSpeedMs, Math.Sqrt(frC * G * l)) <= 0.01
                && Rel(g.YawRateMaxDps, 26.7 * Math.Sqrt(42.4 / l)) <= 0.01 && Rel(g.YawAccelMaxDps2, 4 * 42.4 / l) <= 0.01
                && Rel(g.LateralGMax, 0.5 * (gm.TrackWidthM / 2) / gm.ComHeightM) <= 0.01
                && Rel(g.AccelPowerCruiseMs2, g.PowerWPerKg / g.CruiseSpeedMs - g.ResistC * G) <= 0.01;
            add("M-103.6", ok6, "Fr " + F(frW) + "/" + F(frC) + ", stride " + F(tc, "0.0") + "/" + F(tch, "0.0") + " s");

            // M-103.7
            bool ok7 = true; double worst = 0;
            foreach (var a in s.Attacks)
            {
                int cls; double bound, imp;
                SpeciesPhysics.AttackBound(a, out cls, out bound, out imp);
                double E = a.ECapJ;
                bool good = E <= bound * (1 + 1e-9) && (bound == 0 || Rel(E, bound) <= 1e-3) && Math.Abs(a.ImpulseNs - imp) <= 1e-3 * Math.Max(imp, 1.0);
                if (cls != 1) good = good && E <= 10 * MechMeleeUpperJ;
                if (cls == 1) good = good && a.DropHeightM > 0 && a.VTipMs <= Math.Sqrt(2 * G * a.DropHeightM) * 1.01;
                if (cls == 2) good = good && Rel(a.MEffKg, md) <= 0.01 && Rel(a.VTipMs, g.MaxSpeedMs) <= 0.01;
                if (cls == 0) good = good && a.MuscleMassKg <= 0.35 * md;
                if (cls == 3) good = good && a.HoldForceN > 0 && a.HoldForceN <= UtilMax * mus * a.MuscleAreaM2;
                ok7 = ok7 && good; worst = Math.Max(worst, E);
            }
            add("M-103.7", ok7, s.Attacks.Count + " attacks, max E_cap " + F(worst / 1e9) + " GJ");

            // M-103.8
            bool floats = rho < s.Water.RhoW;
            bool ok8 = (s.Water.Floats > 0) == floats && Rel(s.Water.DraftFraction, Math.Min(1.0, rho / s.Water.RhoW)) <= 0.01
                && Rel(s.Water.BellyClearanceM, l - gm.Trunk.C) <= 0.01 && !(s.Stance.SwimSpeedMaxMs > 0 && !floats);
            add("M-103.8", ok8, "floats " + floats);

            // M-103.9
            double ps = md * G / (gm.NLimbs * gm.FootAreaM2), pk = md * G * (lm.KA + lm.KB * frC) / (2 * gm.FootAreaM2);
            bool ok9 = ps <= StaticPressureMax && Rel(ps, s.Soil.StaticPressurePa) <= 0.01 && Rel(pk, s.Soil.PeakPressurePa) <= 0.01
                && (s.Soil.Sinks > 0) == (ps > s.Soil.SoilUltimatePa) && Rel(gm.FootRadiusM, Math.Sqrt(gm.FootAreaM2 / Math.PI)) <= 0.01;
            add("M-103.9", ok9, "static " + F(ps / 1e6, "0.000") + " MPa");

            // M-103.10
            add("M-103.10", lw.BoneD <= 0.9 * lw.LimbD && Rel(lw.Area, lm.LimbAreaM2) <= 0.01, "bone D " + F(lw.BoneD) + " m");
            return o;
        }

        /// <summary>M-103.2 on all species sorted by height.</summary>
        public static List<SpecCheckResult> CheckCross(IReadOnlyList<KaijuSpeciesSpec> specs)
        {
            var sorted = new List<KaijuSpeciesSpec>(specs);
            sorted.Sort((x, y) => x.Geometry.HeightM.Value.CompareTo(y.Geometry.HeightM.Value));
            var o = new List<SpecCheckResult>();
            for (int i = 0; i + 1 < sorted.Count; i++)
            {
                var a = sorted[i]; var b = sorted[i + 1];
                double ratio = b.Mass.MassKg / a.Mass.MassKg, iso = Math.Pow(b.Geometry.HeightM / a.Geometry.HeightM, 3);
                o.Add(new SpecCheckResult { Subject = a.Id + "->" + b.Id, Measure = "M-103.2", Ok = ratio >= 0.5 * iso && ratio <= 2.5 * iso, Detail = "mass ratio " + F(ratio) + " vs isometric " + F(iso) });
            }
            return o;
        }

        public static List<SpecCheckResult> CheckAll(IReadOnlyList<KaijuSpeciesSpec> specs, KaijuTissue tissue)
        {
            var o = new List<SpecCheckResult>();
            foreach (var s in specs) o.AddRange(CheckSpecies(s, tissue));
            o.AddRange(CheckCross(specs));
            return o;
        }

        public static bool Passed(List<SpecCheckResult> r) { foreach (var x in r) if (!x.Ok) return false; return r.Count > 0; }
    }
}
