# Project structure

```text
Assets/
  __Common/
    Bootstrap/         # host startup; Resources/RuntimeComposition.asset holds references only
    Theme/             # Theme.asset, Runtime, Editor, Styles and shared resources
    Agent/             # Agent.asset, Runtime, Editor, Presentation and prompts
  _Features/
    Gallery/           # Gallery.asset, Runtime, Editor, Resources, Tests
    World/             # World.asset, Runtime, Editor, Environment, Resources
    Creation/          # Runtime, Editor, Resources; Creation.asset when needed
  _Settings/           # Runtime, Resources/UI; Settings.asset when needed
    Project/Rendering/ # URP/project-owned configuration assets
    Project/Input/     # global InputSystem_Actions input asset
  _Adapters/           # Adapters.asset, Runtime/Vendor and Editor integration
  _Scenes/
  _Tests/              # cross-feature Editor/Integration checks and runtime fixtures
Packages/
  com.runtime.flecs/          # native session/view bridge, separate runtime/editor asmdefs
  com.runtime.ai-agent/       # reusable agent/UI/providers, separate runtime/editor asmdefs
  com.runtime.capabilities/   # reusable lifecycle/schema/Unity adapters, runtime asmdef
Native/Core, Native/App    # unchanged native ownership and stable C ABI
  Core/vendor/{flecs,behaviortree,clipper2,tinysoundfont}  # submodules of Ecaty forks (pin = commit)
Tests/, Tools/, docs/      # runners/manifests, developer utilities and handoff
```

Each owner's primary config asset sits directly at its base and uses its name: Gallery/Gallery.asset, World/World.asset, Theme/Theme.asset, Agent/Agent.asset and _Adapters/Adapters.asset. Do not add another Config folder. Secondary typed assets stay with their responsibility (World/Environment/Environment.asset; Agent/Presentation/PanelSettings.asset). Settings does not collect other modules' configs. RuntimeComposition is the reference-only startup registry; it retains its Resources load key and injects the owner assets. Config is separate from player/world saves.

Runtime-loadable Flecs definitions, shaders, examples and prompts remain in their owner's Resources folder for now. Their load keys do not dictate their feature's source layout. The reference manifest records Resources loads and direct startup-config references. No generic package depends on an application's Assets paths or feature types. The capability package gets audio-bank data through an explicit host provider; optional vendor adapters remain independently import-gated in _Adapters.

The bridge, agent and capability packages are real independent assemblies; agent code has no Flecs dependency. App feature folders currently share Assembly-CSharp: some vendors compile there and existing World/Creation/Settings types reference each other. This migration does not claim that every feature is an independently distributable package. Further assembly extraction requires contract/dependency work, not circular references or vendor modifications. Editor folders exclude their sources from player builds; editor calls within runtime files use UNITY_EDITOR guards. Tests own their test asmdefs. Run-PlayerScripts compiles Windows player code and verifies that owned Editor sources do not enter player assemblies; Run-All includes it.

GUID-preserving move inventory: Tests/Fixtures/modular-layout-migration.json. Runtime/reference audit: Runtime > Tests > Resource references, or Tests/Run-Resources.cmd. Generated artifacts remain ignored under Tests/Results. Technical package/type/source/save/auth identities are retained for compatibility. Vendors, ProjectSettings' required location, native Core/App, accounts and user saves are unchanged.

Only the crystal gallery presenter remains. GalleryPreviewEntry and the GalleryCatalog resource are current contracts; flat-table/furniture code, its shader and its old checks were retired. Inactive saved records remain; archive UI/carousel/config are deferred. The input actions GUID/global assignment survives its move under _Settings/Project/Input. Migration audit distinguishes preserved active assets from deliberate retirements.

Naming: identifiers carry no product name. App code uses namespaces `Game`, `Game.Editor`, `Game.Tests`, `Game.Adapters`; shared packages use `Runtime.Flecs`, `Runtime.Agents`, `Runtime.Capabilities`; plugin gates are `CAP_*`. Submodule config names in `.gitmodules` and repo URLs keep their original names.
