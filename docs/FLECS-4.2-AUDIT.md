# Flecs 4.2 runtime-engine audit

This is the **pre-implementation audit**. Its recommended core work is now implemented; current workflow and limits: [RUNTIME-ENGINE](RUNTIME-ENGINE.md). Refer to STATUS and verification reports for actual results.

Checked 2026-11-05 against installed **4.2.0+6879066**, upstream commit `68790666a02b1dc679cdc9bc2d19fc285ba4fe1b`.

**Human summary:** the direction is valid. Native reactivity already works. Important 4.2 features are available but not integrated. Keep the C core/C++ host; prioritize reusable per-entity templates, Unity event/capability bindings and reliable source/state persistence. This audit proposes work; it does not claim those integrations are complete.

## Evidence

Reviewed the full 4.2 announcement through Sander's public Medium RSS; the relevant 4.1/4.0, hierarchy, storage, relationships and game-world-as-database articles; release notes; pinned Script, hierarchy, prefab, query, build, trait, REST and design documentation; actual native/Unity bridge code; official ecosystem repository metadata and selected READMEs. This is a focused architecture audit, not a claim to have read every historical post or third-party addon implementation.

New repeatable check: `python Verification/flecs42_capability_audit.py`. [Results](../Verification/flecs42-capability-checks.json) pass against the existing DLL, without changing production code:

- Runtime-defined async click loops maintain independent `mut` state on two instances and write ordinary reflected/queryable ECS components.
- A template accepting another template through an interface composes at runtime. Changing its prop updates output while retaining an unaffected child's identity.
- Default template `Parent` children work with the `ChildOf` target lookup used by our native view bridge. That bridge does not need a speculative hierarchy rewrite.
- `ecs_script_from_scene` updates a constant component assignment while preserving a source comment; a second unchanged call reports no edit.
- Deleting one instance leaves the other instance's event loop functional.

The probe explicitly imports Script Input into its isolated world. Normal Ecaty startup has math but no `flecs.script.on.click` or Shell namespace. Native probe success is **not** a completed Unity input integration, performance benchmark, phone test or full-session restore test.

## What changes our design

| Feature | Actual Ecaty status | Recommended use |
|---|---|---|
| Fine-grained Script reactivity | Working: reflected inputs/Clock drive managed expressions | Keep for ongoing reactive rules; do not replace with an app-specific expression interpreter |
| Async/await and per-instance `mut` | Available; input-driven execution proven in isolated probe | Event sequences and behavior state directly in Script; wire Unity input, picking and completion events |
| Template interfaces, inheritance, collections and includes | Composition proven; no product library | Reusable model, behavior and interaction definitions with explicit entity targets and per-instance state |
| Prefabs and inheritance | Supported; legacy fixtures exercise prefabs | Store reusable defaults separately from live instance state |
| Source edits, scene-to-source, save/undo APIs | Scene-to-source proven; app mainly replaces an entire module | Small edits, provenance and saved authored source; add explicit live-state persistence separately |
| Script Shell and reflected functions | Compiled, not app-integrated | Consider narrow agent/developer commands and discoverable capabilities; `emit` is a Shell command, not an ordinary Script statement |
| Nonfragmenting Parent hierarchy | Present; template parent lookup passes | Benchmark many replicated rigs before migrating existing named ChildOf recipes; prefab Parent children do not automatically inherit names |
| Component inheritance | Upstream capability; not the app's capability model | Shared adapter interfaces where useful; base-type observers currently do not receive derived-type events |
| Meta, Doc, Units, constraint traits and validation hooks | Reflection used; semantic capability descriptions mostly handwritten C# | Make capabilities, supported commands, outputs, units and ownership queryable in ECS; validate adapter inputs |
| Cached queries, query plans, Stats, Metrics and Alerts | UnityViews query cached; stats/metrics/alerts imported | Measure repeated bridge work, add application metrics/alerts; import alone does not configure useful monitoring |

Async blocks cannot directly create entities or assign components. They update `mut` state; reactive template bodies produce ECS changes. Changing template props restarts its async blocks; replacing source also affects task lifetime. Persistence must distinguish definitions, parameters, current state and resumable operations. Saving Script alone does not checkpoint a running simulation.

Script provides runtime-created types, expressions, functions, reactive logic and async state. It does not compile a new native physics solver or automatically bind an unknown Unity plugin. New low-level capabilities still require bindings. Async host functions/futures are a useful integration mechanism; a generic gameplay event/query-wait facility would still be work for us, not an already-wired upstream feature. Unity remains the sole solver for Unity-owned bodies. Follow 4.2's explicit staging rules when publishing events or structural changes during progression.

## Gaps in our implementation

1. **Instance ownership:** one `PhotoClay` actor, `ScriptInput` singleton and shared `AgentBehavior` module constrain composition. Introduce target relationships, per-instance template state and removable behavior-owned commands. Removing a rule does not automatically revert values it previously wrote onto pre-existing entities.
2. **Live realization:** MudBun structure/style currently pass through the C# recipe tool. Arbitrary native structural edits are not yet a universal live MudBun creation interface. Move the supported recipe/capability schema into reflected ECS and make adapters reconcile creation/change/removal. This is essential for the runtime-engine claim.
3. **Semantic discovery:** replace most handwritten capability prose with queryable reflected descriptions/relationships. This follows Sander's database/agent argument: agents need actionable facts, not only transform dumps. AI can inspect relevant capabilities on demand; it need not run every simulation frame.
4. **Bridge traffic:** RuntimePoseBridge rebuilds a query through JSON each frame and publishes node observations as Script strings. Profile, then cache recurring queries and batch native data transfers for frequent internal updates. Preserve Script/query text for AI authoring and diagnostics. This is a more concrete performance concern than C versus C++.
5. **Persistence and edits:** use upstream source-edit APIs for authored changes; separately define instance IDs, state serialization, asset versions and load order. `from_scene` intentionally ignores nonconstant expressions, template bodies and structural changes; it is not a complete world-to-Script exporter or transaction mechanism.

Suggested next milestone: two independently instantiated runtime Script behaviors, Unity pointer/controller input converted into a common event/intent contract, removal/hot replacement, saved definitions and explicit instance-state reload. Reuse existing movement and pose adapters. Verify actual Unity results, not only ECS numbers.

## C versus C++

We already compile `flecs.c` as **C99**, alongside our **C++17** host. The C++ API uses the same C backend; dynamic Script/reflection calls already use C APIs directly. Rewriting the host in C offers no inherent runtime-feature unlock or general performance gain. Keep the stable C ABI and use C APIs wherever they expose a needed feature most directly. Any performance claim needs a project benchmark.

## Addons and ecosystem

The regular distribution enables most built-in addons. **Compiled, imported and product-integrated are different states.** We should use relevant capabilities, not enable every feature indiscriminately.

- Runtime foundation: Script/Async/Input/Math, Meta, Query DSL/Cached Queries, Prefab, relationships, Modules, Systems/Pipeline/Frame, JSON. Script Input still needs its app bridge. Timers are available when needed; no need for a second main loop.
- Discovery/diagnostics: Doc, Units, constraint traits, Query Plans, Stats/Metrics/Alerts, REST/HTTP, Log and Explorer. REST remains a local development boundary; preserve the separate provider trace viewer. Imported Metrics/Alerts need application definitions to be useful.
- Infrastructure: OS API implementation, parsers, Multi World and Entity Ranges serve implementation needs. Multi World supports separate session worlds; it is not multiplayer synchronization.
- Deliberately optional: App must not take Unity's main loop. Script Platform is not selected by our build; Unity already supplies platform facilities. Journal and Perf Trace are off by default; evaluate only for a concrete debugging/profiling need. IR is experimental: measure and regression-test before enabling.
- Official Hub renderer/transform/physics modules do not justify duplicating Unity authority. Older standalone meta/json repositories are archived; built-in addons supersede them. Flecs Lua is an additional language binding, not a requirement for our current Script route. Polyglot is explicitly under construction: an optional future WebAssembly extension route, not a production prerequisite.
- Sander's games are valuable design references, but their README states their renderer/UI/input systems are not open source. Explorer references a Box engine; its public repository/API URL returned 404 during this audit. We cannot assume that implementation is available to reuse.

## Sources

- [4.2 announcement](https://ajmmertens.medium.com/flecs-4-2-is-out-09435e80fdd1), [4.2 release notes](https://github.com/SanderMertens/flecs/releases/tag/v4.2.0), [author RSS](https://medium.com/feed/@ajmmertens)
- [Game worlds as databases and agents](https://ajmmertens.medium.com/why-it-is-time-to-start-thinking-of-games-as-databases-e7971da33ac3), [data-oriented hierarchies](https://ajmmertens.medium.com/building-an-ecs-data-oriented-hierarchies-62fb2847d100), [relationship roadmap](https://ajmmertens.medium.com/a-roadmap-to-entity-relationships-5b1d11ebb4eb)
- [Pinned Script manual](https://github.com/SanderMertens/flecs/blob/68790666a02b1dc679cdc9bc2d19fc285ba4fe1b/docs/FlecsScript.md), [pinned hierarchy manual](https://github.com/SanderMertens/flecs/blob/68790666a02b1dc679cdc9bc2d19fc285ba4fe1b/docs/HierarchiesManual.md), [FAQ: C/C++ and query costs](https://www.flecs.dev/flecs/FAQ.html)
- [Explorer](https://github.com/flecs-hub/explorer), [author's games](https://github.com/SanderMertens/flecs-games), [Polyglot](https://github.com/flecs-hub/flecs-polyglot)
