using System;
namespace Game.Audio
{
    public readonly struct VoiceGrant
    {
        public readonly bool Accepted;
        public readonly int VoiceId;       // slot index, -1 when rejected
        public readonly int StolenEventSlot; // slot index whose occupant was stolen, -1 if none (same as VoiceId when stolen)
        public readonly long StolenEventId;
        public VoiceGrant(bool ok, int voice, long stolenId) { Accepted = ok; VoiceId = voice; StolenEventSlot = stolenId >= 0 ? voice : -1; StolenEventId = stolenId; }
    }

    /// <summary>
    /// Fixed-size voice pool with priority stealing. Score = audibility (dB above floor) + kind bonus; a new request steals the lowest-scored voice only if it beats it by the hysteresis;
    /// ties break on older tick, then lower event id. The last <c>reservedCritical</c> slots are only for critical kinds. Deterministic, allocation-free after construction.
    /// </summary>
    public sealed class VoiceAllocator
    {
        readonly int _max, _reserved;
        readonly bool[] _active; readonly double[] _score; readonly long[] _tick; readonly long[] _id; readonly bool[] _critical;
        readonly double _hyst;
        public int ActiveCount { get; private set; }

        public VoiceAllocator(int maxVoices, int reservedCritical, double hysteresisDb)
        {
            if (maxVoices < 1 || reservedCritical < 0 || reservedCritical >= maxVoices) throw new ArgumentOutOfRangeException(nameof(maxVoices));
            _max = maxVoices; _reserved = reservedCritical; _hyst = hysteresisDb;
            _active = new bool[maxVoices]; _score = new double[maxVoices]; _tick = new long[maxVoices]; _id = new long[maxVoices]; _critical = new bool[maxVoices];
        }

        public int Capacity => _max;

        public static bool IsCritical(AudioEventKind k)
            => k == AudioEventKind.QuakeP || k == AudioEventKind.QuakeS || k == AudioEventKind.QuakeRumble || k == AudioEventKind.MuzzleBlast || k == AudioEventKind.FootContact;

        /// <summary>Kind bonus in dB-equivalents (Assumed): big, rare, gameplay-critical events outrank equal-level clutter.</summary>
        public static double KindBonus(AudioEventKind k)
        {
            switch (k)
            {
                case AudioEventKind.QuakeP: case AudioEventKind.QuakeS: case AudioEventKind.QuakeRumble: return 12;
                case AudioEventKind.FootContact: case AudioEventKind.MuzzleBlast: case AudioEventKind.Impact: return 8;
                case AudioEventKind.StructureFailure: return 6;
                case AudioEventKind.SupersonicCrack: case AudioEventKind.WaterSplash: return 3;
                case AudioEventKind.DebrisRain: case AudioEventKind.Wade: return 0;
                default: return -6; // ambience
            }
        }

        /// <summary>Priority score: listener-side SPL above the audible floor, plus kind bonus. Negative audibility (below the floor) returns -infinity: cull.</summary>
        public static double Score(double splAtListenerDb, double floorDb, AudioEventKind kind)
            => splAtListenerDb < floorDb ? double.NegativeInfinity : (splAtListenerDb - floorDb) + KindBonus(kind);

        public VoiceGrant Request(long eventId, AudioEventKind kind, double score, long tick)
        {
            if (double.IsNegativeInfinity(score) || double.IsNaN(score)) return new VoiceGrant(false, -1, -1);
            bool crit = IsCritical(kind);
            int limit = crit ? _max : _max - _reserved;
            // free slot?
            if (ActiveCount < limit || (crit && ActiveCount < _max))
            {
                for (int i = 0; i < _max; i++) if (!_active[i]) { Occupy(i, eventId, score, tick, crit); return new VoiceGrant(true, i, -1); }
            }
            // steal lowest: candidates are all active slots; non-critical may not steal critical voices
            int worst = -1;
            for (int i = 0; i < _max; i++)
            {
                if (!_active[i]) continue;
                if (!crit && _critical[i]) continue;
                if (worst < 0 || Worse(i, worst)) worst = i;
            }
            if (worst >= 0 && score > _score[worst] + _hyst)
            {
                long old = _id[worst];
                Occupy(worst, eventId, score, tick, crit, true);
                return new VoiceGrant(true, worst, old);
            }
            return new VoiceGrant(false, -1, -1);
        }

        bool Worse(int a, int b)
        {
            if (_score[a] != _score[b]) return _score[a] < _score[b];
            if (_tick[a] != _tick[b]) return _tick[a] < _tick[b];
            return _id[a] < _id[b];
        }

        void Occupy(int slot, long id, double score, long tick, bool crit, bool replacing = false)
        {
            if (!replacing) ActiveCount++;
            _active[slot] = true; _id[slot] = id; _score[slot] = score; _tick[slot] = tick; _critical[slot] = crit;
        }

        public void UpdateScore(int voiceId, double score) { if (_active[voiceId]) _score[voiceId] = score; }

        public void Release(int voiceId) { if (voiceId >= 0 && voiceId < _max && _active[voiceId]) { _active[voiceId] = false; ActiveCount--; } }
    }
}
