FAIL
# M1-save combined evaluation (T-025: Core save system + destruction@v1 section). eval-arch, read-only.

Run by me: `DotnetTests/Core.Tests` 96 passed / 0 failed. (WorldDestruction.Tests not re-run.) No `expectations/T-025.md`; spec = `Assets/Game/Core/MODULE.md` save section + `tasks/T-025.progress.md` claims. Probes written in scratchpad against `Core.csproj`.

## Scores
| Criterion | Score | Note |
|---|---|---|
| Atomic write | PASS | `.tmp` fully written, `Flush(true)`, `File.Replace`/`Move` (`Runtime/Save.cs:147-167`); injected failure leaves old file, tmp removed; stale `.tmp` from a crash is overwritten (probe) |
| Version / migration handling | PASS with defect | chain pre-validated (`Save.cs:209-217`); migration order fine; DEF-M1save-1 |
| Corrupt / missing file | PASS | empty file, truncated, binary, trailing garbage, duplicate field, schema 1.5 -> Corrupt/UnknownSchema statuses, no throw; Missing for absent file; JSON depth capped at 128 (`Json.cs:62`). BOM accepted. Locked/unreadable file throws IOException (DEF-M1save-3) |
| Forward compatibility | PASS with minor | newer envelope -> NewerSchema, file untouched; newer owned section -> SectionTooNew; unowned sections and unknown values (Raw) carried verbatim; DEF-M1save-4 |
| Determinism of serialisation | PASS | ordinal order, `\n`, invariant culture, f64 as bit pattern, culture tests de-DE/tr-TR, golden `destruction_v1.json` byte-compare |
| No data loss on failed save | PASS | serialise before touching disk; wrap in SaveWriteException; previous save unchanged |
| Section ownership boundaries | PASS | one owner per section name enforced (`Save.cs:184`); destruction participant touches only `destruction`; Core has no building knowledge |
| "Restore applies nothing unless all can be read" | FAIL | DEF-M1save-1 |

## Defects
- **DEF-M1save-1 (blocking)**: `SaveSystem.Restore` only pre-validates versions/migrations. Participants are then migrated and `Read` one after another (`Save.cs:218-228`); a `Read` that throws leaves earlier participants already applied, and the `FormatException` escapes `Restore` instead of becoming a `SaveLoadStatus.Corrupt` result. Probe: two participants `a` (ok) and `b` (throws FormatException): `Restore` threw FormatException with `a.got == 1` (a already applied). This contradicts `Core/MODULE.md` line 64 ("applies nothing unless all of them can be read") and the Restore XML doc; the destruction participant's own atomic Read protects only its own model. Migrations also mutate the loaded `SaveDocument` in place (`Migrate(s)`, `s.Version = ...`), so a failure mid-chain leaves a half-migrated document. PASS: Restore catches participant exceptions and returns Corrupt with no participant changed (two-phase: parse/validate all, then commit), or MODULE.md states the real guarantee; add a two-participant failing-Read test.
- **DEF-M1save-2 (minor)**: no test covers DEF-1 (multi-participant failure) - `SaveTests`/`ExtensionProofTests` only have single-failure cases. PASS: test named for it.
- **DEF-M1save-3 (minor)**: `JsonFileSaveStore.Load` (`Save.cs:141-145`) lets `IOException`/`UnauthorizedAccessException` escape from `File.ReadAllText`, unlike every other bad-file case that returns a status. PASS: map to a status (e.g. Corrupt/Unreadable) or document the throw.
- **DEF-M1save-4 (minor)**: `DestructionSaveParticipant.Read/Write` drop unknown fields on records of its own section (reads only 5 known fields, `Write` rebuilds from model), so a same-version file from a newer minor build loses extra fields on re-save; also no `.bak`/previous-generation fallback when the primary file is corrupt (by design, but a corrupt primary means total loss). PASS: record unknown fields kept as Raw, or an explicit statement in MODULE.md that same-version extras are dropped.
- **DEF-M1save-5 (minor)**: durability is rename-only (directory not fsynced, documented `MODULE.md:63`); crash safety proven only by injected stream failure, not a killed process. Accepted as documented; listed for completeness. PASS: unchanged if risk accepted.
- **DEF-M1save-6 (minor)**: `tasks/T-025.progress.md` notes the composition root does not register `DestructionSaveParticipant` and Unity EditMode run was not done (new .meta hand-written); the save is not reachable at runtime yet. PASS: registration in the Core composition root behind a flag, and a Unity EditMode run.

Counts: blocking 1, minor 5.
