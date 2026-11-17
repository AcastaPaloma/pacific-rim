// T-152b: bridges the Combat ammo query to the HUD's IAmmoSource, behind flag ui.hud_data (default off; committed config installs nothing).
using System;
using Game.Combat;
using Game.Core;
using Game.UI;

namespace Game.Composition
{
    /// <summary>Adapts <see cref="IAmmoQuery"/> to <see cref="IAmmoSource"/>. mechId is the EntityId value of the cannon mount. Read-only.</summary>
    public sealed class CombatAmmoSource : IAmmoSource
    {
        private readonly IAmmoQuery _query;
        public CombatAmmoSource(IAmmoQuery query) { _query = query ?? throw new ArgumentNullException(nameof(query)); }

        public bool TryGetAmmo(int mechId, out CannonAmmoState state)
        {
            state = default;
            if (mechId <= 0 || !_query.TryGetAmmo(EntityId.FromValue((ulong)mechId), out AmmoStatus s)) return false;
            state = new CannonAmmoState(s.Ammo, s.Capacity, s.Reloading, (float)s.ReloadFrac);
            return true;
        }
    }

    /// <summary>Adapts <see cref="IHealthQuery"/> to <see cref="IHealthSource"/> (T-154c). mechId is the EntityId value. Read-only.</summary>
    public sealed class CombatHealthSource : IHealthSource
    {
        private readonly IHealthQuery _query;
        public CombatHealthSource(IHealthQuery query) { _query = query ?? throw new ArgumentNullException(nameof(query)); }

        public bool TryGetHealthFraction(int mechId, out float fraction)
        {
            fraction = 0f;
            if (mechId <= 0 || !_query.TryGetHealth(EntityId.FromValue((ulong)mechId), out HealthStatus s) || !(s.MaxHitPoints > 0)) return false;
            fraction = (float)s.Fraction;
            return true;
        }
    }

    public static class HudDataInstaller
    {
        public const string Flag = "ui.hud_data";

        /// <summary>Null (nothing registered) when the flag is off or no ammo query exists (IAmmoQuery, else the CombatHandle's system). Registers IAmmoSource.</summary>
        public static IAmmoSource Install(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (registry == null || flags == null || !flags.IsOn(Flag)) return null;
            if (!registry.TryResolve(out IAmmoQuery query))
            {
                if (!registry.TryResolve(out CombatHandle handle) || handle?.System == null) return null;
                query = handle.System;
            }
            var src = new CombatAmmoSource(query);
            registry.Register<IAmmoSource>(src);
            return src;
        }

        /// <summary>
        /// T-154c: registers IHealthSource when the flag is on and a health query exists (IHealthQuery, else the CombatHandle's LinearEntityHealth).
        /// Separate from <see cref="Install"/> so existing callers and registry counts are unchanged. Null (nothing registered) otherwise.
        /// </summary>
        public static IHealthSource InstallHealth(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (registry == null || flags == null || !flags.IsOn(Flag)) return null;
            if (!registry.TryResolve(out IHealthQuery query))
            {
                if (!registry.TryResolve(out CombatHandle handle) || handle?.Health == null) return null;
                query = handle.Health;
            }
            var src = new CombatHealthSource(query);
            registry.Register<IHealthSource>(src);
            return src;
        }
    }
}
