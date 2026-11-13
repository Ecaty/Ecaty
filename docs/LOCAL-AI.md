# Local AI and dictation

## Try it (Windows)

1. Open Ecaty and press Play. Open the chat, choose the account icon -> **On device** -> **Start**. Existing provider preferences are preserved, so switch explicitly if it still shows a cloud provider.
2. Type a request, or attach a photo using the existing Editor photo picker (the player image picker/camera is still pending). The local provider uses the same Flecs query/Script/model tools as the cloud adapters.
3. Tap the microphone to record, tap its stop icon to transcribe. Text appears in the draft; review/send it yourself. Closing chat, losing focus or cancelling stops recording. Maximum recording is 30 seconds; automatic stop is scheduled at 29 seconds.

Installed here in ignored `LocalData/AI`. On a fresh checkout, run `python Tools/LocalAI/setup.py` (about 3.6 GB of models plus runtime downloads; Python 3.10+). No AI account/API key required; internet is needed only for installation. No automatic cloud fallback. Model weights are not public Git assets.

## What actually runs

User text/photo -> llama.cpp loopback server -> validated tool requests -> existing application tools -> native Flecs + Unity adapters -> tool results -> local assistant. Whisper.cpp receives the recorded WAV on loopback and returns text. Unity Microphone captures audio only after the user taps record. The app does not deliberately write recordings to disk. OS memory/paging and optional developer traces are separate privacy considerations.

The reusable `com.runtime.ai-agent` package owns provider/speech/process lifetimes and UI. It has no Flecs dependency. GameBootstrap supplies the model directory, services, instructions and world tools. Servers start lazily, bind 127.0.0.1, use fixed ports 18080/18081, and owned processes stop with the session. The panel releases the services before cancelling HTTP requests during Play-mode exit or domain reload. A healthy already-running compatible server can be reused and is not killed. This prototype loopback interface is not a multi-user authentication/security boundary.

## Model choice and honest limits

Detected development hardware: i9-9900K, 32 GB RAM, RTX 2070 8 GB. Initial model is **Qwen3.5-4B Q4_K_M**, with its F16 vision projector on CPU; multilingual Whisper **base** runs on CPU. Vulkan offloads language inference to GPU and leaves some memory for rendering. Complex scenes may need fewer GPU layers/lower context to avoid competing with graphics.

The model does not arrive knowing Ecaty. Instructions, tool schemas and live query results teach it the current engine interface. Small-model tool reliability/geometry quality must be measured; it cannot be promised to match frontier ChatGPT/Claude. Passing one primitive-edit test would not prove complex creature/behaviour authoring. Existing simulation remains local and runs without asking an LLM each frame.

There is no single "strongest llama.cpp model" independent of hardware/task. [Qwen3.8-27B](https://huggingface.co/Qwen/Qwen3.8-27B) is a current larger multimodal candidate for GGUF testing on substantially more memory; partial CPU offload can run larger models but may be too slow while playing. [Qwen3-Coder-Next](https://huggingface.co/Qwen/Qwen3-Coder-Next) is another code/tool candidate with a much larger total weight footprint, and is text-only. Neither has been installed/qualified here. Model claims/benchmarks are not evidence of our engine success rate.

To replace a model, stop local AI, install a compatible licensed GGUF/projector, then edit ignored `LocalData/AI/config.json`: model, projector, contextSize, gpuLayers, threads. Paths must stay under that directory. Match the projector to the model; omit projector for text-only models. The installed binary uses Jinja tool templates. Reasoning is enabled for tool-bearing tasks, disabled for simple text/vision-only questions. Tool rounds/calls/argument/result sizes are bounded; this is not yet a hostile-code sandbox or complete moderation system.

## Measurements from the first successful Editor check

On this machine, one real three-tool edit (query, apply Script, verify query) took **13.6 seconds**. Whisper recognized its approximately 11-second public sample in **4.5 seconds including startup**. Model loading took **5.8 seconds with warm OS cache** (about 12 seconds on the first load); the tiny generated red JPEG test took **0.4 seconds warm**. These are individual fixture measurements, not averages or predictions for photographs/complex creations. Initial broad-query attempts failed; clearer query guidance and actionable size errors let the model complete the task. Full character/behaviour quality remains unqualified.

## Reproducibility and deployment

Installer pins llama.cpp b12177 (Windows Vulkan), whisper.cpp b5454, quantization revision and speech model revision, verifies binary/model SHA-256, and saves licenses plus `UPSTREAM.json`. Runtime/model binaries remain ignored. Selected Qwen weights are Apache-2.0; llama.cpp/whisper.cpp code is MIT. Verify exact dependency redistribution terms before packaging.

Editor uses `<project>/LocalData/AI`. Windows players use `<player-folder>/LocalAI`. Run `python Tools/LocalAI/stage.py path/to/Ecaty.exe` to copy the pack beside a player, or launch a development player with `-localAiRoot <absolute-pack-folder>` to avoid duplicating it. Do not put user conversations/accounts in the pack. No download UI/installer experience is implemented yet.

Mobile is **not implemented**: it needs llama.cpp/whisper.cpp embedded native libraries, appropriate GPU backend, permissions and smaller models, plus battery/memory/thermal testing. Windows helper processes are not a cross-platform mobile solution. There is no WebGL inference backend in this implementation.

## Verification and code map

- Runtime > Verification > Verify local AI protocols and dictation: synthetic transport + real UI lifecycle, cancellation, input validation, WAV conversion. No microphone is opened.
- Runtime > Verification > Verify local AI and speech: actual local runtimes, official whisper.cpp `samples/jfk.wav`, generated red JPEG and real native Flecs/Unity world edit. The installer fetches the pinned public WAV into ignored `Logs/LocalAiFixture/jfk.wav`. Results: `Verification/local-ai-live-checks.json`.
- The packaged Windows test also passed a real local ground-colour edit in 7.5 seconds, using the default staged model directory, with no cloud service. Build succeeded with 0 errors and 37 warnings (imported-plugin/development-build warnings remain). Physical microphone quality and photo-to-articulated-character quality require separate tests. Do not equate a fixture recording with a real mic test.

| Area | Source |
|---|---|
| llama.cpp tool/image protocol | Packages/com.runtime.ai-agent/Runtime/Providers/Local/LlamaCppProvider.cs |
| Windows local process ownership | same folder, LocalAiServices.cs and LocalAiLifetime.cs |
| Whisper HTTP and PCM conversion | Packages/com.runtime.ai-agent/Runtime/Speech/ |
| Tap-to-record and draft review | Runtime/UI/AgentChatController.cs and AgentChatView.cs |
| App-specific configuration/tools | Assets/__Common/Bootstrap/GameBootstrap.cs, WorldAgentTools.cs |

Upstream: [llama.cpp](https://github.com/ggml-org/llama.cpp), [function calling](https://github.com/ggml-org/llama.cpp/blob/master/docs/function-calling.md), [whisper.cpp](https://github.com/ggml-org/whisper.cpp), [Qwen3.5-4B](https://huggingface.co/Qwen/Qwen3.5-4B).

The November 7 shutdown checks cover pending startup, text and photo inference. The local photo fixture uses a structured recipe through the normal chat entry point and creates two solid MudBun brushes plus a pivot. See the current STATUS entry and the local-ai-shutdown and local-ai-photo reports under Verification.
