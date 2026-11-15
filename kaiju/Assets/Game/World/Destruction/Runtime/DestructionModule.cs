// Engine-free composition hook: registers IDamageModel only when destruction.enabled is on (MODULARITY rule 5).
using Game.Core;

namespace Game.World.Destruction
{
    public static class DestructionModule
    {
        /// <summary>Returns the model it registered, or null when the flag is off (nothing registered, bus untouched).</summary>
        public static DamageModel Install(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (registry == null || flags == null || !flags.IsOn(DestructionFlags.Enabled)) return null;
            IEventBus bus = null;
            registry.TryResolve(out bus);
            var model = new DamageModel(bus);
            registry.Register<IDamageModel>(model);
            return model;
        }
    }
}
