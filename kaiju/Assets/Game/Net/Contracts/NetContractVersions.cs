namespace Game.Net
{
    /// <summary>Versions of the Net layer's public contracts. Any bump makes the handshake refuse older peers.</summary>
    public static class NetContractVersions
    {
        /// <summary>Wire protocol / Net layer version (the handshake `protocol` field).</summary>
        public const int Protocol = 1;
        public const int Transport = 1;
        public const int SessionHandshake = 1;
        public const int IntentPacket = 1;
        public const int StateSnapshot = 1;
    }
}
