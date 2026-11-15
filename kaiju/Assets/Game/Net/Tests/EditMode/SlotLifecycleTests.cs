using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Net.Tests
{
    /// <summary>
    /// T-030c: slot release (leave, timeout, link down, kick), rejoin into a freed slot with the handshake re-checked,
    /// the M-057 join-burst cell, the M-055/M-056 stress and outage cell, and the host-egress reading of the N=2 vs N=4 clause.
    /// Steps are Core steps (0.02 s); no real sleeping.
    /// </summary>
    public class SlotLifecycleTests
    {
        private const double StepS = 0.02;

        private static FeatureFlagSnapshot Flags(bool coop = true, params string[] extraOn)
        {
            var l = new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("net.coop", coop) };
            foreach (var n in extraOn) l.Add(new KeyValuePair<string, bool>(n, true));
            return new FeatureFlagSnapshot(l);
        }

        private sealed class Sink : IIntentSink
        {
            public readonly Dictionary<int, List<(ulong, IntentSource)>> Got = new Dictionary<int, List<(ulong, IntentSource)>>();
            public void ApplyIntent(PlayerSlot slot, in IntentPayload payload, IntentSource source)
            {
                if (!Got.TryGetValue(slot.Index, out var l)) Got[slot.Index] = l = new List<(ulong, IntentSource)>();
                l.Add((payload.Lo, source));
            }
            public int Fresh(int slot) => Got.TryGetValue(slot, out var l) ? l.Count(x => x.Item2 == IntentSource.Fresh) : 0;
        }

        private static StateSnapshot Bodies(long step, int n)
        {
            var e = new EntityState[n];
            for (int i = 0; i < n; i++) e[i] = new EntityState(EntityId.FromValue((ulong)(i + 1)), i + step * 0.01f, 0, 0, 0.5f, 0, 0, 0, 0);
            return new StateSnapshot { HostStep = step, Entities = e };
        }

        private sealed class Rig
        {
            public NetLog HostLog = new NetLog();
            public NetHostSession Host;
            public Sink Sink = new Sink();
            public long Step;
            public readonly List<NetClientSession> Clients = new List<NetClientSession>();
            public Rig(int maxSlots = 4, int bodies = 3, FeatureFlagSnapshot flags = null, int timeout = 0, ContractVersion[] contracts = null)
            {
                var v = contracts == null ? new HandshakeValidator(flags ?? Flags(), StepS, "1.0", maxSlots)
                                          : new HandshakeValidator(flags ?? Flags(), StepS, "1.0", maxSlots, NetContractVersions.Protocol, contracts);
                Host = new NetHostSession(v, s => Bodies(s, bodies), HostLog) { SlotTimeoutSteps = timeout };
            }

            public NetClientSession Connect(FeatureFlagSnapshot flags = null, LoopbackTransport[] ends = null)
            {
                LoopbackTransport.CreatePair(out var h, out var c);
                Host.AddConnection(h);
                var cl = new NetClientSession(c, flags ?? Flags(), StepS, "1.0", new NetLog(), 50);
                Clients.Add(cl); cl.Join();
                return cl;
            }

            /// <summary>One Core step. Clients in <paramref name="silent"/> neither poll nor send (a frozen process).</summary>
            public void Tick(HashSet<NetClientSession> silent = null)
            {
                Host.Poll(Step); Host.Step(Step, Sink);
                foreach (var c in Clients)
                {
                    if (silent != null && silent.Contains(c)) continue;
                    c.Poll(Step);
                    if (c.State == ClientState.Joined) { c.SendIntent(Step, new IntentPayload(1, (ulong)(Step + 1))); c.Update(Step); }
                }
                Step++;
            }
            public void Run(int steps, HashSet<NetClientSession> silent = null) { for (int i = 0; i < steps; i++) Tick(silent); }
        }

        [Test]
        public void Leave_FreesSlotAtOnce_AndClientCanRejoinTheSameSlotWithFreshSequence()
        {
            var rig = new Rig();
            var a = rig.Connect(); rig.Run(5);
            Assert.AreEqual(ClientState.Joined, a.State); Assert.AreEqual(1, a.Slot.Index);
            rig.Run(20);
            int freshBefore = rig.Sink.Fresh(1);
            Assert.Greater(freshBefore, 10);
            Assert.AreEqual(2, rig.Host.OccupiedSlots);

            a.Leave(rig.Step); rig.Run(1);
            Assert.AreEqual(1, rig.Host.OccupiedSlots, "slot freed on the next poll");
            Assert.IsFalse(rig.Host.IsOccupied(PlayerSlot.FromIndex(1)));
            var rel = rig.HostLog.Records.Single(r => r.Kind == NetLogKind.SlotRelease);
            Assert.AreEqual(1, rel.Slot); Assert.AreEqual(0, rel.A, "reason: left");

            rig.Clients.Clear();
            var b = rig.Connect(); rig.Run(5);
            Assert.AreEqual(ClientState.Joined, b.State); Assert.AreEqual(1, b.Slot.Index, "rejoined the freed slot");
            rig.Run(20);
            Assert.Greater(rig.Sink.Fresh(1), freshBefore + 15, "the new client's restarted sequence numbers are accepted (filter reset)");
            Assert.AreEqual(2, rig.Host.OccupiedSlots);
        }

        [Test]
        public void Timeout_ReleasesSilentClient_RejoinGetsSlotBack()
        {
            var rig = new Rig(timeout: 50);
            var a = rig.Connect(); rig.Run(10);
            Assert.AreEqual(2, rig.Host.OccupiedSlots);
            var silent = new HashSet<NetClientSession> { a };
            rig.Run(40, silent); Assert.AreEqual(2, rig.Host.OccupiedSlots, "still within the timeout");
            rig.Run(20, silent);
            Assert.AreEqual(1, rig.Host.OccupiedSlots);
            var rel = rig.HostLog.Records.Single(r => r.Kind == NetLogKind.SlotRelease);
            Assert.AreEqual(1, rel.A, "reason: timeout"); Assert.Greater(rel.B, 50);
            Assert.IsFalse(a.Link.IsConnected, "the host closed the dead link");
            a.Poll(rig.Step);
            Assert.AreEqual(ClientState.Lost, a.State);
            Assert.AreEqual(1, a.Log.Count(NetLogKind.LinkLost));

            rig.Clients.Clear();
            var b = rig.Connect(); rig.Run(5);
            Assert.AreEqual(1, b.Slot.Index);
        }

        [Test]
        public void LinkDown_ReleasesSlot()
        {
            var rig = new Rig();
            var a = rig.Connect(); rig.Run(10);
            a.Link.Close(); rig.Run(1);
            Assert.AreEqual(1, rig.Host.OccupiedSlots);
            Assert.AreEqual(2, rig.HostLog.Records.Single(r => r.Kind == NetLogKind.SlotRelease).A, "reason: link down");
        }

        [Test]
        public void HostKick_FreesSlotAndClientSeesLinkLost()
        {
            var rig = new Rig();
            var a = rig.Connect(); rig.Run(10);
            Assert.IsTrue(rig.Host.Release(a.Slot, rig.Step));
            Assert.IsFalse(rig.Host.Release(a.Slot, rig.Step), "second release is a no-op");
            Assert.AreEqual(1, rig.Host.OccupiedSlots);
            rig.Run(1);
            Assert.AreEqual(ClientState.Lost, a.State);
        }

        [Test]
        public void LowestFreeSlotIsReused_AndFullStillRefuses()
        {
            var rig = new Rig(maxSlots: 4);
            var c1 = rig.Connect(); var c2 = rig.Connect(); var c3 = rig.Connect(); rig.Run(5);
            Assert.AreEqual(new[] { 1, 2, 3 }, new[] { c1.Slot.Index, c2.Slot.Index, c3.Slot.Index });
            var c4 = rig.Connect(); rig.Run(3);
            Assert.AreEqual(ClientState.Refused, c4.State); Assert.IsTrue((c4.Refusal.Code & RefuseCode.SessionFull) != 0);
            c2.Leave(rig.Step); rig.Run(2);
            var c5 = rig.Connect(); rig.Run(3);
            Assert.AreEqual(ClientState.Joined, c5.State); Assert.AreEqual(2, c5.Slot.Index, "the gap, not slot 4");
            Assert.AreEqual(4, rig.Host.OccupiedSlots);
        }

        [Test]
        public void Rejoin_StillComparesFlagSnapshotAndContractVersions()
        {
            var rig = new Rig();
            var a = rig.Connect(); rig.Run(5); a.Leave(rig.Step); rig.Run(2);

            var badFlags = rig.Connect(Flags(true, "units.placeholderBody")); rig.Run(3);
            Assert.AreEqual(ClientState.Refused, badFlags.State);
            Assert.IsTrue((badFlags.Refusal.Code & RefuseCode.FlagsMismatch) != 0);
            Assert.AreEqual(1, rig.Host.OccupiedSlots, "a refused rejoin takes no slot");

            var noCoop = rig.Connect(Flags(false)); rig.Run(3);
            Assert.IsTrue((noCoop.Refusal.Code & RefuseCode.CoopDisabled) != 0);

            var oldContracts = NetContracts.Local().Select(c => c.Name == "Net.IntentPacket" ? new ContractVersion(c.Name, c.Version + 1) : c).ToArray();
            var rig2 = new Rig(contracts: oldContracts);
            var cm = rig2.Connect(); rig2.Run(3);
            Assert.IsTrue((cm.Refusal.Code & RefuseCode.ContractMismatch) != 0);
            Assert.That(cm.Refusal.Details.Any(d => d.StartsWith("contract:Net.IntentPacket")));

            var good = rig.Connect(); rig.Run(3);
            Assert.AreEqual(1, good.Slot.Index);
        }

        [Test]
        public void JoinBurst_TenAtOnce_FirstThreeAccepted_RestRefused_RefusedConnectionsReaped()
        {
            var rig = new Rig(maxSlots: 4) { };
            rig.Host.RefusedLingerSteps = 30;
            var burst = new List<NetClientSession>();
            for (int i = 0; i < 10; i++) burst.Add(rig.Connect());
            rig.Run(3);
            Assert.AreEqual(new[] { 1, 2, 3 }, burst.Take(3).Select(c => c.Slot.Index).ToArray(), "connection order decides, deterministically");
            Assert.AreEqual(3, burst.Count(c => c.State == ClientState.Joined));
            Assert.AreEqual(7, burst.Count(c => c.State == ClientState.Refused && (c.Refusal.Code & RefuseCode.SessionFull) != 0));
            Assert.AreEqual(4, rig.Host.OccupiedSlots);
            Assert.AreEqual(3, rig.HostLog.Count(NetLogKind.HandshakeAccept)); Assert.AreEqual(7, rig.HostLog.Count(NetLogKind.HandshakeRefuse));
            rig.Run(40);
            Assert.IsTrue(burst.Skip(3).All(c => !c.Link.IsConnected), "refused links are closed after the linger");
            Assert.AreEqual(4, rig.Host.OccupiedSlots);
            // joined players keep flowing through the burst's aftermath
            Assert.Greater(rig.Sink.Fresh(3), 30);
        }

        [Test]
        public void JoinBurst_LeaveAndRejoinChurn_NeverLeaksSlots()
        {
            var rig = new Rig(maxSlots: 4);
            var live = new List<NetClientSession>();
            for (int round = 0; round < 40; round++)
            {
                while (live.Count < 3) live.Add(rig.Connect());
                rig.Run(4);
                Assert.AreEqual(3, live.Count(c => c.State == ClientState.Joined), "round " + round);
                Assert.AreEqual(new[] { 1, 2, 3 }, live.Select(c => c.Slot.Index).OrderBy(x => x).ToArray());
                var gone = live[round % 3]; gone.Leave(rig.Step); live.Remove(gone); rig.Clients.Remove(gone);
                rig.Run(1);
            }
            Assert.AreEqual(1 + live.Count, rig.Host.OccupiedSlots, "the host's own slot plus the clients still joined");
        }

        // ---- M-055 / M-056 stress cell: RTT 150 ms, 5% loss, forced 400 ms outage, recovery within 1 s ------------------

        [Test]
        public void Stress_Rtt150_Loss5_Outage400ms_RecoversWithinOneSecond()
        {
            var hostLog = new NetLog();
            var host = new NetHostSession(new HandshakeValidator(Flags(), StepS, "1.0", 4), s => Bodies(s, 11), hostLog);
            LoopbackTransport.CreatePair(out var he, out var ce);
            var hs = new SimulatedLinkTransport(he, 21, new LinkConditions { DelaySteps = 3, JitterSteps = 1, UnreliableLoss = 0.05 });
            var cs = new SimulatedLinkTransport(ce, 22, new LinkConditions { DelaySteps = 3, JitterSteps = 1, UnreliableLoss = 0.05 });
            host.AddConnection(hs);
            var clog = new NetLog();
            var client = new NetClientSession(cs, Flags(), StepS, "1.0", clog, 50);
            client.Join();
            var sink = new Sink();
            const int OutageFrom = 500, OutageSteps = 20; // 400 ms
            long recoveredAt = -1;
            for (long s = 0; s < 1200; s++)
            {
                bool outage = s >= OutageFrom && s < OutageFrom + OutageSteps;
                var cond = new LinkConditions { DelaySteps = 3, JitterSteps = 1, UnreliableLoss = outage ? 1.0 : 0.05 };
                hs.Conditions = cond; cs.Conditions = cond;
                hs.Advance(s); cs.Advance(s);
                host.Poll(s); client.Poll(s);
                host.Step(s, sink);
                if (client.State == ClientState.Joined) { client.SendIntent(s, new IntentPayload(1, (ulong)(s + 1))); client.Update(s); }
                if (s > OutageFrom + OutageSteps && recoveredAt < 0 && client.State == ClientState.Joined && !client.Interpolator.IsStale && client.LastHostStepSeen > OutageFrom + OutageSteps) recoveredAt = s;
            }
            var stale = clog.Records.Where(r => r.Kind == NetLogKind.SnapshotStale).ToArray();
            Assert.GreaterOrEqual(stale.Length, 2, "stale on then off");
            Assert.AreEqual(1, stale[0].A); Assert.AreEqual(0, stale[1].A);
            long recoverySteps = recoveredAt - (OutageFrom + OutageSteps);
            TestContext.Progress.WriteLine("Stress: recovery {0} steps ({1} ms) after a 400 ms outage; stale on at step {2}, off at {3}; host hold={4} neutral={5}",
                recoverySteps, recoverySteps * 20, stale[0].Step, stale[1].Step, hostLog.Count(NetLogKind.IntentHold), hostLog.Count(NetLogKind.IntentNeutral));
            Assert.That(recoveredAt, Is.GreaterThan(0));
            Assert.LessOrEqual(recoverySteps, 50, "back within 1 s of the link returning");
            Assert.Greater(hostLog.Count(NetLogKind.IntentHold), 0);
            Assert.AreEqual(ClientState.Joined, client.State);
            Assert.AreEqual(2, host.OccupiedSlots, "a 400 ms outage costs no slot");
        }

        // ---- M-057 host egress reading: N=2 (1 client) <= 0.8 x N=4 (3 clients) -------------------------------------------

        private static double HostEgress(int clients, int bodies)
        {
            var rig = new Rig(bodies: bodies);
            for (int i = 0; i < clients; i++) rig.Connect();
            rig.Run(50 * 30);
            // Net.Log Bandwidth is per connection and per second: A = bytes down; add 60 B assumed overhead per packet (C).
            var recs = rig.HostLog.Records.Where(r => r.Kind == NetLogKind.Bandwidth && r.Step >= 50 * 5).ToArray();
            double total = recs.Sum(r => (double)(r.A + 60 * r.C));
            double seconds = recs.Select(r => r.Step / 50).Distinct().Count();
            return total / seconds;
        }

        [Test]
        public void M057_HostEgress_TwoSlotsAtMostEightyPercentOfFour()
        {
            double n2 = HostEgress(1, 11), n4 = HostEgress(3, 13);
            TestContext.Progress.WriteLine(string.Format(CultureInfo.InvariantCulture, "M-057 host egress: N=2 {0:F0} B/s, N=4 {1:F0} B/s, ratio {2:F3}", n2, n4, n2 / n4));
            Assert.LessOrEqual(n2, 0.8 * n4);
        }
    }
}
