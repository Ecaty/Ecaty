using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Runtime.Flecs;
using Runtime.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    // Only attached for an explicit development-player command-line request.
    public sealed class CapabilitySmoke : MonoBehaviour
    {
        static void Require(bool value, string error)
        {
            if (!value)
                throw new InvalidOperationException(error);
        }
        static async Task Until(Func<bool> condition)
        {
            var end = DateTime.UtcNow.AddSeconds(25);
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                    throw new TimeoutException("Player capability condition did not complete");
                await Task.Yield();
            }
        }
        async void Start()
        {
            var checks = new JArray();
            var report = new JObject { ["passed"] = false, ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["unity"] = Application.unityVersion, ["flecs"] = FlecsSession.Version, ["checks"] = checks };
            var errors = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
            Application.logMessageReceived += capture;
            try
            {
                Application.runInBackground = true;
                var world = GetComponent<FlecsWorld>();
                var runtime = GetComponent<CapabilityRuntime>();
                var experience = GetComponent<RuntimeExperience>();
                world.Session.SetManagedScript("CapabilityWorkshop", Resources.Load<TextAsset>("Runtime/Examples/Capabilities/CapabilityWorkshop").text);
                world.SyncViews();
                runtime.Reconcile();
                JObject Read(string entity, string type)
                {
                    string text = world.Session.ComponentJson(world.Session.Lookup(entity), type);
                    return string.IsNullOrEmpty(text) ? null : JObject.Parse(text);
                }
                var names = new[] { "BodyBinding", "MaterialBinding", "AudioBinding", "ParticleBinding", "LightBinding", "RoadBinding", "TerrainBinding", "NavSurfaceBinding", "NavigationBinding", "HandleBinding", "FeedbackBinding", "SequenceBinding", "BuildingBinding", "VfxBinding", "CameraBinding", "TimelineBinding", "TreeBinding", "LayoutBinding", "RigBinding", "WaterBinding", "FloatBodyBinding", "BuoyancyBinding", "AreaBinding" };
                await Until(() => { foreach (var name in names) { var state = Read(name, "CapState"); if (state?.I("phase") == -1) throw new InvalidOperationException(name + ": " + state); } return names.All(name => Read(name, "CapState")?.I("phase") == 1); });
                await Until(() => Read("BodyBinding", "CapReadback")?.F("y") < 2.8f && Read("AudioBinding", "CapAudioMeter")?.F("samples") > 0 && Read("RigBinding", "CapRigResult")?.F("error") < .2f);
                Require(world.FindView(world.Session.Lookup("CapArea")).GetComponentsInChildren<MeshFilter>().Any(f => f.sharedMesh && f.sharedMesh.vertexCount > 0), "Filled area has no actual mesh");
                Require(world.FindView(world.Session.Lookup("CapRoad")).GetComponentsInChildren<MeshFilter>().Any(f => f.sharedMesh && f.sharedMesh.vertexCount > 0), "Road has no actual mesh");
                await Until(() => Read("NavigationBinding", "CapPath")?.I("status") > 0);
                var camera = runtime.ViewCamera;
                var target = world.FindView(world.Session.Lookup("CapBall")).transform;
                await Until(() => Vector3.Dot(camera.transform.forward, (target.position - camera.transform.position).normalized) > .9f);
                checks.Add("Packaged Script creates 23 bindings; real physics, IK, navigation, road mesh and native audio callbacks run");
                var config = Read("BodyBinding", "CapBody");
                config["kinematic"] = true;
                world.Session.SetComponentJson("BodyBinding", "CapBody", config.ToString());
                runtime.Reconcile();
                var body = world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>();
                body.position = new Vector3(1, 2, 1);
                var road = Read("RoadBinding", "CapSpline");
                road["width"] = 1.2f;
                world.Session.SetComponentJson("RoadBinding", "CapSpline", road.ToString());
                string saved = experience.Capture();
                experience.Load(saved);
                await Until(() => { foreach (var name in names) { var state = Read(name, "CapState"); if (state?.I("phase") == -1) throw new InvalidOperationException(name + ": " + state); } return names.All(name => Read(name, "CapState")?.I("phase") == 1); });
                var restored = world.FindView(world.Session.Lookup("CapBall")).GetComponent<Rigidbody>();
                Require(restored && Mathf.Abs(restored.position.y - 2) < .1f, "Packaged body state did not restore");
                Require(Mathf.Abs(Read("RoadBinding", "CapSpline").F("width") - 1.2f) < .001f, "Live edited configuration did not restore");
                checks.Add("Packaged session rebuild restores sources, plugin instances, live edited configuration and body state");
                Require(errors.Count == 0, string.Join(" | ", errors.Distinct()));
                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); try { var world = GetComponent<FlecsWorld>(); report["states"] = JObject.Parse(world.Session.Query("CapBinding, ?CapState"))["results"]; report["availableTypes"] = JObject.Parse(world.Session.Query("CapType"))["results"]; } catch { } }
            Application.logMessageReceived -= capture;
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-capabilitySmokeResult");
            if (at >= 0 && at + 1 < args.Length)
                File.WriteAllText(args[at + 1], report.ToString());
            Application.Quit((bool)report["passed"] ? 0 : 1);
        }
    }
}
