using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>Feature flags (contract v1). A name that is not listed reads off. Reads never allocate.</summary>
    public interface IFeatureFlags
    {
        bool IsOn(string flagName);
        int Count { get; }
        string NameAt(int index);   // ordinal name order: stable across runs and machines
        bool ValueAt(int index);
    }

    /// <summary>Immutable flag set fixed for one session. Enumerable in ordinal name order, serialisable.</summary>
    public sealed class FeatureFlagSnapshot : IFeatureFlags
    {
        private readonly string[] _names;
        private readonly bool[] _values;

        public FeatureFlagSnapshot(IEnumerable<KeyValuePair<string, bool>> flags)
        {
            var list = new List<KeyValuePair<string, bool>>(flags ?? Array.Empty<KeyValuePair<string, bool>>());
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            _names = new string[list.Count];
            _values = new bool[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrEmpty(list[i].Key)) throw new ArgumentException("Empty flag name");
                if (i > 0 && list[i].Key == list[i - 1].Key) throw new ArgumentException("Duplicate flag " + list[i].Key);
                _names[i] = list[i].Key;
                _values[i] = list[i].Value;
            }
        }

        public static FeatureFlagSnapshot AllOff { get; } = new FeatureFlagSnapshot(null);

        public int Count => _names.Length;
        public string NameAt(int index) => _names[index];
        public bool ValueAt(int index) => _values[index];

        public bool IsOn(string flagName)
        {
            if (flagName == null) return false;
            int lo = 0, hi = _names.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int c = string.CompareOrdinal(_names[mid], flagName);
                if (c == 0) return _values[mid];
                if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return false;
        }

        /// <summary>Canonical text form, e.g. for stamping into saves or replays: one "name=0|1" per line.</summary>
        public string ToCanonicalString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _names.Length; i++) sb.Append(_names[i]).Append('=').Append(_values[i] ? '1' : '0').Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Fixed-step simulation clock (contract v1). The 64-bit step counter is the unit of time;
    /// seconds are derived as Step * StepSeconds, so there is no accumulated drift. Never reads a wall clock.
    /// </summary>
    public interface ISimulationClock
    {
        long Step { get; }
        double StepSeconds { get; }
        double TimeSeconds { get; }
        double TimeAt(long step);
    }
}
