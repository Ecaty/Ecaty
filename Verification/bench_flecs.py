"""Micro-benchmark of bridge calls via ctypes (copy of the DLL, isolated world, REST off)."""
import ctypes as c, sys, time, json
lib = c.CDLL(sys.argv[1])
P = c.c_void_p; U = c.c_uint64
def b(n, r, *a):
    f = getattr(lib, n); f.restype = r; f.argtypes = a; return f
create=b('fu_create',P,c.c_ushort); destroy=b('fu_destroy',None,P)
abi=b('fu_abi_version',c.c_int)
managed=b('fu_set_managed_script',c.c_int,P,c.c_char_p,c.c_char_p)
run=b('fu_run_script',c.c_int,P,c.c_char_p); err=b('fu_last_error',c.c_char_p,P)
tick=b('fu_tick',c.c_int,P,c.c_float); lookup=b('fu_lookup',U,P,c.c_char_p)
ejson=b('fu_entity_json',P,P,c.c_char_p); cjson=b('fu_component_json',P,P,U,c.c_char_p)
free=b('fu_free',None,P); qjson=b('fu_query_json',P,P,c.c_char_p)
read=b('fu_read_component',c.c_int,P,U,U,P,c.c_int)
write=b('fu_write_components',c.c_int,P,U,P,P,c.c_int,c.c_int)
rows=b('fu_read_query_data',c.c_int,P,c.c_char_p,c.c_char_p,P,P,P,c.c_int,c.c_int)
setj=b('fu_set_component_json',c.c_int,P,c.c_char_p,c.c_char_p,c.c_char_p)
views=b('fu_read_views',c.c_int,P,P,c.c_int)

def T(label, fn, n):
    fn()
    t=time.perf_counter()
    for _ in range(n): fn()
    dt=(time.perf_counter()-t)/n*1e6
    print(f"{label:60s} {dt:10.2f} us/call"); return dt

w=create(0); assert w, err(None)
def ok(rc):
    assert rc==0, err(w)
N=1000
ok(managed(w,b"Schema",b"""struct Vec(x: f32, y: f32, z: f32)
struct Big(a: f32, b: f32, c: f32, d: f32, e: i32, f: bool, g: f32, h: f32)
struct Wave(phase: f32, speed: f32, out: f32)
"""))
# Settings entity with 6 components similar to GalleryTreeSettings
ok(managed(w,b"Settings",b"Settings { Big: {1,2,3,4,5,true,7,8}\n Vec: {1,2,3}\n Wave: {0,1,0}\n Position: {1,2,3}\n Rotation: {0,0,0}\n Scale: {1,1,1} }"))
lines="\n".join(f"E{i} {{ Vec: {{{i},0,0}} }}" for i in range(N))
ok(managed(w,b"Bulk",lines.encode()))
settings=lookup(w,b"Settings"); big=lookup(w,b"Big"); vec=lookup(w,b"Vec")
ents=(U*N)(*[lookup(w,f"E{i}".encode()) for i in range(N)])
print("baseline ctypes call (fu_abi_version)")
T("fu_abi_version (pure P/Invoke-ish overhead, python)", lambda: abi(), 200000)
def ej():
    p=ejson(w,b"Settings"); s=c.string_at(p); free(p); return s
s=ej(); print("entity_json bytes:",len(s))
T("fu_entity_json(Settings, 6 comps)+copy", ej, 20000)
T("  + python json.loads", lambda: json.loads(ej()), 20000)
def cj():
    p=cjson(w,settings,b"Big"); s=c.string_at(p); free(p)
T("fu_component_json(Big)", cj, 50000)
buf=(c.c_byte*32)()
T("fu_read_component(Big, 32B binary)", lambda: read(w,settings,big,buf,32), 200000)
T("fu_set_component_json(Settings, Big)", lambda: setj(w,b"Settings",b"Big",b'{"a":1,"b":2,"c":3,"d":4,"e":5,"f":true,"g":7,"h":8}'), 50000)
one=(U*1)(settings)
T("fu_write_components(Big, 1 entity)", lambda: write(w,big,one,buf,32,1), 200000)
# Bulk
vals=(c.c_float*(3*N))(); out_e=(U*N)();
def qj():
    p=qjson(w,b"Vec"); s=c.string_at(p); free(p); return s
print("query_json bytes for N=%d:"%N, len(qj()))
tq=T(f"fu_query_json('Vec') N={N} (cached query)", qj, 500)
T(f"  + python json.loads N={N}", lambda: json.loads(qj()), 300)
tr=T(f"fu_read_query_data('Vec') N={N} memcpy", lambda: rows(w,b"Vec",None,out_e,None,vals,12,N), 5000)
tw=T(f"fu_write_components(Vec) N={N}", lambda: write(w,vec,ents,vals,12,N), 2000)
# Views (fixed snapshot)
ok(managed(w,b"Views","\n".join(f"V{i} {{ Position: {{{i},0,0}}\n Rotation: {{0,0,0}}\n Scale: {{1,1,1}}\n Color: {{1,1,1,1}}\n Primitive: {{0}} }}" for i in range(N)).encode()))
vbuf=(c.c_byte*(80*(N+50)))()
T(f"fu_read_views N~{N} (80B fu_view, try_get x4 per entity)", lambda: views(w,vbuf,N+50), 2000)
ok(managed(w,b"Views",b""))
# Tick costs
T("fu_tick baseline (no reactive script)", lambda: tick(w,0.016), 3000)
# Reactive managed expressions that depend on Clock: N entities
NL = chr(10)
ok(managed(w,b"Reactive",("using flecs.script.math"+NL+NL.join(f"E{i} {{ Wave: {{{i*0.01}, 1, sin(Clock[Clock].time * 1 + {i*0.01})}} }}" for i in range(N))).encode()))
T(f"fu_tick with {N} Clock-dependent managed Script expressions", lambda: tick(w,0.016), 500)
ok(managed(w,b"Reactive",b""))
# Compiled SpinSystem on N entities
ok(managed(w,b"Spinners","\n".join(f"S{i} {{ Rotation: {{0,0,0}}\n Spin: {{1,2,3}} }}" for i in range(N)).encode()))
T(f"fu_tick with compiled SpinSystem over {N} entities (+empty bulk)", lambda: tick(w,0.016), 3000)
ok(managed(w,b"Spinners",b""))
# Template instantiation
ok(managed(w,b"Tmpl",b"template Thing {\n prop size: f32 = 1\n Vec: {size, size*2, size*3}\n Part { Vec: {size,0,0} }\n}\n"))
src="\n".join(f"T{i} {{ Thing: {{size: {i}}} }}" for i in range(N)).encode()
t=time.perf_counter(); ok(managed(w,b"Inst",src)); dt=time.perf_counter()-t
print(f"{'instantiate %d template instances (2 entities each) via managed script'%N:60s} {dt*1e3:10.2f} ms total")
t=time.perf_counter(); ok(managed(w,b"Inst",src.replace(b"size: 5}",b"size: 6}",1))); dt=time.perf_counter()-t
print(f"{'reload same managed script with one prop changed':60s} {dt*1e3:10.2f} ms total")
t=time.perf_counter(); ok(run(w,b"Settings { Big: {9,9,9,9,9,false,9,9} }")); dt=time.perf_counter()-t
print(f"{'fu_run_script one-line assignment (parse+eval)':60s} {dt*1e6:10.2f} us")
T("fu_run_script one-line assignment", lambda: run(w,b"Settings { Big: {9,9,9,9,9,false,9,9} }"), 5000)
thing=lookup(w,b"Thing"); print("Thing size via fu_component_size:", b('fu_component_size',c.c_int,P,U)(w,thing))
cnt=[0]
def prop_change():
    cnt[0]+=1
    setj(w,b"T5",b"Thing",('{"size":%d}'%(cnt[0]%7+1)).encode()); tick(w,0.016)
T("set one instance's template prop (JSON) + tick (1000 instances live)", prop_change, 500)
T("tick only (1000 template instances live, nothing changed)", lambda: tick(w,0.016), 2000)
p=ejson(w,b"T5.Part"); print("T5.Part after prop change:", c.string_at(p)[:200]); free(p)
destroy(w)
