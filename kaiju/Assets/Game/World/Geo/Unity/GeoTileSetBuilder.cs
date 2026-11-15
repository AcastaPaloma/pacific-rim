// Staged multi-tile build (T-021 step 2): one tile per BuildNext() call so a caller can spread the cost over frames.
// Tile roots sit at (tile origin - floating origin); seams and stitching are step 3.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.World.Geo.Unity
{
    public sealed class GeoTileSetBuilder
    {
        readonly GeoTileSet _set;
        readonly IReadOnlyDictionary<string, double> _settings;
        readonly List<GeoTileId> _order;
        readonly List<GeoTileInstance> _built = new List<GeoTileInstance>();
        int _next;

        public GeoTileSet Set => _set;
        public GeoWorldService World { get; }
        public GameObject Root { get; }
        public GeoTileId Reference { get; private set; }
        public double FloatingOriginX { get; private set; }
        public double FloatingOriginZ { get; private set; }
        public bool BuildingColliders = true;
        public bool TerrainCollider = true;
        public IReadOnlyList<GeoTileInstance> Instances => _built;
        public int Pending => _order.Count - _next;
        public bool Done => _next >= _order.Count;

        /// <param name="reference">Tile whose origin is the initial floating origin (must be in the set).</param>
        public GeoTileSetBuilder(GeoTileSet set, GeoTileId reference, IReadOnlyDictionary<string, double> settings, GeoWorldService world = null)
        {
            if (!set.Contains(reference)) throw new ArgumentException("reference tile " + reference + " is not in the set");
            _set = set; _settings = settings;
            _order = new List<GeoTileId>(set.Tiles);
            _order.Remove(reference); _order.Insert(0, reference);
            World = world ?? new GeoWorldService();
            Reference = reference;
            FloatingOriginX = reference.OriginX; FloatingOriginZ = reference.OriginZ;
            Root = new GameObject("GeoTileSet");
        }

        /// <summary>Builds the next tile (the reference first) and registers it with the world service; null when all are built.</summary>
        public GeoTileInstance BuildNext()
        {
            if (Done) return null;
            var id = _order[_next++];
            GeoTileSet.OffsetFrom(Reference, id, out var dx, out var dz);   // exact multiples of 2048 m
            var inst = GeoTileBuilder.Build(_set.Directory(id), new GeoBuildOptions
            {
                FloatingOriginX = FloatingOriginX, FloatingOriginZ = FloatingOriginZ,
                Settings = _settings, BuildingColliders = BuildingColliders, TerrainCollider = TerrainCollider,
                VerifyHashes = false, Parent = Root.transform,
            });
            if (inst.transform.localPosition != new Vector3((float)dx, 0f, (float)dz)) throw new InvalidOperationException(id + ": root not at its tile offset");
            _built.Add(inst);
            World.Add(inst);
            return inst;
        }

        /// <summary>Builds every remaining tile in one call.</summary>
        public void BuildAll() { while (BuildNext() != null) { } }

        /// <summary>Floating-origin rebase (F-042) for all tile roots built so far; later tiles use the new origin. Origin must be a tile corner.</summary>
        public void ShiftOrigin(double originX, double originZ)
        {
            if (originX % GeoTileId.BaseSizeM != 0 || originZ % GeoTileId.BaseSizeM != 0) throw new ArgumentException("origin must be a tile corner");
            foreach (var t in _built) t.ShiftOrigin(originX, originZ);
            FloatingOriginX = originX; FloatingOriginZ = originZ;
        }
    }
}
