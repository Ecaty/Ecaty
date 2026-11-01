# Project and decisions

## Product intent

Ecaty explores giving life to a user's creations as interactive and/or narrative digital experiences. Possible later inputs include text, audio, drawings, toys, physical objects, playdough and room scans. Users can iteratively direct the result. Scanning and automatic reconstruction are future capabilities, not requirements for the first runtime test.

The user has practical Flecs C++/Unreal and Unity experience, including Flecs Script driving UI/world changes. Preserve runtime query and Script capabilities as first-class features. Avoid explaining them away as mere data export.

The intended product is an interactive experience, not a traditional user-facing game editor. Users need not be embodied in the world. Do not pivot to generated video, assume a single story/game genre, or promise that an AI can create physically correct arbitrary behavior without supporting systems. Morean/Nodus-style visualization products are outside the intended direction. Krebi is a separate project and must not be migrated or modified here.

## Approved baseline

- First platform: Windows x64. Unity **6000.4.10f1**, URP **17.4.0**.
- Native language: C++17 for human-friendly contributions, using Flecs' C++ API. Matching upstream Flecs **v4.1.6** `flecs.c`/`flecs.h` remain unmodified C99.
- One native Flecs world per session. Native C ABI exports `fu_*`; Unity's `FlecsSession` owns the handle.
- AI's intended loop: inspect via queries/reflection, create/update via Flecs Script, observe results and iterate. The ChatGPT adapter now supplies this loop; its protocol is tested with synthetic transport, pending a live account test.
- Minimal product UI: a small input with icon buttons, conversation only when present, and errors only when needed. Use locally bundled Google Material Symbols; avoid explanatory labels and engine details in user chat. UI Toolkit is the chosen starting point; authentication uses a system browser. No embedded WebView is required for this interface.
- AI-agent UI and infrastructure must be reusable with different services through adapters; no provider-specific protocol or credentials in the shared view, and no Flecs dependency in the generic AI package. The app supplies instructions and world callbacks.
- First provider: ChatGPT, with users authorizing their own eligible account. See `AI-INTEGRATION.md`; account access is not equivalent to unlimited/free inference.
- Local development with the Unity Editor is the primary workflow. Public GitHub repositories preserve source/history and allow other accounts and contributors to resume.
- Commercial direction remains exploratory: low-cost/free local experience; hosted multiplayer/storage could be paid later. No business model has been proven.

## Repositories and ownership

| Repository | Checkout location | Owns |
|---|---|---|
| https://github.com/Ecaty/Ecaty | Root | Unity app, assets, host/tool wiring, composition/build scripts, canonical project context |
| https://github.com/Ecaty/flecs-native | `Native/Core` | Shared C++ components/reflection, world lifetime, native C ABI, REST/snapshot bridge, pinned Flecs sources |
| https://github.com/Ecaty/ecaty-native | `Native/App` | Application-specific components and systems; initially Spin |
| https://github.com/Ecaty/flecs-unity | `Packages/com.ecaty.flecs-unity` | Reusable C# lifecycle, Unity views, configurable bootstrap, .flecs importer, Editor cleanup |
| https://github.com/Ecaty/ai-agent-unity | `Packages/com.ecaty.ai-agent` | Reusable AI chat UI, provider/authentication/tool/speech contracts; future adapters and orchestration |

The four children are Git submodules pinned to commits. The two C# children are embedded Unity packages, not second copies in Assets. Core and App are linked into one `flecs_unity.dll`; do not load separate copies of Flecs for each module. Flecs is vendored as the two matching tagged distribution files plus its license to keep the dependency small and reproducible.

## Execution and authority

1. `EcatyBootstrap` selects the application's initial Script/material and calls the reusable bootstrap. It supplies the sample camera; the shared package does not impose a camera or app UI.
2. Native `fu_create` imports standard addons, registers shared components, invokes the linked application registration hook and prepares the presentation query.
3. Unity's main-thread Update services queued native REST edits before progressing the Flecs pipeline. Structural Script edits therefore happen outside progress.
4. A native snapshot of entities with Position supplies hierarchy, transform, primitive, tint and label information. Unity creates/updates/removes views, then batches actual object transform/material observations back into the reflected Presented component (C ABI v2). Presented is an acknowledgement, not a second authoritative transform or physics simulation.
5. Explorer edits are live world changes; they are not automatically written back to source. Persist intended starting changes in `Assets/Ecaty/Resources/EcatyWorld.flecs`.

The demo supports Position/Rotation/Scale/Color/Primitive/Label, ChildOf hierarchy, prefab instantiation and the application's native Spin system. Script-defined extra data is queryable without a C# class; new visual effects still require an adapter.

## Boundaries and unresolved work

- No physics engine is integrated. Presentation primitives have their generated Unity colliders removed. Jolt was evaluated as a possible future native authority, not installed. A future adapter would send configuration/commands to Jolt and return poses/events to Flecs, then display them in Unity. Never simulate the same bodies in both engines.
- No multiplayer, persistent world/session storage, scanning/reconstruction, procedural creature animation or arbitrary game generation exists yet.
- Unity remains responsible for presentation and deployment. Replacing it with a custom C++ engine is not an approved migration.
- Only Windows x64 is initially verified. Mobile/XR require target libraries and testing. Browser builds need WebAssembly/static native integration and a different transport; the Windows DLL and native REST listener cannot run unchanged in a browser.
- Built-in Flecs REST binds loopback and is a development tool, not the production/authenticated AI boundary. Script changes are not transactional.
- Preserve one authoritative owner for each subsystem; native components can represent configuration, commands and results without introducing a second simulator.

## Communication preference

Be concise, concrete and candid. Lead with the result. State what works, what is proposed and what has actually been tested. Do not rubber-stamp suggestions, repeatedly change direction to agree, or replace implementation with vague AI promises.
