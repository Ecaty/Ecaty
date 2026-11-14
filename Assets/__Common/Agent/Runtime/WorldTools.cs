using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Agents;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    // The tools of the AI inside an entered world. Every call has a schema, acts on that world only and answers with
    // one JSON object: ok, the tool's own fields, error (with line and column when known) and the world's alerts.
    // Paths in answers are relative to the world. A tool that changes the world keeps a checkpoint first. A change
    // that was refused whole drops it; one that failed part way undoes to it.
    public static class WorldTools
    {
        private static readonly Regex Name = new(@"\A[A-Za-z_][A-Za-z0-9_]{0,47}\z", RegexOptions.CultureInvariant);
        private static readonly Regex Path = new(@"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z", RegexOptions.CultureInvariant);

        public static IReadOnlyList<IAgentTool> Create(FlecsWorld world, AgentMainThread main)
        {
            var tools = new List<IAgentTool>();
            void Add(string name, string description, JObject properties, string[] required, Action<Call, JObject> action) =>
                tools.Add(new Tool(world, main, name, description, properties, required, action));

            Add("describe", "Describe a kind or a type: members, ranges and meaning.",
                Props("name", Text("A kind name, a path or a type name.")), new[] { "name" }, Describe);
            Add("list_kinds", "List the kinds of this world and of the library.",
                Props(), new string[0], ListKinds);
            Add("query", "List the entities of this world that match a query expression.",
                Props("expression", Text("Components to match, e.g. Position, ?Color."), "limit", Whole("Most rows to return (default 100).")), new[] { "expression" }, Query);
            Add("inspect", "Read one entity of this world with its components.",
                Props("entity", Text("Path inside the world.")), new[] { "entity" }, Inspect);
            Add("spawn", "Make an instance of a kind and set its values.",
                Props("kind", Text("Kind name or path."), "name", Text("Name of the new entity."), "parent", Text("Path of the parent inside the world."),
                    "values", new JObject { ["type"] = "object", ["description"] = "Component name to value object, e.g. {\"Position\":{\"x\":0,\"y\":1,\"z\":0}}.", ["additionalProperties"] = new JObject { ["type"] = "object" } }),
                new[] { "kind" }, Spawn);
            Add("set", "Set members of one component of an entity; members left out keep their value.",
                Props("entity", Text("Path inside the world."), "component", Text("Component name."), "value", new JObject { ["type"] = "object", ["description"] = "The members to set, e.g. {\"x\":2}." }),
                new[] { "entity", "component", "value" }, SetValue);
            Add("remove", "Remove a component from an entity, or the whole entity when no component is given.",
                Props("entity", Text("Path inside the world."), "component", Text("Component name.")), new[] { "entity" }, Remove);
            Add("define_kind", "Define or replace a kind with Script text; empty code removes it.",
                Props("name", Text("Kind name."), "code", Text("Script that declares the kind.")), new[] { "name", "code" }, DefineKind);
            Add("undo_kind", "Restore the previous text of a kind.",
                Props("name", Text("Kind name.")), new[] { "name" }, UndoKind);
            Add("add_rule", "Add one rule: when something is noticed, the thens run in order.",
                Props("name", Text("Name of the rule."), "owner", Text("Kind or element the rule belongs to, relative to the world."),
                    "when", Text("What is noticed, e.g. Touches."), "that", Text("For Touches: the kind or element touched."),
                    "count", Text("For Reaches: the count watched."), "n", Number("For Reaches: the value."), "seconds", Number("For Every: seconds between firings."),
                    "thens", new JObject
                    {
                        ["type"] = "array",
                        ["description"] = "Things to do, in order.",
                        ["items"] = Closed(Props("verb", Text("What to do, e.g. Counts."), "target", Text("That or This."), "kind", Text("For Makes: the kind."),
                            "count", Text("For Counts: the count."), "by", Number("For Counts: how much.")), new[] { "verb" })
                    }),
                new[] { "name", "owner", "when", "thens" }, AddRule);
            Add("apply_script", "Apply Script to this world after a dry run.",
                Props("code", Text("The Script text.")), new[] { "code" }, ApplyScript);
            Add("play", "Start Play.", Props(), new string[0], (c, r) => { c.Session.PlayWorld(c.Entered); r["playing"] = true; });
            Add("stop", "Stop Play and return to the state before it.", Props(), new string[0], Stop);
            Add("undo", "Undo the last change to the world.", Props(), new string[0], Undo);
            Add("alerts", "List what is wrong in the world.", Props(), new string[0], (c, r) => { });
            return tools;
        }

        // ---- schema helpers

        private static JObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
        private static JObject Number(string description) => new() { ["type"] = "number", ["description"] = description };
        private static JObject Whole(string description) => new() { ["type"] = "integer", ["description"] = description };
        private static JObject Props(params object[] pairs)
        {
            var properties = new JObject();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                properties[(string)pairs[i]] = (JObject)pairs[i + 1];
            return properties;
        }
        private static JObject Closed(JObject properties, string[] required) =>
            new() { ["type"] = "object", ["properties"] = properties, ["required"] = new JArray(required), ["additionalProperties"] = false };

        // ---- the world a call acts on

        internal sealed class Failure : Exception
        {
            public int Line, Column;
            public bool Refused; // the engine refused the change whole: the world is as it was
            public Failure(string message, int line = 0, int column = 0, bool refused = false) : base(message)
            {
                Line = line;
                Column = column;
                Refused = refused;
            }
        }

        internal sealed class Call
        {
            public readonly FlecsWorld World;
            public readonly JObject Args;
            public FlecsSession Session => World.Session;
            public ulong Entered => Session.EnteredWorld;
            private string prefix;
            public string Prefix => prefix ??= "worlds." + Session.EntityName(Entered); // an undo gives the world a new entity, not a new name
            public Call(FlecsWorld world, JObject args)
            {
                World = world;
                Args = args;
            }

            public string Str(string name)
            {
                if (Args[name]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)Args[name]))
                    throw new Failure("Missing argument: " + name + ".");
                return ((string)Args[name]).Trim();
            }
            public string OptStr(string name) => Args[name]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)Args[name]) ? ((string)Args[name]).Trim() : null;

            // The full path of an entity named inside the world; refuses a path that points outside it.
            public string Full(string path)
            {
                if (path.Contains("::") || path.Split('.').Any(s => s.Trim().Length == 0))
                    throw new Failure("Invalid path: " + path + ".");
                string prefix = Prefix;
                if (path == prefix || path.StartsWith(prefix + ".", StringComparison.Ordinal))
                    return path;
                string first = path.Split('.')[0];
                if (first == "worlds" || first == "library")
                    throw new Failure("Path is outside the entered world: " + path + ".");
                return prefix + "." + path;
            }

            public string Relative(string path)
            {
                string prefix = Prefix;
                if (path == prefix)
                    return "";
                return path.StartsWith(prefix + ".", StringComparison.Ordinal) ? path.Substring(prefix.Length + 1) : path;
            }

            // Every string in a result that names something inside the world becomes relative to it.
            public JToken Relative(JToken token)
            {
                string prefix = Prefix;
                foreach (var value in (token is JContainer container ? container.DescendantsAndSelf() : new[] { token }).OfType<JValue>().Where(v => v.Type == JTokenType.String).ToList())
                {
                    string text = (string)value;
                    if (text == prefix || text.StartsWith(prefix + ".", StringComparison.Ordinal))
                        value.Value = Relative(text);
                }
                return token;
            }

            // Keeps a checkpoint and runs the change. A change that was refused whole only drops the checkpoint;
            // one that failed part way undoes to it, which gives every entity of the world a new id.
            public void Change(Action change)
            {
                ulong world = Entered;
                Session.CheckpointWorld(world);
                try
                {
                    change();
                }
                catch (Failure failure) when (failure.Refused)
                {
                    Session.DropCheckpoint(world);
                    throw;
                }
                catch (Exception)
                {
                    Session.TryUndoWorld(world, out _, out _);
                    World.SyncViews();
                    throw;
                }
                World.SyncViews();
            }

            public bool DryRun(string code)
            {
                if (Session.TryDryRunWorld(Entered, code, out string error, out int line, out int column))
                    return true;
                throw new Failure(error, line, column);
            }

            public void Run(string code)
            {
                if (!Session.TryRunWorldScript(Entered, code, out string error, out int line, out int column))
                    throw new Failure(error, line, column);
            }
        }

        // ---- the tools

        private static void Describe(Call c, JObject result)
        {
            string name = c.Str("name");
            string prefix = c.Prefix;
            var tries = new List<string>();
            if (!name.Contains('.'))
            {
                tries.Add(prefix + ".kinds." + name);
                tries.Add("library." + name);
                tries.Add(name);
            }
            else if (name == "library" || name.StartsWith("library.", StringComparison.Ordinal))
                tries.Add(name);
            else if (name.StartsWith("worlds.", StringComparison.Ordinal))
                tries.Add(c.Full(name));
            else
            {
                tries.Add(c.Full(name));
                tries.Add(name);
            }
            foreach (string path in tries.Distinct())
            {
                string json = c.Session.Describe(path);
                if (json != null)
                {
                    result["description"] = c.Relative(JToken.Parse(json));
                    return;
                }
            }
            throw new Failure("Nothing is named " + name + ". Use list_kinds for kinds.");
        }

        private static void ListKinds(Call c, JObject result)
        {
            result["world"] = c.Relative(JToken.Parse(c.Session.Kinds(c.Entered) ?? "[]"));
            result["library"] = c.Relative(JToken.Parse(c.Session.Kinds(0) ?? "[]"));
        }

        private static void Query(Call c, JObject result)
        {
            string expression = c.Str("expression");
            int limit = c.Args["limit"]?.Type == JTokenType.Integer ? Math.Clamp((int)c.Args["limit"], 1, 500) : 100;
            string json = c.Session.QueryWorld(c.Entered, expression, limit);
            if (json.Length > 60000)
                throw new Failure("Too many matches. Add a required component or lower the limit.");
            result["result"] = c.Relative(JToken.Parse(json));
        }

        private static void Inspect(Call c, JObject result)
        {
            string full = c.Full(c.Str("entity"));
            if (c.Session.Lookup(full) == 0)
                throw new Failure("No entity " + c.Relative(full) + " in this world.");
            result["entity"] = c.Relative(JToken.Parse(c.Session.EntityJson(full)));
        }

        private static ulong FindKind(Call c, string kind, out string path)
        {
            var tries = new List<string>();
            if (!kind.Contains('.'))
            {
                tries.Add(c.Prefix + ".kinds." + kind);
                tries.Add("library." + kind);
            }
            else if (kind.StartsWith("library.", StringComparison.Ordinal))
                tries.Add(kind);
            else
                tries.Add(c.Full(kind));
            foreach (string candidate in tries)
            {
                ulong id = c.Session.Lookup(candidate);
                if (id != 0 && c.Session.Describe(candidate) is string json && (string)JObject.Parse(json)["is"] == "kind")
                {
                    path = candidate;
                    return id;
                }
            }
            path = null;
            return 0;
        }

        private static void Spawn(Call c, JObject result)
        {
            string kindName = c.Str("kind");
            ulong kind = FindKind(c, kindName, out string kindPath);
            if (kind == 0)
                throw new Failure("No kind named " + kindName + ". Use list_kinds.");
            string name = c.OptStr("name");
            if (name != null && !Name.IsMatch(name))
                throw new Failure("Use a simple name for the entity.");
            string parentPath = c.OptStr("parent") is string given ? c.Full(given) : c.Prefix;
            ulong parent = c.Session.Lookup(parentPath);
            if (parent == 0)
                throw new Failure("No parent " + c.Relative(parentPath) + " in this world.");
            if (name == null)
            {
                string stem = kindPath.Substring(kindPath.LastIndexOf('.') + 1);
                stem = char.ToLowerInvariant(stem[0]) + stem.Substring(1);
                int n = 1;
                while (c.Session.Lookup(parentPath + "." + stem + n) != 0)
                    n++;
                name = stem + n;
            }
            else if (c.Session.Lookup(parentPath + "." + name) != 0)
                throw new Failure("There is already an entity named " + name + " there.");
            var values = c.Args["values"] as JObject;
            if (values != null)
                foreach (var property in values.Properties())
                    if (!Path.IsMatch(property.Name) || property.Value.Type != JTokenType.Object)
                        throw new Failure("values: " + property.Name + " needs a component name and an object value.");
            var fitted = new List<KeyValuePair<string, JObject>>();
            if (values != null)
                foreach (var property in values.Properties())
                    fitted.Add(new KeyValuePair<string, JObject>(property.Name, Fitted(c, property.Name, (JObject)property.Value)));
            string entityPath = parentPath + "." + name;
            c.Change(() =>
            {
                ulong entity = c.Session.Spawn(kind, parent, name);
                entityPath = c.Session.EntityPath(entity) ?? entityPath;
                foreach (var item in fitted)
                    SetOne(c, entityPath, item.Key, item.Value, false); // the instance is already there: undo it
            });
            result["entity"] = c.Relative(entityPath);
            result["kind"] = c.Relative(kindPath);
        }

        // Checks a value against the description of its type before the engine sees it, so that a mistake is answered
        // with what would be right. Members left out keep their value. An entity named relative to the world gets its full path.
        private static JObject Fitted(Call c, string component, JObject value)
        {
            string json = c.Session.Lookup(component) != 0 ? c.Session.Describe(component) : null;
            var description = json == null ? null : JObject.Parse(json);
            if (description == null)
                throw new Failure("Unknown component: " + component + ". Use describe.");
            if ((string)description["is"] == "tag")
                throw new Failure(component + " is a tag without a value. Add it with apply_script: name { " + component + " }.");
            if ((string)description["is"] != "component")
                throw new Failure(component + " is not a component. Use describe.");
            var fitted = (JObject)value.DeepClone();
            Fit(c, description["members"] as JArray, fitted, component);
            return fitted;
        }

        private static string MemberNames(JArray members) => string.Join(", ", (members ?? new JArray()).OfType<JObject>().Select(m => (string)m["name"]));

        private static void Fit(Call c, JArray members, JObject value, string label)
        {
            foreach (var property in value.Properties().ToList())
            {
                var member = members?.OfType<JObject>().FirstOrDefault(m => (string)m["name"] == property.Name);
                if (member == null)
                    throw new Failure(label + " has no member " + property.Name + ". Members: " + MemberNames(members) + ".");
                string path = label + "." + property.Name;
                if (member["count"] != null)
                {
                    int count = (int)member["count"];
                    if (!(property.Value is JArray list) || list.Count > count)
                        throw new Failure(path + " must be an array of at most " + count + " items.");
                    for (int i = 0; i < list.Count; i++)
                        list[i] = FitOne(c, member, list[i], path + "[" + i + "]");
                }
                else
                    property.Value = FitOne(c, member, property.Value, path);
            }
        }

        private static JToken FitOne(Call c, JObject member, JToken token, string path)
        {
            if (member["members"] is JArray inner)
            {
                if (!(token is JObject nested))
                    throw new Failure(path + " must be an object with: " + MemberNames(inner) + ".");
                Fit(c, inner, nested, path);
                return nested;
            }
            if (member["values"] is JArray values)
            {
                if (token.Type != JTokenType.String || !values.Values<string>().Contains((string)token))
                    throw new Failure(path + " must be one of: " + string.Join(", ", values.Values<string>()) + ".");
                return token;
            }
            switch ((string)member["type"] ?? "")
            {
                case "flecs.meta.bool":
                    if (token.Type != JTokenType.Boolean)
                        throw new Failure(path + " must be true or false.");
                    return token;
                case "flecs.meta.string":
                    if (token.Type != JTokenType.String)
                        throw new Failure(path + " must be a text.");
                    return token;
                case "flecs.meta.entity":
                case "flecs.meta.id":
                    if (token.Type != JTokenType.String)
                        throw new Failure(path + " must name an entity, or be empty for none.");
                    string name = ((string)token).Trim();
                    if (name.Length == 0 || name == "0" || name == "#0")
                        return "#0";
                    string full = name.StartsWith("library.", StringComparison.Ordinal) ? name : c.Full(name);
                    if (c.Session.Lookup(full) == 0)
                        throw new Failure(path + ": nothing is named " + name + " in this world.");
                    return full;
                default:
                    if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                        throw new Failure(path + " must be a number.");
                    if (member["min"] != null && member["max"] != null)
                    {
                        double number = (double)token, min = (double)member["min"], max = (double)member["max"];
                        if (min > -1e30 && max < 1e30 && (number < min || number > max))
                            throw new Failure(path + " must be between " + min.ToString("0.###", CultureInfo.InvariantCulture) + " and " + max.ToString("0.###", CultureInfo.InvariantCulture) + ".");
                    }
                    return token;
            }
        }

        // The engine sets a component whole or not at all.
        private static void SetOne(Call c, string entityPath, string component, JObject value, bool refused)
        {
            try
            {
                c.Session.SetComponentJson(entityPath, component, value.ToString(Formatting.None));
            }
            catch (InvalidOperationException)
            {
                throw new Failure("Cannot set " + component + " on " + c.Relative(entityPath) + ". Check the value against describe(\"" + component + "\").", refused: refused);
            }
        }

        private static void SetValue(Call c, JObject result)
        {
            string full = c.Full(c.Str("entity"));
            string component = c.Str("component");
            if (!Path.IsMatch(component))
                throw new Failure("Invalid component name: " + component + ".");
            if (!(c.Args["value"] is JObject value))
                throw new Failure("Missing argument: value.");
            if (c.Session.Lookup(full) == 0)
                throw new Failure("No entity " + c.Relative(full) + " in this world.");
            var fitted = Fitted(c, component, value);
            c.Change(() => SetOne(c, full, component, fitted, true));
            result["entity"] = c.Relative(full);
            result["component"] = component;
        }

        private static void Remove(Call c, JObject result)
        {
            string full = c.Full(c.Str("entity"));
            string component = c.OptStr("component");
            ulong entity = c.Session.Lookup(full);
            if (entity == 0)
                throw new Failure("No entity " + c.Relative(full) + " in this world.");
            string relative = c.Relative(full);
            if (relative.Length == 0 || relative == "kinds" || relative.StartsWith("kinds.", StringComparison.Ordinal) || relative == "sources" || relative.StartsWith("sources.", StringComparison.Ordinal))
                throw new Failure("This cannot be removed here. Kinds go with define_kind and empty code.");
            if (component == null)
            {
                c.Change(() =>
                {
                    if (c.Session.DeleteEntity(full) < 1)
                        throw new Failure("Could not remove " + relative + ".", refused: true);
                });
                result["removed"] = relative;
                return;
            }
            if (!Path.IsMatch(component))
                throw new Failure("Invalid component name: " + component + ".");
            ulong type = c.Session.Lookup(component);
            if (type == 0)
                throw new Failure("Unknown component: " + component + ".");
            c.Change(() =>
            {
                if (!c.Session.RemoveComponent(entity, type))
                    throw new Failure(relative + " has no " + component + ".", refused: true);
            });
            result["removed"] = relative + "." + component;
        }

        private static void DefineKind(Call c, JObject result)
        {
            string name = c.Str("name");
            if (!Name.IsMatch(name))
                throw new Failure("Use a simple kind name.");
            string code = c.Args["code"]?.Type == JTokenType.String ? (string)c.Args["code"] : throw new Failure("Missing argument: code.");
            string prefix = c.Prefix;
            bool removed = false;
            c.Change(() =>
            {
                if (!c.Session.TryApplySource(prefix + ".sources." + name, prefix + ".kinds", code, out removed, out string error, out int line, out int column))
                    throw new Failure(error, line, column, true); // a source is applied whole or not at all
            });
            result["kind"] = "kinds." + name;
            result["removed"] = removed;
        }

        private static void UndoKind(Call c, JObject result)
        {
            string name = c.Str("name");
            if (!Name.IsMatch(name))
                throw new Failure("Use a simple kind name.");
            string source = c.Prefix + ".sources." + name;
            c.Change(() =>
            {
                if (!c.Session.TryUndoSource(source, out string error))
                    throw new Failure(error, refused: true);
            });
            result["kind"] = "kinds." + name;
            result["text"] = c.Session.SourceText(source);
        }

        private static string PathArg(Call c, JObject source, string name, bool required)
        {
            var token = source[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required)
                    throw new Failure("Missing argument: " + name + ".");
                return null;
            }
            string text = token.Type == JTokenType.String ? ((string)token).Trim() : "";
            if (!Path.IsMatch(text))
                throw new Failure(name + " needs a name or a path relative to the world.");
            return text;
        }

        private static string Num(JObject source, string name) =>
            source[name] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? Convert.ToString((double)v, CultureInfo.InvariantCulture) : null;

        private static string RuleScript(Call c)
        {
            string name = c.Str("name");
            if (!Name.IsMatch(name))
                throw new Failure("Use a simple name for the rule.");
            var rule = new List<string> { "when: " + PathArg(c, c.Args, "when", true) };
            if (PathArg(c, c.Args, "that", false) is string that)
                rule.Add("that: " + that);
            if (PathArg(c, c.Args, "count", false) is string count)
                rule.Add("count: " + count);
            if (Num(c.Args, "n") is string n)
                rule.Add("n: " + n);
            if (Num(c.Args, "seconds") is string seconds)
                rule.Add("seconds: " + seconds);
            var thens = c.Args["thens"] as JArray ?? throw new Failure("Missing argument: thens.");
            if (thens.Count == 0 || thens.Count > 6)
                throw new Failure("thens needs one to six items.");
            var items = new List<string>();
            foreach (var item in thens)
            {
                var then = item as JObject ?? throw new Failure("Each of thens must be an object.");
                var parts = new List<string> { "verb: " + PathArg(c, then, "verb", true) };
                foreach (string field in new[] { "target", "kind", "count" })
                    if (PathArg(c, then, field, false) is string text)
                        parts.Add(field + ": " + text);
                if (Num(then, "by") is string by)
                    parts.Add("by: " + by);
                items.Add("{" + string.Join(", ", parts) + "}");
            }
            rule.Add("thens: [" + string.Join(", ", items) + "]");
            return "rules {\n  " + name + " { (RuleOf, " + PathArg(c, c.Args, "owner", true) + "); Rule: {" + string.Join(", ", rule) + "} }\n}\n";
        }

        private static void AddRule(Call c, JObject result)
        {
            string script = RuleScript(c);
            c.DryRun(script);
            c.Change(() => c.Run(script));
            result["rule"] = "rules." + c.Str("name");
        }

        private static void ApplyScript(Call c, JObject result)
        {
            string code = c.Str("code");
            c.DryRun(code);
            c.Change(() => c.Run(code));
            result["applied"] = true;
        }

        private static void Stop(Call c, JObject result)
        {
            if (!c.Session.TryStopWorld(c.Entered, out _, out string error))
                throw new Failure(error);
            c.World.SyncViews();
            result["playing"] = false;
        }

        private static void Undo(Call c, JObject result)
        {
            if (!c.Session.TryUndoWorld(c.Entered, out _, out string error))
                throw new Failure(error);
            c.World.SyncViews();
            result["undone"] = true;
        }

        // ---- the tool class

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
            private readonly Action<Call, JObject> action;
            public Tool(FlecsWorld world, AgentMainThread main, string name, string description, JObject properties, string[] required, Action<Call, JObject> action)
            {
                this.world = world;
                this.main = main;
                this.action = action;
                Name = name;
                Description = description;
                InputJsonSchema = Closed(properties, required).ToString(Formatting.None);
            }
            public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
            {
                var args = JObject.Parse(argumentsJson);
                return main.RunAsync(() => Run(args), cancellationToken);
            }

            private string Run(JObject args)
            {
                var call = new Call(world, args);
                var result = new JObject { ["ok"] = true };
                try
                {
                    if (call.Session.EnteredWorld == 0)
                        throw new Failure("Enter a world first.");
                    action(call, result);
                }
                catch (Failure failure)
                {
                    result = new JObject { ["ok"] = false, ["error"] = failure.Message };
                    if (failure.Line > 0)
                    {
                        result["line"] = failure.Line;
                        result["column"] = failure.Column;
                    }
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    result = new JObject { ["ok"] = false, ["error"] = error.Message };
                }
                result["alerts"] = Alerts(call);
                return result.ToString(Formatting.None);
            }

            // The alerts of the world as it is now; the world may have a new entity after an undo.
            private static JArray Alerts(Call call)
            {
                var list = new JArray();
                try
                {
                    ulong entered = call.Session.EnteredWorld;
                    if (entered == 0)
                        return list;
                    foreach (var row in JArray.Parse(call.Session.Alerts(entered) ?? "[]").OfType<JObject>())
                        list.Add(new JObject { ["entity"] = call.Relative((string)row["entity"] ?? ""), ["message"] = (string)row["message"] ?? (string)row["alert"] ?? "" });
                }
                catch (Exception) { }
                return list;
            }
        }
    }

    // The tools the AI gets: the world's while one is entered, the application's otherwise. Read for every request.
    public sealed class EnteredWorldTools : IReadOnlyList<IAgentTool>
    {
        private readonly FlecsSession session;
        private readonly IReadOnlyList<IAgentTool> outside, inside;
        public EnteredWorldTools(FlecsSession session, IReadOnlyList<IAgentTool> outside, IReadOnlyList<IAgentTool> inside)
        {
            this.session = session;
            this.outside = outside;
            this.inside = inside;
        }
        private IReadOnlyList<IAgentTool> Current => session.EnteredWorld != 0 ? inside : outside;
        public int Count => Current.Count;
        public IAgentTool this[int index] => Current[index];
        public IEnumerator<IAgentTool> GetEnumerator() => Current.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
