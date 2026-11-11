# Archive: walking actor (motion for created models)

Snapshot tag: `archive/walking-actor` (parent e87cb79; submodules Native/Core 6f4c94a, Native/App f5ad452, flecs-unity e52267e, ai-agent 6605d12). Restore with `git checkout archive/walking-actor -- <path>`; `git submodule update` for the matching native/bridge code.

Status 2026-11-11: still on main. Planned: refactor to a generic Motion capability during creation-tools design (BACKLOG Decisions); this file records where the pieces are in case parts are removed.

| Piece | Path | What it does |
|---|---|---|
| Schema | Assets/_Features/World/Resources/Runtime/Schemas/ActorSchema.flecs | ActorControl{mode,speed,acceleration}, MotorPolicy{spatial,gravityScale}, ActorRig (JSON), ActorMotion (actual pose/velocity/grounded), MoveIntent/SpatialIntent |
| Motor rules | .../Runtime/Behaviors/ActorBehavior.flecs | `RuntimeMotorDriver` template: mode 0 idle, 1 player (PlayerMotion/LiftIntent), 2 follow (MoveGoal), 3 script (ScriptInput) → intents |
| Spatial example | .../Runtime/Behaviors/RuntimeSpatialBehavior.flecs | hover/height hold from ActorMotion feedback |
| Engine registry | .../Runtime/Schemas/EngineSchema.flecs | `Motor` engine capability description |
| Unity adapter | Assets/_Features/World/Runtime/DioramaRuntime.cs | CharacterController body, executes intents, writes ActorMotion; rigs Legs Animator (hips, leg chains, lean, boing) from model pivots |
| Experiences | Assets/_Features/World/Runtime/RuntimeExperience.cs | per-model actor + driver modules (ActorDriver / Drivers.<id>) |
| AI tools | Assets/__Common/Agent/Runtime/EcatyAgentTools.cs | `inspect_actor`, `configure_actor`; observe_world reports ActorMotion |
| Startup | Assets/__Common/Bootstrap/EcatyBootstrap.cs | adds DioramaRuntime to the world |
| Checks | Assets/_Features/World/Editor/RuntimeActorVerification.cs (gait + movement + focus), RuntimeSpatialVerification.cs, RuntimeExperienceVerification.cs; LocalAiVerification / LocalAiSmoke | Extended suite |
| Vendor | Legs Animator (FImpossible) | procedural legs; presentation only |
