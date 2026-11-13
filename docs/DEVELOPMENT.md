# Development guide

Archived dated notes: [history/DEVELOPMENT-log-2026-11.md](history/DEVELOPMENT-log-2026-11.md) (read only if something is missing).

## Start / resume

- Clone recursively, or `git submodule update --init --recursive` (on Windows set `git config --global core.longpaths true` first; Native/Core's third-party libraries are nested submodules; see [Updating third-party forks](#updating-third-party-forks)). Install Git LFS. Use Unity 6000.6.7f1 on Windows x64.
- Open the Ecaty checkout. Read `AGENTS.md`, `docs/PROJECT.md`, this file, `docs/STATUS.md` (current state and next action), `docs/BACKLOG.md`. Inspect parent and submodule Git status first. Code and scripts must not depend on the checkout location or a particular account.
- Unity CLI (`com.unity.pipeline`) gives the live Editor connection. If missing: `unity pipeline install --project-path <checkout>`; focus/refresh the Editor. Always pin `--project-path <checkout>`; never drive another project's Editor or start a second Editor on this folder. Discover commands with `unity command --query <topic>`; do not guess signatures.
- Codex discovers `AGENTS.md` automatically; other chat tools need the context supplied. The in-app agent gets tools/capabilities explicitly, not developer docs.
- Handoff lives in repo files, not chat history or account memory.

## Controls (normal Play = CrystalWorldGallery)

- RMB full orbit (bounded elevation); MMB turn selected tier; wheel approach/zoom; LMB hold a ghost and drop on a world to swap. Escape cancels/returns and closes Settings first.
- Top invitation enters an empty creation context; first deliberate placement creates a disposable world. Inactive saved worlds stay hidden; archive UI deferred.
- Tune `Assets/_Features/Gallery/Gallery.asset` (spacing, ring width/style/opacity/material, sphere/rim opacity, rear fade, clearance, idle speed, glass crossing, return/reveal timing, loaded-world strength, approach cutoff, RingRotationSmoothTime) and `Theme/Theme.asset` (LoadedWorld colour; Editor-only, exported to `Theme/ThemeConfig.flecs` like the gallery config). Edits update native GalleryTreeConfig and the runtime theme live in Play. Odin icon tabs are optional.
- Local layered arrangement: `GalleryPrototype/tree-arrangement.json` under Application.persistentDataPath, version 2 (a version 1 file is converted on read and copied to `tree-arrangement.v1.json` before the first version 2 write; older `arrangement.json` is read-only import). Never commit any of them.
- Seed creation enters the saved primitive world path ([GALLERY-WORLDS](GALLERY-WORLDS.md)).

## Code map

| Change | Location |
|---|---|
| Gallery presenter/interaction/native contracts | `Assets/_Features/Gallery` (`Runtime/CrystalWorldGallery.cs`, `Gallery.asset`, `Resources/Gallery/Examples/GalleryCatalog.flecs`) |
| Gallery scenarios (compiled Editor suite) | `Assets/_Features/Gallery/Editor/GalleryScenarioSuite.cs` |
| Seed controls/icons | `Assets/_Settings/Runtime/WorkshopDock.cs`, `Assets/_Features/Creation/Runtime/WorkshopRadialMenu.cs`, `Assets/_Features/Creation/Editor/WorkshopSeedArt.cs` |
| Seed motion / lobby / world looks | `Assets/_Features/Creation/Resources/Workshop/Runtime/WorkshopSeedMotion.flecs`, `WorkshopLobby.flecs`; `Assets/_Features/World/Resources/Workshop/Runtime/WorkshopWorldLooks.flecs` |
| Post-processing capability | `Packages/com.runtime.capabilities/Runtime/Capabilities/PostProcessingCapability.cs` |
| Actor schema/behavior, reactive behavior | `Assets/_Features/World/Resources/Runtime/{Schemas/ActorSchema.flecs,Behaviors/ActorBehavior.flecs,Behaviors/ReactiveBehavior.flecs}` |
| Ground motor / runtime plugin bindings | `Assets/_Features/World/Runtime/DioramaRuntime.cs` |
| Startup, sample camera | `Assets/__Common/Bootstrap/GameBootstrap.cs` |
| Host agent tools | `Assets/__Common/Agent/Runtime/WorldAgentTools.cs` |
| Verification world | `Assets/_Tests/Fixtures/Resources/Tests/Fixtures/VerificationWorld.flecs` |
| Shared native components/reflection | `Native/Core/include/FlecsComponents.hpp`, `Native/Core/src/Components.cpp` |
| Native world, REST, C ABI | `Native/Core/src/flecs_unity.cpp`, `Native/Core/include/flecs_unity.h` |
| Source manager (kinds, undo), worlds and world documents | `Native/Core/src/Sources.cpp`, `Native/Core/src/Worlds.cpp`; C#: `Packages/com.runtime.flecs/Runtime/FlecsSession.cs` |
| Look and WorldStyle (data), motion loops | `Native/Core/src/Look.cpp`, `Native/Core/src/Motion.cpp`; tests `Native/Core/tests/look_tests.cpp`, `motion_tests.cpp`; Unity look adapter: `Packages/com.runtime.capabilities/Runtime/Look/` |
| Gallery world documents | `Assets/_Features/Gallery/Runtime/GalleryWorldDocument.cs` |
| App native systems (Spin) | `Native/App/include/Application.hpp`, `Native/App/src/Application.cpp` |
| Managed native ownership | `Packages/com.runtime.flecs/Runtime/FlecsSession.cs` |
| Rendering/presentation adapters | `Packages/com.runtime.flecs/Runtime/FlecsWorld.cs` |
| Shared startup | `Packages/com.runtime.flecs/Runtime/FlecsBootstrap.cs` |
| AI chat/providers/diagnostics | `Packages/com.runtime.ai-agent` (Runtime/Diagnostics, Runtime/UI) |
| Docs | [PROJECT-STRUCTURE](PROJECT-STRUCTURE.md), [RESOURCES](RESOURCES.md), [AI-INTEGRATION](AI-INTEGRATION.md), [RUNTIME-ENGINE](RUNTIME-ENGINE.md), [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md), [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md), [BROWSER-TRACE](BROWSER-TRACE.md), [TESTING](TESTING.md) |

## Visual Studio

- Same Visual Studio for both languages. C#: open Unity's generated solution (enable Embedded packages in Unity Preferences > External Tools if package projects are missing; includes `Runtime.Agents.csproj`, `Runtime.Agents.Editor.csproj`).
- C++: File > Open > Folder > `Native` (CMakeLists/`CMakePresets.json`). Needs Desktop development with C++ incl. C++ CMake tools. Checked machine: VS 2026 (18), CMake 4.3.1.
- Optional solution: `./Native/Generate-Solution.ps1` (writes ignored `Native/build-vs/FlecsUnity.sln`; `-Build` builds Release). Do not merge it into Unity's solution.

## Native rebuild

Native layout: `Native/Core/src/Modules.hpp` documents the modules (`core.cap`, `core.tree`, `core.geometry`, `app`) and the one phase map every native system follows. Public component names stay at the root scope; a reaction to a changed component starts in an observer that only notes the change, and a system applies it once per frame (Flecs locks tables inside observers). Before committing native work, also build Debug with our warnings and the core tests, and run the smokes against it (a Debug build checks things Release skips): `cmake -S Native -B Native/build-tests -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Debug -DFU_WARNINGS=ON -DFU_BUILD_TESTS=ON -DUNITY_PLUGIN_DIR=<any folder>`, `cmake --build Native/build-tests`, run `Native/build-tests/Core/flecs_unity_core_tests.exe` (built from `Native/Core/tests`: `core_tests.cpp`, `source_tests.cpp` for the source manager, `world_tests.cpp` for worlds and world documents, `look_tests.cpp` for Look and WorldStyle, `motion_tests.cpp` for motion loops), then the Python smokes with `FLECS_UNITY_LIBRARY=<that folder>/flecs_unity.dll` and `FLECS_UNITY_DEBUG_BUILD=1`. CI does the same.

- Contract checks: `python Verification/contract_checks.py` (header, exports, C# imports, ABI constants, every `.flecs` file loads, and the checks `sources`, `kinds`, `worlds` and `world-documents`). It needs the Release library.
- A prefab that has children must be declared with `@tree ChildOf` (the source manager adds it to a kind text that lacks it and refuses `@tree Parent`). Flecs stores a prefab's children with `Parent` by default; then the children cannot change after the first instance and instance parts have no names.
- Close the Ecaty Editor/player first (a stopped Play session may keep the DLL loaded).
- MinGW-w64: `./Native/Build.ps1 -Compiler GCC`; MSVC: `./Native/Build.ps1 -Compiler MSVC`; direct: `cmake -S Native -B Native/build` then `cmake --build Native/build --config Release`.
- Output: `Assets/Plugins/Flecs/x86_64/flecs_unity.dll` (Core + App in one library). Keep ABI version/layout in sync with C#; no C++ classes/STL strings across it; free owned UTF-8 with `fu_free`. Product-specific systems go in `Native/App` (`flecs_unity::register_application`), never Core.
- Changing the compiled Spin system: edit `Native/App/src/Application.cpp`, close Unity, rebuild, reopen. Different logic can instead be reactive Script (no rebuild). A new presentation feature also needs a C# adapter.
- Sanitizers: `-DFU_SANITIZE=ON` builds with AddressSanitizer; use MSVC or Clang, not MinGW.
- Worktrees: `Tools/Sync-WorktreeSubmodules.ps1` copies a worktree's submodule commits into the main checkout's submodule repositories before a worktree branch is merged or tested there.

## Tests

- Read `Tests/Results/latest.txt` after any run: each run writes a compact summary there. Agents should read it instead of large JSON/logs/HTML; open detail only for a failure.
- Runners (`Tests/*.cmd`, target this project's Editor via `Tests/UnityCli.ps1`): `Run-All.cmd` (resources, Validator, native, EditMode, PlayMode, gallery, Windows player-script compile + editor-source exclusion), `Run-Gallery.cmd`, `Run-EditMode.cmd`, `Run-PlayMode.cmd`, `Run-Native.cmd`, `Run-Resources.cmd`, `Run-Validator.cmd`, `Run-PlayerScripts.cmd`; `Open-Latest-Gallery.cmd`/`Open-Latest-Results.cmd` open reports for humans. See [TESTING](TESTING.md) and [Tests/README](../Tests/README.md).
- Gallery: Runtime > Tests > Gallery scenarios. Add scenarios to the compiled suite rather than one-off scripts; assert on real native Flecs data and Unity outputs; tests must certify current behavior and refresh/recompile saved source first. Preserve unsaved scenes; restore test overrides; scenarios use disposable fixtures and the runner restores save bytes. Artifacts (PNG/JSON/HTML) are ignored by Git and are not aesthetic approval.
- Python/native: `python Verification/native_smoke.py` (ABI, Script, queries, REST), `python Verification/runtime_engine_smoke.py`, `python Verification/runtime_behavior_smoke.py` (native managed-module smoke), `python Verification/capability_native_smoke.py`, `python Verification/player_rest_smoke.py` (one instance only, port 27750).
- Editor entries (Runtime menu or `Game.Editor.*.Start()`): `RuntimeSpatialVerification`, `RuntimeActorVerification`, `LiveProviderVerification` (opt-in, after owner authorization), `MultiProviderVerification`, `ChatLoopVerification`, `Verification.RunAll`, `Verification.BuildPlayer` (builds `Build/Ecaty.exe`), `Runtime.Agents.Editor.AgentPackageVerification.RunAll()`. Reports go to `Verification/*.json`.
- Player flags: `Build/Ecaty.exe -batchmode -nographics -flecsSmoke -flecsSmokeResult <abs json> -logFile <abs log>` (80 frames); `-runtimeEngineSmoke -runtimeEngineSmokeResult <abs json>` (graphical, no `-nographics`: MudBun needs compute shaders); `-capabilitySmokeResult <abs json>`.
- Run only relevant checks; report what ran and limits. Object-state checks are not visual review. Keep native HTTP operations on the world-owning thread.

## Runtime and capability workflow

- Runtime: Play > Runtime > Runtime engine > Start runtime playground / Start creature test (WASD or gamepad moves; click ground to follow; Escape leaves chat focus). Save/Resume uses the versioned local experience doc; startup stays empty. Prefer create_model/configure_model/apply_runtime_behavior and source edit/undo tools. Inspect `PhotoClay` ActorControl/MoveIntent/ActorMotion/ActorRig in Explorer; Script can write ScriptInput.MoveIntent when mode is 3. Query/type caches are world-owned; do not retain IDs across world replacement.
- Capabilities: run Prepare capability assets, then Start capability workshop or Verify capabilities.
- Never pass persistent source assets to vendor APIs that own/destroy inputs (clone Broccoli pipelines first). Spline adapters set the bundled runtime material explicitly. First shader builds are slow: use a long CLI timeout, inspect the build report, never start a second build while one runs.

## Licensed dependencies

- Import MudBun, FImpossible animators and Boing Kit locally before compiling; sources stay Git-ignored. Feel and Spline Mesher Pro are imported (Feel used for gallery springs). World.asset (RuntimeDioramaAssets) references MudBun's URP material: recheck after a fresh import.
- After reimporting vendor assets run `python Tools/apply_vendor_compatibility.py` (scoped Unity 6.6 fixes); reapply the documented Boing EntityId one-line patch if needed. CapabilityPluginDiscovery sets optional plugin symbols; inspect its paths if imports move. Never edit vendor code. Imported Sirenix tree/root meta stay ignored. See [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md).

## AI package

- `Packages/com.runtime.ai-agent` (submodule `Ecaty/ai-agent-unity`) holds generic chat UI, contracts, Windows local llama.cpp/Whisper, ChatGPT/OpenRouter/Gemini adapters, account selector, diagnostics. Host supplies Flecs tools via `IAgentTool`; marshal work to the owning thread. Credentials stay in provider adapters. Trace viewer: [BROWSER-TRACE](BROWSER-TRACE.md); host chooses log directory/title.

## Git across submodules

- Commit/push a changed child first, then commit the updated pointer in Ecaty; the pointer must name a commit on the public child remote. Task branches `codex/<task>`; no force pushes or global Git config changes. `.meta` files travel with assets.
- Before publishing, review the staged diff in each repo for credentials/local data; do not stage unrelated files. Pushes need current-task authorization.

## Updating third-party forks

- The only outside GitHub code is in `Native/Core/vendor`, each a submodule of our fork in the Ecaty org; the submodule commit is the pin. Never edit these sources in place.

| Path | Fork | Original tracked | Fork branch holding the pin |
|---|---|---|---|
| `vendor/flecs` | Ecaty/flecs | SanderMertens/flecs | master |
| `vendor/behaviortree` | Ecaty/BehaviorTree.CPP | BehaviorTree/BehaviorTree.CPP | master |
| `vendor/clipper2` | Ecaty/Clipper2 | PolyClipping/Clipper2 (our fork comes from AngusJohnson/Clipper2, which lacks these commits) | ecaty |
| `vendor/tinysoundfont` | Ecaty/TinySoundFont | schellingb/TinySoundFont | main |

- `.github/workflows/upstream-check.yml` runs weekly (or by hand) and opens or updates one `upstream` issue per library when the original has new commits or a new release. It changes nothing.
- To update: `gh repo sync Ecaty/<fork>` (for Clipper2, push the wanted PolyClipping commit to the fork's `ecaty` branch instead) → `git -C Native/Core/vendor/<lib> fetch origin <commit>` and `checkout <commit>` → rebuild (`./Native/Build.ps1`; the binary reports `FLECS_VERSION+<flecs short commit>`) → run `Tests/Run-Native.cmd` (and `Run-All.cmd` for Flecs) → commit the pin in Native/Core, then the Native/Core pointer in Ecaty → close the issue. If we ever must patch a library, commit the patch on an `ecaty` branch in its fork and pin that.
- Windows: Flecs has very long test paths, so run `git config --global core.longpaths true` once before cloning. Use `git submodule update --init --recursive --depth 1` (or `git clone --recurse-submodules --shallow-submodules`): without `--depth 1`, a pin behind the fork's tip pulls full history (Flecs alone is ~230 MB instead of ~30 MB).

## Workflow rules

- Preserve unrelated changes, Unity GUIDs and unsaved scenes. Child repos link back to Ecaty's canonical context.
- Replaced features: remove superseded code/config/tests once the replacement is verified; keep only minimal save migration.
- Keep `docs/STATUS.md` current (current state, verification evidence, known limits, exact next action) after meaningful milestones; keep BACKLOG current. No dated checkpoint sections or session rituals. Record lasting approved decisions in `PROJECT.md`.

## Code style

- C#: one statement per line, braces on their own lines, aim for at most 140 characters (`.editorconfig`). Reformat with `dotnet format whitespace Assembly-CSharp.csproj --include Assets/_Features/` (trailing slash; use the Unity-generated csproj so `#if CAP_*` code is active). Keep formatting-only changes in their own commits.
