// Dissolves the baked fault pieces (World.Geo faults layer, short pieces, median 359 m) into one ordered BayTM float64 polyline per
// FaultName + Section (R-022 section 1, M-053 step 1). Pieces are joined end to end within a small tolerance; whatever cannot be joined
// stays a separate chain, ordered after the main chain, and its gap is reported (the gap itself is NOT mapped trace: no rupture crosses it).
// A rupture runs on a FAULT SYSTEM (all sections with the same FaultName chained along strike, gaps up to MaxBridgeGapM bridged and flagged).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Game.World.Geo;

namespace Game.World.Seismic
{
    /// <summary>One baked piece in BayTM metres (x,z pairs), with the attributes of its feature. Section may be null/empty.</summary>
    public sealed class FaultPiece
    {
        public string FaultName, Section, Linetype, SlipSense;
        /// <summary>The baked `fault_id` property (string, as written in faults_bay.geojson); null when unknown. Only the metric dumps use it (M-053 keys on it).</summary>
        public string BakedFaultId;
        public double[] Xz;
    }

    /// <summary>Slip sense text of the baked `slip_sense` field to <see cref="SlipSense"/>. Codes D / RD / unspecified stay Unknown.</summary>
    public static class SlipSenseParser
    {
        public static SlipSense Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return SlipSense.Unknown;
            switch (s.Trim().ToLowerInvariant())
            {
                case "right lateral": return SlipSense.RightLateral;
                case "left lateral": return SlipSense.LeftLateral;
                case "normal": return SlipSense.Normal;
                case "reverse":
                case "thrust": return SlipSense.Reverse;
                default: return SlipSense.Unknown;
            }
        }
    }

    /// <summary>One dissolved trace. Vertices of all chains are concatenated; <see cref="ChainStart"/> marks where a new chain begins.</summary>
    public sealed class FaultTrace
    {
        public string FaultName, Section;
        /// <summary>Baked `fault_id` of the first piece that has one (null if none).</summary>
        public string BakedFaultId;
        public uint FaultId;
        public ushort SectionIdx;
        public SlipSense SlipSense;
        public int PieceCount;
        /// <summary>BayTM float64 vertices (concatenated chains, main chain first).</summary>
        public double[] X, Z;
        /// <summary>Arc length from the start of the vertex's own chain, m (so differences inside a chain are true arc lengths).</summary>
        public double[] ArcM;
        /// <summary>Start vertex of each chain, ascending; chain k covers [ChainStart[k], ChainStart[k+1]-1].</summary>
        public int[] ChainStart;
        /// <summary>Distance between the end of the walk so far and the start of chain k (k >= 1), m; the unmapped bridge.</summary>
        public double[] GapM;

        public int VertexCount { get { return X.Length; } }
        public int ChainCount { get { return ChainStart.Length; } }
        public int ChainEnd(int chain) { return chain + 1 < ChainStart.Length ? ChainStart[chain + 1] - 1 : X.Length - 1; }
        public double ChainLengthM(int chain) { return ArcM[ChainEnd(chain)]; }
        public double MainChainLengthM { get { return ChainLengthM(0); } }
        public double TotalLengthM { get { double s = 0; for (int k = 0; k < ChainCount; k++) s += ChainLengthM(k); return s; } }
        /// <summary>Sum of the gaps between consecutive chains, m.</summary>
        public double TotalGapM { get { double s = 0; for (int k = 1; k < GapM.Length; k++) s += GapM[k]; return s; } }

        public int ChainOf(int vertex)
        {
            int k = Array.BinarySearch(ChainStart, vertex);
            return k >= 0 ? k : ~k - 1;
        }
    }


    /// <summary>
    /// All sections of one FaultName chained along strike into one polyline (orchestrator decision, T-027 step 2). Segments that bridge a gap
    /// between chain ends are flagged in <see cref="Bridge"/>: they are not mapped trace, carry no surface offset and their end vertices are
    /// never emergence vertices. Every vertex is a mapped trace vertex. Components that could not be reached within the bridge limit are dropped
    /// (counted in <see cref="UnchainedChains"/>).
    /// </summary>
    public sealed class FaultSystem
    {
        public string Name;
        /// <summary>Stable id: FNV-1a 32 of the trimmed FaultName in UTF-8. Never 0.</summary>
        public uint FaultId;
        /// <summary>Section names, ordinal order; SectionIdx indexes this.</summary>
        public string[] Sections;
        /// <summary>Baked `fault_id` per section (parallel to <see cref="Sections"/>; null where unknown).</summary>
        public string[] SectionBakedIds;
        public SlipSense SlipSense;
        public int PieceCount, UnchainedChains;
        public double UnchainedLengthM;
        /// <summary>BayTM float64 vertices.</summary>
        public double[] X, Z;
        /// <summary>Cumulative arc length from vertex 0 along the whole polyline (bridges included), m.</summary>
        public double[] ArcM;
        /// <summary>Section index of each vertex (into <see cref="Sections"/>).</summary>
        public ushort[] SectionOfVertex;
        /// <summary>Bridge[i] is true when segment i (vertex i to i+1) is a bridged gap. Length VertexCount - 1.</summary>
        public bool[] Bridge;
        /// <summary>
        /// Chord[i] is true when segment i is not a bridge but also not a segment of any baked piece: the straight join across a digitisation gap of up to
        /// <see cref="FaultTraceSet.DefaultJoinToleranceM"/> between two pieces. It is still ruptured and shaken (the gap is far below the mapped-scale
        /// uncertainty of 25 to 137 m), but it is not mapped trace, so the M-053 dump leaves it out (T-036c: it measured 20 m off the trace). Length VertexCount - 1.
        /// </summary>
        public bool[] Chord;

        public int VertexCount { get { return X.Length; } }
        public double LengthM { get { return ArcM[ArcM.Length - 1]; } }
        public double BridgedLengthM { get { double s = 0; for (int i = 0; i < Bridge.Length; i++) if (Bridge[i]) s += ArcM[i + 1] - ArcM[i]; return s; } }
        public double MappedLengthM { get { return LengthM - BridgedLengthM; } }
        public int BridgeCount { get { int n = 0; for (int i = 0; i < Bridge.Length; i++) if (Bridge[i]) n++; return n; } }
        /// <summary>True when the vertex ends a bridged segment (not a valid emergence vertex).</summary>
        public bool TouchesBridge(int v) { return (v > 0 && Bridge[v - 1]) || (v < Bridge.Length && Bridge[v]); }
        /// <summary>Arc length of the vertex range [a,b], m (bridges included).</summary>
        public double ArcBetweenM(int a, int b) { return ArcM[b] - ArcM[a]; }
    }

    public sealed class FaultTraceSet
    {
        /// <summary>Default join tolerance, m (piece ends of one mapped fault coincide to digitisation precision).</summary>
        public const double DefaultJoinToleranceM = 50.0;

        readonly List<FaultTrace> _traces = new List<FaultTrace>();
        readonly Dictionary<uint, FaultTrace> _byId = new Dictionary<uint, FaultTrace>();
        readonly List<FaultSystem> _systems = new List<FaultSystem>();
        readonly Dictionary<uint, FaultSystem> _sysById = new Dictionary<uint, FaultSystem>();
        /// <summary>Assumed (orchestrator decision): largest gap between chain ends that a fault system bridges, m.</summary>
        public const double MaxBridgeGapM = 5000.0;
        public IReadOnlyList<FaultSystem> Systems { get { return _systems; } }
        public FaultSystem FindSystem(uint faultId) { FaultSystem t; return _sysById.TryGetValue(faultId, out t) ? t : null; }
        public FaultSystem FindSystem(string faultName) { return FindSystem(HashName(faultName)); }
        public static uint HashName(string faultName)
        {
            byte[] b = Encoding.UTF8.GetBytes((faultName ?? "").Trim());
            uint h = 2166136261u;
            for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 16777619u; }
            return h == 0 ? 1u : h;
        }
        public IReadOnlyList<FaultTrace> Traces { get { return _traces; } }
        public double JoinToleranceM { get; private set; }

        public FaultTrace Find(uint faultId) { FaultTrace t; return _byId.TryGetValue(faultId, out t) ? t : null; }
        public FaultTrace Find(string faultName, string section) { return Find(HashKey(faultName, section)); }

        /// <summary>Stable id of a dissolved key: FNV-1a 32 of "FaultName|Section" in UTF-8 (trimmed, null = empty). Never 0.</summary>
        public static uint HashKey(string faultName, string section)
        {
            byte[] b = Encoding.UTF8.GetBytes((faultName ?? "").Trim() + "|" + (section ?? "").Trim());
            uint h = 2166136261u;
            for (int i = 0; i < b.Length; i++) { h ^= b[i]; h *= 16777619u; }
            return h == 0 ? 1u : h;
        }

        /// <summary>Converts the tile-local faults of a World.Geo layer output to BayTM pieces (tile origin added in float64).</summary>
        public static List<FaultPiece> PiecesFromGeo(IEnumerable<GeoFaultData> faults, double originX, double originZ)
        {
            var list = new List<FaultPiece>();
            foreach (GeoFaultData f in faults)
                foreach (double[] line in f.Lines)
                {
                    var xz = new double[line.Length];
                    for (int i = 0; i + 1 < line.Length; i += 2) { xz[i] = line[i] + originX; xz[i + 1] = line[i + 1] + originZ; }
                    list.Add(new FaultPiece { FaultName = f.FaultName, Section = f.Section, Linetype = f.Linetype, SlipSense = f.SlipSense, BakedFaultId = f.FaultId, Xz = xz });
                }
            return list;
        }

        public static FaultTraceSet Build(IEnumerable<FaultPiece> pieces, double joinToleranceM = DefaultJoinToleranceM)
        {
            var set = new FaultTraceSet { JoinToleranceM = joinToleranceM };
            var pieceSegs = new Dictionary<string, HashSet<SegKey>>(StringComparer.Ordinal);
            var groups = new SortedDictionary<string, List<FaultPiece>>(StringComparer.Ordinal);
            foreach (FaultPiece p in pieces)
            {
                if (p == null || p.Xz == null || p.Xz.Length < 4) continue;
                string key = (p.FaultName ?? "").Trim() + "\n" + (p.Section ?? "").Trim();
                List<FaultPiece> l;
                if (!groups.TryGetValue(key, out l)) groups[key] = l = new List<FaultPiece>();
                l.Add(p);
                string nm = (p.FaultName ?? "").Trim();
                HashSet<SegKey> hs;
                if (!pieceSegs.TryGetValue(nm, out hs)) pieceSegs[nm] = hs = new HashSet<SegKey>();
                for (int i = 0; i + 3 < p.Xz.Length; i += 2)
                {
                    if (p.Xz[i] == p.Xz[i + 2] && p.Xz[i + 1] == p.Xz[i + 3]) continue;   // repeated vertex (dropped by the dissolve)
                    hs.Add(new SegKey(p.Xz[i], p.Xz[i + 1], p.Xz[i + 2], p.Xz[i + 3]));
                }
            }
            // SectionIdx = rank of the section among the sorted sections of the same FaultName.
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            var perName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string key in groups.Keys)
            {
                string name = key.Substring(0, key.IndexOf('\n'));
                int r; perName.TryGetValue(name, out r);
                rank[key] = r; perName[name] = r + 1;
            }
            foreach (KeyValuePair<string, List<FaultPiece>> kv in groups)
            {
                int nl = kv.Key.IndexOf('\n');
                FaultTrace t = Dissolve(kv.Value, kv.Key.Substring(0, nl), kv.Key.Substring(nl + 1), joinToleranceM);
                t.SectionIdx = (ushort)rank[kv.Key];
                FaultTrace clash;
                if (set._byId.TryGetValue(t.FaultId, out clash))
                    throw new InvalidOperationException("FaultId hash collision: " + clash.FaultName + "|" + clash.Section + " and " + t.FaultName + "|" + t.Section);
                set._byId[t.FaultId] = t;
                set._traces.Add(t);
            }
            // fault systems: sections of one FaultName in section order
            var byName = new SortedDictionary<string, List<FaultTrace>>(StringComparer.Ordinal);
            foreach (FaultTrace t in set._traces)
            {
                List<FaultTrace> l;
                if (!byName.TryGetValue(t.FaultName, out l)) byName[t.FaultName] = l = new List<FaultTrace>();
                l.Add(t);
            }
            foreach (KeyValuePair<string, List<FaultTrace>> kv in byName)
            {
                FaultSystem sys = BuildSystem(kv.Key, kv.Value);
                if (sys == null) continue;
                HashSet<SegKey> known;
                pieceSegs.TryGetValue(kv.Key, out known);
                sys.Chord = new bool[sys.Bridge.Length];
                for (int i = 0; i < sys.Chord.Length; i++)
                    sys.Chord[i] = !sys.Bridge[i] && (known == null || !known.Contains(new SegKey(sys.X[i], sys.Z[i], sys.X[i + 1], sys.Z[i + 1])));
                if (set._sysById.ContainsKey(sys.FaultId)) throw new InvalidOperationException("FaultId hash collision on system " + sys.Name);
                set._sysById[sys.FaultId] = sys;
                set._systems.Add(sys);
            }
            return set;
        }

        /// <summary>A segment as an unordered pair of end points (exact double equality: system vertices are copies of piece vertices).</summary>
        readonly struct SegKey : IEquatable<SegKey>
        {
            readonly double _ax, _az, _bx, _bz;
            public SegKey(double x0, double z0, double x1, double z1)
            {
                if (x0 < x1 || (x0 == x1 && z0 <= z1)) { _ax = x0; _az = z0; _bx = x1; _bz = z1; } else { _ax = x1; _az = z1; _bx = x0; _bz = z0; }
            }
            public bool Equals(SegKey o) { return _ax == o._ax && _az == o._az && _bx == o._bx && _bz == o._bz; }
            public override bool Equals(object o) { return o is SegKey k && Equals(k); }
            public override int GetHashCode() { unchecked { int h = _ax.GetHashCode(); h = h * 31 + _az.GetHashCode(); h = h * 31 + _bx.GetHashCode(); return h * 31 + _bz.GetHashCode(); } }
        }

        // ---- chain the sections of one FaultName along strike ----
        sealed class SysComp
        {
            public List<double> X = new List<double>(), Z = new List<double>();
            public List<ushort> Sec = new List<ushort>();
            public List<bool> Bridge = new List<bool>();   // Bridge[i]: segment i -> i+1
            public double Len;
        }

        static FaultSystem BuildSystem(string name, List<FaultTrace> secs)
        {
            var cx = new List<double[]>(); var cz = new List<double[]>(); var cs = new List<ushort>();
            var sections = new string[secs.Count];
            var bakedIds = new string[secs.Count];
            var senseLen = new Dictionary<SlipSense, double>();
            int pieces = 0;
            for (int s = 0; s < secs.Count; s++)
            {
                FaultTrace t = secs[s];
                sections[s] = t.Section ?? "";
                bakedIds[s] = t.BakedFaultId;
                pieces += t.PieceCount;
                double cur; senseLen.TryGetValue(t.SlipSense, out cur); senseLen[t.SlipSense] = cur + t.TotalLengthM;
                for (int k = 0; k < t.ChainCount; k++)
                {
                    int a = t.ChainStart[k], b = t.ChainEnd(k), n = b - a + 1;
                    var x = new double[n]; var z = new double[n];
                    Array.Copy(t.X, a, x, 0, n); Array.Copy(t.Z, a, z, 0, n);
                    cx.Add(x); cz.Add(z); cs.Add((ushort)s);
                }
            }
            if (cx.Count == 0) return null;
            var lens = new double[cx.Count];
            for (int i = 0; i < lens.Length; i++) lens[i] = PolyLen(cx[i], cz[i]);
            var order = new List<int>(); for (int i = 0; i < lens.Length; i++) order.Add(i);
            order.Sort((a, b) => { int c = lens[b].CompareTo(lens[a]); return c != 0 ? c : a.CompareTo(b); });

            var used = new bool[cx.Count];
            var comps = new List<SysComp>(); var compChains = new List<int>();
            foreach (int seed in order)
            {
                if (used[seed]) continue;
                used[seed] = true;
                var comp = new SysComp();
                AppendChain(comp, cx[seed], cz[seed], cs[seed], false, 0, false);
                int count = 1;
                while (true)
                {
                    int tn = comp.X.Count - 1;
                    int best = -1; bool bestTail = true, bestRev = false; double bestD = MaxBridgeGapM;
                    for (int j = 0; j < cx.Count; j++)
                    {
                        if (used[j]) continue;
                        int on = cx[j].Length - 1;
                        Try(Dist(comp.X[tn], comp.Z[tn], cx[j][0], cz[j][0]), j, true, false, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(comp.X[tn], comp.Z[tn], cx[j][on], cz[j][on]), j, true, true, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(comp.X[0], comp.Z[0], cx[j][on], cz[j][on]), j, false, false, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(comp.X[0], comp.Z[0], cx[j][0], cz[j][0]), j, false, true, ref best, ref bestTail, ref bestRev, ref bestD);
                    }
                    if (best < 0) break;
                    used[best] = true; count++;
                    if (bestTail) AppendChain(comp, cx[best], cz[best], cs[best], bestRev, bestD, true);
                    else comp = PrependChain(comp, cx[best], cz[best], cs[best], bestRev, bestD);
                }
                comps.Add(comp); compChains.Add(count);
            }
            int main = 0;
            for (int i = 1; i < comps.Count; i++) if (comps[i].Len > comps[main].Len) main = i;   // strict: earlier (longer seed) wins ties
            SysComp m = comps[main];
            int nv = m.X.Count;
            var arc = new double[nv];
            for (int i = 1; i < nv; i++) arc[i] = arc[i - 1] + Dist(m.X[i - 1], m.Z[i - 1], m.X[i], m.Z[i]);
            SlipSense dom = SlipSense.Unknown; double domLen = -1;
            foreach (KeyValuePair<SlipSense, double> kv in senseLen) if (kv.Value > domLen || (kv.Value == domLen && kv.Key < dom)) { dom = kv.Key; domLen = kv.Value; }
            int lostChains = 0; double lostLen = 0;
            for (int i = 0; i < comps.Count; i++) if (i != main) { lostChains += compChains[i]; lostLen += comps[i].Len; }
            return new FaultSystem
            {
                Name = name, FaultId = HashName(name), Sections = sections, SectionBakedIds = bakedIds, SlipSense = dom, PieceCount = pieces,
                UnchainedChains = lostChains, UnchainedLengthM = lostLen,
                X = m.X.ToArray(), Z = m.Z.ToArray(), ArcM = arc, SectionOfVertex = m.Sec.ToArray(), Bridge = m.Bridge.ToArray(),
            };
        }

        const double CoincidentM = 1e-6;

        // Appends the chain (optionally reversed) at the tail of comp; a gap above CoincidentM adds one flagged bridge segment.
        static void AppendChain(SysComp comp, double[] x, double[] z, ushort sec, bool rev, double gap, bool bridge)
        {
            int n = x.Length;
            for (int q = 0; q < n; q++)
            {
                int i = rev ? n - 1 - q : q;
                if (comp.X.Count > 0 && q == 0)
                {
                    if (gap <= CoincidentM) continue;   // shared vertex: drop the duplicate, no bridge
                    comp.Bridge.Add(true);
                }
                else if (comp.X.Count > 0) comp.Bridge.Add(false);
                comp.X.Add(x[i]); comp.Z.Add(z[i]); comp.Sec.Add(sec);
            }
            comp.Len = 0;
            for (int i = 1; i < comp.X.Count; i++) comp.Len += Dist(comp.X[i - 1], comp.Z[i - 1], comp.X[i], comp.Z[i]);
        }

        // Puts the chain in front of comp so that its last vertex is the one nearest comp's head.
        static SysComp PrependChain(SysComp comp, double[] x, double[] z, ushort sec, bool rev, double gap)
        {
            var r = new SysComp();
            // rev == false: chain's end is near our head (its natural order ends at our head); rev == true: reversed chain.
            AppendChain(r, x, z, sec, rev, 0, false);
            bool coincident = gap <= CoincidentM;
            for (int i = 0; i < comp.X.Count; i++)
            {
                if (i == 0)
                {
                    if (coincident) continue;
                    r.Bridge.Add(true);
                }
                else r.Bridge.Add(comp.Bridge[i - 1]);
                r.X.Add(comp.X[i]); r.Z.Add(comp.Z[i]); r.Sec.Add(comp.Sec[i]);
            }
            r.Len = 0;
            for (int i = 1; i < r.X.Count; i++) r.Len += Dist(r.X[i - 1], r.Z[i - 1], r.X[i], r.Z[i]);
            return r;
        }

        static double PolyLen(double[] x, double[] z) { double s = 0; for (int i = 1; i < x.Length; i++) s += Dist(x[i - 1], z[i - 1], x[i], z[i]); return s; }

        // ---- dissolve one key ----
        sealed class Chain { public List<double> X = new List<double>(), Z = new List<double>(); }

        static FaultTrace Dissolve(List<FaultPiece> pieces, string name, string section, double tol)
        {
            var polys = new List<Chain>();
            var senseLen = new Dictionary<SlipSense, double>();
            foreach (FaultPiece p in pieces)
            {
                var c = new Chain();
                for (int i = 0; i + 1 < p.Xz.Length; i += 2)
                {
                    int n = c.X.Count;
                    if (n > 0 && c.X[n - 1] == p.Xz[i] && c.Z[n - 1] == p.Xz[i + 1]) continue;
                    c.X.Add(p.Xz[i]); c.Z.Add(p.Xz[i + 1]);
                }
                if (c.X.Count < 2) continue;
                polys.Add(c);
                SlipSense s = SlipSenseParser.Parse(p.SlipSense);
                double len = Length(c), cur; senseLen.TryGetValue(s, out cur); senseLen[s] = cur + len;
            }
            // longest first (stable by input order)
            var order = new List<int>(); for (int i = 0; i < polys.Count; i++) order.Add(i);
            var lens = new double[polys.Count]; for (int i = 0; i < polys.Count; i++) lens[i] = Length(polys[i]);
            order.Sort((a, b) => { int c = lens[b].CompareTo(lens[a]); return c != 0 ? c : a.CompareTo(b); });

            var used = new bool[polys.Count];
            var chains = new List<Chain>();
            foreach (int seed in order)
            {
                if (used[seed]) continue;
                used[seed] = true;
                Chain ch = polys[seed];
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    int best = -1; bool bestTail = true, bestRev = false; double bestD = tol;
                    for (int j = 0; j < polys.Count; j++)
                    {
                        if (used[j]) continue;
                        Chain o = polys[j];
                        int tn = ch.X.Count - 1, on = o.X.Count - 1;
                        Try(Dist(ch.X[tn], ch.Z[tn], o.X[0], o.Z[0]), j, true, false, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(ch.X[tn], ch.Z[tn], o.X[on], o.Z[on]), j, true, true, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(ch.X[0], ch.Z[0], o.X[on], o.Z[on]), j, false, false, ref best, ref bestTail, ref bestRev, ref bestD);
                        Try(Dist(ch.X[0], ch.Z[0], o.X[0], o.Z[0]), j, false, true, ref best, ref bestTail, ref bestRev, ref bestD);
                    }
                    if (best >= 0)
                    {
                        used[best] = true; grew = true;
                        Chain o = polys[best];
                        Chain add = bestRev ? Reverse(o) : o;   // tail join: add starts at our tail; head join: add ends at our head
                        if (bestTail) Append(ch, add); else { Append(add, ch); ch = add; }
                    }
                }
                chains.Add(ch);
            }
            // walk: longest chain first, then attach the nearest remaining chain at either end of the walk (flip as needed); gaps recorded.
            var xs = new List<double>(); var zs = new List<double>(); var starts = new List<int>(); var gaps = new List<double>(); var arcs = new List<double>();
            var left = new List<Chain>(chains);
            // chains were built from the longest seeds, but merges can reorder; pick the longest now
            left.Sort((a, b) => Length(b).CompareTo(Length(a)));
            var walk = new List<Chain>(); var walkGap = new List<double>();
            Chain first = left[0]; left.RemoveAt(0);
            walk.Add(first); walkGap.Add(0);
            // the walk is only used to define chain order and chain orientation; chains are not merged
            while (left.Count > 0)
            {
                Chain last = walk[walk.Count - 1];
                int ln = last.X.Count - 1;
                int bi = 0; bool brev = false; double bd = double.MaxValue;
                for (int j = 0; j < left.Count; j++)
                {
                    Chain o = left[j]; int on = o.X.Count - 1;
                    double d0 = Dist(last.X[ln], last.Z[ln], o.X[0], o.Z[0]);
                    double d1 = Dist(last.X[ln], last.Z[ln], o.X[on], o.Z[on]);
                    if (d0 < bd) { bd = d0; bi = j; brev = false; }
                    if (d1 < bd) { bd = d1; bi = j; brev = true; }
                }
                Chain pick = left[bi]; left.RemoveAt(bi);
                walk.Add(brev ? Reverse(pick) : pick); walkGap.Add(bd);
            }
            foreach (Chain c in walk)
            {
                starts.Add(xs.Count);
                double acc = 0;
                for (int i = 0; i < c.X.Count; i++)
                {
                    if (i > 0) acc += Dist(c.X[i - 1], c.Z[i - 1], c.X[i], c.Z[i]);
                    xs.Add(c.X[i]); zs.Add(c.Z[i]); arcs.Add(acc);
                }
            }
            SlipSense dom = SlipSense.Unknown; double domLen = -1;
            foreach (KeyValuePair<SlipSense, double> kv in senseLen) if (kv.Value > domLen || (kv.Value == domLen && kv.Key < dom)) { dom = kv.Key; domLen = kv.Value; }
            return new FaultTrace
            {
                FaultName = name, Section = section, FaultId = HashKey(name, section), SlipSense = dom, PieceCount = pieces.Count,
                BakedFaultId = pieces.Select(q => q.BakedFaultId).FirstOrDefault(q => q != null),
                X = xs.ToArray(), Z = zs.ToArray(), ArcM = arcs.ToArray(), ChainStart = starts.ToArray(), GapM = walkGap.ToArray(),
            };
        }

        static void Try(double d, int j, bool tail, bool rev, ref int best, ref bool bestTail, ref bool bestRev, ref double bestD)
        {
            if (d <= bestD) { if (best >= 0 && d == bestD) return; best = j; bestTail = tail; bestRev = rev; bestD = d; }
        }

        static double Dist(double x0, double z0, double x1, double z1) { double dx = x1 - x0, dz = z1 - z0; return Math.Sqrt(dx * dx + dz * dz); }
        static double Length(Chain c) { double s = 0; for (int i = 1; i < c.X.Count; i++) s += Dist(c.X[i - 1], c.Z[i - 1], c.X[i], c.Z[i]); return s; }
        static Chain Reverse(Chain c) { var r = new Chain(); for (int i = c.X.Count - 1; i >= 0; i--) { r.X.Add(c.X[i]); r.Z.Add(c.Z[i]); } return r; }
        static void Append(Chain a, Chain b)
        {
            int start = 0;
            if (a.X[a.X.Count - 1] == b.X[0] && a.Z[a.Z.Count - 1] == b.Z[0]) start = 1;
            for (int i = start; i < b.X.Count; i++) { a.X.Add(b.X[i]); a.Z.Add(b.Z[i]); }
        }
    }
}
