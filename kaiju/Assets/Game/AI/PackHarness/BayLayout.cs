// T-115d: scenario geometry of the m007_bay harness as data. `Synthetic` is the T-115 / T-115b / T-115c layout (60 km x 60 km, numbers unchanged, so the flat and water maps
// reproduce their logs bit for bit); `Real` is `m007_bay_real@v1` in BayTM@v1 metres (Pipelines/geo, origin SW of the Bay), placed on real geography. Real positions were
// converted from lon/lat with Pipelines/geo/kaiju_geo/common.py lonlat_to_baytm and are approximate (Assumed; T-105 infrastructure is not baked yet, so these are stand-ins
// at the real sites). Node order is fixed (0-1 bridges, 2-4 power, 5-8 cranes, 9 airport): the mixed scenario's guard clusters address nodes by index.
using System;
using System.Collections.Generic;

namespace Game.AI.PackHarness
{
    public sealed class BayLayout
    {
        public struct Node { public string Class; public byte C; public float X, Y, Hp; public Node(string cls, byte c, float x, float y, float hp) { Class = cls; C = c; X = x; Y = y; Hp = hp; } }

        public string Name;
        public Node[] Infra;
        public Node[] CrossInfra;
        public float HuntX, HuntY;       // hunt / churn / raid kaiju spawn and the centre of the mech patrol ring
        public float RaidX, RaidY;       // raid kaiju spawn (synthetic: the hunt spawn)
        public float MixedX, MixedY;
        public float CrossX, CrossY;
        public int[] GuardCluster0 = { 5, 6, 7, 8 };   // Port cranes (2 mechs)
        public int[] GuardCluster1 = { 4 };             // east power node (1 mech)
        public double[] TraceX, TraceZ;                 // emergence rupture trace (61 vertices)

        public static readonly BayLayout Synthetic = BuildSynthetic();
        public static readonly BayLayout Real = BuildReal();

        /// <summary>Diagnostic copy of the synthetic layout with the hunt spawn / patrol ring moved (translation-sensitivity check of T-115d).</summary>
        public static BayLayout SyntheticHuntShifted(float dx, float dy)
        {
            var l = (BayLayout)Synthetic.MemberwiseClone(); l.HuntX += dx; l.HuntY += dy; l.RaidX = l.HuntX; l.RaidY = l.HuntY; return l;
        }

        /// <summary>T-115e: the whole layout (infrastructure, spawns, rupture trace) translated by (dx, dy) metres. Used by the translation-invariance regression test; only valid on maps without a cost field.</summary>
        public BayLayout Translated(double dx, double dy)
        {
            var l = (BayLayout)MemberwiseClone();
            Node[] Mv(Node[] a) { var r = new Node[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = new Node(a[i].Class, a[i].C, (float)(a[i].X + dx), (float)(a[i].Y + dy), a[i].Hp); return r; }
            l.Infra = Mv(Infra); l.CrossInfra = Mv(CrossInfra);
            l.HuntX = (float)(HuntX + dx); l.HuntY = (float)(HuntY + dy); l.RaidX = (float)(RaidX + dx); l.RaidY = (float)(RaidY + dy);
            l.MixedX = (float)(MixedX + dx); l.MixedY = (float)(MixedY + dy); l.CrossX = (float)(CrossX + dx); l.CrossY = (float)(CrossY + dy);
            l.TraceX = new double[TraceX.Length]; l.TraceZ = new double[TraceZ.Length];
            for (int i = 0; i < TraceX.Length; i++) { l.TraceX[i] = TraceX[i] + dx; l.TraceZ[i] = TraceZ[i] + dy; }
            return l;
        }

        public static BayLayout For(string map) => map == "real" ? Real : Synthetic;

        static BayLayout BuildSynthetic()
        {
            var l = new BayLayout
            {
                Name = "m007_bay_synth@v1", HuntX = 6000f, HuntY = 2000f, RaidX = 6000f, RaidY = 2000f, MixedX = 10000f, MixedY = 12000f, CrossX = 14000f, CrossY = 34000f,
                CrossInfra = new[] { new Node("bridge", 2, 34000, 38000, 1500) },
                Infra = new[]
                {
                    new Node("bridge", 2, 34000, 38000, 1500), new Node("bridge", 2, 14000, 46000, 1500),
                    new Node("power", 1, 20000, 38000, 600), new Node("power", 1, 28000, 24000, 600), new Node("power", 1, 46000, 33500, 600),
                    new Node("crane", 0, 39850, 30000, 300), new Node("crane", 0, 40050, 30100, 300), new Node("crane", 0, 40150, 29900, 300), new Node("crane", 0, 39950, 29850, 300),
                    new Node("airport", 3, 30000, 11800, 2000),
                },
            };
            int n = 61; l.TraceX = new double[n]; l.TraceZ = new double[n];
            for (int i = 0; i < n; i++) { l.TraceX[i] = 10000 + i * 500.0; l.TraceZ[i] = 22000 + 2500 * Math.Sin(i * 0.15); }
            return l;
        }

        static BayLayout BuildReal()
        {
            var l = new BayLayout
            {
                Name = "m007_bay_real@v1",
                HuntX = 115019f, HuntY = 61980f,                 // Livermore valley: open land, 40 km from every target (hunt / churn do not use water)
                RaidX = 51555f, RaidY = 82974f,                  // Marin Headlands: the Golden Gate (deep) lies between it and every target
                MixedX = 54138f, MixedY = 55220f,                // Pacifica coast: Pacific side of the Peninsula, the Bay (San Bruno channel side) between it and the Port
                CrossX = 51555f, CrossY = 82974f,                // Marin Headlands (unused by the clause matrix)
                CrossInfra = new[] { new Node("bridge", 2, 56029, 75861, 1500) },
                Infra = new[]
                {
                    new Node("bridge", 2, 56029, 75861, 1500),    // Golden Gate Bridge, San Francisco abutment
                    new Node("bridge", 2, 68535, 77291, 1500),    // Bay Bridge, Oakland touchdown
                    new Node("power", 1, 63378, 70412, 600),      // Potrero (SF)
                    new Node("power", 1, 106027, 101674, 600),    // Pittsburg (Carquinez / Delta)
                    new Node("power", 1, 79725, 70081, 600),      // Coliseum (East Bay), the mixed east guard cluster
                    new Node("crane", 0, 68610, 75626, 300), new Node("crane", 0, 68810, 75726, 300), new Node("crane", 0, 68910, 75526, 300), new Node("crane", 0, 68710, 75476, 300),   // Port of Oakland
                    new Node("airport", 3, 64291, 55095, 2000),   // SFO
                },
            };
            // Hayward fault, Richmond to Fremont (4 control points, BayTM@v1), linear between them
            double[][] p = { new[] { 70912.0, 84061.0 }, new[] { 79724.0, 70747.0 }, new[] { 88564.0, 57442.0 }, new[] { 97422.0, 47476.0 } };
            int n = 61; l.TraceX = new double[n]; l.TraceZ = new double[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / 60.0 * 3.0; int s = Math.Min(2, (int)t); double u = t - s;
                l.TraceX[i] = p[s][0] + (p[s + 1][0] - p[s][0]) * u; l.TraceZ[i] = p[s][1] + (p[s + 1][1] - p[s][1]) * u;
            }
            return l;
        }
    }
}
