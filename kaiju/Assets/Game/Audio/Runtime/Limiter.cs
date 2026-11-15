using System;
namespace Game.Audio
{
    /// <summary>
    /// Deterministic brick-wall limiter: instant attack (the gain applied to a sample never lets |y| exceed the ceiling), exponential release of the gain towards 1.
    /// No look-ahead, no RNG, state is only the gain, so equal inputs give bit-identical outputs. Output is also clamped, so |y| less than or equal to ceiling always holds.
    /// </summary>
    public sealed class Limiter
    {
        readonly float _ceiling; readonly float _releaseCoef;
        float _gain = 1f;
        public float CurrentGain => _gain;
        public Limiter(double ceiling, double releaseS, double sampleRate)
        {
            if (!(ceiling > 0 && ceiling <= 1.0)) throw new ArgumentOutOfRangeException(nameof(ceiling));
            _ceiling = (float)ceiling;
            _releaseCoef = (float)Math.Exp(-1.0 / (Math.Max(releaseS, 1e-4) * sampleRate));
        }
        public float Process(float x)
        {
            float ax = Math.Abs(x);
            float need = ax > _ceiling ? _ceiling / ax : 1f;
            if (need < _gain) _gain = need;
            else _gain = 1f - (1f - _gain) * _releaseCoef; // exponential recovery to unity
            float y = x * _gain;
            if (y > _ceiling) y = _ceiling; else if (y < -_ceiling) y = -_ceiling;
            return y;
        }
        /// <summary>Stereo-linked: one gain from the louder channel so the image does not shift.</summary>
        public void ProcessStereo(float[] l, float[] r, int n)
        {
            for (int i = 0; i < n; i++)
            {
                float ax = Math.Max(Math.Abs(l[i]), Math.Abs(r[i]));
                float need = ax > _ceiling ? _ceiling / ax : 1f;
                if (need < _gain) _gain = need; else _gain = 1f - (1f - _gain) * _releaseCoef;
                l[i] = Clamp(l[i] * _gain); r[i] = Clamp(r[i] * _gain);
            }
        }
        float Clamp(float y) => y > _ceiling ? _ceiling : (y < -_ceiling ? -_ceiling : y);
        public void Reset() { _gain = 1f; }
    }
}
