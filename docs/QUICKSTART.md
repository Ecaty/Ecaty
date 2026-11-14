# The short workflow

**Input/AI -> Flecs Script/data -> native simulation + Unity/plugin adapters -> measured ECS results.** Simulation needs no AI calls per frame.

1. Open **Ecaty** in Unity 6000.6.9f1. Normal Play opens the world gallery.
2. Run **Runtime > Runtime engine > Prepare capability assets** once, then **Start capability workshop**. This is a developer fixture.
3. In Flecs Explorer query `CapBinding`, `CapState`, `CapType` or `CapAsset`. Change a binding's parameters while playing.
4. One binding entity owns one capability. Several bindings can share a target; `CapDependsOn` links their updates. One solver owns movement.
5. Saved experiences rebuild sources, configurations and selected state without asking AI to recreate them.

Working: native tasks/sound/footprints plus geometry, physics, navigation, IK, camera, effects, Feel, layouts and reconstruction. Windows Editor/player checks pass. UniStorm/Crest rendering and mobile still need verification. The spatial creation UI is a separate step.

[Capability details and code map](RUNTIME-CAPABILITIES.md) Â· [Current status](STATUS.md)

Local AI: Play -> chat/account -> **On device** -> **Start**. Tap microphone -> stop -> review draft -> send. No AI account needed. [Setup, model limits and tests](LOCAL-AI.md) Â· [Short backlog](BACKLOG.md) Â· [Workshop/theme direction](PRODUCT-DIRECTION.md).
