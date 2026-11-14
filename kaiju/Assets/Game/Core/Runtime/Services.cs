using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>Plain instance registry. One per session; never static.</summary>
    public sealed class ServiceRegistry : IServiceRegistry, IDisposable
    {
        private readonly Dictionary<Type, object> _byContract = new Dictionary<Type, object>();
        private readonly List<object> _registrationOrder = new List<object>();
        private bool _disposed;

        public void Register<TContract>(TContract implementation) where TContract : class
        {
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            if (_disposed) throw new ObjectDisposedException(nameof(ServiceRegistry));
            if (_byContract.TryGetValue(typeof(TContract), out var existing))
                throw new DuplicateServiceException(typeof(TContract), existing.GetType(), implementation.GetType());
            _byContract.Add(typeof(TContract), implementation);
            _registrationOrder.Add(implementation);
        }

        public TContract Resolve<TContract>() where TContract : class
        {
            if (_byContract.TryGetValue(typeof(TContract), out var impl)) return (TContract)impl;
            throw new ServiceNotRegisteredException(typeof(TContract));
        }

        public bool TryResolve<TContract>(out TContract implementation) where TContract : class
        {
            bool found = _byContract.TryGetValue(typeof(TContract), out var impl);
            implementation = found ? (TContract)impl : null;
            return found;
        }

        public bool IsRegistered<TContract>() where TContract : class => _byContract.ContainsKey(typeof(TContract));

        /// <summary>Disposes IDisposable services in reverse registration order, each instance once.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = null;
            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                bool seenLater = false;  // same instance registered under several contracts: dispose at its last registration only
                for (int j = i + 1; j < _registrationOrder.Count; j++)
                    if (ReferenceEquals(_registrationOrder[j], service)) { seenLater = true; break; }
                if (seenLater || !(service is IDisposable d)) continue;
                try { d.Dispose(); }
                catch (Exception ex) { (failures ?? (failures = new List<Exception>())).Add(ex); }
            }
            _byContract.Clear();
            _registrationOrder.Clear();
            if (failures != null) throw new AggregateException("Service disposal failed", failures);
        }
    }

    /// <summary>Issues EntityIds 1, 2, 3, ... and never reuses one within a session (released IDs stay dead).</summary>
    public sealed class EntityIdAllocator
    {
        private readonly HashSet<ulong> _live = new HashSet<ulong>();
        private ulong _next;

        public EntityIdAllocator(ulong nextValue = 1) { _next = nextValue == 0 ? 1 : nextValue; }

        /// <summary>The value the next Allocate returns; save it to continue numbering after a load.</summary>
        public ulong NextValue => _next;
        public int LiveCount => _live.Count;

        public EntityId Allocate()
        {
            if (_next == ulong.MaxValue) throw new InvalidOperationException("EntityId space exhausted");
            var id = EntityId.FromValue(_next++);
            _live.Add(id.Value);
            return id;
        }

        public bool IsLive(EntityId id) => _live.Contains(id.Value);

        public void Release(EntityId id)
        {
            if (!_live.Remove(id.Value)) throw new InvalidOperationException(id + " is not live (stale or foreign ID)");
        }
    }

    /// <summary>Manually stepped fixed-step clock. The Unity adapter calls Advance once per FixedUpdate.</summary>
    public sealed class SimulationClock : ISimulationClock
    {
        public SimulationClock(double stepSeconds, long startStep = 0)
        {
            if (!(stepSeconds > 0) || double.IsInfinity(stepSeconds)) throw new ArgumentOutOfRangeException(nameof(stepSeconds));
            if (startStep < 0) throw new ArgumentOutOfRangeException(nameof(startStep));
            StepSeconds = stepSeconds;
            Step = startStep;
        }

        public long Step { get; private set; }
        public double StepSeconds { get; }
        public double TimeSeconds => Step * StepSeconds;
        public double TimeAt(long step) => step * StepSeconds;
        public void Advance() => Step++;
    }

    /// <summary>Keeps every reported error. Used by tests and as the inner sink of the Unity logger.</summary>
    public sealed class CollectingErrorSink : ICoreErrorSink
    {
        private readonly List<CoreError> _errors = new List<CoreError>();
        public IReadOnlyList<CoreError> Errors => _errors;
        public int Count => _errors.Count;
        public int CountOf(CoreErrorKind kind) { int n = 0; foreach (var e in _errors) if (e.Kind == kind) n++; return n; }
        public void Report(CoreError error) => _errors.Add(error ?? throw new ArgumentNullException(nameof(error)));
        public void Clear() => _errors.Clear();
    }

    /// <summary>Compares Unity's fixed step with the configured one (single source of truth: Config/CoreSettings).</summary>
    public static class FixedStepCheck
    {
        /// <summary>True when the engine's single-precision step equals the configured step to float precision.</summary>
        public static bool Matches(float engineStepSeconds, double configStepSeconds) =>
            Math.Abs(engineStepSeconds - configStepSeconds) <= configStepSeconds * 1e-6;
    }
}
