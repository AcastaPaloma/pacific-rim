// TEMPORARY (T-030c): kinematic stand-in for the walking mech until T-005's walk lands. No physics, no gait, no feet.
// It obeys the same limits the real command limiter will (accel, decel, yaw rate, yaw accel) so a co-op or scene test
// feels roughly right; delete this file and the module when T-005 passes its walk gate.
using System;
using Game.Cameras;
using Game.Control.Contracts;
using Game.Core;
using Game.Units.Mech.Contracts;

namespace Game.Units.Placeholder
{
    /// <summary>The motion limits of a placeholder body (SI units, radians). Stand-ins until walk_tuning@v1 is a data file.</summary>
    public struct PlaceholderLimits
    {
        public float MaxSpeed;          // m/s at effort 1
        public float Accel;             // m/s^2 speeding up
        public float Decel;             // m/s^2 normal braking (Stop, arrival)
        public float EmergencyDecel;    // m/s^2 EmergencyStop
        public float MaxYawRate;        // rad/s
        public float YawAccel;          // rad/s^2
        public float TurnFirstRad;      // heading error above which the body turns before it walks
        public float ArriveRadius;      // m: a Walk target closer than this counts as reached
        public float HeightM;           // m, reported to the camera

        /// <summary>Round numbers for an ~80 m mech, used when no tuning is supplied.</summary>
        public static PlaceholderLimits Default() => new PlaceholderLimits
        {
            MaxSpeed = 6f, Accel = 0.6f, Decel = 1.2f, EmergencyDecel = 3f,
            MaxYawRate = 0.10f, YawAccel = 0.05f, TurnFirstRad = 0.5236f, ArriveRadius = 1f, HeightM = 80f,
        };

        /// <summary>
        /// Derives the limits from the mech contract's walk tuning (the same fields the real limiter reads):
        /// g and the reference leg length give t* = sqrt(L/g) and the Froude speed scale sqrt(g L); accel and decel are in g,
        /// yaw rate and yaw accel are in degrees per t* and per t*^2. Missing (zero) fields keep <see cref="Default"/>.
        /// </summary>
        public static PlaceholderLimits FromWalkTuning(WalkTuning t, double legLengthM = 0, double gravity = 9.80665, float heightM = 80f)
        {
            var d = Default(); d.HeightM = heightM;
            if (t == null) return d;
            double L = legLengthM > 0 ? legLengthM : t.reference_leg_length_m;
            if (!(L > 0) || !(gravity > 0)) return d;
            double tstar = Math.Sqrt(L / gravity), deg = Math.PI / 180.0;
            if (t.fr_max > 0) d.MaxSpeed = (float)(t.fr_max * Math.Sqrt(gravity * L));
            if (t.accel_start_g > 0) d.Accel = (float)(t.accel_start_g * gravity);
            if (t.decel_normal_g > 0) d.Decel = (float)(t.decel_normal_g * gravity);
            if (t.decel_emergency_g > 0) d.EmergencyDecel = (float)(t.decel_emergency_g * gravity);
            if (t.yaw_rate_max_tstar_deg > 0) d.MaxYawRate = (float)(t.yaw_rate_max_tstar_deg * deg / tstar);
            if (t.yaw_accel_max_tstar_deg > 0) d.YawAccel = (float)(t.yaw_accel_max_tstar_deg * deg / (tstar * tstar));
            if (t.turn_first_deg > 0) d.TurnFirstRad = (float)(t.turn_first_deg * deg);
            return d;
        }
    }

    /// <summary>
    /// One placeholder body: pose, speed and yaw rate on the ground plane (y fixed), advanced by <see cref="Step"/> from an
    /// <see cref="IntentFrame"/>. Heading is atan2(x, z) like <see cref="LocoCommand.Heading"/>. Implements <see cref="ICameraTarget"/>.
    /// Deterministic: the same intents and dt give the same state (the host steps it; clients only interpolate snapshots).
    /// </summary>
    public sealed class PlaceholderBody : ICameraTarget
    {
        public EntityId Id { get; }
        public PlaceholderLimits Limits { get; set; }
        public PlaceholderDamageRecord Damage { get; }
        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public float Heading { get; private set; }
        public float Speed { get; private set; }
        public float YawRate { get; private set; }
        /// <summary>Why the last step's command differed from the request (Accel, Decel, YawRate, YawAccel, TurnBeforeWalk, Arrived, EffortRange).</summary>
        public LimitCause Causes { get; private set; }
        public bool HasArrived { get; private set; }

        public PlaceholderBody(EntityId id, PlaceholderLimits limits, float maxHitPoints = 1000f)
        {
            Id = id; Limits = limits; Damage = new PlaceholderDamageRecord(id, maxHitPoints);
        }

        public void Place(float x, float y, float z, float heading)
        {
            X = x; Y = y; Z = z; Heading = Wrap(heading); Speed = 0; YawRate = 0; HasArrived = false;
        }

        public float VelX => Speed * (float)Math.Sin(Heading);
        public float VelZ => Speed * (float)Math.Cos(Heading);

        public bool TryGetTarget(out CameraVec3 groundPosition, out float heightMetres)
        {
            groundPosition = new CameraVec3(X, Y, Z); heightMetres = Limits.HeightM; return true;
        }

        /// <summary>Advances by <paramref name="dt"/> seconds under <paramref name="intent"/>. A destroyed body stops and ignores intents.</summary>
        public void Step(in IntentFrame intent, float dt)
        {
            if (!(dt > 0f)) return;
            var L = Limits;
            var causes = LimitCause.None;
            float wantSpeed = 0f, wantHeading = Heading, brake = L.Decel;
            bool faceOnly = false;
            var move = Damage.State == PlaceholderDamageState.Destroyed ? MoveId.EmergencyStop : intent.move;
            bool walking = false;

            switch (move)
            {
                case MoveId.Walk:
                {
                    float dx = intent.target.X - X, dz = intent.target.Z - Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    float eff = intent.effort;
                    if (eff < 0f || eff > 1f || float.IsNaN(eff)) { causes |= LimitCause.EffortRange; eff = float.IsNaN(eff) ? 0f : Math.Max(0f, Math.Min(1f, eff)); }
                    if (dist <= L.ArriveRadius) { HasArrived = true; causes |= LimitCause.Arrived; break; }
                    HasArrived = false;
                    walking = true;
                    wantHeading = (float)Math.Atan2(dx, dz);
                    wantSpeed = eff * L.MaxSpeed;
                    // Stopping distance: never faster than can be braked to rest by the target.
                    float stopSpeed = (float)Math.Sqrt(2f * L.Decel * Math.Max(0f, dist - L.ArriveRadius * 0.5f));
                    if (stopSpeed < wantSpeed) { wantSpeed = stopSpeed; causes |= LimitCause.StopDistance; }
                    break;
                }
                case MoveId.Turn:
                {
                    float dx = intent.target.X - X, dz = intent.target.Z - Z;
                    if (dx * dx + dz * dz > 1e-6f) wantHeading = (float)Math.Atan2(dx, dz);
                    faceOnly = true; HasArrived = false;
                    break;
                }
                case MoveId.EmergencyStop: brake = L.EmergencyDecel; HasArrived = false; break;
                default: HasArrived = false; break; // Stand, Stop, Stomp (no foot to lift): come to rest
            }

            // Yaw: bang-bang on the remaining angle, limited by yaw rate and yaw acceleration.
            float err = Wrap(wantHeading - Heading);
            float absErr = Math.Abs(err);
            float desiredRate = 0f;
            if (walking || faceOnly)
            {
                desiredRate = (float)Math.Min(L.MaxYawRate, Math.Sqrt(2f * L.YawAccel * absErr));
                if (desiredRate >= L.MaxYawRate && absErr > 0f) causes |= LimitCause.YawRate;
                desiredRate *= Math.Sign(err);
            }
            // Don't overshoot: if the rate we'd take this step passes the target, land on it.
            float dRate = desiredRate - YawRate, maxDRate = L.YawAccel * dt;
            if (Math.Abs(dRate) > maxDRate) { dRate = Math.Sign(dRate) * maxDRate; causes |= LimitCause.YawAccel; }
            YawRate += dRate;
            float dYaw = YawRate * dt;
            if ((walking || faceOnly) && absErr > 0f && Math.Abs(dYaw) >= absErr && Math.Sign(dYaw) == Math.Sign(err))
            { Heading = Wrap(wantHeading); YawRate = 0f; }
            else Heading = Wrap(Heading + dYaw);

            // Turn before walk: stay slow until roughly aligned.
            if (walking && Math.Abs(Wrap(wantHeading - Heading)) > L.TurnFirstRad) { wantSpeed = 0f; causes |= LimitCause.TurnBeforeWalk; }

            // Speed: accel up, decel down.
            float dv = wantSpeed - Speed;
            if (dv > 0f) { float m = L.Accel * dt; if (dv > m) { dv = m; causes |= LimitCause.Accel; } }
            else if (dv < 0f) { float m = brake * dt; if (-dv > m) { dv = -m; causes |= LimitCause.Decel; } }
            Speed = Math.Max(0f, Speed + dv);

            X += Speed * (float)Math.Sin(Heading) * dt;
            Z += Speed * (float)Math.Cos(Heading) * dt;
            Causes = causes;
        }

        public static float Wrap(float a)
        {
            const float TwoPi = (float)(Math.PI * 2.0);
            a %= TwoPi;
            if (a > Math.PI) a -= TwoPi; else if (a < -Math.PI) a += TwoPi;
            return a;
        }
    }
}
