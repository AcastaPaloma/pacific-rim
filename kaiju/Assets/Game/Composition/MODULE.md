# Game.Composition (T-039)

The game's composition root. The only assembly that references every module's runtime; modules never reference it.

- `GameComposition.Install(registry, flags, errors, inputs)` installs, in order: Geo (`world.geo.enabled`, which also gates the baked Golden Gate Bridge `landmarks` layer, loaded with the tile set), Destruction (`destruction.enabled`), Seismic (`seismic.enabled`, needs `GameCompositionInputs.Faults`), the Destruction/Seismic shaking bridge (both flags), and the scripted kaiju brain factory (`kaiju.scriptedBrain`). Every flag defaults off, so the committed config installs nothing.
- `GameCompositionBoot` runs it once per Play session from `CoreRuntime.Booted` (added to Game.Core.Unity).
- Not wired here (needs a net session and a scene): `net.seatBridge` / `MechSeatBehaviour`. Tile loading and `DistrictRefs` come from the caller through `GameCompositionInputs`.
- Tests: `Tests/EditMode` (Unity EditMode only: the assembly references the Unity adapters).
