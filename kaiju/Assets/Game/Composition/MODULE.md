# Game.Composition (T-039)

The game's composition root. The only assembly that references every module's runtime; modules never reference it.

- `GameComposition.Install(registry, flags, errors, inputs)` installs, in order: Geo (`world.geo.enabled`, which also gates the baked Golden Gate Bridge `landmarks` layer, loaded with the tile set), Destruction (`destruction.enabled`), Seismic (`seismic.enabled`, needs `GameCompositionInputs.Faults`), the Destruction/Seismic shaking bridge (both flags), and the scripted kaiju brain factory (`kaiju.scriptedBrain`). Every flag defaults off, so the committed config installs nothing.
- `GameCompositionBoot` runs it once per Play session from `CoreRuntime.Booted` (added to Game.Core.Unity).
- `NetCoopInstaller` (T-030c): with `net.coop` AND `net.seatBridge` on and `GameCompositionInputs.Net` set, creates the host session + `HostSeatPump` or the client session + `ClientSeatLink` and registers a `NetCoopHandle` (`AttachHostSeat`/`AttachClientSeat` add a `MechSeatBehaviour` to a GameObject). Missing inputs are reported (ConfigMissing), not installed.
- `PlaceholderInstaller` (T-030c): with `units.placeholderBody` on, registers `PlaceholderBodyFactory` (`Create(go, id, seat)` adds the TEMPORARY `PlaceholderBodyBehaviour` and wires the seat's intent frames to it).
- `CombatInstaller` (T-040): with `combat.cannon` and/or `combat.melee` on, registers `CombatHandle` (`Mount(PlaceholderBody, ...)` mounts the enabled weapons through `PlaceholderWeaponMount`; the host calls `Step(dt)`). Missing `ICombatTargetQuery` is reported and an empty world is used; a missing `IEntityHealth` gets a `LinearEntityHealth`.
- Scene wiring (which mech, which transport) is the caller's. Tile loading and `DistrictRefs` come from the caller through `GameCompositionInputs`.
- Tests: `Tests/EditMode` (Unity EditMode only: the assembly references the Unity adapters).
