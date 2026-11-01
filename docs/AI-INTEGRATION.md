# AI integration

## Implemented Windows path

The reusable `Packages/com.ecaty.ai-agent` package supplies provider/auth/tool contracts, UI Toolkit chat, conversation controller, main-thread dispatch, developer traces and a ChatGPT adapter. It has no Flecs dependency. Ecaty supplies instructions and tools in `Assets/Ecaty/Runtime/EcatyAgentTools.cs`.

Press Play, use the account icon and finish sign-in in your system browser. The microphone stays disabled until a speech adapter exists. No embedded browser or API key is used. Conversation history is memory-only and cleared when disconnected. Disconnect removes this app's active credentials; it does not revoke the server grant or log the browser out.

The application interface uses locally bundled Google Material Symbols Rounded: account, microphone and send. The font, Apache-2.0 license, source revision and hashes live in the package's `Runtime/UI/Resources/`. Explicit Unicode codepoints avoid browser-only ligatures. A static instance of the current variable font is used for predictable Unity import.

## Account protocol

The adapter follows the official local/open-source ChatGPT-plan flow checked on 2026-11-01. Availability/plan entitlements remain provider-controlled; do not promise free-plan support or commercial distribution eligibility. An OAuth identity alone is insufficient: require `chatgpt.tokens.use.direct` before inference.

- First registration: `dynamic_agent_client`, persistent `urn:uuid:` host identifier, name hint; retain the issued client ID.
- Fresh PKCE S256, random state/nonce, loopback listener bound to 127.0.0.1 before opening the browser; callback `/auth/callback` on an ephemeral port.
- Exchange at the official token endpoint. Verify RS256 identity signatures against official JWKS, issuer, audience, time, nonce and returning subject. No partner secret.
- Protect state with Windows DPAPI CurrentUser in `%LOCALAPPDATA%/Ecaty/Agents/chatgpt.dat`, outside the checkout. Save rotated tokens atomically; never read Codex/ChatGPT credentials from elsewhere.
- Discover allowed models from the OAuth catalog; prefer gpt-6.1-sol only when listed, otherwise the first visible model. Use streamed Responses with `store=false`, array history, local function tools and encrypted reasoning replay within the tool loop. No API-billing fallback.
- Accept success only after `response.completed`; EOF, incomplete replies and late subscription errors fail the request. Limit a request to 10 rounds/24 tool calls; controller timeout is 3 minutes.

Official references: [registration/sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in), [tokens](https://developers.openai.com/siwc/token-sharing-open-source/token-reference), [models/inference](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference), [preview limits](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations). Recheck before shipping.

## World loop and end-user behavior

1. The user asks a change. The host gives the model private capability instructions and tools.
2. `query` / `inspect_entity` read native Flecs data/reflection. `apply_script` evaluates real Flecs Script on the world-owning thread.
3. The presentation adapter applies the snapshot, then reads actual local transforms and renderer/text colors into the native Presented component.
4. `apply_script` / `observe_world` return queries containing that readback. The model checks internally. The user sees the changed objects, with brief useful chat only.

Never mention Unity, Flecs, ECS, queries, Script or tool machinery in the end-user experience. Readback proves object state, not the pixels on screen. New algorithms/effects still require native systems/presentation adapters. Script edits are not transactional.

## Developer trace

**Ecaty > Agent Trace** displays a chat-like request list: collapsed request/tool foldouts, queries/Script/results, timing, error filter, search, copy and expand/collapse all. It captures no hidden reasoning or auth exchanges. Editor capture defaults on; player capture defaults off. Current-session memory is bounded to 500 entries; local JSONL segments rotate around 10 MB and are ignored by Git. Clear view does not delete log files.

Traces may contain private messages/world data. Authentication payloads are excluded; bearer/JWT/key patterns are additionally redacted. Never publish real traces. The package's collector is independent of its Editor viewer. Mirroring trace records into Flecs entities and a custom Explorer chat panel are possible next steps; **neither is implemented**. Existing Explorer query/entity views do not automatically become a chat logger.

## Verification and remaining work

`Ecaty.Editor.ChatLoopVerification.Start()` runs asynchronous Editor checks and writes `Verification/chat-loop-checks.json`. The service transport is synthetic, confined to Editor verification; native Flecs, queries, Script and Unity objects are real. Checks cover PKCE/client issuance, signed identity/nonce/audience, DPAPI, refresh rotation, three local tools, actual object/readback state, collapsible trace, redaction and terminal stream failures. The normal runtime never uses a fake provider.

Live authorization, account eligibility, inference and provider revocation still need a real account test. No audio/transcription is supplied by the documented plan preview; no speech adapter is installed. Only Windows/Mono is checked. Other OS credential stores, mobile callback flows, IL2CPP stripping and browser native transport are separate work.


## Free-account alternative, checked 2026-11-01

OpenRouter documents browser account authorization using OAuth PKCE and no-purchase `:free` model variants. The current no-credit allowance is 50 inference requests/day and 20/minute; a single agent command normally takes several inference requests. This is a viable optional adapter for prototyping, not unlimited free use. It is **not integrated yet**; the current button still uses ChatGPT. A future free-only adapter must prohibit paid-model fallbacks.

Sources: [OAuth PKCE](https://openrouter.ai/docs/guides/overview/auth/oauth), [free-model limits](https://openrouter.ai/collections/free-models). Gemini API also offers model-specific free quotas, but API project/key setup is separate from consumer Gemini chat sign-in.

## UI on Unity 6.6

Ecaty now targets 6000.6.7f1 and hosts UI through PanelRenderer, using its versioned reload callback instead of UIDocument.rootVisualElement. Input/send/account/voice icons remain provider-neutral. No greeting, onboarding copy or engine narration is added to the app. The UI uses custom styling with a centered composer and optional collapsible history.
