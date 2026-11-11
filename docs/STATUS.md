# Current status

Current state and the exact next action only. Update after meaningful milestones; no per-session checkpoints. History: docs/history/STATUS-log-2026-11.md (and RESUME-log) — read only if something here is missing.

## State — 2026-11-11

- **Architecture:** native C++ Flecs (Native/Core, Native/App) owns runtime entities, data and behavior; Unity presents via a stable C ABI (Packages/com.ecaty.flecs-unity). AI agent package is provider-agnostic; world tools use Flecs queries and Flecs Script.
- **Gallery:** layered crystal world-tree with focus/orbit/zoom, top-down exit without automatic pullback, zoom-driven recentering, angular ring drag, return through the entry portal with near-to-far reveal and recall rim, galaxy-dust rings, persistent loaded-world marker, glass-crossing cue, swap/insert. Swap-while-zoomed hang fixed (CrystalWorldGallery.SetPaused) and play-verified by the user.
- **Gallery config:** values ship as Flecs Script `Assets/_Features/Gallery/GalleryConfig.flecs`, generated from the Editor-only authoring asset `Gallery.asset` (on edit, on build, or Runtime > Gallery > Export config). Runtime and Editor both load through Flecs into a runtime profile copy; Inspector edits apply live in Play. Validator fails if the file is stale.
- **Bridge:** gallery reads one component per call (not whole entities) and typed reads for per-frame scalars: native-read time 2.27 → ~0.74 ms/frame. See docs/FLECS-ARCHITECTURE-AUDIT.md.
- **Verification:** Run-All passed 2026-11-11 01:31 (Validator 147, Resources 68, PlayerScripts, EditMode 8, PlayMode 1, 120/120 gallery checks).
- **Dev workflow:** every test run writes Tests/Results/latest.txt — read that, not the large JSON/logs. The user usually runs tests and does git push; agents prepare changes. Live world over REST at http://127.0.0.1:27750 in Play mode.

## In progress

- BACKLOG 3: native/runtime split — phase 1 (bridge) partly done; next: REST gating, remaining GC sources, ring motion as a native system.

## Next action

See docs/BACKLOG.md item 3 and the plan in docs/PLAN-2026-11.md. Nothing committed yet (user commits/pushes).
