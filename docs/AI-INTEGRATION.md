# ChatGPT integration: approved direction, not implemented

## Desired experience

The user wants their own AI account to power an in-app conversation, keeping model charges/account responsibility with the provider. Ecaty should present a very small chat input/history and microphone. The agent inspects the actual Flecs world using queries/reflection and changes it through Flecs Script, then inspects the result. It should receive an accurate capability/schema description rather than invented engine APIs.

## Verified documentation, 2026-11-01

OpenAI documents Sign in with ChatGPT for local personal/open-source projects and selected private clients. Eligible Plus/Pro users can authorize ChatGPT-plan inference without supplying an API key or partner client secret. Commercial distribution/availability must be checked again before committing to a launch model. A public repository alone is not a guarantee of every provider entitlement. Account/plan/model limits remain.

- [Official quickstart](https://developers.openai.com/siwc/quickstart)
- [Open-source app example](https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt)
- [Registration and sign-in](https://developers.openai.com/siwc/token-sharing-open-source/sign-in)
- [Preview limitations](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)

Use the official flow and revisit its current requirements when implementing. Do not reuse Codex/ChatGPT's existing credentials from disk, intercept web sessions or infer that identity-only login grants model use.

## Package boundary

All shared AI-agent UI, future provider/authentication clients and tool/conversation orchestration live in the public `Ecaty/ai-agent-unity` repository, embedded at `Packages/com.ecaty.ai-agent`. The first package version provides UI Toolkit assets, disposable view bindings and `IAgentProvider`, `IAgentAuthenticator`, `IAgentTool`, `ISpeechToText` contracts. It has no dependency on Flecs, native libraries or provider SDKs.

Ecaty supplies product instructions and Flecs world callbacks. Providers implement their own supported protocols and authentication; the reusable chat view is unchanged when a provider is replaced. Do not confuse a common interface with universal provider capabilities or subscription access. Do not introduce a fake runtime provider to imply ChatGPT works already.

## Planned Windows implementation

1. UI Toolkit conversation and input; Continue with ChatGPT opens the system browser.
2. Native-app OAuth/OIDC authorization with PKCE, random state and nonce, and a loopback callback listener. Official first registration uses `dynamic_agent_client`, a persistent per-host `ext_agent_host_id` and consistent `agent_name_hint=Ecaty`; the callback issues a client ID to retain per account/registration. Validate identity and granted inference scopes before treating the account as connected.
3. Store credentials using Windows-protected account storage outside the project. Refresh and revoke/sign out through supported flows. Keep token/code values out of logs, assets, Script entities, screenshots, commits and build artifacts.
4. Use eligible Responses API requests with local conversation history. At the checked preview: `stream=true`, `store=false`, input history supplied as an array. Several ordinary API parameters are unsupported; consult the limits page rather than copying generic API samples.
5. Expose narrow local tool functions for schema/world inspection, queries and Script application. Marshal world work onto Unity's main thread. The model does not call Unity APIs directly or receive unrestricted arbitrary code execution.
6. Preserve the distinction between provider authentication, app permissions, a user's chat history and native world state. The sign-in route does not give Ecaty access to the user's existing ChatGPT conversations.

## Microphone constraint

The checked ChatGPT-plan preview does **not** support audio/video input or the transcription API. Keep the microphone design, but select a separate supported speech-to-text implementation (possibly local) before promising it works through a ChatGPT subscription. Initial voice is intended as dictation, not a full realtime voice conversation. No speech engine or model files are installed yet.

## Current boundary

Reusable chat UI assets/bindings and provider/tool/speech contracts exist in the package, but are not mounted in the app. No sign-in UI, OAuth implementation, model calls, speech processing or production MCP server exists yet. Native REST/Explorer validates the ECS bridge but is not a secure product AI endpoint. A future tool adapter may call the same native operations locally; a remote connector would require a separately designed authenticated transport.
