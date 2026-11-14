# Runtime capabilities

**Flow:** input or AI -> source-backed Flecs Script -> capability configuration on a binding entity -> disposable Unity/plugin adapter -> measured ECS results. No AI runs per frame. Saved worlds rebuild from sources, configurations and selected state.

## Try it

1. Open the project with Unity 6000.6.7f1 and let imports finish.
2. **Runtime > Runtime engine > Prepare capability assets** once (again after importing a plugin).
3. **Runtime > Runtime engine > Start capability workshop**, then query `CapBinding`, `CapState`, `CapType`, `CapAsset` or `NativeTreeNode` in Flecs Explorer. Edit a configuration in Play; `CapState` reports the applied generation or the failure.
4. **Verify capabilities** runs the workshop/reconstruction check. `Tests/Run-All.cmd` runs the generic contract and lifecycle tests.

## Contract

Each capability has **its own entity** with `CapBinding { target, enabled, revision }` and one configuration component. Several bindings can share a target. `(CapDependsOn, prerequisite)` gates application and regenerates consumers when the prerequisite's result changes.

```flecs
RoadObject { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }
RoadShape {
    CapBinding: {target: RoadObject, enabled: true, revision: 1}
    CapSpline: {points: "[[-2,0,0],[2,0,1]]", width: 1, profile: "", closed: false, fill: false}
}
```

`CapState.phase`: 1 applied, 0 waiting/disabled, -1 failed. A revision change forces reapplication. Reconciliation runs at 20 Hz for up to 256 bindings (a binding beyond that reports phase -1 and is applied once a slot is free) and reads only binding tables whose Flecs data changed (stable query handles with change detection: edits, added/removed bindings, native CapDirty), plus bindings waiting for a target view; an idle world allocates nothing. Steady-state ticks (body, navigation, rig, audio, handle, layout) allocate nothing too, and a configuration is parsed only when its bytes change. `Reconcile()` forces a full pass. Physics and animation tick in their own frame stages.

**Failure recovery:** a failed configuration reports -1 and, if an earlier configuration succeeded, a fresh instance is rebuilt from that last valid configuration. A failed configuration is retried when it changes or a prerequisite's result changes, not every frame. A tick exception disposes the instance until the next edit or dependency change. Consumers keep their last valid output while a prerequisite regenerates or fails.

**Ownership:** one transform owner per root (authored placement, CharacterController motor, dynamic body or layout). Competing owners are rejected. The owner is the exclusive relationship `(TransformOwner, binding)` or `(AppearanceOwner, binding)` on the view entity in the world, so deleting the binding releases it. Material bindings lease appearance separately. Runtime IK must not target a chain owned by another solver.

## Adding a capability (two files)

1. One adapter file holds the configuration class and the adapter (below).
2. Refresh the committed `CapabilityConfigSchema.flecs` export (automatic after compile in Unity); nothing else is registered by hand.

```csharp
[Capability("CapLight", "lighting", 40)]                 // component, category, order
public sealed class LightConfig                            // public fields = Flecs members, in order
{
    public int kind;
    public float intensity = 1, range = 10, colorR = 1, colorG = 1, colorB = 1;   // initializers = defaults
}
public sealed class LightCapability : CapabilityInstance<LightConfig>
{
    public LightCapability(CapabilityContext c) : base(c) { ... }
    protected override void Apply(LightConfig c) { ... }
}
```

- Field types: `float` f32, `int` i32, `bool`, `ulong` u64, `string`; `[EntityField] string` is an entity (resolve with `Context.ResolveEntity` / `Context.Resolve`), `[AssetKey] string` is a key in the capability asset catalog. Non-finite floats are rejected before `Apply`.
- `CapabilityCatalog` discovers configurations and adapters; `CapabilityRuntime.RegisterDiscovered()` registers them and the schema module is generated from them. Shared contracts (CapBinding, CapState, results, NativeTreeNode) stay in `Resources/CapabilitySchema.flecs`.
- **Optional plugins:** put the configuration outside the plugin `#if` and add `Gate = "SYMBOL", Probe = "path/that/exists/when/installed"`; put the adapter inside `#if SYMBOL`. `CapabilityPluginDiscovery` sets the symbol from the probe. The schema exists whether or not the plugin is installed, so saves stay loadable.
- **Tools without Unity:** `CapabilityConfigSchema.flecs` is a committed export of the generated structs (refreshed after each compile, or Runtime > Runtime engine > Export capability schema; an EditMode test fails when stale). Verification/*.py load it after `CapabilitySchema.flecs`.
- **Plugins that create standalone meshes/materials** (e.g. Broccoli) wrap generation in `CreatedObjects.During(...)` and destroy the result on replace/dispose.
- **Saves:** member names, types and order are a saved contract. The capability schema module is app-owned: saves do not carry it and `CapabilityRuntime.InstallSchema` installs the current text once per session, before saved sources, so a member appended later reaches old saves. Append new members; do not rename or reorder. Result components (`CapReadback`, `CapAudioMeter`, `CapRigResult` and the other outputs) carry the `NotSaved` tag and are never written.
- Keep product rules out of adapters. Behavior and recipes belong in runtime source-backed ECS definitions. `Assets/_Adapters/link.xml` preserves reflected adapters in stripped players.

## Implemented surfaces

| Area | Configuration -> execution |
|---|---|
| Generated models | MudModel/ClayPart/rest/style -> MudBun hierarchy; actor/pose/FImpossible/Boing adapters |
| Physics | CapBody, CapForce, CapDrag, CapHandle -> sole PhysX body or existing motor; CapReadback pose/velocity/contacts |
| Shapes/world | CapSpline (Spline Mesher curve or fill), CapBuilding (Cozy kit), CapVegetation (Broccoli pipeline) |
| Ground | CapTerrain heightfield mesh/collider from base plus one spline modifier; NativeFootprint from Clipper |
| Navigation | CapNavSurface runtime NavMesh; CapNavigation -> CapPath and optional motor MoveIntent |
| Appearance | CapMaterial, CapParticles, CapLight, CapVfx (registered VFX Graph), CapPostProcessing (URP Volume) |
| Animation/camera | CapRig TwoBoneIK, CapCamera Cinemachine follow, CapTimeline audio track |
| Audio | CapAudio/CapAudioNote native SoundFont or sine/noise voices; CapAudioMeter; CapFeedback/CapSequence via Feel |
| Layout, water | CapLayout Flexalon circle; CapWater level/density; CapBuoyancy forces |
| Weather/ocean | CapWeather UniStorm; CapOcean registered Crest prefab (one global renderer each; rendering not fully verified) |
| Native behavior | NativeTree BehaviorTree.CPP v4 XML with FlecsSet/FlecsQuery/SimWait leaves; NativeTreeResult status |

These expose selected runtime operations, not every plugin Inspector field. New algorithms still need compiled code. Flecs Script stays the primary reactive layer; a behavior tree is optional task orchestration in the same world. NativeTree XML is bounded to 256 KiB with filesystem includes disabled; literal `{` and `$` in Script strings need escaping.

## Persistence

`CapabilityGraph.Capture(session, runtime, scope, sourcePrefix)` serializes every CapBinding/NativeTree entity under a scope: registered configurations, `CapDependsOn` targets, selected adapter state (e.g. body pose/velocity) and managed sources owned by a prefix. `Restore` validates first, installs owned sources, recreates entities that no source defines (prerequisites first, in one generated module), writes configurations and queues adapter state for the first successful apply. Dropped prerequisites/cycles are reported, not thrown.

- **Experience save** (RuntimeExperience): its sources (without the app-owned modules) plus `capabilityGraph`; older saves without it load unchanged. An unscoped capture (scope null) skips bindings under `worlds.`; those belong to a world document.
- **Gallery saved world:** bindings under `worlds.<id>` are ordinary instances in the world's format 2 document, so they save and load with it (atomic load); adapter state is kept next to the document (`CaptureState(root)` / `QueueRestore`). Activation rebuilds them, unloading removes them. A version 1 record with `graph` is converted on read.
- Reload builds fresh plugin instances. Contacts, plugin internals, playing audio and pending tree/Script waits are not checkpointed; tasks restart. Asset keys must resolve to the same installed/bundled assets.

## Look adapter

`Packages/com.runtime.capabilities/Runtime/Look/` shows the native `Look` and `WorldStyle` components. It is not a `CapBinding` capability: `LookAdapter` (a MonoBehaviour that runs after the world has synced its views) owns a `LookPresenter` and a `WorldStylePresenter`.

- `LookPresenter` gives each element with a `Look` a shared material on its view's renderer. Looks that round to the same values share one reference-counted material (`LookMaterials`), made from two template materials in `Resources/Look` (opaque and clear), so equal looks batch and there are no property blocks. A pattern gets a generated albedo and normal texture (`LookPatternTextures`), cached by the quantised pattern and colours. It takes the appearance with the exclusive `(AppearanceOwner, core.look)` relationship, so other writers see the view as owned, and it restores the original material and releases the relationship when the `Look` goes away.
- `WorldStylePresenter` shows the entered world's `WorldStyle` as overrides on one global post-processing Volume (white balance, colour adjustments, bloom, vignette). With no world entered or no style, every override is off. Values that are set but not drawn yet are listed in `NotRendered`.
- `LookManifest` (`Resources/Look/LookManifest.json`) holds the shader names, the mapping from values to shader and Volume settings, and the pattern numbers, so none are repeated in code.

Motion loops (`MotionLoop`, `SecondPose`) need no adapter: the native module writes the offset and the view stream already sends rest plus offset, so Unity applies it as an ordinary view change.

## Verification

- EditMode `CapabilityContractTests`: generated schema equals the previously shipped schema for all 24 configurations; every configuration round-trips through Flecs; defaults/finite checks; scoped graph capture -> restore in a fresh native world (entities, dependency pairs, configurations); invalid documents rejected.
- PlayMode `CapabilityLifecycleTests`: every compiled adapter with default configuration applies or reports failure, and a second create/apply/remove cycle leaves no new objects.
- Workshop/player checks: `CapabilityVerification` menu and `CapabilitySmoke` (real physics, IK, navigation, meshes, audio callbacks, save/reload).

Windows x64 Editor/development player is the current target; Android/iOS/XR need native builds and device testing. UniStorm/Crest enforce one global renderer. VFX shaders compile at build time.

## Engine adapters (bodies, effects, sound)

Three adapters in `Packages/com.runtime.capabilities/Runtime/` present native engine data. Each is created per `FlecsWorld`, reads through change-reporting queries, and acts only when a query reports a change (or, for sound, a render is waiting). A native library without the matching module is skipped.

- **Bodies and places** (`Bodies/`, `BodyPresenter`, stepped by `BodyAdapter` in FixedUpdate). Reads `Pinned`, `Ghost`, `Free`, the forces and `EffectiveForce`. Creates static colliders for pinned shapes, trigger colliders for ghosts and a Rigidbody per free body, and applies gravity, wind, lift in a fluid and drag. Reports poses back to Flecs (the body's view stays under the world root while simulated) and crossings as `PlaceEntered` and `PlaceLeft` events, which the native module turns into re-parenting.
- **Effects** (`Effects/EffectPresenter`). Reads `Particles`, the child prefab `particle` (its `Look` and `Scale`) and the emitter's `EffectiveForce`. Creates one ParticleSystem per emitter on a child of its view, never one object per particle, with one shared material from the look cache. Particles end at pinned shapes and at the surface of the fluid the emitter is in. Reports nothing back.
- **Sound** (`Sound/`, `SoundPresenter` run by `SoundAdapter` after the views sync). Reads `Sound`, `SoundClip` and `Played`. Creates an AudioSource on the element's view. `SoundSources` finds the samples by `uri` (an in-memory registry, then 16-bit WAV files in one folder), `SoundRenders` shapes them with `fu_sound_shape` on a worker thread and caches the result by (clip, size, rough) quantised to 1/32, and the AudioClip is made on the main thread. The AudioSource plays when `Played.count` goes up. Reports nothing back; a missing clip is skipped with a reason.

## Code map

- `Native/Core/src`: `Capabilities.cpp` (CapBinding/CapState, dependency readiness and dirty marks), `BehaviorTrees.cpp` (NativeTree), `Geometry.cpp` (footprints, outline ABI), `Audio.cpp` (synthesizer). Their systems live in the modules `core.cap`, `core.tree` and `core.geometry`; the phase map is in `Modules.hpp`.
- Native coordination reacts to changes: a binding gets `CapState` and its first `CapDirty` together with `CapBinding`; a consumer is marked again when it gains or loses a prerequisite or when a prerequisite reports a different generation or phase. Marks are applied once per frame and before every `fu_cap_*` call. Footprints are recomputed when a `CapSpline` value is set; a tree is compiled when its definition is set and is not ticked while it only waits for simulation time.
- `Packages/com.runtime.capabilities/Runtime/Capabilities`: runtime lifecycle (CapabilityRuntime), typed contracts (CapabilityCatalog), persistence (CapabilityGraph), built-in adapters.
- `Assets/_Adapters/Runtime/Vendor`: gated optional plugin adapters (Assembly-CSharp because licensed plugins live there). `Assets/_Adapters/Editor`: plugin discovery, asset setup, workshop verification. `Assets/_Adapters/Tests`: generic tests.
- The host supplies audio bank bytes via `CapabilityRuntime.AudioBankProvider`; the asset catalog is referenced by RuntimeComposition (`Assets/_Adapters/Adapters.asset`).

MicroSplat and TVE are excluded. No Jolt, Detour, FastNoise, STK or meshoptimizer. MudBun baking, road texture/vegetation masks, multi-modifier terrain and multiplayer remain separate work.
