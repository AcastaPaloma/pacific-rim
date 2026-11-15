using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;

namespace Game.Net
{
    public sealed class HandshakeResult
    {
        public bool Accepted;
        public HandshakeAccept Accept;   // set when Accepted
        public HandshakeRefuse Refuse;   // set when refused
    }

    /// <summary>
    /// Host-side join check (SessionHandshake@v1). Refuses on any protocol, contract-version, canonical-flag-string,
    /// step-length or net.coop mismatch (all differences are collected, none short-circuits) or when the host is full.
    /// A differing build version alone is only a warning on the Accept. The host diffs; clients never self-diff.
    /// </summary>
    public sealed class HandshakeValidator
    {
        public const string CoopFlag = "net.coop";

        private readonly FeatureFlagSnapshot _flags;
        private readonly string _flagsCanonical;
        private readonly double _stepSeconds;
        private readonly string _buildVersion;
        private readonly int _maxSlots;
        private readonly ContractVersion[] _contracts;
        private readonly int _protocol;

        public HandshakeValidator(FeatureFlagSnapshot hostFlags, double fixedStepSeconds, string buildVersion, int maxSlots = 4)
            : this(hostFlags, fixedStepSeconds, buildVersion, maxSlots, NetContractVersions.Protocol, NetContracts.Local()) { }

        public HandshakeValidator(FeatureFlagSnapshot hostFlags, double fixedStepSeconds, string buildVersion, int maxSlots,
                                  int protocol, ContractVersion[] contracts)
        {
            _flags = hostFlags ?? throw new ArgumentNullException(nameof(hostFlags));
            _flagsCanonical = hostFlags.ToCanonicalString();
            _stepSeconds = fixedStepSeconds; _buildVersion = buildVersion ?? "";
            _maxSlots = maxSlots; _protocol = protocol; _contracts = contracts;
        }

        /// <param name="occupiedSlots">Slots already assigned (the host's own included). The new client gets index <c>occupiedSlots</c>.</param>
        public HandshakeResult Evaluate(HandshakeHello hello, int occupiedSlots, long hostStep, StateSnapshot firstSnapshot)
        {
            var details = new List<string>();
            var code = RefuseCode.None;

            if (hello == null || hello.Contracts == null || hello.FlagsCanonical == null || hello.FlagsHash == null)
                return Refused(RefuseCode.Malformed, new[] { "malformed hello" });

            if (hello.Protocol != _protocol)
            {
                code |= RefuseCode.ProtocolMismatch;
                details.Add("protocol host=" + _protocol.ToString(CultureInfo.InvariantCulture) + " client=" + hello.Protocol.ToString(CultureInfo.InvariantCulture));
            }

            DiffContracts(hello.Contracts, details, ref code);

            if (!HashMatches(hello))
            {
                code |= RefuseCode.Malformed;
                details.Add("flagsHash does not match flagsCanonical");
            }
            if (!string.Equals(hello.FlagsCanonical, _flagsCanonical, StringComparison.Ordinal))
            {
                code |= RefuseCode.FlagsMismatch;
                DiffFlags(hello.FlagsCanonical, details);
            }
            if (BitConverter.DoubleToInt64Bits(hello.FixedStepSeconds) != BitConverter.DoubleToInt64Bits(_stepSeconds))
            {
                code |= RefuseCode.StepLengthMismatch;
                details.Add("fixedStepSeconds host=" + _stepSeconds.ToString("R", CultureInfo.InvariantCulture) + " client=" + hello.FixedStepSeconds.ToString("R", CultureInfo.InvariantCulture));
            }
            // net.coop must be on in both. Reported separately from a generic flag diff.
            if (!_flags.IsOn(CoopFlag)) { code |= RefuseCode.CoopDisabled; details.Add("net.coop is off on host"); }
            if (!CanonicalHasOn(hello.FlagsCanonical, CoopFlag)) { code |= RefuseCode.CoopDisabled; details.Add("net.coop is off on client"); }

            if (occupiedSlots >= _maxSlots)
            {
                code |= RefuseCode.SessionFull;
                details.Add("slots full: " + _maxSlots.ToString(CultureInfo.InvariantCulture));
            }

            if (code != RefuseCode.None) return Refused(code, details.ToArray());

            var warnings = string.Equals(hello.BuildVersion, _buildVersion, StringComparison.Ordinal)
                ? Array.Empty<string>()
                : new[] { "buildVersion host=" + _buildVersion + " client=" + hello.BuildVersion };
            return new HandshakeResult
            {
                Accepted = true,
                Accept = new HandshakeAccept
                {
                    Slot = PlayerSlot.FromIndex(occupiedSlots), HostStep = hostStep, FixedStepSeconds = _stepSeconds,
                    FirstSnapshot = firstSnapshot ?? new StateSnapshot { HostStep = hostStep }, Warnings = warnings,
                },
            };
        }

        private static HandshakeResult Refused(RefuseCode code, string[] details) =>
            new HandshakeResult { Accepted = false, Refuse = new HandshakeRefuse { Code = code, Details = details } };

        private static bool HashMatches(HandshakeHello h)
        {
            var expect = NetContracts.HashFlags(h.FlagsCanonical);
            if (expect.Length != h.FlagsHash.Length) return false;
            for (int i = 0; i < expect.Length; i++) if (expect[i] != h.FlagsHash[i]) return false;
            return true;
        }

        private void DiffContracts(ContractVersion[] client, List<string> details, ref RefuseCode code)
        {
            var host = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var cl = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in _contracts) host[c.Name] = c.Version;
            foreach (var c in client) if (c.Name != null) cl[c.Name] = c.Version;
            var names = new SortedSet<string>(host.Keys, StringComparer.Ordinal);
            names.UnionWith(cl.Keys);
            foreach (var n in names)
            {
                bool h = host.TryGetValue(n, out int hv), c = cl.TryGetValue(n, out int cv);
                if (h && c && hv == cv) continue;
                code |= RefuseCode.ContractMismatch;
                details.Add("contract:" + n + " host=" + (h ? hv.ToString(CultureInfo.InvariantCulture) : "absent") + " client=" + (c ? cv.ToString(CultureInfo.InvariantCulture) : "absent"));
            }
        }

        private void DiffFlags(string clientCanonical, List<string> details)
        {
            int before = details.Count;
            var host = Parse(_flagsCanonical);
            var cl = Parse(clientCanonical);
            var names = new SortedSet<string>(host.Keys, StringComparer.Ordinal);
            names.UnionWith(cl.Keys);
            foreach (var n in names)
            {
                bool h = host.TryGetValue(n, out string hv), c = cl.TryGetValue(n, out string cv);
                if (h && c && hv == cv) continue;
                details.Add("flag:" + n + " host=" + (h ? hv : "absent") + " client=" + (c ? cv : "absent"));
            }
            // Same entries but different text (ordering, whitespace): still a mismatch, say so.
            if (details.Count == before)
                details.Add("flags canonical string differs in form only");
        }

        private static Dictionary<string, string> Parse(string canonical)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in canonical.Split('\n'))
            {
                if (line.Length == 0) continue;
                int eq = line.LastIndexOf('=');
                if (eq <= 0) { d[line] = "?"; continue; }
                d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            return d;
        }

        private static bool CanonicalHasOn(string canonical, string flag) =>
            Parse(canonical).TryGetValue(flag, out var v) && v == "1";
    }
}
