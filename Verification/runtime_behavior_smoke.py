"""Native reactive Script integration: no AI service, Unity Editor or new behavior code."""
import ctypes as c
import json
import math
import os
import subprocess
from pathlib import Path
import tempfile
import time
import traceback

ROOT = Path(__file__).resolve().parents[1]
DLL = Path(os.environ.get('FLECS_UNITY_LIBRARY', ROOT/'Assets/Plugins/Flecs/x86_64/flecs_unity.dll'))
lib = c.CDLL(str(DLL))
P = c.c_void_p
def bind(name, result, *args):
    fn = getattr(lib, name); fn.restype = result; fn.argtypes = args; return fn
create = bind('fu_create', P, c.c_ushort)
destroy = bind('fu_destroy', None, P)
managed = bind('fu_set_managed_script', c.c_int, P, c.c_char_p, c.c_char_p)
script = bind('fu_run_script', c.c_int, P, c.c_char_p)
tick = bind('fu_tick', c.c_int, P, c.c_float)
error = bind('fu_last_error', c.c_char_p, P)
entity = bind('fu_entity_json', P, P, c.c_char_p)
source = bind('fu_managed_script_source', P, P, c.c_char_p)
query = bind('fu_query_json', P, P, c.c_char_p)
free = bind('fu_free', None, P)
version = bind('fu_version', c.c_char_p)

def take(ptr):
    assert ptr, 'Missing native result'
    try: return c.string_at(ptr).decode('utf-8')
    finally: free(ptr)
def inspect(w, path): return json.loads(take(entity(w, path.encode())))
def components(w, path): return inspect(w,path)['components']
def run(w, text): assert script(w,text.encode()) == 0, error(w).decode()
def load(w, text): assert managed(w,b'AgentBehavior',text.encode()) == 0, error(w).decode()
def step(w, dt): assert tick(w,dt) == 0, error(w).decode()
def near(a,b): assert math.isclose(a,b,rel_tol=2e-5,abs_tol=2e-5), (a,b)
def world():
    w=create(0); assert w,error(None)
    assert managed(w,b'World',(ROOT/'Assets/_Tests/Fixtures/Resources/Tests/Fixtures/VerificationWorld.flecs').read_bytes()) == 0,error(w)
    run(w, "struct MotionSettings(amplitude: f32, frequency: f32)\nMotionControls { MotionSettings: {0.2, 4} }")
    return w

BREATHING = '''using flecs.script.math
const t = Clock[Clock].time
const settings = MotionControls[MotionSettings]
const size = 1 + settings.amplitude * sin(t * settings.frequency)
scene { Sphere { Scale: {size, size, size} } }
'''
FOLLOWING = '''struct FollowOffset(x: f32, y: f32, z: f32)
const target = scene.Cube[Position]
scene {
    Sphere {
        FollowOffset: {3, 0, 0}
        Position: {target.x + 3, target.y, target.z}
    }
}
'''
REACTION = '''const positive = scene.Cube[Position].x > 0
scene {
    Sphere {
        if positive {
            Color: {0, 1, 0, 1}
        } else {
            Color: {1, 0, 0, 1}
        }
    }
}
'''

checks=[]
report=dict(passed=False, flecs=version().decode(), checks=checks,
    limits=['Windows x64 native verification; no phone/IL2CPP test',
            'Generated source persistence is tested; full session-state persistence is not implemented',
            'No live inference service request; generated Script supplied by test fixture'])
w=None
try:
    core=ROOT/'Native/Core'
    pinned=subprocess.run(['git','-C',str(core),'rev-parse','HEAD:vendor/flecs'],capture_output=True,text=True,check=True).stdout.strip()
    checked_out=subprocess.run(['git','-C',str(core/'vendor/flecs'),'rev-parse','HEAD'],capture_output=True,text=True,check=True).stdout.strip()
    assert checked_out==pinned, f'vendor/flecs checkout {checked_out} differs from pin {pinned}'
    assert pinned.startswith(report['flecs'].split('+')[1]), f'binary {report["flecs"]} was not built from pin {pinned}'
    dirty=subprocess.run(['git','-C',str(core/'vendor/flecs'),'status','--porcelain','--','distr'],capture_output=True,text=True,check=True).stdout.strip()
    assert not dirty, 'vendor/flecs/distr has local edits'
    report['upstream_commit']=pinned
    checks.append('Flecs submodule pin, its unmodified checkout and the binary revision match')

    w=world()
    sphere_id=inspect(w,'scene.Sphere')['id']
    cube_color=components(w,'scene.Cube')['Color']
    load(w,BREATHING)
    assert take(source(w,b'AgentBehavior'))==BREATHING
    step(w,.25)
    clock=components(w,'Clock')['Clock']
    near(components(w,'scene.Sphere')['Scale']['x'],1+.2*math.sin(clock['time']*4))
    first_size=components(w,'scene.Sphere')['Scale']['x']
    step(w,.25)
    assert components(w,'scene.Sphere')['Scale']['x']!=first_size
    assert components(w,'scene.Cube')['Color']==cube_color
    checks.append('Script-defined type and native Clock drive continuous scale through Flecs reactivity')

    run(w,'MotionControls { MotionSettings: {0.5, 2} }')
    clock=components(w,'Clock')['Clock']
    near(components(w,'scene.Sphere')['Scale']['x'],1+.5*math.sin(clock['time']*2))
    assert 'MotionControls' in take(query(w,b'MotionSettings'))
    checks.append('Query-visible runtime component changes reevaluate behavior without reloading source')

    load(w,FOLLOWING)
    run(w,'scene { Cube { Position: {4, 2, -1} } }')
    assert components(w,'scene.Sphere')['Position']==dict(x=7,y=2,z=-1)
    assert inspect(w,'scene.Sphere')['id']==sphere_id
    checks.append('Hot replacement with target-following logic preserves the existing entity')

    assert managed(w,b'AgentBehavior',b'scene {')!=0
    assert error(w)
    assert take(source(w,b'AgentBehavior'))==FOLLOWING
    run(w,'scene { Cube { Position: {1, 3, 0} } }')
    assert components(w,'scene.Sphere')['Position']==dict(x=4,y=3,z=0)
    checks.append('Failed replacement parse preserves prior source and its live observers')

    load(w,REACTION)
    assert components(w,'scene.Sphere')['Color']['g']==1
    run(w,'scene { Cube { Position: {-1, 3, 0} } }')
    assert components(w,'scene.Sphere')['Color']['r']==1
    load(w,'// stopped\n')
    run(w,'scene { Cube { Position: {1, 3, 0} } }')
    assert components(w,'scene.Sphere')['Color']['r']==1
    checks.append('Conditional reactions and stopping a module use the same interpreter')

    load(w,BREATHING)
    with tempfile.TemporaryDirectory(prefix='Behavior-') as folder:
        saved=Path(folder)/'behavior.flecs'
        saved.write_text(take(source(w,b'AgentBehavior')),encoding='utf-8')
        destroy(w); w=world()
        load(w,saved.read_text(encoding='utf-8'))
        step(w,.25)
        near(components(w,'scene.Sphere')['Scale']['x'],1+.2*math.sin(1))
    checks.append('Saved source reloads into a fresh world and resumes execution without AI regeneration')

    measurements={}
    for label,enabled in [('baseline',False),('reactive',True)]:
        if w: destroy(w)
        w=world()
        if enabled: load(w,BREATHING)
        started=time.perf_counter()
        for _ in range(1000): step(w,.016)
        measurements[label+'_microseconds_per_tick']=(time.perf_counter()-started)*1000
    report['local_native_measurement']=measurements
    checks.append('Repeated ticking, independent reconstruction and teardown complete')
    report['passed']=True
except BaseException as exc:
    report['error']=repr(exc); traceback.print_exc(); raise
finally:
    if w: destroy(w)
    (ROOT/'Verification/runtime-behavior-native-checks.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps(report,indent=2))
