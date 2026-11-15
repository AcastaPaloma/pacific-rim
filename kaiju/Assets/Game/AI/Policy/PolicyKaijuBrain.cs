using System;

namespace Game.AI.Policy
{
    /// <summary>
    /// Policy inference seam (R-028 section 9). Implementations: <see cref="MaskedRandomPolicyNetwork"/> (seeded baseline / tests),
    /// the training bridge's external network (Tools/Training/bridge), and later the exported MLP evaluator.
    /// Must be deterministic for a seed and must not allocate in <see cref="Act"/>.
    /// </summary>
    public interface IPolicyNetwork
    {
        string NetworkId { get; }
        void Reset(ulong seed);
        /// <summary>Choose an action from the encoded observation and base mask (respecting <see cref="PolicyLayout.ManeuverAllowed"/>).</summary>
        void Act(float[] obs, byte[] mask, ref PolicyAction action);
    }

    /// <summary>
    /// RL tactical brain behind <see cref="IKaijuBrain"/> (F-021): encode -> network -> decode. Same cadence as the scripted brain
    /// (decides every BrainPeriodSteps at phase EntityId mod period, holds the last intent otherwise). No allocation in Decide.
    /// </summary>
    public sealed class PolicyKaijuBrain : IKaijuBrain
    {
        private readonly IPolicyNetwork _net;
        private readonly ObservationEncoder _enc = new ObservationEncoder();
        private readonly float[] _x = new float[PolicyLayout.ObsDim];
        private readonly byte[] _mask = new byte[PolicyLayout.MaskDim];
        private PolicyConfig _pcfg;
        private BrainConfig _cfg = BrainConfig.Default();
        private PolicyMemory _mem = PolicyMemory.Initial();
        private BrainIntent _held;
        private bool _started;
        private PolicyAction _last;

        public PolicyKaijuBrain(IPolicyNetwork net, PolicyConfig pcfg)
        {
            _net = net ?? throw new ArgumentNullException(nameof(net));
            _pcfg = pcfg;
        }

        public int ContractVersion => BrainContracts.ContractVersion;
        public string NetworkId => _net.NetworkId;
        public PolicyAction LastAction => _last;
        public ObservationEncoder Encoder => _enc;

        public void Reset(ulong seed, in BrainConfig cfg)
        {
            _cfg = cfg;
            if (_cfg.BrainPeriodSteps < 1) _cfg.BrainPeriodSteps = 1;
            _mem = PolicyMemory.Initial();
            _held = default; _started = false; _last = default;
            _net.Reset(seed);
        }

        public void Decide(in BrainObservation obs, ref BrainIntent intent)
        {
            int period = _cfg.BrainPeriodSteps;
            bool due = !_started || (obs.Step % period) == (long)(obs.Self.Id.Value % (ulong)period);
            if (!due) { intent = _held; return; }
            _started = true;
            _enc.Encode(obs, _cfg, _pcfg, _mem, _x, _mask);
            PolicyAction a = default;
            _net.Act(_x, _mask, ref a);
            _last = a;
            ActionDecoder.Decode(a, obs, _cfg, _pcfg, _enc, ref _mem, ref intent);
            _held = intent;
        }
    }

    /// <summary>Seeded uniform policy over the masked action set (SplitMix64). The "random" baseline of R-028 section 11.</summary>
    public sealed class MaskedRandomPolicyNetwork : IPolicyNetwork
    {
        private ulong _s;
        private readonly int[] _idx = new int[PolicyLayout.NTargetChoices];
        public string NetworkId => "random@1";
        public void Reset(ulong seed) { _s = seed * 0x9E3779B97F4A7C15UL + 0x5851F42D4C957F2DUL; }

        private ulong Next()
        {
            ulong z = (_s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private int Pick(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);

        public void Act(float[] obs, byte[] mask, ref PolicyAction action)
        {
            int n = 0;
            for (int i = 0; i < PolicyLayout.NTargetChoices; i++) if (mask[PolicyLayout.MaskTargetOffset + i] != 0) _idx[n++] = i;
            action.Target = n > 0 ? _idx[Pick(n)] : 0;
            n = 0;
            for (int m = 0; m < PolicyLayout.NManeuvers; m++) if (PolicyLayout.ManeuverAllowed(action.Target, (Maneuver)m, mask)) _idx[n++] = m;
            action.Maneuver = n > 0 ? _idx[Pick(n)] : 0;
            n = 0;
            for (int s = 0; s < PolicyLayout.NSpeeds; s++) if (mask[PolicyLayout.MaskSpeedOffset + s] != 0) _idx[n++] = s;
            action.Speed = n > 0 ? _idx[Pick(n)] : 0;
        }
    }
}
