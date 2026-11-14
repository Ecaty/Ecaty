using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Game.Editor
{
    public static class RuntimeExperienceVerification
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
            int first = Time.frameCount;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (Time.frameCount < first + count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Runtime stopped advancing.");
                await Task.Yield();
            }
        }
        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Game.ExperienceVerification", false))
                Run();
        };
        [MenuItem("Runtime/Runtime engine/Verify runtime experience")]
        public static void Start()
        {
            if (Running)
                return;
            if (EditorApplication.isPlaying)
                Run();
            else
            {
                SessionState.SetBool("Game.ExperienceVerification", true);
                EditorApplication.isPlaying = true;
            }
        }
        private static async void Run()
        {
            if (Running)
                return;
            Running = true;
            bool owned = SessionState.GetBool("Game.ExperienceVerification", false);
            SessionState.EraseBool("Game.ExperienceVerification");
            var checks = new JArray();
            var errors = new List<string>();
            var report = new JObject { ["passed"] = false, ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["checks"] = checks };
            GameObject root = null;
            Keyboard keyboard = null;
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += capture;
            try
            {
                Application.runInBackground = true;
                using var inputFocus = InputFocusScope.Begin();
                await Frames(3);
                var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                root = world.gameObject;
                root.name = "Runtime experience verification";
                var clay = root.AddComponent<ClayCreation>();
                clay.SaveAcceptedRecipes = false;
                var actor = root.AddComponent<DioramaRuntime>();
                actor.Initialize(world, clay);
                actor.ReadDevices = false;
                var experience = root.AddComponent<RuntimeExperience>();
                experience.Initialize(world, clay, actor);
                var tools = WorldAgentTools.Create(world, new AgentMainThread(), clay, actor);
                async Task<string> Call(string name, string field = null, string value = null)
                {
                    var args = new JObject();
                    if (field != null)
                        args[field] = value;
                    return await tools.Single(t => t.Name == name).ExecuteAsync(args.ToString(Formatting.None), CancellationToken.None);
                }
                var recipe = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text);
                var config = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
                config["driver"] = "idle";
                foreach (string id in new[] { "Alpha", "Beta" })
                {
                    config["spawn"] = new JArray(id == "Alpha" ? -2 : 2, 0, 0);
                    await Call("create_model", "creation", new JObject { ["id"] = id, ["recipe"] = recipe.DeepClone(), ["config"] = config.DeepClone() }.ToString(Formatting.None));
                    experience.Models[id].Actor.ReadDevices = false;
                }
                await Frames(12);
                var alpha = experience.Models["Alpha"];
                var beta = experience.Models["Beta"];
                Require(alpha.Actor.Motor && beta.Actor.Motor && alpha.Actor.Motor != beta.Actor.Motor, "Independent motors were not bound.");
                Require(alpha.Actor.Legs && beta.Actor.Legs && alpha.Actor.Secondary && beta.Actor.Secondary, "Procedural rigs did not bind independently.");
                Require(alpha.Creation.RuntimeNodes.Keys.All(p => p.StartsWith("Alpha")) && beta.Creation.RuntimeNodes.Keys.All(p => p.StartsWith("Beta")), "Model hierarchy ownership is mixed.");
                checks.Add("Real chat tools create two independent MudBun models, motors, FImpossible rigs and Boing chains during Play Mode");
                var cap = JObject.Parse(await Call("inspect_capabilities"));
                Require(cap["results"].Any(r => ((string)r["name"]) == "Input"), "Capabilities are not queryable ECS data.");
                string PathOf(RuntimeExperience.Model model, string id) => model.Creation.RuntimeNodes.Single(p => p.Value == model.Creation.FindPart(id)).Key;
                string alphaTarget = PathOf(alpha, "body"), betaTarget = PathOf(beta, "body");
                foreach (string id in new[] { "Alpha", "Beta" })
                {
                    string target = id == "Alpha" ? alphaTarget : betaTarget;
                    string code = "TogglePose toggle_" + id + "(listener:" + id + ",target:" + target + ",degrees:30) { Persistent }";
                    await Call("apply_runtime_behavior", "definition", new JObject { ["id"] = id.ToLowerInvariant(), ["code"] = code }.ToString(Formatting.None));
                }
                await Frames(3);
                Quaternion MeasuredRotation(string path)
                {
                    var pose = JObject.Parse(world.Session.EntityJson(path))["components"]["PoseObserved"];
                    return Quaternion.Euler((float)pose["rx"], (float)pose["ry"], (float)pose["rz"]);
                }
                var before = MeasuredRotation(alphaTarget);
                experience.SendPointer(world.Session.Lookup("Alpha"), Vector2.zero, 1);
                experience.SendPointer(world.Session.Lookup("Alpha"), Vector2.zero, 0);
                await Frames(4);
                bool Active(string name) => (bool)JObject.Parse(world.Session.EntityJson(name))["components"]["TogglePose.mut"]["active"];
                // The pointer event is asynchronous; give the native toggle time to settle before judging it.
                for (int wait = 0; wait < 60 && !Active("toggle_Alpha"); wait++)
                    await Frames(1);
                Require(Active("toggle_Alpha") && !Active("toggle_Beta"),
                    "Input leaked across behavior instances: alpha=" + Active("toggle_Alpha") + " beta=" + Active("toggle_Beta"));
                Require(Quaternion.Angle(before, MeasuredRotation(alphaTarget)) > 20, "Async state did not change the actual live part pose.");
                var observed = JObject.Parse(await Call("observe_over_time", "path", alphaTarget));
                Require((bool)observed["presentation_available"] && (long)observed["presentation_frames_advanced"] > 0, "Actual presentation feedback did not advance.");
                checks.Add("Unity event API -> native async template state -> PoseOffset -> actual MudBun part pose -> Flecs observation; peer state remains unchanged");
                string editSource = world.Session.ManagedScriptSource("Models.Alpha");
                world.Session.EditScript("Models.Alpha", alphaTarget, "ClayStyle", "{0,1,0,0,0,0.45,0,false,0.07}");
                await Frames(4);
                Require(alpha.Creation.FindPart("body").GetComponent<MudBun.MudMaterial>().Color.r > .9f, "Native material edit did not reach MudBun.");
                world.Session.UndoScript("Models.Alpha");
                await Frames(4);
                Require(world.Session.ManagedScriptSource("Models.Alpha") == editSource, "Source undo did not preserve the original definition.");
                checks.Add("Source-preserving edits and undo update the live MudBun material through ECS reconciliation");
                experience.ApplyBehavior("keyboard", "using flecs.script\nstruct KeySignal(value:bool)\ntemplate KeyListener { prop target:entity; mut hit=false; KeySignal:{hit}; async { while true { const event = await on.key_down(target); if event.key == \"ArrowLeft\" { hit=true } } } }\nKeyListener key_test(target:Alpha) { Persistent }");
                await Frames(2);
                // Establish focus after the source-edit/undo round trip; release off-target avoids activating the click rule.
                experience.SendPointer(world.Session.Lookup("Alpha"), Vector2.zero, 1);
                experience.SendPointer(0, Vector2.zero, 0);
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftArrow));
                await Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                await Frames(2);
                Require((bool)JObject.Parse(world.Session.EntityJson("key_test"))["components"]["KeySignal"]["value"], "Keyboard input was not normalized and forwarded to the native listener.");
                Require(world.Session.Lookup("EngineMetrics.models") != 0 && world.Session.Lookup("EngineAlerts.AdapterFault") != 0 &&
                    world.Session.Query("flecs.metrics.Value").Contains("EngineMetrics"), "Native metrics/alerts were not configured.");
                checks.Add("Actual Input System keyboard events reach native async listeners; Flecs metrics and adapter alerts are configured");
                world.Session.SetManagedScript("ExtraBrush", "Alpha { part_hips { part_torso { extra { ClayPart: {\"extra\",40}; ClayRest: {0,0.6,0,0,0,0,0.2,0.2,0.2}; ClayStyle: {0,1,1,0,0,0.4,0,false,0.07} } } } }");
                await Frames(4);
                Require(alpha.Creation.FindPart("extra"), "A runtime ECS entity did not create its brush.");
                world.Session.SetManagedScript("ExtraBrush", "// Removed");
                await Frames(4);
                Require(!alpha.Creation.FindPart("extra"), "Removing a runtime entity did not remove its brush.");
                checks.Add("Native Script creates and removes a brush without a recipe-tool round trip");
                string saved = experience.Capture();
                string[] appOwned = { "ClayBrushSchema", "PoseSchema", "EngineSchema", "RuntimeLibrary", "ActorSchema", "MotorLibrary", "CapabilitySchema" };
                var savedData = JObject.Parse(saved);
                Require((int)savedData["version"] == 3, "A capture is not version 3.");
                Require(!((JArray)savedData["sources"]).Any(s => appOwned.Contains((string)s["path"])), "A capture contains an app-owned module.");
                // Golden save: committed fixture, never written by this check. Later builds must keep loading it.
                string goldenPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tests/Fixtures/Saves/experience-v1.json"));
                Require(File.Exists(goldenPath), "The golden save is missing: " + goldenPath + ". Restore Tests/Fixtures/Saves/experience-v1.json from version control.");
                byte[] goldenBytes = File.ReadAllBytes(goldenPath);
                string savePath =Path.GetFullPath(Path.Combine(Application.dataPath, "../Native/build-flecs-audit/experience-test.json"));
                int revision = world.Session.SourceRevision;
                experience.Save(savePath);
                Require(File.Exists(savePath), "Local save was not written.");
                Require(Active("toggle_Alpha") && world.Session.SourceRevision == revision, "Saving changed the running behavior/source state.");
                var savedAlphaPosition = alpha.Actor.Motor.transform.position;
                experience.RemoveBehavior("alpha");
                await Frames(3);
                Require(world.Session.ManagedScriptSource("RuntimeBehaviors.beta") != null, "Removing one behavior removed its peer.");
                experience.Load(saved);
                await Frames(10);
                alpha = experience.Models["Alpha"];
                beta = experience.Models["Beta"];
                Require(Active("toggle_Alpha") && !Active("toggle_Beta"), "Saved independent mut state was not restored.");
                Require(Vector3.Distance(savedAlphaPosition, alpha.Actor.Motor.transform.position) < .15f, "Actor position did not resume.");
                experience.SendPointer(world.Session.Lookup("Alpha"), Vector2.zero, 1);
                experience.SendPointer(world.Session.Lookup("Alpha"), Vector2.zero, 0);
                await Frames(4);
                Require(!Active("toggle_Alpha"), "Restored behavior did not accept another event.");
                checks.Add("Independent source modules, models, controllers, motor position and template mut state save/reload without AI regeneration; events continue after reload");
                string timed = "struct RuntimeSignal(value:i32)\ntemplate RuntimeTimer { mut ready=false; if ready { RuntimeSignal:{1} } else { RuntimeSignal:{0} }; async { await engine.wait(0.05); ready=true } }\nRuntimeTimer timer_test() { Persistent }";
                experience.ApplyBehavior("timer", timed);
                await Frames(20);
                Require((int)JObject.Parse(world.Session.EntityJson("timer_test"))["components"]["RuntimeSignal"]["value"] == 1, "Native timed async sequence did not run.");
                var diagnostic = JObject.Parse(world.Session.BridgeDiagnostics());
                Require((long)diagnostic["binaryWrites"] > 0 && (long)diagnostic["binaryReads"] > 0 && (long)diagnostic["queryHits"] > 0, "Binary/cache paths were not exercised.");
                report["bridge"] = diagnostic;
                checks.Add("Native simulation-time async wait and generic binary command/readback paths run in the Editor");
                experience.RemoveModel("Alpha");
                await Frames(3);
                Require(beta.Creation.PresentationRoot && beta.Actor.Motor, "Removing one model damaged its peer.");
                checks.Add("Removing one model tears down its motor/presentation without removing the other model");
                var golden = JObject.Parse(File.ReadAllText(goldenPath));
                experience.Load(golden.ToString(Formatting.None));
                await Frames(10);
                Require(((JArray)golden["sources"]).All(s => world.Session.ManagedScriptSource((string)s["path"]) != null), "Golden save: a source module was not restored.");
                Require(((JArray)golden["actors"]).All(a => experience.Models.TryGetValue((string)a["entity"], out var model) && model.Creation.PresentationRoot), "Golden save: a model was not restored.");
                checks.Add("The committed version 1 experience save (Tests/Fixtures/Saves/experience-v1.json) still loads with every source and model");
                // App-owned modules are installed from Resources, not read from the file: put a stale text in a copy of the golden file.
                var stale = (JObject)golden.DeepClone();
                var staleActor = ((JArray)stale["sources"]).Single(s => (string)s["path"] == "ActorSchema");
                staleActor["code"] = "// stale text from an older build\n" + (string)staleActor["code"];
                experience.Load(stale.ToString(Formatting.None));
                await Frames(10);
                foreach (var pair in new[] { ("ActorSchema", "Runtime/Schemas/ActorSchema"), ("EngineSchema", "Runtime/Schemas/EngineSchema"), ("MotorLibrary", "Runtime/Behaviors/ActorBehavior") })
                    Require(world.Session.ManagedScriptSource(pair.Item1) == Resources.Load<TextAsset>(pair.Item2).text, "Loading an older save did not install the current text of " + pair.Item1 + ".");
                checks.Add("Loading a save installs the app's own schema and library modules from Resources, not from the file");
                Require((int)golden["version"] == 1 && File.ReadAllBytes(goldenPath).SequenceEqual(goldenBytes), "The golden save is not the unchanged version 1 file.");
                string upgradedText = experience.Capture();
                var upgraded = JObject.Parse(upgradedText);
                Require((int)upgraded["version"] == 3 && (string)upgraded["engine"] == FlecsSession.Version && (int)upgraded["abi"] == FlecsSession.AbiVersion, "A capture after loading version 1 is not version 3 with engine and abi.");
                Require(!((JArray)upgraded["sources"]).Any(s => appOwned.Contains((string)s["path"])), "A capture after loading version 1 contains an app-owned module.");
                (string[] Models, string[] Sources, string[] States) Shape(string text)
                {
                    var d = JObject.Parse(text);
                    return (((JArray)d["actors"]).Select(a => (string)a["entity"]).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        ((JArray)d["sources"]).Select(x => (string)x["path"] + "\n" + (string)x["code"]).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        ((JArray)d["states"]).Select(x => (string)x["entity"] + ":" + string.Join(",", ((JObject)x["components"]).Properties().Select(c => c.Name).OrderBy(c => c, StringComparer.Ordinal))).OrderBy(x => x, StringComparer.Ordinal).ToArray());
                }
                var shapeBefore = Shape(upgradedText);
                experience.Load(upgradedText);
                var shapeAfter = Shape(experience.Capture());
                Require(shapeBefore.Models.SequenceEqual(shapeAfter.Models) && shapeBefore.Sources.SequenceEqual(shapeAfter.Sources) && shapeBefore.States.SequenceEqual(shapeAfter.States),
                    "Saving and loading a version 3 capture changed its models, sources or states.");
                await Frames(10);
                checks.Add("A version 1 file loads unchanged on disk; the next capture is version 3 with engine and abi and no app-owned modules and reloads to the same models, sources and state components");
                var broken = JObject.Parse(experience.Capture());
                broken["controlled"] = "NoSuchModel";
                var modelsBefore = experience.Models.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                var pathsBefore = world.Session.ManagedPaths.Where(x => x != "RuntimeFaults").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string clayBefore = JObject.Parse(world.Session.EntityJson("Beta"))["components"]["ClayRest"].ToString(Formatting.None);
                string controlledBefore = experience.ControlledEntity;
                bool refused = false;
                try { experience.Load(broken.ToString(Formatting.None)); }
                catch (Exception) { refused = true; }
                Require(refused, "A load that fails after the world was replaced did not throw.");
                Require(modelsBefore.SequenceEqual(experience.Models.Keys.OrderBy(k => k, StringComparer.Ordinal)), "A failed load changed the bound models.");
                Require(pathsBefore.SequenceEqual(world.Session.ManagedPaths.Where(x => x != "RuntimeFaults").OrderBy(x => x, StringComparer.Ordinal)), "A failed load changed the managed sources.");
                Require(JObject.Parse(world.Session.EntityJson("Beta"))["components"]["ClayRest"].ToString(Formatting.None) == clayBefore && experience.ControlledEntity == controlledBefore, "A failed load changed a state value.");
                await Frames(3);
                checks.Add("A load that fails after the world was replaced throws and restores the previous models, sources and state");
                Require(!experience.Rewind(), "Rewind without a snapshot did not report it.");
                Require(world.Session.ManagedScriptSource("RuntimeBehaviors.beta") != null, "The beta behavior is missing before the snapshot.");
                experience.Snapshot();
                experience.RemoveBehavior("beta");
                await Frames(2);
                Require(world.Session.ManagedScriptSource("RuntimeBehaviors.beta") == null, "The behavior was not removed.");
                Require(experience.Rewind() && world.Session.ManagedScriptSource("RuntimeBehaviors.beta") != null, "Rewind did not return to the snapshot.");
                await Frames(3);
                checks.Add("Snapshot and rewind return the experience to its kept state");
                // Root experience tools outside a world; inside a world the AI gets the world tools (AgentToolsVerification).
                var toolSession = world.Session;
                int RuntimeEdits() => toolSession.ManagedPaths.Count(x => x.StartsWith("RuntimeEdits.", StringComparison.Ordinal));
                ulong lab = toolSession.CreateWorld("tool_check");
                try
                {
                    toolSession.EnterWorld(lab);
                    Require(JObject.Parse(await Call("save_experience"))["error"] != null, "An experience tool ran inside an entered world.");
                    toolSession.EnterWorld(0);
                    Require(JObject.Parse(await Call("query", "expression", "Position"))["results"].Count() > 1, "query outside a world is scoped.");
                    int outsideBefore = RuntimeEdits();
                    await Call("apply_script", "code", "outside_one { Position: {0,0,0} }");
                    Require(RuntimeEdits() == outsideBefore + 1 && toolSession.Lookup("outside_one") != 0, "apply_script outside a world changed its behavior.");
                }
                finally
                {
                    toolSession.EnterWorld(0);
                    toolSession.DeleteWorld(lab);
                }
                checks.Add("Experience tools refuse inside an entered world; query and apply_script behave as before outside one");
                Require(errors.Count == 0, "Runtime exceptions: " + string.Join("; ", errors.Take(3)));
                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (keyboard != null && keyboard.added)
                    InputSystem.RemoveDevice(keyboard);
                if (root)
                    Object.DestroyImmediate(root);
                Application.logMessageReceived -= capture;
                report["runtimeErrors"] = JArray.FromObject(errors);
                report["limits"] = new JArray("Windows Editor with supplied fixtures through real tools; no live AI inference or mobile/IL2CPP build", "Selected state persistence; pending awaits restart, not a VM/physics solver snapshot");
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification/runtime-experience-checks.json")), report.ToString());
                Running = false;
                if (owned)
                    EditorApplication.isPlaying = false;
            }
        }
    }
}
