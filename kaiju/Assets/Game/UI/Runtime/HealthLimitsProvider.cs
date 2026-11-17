using System;

namespace Game.UI
{
    /// <summary>Where a mech's hit-point fraction (0..1) comes from (T-154c). Bridged to the Combat health query by an adapter in Composition; Game.UI does not reference Game.Combat.</summary>
    public interface IHealthSource
    {
        bool TryGetHealthFraction(int mechId, out float fraction);
    }

    /// <summary>Delegate-backed source (tests, composition without a new type).</summary>
    public sealed class DelegateHealthSource : IHealthSource
    {
        public delegate bool Getter(int mechId, out float fraction);
        readonly Getter _get;
        public DelegateHealthSource(Getter get) { if (get == null) throw new ArgumentNullException("get"); _get = get; }
        public bool TryGetHealthFraction(int mechId, out float fraction) { return _get(mechId, out fraction); }
    }

    /// <summary>
    /// Adds real health to another provider's view (for example <see cref="CannonAmmoProvider"/>): <see cref="LimitsAvailability.Health"/> and
    /// <see cref="MechLimitsView.HealthFrac"/>. With no inner provider (or one that knows nothing) the view carries health alone. Read-only; reports
    /// nothing when neither the inner provider nor the health source knows the mech.
    /// </summary>
    public sealed class HealthLimitsProvider : ILimitsProvider
    {
        readonly ILimitsProvider _inner;
        readonly IHealthSource _health;

        public HealthLimitsProvider(ILimitsProvider inner, IHealthSource health) { _inner = inner; _health = health; }

        public bool TryGetLimits(int mechId, out MechLimitsView view)
        {
            MechLimitsView v = default(MechLimitsView);
            bool have = _inner != null && _inner.TryGetLimits(mechId, out v);
            if (!have) v = new MechLimitsView(LimitsAvailability.None, 0f, 0f, 0f, 0, 0, 0f);
            view = v;
            float frac;
            if (_health != null && _health.TryGetHealthFraction(mechId, out frac)) { view = view.WithHealth(frac); return true; }
            return have;
        }
    }
}
