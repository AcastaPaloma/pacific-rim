using System;
using Game.Control.Contracts;

namespace Game.Net.ControlBridge
{
    /// <summary>Client side: each send tick reads the local pilot source, encodes it and hands it to the intent sender.</summary>
    public sealed class LocalSeatIntentPublisher
    {
        private readonly IPilotSource _source;
        private readonly Func<long, long, IntentPayload, bool> _send;
        public long Published { get; private set; }

        public LocalSeatIntentPublisher(IPilotSource source, IntentSender sender)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            _send = sender.Send;
        }

        /// <summary>Same, through a joined NetClientSession (which supplies lastHostStepSeen itself).</summary>
        public LocalSeatIntentPublisher(IPilotSource source, NetClientSession client)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (client == null) throw new ArgumentNullException(nameof(client));
            _send = (step, _, payload) => client.SendIntent(step, payload);
        }

        /// <summary>Call once per client Core step. False if the link is closed or not joined yet.</summary>
        public bool Tick(long clientStep, long lastHostStepSeen = 0)
        {
            var pl = IntentCodec.Encode(_source.Read());
            Published++;
            return _send(clientStep, lastHostStepSeen, pl);
        }
    }
}
