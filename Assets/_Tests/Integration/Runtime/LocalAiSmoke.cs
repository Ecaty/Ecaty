using System;
using System.IO;
using System.Linq;
using System.Threading;
using Runtime.Agents;
using Runtime.Agents.Providers.Local;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    // Explicit development-player check. Never runs during normal play or records a microphone.
    public sealed class LocalAiSmoke : MonoBehaviour
    {
        LocalAiServices services; FlecsWorld world; string reportPath; CancellationTokenSource lifetime;
        public void Initialize(LocalAiServices services, FlecsWorld world, string reportPath)
        {
            this.services = services;
            this.world = world;
            this.reportPath = reportPath;
        }
        async void Start()
        {
            var report = new JObject { ["passed"] = false, ["checkedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["platform"] = Application.platform.ToString() };
            lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            try
            {
                using var model = new LlamaCppProvider(services.LlamaEndpoint, services.StartLlamaAsync, services.StopLlama);
                await model.SignInAsync(lifetime.Token);
                var tools = WorldAgentTools.Create(world, new AgentMainThread(), world.GetComponent<ClayCreation>(), world.GetComponent<DioramaRuntime>());
                var timer = System.Diagnostics.Stopwatch.StartNew();
                string reply = await model.ReplyAsync(new AgentRequest(Resources.Load<TextAsset>("Agent/Prompts/AgentInstructions").text, new[] { new AgentMessage(AgentRole.User, "Inspect the world. Make the ground blue. Verify the result. Reply briefly.") }, tools), null, lifetime.Token);
                world.SyncViews();
                var target = world.FindView(world.Session.Lookup("diorama.Ground"));
                if (!target)
                    throw new InvalidOperationException("Ground view missing.");
                var block = new MaterialPropertyBlock();
                target.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                var color = block.GetColor("_BaseColor");
                if (color.b < .8f || color.r > .2f)
                    throw new InvalidOperationException("Local AI did not make the actual rendered ground blue.");
                if (!world.Session.Query("Presented").Contains("frame"))
                    throw new InvalidOperationException("Presented readback missing.");
                report["passed"] = true;
                report["seconds"] = timer.Elapsed.TotalSeconds;
                report["model"] = model.Model;
                report["replyReceived"] = !string.IsNullOrWhiteSpace(reply);
                report["checks"] = new JArray("Windows player starts local model", "real engine tool loop updates ground rendering and native readback", "no cloud service used");
            }
            catch (Exception e) { report["error"] = e is AgentServiceException ? e.Message : e.GetType().Name + ": " + e.Message; }
            finally { File.WriteAllText(reportPath, report.ToString()); services.Dispose(); Application.Quit((bool)report["passed"] ? 0 : 1); }
        }
        void OnDestroy()
        {
            lifetime?.Cancel();
            lifetime?.Dispose();
        }
    }
}
