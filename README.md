# Runtime creation prototype

An exploratory interactive creation app. Native Flecs C++ owns world data and rules; Unity runs rendering and capability adapters; an AI agent inspects and changes it at runtime.

1. Clone recursively and install Git LFS: `git clone --recurse-submodules https://github.com/Ecaty/Ecaty.git`.
2. Open in Unity **6000.6.7f1**, Windows x64. A matching native plugin is included. Import the licensed MudBun, FImpossible animators and Boing Kit locally; see [dependencies](docs/RUNTIME-PLUGINS.md).
3. Press Play. Use the account icon to choose On device, OpenRouter (free coding model), ChatGPT (eligible plan), or Gemini (your AI Studio key).
4. Normal startup opens the world gallery. For the separate developer ground/creature fixture in Play Mode, choose **Runtime > Runtime engine > Start creature test**. Focus Game view: WASD/gamepad moves it; click the ground to follow a point. [Runtime workflow](docs/RUNTIME-ENGINE.md).

Developer tools: [Flecs Explorer](https://www.flecs.dev/explorer/?host=localhost:27750) while running; **Runtime > Agent Trace** for collapsible requests, queries, Script and results. Logs stay local in ignored `Logs/Agents/`.

OpenRouter sign-in and a real free-model tool loop are verified: the model inspected the demo, changed the cube to blue at twice its scale, and Ecaty checked the actual objects/readback. ChatGPT/Gemini protocols pass synthetic-service checks; their live eligibility still needs account-specific testing. The current runtime test adds a ground motor and procedural creature bindings; phone targets, voice, scanning and multiplayer remain future work.

New chat/account: start with [AGENTS.md](AGENTS.md); [docs/README.md](docs/README.md) indexes every doc. [PROJECT](docs/PROJECT.md) records decisions; [DEVELOPMENT](docs/DEVELOPMENT.md) covers builds/workflow; [AI-INTEGRATION](Packages/com.runtime.ai-agent/Documentation~/AI-INTEGRATION.md) covers sign-in/tools; [STATUS](docs/STATUS.md) records actual verification and limits.

Browser logger: **Runtime > Agent Trace (Browser)**, or account > **Trace** in a development app. Offline without Unity: run Tools/TraceViewer/Open-TraceViewer.ps1 and import a JSONL log. [Workflow, phone setup and limits](Packages/com.runtime.ai-agent/Documentation~/BROWSER-TRACE.md).

**Runtime proof:** Play -> **Runtime > Runtime engine > Start spatial demo**; change the target altitude in Flecs and use **Land spatial demo** to land. [Measured runtime workflow](docs/RUNTIME-ENGINE.md).

**Runtime behavior:** [reactive Script and runtime engine](docs/RUNTIME-ENGINE.md). Cube/sphere samples are explicit regression fixtures, not normal startup.

**Capability workshop:** Runtime > Runtime engine > Prepare capability assets, then Start capability workshop. [Short workflow](docs/QUICKSTART.md), [implemented adapters and limits](Packages/com.runtime.capabilities/Documentation~/RUNTIME-CAPABILITIES.md). Native behavior, sound and geometry plus Unity/plugin instances compose through reflected ECS bindings; no inference is needed per frame.

[Project structure and assembly boundaries](docs/PROJECT-STRUCTURE.md) · [Test runners](docs/TESTING.md). Feature configs live beside their source; startup composition contains asset references only.
