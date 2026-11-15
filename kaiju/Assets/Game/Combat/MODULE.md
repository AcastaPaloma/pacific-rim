# Combat (T-040): melee and one cannon, engine-free core

Breakdown IDs: F-005 (melee), F-006 (heavy weapons: arm cannon), D-011. Layer L3. References Core.Contracts, World.Destruction.Contracts only. Numbers: `research/R-027.md`.

## Assemblies
| Assembly | Folder | Engine | Contents |
|---|---|---|---|
| `Game.Combat.Contracts` | `Contracts/` | none | `CVec3`, `ICombatTargetQuery` + `SweepHit`, `ImpactRecord`, `IEntityHealth`, `IWeaponMount`, bus events `WeaponFired`, `RecoilImpulse`, `ImpactImpulse`, `CombatFlags`, `CombatContractVersions` |
| `Game.Combat` | `Runtime/` | none | `Ballistics` (RK4, SI, drag), `DragCurve`, `CannonSpec`/`Cannon`, `ArmSpec`/`MeleeArm`, `ImpactMath`, `ImpactRouter`, `LinearEntityHealth`, `CombatSystem`, `SimpleTargetWorld` |
| tests | `Tests/EditMode` | test | `dotnet test DotnetTests/Combat.Tests`; same sources run in Unity EditMode |

## Behaviour
- Cannon: `TryFire` (finite ammo, reload steps) creates a shell with muzzle velocity plus shooter velocity; recoil impulse `(m_shell + 1.25 m_charge) v` is an OUTPUT (`RecoilImpulse` event and `CombatSystem.RecoilOutbox`). Each fixed step the shell is flown (RK4, gravity, quadratic drag with Mach-dependent Cd, wind) and its path swept as a sphere of calibre radius against `ICombatTargetQuery` and the ground plane; first contact wins; no tunnelling.
- Melee: `MeleeArm` is a rigid rod about a shoulder axis; swing = smoothstep elevation profile, duration stretched so peak tip speed never exceeds `MaxTipSpeed`; the tip (and optional blade samples) are swept each step; one contact per swing; effective mass `I / lambda^2`.
- Contact law (`ImpactMath`): `J = mu (1+e) v_n`, dissipated `0.5 mu (1-e^2) v_n^2` (the delta-KE Destruction expects). Always `0 <= E <= 0.5 m_eff v_n^2` and energy balances exactly.
- Routing (`ImpactRouter`): publishes `ImpactImpulse`; building hits call `IDamageModel.Apply(id, energyJ, Impact, source, step)` (no Destruction change; impulse goes out on the bus because Destruction takes joules only); kaiju/mech hits call `IEntityHealth.TryApply`.
- Determinism: ballistics uses only + - * / sqrt (bit-reproducible); melee uses Sin/Cos for the arm direction (host-authoritative, reproducible per runtime). The host steps it; clients never do.

## Feature flags
`combat.cannon`, `combat.melee` (default off, `Config/FeatureFlags/Combat.json`). The installer (`Composition/Runtime/CombatInstaller.cs`) mounts weapons on the TEMPORARY placeholder body when on.

## Staged to after T-005 (tracked, not dropped)
Recoil into stance (the real body consumes `RecoilImpulse`), melee contact from the walking body and joint/plate damage (F-005), `physics_log` contacts, intent mapping (Control `IntentFrame` has no fire/swing move yet), the Unity physics `ICombatTargetQuery` adapter, grapple/shove/throw, extendable blade and rocket strike, ammo resupply rules, kaiju weapons and mech-as-target health, feel tuning and the founder sign-off.
