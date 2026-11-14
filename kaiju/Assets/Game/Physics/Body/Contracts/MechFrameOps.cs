using System;
using System.Collections.Generic;

namespace Game.Physics.Body.Contracts
{
    /// <summary>Engine-free operations on mech_frame@v1: validation, the R-005 section 1.4 scaling rule, mass sums, and the effective inertias the gain rule needs.</summary>
    public static class MechFrameOps
    {
        public static void Validate(MechFrameData f)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));
            if (f.schema != MechFrameData.Schema) throw new InvalidOperationException("mech frame schema '" + f.schema + "' is not " + MechFrameData.Schema + " (refusing unknown version)");
            if (f.segments == null || f.segments.Length == 0) throw new InvalidOperationException("mech frame has no segments");
            int roots = 0;
            foreach (var s in f.segments)
            {
                if (string.IsNullOrEmpty(s.parent)) roots++; else if (Find(f, s.parent) < 0) throw new InvalidOperationException("segment " + s.id + " has unknown parent " + s.parent);
                if (!(s.mass_kg > 0)) throw new InvalidOperationException("segment " + s.id + " has no mass");
            }
            if (roots != 1) throw new InvalidOperationException("mech frame needs exactly one root segment, found " + roots);
            foreach (var j in f.joints) if (Find(f, j.child) < 0 || Find(f, j.parent) < 0) throw new InvalidOperationException("joint " + j.id + " references an unknown segment");
            double sum = TotalMass(f);
            if (Math.Abs(sum - f.total_mass_kg) > 1e-3 * f.total_mass_kg) throw new InvalidOperationException("segment masses sum to " + sum + " kg, header says " + f.total_mass_kg);
        }

        public static int Find(MechFrameData f, string id)
        {
            for (int i = 0; i < f.segments.Length; i++) if (f.segments[i].id == id) return i;
            return -1;
        }
        public static JointData JointOfChild(MechFrameData f, string child)
        {
            foreach (var j in f.joints) if (j.child == child) return j;
            return null;
        }
        public static double TotalMass(MechFrameData f) { double m = 0; foreach (var s in f.segments) m += s.mass_kg; return m; }
        public static double[] Com(MechFrameData f)
        {
            double m = 0, x = 0, y = 0, z = 0;
            foreach (var s in f.segments) { m += s.mass_kg; x += s.mass_kg * s.com[0]; y += s.mass_kg * s.com[1]; z += s.mass_kg * s.com[2]; }
            return new[] { x / m, y / m, z / m };
        }
        /// <summary>Hip joint height above the sole in the rest pose: the leg length l used in the Froude number.</summary>
        public static double LegLength(MechFrameData f)
        {
            foreach (var j in f.joints) if (j.id.StartsWith("hip", StringComparison.Ordinal)) return j.position[1];
            throw new InvalidOperationException("frame has no hip joint");
        }
        public static List<int> FootSegments(MechFrameData f)
        {
            var r = new List<int>();
            for (int i = 0; i < f.segments.Length; i++) if (f.segments[i].kind == "foot") r.Add(i);
            return r;
        }
        public static bool IsDescendant(MechFrameData f, int seg, int ancestor)
        {
            for (int i = seg; i >= 0; i = string.IsNullOrEmpty(f.segments[i].parent) ? -1 : Find(f, f.segments[i].parent)) if (i == ancestor) return true;
            return false;
        }

        /// <summary>R-005 section 1.4: lengths x H/H0, masses x M/M0, inertias x (M/M0)(H/H0)^2, ROM unchanged. Returns a deep copy.</summary>
        public static MechFrameData Scale(MechFrameData f, double heightM, double massKg)
        {
            double kl = heightM / f.total_height_m, km = massKg / f.total_mass_kg, ki = km * kl * kl;
            var g = new MechFrameData
            {
                schema = f.schema, id = f.id, total_height_m = heightM, total_mass_kg = massKg, com_rest = Mul(f.com_rest, kl),
                hull_volume_m3 = f.hull_volume_m3 * kl * kl * kl, mean_hull_density_kg_m3 = f.mean_hull_density_kg_m3 * km / (kl * kl * kl),
                stance = new StanceData { neutral_stance_width_m = f.stance.neutral_stance_width_m * kl },
                foot_sole = new FootSoleData { length_m = f.foot_sole.length_m * kl, width_m = f.foot_sole.width_m * kl, area_m2 = f.foot_sole.area_m2 * kl * kl, heel_z_m = f.foot_sole.heel_z_m * kl, toe_z_m = f.foot_sole.toe_z_m * kl, ankle_z_m = f.foot_sole.ankle_z_m * kl, ankle_height_m = f.foot_sole.ankle_height_m * kl },
                m0_active_dof = f.m0_active_dof, segments = new SegmentData[f.segments.Length], joints = new JointData[f.joints.Length]
            };
            for (int i = 0; i < f.segments.Length; i++)
            {
                var s = f.segments[i];
                g.segments[i] = new SegmentData
                {
                    id = s.id, kind = s.kind, side = s.side, parent = s.parent, proximal_joint = Mul(s.proximal_joint, kl), distal_end = Mul(s.distal_end, kl), com = Mul(s.com, kl),
                    inertia_principal_kgm2 = Mul(s.inertia_principal_kgm2, ki), length_m = s.length_m * kl, width_x_m = s.width_x_m * kl, height_y_m = s.height_y_m * kl, depth_z_m = s.depth_z_m * kl,
                    mass_kg = s.mass_kg * km, hull_volume_m3 = s.hull_volume_m3 * kl * kl * kl, hull_density_kg_m3 = s.hull_density_kg_m3 * km / (kl * kl * kl)
                };
            }
            for (int i = 0; i < f.joints.Length; i++)
            {
                var j = f.joints[i];
                var dofs = new DofData[j.dofs.Length];
                for (int d = 0; d < dofs.Length; d++) { var o = j.dofs[d]; dofs[d] = new DofData { name = o.name, axis = (double[])o.axis.Clone(), unity_drive = o.unity_drive, positive = o.positive, unity_sign = o.unity_sign, rom_deg = (double[])o.rom_deg.Clone(), m0 = o.m0 }; }
                g.joints[i] = new JointData { id = j.id, parent = j.parent, child = j.child, unity_joint_type = j.unity_joint_type, position = Mul(j.position, kl), dofs = dofs };
            }
            return g;
        }
        static double[] Mul(double[] v, double k) { var r = new double[v.Length]; for (int i = 0; i < v.Length; i++) r[i] = v[i] * k; return r; }

        /// <summary>Inertia about a world axis (0 x, 1 y, 2 z) through <paramref name="point"/> of the given set of segments, rest pose (parallel-axis theorem on principal values).</summary>
        public static double InertiaAbout(MechFrameData f, Predicate<int> include, double[] point, int axis)
        {
            double sum = 0;
            for (int i = 0; i < f.segments.Length; i++)
            {
                if (!include(i)) continue;
                var s = f.segments[i];
                double dx = s.com[0] - point[0], dy = s.com[1] - point[1], dz = s.com[2] - point[2];
                double r2 = axis == 0 ? dy * dy + dz * dz : axis == 1 ? dx * dx + dz * dz : dx * dx + dy * dy;
                sum += s.inertia_principal_kgm2[axis] + s.mass_kg * r2;
            }
            return sum;
        }

        /// <summary>Swing-side effective inertia of a joint DOF: everything distal to the joint, about the joint axis (R-005 section 3.2, rows "swing").</summary>
        public static double DistalInertia(MechFrameData f, JointData joint, int axis)
        {
            int child = Find(f, joint.child);
            return InertiaAbout(f, i => IsDescendant(f, i, child), joint.position, axis);
        }
        /// <summary>Stance-side effective inertia: everything that is not distal to the joint, about the joint axis (the body above a planted leg).</summary>
        public static double ProximalInertia(MechFrameData f, JointData joint, int axis)
        {
            int child = Find(f, joint.child);
            return InertiaAbout(f, i => !IsDescendant(f, i, child), joint.position, axis);
        }
        public static int AxisIndex(DofData d) => Math.Abs(d.axis[0]) > 0.5 ? 0 : Math.Abs(d.axis[1]) > 0.5 ? 1 : 2;
        public static string JointKind(string jointId) { int u = jointId.IndexOf('_'); return u < 0 ? jointId : jointId.Substring(0, u); }
    }
}
