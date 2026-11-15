using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Game.AI.Harness;
using Game.AI.Policy;

namespace Game.AI.Training
{
    /// <summary>Wire protocol constants (R-028 section 5.2). Little-endian; Python reads the sizes from the hello message.</summary>
    public static class Wire
    {
        public const int Magic = 0x4B4A4252; // "KJBR"
        public const int ProtocolVersion = 1;
        public const int KindDecide = 0, KindEnd = 2;
        // event vector: accumulated per agent since its previous decision row (R-028 section 6)
        public const int EvDmgDealt = 0, EvDmgTaken = 1, EvKillsMine = 2, EvTeamKills = 3, EvAllyDeaths = 4, EvIDied = 5,
            EvWin = 6, EvLoss = 7, EvTimeout = 8, EvVoluntarySwitch = 9, EvElapsedS = 10, EvCommitFlanked = 11, EvCommitMulti = 12;
        public const int EvDim = 13;
    }

    public sealed class BridgeConfig
    {
        public int NEnvs = 4;
        public ulong BaseSeed = 1000;
        public string Scenario = "m007_3v4";
        public double DurationS = 0;      // 0 = scenario default
        public int Kaiju = 0, Mechs = 0;  // 0 = scenario default
        public float MechDps = -1f, MechFleeRadiusM = -1f, MechRespawnS = -1f;
        public bool EarlyStop = true;     // end the episode on win / wipe (training); eval runs keep the full duration
        public string LogDir;             // brain_log@v1 per episode when set
        public int LogEpisodes = 0;       // per env, first N episodes
        public string BrainId = "policy@0";
        public double ScriptedAllyFrac = 0; // mixed packs: each kaiju is scripted with this probability (seeded)

        public static BridgeConfig Load(string path)
        {
            var c = new BridgeConfig();
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "n_envs": c.NEnvs = p.Value.GetInt32(); break;
                    case "base_seed": c.BaseSeed = p.Value.GetUInt64(); break;
                    case "scenario": c.Scenario = p.Value.GetString(); break;
                    case "duration_s": c.DurationS = p.Value.GetDouble(); break;
                    case "kaiju": c.Kaiju = p.Value.GetInt32(); break;
                    case "mechs": c.Mechs = p.Value.GetInt32(); break;
                    case "mech_dps": c.MechDps = p.Value.GetSingle(); break;
                    case "mech_flee_radius_m": c.MechFleeRadiusM = p.Value.GetSingle(); break;
                    case "mech_respawn_s": c.MechRespawnS = p.Value.GetSingle(); break;
                    case "early_stop": c.EarlyStop = p.Value.GetBoolean(); break;
                    case "log_dir": c.LogDir = p.Value.GetString(); break;
                    case "log_episodes": c.LogEpisodes = p.Value.GetInt32(); break;
                    case "brain_id": c.BrainId = p.Value.GetString(); break;
                    case "scripted_ally_frac": c.ScriptedAllyFrac = p.Value.GetDouble(); break;
                    default: break; // Python-side keys are ignored
                }
            }
            if (c.NEnvs < 1) throw new ArgumentException("n_envs must be >= 1");
            return c;
        }

        public HarnessScenario Build(ulong seed)
        {
            HarnessScenario sc = Scenario switch
            {
                "m007" => HarnessScenario.M007(seed, false),
                "m007_3v4" => HarnessScenario.M007ThreeVsFour(seed, false),
                _ => throw new ArgumentException("unknown scenario " + Scenario),
            };
            if (DurationS > 0) sc.DurationSeconds = DurationS;
            if (Kaiju > 0) sc.KaijuCount = Kaiju;
            if (Mechs > 0) sc.MechCount = Mechs;
            if (MechDps >= 0f) sc.MechDps = MechDps;
            if (MechFleeRadiusM >= 0f) sc.MechFleeRadiusM = MechFleeRadiusM;
            if (MechRespawnS >= 0f) sc.MechRespawnSeconds = MechRespawnS;
            return sc;
        }
    }

    internal sealed class EpisodeEnd : Exception { public EpisodeEnd() : base("episode end") { } }
    internal sealed class BridgeQuit : Exception { public BridgeQuit() : base("quit") { } }

    /// <summary>One pending protocol row (copied buffers; a row outlives the brain call that produced it).</summary>
    internal sealed class Row
    {
        public int Env, Agent, Kind, Episode;
        public readonly float[] Ev = new float[Wire.EvDim];
        public readonly float[] Obs = new float[PolicyLayout.ObsDim];
        public readonly byte[] Mask = new byte[PolicyLayout.MaskDim];
    }

    /// <summary>Network that hands the decision to the coordinator (Python) and blocks until the action arrives.</summary>
    internal sealed class BridgeNetwork : IPolicyNetwork
    {
        private readonly EnvWorker _env; private readonly int _agent;
        public BridgeNetwork(EnvWorker env, int agent) { _env = env; _agent = agent; }
        public string NetworkId => "bridge@1";
        public void Reset(ulong seed) { }
        public void Act(float[] obs, byte[] mask, ref PolicyAction action) => action = _env.RequestDecision(_agent, obs, mask);
    }

    /// <summary>
    /// TextWriter that receives every brain_log@v1 row of one episode (optionally tees it to a file, with the header brain_id
    /// rewritten to the policy id) and turns hit / death / decision rows into per-agent events. Throws <see cref="EpisodeEnd"/>
    /// on a win or a wipe when early stop is on.
    /// </summary>
    internal sealed class EventTap : TextWriter
    {
        private readonly EnvWorker _env; private readonly TextWriter _tee; private readonly string _brainId;
        private readonly bool _earlyStop; private readonly int _kaiju, _mechs; private readonly bool _respawn;
        public int MechDeaths, KaijuDeaths; public bool Win, Wipe;
        private readonly Dictionary<ulong, ulong> _lastAttacker = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, (float x, float y)> _tpos = new Dictionary<ulong, (float, float)>();
        private readonly Dictionary<ulong, double> _lastCharge = new Dictionary<ulong, double>();
        private readonly (float x, float y, ulong target, bool alive, int state)[] _k;
        private readonly float _mechHp, _kaijuHp, _dEng;
        private readonly List<double> _bear = new List<double>(8);
        public override Encoding Encoding => Encoding.UTF8;

        public EventTap(EnvWorker env, HarnessScenario sc, TextWriter tee, string brainId, bool earlyStop)
        {
            _env = env; _tee = tee; _brainId = brainId; _earlyStop = earlyStop; _kaiju = sc.KaijuCount; _mechs = sc.MechCount;
            _respawn = sc.MechRespawnSeconds > 0f; _mechHp = sc.MechHp; _kaijuHp = sc.KaijuMaxHp; _dEng = sc.Brain.EngageRadiusM;
            _k = new (float, float, ulong, bool, int)[sc.KaijuCount];
            for (int i = 0; i < _k.Length; i++) _k[i].alive = true;
        }

        public override void Write(char value) => throw new NotSupportedException("row writes only");

        public override void Write(string row)
        {
            if (row.StartsWith("{\"kind\":\"header\"", StringComparison.Ordinal))
                row = row.Replace("\"brain_id\":\"scripted@1\"", "\"brain_id\":\"" + _brainId + "\"");
            _tee?.Write(row);
            if (row.StartsWith("{\"kind\":\"hit\"", StringComparison.Ordinal)) OnHit(row);
            else if (row.StartsWith("{\"kind\":\"death\"", StringComparison.Ordinal)) OnDeath(row);
            else if (row.StartsWith("{\"kind\":\"decision\"", StringComparison.Ordinal)) OnDecision(row);
            else if (row.StartsWith("{\"kind\":\"target_sample\"", StringComparison.Ordinal))
                _tpos[Ent(row, "ent")] = (Num(row, "x"), Num(row, "y"));
        }

        public override void Flush() => _tee?.Flush();

        private static bool IsKaiju(ulong id, int k) => id >= 1 && id <= (ulong)k;

        private void OnHit(string row)
        {
            ulong a = Ent(row, "attacker"), v = Ent(row, "victim"); float dmg = Num(row, "damage");
            if (IsKaiju(a, _kaiju)) { _env.AddEvent((int)a - 1, Wire.EvDmgDealt, dmg / _mechHp); _lastAttacker[v] = a; }
            if (IsKaiju(v, _kaiju)) _env.AddEvent((int)v - 1, Wire.EvDmgTaken, dmg / _kaijuHp);
        }

        private void OnDeath(string row)
        {
            ulong e = Ent(row, "ent");
            if (IsKaiju(e, _kaiju))
            {
                KaijuDeaths++; _k[e - 1].alive = false;
                _env.AddEvent((int)e - 1, Wire.EvIDied, 1f);
                for (int i = 0; i < _kaiju; i++) if (i != (int)e - 1) _env.AddEvent(i, Wire.EvAllyDeaths, 1f);
                if (KaijuDeaths >= _kaiju) { Wipe = true; if (_earlyStop) throw new EpisodeEnd(); }
            }
            else
            {
                MechDeaths++;
                if (_lastAttacker.TryGetValue(e, out ulong killer) && IsKaiju(killer, _kaiju)) _env.AddEvent((int)killer - 1, Wire.EvKillsMine, 1f);
                for (int i = 0; i < _kaiju; i++) _env.AddEvent(i, Wire.EvTeamKills, 1f);
                if (!_respawn && MechDeaths >= _mechs) { Win = true; if (_earlyStop) throw new EpisodeEnd(); }
            }
        }

        private void OnDecision(string row)
        {
            ulong e = Ent(row, "ent"); if (!IsKaiju(e, _kaiju)) return;
            int i = (int)e - 1;
            int state = (int)Num(row, "state"), prev = (int)Num(row, "prev_state"), reason = (int)Num(row, "reason");
            ulong tg = Ent(row, "target"); bool alive = Num(row, "alive") != 0f; double t = Num(row, "t");
            _k[i] = (Num(row, "x"), Num(row, "y"), tg, alive, state);
            if (reason == 11) _env.AddEvent(i, Wire.EvVoluntarySwitch, 1f);
            if (state != 3 || tg == 0) return;
            bool entry = prev != 3;
            bool fresh = !_lastCharge.TryGetValue(tg, out double lc) || t - lc > 8.0;
            _lastCharge[tg] = t;
            if (!entry || !fresh || !_tpos.TryGetValue(tg, out var q)) return;
            // commit event (R-024 3.1, online approximation): spread of the engaged members on tg
            _bear.Clear();
            for (int j = 0; j < _kaiju; j++)
            {
                var k = _k[j]; if (!k.alive || k.target != tg) continue;
                double dx = k.x - q.x, dy = k.y - q.y; if (Math.Sqrt(dx * dx + dy * dy) > _dEng) continue;
                double b = Math.Atan2(dy, dx) * 180.0 / Math.PI; if (b < 0) b += 360.0; _bear.Add(b);
            }
            if (_bear.Count < 2) return;
            _bear.Sort();
            double gap = 360.0 - _bear[_bear.Count - 1] + _bear[0];
            for (int j = 1; j < _bear.Count; j++) gap = Math.Max(gap, _bear[j] - _bear[j - 1]);
            bool flanked = 360.0 - gap >= 90.0;
            for (int j = 0; j < _kaiju; j++)
            {
                _env.AddEvent(j, Wire.EvCommitMulti, 1f);
                if (flanked) _env.AddEvent(j, Wire.EvCommitFlanked, 1f);
            }
        }

        // minimal field readers for the fixed brain_log@v1 row format (no nested objects in these rows)
        private static int ValueStart(string row, string key)
        {
            int k = row.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (k < 0) throw new FormatException("missing " + key);
            return k + key.Length + 3;
        }
        private static float Num(string row, string key)
        {
            int s = ValueStart(row, key), e = s;
            while (e < row.Length && row[e] != ',' && row[e] != '}') e++;
            return float.Parse(row.AsSpan(s, e - s), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        private static ulong Ent(string row, string key)
        {
            int s = ValueStart(row, key) + 5, e = row.IndexOf('"', s); // skip "ent:
            return ulong.Parse(row.AsSpan(s, e - s), NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>One environment = one harness thread with auto-reset. Lockstep with the coordinator through two semaphores.</summary>
    internal sealed class EnvWorker
    {
        public readonly int Index;
        private readonly BridgeConfig _cfg;
        private readonly SemaphoreSlim _request = new SemaphoreSlim(0), _reply = new SemaphoreSlim(0);
        public readonly List<Row> Pending = new List<Row>(); // rows to send this batch (end rows, then one decision row)
        private readonly Row[] _last = new Row[BrainContracts.MaxAllies + 1]; // last decision row per agent (for end rows)
        private float[][] _acc = new float[BrainContracts.MaxAllies + 1][];
        private bool[] _policyAgent = new bool[BrainContracts.MaxAllies + 1];
        private PolicyAction _action; private volatile bool _quit;
        public int Episode; public Exception Fault; public long Episodes, Wins;
        private Thread _thread;

        public EnvWorker(int index, BridgeConfig cfg)
        {
            Index = index; _cfg = cfg;
            for (int i = 0; i < _acc.Length; i++) _acc[i] = new float[Wire.EvDim];
        }

        public void Start() { _thread = new Thread(Loop) { IsBackground = true, Name = "env" + Index }; _thread.Start(); }
        public void WaitRequest() => _request.Wait();
        public void Reply(in PolicyAction a) { _action = a; _reply.Release(); }
        public void Quit() { _quit = true; _reply.Release(); }

        public void AddEvent(int agent, int ev, float v) { if (agent >= 0 && agent < _acc.Length) _acc[agent][ev] += v; }

        public PolicyAction RequestDecision(int agent, float[] obs, byte[] mask)
        {
            var r = new Row { Env = Index, Agent = agent, Kind = Wire.KindDecide, Episode = Episode };
            Array.Copy(_acc[agent], r.Ev, Wire.EvDim); Array.Clear(_acc[agent], 0, Wire.EvDim);
            Array.Copy(obs, r.Obs, PolicyLayout.ObsDim); Array.Copy(mask, r.Mask, PolicyLayout.MaskDim);
            _last[agent] = r;
            Pending.Add(r);
            _request.Release();
            _reply.Wait();
            if (_quit) throw new BridgeQuit();
            return _action;
        }

        private void Loop()
        {
            try
            {
                for (Episode = 0; ; Episode++)
                {
                    ulong seed = _cfg.BaseSeed + (ulong)Index * 100000UL + (ulong)Episode;
                    HarnessScenario sc = _cfg.Build(seed);
                    var rng = new SplitMix64(seed ^ 0xA5A5A5A5UL);
                    for (int i = 0; i < _acc.Length; i++) { Array.Clear(_acc[i], 0, Wire.EvDim); _last[i] = null; _policyAgent[i] = false; }
                    sc.BrainFactory = i =>
                    {
                        bool scriptedAlly = _cfg.ScriptedAllyFrac > 0 && rng.NextFloat() < _cfg.ScriptedAllyFrac;
                        if (scriptedAlly) return new ScriptedKaijuBrain();
                        _policyAgent[i] = true;
                        return new PolicyKaijuBrain(new BridgeNetwork(this, i), PolicyConfig.Default());
                    };
                    TextWriter tee = null;
                    if (_cfg.LogDir != null && Episode < _cfg.LogEpisodes)
                    {
                        Directory.CreateDirectory(_cfg.LogDir);
                        tee = new StreamWriter(Path.Combine(_cfg.LogDir, $"brain_log_env{Index}_ep{Episode}.jsonl"), false, new UTF8Encoding(false));
                    }
                    var tap = new EventTap(this, sc, tee, _cfg.BrainId, _cfg.EarlyStop);
                    var harness = new KaijuHarness(sc, new BrainLogWriter(tap));
                    try { HarnessResult res = harness.Run(); if (res.MechsAliveAtEnd == 0 && sc.MechRespawnSeconds <= 0f) tap.Win = true; }
                    catch (EpisodeEnd) { }
                    finally { tee?.Flush(); tee?.Dispose(); }
                    Episodes++; if (tap.Win) Wins++;
                    for (int a = 0; a < sc.KaijuCount; a++)
                    {
                        if (!_policyAgent[a] || _last[a] == null) continue;
                        var r = new Row { Env = Index, Agent = a, Kind = Wire.KindEnd, Episode = Episode };
                        Array.Copy(_acc[a], r.Ev, Wire.EvDim);
                        r.Ev[tap.Win ? Wire.EvWin : (tap.Wipe ? Wire.EvLoss : Wire.EvTimeout)] = 1f;
                        Array.Copy(_last[a].Obs, r.Obs, PolicyLayout.ObsDim); Array.Copy(_last[a].Mask, r.Mask, PolicyLayout.MaskDim);
                        Pending.Add(r);
                    }
                }
            }
            catch (BridgeQuit) { }
            catch (Exception e) { Fault = e; _request.Release(); }
        }
    }

    public static class Program
    {
        public static int Main(string[] args)
        {
            string cfgPath = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--config") cfgPath = args[i + 1];
            if (cfgPath == null) { Console.Error.WriteLine("usage: KaijuBridge --config <bridge.json>"); return 2; }
            BridgeConfig cfg = BridgeConfig.Load(cfgPath);
            using Stream stdin = Console.OpenStandardInput(), stdout = Console.OpenStandardOutput();
            var w = new BinaryWriter(new BufferedStream(stdout, 1 << 20));
            var r = new BinaryReader(new BufferedStream(stdin, 1 << 16));

            w.Write(Wire.Magic); w.Write(Wire.ProtocolVersion); w.Write(PolicyLayout.LayoutVersion);
            w.Write(PolicyLayout.ObsDim); w.Write(PolicyLayout.MaskDim); w.Write(Wire.EvDim);
            w.Write(PolicyLayout.NT); w.Write(PolicyLayout.NA); w.Write(PolicyLayout.NManeuvers); w.Write(PolicyLayout.NSpeeds);
            w.Write(cfg.NEnvs); w.Flush();

            var envs = new EnvWorker[cfg.NEnvs];
            for (int e = 0; e < envs.Length; e++) { envs[e] = new EnvWorker(e, cfg); envs[e].Start(); }
            var decideRows = new List<Row>(envs.Length);
            while (true)
            {
                int rows = 0; decideRows.Clear();
                foreach (var env in envs)
                {
                    env.WaitRequest();
                    if (env.Fault != null) { Console.Error.WriteLine($"env {env.Index} fault: {env.Fault}"); return 3; }
                    rows += env.Pending.Count;
                }
                w.Write(rows);
                foreach (var env in envs)
                {
                    foreach (Row row in env.Pending)
                    {
                        w.Write(row.Env); w.Write(row.Agent); w.Write(row.Kind); w.Write(row.Episode);
                        foreach (float v in row.Ev) w.Write(v);
                        foreach (float v in row.Obs) w.Write(v);
                        w.Write(row.Mask);
                        if (row.Kind == Wire.KindDecide) decideRows.Add(row);
                    }
                }
                w.Flush();
                int n;
                try { n = r.ReadInt32(); } catch (EndOfStreamException) { n = -1; }
                if (n < 0)
                {
                    long eps = 0, wins = 0; foreach (var env in envs) { eps += env.Episodes; wins += env.Wins; env.Quit(); }
                    Console.Error.WriteLine($"bridge: quit after {eps} episodes, {wins} wins");
                    return 0;
                }
                if (n != decideRows.Count) { Console.Error.WriteLine($"bridge: expected {decideRows.Count} actions, got {n}"); return 4; }
                var acts = new PolicyAction[n];
                for (int i = 0; i < n; i++) acts[i] = new PolicyAction(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
                foreach (var env in envs) env.Pending.Clear();
                for (int i = 0; i < n; i++) envs[decideRows[i].Env].Reply(acts[i]);
            }
        }
    }
}
