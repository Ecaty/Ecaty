using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Agents.Diagnostics;
using Ecaty.Agents.Editor;
using Ecaty.Agents.Providers.ChatGPT;
using Ecaty.Agents.Security;
using Ecaty.Agents.UI;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Editor
{
    // Synthetic transport is confined to Editor verification. World/tools are real.
    public static class ChatLoopVerification
    {
        public static bool Running
        {
            get; private set;
        }
        private static readonly string ReportPath = "Verification/chat-loop-checks.json";
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
            var checks = new List<string>();
            GameObject root = null;
            var report = new JObject { ["passed"] = false, ["transport"] = "synthetic OAuth/Responses; real native Flecs and Unity objects" };
            try
            {
                var clear = Encoding.UTF8.GetBytes("synthetic protected bytes");
                Require(WindowsSecretStore.Transform(WindowsSecretStore.Transform(clear, true), false).SequenceEqual(clear), "DPAPI round trip failed");
                checks.Add("Windows DPAPI encrypt/decrypt round trip, no account credentials used");
                AgentTrace.ClearView();
                AgentTrace.Configure(Path.GetFullPath("Logs/Agents/Verification"), true);
                root = new GameObject("Chat loop verification");
                var world = root.AddComponent<FlecsWorld>();
                world.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));
                var main = new AgentMainThread();
                using (var fixture = new Fixture())
                using (var http = new HttpClient(fixture))
                {
                    var store = new MemoryStore();
                    // Older builds may have saved an unverified workspace client after a failed exchange.
                    store.Save(Encoding.UTF8.GetBytes(new JObject
                    {
                        ["HostId"] = "urn:uuid:632f3cdb-7d85-4522-8dcf-7d34fb7b1714",
                        ["PendingClientId"] = "legacy-unverified-workspace"
                    }.ToString(Formatting.None)));
                    using (var provider = new ChatGptProvider("Verification", http, store, fixture.Browser))
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        await provider.SignInAsync(timeout.Token);
                        Require(provider.IsSignedIn && fixture.PkceChecked, "Sign in/PKCE verification failed");
                        checks.Add("legacy unverified workspace client ignored; fresh dynamic registration, loopback, PKCE and signed identity");
                        foreach (var pair in new[] { new[] { "wrong-client", fixture.Nonce }, new[] { "verification-client", "wrong-nonce" } })
                        {
                            bool rejected = false;
                            try
                            {
                                await OAuthProtocol.VerifyIdentity(http, fixture.Identity(), pair[0], pair[1], null, timeout.Token);
                            }
                            catch (InvalidOperationException) { rejected = true; }
                            Require(rejected, "Mismatched audience/nonce accepted");
                        }
                        checks.Add("mismatched identity audience and nonce rejected");
                        var tools = EcatyAgentTools.Create(world, main);
                        var dispatched = await Task.Run(() => tools[0].ExecuteAsync("{\"expression\":\"Position\"}", timeout.Token));
                        Require(dispatched.Contains("Cube"), "Background tool dispatch failed");
                        checks.Add("background agent work marshalled to the Unity/world-owning thread");
                        var response = await provider.ReplyAsync(new AgentRequest(Resources.Load<TextAsset>("Agent/Prompts/AgentInstructions").text,
                            new[] { new AgentMessage(AgentRole.User, "Make the cube blue and twice as big.") }, tools), null, timeout.Token);
                        Require(response == "Done." && fixture.Responses == 4 && fixture.ToolReturns == 3, "Tool loop did not complete");
                        world.Session.ReadViews(out var views);
                        var cube = views.Take(world.ViewCount).Single(v => world.Session.EntityName(v.Entity) == "Cube");
                        var actual = world.FindView(cube.Entity);
                        Require(Vector3.Distance(actual.transform.localScale, Vector3.one * 2) < .001f, "Actual object scale not updated");
                        var block = new MaterialPropertyBlock();
                        actual.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                        Require(block.GetColor("_BaseColor").b > .99f && block.GetColor("_BaseColor").r < .01f, "Actual renderer color not updated");
                        var observations = world.Session.Query("Presented");
                        Require(observations.Contains("frame") && observations.Contains("scale"), "Presentation observations missing");
                        checks.Add("chat -> real query -> generated Script -> Unity transform/material -> Flecs Presented -> chat");
                        var entries = AgentTrace.Snapshot();
                        Require(entries.Count(e => e.Stage == "tool-result") == 3 && entries.Any(e => e.Name == "apply_script" && e.Arguments.Contains("Color")), "Trace missing queries/Script/results");
                        var window = ScriptableObject.CreateInstance<AgentTraceWindow>();
                        try
                        {
                            window.CreateGUI();
                            var folds = window.rootVisualElement.Query<Foldout>().ToList();
                            Require(folds.Count == 4 && folds.All(f => !f.value), "Trace not grouped/collapsed by default");
                            folds[0].value = true;
                            Require(folds[0].value, "Request cannot expand");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(window); }
                        checks.Add("developer trace grouped by request with collapsed tool results, inputs and copy controls");
                        byte[] jpeg = { 255, 216, 255, 217 }; // Minimal signature fixture, never sent outside this process.
                        fixture.Failure = "photo-final";
                        var photo = new AgentRequest("", new[] { new AgentMessage(AgentRole.User, "Photo fixture") }, null, jpeg);
                        Require(await provider.ReplyAsync(photo, null, timeout.Token) == "Final text only.",
                            "Completed text without deltas was lost");
                        var ui = RuntimeComposition.Current.AgentUi;
                        var view = new AgentChatView(ui.Layout);
                        using (var controller = new AgentChatController(view, provider, provider, "", tools))
                        {
                            var button = view.Root.Q<Button>("attachPhotoButton");
                            Require(!button.enabledSelf && button.ClassListContains("agent-chat__hidden"), "Unconfigured photo picker visible");
                            byte[] picked = null;
                            controller.ConfigurePhotoPicker(() => picked, "Photo fixture", "apply_script");
                            Require(button.enabledSelf && !button.ClassListContains("agent-chat__hidden") &&
                                button.tooltip == "Attach photo" && button.Q<Label>().text == "\ue226", "Paperclip unavailable");
                            view.SetDraft("Keep my direction");
                            controller.PickPhoto();
                            Require(!controller.HasPhotoAttachment && view.Draft == "Keep my direction", "Cancelled picker changed the draft");
                            picked = jpeg;
                            controller.PickPhoto();
                            Require(controller.HasPhotoAttachment && view.Draft == "Keep my direction" &&
                                button.tooltip == "Remove photo" && button.Q<Label>().text == "\ue5cd", "Photo did not retain the draft or show removal");
                            controller.RemovePhotoAttachment();
                            Require(!controller.HasPhotoAttachment && button.tooltip == "Attach photo", "Paperclip did not reset on removal");
                            view.SetDraft("");
                            controller.PickPhoto();
                            Require(view.Draft == "Photo fixture", "Photo prompt was not suggested");
                            fixture.Failure = "photo-empty";
                            Require(await controller.SendAsync("Photo fixture") == null && controller.HasPhotoAttachment,
                                "Empty response consumed the attachment");
                            fixture.Failure = "photo-final";
                            Require(await controller.SendAsync("Photo fixture") == null && controller.HasPhotoAttachment,
                                "A text reply without an edit consumed the attachment");
                            fixture.Failure = "photo-apply";
                            Require(await controller.SendAsync("Photo fixture") == "Created." && !controller.HasPhotoAttachment,
                                "Verified tool output did not finish the photo request");
                            Require(button.enabledSelf && button.tooltip == "Attach photo", "Paperclip did not reset after creation");
                            controller.PickPhoto();
                            var toolStarts = AgentTrace.Snapshot().Count(e => e.Stage == "tool-start");
                            fixture.Failure = "photo-stream-abort";
                            Require(await controller.SendAsync("Photo fixture") == null && controller.HasPhotoAttachment &&
                                AgentTrace.Snapshot().Count(e => e.Stage == "tool-start") == toolStarts,
                                "A streamed tool ran before successful completion");
                            fixture.Failure = "photo-stream-apply";
                            Require(await controller.SendAsync("Photo fixture") == "Created." && !controller.HasPhotoAttachment &&
                                AgentTrace.Snapshot().Count(e => e.Stage == "tool-start") == toolStarts + 1 &&
                                AgentTrace.Snapshot().Any(e => e.Stage == "response-detail" && e.Result.Contains("Finished text/tool items: 1")),
                                "Completed streamed tool was lost or applied more than once");
                        }
                        checks.Add("paperclip and remove states, cancelled picker and draft preservation; photo input encoded; final-only reply read; empty and unapplied replies preserve attachment; completed edit clears it");
                        checks.Add("finished tool recovered from stream with sparse terminal output, applied once, and withheld after interrupted stream");
                    }
                    // Expire only synthetic state, then verify rotating refresh and terminal SSE failures.
                    var saved = JObject.Parse(Encoding.UTF8.GetString(store.Load()));
                    saved["Active"]["ExpiresAt"] = 1;
                    store.Save(Encoding.UTF8.GetBytes(saved.ToString(Formatting.None)));
                    fixture.Failure = "eof";
                    using (var provider = new ChatGptProvider("Verification", http, store, fixture.Browser))
                    {
                        bool failed = false;
                        try
                        {
                            await provider.ReplyAsync(new AgentRequest("", new[] { new AgentMessage(AgentRole.User, "EOF fixture") }, null), null, CancellationToken.None);
                        }
                        catch (AgentServiceException) { failed = true; }
                        Require(failed && fixture.Tokens == 2, "EOF/refresh fixture failed");
                        var rotated = JObject.Parse(Encoding.UTF8.GetString(store.Load()));
                        Require((string)rotated["Active"]["RefreshToken"] == "synthetic-refresh-2", "Refresh token rotation not saved");
                        fixture.Failure = "limit";
                        failed = false;
                        try
                        {
                            await provider.ReplyAsync(new AgentRequest("", new[] { new AgentMessage(AgentRole.User, "Limit fixture") }, null), null, CancellationToken.None);
                        }
                        catch (AgentServiceException e) { failed = e.Code == "subscription_sharing_usage_limit_exceeded"; }
                        Require(failed, "Late SSE error treated as success");
                        await provider.SignOutAsync(CancellationToken.None);
                        Require(!provider.IsSignedIn, "Disconnect left credentials active");
                    }
                    checks.Add("rotating refresh saved; early EOF and usage error after deltas rejected; local disconnect");
                }
                AgentTrace.Record("redaction-check", "tool-result", "redaction", "Bearer synthetic-credential", "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJmaXh0dXJlIn0.c2lnbmF0dXJl");
                var serialized = JsonConvert.SerializeObject(AgentTrace.Snapshot());
                Require(!serialized.Contains("synthetic-access") && !serialized.Contains("synthetic-refresh") && !serialized.Contains("synthetic-credential") && !serialized.Contains("eyJhbGciOiJSUzI1NiJ9"), "Credential material in trace");
                checks.Add("auth exchanges excluded from traces; bearer/JWT redaction verified");
                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                report["checks"] = new JArray(checks);
                File.WriteAllText(ReportPath, report.ToString(Formatting.Indented));
                Running = false;
                Debug.Log("ECATY_CHAT_LOOP=" + ((bool)report["passed"] ? "PASS" : "FAIL"));
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
            private readonly RSA rsa = new RSACryptoServiceProvider(2048);
            public string Nonce, Challenge, Redirect, Failure;
            public int Responses, Tokens, ToolReturns;
            public bool PkceChecked;
            public Fixture()
            {
            }
            public async void Browser(string url)
            {
                var fields = OAuthProtocol.Query(new Uri(url).Query);
                Nonce = fields["nonce"];
                Challenge = fields["code_challenge"];
                Redirect = fields["redirect_uri"];
                Require(fields["client_id"] == "dynamic_agent_client" && fields["ext_agent_host_id"].StartsWith("urn:uuid:"), "Registration fields missing");
                using (var browser = new HttpClient())
                    await browser.GetAsync(Redirect + "?" + OAuthProtocol.Form(new Dictionary<string, string> { ["code"] = "synthetic-code", ["client_id"] = "verification-client", ["state"] = fields["state"] }));
            }
            public string Identity()
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var header = OAuthProtocol.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"verification\"}"));
                var claims = OAuthProtocol.Encode(Encoding.UTF8.GetBytes(new JObject { ["iss"] = OAuthProtocol.Issuer, ["aud"] = "verification-client", ["sub"] = "synthetic-subject", ["iat"] = now, ["exp"] = now + 3600, ["nonce"] = Nonce }.ToString(Formatting.None)));
                string input = header + "." + claims;
                return input + "." + OAuthProtocol.Encode(rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            {
                string path = request.RequestUri.AbsolutePath;
                if (path == "/api/accounts/oauth/token")
                {
                    var form = OAuthProtocol.Query(await request.Content.ReadAsStringAsync());
                    Require(form["client_id"] == "verification-client", "Issued client id not used");
                    if (form["grant_type"] == "authorization_code")
                    {
                        Require(form["redirect_uri"] == Redirect && OAuthProtocol.Challenge(form["code_verifier"]) == Challenge, "PKCE/redirect mismatch");
                        PkceChecked = true;
                    }
                    else
                        Require(form["refresh_token"] == "synthetic-refresh-1", "Incorrect refresh token");
                    Tokens++;
                    return Json(new JObject { ["access_token"] = "synthetic-access-" + Tokens, ["refresh_token"] = "synthetic-refresh-" + Tokens, ["id_token"] = Identity(), ["token_type"] = "Bearer", ["expires_in"] = 3600, ["scope"] = OAuthProtocol.RequiredScope });
                }
                if (path == "/.well-known/jwks.json")
                {
                    var key = rsa.ExportParameters(false);
                    return Json(new JObject { ["keys"] = new JArray(new JObject { ["kid"] = "verification", ["kty"] = "RSA", ["alg"] = "RS256", ["n"] = OAuthProtocol.Encode(key.Modulus), ["e"] = OAuthProtocol.Encode(key.Exponent) }) });
                }
                Require(request.Headers.Authorization?.Scheme == "Bearer", "OAuth bearer missing");
                if (path == "/v1/models")
                    return Json(new JObject { ["models"] = new JArray(new JObject { ["slug"] = "verification-model", ["visibility"] = "list" }) });
                Require(path == "/v1/responses", "Unexpected endpoint");
                var body = JObject.Parse(await request.Content.ReadAsStringAsync());
                Require((bool)body["stream"] && !(bool)body["store"] && body["previous_response_id"] == null, "Unsupported transport fields");
                if (Failure != null && Failure.StartsWith("photo-", StringComparison.Ordinal))
                {
                    var input = (JArray)body["input"];
                    var currentPhotoMessage = input.OfType<JObject>().Last(item => (string)item["role"] == "user");
                    Require((string)currentPhotoMessage["content"][1]["image_url"] == "data:image/jpeg;base64,/9j/2Q==",
                        "Photo was not included in the request");
                    var items = new JArray();
                    if (Failure == "photo-final")
                        items.Add(new JObject
                        {
                            ["type"] = "message",
                            ["role"] = "assistant",
                            ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "Final text only." })
                        });
                    var prefix = "";
                    if ((Failure == "photo-apply" || Failure.StartsWith("photo-stream-", StringComparison.Ordinal)) &&
                        !input.Any(item => (string)item["type"] == "function_call_output"))
                    {
                        var call = new JObject
                        {
                            ["type"] = "function_call",
                            ["namespace"] = "app",
                            ["name"] = "apply_script",
                            ["status"] = "completed",
                            ["call_id"] = "photo-fixture",
                            ["arguments"] = new JObject { ["code"] = "scene { Sphere { Scale: {1,1.1,1} } }" }.ToString(Formatting.None)
                        };
                        prefix = Event(new JObject { ["type"] = "response.output_item.done", ["output_index"] = 0, ["item"] = call });
                        if (Failure == "photo-stream-abort")
                            return Sse(prefix + Event(new JObject { ["type"] = "response.incomplete" }));
                        if (Failure == "photo-apply")
                            items.Add(call);
                    }
                    return Sse(prefix + Event(new JObject
                    {
                        ["type"] = "response.completed",
                        ["response"] = new JObject { ["status"] = "completed", ["output"] = items }
                    }));
                }
                if (Failure != null)
                {
                    var partial = Event(new JObject { ["type"] = "response.output_text.delta", ["delta"] = "Partial" });
                    if (Failure == "limit")
                        partial += Event(new JObject { ["type"] = "error", ["code"] = "subscription_sharing_usage_limit_exceeded" });
                    return Sse(partial);
                }
                if (Responses > 0)
                {
                    var result = (body["input"] as JArray).OfType<JObject>().LastOrDefault(i => (string)i["type"] == "function_call_output");
                    Require(result != null && ((string)result["output"]).Contains("Cube"), "World tool output not replayed");
                    ToolReturns++;
                }
                string[] names = { "query", "apply_script", "observe_world" };
                var args = new[] { new JObject { ["expression"] = "Position, ?Color, ?Scale, ?Presented" }, new JObject { ["code"] = "scene { Cube { Color: {0, 0, 1, 1}; Scale: {2, 2, 2} } }" }, new JObject() };
                int round = Responses++;
                JArray output = round < 3 ? new JArray(new JObject { ["type"] = "function_call", ["name"] = names[round], ["namespace"] = "app", ["call_id"] = "call-" + round, ["arguments"] = args[round].ToString(Formatting.None) }) : new JArray(new JObject { ["type"] = "message", ["role"] = "assistant", ["content"] = new JArray(new JObject { ["type"] = "output_text", ["text"] = "Done." }) });
                string data = round == 3 ? Event(new JObject { ["type"] = "response.output_text.delta", ["delta"] = "Done." }) : "";
                return Sse(data + Event(new JObject { ["type"] = "response.completed", ["response"] = new JObject { ["status"] = "completed", ["output"] = output } }));
            }
            private static string Event(JObject data) => "data: " + data.ToString(Formatting.None) + "\n\n";
            private static HttpResponseMessage Json(JObject body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json") };
            private static HttpResponseMessage Sse(string body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    rsa.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
