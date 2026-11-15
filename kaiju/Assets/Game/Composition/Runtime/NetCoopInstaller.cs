// T-030c: installs the co-op net session and seat pump behind net.coop AND net.seatBridge. Off by default: the committed config installs nothing.
using System;
using System.Collections.Generic;
using Game.Control.Contracts;
using Game.Core;
using Game.Net;
using Game.Net.ControlBridge;
using Game.Net.ControlBridge.Unity;
using UnityEngine;

namespace Game.Composition
{
    public enum NetCoopRole { None = 0, Host = 1, Client = 2 }

    /// <summary>What the root cannot know itself for the co-op install (role, transports, the host's snapshot source).</summary>
    public sealed class NetCoopInputs
    {
        public NetCoopRole Role;
        public double StepSeconds = 0.02;
        public string BuildVersion = "dev";
        public int MaxSlots = 4;
        /// <summary>Host: bodies as of a host step (the scene's snapshot source). Required for Host.</summary>
        public Func<long, StateSnapshot> SnapshotSource;
        /// <summary>Host: slots freed after this many silent steps (0 = never). 250 = 5 s at 50 Hz.</summary>
        public int SlotTimeoutSteps = 250;
        /// <summary>Client: the transport to the host. Required for Client.</summary>
        public Func<ITransport> HostLink;
        /// <summary>Client: the local pilot published to the host each step. Required for Client.</summary>
        public IPilotSource LocalPilot;
    }

    /// <summary>What the net install created. Registered in the service registry as itself.</summary>
    public sealed class NetCoopHandle
    {
        public NetCoopRole Role;
        public IFeatureFlags Flags;
        public NetLog Log;
        public NetHostSession HostSession;
        public HostSeatPump HostPump;
        public NetClientSession Client;
        public ClientSeatLink ClientLink;

        /// <summary>Host: a mech seat for <paramref name="mechIndex"/>, handed to a MechSeatBehaviour on <paramref name="go"/>. Remote slots are assigned with <see cref="HostPump"/>.Assign.</summary>
        public MechSeatBehaviour AttachHostSeat(GameObject go, int mechIndex, IIntentArbiter arbiter, IPilotSource local = null)
        {
            if (HostPump == null) throw new InvalidOperationException("not a host install");
            var b = go.GetComponent<MechSeatBehaviour>() ?? go.AddComponent<MechSeatBehaviour>();
            b.ConfigureHost(new MechSeat(Flags, mechIndex, arbiter, local), HostPump);
            return b;
        }

        /// <summary>Client: the local seat behaviour; it publishes <paramref name="local"/> through the link.</summary>
        public MechSeatBehaviour AttachClientSeat(GameObject go, int mechIndex, IIntentArbiter arbiter, IPilotSource local)
        {
            if (ClientLink == null) throw new InvalidOperationException("not a client install");
            var b = go.GetComponent<MechSeatBehaviour>() ?? go.AddComponent<MechSeatBehaviour>();
            b.ConfigureClient(new MechSeat(Flags, mechIndex, arbiter, local), ClientLink);
            return b;
        }
    }

    public static class NetCoopInstaller
    {
        public const string CoopFlag = HandshakeValidator.CoopFlag;

        public static bool Wanted(IFeatureFlags flags) => flags != null && flags.IsOn(CoopFlag) && flags.IsOn(SeatBridgeFlags.Seat);

        public static FeatureFlagSnapshot Snapshot(IFeatureFlags flags)
        {
            var l = new List<KeyValuePair<string, bool>>(flags.Count);
            for (int i = 0; i < flags.Count; i++) l.Add(new KeyValuePair<string, bool>(flags.NameAt(i), flags.ValueAt(i)));
            return new FeatureFlagSnapshot(l);
        }

        /// <summary>Null when either flag is off (nothing registered) or the inputs are missing (reported). Otherwise registers a <see cref="NetCoopHandle"/>.</summary>
        public static NetCoopHandle Install(IServiceRegistry registry, IFeatureFlags flags, ICoreErrorSink errors, NetCoopInputs inputs)
        {
            if (registry == null || !Wanted(flags)) return null;
            if (inputs == null || inputs.Role == NetCoopRole.None)
            { errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Composition] net.coop and net.seatBridge are on but no NetCoopInputs.Role was supplied; net not installed.")); return null; }

            var snap = Snapshot(flags);
            var h = new NetCoopHandle { Role = inputs.Role, Flags = flags, Log = new NetLog() };
            int sps = (int)Math.Round(1.0 / inputs.StepSeconds);
            if (inputs.Role == NetCoopRole.Host)
            {
                if (inputs.SnapshotSource == null)
                { errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Composition] net host install needs NetCoopInputs.SnapshotSource; net not installed.")); return null; }
                var validator = new HandshakeValidator(snap, inputs.StepSeconds, inputs.BuildVersion, inputs.MaxSlots);
                h.HostSession = new NetHostSession(validator, inputs.SnapshotSource, h.Log, 2, sps) { SlotTimeoutSteps = inputs.SlotTimeoutSteps };
                h.HostPump = new HostSeatPump(h.HostSession);
            }
            else
            {
                if (inputs.HostLink == null || inputs.LocalPilot == null)
                { errors?.Report(new CoreError(CoreErrorKind.ConfigMissing, "[Composition] net client install needs NetCoopInputs.HostLink and LocalPilot; net not installed.")); return null; }
                h.Client = new NetClientSession(inputs.HostLink(), snap, inputs.StepSeconds, inputs.BuildVersion, h.Log, sps);
                h.ClientLink = new ClientSeatLink(flags, h.Client, inputs.LocalPilot);
                h.Client.Join();
            }
            registry.Register<NetCoopHandle>(h);
            return h;
        }
    }
}
