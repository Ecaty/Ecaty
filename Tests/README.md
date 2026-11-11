# Project tests

Open this checkout in Unity and wait for imports. These commands connect to that running Editor and never start another one. No AI account or inference call is needed.

```powershell
cd A:\Projects\Ecaty
.\Tests\Run-All.cmd
.\Tests\Run-Gallery.cmd
.\Tests\Run-Validator.cmd
```

Other entry points: Run-Extended.cmd (non-network feature verifiers and Python native smokes from Extended.json), Run-Native.cmd, Run-EditMode.cmd, Run-PlayMode.cmd, Run-Resources.cmd, Run-PlayerScripts.cmd. Or `pwsh -NoProfile -File .\Tests\Run.ps1 -Suite Gallery`.

The shared Run.cmd prefers PowerShell 7 and falls back to Windows PowerShell; it allows this local runner in its own process without changing machine/user execution policy. Install the Unity CLI and ensure `unity` is on PATH (the normal WindowsApps installation is also detected). Each run writes runner.log, so a launcher failure remains readable after a window closes. Known Unity reload progress, including timed busy-title suffixes, is allowed up to five minutes; real prompts are not dismissed.

All runs Odin Validator, the asset/reference audit, Windows64 development player-script compilation, every registered Ecaty EditMode test (including isolated native Flecs contracts), PlayMode tests and production-input gallery scenarios. Validator reports a clear skip if not imported. It currently validates nine owned configuration/style/startup assets and includes the registered GalleryConfigValidator; it does not open scenes or scan vendor imports. Keep feature rules with their feature (Gallery/Editor/GalleryConfigValidator.cs); the generic runner is Assets/_Tests/Editor/AssetValidation.cs. Run-All does not call providers, build players or run every historical Verification script.

Every run also overwrites Tests/Results/latest.txt: one line per stage plus failing check names — the file agents read. Results go to Tests/Results/<timestamp>/. validator output is validation.json; other suites have their own JSON. Gallery includes Gallery/index.html, report.json and eleven real-camera PNG checkpoints. Only context sections collapse/expand; images remain ordinary figures. Open-Latest-Gallery.cmd opens the latest one. Failures retain their report, log and a best-effort failure image. No automatic video is recorded. Results are ignored by Git; review any local artifacts before sharing.

Finish any held drag first. The scripts preserve initial Play/ticking state. Gallery scenarios use disposable fixtures and restore the original in-memory context, even when you started inside a saved world. The runner restores saved-world file bytes after Play mode shuts down and autosaves. Native tests use isolated sessions. Tests read actual native state and invoke production input handlers; screenshots are automated Unity rendering rather than AI aesthetic approval.

Test assemblies live under Assets/_Features/Gallery/Tests; gallery scenario/capture helpers under Assets/_Features/Gallery/Editor and generic validation under Assets/_Tests/Editor. The root Tests folder holds runners and manifests. Tests/Fixtures/resource-load-manifest.json records 68 runtime/config references and GUIDs. New feature runners should reuse Run.ps1 instead of copying Editor lifecycle handling.

## Windows PowerShell 5 repair and context reports — 2026-11-09

UnityCli.ps1 preserves quoted native arguments with ProcessStartInfo; Windows PowerShell 5 and PowerShell 7 have both been exercised against the actual open Editor. Do not replace this with direct native invocation of inline C# strings: Windows PowerShell 5 can remove their quotes.

Every run now writes index.html and run.json, including setup failures before Unity enters Play. TestReport.ps1 shows passed/failed/skipped contexts with green/red separation and ordinary image figures. Open-Latest-Results.cmd opens Results/Latest.html. Run.cmd opens that report on completion and keeps failure consoles open. Automated invocations may set WORLD_TEST_NO_OPEN=1 and WORLD_TEST_NO_PAUSE=1 in their own process.

The original failing run was setup-only; see its backfilled index.html. Final full Run-All.cmd evidence is Results/2026-11-09_17-31-11_735/index.html: 98 gallery checks, native/PlayMode/resource/Validator checks pass. Gallery-only menu captures remain compatible. New worlds are checked for native order zero and first-ring placement.

Final modular-layout verification: Results/2026-11-09_18-41-23_278/index.html. All stages pass, including 77 player assemblies, zero editor-source leaks, 217 preserved migration GUIDs, 141 Odin validations, 4 native EditMode tests, 1 PlayMode test and 98 gallery checks. PlayerScripts has an explicit 240-second engine/300-second transport budget; timed managed-callback progress windows are waited for, not dismissed. Native capability/audio and Unity plugin reconstruction checks were also rerun separately; see docs/STATUS.md.

The active suite checks true top-down exit/startup, persistent loaded-world marking, inside-world resume, matched projected exit scale, explicit outward zoom with distance-based recentering, ring-plane angular dragging, travelling world accents and near-to-far reveal/input suppression. Native tests cover return/reveal, epoch changes, measured-zoom recentering and glass timing while previews pause. Captures are local Unity rendering evidence. Retired auto-fit/transient-recall and flat-table/archive-carousel behavior are not active product paths.

Current gallery evidence: Tests/Results/2026-11-09_20-08-58_486/index.html (108 checks, 11 PNGs; native 6, PlayMode 1, player assemblies 77). The current reference audit covers 67 runtime/config references, 211 preserved active GUIDs and 7 intentional retirements. Root runner refreshes/recompiles saved sources after preserving initial state; its new preflight also passes under Windows PowerShell 5 in Results/2026-11-09_20-13-42_536. Old flat-table/archive-carousel helpers are removed, not alternate tests to run.
