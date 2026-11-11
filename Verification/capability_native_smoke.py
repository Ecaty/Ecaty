import ctypes as c,json,math,pathlib,sys
root=pathlib.Path(__file__).resolve().parent.parent
lib=c.CDLL(str(pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else root/'Native/build-capabilities/plugin/flecs_unity.dll'))
def api(name,restype,args):
 f=getattr(lib,name);f.restype=restype;f.argtypes=args;return f
create=api('fu_create',c.c_void_p,[c.c_ushort]);destroy=api('fu_destroy',None,[c.c_void_p]);run=api('fu_run_script',c.c_int,[c.c_void_p,c.c_char_p]);tick=api('fu_tick',c.c_int,[c.c_void_p,c.c_float]);entity=api('fu_entity_json',c.c_void_p,[c.c_void_p,c.c_char_p]);free=api('fu_free',None,[c.c_void_p]);error=api('fu_last_error',c.c_char_p,[c.c_void_p]);setjson=api('fu_set_component_json',c.c_int,[c.c_void_p,c.c_char_p,c.c_char_p,c.c_char_p]);lookup=api('fu_lookup',c.c_uint64,[c.c_void_p,c.c_char_p]);has=api('fu_cap_has',c.c_int,[c.c_void_p,c.c_uint64,c.c_char_p]);ack=api('fu_cap_ack',None,[c.c_void_p,c.c_uint64]);ready=api('fu_cap_ready',c.c_int,[c.c_void_p,c.c_uint64]);checks=[]
def script(w,s):assert run(w,s.encode())==0,error(w)
def read(w,path):
 p=entity(w,path.encode());assert p,path
 try:return json.loads(c.string_at(p))['components']
 finally:free(p)
def quote(s):return json.dumps(s).replace('{','\\{').replace('$','\\$')
w=create(0);assert w
try:
 script(w,'struct TestValue(value: i32)\nValue { TestValue: {0} }')
 xml='<root BTCPP_format="4"><BehaviorTree ID="Main"><Sequence><FlecsSet entity="Value" component="TestValue" value="{&quot;value&quot;:1}"/><SimWait seconds="0.1"/><FlecsSet entity="Value" component="TestValue" value="{&quot;value&quot;:2}"/></Sequence></BehaviorTree></root>'
 script(w,'MyTree { NativeTree: {xml:'+quote(xml)+',revision:1,enabled:true,loop:false} }')
 tick(w,.05);assert read(w,'Value')['TestValue']['value']==1
 for _ in range(4):tick(w,.05)
 assert read(w,'Value')['TestValue']['value']==2
 before=read(w,'MyTree')['NativeTreeResult']['ticks'];tick(w,.05);assert read(w,'MyTree')['NativeTreeResult']['ticks']==before
 checks.append('Runtime Script XML creates a native behavior tree; ECS writes and simulation-time waiting execute without C++ regeneration; completed tasks stay completed')
 script(w,'MyTree { NativeTree: {xml:"invalid",revision:2,enabled:true} }');tick(w,.05)
 assert read(w,'MyTree')['NativeTreeResult']['status']==-1
 checks.append('Invalid tree definitions are reported as ECS result data')
 script(w,(root/'Packages/com.runtime.capabilities/Runtime/Capabilities/Resources/CapabilitySchema.flecs').read_text())
 script(w,(root/'Packages/com.runtime.capabilities/Runtime/Capabilities/CapabilityConfigSchema.flecs').read_text())
 script(w,'A { CapBinding: {target:Value,enabled:true,revision:1} }\nB { CapBinding: {target:Value,enabled:true,revision:1}; (CapDependsOn,A) }')
 a=lookup(w,b'A');b=lookup(w,b'B');tick(w,.01);assert has(w,b,b'CapDirty') and ready(w,b)==0
 assert setjson(w,b'A',b'CapState',b'{"phase":1,"appliedRevision":1,"generation":1,"message":""}')==0
 ack(w,b);tick(w,.01);assert ready(w,b)==1 and has(w,b,b'CapDirty')
 checks.append('Flecs dependency results propagate dirty generations and gate consumers until prerequisites are ready')
 assert setjson(w,b'A',b'CapBinding',b'{"target":"Value","enabled":false,"revision":1}')==0
 assert ready(w,b)==0
 script(w,'Road { CapSpline: {points:"[[0,0,0],[2,0,0]]",width:1} }');tick(w,.01)
 footprint=json.loads(read(w,'Road')['NativeFootprint']['json']);assert isinstance(footprint,list) and len(footprint[0])>3
 assert setjson(w,b'Road',b'CapSpline',json.dumps({'points':'[[0,0,0],[4,0,0]]','width':2}).encode())==0;tick(w,.01)
 assert read(w,'Road')['NativeFootprint']['json']!=json.dumps(footprint,separators=(',',':'))
 script(w,'Area { CapSpline: {points:"[[0,0,0],[2,0,0],[2,0,2]]",width:0,closed:true,fill:true} }');tick(w,.01)
 area=json.loads(read(w,'Area')['NativeFootprint']['json']);assert isinstance(area,list) and len(area[0])==3
 checks.append('Closed filled-area recipes produce polygon footprints as well as open road strokes')
 checks.append('Disabled prerequisites gate dependents; reflected spline changes generate native ECS footprints without Unity')

finally:destroy(w)
outline=api('fu_path_outline',c.c_int,[c.POINTER(c.c_double),c.c_int,c.c_double,c.POINTER(c.c_double),c.c_int,c.POINTER(c.c_int),c.c_int,c.POINTER(c.c_int)])
points=(c.c_double*6)(0,0,2,0,2,2);rings=c.c_int();n=outline(points,3,.5,None,0,None,0,c.byref(rings));assert n>3 and rings.value>0
out=(c.c_double*(n*2))();starts=(c.c_int*(rings.value+1))();assert outline(points,3,.5,out,n,starts,len(starts),c.byref(rings))==n;assert starts[len(starts)-1]==n
assert outline(points,3,float('nan'),None,0,None,0,c.byref(rings))==-1
checks.append('Native Clipper produces bounded multi-ring footprints and rejects nonfinite input')
audio_create=api('fu_audio_create',c.c_void_p,[c.c_void_p,c.c_int,c.c_int]);audio_destroy=api('fu_audio_destroy',None,[c.c_void_p]);note=api('fu_audio_note',c.c_int,[c.c_void_p,c.c_int,c.c_int,c.c_float,c.c_int,c.c_uint64]);render=api('fu_audio_render',None,[c.c_void_p,c.POINTER(c.c_float),c.c_int])
bank=(root/'Assets/StreamingAssets/Ecaty/Audio/GeneralUser-GS.sf2').read_bytes();data=c.create_string_buffer(bank);a=audio_create(data,len(bank),44100);assert a
try:
 assert note(a,0,60,.8,0,0)==0;assert note(a,1,60,0,0,44100)==0;assert note(a,0,72,.8,0,0)==0
 buf=(c.c_float*4410)();render(a,buf,len(buf));assert max(abs(x) for x in buf)>.0001 and all(math.isfinite(x) and abs(x)<=1 for x in buf)
 note(a,4,60,.8,0,0);render(a,buf,len(buf));assert max(abs(x) for x in buf)>.001
 checks.append('Bundled SoundFont and procedural-noise voices render finite PCM; sample-sorted note scheduling permits immediate notes after future note-off commands')
finally:audio_destroy(a)
a=audio_create(data,len(bank),44100);b=audio_create(data,len(bank),44100);assert a and b
audio_destroy(a)
try:
 note(b,0,67,1,0,0);buf=(c.c_float*4410)();render(b,buf,len(buf));assert max(abs(x) for x in buf)>.0001
 checks.append('Independent emitters share bank lifetime safely; removing one does not invalidate another')
finally:audio_destroy(b)
report={'passed':True,'checks':checks};(root/'Verification/capability-native-checks.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
