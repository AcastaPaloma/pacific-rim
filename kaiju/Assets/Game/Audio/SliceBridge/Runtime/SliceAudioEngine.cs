// T-188: engine-free mixer for the slice: AudioEvent -> AudioPlanner -> VoiceAllocator -> procedural voices -> stereo mix -> Limiter. The Unity behaviour calls Render from OnAudioFilterRead; the headless capture calls it directly.
// Thread model: Submit / SetListener (main thread) and Render (audio thread) share one lock; the lock is held only for the block render. Deterministic: no clock, no RNG outside seeded hashes.
using System;
using System.Collections.Generic;

namespace Game.Audio.SliceBridge
{
    public struct SliceEngineStats
    {
        public int Submitted, Started, RejectedByPlanner, Stolen, PeakActive, LimiterBlocks;
        public float PeakPreLimiter, PeakOut;
        public long Samples;
    }

    public sealed class SliceAudioEngine
    {
        readonly AudioProfile _p;
        readonly VoiceAllocator _alloc;
        readonly Limiter _lim;
        readonly SliceVoice[] _voices;
        readonly object _lock = new object();
        readonly int[] _startedByKind = new int[16];
        float[] _tl = new float[0], _tr = new float[0];
        Listener _listener;
        long _now;           // samples rendered
        SliceEngineStats _stats;
        public float MasterGain = 1f;

        public SliceAudioEngine(AudioProfile profile = null)
        {
            _p = profile ?? new AudioProfile();
            _alloc = new VoiceAllocator(_p.MaxVoices, _p.ReservedCriticalVoices, _p.StealHysteresisDb);
            _lim = new Limiter(_p.LimiterCeiling, _p.LimiterReleaseS, _p.SampleRate);
            _voices = new SliceVoice[_p.MaxVoices];
            for (int i = 0; i < _voices.Length; i++) _voices[i] = new SliceVoice();
        }

        public AudioProfile Profile => _p;
        public int ActiveVoices { get { lock (_lock) return _alloc.ActiveCount; } }
        public SliceEngineStats Stats { get { lock (_lock) { var s = _stats; s.Samples = _now; return s; } } }
        public int StartedOfKind(AudioEventKind k) { lock (_lock) return _startedByKind[(int)k]; }
        public void SetListener(in Listener l) { lock (_lock) _listener = l; }

        public void Submit(List<AudioEvent> events, int blockers = 0)
        {
            if (events == null) return;
            lock (_lock) for (int i = 0; i < events.Count; i++) SubmitLocked(events[i], blockers);
        }

        public void Submit(in AudioEvent ev, int blockers = 0) { lock (_lock) SubmitLocked(ev, blockers); }

        void SubmitLocked(in AudioEvent ev, int blockers)
        {
            _stats.Submitted++;
            if (!AudioPlanner.TryStart(ev, _listener, _p, _alloc, blockers, out var cmd, out long stolen)) { _stats.RejectedByPlanner++; return; }
            if (stolen >= 0) _stats.Stolen++;
            double delay = cmd.StartDelayS;
            if (ev.Kind == AudioEventKind.QuakeP || ev.Kind == AudioEventKind.QuakeS || ev.Kind == AudioEventKind.QuakeRumble)
            {   // seismic travel time through rock, not the speed of sound in air
                double dx = ev.X - _listener.X, dy = ev.Y - _listener.Y, dz = ev.Z - _listener.Z;
                delay = AudioMath.QuakeArrivalS(Math.Sqrt(dx * dx + dy * dy + dz * dz), ev.Kind == AudioEventKind.QuakeS);
            }
            var v = _voices[cmd.VoiceId];
            v.Start(cmd, _now + (long)Math.Round(delay * _p.SampleRate), _p.SampleRate, _p);
            _stats.Started++; _startedByKind[(int)ev.Kind]++;
        }

        /// <summary>Renders <paramref name="count"/> stereo frames (overwrites left/right). Output obeys |y| &lt;= profile limiter ceiling.</summary>
        public void Render(float[] left, float[] right, int count)
        {
            lock (_lock)
            {
                Array.Clear(left, 0, count); Array.Clear(right, 0, count);
                for (int i = 0; i < _voices.Length; i++)
                {
                    var v = _voices[i];
                    if (!v.Active) continue;
                    v.Mix(_now, left, right, count);
                    if (v.Finished(_now + count)) { v.Active = false; _alloc.Release(i); }
                }
                float pre = 0f;
                for (int i = 0; i < count; i++)
                {
                    left[i] *= MasterGain; right[i] *= MasterGain;
                    pre = Math.Max(pre, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
                }
                if (pre > _stats.PeakPreLimiter) _stats.PeakPreLimiter = pre;
                if (pre > _p.LimiterCeiling) _stats.LimiterBlocks++;
                _lim.ProcessStereo(left, right, count);
                for (int i = 0; i < count; i++) _stats.PeakOut = Math.Max(_stats.PeakOut, Math.Max(Math.Abs(left[i]), Math.Abs(right[i])));
                if (_alloc.ActiveCount > _stats.PeakActive) _stats.PeakActive = _alloc.ActiveCount;
                _now += count;
            }
        }

        /// <summary>Interleaved convenience for OnAudioFilterRead (adds into <paramref name="data"/>).</summary>
        public void RenderInterleavedAdd(float[] data, int channels)
        {
            int n = data.Length / channels;
            if (_tl.Length < n) { _tl = new float[n]; _tr = new float[n]; }
            Render(_tl, _tr, n);
            for (int i = 0; i < n; i++)
            {
                data[i * channels] += _tl[i];
                if (channels > 1) data[i * channels + 1] += _tr[i];
            }
        }
    }

    /// <summary>One procedural voice. Kind decides the generator; the plan decides gain, pan, low-pass, pitch (Doppler) and start delay. Reused (no allocation after construction).</summary>
    sealed class SliceVoice
    {
        public bool Active;
        AudioEventKind _kind;
        long _start, _end;
        double _sr;
        float _gl, _gr, _lpA, _lpS;
        // modal bank
        readonly Mode[] _modes = new Mode[ModalSynth.MaxModes];
        readonly double[] _s = new double[ModalSynth.MaxModes], _c = new double[ModalSynth.MaxModes], _rs = new double[ModalSynth.MaxModes], _rc = new double[ModalSynth.MaxModes],
                          _env = new double[ModalSynth.MaxModes], _dec = new double[ModalSynth.MaxModes];
        int _nm; double _norm;
        // noise
        uint _rng; double _nEnv, _nDec, _nGain, _nLp, _nLpS, _tone, _toneEnv, _toneDec, _tonePh, _toneW;
        double _burst;
        long _len;

        public bool Finished(long upTo) => upTo >= _end;


        public void Start(in VoiceCommand cmd, long startSample, double sr, AudioProfile p)
        {
            var ev = cmd.Event; _kind = ev.Kind; _sr = sr; _start = startSample;
            double dbfs = AudioMath.SplToDbfs(cmd.SplAtListenerDb, p);
            float gain = (float)AudioMath.DbfsToLinear(dbfs);
            AudioMath.PanGains(cmd.Pan, out double gl, out double gr);
            _gl = gain * (float)gl; _gr = gain * (float)gr;
            _lpA = (float)(1.0 - Math.Exp(-2.0 * Math.PI * Math.Min(Math.Max(cmd.LowpassHz, 20.0), 0.45 * sr) / sr)); _lpS = 0f;
            _rng = ev.Seed * 747796405u + 2891336453u + (uint)ev.Id; if (_rng == 0) _rng = 1;
            _nm = 0; _nGain = 0; _toneEnv = 0; _burst = 0; _nLpS = 0;
            double pitch = cmd.PitchRatio > 0 ? cmd.PitchRatio : 1.0;
            double lenS;
            switch (ev.Kind)
            {
                case AudioEventKind.FootContact:
                case AudioEventKind.Impact:
                    lenS = Modal(ev, pitch, 3.0);
                    SetNoise(ev.Kind == AudioEventKind.Impact ? 0.25 : 0.12, 0.03, 1500.0);
                    break;
                case AudioEventKind.StructureFailure:
                    lenS = Modal(ev, pitch, 3.5);
                    SetNoise(0.8, 0.5, 900.0);   // crumble
                    lenS = Math.Max(lenS, 2.0);
                    break;
                case AudioEventKind.DebrisRain:
                    lenS = 2.5; SetNoise(1.0, 0.8, 3000.0); _burst = 1.0; break;
                case AudioEventKind.MuzzleBlast:
                    lenS = 1.5; SetNoise(1.0, 0.07, 700.0); SetTone(60.0 * pitch, 0.5, 0.25); break;
                case AudioEventKind.SupersonicCrack:
                    lenS = 0.15; SetNoise(1.0, 0.012, 6000.0); break;
                case AudioEventKind.QuakeP:
                    lenS = 4.0; SetNoise(0.5, 1.6, 120.0); SetTone(19.0, 0.8, 1.8); break;
                case AudioEventKind.QuakeS:
                    lenS = 9.0; SetNoise(0.6, 3.5, 90.0); SetTone(24.0, 0.8, 4.0); break;
                case AudioEventKind.QuakeRumble:
                    lenS = 12.0; SetNoise(0.4, 5.0, 60.0); SetTone(15.0, 0.8, 6.0); break;
                default:
                    lenS = 0.6; SetNoise(1.0, 0.2, 2000.0); break;
            }
            _len = Math.Max(1L, (long)(lenS * sr));
            _end = _start + _len + 1;
            Active = true;
        }

        double Modal(in AudioEvent ev, double pitch, double capS)
        {
            ModalSynth.Build(ev.Material, ev.MassKg > 0 ? ev.MassKg : 1.0, ev.Seed, _modes);
            _nm = 0; _norm = 0; double maxT = 0.05;
            for (int k = 0; k < ModalSynth.MaxModes; k++)
            {
                double f = _modes[k].FreqHz * pitch;
                if (f >= 0.45 * _sr) continue;
                double w = 2.0 * Math.PI * f / _sr;
                _s[_nm] = 0; _c[_nm] = 1; _rs[_nm] = Math.Sin(w); _rc[_nm] = Math.Cos(w);
                _env[_nm] = _modes[k].Amplitude; _dec[_nm] = Math.Exp(-6.9078 / (Math.Max(_modes[k].T60S, 1e-3) * _sr));
                _norm += _modes[k].Amplitude; maxT = Math.Max(maxT, _modes[k].T60S); _nm++;
            }
            if (_norm <= 0) _norm = 1;
            return Math.Min(maxT, capS);
        }

        void SetNoise(double gain, double decayS, double lpHz)
        {
            _nGain = gain; _nEnv = 1.0; _nDec = Math.Exp(-6.9078 / (Math.Max(decayS, 1e-3) * _sr));
            _nLp = 1.0 - Math.Exp(-2.0 * Math.PI * lpHz / _sr);
        }

        void SetTone(double hz, double gain, double decayS)
        {
            _tone = gain; _toneW = 2.0 * Math.PI * hz / _sr; _toneEnv = 1.0; _toneDec = Math.Exp(-6.9078 / (Math.Max(decayS, 1e-3) * _sr)); _tonePh = 0;
        }

        // xorshift32 -> [-1,1)
        double Noise() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng * (1.0 / 2147483648.0)) - 1.0; }

        public void Mix(long blockStart, float[] l, float[] r, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long t = blockStart + i - _start;
                if (t < 0) continue;
                if (t >= _len) break;
                double s = 0;
                if (_nm > 0)
                {
                    double m = 0;
                    for (int k = 0; k < _nm; k++)
                    {
                        m += _env[k] * _s[k];
                        double ns = _s[k] * _rc[k] + _c[k] * _rs[k]; double nc = _c[k] * _rc[k] - _s[k] * _rs[k]; _s[k] = ns; _c[k] = nc; _env[k] *= _dec[k];
                    }
                    s += m / _norm;
                }
                if (_nGain > 0)
                {
                    double n = Noise();
                    if (_burst > 0) { if ((_rng & 0x3FF) < 24) _nEnv = Math.Min(1.0, _nEnv + 0.8); }   // random debris hits
                    _nLpS += _nLp * (n - _nLpS);
                    s += _nGain * _nEnv * _nLpS * (_nLp < 0.2 ? 3.0 : 1.5);
                    _nEnv *= _nDec;
                    if (_burst > 0) _nEnv = Math.Max(_nEnv, 0.0) * 0.9995;
                }
                if (_toneEnv > 1e-5)
                {
                    // slow attack for the infrasonic bed, instant for the muzzle boom
                    double att = _kind == AudioEventKind.MuzzleBlast ? 1.0 : Math.Min(1.0, t / (0.25 * _sr));
                    s += _tone * att * _toneEnv * Math.Sin(_tonePh) + (_kind == AudioEventKind.MuzzleBlast ? 0 : 0.35 * _tone * att * _toneEnv * Math.Sin(2.01 * _tonePh));
                    _tonePh += _toneW; _toneEnv *= _toneDec;
                }
                // end fade avoids a click when the generator is cut at the cap
                long tail = _len - t; double fade = tail < 480 ? tail / 480.0 : 1.0;
                _lpS += _lpA * ((float)(s * fade) - _lpS);
                l[i] += _lpS * _gl; r[i] += _lpS * _gr;
            }
        }
    }
}
