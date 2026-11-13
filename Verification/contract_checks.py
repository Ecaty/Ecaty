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

FIELD_SIZE = {"ulong": 8, "float": 4, "int": 4}


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
    assert native_size == 80, f"fu_view_size() is {native_size}, expected 80"
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


# --- Report --------------------------------------------------------------------------------------------------------

passed = all(row["ok"] for row in results)
REPORT.write_text(json.dumps({"passed": passed, "checks": results}, indent=2) + "\n", encoding="utf-8")
print("PASSED" if passed else "FAILED", f"({sum(r['ok'] for r in results)}/{len(results)} checks), report: {REPORT}")
sys.exit(0 if passed else 1)
