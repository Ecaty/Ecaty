# Current status

Updated: 2026-11-01. Read this after `PROJECT.md` and `DEVELOPMENT.md`.

## Completed foundation

- Separate Unity 6000.4.10f1 Windows project, independent of Krebi/Quantum.
- Shared native runtime in `Native/Core`, application Spin system in `Native/App`, reusable embedded Unity package in `Packages/com.ecaty.flecs-unity`.
- Public repositories: Ecaty/Ecaty, Ecaty/flecs-native, Ecaty/ecaty-native and Ecaty/flecs-unity. The parent pins the child commits with Git submodules.
- One native plugin combines Core, the application module and matching upstream Flecs v4.1.6 sources. Windows x64 plugin included through Git LFS.
- Reusable lifecycle/bootstrap, query/Script C ABI, runtime .flecs importer and Unity presentation. Application-specific startup, camera and sample content stay in the app.
- The developer OnGUI panel has been removed from the shared runtime. Product chat UI has not been added yet.
- Versioned AGENTS/project/development/status/provider notes enable a new chat or account to continue from the repository.

## Verification completed in Ecaty

- GCC/MinGW-w64 12.2 C++17 build; upstream Flecs compiled as C99.
- Native ABI, managed Script, runtime types, queries/reflection, prefab/hierarchy, UTF-8 label copying, independent live worlds and repeated teardown.
- Real loopback REST inspection, query and Script edits, deletion, statistics and CORS preflight.
- Live Unity Editor object checks: four initial views, native rotation, parent transforms, colors, creation/deletion and dynamic component data.
- Windows development player built from the existing SampleScene; 80 advancing runtime frames, native Script spawn and rotation reflected in Unity state.
- External REST edits/queries against the running player, with no player exceptions in that check.

Reports: `Verification/native-checks.json`, `unity-checks.json`, `player-checks.json`, `player-rest-checks.json`.

These are programmatic/object-state checks, not a visual review. MSVC/CMake, mobile, XR, IL2CPP and browser targets are not verified. The code includes build support for CMake/MSVC, but that is not a pass claim.

## Next concrete task

Implement the minimal UI Toolkit chat and official ChatGPT account flow in `docs/AI-INTEGRATION.md`. Start with text and a narrow tool loop: inspect schema/query, apply Script, inspect the result. Verify account eligibility and granted inference scopes. Keep credentials protected outside the project.

The documented ChatGPT-plan preview does not supply audio input/transcription. Select a separate supported dictation implementation before enabling the microphone; no speech engine is installed yet.

No ChatGPT authentication/inference, production AI transport, session storage, scanning, Jolt, procedural character animation or multiplayer is implemented. Do not begin those other subsystems merely because they were discussed.