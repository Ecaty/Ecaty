# Legacy shared behavior workflow

Current multi-model templates, async events and save/resume: [RUNTIME-ENGINE](RUNTIME-ENGINE.md). This page documents the older AgentBehavior compatibility path.

## What works

An already-built app can install new Flecs Script expressions and keep executing them as inputs change. This version uses upstream Flecs reactivity directly, with no Lua, runtime C++ compilation or custom behavior interpreter. Unity still presents the resulting Flecs data.

Historical milestone pin: [2c9c855e84a4440d15259eb271760c88de2e9784](https://github.com/SanderMertens/flecs/commit/2c9c855e84a4440d15259eb271760c88de2e9784), retrieved 2026-11-03. It is master, not a release tag. Its version macros remain 4.1.6; our binary reports `4.1.6+2c9c855`. Exact unmodified distribution hashes are in `Native/Core/vendor/UPSTREAM.json`.

## Try it without an AI call

1. Open Ecaty in Unity 6000.6.7f1 and press Play.
2. Choose **Runtime > Runtime Behavior > Start breathing demo**. The existing sphere pulses continuously.
3. **Stop behavior** stops future Script updates. Existing objects keep their last values.
4. **Save behavior source** writes `Logs/Experiences/AgentBehavior.flecs`. In a fresh Play session, **Load saved behavior source** installs that source again. No AI regeneration is needed.

The menu uses `Assets/__Common/Theme/Resources/ReactiveBehavior.flecs`:

```flecs
using flecs.script.math
const t = Clock[Clock].time
const size = 1 + 0.2 * sin(t * 4)
scene {
    Sphere {
        Scale: {size, size, size}
    }
}
```

The compiled native Clock singleton advances `time`, `delta` and `frame` before pipeline progress. Writing Clock triggers Flecs' own dependency reevaluation. The sine/scale algorithm above is in interpreted source, not a compiled breathing system. Replacing the source with position expressions changes the algorithm while preserving the existing entity and its view. No second Unity simulation runs.

## Chat flow

User request -> `query` / `inspect_entity` -> `inspect_behavior` -> `apply_behavior` -> native reactive module -> normal presentation snapshot -> actual Unity object -> Presented -> `observe_over_time` -> concise reply.

- `apply_script`: one-time edits, including independently editable control data.
- `inspect_behavior`: reads actual native AgentBehavior source, including developer-tool edits.
- `apply_behavior`: creates/replaces the complete AgentBehavior managed module. Preserve other rules in that source.
- `observe_over_time`: three state samples over about 0.4 seconds. It reports Clock frames advanced and measured Presented state; it cannot advance a paused world or see the screen.

The generic AI package remains independent of Flecs. App-specific tools live in `Assets/__Common/Agent/Runtime/EcatyAgentTools.cs`. Requests, Script and results remain inspectable in the existing private browser logger. No app UI was added.

## Runtime inputs and current syntax

For independently editable parameters, first create data with `apply_script`:

```flecs
struct MotionSettings(amplitude: f32, frequency: f32)
MotionControls { MotionSettings: {0.2, 4} }
```

The behavior source can then read `MotionControls[MotionSettings].amplitude` and `frequency`. Updating those external inputs reevaluates the rule without reloading source. Values assigned inside the same managed module are its outputs; do not treat their initial assignment there as an independently editable control. Clock and Presented are engine-owned read-only inputs for generated behavior.

This upstream snapshot changes old syntax: use nested declarations `scene { Cube { ... } }`, not `scene.Cube { ... }`; separate statements with newlines or semicolons. Runtime types use `struct Temperature(value: f32)`. Dotted paths still work in inspection and component reads. The C++ reflection member call order also changed; Core/App registration code was migrated. Do not mix the old release header with this source.

## Verification and limits

- `Verification/runtime-behavior-native-checks.json`: actual native reactivity, runtime type/query data, external parameter edits, target-following replacement, conditions, parse failure recovery, stop and fresh-world source reload. Includes a two-object local timing measurement, not a device/performance guarantee.
- `Verification/runtime-behavior-unity-checks.json`: real app tool calls, changing Unity transforms, Presented, temporal samples, source inspection, hot replacement and source reload. The developer verification world is isolated and cleaned up.
- `Verification/runtime-behavior-player-checks.json`: rebuilt Windows development player, 80 advancing frames, compiled Spin plus reactive sphere scale and presentation readback.
- Existing native ABI/REST/lifetime and Editor object checks also pass with the new upstream.

Run native checks with `python Verification/runtime_behavior_smoke.py`. Run the Editor check through **Runtime > Runtime Behavior > Verify integration**. Build with the existing `Ecaty.Editor.Verification.BuildPlayer()` entry point.

Source saving is not full session persistence: independently created types/control data must also be restored before their behavior; elapsed time and arbitrary world state are not checkpointed. The supplied demo needs no extra control data. There is one AgentBehavior module in this prototype, not independent per-user/per-object versioned modules. Removing a module's newly owned entities differs from editing pre-existing scene objects.

A parse failure preserves the previous source and live observers; evaluation errors can leave partial changes. This development snapshot is not a sandbox for arbitrary untrusted source, and we have not added execution quotas. Physics, audio, MudBun, scanning and multiplayer still require their own integrations. No phone/IL2CPP or live-model behavior generation was verified for this milestone. Interpreter features beyond the recorded checks are not covered by these results.
