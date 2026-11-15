using System;
using System.Numerics;
using Game.Physics.Body.Contracts;
using Game.Physics.Body.Qp;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech.Qp
{
    /// <summary>
    /// Adapter between the engine body (<see cref="IArticulatedBody"/> contract v3) and the planar <see cref="SagittalQpController"/> (step 9.4b of T-005).
    /// Per tick: reads the dense link Jacobian, bias force, link states and foot forces; builds the sagittal sub-model S = [x forward, z up, th (pitch, forward lean +), hip/knee/ankle of leg A, of leg B]
    /// with <see cref="WholeBodyModel"/> (M_ss, point Jacobians, A_G); converts it to the controller's planar coordinates (q9: a = hip - th, b = a - knee, f = ankle - b, same constant map for M, h, Jp);
    /// runs the controller; writes the 6 joint torques as feed-forward (<see cref="IArticulatedBody.SetDofFeedForward"/>, last write wins).
    /// Layout SETTLED by the step 9.4c-2 probes (WholeBodyProbeTests, evidence step4c.md): base columns [lin x, y, z, ang x, y, z] = root COM linear / root angular velocity, world frame, about the root COM (<see cref="BuildSColumns"/>);
    /// the row block of link i starts at <see cref="IArticulatedBody.LinkJacobianRow"/> (engine articulation index * 6, not the link order), rows = link COM linear then angular, world frame; bias h in M qdd + h = S tau + J'f (gravity built from J).
    /// Heading yaw is applied to the base columns and the point-Jacobian rows. Open (step4c): the soft-locked ankle axis (the inert slot is not a column of S; the ankle rows of M carry up to 4 % error).
    /// Buffers are sized on the first Apply (the engine's column count is known after the first step); no allocation afterwards.
    /// </summary>
    public sealed class WholeBodyQpDriver
    {
        const int NS = 9;
        readonly IArticulatedBody _b; readonly double _dt; readonly int[] _dof = new int[6], _foot = new int[2];
        readonly WholeBodyModel _wb; readonly SagittalQpController _ctl; readonly int _links, _root;
        readonly double[] _p = new double[NS * NS], _tmp = new double[NS * NS], _qdS = new double[NS], _hS = new double[NS], _j2 = new double[4 * 2 * NS], _j2prev = new double[4 * 2 * NS], _sole = new double[6];
        readonly int[] _col = new int[NS], _bc = new int[6];
        double[] _jac, _jacS, _bias; int _rows, _cols; bool _havePrev; long _n;
        readonly double _heelZ, _toeZ, _ankleZ, _ankleH, _halfW; readonly bool _lat;
        readonly bool[] _rest;   // step 9.5e: link of a leg or an arm (not the trunk: pelvis, torso, head)
        readonly double _ffAlpha, _ffMax; double _lRestPrev = double.NaN, _dlRest;
        /// <summary>Stage 2 (qp_lateral): heading the yaw task holds (rad, same convention as the yaw argument of Apply); set by the caller each tick.</summary>
        public double DesiredYaw;
        /// <summary>Stage 2: world positions of the 8 sole corners (corner c = 2 * point + k) and the commanded world corner forces of the last Apply (ground on foot, N).</summary>
        public readonly Vector3[] CornerWorld = new Vector3[8], ForceWorld = new Vector3[8];
        public SagittalQpController Controller => _ctl;
        /// <summary>The whole-body model of the last Apply (M_ss, point Jacobians, A_G); read-only access for tests and logs.</summary>
        public WholeBodyModel Model => _wb;
        public long Ticks => _n;
        public bool Ready { get; private set; }

        /// <param name="dofs">hip flexion, knee flexion, ankle plantarflexion of planar leg A, then of leg B.</param>
        /// <param name="footOfLeg">body foot index of planar leg A and B.</param>
        public WholeBodyQpDriver(IArticulatedBody body, WalkTuning tuning, GaitRefRow reference, IQpSolver solver, int[] dofs, int[] footOfLeg, double dt)
        {
            _b = body; _dt = dt; _links = body.LinkCount; Array.Copy(dofs, _dof, 6); Array.Copy(footOfLeg, _foot, 2);
            var qr = new QpReference(reference, tuning.qp_load_thr, tuning.qp_onset > 0.5);
            var opt = SagittalQpOptions.From(tuning); opt.G = body.Tuning.gravity_mps2;
            var lim = new double[6]; for (int i = 0; i < 6; i++) lim[i] = body.GetDof(_dof[i]).ForceLimitNm;
            _ctl = new SagittalQpController(qr, opt, lim, solver);
            _wb = new WholeBodyModel(_links, NS, 4) { Links = _links, S = NS, Points = 4 };
            for (int i = 0; i < _links; i++) { _wb.Mass[i] = body.LinkMassKg(i); }
            var f = body.Frame; for (int i = 0; i < _links; i++) { string id = body.LinkId(i); foreach (var sg in f.segments) if (sg.id == id) { for (int k = 0; k < 3; k++) _wb.InertiaDiag[3 * i + k] = sg.inertia_principal_kgm2[k]; break; } }
            for (int s = 0; s < NS; s++) _wb.SagCols[s] = s;
            int root = 0; for (int i = 0; i < _links; i++) if (body.LinkParent(i) < 0) { root = i; break; }
            _root = root;
            var sole = f.foot_sole; _heelZ = sole.heel_z_m; _toeZ = sole.toe_z_m; _ankleZ = sole.ankle_z_m; _ankleH = sole.ankle_height_m; _halfW = 0.5 * sole.width_m; _lat = opt.Lateral;
            _rest = new bool[_links]; for (int i = 0; i < _links; i++) { string id = body.LinkId(i); _rest[i] = id.Contains("arm") || id.Contains("hand") || id.Contains("thigh") || id.Contains("shank") || id.Contains("foot"); }
            _ffAlpha = Math.Max(0.0, Math.Min(1.0, tuning.qp_yaw_ff_alpha)); _ffMax = tuning.qp_yaw_ff_max;
            for (int leg = 0; leg < 2; leg++) { body.GetFoot(_foot[leg], out var fs); _wb.PointLink[2 * leg] = _wb.PointLink[2 * leg + 1] = fs.Link; }
            // S = P q9
            for (int i = 0; i < 3; i++) _p[i * NS + i] = 1;
            for (int leg = 0; leg < 2; leg++)
            {
                int r = 3 + 3 * leg, a = 3 + 3 * leg, b = 4 + 3 * leg, ff = 5 + 3 * leg;
                _p[r * NS + 2] = 1; _p[r * NS + a] = 1; _p[(r + 1) * NS + a] = 1; _p[(r + 1) * NS + b] = -1; _p[(r + 2) * NS + b] = 1; _p[(r + 2) * NS + ff] = 1;
            }
        }

        /// <summary>Restart at reference node 0 (called when walking starts).</summary>
        public void Reset(bool anchorXoff = false, int startNode = 0) { _ctl.AnchorXoff = anchorXoff; _ctl.StartNode = startNode; _ctl.Init(); _havePrev = false; _n = 0; _lRestPrev = double.NaN; _dlRest = 0; }

        void Allocate()
        {
            _cols = _b.ModelColumns; _rows = _links * 6;
            _jac = new double[_rows * _cols]; _jacS = new double[_rows * NS]; _bias = new double[_cols];
            for (int i = 0; i < _links; i++) _wb.LinkRow[i] = _b.LinkJacobianRow(i);   // engine indices exist once the articulation does (first step)
            for (int k = 0; k < 6; k++) _bc[k] = _b.ModelBaseColumn(k);
            for (int j = 0; j < 6; j++) _col[3 + j] = _b.ModelColumnOfDof(_dof[j]);
        }

        /// <summary>Copies base column combinations (heading frame) and joint columns of the Jacobian into the S-column matrix.</summary>
        void BuildSColumns(double sy, double cy)
        {
            var bc = _bc;
            for (int r = 0; r < _rows; r++)
            {
                int o = r * _cols, os = r * NS;
                _jacS[os] = sy * _jac[o + bc[0]] + cy * _jac[o + bc[2]];                 // forward translation
                _jacS[os + 1] = _jac[o + bc[1]];                                          // up
                _jacS[os + 2] = cy * _jac[o + bc[3]] - sy * _jac[o + bc[5]];              // pitch about the lateral (right) axis, forward lean +
                for (int s = 3; s < NS; s++) _jacS[os + s] = _jac[o + _col[s]];
            }
            _hS[0] = sy * _bias[bc[0]] + cy * _bias[bc[2]]; _hS[1] = _bias[bc[1]]; _hS[2] = cy * _bias[bc[3]] - sy * _bias[bc[5]];
            for (int s = 3; s < NS; s++) _hS[s] = _bias[_col[s]];
        }

        /// <summary>One control tick: reads the body, runs the QP, writes the feed-forward of the six sagittal leg DOFs. Returns false when the engine model was not available yet or the QP kept the previous torques.</summary>
        /// <param name="t">seconds since the QP was enabled; <paramref name="yaw"/> heading (rad, forward = (sin yaw, cos yaw) in world x, z); <paramref name="groundY"/> world height of the ground.</param>
        public double TimeScale { get => _ctl.TimeScale; set => _ctl.TimeScale = value; }
        public bool Apply(double t, double yaw, double groundY)
        {
            if (_b.ModelColumns <= 0) return false;
            if (_jac == null) Allocate();
            if (!_b.GetBodyJacobian(_jac, out int rows, out int cols) || rows != _rows || cols != _cols) return false;
            _b.GetBiasForce(_bias);
            double sy = Math.Sin(yaw), cy = Math.Cos(yaw);
            BuildSColumns(sy, cy);
            // links and points
            for (int i = 0; i < _links; i++)
            {
                _b.GetLink(i, out var ls); int o3 = 3 * i, o9 = 9 * i;
                _wb.Com[o3] = ls.Com.X; _wb.Com[o3 + 1] = ls.Com.Y; _wb.Com[o3 + 2] = ls.Com.Z;
                _wb.RefPos[o3] = ls.Com.X; _wb.RefPos[o3 + 1] = ls.Com.Y; _wb.RefPos[o3 + 2] = ls.Com.Z;   // Jacobian rows are the velocity of the link COM
                var ex = Vector3.Transform(Vector3.UnitX, ls.Rotation); var ey = Vector3.Transform(Vector3.UnitY, ls.Rotation); var ez = Vector3.Transform(Vector3.UnitZ, ls.Rotation);
                _wb.Rot[o9] = ex.X; _wb.Rot[o9 + 3] = ex.Y; _wb.Rot[o9 + 6] = ex.Z; _wb.Rot[o9 + 1] = ey.X; _wb.Rot[o9 + 4] = ey.Y; _wb.Rot[o9 + 7] = ey.Z; _wb.Rot[o9 + 2] = ez.X; _wb.Rot[o9 + 5] = ez.Y; _wb.Rot[o9 + 8] = ez.Z;
            }
            for (int leg = 0; leg < 2; leg++)
            {
                _b.GetFoot(_foot[leg], out var fs); _b.GetLink(fs.Link, out var ls);
                for (int k = 0; k < 2; k++)
                {
                    var pw = ls.Position + Vector3.Transform(new Vector3(0f, (float)-_ankleH, (float)((k == 0 ? _heelZ : _toeZ) - _ankleZ)), ls.Rotation); int p = 2 * leg + k;
                    _wb.PointPos[3 * p] = pw.X; _wb.PointPos[3 * p + 1] = pw.Y; _wb.PointPos[3 * p + 2] = pw.Z;
                }
            }
            _wb.Compute(_jacS, NS);
            // S velocities
            _b.GetLink(_root, out var root);
            var fwd = new Vector3((float)sy, 0f, (float)cy); var right = new Vector3((float)cy, 0f, (float)-sy);
            _qdS[0] = Vector3.Dot(root.Velocity, fwd); _qdS[1] = root.Velocity.Y; _qdS[2] = Vector3.Dot(root.AngularVelocity, right);
            for (int j = 0; j < 6; j++) _qdS[3 + j] = _b.DofVelocity(_dof[j]);
            var up = Vector3.Transform(Vector3.UnitY, root.Rotation); double th = Math.Atan2(Vector3.Dot(up, fwd), up.Y);
            // planar inputs
            var c = _ctl;
            for (int i = 0; i < 9; i++) { c.Q[i] = 0; c.Qd[i] = 0; }
            c.Q[2] = th; c.Qd[0] = _qdS[0]; c.Qd[1] = _qdS[1]; c.Qd[2] = _qdS[2];
            for (int leg = 0; leg < 2; leg++)
            {
                int a = 3 + 3 * leg, b = a + 1, f = a + 2; double hip = _b.DofPosition(_dof[3 * leg]), knee = _b.DofPosition(_dof[3 * leg + 1]), ank = _b.DofPosition(_dof[3 * leg + 2]);
                c.Q[a] = hip - th; c.Q[b] = c.Q[a] - knee; c.Q[f] = ank - c.Q[b];
                double hd = _qdS[a], kd = _qdS[a + 1], ad = _qdS[a + 2];
                c.Qd[a] = hd - c.Qd[2]; c.Qd[b] = c.Qd[a] - kd; c.Qd[f] = ad - c.Qd[b];
            }
            // M9 = P' Mss P, h9 = P' hS
            for (int i = 0; i < NS; i++) for (int j = 0; j < NS; j++) { double s = 0; for (int k = 0; k < NS; k++) s += _wb.Mss[i * NS + k] * _p[k * NS + j]; _tmp[i * NS + j] = s; }
            for (int i = 0; i < NS; i++) { for (int j = 0; j < NS; j++) { double s = 0; for (int k = 0; k < NS; k++) s += _p[k * NS + i] * _tmp[k * NS + j]; c.M[i * NS + j] = s; } double hh = 0; for (int k = 0; k < NS; k++) hh += _p[k * NS + i] * _hS[k]; c.H[i] = hh; }
            // point Jacobians (heading rows: forward, up) and finite-difference Jdot qd
            for (int pnt = 0; pnt < 4; pnt++)
            {
                int b = pnt * 3 * NS;
                for (int s = 0; s < NS; s++) { _j2[pnt * 18 + s] = sy * _wb.Jp[b + s] + cy * _wb.Jp[b + 2 * NS + s]; _j2[pnt * 18 + 9 + s] = _wb.Jp[b + NS + s]; }
                for (int d = 0; d < 2; d++)
                {
                    double bp = 0;
                    for (int j = 0; j < NS; j++)
                    {
                        double s = 0; for (int k = 0; k < NS; k++) s += _j2[pnt * 18 + d * 9 + k] * _p[k * NS + j];
                        c.Jp[pnt * 18 + d * 9 + j] = s;
                        if (_havePrev) bp += (_j2[pnt * 18 + d * 9 + j] - _j2prev[pnt * 18 + d * 9 + j]) / _dt * _qdS[j];
                    }
                    c.Bp[2 * pnt + d] = bp;
                }
                c.Pts[2 * pnt] = sy * _wb.PointPos[3 * pnt] + cy * _wb.PointPos[3 * pnt + 2]; c.Pts[2 * pnt + 1] = _wb.PointPos[3 * pnt + 1] - groundY;
            }
            Array.Copy(_j2, _j2prev, _j2.Length); _havePrev = true;
            if (_lat) LateralInputs(sy, cy, groundY, yaw);
            c.Com[0] = sy * _wb.ComTotal[0] + cy * _wb.ComTotal[2]; c.Com[1] = _wb.ComTotal[1] - groundY;
            double p0 = 0, p1 = 0, p2 = 0, l3 = 0, l5 = 0; for (int s = 0; s < NS; s++) { double v = _qdS[s]; p0 += _wb.AG[s] * v; p1 += _wb.AG[NS + s] * v; p2 += _wb.AG[2 * NS + s] * v; l3 += _wb.AG[3 * NS + s] * v; l5 += _wb.AG[5 * NS + s] * v; }
            double mt = _wb.TotalMass; c.ComD[0] = (sy * p0 + cy * p2) / mt; c.ComD[1] = p1 / mt; c.AngMom = -(cy * l3 - sy * l5);
            for (int leg = 0; leg < 2; leg++)
            {
                _b.GetFoot(_foot[leg], out var fs); int h = 2 * leg, tt = h + 1; double n = fs.Contact || fs.NormalForceN > 0 ? fs.NormalForceN : 0.0, ft = 0;
                if (n > 0)
                {
                    double hx = _wb.PointPos[3 * h], hy = _wb.PointPos[3 * h + 1], hz = _wb.PointPos[3 * h + 2], dx = _wb.PointPos[3 * tt] - hx, dy = _wb.PointPos[3 * tt + 1] - hy, dz = _wb.PointPos[3 * tt + 2] - hz;
                    double len2 = dx * dx + dy * dy + dz * dz; double s = len2 > 0 ? ((fs.Cop.X - hx) * dx + (fs.Cop.Y - hy) * dy + (fs.Cop.Z - hz) * dz) / len2 : 0.5;
                    ft = n * Math.Min(1.0, Math.Max(0.0, s));
                }
                c.FnMeas[tt] = ft; c.FnMeas[h] = n - ft;
            }
            c.HasFnMeas = _n > 0;
            c.Step(t); _n++;
            for (int j = 0; j < 6; j++) _b.SetDofFeedForward(_dof[j], c.Tau[j]);
            if (_lat)
            {
                var fw = new Vector3((float)sy, 0f, (float)cy); var rt = new Vector3((float)cy, 0f, (float)-sy);
                for (int k = 0; k < 8; k++) ForceWorld[k] = fw * (float)c.Force3[3 * k] + rt * (float)c.Force3[3 * k + 1] + Vector3.UnitY * (float)c.Force3[3 * k + 2];
            }
            return c.LastStatus == QpStatus.Optimal;
        }

        /// <summary>Stage 2 inputs: sole corners (heel / toe point +- half the sole width along the foot's local x), lateral CoM state, angular momentum about the CoM (link states: sum m (c_i - c) x v_i + R I R' w) and yaw inertia, heading error.</summary>
        void LateralInputs(double sy, double cy, double groundY, double yaw)
        {
            var c = _ctl; var fw = new Vector3((float)sy, 0f, (float)cy); var rt = new Vector3((float)cy, 0f, (float)-sy);
            double yref = 0;
            for (int leg = 0; leg < 2; leg++)
            {
                _b.GetFoot(_foot[leg], out var fs); _b.GetLink(fs.Link, out var ls);
                for (int k = 0; k < 4; k++)
                {
                    int corner = 4 * leg + k; double z = ((k >> 1) == 0 ? _heelZ : _toeZ) - _ankleZ, x = (k & 1) == 0 ? -_halfW : _halfW;
                    var pw = ls.Position + Vector3.Transform(new Vector3((float)x, (float)-_ankleH, (float)z), ls.Rotation); CornerWorld[corner] = pw;
                    c.Corner[3 * corner] = Vector3.Dot(pw, fw); c.Corner[3 * corner + 1] = Vector3.Dot(pw, rt); c.Corner[3 * corner + 2] = pw.Y - groundY; yref += c.Corner[3 * corner + 1] / 8.0;
                }
            }
            double mt = _wb.TotalMass; var com = new Vector3((float)_wb.ComTotal[0], (float)_wb.ComTotal[1], (float)_wb.ComTotal[2]);
            double vx = 0, vy = 0, vz = 0, lx = 0, ly = 0, lz = 0, izz = 0, lyRest = 0, izzTrunk = 0;
            for (int i = 0; i < _links; i++)
            {
                _b.GetLink(i, out var ls); double m = _wb.Mass[i]; double dx = ls.Com.X - com.X, dy = ls.Com.Y - com.Y, dz = ls.Com.Z - com.Z, ux = ls.Velocity.X, uy = ls.Velocity.Y, uz = ls.Velocity.Z;
                vx += m * ux; vy += m * uy; vz += m * uz;
                lx += m * (dy * uz - dz * uy); ly += m * (dz * ux - dx * uz); lz += m * (dx * uy - dy * ux); izz += m * (dx * dx + dz * dz);
                if (_rest[i]) lyRest += m * (dz * ux - dx * uz); else izzTrunk += m * (dx * dx + dz * dz);
                int o9 = 9 * i;
                for (int k = 0; k < 3; k++)
                {
                    double ex = _wb.Rot[o9 + k], ey = _wb.Rot[o9 + 3 + k], ez = _wb.Rot[o9 + 6 + k], ik = _wb.InertiaDiag[3 * i + k], wk = ex * ls.AngularVelocity.X + ey * ls.AngularVelocity.Y + ez * ls.AngularVelocity.Z;
                    lx += ik * wk * ex; ly += ik * wk * ey; lz += ik * wk * ez; izz += ik * ey * ey;
                    if (_rest[i]) lyRest += ik * wk * ey; else izzTrunk += ik * ey * ey;
                }
            }
            if (c.TrunkYaw)
            {
                // step 9.5e: rate of the legs' and arms' vertical angular momentum about the CoM (finite difference, low-pass); the ground moment must carry it for the trunk to hold its heading
                if (!double.IsNaN(_lRestPrev)) { double raw = (lyRest - _lRestPrev) / _dt; if (_ffMax > 0) { double mx = _ffMax * mt * _b.Tuning.gravity_mps2 * _ctl.Reference.LegLength; raw = Math.Max(-mx, Math.Min(mx, raw)); } _dlRest += _ffAlpha * (raw - _dlRest); }
                _lRestPrev = lyRest; c.DLRest = _dlRest; c.LTrunk = ly - lyRest; c.ITrunk = izzTrunk;
            }
            c.ComY = Vector3.Dot(com, rt); c.ComDY = (vx * rt.X + vz * rt.Z) / mt; c.LRoll = lx * fw.X + lz * fw.Z; c.LYaw = ly; c.Izz = izz; c.YRef = yref;
            double e = yaw - DesiredYaw; while (e > Math.PI) e -= 2 * Math.PI; while (e < -Math.PI) e += 2 * Math.PI; c.YawErr = e;
        }
    }
}
