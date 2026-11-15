using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>One remote body as the client should draw it (world space, full precision).</summary>
    public struct InterpolatedBody
    {
        public EntityId Id;
        public double PosX, PosY, PosZ;
        public double VelX, VelY, VelZ;
        /// <summary>Radians, wrapped to (-pi, pi].</summary>
        public double Yaw;
        /// <summary>True while this body is frozen past the extrapolation limit.</summary>
        public bool Stale;
    }

    /// <summary>
    /// Client-side snapshot interpolation (R-023 section 3). No wall clock: all time is in Core steps (fractional
    /// for render time). Snapshots are keyed by host step. Render time = client step + clock offset - buffer, where
    /// the offset is the best (largest) host-minus-arrival step difference seen in a recent window, slewed by at
    /// most 1% of elapsed time (no steps in the render clock), and the buffer is a fixed number of snapshot intervals.
    /// Between snapshots: cubic Hermite from position and velocity. Past the newest snapshot: extrapolate along the
    /// last velocity for at most <c>maxExtrapolationSeconds</c>, then freeze and flag stale. When new data changes
    /// where a body should be, the change is moved into a per-body offset that decays critically damped, so the drawn
    /// path never jumps; only the target moves.
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        private struct Target
        {
            public EntityId Id;
            public double Px, Py, Pz, Vx, Vy, Vz, Yaw, YawRate;
            public bool Stale;
        }

        // Critically damped offset: e(t) = (e0 + (v0 + w e0) t) exp(-w t), exact for any dt.
        private struct Corr { public double X, Y, Z, Yaw, VX, VY, VZ, VYaw; }

        public const int ClockWindow = 64;
        public const double MaxClockSlew = 0.01;

        private readonly double _stepSeconds;
        private readonly double _bufferSteps;
        private readonly double _maxExtrapSteps;
        private readonly double _omega;

        private readonly List<StateSnapshot> _buf = new List<StateSnapshot>();
        private readonly Dictionary<ulong, Corr> _corr = new Dictionary<ulong, Corr>();
        private readonly long[] _offsets = new long[ClockWindow];
        private int _offsetCount, _offsetHead;
        private double _offsetCur;
        private bool _clockInit;
        private double _lastClient, _lastRender;
        private bool _hasRender;
        private readonly List<Target> _tmpA = new List<Target>(), _tmpB = new List<Target>(), _tmpC = new List<Target>();
        private readonly List<InterpolatedBody> _bodies = new List<InterpolatedBody>();

        public SnapshotInterpolator(double stepSeconds, int snapshotIntervalSteps = 2, int bufferIntervals = 3,
            double maxExtrapolationSeconds = 0.25, double correctionOmega = 12.0)
        {
            if (stepSeconds <= 0 || snapshotIntervalSteps < 1 || bufferIntervals < 1 || maxExtrapolationSeconds < 0 || correctionOmega <= 0)
                throw new ArgumentOutOfRangeException();
            _stepSeconds = stepSeconds;
            _bufferSteps = (double)snapshotIntervalSteps * bufferIntervals;
            _maxExtrapSteps = maxExtrapolationSeconds / stepSeconds;
            _omega = correctionOmega;
        }

        /// <summary>Host step being drawn at the last <see cref="Update"/> (fractional).</summary>
        public double RenderStep => _lastRender;
        /// <summary>True when any body is currently frozen past the extrapolation limit.</summary>
        public bool IsStale { get; private set; }
        public int LateDropped { get; private set; }
        public IReadOnlyList<InterpolatedBody> Bodies => _bodies;

        /// <summary>Feeds a snapshot that arrived at client Core step <paramref name="arrivalStep"/>.</summary>
        public void Receive(long arrivalStep, StateSnapshot s)
        {
            if (s == null) return;
            foreach (var b in _buf) if (b.HostStep == s.HostStep) return; // duplicate (e.g. repeated send)

            _offsets[_offsetHead] = s.HostStep - arrivalStep;
            _offsetHead = (_offsetHead + 1) % ClockWindow;
            if (_offsetCount < ClockWindow) _offsetCount++;
            if (!_clockInit) { _offsetCur = BestOffset(); _clockInit = true; }

            if (_hasRender)
            {
                Evaluate(_lastRender, _tmpA);
                Insert(s);
                Evaluate(_lastRender, _tmpB);
                // Move any change at the already-drawn instant into the damped offset, so the drawn value stays put.
                int i = 0, j = 0;
                while (i < _tmpA.Count && j < _tmpB.Count)
                {
                    var a = _tmpA[i]; var b = _tmpB[j];
                    if (a.Id.Value < b.Id.Value) { i++; continue; }
                    if (a.Id.Value > b.Id.Value) { j++; continue; }
                    _corr.TryGetValue(a.Id.Value, out var c);
                    c.X += a.Px - b.Px; c.Y += a.Py - b.Py; c.Z += a.Pz - b.Pz;
                    c.Yaw += WrapPi(a.Yaw - b.Yaw);
                    _corr[a.Id.Value] = c;
                    i++; j++;
                }
            }
            else Insert(s);
        }

        /// <summary>Advances to client Core step <paramref name="clientStep"/> (fractional allowed, never backwards) and refreshes <see cref="Bodies"/>.</summary>
        public void Update(double clientStep)
        {
            _bodies.Clear();
            IsStale = false;
            if (_buf.Count == 0) return;
            double dtSteps = _hasRender ? Math.Max(0, clientStep - _lastClient) : 0;
            double want = BestOffset();
            double maxMove = MaxClockSlew * dtSteps;
            _offsetCur += Math.Max(-maxMove, Math.Min(maxMove, want - _offsetCur));
            double r = clientStep + _offsetCur - _bufferSteps;

            Evaluate(r, _tmpC);
            double dt = dtSteps * _stepSeconds;
            double decay = Math.Exp(-_omega * dt);
            var seen = new HashSet<ulong>();
            foreach (var t in _tmpC)
            {
                seen.Add(t.Id.Value);
                _corr.TryGetValue(t.Id.Value, out var c);
                if (dt > 0) c = Decay(c, dt, decay);
                _corr[t.Id.Value] = c;
                _bodies.Add(new InterpolatedBody
                {
                    Id = t.Id,
                    PosX = t.Px + c.X, PosY = t.Py + c.Y, PosZ = t.Pz + c.Z,
                    VelX = t.Vx + c.VX, VelY = t.Vy + c.VY, VelZ = t.Vz + c.VZ,
                    Yaw = WrapPi(t.Yaw + c.Yaw),
                    Stale = t.Stale,
                });
                if (t.Stale) IsStale = true;
            }
            if (_corr.Count > seen.Count)
            {
                var gone = new List<ulong>();
                foreach (var k in _corr.Keys) if (!seen.Contains(k)) gone.Add(k);
                foreach (var k in gone) _corr.Remove(k);
            }
            _lastClient = clientStep; _lastRender = r; _hasRender = true;

            // Keep one snapshot at or before the render time as the start of the bracket.
            while (_buf.Count >= 2 && _buf[1].HostStep <= r) _buf.RemoveAt(0);
        }

        private Corr Decay(Corr c, double dt, double decay)
        {
            double w = _omega;
            Corr o;
            o.X = (c.X + (c.VX + w * c.X) * dt) * decay; o.VX = (c.VX - w * (c.VX + w * c.X) * dt) * decay;
            o.Y = (c.Y + (c.VY + w * c.Y) * dt) * decay; o.VY = (c.VY - w * (c.VY + w * c.Y) * dt) * decay;
            o.Z = (c.Z + (c.VZ + w * c.Z) * dt) * decay; o.VZ = (c.VZ - w * (c.VZ + w * c.Z) * dt) * decay;
            o.Yaw = (c.Yaw + (c.VYaw + w * c.Yaw) * dt) * decay; o.VYaw = (c.VYaw - w * (c.VYaw + w * c.Yaw) * dt) * decay;
            return o;
        }

        private double BestOffset()
        {
            long best = long.MinValue;
            for (int i = 0; i < _offsetCount; i++) if (_offsets[i] > best) best = _offsets[i];
            return best;
        }

        private void Insert(StateSnapshot s)
        {
            int i = _buf.Count;
            while (i > 0 && _buf[i - 1].HostStep > s.HostStep) i--;
            if (i == 0 && _buf.Count > 0 && _hasRender && _buf[0].HostStep <= _lastRender)
            { LateDropped++; return; } // older than the bracket start, never needed again
            _buf.Insert(i, s);
        }

        private static double WrapPi(double a)
        {
            a = Math.IEEERemainder(a, 2 * Math.PI);
            return a <= -Math.PI ? a + 2 * Math.PI : a;
        }

        private void Evaluate(double r, List<Target> output)
        {
            output.Clear();
            int n = _buf.Count;
            if (n == 0) return;
            int i = -1;
            for (int k = 0; k < n; k++) { if (_buf[k].HostStep <= r) i = k; else break; }

            if (i < 0) { foreach (var e in _buf[0].Entities) output.Add(FromState(e, 0, false)); return; }
            if (i == n - 1)
            {
                double t = r - _buf[i].HostStep;
                bool stale = t > _maxExtrapSteps;
                double dt = Math.Min(t, _maxExtrapSteps) * _stepSeconds;
                foreach (var e in _buf[i].Entities) output.Add(FromState(e, dt, stale));
                return;
            }
            var a = _buf[i]; var b = _buf[i + 1];
            double h = (b.HostStep - a.HostStep) * _stepSeconds;
            double u = (r - a.HostStep) / (b.HostStep - a.HostStep);
            double u2 = u * u, u3 = u2 * u;
            double h00 = 2 * u3 - 3 * u2 + 1, h10 = u3 - 2 * u2 + u, h01 = -2 * u3 + 3 * u2, h11 = u3 - u2;
            double d00 = 6 * u2 - 6 * u, d10 = 3 * u2 - 4 * u + 1, d01 = -6 * u2 + 6 * u, d11 = 3 * u2 - 2 * u;
            int x = 0, y = 0;
            while (x < a.Entities.Length && y < b.Entities.Length)
            {
                var ea = a.Entities[x]; var eb = b.Entities[y];
                if (ea.Id.Value < eb.Id.Value) { x++; continue; } // gone by b
                if (ea.Id.Value > eb.Id.Value) { y++; continue; } // not yet spawned at a
                x++; y++;
                double yb = ea.Yaw + WrapPi((double)eb.Yaw - ea.Yaw);
                var t = new Target { Id = ea.Id };
                H(h, h00, h10, h01, h11, d00, d10, d01, d11, ea.PosX, ea.VelX, eb.PosX, eb.VelX, out t.Px, out t.Vx);
                H(h, h00, h10, h01, h11, d00, d10, d01, d11, ea.PosY, ea.VelY, eb.PosY, eb.VelY, out t.Py, out t.Vy);
                H(h, h00, h10, h01, h11, d00, d10, d01, d11, ea.PosZ, ea.VelZ, eb.PosZ, eb.VelZ, out t.Pz, out t.Vz);
                H(h, h00, h10, h01, h11, d00, d10, d01, d11, ea.Yaw, ea.YawRate, yb, eb.YawRate, out t.Yaw, out t.YawRate);
                output.Add(t);
            }
        }

        private static void H(double h, double h00, double h10, double h01, double h11, double d00, double d10, double d01, double d11,
            double pa, double va, double pb, double vb, out double p, out double v)
        {
            p = h00 * pa + h10 * h * va + h01 * pb + h11 * h * vb;
            v = (d00 * pa + d10 * h * va + d01 * pb + d11 * h * vb) / h;
        }

        private static Target FromState(EntityState e, double dtSeconds, bool stale) => new Target
        {
            Id = e.Id,
            Px = e.PosX + e.VelX * dtSeconds, Py = e.PosY + e.VelY * dtSeconds, Pz = e.PosZ + e.VelZ * dtSeconds,
            Vx = e.VelX, Vy = e.VelY, Vz = e.VelZ,
            Yaw = e.Yaw + e.YawRate * dtSeconds, YawRate = e.YawRate,
            Stale = stale,
        };
    }
}
