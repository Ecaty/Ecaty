# Current status

Updated: 2026-11-01. Read this after `PROJECT.md` and `DEVELOPMENT.md`.

## Completed foundation

- Separate Unity 6000.4.10f1 Windows project, independent of Krebi/Quantum.
- Shared native runtime in `Native/Core`, application Spin system in `Native/App`, reusable embedded Unity package in `Packages/com.ecaty.flecs-unity`.
- Public repositories: Ecaty/Ecaty, Ecaty/flecs-native, Ecaty/ecaty-native, Ecaty/flecs-unity and Ecaty/ai-agent-unity. The parent pins the child commits with Git submodules.
- One native plugin combines Core, the application module and matching upstream Flecs v4.1.6 sources. Windows x64 plugin included through Git LFS.
- Reusable lifecycle/bootstrap, query/Script C ABI, runtime .flecs importer and Unity presentation. Application-specific startup, camera and sample content stay in the app.
- The developer OnGUI panel has been removed from the shared runtime. A separate provider-neutral AI package now contains reusable chat assets/bindings and provider/authentication/tool/speech contracts; the app does not mount it until a real provider/session is wired.
- Native Visual Studio workflow now has a Windows MSVC CMake preset and optional generated-solution/build helper; Unity continues generating its separate C# solution.
- Versioned AGENTS/project/development/status/provider notes enable a new chat or account to continue from the repository.

## Verification completed in Ecaty

- GCC/MinGW-w64 12.2 C++17 build; upstream Flecs compiled as C99.
- Native ABI, managed Script, runtime types, queries/reflection, prefab/hierarchy, UTF-8 label copying, independent live worlds and repeated teardown.
- Real loopback REST inspection, query and Script edits, deletion, statistics and CORS preflight.
- Live Unity Editor object checks: four initial views, native rotation, parent transforms, colors, creation/deletion and dynamic component data.
- Windows development player built from the existing SampleScene; 80 advancing runtime frames, native Script spawn and rotation reflected in Unity state.
- External REST edits/queries against the running player, with no player exceptions in that check.

Reports: `Verification/native-checks.json`, `unity-checks.json`, `player-checks.json`, `player-rest-checks.json`.

Additional verification: native Visual Studio 2026/CMake 4.3.1 build with MSVC 19.51 passed the same native checks and live Unity bridge checks. The Windows player was rebuilt with that plugin. The separate AI-package check verifies UXML import, input routing, disconnected/empty/busy behavior, optional dictation availability, literal text and disposal. CMake preset parsing and Unity C# solution generation (including the embedded AI package) were also checked.

Additional reports: `Verification/ai-package-checks.json` and `Verification/native-ide-checks.json`. The generated native solution is `Native/build-vs/Ecaty.slnx`; regenerate it using the committed helper rather than committing generated project files.

These are programmatic/object-state checks, not a visual review or live AI-service test. Mobile, XR, IL2CPP and browser targets remain unverified.

## Next concrete task

Implement the official ChatGPT provider/account flow and connect the existing minimal chat view inside `Packages/com.ecaty.ai-agent`, following `docs/AI-INTEGRATION.md`. Keep the view and generic orchestration reusable with other service adapters. Start with text and a narrow tool loop: inspect schema/query, apply Script, inspect the result. Verify account eligibility and granted inference scopes. Keep credentials protected outside the project.

The documented ChatGPT-plan preview does not supply audio input/transcription. Select a separate supported dictation implementation before enabling the microphone; no speech engine is installed yet.

No ChatGPT authentication/inference, production AI transport, session storage, scanning, Jolt, procedural character animation or multiplayer is implemented. Do not begin those other subsystems merely because they were discussed.