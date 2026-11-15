using System;
using System.IO;
using UnityEngine;

namespace Game.Core.Unity
{
    /// <summary>Everything Core creates for one Play session (editor or player).</summary>
    public sealed class CoreRoot : IDisposable
    {
        public const string VerboseBootFlag = "core.verboseBoot";
        public const string SaveFileName = "world.save.json";

        private GameObject _driver;

        private CoreRoot(CoreServices services, UnityLogErrorSink errors, ISaveStore saves, SaveSystem saveSystem)
        { Services = services; Errors = errors; Saves = saves; SaveSystem = saveSystem; }

        public CoreServices Services { get; }
        public UnityLogErrorSink Errors { get; }
        public ISaveStore Saves { get; }
        public SaveSystem SaveSystem { get; }
        public IServiceRegistry Registry => Services.Registry;

        /// <summary>Builds the session's services from the committed config. Does not touch engine state.</summary>
        public static CoreRoot Create(CoreConfigAsset config, string saveDirectory, string buildVersion)
        {
            var errors = new UnityLogErrorSink();
            string settingsText = config != null ? config.CoreSettingsText : null;
            if (settingsText == null)
                throw new InvalidOperationException("[Core] " + CoreConfigAsset.CoreSettingsPath + " is not reachable through Resources/" + CoreConfigAsset.ResourcesName + "; run CoreConfigAsset.CreateOrUpdate.");
            var settings = CoreSettings.Parse(settingsText, CoreConfigAsset.CoreSettingsPath);
            var flags = FeatureFlagLoader.Merge(config.FeatureFlagSources(), CoreConfigAsset.FeatureFlagsFolder, errors);
            var services = new CoreServices(settings, flags, errors);
            var saves = new JsonFileSaveStore(Path.Combine(saveDirectory, SaveFileName));
            var saveSystem = new SaveSystem(buildVersion);
            services.Registry.Register<ISaveStore>(saves);
            services.Registry.Register(saveSystem);
            return new CoreRoot(services, errors, saves, saveSystem);
        }

        /// <summary>Makes the engine's fixed step equal the configured one; reports when the project setting differed.</summary>
        public static float ReconcileFixedStep(float engineStep, double configuredStep, ICoreErrorSink errors)
        {
            if (!FixedStepCheck.Matches(engineStep, configuredStep))
                errors.Report(new CoreError(CoreErrorKind.FixedStepMismatch,
                    "Unity fixed timestep " + engineStep.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " s differs from "
                    + CoreConfigAsset.CoreSettingsPath + " fixedStepSeconds " + configuredStep.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " s; using the config value."));
            return (float)configuredStep;
        }

        internal void StartDriving()
        {
            Time.fixedDeltaTime = ReconcileFixedStep(Time.fixedDeltaTime, Services.Settings.FixedStepSeconds, Errors);
            _driver = new GameObject("[Game.Core]") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            UnityEngine.Object.DontDestroyOnLoad(_driver);
            _driver.AddComponent<CoreClockDriver>().Root = this;
            if (Services.Flags.IsOn(VerboseBootFlag))
                Debug.Log("[Core] booted: step " + Services.Clock.StepSeconds + " s, flags:\n" + Services.Flags.ToCanonicalString());
        }

        public void Dispose()
        {
            if (_driver != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_driver); else UnityEngine.Object.DestroyImmediate(_driver); }
            _driver = null;
            Services.Dispose();
        }
    }

    /// <summary>Advances the simulation clock once per physics step, before any other script's FixedUpdate.</summary>
    [DefaultExecutionOrder(-32000)]
    [AddComponentMenu("")]
    public sealed class CoreClockDriver : MonoBehaviour
    {
        internal CoreRoot Root;
        private void FixedUpdate() { if (Root != null) Root.Services.Clock.Advance(); }
    }

    /// <summary>
    /// The single static composition root (MODULE.md, "Unity adapter"). Reset on every Play Mode entry, so it
    /// works with domain reload disabled; shut down when Play Mode exits or the player quits.
    /// </summary>
    public static class CoreRuntime
    {
        private static CoreRoot s_root;

        /// <summary>Raised once per Play session after the root exists and the clock is driving; composition roots install modules here.</summary>
        public static event Action<CoreRoot> Booted;

        public static bool IsBooted => s_root != null;
        public static CoreRoot Root => s_root ?? throw new InvalidOperationException("[Core] not booted: CoreRuntime.Root is only available in Play Mode or a player.");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEntry() => Shutdown();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Shutdown();
            var config = CoreConfigAsset.Load();
#if UNITY_EDITOR
            if (config != null && config.RefreshFromFolder()) UnityEditor.AssetDatabase.SaveAssets(); // a module added or removed a flag file
#endif
            var root = CoreRoot.Create(config, Application.persistentDataPath, Application.version);
            s_root = root;
            root.StartDriving();
            Booted?.Invoke(root);
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        public static void Shutdown()
        {
            var root = s_root;
            s_root = null;
            root?.Dispose();
        }
    }
}
