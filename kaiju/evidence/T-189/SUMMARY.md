# T-189 smoke summary (feel-check config, batch-mode editor PlayMode, Apple M5 Pro)

Run: `unity test kaiju --mode PlayMode --filter FeelCheck_BootsFightsAndExitsClean` (result: Passed, 1/1, ~80 s incl. 57 s tile load). Raw: `smoke_feelcheck.json`, `playmode_feelcheck.xml`.

- Config `feelcheck` booted: 15 tiles, 16615 buildings, install geo/destruction/seismic/bridge/brain/combat/hangar/audio/emergence all true.
- Combat published: 5 cannon shots (7 WeaponFired, 5 ImpactImpulse: 3 building, 2 kaiju), 2 arm swings; kaiju hit points 400 -> 292 min; audio bridge received 1141 events and emitted 319 (WeaponFired/ImpactImpulse included).
- Quake Mw 6.9 (1126 BuildingDamaged), emergence event logged, impactor removed 2 buildings from the scene.
- Kaiju melee hit the mech (hp 900/1000); the hangar cycle repaired it (1 queued, 1 repaired) and redeployed at Crissy Field (terrain known); cannon refilled, new kaiju placed ahead.
- Exits clean: the test ends play mode, no errors in the log. Not in this run: the screenshot pixel check (screen captures hang in a batch editor; the macOS player run of T-039c owns it), real-device audio, frame timing (batch editor numbers are not representative).
- Note: the scripted duel (kaiju placed 90 m ahead at t=13.5 s) is smoke-only; the kaiju is otherwise 340 m away behind city blocks. Kaiju death/respawn was not reached in the smoke (needs ~8 hits).
