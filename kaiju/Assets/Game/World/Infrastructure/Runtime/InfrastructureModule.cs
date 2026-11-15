// Composition hook of World.Infrastructure (MODULARITY rule 5): registers the registry under IInfrastructureRegistry when the flag is on.
using Game.Core;
using Game.World.Destruction;

namespace Game.World.Infrastructure
{
    public static class InfrastructureModule
    {
        /// <summary>
        /// Returns the registry it registered, or null when the flag is off (nothing registered). Uses the registered IDamageModel when there is
        /// one (damage then flows through it); without one the registry answers spatial queries only.
        /// </summary>
        public static InfraRegistry Install(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (registry == null || flags == null || !flags.IsOn(InfrastructureFlags.Enabled)) return null;
            registry.TryResolve(out IDamageModel damage);
            var reg = new InfraRegistry(damage);
            registry.Register<IInfrastructureRegistry>(reg);
            return reg;
        }
    }
}
