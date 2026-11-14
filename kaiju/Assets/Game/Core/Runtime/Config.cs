using System;
using System.Collections.Generic;
using System.IO;

namespace Game.Core
{
    /// <summary>Core tuning values, loaded from Assets/Game/Config/CoreSettings.json (schema 1).</summary>
    public sealed class CoreSettings
    {
        public CoreSettings(double fixedStepSeconds, int maxEventsPerCascade)
        {
            if (!(fixedStepSeconds > 0) || double.IsInfinity(fixedStepSeconds)) throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
            if (maxEventsPerCascade < 1) throw new ArgumentOutOfRangeException(nameof(maxEventsPerCascade));
            FixedStepSeconds = fixedStepSeconds;
            MaxEventsPerCascade = maxEventsPerCascade;
        }

        public double FixedStepSeconds { get; }
        public int MaxEventsPerCascade { get; }

        /// <summary>Parses the settings file. Settings have no safe default, so a bad file throws (naming file and position).</summary>
        public static CoreSettings Parse(string json, string sourceName)
        {
            JsonValue root;
            try { root = Json.Parse(json); }
            catch (JsonParseException ex) { throw new FormatException(sourceName + ": " + ex.Message, ex); }
            if (root.Kind != JsonKind.Object) throw new FormatException(sourceName + ": root must be an object");
            var schema = root.Get("schema");
            if (schema == null || !schema.TryGetInt64(out long version) || version != CoreContractVersions.CoreSettingsSchema)
                throw new FormatException(sourceName + ": unsupported or missing schema (expected " + CoreContractVersions.CoreSettingsSchema + ")");
            var step = root.Get("fixedStepSeconds");
            var cap = root.Get("maxEventsPerCascade");
            if (step == null || !step.TryGetDouble(out double stepSeconds)) throw new FormatException(sourceName + ": fixedStepSeconds missing or not a number");
            if (cap == null || !cap.TryGetInt64(out long capValue) || capValue < 1 || capValue > int.MaxValue) throw new FormatException(sourceName + ": maxEventsPerCascade missing or out of range");
            return new CoreSettings(stepSeconds, (int)capValue);
        }
    }

    /// <summary>
    /// Loads the feature flag folder Assets/Game/Config/FeatureFlags/: one JSON file per module, each
    /// {"schema":1,"flags":{"name":false,...}}. Files are read in ordinal file-name order and merged; a flag
    /// name listed in two files is an error. Never turns anything on by accident: missing folder or no files =
    /// all off + one warning; any malformed file or duplicate = all off + an error naming file and position.
    /// </summary>
    public static class FeatureFlagLoader
    {
        public static FeatureFlagSnapshot LoadFolder(string folder, ICoreErrorSink errors)
        {
            if (!Directory.Exists(folder))
            {
                errors.Report(new CoreError(CoreErrorKind.ConfigMissing, "Feature flag folder not found: " + folder + ". All flags read off."));
                return FeatureFlagSnapshot.AllOff;
            }
            var sources = new List<KeyValuePair<string, string>>();
            foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.TopDirectoryOnly))
                sources.Add(new KeyValuePair<string, string>(file.Replace('\\', '/'), File.ReadAllText(file)));
            return Merge(sources, folder, errors);
        }

        /// <summary>Merges (source name, JSON text) pairs; sources are processed in ordinal order of their names.</summary>
        public static FeatureFlagSnapshot Merge(IEnumerable<KeyValuePair<string, string>> sources, string folderName, ICoreErrorSink errors)
        {
            var ordered = new List<KeyValuePair<string, string>>(sources);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            if (ordered.Count == 0)
            {
                errors.Report(new CoreError(CoreErrorKind.ConfigMissing, "No feature flag files in " + folderName + ". All flags read off."));
                return FeatureFlagSnapshot.AllOff;
            }
            var merged = new List<KeyValuePair<string, bool>>();
            var owner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var src in ordered)
            {
                var flags = ParseFlags(src.Value, src.Key, errors);
                if (flags == null) return FeatureFlagSnapshot.AllOff;
                foreach (var f in flags)
                {
                    if (owner.TryGetValue(f.Key, out var first))
                        return Malformed(src.Key, "flag '" + f.Key + "' is already defined in " + first, errors);
                    owner.Add(f.Key, src.Key);
                    merged.Add(f);
                }
            }
            return new FeatureFlagSnapshot(merged);
        }

        /// <summary>Parses one flag file on its own.</summary>
        public static FeatureFlagSnapshot Parse(string json, string sourceName, ICoreErrorSink errors)
        {
            if (json == null)
            {
                errors.Report(new CoreError(CoreErrorKind.ConfigMissing, "Feature flag source missing: " + sourceName + ". All flags read off."));
                return FeatureFlagSnapshot.AllOff;
            }
            var flags = ParseFlags(json, sourceName, errors);
            return flags == null ? FeatureFlagSnapshot.AllOff : new FeatureFlagSnapshot(flags);
        }

        private static List<KeyValuePair<string, bool>> ParseFlags(string json, string sourceName, ICoreErrorSink errors)
        {
            try
            {
                var root = Json.Parse(json);
                if (root.Kind != JsonKind.Object) return Fail(sourceName, "root must be an object", errors);
                var schema = root.Get("schema");
                if (schema == null || !schema.TryGetInt64(out long v) || v != CoreContractVersions.FeatureFlagsFileSchema)
                    return Fail(sourceName, "unsupported or missing schema (expected " + CoreContractVersions.FeatureFlagsFileSchema + ")", errors);
                var flags = root.Get("flags");
                if (flags == null || flags.Kind != JsonKind.Object) return Fail(sourceName, "'flags' must be an object", errors);
                var list = new List<KeyValuePair<string, bool>>(flags.Members.Count);
                foreach (var m in flags.Members)
                {
                    if (m.Value.Kind != JsonKind.Bool) return Fail(sourceName, "flag '" + m.Key + "' must be true or false", errors);
                    if (m.Key.Length == 0) return Fail(sourceName, "empty flag name", errors);
                    list.Add(new KeyValuePair<string, bool>(m.Key, m.Value.Bool));
                }
                return list;
            }
            catch (JsonParseException ex) { return Fail(sourceName, ex.Message, errors); }
        }

        private static List<KeyValuePair<string, bool>> Fail(string source, string why, ICoreErrorSink errors)
        {
            Malformed(source, why, errors);
            return null;
        }

        private static FeatureFlagSnapshot Malformed(string source, string why, ICoreErrorSink errors)
        {
            errors.Report(new CoreError(CoreErrorKind.ConfigMalformed, "Malformed feature flag file " + source + ": " + why + ". All flags read off."));
            return FeatureFlagSnapshot.AllOff;
        }
    }

    /// <summary>
    /// The engine-free composition root of one session: creates and registers bus, clock, flags and error sink.
    /// The Unity adapter builds exactly one of these per Play session; tests build as many as they like.
    /// </summary>
    public sealed class CoreServices : IDisposable
    {
        public CoreServices(CoreSettings settings, FeatureFlagSnapshot flags, ICoreErrorSink errors)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Flags = flags ?? FeatureFlagSnapshot.AllOff;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Bus = new EventBus(errors, settings.MaxEventsPerCascade);
            Clock = new SimulationClock(settings.FixedStepSeconds);
            EntityIds = new EntityIdAllocator();
            Registry = new ServiceRegistry();
            Registry.Register<ICoreErrorSink>(errors);
            Registry.Register<IFeatureFlags>(Flags);
            Registry.Register<ISimulationClock>(Clock);
            Registry.Register<IEventBus>(Bus);
            Registry.Register(EntityIds);
        }

        public CoreSettings Settings { get; }
        public FeatureFlagSnapshot Flags { get; }
        public ICoreErrorSink Errors { get; }
        public EventBus Bus { get; }
        public SimulationClock Clock { get; }
        public EntityIdAllocator EntityIds { get; }
        public ServiceRegistry Registry { get; }

        public void Dispose() => Registry.Dispose();
    }
}
