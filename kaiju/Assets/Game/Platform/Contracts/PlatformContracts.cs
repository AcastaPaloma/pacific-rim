using System;
using System.Collections.Generic;
using Game.Net;

namespace Game.Platform
{
    /// <summary>Versions of the Platform layer's public contracts. Bump on any change to a type below.</summary>
    public static class PlatformContractVersions
    {
        public const int Platform = 1;
        /// <summary>Contract id as recorded in saves, logs and the build manifest.</summary>
        public const string Id = "platform@v1";
        /// <summary>Upper bound for lobby size. Not a player count: every caller passes its own N (D-004).</summary>
        public const int MaxLobbyMembers = 256;
    }

    /// <summary>Which implementation sits behind <see cref="IPlatformServices"/>.</summary>
    public enum PlatformKind : byte
    {
        /// <summary>Deterministic offline / in-process implementation (tests, builds without Steam).</summary>
        Null = 0,
        /// <summary>Steamworks adapter. Not implemented until the SDK licence is accepted (Q-233, flag platform.steam).</summary>
        Steam = 1,
    }

    /// <summary>Platform-neutral user id (Steam: the 64-bit SteamID). Zero is invalid.</summary>
    public readonly struct PlatformUserId : IEquatable<PlatformUserId>, IComparable<PlatformUserId>
    {
        public readonly ulong Value;
        public PlatformUserId(ulong value) { Value = value; }
        public bool IsValid => Value != 0UL;
        public bool Equals(PlatformUserId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlatformUserId o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(PlatformUserId other) => Value.CompareTo(other.Value);
        public static bool operator ==(PlatformUserId a, PlatformUserId b) => a.Value == b.Value;
        public static bool operator !=(PlatformUserId a, PlatformUserId b) => a.Value != b.Value;
        public override string ToString() => "user:" + Value;
    }

    /// <summary>Platform-neutral lobby id. Zero is invalid.</summary>
    public readonly struct LobbyId : IEquatable<LobbyId>, IComparable<LobbyId>
    {
        public readonly ulong Value;
        public LobbyId(ulong value) { Value = value; }
        public bool IsValid => Value != 0UL;
        public bool Equals(LobbyId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is LobbyId o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(LobbyId other) => Value.CompareTo(other.Value);
        public static bool operator ==(LobbyId a, LobbyId b) => a.Value == b.Value;
        public static bool operator !=(LobbyId a, LobbyId b) => a.Value != b.Value;
        public override string ToString() => "lobby:" + Value;
    }

    public enum LobbyResult : byte
    {
        Ok = 0,
        NotFound = 1,
        Full = 2,
        AlreadyInLobby = 3,
        NotInLobby = 4,
        InvalidArgument = 5,
        /// <summary>The backing service is not available (Steam not running, flag off).</summary>
        Unavailable = 6,
    }

    public readonly struct LobbyMember
    {
        public readonly PlatformUserId UserId;
        public readonly string DisplayName;
        public readonly bool IsOwner;
        public LobbyMember(PlatformUserId userId, string displayName, bool isOwner)
        { UserId = userId; DisplayName = displayName ?? string.Empty; IsOwner = isOwner; }
    }

    public readonly struct LobbyInfo
    {
        public readonly LobbyId Id;
        public readonly PlatformUserId Owner;
        /// <summary>Capacity chosen by the creator, 1..<see cref="PlatformContractVersions.MaxLobbyMembers"/>.</summary>
        public readonly int MaxMembers;
        public readonly int MemberCount;
        public LobbyInfo(LobbyId id, PlatformUserId owner, int maxMembers, int memberCount)
        { Id = id; Owner = owner; MaxMembers = maxMembers; MemberCount = memberCount; }
    }

    /// <summary>
    /// Everything the game needs from the host platform (contract v1): identity, lobbies for N members and a
    /// point-to-point transport that plugs into Net. Synchronous and callback-free so the null implementation is
    /// deterministic; an asynchronous backend (Steam) completes calls inside the adapter, or reports
    /// <see cref="LobbyResult.Unavailable"/>. The seam to Net is the returned <see cref="ITransport"/>
    /// (Platform.Contracts references Net.Contracts only; Net never references Platform).
    /// </summary>
    public interface IPlatformServices
    {
        PlatformKind Kind { get; }
        bool IsAvailable { get; }

        // Identity
        PlatformUserId LocalUserId { get; }
        string LocalDisplayName { get; }

        // Lobby: one lobby per user at a time; capacity is a parameter, never a constant.
        LobbyResult CreateLobby(int maxMembers, out LobbyId lobby);
        LobbyResult JoinLobby(LobbyId lobby);
        LobbyResult LeaveLobby();
        /// <summary>The lobby the local user is in, or default (invalid) when none.</summary>
        LobbyId CurrentLobby { get; }
        /// <summary>Clears and fills <paramref name="into"/> with joinable (not full) lobbies in ascending id order.</summary>
        void ListLobbies(List<LobbyInfo> into);
        LobbyResult TryGetLobby(LobbyId lobby, out LobbyInfo info);
        /// <summary>Clears and fills <paramref name="into"/> with the members in join order.</summary>
        LobbyResult GetMembers(LobbyId lobby, List<LobbyMember> into);

        // P2P: one transport endpoint per peer. The peer opens its own end with the local user's id; both ends
        // are paired by the implementation. Returns null when the peer is invalid, is the local user, or the
        // backend is unavailable.
        ITransport CreateP2PTransport(PlatformUserId peer);
    }
}
