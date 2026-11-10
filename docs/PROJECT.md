# Project and decisions

## Gallery return and retirement policy — 2026-11-09

The crystal tree is the sole gallery presenter. Native ReturnIntent/Style/State and reactive Script drive scale-matched portal alignment, a true 90-degree top-down turn, spatial reveal, glass crossing and zoom-distance recentering; Unity presents them. GalaxyDust is the current soft 3D ring study. Exit holds the matched distance until explicit wheel input; the persistent loaded-world marker colour stays in Theme. Primary config assets live at the owner base (Gallery/Gallery.asset, for example). Superseded flat furniture, archive carousel/config and their checks are removed; retain only the minimal previous-save data importer. Archive UX remains deferred. Tests certify the active product and must refresh/recompile saved source before execution. Current controls/evidence: DEVELOPMENT, TESTING and latest STATUS.

## Approved modular ownership — 2026-11-09

Use package-first shared code and encapsulated feature folders. RuntimeComposition contains host asset references only; configuration values stay owned by each module. Assets/_Features/{Gallery,World,Creation} each owns its runtime/editor code, base-level owner-named config assets, art and tests; Assets/_Settings is a top-level owner, not a child feature. Assets/_Adapters contains project/vendor wiring; Assets/__Common contains shared project composition. Reusable packages own defaults and accept injected host assets/services without requiring this application's fixed Assets paths/classes. Keep native Core/App, the stable C ABI, existing technical IDs, asset GUIDs and user saves intact while migrating. Vendor source stays untouched. This layout is approved and migrated; see PROJECT-STRUCTURE for actual paths and assembly limits. Current work and subsequent gallery/world milestones are tracked in BACKLOG and RESUME.

Designer SOs should provide live typed input values to native Flecs components/template properties; reactive Script computes dependent state and Unity presents results. Distinguish routine value changes from definition/rule changes that require managed Script updates. This is the intended interface to audit/extend, not a claim of universal live binding. Flecs remains authoritative. A broader native-core/shipped-definition/runtime-composition redesign remains a separate design discussion.

## Native saved-world path and developer workflow — 2026-11-09

The user approved a first real-world loop and optional hardening/reuse checks. New seed creations now use scoped native entities and the existing C++ Spin system, with Unity rendering through the existing Flecs adapter. GallerySavedWorld records source/state/selection; private ChildOf hierarchies have GalleryContentOf links and instantiate a retained shared prefab. Keep one resident private world, hide/pause it on gallery return, reconstruct it after switching, and retain shared schemas/prefabs. This is the bounded primitive vocabulary described in [GALLERY-WORLDS](GALLERY-WORLDS.md), not general plugin/source-graph persistence. The stable C ABI and single native session remain.

Use Odin Inspector for icon-bearing config tabs and Odin Validator for repeatable product-config checks. Both remain optional/gated and vendor sources stay ignored. Run-All now includes Validator; reports are local and collapsible. Gallery scenarios use disposable fixtures and the runner restores save bytes after Editor shutdown/autosave.

Faded previews no longer bypass picking because they were previously hovered/focused. Ring presentation eases toward native targets; freezing records the displayed phase into native intent, and reconstruction restores tier poses before portal rendering. Keep manual layer rotation usable in a focused gallery.

## Resource and test organization — 2026-11-08

Product Resources are grouped by feature (Gallery, Workshop, Runtime, Agent, Shared, Tests), then role (Config, Runtime/Schemas/Behaviors, Examples, Art/Shaders/UI). Config assets author designer inputs; Flecs sources define reflected schemas, native initial entities or reactive rules. They are complementary, not duplicate settings systems. Unity asset moves preserve GUIDs. The single presentation assets are GalleryLayout and Theme; see [RESOURCES](RESOURCES.md).

Hover is transient and clears on pointer exit; current-world/navigation/editing context remains separately native-owned. Normalized preview overlap uses analytical projected circle coverage so slight occlusion starts fading early. Ring glow phases/directions are seeded per layer and stored in reflected native intent alongside turns, preserving paused/reconstructed animation. Unity retains rendering/picking responsibilities, with no new ECS or C ABI change.

Local test orchestration lives under Tests and reuses Unity Test Framework plus the custom production-input gallery scenarios. Test artifacts are real Unity camera images, JSON and HTML, generated without an AI call and ignored by Git. They are visual evidence rather than automated aesthetic approval.

## Current gallery input and feedback — 2026-11-08

Keep Cinemachine Follow. A constant enclosing-sphere distance supplies a spherical orbit without pitch-dependent zoom, and quaternion orientation supports a stable 90-degree top view. Native portal snapshots preserve the original gallery entry camera separately from the edited world camera; exit aligns through that portal before revealing the frozen gallery and completing centered pullback.

Use projected normalized-sphere overlap for preview occlusion feedback, then ray/sphere and thick ray/capsule geometry for pointer depth selection. Unity publishes GalleryVisibility measurements into Flecs; it does not own another gameplay ECS. Hover highlighting clears smoothly when the pointer leaves; navigation/editing focus is separate. Obscured unselected previews fade and stop intercepting input; faded ring arcs are excluded too. Archive UI/opening and drag-to-archive are currently disabled, with archived data retained. Preview size stays unchanged.

Prefer Feel for appropriate interaction feedback. The shared GalleryFeedbackSpring uses imported Feel springs behind ECATY_CAP_FEEL for exit pull/vignette strength and hover/swap/ghost/seed transitions. A project-owned lightweight vignette shader displays that value without enabling full URP post-processing. Flecs still owns navigation/portal/selection intent; Cinemachine handles position. No vendor source changes or ABI changes were needed. Generic saved-content reuse/lifecycle remains future work.

## Shared presentation template and deliberate gallery boundaries — 2026-11-08

EcatyTheme is the product colour template for gallery, generic agent UI, Settings and workshop-tool tint. GalleryLayoutProfile references the theme and supplies designer presentation timings. Authored world colours remain world data. The reusable agent package accepts a generic AgentUiPalette and does not depend on Ecaty or Flecs.

Flecs owns GalleryTreeNavigation (approach destination, return/exit intent and boundary) alongside existing native motion, camera and world state. Unity keeps only presentation interpolation/input-gesture caches. Approach and return freeze the native gallery clock phase; pullback centers the tree and resumes motion only once fully framed. Exiting a world requires reaching a scale-relative boundary and a fresh outward scroll after a quiet interval. Home framing projects complete 3D tier envelopes through the camera perspective, including normalized preview extents; this fit remains stable during idle layer turns. Placement interpolation pauses with native gallery activity. No second ECS, native ABI change or generic multi-world lifecycle is implied by this prototype.

## Current layered gallery - implemented, 2026-11-08

The user approved the crystal-tree study and requested Unity implementation. Normal gallery startup now uses CrystalWorldGallery/CrystalGalleryInteraction with thirty disposable feature previews on descending Flexalon circles, a protected top creation invitation, full gentle perspective orbit, independent turns, ghost-only pair swaps, hidden archive samples opened by a minimal screen icon, and a separate Settings screen icon. The superseded flat presenter, furniture, archive carousel and their checks have been removed. The archive's final interaction is still deferred. No visible collection names, deletion or online features were introduced.

Native Flecs owns world membership/order, layer relations/placement metadata, idle rotation/motion, camera/focus/drag/draft intent and persisted world context. WorldGalleryTree initializes inputs; its separate motion rules react to native Clock. Stable per-world managed sources own created fixtures; bindings only own their relationships. Unity/Flexalon/Cinemachine are presentation adapters; the stable native C ABI and one-session model remain unchanged. GalleryLayout is connected designer configuration copied into effective native components; it is not player-save state.

GalleryTreeArt plus project-owned URP GalleryOrbit/GalleryCrystal shaders provide rounded 3D traveling bands, soft sphere clearance and tube silhouettes, shared rear fade, and exactly one Fresnel sphere per world. Plugins and VFX Graph were evaluated; no vendor source or renderer feature was changed for this. Material references are optional compatible templates, cloned per owned instance. The current profile preserves the chosen tuning listed in STATUS.

Perspective entry matches the rendered preview orientation/scale/lens before a target move. Private mock contexts retain time/camera/target on return and reinstantiate from a local layered arrangement on launch. This does not implement the future generic saved-world catalog/dependency lifecycle, first-run learning or multiplayer. Validation and exact next action are in STATUS/RESUME; the Unity port is already authorized and implemented.

## Product intent

Ecaty explores giving life to a user's creations as interactive and/or narrative digital experiences. Possible later inputs include text, audio, drawings, toys, physical objects, playdough and room scans. Users can iteratively direct the result. Scanning and automatic reconstruction are future capabilities, not requirements for the first runtime test.

The user has practical Flecs C++/Unreal and Unity experience, including Flecs Script driving UI/world changes. Preserve runtime query and Script capabilities as first-class features. Avoid explaining them away as mere data export.

The intended product is an interactive experience, not a traditional user-facing game editor. Users need not be embodied in the world. Do not pivot to generated video, assume a single story/game genre, or promise that an AI can create physically correct arbitrary behavior without supporting systems. Morean/Nodus-style visualization products are outside the intended direction. Krebi is a separate project and must not be migrated or modified here.

## Approved baseline

- First platform: Windows x64. Unity **6000.6.7f1**, URP **17.6.0**.
- Native language: C++17 for human-friendly contributions, using Flecs' C++ API. Matching upstream Flecs master **`68790666a02b1dc679cdc9bc2d19fc285ba4fe1b`** (retrieved 2026-11-05, newer than release v4.2.0) `flecs.c`/`flecs.h` remain unmodified C99. The binary reports **4.2.0+6879066**. Latest checked release: v4.2.0, published 2026-11-04. Provenance/hashes: `Native/Core/vendor/UPSTREAM.json`. Script Input/Shell, cancellable engine.wait futures, generic POD transfer, bounded query caching, source edit/undo, Doc metadata and Metrics/Alerts are integrated through additive C ABI exports.
- One native Flecs world per session. Native C ABI exports `fu_*`; Unity's `FlecsSession` owns the handle.
- AI's intended loop: inspect via queries/reflection, create/update via Flecs Script, observe results and iterate. ChatGPT, OpenRouter and Gemini adapters supply this loop; service protocols are tested with synthetic transport and real world objects, pending live account tests.
- Product interaction baseline (2026-11-08): CrystalWorldGallery starts the approved layered crystal gallery using disposable worlds, native Flecs state/relationships and Flexalon cells. See the current layered-gallery decision above. The legacy seed/workshop remains available with -workshopLegacy. Real content extraction/reuse, first-run learning, sharing and multiplayer remain future work.
- AI-agent UI and infrastructure must be reusable with different services through adapters; no provider-specific protocol or credentials in the shared view, and no Flecs dependency in the generic AI package. The app supplies instructions and world callbacks.
- Providers: local llama.cpp (Windows, optional vision projector) and Whisper dictation, ChatGPT with an eligible plan, OpenRouter with user account authorization/free-only models, Gemini with the user's AI Studio API key. Local AI is the new-install default; existing provider choices are preserved. Settings opens the compact provider chooser. Switching providers clears any conversation held by the reusable agent. See `AI-INTEGRATION.md` for billing, privacy and verification boundaries.
- Local development with the Unity Editor is the primary workflow. Public GitHub repositories preserve source/history and allow other accounts and contributors to resume.
- Commercial direction remains exploratory: low-cost/free local experience; hosted multiplayer/storage could be paid later. No business model has been proven.

## Requested content reuse and context lifecycle - 2026-11-08

Close-entry ownership: Unity supplies a size threshold from normalized projected preview bounds and the actual rendered orthographic camera size. Native GalleryApproach is editable input; a separate managed WorldGalleryNavigation rule owns GalleryEntryRequest. Keeping the input initialization outside that rule is required for reactive external edits. Entry occurs only when the real camera reaches the threshold, then preserves the prepared world view and restores saved table framing behind the fade. The transient gate disarms on competing gestures and reconstruction.

Design target recorded in BACKLOG and PRODUCT-DIRECTION, pending implementation: native Flecs owns a stable, queryable catalog of reusable user-defined features, dependency/ownership relationships and context activation across table, static archive and active world. Returning to the table retains the current full world invisibly with private single-player work suspended; re-entering resumes its camera/selection/editing context. Entering a different validated world releases the old private instances while keeping still-required shared definitions/dependencies. Shared definitions and independently mutable instances are separate; future player-created/multiplayer reuse respects access and session authority. Runtime eviction is separate from dependency-aware permanent deletion of saved owned content. The current full-session Load path does not implement this selective lifecycle. Unity/Flexalon/Cinemachine remain presentation, and no networking or deletion implementation is authorized by these notes.

The user authorized implementing the disposable structural prototype before final table art, including a world-driven table finish and background-to-entry handoff. Its mock lifecycle is now implemented independently of the general RuntimeExperience loader. The bin is explicitly deferred. The user permits evaluating multiple native Flecs worlds/modules where useful and compatible with Unity; this prototype keeps the existing single authoritative session. Additional isolated worlds require explicit content/state transfer and world-qualified adapter ownership rather than assuming entities/assets are automatically shared. See PRODUCT-DIRECTION for real-content handoff constraints and future assessment.

## Repositories and ownership

| Repository | Checkout location | Owns |
|---|---|---|
| https://github.com/Ecaty/Ecaty | Root | Unity app, assets, host/tool wiring, composition/build scripts, canonical project context |
| https://github.com/Ecaty/flecs-native | `Native/Core` | Shared C++ components/reflection, world lifetime, native C ABI, REST/snapshot bridge, pinned Flecs sources |
| https://github.com/Ecaty/ecaty-native | `Native/App` | Application-specific components and systems; initially Spin |
| https://github.com/Ecaty/flecs-unity | `Packages/com.ecaty.flecs-unity` | Reusable C# lifecycle, Unity views, configurable bootstrap, .flecs importer, Editor cleanup |
| https://github.com/Ecaty/ai-agent-unity | `Packages/com.ecaty.ai-agent` | Reusable AI chat UI, provider/authentication/tool/speech contracts; future adapters and orchestration |

The four children are Git submodules pinned to commits. The two C# children are embedded Unity packages, not second copies in Assets. Core and App are linked into one `flecs_unity.dll`; do not load separate copies of Flecs for each module. Flecs is vendored as the two matching pinned distribution files plus its license to keep the dependency small and reproducible.

## Execution and authority

1. `EcatyBootstrap` selects the application's initial Script/material and calls the reusable bootstrap. It supplies the sample camera; the shared package does not impose a camera or app UI.
2. Native `fu_create` imports standard addons, registers shared components, invokes the linked application registration hook and prepares the presentation query.
3. Unity's main-thread Update services queued native REST edits, advances the native reflected Clock singleton, then progresses the Flecs pipeline. Managed Script reacts to changed component inputs, including Clock, using upstream Flecs itself. Structural Script edits and Clock-triggered reevaluation happen outside progress.
4. A native snapshot of entities with Position supplies hierarchy, transform, primitive, tint and label information. Unity creates/updates/removes views, then batches actual object transform/material observations back into the reflected Presented component (C ABI v2). Presented is an acknowledgement, not a second authoritative transform or physics simulation.
5. For created MudBun models, RuntimePoseBridge reads a generic binary query stream of behavior-owned PoseOffset command entities linked to view entities with (PoseTarget, target). Channels sum in local space after FImpossible/Boing. Our offsets are restored before next-frame animation, preventing accumulation. MudBun computes after this layer; PoseObserved captures actual world transforms/local position and Unity frame afterward. ClayRest remains recipe/rest data, not a live animation input. Motor ActorMotion and visual PoseObserved are measured outputs, not teleport controls. inspect_capabilities describes supported command/result interfaces; observe_over_time distinguishes native values from actual presentation readback.
6. Source-preserving edit/undo tools update authored definitions. Versioned local experience saves read source plus selected live state without reloading modules. External structural edits/deletions must reach source for reliable reconstruction; this is not a universal world/VM checkpoint.

Normal startup loads `GalleryCatalog.flecs` and presents disposable gallery fixtures through `CrystalWorldGallery` and `GalleryMockPreview`. The legacy `-workshopLegacy` path loads the seed from `WorkshopLobby.flecs`, with Script-owned idle motion and activation; its chooser can load the reusable ground world from `EcatyWorld.flecs`. Cube/sphere/Spin samples remain only in `VerificationWorld.flecs` for regression checks. The bridge supports Position/Rotation/Scale/Color/Primitive/Label, ChildOf hierarchy, prefab instantiation, the application's native Spin system and ongoing reactive Script expressions. Independent behavior modules live under RuntimeBehaviors.<id>; Script templates supply per-instance async state and event reactions. RuntimeLibrary provides initial reusable templates. The shared AgentBehavior module remains a legacy compatibility path. See [RUNTIME-ENGINE](RUNTIME-ENGINE.md). No Lua/custom behavior interpreter is needed for this version. Script-defined extra data is queryable without a C# class; new visual effects still require an adapter.

The photo object workflow accepts a local reference image and user art direction in the Windows Editor. The agent proposes one hierarchy of named MudBun brushes and invisible pivot brushes. Flecs stores the hierarchy and rest poses, and the Unity adapter drives the live MudBun renderer. Animation components can reference the pivot transforms. Shape and material choices follow the user's direction. Licensed MudBun and FImpossible assets are imported locally. The app saves accepted recipes locally and starts each Play session without a photo object. Licensed Boing Kit, Feel and Spline Mesher Pro are also imported locally and ignored by Git. Runtime adapters include MudBun, FImpossible and Boing plus the shared capability lifecycle and optional plugin bindings described in [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md). MicroSplat and TVE are excluded. See [RUNTIME-ENGINE](RUNTIME-ENGINE.md) and [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md).

## Runtime capability decision — 2026-11-06

The reusable package now provides CapBinding/configuration/commands/CapState, explicit CapDependsOn relationships, disposable adapter instances, pose/appearance ownership and selected-state reconstruction. Each capability uses its own binding entity; multiple bindings compose on a target. Dynamic schemas and metadata are queryable by AI and developer tools. Optional licensed-plugin adapters remain separate source modules gated by detected imports; they compile in Assembly-CSharp where required by the vendor assembly boundary.

Native/Core additionally owns pinned BehaviorTree.CPP, TinySoundFont and Clipper2. They share the same Flecs world/C ABI; audio renderer callbacks use independent bounded state and shared immutable bank data. GeneralUser GS is bundled with license/provenance. No alternative ECS, physics solver or per-frame AI loop was added. See [current workflow and actual coverage](RUNTIME-CAPABILITIES.md); advanced design targets remain in CAPABILITY-CONTRACT. New recipes run at runtime; new underlying algorithms still require compiled implementation.

## Boundaries and unresolved work

- Unity CharacterController is the sole movement/collision solver for each bound generated actor, supporting ground and controlled 3D steering with configurable gravity. Flecs stores ActorControl, MotorPolicy, MoveIntent, SpatialIntent, ActorMotion and ActorRig as runtime Script-defined components. Player input, follow goals and new reactive Script rules share that command/result protocol. DioramaRuntime consumes intent, resolves gravity/collisions once, and publishes actual pose/velocity/grounding. Photo brushes have rest data rather than generic Position, so the view bridge does not overwrite animated pivots. Jolt is not installed. Do not add a second solver for the same body.
- Runtime-created Legs Animator/Leaning Animator and BoingBones bind to the actual MudBun pivot hierarchy. These compiled plugin adapters provide reusable capabilities; runtime Script supplies data and control logic. RuntimeExperience binds independent named models/actors, up to 64 bindings; only the selected actor reads movement devices. Controlled hovering/flight-like movement can be composed from 3D steering; aerodynamics and vehicle dynamics need further adapters. Ground NavMesh generation/path-direction capabilities now route to existing motors; they do not replace a movement solver. Versioned local saves restore managed definitions, model/rig configuration, clock, positions, input and selected state including Persistent-tagged template mut. Pending async operations restart; arbitrary VM/physics checkpoints, multiplayer, cloud storage, scanning/reconstruction and arbitrary game generation remain unimplemented.
- Unity remains responsible for presentation and deployment. Replacing it with a custom C++ engine is not an approved migration.
- Only Windows x64 is initially verified. Mobile/XR require target libraries and testing. Browser builds need WebAssembly/static native integration and a different transport; the Windows DLL and native REST listener cannot run unchanged in a browser.
- Built-in Flecs REST binds loopback and is a development tool, not the production/authenticated AI boundary. Script changes are not transactional.
- Preserve one authoritative owner for each subsystem; native components can represent configuration, commands and results without introducing a second simulator.

## Communication preference

Be concise, concrete and candid. Lead with the result. State what works, what is proposed and what has actually been tested. Do not rubber-stamp suggestions, repeatedly change direction to agree, or replace implementation with vague AI promises.

## Browser diagnostics decision

The developer logger is a reusable, read-only browser viewer backed by the existing trace collector; it is independent of Flecs Explorer and Unity Editor UI. Editor/development builds can serve it locally; LAN requires an explicit toggle and pairing link. Offline HTML/JSONL inspection works without a running Unity app. Release builds do not start it. Windows is verified; mobile target/device setup remains separate. Detailed workflow and boundaries: BROWSER-TRACE.md.

## Local AI and creative direction (2026-11-06)

Approved: Windows local llama.cpp inference with multimodal/tool calling and Whisper.cpp transcription, reusable in the AI package; no cloud fallback without explicit provider selection. Unity handles microphone capture only on explicit user action. The agent uses the same application tools as cloud providers. Initial weights are selected for the available 8 GB GPU, not promised to match frontier cloud quality. Mobile requires a future embedded backend. See [LOCAL-AI](LOCAL-AI.md).

Cozy responsive workshop feel, object-bound spatial tools and user-changeable themes across visuals/motion/audio are recorded in [PRODUCT-DIRECTION](PRODUCT-DIRECTION.md). These designs do not imply completed spatial UX/theme adapters. [BACKLOG](BACKLOG.md) is the maintained task list; [DISTRIBUTION](DISTRIBUTION.md) records current store/licensing concerns.
