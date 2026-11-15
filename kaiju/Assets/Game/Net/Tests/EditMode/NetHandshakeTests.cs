using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    public class NetHandshakeTests
    {
        private const double Step = 0.02;

        private static FeatureFlagSnapshot Flags(bool coop = true, params string[] extraOn)
        {
            var l = new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", coop) };
            foreach (var n in extraOn) l.Add(new KeyValuePair<string, bool>(n, true));
            return new FeatureFlagSnapshot(l);
        }

        private static HandshakeValidator Host(FeatureFlagSnapshot f = null, int max = 4, string build = "1.0") =>
            new HandshakeValidator(f ?? Flags(), Step, build, max);

        private static HandshakeHello Client(FeatureFlagSnapshot f = null, double step = Step, string build = "1.0") =>
            NetContracts.BuildHello(f ?? Flags(), step, build);

        [Test]
        public void Accept_WhenEverythingMatches()
        {
            var snap = new StateSnapshot { HostStep = 7, Entities = new[] { new EntityState(EntityId.FromValue(3), 1, 2, 3, 0, 0, 0, 0.5f, 0) } };
            var r = Host().Evaluate(Client(), occupiedSlots: 1, hostStep: 7, firstSnapshot: snap);
            Assert.IsTrue(r.Accepted);
            Assert.AreEqual(1, r.Accept.Slot.Index);
            Assert.AreEqual(7, r.Accept.HostStep);
            Assert.AreEqual(Step, r.Accept.FixedStepSeconds);
            Assert.AreEqual(0, r.Accept.Warnings.Length);
            Assert.AreSame(snap, r.Accept.FirstSnapshot);
        }

        [Test]
        public void Accept_BuildVersionDifferenceIsOnlyAWarning()
        {
            var r = Host().Evaluate(Client(build: "2.0"), 1, 0, null);
            Assert.IsTrue(r.Accepted);
            Assert.AreEqual(1, r.Accept.Warnings.Length);
            StringAssert.Contains("buildVersion", r.Accept.Warnings[0]);
        }

        [Test]
        public void Refuse_Protocol()
        {
            var h = Client(); h.Protocol = 2;
            var r = Host().Evaluate(h, 1, 0, null);
            Assert.IsFalse(r.Accepted);
            Assert.AreEqual(RefuseCode.ProtocolMismatch, r.Refuse.Code);
            CollectionAssert.AreEqual(new[] { "protocol host=1 client=2" }, r.Refuse.Details);
        }

        [Test]
        public void Refuse_ContractVersion_ListsTheName()
        {
            var h = Client();
            var c = h.Contracts.ToArray();
            int i = System.Array.FindIndex(c, x => x.Name == "Net.IntentPacket");
            c[i] = new ContractVersion("Net.IntentPacket", 2);
            h.Contracts = c;
            var r = Host().Evaluate(h, 1, 0, null);
            Assert.AreEqual(RefuseCode.ContractMismatch, r.Refuse.Code);
            CollectionAssert.AreEqual(new[] { "contract:Net.IntentPacket host=1 client=2" }, r.Refuse.Details);
        }

        [Test]
        public void Refuse_ContractMissingOnClient()
        {
            var h = Client();
            h.Contracts = h.Contracts.Where(x => x.Name != "Core.Clock").ToArray();
            var r = Host().Evaluate(h, 1, 0, null);
            Assert.AreEqual(RefuseCode.ContractMismatch, r.Refuse.Code);
            CollectionAssert.AreEqual(new[] { "contract:Core.Clock host=1 client=absent" }, r.Refuse.Details);
        }

        [Test]
        public void Refuse_FlagCanonicalString_ListsTheFlagNames()
        {
            var r = Host().Evaluate(Client(Flags(true, "debug.x")), 1, 0, null);
            Assert.AreEqual(RefuseCode.FlagsMismatch, r.Refuse.Code);
            CollectionAssert.AreEqual(new[] { "flag:debug.x host=absent client=1" }, r.Refuse.Details);
        }

        [Test]
        public void Refuse_StepLength()
        {
            var r = Host().Evaluate(Client(step: 1.0 / 60.0), 1, 0, null);
            Assert.AreEqual(RefuseCode.StepLengthMismatch, r.Refuse.Code);
            Assert.AreEqual(1, r.Refuse.Details.Length);
            StringAssert.StartsWith("fixedStepSeconds host=0.02 client=", r.Refuse.Details[0]);
        }

        [Test]
        public void Refuse_StepLength_NearlyEqualIsStillADifference()
        {
            var r = Host().Evaluate(Client(step: System.BitConverter.Int64BitsToDouble(System.BitConverter.DoubleToInt64Bits(Step) + 1)), 1, 0, null);
            Assert.AreEqual(RefuseCode.StepLengthMismatch, r.Refuse.Code);
        }

        [Test]
        public void Refuse_CoopOffOnBoth()
        {
            var r = Host(Flags(false)).Evaluate(Client(Flags(false)), 1, 0, null);
            Assert.AreEqual(RefuseCode.CoopDisabled, r.Refuse.Code);
            Assert.AreEqual(2, r.Refuse.Details.Length);
        }

        [Test]
        public void Refuse_CoopOffOnClientOnly_AlsoFlagsMismatch()
        {
            var r = Host().Evaluate(Client(Flags(false)), 1, 0, null);
            Assert.AreEqual(RefuseCode.CoopDisabled | RefuseCode.FlagsMismatch, r.Refuse.Code);
            CollectionAssert.Contains(r.Refuse.Details, "flag:net.coop host=1 client=0");
            CollectionAssert.Contains(r.Refuse.Details, "net.coop is off on client");
        }

        [Test]
        public void Refuse_ListsEveryDifferenceAtOnce()
        {
            var h = Client(Flags(true, "a.b"), step: 0.05); h.Protocol = 9;
            var r = Host().Evaluate(h, 1, 0, null);
            Assert.AreEqual(RefuseCode.ProtocolMismatch | RefuseCode.FlagsMismatch | RefuseCode.StepLengthMismatch, r.Refuse.Code);
            Assert.AreEqual(3, r.Refuse.Details.Length);
        }

        [Test]
        public void Refuse_SessionFull()
        {
            var r = Host(max: 2).Evaluate(Client(), occupiedSlots: 2, hostStep: 0, firstSnapshot: null);
            Assert.AreEqual(RefuseCode.SessionFull, r.Refuse.Code);
        }

        [Test]
        public void Refuse_HashNotMatchingCanonical_IsMalformed()
        {
            var h = Client(); h.FlagsHash = new byte[32];
            var r = Host().Evaluate(h, 1, 0, null);
            Assert.AreEqual(RefuseCode.Malformed, r.Refuse.Code);
        }

        [Test]
        public void Wire_HandshakeRoundTripOverLoopback()
        {
            LoopbackTransport.CreatePair(out var clientEnd, out var hostEnd);
            var hello = Client(build: "3.1");
            Assert.IsTrue(clientEnd.Send(NetChannel.Reliable, NetCodec.Encode(hello)));

            Assert.IsTrue(hostEnd.TryReceive(out var ch, out var bytes));
            Assert.AreEqual(NetChannel.Reliable, ch);
            Assert.IsTrue(NetCodec.TryDecode(bytes, out HandshakeHello got));
            var snap = new StateSnapshot { HostStep = 12, Entities = new[] { new EntityState(EntityId.FromValue(9), 1, 2, 3, 4, 5, 6, 7, 8) } };
            var res = Host().Evaluate(got, 1, 12, snap);
            Assert.IsTrue(res.Accepted);
            hostEnd.Send(NetChannel.Reliable, NetCodec.Encode(res.Accept));

            Assert.IsTrue(clientEnd.TryReceive(out _, out var back));
            Assert.IsTrue(NetCodec.TryDecode(back, out HandshakeAccept acc));
            Assert.AreEqual(1, acc.Slot.Index);
            Assert.AreEqual(12, acc.FirstSnapshot.HostStep);
            Assert.AreEqual(8f, acc.FirstSnapshot.Entities[0].YawRate);
            Assert.AreEqual(9UL, acc.FirstSnapshot.Entities[0].Id.Value);
            Assert.AreEqual(1, acc.Warnings.Length);
        }

        [Test]
        public void Wire_RefuseRoundTrip_AndGarbageIsRejected()
        {
            var refuse = Host().Evaluate(Client(step: 0.05), 1, 0, null).Refuse;
            Assert.IsTrue(NetCodec.TryDecode(NetCodec.Encode(refuse), out HandshakeRefuse back));
            Assert.AreEqual(refuse.Code, back.Code);
            CollectionAssert.AreEqual(refuse.Details, back.Details);

            Assert.IsFalse(NetCodec.TryDecode(new byte[] { 1, 255, 255, 255, 127 }, out HandshakeHello _));
            Assert.IsFalse(NetCodec.TryDecode(new byte[0], out HandshakeHello _));
            var truncated = NetCodec.Encode(Client()); System.Array.Resize(ref truncated, truncated.Length - 3);
            Assert.IsFalse(NetCodec.TryDecode(truncated, out HandshakeHello _));
        }
    }
}
