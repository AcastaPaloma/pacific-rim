namespace Game.Core
{
    /// <summary>Versions of Core's public contracts. Bump on any breaking change and note it in MODULE.md.</summary>
    public static class CoreContractVersions
    {
        public const int EventBus = 1;
        public const int ServiceRegistry = 1;
        public const int Ids = 1;
        public const int FeatureFlags = 1;
        public const int Clock = 1;
        public const int SaveSchema = SaveDocument.CurrentSchema;
        public const int CoreSettingsSchema = 1;
        public const int FeatureFlagsFileSchema = 1;
    }
}
