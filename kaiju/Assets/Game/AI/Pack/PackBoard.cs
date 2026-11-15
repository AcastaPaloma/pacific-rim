using System;
using Game.Core;

namespace Game.AI.Pack
{
    public enum BoardSource : byte { Own = 0, Called = 1, Map = 2 }

    /// <summary>One opposing unit on the shared board (R-103 4.4). Fixed fields, no allocation.</summary>
    public struct BoardEntry
    {
        public EntityId Id;
        public Float2 Pos, Vel;
        public float HpFrac;
        public bool Alive;
        public double TObserved;
        public EntityId Observer;   // provenance (leak check, R-103 4.1)
        public BoardSource Source;
        public int Claims;          // members currently claiming this target (recomputed each tick)
    }

    /// <summary>
    /// Shared target board of one pack: <see cref="Capacity"/> opposing units (R-103 4.4: 8 targets), sorted by EntityId ordinal so every
    /// iteration is deterministic. Instant sharing in T-113; the delayed/lossy CommsModel is a tracked follow-up (provenance fields are in place).
    /// </summary>
    public sealed class PackBoard
    {
        public const int Capacity = BrainContracts.MaxTargets;
        private readonly BoardEntry[] _e = new BoardEntry[Capacity];
        public int Count { get; private set; }

        public void Clear() { Count = 0; }
        public ref readonly BoardEntry this[int i] => ref _e[i];

        public int IndexOf(EntityId id)
        {
            for (int i = 0; i < Count; i++) if (_e[i].Id == id) return i;
            return -1;
        }

        /// <summary>Insert or refresh an entry; an older observation never overwrites a newer one. Returns false when the board is full of other ids.</summary>
        public bool Observe(EntityId id, Float2 pos, Float2 vel, float hp, bool alive, double t, EntityId observer, BoardSource src)
        {
            int idx = IndexOf(id);
            if (idx < 0)
            {
                if (Count >= Capacity) return false;
                idx = Count++;
                while (idx > 0 && _e[idx - 1].Id.Value > id.Value) { _e[idx] = _e[idx - 1]; idx--; }
                _e[idx] = default; _e[idx].Id = id; _e[idx].TObserved = double.NegativeInfinity;
            }
            if (t < _e[idx].TObserved) return true;
            _e[idx].Pos = pos; _e[idx].Vel = vel; _e[idx].HpFrac = hp; _e[idx].Alive = alive;
            _e[idx].TObserved = t; _e[idx].Observer = observer; _e[idx].Source = src;
            return true;
        }

        /// <summary>Ingest one member's observation: its targets (own sight; death is public inside its window) become board entries.</summary>
        public void Ingest(in BrainObservation obs)
        {
            for (int i = 0; i < obs.TargetCount; i++)
            {
                ref readonly TargetObs t = ref obs.Targets[i];
                double seen = obs.TimeSeconds - t.MemoryAge;
                Observe(t.Id, t.Pos, default, t.HpFrac, t.Alive, seen, obs.Self.Id, BoardSource.Own);
            }
        }

        /// <summary>Drop dead entries and entries older than <paramref name="staleSeconds"/>.</summary>
        public void Prune(double now, float staleSeconds)
        {
            int w = 0;
            for (int r = 0; r < Count; r++)
            {
                if (!_e[r].Alive || now - _e[r].TObserved > staleSeconds) continue;
                if (w != r) _e[w] = _e[r];
                w++;
            }
            Count = w;
        }

        /// <summary>Age-extrapolated position (R-103 4.1: last position + velocity x age, capped at 20 s).</summary>
        public Float2 ExtrapolatedPos(int i, double now)
        {
            float age = (float)Math.Min(20.0, Math.Max(0.0, now - _e[i].TObserved));
            return new Float2(_e[i].Pos.X + _e[i].Vel.X * age, _e[i].Pos.Y + _e[i].Vel.Y * age);
        }

        internal void SetClaims(int i, int claims) { _e[i].Claims = claims; }
    }
}
