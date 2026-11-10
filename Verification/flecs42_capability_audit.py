"""Isolated Flecs 4.2 capability audit; no Unity edits or AI/provider requests."""
import ctypes as c
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
sys.stdout.reconfigure(encoding='utf-8')
root=Path(__file__).resolve().parents[1]
lib=c.CDLL(str(root/'Assets/Plugins/Ecaty/x86_64/flecs_unity.dll'))
P=c.c_void_p
U=c.c_uint64
def bind(name,result,*args):
    fn=getattr(lib,name);fn.restype=result;fn.argtypes=args;return fn
create=bind('fu_create',P,c.c_ushort)
destroy=bind('fu_destroy',None,P)
world=bind('fu_world',P,P)
managed=bind('fu_set_managed_script',c.c_int,P,c.c_char_p,c.c_char_p)
run=bind('fu_run_script',c.c_int,P,c.c_char_p)
tick=bind('fu_tick',c.c_int,P,c.c_float)
error=bind('fu_last_error',c.c_char_p,P)
entity=bind('fu_entity_json',P,P,c.c_char_p)
source=bind('fu_managed_script_source',P,P,c.c_char_p)
free=bind('fu_free',None,P)
lookup=bind('ecs_lookup',U,P,c.c_char_p)
imp=bind('ecs_import_c',U,P,P,c.c_char_p)
from_scene=bind('ecs_script_from_scene',c.c_int,P,U)
version=bind('fu_version',c.c_char_p)
class Mouse(c.Structure):
    _fields_=[('target',U),('screen_x',c.c_float),('screen_y',c.c_float),('local_x',c.c_float),('local_y',c.c_float),('delta_x',c.c_float),('delta_y',c.c_float),('button',c.c_int32),('buttons',c.c_uint32),('ctrl',c.c_bool),('shift',c.c_bool),('alt',c.c_bool),('meta',c.c_bool),('id',U)]
class Result(c.Structure):
    _fields_=[('resolved',c.c_int32),('stopped',c.c_bool)]
mouse=bind('ecs_script_mouse_event',Result,P,c.POINTER(Mouse))
def take(ptr):
    assert ptr
    try:return c.string_at(ptr).decode('utf-8')
    finally:free(ptr)
def install(name,text):assert managed(ctx,name.encode(),text.encode())==0,error(ctx)
def script(text):assert run(ctx,text.encode())==0,error(ctx)
def inspect(path):return json.loads(take(entity(ctx,path.encode())))
def step():assert tick(ctx,0.016)==0,error(ctx)
def click(path):
    target=lookup(w,path.encode());assert target,path
    down=mouse(w,c.byref(Mouse(target=target,buttons=1)))
    up=mouse(w,c.byref(Mouse(target=target,buttons=0)))
    step()
    return down.resolved+up.resolved
checks=[]
report={'passed':False,'checked_at':datetime.now(timezone.utc).isoformat(),
        'flecs':version().decode(),'checks':checks,
        'limits':['Isolated Windows x64 native worlds only; not a Unity input integration test',
                  'No AI inference, performance benchmark, mobile/IL2CPP or full-session persistence test']}
ctx=create(0);assert ctx
w=world(ctx)
try:
    report['initial_module_availability']={p:bool(lookup(w,p.encode())) for p in ['flecs.script.on.click','flecs.script.shell','flecs.script.math']}
    assert imp(w,c.cast(lib.FlecsScriptInputImport,P),b'FlecsScriptInput')
    install('AuditAsync','''using flecs.script
struct AuditCount(value: i32)
template AuditCounter {
  mut clicks = 0
  AuditCount: {clicks}
  async {
    while true {
      await on.click(this)
      clicks = clicks + 1
    }
  }
}
AuditCounter audit_a()
AuditCounter audit_b()
''')
    step()
    assert [click('audit_a'),click('audit_a'),click('audit_b')] == [1,1,1]
    assert inspect('audit_a')['components']['AuditCount']['value'] == 2
    assert inspect('audit_b')['components']['AuditCount']['value'] == 1
    checks.append('Async click loops keep independent mut state and update ordinary reflected components')
    install('AuditTemplates','''struct AuditForm(size: f32)
template AuditFormImpl : AuditForm {
  prop size: f32 = 1
  Scale: {size, size, size}
}
template AuditAssembly {
  prop form: template AuditForm
  prop size: f32 = 2
  form: {size}
  node { Position: {1, 0, 0} }
}
AuditAssembly audit_assembly(form: AuditFormImpl)
''')
    assert inspect('audit_assembly')['components']['Scale']['x'] == 2
    child_id=lookup(w,b'audit_assembly.node')
    assert 'flecs.core.Parent' in inspect('audit_assembly.node')['components']
    script('audit_assembly { AuditAssembly: {AuditFormImpl, 4} }')
    assert inspect('audit_assembly')['components']['Scale']['x'] == 4
    assert lookup(w,b'audit_assembly.node') == child_id
    get_target=bind('ecs_get_target',U,P,U,c.c_int32)
    child_of=lookup(w,b'flecs.core.ChildOf'); assert child_of
    assert get_target(w,child_id,child_of,0)==lookup(w,b'audit_assembly')
    checks.append('Template interfaces compose at runtime; prop changes preserve unaffected child identity')
    checks.append('Default template Parent children remain accessible through ChildOf target lookup used by the snapshot bridge')
    install('AuditSave','''// Keep this author comment.
audit_saved { Position: {1, 2, 3} }
''')
    script('audit_saved { Position: {4, 5, 6} }')
    assert from_scene(w,lookup(w,b'AuditSave')) == 1
    saved=take(source(ctx,b'AuditSave'))
    assert '// Keep this author comment.' in saved and '4, 5, 6' in saved
    assert from_scene(w,lookup(w,b'AuditSave')) == 0
    delete=bind('ecs_delete',None,P,U)
    delete(w,lookup(w,b'audit_a'))
    assert not lookup(w,b'audit_a')
    assert click('audit_b') == 1
    assert inspect('audit_b')['components']['AuditCount']['value'] == 2
    checks.append('Scene-to-source edit preserves author comments and detects unchanged source')
    checks.append('Deleting one template instance leaves another instance event loop functional')
    report['passed']=True
except Exception as exc:
    report['error']=str(exc)
    raise
finally:
    destroy(ctx)
    (root/'Verification/flecs42-capability-checks.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report,indent=2))

