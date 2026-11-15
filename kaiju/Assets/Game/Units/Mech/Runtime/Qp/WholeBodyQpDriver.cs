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
    /// ASSUMED until the 9.5 probes settle them (WholeBodyProbeTests): base column order [lin x, y, z, ang x, y, z] in the world frame about the root COM (<see cref="BuildSColumns"/>), Jacobian rows = link index * 6
    /// (<see cref="LinkRowIndex"/>), Jacobian reference point = link COM, bias sign as the engine reports (counteracting forces = +h). Heading yaw is applied to the base columns and the point-Jacobian rows.
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
        readonly double _heelZ, _toeZ, _ankleZ, _ankleH;
        public readonly int[] LinkRowIndex;                       // row block (6 rows) of each link in the Jacobian; default = link index
        public bool JacobianRefIsCom = true;
        public SagittalQpController Controller => _ctl;
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
            LinkRowIndex = new int[_links]; for (int i = 0; i < _links; i++) { LinkRowIndex[i] = i; _wb.LinkRow[i] = 6 * i; }
            for (int s = 0; s < NS; s++) _wb.SagCols[s] = s;
            int root = 0; for (int i = 0; i < _links; i++) if (body.LinkParent(i) < 0) { root = i; break; }
            _root = root;
            var sole = f.foot_sole; _heelZ = sole.heel_z_m; _toeZ = sole.toe_z_m; _ankleZ = sole.ankle_z_m; _ankleH = sole.ankle_height_m;
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
        public void Reset() { _ctl.Init(); _havePrev = false; _n = 0; }

        void Allocate()
        {
            _cols = _b.ModelColumns; _rows = _links * 6;
            _jac = new double[_rows * _cols]; _jacS = new double[_rows * NS]; _bias = new double[_cols];
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
                if (JacobianRefIsCom) { _wb.RefPos[o3] = ls.Com.X; _wb.RefPos[o3 + 1] = ls.Com.Y; _wb.RefPos[o3 + 2] = ls.Com.Z; } else { _wb.RefPos[o3] = ls.Position.X; _wb.RefPos[o3 + 1] = ls.Position.Y; _wb.RefPos[o3 + 2] = ls.Position.Z; }
                var ex = Vector3.Transform(Vector3.UnitX, ls.Rotation); var ey = Vector3.Transform(Vector3.UnitY, ls.Rotation); var ez = Vector3.Transform(Vector3.UnitZ, ls.Rotation);
                _wb.Rot[o9] = ex.X; _wb.Rot[o9 + 3] = ex.Y; _wb.Rot[o9 + 6] = ex.Z; _wb.Rot[o9 + 1] = ey.X; _wb.Rot[o9 + 4] = ey.Y; _wb.Rot[o9 + 7] = ey.Z; _wb.Rot[o9 + 2] = ez.X; _wb.Rot[o9 + 5] = ez.Y; _wb.Rot[o9 + 8] = ez.Z;
                _wb.LinkRow[i] = 6 * LinkRowIndex[i];
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
            return c.LastStatus == QpStatus.Optimal;
        }
    }
}
