# Browser developer trace

This viewer is independent of Flecs Explorer. It uses the same redacted trace collector as **Runtime > Agent Trace** and does not invoke the AI, edit the world or expose the Flecs REST API.

## Open it

- Editor: **Runtime > Agent Trace (Browser)**. This also works outside Play mode; use the Source selector for a recent log.
- Running development app: account icon > **Trace** > **Open browser**. **Copy connection** copies a complete pairing link; paste it into a browser. A Windows development player can also be launched with `-agentTraceBrowser`.
- Offline, without Unity or a running app: run `Tools/TraceViewer/Open-TraceViewer.ps1`, then **Import log**. Alternatively, use **Save viewer** once in the browser. The exported HTML includes its CSS/JavaScript, but no logs or pairing key.

Editor logs are in the ignored `Logs/Agents/` folder. Development-player logs are in `Application.persistentDataPath/Diagnostics/Agents`. Existing JSONL logs are supported; old files need not contain the new sequence/session fields. File import stays in the browser, with no upload or AI request. **Export text** saves a readable report of the loaded requests, tool errors, inputs, results and timings. Embedded JSON such as a photo recipe is expanded. **Export JSONL** keeps the original event format for import and tooling. The live view and imports show the last 500 events; log segments rotate around 10 MB and imports are limited to 20 MB.

## Read a request

Requests group the user input, model name when supplied by the adapter, tool calls, timings, errors and final assistant reply. Expand a tool for its exact input/result. Query results and `Presented` observations reveal the path through Flecs and the actual presentation adapter.

Script inputs receive Flecs 4.1.6 syntax highlighting for keywords, strings, comments, numbers, variables, entity paths and component declarations. Queries and JSON results have separate formatting. **Copy raw** preserves the original text. Highlighting is a display lexer guided by the pinned tokenizer; it is not a parser, Script validator, sandbox or rollback mechanism. A later successful reply is shown as completed even if a recovered tool error occurred earlier; **Errors only** can still find it.

Use search, errors-only filtering, expand/collapse, pause/resume and the Source selector. Paused data remains bounded. Reconnect via a fresh app/menu link after a server restart or after changing LAN mode, because the pairing key changes.

## Phone and another computer

In a **development build**, account icon > Trace > **Allow local network inspection**, then **Copy connection**. Open that link on a computer on the same trusted network. Keep the phone app in the foreground. Local-network mode listens on IPv4 interfaces; the copy button selects an active IPv4 address. If a VPN or another adapter is selected, replace the hostname in the copied link with the phone's Wi-Fi IPv4 address, preserving the port and fragment.

The device must allow the app's local-network access, and the network/firewall must permit the selected port. No firewall rules are changed automatically. Android builds need INTERNET permission; iOS projects must provide the applicable local-network usage description/permissions. Normal mobile suspension rules apply; opening a browser on the same phone can suspend the app, so use a second device for live inspection.

The logger uses managed TCP and bundled web assets with no Flecs or provider dependency. Mobile builds still need the project's native Flecs library, platform account storage/callbacks and target-specific build setup. **This task verifies Windows/Mono, not Android/iOS devices or IL2CPP.** See [Unity Android permissions](https://docs.unity3d.com/6000.6/Documentation/Manual/android-permissions-in-unity.html) and [Apple local-network privacy](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy) when porting.

## Development boundary

The runtime viewer/capture starts only in the Editor and development builds. Normal release builds do not start this logger. Loopback is the default; LAN mode is an explicit developer action. LAN uses HTTP, so use a trusted development network. Possession of the pairing link permits reading private trace data for that running session. The key is sent in an authorization header, never an HTTP query string; its initial URL fragment is removed after the page loads. It is not an AI-provider credential.

All trace API routes require pairing. The server rejects cross-origin reads, non-GET requests, arbitrary file paths and overlarge imports. It serves only viewer assets, bounded trace pages and JSONL files in the configured log folder. Client text is rendered as text nodes, not HTML. Authentication exchanges and hidden reasoning are excluded; existing bearer/JWT/key redaction remains in place. Traces can still contain private messages/world data: never commit or upload them.

## Verification

- `Ecaty.Editor.BrowserTraceVerification.Start()` -> `Verification/browser-trace-checks.json`: real paired HTTP, pagination, denied unauthenticated/cross-origin/mutation requests, safe recent-file reads and collector redaction. Uses synthetic events and preserves prior traces.
- `Ecaty.Editor.BrowserTraceVerification.CheckChatUI()` in a verification Play session -> `Verification/chat-ui-polish-checks.json`: actual caret/selection, six-pixel scrollbar, bottom collapse/reopen, tail-follow and compact layout. It inserts then clears synthetic UI messages; do not run it over a user's live conversation.
- `Verification/browser-viewer-checks.json`: actual browser checks with synthetic data, including live pairing, offline import, syntax tokens, safe text, raw copying and standalone HTML export.
- `PlayerSmoke` checks that browser capture/server are present in a development player and absent in a normal release player. No inference call is made by these tests.
