# Runtime plugin map

Earlier review logs (gallery implementation inspection, user-owned candidates, shader authoring, Feel/Amplify use): [history/RUNTIME-PLUGINS-review-log-2026-11.md](history/RUNTIME-PLUGINS-review-log-2026-11.md). Current status: [RUNTIME-CAPABILITIES](../Packages/com.runtime.capabilities/Documentation~/RUNTIME-CAPABILITIES.md), [RUNTIME-STACK](../Native/Core/docs/RUNTIME-STACK.md).

## Asset map

Checked against the local Ecaty checkout on 2026-11-06. Proposed expansion and source references: [UNITY-CAPABILITY-ROADMAP](UNITY-CAPABILITY-ROADMAP.md). Integration means configuring the purchased plugin through its runtime API, not reproducing or redistributing vendor source. Vendor files stay out of the public repositories.

| Category | Asset / engine | Local state and runtime use | Recommendation |
|---|---|---|---|
| Organic modeling | MudBun | Imported; existing recipe adapter creates brushes and pivots during play. RuntimeMudRenderer computes after animation. | Keep for soft, connected shapes; art direction is not fixed to clay. |
| Curves / areas / structures | Spline Mesher Pro 1.0.16 | Imported as `xyz.staggart-creations.splinemesher-pro`. Runtime source exposes SplineCurveMesher, SplineFillMesher, SplineMesher.Rebuild, settings, generated mesh containers and rebuild events. No Flecs adapter yet. | Best next geometry adapter: Script-defined open curves or closed area boundaries, profile/material references -> Unity Splines -> curve extrusion or filled-area mesh generation. It supports both line/curve and area workflows. Rebuild on edits, measure cost before continuous deformation. |
| Modular buildings | Cozy Builder 0.5.6 | Imported as com.projectdawn.cozy-builder. Runtime CozyRenderer.Build/Clear and generated mesh/GameObject paths inspected. No Flecs adapter or Ecaty player test yet. | Keep for buildings using prepared construction kits and runtime configuration. |
| Vegetation | Broccoli Tree Creator | Imported in Assets/Waldemarst. Runtime example loads a pipeline through TreeFactory.LoadPipeline and generates trees with Spawn. No Flecs adapter or Ecaty player test yet. | Keep for runtime variations using configurable tree pipelines. |
| Camera | Cinemachine 6.6.0 | Installed; no Ecaty adapter. | Runtime target/framing/blending capability. |
| Presentation sequences | Timeline 6.6.0 and Unity Playables | Installed; runtime track/clip creation and graph APIs available. No Ecaty sequence adapter. | Flecs controls sequence intent; Unity coordinates audio/animation/cameras. Save reconstruction data, not Editor asset operations. |
| Ground locomotion / collision | Unity CharacterController | Runtime adapter now consumes MoveIntent and publishes actual ActorMotion. Independent actor capsules and static slab collider; no general rigid-body/joint adapter yet. | Keep one solver. User, goal-follow and Script use the same motor. Vehicles/flight require different motor capabilities. |
| Creature locomotion | FImpossible Legs Animator | Imported; runtime AddComponent, explicit step-curve initialization, actual hips/three-pivot leg chains, procedural idle-glue stepping, ground mask and movement/grounded signals. No imported animation clip or skin required. | Reuse, with scale/foot/gait tuning and valid generated chains. Runtime setup now assigns leg sides/opposite pairs before initialization, enabling alternating steps. Animator binding alone does not prove stepping quality. |
| Body response | FImpossible Leaning Animator | Imported; runtime base/spine references. | Reuse on a torso branch; avoid competing writers on the same pivot. |
| Secondary motion | Boing Kit | Imported; runtime BoingBones on separate chains, visible brush nodes excluded. | Reuse for ears/tails/spring branches. Do not let it drive the leg chain or physics root. |
| Feedback / audio / camera | Feel | Imported. MMF_Player supports AddFeedback, Initialization and PlayFeedbacks at runtime; feedbacks are not limited to Inspector authoring. No Flecs adapter yet. | Next event adapter: Flecs event/sequence plus registered feedback type/assets -> a runtime player. Audio feedback needs actual clips or a synthesizer; Feel does not invent sounds. |
| Navigation | Unity AI Navigation | Installed; no runtime adapter. | Add only when direct goal steering needs obstacle avoidance/pathfinding. Runtime surface builds have costs. |
| Reach / aim / constraints | Unity Animation Rigging 6.6.0 | Installed; local RigBuilder runtime Build/Clear/Evaluate APIs verified. No Ecaty adapter or use yet. | Generate constraints from pivot roles and targets; keep one pose writer per branch alongside FImpossible and Boing. |
| Spatial tools / layouts | Flexalon 4.4.2 | Imported; runtime layout/constraints/interactions inspected. No Ecaty adapter yet. | Use for world-space tools. Route camera/input explicitly and avoid two transform writers. |
| GPU / particle effects | Unity VFX Graph 17.6.0 | Installed; no Ecaty effect adapter. ParticleSystem also available. | Runtime parameters/events/mesh/texture bindings to prepared effect graphs; evaluate device support. |
| Touch / controller input | Unity Input System | Installed; WASD, gamepad and ground-pointer commands feed the same runtime schema. | Keep input separate from the motor and behavior rules. |

## Other owned assets

The supplied audit lists Dreamteck Splines, Prefab World Builder, MicroSplat Runtime Procedural Texturing, Traversal Pro, Malbers Animal Controller, Vehicle Physics Pro Community, Playmaker, Love/Hate, Lean Touch and Dialogue System. These are inventory candidates, not verified installed runtime dependencies. Clayxels is excluded as requested. Digger and Archimatix are excluded from the current direction; destruction is out of scope.

Prioritize one capability at a time: Spline Mesher for paths/structures; Feel for event feedback; navigation only when needed. Specialized controllers can be adapters if they accept runtime-generated geometry/rigs and expose runtime configuration. A controller expecting preauthored animations/avatars is not automatically a universal creature motor. Avoid multiple spline/IK/controller frameworks for the same responsibility.

## Boing EntityId compatibility

The locally imported version already uses UnityEngine.EntityId for Params.InstanceID and lookup keys. In `Assets/Boing Kit/Script/BoingBehavior.cs`, the requested assignment is now `Params.InstanceID = GetEntityId();`. Do not hash to int then implicitly convert back. That loses the typed identity and triggers the obsolete conversion. Reapply this one-line patch after a vendor reimport if upstream has not fixed it; vendor source is ignored by Git. Other preexisting EntityId migration edits are preserved.

## Adapter contract

Store versioned configuration, relationships and reusable source in Flecs. An adapter validates a changed recipe, resolves stable entity/part and asset references, creates/configures the plugin at runtime, and reports realized state/errors back. One owner writes each transform per stage: motor root -> body/leg procedural pose -> separate secondary chains -> MudBun geometry. No model call is required each frame. Versioned local experience save/resume now restores managed sources and selected model/rig/motor/brush/Persistent state. Pending awaits restart; this is not a full VM/physics-solver checkpoint.

## Primary references

- [Spline Mesher Pro, author listing](https://assetstore.unity.com/packages/tools/modeling/spline-mesher-pro-338468) and the imported package's Runtime API.
- [Cozy Builder documentation](https://lukaschod.github.io/cozy-builder-docs/manual/index.html); imported runtime source inspected, integration/build verification still needed.
- [Feel runtime player creation](https://feel-docs.moremountains.com/mmf-player.html).
- [Boing Kit manual](https://longbunnylabs.com/boing-kit-manual/).
- [Legs Animator author manual](https://www.filipmoeglich.pl/download/Legs%20Animator%20-%20User%20Manual.pdf).
- [CharacterController.Move](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CharacterController.Move.html).
- [Dreamteck author manual](https://dreamteck.io/page/dreamteck_splines/user_manual.pdf).
- [Digger runtime documentation](https://github.com/ofux/Digger-Documentation/blob/master/Runtime.md): runtime editing is the Pro feature; ownership of Digger alone does not establish that tier.
