#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Runtime.Flecs;
using Runtime.Capabilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Game
{
    // Orchestrates source ownership/presentation only. Definitions, selection and simulation are native data.
    public sealed class GalleryExperienceStore : IDisposable
    {
        const string Shared = "GallerySharedShapeSource";
        readonly FlecsWorld world;
        string resident; ulong rootId; Transform root; bool visible;
        public int Generation
        {
            get; private set;
        }
        public string ResidentId => resident;
        public Transform ResidentRoot => root;
        public bool Visible => visible;
        public GalleryExperienceStore(FlecsWorld owner)
        {
            world = owner;
            if (world.Session.ManagedScriptSource(Shared) == null)
                world.Session.SetManagedScript(Shared, "prefab GallerySharedShape { Primitive: {1}; Scale: {1,1,1}; Color: {0.8,0.88,0.79,1} }");
        }
        public bool Has(string id) => !string.IsNullOrEmpty((string)ReadRecord(id)?["source"]);
        // Capability bindings live under the private root; world-owned behavior sources use this module prefix.
        static string Scope(string id) => id + "Content";
        static string Owned(string id) => "GalleryWorld_" + id + ".";
        static string GraphModule(string id) => "GalleryGraph_" + id;
        CapabilityRuntime Capabilities => world.GetComponent<CapabilityRuntime>();
        static JObject Graph(JToken record)
        {
            string text = (string)record?["graph"];
            return string.IsNullOrEmpty(text) ? null : JObject.Parse(text);
        }
        JObject ReadRecord(string id)
        {
            if (string.IsNullOrEmpty(id) || world.Session.Lookup(id) == 0)
                return null;
            return (JObject)JObject.Parse(world.Session.EntityJson(id))["components"]?["GallerySavedWorld"];
        }
        public JArray Features(string id)
        {
            var record = ReadRecord(id);
            return record == null ? null : JArray.Parse((string)record["objects"]);
        }
        static JObject V(float x, float y, float z) => new() { ["x"] = x, ["y"] = y, ["z"] = z };
        public static JObject Shape(string name, int kind, Vector3 position, Vector3 scale, Color color, Vector3 spin) => new() { ["name"] = name, ["Position"] = V(position.x, position.y, position.z), ["Rotation"] = V(0, 0, 0), ["Scale"] = V(scale.x, scale.y, scale.z), ["Color"] = new JObject { ["r"] = color.r, ["g"] = color.g, ["b"] = color.b, ["a"] = color.a }, ["Primitive"] = new JObject { ["kind"] = kind }, ["Spin"] = V(spin.x, spin.y, spin.z) };
        public void Create(string id)
        {
            SetRecord(id, new JArray { Shape("Ground", 4, new Vector3(0, -.6f, 0), Vector3.one * .42f, new Color(.30f, .43f, .39f), Vector3.zero), Shape("Object1", 0, Vector3.zero, Vector3.one * .8f, new Color(.86f, .73f, .48f), new Vector3(0, 32, 8)) }, "Object1");
        }
        static string Number(JToken value) => ((float)value).ToString("R", CultureInfo.InvariantCulture);
        static string Source(string id, JArray objects)
        {
            var s = new StringBuilder(id + "Content { Position: {0,0,0}; (GalleryContentOf, " + id + ")\n");
            foreach (JObject shape in objects)
            {
                s.Append((string)shape["name"]).Append(" : GallerySharedShape {\n");
                foreach (string type in new[] { "Position", "Rotation", "Scale", "Color", "Primitive", "Spin" })
                    s.Append(type).Append(": {").Append(string.Join(",", ((JObject)shape[type]).Properties().Select(p => p.Name + ":" + Number(p.Value)))).Append("}\n");
                s.Append("}\n");
            }
            return s.Append("}\n").ToString();
        }
        void SetRecord(string id, JArray objects, string selected, JObject graph = null, bool replaceGraph = false)
        {
            Validate(id, objects, graph);
            var previous = ReadRecord(id);
            string graphText = replaceGraph ? graph?.ToString(Formatting.None) ?? "" : (string)previous?["graph"] ?? "";
            world.Session.SetComponentJson(id, "GallerySavedWorld", new JObject { ["source"] = Source(id, objects), ["objects"] = objects.ToString(Formatting.None), ["selected"] = selected ?? "", ["revision"] = ((int?)previous?["revision"] ?? 0) + 1, ["graph"] = graphText }.ToString(Formatting.None));
        }
        static void Validate(string id, JArray objects, JObject graph = null)
        {
            string invalidGraph = CapabilityGraph.Validate(graph, Scope(id), Owned(id));
            if (invalidGraph != null)
                throw new ArgumentException(invalidGraph);
            if (!System.Text.RegularExpressions.Regex.IsMatch(id ?? "", "^GalleryCreation[1-9][0-9]{0,8}$") || objects.Count > 64)
                throw new ArgumentException("Invalid saved world");
            var names = new HashSet<string>();
            foreach (JObject o in objects)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch((string)o["name"] ?? "", "^[A-Za-z][A-Za-z0-9_]{0,47}$") || !names.Add((string)o["name"]))
                    throw new ArgumentException("Invalid object name");
                foreach (string type in new[] { "Position", "Rotation", "Scale", "Color", "Primitive", "Spin" })
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
        public void Activate(string id, Vector3 origin)
        {
            if (!Has(id))
                return;
            if (resident != id)
            {
                Unload();
                var record = ReadRecord(id);
                world.Session.SetManagedScript("GalleryContent_" + id, (string)record["source"]);
                resident = id;
                Generation++;
                rootId = world.Session.Lookup(id + "Content");
                world.SyncViews();
                root = world.FindView(rootId)?.transform;
                if (!root)
                    throw new InvalidOperationException("Saved world produced no root view");
                if (!world.AcquirePose(rootId, this))
                    throw new InvalidOperationException("Saved world origin already owned");
                // Capability bindings, dependency relations, trees and owned behavior sources follow the objects they target.
                foreach (string issue in CapabilityGraph.Restore(world.Session, Capabilities, Graph(record), Scope(id), GraphModule(id), Owned(id)))
                    Debug.LogWarning(id + ": " + issue);
            }
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
        public void SetVisible(string id, bool show)
        {
            if (resident != id || !root)
                return;
            if (visible != show)
            {
                if (!show)
                    CaptureResident();
                foreach (JObject shape in Features(id))
                {
                    string path = id + "Content." + (string)shape["name"];
                    world.Session.SetComponentJson(path, "Spin", (show ? shape["Spin"] : V(0, 0, 0)).ToString(Formatting.None));
                }
                visible = show;
            }
            root.gameObject.SetActive(show);
        }
        public Transform[] FocusTargets() => resident == null ? Array.Empty<Transform>() : Features(resident).OfType<JObject>().Where(o => (int)o["Primitive"]["kind"] != 4).Select(o => world.FindView(world.Session.Lookup(resident + "Content." + (string)o["name"]))?.transform).Where(t => t).ToArray();
        public void CaptureResident()
        {
            if (resident == null || !root)
                return;
            var objects = Features(resident);
            foreach (JObject shape in objects)
            {
                var components = (JObject)JObject.Parse(world.Session.EntityJson(resident + "Content." + (string)shape["name"]))["components"];
                foreach (string type in new[] { "Position", "Rotation", "Scale", "Color", "Primitive", "Spin" })
                    if (components[type] != null && (type != "Spin" || visible))
                        shape[type] = components[type].DeepClone();
            }
            var graph = CapabilityGraph.Capture(world.Session, Capabilities, Scope(resident), Owned(resident));
            bool empty = !graph["entities"].HasValues && !graph["sources"].HasValues;
            SetRecord(resident, objects, (string)ReadRecord(resident)["selected"], empty ? null : graph, true);
        }
        public JArray Capture()
        {
            CaptureResident();
            var rows = new JArray();
            foreach (var row in JObject.Parse(world.Session.Query("GallerySavedWorld"))["results"])
            {
                string id = (string)row["name"];
                rows.Add(new JObject { ["id"] = id, ["record"] = ReadRecord(id) });
            }
            return rows;
        }
        public void Restore(JArray records)
        {
            records ??= new JArray();
            if (records.Count > 80 || records.Select(r => (string)r["id"]).Distinct().Count() != records.Count)
                throw new ArgumentException("Invalid saved world catalog");
            foreach (var row in records)
                Validate((string)row["id"], JArray.Parse((string)row["record"]["objects"]), Graph(row["record"]));
            Unload();
            foreach (var row in JObject.Parse(world.Session.Query("GallerySavedWorld"))["results"])
                world.Session.SetComponentJson((string)row["name"], "GallerySavedWorld", new JObject { ["source"] = "", ["objects"] = "[]", ["selected"] = "", ["revision"] = 0, ["graph"] = "" }.ToString());
            foreach (var row in records)
            {
                string id = (string)row["id"];
                if (world.Session.Lookup(id) == 0)
                    throw new ArgumentException("Saved-world catalog entry missing");
                SetRecord(id, JArray.Parse((string)row["record"]["objects"]), (string)row["record"]["selected"], Graph(row["record"]), true);
            }
        }
        public void Select(string name)
        {
            if (resident == null)
                return;
            var record = ReadRecord(resident);
            record["selected"] = name;
            world.Session.SetComponentJson(resident, "GallerySavedWorld", record.ToString());
        }
        public void Move(string name, Vector3 local)
        {
            if (resident == null || name == "Ground")
                return;
            world.Session.SetComponentJson(resident + "Content." + name, "Position", V(local.x, local.y, local.z).ToString());
            Select(name);
            world.SyncViews();
        }
        public string Place(Vector3 local)
        {
            if (resident == null)
                return null;
            CaptureResident();
            var objects = Features(resident);
            int n = 1;
            while (objects.Any(o => (string)o["name"] == "Object" + n))
                n++;
            string name = "Object" + n;
            objects.Add(Shape(name, 0, local, Vector3.one * .8f, new Color(.60f, .78f, .76f), new Vector3(0, 25, 0)));
            string id = resident;
            Vector3 origin = root.position;
            Unload();
            SetRecord(id, objects, name);
            Activate(id, origin);
            return name;
        }
        public string Reuse(string sourceWorld, string name, string destinationWorld)
        {
            if (!Has(sourceWorld) || !Has(destinationWorld))
                throw new ArgumentException("Saved worlds required");
            CaptureResident();
            var feature = Features(sourceWorld).First(o => (string)o["name"] == name).DeepClone();
            var objects = Features(destinationWorld);
            int n = 1;
            while (objects.Any(o => (string)o["name"] == "Object" + n))
                n++;
            feature["name"] = "Object" + n;
            objects.Add(feature);
            bool reload = resident == destinationWorld;
            Vector3 origin = root ? root.position : Vector3.zero;
            if (reload)
                Unload();
            SetRecord(destinationWorld, objects, (string)feature["name"]);
            if (reload)
                Activate(destinationWorld, origin);
            return (string)feature["name"];
        }
        public void Unload()
        {
            if (resident == null)
                return;
            CaptureResident();
            world.ReleasePose(rootId, this);
            foreach (string path in world.Session.ManagedPaths.Where(p => p == GraphModule(resident) || p.StartsWith(Owned(resident), StringComparison.Ordinal)).ToArray())
            {
                world.Session.SetManagedScript(path, "");
                world.Session.DeleteEntity(path);
            }
            world.Session.SetManagedScript("GalleryContent_" + resident, "");
            world.Session.DeleteEntity("GalleryContent_" + resident);
            world.Session.DeleteEntity(resident + "Content");
            world.SyncViews();
            resident = null;
            root = null;
            rootId = 0;
            visible = false;
        }
        public void Dispose()
        {
            if (world && world.Session != null && !world.Session.IsDisposed)
                Unload();
        }
    }
}
#endif
