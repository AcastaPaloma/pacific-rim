using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.Control;
using Game.Control.Contracts;
using Game.Physics.Body.Contracts;
using Game.Units.Mech.Contracts;
using Game.Units.Mech.Unity;
using UnityEngine;
using NVec3 = System.Numerics.Vector3;

namespace Game.Units.Mech.Scenarios
{
    /// <summary>A scripted capture: a timeline of intents (never joint targets or forces), optional real external forces for shove tests, optional config edits by data.</summary>
    public sealed class ScenarioSpec
    {
        public string Name;
        public double Seconds;
        public double CruiseFroude;
        public Action<ScriptedPilotSource, MechLimits> Script;
        /// <summary>Called before every step with (simulation, unit, time): only for logged external forces (shoves). Locomotion captures leave it null.</summary>
        public Action<MechSimulation, MechUnit, double> External;
        public Func<MechConfig, MechConfig> Config;
        public float SlopeDeg;
        public double StripStart = -1, StripInterval = 0.5; public int StripFrames = 24;
    }

    public sealed class ScenarioResult
    {
        public string Name; public double Seconds; public bool Fell; public double FellAt = -1; public LocoPhase FinalPhase; public Vector3 FinalCom; public double MeanSpeedLast10s; public int Footfalls; public double MaxExternalN, ExternalWorkJ; public double WallSeconds;
        public int ProbeEvents, ProbeLate; public double MinComHeight = 1e9, MaxYawErrorDeg, MaxLateralM, PathLength;
        public string Line() => string.Format(CultureInfo.InvariantCulture, "{0,-22} t={1,5:F1}s fell={2} at={3:F1} phase={4} com=({5:F2},{6:F2},{7:F2}) v10={8:F2} m/s path={16:F0}m yawErrMax={17:F1}deg footfalls={9} probe={10}/{11}late minComH={12:F2} extMax={13:E2}N extWork={14:E2}J wall={15:F1}s", Name, Seconds, Fell, FellAt, FinalPhase, FinalCom.x, FinalCom.y, FinalCom.z, MeanSpeedLast10s, Footfalls, ProbeEvents, ProbeLate, MinComHeight, MaxExternalN, ExternalWorkJ, WallSeconds, PathLength, MaxYawErrorDeg);
    }

    public static class ScenarioLibrary
    {
        static readonly NVec3 Far = new NVec3(0, 0, 5000);

        public static ScenarioSpec Walk(string name, double froude, double seconds = 75) => new ScenarioSpec
        {
            Name = name, Seconds = seconds, CruiseFroude = froude, StripStart = 45,
            Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, Far, (float)l.EffortForFroude(froude))
        };

        public static IEnumerable<ScenarioSpec> All()
        {
            yield return new ScenarioSpec { Name = "stand", Seconds = 30, Script = (p, l) => p.At(0, MoveId.Stand, default, 0), StripStart = 0, StripInterval = 2, StripFrames = 8 };
            yield return Walk("walk_fr010", 0.10);
            yield return Walk("walk_fr020", 0.20);
            yield return Walk("walk_fr030", 0.30);
            yield return Walk("walk_fr025", 0.25);
            yield return new ScenarioSpec { Name = "start", Seconds = 45, CruiseFroude = 0.20, StripStart = 4, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(5, MoveId.Walk, Far, (float)l.EffortForFroude(0.20)) };
            yield return new ScenarioSpec { Name = "stop", Seconds = 70, CruiseFroude = 0.20, StripStart = 44, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, Far, (float)l.EffortForFroude(0.20)).At(45, MoveId.Stop, default, 0) };
            yield return new ScenarioSpec { Name = "estop", Seconds = 70, CruiseFroude = 0.20, StripStart = 44, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, Far, (float)l.EffortForFroude(0.20)).At(45, MoveId.EmergencyStop, default, 0) };
            yield return new ScenarioSpec { Name = "turn90_inplace", Seconds = 60, CruiseFroude = 0.20, StripStart = 4, StripInterval = 1.5, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Turn, new NVec3(5000, 0, 0), 0) };
            yield return new ScenarioSpec { Name = "curve", Seconds = 90, CruiseFroude = 0.20, StripStart = 40, StripInterval = 1.0, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, Far, (float)l.EffortForFroude(0.20)).At(40, MoveId.Walk, new NVec3(3000, 0, 3300), (float)l.EffortForFroude(0.20)) };
            yield return new ScenarioSpec { Name = "walk_scn", Seconds = 150, CruiseFroude = 0.20, StripStart = 4, StripInterval = 3, StripFrames = 48, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(5, MoveId.Walk, new NVec3(0, 0, 330), (float)l.EffortForFroude(0.20)).At(62, MoveId.Turn, new NVec3(5000, 0, 330), 0).At(95, MoveId.Walk, new NVec3(250, 0, 330), (float)l.EffortForFroude(0.20)) };
            yield return new ScenarioSpec { Name = "stomp", Seconds = 30, StripStart = 2, StripInterval = 0.75, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Stomp, new NVec3(20, 0, 0), 1).At(22, MoveId.Stand, default, 0) };
            yield return new ScenarioSpec
            {
                Name = "intent_over_limit", Seconds = 150, CruiseFroude = 0.20, StripStart = 2, StripInterval = 4, StripFrames = 36,
                // 3x cruise speed, instant reversal, 180 degree heading snap, stop in one step, a target out of reach, 30 Hz stick jitter
                Script = (p, l) =>
                {
                    float cruise = (float)l.EffortForFroude(0.20);
                    p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, Far, 3 * cruise).At(40, MoveId.Walk, new NVec3(0, 0, -5000), 3 * cruise).At(75, MoveId.Walk, new NVec3(0, 0, 1e7f), cruise).At(100, MoveId.Stop, default, 0);
                    for (int i = 0; i < 300; i++) p.At(115 + i / 60.0, MoveId.Walk, i % 2 == 0 ? Far : new NVec3(0, 0, -5000), i % 2 == 0 ? 1f : 0.2f);
                    p.At(120.1, MoveId.Stop, default, 0);
                }
            };
            yield return Shove("shove_005", 0.05); yield return Shove("shove_030", 0.30); yield return Shove("shove_060", 0.60); yield return Shove("shove_100", 1.00);
            yield return new ScenarioSpec { Name = "repeat60", Seconds = 60, CruiseFroude = 0.20, Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(3, MoveId.Walk, new NVec3(0, 0, 70), (float)l.EffortForFroude(0.10)).At(30, MoveId.Turn, new NVec3(2887, 0, 5070), 0).At(48, MoveId.Stop, default, 0) };
            yield return new ScenarioSpec { Name = "single_stance", Seconds = 30, StripStart = 2, StripInterval = 2, StripFrames = 14, Config = c => c.WithWalk(w => { w.stomp_speed_tstar = w.touchdown_speed_tstar; w.step_time_max_tstar *= 4; }), Script = (p, l) => p.At(0, MoveId.Stand, default, 0).At(10, MoveId.Stomp, new NVec3(20, 0, 0), 1) };
        }

        /// <summary>C15: a real external force on the torso (fraction of body weight, horizontal, forward) for 2 s while standing. Logged in ext_f and external_work.</summary>
        static ScenarioSpec Shove(string name, double bodyWeights) => new ScenarioSpec
        {
            Name = name, Seconds = 40, StripStart = 9, StripInterval = 1.0, Script = (p, l) => p.At(0, MoveId.Stand, default, 0),
            External = (sim, unit, t) =>
            {
                if (t < 10 || t >= 12) return;
                int torso = unit.Body.FindLink("torso"); unit.Body.GetLink(torso, out var s);
                unit.Body.AddExternalForce(torso, new NVec3(0, 0, (float)(bodyWeights * unit.Body.TotalMassKg * unit.Body.Tuning.gravity_mps2)), s.Com);
            }
        };

        public static ScenarioSpec Find(string name) { foreach (var s in All()) if (s.Name == name) return s; throw new ArgumentException("unknown scenario " + name); }
    }

    public static class ScenarioRunner
    {
        /// <summary>Runs one scenario with one mech at the origin. Writes gait_log@v1 and physics_log@v1 under <paramref name="outDir"/> when given.</summary>
        public static ScenarioResult Run(ScenarioSpec spec, MechConfig baseConfig, double stepSeconds, string outDir, string commit, string engineInfoJson, bool strips, string captureName = null, bool physicsLog = true)
        {
            var config = spec.Config != null ? spec.Config(baseConfig) : baseConfig;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            using (var sim = new MechSimulation(config.Body, stepSeconds, true, spec.SlopeDeg))
            {
                if (engineInfoJson != null) sim.World.EngineInfoJson = engineInfoJson;
                var pilot = new ScriptedPilotSource(sim.Core.Clock);
                var unit = sim.Spawn(config, new PilotSeat(new SinglePilotArbiter(), pilot), Vector3.zero);
                spec.Script(pilot, unit.Controller.Limits);
                var r = new ScenarioResult { Name = captureName ?? spec.Name, Seconds = spec.Seconds };
                unit.FootfallProbe = (e, receivedAt) => { r.ProbeEvents++; if (receivedAt != e.Step) r.ProbeLate++; r.Footfalls++; };
                if (outDir != null) unit.StartLogs(outDir, captureName ?? spec.Name, commit, spec.CruiseFroude, physicsLog);
                StripCapture strip = strips && outDir != null && spec.StripStart >= 0 ? new StripCapture(unit, spec.StripFrames) : null;
                double nextFrame = spec.StripStart; Vector3 com10 = Vector3.zero; bool have10 = false;
                long steps = (long)Math.Round(spec.Seconds / stepSeconds);
                for (long i = 0; i < steps; i++)
                {
                    spec.External?.Invoke(sim, unit, sim.Time);
                    sim.StepOnce();
                    var b = unit.Body;
                    r.MaxExternalN = Math.Max(r.MaxExternalN, b.ExternalForceMagnitudeN);
                    r.MinComHeight = Math.Min(r.MinComHeight, b.Com.Y);
                    if (!r.Fell) { r.PathLength += Math.Sqrt(b.ComVelocity.X * b.ComVelocity.X + b.ComVelocity.Z * b.ComVelocity.Z) * stepSeconds; b.GetLink(0, out var root); var fw = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, root.Rotation); double ye = Math.Abs(IntentLimiter.Wrap(Math.Atan2(fw.X, fw.Z) - unit.Command.Heading)) * 180 / Math.PI; if (ye > r.MaxYawErrorDeg) r.MaxYawErrorDeg = ye; }
                    if (!r.Fell && (unit.Phase == LocoPhase.Falling || unit.Phase == LocoPhase.Fallen)) { r.Fell = true; r.FellAt = sim.Time; }
                    if (!have10 && sim.Time >= spec.Seconds - 10) { com10 = new Vector3(b.Com.X, b.Com.Y, b.Com.Z); have10 = true; }
                    if (strip != null && sim.Time >= nextFrame && strip.Count < spec.StripFrames) { strip.Capture(sim.Time); nextFrame += spec.StripInterval; }
                }
                r.FinalPhase = unit.Phase; r.FinalCom = new Vector3(unit.Body.Com.X, unit.Body.Com.Y, unit.Body.Com.Z); r.ExternalWorkJ = unit.Body.ExternalWorkJ;
                Vector3 d = r.FinalCom - com10; d.y = 0; r.MeanSpeedLast10s = d.magnitude / 10.0;
                unit.CloseLogs();
                if (strip != null) { strip.Save(Path.Combine(outDir, "strips"), captureName ?? spec.Name); strip.Dispose(); }
                r.WallSeconds = wall.Elapsed.TotalSeconds;
                return r;
            }
        }
    }
}
