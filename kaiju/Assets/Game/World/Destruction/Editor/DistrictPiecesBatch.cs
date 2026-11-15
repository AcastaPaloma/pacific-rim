// Thin Unity entry for the `district_pieces@v1` writer (T-036c). The work is DistrictPiecesExporter (engine-free, dotnet-tested); this only
// loads the district tiles through the same engine-free BuildingsLayer the importer uses and writes the file.
//   unity run <project> -- -executeMethod Game.World.Destruction.Editor.DistrictPiecesBatch.Write [-piecesOut <file>] [-piecesTiles L0_26_36,L0_26_37,...]
// Defaults: evidence/T-036c/district_pieces.json, the 15 district tiles (Golden Gate 3 x 3, SoMa 3 x 2). Exit code 0 on success, 1 on any error.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.World.Geo;
using UnityEditor;
using UnityEngine;

namespace Game.World.Destruction.Editor
{
    public static class DistrictPiecesBatch
    {
        public const string DefaultTiles =
            "L0_25_36,L0_25_37,L0_25_38,L0_26_36,L0_26_37,L0_26_38,L0_27_36,L0_27_37,L0_27_38,L0_29_35,L0_29_36,L0_30_35,L0_30_36,L0_31_35,L0_31_36";

        static string Arg(string name, string fallback)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        public static void Write()
        {
            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string outPath = Arg("-piecesOut", Path.Combine(projectRoot, "evidence", "T-036c", "district_pieces.json"));
                string tilesArg = Arg("-piecesTiles", DefaultTiles);
                var settings = GeoSettings.Load(Path.Combine(Application.dataPath, "Game", "Config", "WorldGeo.json"));
                string tilesRoot = Path.Combine(Application.streamingAssetsPath, "Geo", "tiles", "v1");
                var tiles = new List<DistrictTileBuildings>();
                foreach (string name in tilesArg.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    if (!GeoTileId.TryParse(name, out GeoTileId id)) throw new ArgumentException("bad tile key " + name);
                    var ctx = new GeoTileContext(GeoTileSetLoader.TileDirectory(tilesRoot, id), settings, false);
                    var model = new GeoTileModel(ctx, new IGeoLayer[] { new BuildingsLayer() });
                    tiles.Add(new DistrictTileBuildings { Tile = id, Buildings = model.Buildings });
                }
                var pieces = DistrictPiecesExporter.Build(tiles);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, DistrictPiecesExporter.ToJson(pieces));
                Debug.Log($"[DistrictPiecesBatch] {tiles.Count} tiles, {tiles.Sum(t => t.Buildings.Count)} buildings, {pieces.Count} pieces -> {outPath}");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[DistrictPiecesBatch] failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
