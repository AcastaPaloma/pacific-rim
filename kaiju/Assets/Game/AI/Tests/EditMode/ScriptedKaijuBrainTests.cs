using System;
using Game.AI;
using Game.Core;
using NUnit.Framework;

namespace Game.AI.Tests
{
    public class ScriptedKaijuBrainTests
    {
        private static EntityId E(ulong v) => EntityId.FromValue(v);

        private sealed class Rig
        {
            public readonly ScriptedKaijuBrain Brain = new ScriptedKaijuBrain();
            public readonly BrainObservation Obs = new BrainObservation();
            public BrainIntent Intent;
            public long Step;

            public Rig(ulong selfId = 1, int period = 1, float hp = 1f)
            {
                var cfg = BrainConfig.Default(); cfg.BrainPeriodSteps = period;
                Brain.Reset(42, cfg);
                Obs.Self = new SelfObs { Id = E(selfId), Pos = new Float2(0, 0), HeadingRad = 0.3f, HpFrac = hp };
            }
            public Rig At(double seconds) { Obs.TimeSeconds = seconds; Obs.Step = (long)Math.Round(seconds / 0.02); return this; }
            public Rig Targets(params TargetObs[] t)
            {
                Obs.TargetCount = t.Length; for (int i = 0; i < t.Length; i++) Obs.Targets[i] = t[i]; return this;
            }
            public Rig Allies(params AllyObs[] a)
            {
                Obs.AllyCount = a.Length; for (int i = 0; i < a.Length; i++) Obs.Allies[i] = a[i]; return this;
            }
            public BrainIntent Tick(double seconds) { At(seconds); Brain.Decide(Obs, ref Intent); return Intent; }
        }

        private static TargetObs T(ulong id, float x, float y = 0, float hp = 1f, bool alive = true, bool visible = true, float memAge = 0f)
            => new TargetObs { Id = E(id), Pos = new Float2(x, y), HeadingRad = 0f, HpFrac = hp, Alive = alive, Visible = visible, MemoryAge = memAge };

        private static AllyObs A(ulong id, float x, float y = 0, BrainState st = BrainState.Approach, ulong target = 100)
            => new AllyObs { Id = E(id), Pos = new Float2(x, y), HpFrac = 1f, Alive = true, State = (byte)st, TargetId = E(target) };

        [Test] public void ContractVersionIs1() => Assert.AreEqual(1, new ScriptedKaijuBrain().ContractVersion);

        [Test]
        public void Idle_StaysIdleWithoutTarget_ThenAcquires()
        {
            var r = new Rig().Targets();
            var i = r.Tick(0);
            Assert.AreEqual(BrainState.Idle, i.State); Assert.AreEqual(0f, i.SpeedFrac); Assert.AreEqual(0ul, i.TargetId.Value);
            Assert.AreEqual(0.3f, i.DesiredHeadingRad);
            i = r.Targets(T(100, 800)).Tick(1);
            Assert.AreEqual(BrainState.Approach, i.State); Assert.AreEqual(ReasonCode.TargetAcquired, i.Reason);
            Assert.AreEqual(0.6f, i.SpeedFrac); Assert.AreEqual(100ul, i.TargetId.Value); Assert.AreEqual(0f, i.DesiredHeadingRad, 1e-5f);
        }

        [Test]
        public void Idle_RememberedTargetCounts_ButBeyondMemoryDoesNot()
        {
            var r = new Rig().Targets(T(100, 800, visible: false, memAge: 2.5f));
            Assert.AreEqual(BrainState.Approach, r.Tick(0).State);
            var r2 = new Rig().Targets(T(100, 800, visible: false, memAge: 3.5f));
            Assert.AreEqual(BrainState.Idle, r2.Tick(0).State);
        }

        [Test]
        public void Approach_Solo_ChargesAtStandoff()
        {
            var r = new Rig().Targets(T(100, 800));
            r.Tick(0);
            Assert.AreEqual(BrainState.Approach, r.Targets(T(100, 251)).Tick(1).State);
            var i = r.Targets(T(100, 250)).Tick(2);
            Assert.AreEqual(BrainState.Charge, i.State); Assert.AreEqual(ReasonCode.InStandoffSolo, i.Reason); Assert.AreEqual(1f, i.SpeedFrac);
        }

        [Test]
        public void Approach_Wing_GoesToFlank_AtStandoff()
        {
            var r = new Rig(selfId: 1).Allies(A(2, 100, 200)).Targets(T(100, 800));
            var i = r.Tick(0);
            Assert.AreEqual(0.8f, i.SpeedFrac); // wing approach speed
            i = r.Targets(T(100, 200)).Tick(1);
            Assert.AreEqual(BrainState.Flank, i.State); Assert.AreEqual(ReasonCode.InStandoffWing, i.Reason); Assert.AreEqual(0.8f, i.SpeedFrac);
        }

        [Test]
        public void Approach_Centre_HoldsThenChargesOnAllyCharge()
        {
            var r = new Rig(selfId: 2).Allies(A(1, 100, 200)).Targets(T(100, 800));
            Assert.AreEqual(0.6f, r.Tick(0).SpeedFrac);
            var i = r.Targets(T(100, 240)).Tick(1);
            Assert.AreEqual(BrainState.Approach, i.State); Assert.AreEqual(0f, i.SpeedFrac);
            i = r.Tick(3);
            Assert.AreEqual(BrainState.Approach, i.State);
            r.Allies(A(1, 100, 200, BrainState.Charge));
            i = r.Tick(4);
            Assert.AreEqual(BrainState.Charge, i.State); Assert.AreEqual(ReasonCode.AllyCharging, i.Reason);
        }

        [Test]
        public void Approach_Centre_ChargesAfterHoldTimeout()
        {
            var r = new Rig(selfId: 2).Allies(A(1, 100, 200)).Targets(T(100, 240));
            r.Tick(0); // acquire
            r.Tick(1); // hold starts at t=1
            Assert.AreEqual(BrainState.Approach, r.Tick(6.9).State);
            var i = r.Tick(7.0);
            Assert.AreEqual(BrainState.Charge, i.State); Assert.AreEqual(ReasonCode.CentreHoldTimeout, i.Reason);
        }

        [Test]
        public void Flank_SlotReached_Charges()
        {
            // target at origin; self on bearing 0 at 250 m; ally placed so the pack-centroid bearing is -70 deg => slot bearing 0.
            const float r = 150f; float a = -70f * (float)Math.PI / 180f;
            float cx = r * (float)Math.Cos(a), cy = r * (float)Math.Sin(a);
            var self = new Float2(250, 0);
            var rig = new Rig(selfId: 1).Allies(A(2, 2 * cx - self.X, 2 * cy - self.Y)).Targets(T(100, 0));
            rig.Obs.Self.Pos = new Float2(900, 0);
            rig.Tick(0);                       // Approach
            rig.Obs.Self.Pos = new Float2(200, 100);
            Assert.AreEqual(BrainState.Flank, rig.Tick(1).State);
            rig.Obs.Self.Pos = self;
            var i = rig.Tick(2);
            Assert.AreEqual(BrainState.Charge, i.State); Assert.AreEqual(ReasonCode.FlankSlotReached, i.Reason);
        }

        [Test]
        public void Flank_HeadsToWaypoint_Timeout_AndAllyCharge()
        {
            var rig = new Rig(selfId: 1).Allies(A(2, 200, -300)).Targets(T(100, 200, 0));
            rig.Obs.Self.Pos = new Float2(0, 0);
            rig.Tick(0);
            var i = rig.Tick(1);
            Assert.AreEqual(BrainState.Flank, i.State);
            i = rig.Tick(5);
            Assert.AreEqual(BrainState.Flank, i.State);
            // waypoint: centroid (100,-150) seen from target (200,0): bearing atan2(-150,-100); +70 deg
            float ang = (float)Math.Atan2(-150, -100) + 70f * (float)Math.PI / 180f;
            float wx = 200 + 250 * (float)Math.Cos(ang), wy = 250 * (float)Math.Sin(ang);
            Assert.AreEqual(Math.Atan2(wy, wx), i.DesiredHeadingRad, 1e-4);
            // R-024 rev 2: deadline = margin * arc / speed + slack (>= 10 s); self is ~180 deg round the arc => a long timeout
            double tCharge = -1; ReasonCode rc = ReasonCode.None;
            for (double t = 6; t < 300; t += 0.5) { i = rig.Tick(t); if (i.State == BrainState.Charge) { tCharge = t; rc = i.Reason; break; } }
            Assert.AreEqual(ReasonCode.FlankTimeout, rc);
            Assert.Greater(tCharge, 30.0, "arc-derived timeout must exceed the old fixed 10 s");

            var rig2 = new Rig(selfId: 1).Allies(A(2, 200, -300)).Targets(T(100, 200, 0));
            rig2.Tick(0); rig2.Tick(1);
            rig2.Allies(A(2, 200, 300, BrainState.Charge));
            i = rig2.Tick(2);
            Assert.AreEqual(BrainState.Charge, i.State); Assert.AreEqual(ReasonCode.AllyCharging, i.Reason);
        }

        [Test]
        public void Charge_MeleeInReach_NoContactReturnsToApproach()
        {
            var r = new Rig().Targets(T(100, 200));
            r.Tick(0);
            var i = r.Tick(1); Assert.AreEqual(BrainState.Charge, i.State);
            Assert.AreEqual(BrainAction.None, i.Action);
            i = r.Targets(T(100, 40)).Tick(2); Assert.AreEqual(BrainAction.Melee, i.Action);
            r.Targets(T(100, 100));
            Assert.AreEqual(BrainState.Charge, r.Tick(9.9).State);
            i = r.Tick(10.0);
            Assert.AreEqual(BrainState.Approach, i.State); Assert.AreEqual(ReasonCode.ChargeNoContact, i.Reason);
        }

        [Test]
        public void LowHp_Retreats_FromApproachFlankAndCharge()
        {
            foreach (var setup in new[] { 800f, 200f })
            {
                var r = new Rig().Targets(T(100, setup));
                r.Tick(0); r.Tick(1);
                r.Obs.Self.HpFrac = 0.34f;
                var i = r.Tick(2);
                Assert.AreEqual(BrainState.Retreat, i.State); Assert.AreEqual(ReasonCode.LowHp, i.Reason);
                Assert.AreEqual(0.7f, i.SpeedFrac); Assert.AreEqual(0ul, i.TargetId.Value);
                Assert.AreEqual(BrainAction.None, i.Action);
                Assert.AreEqual(Math.PI, Math.Abs(i.DesiredHeadingRad), 1e-5); // away from target on +x
            }
            var rf = new Rig(selfId: 1).Allies(A(2, 100, 200)).Targets(T(100, 200));
            rf.Tick(0); Assert.AreEqual(BrainState.Flank, rf.Tick(1).State);
            rf.Obs.Self.HpFrac = 0.1f;
            Assert.AreEqual(BrainState.Retreat, rf.Tick(2).State);
        }

        [Test]
        public void Retreat_ThenWithdrawn_NeverReengages()
        {
            var r = new Rig(hp: 0.2f).Targets(T(100, 800));
            r.Tick(0); // Idle->Approach
            Assert.AreEqual(BrainState.Retreat, r.Tick(1).State);
            r.Targets(T(100, 700));
            Assert.AreEqual(BrainState.Retreat, r.Tick(2).State);
            r.Targets(T(100, 650));
            Assert.AreEqual(BrainState.Retreat, r.Tick(3).State);          // far since 2
            Assert.AreEqual(BrainState.Retreat, r.Tick(11.9).State);
            var i = r.Tick(12.0);
            Assert.AreEqual(BrainState.Withdrawn, i.State); Assert.AreEqual(ReasonCode.Withdrawn, i.Reason); Assert.AreEqual(0f, i.SpeedFrac);
            r.Targets(T(100, 300)); r.Obs.Self.HpFrac = 1f;
            Assert.AreEqual(BrainState.Withdrawn, r.Tick(20).State);
        }

        [Test]
        public void Retreat_ComingWithinSafeDistanceResetsTimer()
        {
            var r = new Rig(hp: 0.2f).Targets(T(100, 800));
            r.Tick(0); r.Tick(1);
            r.Targets(T(100, 700)); r.Tick(2);
            r.Targets(T(100, 500)); r.Tick(8);
            r.Targets(T(100, 700)); r.Tick(9);
            Assert.AreEqual(BrainState.Retreat, r.Tick(18.9).State);
            Assert.AreEqual(BrainState.Withdrawn, r.Tick(19.0).State);
        }

        [Test]
        public void TargetLost_GoesIdle_WhenNoOther()
        {
            var r = new Rig().Targets(T(100, 800));
            r.Tick(0);
            var i = r.Targets(T(100, 800, alive: false)).Tick(1);
            Assert.AreEqual(BrainState.Idle, i.State); Assert.AreEqual(ReasonCode.TargetLost, i.Reason);
            r.Targets(T(100, 800));
            Assert.AreEqual(BrainState.Approach, r.Tick(2).State);
            i = r.Targets(T(100, 800, visible: false, memAge: 3.1f)).Tick(3);
            Assert.AreEqual(BrainState.Idle, i.State);
        }

        [Test]
        public void TargetDead_ForcedRetargetIsImmediate_AndResetsChargeToApproach()
        {
            var r = new Rig().Targets(T(100, 200), T(101, 700));
            r.Tick(0); Assert.AreEqual(BrainState.Charge, r.Tick(0.5).State);
            var i = r.Targets(T(100, 200, alive: false), T(101, 700)).Tick(1);
            Assert.AreEqual(BrainState.Approach, i.State); Assert.AreEqual(ReasonCode.RetargetForced, i.Reason); Assert.AreEqual(101ul, i.TargetId.Value);
        }

        [Test]
        public void TargetSelection_PrefersHigherScore()
        {
            // 100: d=300 hp1 ; 101: d=900 hp1 -> 100 ; 102 far but nearly dead and nearer than 101
            var r = new Rig().Targets(T(100, 900), T(101, 300));
            Assert.AreEqual(101ul, r.Tick(0).TargetId.Value);
            var r2 = new Rig().Targets(T(100, 600, hp: 1f), T(101, 600, hp: 0.5f));
            Assert.AreEqual(101ul, r2.Tick(0).TargetId.Value);
            var r3 = new Rig().Targets(T(100, 600), T(101, 600)); // tie -> lowest id
            Assert.AreEqual(100ul, r3.Tick(0).TargetId.Value);
        }

        [Test]
        public void VoluntarySwitch_NeedsRatioAndFourSeconds()
        {
            var r = new Rig().Targets(T(100, 600));
            Assert.AreEqual(100ul, r.Tick(0).TargetId.Value);
            // B score 0.9 vs current 0.5 (1.8x) but only 1 s since acquisition
            r.Targets(T(100, 600), T(101, 600, hp: 0.5f));
            Assert.AreEqual(100ul, r.Tick(1).TargetId.Value);
            Assert.AreEqual(100ul, r.Tick(3.9).TargetId.Value);
            var i = r.Tick(4.0);
            Assert.AreEqual(101ul, i.TargetId.Value); Assert.AreEqual(ReasonCode.RetargetVoluntary, i.Reason);
            // switching back needs another 4 s
            r.Targets(T(100, 600, hp: 0.2f), T(101, 600, hp: 0.9f));
            Assert.AreEqual(101ul, r.Tick(5).TargetId.Value);
            Assert.AreEqual(100ul, r.Tick(8.0).TargetId.Value);
        }

        [Test]
        public void VoluntarySwitch_BelowRatioDoesNotSwitch()
        {
            var r = new Rig().Targets(T(100, 600));
            r.Tick(0);
            // B 0.5 + 0.08 = 0.58 < 1.25 * 0.5 = 0.625
            r.Targets(T(100, 600), T(101, 600, hp: 0.9f));
            Assert.AreEqual(100ul, r.Tick(10).TargetId.Value);
            // B 0.5 + 0.16 = 0.66 > 0.625
            r.Targets(T(100, 600), T(101, 600, hp: 0.8f));
            Assert.AreEqual(101ul, r.Tick(11).TargetId.Value);
        }

        [Test]
        public void FocusFire_AlliesOnTargetRaiseScore()
        {
            var r = new Rig(selfId: 1).Allies(A(2, 0, 100, target: 101)).Targets(T(100, 600), T(101, 600));
            Assert.AreEqual(101ul, r.Tick(0).TargetId.Value);
        }

        [Test]
        public void Hold_BetweenDecisions_AndStagger()
        {
            var r = new Rig(selfId: 3, period: 10).Targets();
            r.Obs.Step = 0; r.Brain.Decide(r.Obs, ref r.Intent);       // first call always decides
            Assert.AreEqual(BrainState.Idle, r.Intent.State);
            r.Targets(T(100, 800));
            for (long s = 1; s <= 12; s++)
            {
                r.Obs.Step = s; r.Obs.TimeSeconds = s * 0.02;
                r.Brain.Decide(r.Obs, ref r.Intent);
                if (s < 3) Assert.AreEqual(BrainState.Idle, r.Intent.State, "held at step " + s);
                else { Assert.AreEqual(BrainState.Approach, r.Intent.State, "decided at step " + s); }
            }
            // after step 3 decision, change the world at step 4; intent stays until step 13
            r.Targets(T(100, 100), T(101, 100));
            var held = r.Intent;
            for (long s = 13; s <= 22; s++)
            {
                r.Obs.Step = s; r.Obs.TimeSeconds = s * 0.02;
                r.Brain.Decide(r.Obs, ref r.Intent);
                if (s < 13) Assert.AreEqual(held.TargetId.Value, r.Intent.TargetId.Value);
            }
            Assert.AreEqual(BrainState.Charge, r.Intent.State);
        }

        [Test]
        public void HoldBetweenDecisions_IntentIdenticalUntilNextPhase()
        {
            var r = new Rig(selfId: 5, period: 10).Targets(T(100, 800));
            r.Obs.Step = 0; r.Brain.Decide(r.Obs, ref r.Intent);
            r.Obs.Step = 5; r.Obs.TimeSeconds = 0.1; r.Brain.Decide(r.Obs, ref r.Intent);
            var d5 = r.Intent;
            r.Targets(T(100, -800));               // world flips
            for (long s = 6; s < 15; s++) { r.Obs.Step = s; r.Obs.TimeSeconds = s * 0.02; r.Brain.Decide(r.Obs, ref r.Intent); Assert.AreEqual(d5.DesiredHeadingRad, r.Intent.DesiredHeadingRad); }
            r.Obs.Step = 15; r.Obs.TimeSeconds = 0.3; r.Brain.Decide(r.Obs, ref r.Intent);
            Assert.AreNotEqual(d5.DesiredHeadingRad, r.Intent.DesiredHeadingRad);
        }

        // ---- determinism and allocation ----

        private static ulong Xs(ref ulong s) { s ^= s >> 12; s ^= s << 25; s ^= s >> 27; return s * 0x2545F4914F6CDD1DUL; }
        private static float F(ref ulong s, float lo, float hi) => lo + (hi - lo) * ((Xs(ref s) >> 40) / (float)(1 << 24));

        private static void Fill(BrainObservation o, ref ulong rs, long step)
        {
            o.Step = step; o.TimeSeconds = step * 0.02;
            o.Self.Id = E(4); o.Self.Pos = new Float2(F(ref rs, -500, 500), F(ref rs, -500, 500)); o.Self.HeadingRad = F(ref rs, -3, 3);
            o.Self.HpFrac = step > 700 ? 0.2f : 1f;
            int nt = (int)(Xs(ref rs) % 5);
            o.TargetCount = nt;
            for (int i = 0; i < nt; i++)
                o.Targets[i] = new TargetObs { Id = E((ulong)(100 + i)), Pos = new Float2(F(ref rs, -1100, 1100), F(ref rs, -1100, 1100)), HeadingRad = F(ref rs, -3, 3), HpFrac = F(ref rs, 0, 1), Alive = (Xs(ref rs) % 8) != 0, Visible = (Xs(ref rs) % 4) != 0, MemoryAge = F(ref rs, 0, 4) };
            int na = (int)(Xs(ref rs) % 4);
            o.AllyCount = na;
            for (int i = 0; i < na; i++)
                o.Allies[i] = new AllyObs { Id = E((ulong)(1 + i * 2)), Pos = new Float2(F(ref rs, -800, 800), F(ref rs, -800, 800)), HpFrac = 1f, Alive = (Xs(ref rs) % 6) != 0, State = (byte)(Xs(ref rs) % 5), TargetId = E(100 + Xs(ref rs) % 5) };
        }

        private static ulong Hash(ulong h, in BrainIntent i)
        {
            unchecked
            {
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(i.DesiredHeadingRad)) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(i.SpeedFrac)) * 1099511628211UL;
                h = (h ^ i.TargetId.Value) * 1099511628211UL;
                h = (h ^ (ulong)i.Action) * 1099511628211UL;
                h = (h ^ (ulong)i.State) * 1099511628211UL;
                h = (h ^ (ulong)i.Reason) * 1099511628211UL;
                return h;
            }
        }

        private static ulong RunStream(ulong brainSeed, ulong streamSeed, out int stateMask)
        {
            var b = new ScriptedKaijuBrain(); b.Reset(brainSeed, BrainConfig.Default());
            var o = new BrainObservation(); BrainIntent intent = default;
            ulong rs = streamSeed, h = 14695981039346656037UL; stateMask = 0;
            for (long s = 0; s < 1000; s++)
            {
                Fill(o, ref rs, s);
                b.Decide(o, ref intent);
                h = Hash(h, intent); stateMask |= 1 << (int)intent.State;
            }
            return h;
        }

        [Test]
        public void Determinism_SameSeedSameStream_IdenticalIntents_1000Ticks()
        {
            ulong a = RunStream(7, 99, out int m1), b = RunStream(7, 99, out int m2), c = RunStream(7, 100, out _);
            Assert.AreEqual(a, b); Assert.AreEqual(m1, m2);
            Assert.AreNotEqual(a, c);
            int bits = 0; for (uint v = (uint)m1; v != 0; v &= v - 1) bits++;
            Assert.That(bits, Is.GreaterThanOrEqualTo(3), "stream should exercise several states");
        }

        [Test]
        public void Decide_AllocatesNothing()
        {
            var b = new ScriptedKaijuBrain(); b.Reset(1, BrainConfig.Default());
            var o = new BrainObservation(); BrainIntent intent = default; ulong rs = 5;
            for (long s = 0; s < 200; s++) { Fill(o, ref rs, s); b.Decide(o, ref intent); } // warm up / JIT
            Fill(o, ref rs, 300);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { o.Step = i; o.TimeSeconds = i * 0.02; b.Decide(o, ref intent); }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.AreEqual(0, after - before);
        }
    }
}
