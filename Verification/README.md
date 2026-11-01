# Verification

Run the native check from the Unity project root after building the native plugin:

`python Verification/native_smoke.py`

The Unity verification entry points are `Ecaty.Editor.Verification.RunAll` and `BuildPlayer`. See `docs/DEVELOPMENT.md` for the live Editor workflow and Windows player arguments.

The four `*-checks.json` reports are the actual setup baseline from 2026-11-01. They cover native behavior, Unity object state, advancing player frames and live player REST changes. Test logs and player builds are ignored; no visual inspection, exhaustive Flecs feature coverage or other platform support is implied.