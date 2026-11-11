# Runtime capabilities

**Short workflow:** input or AI -> source-backed Flecs Script -> ECS configuration/commands -> disposable adapters -> native algorithms or Unity/plugins -> measured ECS results. No AI is needed per frame. Reopen an experience by rebuilding from saved sources and selected state.

## Try it

1. Open Ecaty with Unity 6000.6.7f1. Let imports finish.
2. Run **Runtime > Runtime engine > Prepare capability assets** once (also after importing a missing plugin).
3. Run **Runtime > Runtime engine > Start capability workshop**. Normal Play startup opens the gallery; this menu selects the separate capability fixture.
4. In Flecs Explorer query `CapBinding`, `CapState`, `CapType`, `CapAsset` or `NativeTreeNode`. Change a binding configuration during Play. `CapState` reports applied generation or failure.
5. **Verify capabilities** runs the automated workshop/reconstruction check. Other existing runtime-engine menus test generated MudBun actors.

## Common contract

Each capability has **its own entity** with `CapBinding { target, enabled, revision }` and one configuration component. Several capability entities can share a target. `(CapDependsOn, prerequisiteBinding)` gates application and regenerates consumers when prerequisite results change. Use dependency relations explicitly; there is no automatic understanding of every spatial relationship.

```flecs
RoadObject { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }
RoadShape {
    CapBinding: {target: RoadObject, enabled: true, revision: 1}
    CapSpline: {points: "[[-2,0,0],[2,0,1]]", width: 1, profile: "", closed: false, fill: false}
}
```

The registered adapter creates its owned instance, reapplies changed configuration and removes it when disabled, removed or retargeted. Failure is `CapState.phase = -1`; waiting/disabled is 0, applied is 1. Revision can force reapplication. `CapDirty` is acknowledged after successful application. Generic binding reconciliation is limited to 256 instances and runs at 20 Hz; physics and animation have their own frame stages. This is a first synchronous generation scheduler, not a general background job/transaction system.

**Ownership:** a root uses authored placement, the existing CharacterController motor, a dynamic body, or a layout owner. Competing owners are rejected. Physics/animation produce observations instead of two solvers fighting over a transform. Material bindings lease appearance separately. Runtime rig constraints must target chains not already owned by a different IK solver.

## Implemented surfaces

| Area | Configuration -> execution |
|---|---|
| Existing generated models | MudModel/ClayPart/rest/style -> live MudBun hierarchy; existing actor/pose/FImpossible/Boing adapters |
| Physics/manipulation | CapBody, CapForce, CapDrag, CapHandle -> sole PhysX body or existing motor; CapReadback returns measured pose, velocity and contacts |
| Shapes/world | CapSpline -> Spline Mesher curve or closed-area fill; CapBuilding -> registered Cozy kit; CapVegetation -> registered Broccoli pipeline |
| Ground/dependencies | CapTerrain -> bounded heightfield mesh/collider rebuilt from base plus one spline modifier; CapDependsOn -> downstream rebuild; native NativeFootprint contains Clipper open/closed stroke or filled-area rings |
| Navigation | CapNavSurface -> Unity runtime NavMesh; CapNavigation -> CapPath direction and optionally an existing motor's MoveIntent (it does not move a body itself) |
| Appearance/effects | CapMaterial, CapParticles, CapLight; CapVfx -> precompiled registered VFX Graph asset with optional exposed Rate and event |
| World appearance | CapPostProcessing -> runtime URP Volume for grading, exposure, white balance, bloom, vignette and tonemapping. Look presets use source-backed Flecs Script and save/rebuild. |
| Animation/camera | CapRig -> runtime TwoBoneIK chain/goal; CapCamera -> Cinemachine follow camera; CapTimeline -> runtime Playables/Timeline audio track |
| Audio | CapAudio/CapAudioNote -> native SoundFont or simple sine/noise voices, streamed to Unity AudioSource/filter/spatializer; CapAudioMeter reports callback output |
| Feedback/music | CapFeedback -> registered Feel feedback palette; CapSequence -> runtime Feel quantized sound sequencer using catalog clips |
| Spatial layouts | CapLayout -> Flexalon circle layout with child pose ownership. The seed controls also use a camera-facing Flexalon circle as presentation |
| Water | CapWater -> simple level/density plane; CapBuoyancy -> forces on a bound PhysX body |
| Weather/ocean | CapWeather -> UniStorm system; CapOcean -> registered configured Crest prefab. Initial bindings exist; these require separate rendered configuration/verification |
| Native behavior | NativeTree -> BehaviorTree.CPP XML instantiated/replaced during play, with builtin control flow and FlecsSet/FlecsQuery/SimWait nodes |

These expose selected runtime operations, not every plugin Inspector feature. New underlying algorithms still require development. Flecs Script remains the primary reactive/event behavior layer; a behavior tree is an optional task orchestration component in that same ECS world, not a replacement ECS.

## Native-only behavior and sound

`NativeTree.xml` contains a BehaviorTree.CPP v4 `<root BTCPP_format="4">` document. `FlecsSet(entity, component, value)` writes reflected JSON, `FlecsQuery(expression)` tests a world query, `SimWait(seconds)` waits simulation time. Sequence/parallel/fallback/retry/decorator composition is supplied by the library. Query `NativeTreeNode` for our leaf-node ports. XML definitions are self-contained and bounded to 256 KiB; filesystem includes are disabled. Trees halt on removal/disable/replacement; completed non-looping trees remain finished. Change revision to restart. Results are ordinary reflected `NativeTreeResult` data (status 0 idle, 1 running, 2 success, 3 failure, -1 engine error). Literal `{` and `$` inside Flecs Script strings require escaping. Do not treat runtime XML as new compiled C++.

TinySoundFont and GeneralUser GS are bundled with provenance/license. Native renderers share immutable bank samples and own independent bounded voice/command state. The audio callback never queries/mutates live Flecs. Unity handles playback/spatialization/filtering; Feel uses registered clips. Synthesis is instruments and simple procedural primitives, not arbitrary realistic sound generation.

## Persistence and portability

Experience capture retains managed sources, live capability configurations, NativeTree definitions and selected adapter state (initially body pose/velocity). Reload builds a fresh native world and fresh plugin instances. Physics contacts, arbitrary plugin internals, playing audio and pending tree/Script waits are not exact checkpoints; tasks restart. Asset keys must resolve to the same installed/bundled assets.

Windows x64 Editor and development-player capability/reconstruction checks pass. The existing two-model MudBun/FImpossible/Boing and 80-frame UI/native player regressions pass too. The latest build has zero errors and 35 warnings. Windows x64 is the current target. Android/iOS/XR need native builds, asset-path/audio changes where needed and device testing. No mobile/WebGL claim is made. UniStorm/Crest currently enforce one global renderer. VFX assets/shaders are compiled at build time; this does not compile new shader graphs on a phone.

## Code map and extension

- Native/Core/src/RuntimeCapabilities.cpp: dependency coordination, trees, footprints, synthesizer and geometry ABI.
- Packages/com.runtime.capabilities/Runtime/Capabilities: reusable lifecycle/schema, native wrappers and Unity adapters.
- Assets/_Adapters/Runtime/Vendor: independently gated optional plugin adapters. Their source compiles in Assembly-CSharp because several licensed plugins use that assembly. Copy only required adapters plus registry into a future product; optional symbols are discovered by CapabilityPluginDiscovery.
- Assets/_Adapters/Editor/CapabilitySetup.cs: original starter assets/catalog; it preserves existing registered keys.
- Verification/capability_native_smoke.py and CapabilityVerification menu: native and real Unity checks.

To extend: define a small reflected configuration/command/result contract; implement one ICapabilityAdapter and disposable CapabilityInstance; register it with metadata; declare dependencies and pose/appearance ownership; add selected state capture only if necessary. Do not put product rules inside adapters. Behavior and recipes belong in runtime source-backed ECS definitions.

MicroSplat and TVE are excluded. No Jolt, separate Detour, FastNoise, STK or meshoptimizer was added. MudBun baking, road texture/vegetation exclusion masks, general multi-modifier terrain, full spatial UX and multiplayer remain separate work.

The host supplies audio-bank bytes through CapabilityRuntime.AudioBankProvider. Runtime.Capabilities depends on the bridge, not on application code/assets. Capability verification selects its own startup flag and must not enable WorkshopChainVerification (which launches a different self-test). Capability assets are referenced by RuntimeComposition (Assets/_Adapters/Adapters.asset).
