using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ecaty.Flecs;
using Ecaty.Agents.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Ecaty
{
    // Owns bindings/lifetimes and saved definitions; simulation state remains in Flecs.
    [DefaultExecutionOrder(-400)]
    public sealed class RuntimeExperience : MonoBehaviour
    {
        public sealed class Model
        {
            public ClayCreation Creation;
            public DioramaRuntime Actor;
            public GameObject Host;
        }
        private FlecsWorld world;
        private readonly Dictionary<string, Model> models = new(StringComparer.Ordinal);
        private AgentChatPanel chat;
        private readonly HashSet<string> pressedKeys = new(StringComparer.Ordinal);
        private bool loading;
        private FlecsSession monitored;
        private readonly WorkshopPropInput props = new();
        private float lastHealth;
        private string faultText;
        private struct HealthData { public int Sources,Models,Behaviors,Errors; public ulong Reads,Writes,Queries; }
        private readonly ulong[] healthId = new ulong[1];
        private readonly HealthData[] healthData = new HealthData[1];
        public Camera Camera { get; set; }
        public string ControlledEntity { get; private set; } = ClayCreation.RootName;
        public string Error { get; private set; }
        public IReadOnlyDictionary<string, Model> Models => models;
        public static string SavePath => Path.Combine(Application.persistentDataPath, "Experiences", "current.json");
        public void Initialize(FlecsWorld owner, ClayCreation creation, DioramaRuntime actor)
        {
            world = owner; creation.Initialize(owner, ClayCreation.RootName);
            models[ClayCreation.RootName] = new Model { Creation = creation, Actor = actor, Host = owner.gameObject };
            InstallFoundation();
        }
        private void InstallFoundation()
        {
            foreach (var pair in new[] { ("ClayBrushSchema", "Runtime/Schemas/ClaySchema"), ("PoseSchema", "Runtime/Schemas/PoseSchema"), ("EngineSchema", "Runtime/Schemas/EngineSchema"), ("RuntimeLibrary", "Runtime/Catalog/RuntimeLibrary") })
                if (world.Session.ManagedScriptSource(pair.Item1) == null)
                    world.Session.SetManagedScript(pair.Item1, Resources.Load<TextAsset>(pair.Item2).text);
            if (monitored != world.Session)
            {
                monitored = world.Session; faultText = null;
                foreach (string member in new[] { "sources", "models", "behaviors", "adapterErrors", "binaryReads", "binaryWrites", "cachedQueries" })
                    world.Session.DefineMetric("EngineMetrics." + member, "EngineHealth", member, member.StartsWith("binary", StringComparison.Ordinal));
                world.Session.DefineAlert("EngineAlerts.AdapterFault", "AdapterFault", "Runtime adapter rejected a definition: $this");
                foreach (var row in JObject.Parse(world.Session.Query("EngineCapability"))["results"])
                    world.Session.Document(RowPath(row), (string)row["fields"]["values"][0]["notes"]);
                foreach (string module in world.Session.ManagedPaths.Where(p => p.StartsWith("RuntimeBehaviors.", StringComparison.Ordinal)))
                    TagBehavior(module);
            }
        }
        private void TagBehavior(string module) => world.Session.SetComponentJson(module, "EngineBehavior", new JObject { ["source"] = module }.ToString(Formatting.None));
        public Model BindModel(string id)
        {
            if (!Regex.IsMatch(id, @"\A[A-Za-z][A-Za-z0-9_]{0,47}\z")) throw new ArgumentException("Use a simple model ID.");
            if (models.TryGetValue(id, out var existing)) return existing;
            if (models.Count >= 64) throw new InvalidOperationException("This experience supports up to 64 model bindings.");
            var host = new GameObject("Runtime binding - " + id);
            host.transform.SetParent(transform, false);
            var creation = host.AddComponent<ClayCreation>(); creation.Initialize(world, id); creation.SaveAcceptedRecipes = false;
            var actor = host.AddComponent<DioramaRuntime>(); actor.Initialize(world, creation); actor.ReadDevices = false;
            actor.InteractionCamera = Camera;
            return models[id] = new Model { Creation = creation, Actor = actor, Host = host };
        }
        public string CreateModel(string id, string recipe)
        {
            var model = BindModel(id);
            return model.Creation.Apply(recipe, null);
        }
        public string Configure(string id, string config)
        {
            if (!models.TryGetValue(id, out var model)) throw new ArgumentException("Unknown model.");
            string result = model.Actor.Configure(config);
            if ((string)JObject.Parse(config)["driver"] == "player") SelectControl(id);
            return result;
        }
        public void SelectControl(string id)
        {
            if (!models.ContainsKey(id)) throw new ArgumentException("Unknown control target.");
            ControlledEntity = id;
            foreach (var pair in models) pair.Value.Actor.ReadDevices = pair.Key == id;
        }
        public void ApplyEdit(string code)
        {
            world.Session.SetManagedScript("RuntimeEdits.e" + Guid.NewGuid().ToString("N"), code);
            SyncNow(); world.SyncViews();
        }
        public void SyncNow()
        {
            foreach (var row in JObject.Parse(world.Session.Query("MudModel"))["results"])
            {
                if (row["parent"] != null) continue;
                var model = BindModel((string)row["name"]);
                model.Creation.ReconcileNow();
            }
        }
        public void RemoveModel(string id)
        {
            if (!models.TryGetValue(id, out var model)) return;
            model.Actor.ResetActor(); model.Creation.ResetModel();
            foreach (string module in new[] { id == ClayCreation.RootName ? "ActorDriver" : "Drivers." + id,
                "ActorSetup" + id, "Inputs." + id, id == ClayCreation.RootName ? "ClayRecipe" : "Models." + id })
                world.Session.DeleteEntity(module);
            world.Session.DeleteEntity(id);
            if (id != ClayCreation.RootName) { model.Host.SetActive(false); Destroy(model.Host); models.Remove(id); }
            if (ControlledEntity == id) SelectControl(ClayCreation.RootName);
        }
        public string ApplyBehavior(string id, string code)
        {
            if (!Regex.IsMatch(id, @"\A[a-zA-Z][a-zA-Z0-9_]{0,47}\z")) throw new ArgumentException("Invalid behavior ID.");
            world.Session.SetManagedScript("RuntimeBehaviors." + id, code);
            TagBehavior("RuntimeBehaviors." + id);
            return Inspect();
        }
        public void RemoveBehavior(string id)
        {
            if (!Regex.IsMatch(id, @"\A[a-zA-Z][a-zA-Z0-9_]{0,47}\z")) throw new ArgumentException("Invalid behavior ID.");
            world.Session.DeleteEntity("RuntimeBehaviors." + id);
        }
        public string Inspect()
        {
            var entries = new JArray();
            foreach (var pair in models.Where(p => p.Value.Creation.PresentationRoot))
                entries.Add(new JObject { ["entity"] = pair.Key, ["input"] = pair.Value.Actor.InputPath,
                    ["goal"] = pair.Value.Actor.GoalPath, ["command"] = pair.Value.Actor.CommandPath,
                    ["actor"] = JObject.Parse(pair.Value.Actor.Inspect()), ["modelError"] = pair.Value.Creation.LiveError });
            var sources = new JArray();
            foreach (string path in world.Session.ManagedPaths.Where(p => p.StartsWith("RuntimeBehaviors.", StringComparison.Ordinal)))
                sources.Add(new JObject { ["id"] = path.Substring("RuntimeBehaviors.".Length), ["source"] = path });
            return new JObject { ["models"] = entries, ["behaviors"] = sources, ["controlled"] = ControlledEntity,
                ["error"] = Error, ["bridge"] = JObject.Parse(world.Session.BridgeDiagnostics()) }.ToString(Formatting.None);
        }
        public string Capabilities() => world.Session.Query("EngineCapability, BoundCapability");
        private void Update()
        {
            if (loading || world == null || world.Session == null || world.Session.IsDisposed) return;
            try
            {
                foreach (var row in JObject.Parse(world.Session.Query("MudModel"))["results"])
                {
                    string id = (string)row["name"];
                    if (row["parent"] != null) continue; // Current binding IDs are top-level, named entities.
                    if (!models.ContainsKey(id)) { var model = BindModel(id); model.Creation.ReconcileNow(); }
                }
                ForwardInput(); Error = null;
            }
            catch (Exception error) { Error = error.Message; }
            PublishHealth();
        }
        private void PublishHealth()
        {
            if (Time.unscaledTime - lastHealth < .25f) return;
            lastHealth = Time.unscaledTime;
            var issues = models.Select(p => p.Value.Creation.LiveError ?? p.Value.Actor.Error).Where(e => !string.IsNullOrEmpty(e)).ToList();
            if (!string.IsNullOrEmpty(Error)) issues.Add(Error);
            string currentFault = string.Join("; ", issues);
            if (currentFault != faultText) {
                faultText = currentFault;
                string literal = JsonConvert.SerializeObject(currentFault).Replace("{", "\\{").Replace("$", "\\$");
                world.Session.SetManagedScript("RuntimeFaults", issues.Count == 0 ? "// No adapter faults" : "EngineFault { AdapterFault: {" + literal + "} }");
            }
            var d = JObject.Parse(world.Session.BridgeDiagnostics());
            healthId[0] = world.Session.Lookup("EngineTelemetry");
            healthData[0] = new HealthData { Sources = world.Session.ManagedPaths.Count, Models = models.Count(p => p.Value.Creation.PresentationRoot),
                Behaviors = world.Session.ManagedPaths.Count(p => p.StartsWith("RuntimeBehaviors.", StringComparison.Ordinal)), Errors=issues.Count,
                Reads=(ulong)d["binaryReads"], Writes=(ulong)d["binaryWrites"], Queries=(ulong)d["cachedQueries"] };
            world.Session.WriteBatch("EngineHealth", healthId, healthData, 1);
        }
        public WorkshopTools Workshop { get; set; }
        public WorkshopDock Dock { get; set; }
        public bool SeedOwnsPointer => Dock && Dock.OwnsSeedPointer;
        public WorkshopOrbit Navigation { get; set; }
        public bool WorkshopOwnsPointer => Workshop && Workshop.OwnsPointer;
        public bool WorldDragActive => props.IsGrabbing;

        public bool EditingText
        {
            get
            {
                if (!chat) chat = FindAnyObjectByType<AgentChatPanel>();
                return chat && chat.IsEditingText;
            }
        }

        public bool CoversWorldPointer(Vector2 point)
        {
            if (!chat) chat = FindAnyObjectByType<AgentChatPanel>();
            return Ecaty.Flecs.Capabilities.CapabilityInput.OverHandle(Camera) ||
                chat && chat.CoversScreenPoint(point);
        }

        public bool TryWorldPoint(Ray ray, out Vector3 point) => TryWorldPoint(ray, out point, out _, out _);

        public bool HitsWorldObject(Ray ray)
        {
            TryWorldPoint(ray, out _, out bool hitObject, out _);
            return hitObject;
        }

        public bool TryWorldBounds(Ray ray, out Bounds bounds)
        {
            TryWorldPoint(ray, out _, out bool hitObject, out Bounds? picked);
            bounds = picked.GetValueOrDefault();
            if (picked.HasValue && !hitObject)
            {
                var container = world.FindView(world.Session.Lookup("diorama"));
                if (container)
                    foreach (var renderer in container.GetComponentsInChildren<Renderer>())
                        if (renderer.enabled) bounds.Encapsulate(renderer.bounds);
            }
            return picked.HasValue;
        }

        private bool TryWorldPoint(Ray ray, out Vector3 point, out bool hitObject, out Bounds? picked)
        {
            float nearest = float.PositiveInfinity;
            picked = null;
            hitObject = false;
            ulong groundId = world.Session.Lookup("diorama.Ground");
            var ground = world.FindView(groundId);
            if (Physics.Raycast(ray, out var hit, 500, ~(1 << 2), QueryTriggerInteraction.Ignore))
            {
                nearest = hit.distance;
                picked = hit.collider.bounds;
                hitObject = !ground || !hit.transform.IsChildOf(ground.transform);
            }
            foreach (var model in models.Values)
            {
                if (!model.Creation.PresentationRoot) continue;
                Bounds bounds = model.Creation.WorldBounds();
                if (bounds.IntersectRay(ray, out float distance) && distance <= nearest)
                {
                    nearest = distance;
                    picked = bounds;
                    hitObject = true;
                }
            }
            int count = world.Session.ReadViews(out var views);
            for (int i = 0; i < count; i++)
            {
                if (views[i].Primitive < 0) continue;
                var view = world.FindView(views[i].Entity);
                var renderer = view ? view.GetComponentInChildren<Renderer>() : null;
                if (renderer && renderer.bounds.IntersectRay(ray, out float distance) && distance <= nearest)
                {
                    nearest = distance;
                    picked = renderer.bounds;
                    hitObject = views[i].Entity != groundId;
                }
            }
            if (float.IsPositiveInfinity(nearest) && !new Plane(Vector3.up, Vector3.zero).Raycast(ray, out nearest))
            {
                point = default;
                return false;
            }
            point = ray.GetPoint(nearest);
            return true;
        }


        public int SendPointer(ulong target, Vector2 point, uint buttons) => world.Session.Pointer(target, point.x, point.y, buttons);
        public ulong ForwardWorldPointer(Vector2 point, bool pressed, bool alreadyRouted = false, float now = -1f)
        {
            if (!chat) chat = FindAnyObjectByType<AgentChatPanel>();
            bool blocked = !alreadyRouted && (CoversWorldPointer(point) || WorkshopOwnsPointer || SeedOwnsPointer ||
                Navigation && Navigation.IsNavigating || Keyboard.current?.escapeKey.wasPressedThisFrame == true);
            ulong target = 0;
            float closest = float.MaxValue;
            if (!blocked && Camera)
            {
                var ray = Camera.ScreenPointToRay(point);
                foreach (var pair in models)
                {
                    if (!pair.Value.Creation.PresentationRoot) continue;
                    var bounds = pair.Value.Creation.WorldBounds();
                    if (bounds.IntersectRay(ray, out float distance) && distance < closest)
                    {
                        closest = distance;
                        target = world.Session.Lookup(pair.Key);
                    }
                }
            }
            target = props.Resolve(world, Camera, point, pressed, blocked, target, closest, now < 0 ? Time.unscaledTime : now);
            world.Session.Pointer(target, point.x, point.y, !blocked && pressed ? 1u : 0u);
            return target;
        }
        private void ForwardInput()
        {
            if (!chat) chat = FindAnyObjectByType<AgentChatPanel>();
            var pointer = Pointer.current;
            if (pointer != null) ForwardWorldPointer(pointer.position.ReadValue(), pointer.press.isPressed);

            var keyboard = Keyboard.current;
            if (keyboard == null || chat && chat.IsEditingText) {
                foreach (string key in pressedKeys) world.Session.Keyboard(key, false);
                pressedKeys.Clear(); return;
            }
            foreach (var key in keyboard.allKeys)
            {
                string value = key.keyCode switch {
                    Key.Enter or Key.NumpadEnter => "Enter", Key.Space => " ", Key.Escape => "Escape", Key.Tab => "Tab",
                    Key.LeftArrow => "ArrowLeft", Key.RightArrow => "ArrowRight", Key.UpArrow => "ArrowUp", Key.DownArrow => "ArrowDown",
                    Key.Backspace => "Backspace", Key.Delete => "Delete", Key.Home => "Home", Key.End => "End",
                    Key.PageUp => "PageUp", Key.PageDown => "PageDown",
                    _ => key.keyCode.ToString().StartsWith("Digit", StringComparison.Ordinal) ? key.keyCode.ToString().Substring(5) : key.keyCode.ToString().ToLowerInvariant()
                };
                if (key.wasPressedThisFrame) { world.Session.Keyboard(value, true); pressedKeys.Add(value); }
                if (key.wasReleasedThisFrame && pressedKeys.Remove(value)) world.Session.Keyboard(value, false);
            }
        }
        private static string RowPath(JToken row) => row["parent"] == null ? (string)row["name"] : (string)row["parent"] + "." + (string)row["name"];
        public string Capture()
        {
            var sources = new JArray();
            foreach (string path in world.Session.ManagedPaths.Where(p => p != "RuntimeFaults").ToArray())
            {
                string source = world.Session.ManagedScriptSource(path);
                if (source != null) sources.Add(new JObject { ["path"] = path, ["code"] = source });
            }
            var capabilityRuntime=GetComponent<Ecaty.Flecs.Capabilities.CapabilityRuntime>();
            var capabilityTypes=capabilityRuntime!=null?new HashSet<string>(capabilityRuntime.Adapters.Select(a=>a.Component)):new HashSet<string>();
            capabilityTypes.Add("CapBinding");capabilityTypes.Add("NativeTree");
            var actors = new JArray(); var states = new JArray(); var statePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in JObject.Parse(world.Session.Query("Persistent || PersistState"))["results"]) statePaths.Add(RowPath(row));
            foreach(string type in new[]{"CapBinding","NativeTree"})if(world.Session.Lookup(type)!=0)foreach(var row in JObject.Parse(world.Session.Query(type))["results"])statePaths.Add(RowPath(row));
            foreach (var pair in models)
            {
                var model = pair.Value;
                if (!model.Creation.PresentationRoot) continue;
                actors.Add(new JObject { ["entity"] = pair.Key, ["configuration"] = JObject.Parse(model.Actor.Inspect())["configuration"],
                    ["position"] = model.Actor.Motor ? JArray.FromObject(new[] { model.Actor.Motor.transform.position.x, model.Actor.Motor.transform.position.y, model.Actor.Motor.transform.position.z }) : null,
                    ["yaw"] = model.Actor.Motor ? model.Actor.Motor.transform.eulerAngles.y : 0 });
                statePaths.Add(pair.Key);
                foreach (string node in model.Creation.RuntimeNodes.Keys) statePaths.Add(node);
                foreach (string path in new[] { model.Actor.InputPath, model.Actor.GoalPath, model.Actor.CommandPath })
                    if (world.Session.Lookup(path) != 0) statePaths.Add(path);
            }
            foreach (string path in statePaths)
            {
                var c = (JObject)JObject.Parse(world.Session.EntityJson(path))["components"];
                var selected = new JObject();
                var extra = c["PersistState"]?["components"] != null ? JArray.Parse((string)c["PersistState"]["components"]) : new JArray();
                foreach (var value in c.Properties())
                    if (capabilityTypes.Contains(value.Name) || value.Name.EndsWith(".mut", StringComparison.Ordinal) || extra.Any(t => (string)t == value.Name) ||
                        new[] { "PlayerMotion", "LiftIntent", "MoveGoal", "MoveIntent", "SpatialIntent", "ActorControl", "MotorPolicy", "ClayRest", "ClayStyle", "ClayPart" }.Contains(value.Name))
                        selected[value.Name] = value.Value.DeepClone();
                if (selected.Count > 0) states.Add(new JObject { ["entity"] = path, ["components"] = selected });
            }
            return new JObject { ["version"] = 1, ["capabilities"] = capabilityRuntime?.CaptureState(), ["sources"] = sources, ["actors"] = actors, ["states"] = states,
                ["clock"] = JObject.Parse(world.Session.EntityJson("Clock"))["components"]["Clock"], ["controlled"] = ControlledEntity }.ToString(Formatting.Indented);
        }
        public void Save(string path = null)
        {
            path ??= SavePath; string json = Capture();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(staged, json); if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        private static List<JObject> ValidateSources(JArray sources, JArray states)
        {
            var remaining = sources.Select(entry => entry as JObject ?? throw new ArgumentException("Invalid source module.")).ToList();
            var ordered = new List<JObject>(remaining.Count);
            var probe = new FlecsSession(0);
            try
            {
                while (remaining.Count != 0)
                {
                    int before = remaining.Count;
                    Exception unresolved = null;
                    foreach (var entry in remaining.ToArray())
                    {
                        string path = (string)entry["path"];
                        try { probe.SetManagedScript(path, (string)entry["code"]); }
                        catch (InvalidOperationException error) when (error.Message.Contains("unresolved reference", StringComparison.OrdinalIgnoreCase))
                        {
                            unresolved = new InvalidOperationException($"Source '{path}' cannot be restored: {error.Message}", error);
                            // A failed evaluation may have left partial entities. Retry only from validated sources.
                            probe.Dispose();
                            probe = new FlecsSession(0);
                            foreach (var valid in ordered)
                                probe.SetManagedScript((string)valid["path"], (string)valid["code"]);
                            continue;
                        }
                        ordered.Add(entry);
                        remaining.Remove(entry);
                    }
                    if (remaining.Count == before) throw unresolved;
                }
                foreach (var state in states)
                {
                    string path = (string)state["entity"];
                    if (probe.Lookup(path) == 0) continue;
                    foreach (var component in ((JObject)state["components"]).Properties())
                        probe.SetComponentJson(path, component.Name, component.Value.ToString(Formatting.None));
                }
                return ordered;
            }
            finally { probe.Dispose(); }
        }

        public void Load(string json)
        {
            var data = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (json.Length > 4000000 || (int?)data["version"] != 1 || data["sources"] is not JArray sources || sources.Count > 256 ||
                data["actors"] is not JArray actors || actors.Count > 64 || data["states"] is not JArray states || states.Count > 4096)
                throw new ArgumentException("Invalid experience document.");
            if (sources.Select(s => (string)s["path"]).Distinct().Count() != sources.Count) throw new ArgumentException("Duplicate source module.");
            // Resolve dependencies and state types in an isolated world before replacing the session.
            var orderedSources = ValidateSources(sources, states);
            loading = true;
            GetComponent<Ecaty.Flecs.Capabilities.CapabilityRuntime>()?.ResetBindings();
            try
            {
                foreach (var pair in models.ToArray())
                {
                    pair.Value.Actor.ResetActor(); pair.Value.Creation.ResetModel();
                    if (pair.Key != ClayCreation.RootName) { pair.Value.Host.SetActive(false); Destroy(pair.Value.Host); models.Remove(pair.Key); }
                }
                world.Initialize((ushort)world.restPort);
                if (data["clock"] is JObject clock) world.Session.SetComponentJson("Clock", "Clock", clock.ToString(Formatting.None));
                foreach (var entry in orderedSources) world.Session.SetManagedScript((string)entry["path"], (string)entry["code"]);
                InstallFoundation();
                // Apply authored geometry before binding controllers; restore behavioral state last.
                foreach (var state in states) foreach (var component in ((JObject)state["components"]).Properties())
                    if (new[] { "ClayRest", "ClayStyle", "ClayPart" }.Contains(component.Name) && world.Session.Lookup((string)state["entity"]) != 0)
                        world.Session.SetComponentJson((string)state["entity"], component.Name, component.Value.ToString(Formatting.None));
                foreach (var entry in actors)
                {
                    var model = BindModel((string)entry["entity"]); model.Creation.ReconcileNow(); model.Actor.Initialize(world, model.Creation);
                    if (entry["configuration"] is JObject config) model.Actor.Configure(config.ToString(Formatting.None));
                    if (entry["position"] is JArray p) model.Actor.RestoreMotorPose(new Vector3((float)p[0], (float)p[1], (float)p[2]), Quaternion.Euler(0, (float)entry["yaw"], 0));
                }
                foreach (var state in states) foreach (var component in ((JObject)state["components"]).Properties())
                    if (world.Session.Lookup((string)state["entity"]) != 0) world.Session.SetComponentJson((string)state["entity"], component.Name, component.Value.ToString(Formatting.None));
                GetComponent<Ecaty.Flecs.Capabilities.CapabilityRuntime>()?.QueueRestore(data["capabilities"] as JObject);
                SelectControl((string)data["controlled"]); Error = null;
            }
            finally { loading = false; }
        }
    }
}
