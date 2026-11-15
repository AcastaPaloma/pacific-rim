using System;

namespace Game.Units.Mech
{
    /// <summary>
    /// The optimised one-step reference (gait_reference_opt@v1) blended to one Froude number and sampled at a step phase. Engine-free; used by ReferenceGait at imp = 2.
    /// Rows are blended node by node, linearly in Froude, clamped to the first and last row; phase is interpolated linearly between nodes; after the last node comes node 0 with stance and swing swapped
    /// (the mirrored periodic boundary). Lengths scale with leg length / the row's leg length, times with its square root (same Froude number), forces and torques are not rescaled.
    /// </summary>
    public sealed class OptimisedReference
    {
        // vector layout per node: [0] pelvis x (moving frame), [1] pelvis z, [2] pitch deg, then stance and swing legs of 16 values each
        public const int Leg = 16, Stance = 3, Swing = 19, Size = 35;
        public const int Hip = 0, Knee = 1, Ankle = 2, HipRate = 3, KneeRate = 4, AnkleRate = 5, FootPitch = 6, AnkleX = 7, AnkleY = 8, TauHip = 9, TauKnee = 10, TauAnkle = 11, HeelFt = 12, HeelFn = 13, ToeFt = 14, ToeFn = 15;

        public int Nodes { get; }
        public double Fr { get; }
        public double StepTime { get; }
        public double StepLength { get; }
        public double Duty { get; }
        public double MeanPelvisHeight { get; }
        public double MeanPelvisPitch { get; }   // rad
        public double FrontReach { get; }        // stance ankle ahead of the hip at heel strike
        readonly double[][] _v;       // blended node vectors
        readonly bool[][] _flags;     // per node: stance heel, stance toe, swing heel, swing toe (from the nearer row)
        readonly double _swingStart;  // step phase at which the swing leg leaves the ground

        public OptimisedReference(GaitReferenceData data, double fr, double legLength)
        {
            var rows = data.rows; int n = rows[0].nodes.Length; Nodes = n; Fr = fr;
            int i = 0; while (i < rows.Length - 1 && fr > rows[i + 1].fr) i++;
            int j = Math.Min(i + 1, rows.Length - 1);
            double f = j == i ? 0 : Math.Max(0, Math.Min(1, (fr - rows[i].fr) / (rows[j].fr - rows[i].fr)));
            var a = rows[i]; var b = rows[j]; var near = f < 0.5 ? a : b;
            double leg = Mix(a.leg_length_m, b.leg_length_m, f), sc = legLength / leg, ts = Math.Sqrt(sc);
            StepTime = Mix(a.step_time_s, b.step_time_s, f) * ts; StepLength = Mix(a.step_length_m, b.step_length_m, f) * sc; Duty = Mix(a.duty_factor, b.duty_factor, f);
            _v = new double[n][]; _flags = new bool[n][];
            double meanZ = 0, meanPitch = 0;
            for (int k = 0; k < n; k++)
            {
                var va = Pack(a.nodes[k]); var vb = Pack(b.nodes[k]); var v = new double[Size];
                for (int c = 0; c < Size; c++) v[c] = Mix(va[c], vb[c], f);
                // lengths of the reference mech to this mech's size, rates to its time scale
                v[0] *= sc; v[1] *= sc;
                foreach (int o in new[] { Stance, Swing })
                {
                    v[o + AnkleX] *= sc; v[o + AnkleY] *= sc;
                    v[o + HipRate] /= ts; v[o + KneeRate] /= ts; v[o + AnkleRate] /= ts;
                }
                _v[k] = v; var nn = near.nodes[k];
                _flags[k] = new[] { nn.stance.heel_contact, nn.stance.toe_contact, nn.swing.heel_contact, nn.swing.toe_contact };
                meanZ += v[1] / n; meanPitch += v[2] / n * Math.PI / 180;
            }
            MeanPelvisHeight = meanZ; MeanPelvisPitch = meanPitch;
            int ks = 0; while (ks < n && _flags[ks][3]) ks++;   // first node with the swing toe off the ground
            _swingStart = (double)ks / n;
            FrontReach = _v[0][Stance + AnkleX] - _v[0][0];
        }

        static double Mix(double a, double b, double f) => (1 - f) * a + f * b;

        static double[] Pack(GaitRefNode nd)
        {
            var v = new double[Size]; v[0] = nd.pelvis_x_rel_m; v[1] = nd.pelvis_z_m; v[2] = nd.pelvis_pitch_deg;
            PackLeg(v, Stance, nd.stance); PackLeg(v, Swing, nd.swing); return v;
        }
        static void PackLeg(double[] v, int o, GaitRefLeg l)
        {
            v[o + Hip] = l.hip_deg; v[o + Knee] = l.knee_deg; v[o + Ankle] = l.ankle_deg; v[o + HipRate] = l.hip_dps; v[o + KneeRate] = l.knee_dps; v[o + AnkleRate] = l.ankle_dps;
            v[o + FootPitch] = l.foot_pitch_deg; v[o + AnkleX] = l.ankle_x_m; v[o + AnkleY] = l.ankle_height_m; v[o + TauHip] = l.hip_tau_nm; v[o + TauKnee] = l.knee_tau_nm; v[o + TauAnkle] = l.ankle_tau_nm;
            v[o + HeelFt] = l.heel_ft_n; v[o + HeelFn] = l.heel_fn_n; v[o + ToeFt] = l.toe_ft_n; v[o + ToeFn] = l.toe_fn_n;
        }

        /// <summary>Node vector k, k = Nodes meaning node 0 with stance and swing swapped.</summary>
        double[] Node(int k)
        {
            if (k < Nodes) return _v[k];
            var s = _v[0]; var m = new double[Size]; m[0] = s[0]; m[1] = s[1]; m[2] = s[2];
            Array.Copy(s, Swing, m, Stance, Leg); Array.Copy(s, Stance, m, Swing, Leg); return m;
        }

        /// <summary>The blended vector at a step phase in [0, 1): stance leg at [Stance..], swing leg at [Swing..]. Contact flags are those of the node at or before the phase.</summary>
        public double[] At(double u, out bool[] flags, out double swingProgress)
        {
            u -= Math.Floor(u); double x = u * Nodes, rx = Math.Round(x); if (Math.Abs(x - rx) < 1e-9) x = rx;   // snap: a phase that is a node to rounding must take that node's contact flags
            if (x >= Nodes) x -= Nodes;
            int i = Math.Min(Nodes - 1, (int)x); double f = x - i;
            var a = Node(i); var b = Node(i + 1); var v = new double[Size];
            for (int c = 0; c < Size; c++) v[c] = Mix(a[c], b[c], f);
            flags = _flags[i];
            swingProgress = u >= _swingStart ? Math.Min(1, (u - _swingStart) / Math.Max(1e-9, 1 - _swingStart)) : 0;
            return v;
        }
    }
}
