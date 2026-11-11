using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Ecaty.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ecaty
{
    [DefaultExecutionOrder(-200)]
    public sealed class ClayCreation : MonoBehaviour
    {
        public const string RootName = "PhotoClay";
        internal const double MinimumBrushSize = .12, MinimumDyeSize = .06, MaximumBrushSize = 3;
        private string ScriptName => EntityPath == RootName ? "ClayRecipe" : "Models." + EntityPath;
        private FlecsWorld owner;
        private string liveSignature;
        public string EntityPath { get; private set; } = RootName;
        public string LiveError
        {
            get; private set;
        }
        public void Initialize(FlecsWorld world, string path)
        {
            if (!Regex.IsMatch(path, @"\A[A-Za-z][A-Za-z0-9_]{0,47}\z"))
                throw new ArgumentException("Invalid model ID.");
            owner = world;
            EntityPath = path;
        }
        private static readonly Type RendererType = typeof(RuntimeMudRenderer);
        private static readonly Type SphereType = Type.GetType("MudBun.MudSphere, MudBun");
        private static readonly Type BoxType = Type.GetType("MudBun.MudBox, MudBun");
        private static readonly Type MaterialType = Type.GetType("MudBun.MudMaterial, MudBun");
        public static bool Available => RendererType != null && SphereType != null && BoxType != null && MaterialType != null;
        private static string SavePath => Path.Combine(Application.persistentDataPath, "Clay", "creation.json");

        private sealed class Brush
        {
            public Component Component;
            public int Kind;
            public JToken RestPose;
        }

        private readonly Dictionary<string, Transform> runtimeNodes = new Dictionary<string, Transform>(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, Transform> RuntimeNodes => runtimeNodes;
        public Transform PresentationRoot => surface ? surface.transform : null;
        public event Action Rebuilding, Rebuilt;
        public Transform FindPart(string id) => brushes.TryGetValue(id, out var brush) && brush.Component ? brush.Component.transform : null;

        public Bounds WorldBounds()
        {
            var local = LocalBounds();
            var result = new Bounds();
            bool first = true;
            if (!PresentationRoot)
                return result;
            for (int i = 0; i < 8; i++)
            {
                var p = PresentationRoot.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (first)
                {
                    result = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                    result.Encapsulate(p);
            }
            return result;
        }
        public Bounds LocalBounds()
        {
            var bounds = new Bounds();
            bool first = true;
            if (!surface)
                return bounds;
            foreach (var brush in brushes.Values)
            {
                if (brush.Kind == 2 || !brush.Component ||
                    brush.Component.GetType().GetProperty("Operator").GetValue(brush.Component).ToString() == "Dye")
                    continue;
                var matrix = surface.transform.worldToLocalMatrix * brush.Component.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? -.5f : .5f,
                        (i & 2) == 0 ? -.5f : .5f, (i & 4) == 0 ? -.5f : .5f));
                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                        bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        private FlecsSession Session => (owner ? owner : GetComponent<FlecsWorld>()).Session;
        private JObject accepted;
        private GameObject surface;
        private Behaviour generator;
        private readonly Dictionary<string, Brush> brushes = new Dictionary<string, Brush>(StringComparer.Ordinal);

        public string Inspect() => accepted?.ToString(Formatting.None) ?? "{\"version\":4,\"parts\":[]}";
        public bool SaveAcceptedRecipes { get; set; } = true;
        public string Apply(string json) => Apply(json, SaveAcceptedRecipes ? SavePath : null);

        public string Apply(string json, string path)
        {
            var recipe = Validate(json);
            if (!Available || !SystemInfo.supportsComputeShaders)
                throw new InvalidOperationException("Photo creation needs MudBun and a supported graphics device.");
            string staged = null;
            if (path != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            }

            var previous = accepted;
            GetComponent<RuntimePoseBridge>()?.Restore();
            Rebuilding?.Invoke();
            try
            {
                if (staged != null)
                    File.WriteAllText(staged, recipe.ToString(Formatting.Indented));
                Install(recipe);
                if (staged != null)
                {
                    if (File.Exists(path))
                        File.Replace(staged, path, null);
                    else
                        File.Move(staged, path);
                }
                accepted = recipe;
                Rebuilt?.Invoke();
                return new JObject
                {
                    ["saved"] = path != null,
                    ["brushes"] = brushes.Values.Count(brush => brush.Kind != 2),
                    ["pivots"] = brushes.Values.Count(brush => brush.Kind == 2)
                }.ToString(Formatting.None);
            }
            catch
            {
                if (previous != null)
                {
                    Install(previous);
                    Rebuilt?.Invoke();
                }
                else
                {
                    Session.SetManagedScript(ScriptName, "");
                    if (surface)
                        surface.SetActive(false);
                }
                throw;
            }
            finally { if (staged != null && File.Exists(staged)) File.Delete(staged); }
        }

        private static JObject Validate(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > 16000)
                throw new ArgumentException("Invalid photo recipe size.");
            var value = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            int version = value["version"]?.Type == JTokenType.Integer ? (int)value["version"] : 0;
            if (version < 1 || version > 4 || value.Count != (version == 2 ? 3 : 2) ||
                !(value["parts"] is JArray parts) || parts.Count < 1 || parts.Count > (version >= 3 ? 64 : 24))
                throw new ArgumentException("Expected a photo recipe with 1 to 64 brushes and pivots.");
            var byId = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var sizeErrors = new List<string>();
            string positionKey = version == 4 ? "localPosition" : "position";
            string rotationKey = version == 4 ? "localRotation" : "rotation";
            int visible = 0;
            foreach (var token in parts)
            {
                if (!(token is JObject part) || part["id"]?.Type != JTokenType.String)
                    throw new ArgumentException("Each part needs a string id.");
                if (part["shape"]?.Type != JTokenType.String)
                    throw new ArgumentException($"{part["id"]}.shape must be a string (pivot, ellipsoid or box). Put size, color and position arrays directly on the part.");
                string id = (string)part["id"], shape = (string)part["shape"];
                bool pivot = version >= 3 && shape == "pivot";
                if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9_]{0,31}\z") || !byId.TryAdd(id, part))
                    throw new ArgumentException($"Invalid or duplicate part id: {id}.");
                if (!pivot && shape != "ellipsoid" && shape != "box")
                    throw new ArgumentException($"{id}.shape must be pivot, ellipsoid or box.");
                if (version == 4 && !pivot && !part.ContainsKey(rotationKey))
                    part[rotationKey] = new JArray(0, 0, 0);
                bool dye = version == 4 && (string)part["operation"] == "dye";
                int fields = pivot ? 3 + (part.ContainsKey(rotationKey) ? 1 : 0) : 6 + (part.ContainsKey("finish") ? 1 : 0);
                if (version == 4 && !pivot)
                {
                    if (part.ContainsKey("operation"))
                    {
                        if (!dye && (string)part["operation"] != "solid")
                            throw new ArgumentException(id + ".operation must be solid or dye.");
                        fields++;
                    }
                    if (part.ContainsKey("blend"))
                    {
                        var blend = part["blend"];
                        if ((blend.Type != JTokenType.Float && blend.Type != JTokenType.Integer) ||
                            !((double)blend >= 0 && (double)blend <= .25))
                            throw new ArgumentException(id + ".blend must be 0 to 0.25.");
                        fields++;
                    }
                }
                if (part.Count != fields + (version >= 3 ? 1 : 0))
                    throw new ArgumentException($"{id} has missing or unsupported fields. " +
                        (pivot ? $"A pivot uses id, parent, shape and {positionKey}, with optional {rotationKey}." :
                            $"A brush needs id, shape, {positionKey}, size and color, plus parent for a hierarchy."));
                if (version >= 3 && part["parent"]?.Type != JTokenType.Null && part["parent"]?.Type != JTokenType.String)
                    throw new ArgumentException("Each part needs a parent pivot ID or null.");
                ValidateVector(part, positionKey, -2.5f, 2.5f);
                if (!pivot || part.ContainsKey(rotationKey))
                    ValidateVector(part, rotationKey, -360, 360);
                if (pivot)
                    continue;
                if (!dye)
                    visible++;
                ValidateVector(part, "size", dye ? MinimumDyeSize : MinimumBrushSize, MaximumBrushSize, sizeErrors);
                ValidateVector(part, "color", 0, 1);
                if (part.ContainsKey("finish"))
                    ValidateVector(part, "finish", 0, 1);
            }

            if (sizeErrors.Count > 0)
                throw new ArgumentException("Brush dimensions outside the allowed range. Adjust: " + string.Join(", ", sizeErrors));
            if (visible == 0)
                throw new ArgumentException("Include at least one solid brush for the surface.");
            if (version == 2)
                return UpgradeRig(value, byId);
            if (version >= 3)
            {
                if (byId.Values.Count(part => part["parent"].Type == JTokenType.Null) != 1)
                    throw new ArgumentException("Use one root with a null parent.");
                foreach (var part in byId.Values)
                {
                    string parent = (string)part["parent"];
                    int ancestors = 0;
                    while (parent != null)
                    {
                        if (!byId.TryGetValue(parent, out var next) || (string)next["shape"] != "pivot")
                            throw new ArgumentException("Parent must be an existing pivot: " + parent);
                        if (++ancestors >= byId.Count)
                            throw new ArgumentException("The parent hierarchy contains a cycle.");
                        parent = (string)next["parent"];
                    }
                }
            }
            return value;
        }

        // Earlier rigs stored influence spheres separately. On explicit import, attach each
        // legacy brush to the closest influence volume while preserving its object-space pose.
        private static JObject UpgradeRig(JObject legacy, Dictionary<string, JObject> visible)
        {
            if (!(legacy["bones"] is JArray bones) || bones.Count < 1 || bones.Count > 24)
                throw new ArgumentException("Invalid legacy rig.");
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var entry in bones)
            {
                if (!(entry is JObject bone) || bone["id"]?.Type != JTokenType.String ||
                    (bone["parent"]?.Type != JTokenType.String && bone["parent"]?.Type != JTokenType.Null))
                    throw new ArgumentException("Invalid legacy bone.");
                ValidateVector(bone, "position", -2.5f, 2.5f);
                var radius = bone["radius"];
                if ((radius?.Type != JTokenType.Float && radius?.Type != JTokenType.Integer) ||
                    !((double)radius >= .05 && (double)radius <= 2))
                    throw new ArgumentException("Invalid legacy bone radius.");
                string id;
                do
                {
                    id = "pivot_" + index++;
                } while (visible.ContainsKey(id));
                if (!ids.TryAdd((string)bone["id"], id))
                    throw new ArgumentException("Duplicate legacy bone.");
            }

            var nodes = new JArray();
            foreach (JObject bone in bones)
            {
                string parent = (string)bone["parent"];
                if (parent != null && !ids.ContainsKey(parent))
                    throw new ArgumentException("Unknown legacy bone parent.");
                var pivot = new JObject
                {
                    ["id"] = ids[(string)bone["id"]],
                    ["shape"] = "pivot",
                    ["parent"] = parent == null ? JValue.CreateNull() : new JValue(ids[parent]),
                    ["position"] = bone["position"].DeepClone()
                };
                if (bone.ContainsKey("rotation"))
                    pivot["rotation"] = bone["rotation"].DeepClone();
                nodes.Add(pivot);
            }
            foreach (var part in visible.Values)
            {
                var position = ReadVector(part["position"]);
                var closest = bones.OrderBy(bone => Vector3.Distance(position, ReadVector(bone["position"])) - (float)bone["radius"]).First();
                var brush = (JObject)part.DeepClone();
                brush["parent"] = ids[(string)closest["id"]];
                nodes.Add(brush);
            }
            return Validate(new JObject { ["version"] = 3, ["parts"] = nodes }.ToString(Formatting.None));
        }

        private static void ValidateVector(JObject part, string key, double min, double max, List<string> outOfRange = null)
        {
            string field = (string)part["id"] + "." + key;
            if (!(part[key] is JArray values) || values.Count != 3)
                throw new ArgumentException(field + " needs three numbers.");
            for (int i = 0; i < values.Count; i++)
            {
                var value = values[i];
                string axis = field + "." + "xyz"[i];
                if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
                    throw new ArgumentException(axis + " needs a number.");
                var number = (double)value;
                if (double.IsNaN(number) || number < min || number > max)
                {
                    string actual = FormattableString.Invariant($"{axis}={number:G}");
                    string error = FormattableString.Invariant($"{actual} must be {min} to {max}");
                    if (outOfRange != null)
                        outOfRange.Add(error);
                    else
                        throw new ArgumentException(error + ".");
                }
            }
        }

        private void Install(JObject recipe)
        {
            var session = Session;
            if (session.ManagedScriptSource("ClayBrushSchema") == null)
                session.SetManagedScript("ClayBrushSchema",
                    "struct ClayRest(px: f32, py: f32, pz: f32, rx: f32, ry: f32, rz: f32, sx: f32, sy: f32, sz: f32)\n" +
                    "struct ClayPart(id: string, order: i32)\nMudModel {}\n" +
                    "struct ClayStyle(kind: i32, r: f32, g: f32, b: f32, metallic: f32, smoothness: f32, glow: f32, dye: bool, blend: f32)\n");

            var structure = new StringBuilder(EntityPath + " {\n");
            var data = new StringBuilder(EntityPath + " {\nMudModel\nClayRest: {0, 0.75, -1, 0, 0, 0, 1, 1, 1}\n");
            var children = recipe["parts"].OfType<JObject>().ToLookup(part => (string)part["parent"] ?? "");
            var order = new List<(string Id, string Parent, string Path, int Sibling)>();
            bool local = (int)recipe["version"] == 4;
            string positionKey = local ? "localPosition" : "position";
            string rotationKey = local ? "localRotation" : "rotation";

            void Append(string parent, string parentPath, Vector3 parentPosition, Quaternion parentRotation)
            {
                int sibling = 0;
                foreach (var part in children[parent])
                {
                    string id = (string)part["id"];
                    string name = "part_" + id;
                    string path = parentPath + "." + name;
                    order.Add((id, parent, path, sibling++));
                    int kind = (string)part["shape"] == "pivot" ? 2 : (string)part["shape"] == "box" ? 1 : 0;
                    var position = ReadVector(part[positionKey]);
                    var rotation = part[rotationKey] == null ? Quaternion.identity : Quaternion.Euler(ReadVector(part[rotationKey]));
                    var inverse = Quaternion.Inverse(parentRotation);
                    var localPosition = local ? position : inverse * (position - parentPosition);
                    var localRotation = local ? rotation : inverse * rotation;
                    var objectPosition = local ? parentPosition + parentRotation * position : position;
                    var objectRotation = local ? parentRotation * rotation : rotation;
                    if (local && (Mathf.Abs(objectPosition.x) > 2.5f || Mathf.Abs(objectPosition.y) > 2.5f || Mathf.Abs(objectPosition.z) > 2.5f))
                        throw new ArgumentException(id + ".localPosition puts the brush outside the object bounds (±2.5).");
                    bool dye = (string)part["operation"] == "dye";
                    float blend = (float?)part["blend"] ?? (dye ? .02f : .07f);
                    structure.Append(name).Append(" {\n");
                    data.Append(name).Append(" {\nClayPart: {\"").Append(id).Append("\",").Append(sibling - 1).Append("}\nClayRest: {")
                        .Append(Vector(localPosition)).Append(", ")
                        .Append(Vector(localRotation.eulerAngles)).Append(", ")
                        .Append(Vector(kind == 2 ? Vector3.one : ReadVector(part["size"]))).Append("}\n")
                        .Append("ClayStyle: {").Append(kind).Append(", ")
                        .Append(Vector(kind == 2 ? Vector3.zero : ReadVector(part["color"]))).Append(", ")
                        .Append(Vector(part["finish"] == null ? new Vector3(0, .45f, 0) : ReadVector(part["finish"]))).Append(", ")
                        .Append(dye ? "true" : "false").Append(", ")
                        .Append(blend.ToString("R", CultureInfo.InvariantCulture)).Append("}\n");
                    Append(id, path, objectPosition, objectRotation);
                    structure.Append("}\n");
                    data.Append("}\n");
                }
            }

            Append("", EntityPath, Vector3.zero, Quaternion.identity);
            var declaration = data.Append("}\n").ToString();
            if (session.ManagedScriptSource(ScriptName) != declaration)
                session.SetManagedScript(ScriptName, declaration);
            SyncBrushes(session, order);
            var bridge = GetComponent<RuntimePoseBridge>();
            if (!bridge)
                bridge = gameObject.AddComponent<RuntimePoseBridge>();
            bridge.Initialize(this, session);
        }

        private void SyncBrushes(FlecsSession session, List<(string Id, string Parent, string Path, int Sibling)> order)
        {
            if (!surface)
            {
                var settings = RuntimeComposition.Current.World;
                var material = settings ? settings.MudMaterial : Resources.Load<Material>("EcatyMudLiveMaterial");
#if UNITY_EDITOR
                if (!material)
                    material = Type.GetType("MudBun.ResourcesUtilEditor, MudBun")?.GetProperty("DefaultMeshMaterial")?.GetValue(null) as Material;
#endif
                if (!material || !material.shader || !material.shader.isSupported)
                    throw new InvalidOperationException("MudBun's live mesh material is unavailable.");
                surface = new GameObject(EntityPath);
                surface.SetActive(false);
                surface.transform.SetParent(transform, false);
                generator = (Behaviour)surface.AddComponent(RendererType);
                RendererType.GetField("RenderMaterialMesh").SetValue(generator, material);
                RendererType.GetField("MaxVoxelsK").SetValue(generator, 64);
                RendererType.GetField("MaxChunks").SetValue(generator, 64);
                RendererType.GetField("VoxelDensity").SetValue(generator, 16f);
            }

            var origin = JObject.Parse(session.EntityJson(EntityPath))["components"]["ClayRest"];
            surface.transform.localPosition = ReadFields(origin, "px", "py", "pz");
            runtimeNodes.Clear();
            runtimeNodes[EntityPath] = surface.transform;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in order)
            {
                var components = JObject.Parse(session.EntityJson(node.Path))["components"];
                var rest = components["ClayRest"];
                var style = components["ClayStyle"];
                int kind = (int)style["kind"];
                Transform parent = node.Parent.Length == 0 ? surface.transform : brushes[node.Parent].Component.transform;
                seen.Add(node.Id);
                if (!brushes.TryGetValue(node.Id, out var brush) || !brush.Component || brush.Kind != kind)
                {
                    if (brush != null && brush.Component)
                    {
                        var old = brush.Component.transform;
                        // A child may survive the shape change under a different pivot.
                        while (old.childCount > 0)
                            old.GetChild(0).SetParent(null, false);
                        RemoveBrush(old.gameObject);
                    }
                    var go = new GameObject("part_" + node.Id);
                    go.transform.SetParent(parent, false);
                    var type = kind == 1 ? BoxType : SphereType;
                    var component = go.AddComponent(type);
                    if (kind == 2)
                    {
                        var operation = type.GetProperty("Operator");
                        operation.SetValue(component, Enum.Parse(operation.PropertyType, "NoOp"));
                        type.GetProperty("Radius").SetValue(component, .05f);
                    }
                    else
                    {
                        go.AddComponent(MaterialType);
                        if (kind == 1)
                            type.GetProperty("Round").SetValue(component, .045f);
                    }
                    brushes[node.Id] = brush = new Brush { Component = component, Kind = kind };
                }

                var target = brush.Component.transform;
                runtimeNodes[node.Path] = target;
                if (target.parent != parent || !JToken.DeepEquals(brush.RestPose, rest))
                {
                    target.SetParent(parent, false);
                    target.SetLocalPositionAndRotation(ReadFields(rest, "px", "py", "pz"),
                        Quaternion.Euler(ReadFields(rest, "rx", "ry", "rz")));
                    target.localScale = ReadFields(rest, "sx", "sy", "sz");
                    brush.RestPose = rest;
                }
                if (target.GetSiblingIndex() != node.Sibling)
                    target.SetSiblingIndex(node.Sibling);
                if (kind == 2)
                    continue;
                var brushType = brush.Component.GetType();
                var op = brushType.GetProperty("Operator");
                op.SetValue(brush.Component, Enum.Parse(op.PropertyType, (bool)style["dye"] ? "Dye" : "Union"));
                brushType.GetProperty("Blend").SetValue(brush.Component, (float)style["blend"]);
                var material = brush.Component.GetComponent(MaterialType);
                var color = new Color((float)style["r"], (float)style["g"], (float)style["b"]);
                MaterialType.GetProperty("Color").SetValue(material, color);
                MaterialType.GetProperty("Metallic").SetValue(material, (float)style["metallic"]);
                MaterialType.GetProperty("Smoothness").SetValue(material, (float)style["smoothness"]);
                MaterialType.GetProperty("Emission").SetValue(material, color * (float)style["glow"]);
            }
            foreach (string id in brushes.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                var removed = brushes[id].Component;
                if (removed)
                    RemoveBrush(removed.gameObject);
                brushes.Remove(id);
            }
            surface.SetActive(true);
            RendererType.GetMethod("RescanBrushesImmediate").Invoke(generator, null);
        }

        string liveQueryPath, liveQuery;
        // Native ECS is the live model definition, including changes from Script/Explorer.
        private void Update()
        {
            var session = Session;
            if (session == null || session.IsDisposed || session.Lookup("ClayPart") == 0)
                return;
            try
            {
                if (session.Lookup(EntityPath) == 0)
                {
                    if (surface)
                        ResetModel();
                    return;
                }
                if (liveQueryPath != EntityPath)
                {
                    liveQueryPath = EntityPath;
                    liveQuery = "[in] ClayPart, [in] ClayRest, [in] ClayStyle, ?(ChildOf, $parent), ?MudModel(" + EntityPath + ")";
                }
                string changed = session.QueryChanges(liveQuery); // expression built once per path, not every frame
                if (changed == null)
                    return;
                Reconcile(JObject.Parse(changed));
                LiveError = null;
            }
            catch (Exception error) { LiveError = error.Message; }
        }
        public void ReconcileNow()
        {
            Reconcile(JObject.Parse(Session.Query("ClayPart, ClayRest, ClayStyle, ?(ChildOf, $parent)")));
        }
        private void Reconcile(JObject snapshot)
        {
            var rows = snapshot["results"].Where(r => ((string)r["parent"] ?? "").StartsWith(EntityPath + ".", StringComparison.Ordinal)
                || (string)r["parent"] == EntityPath).ToList();
            var byPath = rows.ToDictionary(r => (string)r["parent"] + "." + (string)r["name"], StringComparer.Ordinal);
            if (rows.Count == 0)
            {
                if (surface)
                    ResetModel();
                return;
            }
            var parts = new JArray();
            var order = new List<(string Id, string Parent, string Path, int Sibling)>();
            void Append(string parent, int depth)
            {
                if (depth > 64)
                    throw new InvalidOperationException("Model hierarchy too deep.");
                foreach (var row in rows.Where(r => (string)r["parent"] == parent).OrderBy(r => (int)r["fields"]["values"][0]["order"]))
                {
                    var values = row["fields"]["values"];
                    var part = values[0];
                    var rest = values[1];
                    var style = values[2];
                    string path = parent + "." + (string)row["name"], id = (string)part["id"];
                    string parentId = parent == EntityPath ? null : (string)byPath[parent]["fields"]["values"][0]["id"];
                    int kind = (int)style["kind"];
                    if (kind < 0 || kind > 2)
                        throw new ArgumentException("Unsupported brush kind.");
                    var item = new JObject
                    {
                        ["id"] = id,
                        ["parent"] = parentId,
                        ["shape"] = kind == 2 ? "pivot" : kind == 1 ? "box" : "ellipsoid",
                        ["localPosition"] = new JArray(rest["px"], rest["py"], rest["pz"]),
                        ["localRotation"] = new JArray(rest["rx"], rest["ry"], rest["rz"])
                    };
                    if (kind != 2)
                    {
                        item["size"] = new JArray(rest["sx"], rest["sy"], rest["sz"]);
                        item["color"] = new JArray(style["r"], style["g"], style["b"]);
                        item["finish"] = new JArray(style["metallic"], style["smoothness"], style["glow"]);
                        item["blend"] = style["blend"].DeepClone();
                        item["operation"] = (bool)style["dye"] ? "dye" : "solid";
                    }
                    parts.Add(item);
                    order.Add((id, parentId ?? "", path, (int)part["order"]));
                    Append(path, depth + 1);
                }
            }
            Append(EntityPath, 0);
            var recipe = Validate(new JObject { ["version"] = 4, ["parts"] = parts }.ToString(Formatting.None));
            string signature = recipe.ToString(Formatting.None);
            if (signature == liveSignature)
                return;
            GetComponent<RuntimePoseBridge>()?.Restore();
            Rebuilding?.Invoke();
            SyncBrushes(Session, order);
            var bridge = GetComponent<RuntimePoseBridge>();
            if (!bridge)
                bridge = gameObject.AddComponent<RuntimePoseBridge>();
            bridge.Initialize(this, Session);
            accepted = recipe;
            liveSignature = signature;
            Rebuilt?.Invoke();
        }
        public void ResetModel()
        {
            GetComponent<RuntimePoseBridge>()?.Restore();
            Rebuilding?.Invoke();
            if (surface)
            {
                surface.SetActive(false);
                Release(surface);
            }
            surface = null;
            generator = null;
            accepted = null;
            liveSignature = null;
            brushes.Clear();
            runtimeNodes.Clear();
        }

        private static Vector3 ReadVector(JToken vector) => new Vector3((float)vector[0], (float)vector[1], (float)vector[2]);
        private static Vector3 ReadFields(JToken fields, string x, string y, string z) =>
            new Vector3((float)fields[x], (float)fields[y], (float)fields[z]);
        private static string Vector(Vector3 value) => FormattableString.Invariant($"{value.x:R}, {value.y:R}, {value.z:R}");

        private static void RemoveBrush(GameObject brush)
        {
            brush.SetActive(false);
            brush.transform.SetParent(null, false);
            Release(brush);
        }

        private void OnDestroy() => Release(surface);
        private static void Release(UnityEngine.Object item)
        {
            if (!item)
                return;
            if (Application.isPlaying)
                Destroy(item);
            else
                DestroyImmediate(item);
        }
    }
}
