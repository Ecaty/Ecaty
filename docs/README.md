# Documentation index

Default read before changing code: [AGENTS](../AGENTS.md), [STATUS](STATUS.md), [BACKLOG](BACKLOG.md), [PROJECT-STRUCTURE](PROJECT-STRUCTURE.md). Everything else is read on demand by area.

## Repos and backlogs

The runtime engine is split into reusable repos (submodules). Each has its own `AGENTS.md` (rules and doc index; its `CLAUDE.md` loads it automatically when you work in that folder), `BACKLOG.md` (its own tasks) and docs. This repo holds product docs, status and the cross-repo overview.

| Repo (path) | Role | Entry point | Backlog |
|---|---|---|---|
| this repo | product: Unity app, features, product docs and plans | [AGENTS](../AGENTS.md) | [BACKLOG](BACKLOG.md) |
| `Native/Core` (Ecaty/flecs-native) | engine: native Flecs runtime, C ABI, vendor forks | [AGENTS](../Native/Core/AGENTS.md) | [BACKLOG](../Native/Core/BACKLOG.md) |
| `Packages/com.runtime.flecs` (Ecaty/flecs-unity) | engine: Unity bridge and presentation | [AGENTS](../Packages/com.runtime.flecs/AGENTS.md) | [BACKLOG](../Packages/com.runtime.flecs/BACKLOG.md) |
| `Packages/com.runtime.capabilities` (Ecaty/capabilities-unity) | engine: capability contracts, lifecycle, adapters | [AGENTS](../Packages/com.runtime.capabilities/AGENTS.md) | [BACKLOG](../Packages/com.runtime.capabilities/BACKLOG.md) |
| `Packages/com.runtime.ai-agent` (Ecaty/ai-agent-unity) | engine: AI chat, providers, local inference, traces | [AGENTS](../Packages/com.runtime.ai-agent/AGENTS.md) | [BACKLOG](../Packages/com.runtime.ai-agent/BACKLOG.md) |
| `Native/App` (Ecaty/ecaty-native) | product: app-specific native systems | [AGENTS](../Native/App/AGENTS.md) | product BACKLOG |

Engine docs live in their repos (`Native/Core/docs/`, packages' `Documentation~/`); the sections below link them.

## Start here
- [STATUS](STATUS.md): current state and the exact next action.
- [BACKLOG](BACKLOG.md): canonical short task list and recorded decisions.
- [PLAN-2026-11](PLAN-2026-11.md): current multi-session plan.
- Engine backlogs: see the table above; read the one for the repo you work in.
- [PROJECT-STRUCTURE](PROJECT-STRUCTURE.md): where code, config and assets belong; assembly boundaries.

## Architecture
- [PROJECT](PROJECT.md): approved architectural decisions.
- [FLECS-ARCHITECTURE-AUDIT](../Native/Core/docs/FLECS-ARCHITECTURE-AUDIT.md): native/bridge audit.
- [FLECS-NOTES](../Native/Core/docs/FLECS-NOTES.md): Flecs 4.2 features by topic (prefabs, Script, relationships, queries, observers, saves) with recommendations for us.
- [RUNTIME-STACK](../Native/Core/docs/RUNTIME-STACK.md), [RUNTIME-ENGINE](RUNTIME-ENGINE.md), [RUNTIME-COMPOSITION-DESIGN](../Native/Core/docs/RUNTIME-COMPOSITION-DESIGN.md): runtime layers, models, behaviors, composition.

## Third-party code
- [THIRD-PARTY](../Native/Core/docs/THIRD-PARTY.md): vendor forks, checkout, weekly upstream check and updates.

## Capabilities and plugins
- [CAPABILITY-CONTRACT](../Packages/com.runtime.capabilities/Documentation~/CAPABILITY-CONTRACT.md), [RUNTIME-CAPABILITIES](../Packages/com.runtime.capabilities/Documentation~/RUNTIME-CAPABILITIES.md): capability components and adapters.
- [RUNTIME-PLUGINS](RUNTIME-PLUGINS.md): owned plugin map and adapter contract.
- [UNITY-CAPABILITY-ROADMAP](UNITY-CAPABILITY-ROADMAP.md): product-facing tools and capability decisions.

## AI
- [AI-INTEGRATION](../Packages/com.runtime.ai-agent/Documentation~/AI-INTEGRATION.md): providers, accounts, tools. [LOCAL-AI](LOCAL-AI.md): local inference and speech. [BROWSER-TRACE](../Packages/com.runtime.ai-agent/Documentation~/BROWSER-TRACE.md): developer traces.

## Product
- [PRODUCT-DIRECTION](PRODUCT-DIRECTION.md), [GALLERY-WORLDS](GALLERY-WORLDS.md), [WORKSHOP-CHAIN](WORKSHOP-CHAIN.md), [DISTRIBUTION](DISTRIBUTION.md).

## Process
- [DEVELOPMENT](DEVELOPMENT.md): builds and workflow. [TESTING](TESTING.md): runners, suites, verifier inventory. [RESOURCES](RESOURCES.md). [QUICKSTART](QUICKSTART.md).

## Archive
- `history/` and `prototypes/`: read only when the current docs lack something.
