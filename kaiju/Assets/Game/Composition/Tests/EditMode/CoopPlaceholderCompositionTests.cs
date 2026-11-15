using System.Collections.Generic;
using System.IO;
using Game.Composition;
using Game.Control.Contracts;
using Game.Core;
using Game.Net;
using Game.Net.ControlBridge;
using Game.Net.ControlBridge.Unity;
using Game.Units.Placeholder;
using Game.Units.Placeholder.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = Game.Core.EntityId;

namespace Game.Composition.Tests
{
    /// <summary>T-030c: the net/seat and placeholder installers. Flags off = nothing registered; both net flags needed; seat drives the body.</summary>
    public class CoopPlaceholderCompositionTests
    {
        private sealed class Errors : ICoreErrorSink
        {
            public readonly List<CoreError> List = new List<CoreError>();
            public void Report(CoreError e) => List.Add(e);
        }

        private sealed class FirstValid : IIntentArbiter
        {
            public IntentFrame Merge(IReadOnlyList<PilotInput> inputs)
            {
                for (int i = 0; i < inputs.Count; i++)
                    if (inputs[i].valid) return new IntentFrame { move = inputs[i].move, target = inputs[i].target, effort = inputs[i].effort, pilots = 1 };
                return default(IntentFrame);
            }
        }

        private sealed class FuncPilot : IPilotSource { public PilotInput Value; public PilotInput Read() => Value; }

        private static FeatureFlagSnapshot Flags(bool coop, bool seat, bool body = false) => new FeatureFlagSnapshot(new List<KeyValuePair<string, bool>>
        {
            new KeyValuePair<string, bool>("net.coop", coop),
            new KeyValuePair<string, bool>("net.seatBridge", seat),
            new KeyValuePair<string, bool>("units.placeholderBody", body),
        });

        private static NetCoopInputs HostInputs() => new NetCoopInputs { Role = NetCoopRole.Host, SnapshotSource = s => new StateSnapshot { HostStep = s } };

        [Test]
        public void NetNeedsBothFlags()
        {
            foreach (var f in new[] { Flags(false, false), Flags(true, false), Flags(false, true) })
            {
                var reg = new ServiceRegistry();
                var r = GameComposition.Install(reg, f, new Errors(), new GameCompositionInputs { Net = HostInputs() });
                Assert.That(r.Net, Is.Null); Assert.That(reg.IsRegistered<NetCoopHandle>(), Is.False);
            }
        }

        [Test]
        public void HostInstall_RegistersSessionAndPump_AndSeatDrivesPlaceholderBody()
        {
            var reg = new ServiceRegistry();
            var flags = Flags(true, true, true);
            var r = GameComposition.Install(reg, flags, new Errors(), new GameCompositionInputs { Net = HostInputs() });
            Assert.That(r.Net, Is.Not.Null); Assert.That(r.Net.HostSession, Is.Not.Null); Assert.That(r.Net.HostPump, Is.Not.Null);
            Assert.That(reg.Resolve<NetCoopHandle>(), Is.SameAs(r.Net));
            Assert.That(r.Placeholder, Is.Not.Null); Assert.That(reg.Resolve<PlaceholderBodyFactory>(), Is.SameAs(r.Placeholder));

            var go = new GameObject("mech");
            try
            {
                var local = new FuncPilot { Value = new PilotInput { move = MoveId.Walk, target = new System.Numerics.Vector3(0, 0, 500), effort = 1f, valid = true } };
                var seat = r.Net.AttachHostSeat(go, 0, new FirstValid(), local);
                var body = r.Placeholder.Create(go, EntityId.FromValue(1), seat);
                for (int i = 0; i < 100; i++) { seat.Tick(i * 0.02); body.Tick(0.02f); }
                Assert.That(body.Body.Speed, Is.GreaterThan(0.5f));
                Assert.That(go.transform.position.z, Is.GreaterThan(0.1f));
                Assert.That(body.TryGetTarget(out var p, out float h), Is.True); Assert.That(h, Is.GreaterThan(0f));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void NetOn_WithoutInputs_IsReportedNotInstalled()
        {
            var reg = new ServiceRegistry(); var errors = new Errors();
            var r = GameComposition.Install(reg, Flags(true, true), errors, new GameCompositionInputs());
            Assert.That(r.Net, Is.Null); Assert.That(errors.List.Count, Is.EqualTo(1));
            r = GameComposition.Install(new ServiceRegistry(), Flags(true, true), errors, new GameCompositionInputs { Net = new NetCoopInputs { Role = NetCoopRole.Host } });
            Assert.That(r.Net, Is.Null); Assert.That(errors.List.Count, Is.EqualTo(2), "host without a snapshot source");
        }

        [Test]
        public void ClientInstall_JoinsOverTheSuppliedLink()
        {
            LoopbackTransport.CreatePair(out var hostEnd, out var clientEnd);
            var reg = new ServiceRegistry();
            var flags = Flags(true, true);
            var r = GameComposition.Install(reg, flags, new Errors(), new GameCompositionInputs
            { Net = new NetCoopInputs { Role = NetCoopRole.Client, HostLink = () => clientEnd, LocalPilot = new FuncPilot() } });
            Assert.That(r.Net, Is.Not.Null); Assert.That(r.Net.ClientLink, Is.Not.Null);
            Assert.That(r.Net.Client.State, Is.EqualTo(ClientState.Joining));
            // a host with the same flags accepts it
            var hostReg = new ServiceRegistry();
            var h = NetCoopInstaller.Install(hostReg, flags, new Errors(), HostInputs());
            h.HostSession.AddConnection(hostEnd);
            h.HostSession.Poll(1);
            r.Net.Client.Poll(2);
            Assert.That(r.Net.Client.State, Is.EqualTo(ClientState.Joined));
            Assert.That(r.Net.Client.Slot.Index, Is.EqualTo(1));
        }

        [Test]
        public void PlaceholderFlagOff_NothingRegistered()
        {
            var reg = new ServiceRegistry();
            var r = GameComposition.Install(reg, Flags(false, false, false), new Errors(), new GameCompositionInputs());
            Assert.That(r.Placeholder, Is.Null); Assert.That(reg.IsRegistered<PlaceholderBodyFactory>(), Is.False);
        }

        [Test]
        public void CommittedFlagFiles_RegisterTheNewFlagsOff()
        {
            var errors = new Errors();
            var flags = FeatureFlagLoader.LoadFolder(Path.Combine(Application.dataPath, "Game/Config/FeatureFlags"), errors);
            Assert.That(errors.List, Is.Empty);
            foreach (var n in new[] { "net.coop", "net.seatBridge", "units.placeholderBody" })
            {
                bool found = false;
                for (int i = 0; i < flags.Count; i++) if (flags.NameAt(i) == n) { found = true; Assert.That(flags.ValueAt(i), Is.False, n); }
                Assert.That(found, Is.True, n + " registered");
            }
        }
    }
}
