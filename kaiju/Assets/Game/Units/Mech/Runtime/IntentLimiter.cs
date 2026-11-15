using System;
using System.Numerics;
using Game.Control.Contracts;
using Game.Units.Mech.Contracts;

namespace Game.Units.Mech
{
    /// <summary>Physical limits of one mech, derived from its frame and walk_tuning@v1 (R-001 sections 1-4). Nothing here is specific to 80 m or 10,000 t.</summary>
    public sealed class MechLimits
    {
        public readonly double G, LegLength, TStar, VMax, VCruise, AccelStart, DecelNormal, DecelEmergency, LateralAccel, YawRateMax, YawAccelMax, TurnFirst, StopSpeed, YawRateWalking;
        public readonly WalkTuning Tuning;

        public MechLimits(WalkTuning t, double legLength, double g)
        {
            Tuning = t; G = g; LegLength = legLength; TStar = Math.Sqrt(legLength / g);
            VMax = Math.Sqrt(t.fr_max * g * legLength); VCruise = Math.Sqrt(t.fr_cruise * g * legLength);
            AccelStart = t.accel_start_g * g; DecelNormal = t.decel_normal_g * g; DecelEmergency = t.decel_emergency_g * g; LateralAccel = t.lateral_accel_g * g;
            YawRateMax = t.yaw_rate_max_tstar_deg * Math.PI / 180 / TStar; YawRateWalking = t.yaw_rate_walking_tstar_deg * Math.PI / 180 / TStar; YawAccelMax = t.yaw_accel_max_tstar_deg * Math.PI / 180 / (TStar * TStar);
            TurnFirst = t.turn_first_deg * Math.PI / 180; StopSpeed = Math.Sqrt(t.stop_speed_fr * g * legLength);
        }
        public double StepTime(double speed) => Lipm.StepTime(speed, LegLength, G, Tuning.stride_k, Tuning.stride_exp, Tuning.step_time_max_tstar * TStar);
        /// <summary>Effort that asks for a given Froude number: v = effort x v_max.</summary>
        public double EffortForFroude(double fr) => Math.Sqrt(fr / Tuning.fr_max);
    }

    /// <summary>
    /// F-002 / D-013 "within mass and balance limits": turns the intent as received into the command the mech can physically pursue (R-005 section 5.2 step 1).
    /// Every difference is reported as a <see cref="LimitCause"/>; nothing is dropped silently and nothing is obeyed beyond the limits.
    /// </summary>
    public sealed class IntentLimiter
    {
        readonly MechLimits _l;
        readonly double _dt;
        double _speed, _heading, _yawRate;
        bool _init;

        public IntentLimiter(MechLimits limits, double dt) { _l = limits; _dt = dt; }

        public LocoCommand Update(in IntentFrame intent, Vector2 position, float bodyHeading, double actualSpeed, double reachSpeed = double.MaxValue)
        {
            if (!_init) { _heading = bodyHeading; _init = true; }
            var causes = LimitCause.None;
            double effort = intent.effort;
            if (double.IsNaN(effort)) { effort = 0; causes |= LimitCause.EffortRange; }
            if (effort > 1) { effort = 1; causes |= LimitCause.EffortRange; }
            if (effort < 0) { effort = 0; causes |= LimitCause.EffortRange; }

            Vector2 to = new Vector2(intent.target.X, intent.target.Z) - position;
            double dist = to.Length();
            bool hasTarget = !float.IsNaN(to.X + to.Y) && dist > 1e-3;
            double wantHeading = _heading, wantSpeed = 0, decel = _l.DecelNormal;
            double requestedSpeed = 0;
            double arrive = _l.Tuning.arrive_fraction_of_step * _l.StepTime(_l.VCruise) * _l.VCruise;
            switch (intent.move)
            {
                case MoveId.Walk:
                    requestedSpeed = intent.effort * _l.VMax;
                    if (!hasTarget || dist < arrive) { causes |= LimitCause.Arrived; break; }
                    wantHeading = Math.Atan2(to.X, to.Y);
                    wantSpeed = effort * _l.VMax;
                    // stop on the target: v^2 / (2 a) must not exceed the distance left (R-005 5.2 step 1)
                    double vStop = Math.Sqrt(2 * _l.DecelNormal * Math.Max(0, dist - arrive));
                    if (vStop < wantSpeed) { wantSpeed = vStop; causes |= LimitCause.StopDistance; }
                    break;
                case MoveId.Turn:
                    if (hasTarget) wantHeading = Math.Atan2(to.X, to.Y);
                    break;
                case MoveId.EmergencyStop: decel = _l.DecelEmergency; break;
                default: break; // Stand, Stop, Stomp: come to rest
            }
            // the legs can only cover so much ground per step: the speed the step reach allows caps the command
            if (wantSpeed > reachSpeed) { wantSpeed = reachSpeed; causes |= LimitCause.StepReach; }
            double headingError = Wrap(wantHeading - _heading);
            // a large heading change is made by slowing, turning on the spot, then walking (never a spin at speed)
            if (wantSpeed > 0 && Math.Abs(headingError) > _l.TurnFirst && (_speed < _l.Tuning.turn_first_speed_frac * _l.VCruise || Math.Abs(headingError) > _l.Tuning.turn_stop_factor * _l.TurnFirst)) { wantSpeed = 0; causes |= LimitCause.TurnBeforeWalk; }

            // speed: rate limited both ways
            double dv = wantSpeed - _speed;
            if (dv > _l.AccelStart * _dt) { dv = _l.AccelStart * _dt; causes |= LimitCause.Accel; }
            else if (dv < -decel * _dt) { dv = -decel * _dt; if (intent.move != MoveId.Stop && intent.move != MoveId.EmergencyStop && intent.move != MoveId.Stand) causes |= LimitCause.Decel; }
            _speed = Math.Max(0, _speed + dv);

            // heading: yaw rate limited by the absolute limit and by lateral acceleration at speed, yaw acceleration limited
            double rateMax = _l.YawRateMax;
            double v = Math.Max(_speed, actualSpeed);
            if (v > 1e-3 && _l.LateralAccel / v < rateMax) rateMax = _l.LateralAccel / v;
            if (_speed > _l.Tuning.walking_speed_frac * _l.VCruise && _l.YawRateWalking < rateMax) rateMax = _l.YawRateWalking;
            // approach the heading without overshoot: rate <= sqrt(2 a |error|)
            double wantRate = Math.Sign(headingError) * Math.Min(Math.Abs(headingError) / _dt, Math.Sqrt(2 * _l.Tuning.yaw_approach_factor * _l.YawAccelMax * Math.Abs(headingError)));
            if (Math.Abs(wantRate) > rateMax) { wantRate = Math.Sign(wantRate) * rateMax; causes |= v > 1e-3 && _l.LateralAccel / v < _l.YawRateMax ? LimitCause.LateralAccel : LimitCause.YawRate; }
            double dr = wantRate - _yawRate, drMax = _l.YawAccelMax * _dt;
            if (Math.Abs(dr) > drMax) { dr = Math.Sign(dr) * drMax; if (Math.Abs(headingError) > _l.Tuning.yaw_report_threshold_rad) causes |= LimitCause.YawAccel; }
            _yawRate += dr;
            _heading = Wrap(_heading + _yawRate * _dt);

            return new LocoCommand { Move = intent.move, Speed = (float)_speed, Heading = (float)_heading, YawRate = (float)_yawRate, RequestedSpeed = (float)requestedSpeed, RequestedHeading = (float)wantHeading, Causes = causes };
        }

        /// <summary>A fall ends every command: the limiter restarts from rest.</summary>
        public void Reset(float heading) { _speed = 0; _yawRate = 0; _heading = heading; }
        public static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a <= -Math.PI) a += 2 * Math.PI; return a; }
    }
}
