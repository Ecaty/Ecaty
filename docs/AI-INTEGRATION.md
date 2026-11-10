# AI integration

Local inference is now available alongside cloud providers: [LOCAL-AI](LOCAL-AI.md). On device is the new-install default, with llama.cpp tools/vision and Whisper dictation on Windows; existing provider selections remain unchanged. No cloud fallback occurs automatically.

## Implemented Windows path

The reusable `Packages/com.ecaty.ai-agent` package supplies provider/auth/tool contracts, UI Toolkit chat, conversation controller, main-thread dispatch, developer traces and ChatGPT, OpenRouter and Gemini adapters. It has no Flecs dependency. Ecaty supplies instructions and tools in `Assets/__Common/Agent/Runtime/EcatyAgentTools.cs`.

Press Play, open the account icon, choose a provider and connect. OpenRouter is selected on a fresh installation. ChatGPT/OpenRouter open the system browser; the browser returns to an ephemeral loopback callback while the app keeps running. A browser redirect alone proves neither successful credential exchange nor inference. Gemini opens AI Studio through **Get key**, then accepts the user's key in a masked field; this is API-key authorization, not consumer Gemini chat-account OAuth.

Connections are independent; switching providers clears conversation history before another service can receive it. History remains memory-only. Disconnect clears the selected service's local credentials; it does not revoke the server grant or sign the browser out. Cancel stops an active connection/request. No embedded browser is used. The microphone remains disabled until a speech adapter exists.

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

If a previous attempt was tied to the wrong ChatGPT workspace, cancel the browser attempt. In the app account panel select ChatGPT, choose **Reset sign-in** (or **Disconnect** if connected), then **Continue** and choose the intended account and workspace in the browser. The reset clears the active local session. A retry without a verified account starts dynamic registration and keeps the same host ID.

## World loop and end-user behavior

1. The user asks a change. The host gives the model private capability instructions and tools.
2. `query` / `inspect_entity` read native Flecs data/reflection. `apply_script` evaluates real Flecs Script on the world-owning thread.
3. The presentation adapter applies the snapshot, then reads actual local transforms and renderer/text colors into the native Presented component.
4. `apply_script` / `observe_world` return queries containing that readback. The model checks internally. The user sees the changed objects, with brief useful chat only.

Never mention Unity, Flecs, ECS, queries, Script or tool machinery in the end-user experience. Readback proves object state, not the pixels on screen. New algorithms/effects still require native systems/presentation adapters. Script edits are not transactional.

## Developer trace

**Runtime > Agent Trace** displays a chat-like request list: collapsed request/tool foldouts, queries/Script/results, timing, error filter, search, copy and expand/collapse all. It captures no hidden reasoning or auth exchanges. Editor and development-player capture default on; normal release capture is off. Current-session memory is bounded to 500 entries; local JSONL segments rotate around 10 MB and are ignored by Git. Clear view does not delete log files.

Traces may contain private messages/world data. Authentication payloads are excluded; bearer/JWT/key patterns are additionally redacted. Never publish real traces. The package's collector is independent of its Editor viewer. Mirroring trace records into Flecs entities and a custom Explorer chat panel are possible next steps; **neither is implemented**. Existing Explorer query/entity views do not automatically become a chat logger.

## Verification and remaining work

`Ecaty.Editor.ChatLoopVerification.Start()` runs asynchronous Editor checks and writes `Verification/chat-loop-checks.json`. The service transport is synthetic, confined to Editor verification; native Flecs, queries, Script and Unity objects are real. Checks cover PKCE/client issuance, signed identity/nonce/audience, DPAPI, refresh rotation, three local tools, actual object/readback state, collapsible trace, redaction and terminal stream failures. The normal runtime never uses a fake provider.

Live authorization, account eligibility, inference and provider revocation still need a real account test. No audio/transcription is supplied by the documented plan preview; no speech adapter is installed. Only Windows/Mono is checked. Other OS credential stores, mobile callback flows, IL2CPP stripping and browser native transport are separate work.


## Added providers, checked 2026-11-03

### OpenRouter

Implemented browser PKCE S256 + random state using the official `/auth` and `/api/v1/auth/keys` exchange. Loopback binds only 127.0.0.1 before browser launch; wrong/duplicate state is rejected and slow connections are bounded. A returned key is validated using `/api/v1/key` before it is saved to its own DPAPI file. No app secret or developer inference account is used.

The reusable adapter accepts `openrouter/free` by default; Ecaty explicitly selects the catalog-verified coding model `cohere/north-mini-code:free` for consistent behavior across tool rounds. No model is silently substituted if it becomes unavailable. The optional free router filters for requested capabilities, including tools. Ecaty does not send optional strict-routing or parallel-call flags: these excluded all available free endpoints during the first real test. Execution of returned tools is still sequential. A configured explicit ID must end in `:free`; paid IDs are refused at construction. There are no paid-model fallbacks or retry loops. Tool-call identifiers and provider metadata are replayed during a request; incomplete/truncated replies fail before their tools execute. Tool calls execute sequentially through the host's existing world tools. Results are enveloped as `{ "result": <original data> }` so native numeric entity IDs cannot be interpreted as provider document IDs; original world data is preserved. Real entity inspection is included in the regression checks. The adapter shows final text after completion rather than token streaming. Sign-in timeout has a distinct message from user cancellation. Redacted/bounded inference validation messages are recorded only in developer traces; authentication response bodies are excluded.

No-purchase free variants currently allow 50 inference requests/day and 20/minute. One agent action may need several requests. Capacity/model quality can vary. Account/provider privacy policies still apply. Sources: [authorization](https://openrouter.ai/docs/guides/overview/auth/oauth), [free route](https://openrouter.ai/openrouter/free), [free-model limits](https://openrouter.ai/collections/free-models), [tools](https://openrouter.ai/docs/guides/features/tool-calling).

### Gemini

Implemented the user's AI Studio API-key connection. The key is sent in `x-goog-api-key`, never in a URL. Connecting checks model metadata/access without generating billable text, then saves the key in a separate DPAPI file. The default model is `gemini-3.8-flash`, listed with a Free tier on the check date; model availability is provider-controlled. No automatic alternative model is selected.

Native `generateContent` includes host instructions and `parametersJsonSchema` function declarations. Full model parts, opaque `thoughtSignature` values and call IDs are preserved within the request; hidden thought parts are neither displayed nor logged. Function responses carry actual world-tool results back to the model. Only a completed STOP response can execute tools or complete a reply; safety/truncation/quota failures are surfaced. Output is displayed after the completed request.

Free API access is separate from the consumer Gemini chat app. The user must use a **Free-tier Google project** for no-cost inference. A billing-enabled project can incur Google charges; Ecaty cannot force a paid project onto its free allowance, so the connection sheet says Google controls billing/data use. Free-tier data use and availability depend on Google's terms/account/region. Sources: [keys](https://ai.google.dev/gemini-api/docs/api-key), [billing](https://ai.google.dev/gemini-api/docs/billing), [pricing](https://ai.google.dev/gemini-api/docs/pricing), [function calling](https://ai.google.dev/gemini-api/docs/function-calling), [native API](https://ai.google.dev/api/generate-content).

### Reusable integration and verification

`AgentProviderHub` composes provider registrations and their lazy adapters. Ecaty remembers only the last selected service ID in PlayerPrefs; secrets live in provider-specific protected stores outside the repository. The generic package still has no Flecs dependency. The host supplies the same tools/instructions to every adapter; model-specific protocols stay in their adapters. New service loops limit 10 rounds/24 calls and bound inputs/results/HTTP replies. An unavailable model/quota fails without forwarding the request to a different provider.

`Ecaty.Editor.MultiProviderVerification.Start()` writes `Verification/provider-checks.json`: real callback HTTP and world/tools with synthetic service transport. It covers state/PKCE, credential restoration/local disconnect, paid-ID rejection, cancellation, 401/429, incomplete/malformed replies, unknown tools, signature replay, actual color/scale/readback, UI provider selection and conversation isolation. Existing ChatGPT checks remain separate. These tests do not prove live provider eligibility, availability or generated Script quality. OpenRouter browser authorization, protected credential restoration after Play restarts and live free inference passed on 2026-11-03. The selected model inspected both entities, applied one valid Script, checked the result and completed in about 11 seconds. Actual cube color/scale, unchanged sphere and Presented readback were independently checked. See `Verification/live-provider-checks.json`; it contains only public provider/model names, tool names and verification results. ChatGPT/Gemini still need their own live eligibility/inference checks. After connection, `Ecaty.Editor.LiveProviderVerification.Start()` runs a real cube change and saves only pass/fail/object-state checks; it does not save the assistant reply or credentials. The final Windows player and actual account-panel layout are checked separately in `player-checks.json` and `provider-ui-checks.json`.

Only Windows/Mono is currently verified. Mobile needs secure target-specific stores and browser return handling as well as Flecs target libraries. Windows local inference and speech are now installed and verified separately; see LOCAL-AI.md. Mobile embedded inference remains pending.

## UI on Unity 6.6

Ecaty now targets 6000.6.7f1 and hosts UI through PanelRenderer, using its versioned reload callback instead of UIDocument.rootVisualElement. Input/send/account/voice icons remain provider-neutral. No greeting, onboarding copy or engine narration is added to the app. The UI uses custom styling with a centered composer and optional collapsible history.

The host supplies explicit Flecs 4.1.6 grammar guidance in both private instructions and the apply_script description. A real-model failure demonstrated that bad dotted assignment can create a child entity and shadow a component on a subsequent retry. Script remains non-transactional: inspect after failures or reset the development world; do not claim rollback or guaranteed arbitrary model correctness. The fixed free coding model passed the documented case, which is not a reliability guarantee for every request.

## Browser logger and response style

The independent browser logger is now implemented. See [BROWSER-TRACE](BROWSER-TRACE.md) for live pairing, opt-in LAN inspection, phone setup and offline HTML/JSONL use. It shares the existing redacted collector and does not expose world-editing endpoints. The private host instructions now request only a short natural attribute value for simple questions (for example Reddish-orange.), with technical details only when requested. This is model guidance, not a guarantee or destructive post-processing of replies.

## Reactive behavior tools (2026-11-03)

The host now also supplies `inspect_behavior`, `apply_behavior` and `observe_over_time`. `apply_script` remains a one-time edit; `apply_behavior` installs/replaces the complete native managed AgentBehavior module. Flecs reevaluates dependencies when Clock or external components change. The agent reads existing source before replacing it, preserves unrelated rules and samples advancing native/Presented state before claiming animation. No provider-specific execution mechanism or Lua is involved. [Runtime syntax, workflow and limits](RUNTIME-BEHAVIORS.md).

The native/Unity/Windows checks use supplied source and real local objects. No new inference call was made for this milestone, so generated behavior quality with a live model remains to be tested by the user.
