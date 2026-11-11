# Verification

Feature verifiers (Editor menus under Runtime > Verification) and Python native smokes write JSON reports here. Reports are local only (ignored by Git): they are evidence for one run, not certification of current code. The regression suites are in Tests/ — read Tests/Results/latest.txt for the latest run.

- `native_smoke.py`, `capability_native_smoke.py`, `runtime_engine_smoke.py`, `runtime_behavior_smoke.py`, `workshop_chain_smoke.py`: native ABI checks against the built DLL (`python Verification/<name>.py`).
- `player_rest_smoke.py`: REST edits against a running development player (port 27750).
- `bench_flecs.py <path-to-flecs_unity.dll>`: bridge/Script micro-benchmarks used by docs/FLECS-ARCHITECTURE-AUDIT.md.
