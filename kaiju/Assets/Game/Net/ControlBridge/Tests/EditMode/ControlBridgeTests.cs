using System;
using System.Collections.Generic;
using System.Numerics;
using Game.Control.Contracts;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.ControlBridge.Tests
{
    public class ControlBridgeTests
    {
        private const int TicksPerStep = 20; // 1 ms link ticks, Core step 0.02 s
        private const double Step = 0.02;

        private sealed class FirstValidArbiter : IIntentArbiter
        {
            public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
            {
                for (int i = 0; i < inputs.Count; i++)
                    if (inputs[i].valid)
                        return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
                return default(IntentFrame);
            }
        }

        private sealed class ScriptedPilot : IPilotSource
        {
            public Func<PilotInput> Next; public PilotInput Last;
            public PilotInput Read() { Last = Next(); return Last; }
        }

        private static PilotInput Script(long n) => new PilotInput
        {
            move = (MoveId)(1 + (int)(n % 5)),
            target = new Vector3(n * 0.37f - 5f, 0f, -n * 1.25f),
            effort = (n % 11) / 10f,
            valid = true,
        };

        // ---- codec -----------------------------------------------------------------------------------------

        [Test]
        public void Codec_RoundTrip_Randomised_TargetExactEffortWithinBound()
        {
            var rng = new Random(12345);
            for (int i = 0; i < 20000; i++)
            {
                var p = new PilotInput
                {
                    move = (MoveId)rng.Next(0, 6),
                    target = new Vector3(F(rng), F(rng), F(rng)),
                    effort = (float)(rng.NextDouble() * 4 - 1),
                    valid = rng.Next(0, 8) != 0,
                };
                var d = IntentCodec.Decode(IntentCodec.Encode(p));
                Assert.AreEqual(p.valid, d.valid);
                if (!p.valid) { Assert.AreEqual(default(PilotInput), d); continue; }
                Assert.AreEqual(p.move, d.move);
                Assert.AreEqual(BitConverter.SingleToInt32Bits(p.target.X), BitConverter.SingleToInt32Bits(d.target.X));
                Assert.AreEqual(BitConverter.SingleToInt32Bits(p.target.Y), BitConverter.SingleToInt32Bits(d.target.Y));
                Assert.AreEqual(BitConverter.SingleToInt32Bits(p.target.Z), BitConverter.SingleToInt32Bits(d.target.Z));
                Assert.LessOrEqual(Math.Abs(p.effort - d.effort), IntentCodec.EffortMaxError);
            }
        }

        private static float F(Random r) => (float)((r.NextDouble() - 0.5) * 2000.0);

        [Test]
        public void Codec_EdgeCases()
        {
            Assert.AreEqual(default(IntentPayload).Lo, IntentCodec.Encode(default(PilotInput)).Lo);
            Assert.AreEqual(0UL, IntentCodec.Encode(new PilotInput { move = MoveId.Walk, valid = false }).Hi, "invalid = neutral payload");
            Assert.IsFalse(IntentCodec.Decode(default(IntentPayload)).valid);
            var d = IntentCodec.Decode(IntentCodec.Encode(new PilotInput { move = MoveId.Stomp, valid = true, effort = 7f, target = new Vector3(float.NaN, float.PositiveInfinity, -0f) }));
            Assert.AreEqual(3f, d.effort, IntentCodec.EffortMaxError, "saturates but stays above 1");
            Assert.IsTrue(float.IsNaN(d.target.X)); Assert.IsTrue(float.IsPositiveInfinity(d.target.Y));
            Assert.AreEqual(0f, IntentCodec.Decode(IntentCodec.Encode(new PilotInput { valid = true, effort = float.NaN })).effort, IntentCodec.EffortMaxError);
            Assert.AreEqual(0f, IntentCodec.Decode(IntentCodec.Encode(new PilotInput { valid = true, effort = 0f })).effort, IntentCodec.EffortMaxError);
        }

        // ---- two-peer loopback -----------------------------------------------------------------------------

        private sealed class Rig
        {
            public NetHostSession Host; public NetClientSession Client;
            public SimulatedLinkTransport HostSide, ClientSide;
            public RemotePilotSource Remote; public PilotSeat Seat; public IntentSinkFanout Fanout;
            public ScriptedPilot ClientPilot; public LocalSeatIntentPublisher Publisher;
            public long Tick;

            public Rig(int rttMs)
            {
                var flags = new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", true) });
                Host = new NetHostSession(new HandshakeValidator(flags, Step, "1.0", 4), s => new StateSnapshot { HostStep = s, Entities = new EntityState[0] }, new NetLog());
                LoopbackTransport.CreatePair(out var h, out var c);
                var cond = new LinkConditions { DelaySteps = rttMs / 2 };
                HostSide = new SimulatedLinkTransport(h, 1, cond); ClientSide = new SimulatedLinkTransport(c, 2, cond);
                Host.AddConnection(HostSide);
                Client = new NetClientSession(ClientSide, flags, Step, "1.0", new NetLog(), 50);
                Client.Join();
            }

            public void Setup()
            {
                while (Client.State != ClientState.Joined) { Advance(); Assert.Less(Tick, 2000, "join"); }
                Remote = new RemotePilotSource(Client.Slot);
                Fanout = new IntentSinkFanout(); Fanout.Add(Remote);
                Seat = new PilotSeat(new FirstValidArbiter(), Remote);
                ClientPilot = new ScriptedPilot();
                Publisher = new LocalSeatIntentPublisher(ClientPilot, Client);
            }

            private void Advance()
            {
                Tick++;
                HostSide.Advance(Tick); ClientSide.Advance(Tick);
                Host.Poll(Tick / TicksPerStep); Client.Poll(Tick / TicksPerStep);
            }

            /// <summary>One Core step: advance the links, host step, then (optionally) the client publishes.</summary>
            public IntentFrame RunStep(bool clientSends, long clientStep)
            {
                for (int i = 0; i < TicksPerStep; i++) Advance();
                Host.Step(Tick / TicksPerStep, Fanout);
                var frame = Seat.Read();
                if (clientSends) Assert.IsTrue(Publisher.Tick(clientStep));
                return frame;
            }
        }

        private static void AssertSame(PilotInput p, IntentFrame f)
        {
            Assert.AreEqual(p.move, f.move);
            Assert.AreEqual(p.target, f.target);
            Assert.AreEqual(p.effort, f.effort, IntentCodec.EffortMaxError);
        }

        [TestCase(0, 2)]
        [TestCase(100, 5)]
        public void Loopback_ClientScript_ArrivesInSeat_InOrder_NoSkips(int rttMs, int maxLagSteps)
        {
            var rig = new Rig(rttMs); rig.Setup();
            long n = 0; rig.ClientPilot.Next = () => Script(n);
            var outputs = new List<IntentFrame>(); var sources = new List<IntentSource>();
            for (int k = 0; k < 120; k++)
            {
                outputs.Add(rig.RunStep(true, n));
                sources.Add(rig.Remote.LastSource);
                n++;
            }
            // first fresh output identifies the lag; from then on output(k) must equal script(k - lag) exactly
            int first = sources.IndexOf(IntentSource.Fresh);
            Assert.GreaterOrEqual(first, 0);
            int lag = first; // output index first shows script(0)
            Assert.LessOrEqual(lag, maxLagSteps, "one-way delay + one step");
            for (int k = first; k < outputs.Count; k++)
            {
                Assert.AreEqual(IntentSource.Fresh, sources[k], "step " + k);
                AssertSame(Script(k - first), outputs[k]);
            }
            Assert.AreEqual(outputs.Count - first, rig.Remote.Fresh);
            Assert.AreEqual(0, rig.Remote.Held);
        }

        [Test]
        public void Loopback_Dropout_HoldsThenNeutral()
        {
            var rig = new Rig(0); rig.Setup();
            long n = 0; rig.ClientPilot.Next = () => Script(n);
            for (; n < 20; n++) rig.RunStep(true, n);
            // client goes silent; the last fresh intent is the newest one that reached the host
            IntentFrame lastFresh = default; int heldSeen = 0; bool neutralSeen = false; int stepsSilent = 0;
            // finish draining: run until the first non-fresh step
            while (true)
            {
                var f = rig.RunStep(false, n);
                if (rig.Remote.LastSource == IntentSource.Fresh) { lastFresh = f; continue; }
                Assert.AreEqual(IntentSource.Held, rig.Remote.LastSource);
                AssertSame(Script(19), lastFresh);
                heldSeen = 1; AssertSame(Script(19), f);
                break;
            }
            for (stepsSilent = 1; stepsSilent < 30; stepsSilent++)
            {
                var f = rig.RunStep(false, n);
                if (rig.Remote.LastSource == IntentSource.Held) { heldSeen++; AssertSame(Script(19), f); Assert.IsFalse(neutralSeen); }
                else
                {
                    Assert.AreEqual(IntentSource.Neutral, rig.Remote.LastSource);
                    neutralSeen = true;
                    Assert.IsFalse(rig.Remote.Read().valid);
                    Assert.AreEqual(0, f.pilots, "arbiter sees no valid pilot");
                }
            }
            Assert.IsTrue(neutralSeen);
            Assert.AreEqual(IntentHostBuffer.HoldSteps, heldSeen, "hold limit is IntentHostBuffer.HoldSteps");
            Assert.AreEqual(heldSeen, rig.Remote.Held);
            Assert.Greater(rig.Remote.Neutral, 0);
            // resume: fresh again
            n = 100; var back = rig.RunStep(true, n); back = rig.RunStep(true, n + 1);
            Assert.AreEqual(IntentSource.Fresh, rig.Remote.LastSource);
        }

        [Test]
        public void RemotePilotSource_NeutralAsStop_AndSlotFilter()
        {
            var a = PlayerSlot.FromIndex(1); var b = PlayerSlot.FromIndex(2);
            var src = new RemotePilotSource(a) { NeutralAsStop = true };
            src.ApplyIntent(b, IntentCodec.Encode(Script(3)), IntentSource.Fresh);
            Assert.AreEqual(0, src.Fresh);
            src.ApplyIntent(a, default(IntentPayload), IntentSource.Neutral);
            var r = src.Read();
            Assert.IsTrue(r.valid); Assert.AreEqual(MoveId.Stop, r.move); Assert.AreEqual(1, src.Neutral);
        }
    }
}
