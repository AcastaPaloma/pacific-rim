# Platform
Purpose: everything the game needs from the host platform behind one interface, so Net, the build and the game never depend on Steam directly (Q-233). Identity, lobbies for N members, and a P2P transport that returns the existing `Net` `ITransport`. A null/loopback implementation ships now; the Steam adapter is a drop-in later.

Breakdown IDs: F-063 (Steam adapter, later), Q-233, M-121 (build). Task: T-141. Layer: L4 (peer of Net). References only `Game.Core.Contracts` and `Game.Net.Contracts`; `Net` never references Platform.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Platform.Contracts` | `Contracts/` | none | `IPlatformServices`, `PlatformKind`, `PlatformUserId`, `LobbyId`, `LobbyInfo`, `LobbyMember`, `LobbyResult`, `PlatformContractVersions` (`platform@v1`) |
| `Game.Platform.Null` | `Null/` | none | `NullPlatformServices`, `NullPlatformHub` (shared in-process state for several users), `LoopbackTransport` |
| tests | `DotnetTests/Platform.Tests` | test | NUnit, dotnet only |

## Contract `platform@v1`
- Identity: `LocalUserId` (0 invalid), `LocalDisplayName`.
- Lobby: `CreateLobby(maxMembers, out id)` with 1..256 members (never a fixed 2), `JoinLobby`, `LeaveLobby`, `CurrentLobby`, `ListLobbies` (joinable only, ascending id), `TryGetLobby`, `GetMembers` (join order, first = owner). One lobby per user; when the owner leaves, ownership passes to the earliest remaining member; an empty lobby disappears. Results are a `LobbyResult` enum, never exceptions.
- P2P: `CreateP2PTransport(peer)`; both peers call it with the other's id and receive the two ends of one link. Null returns a `LoopbackTransport` (ordered, lossless, no clock); a Steam adapter wraps its P2P session in `ITransport`. Null return for invalid or self peer.
- Synchronous and callback-free so the null implementation is deterministic. The Steam adapter completes calls inside itself or returns `Unavailable`.

## Feature flag
`platform.steam` (`Config/FeatureFlags/Platform.json`, default off). No Steam implementation exists; composition uses `NullPlatformServices` until the SDK licence row closes. **Not wired** into `Game.Composition` (another track's file); a follow-up installer should register `IPlatformServices` and pick the implementation from the flag.

## Known limits / TODO
- Steam adapter (needs the SDK, a founder action); presence, invites, rich presence, NAT relay options and async completion are staged for it.
- Null lobby has no lobby data/metadata, no kick, no clock-based expiry.
- Composition installer (follow-up): register `IPlatformServices`, and a `PlatformTransportFactory` that feeds `CreateP2PTransport` into `Net` host/client setup.
