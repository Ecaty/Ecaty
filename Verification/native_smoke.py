"""Real native ABI + REST tests. Standard-library only. Run after Native/Build.ps1."""
import ctypes as c
import os
import json
import pathlib
import socket
import time
import traceback
import urllib.request
import urllib.parse
import http.client
from concurrent.futures import ThreadPoolExecutor

ROOT = pathlib.Path(__file__).resolve().parents[1]
lib = c.CDLL(os.environ.get("ECATY_NATIVE_LIBRARY", str(ROOT / "Assets/Plugins/Ecaty/x86_64/flecs_unity.dll")))
P = c.c_void_p

class View(c.Structure):
    _fields_ = [("entity", c.c_uint64), ("parent", c.c_uint64)] + [
        (name, c.c_float) for name in "px py pz rx ry rz sx sy sz r g b a".split()
    ] + [("primitive", c.c_int32), ("has_label", c.c_int32)]

def bind(name, ret, *args):
    fn = getattr(lib, name)
    fn.restype, fn.argtypes = ret, args
    return fn

create = bind("fu_create", P, c.c_uint16)
destroy = bind("fu_destroy", None, P)
tick = bind("fu_tick", c.c_int32, P, c.c_float)
script = bind("fu_run_script", c.c_int32, P, c.c_char_p)
managed = bind("fu_set_managed_script", c.c_int32, P, c.c_char_p, c.c_char_p)
error = bind("fu_last_error", c.c_char_p, P)
read = bind("fu_read_views", c.c_int32, P, c.POINTER(View), c.c_int32)
query = bind("fu_query_json", P, P, c.c_char_p)
entity = bind("fu_entity_json", P, P, c.c_char_p)
label = bind("fu_label", P, P, c.c_uint64)
free = bind("fu_free", None, P)
write_presented = bind("fu_write_presented", c.c_int32, P, c.POINTER(View), c.c_int32, c.c_int32)
abi = bind("fu_abi_version", c.c_int32)
size = bind("fu_view_size", c.c_int32)
version = bind("fu_version", c.c_char_p)
create_ex = bind("fu_create_ex", P, c.c_uint16, c.c_uint32)
rest_status = bind("fu_rest_status", c.c_int32, P)
query_create = bind("fu_query_create", c.c_int32, P, c.c_char_p)
query_destroy = bind("fu_query_destroy", None, P, c.c_int32)
query_changed = bind("fu_query_changed", c.c_int32, P, c.c_int32)
query_read = bind("fu_query_read_data", c.c_int32, P, c.c_int32, c.c_char_p, c.POINTER(c.c_uint64), c.POINTER(c.c_uint64), c.c_void_p, c.c_int32, c.c_int32)
json_into = bind("fu_component_json_into", c.c_int32, P, c.c_uint64, c.c_uint64, c.c_char_p, c.c_int32)
label_into = bind("fu_label_into", c.c_int32, P, c.c_uint64, c.c_char_p, c.c_int32)
lookup = bind("fu_lookup", c.c_uint64, P, c.c_char_p)
diagnostics = bind("fu_bridge_diagnostics", P, P)
checks = []

def value(ptr):
    assert ptr, "Missing JSON"
    try: return json.loads(c.string_at(ptr).decode())
    finally: free(ptr)

def run(w, source):
    assert script(w, source.encode()) == 0, error(w).decode()

def views(w):
    n = read(w, None, 0)
    a = (View * n)()
    assert read(w, a, n) == n
    return list(a)

def text_value(ptr):
    assert ptr, "Missing label"
    try: return c.string_at(ptr).decode("utf-8")
    finally: free(ptr)

def request(w, port, path, method="GET", body=None):
    def send():
        # Match Explorer's supported query-parameter form for Script updates.
        # Avoid assumptions about body-fragment handling in the upstream development server.
        request_path=path
        if body is not None:
            request_path += ("&" if "?" in request_path else "?") + "code=" + urllib.parse.quote(body,safe="")
        conn = http.client.HTTPConnection("127.0.0.1", port, timeout=8)
        try:
            conn.request(method, "/" + request_path)
            response = conn.getresponse()
            text = response.read().decode()
            assert response.status == 200, (response.status, text)
            if method == "OPTIONS":
                assert response.getheader("Access-Control-Allow-Origin") == "*"
                assert "PUT" in response.getheader("Access-Control-Allow-Methods", "")
            return text
        finally: conn.close()
    with ThreadPoolExecutor(max_workers=1) as pool:
        task = pool.submit(send)
        deadline = time.monotonic() + 9
        while not task.done() and time.monotonic() < deadline:
            assert tick(w, 0.02) == 0
            time.sleep(0.005)
        text=task.result()
        return json.loads(text) if text else None

report = {"passed": False, "flecs": version().decode(), "checks": checks}
w = None
try:
    assert abi() == 3
    assert size() == c.sizeof(View)
    w = create(0)
    assert w, error(None)
    demo = (ROOT / "Assets/_Tests/Fixtures/Resources/Tests/Fixtures/VerificationWorld.flecs").read_bytes()
    assert managed(w, b"HelloWorld", demo) == 0, error(w)
    a = views(w)
    assert len(a) == 3, len(a)
    assert not any(v.has_label for v in a), "Initial world must not contain a greeting"
    assert len([v for v in a if v.primitive >= 0]) == 2
    checks.append("ABI layout, reflected components, managed scene, prefab instantiation")
    cube = next(v for v in a if v.primitive == 0)
    assert cube.parent != 0
    ack = (View * 1)(cube)
    ack[0].b = 0.314
    assert write_presented(w, ack, 1, 7) == 0
    data = value(entity(w, b"scene.Cube"))
    assert 'Presented' in json.dumps(data) and '0.314' in json.dumps(data)
    assert abs(next(v for v in views(w) if v.entity == cube.entity).b - cube.b) < .0001
    checks.append("presentation acknowledgement is reflected without changing authoritative color")
    assert tick(w, 0.25) == 0
    cube_after = next(v for v in views(w) if v.entity == cube.entity)
    assert cube_after.ry > cube.ry + 1
    checks.append("native systems progress and ChildOf hierarchy")
    assert managed(w, b"Utf8Scope", b'LabelProbe { Position: {0,2,0}; Label: {text: "Probe"} }') == 0
    greeting = next(v for v in views(w) if v.has_label)
    assert text_value(label(w, greeting.entity)) == "Probe"
    run(w, 'LabelProbe { Label: {text: "CrÃ¨me ðŸ¦€"} }')
    assert text_value(label(w, greeting.entity)) == "CrÃ¨me ðŸ¦€"
    assert managed(w, b"Utf8Scope", b"") == 0
    assert managed(w, b"LabelScope", '''
prefab TextStyle { Label: {text: "Shared label"} }
FirstText : TextStyle { Position: {7,0,0} }
SecondText : TextStyle { Position: {8,0,0} }
'''.encode()) == 0
    first = next(v for v in views(w) if v.px == 7)
    second = next(v for v in views(w) if v.px == 8)
    run(w, 'FirstText { Label: {text: "Changed label"} }')
    assert text_value(label(w, first.entity)) == "Changed label"
    assert text_value(label(w, second.entity)) == "Shared label"
    assert managed(w, b"LabelScope", b"") == 0
    assert len(views(w)) == 3
    checks.append("C++ reflected UTF-8 labels, prefab copies and managed cleanup")
    other = create(0)
    assert other, error(None)
    try:
        assert managed(other, b"HelloWorld", demo) == 0
        other_cube = next(v for v in views(other) if v.primitive == 0)
        assert tick(other, 0.5) == 0
        assert next(v for v in views(other) if v.entity == other_cube.entity).ry > other_cube.ry + 1
        assert tick(w, 0.25) == 0
        assert next(v for v in views(w) if v.entity == cube.entity).ry > cube_after.ry + 1
    finally:
        destroy(other)
    assert tick(w, 0.25) == 0
    checks.append("simultaneous C++ worlds progress independently and survive peer teardown")
    run(w, "scene { Cube { Position: {3, 1, 0}; Color: {1, 0, 0, 1} } }")
    cube_after = next(v for v in views(w) if v.entity == cube.entity)
    assert cube_after.px == 3 and cube_after.py == 1 and cube_after.g == 0
    run(w, "struct Temperature(value: f32)\nscene { Cube { Temperature: {42} } }\n")
    assert "Temperature" in json.dumps(value(entity(w, b"scene.Cube")))
    assert "Cube" in json.dumps(value(query(w, b"Position, Primitive")))
    run(w, "using flecs.script.math\nMathCheck { Position: {sqrt(9), 0, 0} }")
    assert any(v.px == 3 and v.entity != cube.entity for v in views(w))
    # Delete the math example via a managed lifetime, keeping baseline assertions simple.
    assert managed(w,b"MathScope",b"using flecs.script.math\nMathOwned { Position: {sqrt(16),0,0} }")==0
    assert managed(w,b"MathScope",b"")==0
    # MathCheck is intentionally a persistent extra transform for the remaining checks.
    checks.append("runtime Script edits, runtime types, query/entity JSON")
    assert managed(w,b"Transient",b"Temporary { Position: {0,0,0}; Primitive: {0} }")==0
    assert len(views(w)) == 5
    assert managed(w,b"Transient",b"")==0
    assert len(views(w)) == 4
    assert script(w,b"invalid {") != 0
    assert error(w)
    checks.append("managed deletion and invalid Script error handling")
    destroy(w); w = None
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0)); port = sock.getsockname()[1]
    w = create(port)
    assert w, error(None)
    assert managed(w,b"HelloWorld",demo)==0
    request(w,port,"entity/scene/Cube?values=true&type_info=true")
    request(w,port,"script/HelloWorld", "OPTIONS")
    request(w,port,"query?expr="+urllib.parse.quote("Position, Primitive"))
    request(w,port,"entity/RemoteScene", "PUT")
    request(w,port,"script/RemoteScene", "PUT", "RemoteCube { Position: {4,0,0}; Primitive: {0}; Color: {0,1,0,1} }")
    assert any(v.px == 4 and v.g == 1 for v in views(w))
    request(w,port,"script/RemoteScene", "PUT", "RemoteCube { Position: {-4,0,0}; Primitive: {1} }")
    assert any(v.px == -4 and v.primitive == 1 for v in views(w))
    request(w,port,"script/RemoteScene", "PUT", "")
    assert len(views(w)) == 3
    request(w,port,"stats/world")
    checks.append("live REST entity inspection, queries, Script create/edit/delete, statistics")
    destroy(w); w=None
    for _ in range(3):
        w=create(port); assert w, error(None)
        tick(w,0.02); destroy(w); w=None
    checks.append("repeated world lifetime and REST port reuse")

    # ABI 3: busy REST port is non-fatal; read-only REST refuses writes.
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0)); port = sock.getsockname()[1]
    w = create_ex(port, 1)
    assert w and rest_status(w) == 1, error(None)
    busy = create(port)
    assert busy, "A busy REST port must not fail world creation"
    try:
        assert rest_status(busy) == -1 and b"port" in error(busy)
        assert tick(busy, 0.02) == 0
    finally:
        destroy(busy)
    request(w, port, "entity/flecs/core/World")
    try:
        request(w, port, "entity/ReadOnlyProbe", "PUT")
        raise AssertionError("read-only REST accepted a write")
    except AssertionError as exc:
        assert "403" in repr(exc), exc
    destroy(w); w = None
    checks.append("REST port conflict is non-fatal; read-only REST refuses writes")

    # Stable query handles survive unrelated managed-script reloads and rebuild when a referenced type is redefined.
    w = create(0)
    assert managed(w, b"Types", b"struct Heat(value: f32)\nHot { Heat: {3} }\nWarm { Heat: {1} }") == 0
    handle = query_create(w, b"Heat")
    assert handle > 0, error(w)
    assert query_changed(w, handle) == 1
    ents = (c.c_uint64 * 8)(); vals = (c.c_float * 8)()
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == 2 and sorted(vals[:2]) == [1, 3]
    assert query_changed(w, handle) == 0, "reading the query consumes its change state"
    builds = json.loads(c.string_at(diagnostics(w)).decode())["queryBuilds"]
    for i in range(5):
        assert managed(w, b"Unrelated", f"Other{i} {{ Position: {{{i},0,0}} }}".encode()) == 0
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == 2
    diag = json.loads(c.string_at(diagnostics(w)).decode())
    assert diag["queryBuilds"] == builds, diag
    assert query_changed(w, handle) == 0
    assert managed(w, b"Unrelated", b"Other { Position: {9,0,0} }") == 0
    assert query_changed(w, handle) == 1, "a source change must report each query as changed once (full refresh)"
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == 2 and query_changed(w, handle) == 0
    run(w, "Hot { Heat: {5} }")
    assert query_changed(w, handle) == 1
    assert managed(w, b"Types", b"struct Heat(value: f32, extra: f32)\nHot { Heat: {7, 1} }") == 0
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == -1, "redefined type must not match the old layout"
    pair = (c.c_float * 16)()
    assert query_read(w, handle, None, ents, None, pair, 8, 8) == 1 and pair[0] == 7
    assert json.loads(c.string_at(diagnostics(w)).decode())["queryRebuilds"] >= 1
    query_destroy(w, handle)
    assert query_read(w, handle, None, ents, None, pair, 8, 8) == -1
    # A fixed source entity that is deleted and recreated (new id) must not leave the query matching nothing.
    assert managed(w, b"Sources", b"Model { Heat: {4, 0}; Position: {0,0,0} }") == 0
    source = query_create(w, b"Heat, Position(Model)")
    assert source > 0 and query_read(w, source, None, ents, None, pair, 8, 8) == 2
    assert managed(w, b"Sources", b"") == 0
    assert managed(w, b"Sources", b"Model { Heat: {6, 0}; Position: {0,0,0} }") == 0
    assert query_read(w, source, None, ents, None, pair, 8, 8) == 2, "fixed source was not re-resolved"
    checks.append("stable query handles: no rebuild on unrelated reloads, rebuild on type redefinition, change detection")

    # Parse-once: repeated commands reuse the parse; a source change invalidates it.
    for i in range(3):
        run(w, "Hot { Heat: {9, 2} }")
    diag = json.loads(c.string_at(diagnostics(w)).decode())
    assert diag["scriptReuses"] >= 2, diag
    assert script(w, b"Hot { Missing: {1} }") != 0 and script(w, b"Hot { Missing: {1} }") != 0
    checks.append("repeated fu_run_script reuses its parsed script")

    # Buffer copies: no owned string crosses the ABI.
    hot = lookup(w, b"Hot"); heat = lookup(w, b"Heat")
    buf = c.create_string_buffer(4)
    n = json_into(w, hot, heat, buf, 4)
    assert n > 4
    buf = c.create_string_buffer(n)
    assert json_into(w, hot, heat, buf, n) == n and json.loads(buf.raw[:n].decode())["value"] == 9
    assert json_into(w, hot, lookup(w, b"Position"), buf, n) == -1
    assert managed(w, b"Labels", 'Tag { Position: {0,0,0}; Label: {text: "Crème"} }'.encode()) == 0
    tag = lookup(w, b"Tag"); lb = c.create_string_buffer(32)
    n = label_into(w, tag, lb, 32)
    assert lb.raw[:n].decode() == "Crème"
    assert label_into(w, hot, lb, 32) == -1
    checks.append("component JSON and labels copy into caller buffers")

    # Cached-query view snapshot: optional fields, defaults and prefab-inherited values.
    assert managed(w, b"ViewProbe", b"""
prefab Base { Scale: {2,2,2} }
Plain { Position: {1,0,0} }
Full : Base { Position: {2,0,0}; Rotation: {0,45,0}; Color: {0.5,0.25,0,1}; Primitive: {1} }
""") == 0
    vs = {v.px: v for v in views(w)}
    plain, full = vs[1], vs[2]
    assert (plain.sx, plain.r, plain.primitive, plain.has_label) == (1, 1, -1, 0)
    assert (full.ry, full.sx, full.g, full.primitive) == (45, 2, 0.25, 1)
    assert any(v.has_label for v in views(w))
    destroy(w); w = None
    checks.append("UnityViews cached query: optional fields, defaults and inherited values")

    # Bisect switches restore the ABI 2 behaviour per feature and give the same results.
    w = create_ex(0, 0x700)
    assert json.loads(c.string_at(diagnostics(w)).decode())["compat"] == 0x700
    assert managed(w, b"Types", b"struct Heat(value: f32)\nHot { Heat: {3} }") == 0
    for i in range(3):
        run(w, "Hot { Heat: {4} }")
    diag = json.loads(c.string_at(diagnostics(w)).decode())
    assert diag["scriptReuses"] == 0, diag
    handle = query_create(w, b"Heat")
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == 1 and vals[0] == 4
    builds = json.loads(c.string_at(diagnostics(w)).decode())["queryBuilds"]
    assert managed(w, b"Unrelated", b"Other { Position: {9,0,0} }") == 0
    assert query_read(w, handle, None, ents, None, vals, 4, 8) == 1
    assert json.loads(c.string_at(diagnostics(w)).decode())["queryBuilds"] == builds + 1, "flush switch must recompile"
    assert managed(w, b"ViewProbe", b"""
prefab Base { Scale: {2,2,2} }
Plain { Position: {1,0,0} }
Full : Base { Position: {2,0,0}; Rotation: {0,45,0}; Color: {0.5,0.25,0,1}; Primitive: {1} }
""") == 0
    vs = {v.px: v for v in views(w)}
    assert (vs[1].sx, vs[1].primitive) == (1, -1) and (vs[2].ry, vs[2].sx, vs[2].g, vs[2].primitive) == (45, 2, 0.25, 1)
    destroy(w); w = None
    checks.append("bisect switches: per-call parse, query flush on source change, per-entity view read")
    report["passed"]=True
except BaseException as exc:
    report["error"]=repr(exc)
    traceback.print_exc()
    raise
finally:
    if w: destroy(w)
    (ROOT/"Verification/native-checks.json").write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
