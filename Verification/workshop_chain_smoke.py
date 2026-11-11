"""Regression for the shared workshop scenario against the shipped Flecs DLL."""
import ctypes as c
import json
from pathlib import Path
import sys

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[1]
FIXTURES = Path(sys.argv[2]) if len(sys.argv) > 2 else ROOT / "Assets/_Features/Creation/Resources/Workshop/Examples/AssemblyChain"
lib = c.CDLL(str(ROOT / "Assets/Plugins/Flecs/x86_64/flecs_unity.dll"))
P, S, I, U, F = c.c_void_p, c.c_char_p, c.c_int, c.c_uint64, c.c_float


def bind(name, result, *args):
    func = getattr(lib, "fu_" + name)
    func.restype, func.argtypes = result, args
    return func


create = bind("create", P, c.c_ushort)
destroy = bind("destroy", None, P)
install = bind("set_managed_script", I, P, S, S)
set_json = bind("set_component_json", I, P, S, S, S)
entity_json = bind("entity_json", P, P, S)
free = bind("free", None, P)
tick = bind("tick", I, P, F)
pointer = bind("pointer", I, P, U, F, F, c.c_uint32, c.c_uint32)
lookup = bind("lookup", U, P, S)
delete = bind("delete_entity", I, P, S)
error = bind("last_error", S, P)


def put(name, source):
    assert install(world, name.encode(), source.encode()) == 0, error(world)


def read(path, component):
    data = entity_json(world, path.encode())
    assert data, path
    try:
        return json.loads(c.string_at(data))["components"][component]
    finally:
        free(data)


def step():
    for _ in range(4):
        assert tick(world, 0.02) == 0


def click(name):
    target = lookup(world, name.encode())
    assert target
    assert pointer(world, target, 0, 0, 1, 0) >= 0
    assert pointer(world, target, 0, 0, 0, 0) >= 0
    step()


def move_bell(x):
    point = json.dumps({"x": x, "y": 0.28, "z": 0}).encode()
    assert set_json(world, b"ChainBell", b"Position", point) == 0, error(world)
    step()


def open_gate():
    return read("ChainGateLink", "WorkshopGateState")["open"]


world = create(0)
assert world
checks = []
try:
    resources = ROOT / "Assets/_Features/World/Resources"
    put("ActorSchema", (resources / "Runtime/Schemas/ActorSchema.flecs").read_text())
    put("EngineSchema", (resources / "Runtime/Schemas/EngineSchema.flecs").read_text())
    cap_schema = ROOT / "Packages/com.runtime.capabilities/Runtime/Capabilities/Resources/CapabilitySchema.flecs"
    put("CapabilitySchema", cap_schema.read_text())
    # Configuration structs are generated from [Capability] classes and exported for tools without Unity.
    put("CapabilityConfigSchema", (ROOT / "Packages/com.runtime.capabilities/Runtime/Capabilities/CapabilityConfigSchema.flecs").read_text())
    put("ActorFixtures", """
ChainBunny { ActorMotion: {0,0,0,0,0,0,0,true} }
ChainDuck { ActorMotion: {0,0,-0.6,0,0,0,0,true} }
ChainBunnyGoal { MoveGoal: {0,0,0.24} }
ChainDuckGoal { MoveGoal: {0,-0.6,0.24} }
""")
    put("ChainObjects", (FIXTURES / "WorkshopChainObjects.flecs").read_text())
    put("ChainRules", (FIXTURES / "WorkshopChain.flecs").read_text())
    links = (FIXTURES / "WorkshopChainLinks.flecs").read_text()
    put("ChainLinks", links)
    step()
    assert not open_gate()
    click("ChainPlate")
    assert read("ChainBellLink", "WorkshopBell.mut")["rings"] == 0
    click("ChainBell")
    assert read("ChainBellLink.chime", "CapAudioNote")["sequence"] == 1
    assert not read("ChainBellLink", "WorkshopBell.mut")["following"]
    assert not open_gate()
    checks.append("Wrong target is ignored. An out-of-range bell still makes a sound command without luring the bunny.")

    move_bell(1.4)
    click("ChainBell")
    assert read("ChainBellLink.chime", "CapAudioNote")["sequence"] == 2
    assert read("ChainBellLink", "WorkshopBell.mut")["following"]
    assert abs(read("ChainBellLink.motion", "MoveGoal")["x"] - 1.4) < 0.001
    assert not open_gate(), "A movement request alone must not vacate the plate."
    move_bell(1.6)
    assert abs(read("ChainBellLink.motion", "MoveGoal")["x"] - 1.6) < 0.001
    assert set_json(world, b"ChainBunny", b"ActorMotion", b'{"x":1.6}') == 0
    step()
    assert open_gate()
    assert read("ChainGateLink.pose", "Position")["y"] > 1.4
    assert read("ChainCrossLink.motion", "MoveGoal")["z"] == 2
    checks.append("Live placement edit and tap drive a live goal. Measured movement releases the plate, raises the gate, and redirects the duck.")

    # The same native tree can inspect a cross-entity event result and command another entity.
    xml = ('<root BTCPP_format="4"><BehaviorTree ID="Main"><Sequence>'
           '<FlecsQuery expression="WorkshopGateOpen(ChainGateLink)"/>'
           '<FlecsSet entity="ChainExit" component="Position" value="{&quot;x&quot;:0,&quot;y&quot;:0,&quot;z&quot;:3}"/>'
           '</Sequence></BehaviorTree></root>')
    encoded = json.dumps(xml).replace("{", "\\{").replace("$", "\\$")
    put("Tree", "ChainTree { NativeTree: {xml:" + encoded + ",revision:1,enabled:true,loop:false} }")
    step()
    assert read("ChainTree", "NativeTreeResult")["status"] == 2
    assert read("ChainCrossLink.motion", "MoveGoal")["z"] == 3
    checks.append("Runtime BehaviorTree.CPP XML reads the gate-open tag and changes a different entity; the tree fails when the gate closes.")

    # Reset the approach state, then point the same recipe at a different activator.
    assert set_json(world, b"ChainBunny", b"ActorMotion", b'{"x":0}') == 0
    step()
    assert not open_gate()
    put("Tree", "ChainTree { NativeTree: {xml:" + encoded + ",revision:2,enabled:true,loop:false} }")
    step()
    assert read("ChainTree", "NativeTreeResult")["status"] == 3
    assert read("ChainCrossLink.motion", "MoveGoal")["z"] < 0
    assert delete(world, b"ChainLinks") == 1
    put("RemixActivator", "ChainButton { Position: {1.2,0,0} }")
    put("ChainLinks", links.replace("bell:ChainBell,", "bell:ChainButton,"))
    step()
    previous = read("ChainBellLink", "WorkshopBell.mut")["rings"]
    click("ChainBell")
    assert read("ChainBellLink", "WorkshopBell.mut")["rings"] == previous
    click("ChainButton")
    assert read("ChainBellLink", "WorkshopBell.mut")["rings"] == previous + 1
    assert abs(read("ChainBellLink.motion", "MoveGoal")["x"] - 1.2) < 0.001
    checks.append("Reinstalled runtime links accept a different source entity and no longer accept the previous one.")
finally:
    destroy(world)

print(json.dumps({"passed": True, "checks": checks,
                  "limits": ["Native ABI fixture with injected ActorMotion; Unity presentation and motor require the Editor scenario."]},
                 indent=2))

