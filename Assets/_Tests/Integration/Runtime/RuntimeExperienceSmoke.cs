using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty
{
    // Explicitly requested verification mode only; never runs in normal sessions.
    public sealed class RuntimeExperienceSmoke : MonoBehaviour
    {
        private static async Task Frames(int count)
        {
            int first = Time.frameCount;
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (Time.frameCount < first + count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Player frames did not advance.");
                await Task.Yield();
            }
        }
        private static void Require(bool value, string error)
        {
            if (!value)
                throw new InvalidOperationException(error);
        }
        private async void Start()
        {
            var checks = new JArray();
            var report = new JObject
            {
                ["passed"] = false,
                ["testedUtc"] = DateTime.UtcNow.ToString("O"),
                ["unity"] = Application.unityVersion,
                ["flecs"] = Ecaty.Flecs.FlecsSession.Version,
                ["graphics"] = SystemInfo.graphicsDeviceType.ToString(),
                ["checks"] = checks
            };
            try
            {
                await Frames(3);
                var experience = GetComponent<RuntimeExperience>();
                var world = GetComponent<Ecaty.Flecs.FlecsWorld>();
                Require(experience && ClayCreation.Available && SystemInfo.supportsComputeShaders, "Runtime experience or graphics capabilities missing.");
                var config = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
                config["driver"] = "idle";
                foreach (string id in new[] { "SmokeA", "SmokeB" })
                {
                    experience.CreateModel(id, Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text);
                    config["spawn"] = new JArray(id == "SmokeA" ? -2 : 2, 0, 0);
                    experience.Configure(id, config.ToString(Formatting.None));
                    experience.Models[id].Actor.ReadDevices = false;
                }
                await Frames(12);
                foreach (var pair in experience.Models.Where(p => p.Key.StartsWith("Smoke", StringComparison.Ordinal)))
                {
                    Require(pair.Value.Creation.PresentationRoot && pair.Value.Actor.Motor && pair.Value.Actor.Legs && pair.Value.Actor.Secondary,
                        "Model/rig/motor missing in player: " + pair.Key);
                }
                checks.Add("Two independent MudBun models with motors, FImpossible rigs and Boing chains created in the built player");
                var model = experience.Models["SmokeA"];
                string target = model.Creation.RuntimeNodes.Single(p => p.Value == model.Creation.FindPart("body")).Key;
                experience.ApplyBehavior("tap", "TogglePose smoke_toggle(listener:SmokeA,target:" + target + ",degrees:30) { Persistent }");
                await Frames(3);
                experience.SendPointer(world.Session.Lookup("SmokeA"), Vector2.zero, 1);
                experience.SendPointer(world.Session.Lookup("SmokeA"), Vector2.zero, 0);
                await Frames(4);
                bool Active() => (bool)JObject.Parse(world.Session.EntityJson("smoke_toggle"))["components"]["TogglePose.mut"]["active"];
                Require(Active(), "Native async input did not change player state.");
                var pose = JObject.Parse(world.Session.EntityJson(target))["components"]["PoseObserved"];
                Require(pose != null && (bool)pose["offsetApplied"] && (long)pose["frame"] > 0, "Actual player pose readback missing.");
                checks.Add("Native async input -> runtime behavior -> actual post-animation pose feedback");
                string saved = experience.Capture();
                Require(Active(), "Capture modified running state.");
                experience.RemoveBehavior("tap");
                await Frames(2);
                experience.Load(saved);
                await Frames(10);
                Require(Active() && experience.Models["SmokeB"].Actor.Motor, "Built-player save/reload lost state or peer model.");
                experience.SendPointer(world.Session.Lookup("SmokeA"), Vector2.zero, 1);
                experience.SendPointer(world.Session.Lookup("SmokeA"), Vector2.zero, 0);
                await Frames(3);
                Require(!Active(), "Restored behavior did not continue accepting input.");
                checks.Add("Versioned source/state save/reload recreates models and continues independent runtime behavior without AI regeneration");
                report["bridge"] = JObject.Parse(world.Session.BridgeDiagnostics());
                report["frames"] = Time.frameCount;
                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); }
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-runtimeEngineSmokeResult");
            if (index >= 0 && index + 1 < args.Length)
                File.WriteAllText(args[index + 1], report.ToString());
            Debug.Log("ECATY_RUNTIME_ENGINE_SMOKE=" + ((bool)report["passed"] ? "PASS" : "FAIL"));
            Application.Quit((bool)report["passed"] ? 0 : 1);
        }
    }
}
