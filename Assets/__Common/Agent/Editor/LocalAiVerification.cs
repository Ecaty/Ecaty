using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Agents.Providers.Local;
using Ecaty.Agents.Speech;
using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ecaty.Editor {
    public static class LocalAiVerification {
        public static bool Running { get; private set; }
        [MenuItem("Runtime/Verification/Verify local AI and speech")]
        public static async void Start() {
            if (Running) return; Running = true;
            var report = new JObject { ["passed"] = false, ["checkedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["transport"] = "real loopback llama.cpp and whisper.cpp; real native Flecs and Unity views" };
            var checks = new JArray(); GameObject root = null; var counts=new Dictionary<string,int>();
            using var services = new LocalAiServices(Path.GetFullPath("LocalData/AI"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            try {
                var sw = Stopwatch.StartNew();
                using var speech = new WhisperSpeechToText(services.WhisperEndpoint, services.StartWhisperAsync);
                string spoken = await speech.TranscribeAsync(File.ReadAllBytes("Logs/LocalAiFixture/jfk.wav"), "audio/wav", timeout.Token);
                Require(spoken.ToLowerInvariant().Contains("country"), "Official speech sample was not recognized.");
                report["speechSeconds"] = sw.Elapsed.TotalSeconds; checks.Add("multilingual Whisper base: official JFK WAV transcribed locally; no microphone opened");
                using var provider = new LlamaCppProvider(services.LlamaEndpoint, services.StartLlamaAsync, services.StopLlama);
                sw.Restart(); await provider.SignInAsync(timeout.Token); report["loadSeconds"] = sw.Elapsed.TotalSeconds;
                report["model"] = provider.Model; report["vision"] = provider.SupportsVision;
                Require(provider.SupportsVision, "Loaded model/projector did not expose vision support.");
                var texture = new Texture2D(32,32,TextureFormat.RGB24,false);texture.SetPixels(Enumerable.Repeat(UnityEngine.Color.red,1024).ToArray());texture.Apply();var jpeg=texture.EncodeToJPG();UnityEngine.Object.DestroyImmediate(texture);
                sw.Restart(); var color=await provider.ReplyAsync(new AgentRequest("Describe the image in one or two words.",new[]{new AgentMessage(AgentRole.User,"What is the dominant color?")},Array.Empty<IAgentTool>(),jpeg),null,timeout.Token);
                report["visionSeconds"]=sw.Elapsed.TotalSeconds;Require(color.ToLowerInvariant().Contains("red"),"Local vision did not identify the red test image.");checks.Add("actual JPEG through CPU vision projector");
                root = new GameObject("Local AI verification"); var world=root.AddComponent<FlecsWorld>();world.Initialize(0,Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));world.SyncViews();
                var clay=root.AddComponent<ClayCreation>();var engine=root.AddComponent<DioramaRuntime>();engine.Initialize(world,clay);var experience=root.AddComponent<RuntimeExperience>();experience.Initialize(world,clay,engine);var tools=EcatyAgentTools.Create(world,new AgentMainThread(),clay,engine).Select(t=>(IAgentTool)new CountTool(t,counts)).ToArray();
                sw.Restart(); var response=await provider.ReplyAsync(new AgentRequest(Resources.Load<TextAsset>("Agent/Prompts/AgentInstructions").text,new[]{new AgentMessage(AgentRole.User,"Inspect the current world. Make the cube blue and twice its current size. Leave the sphere unchanged. Verify your change. Keep your final reply short.")},tools),null,timeout.Token);
                report["engineSeconds"]=sw.Elapsed.TotalSeconds;report["tools"]=JObject.FromObject(counts);report["replyReceived"]=!string.IsNullOrWhiteSpace(response);
                world.SyncViews();world.Session.ReadViews(out var views);var cube=views.Take(world.ViewCount).Single(v=>world.Session.EntityName(v.Entity)=="Cube");var sphere=views.Take(world.ViewCount).Single(v=>world.Session.EntityName(v.Entity)=="Sphere");var actual=world.FindView(cube.Entity);var block=new MaterialPropertyBlock();actual.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                Require(Vector3.Distance(actual.transform.localScale,Vector3.one*2)<.001f,"Actual Unity cube scale was not doubled.");Require(block.GetColor("_BaseColor").b>.9f&&block.GetColor("_BaseColor").r<.15f,"Actual Unity cube was not blue.");
                Require(Vector3.Distance(world.FindView(sphere.Entity).transform.localScale,Vector3.one)<.001f,"Sphere scale changed.");var sb=new MaterialPropertyBlock();world.FindView(sphere.Entity).GetComponentInChildren<Renderer>().GetPropertyBlock(sb);Require(sb.GetColor("_BaseColor").r>.9f&&sb.GetColor("_BaseColor").g>.5f&&sb.GetColor("_BaseColor").b<.2f,"Sphere color changed.");
                Require(counts.ContainsKey("query")&&counts.ContainsKey("apply_script"),"Agent did not inspect and apply Flecs Script.");Require(world.Session.Query("Presented").Contains("scale"),"Native presentation readback missing.");checks.Add("actual local tool loop: query -> Flecs Script -> Unity color/scale -> native readback; sphere preserved");report["passed"]=true;
            } catch(Exception e) {report["errorType"]=e.GetType().Name;report["error"]=e.Message;if(e is AgentServiceException service)report["diagnostic"]=service.Diagnostic;}
            finally {if(root)UnityEngine.Object.DestroyImmediate(root);report["checks"]=checks;report["tools"]=JObject.FromObject(counts);Directory.CreateDirectory("Verification");File.WriteAllText("Verification/local-ai-live-checks.json",report.ToString());Running=false;}
        }
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        sealed class CountTool:IAgentTool {
            readonly IAgentTool tool;readonly Dictionary<string,int> counts;public CountTool(IAgentTool tool,Dictionary<string,int> counts){this.tool=tool;this.counts=counts;}
            public string Name=>tool.Name;public string Description=>tool.Description;public string InputJsonSchema=>tool.InputJsonSchema;
            public async Task<string> ExecuteAsync(string args,CancellationToken token){counts[Name]=counts.TryGetValue(Name,out var n)?n+1:1;var result=await tool.ExecuteAsync(args,token);Directory.CreateDirectory("Logs/LocalAiFixture");File.AppendAllText("Logs/LocalAiFixture/tools.jsonl",new JObject{["tool"]=Name,["arguments"]=args,["result"]=result}.ToString(Newtonsoft.Json.Formatting.None)+"\n");return result;}
        }
    }
}
