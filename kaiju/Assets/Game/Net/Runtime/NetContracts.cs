using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Net
{
    /// <summary>The contract map a peer sends in Hello: Core's contracts plus Net's, sorted ordinally by name.</summary>
    public static class NetContracts
    {
        public static ContractVersion[] Local()
        {
            var l = new List<ContractVersion>
            {
                new ContractVersion("Core.EventBus", CoreContractVersions.EventBus),
                new ContractVersion("Core.ServiceRegistry", CoreContractVersions.ServiceRegistry),
                new ContractVersion("Core.Ids", CoreContractVersions.Ids),
                new ContractVersion("Core.FeatureFlags", CoreContractVersions.FeatureFlags),
                new ContractVersion("Core.Clock", CoreContractVersions.Clock),
                new ContractVersion("Core.SaveSchema", CoreContractVersions.SaveSchema),
                new ContractVersion("Core.CoreSettingsSchema", CoreContractVersions.CoreSettingsSchema),
                new ContractVersion("Core.FeatureFlagsFileSchema", CoreContractVersions.FeatureFlagsFileSchema),
                new ContractVersion("Net.Transport", NetContractVersions.Transport),
                new ContractVersion("Net.SessionHandshake", NetContractVersions.SessionHandshake),
                new ContractVersion("Net.IntentPacket", NetContractVersions.IntentPacket),
                new ContractVersion("Net.StateSnapshot", NetContractVersions.StateSnapshot),
            };
            l.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return l.ToArray();
        }

        public static byte[] HashFlags(string canonical)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical ?? ""));
        }

        /// <summary>Builds the Hello a client sends for its own flags, step length and build.</summary>
        public static HandshakeHello BuildHello(FeatureFlagSnapshot flags, double fixedStepSeconds, string buildVersion)
        {
            string canon = flags.ToCanonicalString();
            return new HandshakeHello
            {
                Protocol = NetContractVersions.Protocol, Contracts = Local(), FlagsCanonical = canon,
                FlagsHash = HashFlags(canon), FixedStepSeconds = fixedStepSeconds, BuildVersion = buildVersion,
            };
        }
    }
}
