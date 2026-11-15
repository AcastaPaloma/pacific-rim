using System;
using Game.Core;

namespace Game.Net
{
    /// <summary>State of one replicated body at a host step (world space, full precision).</summary>
    public readonly struct EntityState
    {
        public readonly EntityId Id;
        public readonly float PosX, PosY, PosZ;
        public readonly float VelX, VelY, VelZ;
        public readonly float Yaw;
        public readonly float YawRate;

        public EntityState(EntityId id, float px, float py, float pz, float vx, float vy, float vz, float yaw, float yawRate)
        {
            Id = id; PosX = px; PosY = py; PosZ = pz; VelX = vx; VelY = vy; VelZ = vz; Yaw = yaw; YawRate = yawRate;
        }
    }

    /// <summary>Host -> client state at host Core step <see cref="HostStep"/>, keyed by EntityId (StateSnapshot@v1).</summary>
    public sealed class StateSnapshot
    {
        public long HostStep;
        /// <summary>Sorted ascending by EntityId by the producer.</summary>
        public EntityState[] Entities = Array.Empty<EntityState>();
    }
}
