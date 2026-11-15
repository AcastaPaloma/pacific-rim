// Damage proxies: how an infrastructure target is registered with World.Destruction's IDamageModel (Assumed, ModelRevision 1).
// IDamageModel takes BuildingAttributes only, so each target is described as an equivalent concrete/steel structure by height, footprint and
// levels. Only tagged or measured values are used; the rest are the constants below (R-021 capacity model; no infrastructure-specific
// fragility source is read yet, tracked in MODULE.md).
using System;
using Game.World.Destruction;

namespace Game.World.Infrastructure
{
    public static class InfraDamageProfile
    {
        // Assumed defaults, used only when the OSM record has no value.
        public const double BridgeHeightM = 12.0, BridgeDefaultWidthM = 7.0, BridgeLaneM = 3.5;
        public const double TowerHeightM = 40.0, TowerFootprintM2 = 100.0;
        public const double LineHeightM = 15.0, LineCorridorM = 3.0;
        public const double SubstationHeightM = 8.0, PlantHeightM = 20.0, CraneHeightM = 50.0, CraneFootprintM2 = 400.0;
        public const double RunwayHeightM = 1.0, RunwayDefaultWidthM = 45.0;
        public const double TerminalHeightM = 15.0, HangarHeightM = 20.0, ControlTowerHeightM = 40.0, ControlTowerFootprintM2 = 150.0;
        public const double MinFootprintM2 = 25.0;

        static double Or(double v, double fallback) => double.IsNaN(v) || v <= 0 ? fallback : v;

        public static BuildingAttributes AttributesOf(InfraRecord r)
        {
            double h, a;
            switch (r.Id.Kind)
            {
                case InfraKind.Bridge:
                    h = BridgeHeightM;
                    a = Or(r.LengthM, 20) * Or(r.WidthM, double.IsNaN(r.Lanes) || r.Lanes <= 0 ? BridgeDefaultWidthM : r.Lanes * BridgeLaneM);
                    break;
                case InfraKind.PowerTower: h = Or(r.HeightM, TowerHeightM); a = TowerFootprintM2; break;
                case InfraKind.PowerLine: h = LineHeightM; a = Or(r.LengthM, 100) * LineCorridorM; break;
                case InfraKind.Substation: h = SubstationHeightM; a = Or(r.AreaM2, 400); break;
                case InfraKind.PowerPlant: h = PlantHeightM; a = Or(r.AreaM2, 2000); break;
                case InfraKind.PortCrane: h = Or(r.HeightM, CraneHeightM); a = CraneFootprintM2; break;
                case InfraKind.Runway: h = RunwayHeightM; a = Or(r.LengthM, 1000) * Or(r.WidthM, RunwayDefaultWidthM); break;
                case InfraKind.Terminal: h = Or(r.HeightM, TerminalHeightM); a = Or(r.AreaM2, 2000); break;
                case InfraKind.Hangar: h = Or(r.HeightM, HangarHeightM); a = Or(r.AreaM2, 1000); break;
                case InfraKind.ControlTower: h = Or(r.HeightM, ControlTowerHeightM); a = ControlTowerFootprintM2; break;
                default: throw new ArgumentException("Unknown kind " + r.Id.Kind, nameof(r));
            }
            return new BuildingAttributes { HeightM = h, Levels = 0, FootprintM2 = Math.Max(a, MinFootprintM2), Residential = false, YearBuilt = 0 };
        }
    }
}
