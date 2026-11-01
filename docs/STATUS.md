# Current status

Updated: 2026-11-01. Read PROJECT and DEVELOPMENT first.

## Implemented

- Unity 6000.4.10f1 Windows x64, native C++17 Flecs v4.1.6 (unmodified matching C/header), one composed plugin. Core/App/Flecs bridge/AI package are separate public submodules.
- Native runtime queries, Script, runtime component reflection, prefabs/hierarchy, label ownership, loopback REST/Explorer and Spin demo.
- Bidirectional observation path: Unity applies views, measures object transform/material state, batches Presented into Flecs. C ABI v2, original 80-byte view layout retained. No second physics simulation.
- Reusable provider-neutral UI/controller/tools/main-thread dispatch. Windows ChatGPT account protocol and streamed local tool loop. Credentials protected outside the project with DPAPI. No API key/billing fallback.
- Minimal icon-based input; Google Material Symbols Rounded bundled with license/provenance. Account icon opens sign-in; microphone remains disabled.
- Ecaty host tools: query, inspect_entity, apply_script, observe_world. Agent instructions keep all engine/tool narration internal.
- Developer-only chat-like trace: request/tool foldouts, search/errors, copy/expand/collapse, local ignored JSONL. Explorer trace mirroring/custom panel is not implemented.

## Verification

- MSVC 19.51 / Visual Studio 2026 native build passed ABI/reflection, Script/query, observation isolation, string/prefab copying, independent worlds, lifetime and real REST checks (`native-checks.json`).
- Live Editor checks pass native progression, actual object transform/color, hierarchy, spawn/delete, runtime data and queryable Presented (`unity-checks.json`).
- Bundled Google font/glyphs, UXML, disconnected/empty/busy rules, optional dictation and disposal pass (`ai-package-checks.json`).
- Synthetic account/service transport with real world/tools passes PKCE/client issuance, signed identity and rejection cases, DPAPI, refresh rotation, query -> Script -> Unity -> Presented -> chat, grouped trace/redaction and late-stream failure checks (`chat-loop-checks.json`). No real account credentials used in these checks.
- Windows development player and external REST checks are recorded in `player-checks.json` / `player-rest-checks.json`; reports describe the exact checks performed. Object-state verification is not visual review.

All reports live under Verification. Read their actual results; do not treat protocol fixtures as proof of a live service connection.

## Next concrete step / limits

Press Play, use the account icon, finish browser consent with an eligible ChatGPT account, then try “Make the cube blue and twice as big.” Verify actual model/tool behavior and subscription eligibility. Live ChatGPT authorization/inference has not been verified yet. Keep developer traces private.

Mobile/XR, IL2CPP and browser targets remain unverified. No speech, physics/Jolt, multiplayer, storage, scanning, procedural creatures or arbitrary behavior generation. Do not start those systems implicitly.
