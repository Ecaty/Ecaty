using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecaty.Editor
{
    [DefaultExecutionOrder(30000)]
    public sealed class SpatialMeshSnapshot : MonoBehaviour
    {
        public Func<Bounds> Read;
        private TaskCompletionSource<Bounds> pending;
        public async Task<Bounds> Take()
        {
            pending = new TaskCompletionSource<Bounds>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = pending.Task;
            if (await Task.WhenAny(task, Task.Delay(40000)) != task)
                throw new TimeoutException("Late mesh sampling did not run.");
            return await task;
        }
        private void LateUpdate()
        {
            var request = pending;
            if (request == null)
                return;
            pending = null;
            try
            {
                request.SetResult(Read());
            }
            catch (Exception error) { request.SetException(error); }
        }
    }

    public static class RuntimeSpatialVerification
    {
        public static bool Running
        {
            get; private set;
        }
        private static void Require(bool value, string error)
        {
            if (!value)
                throw new InvalidOperationException(error);
        }
        private static async Task Frames(int count)
        {
            int until = Time.frameCount + count;
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (Time.frameCount < until)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Play frames stopped advancing.");
                await Task.Yield();
            }
        }
        [InitializeOnLoadMethod]
        private static void Initialize() => EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Ecaty.SpatialCheck", false))
                Run();
        };
        [MenuItem("Runtime/Runtime engine/Verify spatial workflow")]
        public static void Start()
        {
            if (Running)
                return;
            if (EditorApplication.isPlaying)
            {
                Run();
                return;
            }
            SessionState.SetBool("Ecaty.SpatialCheck", true);
            EditorApplication.isPlaying = true;
        }
        private static async void Run()
        {
            if (Running)
                return;
            Running = true;
            bool owned = SessionState.GetBool("Ecaty.SpatialCheck", false);
            SessionState.EraseBool("Ecaty.SpatialCheck");
            var checks = new JArray();
            var errors = new List<string>();
            var report = new JObject { ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["passed"] = false, ["checks"] = checks };
            Application.LogCallback capture = (text, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text); };
            Application.logMessageReceived += capture;
            GameObject root = null, ceiling = null;
            try
            {
                Application.runInBackground = true;
                await Frames(3);
                var foreignColliders = Object.FindObjectsByType<Collider>();
                var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                root = world.gameObject;
                root.name = "Spatial workflow verification";
                var clay = root.AddComponent<ClayCreation>();
                clay.SaveAcceptedRecipes = false;
                var engine = root.AddComponent<DioramaRuntime>();
                engine.Initialize(world, clay);
                engine.ReadDevices = false;
                engine.AutoStep = false;
                void IsolateMotor()
                {
                    foreach (var collider in foreignColliders)
                        if (collider && collider.enabled)
                            Physics.IgnoreCollision(engine.Motor, collider);
                }
                var tools = EcatyAgentTools.Create(world, new AgentMainThread(), clay, engine);
                async Task Behavior(string source) { world.Session.SetManagedScript("VerificationBehavior", source); world.SyncViews(); await Task.Yield(); }
                Task<string> Call(string name, string field = null, string text = null) => tools.First(t => t.Name == name).ExecuteAsync(
                    field == null ? "{}" : new JObject { [field] = text }.ToString(), default);
                var capabilities = JObject.Parse(await Call("inspect_capabilities"));
                Require(capabilities["motion"] != null && capabilities["pose"] != null, "Capabilities were not discoverable.");
                await Call("apply_photo_object", "recipe", Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text);
                await Call("configure_actor", "config", Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
                await Frames(8);
                var mud = clay.PresentationRoot.GetComponent<RuntimeMudRenderer>();
                var snapshot = root.AddComponent<SpatialMeshSnapshot>();
                snapshot.Read = () => BoundsOf(mud);
                var restBounds = await snapshot.Take();
                var restEar = clay.FindPart("ear_brush").localPosition;
                await Call("apply_script", "code", "struct HoverParameters(height: f32, sway: f32)\nMotionTuning { HoverParameters: {1.8,0.25} }");
                Require(world.Session.Query("HoverParameters").Contains("MotionTuning"), "New runtime component is not queryable ECS data.");
                await Call("configure_actor", "config", "{\"driver\":\"script\",\"motion\":\"spatial\",\"gravityScale\":0}");
                IsolateMotor();
                var code = Resources.Load<TextAsset>("Runtime/Behaviors/RuntimeSpatialBehavior").text;
                await Behavior(code);
                for (int i = 0; i < 200; i++)
                {
                    engine.StepMotor(1f / 60);
                    await Frames(1);
                }
                Require(engine.Motor.transform.position.y > 1.55f, "Runtime feedback rule did not lift the actual collider.");
                var state = JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"];
                Require(Mathf.Abs((float)state["ActorMotion"]["y"] - engine.Motor.transform.position.y) < .002f, "Motor readback is not actual movement.");
                Require(Mathf.Abs((float)state["PoseObserved"]["y"] - clay.PresentationRoot.position.y) < .002f, "Visual root readback is not the actual scene transform.");
                report["hoverHeight"] = engine.Motor.transform.position.y;
                checks.Add("new reflected control type and feedback rule installed during Play lift the real collision motor; actual motor and visual poses match ECS results");
                string earPath = clay.RuntimeNodes.First(n => n.Value == clay.FindPart("ear_brush")).Key;
                var earObserved = JObject.Parse(world.Session.EntityJson(earPath))["components"]["PoseObserved"];
                Require((bool)earObserved["offsetApplied"] && (float)earObserved["lx"] > restEar.x + .65f, "Live pose command did not reach the visible brush.");
                // Freeze motor stepping and compare geometry with/without only the pose layer.
                // Bounds can be world-oriented: comparing one axis while the body turns is invalid.
                await Behavior(code.Substring(0, code.IndexOf("\nEarWiggle {", StringComparison.Ordinal)));
                await Frames(16);
                var plainBounds = await snapshot.Take();
                Require(!world.Session.Query("PoseOffset, (PoseTarget, *)").Contains("EarWiggle"), "Replaced behavior retained its old pose command entity.");
                await Behavior(code);
                await Frames(16);
                var deformedBounds = await snapshot.Take();
                report["meshBounds"] = new JObject { ["rest"] = restBounds.size.ToString("R"), ["unposed"] = plainBounds.size.ToString("R"), ["posed"] = deformedBounds.size.ToString("R") };
                Require(Vector3.Distance(deformedBounds.size, plainBounds.size) > .15f, "The live MudBun geometry did not change with the pose layer.");
                checks.Add("additive pose reaches a real brush after procedural animation and changes generated MudBun geometry");
                var observed = JObject.Parse(await Call("observe_over_time", "path", earPath));
                Require((bool)observed["presentation_available"] && (long)observed["presentation_frames_advanced"] > 0,
                    "Temporal tool verified native numbers without advancing actual presentation.");
                checks.Add("the same agent observation tool returns advancing actual scene pose frames");

                // New source changes the motion law live, using the same capabilities.
                ceiling = new GameObject("Spatial verification ceiling");
                ceiling.transform.position = new Vector3(engine.Motor.transform.position.x, 4.8f, engine.Motor.transform.position.z);
                ceiling.AddComponent<BoxCollider>().size = new Vector3(8, .2f, 8);
                Physics.SyncTransforms();
                await Call("apply_script", "code", "MotionTuning { HoverParameters: {3.6,0.25} }");
                await Behavior(code.Replace("settings.sway * sin(t)", "-0.35").Replace("25 * sin(t * 3)", "45 * sin(t * 5)"));
                float beforeX = engine.Motor.transform.position.x;
                for (int i = 0; i < 140; i++)
                {
                    engine.StepMotor(1f / 60);
                    await Frames(1);
                }
                state = JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"];
                Require(engine.Motor.transform.position.x < beforeX - .7f, "Hot replacement did not change the movement law.");
                Require(engine.Motor.transform.position.y < 3.1f && (float)state["SpatialIntent"]["y"] > .4f && Mathf.Abs((float)state["ActorMotion"]["vy"]) < .1f,
                    "A ceiling did not distinguish requested ascent from actual blocked movement.");
                report["ceilingHeight"] = engine.Motor.transform.position.y;
                checks.Add("runtime source replacement changes direction and pose frequency; collision readback reports blocked ascent rather than echoing the command");
                Object.DestroyImmediate(ceiling);
                ceiling = null;
                for (int i = 0; i < 150; i++)
                {
                    engine.StepMotor(1f / 60);
                    await Frames(1);
                }
                Require(engine.Motor.transform.position.y > 3.35f, "The same active rule did not resume ascent after removing the ceiling.");

                await Behavior("// Removed the motion and pose rules");
                await Call("configure_actor", "config", "{\"driver\":\"idle\",\"motion\":\"ground\",\"gravityScale\":1}");
                IsolateMotor();
                for (int i = 0; i < 150; i++)
                {
                    engine.StepMotor(1f / 60);
                    await Frames(1);
                }
                state = JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"];
                report["landedHeight"] = engine.Motor.transform.position.y;
                report["landedGrounded"] = (bool)state["ActorMotion"]["grounded"];
                Require(engine.Motor.transform.position.y < .1f && (bool)report["landedGrounded"], "The actual collider did not land with normal gravity.");
                earObserved = JObject.Parse(world.Session.EntityJson(earPath))["components"]["PoseObserved"];
                Require(!(bool)earObserved["offsetApplied"] && Mathf.Abs((float)earObserved["lx"] - restEar.x) < .002f,
                    "Removing a pose rule left an accumulated visual offset.");
                checks.Add("removing behavior-owned command entities restores additive pose and normal-gravity policy lands the same collider without rebuilding it");
                Require(errors.Count == 0, "Runtime exceptions: " + string.Join("; ", errors.Take(3)));
                report["passed"] = true;
                report["limits"] = new JArray("Windows Editor; supplied Script through actual tools; no live AI inference", "controlled 3D motion, not aerodynamics; one actor", "no mobile/IL2CPP or full experience persistence test");
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (ceiling)
                    Object.DestroyImmediate(ceiling);
                if (root)
                    Object.DestroyImmediate(root);
                Application.logMessageReceived -= capture;
                report["runtimeErrors"] = JArray.FromObject(errors);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification/runtime-spatial-checks.json")), report.ToString());
                Running = false;
                if (owned)
                    EditorApplication.isPlaying = false;
            }
        }
        private static Bounds BoundsOf(RuntimeMudRenderer mud)
        {
            var kind = typeof(MudBun.MudRendererBase).GetNestedType("GeneratedMeshType");
            var method = typeof(MudBun.MudRendererBase).GetMethod("GenerateMesh", new[] { kind, typeof(bool), typeof(Mesh), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            var mesh = (Mesh)method.Invoke(mud, new object[] { Enum.Parse(kind, "Standard"), false, null, false, false, false, false });
            try
            {
                Require(mesh && mesh.vertexCount > 0, "Runtime MudBun mesh readback was empty.");
                return mesh.bounds;
            }
            finally { if (mesh) Object.DestroyImmediate(mesh); }
        }
    }
}
