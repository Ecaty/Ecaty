# Engine plan: review and target architecture

Status: **decisions made (user, 2026-11-13: the recommended option for all ten, see section 5). Phases 0, 1 and 2 are implemented, tested and merged to `main`. Phase 3 is implemented on this branch (see its status block in section 4): complete on the engine side, in use for gallery worlds and the AI tools, with the open parts of 3.2 and 3.5 listed there. Phase 4 has a first slice of every step, 4.1 to 4.6 (see its status block).** Date: 2026-11-13. Branch `feature/engine-architecture-review`, reviewed at parent `e6be5e5` (Native/Core `d30176f`, Native/App `44d491b`, flecs bridge `c92db08`, ai-agent `aa9dc8a`).

Readable page with pickable decisions: https://claude.ai/artifact/3mxTpAwGuAYdgmeBEE9jrw (private to the owner until shared).

Direction: a full runtime game engine with a cozy feeling, where players and AI create worlds, creations, behaviours, looks, motion and sound at runtime. Requirements come from `docs/CREATION-TOOLS.md` (branch `feature/creation-tools`) and the five concept notes `docs/creation/{elements,look,links,motion-sound,handles}.md` on the `feature/concept-*` branches. Background: [FLECS-ARCHITECTURE-AUDIT](FLECS-ARCHITECTURE-AUDIT.md), [FLECS-NOTES](FLECS-NOTES.md).

Evidence tags: **[C]** read in code (file:line), **[M]** measured in this review, **[I]** inference. Measurements used the committed Windows DLL (sha256 `b3d7009a…`, equal to the LFS pointer on this branch), an isolated world with REST off, from Python ctypes. Scripts and the six detailed area reports are in the shared session notes folder under `engine-review-bench/`. Unity was not in Play during the review, so no new Editor profile was taken; Editor numbers quoted are from the audit of 2026-11-11.

## Summary

1. **The split is right, but the weight is on the wrong side.** Owned native code is about 1,250 lines with three systems and two observers. Owned C# is about 23,000 lines. Most runtime state and behaviour (gallery navigation, model registry, workshop wiring, motion rig, capability coordination data) lives in C# and is mirrored into Flecs, not owned by it.
2. **Reloading a managed script deletes and recreates everything it made** [C, M]. Entity ids change, instances lose their kind, runtime values are lost, and reloading a schema script strips those components from every entity. An edit costs time proportional to the script: 0.14 ms for a 6-entity script, 19 ms for a 900-entity one. This is the main constraint on "AI-editable at runtime" and on the prefab kind model, and the plan is built around it.
3. **The engine contracts are Script-defined and string-encoded.** Capability types have six field kinds, hide structure in JSON strings, and are read by C# as JSON at 20 Hz and in several per-frame paths. Native code reads them by name lookup and through a hand-copied struct.
4. **View sync is a full copy every frame**, primitives only, one GameObject per entity, with a full acknowledgement written back. It is cheap natively (0.19 ms for 10,000 views) but the C# side scales with entity count, not with change.
5. **The concepts need five native runtimes that do not exist yet**: places and forces, links and rules, motion loops and tweens, look, and sound shaping. The measurements say Flecs can carry them (nearest-place force lookup for 1,002 bodies reads in 5.5 µs).
6. **Tests are not ready for a refactor of this size**: no C++ tests, no performance budgets, no golden saves, Unity suites need an open Editor and are not in CI.

Recommended order: safety net and one spike first, then native foundation, then **Look as the first vertical slice**, then sources, kinds and saves, then the remaining runtimes. Decisions for the user are in section 5.

---

## 1. Current architecture

```
 Player input / AI agent (Packages/com.runtime.ai-agent: providers, tool loop, chat UI; no Flecs dependency)
        │  22 tools supplied by the app (WorldAgentTools, RuntimeExperienceTools)
        ▼
 Unity C#  (~23,000 lines)
   Assets/_Features/Gallery   crystal tree: navigation, drag, entry/return, layout, picking, saved worlds
   Assets/_Features/World     RuntimeExperience (models, save/load), DioramaRuntime (actor motor)
   Assets/_Features/Creation  ClayCreation (MudBun), Workshop* (legacy wiring), RuntimePoseBridge
   Assets/__Common            GameBootstrap, Theme, Agent wiring
   Packages/com.runtime.capabilities + Assets/_Adapters   CapabilityRuntime, 24 configs, 8 gated vendor adapters
        │  FlecsWorld.SyncViews every frame (all views)      CapabilityRuntime.Reconcile at 20 Hz (JSON)
        ▼
 Packages/com.runtime.flecs   FlecsSession: P/Invoke over the C ABI (ABI 3, 61 exports)
        ▼  JSON (entity, query, component)  |  typed blittable reads/writes  |  stable query handles
 flecs_unity.dll (~1,250 owned lines)
   Native/Core  flecs_unity.cpp (world, ABI, query slots, script calls, REST)
                RuntimeCapabilities.cpp (dependency readiness, BehaviorTree.CPP, Clipper2 footprints, TinySoundFont)
   Native/App   Application.cpp (Spin, gallery ring and tree clock: 3 systems, 2 observers)
   Flecs 4.2.0 world: 26 .flecs files (582 lines) plus Script text built in C#; all loaded as managed scripts
   vendor forks as submodules: flecs, behaviortree, clipper2, tinysoundfont
```

Per frame today: `fu_tick` services REST, resolves waits, sets `Clock`, runs the pipeline, then `fu_caps_tick` (`Native/Core/src/flecs_unity.cpp:205-227`). Unity then reads every view, applies it, and writes every observation back (`Packages/com.runtime.flecs/Runtime/FlecsWorld.cs:100-242`).

---

## 2. Findings by area

### 2.1 Native core and app

**Good**
- The ABI is disciplined: version and view-size check at session start (`FlecsSession.cs:55`), no owned strings on per-frame paths, stable query handles that survive source changes, parse-once commands, REST only in Editor and development builds, non-fatal busy port.
- All contracts are reflected, so Script, JSON, REST and the typed path see the same data.
- Vendor code is pinned as fork submodules with a weekly upstream check.

**Limits**

| # | Finding | Evidence |
|---|---|---|
| N1 | **A managed-script reload deletes every entity the script created, then re-evaluates.** Ids change (`0x47d` became `0x10000047d`). Cost follows script size. | [C] `ecs_script_clear` → `flecs_delete_with((EcsScript, script))` in the vendored `flecs.c`; called from `fu_set_managed_script` (`flecs_unity.cpp:273-274`), `fu_script_edit` (`:643`), `fu_script_undo` (`:631`). [M] reload of a 6-entity script 138 µs; `fu_script_edit` on a 900-entity script **19.2 ms**; a live component write 1.7 µs. |
| N2 | **Kinds as prefabs break on reload** (table below). | [M] |
| N3 | Engine contracts (`CapBinding`, `CapState`, `CapDependsOn`, `CapDirty`) are Script structs. Native code looks them up by name several times per tick and casts `CapState` to a hand-copied struct. A member change in the `.flecs` file would corrupt reads silently. | [C] `RuntimeCapabilities.cpp:72` (`CapOutput`), `:73-79`, `:81-91`; schema in `Packages/com.runtime.capabilities/Runtime/Capabilities/Resources/CapabilitySchema.flecs:1-2` |
| N4 | Capability work runs outside the pipeline with ad-hoc queries, so idle cost grows with content and is invisible to pipeline stats. | [C] `fu_caps_tick` `RuntimeCapabilities.cpp:120-149`, called at `flecs_unity.cpp:225`. [M] empty tick 24 µs; with 64 / 256 / 1,000 bindings 32 / 47 / 103 µs; with 16 / 64 / 256 unchanged splines 49 / 99 / **288 µs** (a key string is rebuilt per spline per tick, `:98-100`); with 16 / 64 / 256 waiting trees 38 / 76 / **236 µs** (the XML is copied and compared per tree per tick, `:129`). |
| N5 | No modules and no phase map. Every type sits in the root scope (`Position`, `Spin`, `CapBinding`), where user or AI content can collide with it. Three systems, two in PreUpdate, one in OnUpdate. | [C] `Components.cpp:6-41`, `Application.cpp:15,72,77,107` |
| N6 | `RuntimeCapabilities.cpp` is 207 dense lines holding four unrelated things (coordination, behaviour trees, footprints, synthesizer) with file-level static maps. Hard to review and to extend. | [C] `RuntimeCapabilities.cpp:55,59` |
| N7 | Tool queries share the cached, change-detecting slots used by features. The 33rd distinct expression destroys all 32 named slots. | [C] `flecs_unity.cpp:426-430`, `:492-496`. [M] 40 distinct queries → 41 query builds; a new expression costs 24 µs, a repeated one 1.9 µs. |
| N8 | Any managed-script set, undo or entity delete marks **every** query handle as changed, so every consumer refreshes fully. Each AI edit therefore re-parses all capability bindings. | [C] `flecs_unity.cpp:65-71`, `:609`. [M] an unrelated script set flips an idle `[in] A` handle to changed. |
| N9 | The transform contract is Euler `Rotation`, no quaternion, and the view struct is fixed at position, rotation, scale, colour, primitive, label. Anything richer goes through capabilities and JSON. | [C] `FlecsComponents.hpp:9-17`, `flecs_unity.h:14-23` |
| N10 | Errors cross the ABI as one 4 KB text buffer. No line, column or entity is reported in a structured way for the AI. | [C] `flecs_unity.cpp:32`, `:255`, `:281-286` |
| N12 | **Redeclaring a struct that is in use corrupts stored values and reports success.** `struct Wings(span: f32, sweep: f32)` over a live `Wings(span: f32)` changes the component size from 4 to 8 while existing storage keeps 4-byte values; neighbouring entities then read each other's data. Flecs logs "cannot change trait 'Component' ... already in use" as fatal, but with soft asserts execution continues and the call returns 0. Any Script source can do this today: AI edits, REST, saved sources. | [M] `spike4.py`; [C] `fu_run_script` `flecs_unity.cpp:231-258`, `fu_set_managed_script` `:260-289`, `FLECS_SOFT_ASSERT` `Native/Core/CMakeLists.txt:32` |
| N11 | Release builds keep every addon (REST, HTTP) and keep asserts (`FLECS_KEEP_ASSERT`, `FLECS_SOFT_ASSERT`). No warning flags, no sanitizer build, no C++ test target. The cost of kept asserts is not measured. | [C] `Native/Core/CMakeLists.txt:32` |

**Kind reload matrix** [M]. Schema, kinds and instances were three managed scripts; `Speed` and `Wings` are `(OnInstantiate, Inherit)`, `Pose` is `auto_override`, parts use `@tree ChildOf`.

| Step | Result |
|---|---|
| Initial | Works as FLECS-NOTES describes: `plane2` stores only `Pose`; inherited values resolve; `plane1.engine` is found by path. |
| Reload the kind script with one value changed | Instances **lose `IsA`**: inherited `Speed` and `Wings` read as missing until the instance script is also reloaded. Own overrides survive. |
| Runtime write on an instance, then reload the kind | The runtime value survives. |
| Reload the instance script | Runtime values on its entities are **lost** (`Wings 44` back to inherited, `Pose 0.5` back to 0). |
| `fu_script_edit` an override into the instance script | Works and is written into the source, but it is a reload: other runtime values in that script reset. |
| Kind gains a new part | No error, existing instances do **not** get the part, and they lose `IsA` again. |
| Reload the schema script (even adding an unrelated struct) | **Every component of every type in that script is removed from every entity** and stays missing until all dependent scripts are reloaded. |

The app already works around this in places: schemas are installed only when missing (`Assets/_Features/World/Runtime/RuntimeExperience.cs:61`), AI edits are appended as new modules (`:120`), and saves overlay state after sources. Those workarounds are why saved sessions keep old schemas forever (2.6).

**Dead or superseded**: `fu_create` (use `fu_create_ex`), `fu_query_changes`, string-keyed `fu_read_query_data`, owned-string `fu_label` and `fu_entity_name`; `FlecsSession.Pointer`/`Keyboard` are unused by the app. Remove at the next ABI bump.

### 2.2 Unity ↔ Flecs bridge

**Good**: typed `TryRead`, `ReadQuery`, `WriteBatch`; handle cache; label bytes compared before a string is built.

| # | Finding | Evidence |
|---|---|---|
| B1 | `SyncViews` applies every view every frame: three transform writes, a `MaterialPropertyBlock` set, a property-block read-back, and an acknowledgement for each entity, changed or not. | [C] `FlecsWorld.cs:127-232`. [M] native part only: `fu_read_views` 19 µs at 1,000 and 186 µs at 10,000; `fu_write_presented` 34 µs and 331 µs. The C# part was not profiled in this review. |
| B2 | `DioramaRuntime.SyncGround` calls `world.SyncViews()` again, once per model per frame. | [C] `Assets/_Features/World/Runtime/DioramaRuntime.cs:122` |
| B3 | Views are keyed by entity id. Because a reload changes ids (N1), the GameObjects of a reloaded script are destroyed and rebuilt, and capability instances hosted under them go with them. | [C] `FlecsWorld.cs:58`, `:233-241`; [M] N1 |
| B4 | Pose and appearance ownership are C# dictionaries, invisible to Flecs, the AI and saves. `SyncViews` writes scale even when a pose owner exists. | [C] `FlecsWorld.cs:28-53`, `:140` |
| B5 | A per-renderer property block is the only colour path. In URP this takes renderers off the SRP Batcher [I, general Unity behaviour, not measured here]. | [C] `FlecsWorld.cs:159-166` |
| B6 | C# struct mirrors are checked by total size only. Two fields swapped with equal size would pass. | [C] `FlecsSession.cs:332-338` |
| B7 | `Presented` is written for every view each frame. It is read by tests, the agent prompt and one agent tool, not by gameplay. | [C] `fu_write_presented` `flecs_unity.cpp:336-348`; usage found only under tests, `WorldAgentTools.cs` and the prompt. [M] it does not disturb change detection on `Position`. |

### 2.3 Capabilities and adapters

**Good**: the contract (binding entity, one config, disposable instance, `CapState`, explicit dependencies) is sound and worth keeping. Reconcile is change-gated. All eight vendor configs sit outside their `#if` with a gate and probe. Failure recovery and graph capture/restore are tested.

| # | Finding | Evidence |
|---|---|---|
| C1 | No typed reads anywhere. Reconcile parses query JSON with Newtonsoft; several adapters read and parse JSON every frame or fixed step. This is the remaining ~22 KB/frame the audit attributed to `CapabilityRuntime.Update`; the idle test only covers `Reconcile(false)`. | [C] `CapabilityRuntime.cs:276`, `:293`; `UnityCapabilities.cs:57-58` (body, every fixed step); audio and layout ticks (report) |
| C2 | The 256-binding limit throws before the `try`, so one extra binding stops all capability processing, repeatedly. | [C] `CapabilityRuntime.cs:296-298` vs the `try` at `:311` |
| C3 | Only six field types (f32, i32, bool, u64, string, entity). Vectors are scalar triples, enums are bare ints, lists are JSON inside strings (`points`, sequences, tree XML). No ranges or units. This is what the AI has to write and what tools have to drag. | [C] `CapabilityCatalog.cs:121-134`; `CapabilityConfigSchema.flecs` |
| C4 | A restored session keeps its saved schema and only gains whole missing structs, so a member appended later never reaches old saves. | [C] `CapabilityRuntime.cs:185-196` |
| C5 | Product rules inside generic adapters: `CapHandle` knows `CapSpline`, `CapBuilding`, `ClayRest`; `CapLayout` skips children named "Visual"; `CapNavigation` writes `MoveIntent`; native footprints hard-code `CapSpline` members. | [C] `SpatialCapabilities.cs:201-218`, `LayoutCapability.cs:54`, `UnityCapabilities.cs:288`, `RuntimeCapabilities.cpp:92-108` |
| C6 | `CapMaterial` sets RGB, metallic and smoothness through a property block on four hard-coded property names, plus an optional catalogue material. No shader choice, textures, alpha, emission or pattern. | [C] `UnityCapabilities.cs:133-176` |
| C7 | The asset catalogue is filled only by an Editor step, resolved by linear scan, throws on an unknown key and returns null on a wrong type. A missing optional asset fails the binding instead of degrading. | [C] `CapabilityAssets.cs:15-23`; `Assets/_Adapters/Editor/CapabilitySetup.cs:18-21` |
| C8 | Trigger counters start at `int.MinValue`, so one-shot effects replay on load or re-enable. `CapRig` claims no ownership although the docs require it. | [C] report `review-capabilities.md`; [I] for the replay |

### 2.4 Features

**Gallery** (`Assets/_Features/Gallery`, about 5,200 lines)
- The gallery is a C# application with a Flecs mirror. Navigation, entry/exit/return, drag and swap, ring assignment and picking policy run in `CrystalWorldGallery` (about 2,150 lines in seven files). About 35 components are written by C# and read back by C#; several (`GalleryVisibility`, `GalleryDrag`, `GalleryCell`, `GalleryProjection`) are read by nothing outside tests. Native code only integrates the tree clock and ring turns. [C]
- Remaining per-frame JSON: `experiences.Has()` does a full `EntityJson` plus parse every frame while inside a world (`GalleryExperienceStore.cs:33`, `:44-48`); approach, view and drag state are written as JSON per frame while active. [C]
- **Data-loss path**: restore rejects more than 80 entries (`CrystalGalleryInteraction.cs:724`, `GalleryExperienceStore.cs:191`) but creating and saving have no cap. With 30 fixtures, the 51st created world makes the save unloadable on the next launch. [C for the two checks; I for the overwrite that follows]
- Config flows asset → generated Script → Flecs → C# copy → clamped copy → Flecs again; the C# copy is what the code uses, so a runtime edit of the Flecs config does nothing in a player. Six rings, 30 fake worlds and other limits are hard-coded. [C]

**World and Creation** (about 6,200 lines)
- `RuntimeExperience.models` is a second entity table (64 models) synchronised by scanning. `DioramaRuntime` keeps the rig as a JSON blob mirrored into the opaque `ActorRig{json}` component and re-parsed every frame per model (`DioramaRuntime.cs:381-382`). Actor position is saved from the Unity transform, not from Flecs. [C]
- A creation exists in three forms (JSON recipe, Script text, live entities) with C# converting both ways; parts are identified by path. `ClaySchema.flecs` is duplicated verbatim in a C# string. [C]
- The legacy workshop (`WorkshopTools`, `WorkshopDock`, chain templates, hard-coded `ChainBell` ids) is C# game logic already scheduled for removal in CREATION-TOOLS; `WorkshopChainLinks.flecs` and `WorkshopChainObjects.flecs` are referenced by no C#. [C]
- `GameBootstrap` chooses the product mode from command-line flags, builds the whole workshop stack and then disables it for the gallery. [C]

### 2.5 AI agent

**Good**: the package has no Flecs dependency (`Runtime.Agents.asmdef` has no references). The tool loop is bounded (10 rounds, 24 calls, 64 KB). Nothing runs per frame. Secrets are redacted in traces.

| # | Finding | Evidence |
|---|---|---|
| A1 | `apply_script` writes a new permanent module `RuntimeEdits.e<guid>` per call. The model cannot undo or replace it, and load rejects more than 256 sources, so a long session can produce a save that will not load. | [C] `RuntimeExperience.cs:120`, `:572`; [I] for the consequence |
| A2 | No scoping. Script and edit tools reach any entity, including the gallery and engine types. No dry run, no transaction, no snapshot before an edit. | [C] `WorldAgentTools.cs:17-27`, `RuntimeExperienceTools.cs:33-49` |
| A3 | Every tool takes one string; structured arguments are JSON inside that string, so providers cannot validate them. | [C] same files |
| A4 | The prompt is one hand-written 6 KB file with stale diorama-era statements; legacy tools are still registered beside their replacements. Nothing is generated from the live world except capability rows. Alerts are never pushed back after an edit. | [C] `Assets/__Common/Agent/Resources/Agent/Prompts/AgentInstructions.txt` |
| A5 | No tool lists kinds, describes a type, or documents a member. | [C] |
| A6 | Developer traces are on by default in the Editor and development builds; AGENTS.md says opt-in. | [C] `Packages/com.runtime.ai-agent/Runtime/Diagnostics/AgentTraceBrowser.cs:29` |
| A7 | The ChatGPT adapter duplicates the tool loop without the argument check. | [C] `ChatGptProvider.cs:107-170` vs `Providers/ToolLoop.cs:126-179` |

### 2.6 Persistence

- Two unrelated formats: the experience save (`Experiences/current.json`, one slot, version 1) and gallery saved worlds (`GalleryPrototype/tree-arrangement.json`, version 1, a bounded vocabulary of components). Neither has migration code; a different version is rejected. [C] `RuntimeExperience.cs:572`
- The experience save is all sources plus a state overlay chosen by tags and by a **hard-coded list of component names** (`RuntimeExperience.cs:488`). Model geometry is stored twice. [C]
- Load validates in a throwaway world first (good), then rebuilds the live world; a failure after that point leaves a half-restored world. Gallery restore throws after destructive steps. [C]
- Saved sessions keep their saved schemas (N2 workaround, C4), so types cannot evolve.
- Not saved: velocities, pending waits, undo history, command components.

### 2.7 Tests, tooling, CI

- Unity suites (Validator 150, Resources 68, EditMode 15, PlayMode 3, Gallery 131, Extended 12) drive an already open Editor through the `unity` CLI. They cannot run in CI and are not in it. [C] `Tests/Run.ps1`
- CI builds the Windows DLL and runs five Python smokes. The built DLL is discarded; nothing checks that the committed binary matches the sources beyond the Flecs revision. [C] `.github/workflows/native.yml`
- No C++ test target, no sanitizer or warning flags, no Debug build. Eight exports are never called from Python. [C]
- `bench_flecs.py` prints numbers; nothing asserts a budget. No GC or frame-time test exists.
- No golden save files. No test loads all 26 `.flecs` files. No test runs two worlds.

### 2.8 Amplify (new local imports)

- Amplify Shader Editor 1.9.9.15 is Editor-only (its asmdef targets the Editor; there is no runtime code). Shaders it writes are plain ShaderLab with the graph in a trailing comment and no runtime dependency on the package. [C, in the main checkout]
- **Amplify Shader Pack 2.3.11 is not usable yet**: the folder holds textures, models, functions and nine `.unitypackage` files, and no `.shader` files. `Samples URP 17x.unitypackage` has to be imported. Extracted to scratch, it contains 108 shaders and 157 materials; 101 shaders are SRP-Batcher compatible and 102 support instancing; most have about ten passes and about 150 KB of source. Property defaults are mostly zero; the real look values are in the sample materials. [C]
- The pack's licence text says only that redistribution is "frowned upon". Shipping compiled shaders inside a game build is the normal Asset Store use, but that is my reading, not a quoted clause; confirm before a release. [I]

---

## 3. Target architecture

### 3.1 Three layers, by how often they change

| Layer | Holds | Written by | Reload |
|---|---|---|---|
| **Native modules** (C++) | Engine contracts (reflected components with ranges, units, doc briefs) and generic runtimes (systems, observers, timers) | Us, compiled | Never at runtime. Types are stable, so the bridge can be typed and saves can evolve by appended members. |
| **Library** (Flecs Script) | Kinds (prefab chains), part and material prefabs, recipes (templates), rules | Us, the player, the AI | Rare, per kind. Dependents are re-attached automatically (3.3). |
| **World** (data) | Instances: name, kind, parent place, overrides, links. Counts, settings. | Tools and AI through typed writes | No reload. Ids are stable while the world is loaded. |

Rule of thumb, extending the audit's: **a type that Unity or a native system must understand is native; a thing a player can keep in the pouch is a kind; a thing in a world is an instance holding only its differences.**

### 3.2 Native modules and phases

```
core        Clock, Transform (position, quaternion, scale), hierarchy, presentation contract, sources, events in
cap         CapBinding, CapState, CapDependsOn; readiness and dirty propagation
look        Look, WorldStyle
motion      MotionLoop, SecondPose, Tween, MotionPath, MotionOffset; steering goals
places      Place, Area, Gravity, Buoyancy, Drag, Wind, Body, EffectiveForce; re-parenting
links       Rule, Count, timers, sensors, RuleDispatch, Playing
sound       Sound, SoundClip; synthesizer and offline shaping
geometry    Clipper2 outlines and footprints
tree        BehaviorTree.CPP for sentences that wait
app.gallery ring and tree motion (Native/App)
```

- Each is a `flecs::module` with its own file, imported in a fixed order. Systems, observers and module state live in the module (`core.cap`, `core.tree`, `core.geometry`, `app`). **Public component names stay at the root scope for now** (changed in Phase 1): Script, JSON, C# readers and saves all use those names, so moving them belongs to the save migration of step 3.4.
- One phase map, documented once: **OnLoad** events and input from Unity; **PreUpdate** intents and timers; **OnUpdate** simulation (motion, forces, rules); **PostUpdate** derived state and capability coordination; **PreStore** snapshot export. Rule systems run only while the world has `Playing`.
- No polling: capability coordination, footprints and trees react to changes, and low-rate work uses Flecs timers. Idle tick cost must not grow with content. An observer only notes what changed, because Flecs locks tables while it reports a change; a system applies the notes once per frame and returns at once when there are none.
- Threads stay off until a profile shows a CPU-bound native system.

### 3.3 Sources, kinds and instances

- **Kinds are prefab chains in the library scope**, as FLECS-NOTES recommends: engine components `(OnInstantiate, Inherit)`, per-instance state `auto_override`, named parts with `@tree ChildOf`, companion components through `With`. (Changed in Phase 3: the built-in components are copied to instances and a kind's changed value is pushed to them, because of a Flecs defect with cached queries on inherited components; content may still declare its own inherited components. See the Phase 3 status in section 4.)
- **Instances are data, not Script.** A native `spawn(kind, parent, overrides)` creates them; tools and the AI change them with typed or JSON writes (1.7 µs measured) with no reload, so ids, Unity objects and plugin instances stay alive during editing. Entity JSON of an instance already contains only its overrides [M].
- **A native source manager owns the text of every kind, recipe and template** (one source per kind, never one per world) and **applies a changed source in place** instead of reloading it (result of the step 0.3 spike, in section 4). It evaluates the new text over the existing prefab, finds what the new text no longer defines by evaluating it once under a staging scope, removes exactly that, and syncs parts on existing instances. It returns structured errors (source, line, message) and keeps a bounded undo stack per source. Kinds and instances are not Flecs managed scripts; managed scripts stay for schemas (never reloaded) and for reactive rule scripts.
- **Parts are instances of the kind's parts.** Flecs copies a prefab's children on instantiation, so a copied part does not follow later edits of the kind's part and stores every value. The manager therefore creates each part as `part : Kind.part`, which stores nothing and follows the kind; it adds parts that a kind gains and removes parts that a kind loses, unless the instance changed that part.
- **Types are never redefined.** The manager and the script entry points reject a source that redeclares an existing type with a different layout (finding N12).
- **Edit protocol**: while a value is dragged, write the component (live). On release, commit: for an instance nothing more is needed; for a kind, the manager applies the edited source in place. Target: commit under 1 ms (measured in the spike: about 0.1 ms including the staging pass).
- **Schema scripts are never reloaded in place.** Player- or AI-defined types live in their own scripts and change by adding a new version.
- If a later Flecs version updates a prefab script in place (keeping entity ids and instances), we adopt it and remove our own step.

### 3.4 Bridge (ABI 4)

1. **Change-driven view stream.** Native keeps one cached query with change detection and returns only changed tables plus removed ids. Unity applies only those. Static scenery costs nothing per frame.
2. **Typed multi-field streams** for capability and feature data: one call returns several components of a query as arrays, changed tables only. JSON stays for the AI, tools, saves and diagnostics.
3. **Type layouts exported** (`fu_type_layout`: member name, offset, type, count, range, unit). C# mirrors are generated from it at build time and verified at startup, so drift fails loudly.
4. **Ownership in the world.** Transform and appearance ownership become relationships (`(TransformOwner, binding)`), queryable, replacing the C# dictionaries. (Done in Phase 2. Not saved: a binding claims again when it is applied.)
5. **Events in.** One batched call carries Unity facts to native custom events: contact begin and end, tap, arrival, area enter and exit. Links need these.
6. **Tool calls are separate from feature calls.** One-shot uncached JSON queries with a row limit; a script change invalidates only the handles whose types or entities it touched.
7. **`Presented` becomes opt-in** (a tag on entities whose observed pose something needs), not a full write-back every frame.
8. Later, only if a profile asks for it: instanced drawing of simple repeated things from native arrays through BatchRendererGroup, and a pinned native staging buffer read as `NativeArray`. Flecs stays the only ECS.

### 3.5 Capability coordination

- `CapBinding`, `CapState`, `CapDependsOn` become native components in the `cap` module with the same member names, so Script and saves stay compatible. `CapDependsOn` gets `(OnDeleteTarget, Remove)` and `Acyclic`; `CapState` is added by `With`.
- Readiness and dirty propagation run from observers into a dirty list, processed by a 20 Hz timer system in PostUpdate. C# reads the dirty list as a typed stream and parses a config only when that binding changed.
- Config types grow real fields: vectors, colours, enums, arrays, ranges, units, doc briefs. The C# config class stays the single definition and still generates the schema.
- Command and tick paths (drag, force, audio notes, layout) use typed reads. Target: zero managed allocation per frame at idle and during a drag.
- Product rules leave the generic adapters. The 256 limit becomes a reported failure on the extra binding, not an exception.
- New concept capabilities follow one pattern: **native module owns the data and the rule; the adapter only applies results to Unity or a plugin and reports facts back.**

### 3.6 Look and the Amplify adapter

Follows `docs/creation/look.md`: one `Look` per element with the same values in every world style, and the style chosen on the world.

- **Native**: `Look { colour, pattern{colour, shape, random, size}, material{polish, metal, clear, glow} }` and `WorldStyle { style }` on the world entity. Sharing a look is `IsA` to a look prefab, so an edit reaches every user; an effect's particle prefab carries the look for all its particles.
- **Shaders**: a small set of **our own look shaders, one per world style, all with the same property contract**, authored in Amplify Shader Editor and committed as the plain `.shader` files it writes (they have no runtime dependency on Amplify). Each uses one per-material constant buffer, instancing and no custom keywords, so no variant can be stripped from a build. Start with Realistic and Toon.
- **Adapter** (`Look` capability, C#): maps `Look` to a material instance on the style's shader. Instances are cached and shared by quantised look values and reference-counted, so equal looks batch; no property blocks. The pattern becomes an albedo and normal texture from one generator, cached per look. Switching `WorldStyle` swaps materials; values do not change.
- **Manifest**: an Editor step writes a committed, versioned `LookManifest.json` (shader per style, property names, ranges, defaults, queue, batching flags). The runtime and the AI's type description read the manifest, never the Editor API.
- **Amplify Shader Pack is optional content, not the foundation.** Behind `CAP_AMPLIFY` with a probe path, the Editor step can register a few curated pack materials (for example water, glass, hologram, dissolve, snow cover) as named recipes with a handful of mapped parameters, built from the pack's sample materials into an ignored folder. If the pack is absent the element falls back to the style's own shader, keeps its recipe key in the save and reports a note, not a failure. This needs a non-throwing asset lookup (C7).
- **Why not the pack as the base**: 108 heavy shaders with different property sets cannot give "same inputs in every style", and a generic property bag gives the AI nothing stable to reason about.
- **Replaces** `CapMaterial` and the colour path in `FlecsWorld` once verified.

### 3.7 Persistence

- **One world document** for gallery worlds and experiences: format version, engine and schema versions, the kinds the world owns (Script sources), instances as override records (name, kind, parent, components that differ, pairs), counts and settings, asset references. Capability bindings are ordinary instances, so the separate capability graph folds in.
- **Versioned migration** with a chain of small migrators and golden files for every released format.
- **Atomic load**: build under a staging root, validate, then swap; a failure leaves the previous world untouched.
- **Play and rewind** use the same serializer in memory: snapshot on Play, restore on Stop.
- Types evolve by appended members because they are native and matched by name in JSON; the frozen-schema workaround goes.
- Kept items and shared recipes live in a library scope outside worlds and are reached by `IsA`.
- Several slots and autosave come with the new format; the gallery's 80-entry cap is enforced when creating, not when loading.

### 3.8 AI contract

- Tools act on the entered world only (CREATION-TOOLS T6): queries are scoped to its subtree, Script evaluates under it, the library is a separate read-only tool.
- Structured tools with real schemas: `describe(type or kind)`, `list_kinds`, `query`, `spawn`, `set`, `define_kind`, `add_rule`, `remove`, `undo`. `apply_script` remains for kinds and rules and goes through the source manager, replacing append-only modules.
- The world documents itself: doc briefs, ranges and units on every native member, alerts for invalid states ("instance without a kind", "link to a missing element"). Each tool result carries errors and the alerts the edit raised.
- A dry run evaluates a script in a scratch world before applying (the save loader already validates this way).
- The prompt shrinks to rules of conduct; type and tool descriptions are generated from the live world.

### 3.9 Performance budgets

Targets on the development desktop, release DLL. Each becomes an automated check (step 0.1). Mobile budgets are set after the device baseline (6.1).

| Budget | Target | Today |
|---|---|---|
| Native tick, idle world | ≤ 50 µs, independent of bindings, splines and trees | Met in Phase 1: 26 µs empty, 26 µs with 1,000 bindings or 256 splines, 31 µs with 256 waiting trees (before: up to 288 µs) [M] |
| Native tick, 1,000 moving elements | ≤ 0.5 ms | not built; 1,000 `Spin` ≈ 64 µs (audit) |
| View sync, 10,000 static entities | ≤ 50 µs native, no C# work | Met in Phase 2: 29 µs per tick including the change read; C# touches only changed views (before: 186 µs read + 331 µs write-back + a full C# apply) [M] |
| Bridge traffic per frame | ≤ 0.2 ms, 0 B managed allocation | Capabilities: 0 B in steady state since Phase 2 (PlayMode test); gallery ≈ 0.085 ms and < 0.1 KB (audit). No new Editor profile was taken. |
| Live value write (drag) | ≤ 5 µs | 1.7 µs [M] |
| Edit commit (release) | ≤ 1 ms | 0.14 ms to 19 ms, by script size [M] |
| Kind edit with 1,000 instances | ≤ 5 ms | reload breaks instances; in-place application ≈ 0.1 ms in the spike [M] |
| Load 1,000 instances | ≤ 100 ms | ≈ 12 µs per instance natively [M] |
| AI tool query | ≤ 1 ms native, never disturbs feature queries | Met in Phase 1: 15 µs for a new expression, no query slot used [M] |

---

## 4. Phased plan

Effort: **S** up to half a day, **M** one to two days, **L** three to five days, **XL** more than a week, in agent working time with the user reviewing between steps. Model: **F** needs Fable (architecture, native design, anything touching the ABI or save format); **S** Sonnet can do it from a written brief; **F→S** Fable writes the brief and reviews, Sonnet implements. Every step ends with `Tests/Run-All.cmd` green and STATUS updated; only extra checks are listed.

### Phase 0: safety net and one spike

| Step | What | Check | Risk | Effort |
|---|---|---|---|---|
| 0.1 | Benchmark harness in `Verification/` with JSON output and the budgets of 3.9 as thresholds; run it in native CI. Start from this review's scripts. | CI fails when a budget regresses by more than 25 %. | Noisy CI timings; use ratios against an in-run baseline. | S, **S** |
| 0.2 | Native contract checks in CI: every `.flecs` file loads in a scratch world; header exports match `DllImport` declarations; Debug build with warnings on. | New Python checks pass; the eight uncalled exports are covered or removed. | Low. | M, **S** |
| 0.3 | **Kind lifecycle spike**: how a kind can change while its instances live (re-attach after reload versus in-place application), and instances as data. **Done, see the result below.** | Every row of the matrix passes; kind edit with 1,000 instances timed. | This decides 3.3. | M, **F** |
| 0.4 | Golden saves: one experience and one gallery arrangement captured as fixtures with a load test. | EditMode test loads both. | Low. | S, **S** |
| 0.5 | Small defects found in review: 256-binding throw, gallery 80-entry cap at creation, per-frame `Has()` read, duplicate `SyncViews`, trace default. | Existing suites plus one test each. | Low. | S–M, **S** |

**Phase 0 status (2026-11-13).** Implemented on `feature/engine-architecture-review` after the user's go.

| Step | State | Evidence |
|---|---|---|
| 0.1 | Done. `Verification/bench_engine.py` with `Verification/budgets/engine-budgets.json`: 16 timings as ratios against an empty tick plus 2 behaviour flags; guards are enforced, targets are reported with the step that will meet them. In the Extended suite; CI runs it as a report until the guards are confirmed on the runner. | Passes against the committed DLL (about 45 s); ratios vary by about 10 % between runs. Two things it turned up: `tick_splines_256` runs about four times slower in roughly one process in four (cause unknown, to look at in step 1.3; the harness takes the best of three fresh processes), and `fu_write_presented` crashes on an entity id of 0 (to guard in step 2.2). |
| 0.2 | Done except the Debug build. `Verification/contract_checks.py`: header against DLL exports and C# imports, ABI constants, all 26 `.flecs` files loading after their prerequisites, and real assertions for the eight exports no smoke called. A gate in CI and in the Extended suite. **The Debug build with warnings moved to step 1.1**, where the native files are split anyway. | 36 of 36 checks pass. |
| 0.3 | Done, result below. | |
| 0.4 | Checks added to the experience verifier and the gallery suite. Each seeds `Tests/Fixtures/Saves/<name>-v1.json` from its own fixture scenario the first time it runs, then loads the committed file on every later run. The two files were seeded by the 2026-11-13 run and are committed (32 gallery entries with two created worlds; an experience with the models Alpha and Beta; fixture data only). | Both checks pass in Unity. |
| 0.5 | Done: a binding over the 256 budget fails alone and is retried (with a PlayMode test); world creation is refused at the 80-entry limit that restore enforces; the per-frame saved-world read is cached; actors refresh the views once per frame, not once per actor; trace capture is opt-in (toggle or `-agentTrace`). | Run in Unity on 2026-11-13 from a temporary test branch in the main checkout: **Run-All passed** (Validator 150, Resources 68, PlayerScripts, EditMode 15, PlayMode 4 including the new budget test, Gallery 132) and **Extended passed 14 of 14** (including the contract checks and the benchmark budgets). Not covered: a built player (the trace gating in `PlayerSmoke` only runs there) and Android. |

**Step 0.3 result (2026-11-13, done)** [M]. Scripts `spike_kinds.py`, `spike2.py`, `spike3.py`, `spike4.py` in the shared notes folder; current DLL, Python ctypes.

| Question | Result |
|---|---|
| Reload the kind, then re-add `IsA` on its instances (the plan's first idea) | **Rejected.** Values come back, but re-adding `IsA` instantiates the parts again (`engine` twice), part ids change, and reloading a base kind also detaches the kinds derived from it. 7.5 ms for 1,000 instances. |
| Evaluate the changed kind source over the existing prefab (one-shot, no reload) | **Works.** Prefab, instance and part ids stay. Instances see the new inherited value at once, also through two levels of `IsA`. About 35 µs with 1,000 instances (3 µs when the parsed text is cached); a direct component write on the prefab is 5 µs. |
| Finding what a new source removed | Evaluate it once under a staging scope: base kinds resolve to the live ones, the staged prefab lists exactly the components and parts the source defines, and deleting the staging scope leaves the live kind untouched. Staging plus apply: about 0.1 ms. |
| Parts of existing instances | Flecs copies parts: an instance's part has no `IsA` to the kind's part and does not follow its edits. A part created as `part : Kind.part` follows and stores nothing. A part added to a kind does not reach existing instances by itself; one removed from a kind stays on them. Adding one part to 1,000 instances through Script took 12.7 ms. |
| Deleting a kind that has instances | Instances stay alive and lose their inherited values. The manager must refuse, or first copy the values onto the instances. |
| Templates | A template can be redefined in place the same way; existing instances re-evaluate and keep their ids. |
| Instances created by a one-shot script (data, no managed script) | Survive every kind change with ids, overrides and runtime values intact. |
| Redeclaring a struct in use | Corrupts stored values and returns success (finding N12). |

Consequences: section 3.3 now uses in-place application; step 3.1 builds that (plus the part sync and the type guard); step 1.5 is new.

### Phase 1: native foundation

| Step | What | Check | Risk | Effort |
|---|---|---|---|---|
| 1.1 | Modules and the phase map; split `RuntimeCapabilities.cpp` into `cap`, `tree`, `geometry`, `sound` files with no behaviour change. Add a Debug build with warnings on our own sources (moved here from step 0.2) and the small C++ test target of decision 7. | Python smokes unchanged; Explorer shows modules and systems in phases; the Debug build is clean. | Name paths change for Script; keep root aliases for one release. | M, **F→S** |
| 1.2 | `cap` contracts native; observers, dirty list and timer system replace `coordinate()`; relationship traits. | Capability contract and lifecycle tests; idle tick with 1,000 bindings within budget. | Saved schemas that redefine these structs must be skipped on load. | M, **F** |
| 1.3 | Footprints and trees react to `OnSet`; no per-tick string work. | Idle tick with 256 splines and 256 trees within budget; footprint outputs identical. | Low. | S–M, **F→S** |
| 1.5 | Reject a script that redeclares an existing type with a different layout (N12), at `fu_run_script`, `fu_set_managed_script` and REST edits, before anything is evaluated. | Python test: the redeclaration returns an error, the size is unchanged and stored values read back intact. | Detecting it before evaluation needs a pre-parse of type statements; a Flecs log hook is the fallback. | S–M, **F** |
| 1.4 | Separate tool queries from feature handles; scoped invalidation on source change. | Benchmark: 40 tool queries cause no rebuild of feature handles; an unrelated script set leaves an idle handle unchanged. | Missed invalidation gives stale reads; keep the "type no longer alive" rebuild. | M, **F** |

**Phase 1 status (2026-11-13).** Implemented on `feature/engine-architecture-review` (native branches `feature/native-foundation` in Native/Core and Native/App, `feature/query-once` in the bridge package). Windows and Android libraries rebuilt.

| Step | State | Evidence |
|---|---|---|
| 1.5 | Done. `fu_run_script` and `fu_set_managed_script` refuse a script that changes the layout of a type in use, or of any compiled type, before evaluating it: the new declarations are built under a throwaway scope and the layouts Flecs computes are compared. A managed script may still change its own types. What the check cannot see (a member entity, a REST edit) is caught by a log hook: the call fails and `fu_bridge_diagnostics` counts `layoutFaults`. | Four contract checks; stored values stay intact in every refused case. REST edits are counted, not prevented. |
| 1.1 | Done, with one change: public names stay at the root (section 3.2). `RuntimeCapabilities.cpp` is split into `Capabilities`, `BehaviorTrees`, `Geometry` and `Audio`; `Modules.hpp` holds the phase map. `-DFU_WARNINGS=ON` and `-DFU_BUILD_TESTS=ON` exist; CI builds Debug with both, fails on a warning in our sources, runs the core tests and repeats the smokes on the Debug library. | Debug build: no warning in our sources; 28 core tests pass; all smokes pass on Debug and Release. **No sanitizer build:** the MinGW toolchain has none; it needs an MSVC or clang job. |
| 1.2 | Done. `CapBinding` and `CapState` are compiled components with the Script schema's names and layout. A binding gets its state and first dirty mark together with the component. Marks are noted by observers and applied once per frame and before every `fu_cap_*` call; a repeated identical result no longer marks consumers. | Idle tick with 1,000 bindings: 103 µs before, 26 µs now (an empty world is 26 µs). |
| 1.3 | Done. Footprints are recomputed when a `CapSpline` value is set. A tree is compiled when its definition is set and is not ticked while it only waits for simulation time (only under plain sequence-type nodes; reactive and timer nodes keep it ticking). | 256 unchanged splines: 288 µs before, 26 µs now. 256 waiting trees: 239 µs before, 31 µs now. The four-times-slower runs seen in Phase 0 are gone with the per-tick string work. |
| 1.4 | Done. `fu_query_once_json` (uncached, no slot, optional row limit) for tools; the AI `query` tool uses it through `FlecsSession.QueryOnce`. The string-keyed path replaces only its least recently used slot. A source change no longer flags every handle as changed. | Benchmark flags `handles_survive_unrelated_script_set` and `tool_queries_keep_feature_slots` are 1 and enforced; a new tool expression costs 15 µs (was 27 µs and a slot). |

Found on the way: the Debug build showed that writing a component inside an add, remove or set observer changes a locked table (Flecs asserts in Debug and says nothing in Release). The new modules avoid it; **the existing gallery observers in `Native/App/src/Application.cpp` write `GalleryLayerState` and `GalleryTreeMotion` inside `OnSet` observers** and are only safe because Script creates those components first. They are listed for step 6.3.

The benchmark guards were re-measured after Phase 1, so a return to the old costs now fails the run.

**Unity verification (2026-11-13, rebuilt DLL loaded, temporary test branch in the main checkout):** Run-All passed (Validator 150, Resources 68, PlayerScripts, EditMode 15, PlayMode 4, Gallery 132) and Extended passed 14 of 14. Both golden saves loaded without being rewritten. The Editor log has no Flecs error, layout fault or locked-table message. Not covered: a built player and Android (the `.so` is rebuilt but has not run on a device).

### Phase 2: bridge ABI 4

| Step | What | Check | Risk | Effort |
|---|---|---|---|---|
| 2.1 | `fu_type_layout`; generated C# mirrors; startup verification. | A deliberately wrong mirror fails at startup in a test. | Low. | M, **F→S** |
| 2.2 | Change-driven view stream, quaternion rotation, ownership relationships, opt-in `Presented`. | Budget "10,000 static entities"; gallery suite; a Play profile of `SyncViews` before and after. | Touches every presenter; keep the old call until the suites pass on the new one, then remove it. | L, **F** |
| 2.3 | Typed streams for capability reconcile and tick paths. | PlayMode test asserts 0 B per frame over 300 frames with bodies, audio and layout active. | Config parsing differences; compare applied results on the workshop scene. | M–L, **F→S** |
| 2.4 | Batched events in. | Python test: a contact event reaches a native observer in the same tick. | Low. | S, **F→S** |

**Phase 2 status (2026-11-13).** Implemented on `feature/engine-architecture-review` (native and bridge branches `feature/bridge-abi4`). Design and review were done here; the C# bridge, the capability conversion, the test updates, the builds and the Unity runs were done by cheaper agents from written briefs. ABI version is 4; Windows and Android libraries rebuilt.

| Step | State | Evidence |
|---|---|---|
| 2.1 | Done, without generated mirrors. `fu_type_layout_into` describes a reflected type leaf by leaf. `FlecsSession` checks every managed mirror against it the first time a typed read or write uses it (offset and size of each leaf, float against non-float, swapped names), then caches the result. **Mirrors are still written by hand**; generating them was not needed yet. | Contract check `type-layout`; all typed mirrors in the app passed the check in the Unity runs (no layout mismatch in the Editor log). |
| 2.2 | Done. `fu_view_changes` returns the views of tables that changed and the entities that stopped being views; `FlecsWorld.SyncViews` applies only those. A released pose or appearance snaps back to the world's values; pose-owned views are still observed every frame, so `Presented` keeps following physics bodies. New `Orientation` component (quaternion) replaces the Euler `Rotation` for an entity that has it; existing content is unchanged. `Presented` is written for changed and pose-owned views, not for every view (this replaces the planned opt-in tag: nothing has to be tagged). Pose and appearance ownership are exclusive relationships in the world, `(TransformOwner, owner)` and `(AppearanceOwner, owner)` on the view: `fu_claim` / `fu_release`, two flags in the view stream, and the two C# dictionaries are gone. The owner is the binding entity (the application module for a saved world's origin). Deleting the owner ends the ownership and Unity takes the pose back. A script or the AI can read who owns a view with a query. | 10,000 static views plus one moving: 29 µs per tick including the change read (target 50 µs); before, 244 µs for the read and 325 µs for the write-back every frame. Contract checks `view-changes` and `ownership`; PlayMode test `PoseOwnershipIsExclusiveAndEndsWithItsOwner`. Gallery 132 checks pass on the new sync. |
| 2.3 | Done. Reconcile reads binding rows as typed data and compares each configuration's bytes; JSON is parsed only for a new or changed binding. Body, navigation, rig, audio, handle and layout tick paths read and publish typed components. `CapState` stays JSON (it has a string member) and is written only when it changes. | PlayMode test `SteadyStateTicksAllocateNothing`: 300 steps with a body, a force, a drag and a light allocate 0 bytes. |
| 2.4 | Done. `fu_emit_events` delivers a batch of facts (event, entity, other entity, point, value) as Flecs events; `FlecsSession.Emit` wraps it. Nothing sends events yet: the consumers come with links and places (steps 4.3, 4.4). | Core test: an observer receives the fact during the call. Contract check `emit-events`. |

Open-item automation done alongside: `Tools/Sync-WorktreeSubmodules.ps1` (copies a worktree's submodule commits into the main checkout; its fetch path ran for real in this phase) and an AddressSanitizer build (`-DFU_SANITIZE=ON`, MSVC; a separate CI job; tests and smokes pass under it with no report).

**Unity verification (2026-11-13, ABI 4 library loaded):** Run-All passed on head `8a8b556` (Validator 150, Resources 68, PlayerScripts, EditMode 15, PlayMode 6, Gallery 132) and Extended passed 14 of 14 on the same head. Both golden saves loaded. The Editor log has no Flecs error, layout mismatch or locked-table message; two `NullReferenceException` lines come from the UniStorm vendor script at start. The first Run-All of this phase failed in the new allocation test because its setup script wrote `.5`, which Flecs Script rejects; the runner reported that as "Tests exceeded three minutes", which hides the real failure and should be fixed in the runner. Not covered: a built player and Android on a device.

**A crash found and fixed in this phase.** The first Gallery run with ownership in the world crashed the Editor twice (native `abort`, no message in the log). Cause: the gallery store claims a saved world's origin with the application module as owner. A module is a component, and Flecs gives a pair whose relationship is a plain tag the data of its target, so it tried to construct the module on the view. Both relationships now have the `PairIsTag` trait, with a contract check and a C++ test that use a component as owner. The native checks had passed before the Unity run because none of them used a component as owner. Costs to know: every distinct owner puts its views in a table of their own (fine for the 256-binding budget; one table per layout, not per child), and ownership is runtime state, not written into saves: an owner claims again when its binding is applied after a load.

Not verified: whether a view is re-sent when a value it inherits from a prefab changes. It matters for kinds (step 3.2) and is listed there. Measured on the way: an inherit trait added to `Color` or `Scale` after the view queries exist is ignored by them (an instance showed the default scale, not its kind's), so step 3.2 has to set those traits at registration, before `core.view` is imported.

### Phase 3: sources, kinds, worlds, saves

| Step | What | Check | Risk | Effort |
|---|---|---|---|---|
| 3.1 | Native source manager: in-place application with a staging pass, part sync, refusal to delete a kind in use, structured errors, undo. | The 0.3 matrix as a permanent test; edit-commit budget. | The core of the design. Part sync when an instance changed a part needs a clear rule (keep the instance's part). | L, **F** |
| 3.2 | Kind system: library scope, traits on engine components (set natively at registration), `spawn`, a first part and material library, pouch items as kinds. Check that a view is re-sent when an inherited value changes on its kind. | Spawn 1,000 instances, edit the kind, verify all follow (also on screen) and saves hold only overrides. | Child-part policy when a kind changes (decision 2). | L, **F** |
| 3.3 | World scope: `worlds.<id>` subtree, `(Entered, world)`, scoped queries and Script. | Two worlds loaded; a tool call in one cannot see the other. | Gallery private-root code must move to the same model. | M, **F→S** |
| 3.4 | World document v2, migration from both v1 formats, atomic load, Play snapshot and rewind. | Golden v1 files load; round trip equals; a corrupted file leaves the world untouched. | Save compatibility is the highest user-facing risk; keep the v1 reader until the user confirms their saves load. | L, **F** |
| 3.5 | Retire what the above replaces: `RuntimeEdits.*` modules, the `models` registry, the duplicated clay schema, the frozen-schema path, the component whitelist. | Suites; grep shows no references. | Remove only after the replacement is verified, per AGENTS.md. | M, **S** |

**Phase 3 status (2026-11-13).** Implemented on `feature/engine-architecture-review` (submodule branches `feature/sources-kinds-worlds`; ABI version 5; Windows and Android libraries rebuilt). The engine side of steps 3.1 to 3.4 is complete. The product uses it for gallery worlds and the AI tools; the experience save has its own version 2 (see 3.4); parts of 3.2 and 3.5 are open and listed with their reasons. Design, the native code and the review were done here; the C# bridge, the product changes, the Python checks, the benchmarks, the builds and the Unity runs were done by cheaper agents from written briefs.

| Step | State | What exists, and what is open |
|---|---|---|
| 3.1 | Done. | `Native/Core/src/Sources.cpp`: `fu_source_apply` evaluates a kind's text over the existing entities after a staging pass, so a refused text (script error with line and column, a type declaration, an entity that is not a kind or template, a kind still in use) changes nothing. What a new text no longer defines is removed; an instance that changed a removed part keeps it with the values it had. `fu_source_undo` (16 steps per source), `fu_source_remove`, `fu_source_text_into`, `fu_error_position`. The step 0.3 matrix is a permanent C++ test (`tests/source_tests.cpp`), run for texts with and without a tree annotation. |
| 3.2 | Engine part done. | `fu_spawn` creates an instance whose parts follow the kind's parts by name (`plane1.engine` is an instance of `Plane.engine`); an instance a script makes is attached the same way when the script has run (`fu_attach` for anything else). A kind that gains or loses a part passes that on to every instance. Values: see "Changed against the plan" below. Checked with 1,000 instances natively and on screen in a PlayMode test. **Open:** a first part and material library and pouch items as kinds. Both are content, and the content model is still being decided on the concept side; nothing in the engine blocks them. |
| 3.3 | Done. | `Native/Core/src/Worlds.cpp`: a world is the subtree `worlds.<id>`; `fu_world_create`, `fu_world_delete`, `fu_world_enter` / `fu_world_entered` (`(Entered, world)` on the `viewer` entity), `fu_world_query_json` (returns only entities inside the world), `fu_world_run_script` (evaluated under the world). Two worlds with the same names are tested: a query or a script in one does not reach the other. **Product:** a gallery saved world now lives under `worlds.<id>` (the private root `<id>Content` and the three managed-script families are gone), and the AI tools act inside the entered world: `query`, `inspect_entity`, `observe_over_time` and `apply_script` are scoped, `apply_script` returns line and column on an error and writes no module, and `define_kind` / `undo_kind` are new. |
| 3.4 | Done for gallery worlds; the experience has its own version 2 (provisional). | **Engine:** world document, format 2: the texts of the world's sources, then one record per entity with its kinds and only what differs from them. `fu_world_load_json` builds the document under a separate root and replaces the loaded world only when all of it was accepted: a cut document, another format, an unknown type, a bad value, a source that does not apply and a missing kind are each refused and leave the loaded world untouched (tested). `fu_world_snapshot` / `fu_world_rewind` for Play and Stop. A tag `NotSaved` marks components that are not written (`Presented`, `CapState`, ownership, capability results). **Gallery:** the arrangement file is version 2 and holds each saved world as a format 2 document; a version 1 file is converted on read (and copied to `tree-arrangement.v1.json` before the first version 2 write); restore converts, validates and probe-loads every document before anything changes, and puts the previous arrangement back if a later step throws. The golden version 1 file loads, is not modified, saves as version 2 and reloads equal; a cut document is refused and changes nothing (Gallery suite, now 142 checks). **Experience (provisional):** the file is version 2 = the version 1 keys plus `engine` and `abi`, with a migration chain (`ExperienceMigrations`), one restore path used for the probe session and the live session, a rollback to the previous capture when a load fails after the world was replaced, and `Snapshot` / `Rewind`. It is NOT a native world document: the experience's content is made by managed scripts at the root and about 88 runtime and 120 test call sites address root paths, and that whole path (models, rig, workshop) is replaced in Phase 4. It moves to `worlds.<id>` with that replacement; the version field and the migration chain are there so that step can add 2 to 3. The golden version 1 file loads, is not modified, and a failed load leaves models, sources and states as before (Extended suite). |
| 3.5 | Partly done. | **Retired:** the duplicated clay schema text (`ClayCreation` loads the resource), the gallery's fixed component vocabulary as a live path (kept only as the version 1 reader), the `GalleryContent_` / `GalleryGraph_` / `GalleryWorld_` managed scripts and `GalleryContentOf`, and `RuntimeEdits` modules for anything done inside an entered world. **Not retired, with the reason:** `RuntimeEdits` modules for the root experience (the workshop persists its props this way; goes with the workshop in 4.4, a guard now refuses the 257th source instead of producing a save that cannot load); the `models` registry and the component whitelist (the root experience save and the per-model Unity hosts depend on them; after 4.2 and 4.4); the frozen-schema path (saved experiences still carry their schema modules; safe to drop once the version 2 experience file has been in use, then delete `CapabilitySchemaAdditions`). |

**Product files touched in this phase** (for the session that owns `Assets/`): `Assets/_Features/Gallery/Runtime/GalleryExperienceStore.cs`, `GalleryWorldDocument.cs` (new), `CrystalGalleryInteraction.cs`, `Assets/_Features/Gallery/Resources/Gallery/Runtime/WorldGalleryTree.flecs`, `Assets/_Features/Gallery/Editor/GalleryScenarioSuite.cs`, `Assets/_Features/World/Runtime/RuntimeExperience.cs`, `RuntimeExperienceTools.cs`, `Assets/_Features/World/Editor/RuntimeExperienceVerification.cs`, `Assets/__Common/Agent/Runtime/WorldAgentTools.cs`, `Assets/_Features/Creation/Runtime/ClayCreation.cs`, `Assets/_Adapters/Tests/PlayMode/CapabilityLifecycleTests.cs`; in the capabilities package `CapabilityGraph.cs`, `CapabilityRuntime.cs` (an unscoped capture skips bindings under `worlds.`) and `CapabilitySchema.flecs` (result components tagged `NotSaved`).

**Checks.** Native: 348 C++ checks in a Debug build (`flecs_unity_core_tests`), 52 contract checks against the Release library (new: `sources`, `kinds`, `worlds`, `world-documents`), all smokes, benchmark within its guards. Unity, from a detached checkout in the main folder: the engine side alone at `df3a3fb` passed Run-All and Extended. With the product changes at `6e96536`: Run-All passed (Validator 150, Resources 68, PlayerScripts, EditMode 15, PlayMode 8 with the new tests `KindEditReachesEveryInstanceOnScreen` and `WorldDocumentRoundTripsOnScreen`, Gallery 142) and Extended passed 13 of 14; the one failure was the benchmark guard `spawn_prefab_instance` (0.78 against 0.75), a real cost: an instance a script makes now gets its parts attached, about 8 µs more per instance with one part. The guard was raised with that reason. The final state at `18fcbf8`: Run-All passed with the same counts and Extended passed 14 of 14. The Editor logs had no Flecs error and both golden save files stayed byte-identical. Not covered: a built player, Android on a device, and the AI tools driven by a real model.

**Measured** (Release library, best of three processes; empty tick 24.5 µs):

| What | Time |
|---|---|
| Apply a kind's text with one changed value, 1,000 instances | 1.3 ms |
| Apply a text that adds or removes one part, 1,000 instances | 10.8 ms |
| `fu_spawn`, kind with three parts | 39 µs per instance |
| Save a world with 1,000 instances | 12.9 ms |
| Load that document | 74 ms |

The 1 ms target for a value edit (section 3.3) is not met with 1,000 instances; see the next paragraph for why. All five have guards in `Verification/budgets/engine-budgets.json`.

**Changed against the plan, with the reasons.**

- **Built-in components are copied to instances, not inherited.** The plan had `Position`, `Scale`, `Color` and the others inherited through `IsA`. That was built first and it worked (an inherited change was re-sent to Unity), but it exposed a defect in the pinned Flecs revision: a cached query that matches an entity through `IsA` can keep a stale entry when that entity's table is deleted. Deleting one of two worlds whose instances inherited their position hit an internal assert and then crashed. Every cached query on an inherited component would be exposed, also the ones features and tools create. So instances keep their own copies, as before this phase, and the source manager gives a kind's changed value to every instance that still holds the old value; an instance that changed its value keeps it. A write to a kind through the bridge does the same. Consequences: a value edit costs about 1.3 µs per instance; an instance value that happens to equal the kind's follows the kind; existing systems, queries and JSON readers see no change. A component that content declares with `(OnInstantiate, Inherit)` is still read from the kind. Documents write only what differs either way. Check again when Flecs is updated.
- **Kinds are always stored with `@tree ChildOf`.** Flecs stores a prefab's children with its newer `Parent` storage by default. With it, a prefab's children cannot change once it was instantiated (Debug builds abort, Release builds keep instantiating the old set), and instance parts get no names. The manager adds the annotation to a text that has none and refuses `@tree Parent`.
- **Evaluating under a scope.** Flecs Script does not use the world's current scope, and a base kind in `Kind : Base` is looked up in the root only. The manager wraps the text in its scope and names the scope with `using`; reported lines are mapped back to the author's text.
- **Not expressible in a document:** that an instance removed something its kind has. It comes back on load.
- **One snapshot per world.** Play and a per-edit undo cannot both use it; Phase 5 needs its own history for edits.
- **Rewind is a reload.** Entities get new ids, so Unity rebuilds its objects for that world. Restoring in place is left for Phase 6.
- **Scoped scripts isolate writes, not reads.** A script in one world cannot create or change entities of another, but it can name them by full path in a pair or a value.

**Flecs facts found here** (also in the shared notes): `ecs_get_target(e, IsA, i)` continues into the kinds of e's kinds; two children must never share a name, even briefly (deleting one leaves the other unreachable by name); a query that combines a fixed term with an invalid expression asserts, so the expression is validated alone first.

### Phase 4: runtimes for the concepts

Each is a native module, an adapter and its tests. They can run one at a time after their prerequisites; Look needs only Phases 0–1.

| Step | What | Prerequisite | Check | Effort |
|---|---|---|---|---|
| 4.1 | **Look**: native component, own Realistic and Toon shaders, manifest, material cache, pattern generator; optional Amplify recipes; retire `CapMaterial`. | 1.1, 1.2 | Screenshot test per style; equal looks share a material; a build without the pack loads a save that names a recipe. | L, **F** (shader authoring needs the user's eye) |
| 4.2 | **Motion**: loops, tweens and `MotionOffset` on parts natively; the walking actor becomes the Motion capability with a typed rig. | 2.2, 2.3 | Python test of loop shapes against the feel values; actor verifier without the gait check. | L, **F** |
| 4.3 | **Places and Body**: force resolution with `up` traversal, `EffectiveForce`, area triggers re-parent through events; the PhysX adapter applies forces. | 2.4, 3.3 | The fish, rock, ship and drifting cases from the elements concept as a test. | L, **F** |
| 4.4 | **Links, rules and Play**: events, `Rule` entities, dispatch, counts, timers, sensors, trees for waiting chains; legacy workshop removed. | 3.1–3.4, 4.2, 4.3 | The coin game of the links concept runs headless in Python and in a PlayMode test. | XL, **F** |
| 4.5 | **Effects**: one effect element with a particle prefab; VFX adapter. | 4.1, 4.3 | Editing the particle look changes all particles; rain stops at ground and water. | M–L, **F→S** |
| 4.6 | **Sound**: `Sound` and `SoundClip`, offline shaping in the native audio layer. | 1.1 | Render cache keyed by values; audio callback test. | L, **F** |

**Phase 4 status (2026-11-14).** Started on the user's go. Every step 4.1 to 4.6 has a first slice: the native module, a Unity adapter where one is needed, and tests. What each step still lacks is listed in its row; most of it needs the concept decisions or the user's eye. Both follow concept notes that are not final (`docs/creation/look.md`, `world-style.md` and `motion-sound.md` on the concept branches), so every member name below is provisional: a rename is a change in one native file, the adapter's mirror and a document migration. All new built-in names are at the root (`Look`, `Free`, `Gravity`, `Rule`, `Count`, `Sound` and the others): a content script that declares a type of the same name is refused by the type guard with a message that names it.

| Step | State | What exists, and what is open |
|---|---|---|
| 4.1 Look | First slice done. | **Native** (`Native/Core/src/Look.cpp`): `Look { colour, pattern {colour, shape, random, size}, material {polish, metal, clear, glow} }` and `WorldStyle { hand, chunk, ink, warmth, vivid, air, glow }` on the world entity, with ranges and a brief on every member. `WorldStyle` follows the newer world-style note: seven values and no list of preset styles, so there is one look shader for every world and the style is post-processing. This replaces "one shader per world style" from section 3.6 and decision 4. A kind's `Look` reaches its instances like any value, and a world's style is in its document. **Unity** (`Packages/com.runtime.capabilities/Runtime/Look/`): materials are instances of two committed URP Lit templates (opaque and clear), cached and shared by quantised look values and reference-counted, with no property blocks; a CPU pattern generator makes an albedo and a normal map per pattern, cached the same way; `LookPresenter` takes over an element's appearance (the `AppearanceOwner` relationship of Phase 2) and gives it back when the `Look` goes; `WorldStylePresenter` writes mood, glow and vignette into a URP volume for the entered world; `LookManifest.json` holds the shader, property names and mapping constants. **Open:** our own shader with the user's eye (URP Lit is the stand-in; the pattern constants and the glass are first guesses); `hand`, `chunk` and `ink` are stored but not drawn (they need a full-screen pass); shader variants for patterned and glass looks can be stripped from a player build until a variant collection lists them; optional Amplify recipes; `CapMaterial` is not retired (content and saves still use it; it needs a migration to `Look`). |
| 4.2 Motion | First slice done. | **Native** (`Native/Core/src/Motion.cpp`): `MotionLoop { snap, bounce, period, phase }` on an element or one part and `SecondPose { move, turn, stretch }` on each part that moves. A target curve shaped by snap is followed by a spring whose damping comes from bounce; the module writes `MotionOffset` on the parts, and the view stream sends a part's rest pose plus the offset, so the authored `Position`, `Rotation` and `Scale` stay as they are and no bridge or C# change was needed. The concept's shapes are tests: breathe stays between rest and the second pose, wobble overshoots both ends, tick-tock holds at the ends, a later phase moves against an earlier one, and ten or a hundred frames a second give the same loop. 256 moving parts cost about 95 µs per tick; a world without loops pays nothing (both guarded). **Open:** `Tween` (one-shot, for reactions; it belongs with links in 4.4), `MotionPath` (Act), a per-part delay and the Wander value from the concept's open decisions, and the walking actor as a Motion capability with a typed rig (it still runs through `DioramaRuntime`). |
| 4.3 Places and Body | First slice done. | **Native** (`Native/Core/src/Places.cpp`): `Free {density}`, the tags `Pinned` and `Ghost`, and the forces `Gravity`, `Float`, `Wind`, `Drag` on places. A free body takes each force from the nearest place above it in the hierarchy; the result is `EffectiveForce` on the body, resolved only when the body is created or re-parented or a force above it changes (1,000 idle bodies cost nothing per tick; a gravity edit that reaches 1,000 bodies about 0.33 ms). The physics adapter reports a body crossing a ghost's edge as the events `PlaceEntered` and `PlaceLeft`; the module then puts the body in the innermost ghost it is inside (deepest, then smallest) and writes its position in the new place's space, so it stays where it is in the world, also for a turned and scaled place. The concept's cases are tests: the fish stays, the rock sinks, the ship rises, a body in an empty world drifts. This is done without a cached query that walks up the hierarchy (the plan's `up` traversal): observers note what changed and one system resolves it. **Unity** (`Packages/com.runtime.capabilities/Runtime/Bodies/`): colliders for `Pinned`, trigger volumes for `Ghost`, a rigid body per `Free` element, forces from `EffectiveForce` (gravity, wind, lift from the submerged part, drag), crossings sent as events, and poses written back to Flecs. A physics body's view is kept out of the view hierarchy (`FlecsWorld.SetDetached`), because a rigid body must not sit under a scaled or moving parent. New bridge call `fu_remove_component`. Seven deterministic PlayMode tests (scripted physics steps). **Open:** a place is sized with `Scale`, which also scales everything inside it, so every child needs compensating numbers: a size of its own (the concept's `Shape`) is needed before tools build places; line and point forces; the existing `CapBody`, `CapWater` and `CapBuoyancy` capabilities are not retired; collider shapes are box, sphere and capsule only. |
| 4.4 Links, rules and Play | First slice done. | **Native** (`Native/Core/src/Links.cpp`): a rule is one sentence stored as data, `Rule { when, that, count, n, seconds, thens[6] }` with `(RuleOf, owner)`; the owner is a kind (every instance acts alike) or one element; a `Then` is `{ verb, target, kind, count, by }`; counts are elements with `Count { start, value }`. Rules run only in a world that plays: `fu_world_play` takes a snapshot, sets every count to its start and adds `Playing`; `fu_world_stop` rewinds. What is noticed arrives as an event (`Touched`, `Tapped` from Unity) or a change (a count, a switch); an observer notes it and one system matches the rules and runs their thens; removals happen at the end of that pass, and a removed shape hands the bodies inside it to its parent. Running in this slice: at start, touches, you tap, every N s, reaches N, is on, is off; removes, counts, switches on and off, makes, grows, shrinks, plays, you win, you lose. The coin game of the concept is a native test and a contract check: it plays, wins, loses and rewinds, and two worlds with the same game do not affect each other. A playing world with no event costs about a fifth of an empty tick more; a world that does not play costs nothing. **Unity:** the physics adapter reports each beginning contact once per pair as `Touched` (again only after 0.3 s apart); `PlayWorld` / `TryStopWorld` in the bridge; PlayMode tests play the coin game with real physics and check that Stop puts every object back. **Open:** is near, sees, hears (they need sensors); the moving verbs only set a pair (`GoesTo`, `Follows`, `Avoids`) because steering is not built; carries, pushes, says do nothing yet; sentences that wait (a behaviour tree per rule); input (`Tapped`, `IsYou`) has no adapter; no editor for links; **the legacy workshop is not removed** (its replacement, the links editor, is product work). |
| 4.5 Effects | First slice done. | **Native:** `Particles { rate, life }` on a shape makes it an emitter; its prefab child `particle` is one particle whose `Look` and `Scale` are every particle's; the emitter gets an `EffectiveForce` from the places it is in (and from itself), resolved like a body's. **Unity** (`Packages/com.runtime.capabilities/Runtime/Effects/`): one particle system per emitter, never a GameObject per particle; all particles share one material from the look cache, so two emitters and an ordinary element with the same look batch together and an edit of the particle's look reaches every particle; forces from `EffectiveForce`; particles end at pinned shapes (world collision) and at the surface of a fluid volume. Six PlayMode tests. **Open:** the fluid-surface end has no test; an instance of a kind gets a non-prefab `particle` child, whose later changes the adapter does not watch; selecting and editing one particle; VFX Graph for large counts. |
| 4.6 Sound | First slice done. | **Native** (`Native/Core/src/Sound.cpp`): `Sound { clip, size, rough }` on an element and `SoundClip { from, uri, seconds }` on a clip element; the samples are never in the world. `fu_sound_shape` is the shaping: one granular shift moves pitch and size together and keeps the length (about an octave and a half each way at the ends), then a filter (smoother) or growl, drive and breath noise (rougher). It is a pure function with no world access, so it runs on a worker thread, and the same values always give the same output, so a render is cached by (clip, size, rough). Measured in tests: the pitch at +1, +0.5 and -1, the length, loudness kept, no clipping, hiss removed by smooth. The rule verb plays adds one to `Played` on the element. **Unity** (`Packages/com.runtime.capabilities/Runtime/Sound/`): sources by clip name (an in-memory registry and 16-bit WAV files in the app's sounds folder, names only, never paths), renders cached and shaped off the main thread, one audio source per element, played when `Played` rises. **Open:** recording from the microphone, the instrument bank as a clip source, previews while dragging, the loudness envelope for mouth and body, the "audio callback" check of the plan (this design plays pre-rendered clips and has no callback); the existing `CapAudio` synthesizer path is untouched. |

**Checks so far.** Native: 487 C++ checks (new files `look_tests.cpp`, `motion_tests.cpp`, `places_tests.cpp`, `links_tests.cpp`, `sound_tests.cpp`), contract checks `look`, `motion`, `places` and `links`, benchmark within its guards. Unity at `ca444e3` from a detached checkout in the main folder: Run-All passed (Validator 150, Resources 68, PlayerScripts, EditMode 22, PlayMode 43 with the look, motion, body, link, effect and sound tests, Gallery 142) and Extended passed 14 of 14; the Editor log had no Flecs error, Unity rewrote no tracked file, and both golden save files stayed byte-identical.  Screenshots of a row of looks are written by a PlayMode test to `Logs/Look/` for a person to judge; nothing asserts on pixels.

**Phase 3 leftovers closed in this pass:** the experience file is version 3 and no longer stores the schema and library modules the app ships (`ClayBrushSchema`, `PoseSchema`, `EngineSchema`, `RuntimeLibrary`, `ActorSchema`, `MotorLibrary`, `CapabilitySchema`), so a loaded save gets the current texts; the `CapabilitySchemaAdditions` branch is deleted; the other docs are synced.

### Phase 5: AI contract

| Step | What | Check | Effort |
|---|---|---|---|
| 5.1 | Tools v2: structured, world-scoped, with `describe`, `list_kinds`, `spawn`, `set`, `undo`; dry run; results carry errors and alerts. | Scripted chat-loop test: build a small scene, break it, read the alert, undo. | L, **F→S** |
| 5.2 | Doc briefs, ranges, units and alerts on every native type; prompt generated from the world. | A test fails when a native member has no brief. | M, **S** |
| 5.3 | Remove legacy tools and the duplicated ChatGPT loop. | Provider verifications. | S, **S** |

**Phase 5 status (2026-11-14).** All three steps are done for an entered world. The root experience (the old workshop) keeps its old tools until it is replaced, which is a product decision.

| Step | State | What exists, and what is open |
|---|---|---|
| 5.1 Tools | Done for entered worlds. | **Native** (`Describe.cpp`, `Worlds.cpp`): `fu_describe_json` (a type with members, nested, ranges, enum values and briefs, or a kind with bases, parts, instances and source), `fu_kinds_json`, `fu_alerts_json`, `fu_world_dry_run` (the script runs against a copy that is thrown away), `fu_world_checkpoint`, `fu_world_undo` and `fu_world_checkpoint_drop` (the last 16 checkpoints of a world, separate from Play's snapshot). `fu_set_component_json` now keeps the members its text leaves out and changes nothing when the text does not fit. **Package** (`Packages/com.runtime.ai-agent`): `AgentToolSchema` checks a call against the tool's JSON schema in the shared tool loop before the tool runs (type, enum, properties, required, no unknown properties, items); every provider passes the schema to its service; the chat can take its instructions and its tool list from the application's state for each request. **Application** (`Assets/__Common/Agent/Runtime/WorldTools.cs`): while a world is entered the AI gets `describe`, `list_kinds`, `query`, `inspect`, `spawn`, `set`, `remove`, `define_kind`, `undo_kind`, `add_rule`, `apply_script`, `play`, `stop`, `undo` and `alerts`. Each acts on that world only, takes and answers paths relative to it, refuses a path outside it, and answers one object: `ok`, its fields, `error` (with line and column when known) and the world's alerts. `set` and the values of `spawn` are checked against the type's description first, so a wrong member, type or range is answered with what would be right. `apply_script` and `add_rule` run a dry run first. Every change keeps a checkpoint; a change the engine refused whole drops it and leaves the entities as they are, one that failed part way undoes to it. **Open:** `undo` reloads the world, so every entity gets a new id and its Unity objects are rebuilt; tags and relationships are added through `apply_script`, not `set`; the photo flow has no tool inside a world; nothing was tried with a real model (whether it uses `spawn` and `add_rule` well, reads the alerts, and whether ChatGPT in non-strict mode and the llama.cpp grammar accept the schemas). |
| 5.2 Briefs and prompt | Done. | Every native type, member, tag, relationship and event has a brief (`document_core` adds them for the types that existed before the plan); `tests/tool_tests.cpp` and the `tools` contract check fail when one is missing. Ranges are on the members that have one. Alerts are Flecs alerts next to the feature: a rule without an owner, a body that is free and pinned, a count outside a world. Inside a world the instructions are five short rules of conduct (`WorldInstructions.txt`) followed by a type text that `WorldTypeText` builds from `Describe` for each request, under 6,000 characters. **Open:** units are in the brief texts, not Flecs units; the list of types in the type text is a fixed list in `WorldTypeText.Types`, so a new type has to be added there; the alerts "instance without a kind" and "link to a missing element" from section 3.8 are not written; the root prompt (`AgentInstructions.txt`) is still hand-written. |
| 5.3 Old tools, one loop | Done as far as the engine goes. | The ChatGPT adapter uses the shared `ToolLoop` (one request id, the same limits, checks and trace records as the others). `define_kind`, `undo_kind` and the entered-world branches of the old `query`, `inspect_entity`, `apply_script` and observe tools are removed; their checks moved to `AgentToolsVerification`. **Open:** the old single-string tools of the root experience (`WorldAgentTools`, `RuntimeExperienceTools`) stay until the old workshop is replaced. |

**Costs (desktop, release build).** Describe a type about 60 µs; a checkpoint about 13 ms and a dry run about 96 ms for a world of 1,000 instances; the alerts add nothing measurable to an idle tick. All are guarded in the benchmark.

**Checks.** Native: 522 C++ checks (`tool_tests.cpp` is new), 59 contract checks, benchmark within its guards. Unity at `116c96f` from a detached checkout in the main folder: Run-All passed (Validator 150, Resources 68, PlayerScripts, EditMode 22, PlayMode 43, Gallery 142) and Extended passed 15 of 15, with the new verifier "Agent tools and schemas" (a scripted chat through the real tool loop: build a small scene, break it, read the alert, undo). The Editor log had no Flecs error, Unity rewrote no tracked file and both golden save files stayed byte-identical. The run on the head with step 5.3 is recorded below.

### Phase 6: scale and platforms

| Step | What | Check | Effort |
|---|---|---|---|
| 6.1 | Device baseline on the Android test phone; release native configuration (decision 5). | Budgets recorded for the phone. | M, **F→S** |
| 6.2 | Instanced drawing for repeated simple things, only if 6.1 or a desktop profile shows GameObject cost. | Before and after profile. | L, **F** |
| 6.3 | Gallery behaviour moved into native systems where it is still mirrored C# (ring assignment first). | Gallery suite. | L, **F→S** |

**Order.** 0 → 1 → 4.1 (Look) alongside 2 → 3 → 4.2–4.6 → 5 → 6. Creation-tools delivery step 1 (arcs, seed, handles; C# only) does not depend on this plan and can proceed in parallel; its steps 2–4 should wait for Phase 3 so wires, volumes and shells are not built twice.

---

## 5. Decisions for the user

Recommended option first in each. **Decided by the user on 2026-11-13: the recommended option in all ten (marked).**

Changed by the user on 2026-11-13: do not ask Sander Mertens. We build our own re-attach step (decision 2, option A); if a later Flecs version updates a script's entities in place, we move to that version and remove ours.

**1. Where do a world's things live?**
- [x] **A. Kinds are Script, instances are data holding only overrides** (recommended). Ids stay stable while editing, edits cost microseconds, saves hold differences.
- [ ] B. Everything is Script, one managed script per element. Fully readable sources; every committed edit recreates that element and its Unity objects.
- [ ] C. Keep today's model (large scripts plus a state overlay). No work now; kinds and fast edits stay blocked.

**2. How do kinds survive a reload?**
- [x] **A. A native source manager keeps kinds and their instances intact when a kind changes** (recommended). After the step 0.3 spike it does this by applying the changed source in place, not by reloading and re-attaching. It is removed if a later Flecs version does this itself.
- [ ] B. Never reload for value edits: write the world live and keep the source text in our own store. Faster, but text and world can drift.
- [ ] C. Wait for an upstream answer before building kinds.

**3. Where are engine contracts defined?**
- [x] **A. In native C++ modules** (capability, look, motion, body, place, rule), with Script reserved for content (recommended).
- [ ] B. Keep them as Script schemas. Easier to change today; keeps JSON-only reads and frozen save schemas.

**4. Look shaders**
- [x] **A. Our own small set authored in Amplify Shader Editor, one per world style with the same inputs; the Shader Pack as optional named recipes** (recommended).
- [ ] B. Pack shaders as the base, with a mapping per shader. More looks sooner; values do not carry across styles and the pack becomes a hard dependency.
- [ ] C. A generic property bag on any catalogued shader. Most flexible; nothing stable for tools or the AI.

**4b. Which styles first?**
- [x] **Realistic and Toon** (recommended)
- [ ] Realistic, Toon and Clay
- [ ] All six from the concept

**5. Release native build**
- [x] **A. Strip REST and HTTP from release; keep soft asserts until their cost is measured** (recommended; AI-written Script should not be able to crash the player).
- [ ] B. Strip REST, HTTP and asserts.
- [ ] C. Leave as is.

**6. Order of work**
- [x] **A. Safety net and native foundation, then Look as the first full slice, then sources, kinds and saves, then the other runtimes** (recommended).
- [ ] B. Foundation through saves first (Phases 0–3), then all runtimes. Cleanest; nothing visible for longer.
- [ ] C. Build the concept features on today's architecture and refactor later. Fastest to a demo; most of it is rewritten.

**7. Native tests**
- [x] **A. Keep Python contract tests as the main net and add a small C++ test target with a sanitizer build for module logic** (recommended).
- [ ] B. Python only.

**8. Rotation in the transform contract (ABI 4)**
- [x] **A. Quaternion in the contract, Euler accepted in Script as authoring input** (recommended; 3D turning for every element is in the handles concept).
- [ ] B. Keep Euler degrees.

**9. Developer traces**
- [x] **A. Make them opt-in, as AGENTS.md says** (recommended).
- [ ] B. Keep them on by default in the Editor and change the AGENTS.md wording.

**10. Saves**
- [x] **A. One world document for gallery worlds and experiences, with migration from both current formats** (recommended).
- [ ] B. Keep two formats and add migration to each.

## Not verified in this review

- No Unity Play profile was taken; C# costs of `SyncViews` and capability ticks are from code reading and the audit's numbers.
- The cost of `FLECS_KEEP_ASSERT` in release was not measured.
- The Amplify licence reading and the SRP Batcher statement are general knowledge, not tested in this project; pack shaders were inspected from the extracted package, not compiled.
- Android behaviour is untested (the library has not run on a device).
- The replay of one-shot effects on load (C8) and the unloadable save after many AI edits (A1) follow from the code but were not reproduced.
