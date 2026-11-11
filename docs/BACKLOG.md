# Ecaty backlog

The canonical short task list. Keep it current; history lives in docs/history/BACKLOG-log-2026-11.md. Mark work done only with current evidence. New ideas do not automatically displace the current item. For new features, first propose a cheap mockup (sketch, HTML/Artifact prototype, or a throwaway config/scene toggle) before implementing.

1. **[Done] Modular restructure.** Package-first shared code; feature-owned code/config/assets/tests; top-level _Settings; wiring in _Adapters, shared composition in __Common.
2. **[Done; user play-verified 2026-11-11] Gallery return/reveal/recall, galaxy rings, swap.** Glass-like screen controls remain a future presentation candidate.
3. **[In progress] Native/runtime split and full use of Flecs.** Audit use of Flecs Shell/Script (templates, loops, reactivity), query language and addons (alerts, metrics, timers, pipelines, units, doc, REST/Explorer). Decide what is compiled C++ Flecs (core reusable components/systems, cached queries, pipeline phases) vs runtime (user-created Script/templates/prefabs). Answer with measurements: profile C# orchestration, bridge traffic (JSON per frame) and Script evaluation before any broad redesign. See docs/FLECS-ARCHITECTURE-AUDIT.md.
4. **[Gallery done 2026-11-11] Config as Flecs Script, SOs Editor-only.** Gallery: Gallery.asset (Editor authoring) → generated GalleryConfig.flecs (shipped), live in Play. Remaining: Theme (colors/UI palettes) via the same pattern; later let native systems read config components directly instead of C# copies.
5. **Extend the real-world loop.** Done and verified (main 74c82c7): typed capability configs (config class + adapter in one file, generated schema, committed export for tools); saved experiences and gallery worlds persist and rebuild capability bindings, CapDependsOn relations, NativeTree and owned sources; failures keep the last valid result; leak-free dispose and allocation-free idle reconcile. Remaining: world-scoped AI tool routing, cross-world object/feature reuse with bindings, preserving editing context. No second C# ECS.
6. **Creation through reusable composition.** Progressively revealed 3D tool families, object logic/behaviors/relationships, environment/weather/time, capability-driven influence volumes. Mockup decisions (user, 2026-11-11, docs/prototypes/creation-tools.html): tools appear **only on press-and-hold** (tap just selects); life = **recipe shelf by default plus wires** for cause→reaction links; influence = **source objects whose halo is their reach** (rain cloud, lamp); environment = sun token + time dial for now (no preference stated). Next: design note docs/CREATION-TOOLS.md, then implementation thread.
7. **Later product flows (design before implementing).** First-ever learning world with resumable progress; launch continuity; archive browsing/restoration and bin; text-light organization; shared/discovered worlds and multiplayer authority. No networking or publishing authorized.

Retained ideas, not immediate implementation:

- Preview identity includes distinctive environment, weather and behavior as well as objects; normalize previews to one cell while keeping world styles independent.
- Idle creations may gently attract attention. Cross-preview interactions were deferred when enclosed spheres were chosen; do not silently reintroduce them.
- Groups loosely gather from afar and spread on approach; folder-like organization remains an option.
- Future filmmaking/timelines, sharing, glTF export, cloud previews and 3D printing; no delivery/platform rights implied.
- AI/advanced authoring composes supported definitions and rules; casual players compose recognizable capabilities. Games and interactive experiences in scope, with versioned patches and future permission-aware multiplayer.
- Known separate vendor issue: unused Cavity Lit/URP shader incompatibility (no vendor patch applied).

## Decisions

- 2026-11-11: Product docs (PRODUCT-DIRECTION, RUNTIME-PLUGINS, UNITY-CAPABILITY-ROADMAP): trim to current content; archive the rest in docs/history/, do not delete.
- 2026-11-11: Retire the old AgentBehavior compatibility path and the -workshopLegacy start option. Done (tools, menu, verifier removed; RUNTIME-BEHAVIORS archived in docs/history/).
- 2026-11-11: Rename existing identifiers (Ecaty.* namespaces, com.ecaty.* packages, ECATY_CAP_* defines, menu paths) later, once the product name is settled; add no new ones.
- 2026-11-11: Next cheap mockup after creation tools: decide later.
- 2026-11-11: Overnight agent work allowed (keep-awake approved).
- 2026-11-11: Test phone: ARCore (Depth API) is required; use a non-Google mid-range Android for multi-device coverage. Chosen (delegated to assistant): Motorola edge 60 fusion 8/256 (~€279, Galaxus.de, ships to DE); later optional second device with a different chip, e.g. Motorola edge 70 fusion (Snapdragon). Confirm the exact model on developers.google.com/ar/devices before buying. iOS/ARKit: skipped for now (Android/ARCore only).
- 2026-11-11: Docs index and smaller mandatory read set approved (PROPOSAL-docs-index-and-trim.md sections 1-2): add docs/README.md index; agents read only AGENTS, STATUS, BACKLOG, PROJECT-STRUCTURE by default; rest on demand.
