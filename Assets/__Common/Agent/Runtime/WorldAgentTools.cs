using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    public static class WorldAgentTools
    {
        public static IReadOnlyList<IAgentTool> Create(FlecsWorld world, AgentMainThread main, ClayCreation clay = null, DioramaRuntime engine = null)
        {
            var experience = world.GetComponent<RuntimeExperience>();
            var tools = new List<IAgentTool> {
                new Tool("query", "Inspect the live world with a native Flecs query. Example: Position, ?Primitive, ?Color, ?Scale, ?Spin, ?Presented. Query Clock for native time.", "expression", main,
                    a => Query(world, (string)a["expression"])),
                new Tool("inspect_entity", "Inspect an entity/component by full path with reflected types and values. Example: diorama.Ground", "path", main,
                    a => world.Session.EntityJson((string)a["path"])),
                new Tool("apply_script", "Apply one-time upstream Flecs Script edits. Example: scene { Cube { Color: {0, 0, 1, 1}; Scale: {2, 2, 2} } }. Use nested entity scopes, not dotted entity declarations. Separate statements by semicolons or newlines. Define runtime types as struct Temperature(value: f32). Use apply_behavior for ongoing motion or reactive rules. Errors are not transactional; inspect after failure.", "code", main,
                    a => { if (experience) experience.ApplyEdit((string)a["code"]); else world.Session.RunScript((string)a["code"]); world.SyncViews(); return Observe(world, engine); }),
                new Tool("observe_world", "Refresh actual presentation readback. Presented contains measured transforms/color and an applied frame counter. Photo actors are in runtime_actor with measured ActorMotion/PoseObserved. This is actual transform data, not screen perception.", null, main,
                    a => { world.SyncViews(); return Observe(world, engine); }),
                new ObserveOverTime(world, main)
            };
            if (clay != null && ClayCreation.Available)
            {
                tools.Add(new Tool("inspect_photo_object", "Read the current editable photo object and its named parts before changing it.", null, main,
                    _ => clay.Inspect()));
                tools.Add(new Tool("apply_photo_object",
                    "Create or edit a live MudBun object. Inspect before editing and preserve unrelated data and IDs. Supply a recipe object with version:4 and parts. Use shape strings and numeric arrays, never nested objects for shape or vectors. " +
                    "Every node has id, parent (pivot ID or null for the sole root), shape and localPosition. IDs start with a lowercase letter and use lowercase letters, digits or underscores. " +
                    "localPosition and localRotation are ALWAYS relative to the immediate parent. Axes are x right, y up, negative z toward the viewer. For example, head pivot [0,0.45,0], ear-base pivot [0.42,0.55,0] relative to head, ear brush [0,0.55,0] relative to ear base. " +
                    "Use shape pivot for an invisible MudBun brush with optional localRotation (Euler degrees). Only pivots may have children. " +
                    FormattableString.Invariant($"Ellipsoid and box leaves need full size ({ClayCreation.MinimumBrushSize} to {ClayCreation.MaximumBrushSize} on every axis for solids), and color (RGB 0 to 1). Omitted localRotation defaults to [0,0,0]. ") +
                    FormattableString.Invariant($"Optional operation dye paints flat markings without adding volume, with minimum size {ClayCreation.MinimumDyeSize}. Use it for drawn eyes, cheeks, whiskers, mouth lines and paw pads where appropriate. Put dyes after the solid brushes they color and place dye volumes across the surface. ") +
                    "Optional blend (0 to 0.25) adjusts edge softness. Optional finish is [metallic,smoothness,glow], each 0 to 1. If validation names invalid fields, correct them and retry the same recipe. " +
                    "Place pivots where the object actually bends. Attach each visible piece to the pivot that moves it. For articulated legs use distinct upper, knee and foot pivots in order, with visible pieces branching from their controlling pivot. " +
                    "Parent surface details to their moving part. Use meaningful rest axes where useful. This builds the hierarchy for procedural controls without configuring or starting an animator. The app validates, displays and saves it.", "recipe", main,
                    a => clay.Apply(PhotoRecipeInput.Read(a["recipe"])), PhotoRecipeInput.Schema()));
            }
            if (engine != null)
            {
                tools.Add(new Tool("inspect_capabilities", "Discover implemented runtime motion/pose commands, measured outputs and limitations before proposing a new kind of behavior. Flecs data alone does not imply a bound capability.", null, main,
                    _ => engine.Capabilities()));
                tools.Add(new Tool("inspect_actor", "Read runtime motor/animation configuration, actual ActorMotion and actual visual PoseObserved. Inspect the photo model for available pivot IDs.", null, main,
                    _ => engine.Inspect()));
                tools.Add(new Tool("configure_actor", "Configure the existing generated model at runtime. Pass a JSON patch as config. Fields: motion (ground/spatial), gravityScale (0..4; 0 disables gravity), facingYaw (-180..180 degrees rotating the created model so its front points along motor +Z; use 180 for a model drawn facing -Z), driver (idle/player/follow/script), speed 0.1 to 8, acceleration 0.1 to 40, spawn [x,y,z] used only when the motor is first created, hips (pivot ID), legs (array of [upper,knee,foot] pivot IDs), lean (a pivot below hips), boing (separate secondary branch root IDs). Supply real existing nonzero chains, not guessed names. Example: {\"driver\":\"player\",\"hips\":\"pelvis\",\"legs\":[[\"left_hip\",\"left_knee\",\"left_foot\"]],\"boing\":[\"ear_base\"]}. Settings become runtime Flecs ActorControl, ActorRig, MoveIntent and ActorMotion. Player driver accepts WASD/gamepad. Follow driver uses Goal.MoveGoal. Script driver reads ScriptInput.MoveIntent{x,z} for ground motion or ScriptInput.SpatialIntent{x,y,z} for spatial motion. These are normalized steering inputs. Use spatial plus gravityScale 0 for controlled hovering, with Script feedback from ActorMotion to hold a chosen height. Motor resolves ground/collisions and publishes actual state. Input and Goal can be updated with apply_script. Use apply_behavior for new ongoing control rules. Existing animation bindings remain when a patch changes only driver/speed.", "config", main,
                    a => engine.Configure((string)a["config"])));
            }
            if (experience)
                tools.AddRange(RuntimeExperienceTools.Create(world, experience, main));
            return tools;
        }

        private static string Query(FlecsWorld world, string expression)
        {
            string result = world.Session.QueryOnce(expression); // a model-written expression must not take a feature query slot
            return result.Length <= 60000 ? result : new JObject
            {
                ["error"] = "Too many matches. Optional-only queries include engine internals. Add a required component, e.g. Position, ?Primitive, ?Color, ?Scale; or inspect_entity with an exact entity path. Do not repeat this query unchanged."
            }.ToString(Formatting.None);
        }

        private static string Observe(FlecsWorld world, DioramaRuntime engine = null)
        {
            var result = JObject.Parse(world.Session.Query("Position, ?Primitive, ?Color, ?Scale, ?Spin, ?Presented"));
            if (engine != null && engine.Motor)
                result["runtime_actor"] = JObject.Parse(engine.Inspect());
            if (world.TryGetComponent<RuntimeExperience>(out var experience))
                result["experience"] = JObject.Parse(experience.Inspect());
            return result.ToString(Formatting.None);
        }

        private sealed class ObserveOverTime : IAgentTool
        {
            private readonly FlecsWorld world;
            private readonly AgentMainThread main;
            public string Name => "observe_over_time";
            public string Description => "Sample one entity's native state and measured Presented/PoseObserved state three times over about 0.4 seconds. Use after changing motion; compare the requested property and verify that Clock/frame advanced. Static readback is not proof of animation. This does not advance a paused world.";
            public string InputJsonSchema => "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"],\"additionalProperties\":false}";
            public ObserveOverTime(FlecsWorld world, AgentMainThread main)
            {
                this.world = world;
                this.main = main;
            }
            public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
            {
                var args = JObject.Parse(argumentsJson);
                if (args["path"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)args["path"]))
                    throw new ArgumentException("Missing tool argument: path");
                string path = (string)args["path"];
                var samples = new JArray();
                for (int i = 0; i < 3; i++)
                {
                    if (i != 0)
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    samples.Add(await main.RunAsync(() =>
                    {
                        world.SyncViews();
                        return new JObject
                        {
                            ["clock"] = JObject.Parse(world.Session.EntityJson("Clock"))["components"]?["Clock"],
                            ["entity"] = JObject.Parse(world.Session.EntityJson(path))
                        };
                    }, cancellationToken).ConfigureAwait(false));
                }
                long first = (long?)samples[0]["clock"]?["frame"] ?? 0;
                long last = (long?)samples[2]["clock"]?["frame"] ?? 0;
                var firstComponents = samples[0]["entity"]?["components"];
                var lastComponents = samples[2]["entity"]?["components"];
                var firstPose = firstComponents?["PoseObserved"] ?? firstComponents?["Presented"];
                var lastPose = lastComponents?["PoseObserved"] ?? lastComponents?["Presented"];
                bool measured = firstPose != null && lastPose != null;
                return new JObject
                {
                    ["path"] = path,
                    ["frames_advanced"] = last - first,
                    ["presentation_available"] = measured,
                    ["presentation_frames_advanced"] = measured ? (long)lastPose["frame"] - (long)firstPose["frame"] : 0,
                    ["verification"] = measured ? "Compare PoseObserved/Presented values with the request; native command changes alone do not prove scene motion."
                        : "No actual scene pose readback for this entity. Native values alone cannot verify a visible result.",
                    ["samples"] = samples
                }.ToString(Formatting.None);
            }
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
            private readonly string field;
            private readonly bool acceptsObject;
            private readonly AgentMainThread main;
            private readonly Func<JObject, string> action;
            public Tool(string name, string description, string field, AgentMainThread main, Func<JObject, string> action, JObject fieldSchema = null)
            {
                acceptsObject = fieldSchema != null;
                Name = name;
                Description = description;
                this.field = field;
                this.main = main;
                this.action = action;
                var properties = new JObject();
                var required = new JArray();
                if (field != null)
                {
                    properties[field] = fieldSchema ?? new JObject { ["type"] = "string" };
                    required.Add(field);
                }
                InputJsonSchema = new JObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false }.ToString(Formatting.None);
            }
            public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
            {
                var arguments = JObject.Parse(argumentsJson);
                if (field != null && !(acceptsObject && arguments[field] is JObject) &&
                    (arguments[field]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)arguments[field])))
                    throw new ArgumentException("Missing tool argument: " + field);
                return main.RunAsync(() => action(arguments), cancellationToken);
            }
        }
    }
}
