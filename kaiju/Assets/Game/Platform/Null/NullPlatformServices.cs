using System;
using System.Collections.Generic;
using Game.Net;

namespace Game.Platform.Null
{
    /// <summary>
    /// Shared in-process state for <see cref="NullPlatformServices"/> instances: lobbies and loopback links.
    /// Create one hub per test and several services on it to simulate N users. Deterministic: ids count up from 1,
    /// no clock, no randomness. Not thread-safe.
    /// </summary>
    public sealed class NullPlatformHub
    {
        internal sealed class Lobby
        {
            public ulong Id;
            public int Max;
            public readonly List<PlatformUserId> Members = new List<PlatformUserId>();
        }

        internal readonly SortedDictionary<ulong, Lobby> Lobbies = new SortedDictionary<ulong, Lobby>();
        internal readonly Dictionary<ulong, NullPlatformServices> Users = new Dictionary<ulong, NullPlatformServices>();
        internal readonly Dictionary<ulong, ulong> UserLobby = new Dictionary<ulong, ulong>();
        // key = (owner of the end that is waiting) -> waiting ends by peer
        internal readonly Dictionary<(ulong, ulong), LoopbackTransport> Pending = new Dictionary<(ulong, ulong), LoopbackTransport>();
        internal ulong NextLobby = 1;
    }

    /// <summary>Offline implementation of <see cref="IPlatformServices"/> (flag platform.steam off, tests, no-Steam builds).</summary>
    public sealed class NullPlatformServices : IPlatformServices
    {
        public const ulong DefaultUserId = 1UL;
        public const string DefaultDisplayName = "Local Player";

        readonly NullPlatformHub _hub;

        public NullPlatformServices() : this(new PlatformUserId(DefaultUserId), DefaultDisplayName, null) { }

        public NullPlatformServices(PlatformUserId localUser, string displayName, NullPlatformHub hub)
        {
            if (!localUser.IsValid) throw new ArgumentException("invalid user id", nameof(localUser));
            _hub = hub ?? new NullPlatformHub();
            if (_hub.Users.ContainsKey(localUser.Value)) throw new ArgumentException("user already registered on hub", nameof(localUser));
            LocalUserId = localUser;
            LocalDisplayName = string.IsNullOrEmpty(displayName) ? DefaultDisplayName : displayName;
            _hub.Users.Add(localUser.Value, this);
        }

        public PlatformKind Kind => PlatformKind.Null;
        public bool IsAvailable => true;
        public PlatformUserId LocalUserId { get; }
        public string LocalDisplayName { get; }

        public LobbyId CurrentLobby => _hub.UserLobby.TryGetValue(LocalUserId.Value, out var l) ? new LobbyId(l) : default;

        public LobbyResult CreateLobby(int maxMembers, out LobbyId lobby)
        {
            lobby = default;
            if (maxMembers < 1 || maxMembers > PlatformContractVersions.MaxLobbyMembers) return LobbyResult.InvalidArgument;
            if (_hub.UserLobby.ContainsKey(LocalUserId.Value)) return LobbyResult.AlreadyInLobby;
            var l = new NullPlatformHub.Lobby { Id = _hub.NextLobby++, Max = maxMembers };
            l.Members.Add(LocalUserId);
            _hub.Lobbies.Add(l.Id, l);
            _hub.UserLobby[LocalUserId.Value] = l.Id;
            lobby = new LobbyId(l.Id);
            return LobbyResult.Ok;
        }

        public LobbyResult JoinLobby(LobbyId lobby)
        {
            if (!lobby.IsValid) return LobbyResult.InvalidArgument;
            if (_hub.UserLobby.ContainsKey(LocalUserId.Value)) return LobbyResult.AlreadyInLobby;
            if (!_hub.Lobbies.TryGetValue(lobby.Value, out var l)) return LobbyResult.NotFound;
            if (l.Members.Count >= l.Max) return LobbyResult.Full;
            l.Members.Add(LocalUserId);
            _hub.UserLobby[LocalUserId.Value] = l.Id;
            return LobbyResult.Ok;
        }

        public LobbyResult LeaveLobby()
        {
            if (!_hub.UserLobby.TryGetValue(LocalUserId.Value, out var id)) return LobbyResult.NotInLobby;
            var l = _hub.Lobbies[id];
            l.Members.Remove(LocalUserId);          // owner = first member, so ownership passes to the earliest joiner
            _hub.UserLobby.Remove(LocalUserId.Value);
            if (l.Members.Count == 0) _hub.Lobbies.Remove(id);
            return LobbyResult.Ok;
        }

        public void ListLobbies(List<LobbyInfo> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            foreach (var kv in _hub.Lobbies)
                if (kv.Value.Members.Count < kv.Value.Max) into.Add(Info(kv.Value));
        }

        public LobbyResult TryGetLobby(LobbyId lobby, out LobbyInfo info)
        {
            info = default;
            if (!lobby.IsValid) return LobbyResult.InvalidArgument;
            if (!_hub.Lobbies.TryGetValue(lobby.Value, out var l)) return LobbyResult.NotFound;
            info = Info(l);
            return LobbyResult.Ok;
        }

        public LobbyResult GetMembers(LobbyId lobby, List<LobbyMember> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (!lobby.IsValid) return LobbyResult.InvalidArgument;
            if (!_hub.Lobbies.TryGetValue(lobby.Value, out var l)) return LobbyResult.NotFound;
            for (int i = 0; i < l.Members.Count; i++)
            {
                var u = l.Members[i];
                into.Add(new LobbyMember(u, _hub.Users[u.Value].LocalDisplayName, i == 0));
            }
            return LobbyResult.Ok;
        }

        public ITransport CreateP2PTransport(PlatformUserId peer)
        {
            if (!peer.IsValid || peer == LocalUserId) return null;
            // The peer already opened its end towards us: take the other end.
            if (_hub.Pending.TryGetValue((peer.Value, LocalUserId.Value), out var waiting) && waiting.IsConnected)
            {
                _hub.Pending.Remove((peer.Value, LocalUserId.Value));
                return waiting;
            }
            var pair = LoopbackTransport.CreatePair();
            _hub.Pending[(LocalUserId.Value, peer.Value)] = pair.Item2;   // keep the far end for the peer
            return pair.Item1;
        }

        static LobbyInfo Info(NullPlatformHub.Lobby l) => new LobbyInfo(new LobbyId(l.Id), l.Members[0], l.Max, l.Members.Count);
    }
}
