"""Native contract checks: header vs DLL vs C# imports, ABI constants, every .flecs file, and exports no smoke calls.

Standard library only. Writes Verification/contract-checks.json and exits 1 when any check fails.
Run with FLECS_UNITY_LIBRARY pointing at the built flecs_unity.dll (default: Assets/Plugins/Flecs/x86_64).
"""
import contextlib
import ctypes as c
import json
import os
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
DLL = os.environ.get("FLECS_UNITY_LIBRARY", str(ROOT / "Assets/Plugins/Flecs/x86_64/flecs_unity.dll"))
HEADER = ROOT / "Native/Core/include/flecs_unity.h"
SESSION_CS = ROOT / "Packages/com.runtime.flecs/Runtime/FlecsSession.cs"
REPORT = ROOT / "Verification/contract-checks.json"
SOURCE_ROOTS = ("Assets", "Packages")

# --- Prerequisites for loading each .flecs file as a managed script ------------------------------------------------

WORLD_RESOURCES = "Assets/_Features/World/Resources"
WORLD = f"{WORLD_RESOURCES}/Runtime"
GALLERY = "Assets/_Features/Gallery/Resources/Gallery"
WORKSHOP = "Assets/_Features/Creation/Resources/Workshop"
CAPS = "Packages/com.runtime.capabilities/Runtime/Capabilities"
CLAY, POSE, ENGINE, ACTOR = (f"{WORLD}/Schemas/{n}Schema.flecs" for n in ("Clay", "Pose", "Engine", "Actor"))
CAP_SCHEMA, CAP_CONFIG = f"{CAPS}/Resources/CapabilitySchema.flecs", f"{CAPS}/CapabilityConfigSchema.flecs"
CATALOG, TREE = f"{GALLERY}/Examples/GalleryCatalog.flecs", f"{GALLERY}/Runtime/WorldGalleryTree.flecs"
LOBBY = f"{WORKSHOP}/Runtime/WorkshopLobby.flecs"
CHAIN_DIR = f"{WORKSHOP}/Examples/AssemblyChain"
VERIFICATION_WORLD = "Assets/_Tests/Fixtures/Resources/Tests/Fixtures/VerificationWorld.flecs"

# Script text the app generates at runtime and that a file needs; prerequisites name it as "inline:<key>".
INLINE = {
    # Verification/workshop_chain_smoke.py creates the actors the links point at.
    "chain-actors": """
ChainBunny { ActorMotion: {0,0,0,0,0,0,0,true} }
ChainDuck { ActorMotion: {0,0,-0.6,0,0,0,0,true} }
ChainBunnyGoal { MoveGoal: {0,0,0.24} }
ChainDuckGoal { MoveGoal: {0,-0.6,0.24} }
""",
    # Assets/_Features/World/Editor/RuntimeActorMenu.cs:28 (DemoSpatialSettings).
    "hover-settings": "struct HoverParameters(height: f32, sway: f32)\nMotionTuning { HoverParameters: {1.8,0.25} }",
    # ClayCreation builds this part chain when a photo object is applied (RuntimeSpatialVerification.cs:123).
    "photo-clay": "PhotoClay { ActorMotion: {0,0,0,0,0,0,0,true}\n part_hips { part_torso { part_ear_base { part_ear_tip { part_ear_brush {} } } } } }",
}

# file -> ordered prerequisites, loaded first in a fresh world. The comment gives the C# location that justifies each row.
PREREQUISITES = {
    CLAY: [],  # RuntimeExperience.cs:60 InstallFoundation loads the four foundation scripts in this order
    POSE: [],  # RuntimeExperience.cs:60 (also RuntimePoseBridge.cs:43, standalone)
    ENGINE: [],  # RuntimeExperience.cs:60
    f"{WORLD}/Catalog/RuntimeLibrary.flecs": [CLAY, POSE, ENGINE],  # RuntimeExperience.cs:60: loaded after the other three
    ACTOR: [],  # DioramaRuntime.cs:110, standalone
    f"{WORLD}/Behaviors/ActorBehavior.flecs": [ACTOR],  # DioramaRuntime.cs:240 follows ActorSchema (line 110)
    f"{WORLD}/Behaviors/RuntimeSpatialBehavior.flecs":
        [ACTOR, POSE, "inline:hover-settings", "inline:photo-clay"],  # RuntimeActorMenu.cs:28-29 after Start() built the actor
    f"{WORLD}/Behaviors/ReactiveBehavior.flecs": [VERIFICATION_WORLD],  # PlayerSmoke.cs:67 runs it over the fixture world
    f"{WORLD}/Examples/World.flecs": [],  # GameBootstrap.cs:38 / FlecsBootstrap.Create: root script of a world
    VERIFICATION_WORLD: [],  # GameBootstrap.cs:38 (smoke) root script
    f"{WORLD}/Examples/Capabilities/CapabilityWorkshop.flecs":
        [CAP_SCHEMA, CAP_CONFIG],  # CapabilityVerification.cs:95 after CapabilityRuntime.InstallSchema
    f"{WORLD_RESOURCES}/Workshop/Runtime/WorkshopWorldLooks.flecs": [CAP_SCHEMA, CAP_CONFIG],  # WorkshopDock.cs:279 needs CapPostProcessing
    CAP_SCHEMA: [],  # CapabilityRuntime.cs:189 InstallSchema (the app appends the generated config structs to this module)
    CAP_CONFIG: [CAP_SCHEMA],  # CapabilityRuntime.cs:189 (concatenated after CapabilitySchema)
    CATALOG: [],  # GameBootstrap.cs:38 gallery root script; NativeGalleryTests.cs:17 loads it first
    TREE: [CATALOG],  # CrystalWorldGallery.cs:246 (GalleryView/GalleryFocus come from the catalog)
    f"{GALLERY}/Runtime/WorldGalleryTreeMotion.flecs": [CATALOG, TREE],  # CrystalWorldGallery.cs:247 follows the tree schema
    f"{GALLERY}/Runtime/WorldGalleryNavigation.flecs": [CATALOG, TREE],  # CrystalWorldGallery.cs:248; GallerySettings is in the catalog
    "Assets/_Features/Gallery/GalleryConfig.flecs": [],  # GalleryConfigScript.cs:56 loads it alone
    "Assets/__Common/Theme/ThemeConfig.flecs": [],  # ThemeScript.cs:52 loads it alone
    LOBBY: [],  # WorkshopSeed.cs:54 startup root script
    f"{WORKSHOP}/Runtime/WorkshopSeedMotion.flecs": [LOBBY],  # WorkshopSeed.cs:56 after the lobby defines WorldSeed
    f"{CHAIN_DIR}/WorkshopChainObjects.flecs": [ENGINE],  # PersistState comes from EngineSchema (workshop_chain_smoke.py)
    f"{CHAIN_DIR}/WorkshopChain.flecs": [ACTOR, ENGINE, CAP_SCHEMA, CAP_CONFIG],  # WorkshopTools.cs:201 ApplyBehavior in a foundation world
    f"{CHAIN_DIR}/WorkshopChainLinks.flecs":  # not referenced by C#; Verification/workshop_chain_smoke.py loads it last
        [ACTOR, ENGINE, CAP_SCHEMA, CAP_CONFIG, "inline:chain-actors", f"{CHAIN_DIR}/WorkshopChainObjects.flecs",
         f"{CHAIN_DIR}/WorkshopChain.flecs"],
}


# file -> reason it cannot be loaded standalone. Keep as small as possible; empty when every file loads.
EXPECTED_UNLOADABLE = {}

# --- Reporting -----------------------------------------------------------------------------------------------------

results = []


def record(name, ok, detail=""):
    results.append({"name": name, "ok": bool(ok), "detail": detail})
    print(f"[{'PASS' if ok else 'FAIL'}] {name}" + (f": {detail}" if detail else ""))


def check(name):
    """Register a check: it returns a detail string, raises AssertionError to fail, or returns a list of (name, ok, detail)."""
    def decorate(function):
        try:
            outcome = function()
            if isinstance(outcome, list):
                for row in outcome:
                    record(*row)
            else:
                record(name, True, outcome or "")
        except AssertionError as failure:
            record(name, False, str(failure))
        except Exception as failure:  # an unexpected error is still a failed check, and later checks must run
            record(name, False, f"{type(failure).__name__}: {failure}")
        return function
    return decorate


# --- Native bindings -----------------------------------------------------------------------------------------------

lib = c.CDLL(DLL)
P, S = c.c_void_p, c.c_char_p
SIGNATURES = {
    "fu_create": (P, [c.c_uint16]), "fu_destroy": (None, [P]), "fu_tick": (c.c_int32, [P, c.c_float]),
    "fu_abi_version": (c.c_int32, []), "fu_view_size": (c.c_int32, []), "fu_last_error": (S, [P]),
    "fu_set_managed_script": (c.c_int32, [P, S, S]), "fu_managed_script_source": (P, [P, S]),
    "fu_set_component_json": (c.c_int32, [P, S, S, S]), "fu_entity_json": (P, [P, S]), "fu_lookup": (c.c_uint64, [P, S]),
    "fu_entity_path": (P, [P, c.c_uint64]), "fu_entity_name": (P, [P, c.c_uint64]), "fu_free": (None, [P]),
    "fu_name_into": (c.c_int32, [P, c.c_uint64, c.c_char_p, c.c_int32]),
    "fu_query_create": (c.c_int32, [P, S]), "fu_query_json_h": (P, [P, c.c_int32]),
    "fu_document": (c.c_int32, [P, S, S]), "fu_script_capture": (c.c_int32, [P, S]),
    "fu_pointer": (c.c_int32, [P, c.c_uint64, c.c_float, c.c_float, c.c_uint32, c.c_uint32]),
    "fu_keyboard": (c.c_int32, [P, S, c.c_int32, c.c_int32, c.c_uint32]),
    "fu_run_script": (c.c_int32, [P, S]), "fu_component_json": (P, [P, c.c_uint64, S]),
    "fu_component_size": (c.c_int32, [P, c.c_uint64]), "fu_bridge_diagnostics": (P, [P]),
    "fu_cap_has": (c.c_int, [P, c.c_uint64, S]), "fu_cap_ack": (None, [P, c.c_uint64]),
    "fu_cap_ready": (c.c_int, [P, c.c_uint64]), "fu_delete_entity": (c.c_int32, [P, S]),
    "fu_query_once_json": (P, [P, S, c.c_int32]),
    "fu_type_layout_into": (c.c_int32, [P, c.c_uint64, c.c_char_p, c.c_int32]),
}
for _name, (_result, _args) in SIGNATURES.items():
    _function = getattr(lib, _name)
    _function.restype, _function.argtypes = _result, _args


class World:
    """An isolated native world (REST disabled) with owned-string handling."""

    def __init__(self):
        self.ctx = lib.fu_create(0)
        assert self.ctx, "fu_create(0) returned null"

    def close(self):
        lib.fu_destroy(self.ctx)

    @staticmethod
    def take(pointer):
        """Copies and frees an owned UTF-8 string; None for NULL."""
        if not pointer:
            return None
        try:
            return c.string_at(pointer).decode("utf-8")
        finally:
            lib.fu_free(pointer)

    def error(self):
        return (lib.fu_last_error(self.ctx) or b"").decode("utf-8", "replace").strip()

    def load(self, name, code):
        """Returns None on success, else the script error text."""
        rc = lib.fu_set_managed_script(self.ctx, name.encode(), code.encode())
        return None if rc == 0 else (self.error() or f"rc={rc}")

    def require_load(self, name, code):
        problem = self.load(name, code)
        assert problem is None, f"{name}: {problem}"

    def lookup(self, path):
        return lib.fu_lookup(self.ctx, path.encode())

    def entity(self, path):
        return json.loads(self.take(lib.fu_entity_json(self.ctx, path.encode())))

    def tick(self, count=4):
        for _ in range(count):
            assert lib.fu_tick(self.ctx, 0.02) == 0, self.error()


@contextlib.contextmanager
def isolated_world():
    world = World()
    try:
        yield world
    finally:
        world.close()


# --- Parsing the header and the C# imports -------------------------------------------------------------------------

KIND_OF_C = {"int32_t": "int", "int": "int", "uint32_t": "uint", "uint16_t": "ushort", "uint64_t": "ulong",
             "float": "float", "double": "double", "void": "void"}
KIND_OF_CS = {"int": "int", "uint": "uint", "ushort": "ushort", "ulong": "ulong", "float": "float", "double": "double",
              "void": "void", "IntPtr": "ptr", "string": "ptr"}


def strip_c_comments(text):
    return re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)


def c_kind(declaration):
    """Kind of a C type or parameter declaration: pointers collapse to 'ptr'."""
    declaration = declaration.replace("const", " ")
    if "*" in declaration:
        return "ptr"
    words = declaration.split()
    return KIND_OF_C.get(words[0] if len(words) == 1 else " ".join(words[:-1]), f"unknown:{declaration.strip()}")


def parse_header():
    """name -> (return kind, [parameter kinds]) for every FU_API prototype."""
    text = strip_c_comments(HEADER.read_text(encoding="utf-8"))
    functions = {}
    for ret, name, params in re.findall(r"FU_API\s+([^;(]*?)\b(fu_\w+)\s*\(([^)]*)\)\s*;", text):
        params = [p.strip() for p in params.split(",") if p.strip() and p.strip() != "void"]
        functions[name] = (c_kind(ret), [c_kind(p) for p in params])
    return functions


def split_top_level(text):
    parts, depth, current = [], 0, ""
    for char in text:
        depth += char in "([<"
        depth -= char in ")]>"
        if char == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += char
    return parts + [current] if current.strip() else parts


def cs_kind(declaration):
    """Kind of a C# parameter or return type declaration (attributes already removed)."""
    words = [w for w in declaration.replace("unsafe", " ").split() if w not in ("static", "extern", "private", "public", "internal")]
    if words and words[0] in ("ref", "out", "in"):
        return "ptr"
    type_name = " ".join(words[:-1]) if len(words) > 1 else " ".join(words)
    if type_name.endswith(("*", "[]")):
        return "ptr"
    return KIND_OF_CS.get(type_name, f"unknown:{type_name}")


def parse_csharp_imports():
    """name -> (return kind, [parameter kinds]) for every fu_* DllImport extern under Assets and Packages."""
    imports = {}
    pattern = re.compile(r"\[DllImport\([^\]]*\)\]\s*(?:\[[^\]]*\]\s*)*([^;{]*?\bextern\b[^;{]*?)\(([^;]*?)\)\s*;", re.S)
    for root in SOURCE_ROOTS:
        for path in sorted((ROOT / root).rglob("*.cs")):
            if "Library" in path.relative_to(ROOT).parts:
                continue
            for head, params in pattern.findall(path.read_text(encoding="utf-8-sig")):
                *return_words, name = head.split()
                if not name.startswith("fu_"):
                    continue
                params = re.sub(r"\[(?:In|Out|MarshalAs)[^\]]*\]", " ", params)
                imports[name] = (cs_kind(" ".join(return_words) + " x"), [cs_kind(p) for p in split_top_level(params)])
    return imports


@check("header-exports-in-dll")
def header_vs_dll():
    missing = [name for name in parse_header() if not hasattr(lib, name)]
    assert not missing, "declared in the header but not exported by the DLL: " + ", ".join(missing)
    return f"{len(parse_header())} FU_API functions exported"


@check("header-matches-csharp-imports")
def header_vs_csharp():
    header, imports = parse_header(), parse_csharp_imports()
    problems = []
    for name, (ret, params) in sorted(imports.items()):
        if name not in header:
            problems.append(f"{name}: C# import has no header prototype")
            continue
        header_ret, header_params = header[name]
        if len(params) != len(header_params):
            problems.append(f"{name}: C# has {len(params)} parameters, header has {len(header_params)}")
            continue
        wrong = [f"#{i + 1} C# {cs} vs header {ch}" for i, (cs, ch) in enumerate(zip(params, header_params)) if cs != ch]
        if ret != header_ret:
            wrong.append(f"return C# {ret} vs header {header_ret}")
        if wrong:
            problems.append(f"{name}: " + "; ".join(wrong))
    assert not problems, " | ".join(problems)
    unused = sorted(set(header) - set(imports))
    return f"{len(imports)} C# imports match; header functions without a C# import: {', '.join(unused)}"


# --- ABI constants -------------------------------------------------------------------------------------------------

FIELD_SIZE = {"ulong": 8, "float": 4, "int": 4, "uint": 4}


def csharp_view_size(source):
    body = re.search(r"struct View\s*\{(.*?)\n\s*\}", source, re.S).group(1)
    offset, alignment = 0, 1
    for type_name, names in re.findall(r"public\s+(\w+)\s+([\w\s,]+);", body):
        size = FIELD_SIZE[type_name]
        for _ in names.split(","):
            offset = -(-offset // size) * size + size
            alignment = max(alignment, size)
    return -(-offset // alignment) * alignment


@check("abi-constants")
def abi_constants():
    source = SESSION_CS.read_text(encoding="utf-8-sig")
    managed_version = int(re.search(r"const int AbiVersion\s*=\s*(\d+)", source).group(1))
    native_version, native_size, managed_size = lib.fu_abi_version(), lib.fu_view_size(), csharp_view_size(source)
    assert native_version == managed_version, f"fu_abi_version() {native_version} != C# AbiVersion {managed_version}"
    assert native_size == 96, f"fu_view_size() is {native_size}, expected 96"
    assert native_size == managed_size, f"fu_view_size() {native_size} != C# View size {managed_size}"
    return f"ABI {native_version}, view size {native_size} bytes"


# --- Every Flecs Script file loads ---------------------------------------------------------------------------------


def flecs_files():
    return sorted(path.relative_to(ROOT).as_posix() for root in SOURCE_ROOTS for path in (ROOT / root).rglob("*.flecs")
                  if path.is_file() and "Library" not in path.relative_to(ROOT).parts)


def source_of(entry):
    return INLINE[entry[7:]] if entry.startswith("inline:") else (ROOT / entry).read_text(encoding="utf-8-sig")


def load_with_prerequisites(file):
    """Returns None when the file loads in a fresh world after its prerequisites, else the failing script and error."""
    with isolated_world() as world:
        for entry in PREREQUISITES[file] + [file]:
            problem = world.load(pathlib.PurePosixPath(entry).stem if "/" in entry else entry, source_of(entry))
            if problem:
                return f"{entry} failed: {problem}"
    return None


def resource_key(file):
    """The key C# uses to load the file: the path below Resources/ without extension, else the file name."""
    parts = file.rsplit("/Resources/", 1)
    return parts[1][:-len(".flecs")] if len(parts) == 2 else pathlib.PurePosixPath(file).name


def unreferenced(files):
    sources = "\n".join(path.read_text(encoding="utf-8-sig") for root in SOURCE_ROOTS for path in (ROOT / root).rglob("*.cs"))
    return [f for f in files if not re.search(r"(?<!\w)" + re.escape(resource_key(f)) + r"(?!\w)", sources)]


@check("flecs-scripts")
def script_files():
    files = flecs_files()
    rows = []
    for file in files:
        if file in EXPECTED_UNLOADABLE:
            continue
        if file not in PREREQUISITES:
            rows.append((f"script {file}", False, "no prerequisite entry and not in EXPECTED_UNLOADABLE"))
            continue
        problem = load_with_prerequisites(file)
        rows.append((f"script {file}", problem is None, problem or "loads after " + (", ".join(
            pathlib.PurePosixPath(p).name for p in PREREQUISITES[file]) or "nothing")))
    stale = sorted((set(PREREQUISITES) | set(EXPECTED_UNLOADABLE)) - set(files))
    rows.append(("script tables name existing files", not stale, "missing: " + ", ".join(stale) if stale else
                 f"{len(PREREQUISITES)} prerequisite rows; EXPECTED_UNLOADABLE = {EXPECTED_UNLOADABLE or 'empty'}"))
    rows.append(("scripts without any C# reference (information)", True, ", ".join(unreferenced(files)) or "none"))
    return rows


# --- Exports no smoke test calls -----------------------------------------------------------------------------------


@check("entity-path-and-name")
def entity_path_and_name():
    with isolated_world() as world:
        world.require_load("Nested", "alpha { beta {} }")
        beta = world.lookup("alpha.beta")
        assert beta, "alpha.beta was not created"
        path, name = World.take(lib.fu_entity_path(world.ctx, beta)), World.take(lib.fu_entity_name(world.ctx, beta))
        assert (path, name) == ("alpha.beta", "beta"), f"path={path!r} name={name!r}"
        assert lib.fu_entity_path(world.ctx, 0xDEAD0000) is None and lib.fu_entity_name(world.ctx, 0xDEAD0000) is None, \
            "a dead entity must return NULL"
    return "a.b -> 'alpha.beta' / 'beta'; dead entity -> NULL"


@check("name-into-buffer")
def name_into_buffer():
    with isolated_world() as world:
        world.require_load("Names", "alpha { beta {} }")
        beta = world.lookup("alpha.beta")
        full, small = c.create_string_buffer(16), c.create_string_buffer(2)
        assert lib.fu_name_into(world.ctx, beta, full, 16) == 4 and full.value == b"beta", f"full buffer: {full.raw!r}"
        assert lib.fu_name_into(world.ctx, beta, small, 2) == 4, "a too-small buffer must still return the full length"
        assert small.raw == b"be", f"truncated copy {small.raw!r}"
        assert lib.fu_name_into(world.ctx, 0xDEAD0000, full, 16) == -1, "dead entity must return -1"
    return "returns 4 for 'beta'; copies 'be' into a 2-byte buffer; -1 for a dead entity"


@check("query-json-handle")
def query_json_handle():
    with isolated_world() as world:
        world.require_load("Things", "Probe { Position: {1,2,3} }")
        handle = lib.fu_query_create(world.ctx, b"Position")
        assert handle > 0, f"fu_query_create returned {handle}: {world.error()}"
        found = World.take(lib.fu_query_json_h(world.ctx, handle))
        assert found and "Probe" in found, f"Probe missing from {found}"
    return f"handle {handle} lists Probe"


@check("document-brief")
def document_brief():
    with isolated_world() as world:
        world.require_load("Docs", "Probe { Position: {1,2,3} }")
        assert lib.fu_document(world.ctx, b"Probe", b"a documented probe") == 0, "existing entity must return 0"
        assert lib.fu_document(world.ctx, b"Missing", b"x") == -1, "missing entity must return -1"
        described = json.dumps(world.entity("Probe"))
        assert "a documented probe" in described, f"brief not visible in entity JSON: {described}"
    return "0 for an existing entity, -1 for a missing one, brief present in entity JSON"


@check("script-capture")
def script_capture():
    with isolated_world() as world:
        world.require_load("CaptureSource", "Probe { Position: {1,2,3} }")
        assert lib.fu_script_capture(world.ctx, b"CaptureSource") == 0, "an unchanged scene must return 0"
        assert lib.fu_set_component_json(world.ctx, b"Probe", b"Position", b'{"x":9,"y":2,"z":3}') == 0, world.error()
        assert lib.fu_script_capture(world.ctx, b"CaptureSource") == 1, "a changed component must return 1"
        source = World.take(lib.fu_managed_script_source(world.ctx, b"CaptureSource"))
        assert source and "Position: {9," in source, f"new value missing from source: {source!r}"
        assert lib.fu_script_capture(world.ctx, b"Missing") == -1, "an unknown script must return -1"
    return "0 unchanged, 1 after a change with Position {9, 2, 3} in the source, -1 for an unknown script"


INPUT_SCRIPT = """
using flecs.script
template Widget {
  mut activations = 0
  mut downs = 0
  mut key = ""
  async { while true { await on.activate(this)
    activations = activations + 1 } }
  async { while true {
    const e = await on.key_down(this)
    downs = downs + 1
    key = e.key } }
}
Widget Button {}
"""


@check("pointer-and-keyboard")
def pointer_and_keyboard():
    with isolated_world() as world:
        world.require_load("Input", INPUT_SCRIPT)
        world.tick()
        button = world.lookup("Button")
        assert lib.fu_pointer(world.ctx, 0xDEAD0000, 0, 0, 1, 0) == -1, "an invalid entity must return -1"
        assert lib.fu_keyboard(world.ctx, None, 1, 0, 0) == -1, "a null key must return -1"
        assert lib.fu_pointer(world.ctx, button, 0, 0, 1, 0) >= 0 and lib.fu_pointer(world.ctx, button, 0, 0, 0, 0) >= 0
        world.tick()
        assert lib.fu_keyboard(world.ctx, b"a", 1, 0, 0) >= 0, "a valid key must return >= 0"
        world.tick()
        state = world.entity("Button")["components"]["Widget.mut"]
        assert state["activations"] == 1, f"on.activate did not fire once: {state}"
        assert (state["downs"], state["key"]) == (1, "a"), f"on.key_down did not deliver the focused key: {state}"
    return "invalid entity and null key -> -1; click raised on.activate once and key 'a' reached on.key_down of the focused entity"


# --- Type redeclaration guard ---------------------------------------------------------------------------------------
# A script that redeclares a type entities already use, with another layout, would change the component size under
# live data (docs/ENGINE-PLAN.md finding N12). The native entry points must refuse it and leave stored values intact.

def run_script(world, code):
    """Returns None on success, else the error text."""
    return None if lib.fu_run_script(world.ctx, code.encode()) == 0 else (world.error() or "failed")


def component(world, entity, kind):
    text = World.take(lib.fu_component_json(world.ctx, world.lookup(entity), kind.encode()))
    return json.loads(text) if text else None


def layout_faults(world):
    return json.loads(World.take(lib.fu_bridge_diagnostics(world.ctx)))["layoutFaults"]


WINGS = "struct Wings(span: f32)\nstruct Tail(len: f32)\na { Wings: {1}; Tail: {7} }\nb { Wings: {2}; Tail: {8} }"


@check("type-guard-rejects-layout-change")
def type_guard_rejects():
    with isolated_world() as world:
        assert run_script(world, WINGS) is None, world.error()
        refused = {
            "extra member": "struct Wings(span: f32, sweep: f32)",
            "other member type": "struct Wings(span: i32)",
            "renamed member": "struct Wings(reach: f32)",
            "compiled component": "struct Position(x: f32)",
        }
        for label, code in refused.items():
            problem = run_script(world, code)
            assert problem and "cannot be redeclared" in problem, f"{label} was accepted: {problem!r}"
        problem = world.load("OtherSource", "struct Wings(span: f32, sweep: f32)")
        assert problem and "Wings" in problem, f"another managed script redeclared Wings: {problem!r}"
        assert lib.fu_component_size(world.ctx, world.lookup("Wings")) == 4, "the size of Wings changed"
        assert (component(world, "a", "Wings"), component(world, "b", "Wings")) == ({"span": 1}, {"span": 2}), "stored values changed"
        assert layout_faults(world) == 0, "Flecs reported a component changed while in use"
        assert world.lookup("__fu_type_check") == 0, "the staging scope was left behind"
    return "in-use and compiled types keep their layout; one-shot and managed scripts are both refused; values intact"


@check("type-guard-allows-safe-declarations")
def type_guard_allows():
    with isolated_world() as world:
        assert run_script(world, WINGS) is None, world.error()
        allowed = {
            "identical redeclaration": "struct Wings(span: f32)",
            "keyword in a comment and a string": '// struct Wings(a: f32, b: f32)\nnote { Label: {"struct Wings(a: f32, b: f32)"} }',
            "new type": "struct Unused(v: f32)",
            "type nothing uses": "struct Unused(v: f32, w: f32)",
            "nested declaration of another type": "holder { struct Wings(span: f32, sweep: f32) }",
        }
        for label, code in allowed.items():
            problem = run_script(world, code)
            assert problem is None, f"{label} was refused: {problem}"
        # A managed script may change its own types: the reload deletes them (and their values) before evaluating.
        world.require_load("Schema", "struct Fin(h: f32)\nf1 { Fin: {3} }")
        world.require_load("Schema", "struct Fin(h: f32, k: f32)\nf1 { Fin: {3, 4} }")
        assert component(world, "f1", "Fin") == {"h": 3, "k": 4}, component(world, "f1", "Fin")
        # A module-scoped type is found under its module.
        world.require_load("Shop", "module shop\nstruct Price(v: f32)\nitem { Price: {5} }")
        problem = run_script(world, "module shop\nstruct Price(v: f32, tax: f32)")
        assert problem and "Price" in problem, f"module-scoped redeclaration was accepted: {problem!r}"
        assert layout_faults(world) == 0
    return "identical, unused, new, nested and own-script declarations pass; a module-scoped change is refused"


@check("type-guard-templates")
def type_guard_templates():
    with isolated_world() as world:
        assert run_script(world, "struct Tail(len: f32)\ntemplate Tree { prop height: f32 = 2\n trunk { Tail: {height} } }\nt1 { Tree: {height: 3} }") is None, world.error()
        assert run_script(world, "template Tree { prop height: f32 = 2\n trunk { Tail: {height * 10} } }") is None, world.error()
        world.tick(1)
        assert component(world, "t1.trunk", "Tail") == {"len": 30}, "a body-only change did not reach the instance"
        problem = run_script(world, "template Tree { prop height: f32 = 2\n prop width: f32 = 1\n trunk { Tail: {height} } }")
        assert problem and "Tree" in problem, f"a prop was added to a template in use: {problem!r}"
        assert component(world, "t1", "Tree") == {"height": 3}
    return "a template body may change in place; its props may not while instances exist"


@check("type-guard-reports-unchecked-forms")
def type_guard_fault_hook():
    # A Debug build of Flecs aborts the process on this fault by design; the report matters for Release builds.
    if os.environ.get("FLECS_UNITY_DEBUG_BUILD"):
        return "skipped: FLECS_UNITY_DEBUG_BUILD is set"
    with isolated_world() as world:
        assert run_script(world, WINGS) is None, world.error()
        before = layout_faults(world)
        # A member entity changes the struct without a declaration statement: the check cannot see it,
        # so the call must at least fail and say the world may be inconsistent.
        problem = run_script(world, "Wings { sweep { flecs.meta.member: {type: f32} } }")
        assert problem and "changed layout" in problem, f"the layout fault was not reported: {problem!r}"
        assert layout_faults(world) == before + 1
    return "a layout change through a member entity fails the call and is counted in the diagnostics"


# --- Native modules ------------------------------------------------------------------------------------------------
# Capability coordination, footprints and behaviour trees react to changes (docs/ENGINE-PLAN.md steps 1.1 to 1.3).

def dirty(world, entity):
    return lib.fu_cap_has(world.ctx, world.lookup(entity), b"CapDirty") == 1


def set_state(world, entity, phase, generation):
    state = json.dumps({"phase": phase, "appliedRevision": 1, "generation": generation, "message": ""}).encode()
    assert lib.fu_set_component_json(world.ctx, entity.encode(), b"CapState", state) == 0, world.error()


@check("modules-own-their-systems")
def modules_own_systems():
    with isolated_world() as world:
        systems = ["core.cap.MarkDirty", "core.geometry.UpdateFootprints", "core.tree.TickTrees", "app.SpinSystem", "app.GalleryRingMotionSystem"]
        missing = [name for name in systems if not world.lookup(name)]
        assert not missing, f"not found: {missing}"
        public = ["CapBinding", "CapState", "CapDependsOn", "CapDirty", "NativeTree", "NativeTreeResult", "NativeFootprint", "Position", "Spin", "GalleryLayerIntent"]
        moved = [name for name in public if not world.lookup(name)]
        assert not moved, f"public names must stay at the root scope: {moved}"
    return "systems live in core.cap, core.geometry, core.tree and app; public component names stay at the root"


@check("capability-coordination-reacts-to-changes")
def capability_coordination():
    with isolated_world() as world:
        assert run_script(world, "T { Position: {0,0,0} }\nA { CapBinding: {target: T, enabled: true, revision: 1} }\n"
                                 "B { CapBinding: {target: T, enabled: true, revision: 1}; (CapDependsOn, A) }\n"
                                 "C { CapBinding: {target: T, enabled: true, revision: 1} }") is None, world.error()
        a, b = world.lookup("A"), world.lookup("B")
        assert component(world, "A", "CapState") == {"phase": 0, "appliedRevision": 0, "generation": 0, "message": None}, \
            f"a new binding must report a state at once: {component(world, 'A', 'CapState')}"
        assert dirty(world, "A") and dirty(world, "B"), "a new binding must start dirty"
        assert lib.fu_cap_ready(world.ctx, a) == 1 and lib.fu_cap_ready(world.ctx, b) == 0, "B must wait for A"
        for entity in ("A", "B", "C"):
            lib.fu_cap_ack(world.ctx, world.lookup(entity))
        world.tick(1)
        assert not dirty(world, "B"), "an acknowledged binding became dirty with no change"
        set_state(world, "A", 1, 1)
        world.tick(1)
        assert dirty(world, "B") and lib.fu_cap_ready(world.ctx, b) == 1, "a prerequisite result must mark its consumer"
        assert not dirty(world, "C"), "an unrelated binding was marked"
        lib.fu_cap_ack(world.ctx, b)
        set_state(world, "A", 1, 1)
        world.tick(1)
        assert not dirty(world, "B"), "the same result again must not mark the consumer"
        set_state(world, "A", -1, 1)
        world.tick(1)
        assert dirty(world, "B") and lib.fu_cap_ready(world.ctx, b) == -1, "a failed prerequisite must mark and fail its consumer"
        lib.fu_cap_ack(world.ctx, b)
        assert run_script(world, "C { (CapDependsOn, A) }") is None, world.error()
        world.tick(1)
        assert dirty(world, "C"), "a new prerequisite must mark the binding"
        lib.fu_cap_ack(world.ctx, world.lookup("C"))
        assert lib.fu_delete_entity(world.ctx, b"A") == 1
        world.tick(1)
        assert dirty(world, "B") and dirty(world, "C"), "a deleted prerequisite must mark its consumers"
        assert lib.fu_cap_ready(world.ctx, b) == 1, "a binding without prerequisites is ready"
    return "new bindings start dirty with a state; results, added and deleted prerequisites mark consumers; repeats do not"


@check("trees-rest-while-waiting")
def trees_rest():
    xml = ('<root BTCPP_format="4"><BehaviorTree ID="Main"><Sequence><SimWait seconds="0.5"/>'
           '<FlecsSet entity="Value" component="Position" value="{&quot;x&quot;:7,&quot;y&quot;:0,&quot;z&quot;:0}"/>'
           '</Sequence></BehaviorTree></root>').replace('"', '\\"').replace("{", "\\{")
    with isolated_world() as world:
        assert run_script(world, 'Value { Position: {0,0,0} }\nWaiter { NativeTree: {xml: "' + xml + '", revision: 1, enabled: true, loop: false} }') is None, world.error()
        world.tick(2)
        first = component(world, "Waiter", "NativeTreeResult")
        world.tick(10)                       # 0.24 s of simulation time: still waiting
        resting = component(world, "Waiter", "NativeTreeResult")
        assert first["status"] == 1 and resting["ticks"] == first["ticks"], f"a waiting tree was ticked: {first} -> {resting}"
        assert component(world, "Value", "Position")["x"] == 0
        world.tick(20)                       # past 0.5 s
        done = component(world, "Waiter", "NativeTreeResult")
        assert done["status"] == 2 and component(world, "Value", "Position")["x"] == 7, f"the tree did not finish on time: {done}"
        # Setting the same definition again must not restart a finished tree.
        assert run_script(world, 'Waiter { NativeTree: {xml: "' + xml + '", revision: 1, enabled: true, loop: false} }') is None, world.error()
        world.tick(2)
        assert component(world, "Waiter", "NativeTreeResult")["ticks"] == done["ticks"], "an identical definition restarted the tree"
    return "a tree waiting for simulation time is not ticked, finishes on time, and is not restarted by an identical definition"


@check("query-once-takes-no-slot")
def query_once():
    with isolated_world() as world:
        assert run_script(world, "; ".join(f"p{i} {{ Position: {{{i},0,0}} }}" for i in range(5))) is None, world.error()
        slots = lambda: json.loads(World.take(lib.fu_bridge_diagnostics(world.ctx)))["cachedQueries"]
        before = slots()
        rows = json.loads(World.take(lib.fu_query_once_json(world.ctx, b"Position", 0)))["results"]
        assert len(rows) == 5, f"expected 5 rows, got {len(rows)}"
        limited = json.loads(World.take(lib.fu_query_once_json(world.ctx, b"Position", 2)))["results"]
        assert len(limited) == 2, f"a limit of 2 returned {len(limited)} rows"
        assert not lib.fu_query_once_json(world.ctx, b"Position, (", 0), "an invalid expression must return NULL"
        assert "Invalid query" in world.error(), world.error()
        assert slots() == before, "a one-off query took a query slot"
    return "all rows, a row limit, NULL with an error for an invalid expression, and no query slot used"


# --- ABI 4: view changes, type layout, events ----------------------------------------------------------------------

class View(c.Structure):
    _fields_ = [("entity", c.c_uint64), ("parent", c.c_uint64)] + [
        (name, c.c_float) for name in "px py pz rx ry rz sx sy sz r g b a".split()
    ] + [("primitive", c.c_int32), ("has_label", c.c_int32)] + [
        (name, c.c_float) for name in "qx qy qz qw".split()] + [("flags", c.c_uint32)]


class Event(c.Structure):
    _fields_ = [("event", c.c_uint64), ("entity", c.c_uint64), ("other", c.c_uint64),
                ("x", c.c_float), ("y", c.c_float), ("z", c.c_float), ("value", c.c_float)]


def bind_extra(name, result, args):
    function = getattr(lib, name)
    function.restype, function.argtypes = result, args
    return function


view_changes = bind_extra("fu_view_changes", c.POINTER(View),
                          [P, c.POINTER(c.c_int32), c.POINTER(c.POINTER(c.c_uint64)), c.POINTER(c.c_int32)])
read_views = bind_extra("fu_read_views", c.c_int32, [P, c.POINTER(View), c.c_int32])
emit_events = bind_extra("fu_emit_events", c.c_int32, [P, c.POINTER(Event), c.c_int32])
claim = bind_extra("fu_claim", c.c_int32, [P, c.c_uint64, c.c_uint64, c.c_uint64])
release = bind_extra("fu_release", c.c_int32, [P, c.c_uint64, c.c_uint64, c.c_uint64])


def changes(world):
    """One fu_view_changes call: ({entity: copy of the view}, [removed entity ids])."""
    count, removed_count, removed = c.c_int32(-1), c.c_int32(-1), c.POINTER(c.c_uint64)()
    views = view_changes(world.ctx, c.byref(count), c.byref(removed), c.byref(removed_count))
    assert views, f"fu_view_changes returned NULL: {world.error()}"
    changed = {}
    for i in range(count.value):
        copy = View()
        c.memmove(c.byref(copy), c.byref(views[i]), c.sizeof(View))
        changed[copy.entity] = copy
    return changed, [removed[i] for i in range(removed_count.value)]


@check("view-changes")
def view_changes_stream():
    with isolated_world() as world:
        world.require_load("Views", "a { Position: {1,0,0} }\nb { Position: {2,0,0}; Rotation: {0,45,0} }\n"
                                    "c { Position: {3,0,0}; Rotation: {0,0,0}; Spin: {0,90,0} }\n"
                                    "d { Position: {4,0,0}; Orientation: {0, 0.7071, 0, 0.7071} }")
        ids = {name: world.lookup(name) for name in "abcd"}
        assert all(ids.values()), f"entities missing: {ids}"
        first, removed = changes(world)
        assert set(first) == set(ids.values()) and not removed, \
            f"first call must return every view, got {len(first)}, removed {removed}"
        d, a = first[ids["d"]], first[ids["a"]]
        assert d.flags & 1 and abs(d.qy - 0.7071) < 1e-4 and abs(d.qw - 0.7071) < 1e-4 and d.qx == 0 and d.qz == 0, \
            f"oriented view: flags {d.flags}, q ({d.qx}, {d.qy}, {d.qz}, {d.qw})"
        assert a.flags == 0 and a.qw == 1 and (a.qx, a.qy, a.qz) == (0, 0, 0), \
            f"unoriented view: flags {a.flags}, q ({a.qx}, {a.qy}, {a.qz}, {a.qw})"
        again, removed = changes(world)
        assert not again and not removed, f"nothing changed, got {len(again)} views, removed {removed}"
        # fu_read_views returns every view and leaves the change stream alone.
        buffer = (View * 16)()
        assert read_views(world.ctx, buffer, 16) == 4, "fu_read_views must return all four views"
        between, removed = changes(world)
        assert not between and not removed, "fu_read_views disturbed the change stream"
        world.tick(1)
        spun, _ = changes(world)
        assert set(spun) == {ids["c"]}, f"after a tick only the spinning entity changes, got {sorted(spun)}"
        assert lib.fu_set_component_json(world.ctx, b"a", b"Position", b'{"x":9,"y":0,"z":0}') == 0, world.error()
        written, _ = changes(world)
        assert ids["a"] in written and written[ids["a"]].px == 9, \
            f"a Position write must resend that entity: {sorted(written)}"
        assert ids["d"] not in written and ids["c"] not in written, "entities in other tables must not be resent"
        assert lib.fu_delete_entity(world.ctx, b"b") == 1, "b was not deleted"
        gone, removed = changes(world)
        assert removed == [ids["b"]] and ids["b"] not in gone, f"a deleted entity must be listed as removed: {removed}"
        world.require_load("Gain", "a { Color: {1,0,0,1} }")
        moved, removed = changes(world)
        assert ids["a"] in moved and moved[ids["a"]].r == 1 and not removed, \
            f"an entity that moves table is resent: {sorted(moved)}, removed {removed}"
    return ("all views first, nothing when idle, only the spinner after a tick, writes and table moves resent, "
            "deletes removed, read_views leaves the stream alone, Orientation flag and values")


@check("type-layout")
def type_layout():
    def layout(world, name, capacity=4096):
        buffer = c.create_string_buffer(capacity)
        length = lib.fu_type_layout_into(world.ctx, world.lookup(name), buffer, capacity)
        return length, buffer

    def parse(length, buffer):
        assert 0 < length < len(buffer.raw), f"layout length {length}"
        return json.loads(buffer.raw[:length].decode())

    with isolated_world() as world:
        world.require_load("Mix", "struct Mix(a: f32, b: bool, c: i32)")
        world.require_load("Plain", "thing {}")
        info = parse(*layout(world, "Position"))
        assert info["size"] == 12 and [(f["offset"], f["size"], f["kind"]) for f in info["fields"]] == \
            [(0, 4, "f32"), (4, 4, "f32"), (8, 4, "f32")], info
        info = parse(*layout(world, "CapBinding"))
        fields = {f["name"]: f for f in info["fields"]}
        assert info["size"] == 16, info
        assert (fields["target"]["offset"], fields["target"]["kind"]) == (0, "entity"), fields.get("target")
        assert (fields["enabled"]["offset"], fields["enabled"]["kind"]) == (8, "bool"), fields.get("enabled")
        assert (fields["revision"]["offset"], fields["revision"]["kind"]) == (12, "i32"), fields.get("revision")
        info = parse(*layout(world, "Presented"))
        assert "position.x" in {f["name"] for f in info["fields"]}, f"nested leaves: {[f['name'] for f in info['fields']]}"
        info = parse(*layout(world, "Mix"))
        assert [(f["name"], f["offset"]) for f in info["fields"]] == [("a", 0), ("b", 4), ("c", 8)], info
        length, _ = layout(world, "Position")
        small, buffer = layout(world, "Position", 8)
        assert small == length and len(buffer.value) < length, \
            f"a too-small buffer must return the full length {length}, got {small}"
        assert lib.fu_type_layout_into(world.ctx, 0xDEAD0000, c.create_string_buffer(64), 64) == -1, \
            "an unknown id must return -1"
        assert lib.fu_type_layout_into(world.ctx, world.lookup("thing"), c.create_string_buffer(64), 64) == -1, \
            "a non-component entity must return -1"
    return ("Position, CapBinding, Presented (position.x) and a Script struct report offsets; a short buffer returns "
            "the full length; unknown and non-component ids return -1")


@check("emit-events")
def emit_event_calls():
    with isolated_world() as world:
        world.require_load("Events", "Touched {}\nsubject {}\nother {}")
        touched, subject, other = (world.lookup(n) for n in ("Touched", "subject", "other"))
        live = (Event * 1)(Event(touched, subject, other, 1, 2, 3, 0.5))
        assert lib.fu_emit_events(world.ctx, live, 1) == 1, "a live event and entity must be delivered"
        mixed = (Event * 2)(Event(touched, 0xDEAD0000, 0, 0, 0, 0, 0), Event(touched, subject, 0, 0, 0, 0, 0))
        assert lib.fu_emit_events(world.ctx, mixed, 2) == 1, "an entry with a dead entity must be skipped"
        assert lib.fu_emit_events(world.ctx, None, 3) == -1, "a null array with a positive count is a bad argument"
    return "1 delivered for a live event and entity; a dead entity is skipped; -1 for a null array"


@check("ownership")
def exclusive_ownership():
    with isolated_world() as world:
        world.require_load("Owned", "view { Position: {0,0,0} }\nOwnerA {}\nOwnerB {}")
        view, a, b = (world.lookup(n) for n in ("view", "OwnerA", "OwnerB"))
        pose, look = world.lookup("TransformOwner"), world.lookup("AppearanceOwner")
        assert all((view, a, b, pose, look)), "the view, both owners and both relationships must exist"
        changes(world)
        assert claim(world.ctx, view, pose, a) == 1 and claim(world.ctx, view, pose, a) == 1, "a free relation, then again by its owner"
        assert claim(world.ctx, view, pose, b) == 0, "a second owner must be refused"
        assert changes(world)[0][view].flags == 2, "a pose claim must set the pose-owned flag"
        assert release(world.ctx, view, pose, b) == 0, "a non-owner must not release"
        assert release(world.ctx, view, pose, a) == 1 and release(world.ctx, view, pose, a) == 0, "release once, then nothing to release"
        assert changes(world)[0][view].flags == 0, "release must clear the flag"
        assert claim(world.ctx, view, look, b) == 1, "the appearance relation is independent"
        assert changes(world)[0][view].flags == 4, "an appearance claim must set the appearance-owned flag"
        rows = World.take(lib.fu_query_once_json(world.ctx, b"(AppearanceOwner, $owner)", 0))
        assert rows and len(json.loads(rows)["results"]) == 1, "the owner must be visible to queries"
        assert claim(world.ctx, view, pose, a) == 1
        assert lib.fu_delete_entity(world.ctx, b"OwnerB") == 1 and lib.fu_delete_entity(world.ctx, b"OwnerA") == 1
        view_after = changes(world)[0][view]
        assert view_after.flags == 0, f"deleting the owners must clear both flags, got {view_after.flags}"
        assert claim(world.ctx, view, pose, b) == -1, "a deleted owner is a bad argument"
        assert claim(world.ctx, 0, pose, a) == -1 and release(world.ctx, view, pose, 0) == -1, "zero ids are bad arguments"
        # A component entity as owner (the app module owns a saved world's origin): the pair must carry no data.
        module = world.lookup("app")
        assert module and claim(world.ctx, view, pose, module) == 1, "a module entity must be able to own"
        assert lib.fu_tick(world.ctx, 0.016) == 0 and changes(world)[0][view].flags == 2
        assert release(world.ctx, view, pose, module) == 1
    return "exclusive claims, release by owner only, flags 2 and 4 in the view stream, cleared when the owner is deleted"


# --- ABI 5: sources and kinds ---------------------------------------------------------------------------------------
# A source is a named script text that defines kinds; applying it again edits them in place (src/Sources.cpp).

source_apply = bind_extra("fu_source_apply", c.c_int32, [P, S, S, S])
source_undo = bind_extra("fu_source_undo", c.c_int32, [P, S])
source_remove = bind_extra("fu_source_remove", c.c_int32, [P, S])
source_text_into = bind_extra("fu_source_text_into", c.c_int32, [P, S, c.c_char_p, c.c_int32])
error_position = bind_extra("fu_error_position", c.c_int32, [P, c.POINTER(c.c_int32), c.POINTER(c.c_int32)])
spawn = bind_extra("fu_spawn", c.c_uint64, [P, c.c_uint64, c.c_uint64, S])
attach = bind_extra("fu_attach", c.c_int32, [P, c.c_uint64])

PROBE_KIND = "prefab Probe {\n  Color: {1,0,0,1}\n  Primitive: {0}\n  prefab part { Color: {0,1,0,1} }\n}"


def apply_source(world, name, text):
    return source_apply(world.ctx, name.encode(), b"library", text.encode())


def source_text(world, name):
    buffer = c.create_string_buffer(8192)
    length = source_text_into(world.ctx, name.encode(), buffer, 8192)
    return None if length < 0 else buffer.raw[:length].decode()


def color_of(world, entity, field="r"):
    """One field of the Color an entity has, owned or inherited; None when it has none."""
    text = World.take(lib.fu_component_json(world.ctx, entity, b"Color"))
    return json.loads(text)[field] if text else None


@check("sources")
def sources():
    with isolated_world() as world:
        assert apply_source(world, "sources.Probe", PROBE_KIND) == 0, world.error()
        kind, part = world.lookup("library.Probe"), world.lookup("library.Probe.part")
        assert kind and part, "the kind and its part must exist under the scope"
        assert world.lookup("library.__fu_staging") == 0, "the staging scope was left behind"
        instance = spawn(world.ctx, kind, 0, b"i1")
        assert instance and world.lookup("i1.part"), f"spawn failed: {world.error()}"
        part_instance = world.lookup("i1.part")
        # An edit keeps every id and reaches the instance.
        edited = PROBE_KIND.replace("{1,0,0,1}", "{0.5,0,0,1}")
        assert apply_source(world, "sources.Probe", edited) == 0, world.error()
        assert (world.lookup("library.Probe"), world.lookup("library.Probe.part"), world.lookup("i1"), world.lookup("i1.part")) == \
            (kind, part, instance, part_instance), "an edit changed an entity id"
        assert color_of(world, instance) == 0.5, "the instance did not read the edited value"
        assert source_text(world, "sources.Probe") == edited, "the stored text is not the applied text"
        # A refused text changes nothing and reports its position.
        before = source_text(world, "sources.Probe")
        broken = "prefab Probe {\n  Color: {1,0,0,1}\n  Missing: {2}\n}"
        assert apply_source(world, "sources.Probe", broken) == -1, "an unknown type must be refused"
        line, column = c.c_int32(0), c.c_int32(0)
        assert error_position(world.ctx, c.byref(line), c.byref(column)) == 1 and line.value == 3, \
            f"error position: line {line.value}, column {column.value}"
        assert world.error(), "a refused text must leave an error message"
        assert color_of(world, instance) == 0.5 and source_text(world, "sources.Probe") == before, "a refused text changed the kind or its text"
        assert world.lookup("library.__fu_staging") == 0, "a refused text left the staging scope behind"
        refused = {
            "type declaration": "struct Extra(v: f32)\n" + edited,
            "entity that is not a kind": edited + "\nloose { Color: {0,0,1,1} }",
        }
        for label, text in refused.items():
            assert apply_source(world, "sources.Probe", text) == -1, f"{label} was accepted"
            assert source_text(world, "sources.Probe") == before and world.lookup("library.loose") == 0, f"{label} changed something"
        assert apply_source(world, "sources.New", "prefab Fresh { Color: {1,1,1,1} }\nprefab Bad : Missing {}") == -1, "a new source with an error"
        assert world.lookup("library.Fresh") == 0 and source_text(world, "sources.New") is None, "a refused new source left something"
        # A kind in use cannot be dropped or removed.
        assert apply_source(world, "sources.Probe", "prefab Other { Color: {1,1,1,1} }") == -1, "dropping a kind in use must be refused"
        assert source_remove(world.ctx, b"sources.Probe") == -1, "removing a source whose kind is in use must be refused"
        assert world.lookup("library.Probe") == kind and color_of(world, instance) == 0.5, "the kind in use was changed"
        # Undo returns the previous values.
        assert apply_source(world, "sources.Probe", PROBE_KIND.replace("{1,0,0,1}", "{0.25,0,0,1}")) == 0, world.error()
        assert color_of(world, instance) == 0.25
        assert source_undo(world.ctx, b"sources.Probe") >= 0, world.error()
        assert color_of(world, instance) == 0.5 and source_text(world, "sources.Probe") == edited, "undo did not restore the previous text"
        assert source_undo(world.ctx, b"sources.Missing") == -1, "undo of an unknown source must return -1"
        # Text reading: the current text, the full length for a short buffer, -1 for an unknown source.
        small = c.create_string_buffer(4)
        assert source_text_into(world.ctx, b"sources.Probe", small, 4) == len(edited.encode()), "a short buffer must return the full length"
        assert source_text_into(world.ctx, b"sources.Missing", small, 4) == -1, "an unknown source must return -1"
        # Once nothing uses the kind, the source can be removed with what it defined.
        assert lib.fu_delete_entity(world.ctx, b"i1") == 1
        assert source_remove(world.ctx, b"sources.Probe") >= 0, world.error()
        assert world.lookup("library.Probe") == 0 and source_text(world, "sources.Probe") is None, "the removed source left its kind behind"
        assert world.lookup("library.__fu_staging") == 0, "the staging scope exists"
    return ("edit keeps ids; refused texts (script error with line 3, struct, non-prefab, new source, kind in use) change nothing; "
            "undo restores; text reads full length and -1; removal after the last instance; no staging scope")


def instance_is(world, path, base):
    return world.entity(path).get("pairs", {}).get("flecs.core.IsA") == base


@check("kinds")
def kinds():
    count = 1000
    text = ("prefab Craft {\n  Position: {0,0,0}\n  Color: {1,0,0,1}\n  Primitive: {0}\n"
            "  prefab wing {\n    Position: {1,0,0}\n    Color: {0,1,0,1}\n    prefab tip { Color: {0,0,1,1} }\n  }\n}")
    with isolated_world() as world:
        assert apply_source(world, "sources.Craft", text) == 0, world.error()
        kind = world.lookup("library.Craft")
        ids = [spawn(world.ctx, kind, 0, f"i{n}".encode()) for n in range(count)]
        assert all(ids) and len(set(ids)) == count, f"spawn failed: {world.error()}"
        # Every part follows the kind's part. Built-in components are not inherited: an instance owns a copy.
        for n in range(count):
            for path, base in ((f"i{n}", "library.Craft"), (f"i{n}.wing", "library.Craft.wing"), (f"i{n}.wing.tip", "library.Craft.wing.tip")):
                assert instance_is(world, path, base), f"{path} does not follow {base}"
        for path in ("i0", "i0.wing", "i999", "i999.wing.tip"):
            owned = world.entity(path).get("components", {})
            assert "Color" in owned, f"{path} does not own a copy of Color: {sorted(owned)}"
        wings = [world.lookup(f"i{n}.wing") for n in range(count)]
        tips = [world.lookup(f"i{n}.wing.tip") for n in range(count)]
        assert all(wings) and all(tips), "an instance is missing a part"
        first, removed = changes(world)
        assert set(ids) <= set(first) and not removed, f"the first report must contain every instance ({len(first)} views)"
        # One instance has its own value, then the kind and a part change.
        assert lib.fu_set_component_json(world.ctx, b"i7", b"Color", b'{"r":0.9,"g":0,"b":0,"a":1}') == 0, world.error()
        changes(world)
        edited = text.replace("Color: {1,0,0,1}", "Color: {0.5,0,0,1}").replace("Color: {0,1,0,1}", "Color: {0,0.5,0,1}")
        assert apply_source(world, "sources.Craft", edited) == 0, world.error()
        assert world.lookup("library.Craft") == kind, "the kind id changed"
        wrong = [n for n in range(count) if n != 7 and color_of(world, ids[n]) != 0.5]
        assert not wrong, f"{len(wrong)} instances did not read the new colour, first {wrong[:3]}"
        assert abs(color_of(world, ids[7]) - 0.9) < 1e-6, "an instance with its own value lost it"
        wrong = [n for n in range(count) if color_of(world, wings[n], "g") != 0.5]
        assert not wrong, f"{len(wrong)} parts did not read the new colour, first {wrong[:3]}"
        moved, removed = changes(world)
        assert not removed, f"an edit reported removed views: {removed[:3]}"
        missing = [n for n in range(count) if n != 7 and (ids[n] not in moved or moved[ids[n]].r != 0.5)]
        assert not missing, f"fu_view_changes did not resend {len(missing)} instances with the new value, first {missing[:3]}"
        assert all(wings[n] in moved and moved[wings[n]].g == 0.5 for n in range(count)), "fu_view_changes did not resend every part"
        # A part added to the kind appears on every instance.
        grown = edited.replace("  prefab wing", "  prefab fin { Color: {1,1,0,1} }\n  prefab wing")
        assert apply_source(world, "sources.Craft", grown) == 0, world.error()
        fins = [world.lookup(f"i{n}.fin") for n in range(count)]
        assert all(fins), f"{fins.count(0)} instances lack the new part"
        assert all(instance_is(world, f"i{n}.fin", "library.Craft.fin") for n in (0, 500, 999)), "a new part does not follow the kind's part"
        assert color_of(world, fins[3]) == 1 and abs(color_of(world, ids[7]) - 0.9) < 1e-6, "values after the part was added"
        # A direct write to the kind reaches the instances that still hold its old value.
        assert lib.fu_set_component_json(world.ctx, b"library.Craft", b"Color", b'{"r":0.3,"g":0,"b":0,"a":1}') == 0, world.error()
        wrong = [n for n in range(count) if n != 7 and abs(color_of(world, ids[n]) - 0.3) > 1e-6]
        assert not wrong, f"{len(wrong)} instances did not follow a direct write to the kind, first {wrong[:3]}"
        assert abs(color_of(world, ids[7]) - 0.9) < 1e-6, "an own value was overwritten by a direct write to the kind"
        # An instance made by a script is attached to its kind.
        assert lib.fu_run_script(world.ctx, b"x : library.Craft {}") == 0, world.error()
        assert attach(world.ctx, world.lookup("x")) >= 1, "fu_attach must report the kinds of the instance"
        assert instance_is(world, "x.wing", "library.Craft.wing") and world.lookup("x.fin"), "the scripted instance does not have its parts"
        assert attach(world.ctx, 0xDEAD0000) == -1, "a dead entity must return -1"
        # A component that content declares as inherited is read from the kind and not stored on the instance.
        assert lib.fu_run_script(world.ctx, b"struct Speed(v: f32)\nSpeed { (OnInstantiate, Inherit) }") == 0, world.error()
        assert apply_source(world, "sources.Rig", "prefab Rig {\n  Position: {0,0,0}\n  Speed: {5}\n}") == 0, world.error()
        rig = spawn(world.ctx, world.lookup("library.Rig"), 0, b"r1")
        assert rig, world.error()
        assert "Speed" not in world.entity("r1").get("components", {}), "an inherited component is stored on the instance"
        assert component(world, "r1", "Speed") == {"v": 5}, f"the instance does not read the kind's Speed: {component(world, 'r1', 'Speed')}"
    return (f"{count} instances: parts follow, each owns a copy of Color, kind and part edits and a direct write reach all, "
            "fu_view_changes resends all and removes none, an added part reaches all, an own value stays, fu_attach on a scripted instance, "
            "a declared inherited component stays on the kind")


# --- ABI 5: worlds --------------------------------------------------------------------------------------------------
# A world is the subtree under worlds.<id>: scoped tools, a document, play and rewind (src/Worlds.cpp).

world_create = bind_extra("fu_world_create", c.c_uint64, [P, S])
world_delete = bind_extra("fu_world_delete", c.c_int32, [P, c.c_uint64])
world_enter = bind_extra("fu_world_enter", c.c_int32, [P, c.c_uint64])
world_entered = bind_extra("fu_world_entered", c.c_uint64, [P])
world_query = bind_extra("fu_world_query_json", P, [P, c.c_uint64, S, c.c_int32])
world_run = bind_extra("fu_world_run_script", c.c_int32, [P, c.c_uint64, S])
world_save = bind_extra("fu_world_save_json", P, [P, c.c_uint64])
world_load = bind_extra("fu_world_load_json", c.c_uint64, [P, S, S])
world_snapshot = bind_extra("fu_world_snapshot", c.c_int32, [P, c.c_uint64])
world_rewind = bind_extra("fu_world_rewind", c.c_uint64, [P, c.c_uint64])

WORLD_SCHEMA = "struct Fuel(left: f32)\nstruct Pilot(who: entity)\nLikes {}\nBroken {}"
PLANE = ("prefab Plane {\n  Position: {0,0,0}\n  Color: {1,0,0,1}\n  Primitive: {0}\n"
         "  prefab engine { Position: {0,1,0}; Color: {0,1,0,1} }\n}")
PLANE_CONTENT = ("p1 { Position: {1,2,3}; Fuel: {7} }\np1 { engine { Position: {5,5,5} } }\n"
                 "bob { Position: {4,0,0}; Label: {\"Bob\"}; (Likes, p1); Broken }\np2 { Pilot: {bob} }")


def make_world(world, name):
    """A world with a kind of its own, two instances (p1 changed), a plain entity with a tag, a pair and an entity reference."""
    entity = world_create(world.ctx, name.encode())
    assert entity, world.error()
    assert source_apply(world.ctx, f"worlds.{name}.sources.Plane".encode(), f"worlds.{name}.kinds".encode(), PLANE.encode()) == 0, world.error()
    kind = world.lookup(f"worlds.{name}.kinds.Plane")
    assert spawn(world.ctx, kind, entity, b"p1") and spawn(world.ctx, kind, entity, b"p2"), world.error()
    assert world_run(world.ctx, entity, PLANE_CONTENT.encode()) == 0, world.error()
    return entity


def world_rows(world, entity, expression):
    """Paths of the rows of a scoped query, or None with the error left in world.error()."""
    text = World.take(world_query(world.ctx, entity, expression.encode(), 0))
    if text is None:
        return None
    return sorted((row["parent"] + "." if row.get("parent") else "") + row["name"] for row in json.loads(text)["results"])


@check("worlds")
def worlds():
    with isolated_world() as world:
        assert lib.fu_run_script(world.ctx, WORLD_SCHEMA.encode()) == 0, world.error()
        a, b = make_world(world, "a"), make_world(world, "b")
        assert a and b and a != b, "two worlds with the same entity names must exist"
        assert world_create(world.ctx, b"a") == a, "creating a world again must return it"
        assert world_create(world.ctx, b"not valid") == 0 and world.error(), "a bad id must be refused"
        assert world_rows(world, a, "Position") == ["worlds.a.bob", "worlds.a.p1", "worlds.a.p1.engine", "worlds.a.p2", "worlds.a.p2.engine"], \
            f"a query in world a: {world_rows(world, a, 'Position')}"
        assert world_rows(world, b, "Position") == ["worlds.b.bob", "worlds.b.p1", "worlds.b.p1.engine", "worlds.b.p2", "worlds.b.p2.engine"], \
            f"a query in world b: {world_rows(world, b, 'Position')}"
        assert world_rows(world, a, "Fuel") == ["worlds.a.p1"], "a query with a component"
        assert world_rows(world, a, "Label, (Likes, $who)") == ["worlds.a.bob"], "a query with a pair"
        assert world_rows(world, a, "not a query (") is None and "Invalid query" in world.error(), \
            f"an invalid expression must return NULL with an error: {world.error()!r}"
        assert world_rows(world, a, "Position") is not None, "the world must work after an invalid query"
        # A script runs under its world only.
        assert world_run(world.ctx, a, b"only_a { Position: {9,9,9} }") == 0, world.error()
        assert world.lookup("worlds.a.only_a") and not world.lookup("worlds.b.only_a") and not world.lookup("only_a"), \
            "a script in world a created an entity outside it"
        assert world_run(world.ctx, a, b"Missing: {") == -1 and world.error(), "a bad script must be refused with an error"
        assert world_run(world.ctx, a, b"worlds { b { bob { Position: {77,0,0} } } }") == 0, world.error()
        assert component(world, "worlds.b.bob", "Position")["x"] == 4, "world b changed"
        # Entering.
        assert world_entered(world.ctx) == 0, "no world is entered at first"
        assert world_enter(world.ctx, a) == 0 and world_entered(world.ctx) == a, "enter a"
        assert world_enter(world.ctx, b) == 0 and world_entered(world.ctx) == b, "entering another leaves the first"
        assert world_enter(world.ctx, world.lookup("worlds.a.bob")) == -1, "an entity that is not a world cannot be entered"
        assert world_entered(world.ctx) == b, "a refused enter changed the entered world"
        assert world_enter(world.ctx, 0) == 0 and world_entered(world.ctx) == 0, "0 leaves"
        # Deleting one world leaves the other working.
        changes(world)
        old_b = [world.lookup(p) for p in ("worlds.b.bob", "worlds.b.p1", "worlds.b.p1.engine")]
        assert all(old_b), "world b entities missing"
        assert world_enter(world.ctx, b) == 0
        assert world_delete(world.ctx, b) == 1, world.error()
        assert not world.lookup("worlds.b") and not world.lookup("worlds.b.p1") and not world.lookup("worlds.b.kinds.Plane"), "the world or its content is left"
        assert source_text(world, "worlds.b.sources.Plane") is None and source_text(world, "worlds.a.sources.Plane"), \
            "the sources of the deleted world are left, or the other world lost its own"
        assert world_entered(world.ctx) == 0, "deleting the entered world must leave it"
        assert world_delete(world.ctx, b) == 0 and world_delete(world.ctx, world.lookup("worlds.a.bob")) == 0, "an entity that is not a world is not deleted"
        assert world.lookup("worlds.a.p1") and world.lookup("worlds.a.kinds.Plane"), "world a lost something"
        world.tick(2)
        gone, removed = changes(world)
        assert set(old_b) <= set(removed), f"the views of the deleted world were not reported removed: {removed}"
        assert world_run(world.ctx, a, b"p1 { Position: {8,0,0} }") == 0, world.error()
        moved, removed = changes(world)
        assert world.lookup("worlds.a.p1") in moved and moved[world.lookup("worlds.a.p1")].px == 8 and not removed, \
            f"world a does not report its change after the other was deleted: {sorted(moved)}, removed {removed}"
        assert world_rows(world, a, "Fuel") == ["worlds.a.p1"], "world a does not answer queries"
    return ("queries and scripts stay inside their world, invalid query returns NULL with an error, enter/entered/leave, "
            "a non-world is refused, deleting a world removes its content and sources and leaves the other world working")


def saved(world, entity):
    text = World.take(world_save(world.ctx, entity))
    assert text, f"fu_world_save_json returned NULL: {world.error()}"
    return text


def refuse(world, name, text, entity, expected_fuel):
    """Loads a broken document: it must be refused with an error and leave the loaded world as it was."""
    before = [world.lookup(p) for p in ("worlds.a", "worlds.a.p1", "worlds.a.p1.engine", "worlds.a.bob", "worlds.a.kinds.Plane")]
    assert world_load(world.ctx, b"a", text.encode()) == 0, f"{name} was accepted"
    assert world.error(), f"{name} was refused without an error message"
    after = [world.lookup(p) for p in ("worlds.a", "worlds.a.p1", "worlds.a.p1.engine", "worlds.a.bob", "worlds.a.kinds.Plane")]
    assert before == after and before[0] == entity, f"{name} changed entity ids"
    assert component(world, "worlds.a.p1", "Fuel") == {"left": expected_fuel}, f"{name} changed a value"
    assert not world.lookup("worlds.__loading"), f"{name} left worlds.__loading behind"


@check("world-documents")
def world_documents():
    with isolated_world() as world:
        assert lib.fu_run_script(world.ctx, WORLD_SCHEMA.encode()) == 0, world.error()
        a = make_world(world, "a")
        document = saved(world, a)
        parsed = json.loads(document)
        assert parsed["format"] == 2, f"document format {parsed['format']}"
        assert any("prefab Plane" in source["text"] for source in parsed["sources"]), "the kind's source text is not in the document"
        records = {record["path"]: record for record in parsed["entities"]}
        assert "p1.engine" in records and "p2.engine" not in records, "a part is written only where the instance changed it"
        assert set(records["p2"]["components"]) == {"Pilot"}, f"p2 writes more than it stores itself: {records['p2']['components']}"
        assert set(records["p1"]["components"]) == {"Position", "Fuel"}, f"p1 components: {records['p1']['components']}"
        assert '"Color"' not in document and "Presented" not in document, "a value an instance has from its kind or a run-time result was written"
        assert records["bob"]["tags"] == ["Broken"] and records["bob"]["pairs"] == [["Likes", "~p1"]], "tag and pair records"
        assert "Bob" in json.dumps(records["bob"]["components"]), "the string value is not in the document"
        # Under another id the document gives the same world.
        copy = world_load(world.ctx, b"copy", document.encode())
        assert copy and world.lookup("worlds.copy") == copy, f"load failed: {world.error()}"
        assert component(world, "worlds.copy.p1", "Position") == {"x": 1, "y": 2, "z": 3}, "an own value is not back"
        assert component(world, "worlds.copy.p1", "Fuel") == {"left": 7}
        assert component(world, "worlds.copy.p1.engine", "Position")["x"] == 5, "the changed part is not back"
        assert component(world, "worlds.copy.p2.engine", "Position")["y"] == 1, "the unchanged part is not back with the kind's value"
        assert component(world, "worlds.copy.p2", "Color")["r"] == 1, "an instance does not hold the kind's value after loading"
        assert component(world, "worlds.copy.bob", "Label") == {"text": "Bob"}, "the string value is not back"
        assert component(world, "worlds.copy.p2", "Pilot") == {"who": "worlds.copy.bob"}, "an entity reference does not point inside the loaded world"
        assert instance_is(world, "worlds.copy.p2", "worlds.copy.kinds.Plane"), "an instance follows another world's kind"
        assert world.entity("worlds.copy.bob")["pairs"].get("Likes") == "worlds.copy.p1", f"pair: {world.entity('worlds.copy.bob')['pairs']}"
        # Refused documents change nothing.
        world_run(world.ctx, a, b"p1 { Fuel: {55} }")
        cut = document[:len(document) // 2]
        refused = {
            "a cut document": cut,
            "a wrong format": document.replace('"format":2', '"format":9', 1),
            "an unknown component type": document.replace('"Fuel"', '"Gone"', 1),
            "a value that does not fit its type": document.replace('{"left":7}', '{"left":[]}', 1),
        }
        for name, text in refused.items():
            assert text != document or name == "a cut document", f"{name}: the replacement did not change the document"
            refuse(world, name, text, a, 55)
        # The intact document replaces the loaded world with new entities.
        old_p1 = world.lookup("worlds.a.p1")
        loaded = world_load(world.ctx, b"a", document.encode())
        assert loaded and loaded != a and world.lookup("worlds.a") == loaded, f"load over the existing world failed: {world.error()}"
        assert world.lookup("worlds.a.p1") != old_p1 and component(world, "worlds.a.p1", "Fuel") == {"left": 7}, "the world was not replaced by the saved one"
        assert not world.lookup("worlds.__loading"), "worlds.__loading was left behind"
        # Play and rewind.
        a = loaded
        assert world_enter(world.ctx, a) == 0
        assert world_rewind(world.ctx, a) == 0 and world.error(), "a world without a snapshot cannot be rewound"
        assert world_snapshot(world.ctx, a) == 0, world.error()
        assert world_run(world.ctx, a, b"p1 { Fuel: {1}; Position: {50,50,50} }\nextra { Position: {0,0,0} }") == 0, world.error()
        assert spawn(world.ctx, world.lookup("worlds.a.kinds.Plane"), a, b"p3"), world.error()
        assert lib.fu_delete_entity(world.ctx, b"worlds.a.p2") == 1
        restored = world_rewind(world.ctx, a)
        assert restored and world.lookup("worlds.a") == restored, f"rewind failed: {world.error()}"
        assert component(world, "worlds.a.p1", "Fuel") == {"left": 7} and component(world, "worlds.a.p1", "Position")["x"] == 1, "changed values are not back"
        assert world.lookup("worlds.a.p2") and not world.lookup("worlds.a.p3") and not world.lookup("worlds.a.extra"), \
            "a deleted entity is not back, or a new one is left"
        assert world_entered(world.ctx) == restored, "the world is not entered after the rewind"
    return ("format 2 with the kind's text and only what instances store; a copy under another id has the same values and points inside itself; "
            "a cut, wrong-format, unknown-type and bad-value document are refused and change nothing; load replaces; snapshot and rewind restore")


# --- Report --------------------------------------------------------------------------------------------------------

passed = all(row["ok"] for row in results)
REPORT.write_text(json.dumps({"passed": passed, "checks": results}, indent=2) + "\n", encoding="utf-8")
print("PASSED" if passed else "FAILED", f"({sum(r['ok'] for r in results)}/{len(results)} checks), report: {REPORT}")
sys.exit(0 if passed else 1)
