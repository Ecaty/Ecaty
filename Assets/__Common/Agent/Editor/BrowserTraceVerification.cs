using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents.Diagnostics;
using Ecaty.Agents.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Editor
{
    public static class BrowserTraceVerification
    {
        public static bool Running
        {
            get; private set;
        }
        private static AgentTraceWebServer fixture;
        [UnityEditor.InitializeOnLoadMethod]
        private static void CleanupHook()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += CloseFixture;
            UnityEditor.EditorApplication.quitting += CloseFixture;
        }
        public static string FixtureAddress => fixture?.BaseUri;
        private static void Require(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        private static Dictionary<string, string> Assets() => new Dictionary<string, string>
        {
            ["/"] = Resources.Load<TextAsset>("EcatyAgentTrace/index.html").text,
            ["/viewer.js"] = Resources.Load<TextAsset>("EcatyAgentTrace/viewer.js").text,
            ["/viewer.css"] = Resources.Load<TextAsset>("EcatyAgentTrace/viewer.css").text
        };
        private static AgentTraceEntry[] Demo() => new[] {
            new AgentTraceEntry { Sequence = 1, Session = "fixture", Request = "demo", Stage = "request", Name = "Fixture", Time = "2026-11-03T00:00:00Z", Arguments = "Make the cube blue. <img src=x onerror=alert(1)>" },
            new AgentTraceEntry { Sequence = 2, Session = "fixture", Request = "demo", Stage = "tool-result", Name = "query", Arguments = "{\"expression\":\"Position, ?Color, ?Presented\"}", Result = "{\"results\":[{\"name\":\"Cube\"}]}" },
            new AgentTraceEntry { Sequence = 3, Session = "fixture", Request = "demo", Stage = "tool-result", Name = "apply_script", Arguments = JsonConvert.SerializeObject(new { code = "// Flecs 4.1.6\nconst size = 2\nscene.Cube { Color: {0, 0, 1, 1}; Scale: {$size, $size, $size} }" }), Result = "{\"Presented\":{\"color\":{\"r\":0,\"g\":0,\"b\":1}}}", Milliseconds = 12 },
            new AgentTraceEntry { Sequence = 4, Session = "fixture", Request = "demo", Stage = "completed", Name = "Fixture", Result = "Done.", Milliseconds = 123 }
        };
        private static AgentTraceBatch ReadDemo(long after, string session, int limit)
        {
            bool reset = session != "fixture";
            var page = Demo().Where(e => reset || e.Sequence > after).Take(limit).ToArray();
            return new AgentTraceBatch { Session = "fixture", Reset = reset, Latest = 4, Cursor = page.Length > 0 ? page.Last().Sequence : after, Entries = page };
        }
        public static void OpenFixture(bool openBrowser = true)
        {
            fixture?.Dispose();
            fixture = new AgentTraceWebServer("Synthetic browser verification", Assets(), Path.GetFullPath("Logs/TraceVerification"), false, 0, ReadDemo, "synthetic-browser-verification-key");
            if (openBrowser)
                Application.OpenURL(fixture.ViewerUri());
        }
        public static void CloseFixture()
        {
            fixture?.Dispose();
            fixture = null;
        }
        public static async void Start()
        {
            if (Running)
                return;
            Running = true;
            var checks = new JArray();
            var report = new JObject { ["passed"] = false, ["transport"] = "real HTTP; synthetic trace data; no AI requests", ["unity"] = Application.unityVersion };
            bool capture = AgentTrace.Enabled;
            try
            {
                var folder = Path.GetFullPath("Logs/TraceVerification");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "synthetic.jsonl"), string.Join("\n", Demo().Select(JsonConvert.SerializeObject)));
                using (var server = new AgentTraceWebServer("Verification", Assets(), folder, port: 0, read: ReadDemo))
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    var page = await http.GetAsync(server.BaseUri);
                    Require(page.IsSuccessStatusCode && page.Headers.Contains("Content-Security-Policy") && page.Headers.CacheControl.NoStore, "Viewer headers/page invalid");
                    Require((await page.Content.ReadAsStringAsync()).Contains("Flecs Script highlighting"), "Viewer asset missing");
                    Require((await http.GetAsync(server.BaseUri + "api/trace")).StatusCode == HttpStatusCode.Unauthorized, "Unpaired trace readable");
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
                    Require((await http.GetAsync(server.BaseUri + "api/trace")).StatusCode == HttpStatusCode.Unauthorized, "Wrong pairing key accepted");
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.AccessKey);
                    var data = JObject.Parse(await http.GetStringAsync(server.BaseUri + "api/trace?after=0&limit=2"));
                    Require((bool)data["Trace"]["Reset"] && (data["Trace"]["Entries"] as JArray).Count == 2, "Initial page/cursor invalid");
                    var next = JObject.Parse(await http.GetStringAsync(server.BaseUri + "api/trace?after=2&session=fixture&limit=2"));
                    Require(!(bool)next["Trace"]["Reset"] && (long)next["Trace"]["Cursor"] == 4, "Incremental page failed");
                    checks.Add("paired read-only HTTP viewer; CSP/no-store; wrong/missing key rejected; incremental cursor/session polling");
                    http.DefaultRequestHeaders.Add("Origin", "https://unrelated.example");
                    Require((await http.GetAsync(server.BaseUri + "api/trace")).StatusCode == HttpStatusCode.Forbidden, "Cross-origin trace readable");
                    http.DefaultRequestHeaders.Remove("Origin");
                    Require((await http.PostAsync(server.BaseUri + "api/trace", new StringContent("{}"))).StatusCode == HttpStatusCode.MethodNotAllowed, "Mutation accepted");
                    Require((await http.GetAsync(server.BaseUri + "api/log?name=..%2fAGENTS.md")).StatusCode == HttpStatusCode.BadRequest, "Path traversal accepted");
                    Require((await http.GetStringAsync(server.BaseUri + "api/logs")).Contains("synthetic.jsonl"), "Recent log listing failed");
                    Require((await http.GetStringAsync(server.BaseUri + "api/log?name=synthetic.jsonl")).Contains("apply_script"), "Log loading failed");
                    checks.Add("cross-origin reads and mutations blocked; recent log read works; file traversal rejected");
                }
                using (var lan = new AgentTraceWebServer("LAN fixture", Assets(), folder, allowLan: true, port: 0, read: ReadDemo))
                using (var http = new HttpClient())
                {
                    Require(lan.AllowLan, "Explicit LAN mode not enabled");
                    Require((await http.GetAsync(lan.BaseUri + "api/trace")).StatusCode == HttpStatusCode.Unauthorized, "LAN bypassed pairing");
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", lan.AccessKey);
                    Require((await http.GetAsync(lan.BaseUri + "api/trace")).IsSuccessStatusCode, "Paired LAN listener failed");
                }
                checks.Add("LAN binding is opt-in and still requires pairing; loopback HTTP checks only, no phone-device claim");
                AgentTrace.Enabled = true;
                var before = AgentTrace.Read(0, null);
                string id = "browser-check-" + Guid.NewGuid().ToString("N");
                AgentTrace.Record(id, "request", "Fixture", "Synthetic request");
                AgentTrace.Record(id, "tool-result", "query", "{}", "Bearer synthetic-secret AIza12345678901234567890123456789012345");
                AgentTrace.Record(id, "completed", "Fixture", result: "Done.");
                var batch = AgentTrace.Read(before.Latest, before.Session, 2);
                Require(batch.Entries.Length == 2 && batch.Entries.All(e => e.Request == id), "Collector cursor failed");
                var tail = AgentTrace.Read(batch.Cursor, batch.Session);
                Require(tail.Entries.Length == 1 && tail.Entries[0].Stage == "completed", "Collector paging lost entry");
                Require(!JsonConvert.SerializeObject(batch).Contains("synthetic-secret") && !JsonConvert.SerializeObject(batch).Contains("AIza123"), "Redaction bypassed in browser data");
                checks.Add("same collector/redaction feeds Editor and browser, paginated without clearing prior user traces");
                report["passed"] = true;
            }
            catch (Exception e) { report["error"] = e.GetType().Name + ": " + e.Message.Replace("\0", "").Trim(); Debug.LogException(e); }
            finally { AgentTrace.Enabled = capture; report["checks"] = checks; report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O"); File.WriteAllText("Verification/browser-trace-checks.json", report.ToString(Formatting.Indented)); Running = false; }
        }
        public static async void CheckChatUI()
        {
            var report = new JObject { ["passed"] = false, ["unity"] = Application.unityVersion };
            AgentChatView view = null;
            StyleLength originalWidth = default;
            try
            {
                var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
                Require(panel, "Enter the verification Play session first");
                view = (AgentChatView)typeof(AgentChatPanel).GetField("view", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(panel);
                originalWidth = view.Root.style.width;
                for (int i = 0; i < 25; i++)
                    view.AppendMessage("Synthetic chat layout verification " + i + "\nSecond line.", i % 2 == 0);
                view.SetDraft("Visible caret sample");
                await Task.Delay(200);
                var input = view.Root.Q<TextField>("messageInput");
                var messages = view.Root.Q<ScrollView>("messages");
                var history = view.Root.Q<Button>("historyButton");
                Require(input.textSelection.cursorColor.r > .8f && input.textSelection.selectionColor.b > .3f, "Caret/selection color still dark");
                Require(messages.verticalScroller.resolvedStyle.width <= 8 && messages.verticalScroller.resolvedStyle.backgroundColor.a < .05f, "Scrollbar still has wide/opaque default track");
                Require(view.Root.Q("conversation").Contains(history) && !view.Root.Q("composer").Contains(history), "Collapse button not under conversation");
                Require(history.worldBound.y >= messages.worldBound.yMax - 1, "Collapse button is above the history");
                Require(messages.verticalScroller.highValue > 0 && messages.verticalScroller.value >= messages.verticalScroller.highValue - 3, "New messages did not follow tail");
                view.SetHistoryExpanded(false);
                Require(messages.style.display.value == DisplayStyle.None && history.resolvedStyle.display == DisplayStyle.Flex, "Collapsed conversation cannot reopen");
                view.SetHistoryExpanded(true);
                view.Root.style.width = 340;
                await Task.Delay(150);
                Require(view.Root.Q("composer").worldBound.width <= 342 && input.resolvedStyle.width > 100, "Compact composer overflow");
                report["passed"] = true;
                report["caret"] = input.textSelection.cursorColor.ToString();
                report["scrollbarWidth"] = messages.verticalScroller.resolvedStyle.width;
                report["checks"] = new JArray("light caret/selection", "thin transparent scrollbar", "collapse/reopen below conversation", "new-message auto-scroll", "340px compact composer");
            }
            catch (Exception e) { report["error"] = e.ToString(); Debug.LogException(e); }
            finally
            {
                if (view != null)
                {
                    view.ClearMessages();
                    view.SetDraft("");
                    view.Root.style.width = originalWidth;
                }
                File.WriteAllText("Verification/chat-ui-polish-checks.json", report.ToString(Formatting.Indented));
            }
        }
    }
}
