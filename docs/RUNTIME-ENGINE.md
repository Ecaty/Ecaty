# Runtime engine — short guide

New capability lifecycle, portable native modules and optional plugin adapters: [RUNTIME-CAPABILITIES](../Packages/com.runtime.capabilities/Documentation~/RUNTIME-CAPABILITIES.md). This document covers the existing model/actor/Script foundation.

**Input -> AI queries live capabilities/world -> runtime Flecs definitions -> Unity adapters execute -> actual results return to Flecs -> AI verifies.** No AI call runs each simulation frame.

## Try the complete workflow

1. Press **Play**, then **Runtime > Runtime engine > Start runtime playground**.
2. **CreatureA:** WASD/gamepad movement. **CreatureB:** follows A using runtime Script.
3. Click either creature to toggle its ear pose. Each has independent native async state.
4. Ask the connected agent to change one creation/behavior. Other modules are preserved.
5. Use **Save current experience**, then **Resume saved experience**, or ask the agent to save/resume. Normal startup still has only the diorama ground.

The playground uses supplied fixtures for repeatability. User-generated definitions use the same runtime entry points; the sample is not a required character class.

## What is runtime?

- Flecs Script declares real reflected component types, relationships, templates, expressions and async `mut` state after the app is built.
- `RuntimeBehaviors.<id>` modules coexist and can be replaced/removed independently. `RuntimeLibrary` provides reusable templates; AI can create more.
- `engine.wait(seconds)` is a cancellable native future using simulation time. Async blocks update `mut`; reactive bodies create/update ECS output.
- `RuntimeMotorDriver` is a Script template instantiated per actor. Its independent input/goal/command entities route player, follow or custom rules to the same motor interface.
- A `MudModel` root with named descendants containing `ClayPart`, `ClayRest`, `ClayStyle` becomes a live MudBun hierarchy. Script-created/removed brushes and style/rest changes reconcile without a recipe-tool round trip. Current roots use simple top-level IDs; 64 model bindings and 64 nodes/model are limits, not a mobile performance promise.
- FImpossible/Boing bind to actual pivots. Behavior-owned `PoseOffset` commands target parts through `PoseTarget`; actual results appear in `PoseObserved` after MudBun.
- Solvers, rendering, plugin execution and generic bindings remain compiled. New low-level capabilities need development. We do not compile new C++ or run a second simulation engine during play.

## Ownership and traffic

Unity owns collision resolution, rendering and procedural-plugin execution. Flecs owns definitions, commands, state and relationships. `ActorMotion`, `Presented` and `PoseObserved` are measurements, not competing transform writers.

Movement input, motor readback and pose command/readback now use validated generic native data transfers rather than per-frame Script strings. Repeated queries use a bounded cache; model reconciliation uses change-aware reads. Text queries and Script remain the AI authoring/debugging interface.

Pointer picking forwards native input events. Keyboard names are normalized for Script; chat text focus releases held gameplay keys. Only the selected actor consumes movement devices. No product editor UI was added.

## Save/resume

A versioned local document stores managed source modules, model/rig configuration, actor position, clock, input/steering, authored brush data and explicitly selected extra state. `Persistent`-tagged template instances retain their `.mut` components. `PersistState.components` is a JSON-array string naming additional component types to save.

Saving only reads the running world. Loading validates sources/types in an isolated native world, recreates bindings, then restores state. Pending awaits restart; this is not a mid-instruction VM or physics-solver checkpoint. Source-backed creation/edit tools are the persistence path. Arbitrary external structural edits/deletions that never reach source are not a general world serialization guarantee. Future schema/plugin migrations need explicit handling.

Use upstream source-preserving edits/undo through `edit_definition`/`undo_definition`. `CaptureScript` is an explicit scene-to-source operation with upstream limitations; save does not invoke it on live schema modules.

## Inspect and verify

- Explorer: `Capabilities`, `RuntimeBehaviors`, `EngineTelemetry`, `EngineMetrics`, `EngineAlerts` and each model's reflected types/data.
- Developer browser trace: provider/tool requests and results, as before. No user credentials belong in ECS or saved experiences.
- **Verify runtime experience:** independent models/rigs, events, actual pose feedback, native edits, save/resume, timed rules and removal. Report: `Verification/runtime-experience-checks.json`.
- Native API checks: `python Verification/runtime_engine_smoke.py`.

Windows Editor/native are the verified baseline. The graphical Windows development player also passes the two-model/async/save-reload workflow and the separate 80-frame UI/native smoke check. Summary: `Verification/runtime-engine-upgrade-checks.json`. Imported shader/build warnings and a transient Boing editor OnValidate chain warning remain. Mobile/XR/IL2CPP, networking, vehicle/aerodynamic solvers, sound adapters and pathfinding are not established by these checks. Experimental IR and alternative renderer/physics modules remain deliberately disabled.
