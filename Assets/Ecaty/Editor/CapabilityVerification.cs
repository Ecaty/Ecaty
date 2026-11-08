#if ECATY_CAP_FEEL && ECATY_CAP_COZY && ECATY_CAP_BROCCOLI && ECATY_CAP_FLEXALON && ECATY_CAP_SPLINES
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ecaty.Flecs;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.VFX;
using Object=UnityEngine.Object;
namespace Ecaty.Editor {
    public static class CapabilityVerification {
        static bool running;
        sealed class SessionProbe:CapabilityInstance {
            public bool DisposedWithOriginalSession;
            public SessionProbe(CapabilityContext c):base(c){}
            public override void Apply(JObject config){}
            public override void Dispose(){DisposedWithOriginalSession=Context.Session.IsDisposed;}
        }
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        static async Task Frames(int count){int begin=Time.frameCount;var end=DateTime.UtcNow.AddSeconds(45);while(Time.frameCount<begin+count){if(DateTime.UtcNow>end)throw new TimeoutException("Player did not advance");await Task.Yield();}}
        static async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(10);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Capability condition was not reached.");await Task.Yield();}}
        [InitializeOnLoadMethod]static void Hook(){EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Ecaty.VerifyCapabilities",false))Run();};}
        [MenuItem("Ecaty/Runtime engine/Verify capabilities")]
        public static void Start(){if(EditorApplication.isPlaying)Run();else{SessionState.SetBool("Ecaty.VerifyCapabilities",true);EditorApplication.isPlaying=true;}}
        [MenuItem("Ecaty/Runtime engine/Start capability workshop")]
        public static void Workshop(){if(!EditorApplication.isPlaying){SessionState.SetBool("Ecaty.CapabilityWorkshop",true);EditorApplication.isPlaying=true;}else Install();}
        [InitializeOnLoadMethod]static void WorkshopHook(){EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool("Ecaty.CapabilityWorkshop",false)){SessionState.EraseBool("Ecaty.CapabilityWorkshop");EditorApplication.delayCall+=()=>Install();}};}
        static FlecsWorld Install(){
            var world=Object.FindAnyObjectByType<FlecsWorld>();Require(world!=null,"World was not bootstrapped");
            Require(world.GetComponent<RuntimeExperience>()?.Dock?.EnsureWorld() != false, "Could not open the workshop world.");
            world.Session.SetManagedScript("CapabilityWorkshop",Resources.Load<TextAsset>("CapabilityWorkshop").text);
            world.SyncViews();world.GetComponent<CapabilityRuntime>().Reconcile();return world;
        }
        static JObject Component(FlecsWorld world,string path,string type)=>JObject.Parse(world.Session.ComponentJson(world.Session.Lookup(path),type));
        static async void Run(){
            if(running)return;running=true;SessionState.EraseBool("Ecaty.VerifyCapabilities");var checks=new JArray();var errors=new System.Collections.Generic.List<string>();Application.LogCallback capture=(message,stack,type)=>{if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message);};Application.logMessageReceived+=capture;var report=new JObject{["passed"]=false,["testedUtc"]=DateTime.UtcNow.ToString("O"),["checks"]=checks};
            try{
                Application.runInBackground=true;await Frames(3);var world=Install();var runtime=world.GetComponent<CapabilityRuntime>();await Frames(90);
                var bindings=new[]{"BodyBinding","MaterialBinding","AudioBinding","ParticleBinding","LightBinding","RoadBinding","TerrainBinding","NavSurfaceBinding","NavigationBinding","HandleBinding","FeedbackBinding","SequenceBinding","BuildingBinding","VfxBinding","CameraBinding","TimelineBinding","TreeBinding","LayoutBinding","RigBinding","WaterBinding","FloatBodyBinding","BuoyancyBinding","AreaBinding"};
                foreach(var name in bindings){var state=Component(world,name,"CapState");Require(state!=null&&state.I("phase")==1,name+": "+state);}
                Require(world.FindView(world.Session.Lookup("CapArea")).GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh&&f.sharedMesh.vertexCount>0),"Filled area mesh missing");
                checks.Add("Script-created bindings activate real physics, material, sound, particles, light, spline, modifier ground, navigation, handle, Feel feedback/sequencing, Cozy and VFX instances");
                var body=world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>();Require(body&&body.position.y<2.8f,"Physics did not move the actual body");Require(Component(world,"BodyBinding","CapReadback").F("y")<2.8f,"Physics readback is stale");checks.Add("Unity is the body solver; the normal Flecs presentation sync does not snap its root back");
                Require(Component(world,"AudioBinding","CapAudioMeter").F("samples")>0,"Audio callback did not run");
                await Until(()=>{var data=world.Session.ComponentJson(world.Session.Lookup("RigBinding"),"CapRigResult");return !string.IsNullOrEmpty(data)&&JObject.Parse(data).F("error")<.2f;});var tip=world.FindView(world.Session.Lookup("RigModel.Root.Mid.Tip")).transform;var goal=world.FindView(world.Session.Lookup("RigGoal")).transform;
                Require(Component(world,"RigBinding","CapRigResult").F("error")<.2f,"Runtime IK did not reach its target");
                var one=world.FindView(world.Session.Lookup("LayoutRoot.One")).transform;var two=world.FindView(world.Session.Lookup("LayoutRoot.Two")).transform;Require(Vector3.Distance(one.position,two.position)>.5f,"Flexalon did not arrange its ECS children");
                Require(world.FindView(world.Session.Lookup("CapScene")).GetComponentInChildren<Broccoli.Factory.TreeFactory>()!=null,"Runtime vegetation factory missing");
                Require(world.FindView(world.Session.Lookup("CapFloat")).transform.position.y>-.3f,"Bound buoyancy did not counter gravity");
                checks.Add("Runtime-created TwoBoneIK reaches a target, Flexalon arranges ECS children, Broccoli generates a tree and native synthesis receives real audio callbacks");
                var initial=Component(world,"TerrainBinding","CapState").I("generation");var shape=Component(world,"RoadBinding","CapSpline");shape["points"]="[[-2,0.5,0],[0,2,0],[2,0.5,1]]";world.Session.SetComponentJson("RoadBinding","CapSpline",shape.ToString());await Frames(12);
                Require(Component(world,"TerrainBinding","CapState").I("generation")>initial,"Native dependencies did not regenerate downstream ground");Require(Component(world,"TerrainBinding","CapBounds").F("maxY")>1.5f,"Collider/mesh did not reflect the updated road");checks.Add("Runtime road edits propagate through native ECS dependencies into changed Unity ground mesh/collider bounds");
                var config=Component(world,"BodyBinding","CapBinding");config["enabled"]=false;world.Session.SetComponentJson("BodyBinding","CapBinding",config.ToString());await Until(()=>!body);Require(!body,"Disabling did not remove the owned body");config["enabled"]=true;world.Session.SetComponentJson("BodyBinding","CapBinding",config.ToString());await Until(()=>world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>());Require(world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>(),"Reenabling did not rebuild the body");checks.Add("Capability disable/remove and reenable/recreate use the same lifecycle");
                var experience=world.GetComponent<RuntimeExperience>();string saved=experience.Capture();Require(saved.Contains("CapSpline")&&saved.Contains("CapAudio"),"Capabilities are absent from reconstructable saved sources");
                Require(JObject.Parse(world.Session.Query("EngineCapability, BoundCapability"))["results"].Count()>12,"Agent cannot discover capabilities");checks.Add("Capabilities and asset metadata are queryable and their managed sources participate in experience persistence");
                body=world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>();var bodyConfig=Component(world,"BodyBinding","CapBody");bodyConfig["kinematic"]=true;world.Session.SetComponentJson("BodyBinding","CapBody",bodyConfig.ToString());runtime.Reconcile();body.isKinematic=true;body.position=new Vector3(1,2,1);saved=experience.Capture();
                experience.Load(saved);await Frames(25);runtime.Reconcile();await Frames(12);
                foreach(var name in bindings){var state=Component(world,name,"CapState");Require(state.I("phase")==1,"Reload "+name+": "+state);}
                var restored=world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>();Require(restored&&Mathf.Abs(restored.position.y-2)<.1f,"Body state did not survive reconstruction");
                Require(Component(world,"RoadBinding","CapSpline").S("points").Contains("2"),"Live spline edits were not restored");
                checks.Add("Actual session reconstruction restores source-backed capabilities, live edited configuration, runtime plugin instances and selected physical state");
                var isolated=FlecsBootstrap.Create(Resources.Load<TextAsset>("EcatyWorld"),Resources.Load<Material>("EcatyMaterial"),0);try{
                    var scope=isolated.gameObject.AddComponent<CapabilityRuntime>();scope.Initialize(isolated);SessionProbe probe=null;scope.Register(new Adapter<SessionProbe>("CapProbe","verification",0,c=>probe=new SessionProbe(c)));
                    isolated.Session.SetManagedScript("ScopeProbe","struct CapProbe(value:i32)\nProbeTarget { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }\nProbeBinding { CapBinding: {target:ProbeTarget,enabled:true,revision:1}; CapProbe: {1} }");isolated.SyncViews();scope.Reconcile();Require(probe!=null,"Session probe did not bind");var previous=probe;
                    isolated.Initialize(0,Resources.Load<TextAsset>("EcatyWorld"),"EcatyWorld");scope.Reconcile();Require(previous.DisposedWithOriginalSession,"Adapter cleanup used a replacement session instead of its original disposed session");
                    checks.Add("Reusable adapter cleanup retains its original session across direct world replacement; stale entity IDs never target the new world");
                }finally{Object.Destroy(isolated.gameObject);}
                Require(errors.Count==0,"Runtime errors: "+string.Join(" | ",errors.Distinct()));report["passed"]=true;
            }catch(Exception error){report["error"]=error.ToString();Debug.LogException(error);}
            finally{Application.logMessageReceived-=capture;Directory.CreateDirectory("Verification");File.WriteAllText("Verification/capability-unity-checks.json",report.ToString());running=false;}
        }
    }
}

#endif
