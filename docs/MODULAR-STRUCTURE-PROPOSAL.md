# Modular structure

The package-first layout is approved and migrated. Current paths and assembly boundaries: [PROJECT-STRUCTURE](PROJECT-STRUCTURE.md).

- Each feature owns its source, Config assets, art and tests.
- _Settings is top-level and owns only Settings/project configuration.
- __Common composes shared project assets; _Adapters wires vendors.
- Reusable packages accept host assets/services; vendor sources stay untouched.
- Existing GUIDs, native ABI, namespaces and saved/auth identities are preserved.

The broader native-core/runtime-composition discussion and proposed gallery effects remain [design notes](RUNTIME-COMPOSITION-DESIGN.md), with pending work tracked in [BACKLOG](BACKLOG.md).
