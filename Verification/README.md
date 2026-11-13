# Verification

Feature verifiers (Editor menus under Runtime > Verification) and Python native smokes write JSON reports here. Reports are local only (ignored by Git): they are evidence for one run, not certification of current code. The regression suites are in Tests/ — read Tests/Results/latest.txt for the latest run.

- `native_smoke.py`, `capability_native_smoke.py`, `runtime_engine_smoke.py`, `runtime_behavior_smoke.py`, `workshop_chain_smoke.py`: native ABI checks against the built DLL (`python Verification/<name>.py`).
- `contract_checks.py`: the native header against the DLL exports and the C# `DllImport` declarations, the ABI constants, every `.flecs` file loading after its prerequisites (table at the top of the script: add a row when a new file needs another one first), the exports no smoke called, the type redeclaration guard, and the native modules (capability coordination, resting trees, one-off queries). Set `FLECS_UNITY_DEBUG_BUILD=1` when the library is a Debug build. Runs in CI and in the Extended suite.
- `bench_engine.py`: engine micro-benchmarks as ratios against an empty tick, compared with `budgets/engine-budgets.json`. A `guard` is enforced (a regression fails the run); a `target` is the goal from docs/ENGINE-PLAN.md section 3.9 and is only reported until its step lands. After an intended change run it with `--update-guards` and commit the budgets file. CI reports without failing until the guards are confirmed on the runner.
- `player_rest_smoke.py`: REST edits against a running development player (port 27750).
- `bench_flecs.py <path-to-flecs_unity.dll>`: bridge/Script micro-benchmarks used by docs/FLECS-ARCHITECTURE-AUDIT.md.
