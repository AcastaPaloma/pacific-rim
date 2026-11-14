# STACK.md: Giant Mech vs Kaiju Fighter (zero-cost tool stack)

**What we're building:** a realistic 3D fighting game where giant mechs fight kaiju in destructible cities. Two developers, with Claude Code (Opus) writing most of the code. Development on a MacBook Pro (M5 Pro, 24 GB); the game ships on Windows.

**Cost:** $0 in tools. This assumes the Claude plans and Midjourney we already pay for. The only fee we can't avoid is Steam's $100 per app (itch.io is free). Wherever a paid tool would have gone, Claude writes the system itself.

## The one rule that shapes everything

The game is split into two layers:

1. **Simulation (gameplay):**
   - Pure C# with no Unity references, stepped at a fixed 60 Hz with integer math.
   - It owns every gameplay fact: positions, health, hitboxes, frame data.
   - It is deterministic, which makes rollback netcode, replays and automated testing possible.
2. **Presentation (realism):** animation, ragdolls, destruction, particles, camera and audio. It reads the simulation and never writes to it.

Physics never decides gameplay. If something looks like gameplay physics, the simulation emits an event and the presentation layer shows it.

## Engine and Claude tooling

| Piece | Choice (free) |
|---|---|
| Engine | **Unity 6.6 → 6.7 LTS, Personal** (free under the revenue cap) |
| Renderer | **HDRP**. Water, volumetric clouds and aerial perspective; no ray tracing on Mac; upscaling with STP. |
| Claude ↔ Unity | **Official Unity Claude Code plugin + Unity CLI** |
| Play and verify | **Funplay MCP** or **CoplayDev MCP for Unity** (both MIT). Funplay can simulate input and take screenshots. |
| Version control | **Git + LFS** on a free host. Keep raw source files (mocap, .blend) out of the repo, or use **Unity Version Control** (free up to 25 GB). |
| Release builds | **Unity Build Automation**: 200 free Windows minutes a month, needed for IL2CPP builds, which a Mac can't make |

## Realism layer (all free)

| Need | Tool |
|---|---|
| Foot planting, aiming, pistons | **Animation Rigging** (two-bone IK + raycast grounding, written by Claude) |
| Physics-driven hit reactions | **Custom active ragdoll** (ConfigurableJoints blended toward the animation pose, written by Claude) |
| Building destruction | **Blender Cell Fracture** to pre-fracture, swap the intact mesh for the fractured one on hit, PhysX for the debris |
| Hero collapses | **Blender rigid-body sim** baked to Alembic or vertex-animation textures |
| Cables, power lines | Joint chains or a Verlet rope script |
| Sparks, debris, dust | **VFX Graph**, plus smoke flipbooks rendered from **Blender** smoke sims |
| Camera and cinematics | **Cinemachine 3 + Timeline** |

## Fighting systems (all custom C#)

- **Moves are data:** startup, active and recovery frames, hitboxes, attack level and cancel windows, stored as ScriptableObjects or JSON.
- **Hitstop and stun** come from per-attack-level tables, using Guilty Gear Strive's attack-level table as the model.
- **Input:**
  - Two bytes per player per frame, stored in a ring buffer.
  - A buffer of about 8 frames.
  - Special-move motions are detected from that buffer.
  - The two fighters always face each other (lock-on).
- **Netcode:** our own GGPO-style rollback over **Steamworks P2P**, which is free. **Photon Quantum 3** is the fallback; it's free up to 100 concurrent players.
- **AI opponent:** a deterministic utility AI that runs inside the simulation.

## Animation (we are not animators)

- **Capture:** **FreeMoCap** (open source, multi-camera, uses our phones or webcams) → **Blender**.
- **Libraries:** **Mixamo** (free, royalty-free in games; don't depend on it staying up) and **CMU Motion Capture Database** (free for any use).
- **Cleanup:** **Blender** keyframing plus Claude-written Python scripts that check foot contacts and smooth curves. Cascadeur's free tier can't export FBX, so we skip it.
- **No foot sliding:** each clip's root motion is baked into the move data, so the simulation moves the body exactly as far as the animated feet move.
- **Rigs:**
  - Mechs: Humanoid rig, each part 100% weighted to one bone (Blender Rigify works).
  - Kaiju: Generic rig with procedural IK and tail springs.
- **Scale timing:** walks are slowed by $\sqrt{l_{\text{giant}}/l_{\text{human}}} \approx 6.7\times$ for an 80 m giant. Attacks are slowed only $1.5$–$2.5\times$, with the extra time spent in wind-up and recovery.

## Content

| Type | Free tool | Licence check |
|---|---|---|
| Ideation and turnarounds | Midjourney (already owned; manual handoff, no API) | Paid plan includes commercial use |
| 3D blockouts | **Hunyuan3D** (free web tier / open weights) through **MCP for Blender**; **Meshy** free tier | Hunyuan: Tencent licence. Meshy free: CC BY, so credit Meshy. |
| 3D finishing | **Blender** + **MCP for Blender** | Free |
| Textures, HDRIs, scans | **Poly Haven** | CC0 |
| Sound effects | **Sonniss GDC bundles**, **Freesound** (filtered to CC0), **Kenney** | Royalty-free / CC0 |
| Music | **ACE-Step** (open weights, runs locally) or CC-licensed tracks | Check each licence |
| Audio engine | Unity's built-in audio | Free |
| UI | **UI Toolkit**, written by Claude from Midjourney mockups | Free |

## How agents work here

- Split work **by feature**, not by layer. Example: "Mech A heavy punch, end to end."
- One lead Opus session per human, each in its own worktree. Subagents only run tests, triage logs and review code.
- **Never** edit `.unity` or `.prefab` files as text; go through Editor APIs or the CLI.
- Batch C# edits so Unity compiles and reloads scripts once.
- **Tests are Claude's eyes:**
  - simulation tests under `dotnet test`
  - rollback checksum tests
  - golden replays
  - frame-data assertions
  - bot-driven play-mode smoke tests
- **Humans judge feel and pixels.**
- Target is a locked 60 fps ($16.67$ ms per frame).
- **Steam:**
  - AI-generated art and audio must be disclosed on the store page; code is exempt.
  - Never market the game as "AI-made".
  - No Pacific Rim names or terms anywhere.