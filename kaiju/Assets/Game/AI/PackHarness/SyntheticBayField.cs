// T-115c: synthetic cost field for the m007_bay harness (map `m007_bay_synth@v1+water`), the stand-in for the Track 1 depth/softness raster (T-102 bake) until the real
// provider `m007_bay_real@v1` exists. Same IBayCostField interface the real adapter implements. Layout (R-103 6.2, simplified, all *Assumed*):
// a 7 km x 36 km water ellipse centred (23000, 28000); the southern 15 km of it is a wadable shelf (depth <= 15 m), the rest is a deep channel (up to 120 m,
// so a species is blocked where depth > 0.4 x height: Strider 24 m, Breacher 40 m, Bastion 64 m); a mud-flat band 15 % wider than the ellipse is soft ground.
using System;
using Game.AI.Pack;

namespace Game.AI.PackHarness
{
    public sealed class SyntheticBayField : IBayCostField
    {
        public const string MapId = "m007_bay_synth@v1+water";
        public const float CentreX = 23000f, CentreY = 28000f, SemiX = 3500f, SemiY = 18000f, ShelfTopY = 25000f, ShelfDepthM = 15f, MaxDepthM = 120f;
        private readonly float[] _depth, _soft;
        public float OriginX => 0f;
        public float OriginY => 0f;
        public float CellM { get; }
        public int Nx { get; }
        public int Ny { get; }

        public SyntheticBayField(float extentM = 60000f, float cellM = 250f)
        {
            CellM = cellM; Nx = Ny = (int)(extentM / cellM);
            _depth = new float[Nx * Ny]; _soft = new float[Nx * Ny];
            for (int iy = 0; iy < Ny; iy++)
                for (int ix = 0; ix < Nx; ix++)
                {
                    float x = (ix + 0.5f) * cellM, y = (iy + 0.5f) * cellM;
                    float u = (x - CentreX) / SemiX, v = (y - CentreY) / SemiY, r2 = u * u + v * v;
                    float d = 0f;
                    if (r2 < 1f) { d = MaxDepthM * (1f - r2); if (y < ShelfTopY) d = Math.Min(d, ShelfDepthM * (1f - r2) + 2f); }
                    _depth[iy * Nx + ix] = d;
                    _soft[iy * Nx + ix] = r2 >= 1f && r2 < 1.15f * 1.15f ? 1f : 0f;
                }
        }

        public float DepthM(int ix, int iy) => _depth[iy * Nx + ix];
        public float Softness(int ix, int iy) => _soft[iy * Nx + ix];
        public float DepthAt(float x, float y) { int ix = (int)(x / CellM), iy = (int)(y / CellM); return ix < 0 || iy < 0 || ix >= Nx || iy >= Ny ? 0f : _depth[iy * Nx + ix]; }
        public float SoftAt(float x, float y) { int ix = (int)(x / CellM), iy = (int)(y / CellM); return ix < 0 || iy < 0 || ix >= Nx || iy >= Ny ? 0f : _soft[iy * Nx + ix]; }
    }
}
