// T-122: engine-free adapters that wire the M2 meta modules (Economy, Salvage, Roster, Refit) to Hangar, Destruction and each other.
// Everything here runs only when a meta.* flag installs the services (see MetaInstaller); with the flags off none of it is constructed.
using System;
using System.Collections.Generic;
using Game.Core;
using Game.Hangar;
using Game.Meta.Economy;
using Game.Meta.Refit;
using Game.Meta.Roster;
using Game.Meta.Salvage;
using Game.World.Destruction;
using Game.World.Geo;

namespace Game.Composition
{
    /// <summary>One building as the economy sees it: replacement value = footprint x floors x USD per m2 (PriceTable.BuildingValueUsd).</summary>
    public readonly struct BuildingAssetInfo
    {
        public BuildingAssetInfo(string id, long footprintM2, int floors) { Id = id; FootprintM2 = footprintM2; Floors = floors; }
        public string Id { get; }
        public long FootprintM2 { get; }
        public int Floors { get; }

        /// <summary>Levels from the attributes, else round(H / 4.22 m) with a minimum of 1 (R-004 section 1.4); footprint rounded to whole m2, minimum 1.</summary>
        public static BuildingAssetInfo From(BuildingId id, in BuildingAttributes a)
        {
            int floors = a.Levels > 0 ? a.Levels : Math.Max(1, (int)Math.Round(a.HeightM / 4.22));
            long area = Math.Max(1L, (long)Math.Round(a.FootprintM2));
            return new BuildingAssetInfo(id.ToString(), area, floors);
        }
    }

    /// <summary>Replacement costs of contractable assets (R-104 section 3): the pool a contract offer draws from and the value of one asset by id.</summary>
    public interface IAssetValueProvider
    {
        /// <summary>Appends every known asset (fixed landmarks first, then buildings in input order).</summary>
        void CopyPool(List<ContractAsset> into);
        bool TryGetValue(string assetId, out long replacementUsd);
    }

    /// <summary>
    /// Fixed assets come from <c>economy_prices@v1</c> (bridges, cranes, substation); buildings come from a caller-supplied source (the Geo/Destruction layer
    /// knows the attributes, the economy table knows the USD per m2). Airport and rail rows are not in the table yet (R-104 Q-NEW-c): not invented here.
    /// </summary>
    public sealed class PriceTableAssetValues : IAssetValueProvider
    {
        private readonly PriceTable _prices;
        private readonly Func<IEnumerable<BuildingAssetInfo>> _buildings;

        public PriceTableAssetValues(PriceTable prices, Func<IEnumerable<BuildingAssetInfo>> buildings = null)
        { _prices = prices ?? throw new ArgumentNullException(nameof(prices)); _buildings = buildings; }

        public void CopyPool(List<ContractAsset> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            foreach (var kv in _prices.FixedAssets) into.Add(new ContractAsset(kv.Key, kv.Value.Usd));
            if (_buildings == null) return;
            foreach (var b in _buildings()) into.Add(new ContractAsset(b.Id, _prices.BuildingValueUsd(b.FootprintM2, b.Floors)));
        }

        public bool TryGetValue(string assetId, out long replacementUsd)
        {
            replacementUsd = 0;
            if (string.IsNullOrEmpty(assetId)) return false;
            if (_prices.FixedAssets.TryGetValue(assetId, out var f)) { replacementUsd = f.Usd; return true; }
            if (_buildings == null) return false;
            foreach (var b in _buildings())
                if (string.Equals(b.Id, assetId, StringComparison.Ordinal)) { replacementUsd = _prices.BuildingValueUsd(b.FootprintM2, b.Floors); return true; }
            return false;
        }
    }

    /// <summary>Refit is a hangar action (R-104 section 9): allowed only while the slot is in the Hangar phase and the mech is a flyable mech of that slot.</summary>
    public sealed class HangarRefitGate : IRefitGate
    {
        private readonly IHangarService _hangar;
        private readonly IRosterService _roster;

        /// <param name="hangar">Null = no hangar installed: every refit is refused.</param>
        /// <param name="roster">Null = no roster: only the phase is checked.</param>
        public HangarRefitGate(IHangarService hangar, IRosterService roster = null) { _hangar = hangar; _roster = roster; }

        public bool CanRefit(PlayerSlot slot, EntityId mech)
        {
            if (_hangar == null || !slot.IsValid) return false;
            if (_hangar.GetPhase(slot) != HangarPhase.Hangar) return false;
            if (_roster == null) return true;
            var m = _roster.Get(mech);
            return m != null && m.State == MechState.Flyable && m.Slot.Index == slot.Index;
        }
    }

    public sealed class MetaFightEndResult
    {
        public FightEndResult Roster { get; set; }
        /// <summary>Set when the mech was lost and salvage is installed; a replayed loss returns the stored claim with Replay = true.</summary>
        public SalvageClaim Salvage { get; set; }
        /// <summary>True when the hangar took the damage record (the mech survived and Hangar is installed).</summary>
        public bool EnteredHangar { get; set; }
        public MechBuildSpec FloorSpec { get; set; }
    }

    /// <summary>
    /// The Hangar-to-meta seam. <see cref="OnFightEnd"/> is what the fight-end flow calls instead of <c>IHangarService.EnterHangar</c> when the meta flags are on:
    /// Roster decides the loss (permadeath, floor chassis), the Hangar takes the record of a survivor, a lost mech's wreck is claimed by Salvage, and a floor
    /// chassis granted by the Roster is registered with Refit. The Hangar module itself is unchanged and knows nothing of the meta layer.
    /// </summary>
    public sealed class MetaFightEndBridge : IEventHandler<RefitApplied>
    {
        /// <summary>Fall energy per kg of the 40 m reference mech (g x CoM height 40 m), used when the build spec is unknown (salvage MODULE.md).</summary>
        public const double ReferenceFallJPerKg = 392.0;
        public const double Gravity = 9.81;

        private readonly IRosterService _roster;
        private readonly IHangarService _hangar;
        private readonly ISalvageService _salvage;
        private readonly IRefitService _refit;
        private readonly PriceTable _prices;
        private readonly ICoreErrorSink _errors;
        private readonly IEventBus _bus;

        public MetaFightEndBridge(IRosterService roster, PriceTable prices, IHangarService hangar = null, ISalvageService salvage = null, IRefitService refit = null,
                                  IEventBus bus = null, ICoreErrorSink errors = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _hangar = hangar; _salvage = salvage; _refit = refit; _errors = errors; _bus = bus;
            _bus?.Subscribe<RefitApplied>(this);
        }

        /// <summary>A refit that goes through <see cref="IRefitService.Apply"/> by any path updates the roster record.</summary>
        public void Handle(in RefitApplied e)
        {
            if (_roster.Get(e.Mech) != null) _roster.SetSpecHash(e.Mech, e.SpecHash);
        }

        /// <summary>Creates a roster mech and registers its build with Refit (when installed) under the same spec hash. Throws ArgumentException for an invalid build.</summary>
        public MechInstance CreateMech(PlayerSlot slot, string chassisId, long step, IReadOnlyDictionary<string, string> assignment = null, bool cityIssued = false)
        {
            if (_refit == null) return _roster.Create(slot, chassisId, 0, step);
            assignment = assignment ?? _refit.DefaultBuild(chassisId);
            if (!_refit.TryBuild(chassisId, assignment, out var spec, out var report))
                throw new ArgumentException("build is not valid: " + report.FirstViolation + " " + report.Subject + " " + report.Detail);
            var m = _roster.Create(slot, chassisId, spec.Hash, step);
            _refit.Register(m.MechId, slot, chassisId, assignment, cityIssued, step);
            return m;
        }

        /// <summary>Applies a hangar refit and carries the new spec hash to the roster. The gate and the money are Refit's.</summary>
        public RefitResult ApplyRefit(EntityId mech, IReadOnlyDictionary<string, string> assignment, long step)
        {
            if (_refit == null) throw new InvalidOperationException("meta.refit is not installed");
            var res = _refit.Apply(mech, assignment, step);
            if (res.Status == RefitStatus.Applied && res.Spec != null && _roster.Get(mech) != null) _roster.SetSpecHash(mech, res.Spec.Hash);
            return res;
        }

        public MetaFightEndResult OnFightEnd(IBodyDamageRecord record, long step, double pilotStrain = 0.0, SalvageLocation location = SalvageLocation.Land)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var mechId = _roster.ActiveMech(record.Slot);
            var mech = mechId.IsValid ? _roster.Get(mechId) : null;
            var fe = _roster.OnFightEnd(record, step, pilotStrain);
            var res = new MetaFightEndResult { Roster = fe };

            if (!fe.Lost)
            {
                if (_hangar != null) { _hangar.EnterHangar(record); res.EnteredHangar = true; }
                return res;
            }

            if (_salvage != null && mech != null && !fe.Replay)
            {
                var input = BuildWreck(mech, record, fe.WreckId, location);
                if (input != null) res.Salvage = _salvage.Claim(input, step);
            }
            if (fe.FloorGranted && _refit != null)
            {
                var floor = _roster.Get(fe.FloorMechId);
                if (floor != null && _refit.Chassis.Get(floor.Chassis) != null)
                {
                    res.FloorSpec = _refit.Register(floor.MechId, floor.Slot, floor.Chassis, _refit.DefaultBuild(floor.Chassis), true, step);
                    _roster.SetSpecHash(floor.MechId, res.FloorSpec.Hash);
                }
            }
            return res;
        }

        /// <summary>
        /// The wreck as Salvage sees it. With a registered build the parts are the build's parts split by price class (record part ids are matched to slot names,
        /// a slot without a record part takes the mean integrity of the record); otherwise the chassis composition of the economy table. Absorbed energy per part is
        /// 0 until the body damage record carries it (PartDamage has no energy field); the fall energy is g x CoM height of the build, or the 40 m reference.
        /// </summary>
        public SalvageInput BuildWreck(MechInstance mech, IBodyDamageRecord record, long wreckId, SalvageLocation location)
        {
            var recs = new List<PartDamage>();
            record.CopyParts(recs);
            double mean = 0; int n = 0;
            var byId = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var p in recs)
            {
                double f = p.MaxIntegrity > 0 ? Math.Min(1.0, Math.Max(0.0, p.Integrity / p.MaxIntegrity)) : 0.0;
                byId[p.PartId ?? ""] = f; mean += f; n++;
            }
            mean = n > 0 ? mean / n : 0.0;

            var parts = new List<SalvagePart>();
            double fall = ReferenceFallJPerKg;
            if (_refit != null && _refit.IsRegistered(mech.MechId))
            {
                var build = _refit.Get(mech.MechId);
                foreach (var kv in build.Parts)
                {
                    var def = _refit.Parts.Get(kv.Value);
                    if (def == null) continue;
                    double integ = byId.TryGetValue(kv.Key, out double f) ? f : mean;
                    foreach (var c in def.CompositionKg) AddPart(parts, kv.Key + ":" + c.Key, c.Key, c.Value, integ);
                }
                if (_refit.TryBuild(build.Chassis, build.Parts, out var spec, out _) && spec.ComY > 0) fall = Gravity * spec.ComY;
            }
            else if (_prices.Chassis.TryGetValue(mech.Chassis ?? "", out var comp))
            {
                foreach (var c in comp.Tonnes) AddPart(parts, "hull:" + c.Key, c.Key, checked(c.Value * 1000L), mean);
            }
            if (parts.Count == 0)
            {
                _errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Meta] no parts known for lost mech " + mech.MechId + " (chassis '" + mech.Chassis + "'); wreck " + wreckId + " not salvaged."));
                return null;
            }
            long totalPrice = 0;
            foreach (var p in parts) totalPrice = checked(totalPrice + p.PriceUsd);
            return new SalvageInput
            {
                WreckId = wreckId, SourceKind = SalvageSourceKind.Mech, Location = location, FallEnergyJPerKg = fall,
                BuildPriceUsd = totalPrice > 0 ? totalPrice : 1, Parts = parts.ToArray(),
            };
        }

        private void AddPart(List<SalvagePart> into, string id, string classId, long kg, double integrity)
        {
            if (kg <= 0) return;
            long price = _prices.QuotePart(classId, kg).PriceUsd;
            into.Add(new SalvagePart(id, classId, kg, 0.0, integrity, 1.0, price));
        }
    }
}
