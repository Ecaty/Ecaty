using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Agents.Diagnostics;
using Runtime.Agents.Providers.Gemini;
using Runtime.Agents.Providers.OpenRouter;
using Runtime.Agents.Security;
using Runtime.Agents.UI;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    // Only service replies/credentials are fixtures. Flecs, dispatch, Script and presented objects are real.
    public static class MultiProviderVerification
    {
        public static bool Running
        {
            get; private set;
        }
        private static void Require(bool value, string error)
        {
            if (!value)
                throw new Exception(error);
        }
        public static async void Start()
        {
            if (Running)
                return;
            Running = true;
            var report = new JObject { ["passed"] = false, ["transport"] = "synthetic services; real loopback HTTP, Flecs and Unity objects", ["unity"] = Application.unityVersion };
            var checks = new JArray();
            GameObject root = null;
            bool traceEnabled = AgentTrace.Enabled;
            try
            {
                AgentTrace.ClearView();
                AgentTrace.Configure(Path.GetFullPath("Logs/Agents/Verification"), true);
                root = new GameObject("Provider verification");
                var world = root.AddComponent<FlecsWorld>();
                world.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));
                var tools = WorldAgentTools.Create(world, new AgentMainThread());
                var request = new AgentRequest(Resources.Load<TextAsset>("Agent/Prompts/AgentInstructions").text,
                    new[] { new AgentMessage(AgentRole.User, "Make the cube blue and twice as big.") }, tools);
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var token = timeout.Token;
                    var routerStore = new MemoryStore();
                    using (var fixture = new Fixture(false))
                    using (var http = new HttpClient(fixture))
                    using (var provider = new OpenRouterProvider(routerStore, fixture.Browser, http))
                    {
                        await provider.SignInAsync(token);
                        await fixture.CallbackTask;
                        Require(provider.IsSignedIn && fixture.PkceChecked && fixture.WrongStateRejected, "OpenRouter callback/PKCE failed");
                        checks.Add("OpenRouter real loopback callback rejects wrong state; PKCE exchange validates key before saving");
                        var reply = await provider.ReplyAsync(request, null, token);
                        Require(reply == "Done." && fixture.Rounds == 5 && fixture.ToolReturns == 4 && fixture.SignatureReplayed, "OpenRouter tool loop failed");
                        CheckCube(world);
                        checks.Add("OpenRouter chat -> native query -> Script -> Unity color/scale -> Presented -> final chat");
                        using (var restored = new OpenRouterProvider(routerStore, fixture.Browser, http))
                            Require(restored.IsSignedIn, "Router credentials did not restore");
                        foreach (var failure in new[] { "quota", "invalid", "cut", "badjson", "params" })
                        {
                            fixture.Reset(failure);
                            bool rejected = false;
                            try
                            {
                                await provider.ReplyAsync(request, null, token);
                            }
                            catch (AgentServiceException e) { rejected = true; if (failure == "params") Require(e.Diagnostic.Contains("Unsupported request parameter") && !e.Diagnostic.Contains("sk-or-v1-"), "Router diagnostic leaked credential or lost server error"); }
                            Require(rejected && fixture.Rounds == 1, "Router terminal failure retried or succeeded: " + failure);
                        }
                        fixture.Reset("unknown");
                        Require(await provider.ReplyAsync(request, null, token) == "Done." && fixture.ErrorReturned, "Unknown router tool was not rejected");
                        fixture.Reset("badargs");
                        Require(await provider.ReplyAsync(request, null, token) == "Done." && fixture.ErrorReturned, "Malformed router arguments were not rejected");
                        await provider.SignOutAsync(token);
                        using (var restored = new OpenRouterProvider(routerStore, fixture.Browser, http))
                            Require(!restored.IsSignedIn, "Router disconnect persisted credentials");
                        checks.Add("OpenRouter restore/disconnect; 401/429, truncated/non-JSON replies, unknown tools and bad arguments handled; no retry or paid fallback");
                    }
                    bool paidRejected = false;
                    try
                    {
                        using (new OpenRouterProvider(new MemoryStore(), _ => { }, model: "paid/model"))
                        {
                        }
                    }
                    catch (ArgumentException) { paidRejected = true; }
                    Require(paidRejected, "Paid model accepted");
                    checks.Add("OpenRouter refuses paid model IDs");
                    using (var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
                    using (var provider = new OpenRouterProvider(new MemoryStore(), _ => { }))
                    {
                        bool stopped = false;
                        try
                        {
                            await provider.SignInAsync(cancelled.Token);
                        }
                        catch (OperationCanceledException) { stopped = true; }
                        Require(stopped && !provider.IsSignedIn, "Cancelled browser connection remained pending/signed in");
                    }
                    checks.Add("browser connection cancellation closes listener and does not save credentials");
                    world.Session.RunScript("scene { Cube { Color: {0.4, 0.4, 0.4, 1}; Scale: {1, 1, 1} } }");
                    world.SyncViews();
                    var geminiStore = new MemoryStore();
                    using (var fixture = new Fixture(true))
                    using (var http = new HttpClient(fixture))
                    using (var provider = new GeminiProvider(geminiStore, http))
                    {
                        await provider.ConnectKeyAsync(Fixture.GoogleKey, token);
                        Require(provider.IsSignedIn && fixture.KeyValidated, "Gemini key validation failed");
                        Require(await provider.ReplyAsync(request, null, token) == "Done." && fixture.Rounds == 5 && fixture.ToolReturns == 4 && fixture.SignatureReplayed, "Gemini loop failed");
                        CheckCube(world);
                        checks.Add("Gemini key validated using model metadata; native function calls/IDs and opaque signatures replayed; real world round trip");
                        using (var restored = new GeminiProvider(geminiStore, http))
                            Require(restored.IsSignedIn, "Gemini credentials did not restore");
                        foreach (var failure in new[] { "quota", "invalid", "cut", "badjson", "params" })
                        {
                            fixture.Reset(failure);
                            bool rejected = false;
                            try
                            {
                                await provider.ReplyAsync(request, null, token);
                            }
                            catch (AgentServiceException e) { rejected = true; if (failure == "params") Require(e.Diagnostic.Contains("Unsupported request parameter") && !e.Diagnostic.Contains(Fixture.GoogleKey), "Google diagnostic leaked key or lost server error"); }
                            Require(rejected && fixture.Rounds == 1, "Gemini terminal failure retried or succeeded: " + failure);
                        }
                        fixture.Reset("unknown");
                        Require(await provider.ReplyAsync(request, null, token) == "Done." && fixture.ErrorReturned, "Unknown Gemini tool accepted");
                        await provider.SignOutAsync(token);
                        using (var restored = new GeminiProvider(geminiStore, http))
                            Require(!restored.IsSignedIn, "Gemini disconnect persisted key");
                        checks.Add("Gemini restore/disconnect, account/quota errors and incomplete replies; no hidden thought text or signature in chat/trace");
                    }
                    await VerifyUI(token, checks);
                }
                AgentTrace.Record("provider-redaction", "tool-result", "redaction", "Bearer synthetic-secret", Fixture.GoogleKey + " sk-or-v1-123456789012345678901234567890");
                var trace = JsonConvert.SerializeObject(AgentTrace.Snapshot());
                Require(!trace.Contains(Fixture.GoogleKey) && !trace.Contains("synthetic-secret") && !trace.Contains("opaque-synthetic-signature") && !trace.Contains("private-synthetic-thought"), "Credential/thought leaked to trace");
                checks.Add("provider credentials, Google key pattern, opaque signatures and thought text excluded from trace");
                report["passed"] = true;
            }
            catch (Exception e) { report["error"] = e.ToString(); Debug.LogException(e); }
            finally
            {
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                AgentTrace.Enabled = traceEnabled;
                report["checks"] = checks;
                report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
                File.WriteAllText("Verification/provider-checks.json", report.ToString(Formatting.Indented));
                Running = false;
                Debug.Log("PROVIDERS_SMOKE=" + ((bool)report["passed"] ? "PASS" : "FAIL"));
            }
        }
        private static void CheckCube(FlecsWorld world)
        {
            world.Session.ReadViews(out var views);
            var cube = views.Take(world.ViewCount).Single(v => world.Session.EntityName(v.Entity) == "Cube");
            var actual = world.FindView(cube.Entity);
            Require(Vector3.Distance(actual.transform.localScale, Vector3.one * 2) < .001f, "Actual scale missing");
            var block = new MaterialPropertyBlock();
            actual.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
            Require(block.GetColor("_BaseColor").b > .99f && block.GetColor("_BaseColor").r < .01f, "Actual renderer color missing");
            Require(world.Session.Query("Presented").Contains("scale"), "World observations missing");
        }
        private static async Task VerifyUI(CancellationToken token, JArray checks)
        {
            var a = new EchoProvider("a");
            var b = new EchoProvider("b");
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.runtime.ai-agent/Runtime/UI/AgentChat.uxml");
            using (var hub = new AgentProviderHub(new[] {
                new AgentProviderRegistration("a", "Browser provider", () => a),
                new AgentProviderRegistration("b", "Key provider", () => b, "Google project controls billing.", "https://aistudio.google.com/api-keys")
            }))
            using (var view = new AgentChatView(layout))
            using (var controller = new AgentChatController(view, hub, hub, "", null))
            {
                view.SetAccountPanel(true);
                Require(view.AccountPanelOpen, "Account chooser did not open");
                Require(view.Root.Q<Button>("provider-a") != null && view.Root.Q<Button>("provider-b") != null, "Provider buttons missing");
                await controller.ConnectAsync();
                Require(hub.IsSignedIn && !view.AccountPanelOpen, "Connection did not close chooser");
                Require(await controller.SendAsync("message for provider a") == "a", "First provider not invoked");
                controller.SelectProvider("b");
                Require(view.Root.Q<ScrollView>("messages").childCount == 0, "Conversation not cleared on switch");
                Require(view.Root.Q<TextField>("apiKeyInput").isPasswordField, "Key input not masked");
                view.SetAccountPanel(true);
                await controller.ConnectAsync("synthetic-key");
                Require(await controller.SendAsync("message for provider b") == "b" && b.Last.Messages.Count == 1, "Conversation leaked across services");
                await controller.DisconnectAsync();
                Require(!hub.IsSignedIn, "UI disconnect failed");
                controller.SelectProvider("a");
                Require(hub.IsSignedIn, "Switch discarded another provider's connection");
            }
            checks.Add("account chooser, masked key form, connect/disconnect, retained independent accounts and cleared cross-provider conversation");
        }
        private sealed class EchoProvider : IAgentProvider, IAgentAuthenticator, IAgentKeyAuthenticator
        {
            public string Id
            {
                get;
            }
            public string DisplayName => Id; public bool IsSignedIn
            {
                get; private set;
            }
            public AgentRequest Last;
            public EchoProvider(string id)
            {
                Id = id;
            }
            public Task SignInAsync(CancellationToken token)
            {
                IsSignedIn = true;
                return Task.CompletedTask;
            }
            public Task ConnectKeyAsync(string key, CancellationToken token) => SignInAsync(token);
            public Task SignOutAsync(CancellationToken token)
            {
                IsSignedIn = false;
                return Task.CompletedTask;
            }
            public Task<string> ReplyAsync(AgentRequest request, IProgress<string> progress, CancellationToken token)
            {
                Last = request;
                return Task.FromResult(Id);
            }
        }
        private sealed class MemoryStore : ISecretStore
        {
            private byte[] bytes;
            public byte[] Load() => bytes == null ? null : (byte[])bytes.Clone();
            public void Save(byte[] data) => bytes = (byte[])data.Clone();
        }
        private sealed class Fixture : HttpMessageHandler
        {
            internal const string GoogleKey = "AIza12345678901234567890123456789012345";
            private readonly bool google;
            private string challenge;
            internal Task CallbackTask;
            internal bool PkceChecked, WrongStateRejected, KeyValidated, SignatureReplayed, ErrorReturned;
            internal int Rounds, ToolReturns;
            private string failure;
            internal Fixture(bool google)
            {
                this.google = google;
            }
            internal void Reset(string mode)
            {
                Rounds = 0;
                ToolReturns = 0;
                failure = mode;
                ErrorReturned = false;
            }
            internal void Browser(string url)
            {
                CallbackTask = Callback(url);
            }
            private async Task Callback(string url)
            {
                var fields = LoopbackSignIn.Query(new Uri(url).Query);
                challenge = fields["code_challenge"];
                Require(fields["code_challenge_method"] == "S256" && new Uri(fields["callback_url"]).Host == "127.0.0.1", "Unsafe callback or PKCE");
                using (var browser = new HttpClient())
                {
                    var wrong = await browser.GetAsync(fields["callback_url"] + "?code=synthetic-code&state=wrong");
                    WrongStateRejected = wrong.StatusCode == HttpStatusCode.BadRequest;
                    var good = await browser.GetAsync(fields["callback_url"] + "?code=synthetic-code&state=" + fields["state"]);
                    Require(good.IsSuccessStatusCode, "Valid callback rejected");
                }
            }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                var path = request.RequestUri.AbsolutePath;
                Require(request.RequestUri.Scheme == "https", "Service request was not HTTPS");
                if (path.EndsWith("auth/keys"))
                {
                    var body = JObject.Parse(await request.Content.ReadAsStringAsync());
                    Require((string)body["code"] == "synthetic-code" && (string)body["code_challenge_method"] == "S256" && LoopbackSignIn.Challenge((string)body["code_verifier"]) == challenge, "PKCE exchange invalid");
                    PkceChecked = true;
                    return Json(new JObject { ["key"] = "sk-or-v1-synthetic-12345678901234567890" });
                }
                if (google)
                    Require(request.Headers.GetValues("x-goog-api-key").Single() == GoogleKey && !request.RequestUri.Query.Contains(GoogleKey), "Google key not header-only");
                else
                    Require(request.Headers.Authorization?.Parameter == "sk-or-v1-synthetic-12345678901234567890", "Router authorization missing");
                if (request.Method == HttpMethod.Get)
                {
                    KeyValidated = true;
                    return Json(google ? new JObject { ["supportedGenerationMethods"] = new JArray("generateContent") } : new JObject { ["data"] = new JObject { ["label"] = "fixture" } });
                }
                int round = Rounds++;
                if (failure == "quota")
                    return new HttpResponseMessage((HttpStatusCode)429);
                if (failure == "invalid")
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (failure == "badjson")
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{cut") };
                if (failure == "params")
                    return new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(new JObject
                        {
                            ["error"] = new JObject { ["message"] = "Unsupported request parameter " + (google ? GoogleKey : request.Headers.Authorization.Parameter) }
                        }.ToString(Formatting.None))
                    };
                var payload = JObject.Parse(await request.Content.ReadAsStringAsync());
                if (!google && (payload["provider"]?["require_parameters"] != null || payload["parallel_tool_calls"] != null))
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"message\":\"No endpoints found that can handle the requested parameters\"}}") };
                bool special = failure == "unknown" || failure == "badargs";
                var steps = new[] { "inspect_entity", "query", "apply_script", "observe_world" };
                var arguments = new[] {
                    new JObject { ["path"] = "scene.Cube" },
                    new JObject { ["expression"] = "Position, ?Color, ?Scale, ?Presented" },
                    new JObject { ["code"] = "scene { Cube { Color: {0, 0, 1, 1}; Scale: {2, 2, 2} } }" }, new JObject()
                };
                int max = special ? 1 : 4;
                if (round > 0)
                {
                    if (!google)
                    {
                        var toolMessage = (payload["messages"] as JArray).Last();
                        var toolOutput = JObject.Parse((string)toolMessage["content"]);
                        if (toolOutput["id"] != null && toolOutput["id"].Type != JTokenType.String)
                            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"message\":\"A tool result's output's id field must be a string\"}}") };
                        Require(toolOutput["result"] != null, "Tool output data envelope missing");
                    }
                    var serialized = payload.ToString(Formatting.None);
                    Require(serialized.Contains("opaque-synthetic-signature"), "Provider signature metadata lost");
                    SignatureReplayed = true;
                    if (special)
                    {
                        Require(serialized.Contains("error"), "Rejected call did not return tool error");
                        ErrorReturned = true;
                    }
                    else
                    {
                        Require(serialized.Contains("Cube"), "Real world output not replayed");
                        ToolReturns++;
                    }
                }
                if (google)
                {
                    Require(path.EndsWith("models/gemini-3.8-flash:generateContent"), "Unexpected Gemini model");
                    Require(payload["tools"]?[0]?["functionDeclarations"]?[0]?["parametersJsonSchema"] != null, "Google tool schema missing");
                    if (round > 0)
                        Require(payload.ToString().Contains("functionResponse") && payload.ToString().Contains("fixture-call-0"), "Google function response id lost");
                    var parts = round < max ? new JArray(new JObject
                    {
                        ["thoughtSignature"] = "opaque-synthetic-signature",
                        ["functionCall"] = new JObject
                        {
                            ["id"] = "fixture-call-" + round,
                            ["name"] = failure == "unknown" ? "unknown_action" : steps[round],
                            ["args"] = arguments[round]
                        }
                    }) :
                        new JArray(new JObject { ["thought"] = true, ["text"] = "private-synthetic-thought" }, new JObject { ["text"] = "Done." });
                    return Json(new JObject
                    {
                        ["candidates"] = new JArray(new JObject
                        {
                            ["finishReason"] = failure == "cut" ? "MAX_TOKENS" : "STOP",
                            ["content"] = new JObject { ["role"] = "model", ["parts"] = parts }
                        })
                    });
                }
                Require((string)payload["model"] == "openrouter/free" && payload["models"] == null && (bool?)payload["stream"] == false, "Router paid/fallback route used");
                var assistant = new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = round < max ? null : "Done.",
                    ["reasoning_details"] = new JArray(new JObject { ["type"] = "reasoning.encrypted", ["data"] = "opaque-synthetic-signature" })
                };
                if (round < max)
                    assistant["tool_calls"] = new JArray(new JObject
                    {
                        ["id"] = "fixture-call-" + round,
                        ["type"] = "function",
                        ["function"] = new JObject
                        {
                            ["name"] = failure == "unknown" ? "unknown_action" : steps[round],
                            ["arguments"] = failure == "badargs" ? "[invalid]" : arguments[round].ToString(Formatting.None)
                        }
                    });
                return Json(new JObject { ["choices"] = new JArray(new JObject { ["finish_reason"] = failure == "cut" ? "length" : round < max ? "tool_calls" : "stop", ["message"] = assistant }) });
            }
            private static HttpResponseMessage Json(JObject value) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value.ToString(Formatting.None), Encoding.UTF8, "application/json") };
        }
    }
}
