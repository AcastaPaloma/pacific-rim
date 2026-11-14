// Right-triangulated irregular network (RTIN, after Evans/Kirkpatrick/Townsend 2001 and mapbox/martini, ISC):
// crack-free adaptive mesh of a (2^k + 1)^2 heightfield with a stated maximum vertical error.
using System;
using System.Collections.Generic;

namespace Game.World.Geo
{
    public sealed class RtinResult
    {
        public readonly List<GeoMeshData> Chunks = new List<GeoMeshData>();
        public float[] RenderHeights;     // heights of the mesh at every lattice node
        public double MaxErrorM, RmsErrorM;
        public int Triangles, Vertices;
    }

    public static class RtinMesher
    {
        /// <summary>
        /// Meshes h (row = z, col = x, size n = 2^k + 1) so that no node deviates more than maxError from the mesh,
        /// with no triangle larger than one chunk (chunkCells cells), so chunks can be culled independently.
        /// </summary>
        public static RtinResult Build(float[] h, int n, double spacing, float maxError, int chunkCells, string layer = "terrain")
        {
            int tile = n - 1;
            if ((tile & (tile - 1)) != 0) throw new ArgumentException("grid must be 2^k + 1");
            if (chunkCells <= 0 || tile % chunkCells != 0) throw new ArgumentException("chunkCells must divide the tile");
            var errors = new float[n * n];
            long numTriangles = (long)tile * tile * 2 - 2;
            long numParent = numTriangles - (long)tile * tile;
            // bottom-up error accumulation over the implicit binary triangle tree
            for (long i = numTriangles - 1; i >= 0; i--)
            {
                long id = i + 2;
                int ax = 0, ay = 0, bx = 0, by = 0, cx = 0, cy = 0;
                if ((id & 1) != 0) { bx = by = cx = tile; } else { ax = ay = cy = tile; }
                while ((id >>= 1) > 1)
                {
                    int mx0 = (ax + bx) >> 1, my0 = (ay + by) >> 1;
                    if ((id & 1) != 0) { bx = ax; by = ay; ax = cx; ay = cy; }
                    else { ax = bx; ay = by; bx = cx; by = cy; }
                    cx = mx0; cy = my0;
                }
                int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                float interp = (h[ay * n + ax] + h[by * n + bx]) * 0.5f;
                int mi = my * n + mx;
                float e = Math.Abs(interp - h[mi]);
                if (e > errors[mi]) errors[mi] = e;
                if (i < numParent)
                {
                    int ccx = mx + my - ay, ccy = my + ax - mx;
                    int li = ((ay + ccy) >> 1) * n + ((ax + ccx) >> 1);
                    int ri = ((by + ccy) >> 1) * n + ((bx + ccx) >> 1);
                    float m = Math.Max(errors[li], errors[ri]);
                    if (m > errors[mi]) errors[mi] = m;
                }
            }

            int chunks = tile / chunkCells;
            var tris = new List<int>[chunks * chunks];
            for (int c = 0; c < tris.Length; c++) tris[c] = new List<int>();
            var stack = new Stack<int[]>();
            stack.Push(new[] { 0, 0, tile, tile, tile, 0 });
            stack.Push(new[] { tile, tile, 0, 0, 0, tile });
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                int ax = t[0], ay = t[1], bx = t[2], by = t[3], cx = t[4], cy = t[5];
                int mx = (ax + bx) >> 1, my = (ay + by) >> 1;
                bool big = Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by)) > chunkCells;
                if (Math.Abs(ax - cx) + Math.Abs(ay - cy) > 1 && (big || errors[my * n + mx] > maxError))
                {
                    stack.Push(new[] { cx, cy, ax, ay, mx, my });
                    stack.Push(new[] { bx, by, cx, cy, mx, my });
                }
                else
                {
                    int ccx = Math.Min((ax + bx + cx) / 3 / chunkCells, chunks - 1);
                    int ccy = Math.Min((ay + by + cy) / 3 / chunkCells, chunks - 1);
                    var l = tris[ccy * chunks + ccx];
                    l.Add(ay * n + ax); l.Add(by * n + bx); l.Add(cy * n + cx);
                }
            }

            var res = new RtinResult { RenderHeights = new float[n * n] };
            var filled = new bool[n * n];
            for (int c = 0; c < tris.Length; c++)
            {
                var l = tris[c];
                var map = new Dictionary<int, int>();
                var pos = new List<float>();
                var idx = new int[l.Count];
                for (int k = 0; k < l.Count; k++)
                {
                    int node = l[k];
                    if (!map.TryGetValue(node, out int vi))
                    {
                        vi = map.Count;
                        map[node] = vi;
                        pos.Add((float)((node % n) * spacing)); pos.Add(h[node]); pos.Add((float)((node / n) * spacing));
                    }
                    idx[k] = vi;
                }
                // winding: upward-facing (clockwise seen from +y): in (x, z) the signed area must be negative
                for (int k = 0; k < idx.Length; k += 3)
                {
                    float x0 = pos[idx[k] * 3], z0 = pos[idx[k] * 3 + 2];
                    float x1 = pos[idx[k + 1] * 3], z1 = pos[idx[k + 1] * 3 + 2];
                    float x2 = pos[idx[k + 2] * 3], z2 = pos[idx[k + 2] * 3 + 2];
                    if ((x1 - x0) * (z2 - z0) - (x2 - x0) * (z1 - z0) > 0) { int s = idx[k + 1]; idx[k + 1] = idx[k + 2]; idx[k + 2] = s; }
                    Raster(h, n, l[k], l[k + 1], l[k + 2], res.RenderHeights, filled);
                }
                var p = pos.ToArray();
                var mesh = new GeoMeshData
                {
                    Layer = layer, Material = "terrain", Name = "terrain_" + (c % chunks) + "_" + (c / chunks),
                    Positions = p, Indices = idx, ChunkX = c % chunks, ChunkZ = c / chunks,
                    Normals = SmoothNormals(p, idx), Uv = new float[p.Length / 3 * 2],
                };
                double size = tile * spacing;
                for (int v = 0; v < p.Length / 3; v++) { mesh.Uv[2 * v] = (float)(p[3 * v] / size); mesh.Uv[2 * v + 1] = (float)(p[3 * v + 2] / size); }
                res.Chunks.Add(mesh);
                res.Triangles += idx.Length / 3;
                res.Vertices += p.Length / 3;
            }
            double se = 0, me = 0;
            for (int i = 0; i < h.Length; i++)
            {
                double d = Math.Abs(res.RenderHeights[i] - h[i]);
                se += d * d; if (d > me) me = d;
            }
            res.MaxErrorM = me;
            res.RmsErrorM = Math.Sqrt(se / h.Length);
            return res;
        }

        // Fill the mesh height at every lattice node covered by triangle (a, b, c).
        static void Raster(float[] h, int n, int a, int b, int c, float[] outH, bool[] filled)
        {
            int ax = a % n, ay = a / n, bx = b % n, by = b / n, cx = c % n, cy = c / n;
            int x0 = Math.Min(ax, Math.Min(bx, cx)), x1 = Math.Max(ax, Math.Max(bx, cx));
            int y0 = Math.Min(ay, Math.Min(by, cy)), y1 = Math.Max(ay, Math.Max(by, cy));
            double den = (double)(by - cy) * (ax - cx) + (double)(cx - bx) * (ay - cy);
            if (den == 0) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * n + x;
                    if (filled[i]) continue;
                    double wa = ((by - cy) * (double)(x - cx) + (cx - bx) * (double)(y - cy)) / den;
                    double wb = ((cy - ay) * (double)(x - cx) + (ax - cx) * (double)(y - cy)) / den;
                    double wc = 1 - wa - wb;
                    if (wa < -1e-9 || wb < -1e-9 || wc < -1e-9) continue;
                    outH[i] = (float)(wa * h[a] + wb * h[b] + wc * h[c]);
                    filled[i] = true;
                }
        }

        public static float[] SmoothNormals(float[] p, int[] idx)
        {
            var nrm = new float[p.Length];
            for (int k = 0; k < idx.Length; k += 3)
            {
                int a = idx[k] * 3, b = idx[k + 1] * 3, c = idx[k + 2] * 3;
                float ux = p[b] - p[a], uy = p[b + 1] - p[a + 1], uz = p[b + 2] - p[a + 2];
                float vx = p[c] - p[a], vy = p[c + 1] - p[a + 1], vz = p[c + 2] - p[a + 2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                foreach (int q in new[] { a, b, c }) { nrm[q] += nx; nrm[q + 1] += ny; nrm[q + 2] += nz; }
            }
            for (int q = 0; q < nrm.Length; q += 3)
            {
                float l = (float)Math.Sqrt(nrm[q] * nrm[q] + nrm[q + 1] * nrm[q + 1] + nrm[q + 2] * nrm[q + 2]);
                if (l > 0) { nrm[q] /= l; nrm[q + 1] /= l; nrm[q + 2] /= l; } else nrm[q + 1] = 1;
            }
            return nrm;
        }
    }
}
