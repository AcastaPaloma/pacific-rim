using System;
using Game.Control.Contracts;
using UnityEngine;

namespace Game.Net.ControlBridge.Unity
{
    /// <summary>
    /// One per mech. Owns that mech's <see cref="MechSeat"/> (PilotSeat + RemotePilotSource per assigned remote slot) and, once per FixedUpdate,
    /// pumps the shared host (or client link), then publishes <see cref="Frame"/> for the unit controller / placeholder body.
    /// Inert until configured by a composition root and until net.seatBridge is on. No scene wiring; nothing finds it by type.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MechSeatBehaviour : MonoBehaviour
    {
        public MechSeat Seat { get; private set; }
        public HostSeatPump HostPump { get; private set; }
        public ClientSeatLink ClientLink { get; private set; }
        public IntentFrame Frame { get; private set; }
        public event Action<IntentFrame> IntentReady;

        public void ConfigureHost(MechSeat seat, HostSeatPump pump) { Seat = seat; HostPump = pump; ClientLink = null; pump.AddMech(seat); }
        public void ConfigureClient(MechSeat localSeat, ClientSeatLink link) { Seat = localSeat; ClientLink = link; HostPump = null; }

        /// <summary>One simulation step. FixedUpdate calls it with Time.fixedTimeAsDouble; tests call it with their own stamp.</summary>
        public void Tick(double stamp)
        {
            if (Seat == null || !Seat.BridgeOn) return;
            HostPump?.Advance(stamp);
            ClientLink?.Advance(stamp);
            if (HostPump == null) Seat.Read(); // client: local seat only (host copy is authoritative)
            Frame = Seat.LastIntent;
            IntentReady?.Invoke(Frame);
        }

        void FixedUpdate() => Tick(Time.fixedTimeAsDouble);
    }
}
