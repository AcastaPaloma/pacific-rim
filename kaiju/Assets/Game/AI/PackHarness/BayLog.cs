// brain_log@v2 writer (R-103 6.4; row contract in Tools/m007_bay.py). JSON Lines, invariant culture, round-trip floats.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Game.AI.Pack;

namespace Game.AI.PackHarness
{
    public sealed class BayLog
    {
        readonly TextWriter _w;
        readonly StringBuilder _sb = new StringBuilder(512);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        float _dt = 0.1f;

        public BayLog(TextWriter w) { _w = w; }
        static string F(float v) => v.ToString("R", Inv);
        static string D(double v) => v.ToString("R", Inv);
        static string E(ulong v) => "ent:" + v.ToString(Inv);
        void Emit() { _sb.Append('\n'); _w.Write(_sb.ToString()); _sb.Clear(); }
        double T(long step) => step * (double)_dt;

        public void Header(BayScenario s, int logPeriodSteps, bool packLayer)
        {
            _dt = s.StepSeconds;
            ulong h = 14695981039346656037UL;
            foreach (char ch in string.Join("|", s.BrainId, s.Name, s.Composition, F(s.StepSeconds), s.DecisionEverySteps.ToString(Inv), s.CoordinatorEverySteps.ToString(Inv), F(s.CallRangeM), F(s.HearingRangeM), F(s.MechSpeed), F(s.InfraDamageRate),
                         F(s.Pack.RaidDamageRate), F(s.Pack.MechStrength), F(s.Pack.MechValue))) { h ^= ch; h *= 1099511628211UL; }
            _sb.Append("{\"kind\":\"header\",\"schema\":\"brain_log@v2\",\"run_id\":\"").Append(s.ScenarioId).Append('_').Append(s.Composition).Append('_').Append(s.BrainId).Append('_').Append(s.Seed.ToString(Inv))
               .Append("\",\"seed\":").Append(s.Seed.ToString(Inv)).Append(",\"step_seconds\":").Append(F(s.StepSeconds))
               .Append(",\"brain_period_steps\":").Append(logPeriodSteps.ToString(Inv))
               .Append(",\"brain_id\":\"").Append(s.BrainId).Append("\",\"scenario\":\"").Append(s.ScenarioId).Append("\",\"composition\":\"").Append(s.Composition)
               .Append("\",\"config_hash\":\"").Append(h.ToString("x16", Inv)).Append("\",\"call_range_m\":").Append(F(s.CallRangeM))
               .Append(",\"map\":\"").Append(s.Map == "water" ? SyntheticBayField.MapId : "m007_bay_synth@v1").Append("\",\"decision_period_steps\":").Append(s.DecisionEverySteps.ToString(Inv)).Append('}');
            Emit();
        }

        static char SpeciesLetter(int sp) => sp == 1 ? 'L' : sp == 2 ? 'M' : 'H';

        public void Decision(long step, double t, ulong ent, int species, int pack, string role, string goal, bool alive, float x, float y, float heading, float speed, float hp,
            in BrainIntent intent, BrainState prev, BrainObservation obs, float boardAge, string boardSource)
        {
            ulong tgt = intent.TargetId.Value;
            int vis = 0; float age = 0f;
            for (int i = 0; i < obs.TargetCount; i++)
                if (tgt != 0 && obs.Targets[i].Id.Value == tgt) { vis = obs.Targets[i].Visible ? 1 : 0; age = obs.Targets[i].MemoryAge; break; }
            _sb.Append("{\"kind\":\"decision\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t))
               .Append(",\"ent\":\"").Append(E(ent)).Append("\",\"pack\":").Append(pack.ToString(Inv))
               .Append(",\"species\":\"").Append(SpeciesLetter(species)).Append("\",\"role\":\"").Append(role).Append("\",\"goal\":\"").Append(goal)
               .Append("\",\"alive\":").Append(alive ? 1 : 0).Append(",\"x\":").Append(F(x)).Append(",\"y\":").Append(F(y)).Append(",\"heading_rad\":").Append(F(heading))
               .Append(",\"speed\":").Append(F(speed)).Append(",\"hp\":").Append(F(hp))
               .Append(",\"state\":").Append(((int)intent.State).ToString(Inv)).Append(",\"prev_state\":").Append(((int)prev).ToString(Inv))
               .Append(",\"reason\":").Append(((int)intent.Reason).ToString(Inv))
               .Append(",\"target\":\"").Append(E(tgt)).Append("\",\"tgt_visible\":").Append(vis.ToString(Inv)).Append(",\"tgt_mem_age_s\":").Append(F(age))
               .Append(",\"board_age_s\":").Append(F(boardAge)).Append(",\"board_source\":\"").Append(boardSource).Append("\"}");
            Emit();
        }

        public void Sample(long step, double t, ulong ent, bool alive, float x, float y, float heading, float hp)
        {
            _sb.Append("{\"kind\":\"target_sample\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"ent\":\"").Append(E(ent))
               .Append("\",\"tkind\":\"mech\",\"alive\":").Append(alive ? 1 : 0).Append(",\"x\":").Append(F(x)).Append(",\"y\":").Append(F(y))
               .Append(",\"heading_rad\":").Append(F(heading)).Append(",\"hp\":").Append(F(hp)).Append('}');
            Emit();
        }

        public void InfraRow(long step, double t, ulong ent, string cls, float hp, float hpMax, float x, float y)
        {
            _sb.Append("{\"kind\":\"infra\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"ent\":\"").Append(E(ent)).Append("\",\"id\":\"").Append(E(ent))
               .Append("\",\"class\":\"").Append(cls).Append("\",\"hp\":").Append(F(hp)).Append(",\"hp_max\":").Append(F(hpMax)).Append(",\"x\":").Append(F(x)).Append(",\"y\":").Append(F(y)).Append('}');
            Emit();
        }

        public void Hit(long step, ulong attacker, ulong victim, float damage, float victimHpAfter)
        {
            _sb.Append("{\"kind\":\"hit\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(T(step))).Append(",\"attacker\":\"").Append(E(attacker))
               .Append("\",\"victim\":\"").Append(E(victim)).Append("\",\"damage\":").Append(F(damage)).Append(",\"victim_hp_after\":").Append(F(victimHpAfter)).Append('}');
            Emit();
        }

        public void Death(long step, ulong ent)
        {
            _sb.Append("{\"kind\":\"death\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(T(step))).Append(",\"ent\":\"").Append(E(ent)).Append("\"}");
            Emit();
        }

        public void Call(long step, double t, ulong sender, List<ulong> delivered, float delay, float dist, float range)
        {
            _sb.Append("{\"kind\":\"call\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"sender\":\"").Append(E(sender)).Append("\",\"delivered_to\":[");
            for (int i = 0; i < delivered.Count; i++) { if (i > 0) _sb.Append(','); _sb.Append('"').Append(E(delivered[i])).Append('"'); }
            _sb.Append("],\"delay_s\":").Append(F(delay)).Append(",\"lost\":0,\"dist_m\":").Append(F(dist)).Append(",\"range_m\":").Append(F(range)).Append('}');
            Emit();
        }

        public void RoleChange(long step, double t, ulong member, string from, string to, string reason)
        {
            _sb.Append("{\"kind\":\"role_change\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"member\":\"").Append(E(member))
               .Append("\",\"from\":\"").Append(from).Append("\",\"to\":\"").Append(to).Append("\",\"reason\":\"").Append(reason).Append("\"}");
            Emit();
        }

        public void GoalChange(long step, double t, int pack, string goal, ulong target)
        {
            _sb.Append("{\"kind\":\"goal_change\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"pack\":").Append(pack.ToString(Inv))
               .Append(",\"goal\":\"").Append(goal).Append("\",\"target\":").Append(target == 0 ? "null" : "\"" + E(target) + "\"").Append('}');
            Emit();
        }

        public void MechTarget(long step, double t, ulong mech, ulong target)
        {
            _sb.Append("{\"kind\":\"mech_target\",\"step\":").Append(step.ToString(Inv)).Append(",\"t\":").Append(D(t)).Append(",\"mech\":\"").Append(E(mech))
               .Append("\",\"target\":\"").Append(E(target)).Append("\"}");
            Emit();
        }
    }
}
