"""Engine performance benchmark and regression guard. Standard-library only.

Usage: python Verification/bench_engine.py [--report PATH] [--update-guards]

Every metric is the minimum over several batches of averaged calls, reported in
microseconds and as a ratio to the empty-world fu_tick (the reference), so the
numbers transfer between machines. Budgets live in Verification/budgets/engine-budgets.json
(a subfolder: /Verification/*.json is git-ignored). A `guard` is enforced; a `target`
(goal of docs/ENGINE-PLAN.md 3.9) is only reported.
"""
import argparse
import contextlib
import ctypes as c
import itertools
import json
import os
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[1]
DLL = os.environ.get("FLECS_UNITY_LIBRARY", str(ROOT / "Assets/Plugins/Flecs/x86_64/flecs_unity.dll"))
BUDGETS = ROOT / "Verification/budgets/engine-budgets.json"
DEFAULT_REPORT = ROOT / "Verification/engine-bench.json"
CAPABILITY_DIR = ROOT / "Packages/com.runtime.capabilities/Runtime/Capabilities"
BATCHES = 7
PROCESSES = 3
GUARD_MARGIN = 1.5
REFERENCE = "tick_empty"

lib = c.CDLL(DLL)
P, U64 = c.c_void_p, c.c_uint64


def bind(name, ret, *args):
    fn = getattr(lib, name)
    fn.restype, fn.argtypes = ret, args
    return fn


create = bind("fu_create", P, c.c_uint16)
destroy = bind("fu_destroy", None, P)
tick = bind("fu_tick", c.c_int32, P, c.c_float)
managed = bind("fu_set_managed_script", c.c_int32, P, c.c_char_p, c.c_char_p)
error = bind("fu_last_error", c.c_char_p, P)
version = bind("fu_version", c.c_char_p)
free = bind("fu_free", None, P)
query_json = bind("fu_query_json", P, P, c.c_char_p)
query_once = bind("fu_query_once_json", P, P, c.c_char_p, c.c_int32)
set_json = bind("fu_set_component_json", c.c_int32, P, c.c_char_p, c.c_char_p, c.c_char_p)
read_views = bind("fu_read_views", c.c_int32, P, P, c.c_int32)
view_changes = bind("fu_view_changes", P, P, c.POINTER(c.c_int32), P, c.POINTER(c.c_int32))
write_presented = bind("fu_write_presented", c.c_int32, P, P, c.c_int32, c.c_int32)
diagnostics = bind("fu_bridge_diagnostics", P, P)
query_create = bind("fu_query_create", c.c_int32, P, c.c_char_p)
query_changed = bind("fu_query_changed", c.c_int32, P, c.c_int32)
query_read = bind("fu_query_read_data", c.c_int32, P, c.c_int32, c.c_char_p, P, P, P, c.c_int32, c.c_int32)
script_edit = bind("fu_script_edit", c.c_int32, P, c.c_char_p, c.c_char_p, c.c_char_p, c.c_char_p)


class View(c.Structure):
    _fields_ = [("entity", U64), ("parent", U64)] + [
        (name, c.c_float) for name in "px py pz rx ry rz sx sy sz r g b a".split()
    ] + [("primitive", c.c_int32), ("has_label", c.c_int32)] + [
        (name, c.c_float) for name in "qx qy qz qw".split()] + [("flags", c.c_uint32)]


# ---- helpers -------------------------------------------------------------

@contextlib.contextmanager
def world():
    handle = create(0)
    if not handle:
        raise RuntimeError("fu_create failed")
    try:
        yield handle
    finally:
        destroy(handle)


def set_script(w, name, source):
    if isinstance(source, str):
        source = source.encode()
    if managed(w, name.encode(), source) != 0:
        raise RuntimeError(f"script {name}: {(error(w) or b'').decode()}")


def read_json(ptr, w):
    """Parse and free a JSON string returned by the library."""
    if not ptr:
        raise RuntimeError((error(w) or b"call failed").decode())
    text = c.string_at(ptr).decode()
    free(ptr)
    return json.loads(text)


def timed(fn, calls):
    """Average microseconds per call over one batch (after one warm-up call)."""
    fn()
    start = time.perf_counter()
    for _ in range(calls):
        fn()
    return (time.perf_counter() - start) / calls * 1e6


def best(batch):
    """Minimum over BATCHES runs of a function returning one batch's microseconds."""
    return min(batch() for _ in range(BATCHES))


def best_calls(fn, calls):
    return best(lambda: timed(fn, calls))


def capability_world(w):
    set_script(w, "CapabilitySchema", (CAPABILITY_DIR / "Resources/CapabilitySchema.flecs").read_bytes())
    set_script(w, "CapabilityConfigSchema", (CAPABILITY_DIR / "CapabilityConfigSchema.flecs").read_bytes())


def bulk_views_script(n):
    return "\n".join(
        f"E{i} {{ Position: {{{i},0,0}}\n Rotation: {{0,0,0}}\n Scale: {{1,1,1}}\n Color: {{1,1,1,1}}\n Primitive: {{0}} }}"
        for i in range(n))


# ---- tick metrics --------------------------------------------------------

def tick_empty():
    with world() as w:
        return best_calls(lambda: tick(w, 0.016), 3000)


def tick_bindings(n):
    with world() as w:
        capability_world(w)
        lines = ["T { Position: {0,0,0} }"]
        for i in range(n):
            dependency = f"\n (CapDependsOn, B{i - 1})" if i % 4 else ""
            lines.append(f"B{i} {{ CapBinding: {{target: T, enabled: true, revision: 1}}{dependency} }}")
        set_script(w, "Bindings", "\n".join(lines))
        return best_calls(lambda: tick(w, 0.016), 600)


def tick_splines_256():
    points = json.dumps([[i * 1.0, 0, (i % 3) * 0.5] for i in range(24)])
    with world() as w:
        capability_world(w)
        lines = ["T { Position: {0,0,0} }"]
        lines += [
            f'S{i} {{ CapBinding: {{target: T, enabled: true, revision: 1}}\n'
            f' CapSpline: {{points: "{points}", width: 1, profile: "", closed: false, fill: false}} }}'
            for i in range(256)]
        set_script(w, "Splines", "\n".join(lines))
        tick(w, 0.016)
        return best_calls(lambda: tick(w, 0.016), 600)


def tick_trees_256():
    waits = "".join(f'<SimWait seconds="{i + 1}"/>' for i in range(40))
    xml = f'<root BTCPP_format="4"><BehaviorTree ID="Main"><Sequence>{waits}</Sequence></BehaviorTree></root>'
    escaped = xml.replace('"', '\\"')
    with world() as w:
        set_script(w, "Trees", "\n".join(
            f'N{i} {{ NativeTree: {{xml: "{escaped}", revision: 1, enabled: true, loop: true}} }}'
            for i in range(256)))
        tick(w, 0.016)
        return best_calls(lambda: tick(w, 0.016), 400)


# ---- view snapshot and presentation -------------------------------------

def read_views_metric(n):
    with world() as w:
        set_script(w, "Bulk", bulk_views_script(n))
        buffer = (View * n)()
        tick(w, 0.016)
        return best_calls(lambda: read_views(w, buffer, n), 100)


def view_changes_10000_static():
    """10,000 static views and one spinning entity: a frame is one tick plus one change report."""
    n = 10000
    with world() as w:
        set_script(w, "Bulk", bulk_views_script(n))
        set_script(w, "Spinner", "spinner { Position: {0,0,0}; Rotation: {0,0,0}; Spin: {0,90,0} }")
        count, removed_count = c.c_int32(0), c.c_int32(0)
        removed = c.c_void_p()

        def frame():
            tick(w, 0.016)
            if not view_changes(w, c.byref(count), c.byref(removed), c.byref(removed_count)):
                raise RuntimeError((error(w) or b"").decode())
        frame()
        if count.value != n + 1:
            raise RuntimeError(f"the first change report returned {count.value} views")
        frame()
        if count.value != 1:
            raise RuntimeError(f"a static frame reported {count.value} changed views, expected the spinner only")
        return best_calls(frame, 300)


def write_presented_10000():
    n = 10000
    with world() as w:
        set_script(w, "Bulk", bulk_views_script(n))
        buffer = (View * n)()
        tick(w, 0.016)
        read_views(w, buffer, n)
        return best_calls(lambda: write_presented(w, buffer, n, 1), 40)


# ---- live edits and script reloads ---------------------------------------

CART = (b"cart { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {0}\n"
        b" wheel1 { Position: {1,0,0}; Primitive: {3} }\n wheel2 { Position: {-1,0,0}; Primitive: {3} }\n"
        b" wheel3 { Position: {1,0,1}; Primitive: {3} }\n wheel4 { Position: {-1,0,1}; Primitive: {3} }\n"
        b" lamp { Position: {0,1,0}; Color: {1,1,0,1}; Primitive: {1} } }")


def live_write_component():
    with world() as w:
        set_script(w, "el.cart", CART)
        write = lambda: set_json(w, b"cart.lamp", b"Position", b'{"x":0,"y":2,"z":0}')
        return best_calls(write, 5000)


def reload_script_6_entities():
    counter = itertools.count()
    with world() as w:
        set_script(w, "el.cart", CART)
        reload = lambda: set_script(w, "el.cart", CART.replace(b"{0,1,0}", b"{0,%d,0}" % (next(counter) % 5)))
        return best_calls(reload, 300)


def script_edit_900_entities():
    counter = itertools.count()
    world_source = "\n".join(
        f"c{i} {{ Position: {{{i},0,0}}; Scale: {{1,1,1}}; Primitive: {{0}}\n"
        f" w1 {{ Position: {{1,0,0}}; Primitive: {{3}} }}\n w2 {{ Position: {{-1,0,0}}; Primitive: {{3}} }} }}"
        for i in range(300))
    with world() as w:
        set_script(w, "world", world_source)

        def edit():
            if script_edit(w, b"world", b"c7.w1", b"Position", b"{0,%d,0}" % (next(counter) % 7)) != 0:
                raise RuntimeError((error(w) or b"").decode())
        return best_calls(edit, 40)


# ---- agent/tool query path ------------------------------------------------

def tool_world(w):
    entities = "\n".join(f"t{i} {{ Position: {{0,0,0}} }}" for i in range(3000))
    set_script(w, "schema", f"struct A(v: f32)\nstruct B(v: f32)\ne1 {{ A: {{1}}; B: {{2}} }}\n{entities}")


def tool_query_same():
    with world() as w:
        tool_world(w)
        return best_calls(lambda: read_json(query_json(w, b"A"), w), 5000)


def tool_query_new():
    counter = itertools.count()
    with world() as w:
        tool_world(w)
        query = lambda: read_json(query_once(w, b"A, ?B, !(ChildOf, t%d)" % next(counter), 0), w)
        return best_calls(query, 150)


def spawn_prefab_instance():
    instances = 1000
    source = "\n".join(f"p{i} : Plane {{ Speed: {{{i}}} }}" for i in range(instances))

    def batch():
        with world() as w:
            set_script(w, "kinds", PLANE_KINDS)
            start = time.perf_counter()
            set_script(w, "many", source)
            return (time.perf_counter() - start) / instances * 1e6
    return best(batch)


PLANE_KINDS = """struct Speed(v: f32)
struct Wings(span: f32)
struct Pose(t: f32)
Speed { (OnInstantiate, Inherit) }
Wings { (OnInstantiate, Inherit) }
@tree ChildOf
prefab Vehicle { Speed: {10}; auto_override | Pose: {0} }
@tree ChildOf
prefab Plane : Vehicle { Speed: {200}; Wings: {30}
  engine { Speed: {1} } }
"""


def typed_read_bodies_up():
    lines = ["struct Gravity(g: f32)\nstruct Buoyancy(d: f32)\nstruct Drag(k: f32)\nstruct Body(density: f32)",
             "land { Gravity: {9.8}", " water { Buoyancy: {1}; Drag: {3}"]
    lines += [f"  fish{i} {{ Body: {{1}} }}" for i in range(500)]
    lines += [" }"] + [f" rock{i} {{ Body: {{3}} }}" for i in range(500)] + ["}"]
    lines += ["space { Gravity: {0}", " ship { Body: {2} } }", "drift { Body: {1} }"]
    with world() as w:
        set_script(w, "places", "\n".join(lines))
        handle = query_create(w, b"Body, ?Gravity(up), ?Buoyancy(up), ?Drag(up)")
        entities = (U64 * 2048)()
        values = (c.c_float * 2048)()
        read = lambda: query_read(w, handle, None, entities, None, values, 4, 2048)
        if read() < 1000:
            raise RuntimeError("typed read returned too few rows")
        return best_calls(read, 1000)


# ---- behaviour checks (1 = as intended) ----------------------------------

def handles_survive_unrelated_script_set():
    with world() as w:
        set_script(w, "schema", "struct A(v: f32)")
        set_script(w, "data", "\n".join(f"e{i} {{ A: {{{i}}} }}" for i in range(1000)))
        handle = query_create(w, b"[in] A")
        entities = (c.c_uint64 * 1024)()
        values = (c.c_float * 1024)()
        query_read(w, handle, None, entities, None, values, 4, 1024)  # a read consumes the pending change report
        set_script(w, "other", "x { Position: {0,0,0} }")
        return int(query_changed(w, handle) == 0)


def tool_queries_keep_feature_slots():
    def builds(w):
        return read_json(diagnostics(w), w)["queryBuilds"]
    with world() as w:
        tool_world(w)
        read_json(query_json(w, b"A"), w)
        for i in range(40):
            read_json(query_once(w, b"A, ?B, !(ChildOf, t%d)" % i, 0), w)
        before = builds(w)
        read_json(query_json(w, b"A"), w)
        return int(builds(w) == before)


METRICS = [
    ("tick_empty", tick_empty),
    ("tick_bindings_64", lambda: tick_bindings(64)),
    ("tick_bindings_256", lambda: tick_bindings(256)),
    ("tick_bindings_1000", lambda: tick_bindings(1000)),
    ("tick_splines_256", tick_splines_256),
    ("tick_trees_256", tick_trees_256),
    ("read_views_1000", lambda: read_views_metric(1000)),
    ("view_changes_10000_static", view_changes_10000_static),
    ("read_views_10000", lambda: read_views_metric(10000)),
    ("write_presented_10000", write_presented_10000),
    ("live_write_component", live_write_component),
    ("reload_script_6_entities", reload_script_6_entities),
    ("script_edit_900_entities", script_edit_900_entities),
    ("tool_query_same", tool_query_same),
    ("tool_query_new", tool_query_new),
    ("spawn_prefab_instance", spawn_prefab_instance),
    ("typed_read_bodies_up", typed_read_bodies_up),
]
FLAGS = [
    ("handles_survive_unrelated_script_set", handles_survive_unrelated_script_set),
    ("tool_queries_keep_feature_slots", tool_queries_keep_feature_slots),
]


# ---- running -------------------------------------------------------------

def measure_isolated(metric_id):
    """Minimum of PROCESSES fresh-process measurements of one metric.

    Some metrics (tick_splines_256, tick_bindings_1000) come out about 4x slower in roughly one
    process in four, whatever ran before; the best of a few fresh processes skips those outliers.
    """
    def run_once():
        result = subprocess.run([sys.executable, str(pathlib.Path(__file__).resolve()), "--measure", metric_id],
                                capture_output=True, text=True, check=True)
        return float(result.stdout.strip().splitlines()[-1])
    return min(run_once() for _ in range(PROCESSES))


# ---- evaluation ------------------------------------------------------------

def target_value(budget, ratios):
    """Goal in ratio units, or None. `target_of` scales another metric's measured ratio."""
    if "target" not in budget:
        return None
    other = budget.get("target_of")
    return budget["target"] * ratios[other] if other else budget["target"]


def evaluate(metric_id, value, budget, ratios):
    """Build a report entry whose status is ok, 'target not met ...' or FAIL."""
    target = target_value(budget, ratios)
    met = target is None or (value == target if budget["kind"] == "flag" else value <= target)
    if budget["kind"] == "flag":
        failed = budget.get("enforce", False) and not met
    else:
        failed = value > budget["guard"]
    if failed:
        status = "FAIL"
    elif met:
        status = "ok"
    else:
        status = f"target not met (expected until step {budget.get('target_step', '?')})"
    return {"id": metric_id, "kind": budget["kind"], "guard": budget.get("guard"),
            "target": target, "target_met": met, "status": status}


def format_number(value, digits=2):
    return "-" if value is None else f"{value:.{digits}f}"


def print_row(entry):
    if entry["kind"] == "flag":
        print(f"{entry['id']:38s} {'':>10s} {entry['value']:>8d} {'':>8s} "
              f"{format_number(entry['target'], 0):>9s}  {entry['status']}")
    else:
        print(f"{entry['id']:38s} {entry['us']:>10.2f} {entry['ratio']:>8.2f} "
              f"{format_number(entry['guard']):>8s} {format_number(entry['target']):>9s}  {entry['status']}")


def update_guards(budgets, ratios):
    for metric_id, ratio in ratios.items():
        budgets[metric_id]["guard"] = float(f"{ratio * GUARD_MARGIN:.2g}")
    BUDGETS.write_text(json.dumps(budgets, indent=2) + "\n", encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--report", default=str(DEFAULT_REPORT))
    parser.add_argument("--update-guards", action="store_true",
                        help="rewrite guards to 1.5x the ratios measured in this run")
    parser.add_argument("--measure", metavar="ID", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.measure:
        print(dict(METRICS)[args.measure]())
        return 0

    budgets = json.loads(BUDGETS.read_text(encoding="utf-8"))
    microseconds = {metric_id: measure_isolated(metric_id) for metric_id, _ in METRICS}
    flags = {flag_id: run() for flag_id, run in FLAGS}
    reference_us = microseconds[REFERENCE]
    ratios = {metric_id: us / reference_us for metric_id, us in microseconds.items()}
    if args.update_guards:
        update_guards(budgets, ratios)

    entries = []
    for metric_id, us in microseconds.items():
        entry = evaluate(metric_id, ratios[metric_id], budgets[metric_id], ratios)
        entries.append({**entry, "us": us, "ratio": ratios[metric_id]})
    for flag_id, value in flags.items():
        entries.append({**evaluate(flag_id, value, budgets[flag_id], ratios), "value": value})

    print(f"{'metric':38s} {'us':>10s} {'ratio':>8s} {'guard':>8s} {'target':>9s}  status")
    for entry in entries:
        print_row(entry)
    passed = all(entry["status"] != "FAIL" for entry in entries)
    print(f"reference {REFERENCE} = {reference_us:.2f} us; guards {'updated' if args.update_guards else 'enforced'}")
    print("PASS" if passed else "FAIL")

    report = {"passed": passed, "reference_us": reference_us, "dll": DLL,
              "version": (version() or b"").decode(), "metrics": entries}
    pathlib.Path(args.report).write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())
