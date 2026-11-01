"""Real native ABI + REST tests. Standard-library only. Run after Native/Build.ps1."""
import ctypes as c
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
lib = c.CDLL(str(ROOT / "Assets/Plugins/Ecaty/x86_64/flecs_unity.dll"))
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
    assert abi() == 2
    assert size() == c.sizeof(View)
    w = create(0)
    assert w, error(None)
    demo = (ROOT / "Assets/Ecaty/Resources/EcatyWorld.flecs").read_bytes()
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
    assert managed(w, b"Utf8Scope", b'LabelProbe { Position: {0,2,0} Label: {text: "Probe"} }') == 0
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
    run(w, "scene.Cube { Position: {3, 1, 0} Color: {1, 0, 0, 1} }")
    cube_after = next(v for v in views(w) if v.entity == cube.entity)
    assert cube_after.px == 3 and cube_after.py == 1 and cube_after.g == 0
    run(w, "struct Temperature {\n value = f32\n}\nscene.Cube { Temperature: {42} }\n")
    assert "Temperature" in json.dumps(value(entity(w, b"scene.Cube")))
    assert "Cube" in json.dumps(value(query(w, b"Position, Primitive")))
    run(w, "using flecs.script.math\nMathCheck { Position: {sqrt(9), 0, 0} }")
    assert any(v.px == 3 and v.entity != cube.entity for v in views(w))
    # Delete the math example via a managed lifetime, keeping baseline assertions simple.
    assert managed(w,b"MathScope",b"using flecs.script.math\nMathOwned { Position: {sqrt(16),0,0} }")==0
    assert managed(w,b"MathScope",b"")==0
    # MathCheck is intentionally a persistent extra transform for the remaining checks.
    checks.append("runtime Script edits, runtime types, query/entity JSON")
    assert managed(w,b"Transient",b"Temporary { Position: {0,0,0} Primitive: {0} }")==0
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
    request(w,port,"script/RemoteScene", "PUT", "RemoteCube { Position: {4,0,0} Primitive: {0} Color: {0,1,0,1} }")
    assert any(v.px == 4 and v.g == 1 for v in views(w))
    request(w,port,"script/RemoteScene", "PUT", "RemoteCube { Position: {-4,0,0} Primitive: {1} }")
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
    report["passed"]=True
except BaseException as exc:
    report["error"]=repr(exc)
    traceback.print_exc()
    raise
finally:
    if w: destroy(w)
    (ROOT/"Verification/native-checks.json").write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
