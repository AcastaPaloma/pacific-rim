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
        readonly Dictionary<GeoTileId, GeoTileInstance> _byTile = new Dictionary<GeoTileId, GeoTileInstance>();
        // building ids are unique across a tile set (GeoSeams.AssertSingleOwner), so one global dictionary is safe
        readonly Dictionary<BuildingId, GeoTileInstance> _owners = new Dictionary<BuildingId, GeoTileInstance>();
        readonly List<GeoTileId> _ids = new List<GeoTileId>();
        readonly IEventBus _bus;

        public GeoWorldService(IEventBus bus = null) { _bus = bus; }

        public IReadOnlyList<GeoTileId> Tiles => _ids;
        public double WaterLevelNavd88 => _tiles.Count > 0 ? _tiles[0].Model.Context.WaterLevelNavd88 : double.NaN;

        public void Add(GeoTileInstance t)
        {
            _tiles.Add(t); _ids.Add(t.Model.Tile); _byTile[t.Model.Tile] = t;
            foreach (var kv in t.Model.ById) _owners[kv.Key] = t;
        }

        GeoTileInstance Owner(BuildingId id) => _owners.TryGetValue(id, out var t) ? t : null;

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
            if (!_byTile.TryGetValue(GeoTileId.Containing(x, z, _ids.Count > 0 ? _ids[0].Level : 0), out var t)) return double.NaN;
            return t.Model.Heights.SampleData(x - t.Model.Context.OriginX, z - t.Model.Context.OriginZ);
        }
    }

    public static class GeoModule
    {
        /// <summary>Tile set built when none is requested: the single M0 tile (existing scenes and tests unchanged).</summary>
        public static readonly string[] DefaultTileKeys = { "L0_30_36" };

        /// <summary>Web demo: root that replaces Application.streamingAssetsPath for File IO. A WebGL player cannot File-read
        /// StreamingAssets (it is an http URL), so the slice preloads the files into the in-memory filesystem and sets this. Null = streamingAssetsPath.</summary>
        public static string StreamingRootOverride;
        public static string StreamingRoot => StreamingRootOverride ?? Application.streamingAssetsPath;

        public static string DefaultTilesRoot => System.IO.Path.Combine(StreamingRoot, "Geo/tiles/v1");

        /// <summary>Loads the tile set (manifests validated, hashes not re-verified at runtime) and returns a staged builder; call BuildNext() once per tile.
        /// keys null = DefaultTileKeys; the first key holds the floating origin.</summary>
        public static GeoTileSetBuilder CreateTileSetBuilder(IEnumerable<string> keys = null, string tilesRoot = null, IReadOnlyDictionary<string, double> settings = null, GeoWorldService world = null)
        {
            var ids = GeoTileSet.ParseKeys(keys ?? DefaultTileKeys);
            var set = GeoTileSetLoader.Load(tilesRoot ?? DefaultTilesRoot, ids, verifyHashes: false);
            return new GeoTileSetBuilder(set, ids[0], settings ?? GeoTileBuilder.LoadSettings(), world);
        }

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
