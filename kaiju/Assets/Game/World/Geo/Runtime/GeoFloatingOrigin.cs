// Engine-free floating-origin policy (T-021 step 4a, M-015). The origin is always a tile corner (a multiple of the tile size),
// so a rebase moves tile roots by exact integers. A body is kept within RebaseDistanceM of the origin on both axes.
using System;

namespace Game.World.Geo
{
    public static class GeoFloatingOrigin
    {
        public const double DefaultRebaseDistanceM = 3072.0;

        /// <summary>True when the body is further than <paramref name="rebaseDistanceM"/> from the origin on either axis.</summary>
        public static bool NeedsRebase(double originX, double originZ, double bodyX, double bodyZ, double rebaseDistanceM = DefaultRebaseDistanceM) =>
            Math.Abs(bodyX - originX) > rebaseDistanceM || Math.Abs(bodyZ - originZ) > rebaseDistanceM;

        /// <summary>New origin: the corner of the tile containing the body when a rebase is needed; otherwise the current origin.</summary>
        public static void Update(double originX, double originZ, double bodyX, double bodyZ, out double newOriginX, out double newOriginZ,
                                  double rebaseDistanceM = DefaultRebaseDistanceM, double tileSizeM = GeoTileId.BaseSizeM)
        {
            newOriginX = originX; newOriginZ = originZ;
            if (!NeedsRebase(originX, originZ, bodyX, bodyZ, rebaseDistanceM)) return;
            newOriginX = originX + Math.Floor((bodyX - originX) / tileSizeM) * tileSizeM;
            newOriginZ = originZ + Math.Floor((bodyZ - originZ) / tileSizeM) * tileSizeM;
        }
    }
}
