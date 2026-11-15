using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.AI.Harness
{
    /// <summary>Sanity report of R-024 section 3 pack primitives computed from brain_log@v1 lines (C# only; M-007 tooling is T-033).</summary>
    public sealed class PackReport
    {
        public int EvalTicks, CommitEvents, CommitEventsMulti, CommitEventsFlanked;
        public double FrCommit = double.NaN, FrTimeFraction = double.NaN;
        public int FfTicks; public double MeanFf = double.NaN, MeanFfChance = double.NaN, FocusExcess = double.NaN;
        public int VoluntarySwitches, Thrash; public double SwitchesPerKaijuMinuteEngaged = double.NaN, ThrashFraction;
        public int Leaks; public int Hits, Deaths; public double DamageTopTargetShare30s = double.NaN;
    }

    public static class PackMetrics
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Row
        {
            public string Kind; public long Step; public double T; public ulong Ent; public ulong Target;
            public float X, Y; public int State, PrevState, Reason, Alive, TgtVisible; public float MemAge; public float Dmg;
            public ulong Victim;
        }

        // minimal flat-JSON field access (the writer emits flat objects except header.size_class)
        private static string Raw(string line, string key)
        {
            string pat = "\"" + key + "\":";
            int i = line.IndexOf(pat, StringComparison.Ordinal); if (i < 0) return null;
            i += pat.Length;
            if (line[i] == '"') { int j = line.IndexOf('"', i + 1); return line.Substring(i + 1, j - i - 1); }
            int e = i; while (e < line.Length && line[e] != ',' && line[e] != '}') e++;
            return line.Substring(i, e - i);
        }
        private static ulong EntId(string s) => s == null ? 0UL : ulong.Parse(s.Substring(4), Inv);
        private static float Fl(string line, string k) => float.Parse(Raw(line, k), Inv);
        private static int In(string line, string k) => int.Parse(Raw(line, k), Inv);

        private static Row Parse(string line)
        {
            string kind = Raw(line, "kind");
            var r = new Row { Kind = kind };
            switch (kind)
            {
                case "decision":
                    r.Step = long.Parse(Raw(line, "step"), Inv); r.T = double.Parse(Raw(line, "t"), Inv); r.Ent = EntId(Raw(line, "ent"));
                    r.X = Fl(line, "x"); r.Y = Fl(line, "y"); r.State = In(line, "state"); r.PrevState = In(line, "prev_state");
                    r.Reason = In(line, "reason"); r.Target = EntId(Raw(line, "target")); r.Alive = In(line, "alive");
                    r.TgtVisible = In(line, "tgt_visible"); r.MemAge = Fl(line, "tgt_mem_age_s"); break;
                case "target_sample":
                    r.Step = long.Parse(Raw(line, "step"), Inv); r.T = double.Parse(Raw(line, "t"), Inv); r.Ent = EntId(Raw(line, "ent"));
                    r.X = Fl(line, "x"); r.Y = Fl(line, "y"); r.Alive = In(line, "alive"); break;
                case "hit":
                    r.Step = long.Parse(Raw(line, "step"), Inv); r.Ent = EntId(Raw(line, "attacker")); r.Victim = EntId(Raw(line, "victim")); r.Dmg = Fl(line, "damage"); break;
                case "death": r.Step = long.Parse(Raw(line, "step"), Inv); r.Ent = EntId(Raw(line, "ent")); break;
            }
            return r;
        }

        /// <summary>E[max bin count]/n for n kaiju choosing uniformly among m targets (exact enumeration).</summary>
        public static double FfChance(int n, int m)
        {
            if (n < 1 || m < 1) return 1.0;
            double combos = Math.Pow(m, n); if (combos > 2e6) return double.NaN;
            int[] cnt = new int[m]; double sum = 0; long total = (long)combos;
            for (long c = 0; c < total; c++)
            {
                long v = c; Array.Clear(cnt, 0, m);
                for (int i = 0; i < n; i++) { cnt[(int)(v % m)]++; v /= m; }
                int mx = 0; for (int i = 0; i < m; i++) if (cnt[i] > mx) mx = cnt[i];
                sum += mx;
            }
            return sum / total / n;
        }

        public static PackReport Compute(IEnumerable<string> lines, double dEng = 400.0, double periodSeconds = 0.2)
        {
            var rep = new PackReport();
            var kaiju = new Dictionary<ulong, Row>();             // latest decision per kaiju
            var tpos = new Dictionary<ulong, Row>();              // latest target sample per target
            var tstep = new Dictionary<ulong, long>();
            var lastCharge = new Dictionary<ulong, double>();     // target -> last time any kaiju decided Charge toward it
            var lastSwitch = new Dictionary<ulong, (ulong prev, double t)>();
            var dmgByWindow = new Dictionary<long, Dictionary<ulong, double>>();
            double sumFf = 0, sumChance = 0; int flankedTicks = 0, multiTicks = 0; long engagedDecisions = 0;
            long curStep = -1; var stepRows = new List<Row>();

            void EvalStep()
            {
                if (stepRows.Count == 0) return;
                rep.EvalTicks++;
                double now = stepRows[0].T;
                // alive targets sampled at this step
                int liveTargets = 0; foreach (var kv in tstep) if (kv.Value == curStep && tpos[kv.Key].Alive == 1) liveTargets++;
                var groups = new Dictionary<ulong, List<float>>();
                int withTarget = 0; var perTarget = new Dictionary<ulong, int>();
                foreach (var k in kaiju.Values)
                {
                    if (k.Alive != 1 || k.Target == 0) continue;
                    withTarget++; perTarget.TryGetValue(k.Target, out int c); perTarget[k.Target] = c + 1;
                    if (!tpos.TryGetValue(k.Target, out Row q)) continue;
                    double dx = k.X - q.X, dy = k.Y - q.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) > dEng) continue;
                    if (!groups.TryGetValue(k.Target, out var lst)) groups[k.Target] = lst = new List<float>();
                    double b = Math.Atan2(dy, dx) * 180.0 / Math.PI; if (b < 0) b += 360.0;
                    lst.Add((float)b);
                }
                var flanked = new HashSet<ulong>(); var multi = new HashSet<ulong>();
                foreach (var kv in groups)
                {
                    if (kv.Value.Count < 2) continue;
                    multi.Add(kv.Key); multiTicks++;
                    var b = kv.Value; b.Sort(); float maxGap = 0;
                    for (int i = 0; i < b.Count; i++) { float g = i + 1 < b.Count ? b[i + 1] - b[i] : b[0] + 360f - b[i]; if (g > maxGap) maxGap = g; }
                    if (360f - maxGap >= 90f) { flanked.Add(kv.Key); flankedTicks++; }
                }
                // commit events: only decisions made at this step
                foreach (var r in stepRows)
                {
                    if (r.Kind != "decision" || r.Alive != 1 || r.Target == 0) continue;
                    if (r.State == 3)
                    {
                        bool entry = r.PrevState != 3;
                        bool fresh = !lastCharge.TryGetValue(r.Target, out double lc) || now - lc > 8.0;
                        if (entry && fresh && multi.Contains(r.Target)) { rep.CommitEvents++; rep.CommitEventsMulti++; if (flanked.Contains(r.Target)) rep.CommitEventsFlanked++; }
                        else if (entry && fresh) rep.CommitEvents++;
                        lastCharge[r.Target] = now;
                    }
                }
                // focus fire
                if (withTarget >= 2 && liveTargets >= 2)
                {
                    int mx = 0; foreach (int c in perTarget.Values) if (c > mx) mx = c;
                    sumFf += (double)mx / withTarget; sumChance += FfChance(withTarget, liveTargets); rep.FfTicks++;
                }
                stepRows.Clear();
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                Row r = Parse(line);
                if (r.Kind == "header") continue;
                if (r.Kind == "hit")
                {
                    rep.Hits++; long w = (long)(r.Step * 0.02 / 30.0);
                    if (!dmgByWindow.TryGetValue(w, out var d)) dmgByWindow[w] = d = new Dictionary<ulong, double>();
                    d.TryGetValue(r.Victim, out double cur); d[r.Victim] = cur + r.Dmg; continue;
                }
                if (r.Kind == "death") { rep.Deaths++; continue; }
                if (r.Step != curStep) { EvalStep(); curStep = r.Step; }
                if (r.Kind == "target_sample") { tpos[r.Ent] = r; tstep[r.Ent] = r.Step; continue; }
                // decision: leak, switching, then record
                if (r.Target != 0 && r.TgtVisible == 0 && r.MemAge > 3f) rep.Leaks++;
                if (kaiju.TryGetValue(r.Ent, out Row prevRow) && prevRow.Target != 0 && r.Target != 0 && prevRow.Target != r.Target && r.Reason == 11)
                {
                    rep.VoluntarySwitches++;
                    if (lastSwitch.TryGetValue(r.Ent, out var ls) && ls.prev == r.Target && r.T - ls.t <= 4.0) rep.Thrash++;
                    lastSwitch[r.Ent] = (prevRow.Target, r.T);
                }
                if (r.Alive == 1 && r.Target != 0 && tpos.TryGetValue(r.Target, out Row tq))
                {
                    double dx = r.X - tq.X, dy = r.Y - tq.Y; if (Math.Sqrt(dx * dx + dy * dy) <= dEng) engagedDecisions++;
                }
                kaiju[r.Ent] = r; stepRows.Add(r);
            }
            EvalStep();

            rep.FrCommit = rep.CommitEventsMulti > 0 ? (double)rep.CommitEventsFlanked / rep.CommitEventsMulti : double.NaN;
            rep.FrTimeFraction = multiTicks > 0 ? (double)flankedTicks / multiTicks : double.NaN;
            if (rep.FfTicks > 0)
            {
                rep.MeanFf = sumFf / rep.FfTicks; rep.MeanFfChance = sumChance / rep.FfTicks;
                rep.FocusExcess = rep.MeanFfChance < 1.0 ? (rep.MeanFf - rep.MeanFfChance) / (1.0 - rep.MeanFfChance) : double.NaN;
            }
            double engagedMin = engagedDecisions * periodSeconds / 60.0;
            rep.SwitchesPerKaijuMinuteEngaged = engagedMin > 0 ? rep.VoluntarySwitches / engagedMin : double.NaN;
            rep.ThrashFraction = rep.VoluntarySwitches > 0 ? (double)rep.Thrash / rep.VoluntarySwitches : 0.0;
            double share = 0; int nw = 0;
            foreach (var d in dmgByWindow.Values) { double tot = 0, mx = 0; foreach (double v in d.Values) { tot += v; if (v > mx) mx = v; } if (tot > 0) { share += mx / tot; nw++; } }
            if (nw > 0) rep.DamageTopTargetShare30s = share / nw;
            return rep;
        }
    }
}
