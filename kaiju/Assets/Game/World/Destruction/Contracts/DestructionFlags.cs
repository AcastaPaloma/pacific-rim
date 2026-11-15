// Feature flag names of World.Destruction (Assets/Game/Config/FeatureFlags/WorldDestruction.json, default off).
namespace Game.World.Destruction
{
    public static class DestructionFlags
    {
        /// <summary>Register the IDamageModel service.</summary>
        public const string Enabled = "destruction.enabled";
    }
}
