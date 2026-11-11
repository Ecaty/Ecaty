using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ecaty.Editor
{
    public static class RuntimeBehaviorVerification
    {
        private static bool running;
        [Serializable]
        private sealed class Report
        {
            public bool passed;
            public string flecs, error;
            public List<string> checks = new List<string>();
            public string[] limits = { "Isolated real Unity objects; no live AI service call", "No phone or IL2CPP verification", "Behavior source reload only; full session saving is not implemented" };
        }
        private static void Require(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        private static FlecsWorld Current => UnityEngine.Object.FindAnyObjectByType<FlecsWorld>();
        private static string SavedPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/Experiences/AgentBehavior.flecs"));

        [MenuItem("Runtime/Runtime Behavior/Start breathing demo")]
        public static void StartDemo()
        {
            var world = Current;
            world.Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, Resources.Load<TextAsset>("Runtime/Behaviors/ReactiveBehavior").text);
            world.SyncViews();
        }
        [MenuItem("Runtime/Runtime Behavior/Stop behavior")]
        public static void StopDemo()
        {
            Current.Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, "// stopped\n");
            Current.SyncViews();
        }
        [MenuItem("Runtime/Runtime Behavior/Save behavior source")]
        public static void SaveSource()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavedPath));
            File.WriteAllText(SavedPath, Current.Session.ManagedScriptSource(EcatyAgentTools.BehaviorEntity) ?? "");
        }
        [MenuItem("Runtime/Runtime Behavior/Load saved behavior source")]
        public static void LoadSource()
        {
            Current.Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, File.ReadAllText(SavedPath));
            Current.SyncViews();
        }
        [MenuItem("Runtime/Runtime Behavior/Start breathing demo", true)]
        [MenuItem("Runtime/Runtime Behavior/Stop behavior", true)]
        [MenuItem("Runtime/Runtime Behavior/Save behavior source", true)]
        private static bool Active() => Application.isPlaying && Current && Current.Session != null && !Current.Session.IsDisposed;
        [MenuItem("Runtime/Runtime Behavior/Load saved behavior source", true)]
        private static bool CanLoad() => Active() && File.Exists(SavedPath);

        [MenuItem("Runtime/Runtime Behavior/Verify integration")]
        public static async void Start()
        {
            if (running)
                return;
            running = true;
            var report = new Report { flecs = FlecsSession.Version };
            GameObject root = null;
            FlecsWorld world = null;
            EditorApplication.CallbackFunction advance = null;
            string saved = Path.Combine(Path.GetTempPath(), "EcatyBehavior-" + Guid.NewGuid().ToString("N") + ".flecs");
            try
            {
                root = new GameObject("Reactive behavior verification");
                root.SetActive(false); // Never take an active world's port or change the open scene.
                world = root.AddComponent<FlecsWorld>();
                world.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));
                world.Session.ReadViews(out var initial);
                ulong id = initial.First(v => world.Session.EntityName(v.Entity) == "Sphere").Entity;
                var view = world.FindView(id);
                var before = view.transform.localScale;
                var tools = EcatyAgentTools.Create(world, new Ecaty.Agents.AgentMainThread());
                var apply = tools.First(t => t.Name == "apply_behavior");
                var inspect = tools.First(t => t.Name == "inspect_behavior");
                var observe = tools.First(t => t.Name == "observe_over_time");
                string code = Resources.Load<TextAsset>("Runtime/Behaviors/ReactiveBehavior").text;
                await apply.ExecuteAsync(JsonConvert.SerializeObject(new
                {
                    code
                }), CancellationToken.None);
                world.Session.Tick(.25f);
                world.SyncViews();
                Require(Vector3.Distance(before, view.transform.localScale) > .01f, "Reactive Scale did not reach the Unity object");
                var native = JObject.Parse(world.Session.EntityJson("scene.Sphere"))["components"];
                Require(Math.Abs((float)native["Scale"]["x"] - (float)native["Presented"]["scale"]["x"]) < .0001f, "Presented does not acknowledge actual reactive scale");
                report.checks.Add("apply_behavior tool -> native reactive Script -> actual Unity transform -> Presented readback");

                advance = () => { world.Session.Tick(.016f); world.SyncViews(); };
                EditorApplication.update += advance;
                var observed = JObject.Parse(await observe.ExecuteAsync("{\"path\":\"scene.Sphere\"}", CancellationToken.None));
                EditorApplication.update -= advance;
                advance = null;
                Require((long)observed["frames_advanced"] > 0 && ((JArray)observed["samples"]).Count == 3, "Temporal tool did not observe advancing frames");
                float first = (float)observed["samples"][0]["entity"]["components"]["Presented"]["scale"]["x"];
                float last = (float)observed["samples"][2]["entity"]["components"]["Presented"]["scale"]["x"];
                Require(Math.Abs(first - last) > .0001f, "Temporal readback did not observe changing scale");
                report.checks.Add("observe_over_time samples actual changing presentation across advancing native frames");

                string source = (string)JObject.Parse(await inspect.ExecuteAsync("{}", CancellationToken.None))["code"];
                Require(source == code, "Tool source inspection differs from the active native module");
                File.WriteAllText(saved, source);
                string positionCode = "using flecs.script.math\nconst t = Clock[Clock].time\nscene { Sphere { Position: {1.5, sin(t * 3), 0} } }\n";
                await apply.ExecuteAsync(JsonConvert.SerializeObject(new
                {
                    code = positionCode
                }), CancellationToken.None);
                world.Session.Tick(.25f);
                world.SyncViews();
                Require(world.FindView(id) == view && Math.Abs(view.transform.localPosition.y) > .001f, "Hot replacement did not preserve the existing view or apply new logic");
                report.checks.Add("Different Script algorithm hot-reloads while preserving the entity and Unity view");

                world.Shutdown();
                world.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));
                world.Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, File.ReadAllText(saved));
                world.Session.Tick(.25f);
                world.SyncViews();
                world.Session.ReadViews(out var restored);
                int count = world.ViewCount;
                ulong restoredId = restored.Take(count).First(v => world.Session.EntityName(v.Entity) == "Sphere").Entity;
                Require(world.FindView(restoredId).transform.localScale.x > 1.1f, "Saved source did not resume in a new session");
                report.checks.Add("UTF-8 behavior source restores into a new native session without regeneration");
                report.passed = true;
            }
            catch (Exception ex) { report.error = ex.ToString(); Debug.LogException(ex); }
            finally
            {
                if (advance != null)
                    EditorApplication.update -= advance;
                if (world)
                    world.Shutdown();
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                if (File.Exists(saved))
                    File.Delete(saved);
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification/runtime-behavior-unity-checks.json"));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                running = false;
                Debug.Log("ECATY_RUNTIME_BEHAVIOR=" + (report.passed ? "PASS" : "FAIL"));
            }
        }
    }
}
