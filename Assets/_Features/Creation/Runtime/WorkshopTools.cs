using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Runtime.Flecs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    [DefaultExecutionOrder(-450)]
    public sealed class WorkshopTools : MonoBehaviour
    {
        private sealed class Item
        {
            public string Id, Shape, Source;
            public bool Actor;
            public float[] Size, Color;
        }

        private sealed class Wire
        {
            public string From, To, Id, Instance, Source;
            public string[] Needs;
        }

        private sealed class Kit
        {
            public Item[] Items;
            public Wire[] Wires;
        }

        private RuntimeExperience experience;
        private WorkshopOrbit navigation;
        private FlecsWorld world;
        private FlecsSession session;
        private Camera camera;
        private Kit kit;
        private GameObject tray, port, ghost;
        private readonly Dictionary<GameObject, Item> previews = new();
        private readonly List<Material> materials = new();
        private readonly Dictionary<Item, Material> previewMaterials = new();
        private Material wireMaterial;
        private LineRenderer line;
        private Item placing;
        private Vector2 down, wireDown;
        private float downAt;
        private bool emptyPress, wasDown, linking, trayOpening;
        private static readonly string[] OutputTypes = { "MoveGoal", "Position", "Scale", "Color" };
        private static readonly string[] OutputQueries = Array.ConvertAll(OutputTypes, type => "[in] " + type + ", [in] WorkshopOutput");
        private string selected;
        private int pointerClaimFrame = -1;
        public bool OwnsPointer => tray || ghost || linking || pointerClaimFrame == Time.frameCount;

        public void Initialize(RuntimeExperience owner, WorkshopOrbit orbit)
        {
            experience = owner;
            navigation = orbit;
            world = GetComponent<FlecsWorld>();
            camera = owner.Camera;
            session = world.Session;
            kit = JsonConvert.DeserializeObject<Kit>(Resources.Load<TextAsset>("Workshop/Data/WorkshopKit").text);
            wireMaterial = WorkshopVisuals.MakeMaterial(new Color(.75f, .95f, 1f));
            materials.Add(wireMaterial);
            foreach (var item in kit.Items)
            {
                var color = item.Color;
                var material = WorkshopVisuals.MakeMaterial(new Color(color[0], color[1], color[2]));
                materials.Add(material);
                previewMaterials.Add(item, material);
            }
        }

        public bool Place(string id, Vector3 point)
        {
            var item = kit.Items.FirstOrDefault(candidate => candidate.Id == id);
            if (item == null || world.Session.Lookup(id) != 0)
                return false;
            EnsureRules();
            if (item.Actor)
            {
                var recipe = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text);
                foreach (var part in recipe["parts"])
                    if (part["color"] != null)
                        part["color"] = JArray.FromObject(item.Color);
                var config = JObject.Parse(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
                config["driver"] = "follow";
                config["spawn"] = new JArray(point.x, 0, point.z);
                experience.CreateModel(id, recipe.ToString(Formatting.None));
                experience.Configure(id, config.ToString(Formatting.None));
                var actor = experience.Models[id].Actor;
                actor.ReadDevices = false;
                world.Session.SetComponentJson(actor.GoalPath, "MoveGoal",
                    JsonConvert.SerializeObject(new
                    {
                        x = point.x,
                        z = point.z,
                        stopDistance = .24f
                    }));
            }
            else
            {
                string source = item.Source.Replace("%x%", Number(point.x)).Replace("%z%", Number(point.z))
                    .Replace("%before%", Number(point.z - 1.25f)).Replace("%after%", Number(point.z + 1.35f));
                experience.ApplyEdit(source);
            }
            return true;
        }

        public bool Connect(string from, string to)
        {
            var wire = kit.Wires.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);
            if (wire == null || !Ready(wire))
                return false;
            var former = Installed(wire.Instance);
            if (former == wire)
                return true;
            experience.ApplyBehavior(wire.Id, wire.Source);
            if (former != null && former.To != wire.To)
                StopActor(former.To);
            return true;
        }

        public bool Disconnect(string from)
        {
            var wire = kit.Wires.FirstOrDefault(w => w.From == from && Installed(w.Instance) == w);
            if (wire == null)
                return false;
            RemoveWire(wire);
            return true;
        }

        private void RemoveWire(Wire wire)
        {
            foreach (var candidate in kit.Wires)
                if (candidate.Needs.Contains(wire.Instance) && Installed(candidate.Instance) == candidate)
                    RemoveWire(candidate);
            experience.RemoveBehavior(wire.Id);
            if (world.Session.Lookup(wire.Instance) != 0)
                world.Session.DeleteEntity(wire.Instance);
            StopActor(wire.To);
            if (wire.To == "ChainGate")
            {
                var waiting = JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("ChainWait"), "Position"));
                world.Session.SetComponentJson("ChainGate", "Position", JsonConvert.SerializeObject(
                    new
                    {
                        x = (float)waiting["x"],
                        y = .4f,
                        z = (float)waiting["z"] + 1.25f
                    }));
            }
        }

        private void StopActor(string id)
        {
            if (!experience.Models.TryGetValue(id, out var model) || !model.Actor.Motor)
                return;
            var point = model.Actor.Motor.transform.position;
            world.Session.SetComponentJson(model.Actor.GoalPath, "MoveGoal",
                JsonConvert.SerializeObject(new
                {
                    x = point.x,
                    z = point.z,
                    stopDistance = .24f
                }));
        }

        private Wire Installed(string instance)
        {
            ulong entity = world.Session.Lookup(instance);
            if (entity == 0)
                return null;
            string json = world.Session.ComponentJson(entity, "WorkshopConnection");
            if (string.IsNullOrEmpty(json))
                return null;
            var link = JObject.Parse(json);
            return kit.Wires.FirstOrDefault(w => w.Instance == instance && w.From == (string)link["source"] &&
                w.To == (string)link["target"] && world.Session.Lookup("RuntimeBehaviors." + w.Id) != 0);
        }

        private bool Ready(Wire wire)
        {
            if (world.Session.Lookup(wire.From) == 0 || world.Session.Lookup(wire.To) == 0 ||
                !wire.Needs.All(path => kit.Wires.Any(w => w.Instance == path) ? Installed(path) != null :
                    world.Session.Lookup(path) != 0))
                return false;
            // A lure and crossing can each drive an actor, with a single goal writer per actor.
            var other = wire.Instance == "ChainBellLink" ? Installed("ChainCrossLink") :
                wire.Instance == "ChainCrossLink" ? Installed("ChainBellLink") : null;
            return other == null || other.To != wire.To;
        }

        private void EnsureRules()
        {
            if (world.Session.Lookup("WorkshopProp") == 0)
                experience.ApplyEdit("WorkshopProp {}");
            if (world.Session.Lookup("WorkshopBell") == 0)
                experience.ApplyBehavior("chainRules", Resources.Load<TextAsset>("Workshop/Examples/AssemblyChain/WorkshopChain").text);
        }

        private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Script outputs live on their wires. Applying values here keeps the target entities
        // independent of managed-script ownership when a wire is removed.
        private void ApplyOutputs()
        {
            var activeSession = world.Session;
            if (activeSession.Lookup("WorkshopOutput") == 0)
                return;
            for (int t = 0; t < OutputTypes.Length; t++)
            {
                string type = OutputTypes[t];
                if (activeSession.Lookup(type) == 0)
                    continue;
                string changed = activeSession.QueryChanges(OutputQueries[t]);
                if (string.IsNullOrEmpty(changed))
                    continue;
                foreach (var row in (JArray)JObject.Parse(changed)["results"])
                {
                    var fields = row["fields"]["values"];
                    string target = (string)fields[1]["target"];
                    if (activeSession.Lookup(target) != 0)
                        activeSession.SetComponentJson(target, type, fields[0].ToString(Formatting.None));
                }
            }
        }

        private void Update()
        {
            if (kit == null || !camera)
                return;
            ApplyOutputs();
            var pointer = Pointer.current;
            if (pointer == null)
                return;
            RoutePointer(pointer.position.ReadValue(), pointer.press.isPressed, Time.unscaledTime,
                Keyboard.current?.escapeKey.wasPressedThisFrame == true && !experience.EditingText,
                pressedThisFrame: pointer.press.wasPressedThisFrame);
        }

        public void RoutePointer(Vector2 point, bool pressed, float now, bool cancel = false, bool pressedThisFrame = false)
        {
            if (experience.Dock)
            {
                if (pressedThisFrame && !pressed)
                    experience.Dock.StepSeedPointer(point, true, cancel);
                experience.Dock.StepSeedPointer(point, pressed, cancel);
                if (!experience.Dock.HasWorld)
                    return;
            }
            StepPointer(point, pressed, now, navigation.IsNavigating || experience.WorldDragActive ||
                experience.SeedOwnsPointer || experience.CoversWorldPointer(point), cancel);
        }

        public void StepPointer(Vector2 point, bool pressed, float now, bool blocked = false, bool cancel = false)
        {
            if (session != world.Session)
            {
                session = world.Session;
                Close();
                selected = null;
                wasDown = pressed;
                emptyPress = false;
            }

            bool started = pressed && !wasDown, released = !pressed && wasDown;
            wasDown = pressed;
            if (tray || ghost || linking)
                pointerClaimFrame = Time.frameCount;
            if (cancel)
            {
                Close();
                selected = null;
                emptyPress = false;
            }
            if (blocked)
            {
                if (released)
                {
                    linking = false;
                    emptyPress = false;
                    if (line)
                        line.enabled = false;
                }
                return;
            }
            var ray = camera.ScreenPointToRay(point);
            var floor = new Plane(Vector3.up, Vector3.zero);
            bool onFloor = floor.Raycast(ray, out float depth);
            Vector3 at = onFloor ? ray.GetPoint(depth) : Vector3.zero;
            onFloor &= Mathf.Abs(at.x) <= 7.6f && Mathf.Abs(at.z) <= 7.6f;
            if (tray)
            {
                tray.transform.rotation = camera.transform.rotation;
                if (!released)
                    return;
                if (trayOpening)
                {
                    trayOpening = false;
                    return;
                }
                var pick = previews.FirstOrDefault(pair => pair.Key.GetComponent<Renderer>().bounds.IntersectRay(ray));
                if (pick.Key)
                {
                    placing = pick.Value;
                    ghost = MakePreview(placing, transform);
                }
                Destroy(tray);
                tray = null;
                previews.Clear();
                return;
            }
            if (ghost)
            {
                ghost.SetActive(onFloor);
                if (onFloor)
                {
                    ghost.transform.position = at + Vector3.up * .3f;
                    if (released)
                    {
                        Place(placing.Id, at);
                        Close();
                    }
                }
                else if (released)
                    Close();
                return;
            }

            UpdatePort();
            if (linking)
            {
                var target = Pick(ray);
                if (released)
                {
                    if ((point - wireDown).sqrMagnitude < 64)
                        Disconnect(selected);
                    else
                        Connect(selected, target);
                    linking = false;
                    if (line)
                        line.enabled = false;
                }
                else
                    DrawWire(port.transform.position, target != null && BoundsFor(target, out Bounds bounds) ? bounds.center :
                    onFloor ? at + Vector3.up * .15f : ray.GetPoint(4));
                return;
            }

            if (started && port && port.GetComponent<Renderer>().bounds.IntersectRay(ray))
            {
                wireDown = point;
                linking = true;
                return;
            }
            if (started)
            {
                down = point;
                downAt = now;
                emptyPress = onFloor && !experience.HitsWorldObject(ray);
            }
            if (emptyPress && (point - down).sqrMagnitude > 81)
                emptyPress = false;
            if (pressed && emptyPress && now - downAt >= .34f)
            {
                emptyPress = false;
                selected = null;
                UpdatePort();
                OpenTray(at);
            }
            if (released)
            {
                emptyPress = false;
                if ((point - down).sqrMagnitude <= 81)
                    selected = Pick(ray);
            }
        }

        private void OpenTray(Vector3 at)
        {
            var items = kit.Items.Where(item => world.Session.Lookup(item.Id) == 0).ToArray();
            if (items.Length == 0)
                return;
            trayOpening = true;
            tray = new GameObject("Workshop miniatures");
            tray.transform.SetParent(transform, false);
            float scale = camera.orthographicSize / 5.5f;
            var viewPoint = camera.WorldToViewportPoint(at + camera.transform.up * (scale * 1.05f));
            viewPoint.x = Mathf.Clamp(viewPoint.x, .14f, .86f);
            viewPoint.y = Mathf.Clamp(viewPoint.y, .17f, .83f);
            tray.transform.SetPositionAndRotation(camera.ViewportToWorldPoint(viewPoint), camera.transform.rotation);
            tray.transform.localScale = Vector3.one * scale;
            const float radius = 1.02f;
#if CAP_FLEXALON
            var layout = tray.AddComponent<Flexalon.FlexalonCircleLayout>();
            layout.Plane = Flexalon.Plane.XY;
            layout.Radius = radius;
            layout.SpacingType = Flexalon.FlexalonCircleLayout.SpacingOptions.Evenly;
#endif
            for (int i = 0; i < items.Length; i++)
            {
                var preview = MakePreview(items[i], tray.transform);
                float angle = i * Mathf.PI * 2 / items.Length;
                preview.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
#if CAP_FLEXALON
                preview.AddComponent<Flexalon.FlexalonObject>();
#endif
                previews.Add(preview, items[i]);
            }
#if CAP_FLEXALON
            layout.ForceUpdate();
#endif
        }

        private GameObject MakePreview(Item item, Transform parent)
        {
            var s = item.Size;
            var main = WorkshopVisuals.Miniature(parent, Enum.Parse<PrimitiveType>(item.Shape),
                new Vector3(s[0], s[1], s[2]), previewMaterials[item]);
            main.name = item.Id + " preview";
            if (item.Actor)
            {
                bool bunny = item.Id == "ChainBunny";
                var top = WorkshopVisuals.Miniature(main.transform, PrimitiveType.Sphere,
                    bunny ? new Vector3(.42f, .95f, .45f) : new Vector3(.63f, .6f, .63f), previewMaterials[item]);
                top.transform.localPosition = bunny ? new Vector3(0, .6f, 0) : new Vector3(.42f, .45f, 0);
            }
            return main;
        }

        private bool BoundsFor(string id, out Bounds bounds)
        {
            bounds = default;
            if (experience.Models.TryGetValue(id, out var model) && model.Creation.PresentationRoot)
            {
                bounds = model.Creation.WorldBounds();
                return true;
            }
            var view = world.FindView(world.Session.Lookup(id));
            var renderer = view ? view.GetComponentInChildren<Renderer>() : null;
            if (!renderer)
                return false;
            bounds = renderer.bounds;
            return true;
        }

        private string Pick(Ray ray)
        {
            string result = null;
            float nearest = float.PositiveInfinity;
            foreach (var item in kit.Items)
            {
                if (BoundsFor(item.Id, out Bounds bounds) && bounds.IntersectRay(ray, out float distance) && distance < nearest)
                {
                    result = item.Id;
                    nearest = distance;
                }
            }
            return result;
        }

        public bool IsOverPort(Vector2 point) =>
            port && port.GetComponent<Renderer>().bounds.IntersectRay(camera.ScreenPointToRay(point));

        private void UpdatePort()
        {
            if (selected == null || !BoundsFor(selected, out Bounds bounds) ||
                !kit.Wires.Any(w => w.From == selected && Ready(w)))
            {
                if (port)
                    Destroy(port);
                port = null;
                return;
            }
            if (!port)
            {
                port = WorkshopVisuals.Miniature(transform, PrimitiveType.Sphere, Vector3.one, wireMaterial);
                port.name = "Workshop link port";
            }
            port.transform.position = bounds.center + Vector3.up * (bounds.extents.y + .22f) + camera.transform.right * .22f;
            port.transform.localScale = Vector3.one * Mathf.Clamp(camera.orthographicSize * .044f, .10f, .35f);
        }

        private void DrawWire(Vector3 start, Vector3 end)
        {
            if (!line)
            {
                line = new GameObject("Workshop wire").AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false);
                line.sharedMaterial = wireMaterial;
                line.positionCount = 3;
            }
            line.enabled = true;
            line.widthMultiplier = Mathf.Clamp(camera.orthographicSize * .008f, .013f, .06f);
            line.SetPosition(0, start);
            line.SetPosition(1, (start + end) * .5f + Vector3.up * .18f);
            line.SetPosition(2, end);
        }

        private void Close()
        {
            if (tray)
                Destroy(tray);
            if (ghost)
                Destroy(ghost);
            if (port)
                Destroy(port);
            tray = ghost = port = null;
            previews.Clear();
            placing = null;
            linking = false;
            if (line)
                line.enabled = false;
        }

        private void OnDestroy()
        {
            Close();
            foreach (var material in materials)
            if (material)
                Destroy(material);
        }
    }
}

