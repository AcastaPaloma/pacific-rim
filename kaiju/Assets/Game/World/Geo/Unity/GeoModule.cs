// Registration of World.Geo with Core (service registry + flags + bus), and the attribution asset.
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.World.Geo.Unity
{
    /// <summary>IGeoWorld over the tiles built in this session.</summary>
    public sealed class GeoWorldService : IGeoWorld
    {
        readonly List<GeoTileInstance> _tiles = new List<GeoTileInstance>();
        readonly List<GeoTileId> _ids = new List<GeoTileId>();
        readonly IEventBus _bus;

        public GeoWorldService(IEventBus bus = null) { _bus = bus; }

        public IReadOnlyList<GeoTileId> Tiles => _ids;
        public double WaterLevelNavd88 => _tiles.Count > 0 ? _tiles[0].Model.Context.WaterLevelNavd88 : double.NaN;

        public void Add(GeoTileInstance t) { _tiles.Add(t); _ids.Add(t.Model.Tile); }

        GeoTileInstance Owner(BuildingId id)
        {
            foreach (var t in _tiles) if (t.Model.ById.ContainsKey(id)) return t;
            return null;
        }

        public bool TryGetBuilding(BuildingId id, out GeoBuildingInfo info)
        {
            info = default;
            var t = Owner(id);
            if (t == null || t.Model.Removed.Contains(id)) return false;
            var b = t.Model.ById[id];
            double top = b.BaseNavd88M + b.HeightM;
            if (b.HasParts && t.Model.PartsOf.TryGetValue(id, out var parts))
                foreach (var p in parts) top = Math.Max(top, p.BaseNavd88M + p.HeightM);
            info = new GeoBuildingInfo
            {
                Id = id, PartOf = b.PartOf, BaseNavd88M = b.BaseNavd88M, TopNavd88M = top, HeightSource = b.HeightSource, HasParts = b.HasParts,
                AnchorX = t.Model.Context.OriginX + b.AnchorLocalX, AnchorZ = t.Model.Context.OriginZ + b.AnchorLocalZ,
            };
            return true;
        }

        public int RemoveBuilding(BuildingId id)
        {
            var t = Owner(id);
            if (t == null) return 0;
            var removed = t.RemoveBuilding(id);
            if (_bus != null)
                foreach (var b in removed)
                    _bus.Publish(new GeoBuildingRemoved { Building = b.Id, Structure = b.PartOf.IsValid ? b.PartOf : b.Id });
            return removed.Count;
        }

        public bool IsRemoved(BuildingId id) { var t = Owner(id); return t != null && t.Model.Removed.Contains(id); }

        public double SampleTerrain(double x, double z)
        {
            foreach (var t in _tiles)
                if (t.Model.Tile.Contains(x, z)) return t.Model.Heights.SampleData(x - t.Model.Context.OriginX, z - t.Model.Context.OriginZ);
            return double.NaN;
        }
    }

    public static class GeoModule
    {
        /// <summary>Composition-root hook. Registers IGeoWorld only when world.geo.enabled is on (default off, MODULARITY rule 5).</summary>
        public static GeoWorldService Install(IServiceRegistry registry, IFeatureFlags flags)
        {
            if (flags == null || !flags.IsOn(GeoFlags.Enabled)) return null;
            IEventBus bus = null;
            registry?.TryResolve(out bus);
            var svc = new GeoWorldService(bus);
            registry?.Register<IGeoWorld>(svc);
            return svc;
        }
    }

    /// <summary>Credits asset: every distinct attribution string of the shipped tile manifests (M-005.8; OSMF guidance §1.5.10).</summary>
    public sealed class GeoAttribution : ScriptableObject
    {
        [Tooltip("Shown on the credits screen and the start-up splash, verbatim.")]
        public string[] Lines = Array.Empty<string>();
        [Tooltip("Tiles whose manifests these lines were collected from.")]
        public string[] Tiles = Array.Empty<string>();
        public string Notes = "OSM-derived layers (buildings, roads, water) are ODbL derivative-database candidates (Q-023): publish them under ODbL; legal review before release.";
    }
}
