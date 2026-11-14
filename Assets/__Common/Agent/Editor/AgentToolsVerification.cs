using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Agents.Providers;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    // Tool schemas in the shared tool loop and the world tools against a real session, driven by a scripted provider
    // (no service, no account). Registered in Tests/Extended.json; report: Verification/agent-tools-checks.json.
    public static class AgentToolsVerification
    {
        public static bool Running
        {
            get; private set;
        }
        private const string ReportPath = "Verification/agent-tools-checks.json";

        private static void Require(bool value, string error)
        {
            if (!value)
                throw new Exception(error);
        }

        // A provider that plays a script of tool calls through the real tool loop.
        private sealed class ScriptedProvider : IAgentProvider
        {
            public string Id => "scripted";
            public string DisplayName => "Scripted";
            private readonly Func<ToolLoop, Task> script;
            public ScriptedProvider(Func<ToolLoop, Task> script) => this.script = script;
            public async Task<string> ReplyAsync(AgentRequest request, IProgress<string> textDelta, CancellationToken cancellationToken)
            {
                await script(new ToolLoop(request, DisplayName)).ConfigureAwait(false);
                return "done";
            }
        }

        private sealed class Probe : IAgentTool
        {
            public int Runs;
            public string Name => "probe";
            public string Description => "Schema probe.";
            public string InputJsonSchema => "{\"type\":\"object\",\"properties\":{\"label\":{\"type\":\"string\"},\"n\":{\"type\":\"integer\"},\"list\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"label\"],\"additionalProperties\":false}";
            public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
            {
                Runs++;
                return Task.FromResult("{\"ok\":true}");
            }
        }

        // Runs a script of calls and returns the parsed answers.
        private static void Play(IReadOnlyList<IAgentTool> tools, Func<Func<string, object, JObject>, Task> steps)
        {
            var provider = new ScriptedProvider(loop => steps((name, args) =>
                JObject.Parse(loop.Execute(name, args is string text ? text : JsonConvert.SerializeObject(args), CancellationToken.None).GetAwaiter().GetResult())));
            provider.ReplyAsync(new AgentRequest("", new[] { new AgentMessage(AgentRole.User, "go") }, tools.ToArray()), null, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static List<string> Names(JToken result) =>
            ((JContainer)result).DescendantsAndSelf().OfType<JProperty>().Where(p => p.Name == "name" && p.Value.Type == JTokenType.String).Select(p => (string)p.Value).ToList();

        public static void Start()
        {
            if (Running)
                return;
            Running = true;
            var checks = new List<string>();
            GameObject root = null;
            var report = new JObject { ["passed"] = false, ["transport"] = "scripted provider through the real tool loop; real native Flecs session" };
            try
            {
                root = new GameObject("Agent tools verification");
                var world = root.AddComponent<FlecsWorld>();
                world.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"));
                var session = world.Session;
                var main = new AgentMainThread();
                var legacy = WorldAgentTools.Create(world, main);
                var inside = WorldTools.Create(world, main);
                var offered = new EnteredWorldTools(session, legacy, inside);

                // Schemas in the tool loop.
                var probe = new Probe();
                Play(new IAgentTool[] { probe }, async call =>
                {
                    var missing = call("probe", "{}");
                    Require(((string)missing["error"]).Contains("Missing required argument: label") && probe.Runs == 0, "A missing property ran the tool: " + missing);
                    var wrong = call("probe", "{\"label\":5}");
                    Require(((string)wrong["error"]).Contains("must be a string") && probe.Runs == 0, "A wrong type ran the tool: " + wrong);
                    var unknown = call("probe", "{\"label\":\"x\",\"extra\":1}");
                    Require(((string)unknown["error"]).Contains("Unknown argument: extra") && probe.Runs == 0, "An unknown property ran the tool: " + unknown);
                    var nested = call("probe", "{\"label\":\"x\",\"list\":[\"a\",2]}");
                    Require(((string)nested["error"]).Contains("list[1]") && probe.Runs == 0, "A wrong item type ran the tool: " + nested);
                    var fraction = call("probe", "{\"label\":\"x\",\"n\":1.5}");
                    Require(((string)fraction["error"]).Contains("integer") && probe.Runs == 0, "A fraction passed as an integer: " + fraction);
                    var valid = call("probe", "{\"label\":\"x\",\"n\":3,\"list\":[\"a\"]}");
                    Require((bool)valid["ok"] && probe.Runs == 1, "A valid call did not run: " + valid);
                    var nullOptional = call("probe", "{\"label\":\"x\",\"n\":null}");
                    Require((bool)nullOptional["ok"] && probe.Runs == 2, "A null optional was refused: " + nullOptional);
                    await Task.CompletedTask;
                });
                checks.Add("tool loop: missing property, wrong type, unknown property, wrong item type and fraction for an integer each return an error and do not run the tool; valid and null-optional calls run");

                // An old single-string tool, outside a world.
                Require(offered.Any(t => t.Name == "inspect_entity") && offered.All(t => t.Name != "spawn"), "Outside a world the old tools must be offered, not the world's");
                Play(offered, async call =>
                {
                    var found = call("query", "{\"expression\":\"Position\"}");
                    Require(found["error"] == null, "The old query tool failed: " + found);
                    var none = call("query", "{}");
                    Require(((string)none["error"]).Contains("Missing required argument: expression"), "The old query tool ran without its argument: " + none);
                    await Task.CompletedTask;
                });
                checks.Add("old single-string tools offered outside a world and still working; world tools not offered");

                var game = session.CreateWorld("game");
                session.EnterWorld(game);
                Require(offered.Any(t => t.Name == "spawn" && t.InputJsonSchema.Contains("\"values\"")) && offered.All(t => t.Name != "inspect_entity" && t.Name != "apply_photo_object"),
                    "Inside a world the world's tools must replace the old ones");
                foreach (var tool in inside)
                    Require(tool.Description.Length > 0 && tool.Description.Length < 120 && !tool.Description.Contains('\n'), tool.Name + " needs one short sentence");
                checks.Add("inside a world the v2 tools are offered instead of the old ones; each has a one-sentence description");

                // Types from the world.
                string types = WorldTypeText.Build(session);
                var look = JObject.Parse(session.Describe("Look"));
                var rule = JObject.Parse(session.Describe("Rule"));
                string Brief(JObject type, string member) => (string)type["members"].OfType<JObject>().First(m => (string)m["name"] == member)["brief"];
                Require(types.Contains(Brief(look, "material")), "Type text lacks the brief of Look.material");
                Require(types.Contains(Brief(rule, "when")), "Type text lacks the brief of Rule.when");
                Require(types.Contains("Touches"), "Type text does not list Touches");
                Require(types.Length < WorldTypeText.Budget, "Type text is " + types.Length + " characters");
                string conduct = Resources.Load<TextAsset>("Agent/Prompts/WorldInstructions")?.text;
                Require(!string.IsNullOrWhiteSpace(conduct) && WorldTypeText.Compose(session, conduct).Contains("Look"), "World instructions are missing");
                checks.Add("type text from Describe: briefs of Look.material and Rule.when, Touches listed, " + types.Length + " of " + WorldTypeText.Budget + " characters, "
                    + WorldTypeText.Types.Count(t => session.Describe(t) != null) + " of " + WorldTypeText.Types.Length + " types described");

                // A scripted chat: build a small scene, break it, read the alert, undo. A tool loop allows 24 calls, so it has three turns.
                string secondName = null;
                int instancesBefore = 0;
                Play(offered, async call =>
                {
                    var described = call("describe", "{\"name\":\"Rule\"}");
                    Require((bool)described["ok"] && (string)described["description"]["is"] == "component", "describe Rule failed: " + described);

                    var kind = call("define_kind", new { name = "Fox", code = "prefab Fox { Primitive: {1}; Scale: {1, 1, 1}; Position: {0, 0, 0}; Color: {1, 0.5, 0, 1} }" });
                    Require((bool)kind["ok"], "define_kind failed: " + kind);
                    var kinds = call("list_kinds", "{}");
                    Require((bool)kinds["ok"] && kinds["world"].ToString().Contains("kinds.Fox"), "list_kinds does not show the new kind: " + kinds);
                    var bare = call("describe", "{\"name\":\"Fox\"}");
                    Require((string)bare["description"]["is"] == "kind", "describe of a bare kind name failed: " + bare);

                    var first = call("spawn", new { kind = "Fox", name = "a", values = new { Position = new { x = 1, y = 0, z = 0 }, Color = new { r = 0, g = 1, b = 0, a = 1 } } });
                    Require((bool)first["ok"] && (string)first["entity"] == "a", "spawn failed: " + first);
                    var second = call("spawn", new { kind = "Fox", values = new { Position = new { x = 2, y = 0, z = 0 } } });
                    Require((bool)second["ok"] && ((string)second["entity"]).StartsWith("fox"), "spawn without a name failed: " + second);
                    secondName = (string)second["entity"];
                    var placed = call("inspect", "{\"entity\":\"a\"}");
                    Require((float)placed["entity"]["components"]["Position"]["x"] == 1f, "The spawned values were not set: " + placed);
                    var missingKind = call("spawn", new { kind = "Nope" });
                    Require(!(bool)missingKind["ok"], "spawn of an unknown kind succeeded");

                    var added = call("add_rule", new { name = "start", owner = "kinds.Fox", when = "AtStart", thens = new[] { new { verb = "Grows" } } });
                    Require((bool)added["ok"] && (string)added["rule"] == "rules.start", "add_rule failed: " + added);
                    var badRule = call("add_rule", new { name = "ghost", owner = "kinds.Missing", when = "AtStart", thens = new[] { new { verb = "Grows" } } });
                    Require(!(bool)badRule["ok"], "add_rule with a missing owner succeeded: " + badRule);

                    var found = call("query", "{\"expression\":\"Position, Primitive\"}");
                    var foundNames = Names(found["result"]);
                    Require((bool)found["ok"] && foundNames.Contains("a") && foundNames.Contains(secondName), "query did not find both instances: " + found);
                    Require(!found.ToString().Contains("worlds.game"), "A path in a result is not relative: " + found);
                    instancesBefore = foundNames.Count;

                    await Task.CompletedTask;
                });
                Play(offered, async call =>
                {
                    // Break it: a script with an unknown component is refused and the world stays as it was.
                    var broken = call("apply_script", new { code = "fine { Position: {0, 0, 0} }\nbroken { NoSuchComponent: {1} }" });
                    Require(!(bool)broken["ok"] && (int)broken["line"] > 0 && broken["error"] != null, "A bad script was not refused with a line: " + broken);
                    Require(call("inspect", "{\"entity\":\"fine\"}")["ok"].Value<bool>() == false, "A refused script changed the world");
                    Require(Names(call("query", "{\"expression\":\"Position, Primitive\"}")["result"]).Count == instancesBefore, "A refused script changed the instances");

                    // A rule without an owner is accepted; the alert follows within simulated seconds.
                    var orphan = call("apply_script", new { code = "rules { orphan { Rule: {when: AtStart, thens: [{verb: Grows}]} } }" });
                    Require((bool)orphan["ok"], "The ownerless rule was refused: " + orphan);
                    for (int i = 0; i < 25; i++)
                        session.Tick(0.1f);
                    var raised = call("alerts", "{}");
                    var alert = ((JArray)raised["alerts"]).OfType<JObject>().FirstOrDefault(a => (string)a["entity"] == "rules.orphan");
                    Require(alert != null && ((string)alert["message"]).Contains("belongs to nothing"), "No alert for the ownerless rule: " + raised);
                    Require(((JArray)raised["alerts"]).All(a => (string)a["entity"] != "rules.start"), "The owned rule raised an alert: " + raised);

                    var undone = call("undo", "{}");
                    Require((bool)undone["ok"], "undo failed: " + undone);
                    Require(session.EnteredWorld != 0, "The undo left the world");
                    for (int i = 0; i < 25; i++)
                        session.Tick(0.1f);
                    var cleared = call("alerts", "{}");
                    Require(((JArray)cleared["alerts"]).Count == 0, "The alert stayed after undo: " + cleared);
                    var afterUndo = Names(call("query", "{\"expression\":\"Position, Primitive\"}")["result"]);
                    Require(afterUndo.Contains("a") && afterUndo.Contains(secondName), "The instances are gone after undo");
                    Require(Names(call("query", "{\"expression\":\"Rule\"}")["result"]).Contains("start") &&
                        !Names(call("query", "{\"expression\":\"Rule\"}")["result"]).Contains("orphan"), "The rules are not as before the ownerless one");

                    await Task.CompletedTask;
                });
                Play(offered, async call =>
                {
                    // Remove and bring back.
                    var removed = call("remove", "{\"entity\":\"a\"}");
                    Require((bool)removed["ok"] && !Names(call("query", "{\"expression\":\"Position, Primitive\"}")["result"]).Contains("a"), "remove did not remove: " + removed);
                    Require((bool)call("undo", "{}")["ok"] && Names(call("query", "{\"expression\":\"Position, Primitive\"}")["result"]).Contains("a"), "undo did not bring the instance back");

                    // A value that does not fit changes nothing.
                    string before = call("inspect", "{\"entity\":\"a\"}")["entity"]["components"]["Position"].ToString(Formatting.None);
                    var misfit = call("set", new { entity = "a", component = "Position", value = new { x = "abc", y = 0, z = 0 } });
                    Require(!(bool)misfit["ok"] && misfit["error"] != null, "A value that does not fit was accepted: " + misfit);
                    Require(call("inspect", "{\"entity\":\"a\"}")["entity"]["components"]["Position"].ToString(Formatting.None) == before, "A failed set changed the value");
                    Require(((string)misfit["error"]).Contains("Position.x must be a number"), "The error does not say what is wrong: " + misfit);
                    var fit = call("set", new { entity = "a", component = "Position", value = new { x = 5, y = 0, z = 0 } });
                    Require((bool)fit["ok"] && (float)call("inspect", "{\"entity\":\"a\"}")["entity"]["components"]["Position"]["x"] == 5f, "set failed: " + fit);
                    var partial = call("set", new { entity = "a", component = "Position", value = new { y = 3 } });
                    var kept = call("inspect", "{\"entity\":\"a\"}")["entity"]["components"]["Position"];
                    Require((bool)partial["ok"] && (float)kept["x"] == 5f && (float)kept["y"] == 3f, "A set of one member did not keep the others: " + kept);
                    var noMember = call("set", new { entity = "a", component = "Position", value = new { q = 1 } });
                    Require(!(bool)noMember["ok"] && ((string)noMember["error"]).Contains("no member q") && ((string)noMember["error"]).Contains("x, y, z"), "An unknown member was not answered with the members: " + noMember);
                    var tooBig = call("set", new { entity = "a", component = "Look", value = new { colour = new { h = 400 } } });
                    Require(!(bool)tooBig["ok"] && ((string)tooBig["error"]).Contains("between 0 and 360"), "A value outside its range was accepted: " + tooBig);
                    Require((bool)call("undo", "{}")["ok"] && (float)call("inspect", "{\"entity\":\"a\"}")["entity"]["components"]["Position"]["y"] == 0f,
                        "A refused set left its checkpoint behind: undo did not take back the last good set");

                    // Nothing outside the world.
                    foreach (string outside in new[] { "worlds.other.a", "library", "library.Fox" })
                    {
                        var refused = call("inspect", new { entity = outside });
                        Require(!(bool)refused["ok"] && ((string)refused["error"]).Contains("outside"), "A path outside the world was not refused: " + outside + " " + refused);
                    }
                    Require(!(bool)call("remove", new { entity = "worlds.other.a" })["ok"], "remove reached outside the world");

                    // Play and stop keep the world.
                    Require((bool)call("play", "{}")["ok"], "play failed");
                    Require((bool)call("stop", "{}")["ok"] && session.EnteredWorld != 0, "stop failed");
                    Require(Names(call("query", "{\"expression\":\"Position, Primitive\"}")["result"]).Contains(secondName), "Play and stop lost an instance");
                    await Task.CompletedTask;
                });
                checks.Add("scripted chat on a real session: define_kind, spawn with values, add_rule, query; refused script with line and unchanged world; ownerless rule applied, alert after two simulated seconds, undo clears it and keeps the rest; remove then undo; a set that does not fit changes nothing and says why (type, member, range), a set of one member keeps the others, a refused set leaves no checkpoint; paths outside the world refused; play and stop");
                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (root)
                    UnityEngine.Object.DestroyImmediate(root);
                report["checks"] = new JArray(checks);
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, report.ToString(Formatting.Indented));
                Running = false;
                Debug.Log("AGENT_TOOLS=" + ((bool)report["passed"] ? "PASS" : "FAIL"));
            }
        }
    }
}
