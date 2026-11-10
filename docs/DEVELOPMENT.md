# Development and continuation

## Current layered gallery controls - 2026-11-08

Normal Play startup runs CrystalWorldGallery. RMB: full orbit (bounded elevation); MMB: turn the selected tier; wheel: approach/zoom; LMB: hold a ghost and drop on a world to swap. Escape cancels/returns and closes Settings before changing the world. The protected top invitation enters an empty creation context; first deliberate placement creates a disposable world. Inactive saved worlds stay hidden; archive UI is deferred. All worlds use one normalized Flexalon cell and a single glass shell.

Tune Assets/_Features/Gallery/Gallery.asset in the Inspector: spacing, rounded-ring width/style/opacity/material, sphere/rim opacity, rear fade, clearance feather, idle speed and glass crossing. Edits update effective native GalleryTreeConfig. World membership/turns/camera/focus/draft and player saves remain Flecs data. Local layered arrangement is GalleryPrototype/tree-arrangement.json under Application.persistentDataPath, separate from the earlier flat arrangement.json; do not commit either. No previous chat/account is needed for code continuation.

Verification: run Tests/Run-Gallery.cmd or Runtime > Tests > Gallery scenarios. See TESTING for the active compiled suite and artifacts.

## Start or resume

Clone recursively, or run `git submodule update --init --recursive` in an existing clone. Install Git LFS for the Unity project's binary assets. Use Unity 6000.6.7f1 on Windows x64 for the verified baseline.

Open the **Ecaty checkout** in Codex and Unity. Read root `AGENTS.md`, `PROJECT.md` and `STATUS.md`. Inspect parent and child Git status before editing. The development machine originally used `A:/Projects/Ecaty`, but the code and build scripts must not depend on that location or a particular user's account.

The official Unity CLI and its `com.unity.pipeline` package provide the live Editor connection. Check for CLI updates, install the connection with `unity pipeline install --project-path <checkout>` if missing, and focus/refresh the Editor so the package loads. Pin every operation to `--project-path <checkout>` when several Editors exist. Use `unity command --query <topic>` to discover current commands; do not guess signatures or drive another project's Editor.

## Active gallery workflow

Only CrystalWorldGallery is active. The superseded flat-table presenter, furniture renderer, 3D archive carousel and their test helpers are removed. Current code/contracts are in _Features/Gallery; GalleryPreviewEntry is a disposable presentation binding, not an ECS or a second presenter. GalleryCatalog.flecs supplies fixtures/shared contracts, WorldGalleryTree initializes typed inputs, and WorldGalleryTreeMotion owns reactive motion/return outputs. The native entry gate still uses independent GalleryApproach inputs and is exercised by the active gallery.

Returning preserves the edited world camera and aligns its rendered scale/orientation with the preview before turning to a true 90-degree top-down view. It holds that distance: further zoom is only from user wheel input. Native measured-zoom/recenter state gradually moves focus to the tree center, reaching it at maximum outward zoom. Neighboring worlds reveal in distance order; a persistent themed rim identifies the resident/last world separately from hover/swap. Gallery startup forces top-down while retaining saved framing and the resident marker; a save inside a world resumes there. Paused preview phase and editing context are preserved.

Gallery/Gallery.asset exposes return/reveal timing, loaded-world strength, approach highlight cutoff, glass crossing and ring controls. Theme/Theme.asset owns LoadedWorld colour separately from Selection. GalaxyDust uses eight soft 3D density samples with twisting strands, seeded travelling movement, shared rear fade and a travelling accent blended from adjacent world's cached colours. No per-pixel world queries are added. MMB measures angle on the picked ring plane, including all quadrants at the top-down pole. Direct world interactions remain 3D and Settings stays 2D. Archive UI and deletion remain deferred.

Run Tests/Run-Gallery.cmd or Runtime > Tests > Gallery scenarios. The compiled suite measures ordinary production input, native state and actual rendering, with local PNG/JSON/HTML artifacts. Run-All also includes resources, Validator, native/PlayMode tests and Windows player-script compilation. Preserve unsaved scenes and restore test overrides afterward. A minimal read-only import of the earlier arrangement.json retains old saved catalog/camera/residence data; the old furniture behavior is not kept for compatibility.

## Capability workflow

Read [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md) for the short lifecycle, capability inventory, commands and limits. Run **Prepare capability assets**, then **Start capability workshop** or **Verify capabilities**. Native checks: `python Verification/capability_native_smoke.py`. The Windows development player accepts `-capabilitySmokeResult <absolute-json-path>` for the explicit packaged reconstruction test.

After reimporting licensed assets, run `python Tools/apply_vendor_compatibility.py` for the scoped Unity 6.6 fixes. CapabilityPluginDiscovery sets per-target optional plugin symbols; inspect its exact paths if imports move. Never pass persistent source assets to vendor APIs that own/destroy inputs: clone Broccoli pipelines first. Registered prefab/material/pipeline references are project assets, not generated AI asset names. Spline adapters assign the bundled runtime material explicitly; avoid the package default-material helper in a player. First shader builds can be slow. Use a generous CLI timeout and inspect the actual build report if the connection times out; do not start another build while Unity is still building.

## Code map

| Change | File or area |
|---|---|
| Seed controls and icon authoring | WorkshopDock.cs, WorkshopRadialMenu.cs, WorkshopSeedArt.cs in Assets/__Common; WorkshopSeedMotion.flecs owns idle motion and activation |
| World appearance | PostProcessingCapability.cs in the shared Unity package; WorkshopWorldLooks.flecs; Runtime > Workshop > Prepare post-processing |
| Blank-world ground | `Assets/__Common/Theme/Resources/EcatyWorld.flecs` |
| Ground motor, runtime plugin bindings and controls | `Assets/_Features/World/Runtime/DioramaRuntime.cs`, `Assets/__Common/Theme/Resources/ActorSchema.flecs`, `Assets/__Common/Theme/Resources/ActorBehavior.flecs` |
| Product startup and sample camera | `Assets/__Common/Bootstrap/EcatyBootstrap.cs` |
| Shared native component layout/reflection | `Native/Core/include/FlecsComponents.hpp`, `Native/Core/src/Components.cpp` |
| Runtime-interpreted behavior and host tools | `Assets/__Common/Theme/Resources/ReactiveBehavior.flecs`, `Assets/__Common/Agent/Runtime/EcatyAgentTools.cs`, [RUNTIME-BEHAVIORS](RUNTIME-BEHAVIORS.md) |
| Application native behavior | `Native/App/include/Application.hpp`, `Native/App/src/Application.cpp` |
| Native world, REST and C ABI | `Native/Core/src/flecs_unity.cpp`, `Native/Core/include/flecs_unity.h` |
| Managed native ownership | `Packages/com.ecaty.flecs-unity/Runtime/FlecsSession.cs` |
| Unity rendering/presentation adapters | `Packages/com.ecaty.flecs-unity/Runtime/FlecsWorld.cs` |
| Configurable shared startup | `Packages/com.ecaty.flecs-unity/Runtime/FlecsBootstrap.cs` |
| ChatGPT integration plan | `docs/AI-INTEGRATION.md` |

## Visual Studio and instruction discovery

Codex discovers the project's `AGENTS.md` when launched in the Ecaty checkout, and that file instructs it to read the related documents. A normal web chat needs the repository context supplied; do not assume every AI product discovers coding instructions. The in-app agent receives its runtime capabilities/tools explicitly rather than reading developer AGENTS files.

Use the same Visual Studio installation for both languages. Unity generates the C# solution; open it as usual. For C++, use File > Open > Folder and select `Native`; Visual Studio reads CMakeLists/CMakePresets and supplies native IntelliSense/build/debug tools. VS Code is optional. If embedded package projects are missing, enable Embedded packages in Unity Preferences > External Tools and regenerate project files; the current setup includes `Ecaty.Agents.csproj` and `Ecaty.Agents.Editor.csproj`.

Install Visual Studio's Desktop development with C++ workload including C++ CMake tools for Windows. The checked machine has Visual Studio 2026 (18) and bundled CMake 4.3.1. `Native/CMakePresets.json` supplies the Windows MSVC configuration for Visual Studio's folder workflow.

If you prefer a traditional native solution, run `./Native/Generate-Solution.ps1`. It detects Visual Studio and uses its bundled CMake, generates `Native/build-vs/Ecaty.sln` (or the generator's .slnx), and leaves generated project files ignored. `./Native/Generate-Solution.ps1 -Build` also builds Release. Do not hand-maintain or merge this generated native solution into Unity's generated C# solution. Close Unity/player before replacing their loaded native plugin.

## AI package

`Packages/com.ecaty.ai-agent` is the `Ecaty/ai-agent-unity` submodule. Keep generic chat UI, agent/provider/authentication/speech contracts and future provider adapters/orchestration there. The Ecaty host supplies its Flecs tools through `IAgentTool` callbacks; marshal native/Unity work to the owning thread. The shared AI package has no Flecs/Unity-bridge dependency.

The package provides a minimal icon-based UI, reusable controller/main-thread dispatcher, contracts, Windows local llama.cpp/Whisper and ChatGPT/OpenRouter/Gemini adapters, account selector and developer trace viewer. EcatyBootstrap mounts it with host instructions/tools. Authentication stays in the provider adapter; the shared view does not own credentials. Account/model availability is provider-controlled. See AI-INTEGRATION for protocol and current limits.

In the Editor, `Ecaty.Agents.Editor.AgentPackageVerification.RunAll()` validates UXML import, disconnected/empty/busy submission rules, optional microphone state, literal model text and disposal. This is not a visual or live-provider test.

## Native rebuild

Close the Ecaty Editor/player before replacing a loaded native DLL. A stopped Play session may still leave the native library loaded by Unity.

- MinGW-w64 gcc + g++: `./Native/Build.ps1 -Compiler GCC`
- MSVC with the detected Visual Studio/bundled CMake: `./Native/Build.ps1 -Compiler MSVC`
- Direct CMake: `cmake -S Native -B Native/build`, then `cmake --build Native/build --config Release`.

The output is `Assets/Plugins/Ecaty/x86_64/flecs_unity.dll`. The root composes shared Core with application App. Keep ABI version/layout synchronized with C# when changing the boundary. No C++ classes or STL strings cross it. Owned UTF-8 results are released through `fu_free`. Current C ABI is v2 (80-byte fu_view unchanged); the new fu_write_presented batch writes observations only. Reflected Label strings have explicit C++ copy/move/destruction ownership.

MinGW links compiler/C++ support statically. Shared component types remain engine-neutral. The App module implements `flecs_unity::register_application(flecs::world&)` in the same plugin. Do not put product-specific systems in shared Core.

## Runtime experience workflow

Read [RUNTIME-ENGINE](RUNTIME-ENGINE.md) for the short current workflow. Play > Runtime > Runtime engine > Start runtime playground creates two independent runtime fixtures. Save/Resume uses the local versioned experience document; startup remains empty.

RuntimeExperience owns bindings/lifetimes and persistence. EngineSchema, RuntimeLibrary and ActorBehavior are normal .flecs assets defining metadata, templates and the motor-routing algorithm. create_model/configure_model/apply_runtime_behavior and source edit/undo tools are the preferred multi-model agent path. Legacy default-model tools remain compatible.

Generic native APIs are in Core/src/flecs_unity.cpp and Core/include/flecs_unity.h; FlecsSession supplies validated POD reads/batches/query streams, component JSON, events, source tools and diagnostics. Query/type caches are world-owned; do not retain IDs across world replacement. C core and C++17 host remain one library.

Run `python Verification/runtime_engine_smoke.py`, then **Verify runtime experience** in Play Mode. Current reports: runtime-engine-native-checks.json and runtime-experience-checks.json. Keep existing actor/spatial/photo regressions for adapter changes. Saved pending awaits restart; source-backed definitions plus selected state are supported, not arbitrary external structural edits or plugin-version migrations.

## Concrete edit loop

Press Play: normal startup shows the seed from WorkshopLobby.flecs. Selecting the plus creates `diorama.Ground`, created by `EcatyWorld.flecs`. The earlier cube/sphere tests use `VerificationWorld.flecs` in isolated verification worlds or the explicit `-flecsSmoke` player mode.

For the new runtime workflow, choose **Runtime > Runtime engine > Start creature test** while playing. This loads a supplied model recipe and control configuration through the same runtime entry points as chat. Focus Game view; WASD or the gamepad left stick moves it, click/tap the ground to follow a point, and press Escape to leave chat text focus. Follow target / Stop are developer shortcuts. No model is created automatically. See [RUNTIME-ENGINE](RUNTIME-ENGINE.md).

In Explorer, inspect `PhotoClay` for ActorControl, MoveIntent, ActorMotion and ActorRig. Change ActorControl.speed to alter movement speed. Follow behavior is source in `ActorBehavior.flecs`, installed as managed ActorDriver. New runtime rules can write ScriptInput.MoveIntent when mode is 3; no native rebuild is needed for supported expressions. To persist starting changes, edit the appropriate source asset.
Create a label at runtime with `Caption { Position: {0,2,0}; Label: {text: "Your caption"} }`, then edit `Caption` with Script. No greeting label is included in the initial scene. To change the existing compiled Spin system itself, edit `Native/App/src/Application.cpp`, close Unity, rebuild the plugin, reopen and Play. Different math/conditions can instead run as reactive Script with Clock and component inputs, without rebuilding. Use the runtime behavior workflow linked above. A completely new presentation feature requires its C# adapter as well as native data/systems.

## Verification

- `Ecaty.Editor.RuntimeSpatialVerification.Start()` / **Runtime > Runtime engine > Verify spatial workflow**: real collider/pose/mesh readback, dynamic Script replacement, collision feedback and landing; `Verification/runtime-spatial-checks.json`.
- `Ecaty.Editor.RuntimeActorVerification.Start()` / **Runtime > Runtime engine > Verify workflow**: isolated Play Mode world, supplied recipes, runtime plugin bindings, synthetic gamepad, collisions, native feedback, follow/custom Script and rebind checks; report `Verification/runtime-actor-checks.json`. No live inference.
- `python Verification/runtime_behavior_smoke.py`: pinned upstream hashes, ongoing native reactivity, external controls, hot replacement, parse recovery, source reload and a small local timing measurement.
- `Ecaty.Editor.RuntimeBehaviorVerification.Start()`: actual apply/inspect/temporal tools, Unity transforms/Presented, hot replacement and source reload; writes `Verification/runtime-behavior-unity-checks.json`. No live inference.
- `python Verification/native_smoke.py`: real native ABI, Script, queries, reflection, managed entity deletion, UTF-8 string ownership, independent worlds and HTTP REST checks.
- `Ecaty.Editor.LiveProviderVerification.Start()`: opt-in real service/world check after owner authorization in Play; writes only pass/fail and object-state results, not credentials or the assistant reply.
- `Ecaty.Editor.MultiProviderVerification.Start()`: real local callback HTTP and Flecs/Unity objects with synthetic OpenRouter/Gemini services; writes Verification/provider-checks.json. Covers auth, failure/cancellation, tools, provider UI and conversation isolation.
- `Ecaty.Editor.ChatLoopVerification.Start()`: asynchronous synthetic service/account checks with real Flecs/Unity objects; poll Verification/chat-loop-checks.json. It does not prove live service access.
- `Ecaty.Agents.Editor.AgentPackageVerification.RunAll()`: font glyphs, UXML and generic input/disposal checks.
- Live Editor entry `Ecaty.Editor.Verification.RunAll`: actual Unity objects, transforms, materials, native system behavior, queries and deletion. It writes `Verification/unity-checks.json`.
- `Ecaty.Editor.Verification.BuildPlayer`: builds `Build/Ecaty.exe` from the existing sample scene without replacing the user's open scene.
- Player: run `Build/Ecaty.exe -batchmode -nographics -flecsSmoke -flecsSmokeResult <absolute-report-path> -logFile <absolute-log-path>` to verify 80 advancing frames.
- `python Verification/player_rest_smoke.py`: external REST edits/queries while the built player's Update runs. Only run one instance on port 27750.

Run only checks relevant to the change. Do not claim visual review from object-state checks. Generated screenshots/videos are for the human unless AI visual inspection is requested. Keep native HTTP operations on the world-owning thread.

## Git across submodules

Commit/push a changed child repository first, then commit its updated pointer in Ecaty. The root pointer must name a commit available in the public child remote. Use task branches such as `codex/<task>`; no force pushes or global Git configuration changes. Root and child `.meta` files travel with their assets. Temporary builds, credentials and local histories are not source.

Before publishing, review the staged diff in each repository and check for credentials/local user data. Do not stage unrelated working files merely to obtain a clean status. Initial setup was authorized to publish all four repositories publicly; future work follows the current user's task authorization.

## Keep the handoff useful

After a meaningful task, update `STATUS.md` with completed behavior, actual checks/results, known limits and the next concrete step. Record lasting approved decisions in `PROJECT.md`, not scattered chat transcripts. Another ChatGPT account needs repository access and local tools, not the previous account's conversation or tokens.

## Browser trace and chat diagnostics

See [BROWSER-TRACE](BROWSER-TRACE.md) for browser/standalone/dev-build/phone workflows and verification. The reusable server, lifecycle, viewer assets and controls are in Packages/com.ecaty.ai-agent/Runtime/Diagnostics and Runtime/UI. The host chooses the log directory/title. Default listener is loopback; LAN and private pairing links are developer-controlled. No remote relay or AI call is used for viewing. Editor and development capture are enabled by the host; release capture/server are off.


## Licensed runtime dependencies

Import MudBun, the FImpossible animators and Boing Kit locally before compiling the current app. The app now references their public runtime types directly. Feel and Spline Mesher Pro are locally imported candidates but have no application adapter yet. Licensed source stays ignored. EcatyRuntimeAssets in Resources references MudBun's live URP material for player inclusion; check this reference on a fresh import. Reapply the documented Boing EntityId one-line patch after vendor updates if necessary. See [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md).


### Built-player runtime check

The opt-in `-runtimeEngineSmoke -runtimeEngineSmokeResult <absolute JSON path>` mode creates two fixture models/rigs in a graphical Windows development player, exercises async input/pose feedback, saves/reloads selected state and confirms subsequent events. It does not call an AI provider. Do not use `-nographics` for this check: MudBun needs compute-shader graphics. The old `-flecsSmoke` check separately covers 80 frames, primitive presentation, chat mounting and provider controls.


## Reusable gallery checks

See [TESTING](TESTING.md). Use Runtime > Tests > Gallery scenarios or `./Tests/Run-All.cmd`; standard native EditMode and PlayMode tests are also available in Unity Test Runner. Add gallery scenarios to the compiled Editor suite instead of generating new one-off verification scripts. Runtime intent/state assertions must read actual native Flecs data and inspect relevant Unity outputs.

Resource roles and current paths: [RESOURCES](RESOURCES.md). Local test commands, automatic camera PNGs and HTML reports: [Tests/README](../Tests/README.md).

## Native world and validation workflow — 2026-11-09

See [GALLERY-WORLDS](GALLERY-WORLDS.md) for the first saved primitive world and its limits. Normal seed creation now enters this path; sample worlds remain mocks. Edit in 3D by dragging objects or clicking empty space. Continue with scoped capability/source persistence and AI tool routing rather than loading a second C# ECS or resetting the whole native session.

GalleryLayout and Theme use optional Odin icon tabs. RingRotationSmoothTime controls eased ring motion. Faded-world picking requires at least 0.98 visibility. Run-Validator.cmd executes installed Odin Validator against product configs, with machine-readable issues in Tests/Results. Do not edit vendor code or scan unsaved scenes for this check. The imported Sirenix tree/root meta stay ignored.
