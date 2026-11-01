# Ecaty

An exploratory interactive creation app. Native Flecs C++ owns the world; Unity presents it; an AI agent inspects and changes it at runtime.

1. Clone recursively and install Git LFS: `git clone --recurse-submodules https://github.com/Ecaty/Ecaty.git`.
2. Open in Unity **6000.6.7f1**, Windows x64. A matching native plugin is included.
3. Press Play. Use the account icon to connect an eligible ChatGPT account in your browser.
4. Try **“Make the cube blue and twice as big.”** The small interface uses bundled Google Material Symbols.

Developer tools: [Flecs Explorer](https://www.flecs.dev/explorer/?host=localhost:27750) while running; **Ecaty > Agent Trace** for collapsible requests, queries, Script and results. Logs stay local in ignored `Logs/Agents/`.

The native/Unity loop and account protocol are verified with synthetic service responses. Live ChatGPT authorization/inference still needs an eligible account test. Voice, scanning, physics and multiplayer are not implemented.

New chat/account: start with [AGENTS.md](AGENTS.md). [PROJECT](docs/PROJECT.md) records decisions; [DEVELOPMENT](docs/DEVELOPMENT.md) covers builds/workflow; [AI-INTEGRATION](docs/AI-INTEGRATION.md) covers sign-in/tools; [STATUS](docs/STATUS.md) records actual verification and limits.
