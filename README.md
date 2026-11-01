# Ecaty

An exploratory interactive creation app: users give life to their creations with an AI agent. Native Flecs C++ owns the world; Unity displays it. The initial milestone is a small, working runtime foundation.

1. Clone with submodules: `git clone --recurse-submodules https://github.com/Ecaty/Ecaty.git`.
2. Open with Unity **6000.4.10f1** on Windows x64. A matching native plugin is included.
3. Press Play: a rotating cube, sphere and label appear.
4. Connect [Flecs Explorer](https://www.flecs.dev/explorer/?host=localhost:27750) to `localhost:27750` while Play is running. Inspect/query/edit entities or the `World` managed Script.

Start a new AI-assisted work session by asking it to read [AGENTS.md](AGENTS.md). Architecture and product intent: [PROJECT](docs/PROJECT.md). Setup/build/tests: [DEVELOPMENT](docs/DEVELOPMENT.md). Completed work and next tasks: [STATUS](docs/STATUS.md).

The reusable [AI-agent package](https://github.com/Ecaty/ai-agent-unity) contains minimal chat UI assets and provider/tool/speech interfaces. It is not mounted in the app yet. ChatGPT sign-in/inference, speech, scanning, physics and multiplayer are **not implemented**.

Use Visual Studio for Unity C# and native C++: open the `Native` folder as a CMake project, or run `Native/Generate-Solution.ps1` for an optional generated native solution. See DEVELOPMENT for the exact workflow.
