// T-030c: adapts the TEMPORARY placeholder body's damage record to Hangar's narrow IBodyDamageRecord (the adapter lives in the
// composition layer so neither Hangar nor Units.Placeholder references the other). Engine-free: also compiled by DotnetTests/Composition.Adapters.Tests.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Hangar;
using Game.Units.Placeholder;

namespace Game.Composition
{
    /// <summary>
    /// One part, "body": integrity = hit points, max = max hit points, hits = hits taken. Hit points are float32 in the record and float64 in
    /// Hangar; float to double is exact, so a damage -> hangar -> fight round trip is bit-exact unless a repair writes a value that float cannot hold.
    /// <see cref="LastHitStep"/> is stamped from <paramref name="step"/> whenever the record's hit count has grown since the last look.
    /// </summary>
    public sealed class PlaceholderDamageAdapter : IBodyDamageRecord
    {
        public const string PartId = "body";
        private readonly PlaceholderDamageRecord _record;
        private readonly Func<long> _step;
        private int _seenHits;
        private long _lastHitStep;

        public PlaceholderDamageAdapter(PlaceholderDamageRecord record, PlayerSlot slot, Func<long> step = null)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            Slot = slot; _step = step; _seenHits = record.HitsTaken;
        }

        public PlayerSlot Slot { get; }
        public PlaceholderDamageRecord Record => _record;

        public void CopyParts(List<PartDamage> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (_record.HitsTaken > _seenHits) { _lastHitStep = _step?.Invoke() ?? _lastHitStep; }
            _seenHits = _record.HitsTaken;
            into.Clear();
            into.Add(new PartDamage { PartId = PartId, Integrity = _record.HitPoints, MaxIntegrity = _record.MaxHitPoints, LastHitStep = _lastHitStep, Hits = _record.HitsTaken });
        }

        public void ApplyParts(IReadOnlyList<PartDamage> parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            for (int i = 0; i < parts.Count; i++)
            {
                if (!string.Equals(parts[i].PartId, PartId, StringComparison.Ordinal)) continue;
                var p = parts[i];
                _record.Import(new PlaceholderDamageData { MaxHitPoints = (float)p.MaxIntegrity, HitPoints = (float)p.Integrity, HitsTaken = (int)Math.Min(int.MaxValue, Math.Max(0, p.Hits)) });
                _seenHits = _record.HitsTaken; _lastHitStep = p.LastHitStep;
                return;
            }
        }
    }
}
