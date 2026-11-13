using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    internal static class RuntimeExperienceTools
    {
        public static IEnumerable<IAgentTool> Create(FlecsWorld world, RuntimeExperience experience, AgentMainThread main)
        {
            yield return new Tool(world, "inspect_experience", "List bound models, per-actor input/goal/command paths, independent behavior IDs and engine diagnostics.", null, main, _ => experience.Inspect());
            yield return new Tool(world, "create_model", "Create/edit an independent named model. creation is a JSON string with id (simple letters/digits/underscores), recipe (same version-4 object accepted by apply_photo_object), optional config (configure_actor patch). Preserve existing models. Use inspect_model before edits.", "creation", main, a =>
            {
                var request = JObject.Parse((string)a["creation"]);
                experience.CreateModel((string)request["id"], request["recipe"].ToString(Formatting.None));
                if (request["config"] is JObject config)
                    experience.Configure((string)request["id"], config.ToString(Formatting.None));
                return experience.Inspect();
            });
            yield return new Tool(world, "inspect_model", "Read the current named model's editable geometry. Use IDs from inspect_experience.", "id", main, a => experience.Models[(string)a["id"]].Creation.Inspect());
            yield return new Tool(world, "configure_model", "actor is a JSON string with id and config. Config has the same fields as configure_actor; this targets one independent model. Its command/input/goal paths come from inspect_experience.", "actor", main, a =>
            {
                var request = JObject.Parse((string)a["actor"]);
                return experience.Configure((string)request["id"], request["config"].ToString(Formatting.None));
            });
            yield return new Tool(world, "remove_model", "Remove one named model and its motor bindings; preserve other models. Inspect behaviors for any references that also need removal.", "id", main, a => { experience.RemoveModel((string)a["id"]); return experience.Inspect(); });
            yield return new Tool(world, "inspect_definition", "Read a managed source module by exact path from inspect_experience or query flecs.script.Script. RuntimeLibrary contains reusable templates; native APIs are not C++ code generation.", "path", main,
                a => new JObject { ["path"] = a["path"], ["code"] = world.Session.ManagedScriptSource((string)a["path"]) ?? "" }.ToString(Formatting.None));
            yield return new Tool(world, "apply_runtime_behavior", "Install/replace ONE independent behavior. definition is a JSON string with id and code. Stored as RuntimeBehaviors.id. Query/read the existing definition first. Use unique names and behavior-owned command entities; target existing model parts with PoseTarget. Native async/await and mut are supported. RuntimeLibrary.TogglePose accepts listener, target, degrees. Add Persistent to its INSTANCE to save mut state. Other behavior modules are preserved. Use observe_over_time on the actual target to verify effects.", "definition", main, a =>
            {
                var definition = JObject.Parse((string)a["definition"]);
                return experience.ApplyBehavior((string)definition["id"], (string)definition["code"]);
            });
            yield return new Tool(world, "remove_runtime_behavior", "Delete ONE behavior module and its owned command entities. Other modules remain. Direct values previously written to existing entities may persist; restore them explicitly if needed.", "id", main,
                a => { experience.RemoveBehavior((string)a["id"]); return experience.Inspect(); });
            yield return new Tool(world, "edit_definition", "Patch one managed-source component expression while preserving surrounding source. edit is a JSON string with source (module path), entity (full path), component (type path), expression (Flecs expression such as {0,1,0,1}). For ongoing expression edits use this or replace that independent behavior. Not transactional.", "edit", main, a =>
            {
                var edit = JObject.Parse((string)a["edit"]);
                world.Session.EditScript((string)edit["source"], (string)edit["entity"], (string)edit["component"], (string)edit["expression"]);
                experience.SyncNow();
                return experience.Inspect();
            });
            yield return new Tool(world, "undo_definition", "Undo the last upstream source edit for one managed module. This is source undo, not reversal of physical simulation or arbitrary side effects.", "path", main,
                a => { world.Session.UndoScript((string)a["path"]); experience.SyncNow(); return experience.Inspect(); });
            yield return new Tool(world, "save_experience", "Save the current local experience when asked. Includes authored sources/models/configs, motor positions, input and selected state. Persistent-tagged instances save template mut; PersistState{components:JSON-array-string} explicitly selects extra data. Pending awaits restart on reload.", null, main,
                _ => { experience.Save(); return "{\"saved\":true}"; });
            yield return new Tool(world, "load_experience", "Replace the current experience with its last local save when the user asks to resume/reload. Definitions are validated in an isolated world first. Pending async operations restart; physical state resumes from saved actor positions.", null, main,
                _ => { experience.Load(File.ReadAllText(RuntimeExperience.SavePath)); return experience.Inspect(); });
            yield return new Tool(world, "snapshot_experience", "Keep the current experience in memory so rewind_experience can return to it.", null, main,
                _ => { experience.Snapshot(); return "{\"snapshot\":true}"; });
            yield return new Tool(world, "rewind_experience", "Return to the state kept by snapshot_experience.", null, main,
                _ => experience.Rewind() ? experience.Inspect() : "{\"error\":\"No snapshot.\"}");
        }
        private sealed class Tool : IAgentTool
        {
            public string Name
            {
                get;
            }
            public string Description
            {
                get;
            }
            public string InputJsonSchema
            {
                get;
            }
            private readonly FlecsWorld world;
            private readonly AgentMainThread main;
            private readonly Func<JObject, string> action;
            private readonly string field;
            public Tool(FlecsWorld world, string name, string description, string field, AgentMainThread main, Func<JObject, string> action)
            {
                this.world = world;
                Name = name;
                Description = description;
                this.field = field;
                this.main = main;
                this.action = action;
                var properties = new JObject();
                var required = new JArray();
                if (field != null)
                {
                    properties[field] = new JObject { ["type"] = "string" };
                    required.Add(field);
                }
                InputJsonSchema = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false }.ToString(Formatting.None);
            }
            public Task<string> ExecuteAsync(string json, CancellationToken cancellation)
            {
                var args = JObject.Parse(json);
                if (field != null && (args[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args[field])))
                    throw new ArgumentException("Missing argument: " + field);
                return main.RunAsync(() => world.Session.EnteredWorld != 0 ? "{\"error\":\"Not available inside an entered world.\"}" : action(args), cancellation);
            }
        }
    }
}
