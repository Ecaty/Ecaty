# Unity capability roadmap

Research: 2026-11-06. Proposal, not newly implemented functionality. Existing Windows checks do not prove these candidates on mobile, XR or IL2CPP. Flecs continues to own recipes, behavior and state; Unity/plugin adapters supply execution and measurements.

## Product-facing tools

The browser tool should be a live companion inspector. Follow the in-game selection by default; allow pinning. Show the selected world/object/recipe, meaningful parameter controls with previews, compatible targets, dependencies, current measured state and a source switch. A small shared-definitions pane shows users of a recipe. No node editor is required now. Both browser and world-space tools must use the same validated edit/undo/source operations and stable entity IDs; the browser is not another simulation.

Use recipe metadata for field labels, ranges, units, grouping, asset/target roles and compatibility. Distinguish authored values from transient solver outputs. Check source revisions before applying edits so stale browser state cannot overwrite a newer AI/user edit. Start with ordinary web controls and a source editor; choose a graph library only after an actual graph-shaped problem appears.

Live sharing can itself be gameplay: deliberately subscribe to a friend's recipe channel and receive later updates automatically. Offer snapshot/fork/freeze alongside the live link. Keep shared definitions separate from each world's bindings and instance state. Updates require compatible installed capabilities; sharing Script cannot install an absent compiled adapter. Networking, version reconciliation and shared-library transport are not implemented.

A moon-gravity recipe can have different consequences in an aquarium, but lower gravity alone does not imply escaping fish. Swimming forces, buoyancy, fluid boundaries, collisions and behavior must participate in the physical model. Global Physics.gravity is not a sufficient per-world design if multiple worlds share a Unity scene.

## Capability decisions

| Capability | Evidence / existing inventory | Recommendation |
|---|---|---|
| Physical objects and mechanisms | Unity PhysX built in. Current Ecaty uses character capsules/static ground, not a general body/joint adapter. | Highest leverage: runtime rigid bodies, compound colliders, joints, impulses, scoped gravity/force fields, collision/trigger readback. One solver owns a body's actual pose. Flecs sends commands and receives observations. Live SDF rendering does not automatically provide cheap changing collision geometry. |
| Reach, aim, grasp and look | Animation Rigging 6.6.0 installed, unused by Ecaty. Local RigBuilder exposes Build, Clear, Evaluate, SyncLayers. | Next rig adapter. Generate constraints from part roles and explicit targets; rebuild when hierarchy changes. Generic transform constraints can serve brush-pivot hierarchies; verify the actual Animator/graph setup without a skin. Keep FImpossible responsible for leg stepping and Boing for separate secondary branches. |
| Paths and obstacle avoidance | AI Navigation 2.0.16 already installed, no Ecaty adapter. | Use its runtime surface builds/updates and paths first. Feed path intent into the existing motor instead of letting two components move the actor. Surface navigation does not solve swimming/flying in a volume. A* Pathfinding is an alternative only if changing-world performance or graph requirements justify it. |
| Curves, filled regions and structures | Imported Spline Mesher Pro 1.0.16; Unity Splines 2.9.1 resolved. Runtime curve/fill rebuild APIs inspected. | Build the next geometry adapter for roads, banks, walls, platforms and filled shapes. Closed-area generation is part of the package, not only line extrusion. Throttle geometry/collider rebuilds. |
| Contextual spatial tools | Flexalon 4.4.2 imported; layout, constraints and interaction APIs inspected; no adapter. | Use for radial/arc tools and previews. Route interaction through the app's actual camera/input owner. Flexalon owns layout transforms; ordinary scene synchronization must not overwrite them. It does not provide glass rendering or voice recognition. |
| Sensory feedback and sound | Feel imported; runtime MMF_Player construction supported. AudioSource/Mixer/ParticleSystem built in. | Runtime feedback adapter with versioned registered feedback types and asset references. Contact, arrival and rule events trigger it. Existing Master Audio/BroAudio are later alternatives if audio management becomes a concrete bottleneck. Sound clips or synthesis still have to exist. |
| Camera direction | Cinemachine 6.6.0 is now installed; no Ecaty adapter. | Runtime follow, framing, target groups and shot blends. Camera intent/targets belong in Flecs; Cinemachine computes the shot. |
| Ropes, cloth, soft matter, 3D liquid | Obi family is a new candidate, not installed. Author documents runtime solvers, actors and generated blueprints. | Strongest new simulation family to trial. Products/licenses are separate. Start with one rope or soft-body case. Runtime generation can be expensive; MudBun-to-simulation representation and deformation need an explicit bridge. Choose CPU/GPU backend and budgets per target. |
| Water and fluid effects | FluXY and Crest 4 imported; local Crest documentation version is 4.23.0. No Ecaty adapters or new compatibility tests yet. | FluXY is 2.5D, useful for smoke/ink/surface effects, not a free 3D pouring solver. Crest provides wave/water and buoyancy facilities; ownership of an older version does not establish a current upgrade. Use simple water volumes/buoyancy when sufficient; use Obi Fluid only for actual volume/spill needs. |
| Buildings and vegetation | Cozy Builder 0.5.6 and Broccoli are now imported. CozyRenderer has runtime Build/Clear; Broccoli includes LoadPipeline/Spawn runtime examples. | Keep these choices for now. Build Flecs adapters around configuration and prepared construction kits/tree pipelines. Runtime API evidence is not a passed Ecaty player test. Digger and Archimatix are excluded from the current direction by user preference. |
| Active physical characters | PuppetMaster candidate; existing FImpossible, Boing and Unity Rigging already cover much pose work. | Consider only for physical balance/active ragdolls. It still needs valid joint/collider/rig setup. Do not add Final IK or another limb solver simply to duplicate the existing stack. |
| Runtime content libraries | glTFast and Addressables not currently listed in manifest. | glTFast for external glTF meshes/materials; Addressables for prepared Unity asset downloads/caching. Keep recipe/state persistence in Flecs. Downloaded content does not add new compiled algorithms to a shipped build. |
| Speech, AR and VR | Input System exists; no current speech/XR adapter. whisper.unity documents local Windows/iOS/Android support. AR Foundation/XR Interaction Toolkit are candidates. | Keep optional modules. Test speech latency/model size on real phones. AR tracking/planes/anchors do not reconstruct arbitrary semantic room objects. XR interactions should emit the same app actions as pointer/touch input. |

Do not replace Flecs with another ECS or state/recipe framework. BehaviorTree.CPP is now an evaluation candidate for complex behavior execution at the user's request; it is not approved or integrated by this research. Avoid duplicate audio, spline and IK stacks. Specialized vehicle/animal controllers may be useful adapters, but authored avatars and fixed rig assumptions are not universal runtime controllers.

## Primary references

- [Animation Rigging 6.6](https://docs.unity3d.com/Packages/com.unity.animation.rigging@6.6/manual/index.html). Local Runtime/AnimationRig/RigBuilder.cs checked against the installed package.
- [Unity force API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody.AddForce.html), [configurable joints](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ConfigurableJoint.html).
- [AI Navigation](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/index.html), [A* runtime graph updates](https://arongranberg.com/astar/documentation/stable/navmeshupdates.html).
- [Cinemachine](https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/index.html).
- [Feel runtime player](https://feel-docs.moremountains.com/mmf-player.html).
- [Obi runtime actor and blueprint generation](https://obi.virtualmethodstudio.com/manual/7.1/scriptingactors.html), [backend options](https://obi.virtualmethodstudio.com/manual/7.0/backends.html).
- [FluXY and its 2.5D model](https://fluxy.virtualmethodstudio.com/), [runtime solver settings and readback cost](https://fluxy.virtualmethodstudio.com/manual/1.3/solver.html).
- [Crest watercraft and buoyancy](https://crest.readthedocs.io/en/stable/user/watercraft.html).
- [PuppetMaster](https://root-motion.com/puppetmasterdox/html/page7.html) is an optional future physical-character capability, not a current recommendation.
- [glTFast runtime import](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.14/manual/ImportRuntime.html), [whisper.unity platform support](https://github.com/Macoron/whisper.unity).
- [AR Foundation](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.4/manual/index.html), [Digger runtime tier](https://github.com/ofux/Digger-Documentation/blob/master/Runtime.md).

Proposed slices, peaceful-runtime-creation follow-ups and adaptive-world research: [history/UNITY-CAPABILITY-ROADMAP-proposals-2026-11.md](history/UNITY-CAPABILITY-ROADMAP-proposals-2026-11.md). Accepted decisions live in [RUNTIME-CAPABILITIES](RUNTIME-CAPABILITIES.md) and [BACKLOG](BACKLOG.md).
