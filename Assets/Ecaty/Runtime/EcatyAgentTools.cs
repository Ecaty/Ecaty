using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ecaty
{
    public static class EcatyAgentTools
    {
        public static IReadOnlyList<IAgentTool> Create(FlecsWorld world, AgentMainThread main)
        {
            return new IAgentTool[] {
                new Tool("query", "Inspect the live world using a native Flecs query expression. Example: Position, ?Primitive, ?Color, ?Scale, ?Spin, ?Presented", "expression", main,
                    a => world.Session.Query((string)a["expression"])),
                new Tool("inspect_entity", "Inspect an entity/component by its full path, including values and reflected type information. Example: scene.Cube", "path", main,
                    a => world.Session.EntityJson((string)a["path"])),
                new Tool("apply_script", "Apply Flecs Script to the world. The application synchronizes views and returns actual presentation observations. Script errors are not transactional; inspect the resulting world if an error occurs.", "code", main,
                    a => { world.Session.RunScript((string)a["code"]); world.SyncViews(); return Observe(world); }),
                new Tool("observe_world", "Refresh and inspect the presented object state. Presented contains measured transforms/color and an applied frame counter. Use this internally to check a change, not as user-facing technical output.", null, main,
                    a => { world.SyncViews(); return Observe(world); })
            };
        }
        private static string Observe(FlecsWorld world) => world.Session.Query("Position, ?Primitive, ?Color, ?Scale, ?Spin, ?Presented");
        private sealed class Tool : IAgentTool
        {
            public string Name { get; }
            public string Description { get; }
            public string InputJsonSchema { get; }
            private readonly string field;
            private readonly AgentMainThread main;
            private readonly Func<JObject, string> action;
            public Tool(string name, string description, string field, AgentMainThread main, Func<JObject, string> action)
            {
                Name = name; Description = description; this.field = field; this.main = main; this.action = action;
                var properties = new JObject(); var required = new JArray();
                if (field != null) { properties[field] = new JObject { ["type"] = "string" }; required.Add(field); }
                InputJsonSchema = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false }.ToString(Formatting.None);
            }
            public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
            {
                var arguments = JObject.Parse(argumentsJson);
                if (field != null && (arguments[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)arguments[field])))
                    throw new ArgumentException("Missing tool argument: " + field);
                return main.RunAsync(() => {
                    return action(arguments);
                }, cancellationToken);
            }
        }
    }
}
