// T-122: composition for the M2 meta layer. Loads the data files, installs Economy, Salvage, Roster and Refit behind their flags (meta.economy, meta.salvage,
// meta.permadeath, meta.refit), registers their save sections (economy@v1, salvage@v1, roster@v1, refit@v1) and builds the adapters. Engine-free.
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Refit;
using Game.Meta.Roster;
using Game.Meta.Salvage;

namespace Game.Composition
{
    /// <summary>Data the meta installer cannot know itself. Only read when a meta flag is on.</summary>
    public sealed class MetaInputs
    {
        public const string PricesPath = "Economy/economy_prices@v1.json";
        /// <summary>Optional: campaign-simulation overlay (fee, advance, floor loss levy). A missing file leaves the base prices untouched and is not an error.</summary>
        public const string TuningPath = "Economy/economy_tuning@v1.json";
        public const string YieldsPath = "Salvage/salvage_yields@v1.json";
        public const string PartsPath = "Refit/parts@v1.json";
        public const string ChassisPath = "Refit/chassis@v1.json";

        /// <summary>Returns the text of a file under the Config root (for example <see cref="PricesPath"/>); throws or returns null when it is missing.</summary>
        public Func<string, string> ReadConfig;
        /// <summary>Opening balance of the economy.</summary>
        public long StartingFunds;
        /// <summary>Buildings that may appear in contract offers (null = fixed assets only).</summary>
        public Func<IEnumerable<BuildingAssetInfo>> Buildings;

        public static MetaInputs FromDirectory(string configRoot, long startingFunds = 0, Func<IEnumerable<BuildingAssetInfo>> buildings = null)
        {
            if (string.IsNullOrEmpty(configRoot)) throw new ArgumentException("empty config root");
            return new MetaInputs { ReadConfig = rel => File.ReadAllText(Path.Combine(configRoot, rel)), StartingFunds = startingFunds, Buildings = buildings };
        }
    }

    /// <summary>What a meta install created. A null member means its flag is off (or a prerequisite or data file was missing, which is reported).</summary>
    public sealed class MetaHandle
    {
        public EconomyService Economy { get; internal set; }
        public SalvageService Salvage { get; internal set; }
        public RosterService Roster { get; internal set; }
        public RefitService Refit { get; internal set; }
        public PriceTable Prices { get; internal set; }
        /// <summary>economy_tuning@v1 when the file was present (already applied to <see cref="Prices"/>), else null.</summary>
        public EconomyTuning Tuning { get; internal set; }
        public IAssetValueProvider Assets { get; internal set; }
        public MetaFightEndBridge Bridge { get; internal set; }
        public EntityIdAllocator Allocator { get; internal set; }
    }

    public static class MetaInstaller
    {
        /// <summary>Price per tonne of every class of the economy table, the pricing Refit uses (same rule and rounding as the table).</summary>
        public static ClassPricing PricingFrom(PriceTable t)
        {
            var d = new Dictionary<string, long>();
            foreach (var kv in t.Classes) d[kv.Key] = kv.Value.UsdPerTonne;
            return new ClassPricing(d, t.SellBackBps);
        }

        /// <summary>
        /// Null when every meta flag is off (nothing registered, no file read). Salvage, Roster and Refit need Economy: with meta.economy off they are
        /// reported and skipped. Refit needs the hangar for its gate: without one every refit is refused (reported). Services are registered with
        /// <paramref name="saves"/> when one is supplied.
        /// </summary>
        public static MetaHandle Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors, MetaInputs inputs, SaveSystem saves, IHangarService hangar)
        {
            if (registry == null || flags == null) return null;
            bool wantEco = flags.IsOn(EconomyFlags.Economy), wantSalvage = flags.IsOn(SalvageFlags.Salvage), wantRoster = flags.IsOn(RosterFlags.Permadeath), wantRefit = flags.IsOn(RefitFlags.Refit);
            if (!wantEco && !wantSalvage && !wantRoster && !wantRefit) return null;

            var h = new MetaHandle();
            if (!wantEco)
            {
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] meta.salvage / meta.permadeath / meta.refit need meta.economy; nothing installed."));
                return h;
            }
            if (inputs?.ReadConfig == null)
            {
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] a meta flag is on but no MetaInputs.ReadConfig was supplied; nothing installed."));
                return h;
            }
            registry.TryResolve(out IEventBus bus);

            var prices = Load(errors, inputs, MetaInputs.PricesPath, PriceTable.Parse);
            if (prices == null) return h;
            h.Prices = prices;
            h.Tuning = LoadTuning(errors, inputs, prices);
            h.Economy = EconomyModule.Install(registry, flags, prices, bus, inputs.StartingFunds);
            saves?.Register(h.Economy);
            h.Assets = new PriceTableAssetValues(prices, inputs.Buildings);
            registry.Register<IAssetValueProvider>(h.Assets);

            if (wantSalvage)
            {
                var yields = Load(errors, inputs, MetaInputs.YieldsPath, YieldsTable.Parse);
                if (yields != null) { h.Salvage = SalvageModule.Install(registry, flags, yields, h.Economy, bus); saves?.Register(h.Salvage); }
            }

            if (wantRoster)
            {
                if (!registry.TryResolve(out EntityIdAllocator alloc)) { alloc = new EntityIdAllocator(); registry.Register<EntityIdAllocator>(alloc); }
                h.Allocator = alloc;
                h.Roster = RosterModule.Install(registry, flags, h.Economy, alloc, bus);
                saves?.Register(h.Roster);
            }

            if (wantRefit)
            {
                var parts = Load(errors, inputs, MetaInputs.PartsPath, PartCatalog.Parse);
                ChassisCatalog chassis = null;
                if (parts != null)
                {
                    string json = ReadText(errors, inputs, MetaInputs.ChassisPath);
                    if (json != null) chassis = Try(errors, MetaInputs.ChassisPath, () => ChassisCatalog.Parse(json, parts));
                }
                if (parts != null && chassis != null)
                {
                    if (hangar == null)
                        errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] meta.refit is on but no hangar is installed (hangar.stub + units.placeholderBody); every refit is refused."));
                    var gate = new HangarRefitGate(hangar, h.Roster);
                    h.Refit = RefitModule.Install(registry, flags, parts, chassis, PricingFrom(prices), h.Economy, bus, h.Salvage, gate);
                    saves?.Register(h.Refit);
                }
            }

            if (h.Roster != null)
            {
                h.Bridge = new MetaFightEndBridge(h.Roster, prices, hangar, h.Salvage, h.Refit, bus, errors, h.Economy, h.Tuning);
                registry.Register<MetaFightEndBridge>(h.Bridge);
            }
            registry.Register<MetaHandle>(h);
            return h;
        }

        private static EconomyTuning LoadTuning(ICoreErrorSink errors, MetaInputs inputs, PriceTable prices)
        {
            string text = null;
            try { text = inputs.ReadConfig(MetaInputs.TuningPath); } catch (Exception) { return null; }   // optional overlay: absent is fine
            if (string.IsNullOrEmpty(text)) return null;
            var t = Try(errors, MetaInputs.TuningPath, () => EconomyTuning.Parse(text));
            t?.ApplyTo(prices);
            return t;
        }

        private static string ReadText(ICoreErrorSink errors, MetaInputs inputs, string rel)
        {
            try
            {
                var text = inputs.ReadConfig(rel);
                if (!string.IsNullOrEmpty(text)) return text;
                errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] config file missing or empty: " + rel));
            }
            catch (Exception e) { errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] cannot read " + rel + ": " + e.Message)); }
            return null;
        }

        private static T Load<T>(ICoreErrorSink errors, MetaInputs inputs, string rel, Func<string, T> parse) where T : class
        {
            string text = ReadText(errors, inputs, rel);
            return text == null ? null : Try(errors, rel, () => parse(text));
        }

        private static T Try<T>(ICoreErrorSink errors, string rel, Func<T> f) where T : class
        {
            try { return f(); }
            catch (Exception e) { errors?.Report(new CoreError(CoreErrorKind.ConfigMalformed, "[Meta] " + rel + " is malformed: " + e.Message)); return null; }
        }
    }
}
