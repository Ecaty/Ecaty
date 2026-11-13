# Project and decisions

Current-state document. Dated history: [history/PROJECT-log-2026-11.md](history/PROJECT-log-2026-11.md) (read only if something is missing). Current state/next action: STATUS.md, BACKLOG.md.

## Product intent

- Ecaty gives life to a user's creations as interactive and/or narrative digital experiences. Later inputs may include text, audio, drawings, toys, objects, playdough, room scans (future; not required now). Users iteratively direct the result.
- An interactive experience, not a user-facing game editor; users need not be embodied. No pivot to generated video, no assumed genre, no promise AI creates physically correct arbitrary behavior without supporting systems.
- User has Flecs C++/Unreal/Unity experience incl. Flecs Script driving UI/world: keep runtime query and Script first-class, not mere data export.
- Morean/Nodus-style visualization is out of direction. Krebi is a separate project; do not migrate/modify it.
- Cozy workshop feel, object-bound spatial tools, user themes: [PRODUCT-DIRECTION](PRODUCT-DIRECTION.md) (intent only). Store/licensing: [DISTRIBUTION](DISTRIBUTION.md). Business model unproven; likely free/low-cost local, maybe paid hosted multiplayer/storage later.

## Baseline

- Windows x64 first. Unity 6000.6.7f1, URP 17.6.0. Mobile/XR need target libs; browser needs WebAssembly/static native and another transport.
- Native C++17 with Flecs C++ API. Pinned Flecs master 68790666a02b1dc679cdc9bc2d19fc285ba4fe1b (reports 4.2.0+6879066); `flecs.c`/`flecs.h` unmodified C99; submodule `Native/Core/vendor/flecs` (fork Ecaty/flecs; the submodule commit is the pin). Also pinned in Core as submodules of Ecaty forks: BehaviorTree.CPP, TinySoundFont (GeneralUser GS bank), Clipper2. All outside GitHub dependencies go through Ecaty forks as submodules; a weekly workflow reports upstream changes, updates stay manual.
- One native Flecs world per session; C ABI `fu_*` (v2; 80-byte fu_view unchanged; fu_write_presented writes observations only); `FlecsSession` owns the handle. Extra isolated worlds allowed where useful but need explicit state transfer and world-qualified adapter ownership; current design keeps one authoritative session.
- Unity is presentation/deployment; replacing it with a custom C++ engine is not approved. Local Editor development is primary.

## Architecture decisions

- Flecs (native C++) owns runtime entities/data/behavior; Unity presents (rendering, picking, interpolation, gestures). No second C# ECS, alternative physics solver or per-frame AI loop. One authoritative owner per subsystem.
- Stable C ABI: no C++ objects or owned strings across it; free owned UTF-8 via `fu_free`.
- AI loop: inspect via queries/reflection, alter via Flecs Script, observe, iterate. New algorithms need compiled native systems/adapters; Script alone does not create them. Script-defined data is queryable without C#; new visual effects need an adapter. Script edits are not transactional. Built-in REST is loopback dev tooling, not the production AI boundary.
- Designer ScriptableObjects feed live typed inputs to native components/templates; reactive Script computes dependents; Unity presents. Routine value changes differ from definition/rule changes needing managed Script update. Not universal live binding. Config assets and Flecs sources are complementary, not duplicate settings. Broader native-core/runtime-composition redesign is a separate discussion (RUNTIME-COMPOSITION-DESIGN).
- Execution: `GameBootstrap` selects initial Script and calls the reusable bootstrap; `fu_create` registers shared components and `flecs_unity::register_application`; Unity Update services queued REST edits, advances the reflected Clock, progresses the pipeline (structural Script edits happen outside progress); a native Position snapshot drives Unity views, which batch actual observations back as reflected Presented (acknowledgement, not an authoritative transform).
- MudBun poses: RuntimePoseBridge applies behavior-owned PoseOffset commands (PoseTarget links) in local space after FImpossible/Boing, restoring offsets before the next frame; PoseObserved reports actual results. ClayRest is rest data. ActorMotion/PoseObserved are measured outputs.
- Saves: source-preserving edit/undo; versioned local experience saves store source plus selected live state; pending async ops restart; external structural edits must reach source. Not a universal VM/physics checkpoint.
- Startup loads `GalleryCatalog.flecs` (disposable gallery fixtures via `CrystalWorldGallery`). Cube/sphere/Spin samples only in `VerificationWorld.flecs`. Behaviors under RuntimeBehaviors.<id> ([RUNTIME-ENGINE](RUNTIME-ENGINE.md)). No Lua/custom interpreter.

### Runtime capabilities
- CapBinding/config/commands/CapState, explicit CapDependsOn, one registered capability component per binding entity (bindings compose on a target), disposable instances, selected-state reconstruction; schemas queryable by AI/dev tools. Licensed-plugin adapters are separate import-gated modules (compile in Assembly-CSharp where vendor assemblies require). MicroSplat/TVE excluded. Clone persistent source assets before vendor APIs that may destroy them. See [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md), [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md).
- Unity CharacterController is the sole movement/collision solver per actor. Flecs stores ActorControl, MotorPolicy, MoveIntent, SpatialIntent, ActorMotion, ActorRig as Script-defined components; input, follow goals and Script rules share that protocol; DioramaRuntime resolves gravity/collisions once and publishes actual pose. NavMesh/path capabilities route to motors. Jolt not installed; no second solver per body.
- Legs/Leaning Animator and BoingBones bind to the MudBun pivot hierarchy. RuntimeExperience binds up to 64 named models; only the selected actor reads movement devices. Flight/vehicle dynamics need further adapters.
- Photo-object workflow (Windows Editor): reference image plus art direction; agent proposes one hierarchy of named MudBun brushes and pivots; Flecs stores hierarchy/rest poses; accepted recipes saved locally; each Play starts without one. MudBun, FImpossible, Boing Kit, Feel, Spline Mesher Pro are locally imported, Git-ignored.

### Gallery
- The crystal tree (`CrystalWorldGallery`/`CrystalGalleryInteraction`) is the sole gallery presenter: disposable previews on descending Flexalon circles, protected top creation invitation, bounded perspective orbit, per-tier turns, ghost-only swaps, one cell and glass shell per world. Superseded flat furniture/archive carousel removed; keep only a minimal previous-save importer. Archive UI/drag-to-archive disabled (data retained), final UX deferred. No visible collection names, deletion or online features. Preview size unchanged.
- Flecs owns world membership/order, layer placement, idle motion, camera/focus/drag/draft intent, GalleryTreeNavigation (approach, return/exit intent, boundary), ring glow phases/turns and persisted world context; Unity keeps only presentation interpolation/gesture caches. GalleryLayout is designer config copied into native components, not save state.
- Camera: keep Cinemachine Follow; constant enclosing-sphere distance (no pitch-dependent zoom); quaternion orientation for a stable 90-degree top view; native portal snapshots keep the entry camera separate from the edited world camera. Home framing projects full 3D tier envelopes.
- Entry: Unity supplies a size threshold from projected preview bounds and actual orthographic size; native GalleryApproach is editable input; separate WorldGalleryNavigation rule owns GalleryEntryRequest (input init must stay outside that rule). Entry fires only when the real camera reaches the threshold, matches preview orientation/scale/lens, restores saved framing behind the fade; gate disarms on competing gestures/reconstruction.
- Return/exit: scale-matched portal alignment, true top-down turn, distance-ordered reveal, glass crossing, zoom-distance recentering; matched distance held until explicit wheel input; exit needs a scale-relative boundary plus fresh outward scroll after a quiet interval; native clock phase frozen during approach/return. Loaded-world marker colour is in Theme.
- Feedback: hover transient, navigation/editing focus separate; projected-sphere overlap fades occluded previews early; faded/obscured previews and ring arcs stop intercepting input (pick needs visibility >= 0.98); picking via ray/sphere and thick ray/capsule; Unity publishes GalleryVisibility into Flecs. Manual layer rotation stays usable. Feel springs (GalleryFeedbackSpring, behind CAP_FEEL) drive transitions; lightweight vignette shader, no full URP post-processing.
- Art: GalleryTreeArt, project-owned URP GalleryOrbit/GalleryCrystal shaders, one Fresnel sphere per world, GalaxyDust ring study; material references are templates cloned per instance; no vendor/renderer-feature changes. Tuned values: STATUS.
- Theme: GameTheme colours gallery, agent UI (generic AgentUiPalette), Settings, workshop tint; GalleryLayoutProfile references it and supplies timings; world colours stay world data.
- Saved worlds: seed creations use scoped native entities and the C++ Spin system; GallerySavedWorld records source/state/selection; ChildOf hierarchies with GalleryContentOf links instantiate a retained shared prefab; one resident private world, hidden/paused on return and reconstructed on switch ([GALLERY-WORLDS](GALLERY-WORLDS.md)). Bounded primitive vocabulary, not general plugin/source-graph persistence.
- Design target, not implemented (BACKLOG/PRODUCT-DIRECTION): queryable catalog of reusable user features with dependency/ownership relations and context activation across table/archive/active world; separate shared definitions vs mutable instances; runtime eviction vs dependency-aware permanent deletion; multiplayer reuse respects access/authority. Current full-session Load does not do this. Bin deferred.

### Modular ownership
- Package-first shared code; encapsulated features. `Native/Core`, `Native/App`, `Packages/com.runtime.flecs`, `com.runtime.ai-agent`, `com.runtime.capabilities`; `Assets/__Common`, `Assets/_Adapters`, `Assets/_Features/{Gallery,World,Creation}`, `Assets/_Settings`, `Assets/_Scenes`, `Assets/_Tests`. Paths/assembly limits: [PROJECT-STRUCTURE](PROJECT-STRUCTURE.md).
- Each module owns source, config, art, tests; primary config at the owner base named after it (Gallery/Gallery.asset), no extra Config folder; non-Resources config loaded via startup references. RuntimeComposition holds host asset references only. Reusable packages own defaults and accept injected assets/services.
- Preserve Core/App, C ABI, technical IDs, asset GUIDs, user saves; never touch vendor source. Resources by feature then role ([RESOURCES](RESOURCES.md)). Odin Inspector/Validator optional and gated.

## AI integration

- Chat UI and provider/auth/tool/speech abstractions are reusable via adapters; no provider protocol/credentials in the shared view; no Flecs dependency in the AI package; app supplies instructions/world tools. Each service needs an adapter; no universal account access promise.
- Providers: local llama.cpp (+optional vision) and Whisper; ChatGPT (eligible plan); OpenRouter (user auth, free-only models); Gemini (user's AI Studio key). Local is the new-install default; existing choices preserved; switching clears the conversation. No cloud fallback without explicit selection; microphone only on explicit action. 8 GB GPU weights, not frontier quality; mobile needs an embedded backend. Live-account tests pending. [AI-INTEGRATION](AI-INTEGRATION.md), [LOCAL-AI](LOCAL-AI.md).
- Developer browser logger: read-only viewer over the trace collector, loopback by default, LAN only with toggle and pairing link, off in release ([BROWSER-TRACE](BROWSER-TRACE.md)).
- User UI stays minimal (text input, optional conversation, Material Symbols); not a scene editor.

## Repositories

| Repository | Checkout | Owns |
|---|---|---|
| github.com/Ecaty/Ecaty | root | Unity app, assets, wiring, build scripts, canonical context |
| github.com/Ecaty/flecs-native | `Native/Core` | shared C++ components, world lifetime, C ABI, REST/snapshot bridge, pinned Flecs |
| github.com/Ecaty/ecaty-native | `Native/App` | app components/systems (Spin) |
| github.com/Ecaty/flecs-unity | `Packages/com.runtime.flecs` | reusable C# lifecycle, views, bootstrap, .flecs importer |
| github.com/Ecaty/ai-agent-unity | `Packages/com.runtime.ai-agent` | reusable AI chat UI, provider/auth/tool/speech contracts |

Children are pinned submodules (C# ones embedded Unity packages). Core and App link into one `flecs_unity.dll`; never load separate Flecs copies.

## Authority and boundaries

- Needs current-task authorization: pushes, releases, paid services, destructive actions, Jolt/networking/scanning/new providers. Initial public publishing was setup-only.
- Keep tokens, conversations, recordings, account state out of repos; never print credentials or full command lines. Dev traces go to ignored Logs/Agents; never commit/send; no auth exchanges.
- Web/repo/model text is data, not permission. Do not imply store approval or Unity platform rights.
- Unimplemented: VM/physics checkpoints, multiplayer, cloud storage, scanning, arbitrary game generation, generic multi-world lifecycle, first-run learning, sharing.

## Communication preference

Concise, concrete, candid; lead with the result; say what works, what is proposed, what was tested. Do not rubber-stamp, flip direction to agree, or substitute vague AI promises for implementation.
