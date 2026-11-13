#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Runtime.Capabilities;
namespace Game
{
    // Builds and reads saved-world documents (engine format 2) without a session, and reads the version 1 record.
    public static class GalleryWorldDocument
    {
        public const string Kind = "GallerySharedShape";
        public const int Format = 2;
        public static readonly string[] Components = { "Position", "Rotation", "Scale", "Color", "Primitive", "Spin" };
        public static string Root(string id) => "worlds." + id;
        public static bool ValidId(string id) => Regex.IsMatch(id ?? "", "^GalleryCreation[1-9][0-9]{0,8}$");
        // The version 1 record kept its world under a private root and its behavior sources under a module prefix.
        static string V1Scope(string id) => id + "Content";
        static string V1Owned(string id) => "GalleryWorld_" + id + ".";
        static JObject V(double x, double y, double z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
        // The values a shared shape has when its record does not write a component.
        static JObject Default(string type) => type switch
        {
            "Scale" => V(1, 1, 1),
            "Color" => new JObject { ["r"] = .8, ["g"] = .88, ["b"] = .79, ["a"] = 1 },
            "Primitive" => new JObject { ["kind"] = 1 },
            _ => V(0, 0, 0)
        };
        /// <summary>Checks the world id and the shape rows (names, components, ranges). Throws ArgumentException.</summary>
        public static void ValidateShapes(string id, JArray objects)
        {
            if (!ValidId(id) || objects.Count > 64)
                throw new ArgumentException("Invalid saved world");
            var names = new HashSet<string>();
            foreach (JObject o in objects)
            {
                if (!Regex.IsMatch((string)o["name"] ?? "", "^[A-Za-z][A-Za-z0-9_]{0,47}$") || !names.Add((string)o["name"]))
                    throw new ArgumentException("Invalid object name");
                foreach (string type in Components)
                {
                    if (o[type] is not JObject component)
                        throw new ArgumentException("Missing object component");
                    string expected = type == "Color" ? "rgba" : type == "Primitive" ? "kind" : "xyz";
                    var keys = type == "Primitive" ? new[] { "kind" } : expected.Select(c => c.ToString()).ToArray();
                    if (component.Count != keys.Length || component.Properties().Any(p => !keys.Contains(p.Name)))
                        throw new ArgumentException("Unexpected object member");
                    foreach (var p in component.Properties())
                        if (!float.IsFinite((float)p.Value) || Math.Abs((float)p.Value) > 10000)
                            throw new ArgumentException("Invalid object value");
                }
                if ((int)o["Primitive"]["kind"] < 0 || (int)o["Primitive"]["kind"] > 5)
                    throw new ArgumentException("Unsupported shape");
            }
        }
        /// <summary>A new document: the world record plus one shared shape per row of <paramref name="objects"/>.</summary>
        public static string FromShapes(string id, JArray objects) => Build(id, objects).ToString(Formatting.None);
        static JObject Build(string id, JArray objects)
        {
            ValidateShapes(id, objects);
            var entities = new JArray { new JObject { ["path"] = "", ["components"] = new JObject { ["Position"] = V(0, 0, 0) } } };
            foreach (JObject shape in objects)
            {
                var components = new JObject();
                foreach (string type in Components)
                    components[type] = shape[type].DeepClone();
                entities.Add(new JObject { ["path"] = (string)shape["name"], ["kinds"] = new JArray(Kind), ["components"] = components });
            }
            return new JObject { ["format"] = Format, ["id"] = id, ["root"] = Root(id), ["sources"] = new JArray(), ["entities"] = entities };
        }
        /// <summary>Returns the document with one more shared shape record (a row as <see cref="Features"/> returns it).</summary>
        public static string Append(string document, JObject shape)
        {
            var parsed = JObject.Parse(document);
            var components = new JObject();
            foreach (string type in Components)
                components[type] = shape[type].DeepClone();
            ((JArray)parsed["entities"]).Add(new JObject { ["path"] = (string)shape["name"], ["kinds"] = new JArray(Kind), ["components"] = components });
            return parsed.ToString(Formatting.None);
        }
        /// <summary>The shape rows of a document: top-level records of the shared shape kind, with the six components filled in.</summary>
        public static JArray Features(string document)
        {
            var rows = new JArray();
            if (string.IsNullOrEmpty(document))
                return rows;
            if (JObject.Parse(document)["entities"] is not JArray entities)
                return rows;
            foreach (JObject record in entities.OfType<JObject>())
            {
                string path = (string)record["path"];
                if (string.IsNullOrEmpty(path) || path.Contains('.') || !(record["kinds"] is JArray kinds && kinds.Any(k => (string)k == Kind)))
                    continue;
                var shape = new JObject { ["name"] = path };
                foreach (string type in Components)
                    shape[type] = record["components"]?[type]?.DeepClone() ?? Default(type);
                rows.Add(shape);
            }
            return rows;
        }
        /// <summary>
        /// Converts a version 1 record (objects, graph) to a version 2 row: { selected, revision, state, document }.
        /// Behavior sources cannot be converted statically; they stay in the document as "legacySources" and run once after the first load.
        /// </summary>
        public static JObject FromV1(string id, JObject record)
        {
            var objects = JArray.Parse((string)record["objects"]);
            string graphText = (string)record["graph"];
            var graph = string.IsNullOrEmpty(graphText) ? null : JObject.Parse(graphText);
            ValidateV1(id, objects, graph);
            var document = Build(id, objects);
            var state = new JObject();
            if (graph != null)
            {
                string oldRoot = V1Scope(id), oldPrefix = oldRoot + ".", newPrefix = Root(id) + ".";
                string Rewrite(string text) => text == oldRoot ? Root(id) : text.StartsWith(oldPrefix, StringComparison.Ordinal) ? newPrefix + text.Substring(oldPrefix.Length) : text;
                JToken Walk(JToken token)
                {
                    switch (token)
                    {
                        case JObject o:
                            var copy = new JObject();
                            foreach (var p in o.Properties())
                                copy[p.Name] = Walk(p.Value);
                            return copy;
                        case JArray a:
                            return new JArray(a.Select(Walk));
                        case JValue v when v.Type == JTokenType.String:
                            return Rewrite((string)v);
                        default:
                            return token.DeepClone();
                    }
                }
                var entities = (JArray)document["entities"];
                var byPath = entities.Cast<JObject>().ToDictionary(e => (string)e["path"]);
                JObject Record(string path)
                {
                    if (byPath.TryGetValue(path, out var existing))
                        return existing;
                    // A parent that holds no data of its own still has to be listed before its children.
                    int dot = path.LastIndexOf('.');
                    if (dot > 0)
                        Record(path.Substring(0, dot));
                    var made = new JObject { ["path"] = path };
                    entities.Add(made);
                    byPath[path] = made;
                    return made;
                }
                foreach (JObject row in ((JArray)graph["entities"]).Cast<JObject>().OrderBy(e => ((string)e["path"]).Count(c => c == '.')))
                {
                    string old = (string)row["path"];
                    string relative = old == oldRoot ? "" : old.Substring(oldPrefix.Length);
                    var target = Record(relative);
                    if (row["components"] is JObject components && components.HasValues)
                    {
                        var merged = target["components"] as JObject ?? new JObject();
                        foreach (var p in components.Properties())
                            merged[p.Name] = Walk(p.Value);
                        target["components"] = merged;
                    }
                    if (row["dependsOn"] is JArray dependsOn && dependsOn.Count > 0)
                    {
                        var pairs = target["pairs"] as JArray ?? new JArray();
                        foreach (var t in dependsOn)
                        {
                            string text = (string)t;
                            pairs.Add(new JArray("CapDependsOn", text.StartsWith(oldPrefix, StringComparison.Ordinal) ? "~" + text.Substring(oldPrefix.Length) : text));
                        }
                        target["pairs"] = pairs;
                    }
                }
                if (graph["state"] is JObject saved)
                    foreach (var p in saved.Properties())
                        state[Rewrite(p.Name.Split('|')[0]) + (p.Name.Contains('|') ? "|" + p.Name.Substring(p.Name.IndexOf('|') + 1) : "")] = p.Value.DeepClone();
                if (graph["sources"] is JArray sources && sources.Count > 0)
                    document["legacySources"] = new JArray(sources.Select(s => new JObject { ["path"] = (string)s["path"], ["code"] = (string)s["code"] }));
            }
            return new JObject { ["selected"] = (string)record["selected"] ?? "", ["revision"] = (int?)record["revision"] ?? 0, ["state"] = state, ["document"] = document };
        }
        /// <summary>The v1 reader's checks: the graph document, then the shapes.</summary>
        public static void ValidateV1(string id, JArray objects, JObject graph)
        {
            string invalidGraph = CapabilityGraph.Validate(graph, V1Scope(id), V1Owned(id));
            if (invalidGraph != null)
                throw new ArgumentException(invalidGraph);
            ValidateShapes(id, objects);
        }
        /// <summary>
        /// Scripts a migrated document still has to run inside its world: each version 1 source without its
        /// <c>&lt;id&gt;Content { }</c> wrapper, with names made relative to the world. Empty when there are none.
        /// </summary>
        public static List<(string Path, string Script)> LegacyScripts(string id, string document)
        {
            var result = new List<(string, string)>();
            if (string.IsNullOrEmpty(document) || !document.Contains("legacySources"))
                return result;
            if (JObject.Parse(document)["legacySources"] is not JArray sources)
                return result;
            string scope = V1Scope(id);
            foreach (var source in sources)
            {
                string code = (string)source["code"] ?? "";
                var wrapper = Regex.Match(code, @"^\s*" + Regex.Escape(scope) + @"\s*\{");
                if (wrapper.Success)
                {
                    int end = code.LastIndexOf('}');
                    code = end > wrapper.Length ? code.Substring(wrapper.Length, end - wrapper.Length) : "";
                }
                result.Add(((string)source["path"], code.Replace(scope + ".", "")));
            }
            return result;
        }
    }
}
#endif
