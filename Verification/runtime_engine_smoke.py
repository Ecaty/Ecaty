"""Native runtime APIs: dynamic data, cached queries, events, source edits and async timers."""
import ctypes as c
import os
import json
from pathlib import Path
from datetime import datetime, timezone
ROOT=Path(__file__).resolve().parents[1]
lib=c.CDLL(os.environ.get('ECATY_NATIVE_LIBRARY',str(ROOT/'Assets/Plugins/Ecaty/x86_64/flecs_unity.dll')))
P=c.c_void_p; U=c.c_uint64

def bind(name,result,*args):
 f=getattr(lib,name);f.restype=result;f.argtypes=args;return f
create=bind('fu_create',P,c.c_ushort);destroy=bind('fu_destroy',None,P)
managed=bind('fu_set_managed_script',c.c_int,P,c.c_char_p,c.c_char_p)
script=bind('fu_run_script',c.c_int,P,c.c_char_p);error=bind('fu_last_error',c.c_char_p,P)
tick=bind('fu_tick',c.c_int,P,c.c_float);lookup=bind('fu_lookup',U,P,c.c_char_p)
entity=bind('fu_entity_json',P,P,c.c_char_p);free=bind('fu_free',None,P)
query=bind('fu_query_json',P,P,c.c_char_p);changes=bind('fu_query_changes',P,P,c.c_char_p)
size=bind('fu_component_size',c.c_int,P,U);read=bind('fu_read_component',c.c_int,P,U,U,P,c.c_int)
write=bind('fu_write_components',c.c_int,P,U,P,P,c.c_int,c.c_int)
rows=bind('fu_read_query_data',c.c_int,P,c.c_char_p,c.c_char_p,P,P,P,c.c_int,c.c_int)
delete=bind('fu_delete_entity',c.c_int,P,c.c_char_p)
set_json=bind('fu_set_component_json',c.c_int,P,c.c_char_p,c.c_char_p,c.c_char_p)
source=bind('fu_managed_script_source',P,P,c.c_char_p)
edit=bind('fu_script_edit',c.c_int,P,c.c_char_p,c.c_char_p,c.c_char_p,c.c_char_p)
undo=bind('fu_script_undo',c.c_int,P,c.c_char_p)
shell=bind('fu_shell_json',P,P,c.c_char_p);stats=bind('fu_bridge_diagnostics',P,P)
def take(ptr):
 assert ptr,error(w)
 try:return c.string_at(ptr).decode()
 finally:free(ptr)
def install(path,text):assert managed(w,path.encode(),text.encode())==0,error(w)
def run(text):assert script(w,text.encode())==0,error(w)
def comp(path):return json.loads(take(entity(w,path.encode())))['components']
def step():assert tick(w,.02)==0
checks=[];report={'passed':False,'checkedUtc':datetime.now(timezone.utc).isoformat(),'checks':checks,
 'limits':['Windows x64 native library; no Unity/device or AI provider call']}
w=create(0);assert w
try:
 install('DynamicSchema','struct DynamicData(value: f32, count: i32, flag: bool)\nDynamic { DynamicData: {1,2,true} }')
 class Data(c.Structure):_fields_=[('value',c.c_float),('count',c.c_int32),('flag',c.c_bool)]
 t=lookup(w,b'DynamicData');e=lookup(w,b'Dynamic');data=Data();assert size(w,t)==c.sizeof(Data)
 assert read(w,e,t,c.byref(data),c.sizeof(data))==1 and data.count==2 and data.flag
 ids=(U*1)(e);values=(Data*1)(Data(3,7,False))
 assert write(w,t,ids,values,c.sizeof(Data),1)==0 and comp('Dynamic')['DynamicData']['count']==7
 assert read(w,e,t,c.byref(data),1)==-1
 assert size(w,lookup(w,b'Label'))==-1
 checks.append('Runtime-defined POD data supports validated reads/batches and rejects size/string-layout mismatches')
 q=b'[in] DynamicData';take(changes(w,q));assert not changes(w,q)
 assert set_json(w,b'Dynamic',b'DynamicData',b'{"value":4,"count":9,"flag":true}')==0
 take(changes(w,q));assert not changes(w,q)
 values=(Data*4)();entities=(U*4)();targets=(U*4)()
 assert rows(w,q,b'',entities,targets,values,c.sizeof(Data),4)==1 and values[0].count==9
 checks.append('Change detection suppresses unchanged snapshots and binary query rows return actual runtime data')
 install('AwaitRules','''struct Signal(value: i32)
template TimedSignal {
 mut ready = false
 if ready { Signal: {1}; child { Position: {0,1,0} } } else { Signal: {0} }
 async { await engine.wait(0.05); ready = true; await engine.wait(3600) }
}
TimedSignal timer_a()
TimedSignal timer_b()
''')
 step();assert comp('timer_a')['Signal']['value']==0
 for _ in range(5):step()
 assert comp('timer_a')['Signal']['value']==1
 assert comp('timer_a.child')['Position']['y']==1
 assert set_json(w,b'timer_a',b'TimedSignal.mut',b'{"ready":false}')==0
 assert comp('timer_a')['Signal']['value']==0
 assert delete(w,b'timer_a')==1
 step();assert comp('timer_b')['Signal']['value']==1
 checks.append('Simulation-time await resumes reactive Script with structural child creation; restored mut state propagates; cancelled instance does not affect peers')
 install('Editable','// Preserve comment\neditable { Position: {1,2,3} }')
 assert edit(w,b'Editable',b'editable',b'Position',b'{4,5,6}')==0,error(w)
 assert 'Preserve comment' in take(source(w,b'Editable')) and comp('editable')['Position']['x']==4
 assert undo(w,b'Editable')==0 and comp('editable')['Position']['x']==1
 checks.append('Source-preserving expression edit and upstream source undo change the live world')
 metric=bind('fu_define_metric',U,P,c.c_char_p,c.c_char_p,c.c_char_p,c.c_int)
 alert=bind('fu_define_alert',U,P,c.c_char_p,c.c_char_p,c.c_char_p)
 run('AuditFault {}')
 assert metric(w,b'AuditMetrics.Count',b'DynamicData',b'count',0)
 assert alert(w,b'AuditAlerts.Fault',b'AuditFault',b'Adapter fault on $this')
 run('faulty { AuditFault }')
 for _ in range(120):step()
 metric_rows=json.loads(take(query(w,b'flecs.metrics.Value')))['results']
 assert any(row['fields']['values'][0]['value']==9 for row in metric_rows)
 assert json.loads(take(query(w,b'flecs.alerts.Instance')))['results']
 delete(w,b'faulty')
 for _ in range(120):step()
 assert not json.loads(take(query(w,b'flecs.alerts.Instance')))['results']
 checks.append('Native metrics sample runtime-defined data and query-based alerts activate then clear')
 result=json.loads(take(shell(w,b'help')));assert not result['errors'] and result['results']
 for n in range(40):take(query(w,('DynamicData'+' '*n).encode()))
 diagnostics=json.loads(take(stats(w)));assert diagnostics['cachedQueries']<=32
 report['bridge']=diagnostics
 checks.append('Script Shell returns structured results and ad-hoc query cache stays bounded')
 report['passed']=True
except Exception as exc:report['error']=str(exc);raise
finally:
 destroy(w)
 (ROOT/'Verification/runtime-engine-native-checks.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps(report,indent=2))
