# STACK.md: Bay Area Mech vs Kaiju (co-op, physics, pixel art)

**What we're building:** an open-world, 1:1-scale San Francisco Bay Area where 2 or more players each pilot an ~80 m mech against kaiju controlled by RL policies. The physics is real and decides fights. Rendering is dithered, limited-palette pixel art.

**Team and tools:** two developers, with Claude agents doing most of the implementation. Development on a MacBook Pro (M5 Pro, 24 GB).

**Cost:** $0 for tools. This assumes the Claude plans and Midjourney we already pay for. Steam's $100 app fee is the only unavoidable cost.

- Design source of truth: `GAME_BREAKDOWN.md`
- Code structure: `MODULARITY.md`
- Agent operating manual: `FABLE_ORCHESTRATOR_PROMPT.md`

## Core architecture

- **Host-authoritative simulation.**
  - The host runs physics, kaiju AI and destruction. Clients send intents, then receive state and predict their own mech.
  - Heavy mechs accelerate slowly, which keeps prediction errors small.
- **Physics decides gameplay.**
  - Momentum, leverage and structural failure resolve fights.
  - There is no rollback or deterministic integer simulation. Those were for the old 1v1 fighter.
- **Real units:** metres, kilograms, $g = 9.81\ \text{m/s}^2$, a fixed physics step, and a floating origin for the 1:1 world.

## Engine and Claude tooling

| Piece | Choice (free) |
|---|---|
| Engine | Unity 6.6 → 6.7 LTS, Personal |
| Renderer | **URP**, with custom renderer features for the pixel style: low-res target, palette lookup table, Bayer dithering, depth haze. (HDRP is in maintenance mode and its realism features aren't needed for this look.) |
| Physics | PhysX: **ArticulationBody** chains for mechs and kaiju, PD joint drives, Rigidbody debris |
| Animation | Animation Rigging (IK), Playables; motion clips blended with physics |
| Networking | Netcode for GameObjects + Steamworks transport (Facepunch.Steamworks, MIT) |
| Kaiju AI runtime | ONNX policies on Unity Inference Engine |
| RL training | Python + PyTorch (MPS on Mac) on a fast tactical environment that mirrors game parameters; validated in headless Unity |
| Claude ↔ Unity | Official Unity Claude Code plugin + Unity CLI; Funplay MCP (input simulation, screenshots); CoplayDev MCP (tests, profiler) |
| Version control | Git + LFS; Unity Build Automation (200 free Windows minutes a month) for IL2CPP builds |

## World data (all free, attribution tracked per layer)

- **Terrain:** USGS 3DEP elevation (public domain)
- **Seabed:** NOAA bathymetry (public domain)
- **Buildings and roads:** OpenStreetMap (ODbL; credit required)
- **Faults:** USGS Quaternary fault database (public domain)

## Content pipeline

| Asset | Tool |
|---|---|
| Concepts (layered) | Midjourney, driven by **computer use**. The founders accept the risk to the account under Midjourney's terms. |
| 3D | Hunyuan3D / Meshy free tier → Blender + MCP for Blender (cleanup, rigid-part split, rigging) |
| Destruction | Blender Cell Fracture pre-fracture; Blender rigid-body bakes for large collapses |
| VFX | VFX Graph; dust, smoke and fire as dithered pixel masses |
| Motion | FreeMoCap + Mixamo/CMU → Blender cleanup scripts |
| Audio | Sonniss / Freesound CC0 / Kenney; ACE-Step for music (licence checked) |

## Shipping

- Windows build via IL2CPP.
- Steam disclosure: AI-generated art and audio must be listed; code written with Claude is exempt.
- **Original IP only.** No franchise names, designs or lore.
