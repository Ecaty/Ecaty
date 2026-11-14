#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.Linq;
using Runtime.Flecs;
using Runtime.Capabilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    // Orchestrates saved worlds. One world is resident at worlds.<id>; the others stay as document text in their catalog record.
    // Definitions, selection and simulation are native data.
    public sealed class GalleryExperienceStore : IDisposable
    {
        const string Shared = "GallerySharedShapeSource";
        const string Probe = "GalleryProbe";
        readonly FlecsWorld world;
        ulong appOwner;
        // The store is not an entity: the application module owns the saved world's origin.
        ulong AppOwner
        {
            get
            {
                if (appOwner == 0)
                    appOwner = world.Session.Lookup("app");
                if (appOwner == 0)
                    throw new InvalidOperationException("The application module is not in the world.");
                return appOwner;
            }
        }
        string resident; ulong worldId; Transform root; bool visible;
        // Undo, rewind, stop and load give a world a new root entity: the session counts them, and the store looks the world up again.
        int seenGeneration;
        // The new root entity is known but its view is not created yet (views are made by SyncViews): Follow tries again on the next call.
        bool pending;
        Vector3 origin;
        // Spin values of a hidden resident, by entity path. The live Spin is zero while hidden.
        readonly Dictionary<string, JToken> paused = new();
        int generation;
        // Counts the resident's replaced or removed roots; a reader gets the count after the store has followed the world.
        public int Generation
        {
            get
            {
                Follow();
                return generation;
            }
        }
        public string ResidentId => resident;
        public Transform ResidentRoot
        {
            get
            {
                Follow();
                return root;
            }
        }
        public bool Visible => visible;
        public GalleryExperienceStore(FlecsWorld owner)
        {
            world = owner;
            if (world.Session.ManagedScriptSource(Shared) == null)
                world.Session.SetManagedScript(Shared, "prefab GallerySharedShape { Primitive: {1}; Scale: {1,1,1}; Color: {0.8,0.88,0.79,1} }");
        }
        /// <summary>Most gallery entries a saved arrangement may hold. Checked when a world is created and when one is restored.</summary>
        public const int MaxEntries = 80;
        // Has() runs every frame while a world is open. Saved-world records change only through WriteRecord or a
        // source change (entity deleted or recreated), so the answer is cached until one of those happens.
        readonly Dictionary<string, bool> hasCache = new();
        FlecsSession hasCacheSession; int hasCacheRevision;
        public bool Has(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            if (hasCacheSession != world.Session || hasCacheRevision != world.Session.SourceRevision)
            {
                hasCache.Clear();
                hasCacheSession = world.Session;
                hasCacheRevision = world.Session.SourceRevision;
            }
            if (!hasCache.TryGetValue(id, out bool has))
                hasCache[id] = has = !string.IsNullOrEmpty((string)ReadRecord(id)?["document"]);
            return has;
        }
        static string Prefix(string id) => GalleryWorldDocument.Root(id) + ".";
        // Takes the resident world's new entity after the native side replaced it (undo, rewind, stop, load), and puts its view where
        // Activate put the old one. Nothing happens, and no native call is made, while the session's counter has not moved.
        void Follow()
        {
            var session = world.Session;
            if (session == null || session.IsDisposed)
                return;
            if (resident == null)
            {
                seenGeneration = session.WorldGeneration;
                return;
            }
            if (seenGeneration != session.WorldGeneration)
            {
                seenGeneration = session.WorldGeneration;
                ulong current = session.Lookup(GalleryWorldDocument.Root(resident));
                if (current == 0)
                {
                    // The world is gone: nothing to save or delete, and the old entity must not be used again.
                    resident = null;
                    root = null;
                    worldId = 0;
                    visible = false;
                    pending = false;
                    paused.Clear();
                    generation++;
                    return;
                }
                if (current != worldId)
                {
                    worldId = current;
                    root = null;
                    pending = true;
                    generation++;
                }
            }
            // While pending only the view is looked for; the world's own update creates it (Activate and Unload sync and call again).
            if (!pending)
                return;
            if (!Mount(false))
                return;
            pending = false;
            root.gameObject.SetActive(visible);
        }
        // Finds the resident's root view and takes over its origin. False while the view does not exist yet, or (when not
        // strict) while another owner holds its pose: Follow runs inside getters and tries again instead of throwing.
        bool Mount(bool strict = true)
        {
            root = world.FindView(worldId)?.transform;
            if (!root)
                return false;
            if (!world.AcquirePose(worldId, AppOwner))
            {
                if (strict)
                    throw new InvalidOperationException("Saved world origin already owned");
                root = null;
                return false;
            }
            root.position = origin;
            root.rotation = Quaternion.identity;
            RefreshLayers();
            return true;
        }
        CapabilityRuntime Capabilities => world.GetComponent<CapabilityRuntime>();
        JObject ReadRecord(string id)
        {
            ulong entity = string.IsNullOrEmpty(id) ? 0 : world.Session.Lookup(id);
            if (entity == 0)
                return null;
            string json = world.Session.ComponentJson(entity, "GallerySavedWorld");
            return string.IsNullOrEmpty(json) ? null : JObject.Parse(json);
        }
        void WriteRecord(string id, JObject record)
        {
            world.Session.SetComponentJson(id, "GallerySavedWorld", record.ToString(Formatting.None));
            hasCache.Clear();
        }
        // The revision moves only when the content does.
        void Store(string id, string document, string selected, string state)
        {
            var previous = ReadRecord(id);
            bool changed = previous == null || (string)previous["document"] != document || (string)previous["selected"] != selected || (string)previous["state"] != state;
            WriteRecord(id, new JObject { ["format"] = GalleryWorldDocument.Format, ["document"] = document, ["selected"] = selected ?? "", ["revision"] = ((int?)previous?["revision"] ?? 0) + (changed ? 1 : 0), ["state"] = state ?? "" });
        }
        string StateText(string id)
        {
            var state = Capabilities?.CaptureState(GalleryWorldDocument.Root(id));
            return state != null && state.HasValues ? state.ToString(Formatting.None) : "";
        }
        static JObject V(float x, float y, float z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
        public static JObject Shape(string name, int kind, Vector3 position, Vector3 scale, Color color, Vector3 spin) => new() { ["name"] = name, ["Position"] = V(position.x, position.y, position.z), ["Rotation"] = V(0, 0, 0), ["Scale"] = V(scale.x, scale.y, scale.z), ["Color"] = new JObject { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a }, ["Primitive"] = new JObject { ["kind"] = kind }, ["Spin"] = V(spin.x, spin.y, spin.z) };
        // Entity paths of the resident world that match a query.
        List<string> Rows(string expression)
        {
            Follow();
            var paths = new List<string>();
            foreach (var row in JObject.Parse(world.Session.QueryWorld(worldId, expression))["results"])
                paths.Add(row["parent"] == null ? (string)row["name"] : (string)row["parent"] + "." + (string)row["name"]);
            return paths;
        }
        // The resident world as a document. A hidden world has zero Spin live, so its remembered values go back in.
        string LiveDocument()
        {
            Follow();
            string text = world.Session.SaveWorld(worldId);
            if (visible || paused.Count == 0)
                return text;
            var document = JObject.Parse(text);
            foreach (JObject record in ((JArray)document["entities"]).Cast<JObject>())
                if (paused.TryGetValue(Prefix(resident) + (string)record["path"], out var spin))
                {
                    if (record["components"] is not JObject components)
                        record["components"] = components = new JObject();
                    components["Spin"] = spin.DeepClone();
                }
            return document.ToString(Formatting.None);
        }
        public JArray Features(string id)
        {
            Follow();
            if (resident != null && resident == id && root)
                return GalleryWorldDocument.Features(LiveDocument());
            var record = ReadRecord(id);
            return record == null ? null : GalleryWorldDocument.Features((string)record["document"]);
        }
        public void Create(string id)
        {
            var objects = new JArray { Shape("Ground", 4, new Vector3(0, -.6f, 0), Vector3.one * .42f, new Color(.30f, .43f, .39f), Vector3.zero), Shape("Object1", 0, Vector3.zero, Vector3.one * .8f, new Color(.86f, .73f, .48f), new Vector3(0, 32, 8)) };
            Store(id, GalleryWorldDocument.FromShapes(id, objects), "Object1", "");
        }
        public void Activate(string id, Vector3 origin)
        {
            if (!Has(id))
                return;
            Follow();
            if (pending)
            {
                world.SyncViews();
                Follow();
            }
            this.origin = origin;
            if (resident != id)
            {
                var record = ReadRecord(id);
                string document = (string)record["document"];
                // A refused document changes nothing: the previous resident stays as it is.
                if (!world.Session.TryLoadWorld(id, document, out ulong loaded, out string error))
                    throw new InvalidOperationException("Saved world " + id + " was refused: " + error);
                seenGeneration = world.Session.WorldGeneration;
                Unload();
                resident = id;
                worldId = loaded;
                pending = false;
                visible = false;
                paused.Clear();
                generation++;
                try
                {
                    // Behavior sources of a version 1 record cannot be converted ahead of time; they run once, inside the world.
                    var scripts = GalleryWorldDocument.LegacyScripts(id, document);
                    foreach (var script in scripts)
                        if (!world.Session.TryRunWorldScript(loaded, script.Script, out string problem, out int line, out _))
                            Debug.LogWarning(id + ": Owned source could not be restored: " + script.Path + (line > 0 ? " line " + line : "") + ": " + problem);
                    if (scripts.Count > 0)
                        Store(id, world.Session.SaveWorld(loaded), (string)record["selected"], (string)record["state"]);
                    world.SyncViews();
                    if (!Mount())
                        throw new InvalidOperationException("Saved world produced no root view");
                    string stateText = (string)record["state"];
                    if (!string.IsNullOrEmpty(stateText))
                        Capabilities?.QueueRestore(JObject.Parse(stateText), true);
                }
                catch
                {
                    resident = null;
                    root = null;
                    worldId = 0;
                    pending = false;
                    world.Session.DeleteWorld(loaded);
                    world.SyncViews();
                    throw;
                }
            }
            if (!root)
                throw new InvalidOperationException("Saved world has no root view");
            root.position = origin;
            root.rotation = Quaternion.identity;
            SetVisible(id, true);
            RefreshLayers();
        }
        void RefreshLayers()
        {
            if (!root)
                return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = GalleryPreviewStage.Layer;
        }
        static readonly string NoSpin = V(0, 0, 0).ToString(Formatting.None);
        public void SetVisible(string id, bool show)
        {
            Follow();
            if (resident != id || !root)
                return;
            if (visible != show)
            {
                if (!show)
                {
                    CaptureResident();
                    paused.Clear();
                    foreach (string path in Rows("Spin"))
                    {
                        paused[path] = JObject.Parse(world.Session.ComponentJson(world.Session.Lookup(path), "Spin"));
                        world.Session.SetComponentJson(path, "Spin", NoSpin);
                    }
                }
                else
                {
                    foreach (var pair in paused)
                        if (world.Session.Lookup(pair.Key) != 0)
                            world.Session.SetComponentJson(pair.Key, "Spin", pair.Value.ToString(Formatting.None));
                    paused.Clear();
                }
                visible = show;
            }
            world.Session.EnterWorld(show ? worldId : 0);
            root.gameObject.SetActive(show);
        }
        public Transform[] FocusTargets()
        {
            Follow();
            if (resident == null)
                return Array.Empty<Transform>();
            var targets = new List<(ulong Entity, Transform View)>();
            foreach (string path in Rows("Primitive"))
            {
                // Direct children only; a shape nested in another entity is not a focus target.
                if (path.Substring(0, Math.Max(0, path.LastIndexOf('.'))) != GalleryWorldDocument.Root(resident))
                    continue;
                ulong entity = world.Session.Lookup(path);
                if (entity == 0 || (int)JObject.Parse(world.Session.ComponentJson(entity, "Primitive"))["kind"] == 4)
                    continue;
                var view = world.FindView(entity)?.transform;
                if (view)
                    targets.Add((entity, view));
            }
            return targets.OrderBy(t => t.Entity).Select(t => t.View).ToArray();
        }
        public void CaptureResident()
        {
            Follow();
            if (resident == null || !root || !visible)
                return;
            // Bindings are entities of the world: the document holds them once. Only adapter state is kept beside it.
            Store(resident, world.Session.SaveWorld(worldId), (string)ReadRecord(resident)?["selected"], StateText(resident));
        }
        public JArray Capture()
        {
            CaptureResident();
            var rows = new JArray();
            foreach (var row in JObject.Parse(world.Session.Query("GallerySavedWorld"))["results"])
            {
                string id = (string)row["name"];
                var record = ReadRecord(id);
                if (string.IsNullOrEmpty((string)record?["document"]))
                    continue;
                string state = (string)record["state"];
                rows.Add(new JObject { ["id"] = id, ["selected"] = (string)record["selected"] ?? "", ["revision"] = (int)record["revision"], ["state"] = string.IsNullOrEmpty(state) ? new JObject() : JObject.Parse(state), ["document"] = JObject.Parse((string)record["document"]) });
            }
            return rows;
        }
        /// <summary>Refuses rows that cannot be restored, changing nothing: the shape of each row, then a probe load of every document.</summary>
        public void Check(JArray rows)
        {
            rows ??= new JArray();
            if (rows.Count > MaxEntries || rows.Select(r => (string)r["id"]).Distinct().Count() != rows.Count)
                throw new ArgumentException("Invalid saved world catalog");
            foreach (var row in rows)
            {
                string id = (string)row["id"];
                if (!GalleryWorldDocument.ValidId(id) || row["document"] is not JObject document || (int?)document["format"] != GalleryWorldDocument.Format || (string)document["root"] != GalleryWorldDocument.Root(id) || (row["state"] != null && row["state"] is not JObject))
                    throw new ArgumentException("Invalid saved world " + id);
            }
            try
            {
                foreach (var row in rows)
                {
                    if (!world.Session.TryLoadWorld(Probe, row["document"].ToString(Formatting.None), out ulong probe, out string error))
                        throw new ArgumentException("Saved world " + (string)row["id"] + " was refused: " + error);
                    world.Session.DeleteWorld(probe);
                }
            }
            finally
            {
                ulong leftover = world.Session.Lookup(GalleryWorldDocument.Root(Probe));
                if (leftover != 0)
                    world.Session.DeleteWorld(leftover);
                hasCache.Clear();
            }
        }
        public void Restore(JArray rows)
        {
            rows ??= new JArray();
            if (rows.Count > MaxEntries || rows.Select(r => (string)r["id"]).Distinct().Count() != rows.Count)
                throw new ArgumentException("Invalid saved world catalog");
            Unload();
            foreach (var row in JObject.Parse(world.Session.Query("GallerySavedWorld"))["results"])
                WriteRecord((string)row["name"], new JObject { ["format"] = GalleryWorldDocument.Format, ["document"] = "", ["selected"] = "", ["revision"] = 0, ["state"] = "" });
            foreach (var row in rows)
            {
                string id = (string)row["id"];
                if (world.Session.Lookup(id) == 0)
                    throw new ArgumentException("Saved-world catalog entry missing");
                var state = row["state"] as JObject;
                WriteRecord(id, new JObject { ["format"] = GalleryWorldDocument.Format, ["document"] = row["document"].ToString(Formatting.None), ["selected"] = (string)row["selected"] ?? "", ["revision"] = (int?)row["revision"] ?? 0, ["state"] = state != null && state.HasValues ? state.ToString(Formatting.None) : "" });
            }
        }
        void SetSelected(string id, string name)
        {
            var record = ReadRecord(id);
            if (record == null)
                return;
            record["selected"] = name;
            WriteRecord(id, record);
        }
        public void Select(string name)
        {
            if (resident != null)
                SetSelected(resident, name);
        }
        public void Move(string name, Vector3 local)
        {
            if (resident == null || name == "Ground")
                return;
            world.Session.SetComponentJson(Prefix(resident) + name, "Position", V(local.x, local.y, local.z).ToString());
            Select(name);
            world.SyncViews();
        }
        // Adds a shape to the resident world. A hidden world keeps its Spin paused and its document is written at once.
        void AddLive(JObject shape)
        {
            Follow();
            string name = (string)shape["name"];
            string path = Prefix(resident) + name;
            world.Session.Spawn(world.Session.Lookup(GalleryWorldDocument.Kind), worldId, name);
            foreach (string type in GalleryWorldDocument.Components)
            {
                if (type == "Spin" && !visible)
                {
                    paused[path] = shape[type].DeepClone();
                    world.Session.SetComponentJson(path, type, NoSpin);
                }
                else
                    world.Session.SetComponentJson(path, type, shape[type].ToString(Formatting.None));
            }
            world.SyncViews();
            if (!visible)
                Store(resident, LiveDocument(), (string)ReadRecord(resident)?["selected"], (string)ReadRecord(resident)?["state"]);
            generation++;
            RefreshLayers();
        }
        static string FreeName(IEnumerable<string> taken)
        {
            var names = new HashSet<string>(taken);
            int n = 1;
            while (names.Contains("Object" + n))
                n++;
            return "Object" + n;
        }
        public string Place(Vector3 local)
        {
            if (resident == null)
                return null;
            string name = FreeName(Features(resident).Select(o => (string)o["name"]));
            AddLive(Shape(name, 0, local, Vector3.one * .8f, new Color(.60f, .78f, .76f), new Vector3(0, 25, 0)));
            Select(name);
            return name;
        }
        public string Reuse(string sourceWorld, string name, string destinationWorld)
        {
            if (!Has(sourceWorld) || !Has(destinationWorld))
                throw new ArgumentException("Saved worlds required");
            CaptureResident();
            var feature = (JObject)Features(sourceWorld).First(o => (string)o["name"] == name).DeepClone();
            feature["name"] = FreeName(Features(destinationWorld).Select(o => (string)o["name"]));
            string added = (string)feature["name"];
            if (resident == destinationWorld)
                AddLive(feature);
            else
            {
                var record = ReadRecord(destinationWorld);
                Store(destinationWorld, GalleryWorldDocument.Append((string)record["document"], feature), (string)record["selected"], (string)record["state"]);
            }
            SetSelected(destinationWorld, added);
            return added;
        }
        public void Unload()
        {
            Follow();
            // A replaced world whose view is not there yet: sync once so its edits are still saved.
            if (pending)
            {
                world.SyncViews();
                Follow();
            }
            if (resident == null)
                return;
            CaptureResident();
            world.ReleasePose(worldId, AppOwner);
            if (world.Session.EnteredWorld == worldId)
                world.Session.EnterWorld(0);
            world.Session.DeleteWorld(worldId);
            world.SyncViews();
            resident = null;
            root = null;
            worldId = 0;
            pending = false;
            visible = false;
            paused.Clear();
        }
        public void Dispose()
        {
            if (world && world.Session != null && !world.Session.IsDisposed)
                Unload();
        }
    }
}
#endif
