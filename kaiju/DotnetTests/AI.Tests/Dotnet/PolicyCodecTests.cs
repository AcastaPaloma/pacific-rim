using System;
using Game.AI;
using Game.AI.Policy;
using Game.Core;
using NUnit.Framework;

namespace Game.AI.Tests
{
    /// <summary>T-041: RL policy observation encoder / action decoder (R-028 sections 2-3).</summary>
    public class PolicyCodecTests
    {
        private static readonly BrainConfig Cfg = BrainConfig.Default();
        private static readonly PolicyConfig PCfg = PolicyConfig.Default();

        private static TargetObs T(ulong id, float x, float y, float hp = 1f, bool vis = true, float age = 0f, bool alive = true, float head = 0f) =>
            new TargetObs { Id = EntityId.FromValue(id), Pos = new Float2(x, y), HpFrac = hp, Visible = vis, MemoryAge = age, Alive = alive, HeadingRad = head };

        private static AllyObs A(ulong id, float x, float y, ulong target = 0, byte state = 1, bool alive = true) =>
            new AllyObs { Id = EntityId.FromValue(id), Pos = new Float2(x, y), HpFrac = 0.9f, Alive = alive, State = state, TargetId = EntityId.FromValue(target), HeadingRad = 0.3f };

        /// <summary>Full observation: 8 targets (one dead, one forgotten) and 7 allies, sorted by id as the contract requires.</summary>
        private static BrainObservation Full(float selfHeading = 0.4f)
        {
            var o = new BrainObservation { Step = 120, TimeSeconds = 2.4 };
            o.Self = new SelfObs { Id = EntityId.FromValue(3), PackId = 1, Pos = new Float2(10f, -20f), HeadingRad = selfHeading, Speed = 5f, HpFrac = 0.7f, Cooldowns = 1f };
            o.TargetCount = 8;
            o.Targets[0] = T(101, 300f, 40f, 0.5f);
            o.Targets[1] = T(102, -500f, 200f);
            o.Targets[2] = T(103, 900f, -700f, 0.2f, vis: false, age: 1.5f);
            o.Targets[3] = T(104, 50f, 30f, 0.9f, head: 3.0f);
            o.Targets[4] = T(105, 100f, 800f, alive: false);
            o.Targets[5] = T(106, 1100f, 10f, vis: false, age: 3.5f);
            o.Targets[6] = T(107, -60f, -900f, 0.4f);
            o.Targets[7] = T(108, 400f, 400f, 0.6f);
            o.AllyCount = 7;
            ulong[] ids = { 1, 2, 4, 5, 6, 7, 8 };
            for (int i = 0; i < 7; i++) o.Allies[i] = A(ids[i], 100f * i - 250f, 70f * i, target: i % 3 == 0 ? 101UL : (i == 4 ? 999UL : 104UL), state: (byte)(i % 6), alive: i != 5);
            return o;
        }

        private static (float[] x, byte[] m, ObservationEncoder e) Enc(BrainObservation o, PolicyMemory mem)
        {
            var e = new ObservationEncoder(); var x = new float[PolicyLayout.ObsDim]; var m = new byte[PolicyLayout.MaskDim];
            e.Encode(o, Cfg, PCfg, mem, x, m);
            return (x, m, e);
        }

        [Test]
        public void Encode_NonFiniteObservation_IsSanitisedAndCounted()
        {
            var o = Full(); o.Self.Pos = new Float2(float.NaN, 5f); o.Targets[0].HpFrac = float.PositiveInfinity;
            var enc = new ObservationEncoder(); var x = new float[PolicyLayout.ObsDim]; var m = new byte[PolicyLayout.MaskDim];
            enc.Encode(o, Cfg, PCfg, PolicyMemory.Initial(), x, m);
            foreach (float v in x) Assert.IsTrue(float.IsFinite(v));
        }

        [Test]
        public void Layout_IsFixed()
        {
            Assert.That(PolicyLayout.ObsDim, Is.EqualTo(313));
            Assert.That(PolicyLayout.MaskDim, Is.EqualTo(19));
            Assert.That(PolicyLayout.SelfDim, Is.EqualTo(9 + PolicyLayout.NTargetChoices + PolicyLayout.NManeuvers));
            Assert.That(PolicyLayout.AllyDim, Is.EqualTo(8 + PolicyLayout.StateOneHot + PolicyLayout.NT + 2));
        }

        [Test]
        public void Encode_FixedSizeAndFinite_EmptyAndFull()
        {
            var empty = new BrainObservation();
            empty.Self = new SelfObs { Id = EntityId.FromValue(1), HpFrac = 1f };
            var big = new float[PolicyLayout.ObsDim + 5]; for (int i = 0; i < big.Length; i++) big[i] = 42f;
            var m = new byte[PolicyLayout.MaskDim];
            new ObservationEncoder().Encode(empty, Cfg, PCfg, PolicyMemory.Initial(), big, m);
            for (int i = PolicyLayout.ObsDim; i < big.Length; i++) Assert.That(big[i], Is.EqualTo(42f), "encoder wrote past ObsDim");
            // empty: only "none" target, only target-free maneuvers
            Assert.That(m[0], Is.EqualTo(1));
            for (int s = 1; s < PolicyLayout.NTargetChoices; s++) Assert.That(m[s], Is.EqualTo(0));
            Assert.That(m[PolicyLayout.MaskManeuverOffset + (int)Maneuver.Charge], Is.EqualTo(0));
            Assert.That(m[PolicyLayout.MaskManeuverOffset + (int)Maneuver.Retreat], Is.EqualTo(1));

            var (x, mk, _) = Enc(Full(), PolicyMemory.Initial());
            foreach (float v in x) Assert.That(float.IsFinite(v), Is.True);
            foreach (float v in x) Assert.That(Math.Abs(v), Is.LessThanOrEqualTo(PCfg.EgoClamp + 1e-6f));
            // 6 usable targets of 8 (105 dead, 106 forgotten)
            int usable = 0; for (int s = 1; s < PolicyLayout.NTargetChoices; s++) usable += mk[s];
            Assert.That(usable, Is.EqualTo(6));
        }

        [Test]
        public void Encode_IsInvariantToInputOrder()
        {
            var o1 = Full(); var o2 = Full();
            // reverse targets and rotate allies in o2 (violates the sorted-input rule on purpose: the encoder must not care)
            for (int i = 0; i < 8; i++) o2.Targets[i] = o1.Targets[7 - i];
            for (int i = 0; i < 7; i++) o2.Allies[i] = o1.Allies[(i + 3) % 7];
            var mem = PolicyMemory.Initial(); mem.CurrentTargetId = 104; mem.LastManeuver = 2;
            var (x1, m1, e1) = Enc(o1, mem); var (x2, m2, e2) = Enc(o2, mem);
            Assert.That(x2, Is.EqualTo(x1));
            Assert.That(m2, Is.EqualTo(m1));
            Assert.That(e2.TargetSlotIds, Is.EqualTo(e1.TargetSlotIds));
        }

        [Test]
        public void Encode_IsInvariantToEntityIdRelabel()
        {
            var o1 = Full(); var o2 = Full();
            Func<ulong, ulong> re = v => v == 0 ? 0 : (v == 999 ? 999 : 5000 - v * 7); // reverses id order, keeps uniqueness
            for (int i = 0; i < 8; i++) { var t = o2.Targets[i]; t.Id = EntityId.FromValue(re(t.Id.Value)); o2.Targets[i] = t; }
            for (int i = 0; i < 7; i++) { var a = o2.Allies[i]; a.Id = EntityId.FromValue(re(a.Id.Value)); a.TargetId = EntityId.FromValue(re(a.TargetId.Value)); o2.Allies[i] = a; }
            var mem1 = PolicyMemory.Initial(); mem1.CurrentTargetId = 101;
            var mem2 = mem1; mem2.CurrentTargetId = re(101);
            var (x1, m1, _) = Enc(o1, mem1); var (x2, m2, _) = Enc(o2, mem2);
            Assert.That(x2, Is.EqualTo(x1));
            Assert.That(m2, Is.EqualTo(m1));
        }

        [Test]
        public void Encode_SlotsSortedUsableFirstThenDistance()
        {
            var (x, _, e) = Enc(Full(), PolicyMemory.Initial());
            float prev = -1f; bool seenUnusable = false;
            for (int s = 0; s < PolicyLayout.NT; s++)
            {
                int o = PolicyLayout.TargetsOffset + s * PolicyLayout.TargetDim;
                Assert.That(x[o], Is.EqualTo(1f));
                if (!e.TargetSlotUsable[s]) { seenUnusable = true; continue; }
                Assert.That(seenUnusable, Is.False, "usable slot after an unusable one");
                Assert.That(x[o + 6], Is.GreaterThanOrEqualTo(prev)); prev = x[o + 6];
            }
            Assert.That(e.TargetSlotIds[0], Is.EqualTo(104UL)); // nearest
        }

        [Test]
        public void Encode_EgoFrame_RotatesWithHeading()
        {
            var o = new BrainObservation { TimeSeconds = 1 };
            o.Self = new SelfObs { Id = EntityId.FromValue(1), HeadingRad = MathF.PI / 2f, HpFrac = 1f };
            o.TargetCount = 1; o.Targets[0] = T(101, 0f, 600f); // due north = straight ahead
            var (x, _, _) = Enc(o, PolicyMemory.Initial());
            int b = PolicyLayout.TargetsOffset;
            Assert.That(x[b + 4], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(x[b + 5], Is.EqualTo(0f).Within(1e-5f));
            o.Targets[0] = T(101, -600f, 0f); // west = to the left
            (x, _, _) = Enc(o, PolicyMemory.Initial());
            Assert.That(x[b + 4], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(x[b + 5], Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void RoundTrip_TargetAndManeuver_FeedBackIntoNextEncoding()
        {
            var o = Full();
            for (int s = 0; s < PolicyLayout.NT; s++)
            {
                var mem = PolicyMemory.Initial();
                var (_, m, e) = Enc(o, mem);
                if (m[1 + s] == 0) continue;
                var intent = default(BrainIntent);
                ActionDecoder.Decode(new PolicyAction(1 + s, (int)Maneuver.FlankLeft, 1), o, Cfg, PCfg, e, ref mem, ref intent);
                Assert.That(intent.TargetId.Value, Is.EqualTo(e.TargetSlotIds[s]));
                Assert.That(intent.State, Is.EqualTo(BrainState.Flank));
                Assert.That(intent.SpeedFrac, Is.EqualTo(PCfg.SpeedTier1));
                Assert.That(intent.Reason, Is.EqualTo(ReasonCode.TargetAcquired));
                var (x2, _, e2) = Enc(o, mem);
                int cs = e2.SlotOf(intent.TargetId.Value);
                Assert.That(cs, Is.EqualTo(s));
                Assert.That(x2[9 + 1 + s], Is.EqualTo(1f));
                Assert.That(x2[18 + (int)Maneuver.FlankLeft], Is.EqualTo(1f));
                Assert.That(x2[PolicyLayout.TargetsOffset + s * PolicyLayout.TargetDim + 12], Is.EqualTo(1f));
            }
        }

        [Test]
        public void Decode_SanitisesMaskedChoices()
        {
            var o = Full();
            var mem = PolicyMemory.Initial();
            var (_, m, e) = Enc(o, mem);
            int dead = e.SlotOf(105);
            Assert.That(m[1 + dead], Is.EqualTo(0));
            var intent = default(BrainIntent);
            ActionDecoder.Decode(new PolicyAction(1 + dead, (int)Maneuver.Charge, 2), o, Cfg, PCfg, e, ref mem, ref intent);
            Assert.That(intent.TargetId.Value, Is.EqualTo(0UL), "dead target must not be targeted");
            Assert.That(intent.State, Is.EqualTo(BrainState.Idle), "Charge without a target degrades to Hold");
            Assert.That(intent.SpeedFrac, Is.EqualTo(0f));
            Assert.That(intent.Action, Is.EqualTo(BrainAction.None));
        }

        [Test]
        public void Decode_ReasonCodes_FromFacts()
        {
            var o = Full();
            var mem = PolicyMemory.Initial();
            var (_, _, e) = Enc(o, mem);
            var it = default(BrainIntent);
            int s104 = e.SlotOf(104), s101 = e.SlotOf(101);
            ActionDecoder.Decode(new PolicyAction(1 + s104, (int)Maneuver.Approach, 0), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Reason, Is.EqualTo(ReasonCode.TargetAcquired));
            (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(1 + s101, (int)Maneuver.Approach, 0), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Reason, Is.EqualTo(ReasonCode.RetargetVoluntary));
            // current target dies -> switch is forced
            var t = o.Targets[0]; t.Alive = false; o.Targets[0] = t; // 101
            (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(1 + e.SlotOf(104), (int)Maneuver.Charge, 2), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Reason, Is.EqualTo(ReasonCode.RetargetForced));
            // Hold with "none" while the target is usable keeps the target (no silent drop)
            (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(0, (int)Maneuver.Hold, 0), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.TargetId.Value, Is.EqualTo(104UL));
            // drop via Retreat, then pick another target soon after -> still voluntary
            (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(0, (int)Maneuver.Retreat, 1), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.TargetId.Value, Is.EqualTo(0UL));
            o.TimeSeconds += 1.0;
            (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(1 + e.SlotOf(108), (int)Maneuver.Approach, 1), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Reason, Is.EqualTo(ReasonCode.RetargetVoluntary));
        }

        [Test]
        public void Decode_ChargeStrikesOnlyInReach()
        {
            var o = new BrainObservation { TimeSeconds = 1 };
            o.Self = new SelfObs { Id = EntityId.FromValue(1), HpFrac = 1f };
            o.TargetCount = 1; o.Targets[0] = T(101, 30f, 0f);
            var mem = PolicyMemory.Initial(); var (_, _, e) = Enc(o, mem); var it = default(BrainIntent);
            ActionDecoder.Decode(new PolicyAction(1, (int)Maneuver.Charge, 2), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Action, Is.EqualTo(BrainAction.Melee));
            Assert.That(it.SpeedFrac, Is.EqualTo(1f));
            o.Targets[0] = T(101, 300f, 0f); (_, _, e) = Enc(o, mem);
            ActionDecoder.Decode(new PolicyAction(1, (int)Maneuver.Approach, 2), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.Action, Is.EqualTo(BrainAction.None));
            Assert.That(it.SpeedFrac, Is.EqualTo(1f));
            o.Targets[0] = T(101, 200f, 0f); (_, _, e) = Enc(o, mem); // inside standoff: approach holds
            ActionDecoder.Decode(new PolicyAction(1, (int)Maneuver.Approach, 2), o, Cfg, PCfg, e, ref mem, ref it);
            Assert.That(it.SpeedFrac, Is.EqualTo(0f));
        }

        [Test]
        public void Brain_DeterministicForSeed_AndAllocationFree()
        {
            ulong[] Run(ulong seed, out long alloc)
            {
                var b = new PolicyKaijuBrain(new MaskedRandomPolicyNetwork(), PCfg);
                var cfg = Cfg; b.Reset(seed, cfg);
                var o = Full(); var it = default(BrainIntent);
                var outv = new ulong[400];
                for (int i = 0; i < 50; i++) { o.Step = i; b.Decide(o, ref it); } // warm-up (JIT)
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 400; i++)
                {
                    o.Step = 50 + i; o.TimeSeconds = o.Step * 0.02;
                    b.Decide(o, ref it);
                    outv[i] = it.TargetId.Value * 1000UL + (ulong)it.State * 10UL + (ulong)BitConverter.SingleToInt32Bits(it.SpeedFrac) % 7UL;
                }
                alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                return outv;
            }
            var r1 = Run(17, out long al); var r2 = Run(17, out _); var r3 = Run(18, out _);
            Assert.That(r2, Is.EqualTo(r1));
            Assert.That(r3, Is.Not.EqualTo(r1));
            Assert.That(al, Is.EqualTo(0L));
        }

        [Test]
        public void RandomNetwork_RespectsConditionalMask()
        {
            var net = new MaskedRandomPolicyNetwork(); net.Reset(5);
            var o = Full(); var (x, m, _) = Enc(o, PolicyMemory.Initial());
            for (int i = 0; i < 2000; i++)
            {
                var a = default(PolicyAction); net.Act(x, m, ref a);
                Assert.That(m[a.Target], Is.EqualTo(1));
                Assert.That(PolicyLayout.ManeuverAllowed(a.Target, (Maneuver)a.Maneuver, m), Is.True);
            }
        }
    }
}
