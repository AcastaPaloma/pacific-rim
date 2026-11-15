// Engine-free composition hook of the scripted brain (MODULARITY rule 5): registers a factory only when kaiju.scriptedBrain is on.
using Game.Core;

namespace Game.AI
{
    public sealed class ScriptedBrainFactory : IKaijuBrainFactory
    {
        public string BrainId => "scripted@1";
        public IKaijuBrain Create() => new ScriptedKaijuBrain();
    }

    public static class ScriptedBrainModule
    {
        /// <summary>Returns the factory it registered, or null when the flag is off (nothing registered).</summary>
        public static IKaijuBrainFactory Install(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (registry == null || flags == null || !flags.IsOn(BrainFlags.Scripted)) return null;
            var factory = new ScriptedBrainFactory();
            registry.Register<IKaijuBrainFactory>(factory);
            return factory;
        }
    }
}
