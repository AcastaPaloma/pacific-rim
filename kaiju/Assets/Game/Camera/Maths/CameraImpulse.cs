using System;

namespace Game.Cameras
{
    /// <summary>Envelope of the footfall camera impulse (tuning values from Config/Camera/ThirdPersonRig.json; Assumed, feel panel).</summary>
    public struct ImpulseSettings
    {
        public float MetresPerMegaNs;   // vertical displacement per 1e6 N·s at the reference distance
        public float DegreesPerMegaNs;  // pitch kick per 1e6 N·s at the reference distance
        public float MaxMetres;         // cap on the total displacement
        public float MaxDegrees;        // cap on the total pitch kick
        public float DecaySeconds;      // time for the envelope to fall to 5%
        public float FrequencyHz;       // oscillation frequency
        public float FalloffDistance;   // m: beyond this distance the amplitude falls as distance^-1
    }

    /// <summary>
    /// The camera's response to impulse events: each event starts a decaying sine in vertical position and pitch,
    /// proportional to the event's magnitude, capped, and exactly zero once it has decayed. Deterministic: the same
    /// events at the same times give the same motion. No allocation after construction.
    /// </summary>
    public sealed class CameraImpulse
    {
        const int Capacity = 8;
        const double EndFraction = 1e-3;   // an impulse is dropped once its envelope is below this fraction
        readonly double[] m_Start = new double[Capacity];
        readonly double[] m_Metres = new double[Capacity];
        readonly double[] m_Degrees = new double[Capacity];
        int m_Count;
        double m_Time;
        ImpulseSettings m_Settings;

        public CameraImpulse(ImpulseSettings settings) { m_Settings = settings; }
        public ImpulseSettings Settings { get => m_Settings; set => m_Settings = value; }
        public double Time => m_Time;
        public int ActiveCount => m_Count;
        /// <summary>Current vertical offset (m, positive up; a footfall drops the camera first).</summary>
        public float OffsetMetres { get; private set; }
        /// <summary>Current pitch offset (degrees, positive up).</summary>
        public float OffsetDegrees { get; private set; }
        /// <summary>Seconds after which a single impulse has fully ended.</summary>
        public double Lifetime => m_Settings.DecaySeconds * Math.Log(1.0 / EndFraction) / Math.Log(20.0);

        public void Reset() { m_Count = 0; m_Time = 0; OffsetMetres = 0; OffsetDegrees = 0; }

        /// <summary>Adds an impulse of <paramref name="impulseNs"/> newton-seconds at <paramref name="distance"/> metres from the camera.</summary>
        public void Add(float impulseNs, float distance)
        {
            if (!(impulseNs > 0) || float.IsInfinity(impulseNs)) return;
            double falloff = m_Settings.FalloffDistance > 0 && distance > m_Settings.FalloffDistance ? m_Settings.FalloffDistance / distance : 1.0;
            double mega = impulseNs * 1e-6 * falloff;
            double metres = Math.Min(m_Settings.MaxMetres, mega * m_Settings.MetresPerMegaNs);
            double degrees = Math.Min(m_Settings.MaxDegrees, mega * m_Settings.DegreesPerMegaNs);
            int slot = m_Count < Capacity ? m_Count++ : OldestSlot();
            m_Start[slot] = m_Time; m_Metres[slot] = metres; m_Degrees[slot] = degrees;
        }

        int OldestSlot()
        {
            int oldest = 0;
            for (int i = 1; i < m_Count; i++) if (m_Start[i] < m_Start[oldest]) oldest = i;
            return oldest;
        }

        /// <summary>Advances time by <paramref name="dt"/> seconds and evaluates the offsets (closed form, step-size independent).</summary>
        public void Advance(double dt)
        {
            m_Time += Math.Max(0, dt);
            double metres = 0, degrees = 0, lifetime = Lifetime;
            double rate = Math.Log(20.0) / Math.Max(1e-6, m_Settings.DecaySeconds), omega = 2.0 * Math.PI * m_Settings.FrequencyHz;
            for (int i = m_Count - 1; i >= 0; i--)
            {
                double age = m_Time - m_Start[i];
                if (age >= lifetime)
                {
                    m_Count--; m_Start[i] = m_Start[m_Count]; m_Metres[i] = m_Metres[m_Count]; m_Degrees[i] = m_Degrees[m_Count];
                    continue;
                }
                double wave = Math.Exp(-rate * age) * Math.Sin(omega * age);
                metres -= m_Metres[i] * wave;     // down first
                degrees += m_Degrees[i] * wave;
            }
            OffsetMetres = (float)Math.Max(-m_Settings.MaxMetres, Math.Min(m_Settings.MaxMetres, metres));
            OffsetDegrees = (float)Math.Max(-m_Settings.MaxDegrees, Math.Min(m_Settings.MaxDegrees, degrees));
        }
    }

    /// <summary>Engine-free follow maths of the third-person rig.</summary>
    public static class RigFraming
    {
        /// <summary>Exponential approach with time constant <paramref name="lagSeconds"/> (frame-rate independent).</summary>
        public static double Approach(double current, double target, double lagSeconds, double dt)
        {
            if (lagSeconds <= 1e-6 || dt <= 0) return dt <= 0 ? current : target;
            return target + (current - target) * Math.Exp(-dt / lagSeconds);
        }

        /// <summary>Camera pose that frames a point from an orbit: yaw/pitch (degrees, pitch positive = camera looks up) at a distance.</summary>
        public static CameraPose Orbit(CameraVec3 focus, float yawDeg, float pitchDeg, float distance)
        {
            CameraStabiliser.Basis(yawDeg * Math.PI / 180.0, pitchDeg * Math.PI / 180.0, out _, out _, out var forward);
            return new CameraPose
            {
                Position = new CameraVec3((float)(focus.X - forward[0] * distance), (float)(focus.Y - forward[1] * distance), (float)(focus.Z - forward[2] * distance)),
                YawDeg = yawDeg, PitchDeg = pitchDeg,
            };
        }
    }
}
