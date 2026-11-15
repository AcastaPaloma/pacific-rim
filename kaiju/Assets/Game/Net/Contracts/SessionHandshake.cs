using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>One entry of the contract map exchanged at join: name -> version.</summary>
    public readonly struct ContractVersion : IEquatable<ContractVersion>
    {
        public readonly string Name;
        public readonly int Version;
        public ContractVersion(string name, int version) { Name = name; Version = version; }
        public bool Equals(ContractVersion o) => Version == o.Version && string.Equals(Name, o.Name, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ContractVersion o && Equals(o);
        public override int GetHashCode() => (Name?.Length ?? 0) * 31 + Version;
    }

    /// <summary>Client -> host, reliable channel, before any gameplay data (SessionHandshake@v1).</summary>
    public sealed class HandshakeHello
    {
        public int Protocol;
        /// <summary>Core + Net contract versions. Order does not matter; compared by name.</summary>
        public ContractVersion[] Contracts = Array.Empty<ContractVersion>();
        /// <summary>FeatureFlagSnapshot.ToCanonicalString() of the client.</summary>
        public string FlagsCanonical = "";
        /// <summary>SHA-256 of the UTF-8 bytes of FlagsCanonical (32 bytes).</summary>
        public byte[] FlagsHash = Array.Empty<byte>();
        public double FixedStepSeconds;
        public string BuildVersion = "";
    }

    /// <summary>Bit set of mismatch kinds. A Refuse may carry several.</summary>
    [Flags]
    public enum RefuseCode
    {
        None = 0,
        ProtocolMismatch = 1 << 0,
        ContractMismatch = 1 << 1,
        FlagsMismatch = 1 << 2,
        StepLengthMismatch = 1 << 3,
        CoopDisabled = 1 << 4,
        SessionFull = 1 << 5,
        Malformed = 1 << 6,
    }

    /// <summary>Host -> client. The host closes the connection afterwards.</summary>
    public sealed class HandshakeRefuse
    {
        public RefuseCode Code;
        /// <summary>Human-readable differences, e.g. "contract:IntentPacket host=1 client=2", "flag:net.coop host=1 client=absent".</summary>
        public string[] Details = Array.Empty<string>();
    }

    /// <summary>Host -> client: slot assignment, host clock, step length and the first full snapshot.</summary>
    public sealed class HandshakeAccept
    {
        public PlayerSlot Slot;
        public long HostStep;
        public double FixedStepSeconds;
        public StateSnapshot FirstSnapshot = new StateSnapshot();
        /// <summary>Non-fatal differences (build version).</summary>
        public string[] Warnings = Array.Empty<string>();
    }
}
