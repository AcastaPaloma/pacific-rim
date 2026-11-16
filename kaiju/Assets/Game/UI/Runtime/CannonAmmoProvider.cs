using System;
using System.Collections.Generic;

namespace Game.UI
{
    /// <summary>One mech cannon's ammo and reload state as the HUD needs it. <see cref="ReloadFrac"/> is 0..1 (1 = ready to fire).</summary>
    public readonly struct CannonAmmoState
    {
        public readonly int Count, Max;
        public readonly bool Reloading;
        public readonly float ReloadFrac;

        public CannonAmmoState(int count, int max, bool reloading, float reloadFrac)
        {
            Count = count; Max = max; Reloading = reloading;
            ReloadFrac = !(reloadFrac > 0f) ? 0f : (reloadFrac > 1f ? 1f : reloadFrac);
        }

        /// <summary>From the cannon's own numbers: <paramref name="cooldownSteps"/> left of <paramref name="reloadSteps"/> (Combat Cannon: ReloadSteps, private cooldown).</summary>
        public static CannonAmmoState FromCooldown(int count, int max, double cooldownSteps, int reloadSteps)
        {
            if (reloadSteps <= 0 || !(cooldownSteps > 0)) return new CannonAmmoState(count, max, false, 1f);
            double frac = 1.0 - cooldownSteps / reloadSteps;
            return new CannonAmmoState(count, max, true, (float)frac);
        }
    }

    /// <summary>
    /// Where ammo state comes from. The Combat module (Game.Combat Cannon: Ammo, Spec.MaxAmmo, cooldown) is bridged by a thin adapter
    /// in Composition; Game.UI stays engine-free and does not reference Game.Combat. T-152 gap: the Combat CONTRACT has no ammo query
    /// (only the WeaponFired bus event), so the adapter has to read Cannon directly until Combat adds one.
    /// </summary>
    public interface IAmmoSource
    {
        bool TryGetAmmo(int mechId, out CannonAmmoState state);
    }

    /// <summary>Delegate-backed source so composition code can wire a Cannon without a new type.</summary>
    public sealed class DelegateAmmoSource : IAmmoSource
    {
        public delegate bool Getter(int mechId, out CannonAmmoState state);
        readonly Getter _get;
        public DelegateAmmoSource(Getter get) { if (get == null) throw new ArgumentNullException("get"); _get = get; }
        public bool TryGetAmmo(int mechId, out CannonAmmoState state) { return _get(mechId, out state); }
    }

    /// <summary>
    /// The real limits provider for the one D-019 limit that has runtime state today (ammo; R-122 section 1). Reports an ammo-only
    /// <see cref="MechLimitsView"/>; power, heat and strain stay unavailable. Behind flag ui.hud (default off); with no source or an unknown mech it
    /// reports nothing, so the HUD hides the block. Reads state only: it never fires, reloads or resupplies.
    /// </summary>
    public sealed class CannonAmmoProvider : ILimitsProvider
    {
        readonly IAmmoSource _source;

        public CannonAmmoProvider(IAmmoSource source) { _source = source; }

        public bool TryGetLimits(int mechId, out MechLimitsView view)
        {
            CannonAmmoState s;
            if (_source != null && _source.TryGetAmmo(mechId, out s) && s.Max > 0)
            {
                view = MechLimitsView.AmmoOnly(s.Count, s.Max);
                return true;
            }
            view = default(MechLimitsView);
            return false;
        }

        /// <summary>Reload progress for the HUD pip row (not part of <see cref="MechLimitsView"/> v1); false when unknown.</summary>
        public bool TryGetReload(int mechId, out bool reloading, out float frac)
        {
            CannonAmmoState s;
            if (_source != null && _source.TryGetAmmo(mechId, out s) && s.Max > 0) { reloading = s.Reloading; frac = s.ReloadFrac; return true; }
            reloading = false; frac = 0f; return false;
        }
    }
}
