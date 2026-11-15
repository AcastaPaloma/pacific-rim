using System;
using System.Collections.Generic;

namespace Game.Net
{
    public struct LinkConditions
    {
        /// <summary>Fixed one-way delay in Core steps.</summary>
        public int DelaySteps;
        /// <summary>Extra delay drawn uniformly from 0..JitterSteps (inclusive) per message.</summary>
        public int JitterSteps;
        /// <summary>Probability in [0,1] that an unreliable message is dropped. Reliable is never dropped.</summary>
        public double UnreliableLoss;
    }

    /// <summary>
    /// Deterministic delay/jitter/loss wrapper over any <see cref="ITransport"/>. Sends are held until the
    /// simulated Core step reaches their due step (<see cref="Advance"/>), then forwarded to the inner transport.
    /// Same seed + same call sequence = same outcome. No wall clock, no sleeps, no System.Random.
    /// Reliable messages are delayed but never overtake each other; unreliable ones may be reordered by jitter.
    /// Wrap each endpoint to shape its outgoing direction.
    /// </summary>
    public sealed class SimulatedLinkTransport : ITransport
    {
        private struct Pending { public long Due; public long Seq; public NetChannel Channel; public byte[] Data; }

        private readonly ITransport _inner;
        private readonly List<Pending> _pending = new List<Pending>();
        private ulong _rng;
        private long _step;
        private long _seq;
        private long _lastReliableDue = long.MinValue;

        public LinkConditions Conditions;
        public int DroppedCount { get; private set; }

        public SimulatedLinkTransport(ITransport inner, ulong seed, LinkConditions conditions)
        {
            _inner = inner; _rng = seed; Conditions = conditions;
        }

        public long Step => _step;

        // SplitMix64: tiny, fully specified, identical on every platform.
        private ulong Next()
        {
            unchecked
            {
                ulong z = (_rng += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
        private double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));

        public bool IsConnected => _inner.IsConnected;

        public bool Send(NetChannel channel, ReadOnlySpan<byte> payload)
        {
            if (!_inner.IsConnected) return false;
            // Draw in a fixed order per message so the stream does not depend on the outcome.
            double lossRoll = NextDouble();
            int jitter = Conditions.JitterSteps > 0 ? (int)(Next() % (ulong)(Conditions.JitterSteps + 1)) : 0;
            if (channel == NetChannel.Unreliable && lossRoll < Conditions.UnreliableLoss) { DroppedCount++; return true; }
            long due = _step + Math.Max(0, Conditions.DelaySteps) + jitter;
            if (channel == NetChannel.Reliable)
            {
                if (due < _lastReliableDue) due = _lastReliableDue;
                _lastReliableDue = due;
            }
            _pending.Add(new Pending { Due = due, Seq = _seq++, Channel = channel, Data = payload.ToArray() });
            if (due <= _step) Flush();
            return true;
        }

        /// <summary>Sets the simulated Core step (must not go backwards) and releases everything now due.</summary>
        public void Advance(long step)
        {
            if (step < _step) throw new ArgumentOutOfRangeException(nameof(step), "Simulated step cannot go backwards");
            _step = step;
            Flush();
        }

        private void Flush()
        {
            if (_pending.Count == 0) return;
            _pending.Sort((a, b) => a.Due != b.Due ? a.Due.CompareTo(b.Due) : a.Seq.CompareTo(b.Seq));
            int n = 0;
            while (n < _pending.Count && _pending[n].Due <= _step) { _inner.Send(_pending[n].Channel, _pending[n].Data); n++; }
            _pending.RemoveRange(0, n);
        }

        public bool TryReceive(out NetChannel channel, out byte[] payload) => _inner.TryReceive(out channel, out payload);
        public void Close() { _pending.Clear(); _inner.Close(); }
    }
}
