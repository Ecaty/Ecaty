# Development and continuation

## Start or resume

Clone recursively, or run `git submodule update --init --recursive` in an existing clone. Install Git LFS for the Unity project's binary assets. Use Unity 6000.4.10f1 on Windows x64 for the verified baseline.

Open the **Ecaty checkout** in Codex and Unity. Read root `AGENTS.md`, `PROJECT.md` and `STATUS.md`. Inspect parent and child Git status before editing. The development machine originally used `A:/Projects/Ecaty`, but the code and build scripts must not depend on that location or a particular user's account.

The official Unity CLI and its `com.unity.pipeline` package provide the live Editor connection. Check for CLI updates, install the connection with `unity pipeline install --project-path <checkout>` if missing, and focus/refresh the Editor so the package loads. Pin every operation to `--project-path <checkout>` when several Editors exist. Use `unity command --query <topic>` to discover current commands; do not guess signatures or drive another project's Editor.

## Code map

| Change | File or area |
|---|---|
| Initial cube/sphere/label, colors or starting values | `Assets/Ecaty/Resources/EcatyWorld.flecs` |
| Product startup and sample camera | `Assets/Ecaty/Runtime/EcatyBootstrap.cs` |
| Shared native component layout/reflection | `Native/Core/include/FlecsComponents.hpp`, `Native/Core/src/Components.cpp` |
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

The package provides a minimal icon-based UI, reusable controller/main-thread dispatcher, contracts, Windows ChatGPT adapter and developer trace viewer. EcatyBootstrap mounts it with host instructions/tools. Authentication stays in the provider adapter; the shared view does not own credentials. Account/model availability is provider-controlled. See AI-INTEGRATION for protocol and current limits.

In the Editor, `Ecaty.Agents.Editor.AgentPackageVerification.RunAll()` validates UXML import, disconnected/empty/busy submission rules, optional microphone state, literal model text and disposal. This is not a visual or live-provider test.

## Native rebuild

Close the Ecaty Editor/player before replacing a loaded native DLL. A stopped Play session may still leave the native library loaded by Unity.

- MinGW-w64 gcc + g++: `./Native/Build.ps1 -Compiler GCC`
- MSVC with the detected Visual Studio/bundled CMake: `./Native/Build.ps1 -Compiler MSVC`
- Direct CMake: `cmake -S Native -B Native/build`, then `cmake --build Native/build --config Release`.

The output is `Assets/Plugins/Ecaty/x86_64/flecs_unity.dll`. The root composes shared Core with application App. Keep ABI version/layout synchronized with C# when changing the boundary. No C++ classes or STL strings cross it. Owned UTF-8 results are released through `fu_free`. Current C ABI is v2 (80-byte fu_view unchanged); the new fu_write_presented batch writes observations only. Reflected Label strings have explicit C++ copy/move/destruction ownership.

MinGW links compiler/C++ support statically. Shared component types remain engine-neutral. The App module implements `flecs_unity::register_application(flecs::world&)` in the same plugin. Do not put product-specific systems in shared Core.

## Concrete edit loop

Press Play. In Explorer on localhost:27750, change `scene.Cube`'s `Spin.y` from 40 to 80. The C++ Spin system rotates it twice as fast on subsequent ticks; Unity presents the updated Rotation. To keep the change, edit the corresponding value in `EcatyWorld.flecs`. No native compilation is needed for that value change.

Change `scene.Greeting`'s `Label.text` to update its Unity TextMesh. To change the spinning algorithm, edit `Native/App/src/Application.cpp`, close Unity, rebuild the plugin, reopen and Play. A completely new presentation feature requires its C# adapter as well as native data/systems.

## Verification

- `python Verification/native_smoke.py`: real native ABI, Script, queries, reflection, managed entity deletion, UTF-8 string ownership, independent worlds and HTTP REST checks.
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
