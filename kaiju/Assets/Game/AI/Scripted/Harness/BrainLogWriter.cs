using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Game.AI.Harness
{
    /// <summary>brain_log@v1 writer (R-024 section 4): JSON Lines, UTF-8, "\n", invariant culture, round-trip floats.</summary>
    public sealed class BrainLogWriter
    {
        private readonly TextWriter _w;
        private readonly StringBuilder _sb = new StringBuilder(512);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public BrainLogWriter(TextWriter w) { _w = w; }

        public static string F(float v) => v.ToString("R", Inv);
        public static string D(double v) => v.ToString("R", Inv);
        private static string Ent(ulong v) => "ent:" + v.ToString(Inv);

        private void Emit() { _sb.Append('\n'); _w.Write(_sb.ToString()); _sb.Clear(); }
        public void Flush() => _w.Flush();

        public static ulong ConfigHash(HarnessScenario s)
        {
            BrainConfig c = s.Brain;
            string t = string.Join("|", new[]
            {
                c.BrainPeriodSteps.ToString(Inv), F(c.SightRangeM), F(c.MemorySeconds), F(c.AllyAwarenessM), F(c.CruiseSpeedMps), F(c.MeleeReachM),
                F(c.StandoffM), F(c.EngageRadiusM), F(c.WingOffsetRad), F(c.RetreatHpFrac), F(c.RetargetRatio), F(c.RetargetMinSeconds),
                F(c.FacingHalfAngleRad), F(c.WeightDistance), F(c.WeightHp), F(c.WeightFacing), F(c.WeightFocus), F(c.SpeedApproach),
                F(c.SpeedApproachWing), F(c.SpeedFlank), F(c.SpeedCharge), F(c.SpeedRetreat), F(c.CentreHoldTimeoutSeconds),
                F(c.FlankTimeoutSeconds), F(c.FlankBearingTolRad), F(c.ChargeNoContactSeconds), F(c.RetreatSafeDistanceM), F(c.RetreatSafeSeconds),
            });
            ulong h = 14695981039346656037UL;
            foreach (char ch in t) { h ^= ch; h *= 1099511628211UL; }
            return h;
        }

        public void WriteHeader(HarnessScenario s)
        {
            _sb.Append("{\"kind\":\"header\",\"schema\":\"brain_log@v1\",\"run_id\":\"").Append(s.Name).Append('_').Append(s.Seed.ToString(Inv))
               .Append("\",\"seed\":").Append(s.Seed.ToString(Inv))
               .Append(",\"step_seconds\":").Append(F(s.StepSeconds))
               .Append(",\"brain_period_steps\":").Append(s.Brain.BrainPeriodSteps.ToString(Inv))
               .Append(",\"brain_id\":\"").Append(s.Brain.DirectCharge ? "directcharge@1" : "scripted@1").Append("\",\"config_hash\":\"").Append(ConfigHash(s).ToString("x16", Inv))
               .Append("\",\"size_class\":{\"H_m\":80,\"mass_t\":10000},\"d_eng_m\":400,\"scenario\":\"").Append(s.Name).Append("\"}");
            Emit();
        }

        public void WriteDecision(long step, double t, ulong ent, uint pack, string role, bool alive, float x, float y, float heading,
                                  float speed, float hp, in BrainIntent intent, BrainState prev, BrainObservation obs)
        {
            ulong tgt = intent.TargetId.Value;
            int vis = 0; float age = 0f;
            for (int i = 0; i < obs.TargetCount; i++)
                if (obs.Targets[i].Id.Value == tgt && tgt != 0) { vis = obs.Targets[i].Visible ? 1 : 0; age = obs.Targets[i].MemoryAge; break; }
            _sb.Append("{\"kind\":\"decision\",\"step\":").Append(step.ToString(Inv))
               .Append(",\"t\":").Append(D(t))
               .Append(",\"ent\":\"").Append(Ent(ent)).Append("\",\"pack\":").Append(pack.ToString(Inv))
               .Append(",\"role\":\"").Append(role).Append("\",\"alive\":").Append(alive ? 1 : 0)
               .Append(",\"x\":").Append(F(x)).Append(",\"y\":").Append(F(y)).Append(",\"heading_rad\":").Append(F(heading))
               .Append(",\"speed\":").Append(F(speed)).Append(",\"hp\":").Append(F(hp))
               .Append(",\"state\":").Append(((int)intent.State).ToString(Inv)).Append(",\"prev_state\":").Append(((int)prev).ToString(Inv))
               .Append(",\"reason\":").Append(((int)intent.Reason).ToString(Inv))
               .Append(",\"target\":\"").Append(Ent(tgt)).Append("\",\"tgt_visible\":").Append(vis.ToString(Inv))
               .Append(",\"tgt_mem_age_s\":").Append(F(age))
               .Append(",\"i_heading_rad\":").Append(F(intent.DesiredHeadingRad)).Append(",\"i_speed_frac\":").Append(F(intent.SpeedFrac))
               .Append(",\"i_target\":\"").Append(Ent(tgt)).Append("\",\"i_action\":").Append(((int)intent.Action).ToString(Inv))
               .Append(",\"score_cur\":0,\"score_best\":0}"); // scores are not exposed by IKaijuBrain v1
            Emit();
        }

        public void WriteTargetSample(long step, double t, ulong ent, string kind, bool alive, float x, float y, float heading, float hp)
        {
            _sb.Append("{\"kind\":\"target_sample\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t))
               .Append(",\"ent\":\"").Append(Ent(ent)).Append("\",\"tkind\":\"").Append(kind).Append("\",\"alive\":").Append(alive ? 1 : 0)
               .Append(",\"x\":").Append(F(x)).Append(",\"y\":").Append(F(y)).Append(",\"heading_rad\":").Append(F(heading))
               .Append(",\"hp\":").Append(F(hp)).Append('}');
            Emit();
        }

        public void WriteHit(long step, ulong attacker, ulong victim, float damage, float victimHpAfter)
        {
            _sb.Append("{\"kind\":\"hit\",\"step\":").Append(step.ToString(Inv)).Append(",\"attacker\":\"").Append(Ent(attacker))
               .Append("\",\"victim\":\"").Append(Ent(victim)).Append("\",\"damage\":").Append(F(damage))
               .Append(",\"victim_hp_after\":").Append(F(victimHpAfter)).Append('}');
            Emit();
        }

        public void WriteDeath(long step, ulong ent)
        {
            _sb.Append("{\"kind\":\"death\",\"step\":").Append(step.ToString(Inv)).Append(",\"ent\":\"").Append(Ent(ent)).Append("\"}");
            Emit();
        }
    }
}
