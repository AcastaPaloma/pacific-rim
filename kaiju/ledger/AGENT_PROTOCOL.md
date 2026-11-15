# AGENT_PROTOCOL.md: rules every subagent follows

> Shared operating rules for the swarm described in `FABLE_ORCHESTRATOR_PROMPT.md`.
> Your role file in `.claude/agents/<role>.md` adds role-specific inputs, outputs and limits.
> All paths are relative to the project root (`kaiju/`, the folder holding `GAME_BREAKDOWN.md`).

## 1. Read first
1. Your task brief (`tasks/<id>.md`) or the brief pasted in your prompt.
2. `GAME_BREAKDOWN.md` (design truth, follow its §0), `MODULARITY.md`, `STACK.md`.
3. Only the files your role is allowed to see (§6).

## 2. Non-negotiables
- **Pillars P1–P5** decide every trade-off. Work that serves no pillar is not done.
- **Original IP (D-009, D-022).** Never use franchise names, characters, designs, lore or iconography in prompts, code, assets, file names or docs. `art/style_refs/` sets *style only* (palette, dithering, haze, composition); nothing of its subject matter enters the game.
- **Physics truth.** Real units (m, kg, s), g = 9.81 m/s², physics-derived masses (`GAME_BREAKDOWN` §4). Never fake what can be simulated within budget.
- **Modularity.** Follow `MODULARITY.md` §1 and §5 exactly. Cross-module access only through contracts and the event bus.
- **Nothing is lost.** Anything you cannot do: write down why, the alternatives, and a proposed `Q-###` in your output file. Never silently drop a requirement.
- **Evidence over claims.** Every claim cites an artifact: file path, metric value, test name, frame number, or URL.

## 3. IDs
- Quote `D-/F-/M-/R-/Q-` IDs exactly and paste their text in full; never paraphrase a requirement from memory.
- Never invent a new ID number. Use the IDs assigned in your brief. If you need more, put the proposal in a `## Proposed breakdown entries` section of your output file with a placeholder (`R-NEW-a`, `M-NEW-a`, `F-NEW-a`, `Q-NEW-a`); the scribe assigns real numbers.
- Defect IDs are self-allocating: `DEF-<task>-<evaluator>-<n>` (for example `DEF-T004-style-2`).
- **Single-writer files:** `GAME_BREAKDOWN.md` and everything in `ledger/` are edited only by the scribe or the orchestrator.

## 4. Environment and tools
- **Python: always the `kaiju` conda env** (`environment.yml`). Run as `conda run -n kaiju python …` or `/opt/miniconda3/envs/kaiju/bin/python …`. Never use base Python, never `pip install torch`. A missing package is added to `environment.yml` and installed with `conda install -n kaiju -c conda-forge <pkg>` (pip only for the packages already listed under `pip:`).
- **Unity:** editor version from `ProjectSettings/ProjectVersion.txt`. Prefer the Unity CLI in batch mode on *your own worktree's* project: `unity test <project> --mode EditMode|PlayMode -o <xml>` and `unity run <project> --no-tail --log-file <log> --timeout <s> -- -executeMethod <Class.Method>` (the CLI adds `-batchmode -quit` itself; do not pass `-quit`, and do not pass `-nographics` when you need captures). Run `unity status` first: only drive a running editor (CLI or MCP) if its project path is your worktree. An editor attached to any other checkout is off limits.
- **Never edit `.unity` or `.prefab` files as text.** Build scenes and prefabs with Editor scripts. Commit the `.meta` file of every asset and folder you add under `Assets/`.
- Batch C# edits so Unity recompiles once. Check the log for compile errors before claiming anything.
- **Pure logic** goes in engine-free assemblies (`noEngineReferences: true`) and is tested with `dotnet test`.
- Prefer CLI and scripts over GUI, deterministic over interactive. Never run a command that waits for keyboard input.
- Facts come from WebSearch / WebFetch with citations, not from memory.
- Take a capture after every visual change. Record the tool, the exact command and the commit hash that produced each piece of evidence.
- Downloads of raw GIS data go to `Pipelines/geo/cache/` (git-ignored). Record licence and attribution per layer.

## 5. Git: commit small, commit often, always push
- Commit with the helper, which commits only the paths you name and pushes:
  `Tools/agent/commit.sh "<area>: <what> (<IDs>)" <path> [<path>…]`
- Commit after every coherent unit of work and at least every 20–30 minutes of work. Work that is not pushed does not exist.
- Never `git add -A` / `git add .`, never force-push, never rewrite history, never commit to or merge into `main`, never delete branches or other agents' files.
- Implementation agents work in their own worktree and branch, created exactly as the brief says (`.wt/<task>` on branch `task/<task>`). Everyone else writes only their own output files in the integration worktree.
- Do not commit secrets, `Library/`, raw GIS caches or anything in `.gitignore`.

## 6. Independence rules (why: one model grading its own work is unreliable)
- **Expectation agents** never read implementation code, `evidence/`, `evaluations/`, `tasks/*.impl.md`, or the other expectation draft for the same task.
- **Evaluation agents** judge artifacts only: the reconciled spec `expectations/<task>.md`, files in `evidence/<task>/`, the diff, test and metric outputs they produce themselves. They never read `tasks/*.impl.md` or any implementer narrative, and they cite an artifact for every claim.
- **Research agents** do not read implementation code.

## 7. Reporting
- Report to files, not chat. Your final message is at most 5 lines: the file paths you wrote, the verdict or headline result, and any blocker.
- The founders are not available to you. Never stop to ask a question: record it as a proposed `Q-###` with your recommended default, use that default marked `Assumed`, and continue.
- If you are blocked by something deliberate (missing access, a licence question, spending money, anything irreversible), stop that part, record it, and finish everything else.

## 8. Model and effort tiering (founder instruction, 2026-11-14: right model, right thinking level, no wasted tokens)
The orchestrator sets `model` and `effort` on every spawn from this table; the role files carry the same defaults.

| Work | Model | Effort |
|---|---|---|
| Orchestration, expectation reconciliation | strongest (the lead session) | as the lead runs |
| `eval-style`, `eval-feel` (orchestrator prompt §2: strongest model) | strongest (`inherit`) | high |
| Implementation of the hardest slices (new engine APIs, physics control, RL): named in the task's spawn | strongest (`inherit`) | high |
| Other implementation, research, `asset-*`, `eval-physics`, `eval-bay`, `eval-ai` | opus | medium (high for implementation) |
| Expectation drafts, `eval-arch`, `scribe` | sonnet | medium |
| `integrate`, `eval-perf` | sonnet | low |
| Log triage, test running, mechanical refactors, file moves | haiku | low |

Rules: do not escalate a tier to retry a failure before reading why it failed; a fix cycle that only addresses listed defects runs one tier lower in effort than the original build unless the defect is a design error; agents keep outputs within the length limits in their briefs and never paste large logs or files into their reports.

## 9. Unity queue rule (machine-wide)
Several task worktrees share one machine. Before every Unity launch, count the batch-mode editors already running and wait (re-check each minute) while the count is 2 or more:

```bash
ps ax -o command | grep -i "[U]nity.app/Contents/MacOS/Unity .*-batchmode" | grep -vc AssetImportWorker
```

Use exactly this form. The `[U]` keeps the check from counting your own shell, and asset-import workers are excluded. An earlier form of this rule (`pgrep -fl "Unity.*-batchmode" | …`) counted the caller's own shells and made agents wait for nothing: do not use it. The founders' interactive editor (not batch mode) is never counted and never driven.

## 10. Usage rules (founder instruction, 2026-11-14 afternoon; these override §8 where they differ)
The account's usage limit was hit three times during M0. From now on:

| Work | Model | Effort |
|---|---|---|
| Orchestration and expectation reconciliation | lead session | as it runs |
| New implementation of a hard slice | opus | high |
| Fix cycles on listed defects; other implementation; research; asset work | opus | medium |
| `eval-style`, `eval-feel` judging captures | opus | high |
| Other evaluators; expectation drafts; re-checks of listed defects | sonnet | medium |
| Integration, scribe, evidence regeneration, test and metric runs | sonnet | low |
| Log triage, file moves, ledger row updates, mechanical edits, status checks | haiku | low |

- **Fresh agent over a resumed one.** Resume an agent only while its report is recent and the follow-up is small. For anything else start a fresh agent with a narrow brief that names the exact files to read: an old agent re-reads its whole history at full price once its cache has expired (about an hour).
- **Narrow briefs.** Name the files and sections to read; forbid re-reading; cap output length; ask for numbers, not narration.
- **Fewer orchestrator wake-ups.** Chain mechanical steps (re-check → integrate → ledger) in one workflow or one agent instead of one orchestrator turn per step.
- **Stop rules in every implementation brief** (an effort bound and "stop and report" conditions), and no open-ended tuning.
- **No speculative work on the strongest model.** Work that is gated on a founder decision waits for it.
- **New session for new phases.** `ledger/HANDOFF.md` is the resume file: a fresh session that reads it is cheaper than continuing a very long one.

## 11. Hard limits (founder instruction, 2026-11-14, second note; these override §8 and §10 where they differ)
1. **Reasoning caps.** Orchestrator: medium (high only for a genuinely hard decision; never max). Workers are **Sonnet or Haiku only, at low or medium effort.** No worker runs on a stronger model without a founder saying so for that task.
   - Haiku / low: run a command and report numbers, update ledger rows, regenerate evidence with an existing script, file moves, status checks.
   - Sonnet / low: integration, scribe passes, metric runs with interpretation, re-checks of listed defects.
   - Sonnet / medium: implementation steps, evaluation, expectation drafts, research notes.
2. **Minimal, isolated context.** A brief names the exact files (and sections) the worker may read and the exact files it may write, pastes the few requirement lines it needs, and says "read nothing else". No project tour, no conversation history, no "read all docs first". The protocol sections a worker needs are quoted in the brief (usually §4 Unity/Python rules and §5 commit helper in three lines), not assigned as reading.
3. **Atomic tasks.** One purpose per worker, sized to finish in about five minutes of active work and never to sit idle: a worker does not wait on a long build, capture, bake or queue. Long commands are started by a script that writes its result to a file; a later worker (or the orchestrator) reads that file. A slice is a list of such steps in `ledger/QUEUE.md`, not one long-lived implementer.
4. **Reap before compaction.** Any session or worker that approaches about 150k tokens of context, or 85% of its window, stops, writes its state to a shared Markdown file (`tasks/<task>.progress.md` for workers, `ledger/HANDOFF.md` and `ledger/QUEUE.md` for the orchestrator) and ends. Work continues in a fresh session or worker that reads that file. Nobody compacts and nobody resumes a large old agent.
5. **File access per worker.** A worker reads and writes only inside the module folder(s) and task files its brief lists, in its task worktree. Anything outside is off limits even for reading unless listed. (This is enforced by the brief and checked by the architecture evaluator from the diff; the harness has no per-agent path permissions.)

## 12. Keep work moving without wasting tokens (founder instruction, 2026-11-15)
The founders want steady progress whenever they are away, and want token use kept low. When this section and §10 or §11 differ, this section wins on scheduling and §11 still wins on models, effort and brief size.

**Founders offline.**
- Do not wait for an answer from a founder. Pick the option you would recommend and log it in `ledger/HANDOFF.md`, marked "decided autonomously (user offline)", so it can be reviewed later. Then keep going.
- Still ask, and park the work, for the items in `FABLE_ORCHESTRATOR_PROMPT.md` §8 that cannot be undone: deleting assets, rewriting history, spending money, licence or IP questions. Do other work in the meantime.
- If a permission check refuses an action, log it as waiting for the founders and move on. Never route around it through another session or worker.

**Blocked work never idles a session.**
- When one task is blocked (on a founder, a failing result, a long job or another task), work on the parts of the milestone that don't depend on it.
- The milestone progress file (`plans/M<n>_PROGRESS.md`) lists the next steps in order. Read it as a set of tracks, and run every track whose inputs are already merged.

**Parallel tracks.**
- Run independent tracks at the same time, one session each.
- Two tracks are independent when they share no files, neither needs the other's result, and both can merge on their own.
- Each track session gets:
  - its own task worktree `.wt/T-0xx` and branch `task/T-0xx`, cut from the integration head;
  - a narrow brief naming its files, the order of its steps and what done looks like;
  - a stop rule: hand over at about 150k tokens (§11.4).
- The lead orchestrator starts track sessions, tells running sessions which tracks exist so nobody duplicates one, and checks merged results. It doesn't do track work itself.
- Before starting a session, check the linked sessions. Don't start one for work that has already landed or is already running. When a session hands over, start its successor promptly, so a track never sits idle with work left.
- Work that depends on several tracks (a composition root, the evaluator passes) waits until those tracks have merged.

**Saving tokens while running in parallel.**
- Track sessions run on Sonnet at medium effort and do the work themselves. Use at most an occasional Haiku/low helper for mechanical steps. Don't keep an orchestrator with its own workers inside each track.
- The lead orchestrator runs at medium effort, and wakes only on session reports. It never polls.
- Long jobs (Unity, bakes, captures, fetches) run in the background and write their results to a file. Nobody sits waiting on one.
- Archive sessions that have finished, handed over or been replaced, so their notes stop costing context.

**One integration branch, many writers.**
- Every commit, merge or push on the integration branch goes through a shared lock. Take it, check that you are on the integration branch, run git, and release it:
  ```bash
  until mkdir /tmp/kaiju-merge.lock 2>/dev/null; do sleep 15; done
  git -C <integration worktree> branch --show-current   # must print the integration branch
  # ... commit / merge / push with git -C <integration worktree> ...
  rmdir /tmp/kaiju-merge.lock
  ```
- Never run git against `main`. Once, during M1, a merge run from the wrong folder landed on the founders' local `main`.
- Unity still follows §9: start a batch process only while fewer than 2 are running machine-wide. Otherwise do other work and check again later.

## 13. Lighter verification gates (founder instruction, 2026-11-15: too much time went to verification)
The same approach as before (tests, metrics, evaluators, regressions) at a lower dose. This section overrides any earlier rule that asks for more checking. The hard safety rules still apply in full: never `main`, the merge lock, the branch check, no cache symlinks, and the Unity queue.

**Per step and per merge**
- A worker runs only the tests of the modules it touched (dotnet and/or pytest), then reports the pass count and the log path. The orchestrator trusts that report and does not re-run it. It only spot-checks when a result looks surprising.
- Merging a task branch needs:
  - fast tests green on the merge result: dotnet, plus pytest for the touched packages;
  - a one-line regression note.
- Batch the full Unity EditMode run and the `GameCoreConfig.asset` regeneration: once per 3 merges, or once at the end of a session, whichever comes first. When that run fails, bisect over the batch.
- Unity EditMode runs per merge only when the merge changes `.asmdef` files, scenes, prefabs, shaders or `GameCoreConfig.asset`.

**Evaluators**
- No evaluator pass per step. Each track gets one combined evaluator (Sonnet/low, architecture plus the track's own domain) when the track's work for the milestone has merged. Visual work also gets `eval-style`, run once on the final captures.
- An evaluator lists its defects. Only blocking defects (wrong results, broken contracts, failing gates) get a fix cycle. Minor defects go into `LEDGER.md` and wait for a later batch, with no re-check cycle of their own.
- At most one fix cycle per gate. If the gate still fails after it, record the shortfall, route it as a founder or research item, and move on (as T-004 did with C7 and C11).

**Expectations and evidence**
- A small task (one module, under about 300 lines changed) gets one expectation draft, not two reconciled ones. Two independent drafts are kept for hard slices and anything gated by a founder metric.
- Evidence is numbers plus the log or output path. Capture sets, sheets and boards are only made for visual gates or a founder checkpoint. Captures are slimmed as in T-004: short sets kept whole, long sets one frame in 100.

**Lead orchestrator**
- Trust session reports. Verify only what lands on the integration branch, with a cheap `git log`. Read a session's transcript only when its report is unclear or contradicts the branch.
- Metric suites run once per milestone checkpoint and when a track closes, not after every merge.

## 14. Escalate to Opus 5.5 at high effort when it is warranted (founder instruction, 2026-11-15)
This section overrides §11.1 ("workers Sonnet or Haiku only") and §10's "no speculative work on the strongest model" in the two cases below. The orchestrator decides for itself and does not need a founder's approval. It logs each escalation in `ledger/HANDOFF.md` with the reason, marked "escalated to Opus 5.5 high (§14)".

**When to escalate**
1. **A stalled experiment that has substantial evidence it can work.** The stop rule has ended the loop, but the evidence says the goal is reachable: feasibility has been shown offline, there are near misses, or one parameter is binding. Do not give up or route the experiment away in this case. Send Opus 5.5 at high effort.
   - Example: T-005 round 3, where offline strides were feasible at Froude 0.02–0.20 but every Unity run fell.
2. **Reason to think Sonnet at medium effort is out of its depth.** Signs:
   - the same failure repeats across cycles;
   - diagnoses contradict each other or don't hold up against the evidence;
   - fixes make no progress;
   - the work is subtle: numerics, control, solvers, concurrency, shader maths.

   In that case, Opus 5.5 at high effort retries the step or pinpoints what went wrong. Often the cheapest move is a diagnosis only: Opus reads the evidence, names the cause and writes the fix plan, and Sonnet carries out the plan.

**How**
- Use the model `claude-opus-5-5` at effort `high`. This applies to a worker (Agent tool), or to a session (`start_session` with `model`/`effort`) when the work spans several steps.
- The brief stays narrow (§11.2). It includes:
  - the evidence files and the earlier attempts' reports;
  - what has been ruled out;
  - the exact question, for example "why does imp = 2 tracking fall when the offline stride is feasible?";
  - an effort bound and a stop rule.
- One escalation per stall. If Opus also fails, record what it found, route it as a founder or research item (§13), and move on. Don't loop on Opus.
- Return to Sonnet as soon as the hard part is understood. Routine implementation, integration and evaluation stay on the §11 tiers.

## 15. The orchestrator makes every decision; the ambition never shrinks (founder instruction, 2026-11-15 night)
This section overrides every earlier rule that sends a decision to the founders, including §12's list of items that still wait for them, and `FABLE_ORCHESTRATOR_PROMPT.md` §8.

**Every decision is the orchestrator's.**
- Never wait for a founder. For each question, the orchestrator takes the recommended path and logs it in `ledger/HANDOFF.md` as "decided autonomously (founder asleep)", with its reason and the alternatives. Then work goes on.
- Workers and track sessions escalate questions to their orchestrator, never to the founders.
- The recommended path must not degrade the product. When the options differ in ambition, take the one that keeps the full scope:
  - full 3D physics-based mechs and kaiju, real units, real-data San Francisco, simulated destruction and seismic, co-op, an intelligent kaiju, and the pixel-art look;
  - choose the harder, correct approach over a shortcut.
- Staging is allowed, cutting is not. Work may be staged to a later milestone only when the full version stays on the plan, as a tracked requirement with an owner. A placeholder must be flagged and tracked until the real system replaces it, for example the kinematic mech body that stands in until T-005 lands. Never mark a gate passed by lowering a threshold or narrowing a requirement to fit a weak result. Escalate instead (§14), then route it as a research item.
- **The only limits are the safety rules, and they are not decisions:**
  - never merge to or reset `main`;
  - never rewrite pushed history;
  - never delete assets or evidence;
  - never spend money or accept a licence;
  - never drive the founders' Unity editor on port 7800;
  - never route around a refused permission.

  If one of these blocks a step, log it and work on something else.

**Keeping sessions alive while the founders are away**
- The app refuses to start a new session on a turn that another session's message or an automated event began, and it caps how many sessions can run at once. So:
  - At a hand-over, an orchestrator first tries to start its successor. If the start is refused, it leaves the successor as a proposal card and says so in `HANDOFF.md`.
  - Rather than queue work until a session slot is free, run it as Agent-tool workers (Sonnet/medium) from the orchestrator. Those can always be spawned.
  - Keep orchestrator context lean, so one session lasts longer:
    - read summaries, not transcripts;
    - delegate every file-heavy step;
    - don't re-read big files.
- Archive sessions that have finished or been replaced as soon as their result is checked. Never archive a session that still has work running, or whose side sessions do.
