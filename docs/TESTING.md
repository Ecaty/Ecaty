# Testing Ecaty

Open this project in Unity. Finish or cancel any held drag before running tests. No provider account, inference request, chat history or network service is needed for these checks.

## Run checks yourself

```powershell
cd A:\Projects\Ecaty
.\Tests\Run-All.cmd
# Or only the gallery:
.\Tests\Run-Gallery.cmd
```

Open this checkout in Unity first and wait for compilation. Uses PowerShell 7 when available, with Windows PowerShell 5.1 fallback, and the Unity CLI. The scripts target this project, preserve its initial Play/tick state, reject compilation errors and fail on failed assertions or zero tests. They never open a second Editor. See [Tests/README](../Tests/README.md) for all entry points.

All runs Odin Validator, the asset/reference audit, Windows player-script compilation, Ecaty EditMode tests (including isolated native Flecs checks), PlayMode tests and gallery scenarios. Native, EditMode, PlayMode, Resources, Validator, PlayerScripts and Gallery each have a separate Run-*.cmd. Historical Verification feature/build/provider scripts remain separate; All does not invoke providers or build players.

- **Runtime > Tests > Gallery scenarios** runs the same compiled scenarios and creates artifacts; starts/stops Play mode only as needed.
- **Window > General > Test Runner** exposes the registered EditMode and PlayMode tests.

Every command run gets `Tests/Results/<timestamp>/`. Gallery saves nine PNG checkpoints, `Gallery/report.json` and `Gallery/index.html`; failures retain a best-effort failure-state image. Open `Tests/Open-Latest-Gallery.cmd` to browse the latest gallery report. These are actual Unity camera renders generated locally without AI; fast test gestures are captured at readable checkpoints. No automatic video capture is implemented yet. Results are ignored by Git and may contain local preview content; review them before sharing.

Compatibility JSON reports still go to Verification (crystal-gallery-checks.json, resource-reference-checks.json and EditMode/PlayMode result files). New visual artifacts live under Tests/Results, not Verification. The superseded flat-gallery helpers and standalone CrystalGalleryChecks wrapper were removed; extend Assets/_Features/Gallery/Editor/GalleryScenarioSuite.cs rather than generating one-off scripts. GalleryRunArtifacts owns camera capture/report output.

## What these checks prove

The native EditMode suite uses the actual `flecs_unity.dll` through the stable C ABI. It loads the real gallery schemas/rules into an isolated native world and checks Clock-driven turns, frozen phase, the reactive entry gate and reflected navigation state.

The gallery suite uses the running application's production `StepPointer` path for wheel/drag/orbit gestures, reads back native intent/relationships/motion and checks the actual Unity transforms/material properties. Broader lifecycle checks also call developer APIs directly (save/reconstruct, world entry, archive and first-object creation); they do not pretend to be desktop mouse automation. Assertions cover camera continuity/return and complete 3D viewport bounds, exit detent, native freeze/resume including preview transforms, hovered-tier rotation, fixed originals, ghost swaps, seed protection/fade, preview entry alignment, bounded contexts and theme propagation.

The PlayMode smoke test verifies live advancing frames, native startup and real gallery renderers. These are Editor checks; they do not certify a standalone build, device performance, HDRP/mobile shader variants or subjective comfort. A camera screenshot is visual evidence, not a frame-timing benchmark. Manually try repeated wheel input, switching zoom targets, rotating each tier and dragging between candidates.

Newer gallery scenarios also cover constant-radius Follow, pole-safe entry, portal restoration after an internal orbit, occlusion-driven hit suppression, volumetric ring depth, immediate zoom reversal and Feel feedback. Archive tests now verify that its button/opening remain disabled and saved records stay hidden.

The older flat-gallery checks target a retired presenter. Their reports remain historical and are not evidence for the active tree.

## Native changes and built players

`Native/Build.ps1` rebuilds the native DLL. Stop Play first; never replace a loaded native DLL. The existing `python Verification/native_smoke.py` additionally tests ABI/REST/lifetime behavior. Run it with Play stopped so the app's REST port is free. Other feature-specific native/player checks remain documented in DEVELOPMENT and their feature documents. Do not claim all historical suites ran for a presentation-only change.

## AI-assisted testing direction

Use deterministic assertions for contracts, saved scenarios for regressions, and AI to propose edge cases, explore unusual action sequences and explain failures. Preserve a reproducible input trace/random seed when exploration finds a bug; turn it into a normal check. Runtime snapshots plus Unity readback expose both intent and actual presentation. Human review remains necessary for enjoyable movement and visual quality. Tests should assert outcomes, not simply repeat the implementation.

This is an engineering recommendation, not a claim that an AI exploration/replay service is already implemented. Current checks require no model call. The growing research direction includes agents that inspect state, choose actions and report logical bugs, such as [TITAN (2025)](https://arxiv.org/abs/2509.22170). More recent work combines game state with internal call traces ([CA2, May 2026](https://arxiv.org/abs/2605.13918)) and evaluates engine state/input replay separately from visual presentation ([SWE-Game, October 2026 revision](https://arxiv.org/abs/2609.33678)). These papers support using several complementary kinds of evidence; their results do not establish coverage or reliability for Ecaty. Unity supports [Editor, command-line, code-driven and Player test execution](https://docs.unity.com/en-us/engine/6000.5/manual/scripting/test-framework-introduction/running-tests); player/device coverage remains future work for this gallery.

## Reusable presentation configuration

`Assets/_Features/Gallery/Gallery.asset` references `Assets/__Common/Theme/Theme.asset`. The layout exposes focus/zoom smoothing, maximum focus speed, swap fade time, exit pause/distance, full-tree zoom and ring surface softness alongside existing geometry/opacity controls. The theme owns gallery colours, a generic agent-UI palette and a workshop-tool tint. Authored world colours are preserved. Changes are reflected in the gallery; native intent remains Flecs-owned.

Amplify is useful for visually editable shader functions/templates and custom nodes ([publisher documentation](https://amplify.pt/unity/amplify-shader-editor/)). The current compact URP shaders stay project-owned and directly editable; owning Amplify is not the same as importing its authoring editor. Amplify Shader Editor 1.9.9.14 was imported by the user during this refinement; its Assets/AmplifyShaderEditor sources/meta are ignored. The visible compile error at import time was a duplicate local in the gallery test and has been fixed. A separate existing Cavity Lit shader diagnostic appeared on reimport; the gallery does not use that material. No licensed vendor code was changed for this update.


## Tune and profile the current gallery

Use **Runtime > Gallery > Layout and ring controls** (GalleryLayout) for guide style, width, opacity, surface/clearance softness, travelling speed, glow sharpness/intensity, optional compatible material templates, early-occlusion fade coverage, ring random seed, exit distance multiplier, push separation, elastic amount/vignette and fade timing. Material templates are cloned at startup; restart Play after changing the template. Ordinary scalar settings are read live; the exit boundary distance is established on world entry. **Runtime > Gallery > Theme colours** opens the shared GameTheme palette.

The exit uses two gestures at its boundary, with a short minimum separation (default 0.08 seconds), without repeatedly resetting a long quiet-scroll requirement. Feel springs drive the feedback; the vignette rendering is a lightweight project shader. Archive controls are disabled and preview sizes unchanged.

For CPU evidence, use Unity Profiler or the Pipeline diagnostics profiler_record command (90 frames was used here). **GalleryPerformanceProbe.Summarize** writes compact reports under Verification from the loaded Editor frame history. Captures under Temp are ignored. Inclusive sample means include Editor/profiling overhead and are not a player/device FPS guarantee. Native catalog refresh uses the existing bulk Flecs query; the per-frame component cache avoids repeated entity JSON work and is invalidated each frame/updated after local writes.

Final 2026-11-08 evidence: 70 gallery scenarios, 4 real-native checks and 1 PlayMode smoke pass. Quiet Editor captures reduced gallery update mean 36.39 → 5.65 ms and Main Thread 60.09 → 24.96 ms; see gallery-performance-before/after/final.json. Source remains local and the Editor was restored stopped.

The latest hover regression checks that pointer exit clears native hover immediately while the visual highlight eases away. Ring phases include both directions and survive arrangement reconstruction. ResourceVerification checks all 68 migrated resource loads against their GUID manifest; these checks complement actual startup/native tests.

## Latest runner and native-world coverage — 2026-11-09

Run-All includes actual Odin Validator output (validation.json) and product-specific config rules. Optional imports are gated; a missing Validator produces an explicit skip. Known timed reload progress titles are recognized without dismissing user prompts. Launcher transcripts go to each run folder. Batch entry points use a process-local execution-policy override and prefer pwsh with Windows PowerShell fallback; no global policy setting changes.

The gallery suite starts from a disposable thirty-world fixture and restores the user's original runtime state. Run.ps1 preserves original gallery save bytes after stopping/restarting Play, so final autosave does not overwrite them. New checks cover real native creation/Spin, ordinary object drag/placement, world reconstruction/camera/selection, paused hidden residence, private-scope release/shared-prefab survival, object reuse, strict faded picking and eased ring convergence. Native-world and saved-preview PNGs bring the checkpoint total to nine. HTML checks and images are collapsible. No video, player build/restart or new device/performance measurement is implied.

Final 2026-11-09 Run-All: 97 gallery assertions, 4 native EditMode tests, 1 PlayMode smoke, 68 resource references and 133 Validator checks over 8 config roots all pass. Latest report: Tests/Results/2026-11-09_00-52-05_275/Gallery/index.html, with nine PNGs and no capture/cleanup failures. A separate post-test Inspector-window capture failed and the Editor exited afterward; the project was reopened ready/stopped with clean compilation/Console. That optional Pipeline UI capture is not part of Run-All and has no verified image result. A separate read-only desktop view successfully verified Odin icon tabs; a final duplicate-heading cleanup compiled cleanly without changing runtime behavior.

## Player compilation and layout checks

Run-PlayerScripts.cmd compiles Windows64 development player scripts in the existing stopped Editor; it does not build a player or open another Editor. It checks owned Editor-source exclusion and required bridge/agent/capability/application assemblies. Run-All includes this stage before the EditMode/PlayMode/gallery checks. Output DLLs go under that run's ignored PlayerScripts directory. Resource-reference checks now include direct startup config references and the preserved active GUIDs plus intentional retirements.

The active suite checks true top-down exit/startup, persistent loaded-world marking, inside-world resume, matched projected exit scale, explicit outward zoom with distance-based recentering, ring-plane angular dragging, travelling world accents and near-to-far reveal/input suppression. Native tests cover return/reveal, epoch changes, measured-zoom recentering and glass timing while previews pause. Captures are local Unity rendering evidence. Retired auto-fit/transient-recall and flat-table/archive-carousel behavior are not active product paths.

Current gallery evidence: Tests/Results/2026-11-09_20-08-58_486/index.html (108 checks, 11 PNGs; native 6, PlayMode 1, player assemblies 77). The current reference audit covers 67 runtime/config references, 211 preserved active GUIDs and 7 intentional retirements. Root runner refreshes/recompiles saved sources after preserving initial state; its new preflight also passes under Windows PowerShell 5 in Results/2026-11-09_20-13-42_536. Old flat-table/archive-carousel helpers are removed, not alternate tests to run.


## Contract checks, benchmark budgets and golden saves (2026-11-13)

- `python Verification/contract_checks.py`: header, DLL exports and C# imports agree; ABI constants; every `.flecs` file loads. Add a prerequisite row in the script when a new `.flecs` file depends on another.
- Native C++ tests: `flecs_unity_core_tests` (built with `-DFU_BUILD_TESTS=ON`, see DEVELOPMENT "Native rebuild"). CI builds Debug with warnings, fails on a warning in our own sources, runs these tests and repeats the smokes on the Debug library.
- `python Verification/bench_engine.py`: budgets from docs/ENGINE-PLAN.md section 3.9. Guards fail the run on a regression; targets are reported until their step lands. Both run in the Extended suite; CI runs the contract checks as a gate and the benchmark as a report.
- Golden saves: `Tests/Fixtures/Saves/experience-v1.json` (Runtime experience verifier) and `gallery-arrangement-v1.json` (Gallery suite). Each file is written once, from the verifier's own fixture scenario, the first time the verifier runs without it; commit it. From then on the verifier loads the committed file, so a format change must keep it loading (add a migration, never edit or re-seed the file). They hold fixture data only, never a user's saves.

## Extended suite and verifier registry

`.\Tests\Run-Extended.cmd` (or `Run.ps1 -Suite Extended`) runs the verifiers that Run-All does not: bridge integration, workshop chain, runtime actor/behavior/experience/spatial, photo object, the provider-free chat loop and the Python native smokes. Everything is listed in `Tests/Extended.json`; to register a verifier (for example the capability verification) add one entry there. Editor entries name a C# statement and the `Verification/*.json` report it rewrites. The runner starts it with Play stopped, waits for a fresh report with `passed=true`, copies it into `Tests/Results/<run>/Extended/` and stops Play afterwards. Python entries pass on exit code 0 (and `passed=true` when a report is named). A failure does not stop the remaining verifiers; `latest.txt` lists each one as a check and names the failures. Run it before native/bridge changes, with the Editor otherwise idle. Provider, network and local-model verifiers are not included. The runner's former inline `script eval` strings now live in `Assets/_Tests/Editor/RunnerSupport.cs`.

**Tools > Dev Console** (`Assets/_Tests/Editor/DevConsoleWindow.cs`) starts Run All / Gallery / Extended as a background process, shows `latest.txt`, opens the latest HTML report and, in Play mode, lists live `CapBinding`/`CapState` rows read through Flecs queries.

## Editor menu and verifier inventory (2026-11-11)

"Suite" is where it runs automatically. Status: current = used by a feature; manual = developer tool or demo; opt-in = needs services, models or accounts.

| Entry | Purpose | Suite | Status |
|---|---|---|---|
| Runtime > Tests > Gallery scenarios | Gallery scenarios with artifacts | Gallery | current |
| Runtime > Tests > Asset validation | Odin Validator + config rules | Validator | current |
| Runtime > Tests > Resource references, Modular layout | Resource GUID manifest, layout/migration audit (layout runs inside Resources) | Resources | current |
| Runtime > Tests > Player scripts | Windows player-script compile, editor-source exclusion | PlayerScripts | current |
| `Verification.RunAll` | Bridge integration: Script, views, presentation readback | Extended | current |
| `Verification.BuildPlayer` | Builds Build/Ecaty.exe | none | manual |
| Runtime engine > Verify workshop chain | Workshop chain in Play | Extended | current |
| Runtime engine > Verify workflow | Runtime actor | Extended | current |
| Runtime engine > Verify spatial workflow | Spatial demo | Extended | current |
| Runtime engine > Verify runtime experience | Experience save/resume | Extended | current |
| `PhotoObjectVerification.Run` | Photo-to-clay object | Extended | current |
| `ChatLoopVerification.Start` | Chat loop, synthetic transport | Extended | current |
| Runtime engine > Verify capabilities | Capability workflow | none yet | current; owned by the persistence work, register in `Tests/Extended.json` |
| Runtime engine > Prepare capability assets, Start capability workshop | Setup and demo | none | manual |
| Runtime engine > Start workshop chain, Start creature test, Start spatial demo, Land spatial demo, Take player control, Follow target, Stop | Interactive demos in Play | none | manual |
| Runtime engine > Start runtime playground, Save current experience, Resume saved experience | Manual experience demo | none | manual |
| Runtime > Workshop > Icon Studio, Bake selected model / default MudBun seed / stock icons, Select baked seed prefab, Prepare post-processing | Asset authoring tools | none | current tools |
| Runtime > Attach / Remove reference photo | Photo workflow helper | none | manual |
| Runtime > Gallery > Layout and ring controls, Shuffle ring travel, Export config, Theme colours | Gallery authoring | none | current tools |
| Runtime > Agent Trace, Agent Trace (Browser) | Developer trace views | none | current tools (package) |
| Runtime > Verification > local AI and speech; local AI protocols and dictation | Local model/speech checks (need models) | none | opt-in |
| `MultiProviderVerification.Start` | Provider flow with synthetic transport and loopback HTTP | none | candidate for Extended |
| `BrowserTraceVerification.Start` / `CheckChatUI` | Trace collector; chat UI polish (alters UI, needs Play) | none | `Start` is a candidate for Extended |
| `LiveProviderVerification.Start`, `LocalShutdownVerification.Start` | Live accounts / local inference | none | opt-in |
| `AgentPackageVerification.RunAll` | Package contract checks (also run by local-agent verification) | none | candidate for Extended |
| `ChatLoopSetup.Prepare` | Chat loop scene setup | none | setup |
| Flecs > Open Explorer | Opens Flecs Explorer | none | current tool |
| Tools > Dev Console | Runner buttons, latest summary, capability rows | none | current tool |

**Retired 2026-11-11:** the Runtime Behavior menu and `RuntimeBehaviorVerification` (they exercised the removed shared AgentBehavior path; the `inspect_behavior`/`apply_behavior` tools, `-workshopLegacy` and `docs/RUNTIME-BEHAVIORS.md`, now archived in `docs/history/`, went with them). Independent `RuntimeBehaviors.<id>` modules remain covered by Runtime experience. Uncertain items, not deleted: `Verification.BuildPlayer` (no suite builds players) and the candidates marked above. Capability entries belong to the persistence session; local-AI and Agent Trace entries to the ai-agent package.
