# Proposal: docs index, minimal read set, trims (plan item I7)

Status: **trims in section 3 applied 2026-11-11** (PRODUCT-DIRECTION, RUNTIME-PLUGINS, UNITY-CAPABILITY-ROADMAP archived under docs/history/; RUNTIME-BEHAVIORS retired and archived). **Sections 1-2 (docs index, smaller mandatory read) still need approval.** Sizes below are from before the trim. Sizes are current (KB).

## 1. Docs index (new `docs/README.md`, ~2 KB, links only)

Group every doc under one-line purposes:

- **Start here:** `AGENTS.md`, `STATUS.md`, `BACKLOG.md`, `PLAN-2026-11.md`.
- **Architecture:** `PROJECT.md`, `PROJECT-STRUCTURE.md`, `FLECS-ARCHITECTURE-AUDIT.md`, `RUNTIME-STACK.md`, `RUNTIME-ENGINE.md`, `RUNTIME-COMPOSITION-DESIGN.md`.
- **Capabilities/plugins:** `CAPABILITY-CONTRACT.md`, `RUNTIME-CAPABILITIES.md`, `RUNTIME-PLUGINS.md`, `UNITY-CAPABILITY-ROADMAP.md`, `RUNTIME-BEHAVIORS.md`.
- **AI:** `AI-INTEGRATION.md`, `LOCAL-AI.md`, `BROWSER-TRACE.md`.
- **Product:** `PRODUCT-DIRECTION.md`, `GALLERY-WORLDS.md`, `WORKSHOP-CHAIN.md`, `DISTRIBUTION.md`.
- **Process:** `DEVELOPMENT.md`, `TESTING.md`, `RESOURCES.md`, `QUICKSTART.md`.
- **Archive:** `history/`, `prototypes/` (read only when current docs lack something).

## 2. Minimal mandatory read (target about 17 KB)

Today `AGENTS.md` tells every session to read PROJECT (13.6) + DEVELOPMENT (11.8) + STATUS (2.6) before any code change: about 28 KB plus AGENTS. Proposal:

| Always | Size | Why |
|---|---|---|
| `AGENTS.md` (rules) | ~7 | Constraints and ownership |
| `STATUS.md` | 2.6 | Current state, next action |
| `BACKLOG.md` | 3.2 | Task list |
| `PROJECT-STRUCTURE.md` | 4.1 | Where code belongs |

Everything else is read on demand by area (native/bridge: FLECS-ARCHITECTURE-AUDIT + PROJECT; tests: TESTING; AI: AI-INTEGRATION; capabilities: RUNTIME-CAPABILITIES + CAPABILITY-CONTRACT). `AGENTS.md` would say that instead of listing PROJECT/DEVELOPMENT as mandatory. PROJECT.md and DEVELOPMENT.md stay as reference with a "read when" line at the top.

## 3. Trims (each needs your approval)

| Doc | Now | Proposal |
|---|---|---|
| `PRODUCT-DIRECTION.md` | 33.6 | About 14 dated 2026-11-08/09 gallery sections (headings at lines 3-120: orbit study, layered world-tree candidate, structural workbench, background projection, requested world lifecycle, authorized gallery prototype, retained gallery idea, gallery constraints, grouping/preview reactions) are an iteration log. Move them verbatim to `history/PRODUCT-DIRECTION-gallery-log-2026-11.md`; replace with one "Current gallery" section matching what ships (GALLERY-WORLDS.md already describes it). Keep Feel, bench, themes, first visit, references, implementation order. Expected result ~12 KB. |
| `RUNTIME-PLUGINS.md` | 19.9 | Keep "Adapter contract", "Boing EntityId compatibility", "Other owned assets". Archive the dated "Gallery implementation inspection", "User-owned candidates reviewed", "Gallery refinement and shader authoring" and "Current Feel / Amplify use". Fold "Primary references" into a short list. Expected ~7 KB. |
| `UNITY-CAPABILITY-ROADMAP.md` | 22.8 | Keep "Product-facing tools" and "Capability decisions". "Next implementation slice (proposal)", "Follow-up decisions: peaceful runtime creation" and "Adaptive worlds, handles, shaders, music…" overlap BACKLOG items 5-6 and RUNTIME-CAPABILITIES: move accepted decisions there, archive the rest. Expected ~9 KB. |
| `RUNTIME-BEHAVIORS.md` | 6.0 | Titled "Legacy shared behavior workflow". If the Runtime Behavior menu/demo is still a product path, rename and keep (~3 KB: "What works" + verification). If superseded by capabilities, archive it and delete the Runtime Behavior menu items (see the TESTING.md inventory). **Needs your decision.** |

Net effect: the mandatory read drops from about 35 KB to about 17 KB; the four trimmed docs shrink from ~82 KB to ~31 KB with nothing deleted (moved to `history/`).

## 4. How to apply (after approval)

1. Create `docs/README.md`.
2. Move dated sections verbatim to `docs/history/`.
3. Edit the AGENTS.md "read first" lines.
4. Fix links to moved headings, then update STATUS and BACKLOG.
