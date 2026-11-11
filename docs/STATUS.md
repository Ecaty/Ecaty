# Current status

Current state and the exact next action only. Update after meaningful milestones; no per-session checkpoints. History: docs/history/STATUS-log-2026-11.md (and RESUME-log) — read only if something here is missing.

## State — 2026-11-11

- **Architecture:** native C++ Flecs (Native/Core, Native/App) owns runtime entities, data and behavior; Unity presents via a stable C ABI (Packages/com.ecaty.flecs-unity). AI agent package is provider-agnostic; world tools use Flecs queries and Flecs Script.
- **Gallery:** layered crystal world-tree with focus/orbit/zoom, top-down exit without automatic pullback, zoom-driven recentering, angular ring drag, return through the entry portal with near-to-far reveal and recall rim, galaxy-dust rings, persistent loaded-world marker, glass-crossing cue, swap/insert. Swap-while-zoomed hang fixed (CrystalWorldGallery.SetPaused) and play-verified by the user.
- **Gallery config:** values ship as Flecs Script `Assets/_Features/Gallery/GalleryConfig.flecs`, generated from the Editor-only authoring asset `Gallery.asset` (on edit, on build, or Runtime > Gallery > Export config). Runtime and Editor both load through Flecs into a runtime profile copy; Inspector edits apply live in Play. Validator fails if the file is stale.
- **Bridge:** ABI 3 (stable query handles, parse-once scripts, cached view query, non-fatal/read-only REST, buffer copies). Idle gallery native-read 2.27 → 0.085 ms/frame, gallery GC < 0.1 KB/frame. Theme ships as generated Flecs Script. Gallery pick volumes are triggers (a solid invitation sphere had blocked actors in an open world). See docs/FLECS-ARCHITECTURE-AUDIT.md.
- **Native motion:** ring turns and the tree clock are compiled systems + observers in Native/App; their four components are C++ contracts. REST starts only in Editor/development builds.
- **Code/infra:** owned C# reformatted per `.editorconfig`; generated `Verification/*.json` are no longer tracked; `codex/*` branches deleted locally and on GitHub.
- **Verification:** Run-All green on main fa22b93 (Validator 150, Resources 68, PlayerScripts, EditMode 15, PlayMode 3, Gallery 131) and Extended 12/12, focus-independent. One-off Gallery flake 2026-11-11 ("All originals stay fixed while held", 1e-5 tolerance) passed on rerun.
- **Test tooling:** `Run.ps1 -Suite Extended` / `Run-Extended.cmd` reads `Tests/Extended.json`; runner Editor calls are named methods in `Assets/_Tests/Editor/RunnerSupport.cs`; verifier inventory in docs/TESTING.md. Tools > Dev Console verified 2026-11-11: launches suites without blocking the Editor, shows latest.txt, lists CapBinding/CapState rows in Play.
- **Docs:** `docs/README.md` indexes every doc; AGENTS.md now makes only AGENTS, STATUS, BACKLOG and PROJECT-STRUCTURE the default read (others on demand). The proposal is archived as `docs/history/PROPOSAL-docs-index-and-trim.md`. Child-repo AGENTS.md files still link PROJECT/DEVELOPMENT/STATUS, which remain valid.
- **Docs trim and legacy retirement:** PRODUCT-DIRECTION, RUNTIME-PLUGINS and UNITY-CAPABILITY-ROADMAP trimmed to current content, remainder in docs/history/. Removed the shared AgentBehavior path (`inspect_behavior`/`apply_behavior` tools, Runtime Behavior menu and verifier, `-workshopLegacy`, RUNTIME-BEHAVIORS.md archived). Verified by Extended 12/12.
- **Android:** arm64 native library builds with `Native/Build-Android.ps1` into `Assets/Plugins/Flecs/Android/arm64-v8a` (plugin folder renamed from Plugins/Ecaty). `Tools/Build/Android development APK` builds an IL2CPP arm64 APK into `Build/Android` (switches platform; switch back to Windows for tests). ARCore package installed; AR scene not started (T3). Not yet run on a device.
- **Dev workflow:** every test run writes Tests/Results/latest.txt (stages, failing checks/tests). The user pushes; agents commit locally with descriptive messages and `feature/`/`fix/`/`chore/` branch names. Agents may quit/reopen Unity when nothing is unsaved (native DLL swaps). Live world over REST at http://127.0.0.1:27750 in Play mode.

## Next action

Creation tools: user is reviewing mockup v3 (docs/prototypes/creation-tools.html, branch feature/creation-tools); implementation follows the design note. Independent technical work: see docs/PLAN-2026-11.md "Open technical".
