#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Runtime.Agents.UI;
using Runtime.Flecs;
using Flexalon;
using Newtonsoft.Json.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Game
{
    [DefaultExecutionOrder(-390)]
    public sealed partial class CrystalWorldGallery : MonoBehaviour
    {
        public sealed class Item
        {
            public GalleryPreviewEntry Entry;
            public Renderer Shell;
            public SphereCollider Hit;
            public int Level, Slot;
            public int PlacedLayer, PlacedSlot; // the native GalleryPlacement, read by Arrange
            public float Alpha = 1, Highlight, Coverage, Visibility = 1, PublishedOpacity = 1, ScreenRadius, ScreenDepth;
            public Vector2 ScreenCenter; public bool Pickable = true;
            public readonly GalleryFeedbackSpring OpacitySpring = new(1), HighlightSpring = new();
            public FlexalonLerpAnimator Animator;
            public readonly MaterialPropertyBlock Properties = new();
            public Renderer[] Renderers;
            public Color[][] Colors;
        }
        static readonly string[] OriginalIds = { "GalleryAquarium", "GallerySpace", "GalleryGarden", "GalleryRain", "GalleryMarch", "GalleryMusic", "GalleryOcean", "GalleryOrbit" };
        readonly List<Item> items = new();
        readonly List<Material> materials = new();
        readonly List<FlexalonCircleLayout> layers = new();
        readonly Dictionary<string, Item> byId = new();
        FlecsWorld world; RuntimeExperience experience; AgentChatPanel chat; Camera output;
        GalleryExperienceStore experiences;
        public GalleryExperienceStore Experiences => experiences;
        GalleryTreeArt art; GalleryPreviewStage stage; GalleryLayoutProfile profile;
        Transform layoutRoot, invitation, cameraTarget, cameraRig; Renderer invitationShell; Transform invitationCore;
        CinemachineCamera virtualCamera; CinemachineFollow follow;
        JObject view, focus, activity, config, navigation;
#if UNITY_EDITOR
        long profileSignature, configSignature;
#endif
        bool layoutValid;
        JObject layoutConfig;
        readonly List<Item> layoutOrder = new(), arrangeScratch = new();
        bool presentationFrozen;
        int appliedGlowSeed = int.MinValue;
        float time, motionTime, nextRead, currentYaw, currentPitch, currentZoom = 1, entryBlend, travel, travelStart;
        readonly GalleryFeedbackSpring invitationSpring = new(1), destinationSpring = new();
        Vector3 currentCenter, centerVelocity; float zoomVelocity, invitationAlpha = 1; Renderer[] seedRenderers; bool initialized, ready, traveling, disposed, navigationDirty; float saveAfter; int revealFrame;
        public bool ReadDevices { get; set; } = true;
        public IReadOnlyList<Item> Items => items;
        public IReadOnlyList<FlexalonCircleLayout> Layers => layers;
        public GalleryTreeArt Art => art;
        public GalleryLayoutProfile Profile => profile;
        public Camera ViewCamera => output;
        public GalleryPreviewStage Stage => stage;
        public bool InsideWorld => (bool?)focus?["inside"] == true;
        public bool Creating => (bool?)activity?["draft"] == true;
        public string CurrentWorldId => (string)focus?["current"];
        public bool IsDragging => dragged != null;
        public Transform DragGhost => ghost;
        public static string SavePath => Path.Combine(Application.persistentDataPath, "GalleryPrototype", "tree-arrangement.json");
        float F(string key) => (float)config[key];
        // One parsed JObject per (entity, component), reused across frames while the native JSON bytes are unchanged and the
        // caller has not modified it. Reparsing every component every frame was the main idle GC source.
        sealed class CachedComponent
        {
            public JObject Value;
            public byte[] Bytes = Array.Empty<byte>();
            public int Length = -1, Frame = -1;
            public bool Modified = true;
            readonly System.ComponentModel.PropertyChangedEventHandler changed;
            readonly System.Collections.Specialized.NotifyCollectionChangedEventHandler added;
            public CachedComponent()
            {
                changed = (_, _) => Modified = true;
                added = (_, _) => Modified = true;
            }
            public void Set(JObject value)
            {
                if (Value != null)
                {
                    Value.PropertyChanged -= changed;
                    Value.CollectionChanged -= added;
                }
                Value = value;
                Modified = false;
                if (value == null)
                    return;
                value.PropertyChanged += changed;
                value.CollectionChanged += added;
            }
        }
        readonly Dictionary<(string, string), CachedComponent> components = new();
        readonly Dictionary<string, ulong> entityIds = new();
        byte[] jsonBuffer = new byte[1024];
        int readFrame = -1, nativeReads;
        public int NativeReadsThisFrame => nativeReads;
        static readonly Unity.Profiling.ProfilerMarker NativeReadMarker = new("Game.Gallery.NativeRead");
        static readonly Unity.Profiling.ProfilerMarker AppearanceMarker = new("Game.Gallery.Appearance");
        static readonly string[] LayerIds = { "GalleryTreeLayer0", "GalleryTreeLayer1", "GalleryTreeLayer2", "GalleryTreeLayer3",
            "GalleryTreeLayer4", "GalleryTreeLayer5", "GalleryTreeLayer6" };
        static string LayerId(int layer) => layer >= 0 && layer < LayerIds.Length ? LayerIds[layer] : "GalleryTreeLayer" + layer;
        ulong Entity(string id, bool refresh = false)
        {
            if (refresh || !entityIds.TryGetValue(id, out var e))
            {
                e = world.Session.Lookup(id);
                if (e != 0)
                    entityIds[id] = e;
            }
            return e;
        }
        // Reads one component as JSON into a reusable buffer and parses only when the bytes changed (docs/FLECS-ARCHITECTURE-AUDIT.md).
        // Within a frame the same instance is returned, as before; Write invalidates it so the next read returns native truth.
        JObject Read(string id, string type)
        {
            if (readFrame != Time.frameCount)
            {
                readFrame = Time.frameCount;
                nativeReads = 0;
            }
            if (!components.TryGetValue((id, type), out var cached))
            {
                cached = new CachedComponent();
                components.Add((id, type), cached);
            }
            if (cached.Frame == readFrame)
                return cached.Value;
            using (NativeReadMarker.Auto())
            {
                ulong typeId = world.Session.TypeId(type);
                int length = typeId == 0 ? -1 : world.Session.ComponentJsonBytes(Entity(id), typeId, ref jsonBuffer);
                if (length < 0 && typeId != 0)
                    length = world.Session.ComponentJsonBytes(Entity(id, true), typeId, ref jsonBuffer);
                nativeReads++;
                if (length < 0)
                {
                    cached.Set(null);
                    cached.Length = -1;
                }
                else if (cached.Modified || cached.Value == null || length != cached.Length
                    || !new ReadOnlySpan<byte>(jsonBuffer, 0, length).SequenceEqual(new ReadOnlySpan<byte>(cached.Bytes, 0, length)))
                {
                    cached.Set(JObject.Parse(Encoding.UTF8.GetString(jsonBuffer, 0, length)));
                    if (cached.Bytes.Length < length)
                        cached.Bytes = new byte[System.Math.Max(length, 64)];
                    Buffer.BlockCopy(jsonBuffer, 0, cached.Bytes, 0, length);
                    cached.Length = length;
                }
            }
            cached.Frame = readFrame;
            return cached.Value;
        }
        void Invalidate(string id, string type)
        {
            if (components.TryGetValue((id, type), out var cached))
                cached.Frame = -1;
        }
        void InvalidateEntity(string id)
        {
            foreach (var pair in components)
                if (pair.Key.Item1 == id)
                    pair.Value.Frame = -1;
        }
        // Typed single-entity writes for per-frame values (no JSON, no garbage).
        readonly ulong[] oneEntity = new ulong[1];
        readonly float[] oneFloat = new float[1];
        readonly VisibilityData[] oneVisibility = new VisibilityData[1];
        struct VisibilityData
        {
            public float covered, opacity;
            public byte pickable;
        }
        struct EntryData
        {
            public int order, width, depth, kind;
            public byte active;
        }
        struct PlacementData
        {
            public int layer, slot;
        }
        void WriteFloat(string id, string type, float value)
        {
            oneEntity[0] = Entity(id);
            oneFloat[0] = value;
            world.Session.WriteBatch(type, oneEntity, oneFloat, 1);
            Invalidate(id, type);
        }
        void WriteVisibility(Item item, float covered, float opacity, bool pickable)
        {
            oneEntity[0] = Entity(item.Entry.Id);
            oneVisibility[0] = new VisibilityData { covered = covered, opacity = opacity, pickable = (byte)(pickable ? 1 : 0) };
            world.Session.WriteBatch("GalleryVisibility", oneEntity, oneVisibility, 1);
            Invalidate(item.Entry.Id, "GalleryVisibility");
        }
        // Typed binary read for per-frame scalars (single-f32 or leading-f32 structs); falls back to JSON if absent.
        struct Float2
        {
            public float a, b;
        }
        float ReadFloat(string id, string type, string field)
        {
            using (NativeReadMarker.Auto())
            {
                ulong e = Entity(id);
                try
                {
                    if (type == "GalleryMotion")
                    {
                        if (world.Session.TryRead<Float2>(e, type, out var v2))
                            return v2.a;
                    }
                    else if (world.Session.TryRead<float>(e, type, out var v))
                        return v;
                }
                catch (System.InvalidOperationException) { }
            }
            return (float)Read(id, type)[field];
        }
        void Write(string id, string type, JObject value)
        {
            world.Session.SetComponentJson(id, type, value.ToString(Newtonsoft.Json.Formatting.None));
            Invalidate(id, type); // next read returns native truth (same rounding as persisted values)
        }
        void WriteView() => Write("GalleryTreeSettings", "GalleryView", view);
        void WriteFocus() => Write("GalleryTreeSettings", "GalleryFocus", focus);
        void WriteEntry(Item item) => Write(item.Entry.Id, "GalleryEntry", new JObject { ["order"] = item.Entry.Order, ["width"] = item.Entry.Width, ["depth"] = item.Entry.Depth, ["kind"] = item.Entry.Kind, ["active"] = item.Entry.Active });

        public void Initialize(FlecsWorld source, RuntimeExperience runtime, AgentChatPanel agent)
        {
            world = source;
            experience = runtime;
            chat = agent;
            output = runtime.Camera;
            profile = ScriptableObject.CreateInstance<GalleryLayoutProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            LoadConfig(true);
            world.Session.SetManagedScript("GalleryTreeSchema", Resources.Load<TextAsset>("Gallery/Runtime/WorldGalleryTree").text);
            world.Session.SetManagedScript("GalleryTreeRules", Resources.Load<TextAsset>("Gallery/Runtime/WorldGalleryTreeMotion").text);
            world.Session.SetManagedScript("GalleryNavigation", Resources.Load<TextAsset>("Gallery/Runtime/WorldGalleryNavigation").text);
            experiences = new GalleryExperienceStore(world);
            navigation = Read("GalleryTreeSettings", "GalleryTreeNavigation");
            ApplyProfile(true);
            art = new GalleryTreeArt(profile);
            layoutRoot = new GameObject("Layered world gallery - Flexalon").transform;
            layoutRoot.SetParent(transform, false);
            foreach (var id in OriginalIds)
                AddItem(id);
            for (int i = 8; i < 30; i++)
            {
                string id = "GalleryTreeSample" + i;
                EnsureEntity(id, i, i % 8, true);
                AddItem(id);
            }
            invitation = new GameObject("New world invitation - protected").transform;
            invitation.SetParent(transform, false);
            invitation.position = new Vector3(0, 2.1f, 0);
            invitationShell = art.Sphere(invitation, F("diameter"), "Single invitation shell");
            var seedPrefab = Resources.Load<GameObject>("Workshop/Art/Seed/WorkshopSeedArt");
            if (!seedPrefab)
                throw new InvalidOperationException("Baked workshop seed is missing.");
            invitationCore = Instantiate(seedPrefab, invitation, false).transform;
            invitationCore.name = "New world seed";
            seedRenderers = invitationCore.GetComponentsInChildren<Renderer>();
            var seedBounds = seedRenderers[0].bounds;
            foreach (var r in seedRenderers)
                seedBounds.Encapsulate(r.bounds);
            float seedScale = .55f / Mathf.Max(.01f, seedBounds.size.y);
            invitationCore.localScale *= seedScale;
            invitationCore.localPosition = -invitation.InverseTransformPoint(seedBounds.center) * seedScale;
            foreach (var r in seedRenderers)
            {
                var m = new Material(Shader.Find("Gallery/Seed"));
                materials.Add(m);
                r.sharedMaterial = m;
            }
            // Pick volumes are queried with Collider.Raycast only. As triggers they never block actor motors or physics
            // in a world that shares the scene with the gallery.
            var invitationHit = invitation.gameObject.AddComponent<SphereCollider>();
            invitationHit.radius = F("diameter") * .5f;
            invitationHit.isTrigger = true;
            cameraTarget = new GameObject("Native gallery camera target").transform;
            cameraTarget.SetParent(transform, false);
            cameraRig = new GameObject("Gallery Cinemachine orbit").transform;
            cameraRig.SetParent(transform, false);
            virtualCamera = cameraRig.gameObject.AddComponent<CinemachineCamera>();
            virtualCamera.Priority = 20;
            virtualCamera.Follow = cameraTarget;
            virtualCamera.LookAt = cameraTarget;
            output.orthographic = false;
            output.fieldOfView = 32;
            output.nearClipPlane = .025f;
            output.farClipPlane = 240;
            output.backgroundColor = new Color(.094f, .145f, .165f);
            virtualCamera.Lens = LensSettings.FromCamera(output);
            follow = cameraRig.gameObject.AddComponent<CinemachineFollow>();
            follow.TrackerSettings.PositionDamping = Vector3.zero;
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            var light = new GameObject("Gallery soft daylight").AddComponent<Light>();
            light.transform.SetParent(transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            light.intensity = 1.7f;
            light.color = new Color(1, .95f, .86f);
            stage = new GalleryPreviewStage(transform, output);
            stage.PrepareContext = PrepareExperienceContext;
            stage.Camera.fieldOfView = 32;
            exitFeedback = new GalleryExitFeedback(output);
            ReadState();
            bool savedLaunch = File.Exists(SavePath);
            LoadArrangement();
            ReadState();
            Arrange(true);
            if (!InsideWorld)
            {
                view["pitch"] = 90;
                if (!savedLaunch)
                {
                    view["x"] = 0;
                    view["y"] = GalleryCenterY;
                    view["z"] = 0;
                    view["zoom"] = profile.FullTreeZoom;
                }
                WriteView();
            }
            SetupScreenControls();
            ApplyCamera(true);
            revealFrame = Time.frameCount + 4;
            initialized = true;
        }
        void EnsureEntity(string id, int order, int kind, bool active)
        {
            if (world.Session.Lookup(id) != 0)
                return;
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^Gallery[A-Za-z0-9_]+$"))
                throw new ArgumentException("Invalid gallery world ID");
            world.Session.SetManagedScript("GalleryCatalog_" + id, id + " { GalleryEntry: {" + order + ", 1, 1, " + kind + ", " + (active ? "true" : "false") + "}; GalleryCell: {1}; GalleryProjection: {false}; GallerySample: {0}; GalleryResidence: {0,0,0}; GalleryTrack: {0,0,0}; GalleryView: {0,0.5,0,0,23,3,6} }");
        }
        void AddItem(string id)
        {
            if (byId.ContainsKey(id))
                return;
            var data = Read(id, "GalleryEntry");
            var e = new GalleryPreviewEntry { Id = id, Order = (int)data["order"], Width = (int)data["width"], Depth = (int)data["depth"], Kind = (int)data["kind"], Active = (bool)data["active"] };
            e.Anchor = new GameObject(id).transform;
            e.Anchor.SetParent(layoutRoot, false);
            e.Anchor.gameObject.AddComponent<FlexalonObject>().Size = Vector3.one * F("diameter");
            var animator = e.Anchor.gameObject.AddComponent<FlexalonLerpAnimator>();
            animator.InterpolationSpeed = F("response");
            animator.AnimateRotation = animator.AnimateScale = false;
            e.Content = new GameObject("Identifying world features").transform;
            e.Content.SetParent(e.Anchor, false);
            e.SavedFeatures = experiences.Features(id);
            e.Preview = experiences.Has(id) ? new GalleryMockPreview(e.Content, e.SavedFeatures, materials) : new GalleryMockPreview(e.Content, e.Kind, e.Width, e.Depth, materials);
            e.NaturalBounds = e.Preview.MeasureBounds();
            e.TableScale = F("diameter") * .40f / Mathf.Max(.1f, e.NaturalBounds.extents.magnitude);
            e.Content.localScale = Vector3.one * e.TableScale;
            e.Content.localPosition = -e.NaturalBounds.center * e.TableScale;
            foreach (var c in e.Content.GetComponentsInChildren<Collider>())
                c.enabled = false;
            var item = new Item { Entry = e, Animator = animator, Shell = art.Sphere(e.Anchor, F("diameter"), "Single world shell") };
            item.Hit = e.Anchor.gameObject.AddComponent<SphereCollider>();
            item.Hit.isTrigger = true; // pick volume only (see the invitation)
            item.Hit.radius = F("diameter") * .5f;
            item.Renderers = e.Content.GetComponentsInChildren<Renderer>(true);
            item.Colors = item.Renderers.Select(r => r.sharedMaterials.Select(m => m.color).ToArray()).ToArray();
            foreach (var m in item.Renderers.SelectMany(r => r.sharedMaterials).Distinct())
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_ZWrite", 0);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = 3000;
            }
            foreach (var r in item.Renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.forceRenderingOff = true;
            }
            item.Shell.forceRenderingOff = true;
            items.Add(item);
            byId.Add(id, item);
            WriteVisibility(item, 0, 1, true);
        }
        // Gallery values come from Flecs Script (GalleryConfig.flecs). In the Editor that script is generated live from Gallery.asset.
        void LoadConfig(bool force = false)
        {
            string source;
#if UNITY_EDITOR
            GameTheme.RefreshFromEditor(world.Session);
            var asset = GalleryConfigScript.EditorAsset;
            long signature = FieldSignature.Of(asset); // allocation-free; the JSON signature allocated every 0.1 s
            if (!force && signature == configSignature)
                return;
            configSignature = signature;
            source = asset ? GalleryConfigScript.Generate(asset) : null;
            if (source == null)
#endif
                source = RuntimeComposition.Current.GalleryConfig ? RuntimeComposition.Current.GalleryConfig.text : throw new InvalidOperationException("Gallery config script missing.");
            GalleryConfigScript.Load(world.Session, source, profile);
        }
        void ApplyProfile(bool force = false)
        {
#if UNITY_EDITOR
            long signature = FieldSignature.Of(profile) * 31 + FieldSignature.Of(Theme);
            if (!force && signature == profileSignature)
                return;
            profileSignature = signature;
#endif
            config = new JObject { ["diameter"] = Mathf.Max(.3f, profile.WorldDiameter), ["worldGap"] = Mathf.Max(0, profile.WorldSpacing), ["layerGap"] = Mathf.Max(1, profile.LayerSpacing), ["width"] = Mathf.Max(.03f, profile.EffectiveGuideWidth), ["opacity"] = Mathf.Clamp01(profile.LayerOpacity), ["sphereOpacity"] = Mathf.Clamp01(profile.SphereOpacity), ["rim"] = Mathf.Clamp01(profile.WorldSphereOpacity), ["frost"] = profile.Frosted ? 1 : 0, ["rearFade"] = Mathf.Clamp01(profile.RearWorldFade), ["speed"] = profile.IdleRotationEnabled ? profile.IdleDegreesPerSecond * Mathf.Deg2Rad : 0, ["response"] = Mathf.Max(.1f, profile.LayoutResponse), ["style"] = (int)profile.LayerGuide, ["feather"] = Mathf.Max(.04f, profile.RingClearanceFeather) };
            Write("GalleryTreeSettings", "GalleryTreeConfig", config);
            Write("GalleryTreeSettings", "GalleryReturnStyle", new JObject { ["seconds"] = Mathf.Max(.5f, profile.ReturnSeconds), ["revealSeconds"] = Mathf.Max(.3f, profile.ReturnRevealSeconds), ["glassSeconds"] = Mathf.Max(.2f, profile.GlassPassSeconds) });
            bool reseed = force || appliedGlowSeed != profile.RingTravelSeed;
            var random = new System.Random(profile.RingTravelSeed);
            float firstDirection = random.Next(2) == 0 ? -1 : 1;
            appliedGlowSeed = profile.RingTravelSeed;
            for (int i = 1; i <= 6; i++)
            {
                var intent = Read(LayerId(i), "GalleryLayerIntent");
                intent["angle"] = (float)Read(LayerId(i), "GalleryLayerState")["turn"];
                intent["started"] = time;
                intent["speed"] = F("speed") * (i % 2 == 1 ? 1 : -1) / (1 + i * .2f);
                if (reseed)
                {
                    intent["glowPhase"] = (float)random.NextDouble() * Mathf.PI * 2;
                    float direction = i == 1 ? firstDirection : i == 2 ? -firstDirection : random.Next(2) == 0 ? -1 : 1;
                    intent["glowRate"] = direction * Mathf.Lerp(.8f, 1.2f, (float)random.NextDouble());
                }
                Write(LayerId(i), "GalleryLayerIntent", intent);
            }
            if (output)
                output.backgroundColor = Theme.Background;
            if (chat)
                chat.ApplyPalette(Theme.UI);
            if (experience?.Dock)
                experience.Dock.ApplyTheme(Theme.UI);
            if (uiDocument)
                Theme.UI.Apply(uiDocument.rootVisualElement);
            layoutValid = false;
        }
        /// <summary>Acts on GalleryTreeSettings.GalleryEnterIntent written by Flecs Script, then clears it.</summary>
        void ConsumeEnterIntent()
        {
            string id = (string)Read("GalleryTreeSettings", "GalleryEnterIntent")?["world"];
            if (string.IsNullOrEmpty(id))
                return;
            Write("GalleryTreeSettings", "GalleryEnterIntent", new JObject { ["world"] = "" });
            if (!InsideWorld)
                EnterWorld(id);
        }
        void ReadState()
        {
            config = Read("GalleryTreeSettings", "GalleryTreeConfig");
            view = Read("GalleryTreeSettings", "GalleryView");
            focus = Read("GalleryTreeSettings", "GalleryFocus");
            activity = Read("GalleryTreeSettings", "GalleryTreeActivity");
            ReadEntries();
        }
        // One typed read of every catalog entry through a stable native query handle (no JSON).
        void ReadEntries()
        {
            int count;
            using (NativeReadMarker.Auto())
            {
                count = world.Session.ReadQuery("GalleryEntry", null, ref entryEntities, ref entryTargets, ref entryValues);
                nativeReads++;
            }
            RefreshItemMap();
            for (int i = 0; i < count; i++)
                if (itemsByEntity.TryGetValue(entryEntities[i], out var item))
                {
                    item.Entry.Order = entryValues[i].order;
                    item.Entry.Active = entryValues[i].active != 0;
                }
        }
        void RefreshItemMap()
        {
            if (itemsByEntityRevision == world.Session.SourceRevision && itemsByEntityCount == items.Count)
                return;
            itemsByEntity.Clear();
            foreach (var item in items)
            {
                ulong e = Entity(item.Entry.Id, true);
                if (e != 0)
                    itemsByEntity[e] = item;
            }
            itemsByEntityRevision = world.Session.SourceRevision;
            itemsByEntityCount = items.Count;
        }
        // The native app module owns the ring of every active world (GalleryPlacement); this only reads it.
        void ReadPlacements()
        {
            foreach (var item in items)
                item.PlacedLayer = item.PlacedSlot = 0;
            int count;
            using (NativeReadMarker.Auto())
            {
                count = world.Session.ReadQuery("GalleryPlacement", null, ref placementEntities, ref placementTargets, ref placementValues);
                nativeReads++;
            }
            for (int i = 0; i < count; i++)
                if (itemsByEntity.TryGetValue(placementEntities[i], out var item))
                {
                    item.PlacedLayer = placementValues[i].layer;
                    item.PlacedSlot = placementValues[i].slot;
                }
        }
        ulong[] entryEntities = new ulong[64], entryTargets = new ulong[64];
        EntryData[] entryValues = new EntryData[64];
        ulong[] placementEntities = new ulong[64], placementTargets = new ulong[64];
        PlacementData[] placementValues = new PlacementData[64];
        readonly Dictionary<ulong, Item> itemsByEntity = new();
        int itemsByEntityRevision = -1, itemsByEntityCount = -1;
        void EnsureLayers(int count)
        {
            while (layers.Count < count)
            {
                var t = new GameObject("Flexalon tier " + (layers.Count + 1)).transform;
                t.SetParent(layoutRoot, false);
                t.gameObject.AddComponent<FlexalonObject>().Size = Vector3.one * 30;
                var circle = t.gameObject.AddComponent<FlexalonCircleLayout>();
                circle.Plane = Flexalon.Plane.XZ;
                circle.SpacingType = FlexalonCircleLayout.SpacingOptions.Evenly;
                circle.Rotate = FlexalonCircleLayout.RotateOptions.None;
                layers.Add(circle);
            }
        }
        public void Arrange(bool immediate = false)
        {
            // Layers and slots come from the native GalleryPlacement, which follows GalleryEntry; every caller writes the entry first.
            // Allocation-free change check: same placed order and the same (unchanged, cached) config object as last time.
            ReadEntries();
            ReadPlacements();
            var ordered = arrangeScratch;
            ordered.Clear();
            foreach (var item in items)
                if (item.Entry.Active && item.PlacedLayer > 0)
                    ordered.Add(item);
            ordered.Sort(CompareLayoutOrder);
            if (!immediate && layoutValid && ReferenceEquals(config, layoutConfig) && SameOrder(ordered, layoutOrder))
                return;
            layoutValid = true;
            layoutConfig = config;
            layoutOrder.Clear();
            layoutOrder.AddRange(ordered);
            foreach (var item in items)
                item.Level = -1;
            int count = ordered.Count, cursor = 0, level = 0;
            while (cursor < count)
            {
                level = ordered[cursor].PlacedLayer;
                int n = 1;
                while (cursor + n < count && ordered[cursor + n].PlacedLayer == level)
                    n++;
                EnsureLayers(level);
                var layer = layers[level - 1];
                layer.Radius = level * (F("diameter") + F("worldGap"));
                layer.StartAtDegrees = level % 2 == 1 ? 13 : 30;
                layer.transform.localPosition = new Vector3(0, 2.1f - level * F("layerGap"), 0);
                layer.gameObject.SetActive(true);
                for (int i = 0; i < n; i++)
                {
                    var item = ordered[cursor + i];
                    var e = item.Entry;
                    int slot = item.PlacedSlot;
                    bool moved = e.Anchor.parent != layer.transform;
                    var previous = e.Anchor.position;
                    e.Anchor.SetParent(layer.transform, true);
                    e.Anchor.localRotation = Quaternion.identity;
                    e.Anchor.localScale = Vector3.one;
                    e.Anchor.SetSiblingIndex(slot);
                    e.Anchor.gameObject.SetActive(true);
                    item.Level = level;
                    item.Slot = slot;
                    e.Anchor.GetComponent<FlexalonObject>().Size = Vector3.one * F("diameter");
                    item.Hit.radius = F("diameter") * .5f;
                    item.Shell.transform.localScale = Vector3.one * F("diameter");
                    e.TableScale = F("diameter") * .40f / Mathf.Max(.1f, e.NaturalBounds.extents.magnitude);
                    e.Content.localScale = Vector3.one * e.TableScale;
                    e.Content.localPosition = -e.NaturalBounds.center * e.TableScale;
                    if (immediate)
                    {
                        float angle = (layer.StartAtDegrees + slot * 360f / n) * Mathf.Deg2Rad;
                        e.Anchor.localPosition = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * layer.Radius;
                    }
                    else if (moved)
                        e.Anchor.position = previous;
                }
                layer.MarkDirty();
                cursor += n;
            }
            for (int i = level; i < layers.Count; i++)
                layers[i].gameObject.SetActive(false);
            foreach (var item in items)
                if (item.Level < 0)
                    item.Entry.Anchor.gameObject.SetActive(false);
            art.EnsureBands(layoutRoot, level, F("diameter") + F("worldGap"), F("layerGap"), 2.1f, F("width"));
            global::Flexalon.Flexalon.GetOrCreate().UpdateDirtyNodes();
        }
        static readonly Comparison<Item> CompareLayoutOrder = (a, b) =>
        {
            int layer = a.PlacedLayer.CompareTo(b.PlacedLayer);
            return layer != 0 ? layer : a.PlacedSlot.CompareTo(b.PlacedSlot);
        };
        static bool SameOrder(List<Item> a, List<Item> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
        void SetPaused(bool pause)
        {
            // Only a held pose freezes layout interpolation. Focus and ring turns pause idle motion but must let swaps/insertions finish moving.
            bool lockPose = IsDragging || InsideWorld || Creating || entryArmed || Returning;
            foreach (var item in items)
                if (item.Animator)
                    item.Animator.InterpolationSpeed = lockPose ? 0 : F("response");
            bool justLocked = lockPose && !presentationFrozen;
            presentationFrozen = lockPose;
            if (justLocked)
                for (int i = 1; i <= layers.Count; i++)
                {
                    var intent = Read(LayerId(i), "GalleryLayerIntent");
                    intent["angle"] = -layers[i - 1].transform.localEulerAngles.y * Mathf.Deg2Rad;
                    intent["started"] = time;
                    Write(LayerId(i), "GalleryLayerIntent", intent);
                }
            activity = Read("GalleryTreeSettings", "GalleryTreeActivity");
            float rate = pause ? 0 : 1;
            if (Mathf.Approximately((float)activity["rate"], rate))
                return;
            for (int i = 1; i <= 6; i++)
            {
                string id = LayerId(i);
                var intent = Read(id, "GalleryLayerIntent");
                intent["angle"] = justLocked && i <= layers.Count ? -layers[i - 1].transform.localEulerAngles.y * Mathf.Deg2Rad : ReadFloat(id, "GalleryLayerState", "turn");
                intent["started"] = time;
                Write(id, "GalleryLayerIntent", intent);
            }
            activity["elapsed"] = motionTime;
            activity["started"] = time;
            activity["rate"] = rate;
            Write("GalleryTreeSettings", "GalleryTreeActivity", activity);
        }
        void Update()
        {
            if (!initialized || !world || world.Session == null || world.Session.IsDisposed)
                return;
            time = ReadFloat("GalleryAnimation", "GalleryMotion", "time");
            motionTime = ReadFloat("GalleryTreeSettings", "GalleryTreeMotion", "time");
            if (!IsDragging && Time.unscaledTime >= nextRead)
            {
                nextRead = Time.unscaledTime + .1f;
#if UNITY_EDITOR
                LoadConfig();
                ApplyProfile(); // live Inspector tuning; players apply the config once at startup
#endif
                ReadState();
                Arrange();
                ConsumeEnterIntent();
            }
            SetPaused(IsDragging || InsideWorld || Creating || entryArmed || Returning || FocusedGallery || middleWas);
            if (!IsDragging)
                for (int i = 0; i < layers.Count; i++)
                    if (layers[i].gameObject.activeSelf)
                    {
                        float goal = -ReadFloat(LayerId(i + 1), "GalleryLayerState", "turn") * Mathf.Rad2Deg;
                        float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(.02f, profile.RingRotationSmoothTime));
                        if (!presentationFrozen)
                            layers[i].transform.localRotation = Quaternion.Euler(0, Mathf.LerpAngle(layers[i].transform.localEulerAngles.y, goal, blend), 0);
                    }
            if (!ready && Time.frameCount >= revealFrame)
            {
                Arrange(true);
                ready = true;
            }
            foreach (var item in items)
            {
                bool show = ready && item.Entry.Anchor.gameObject.activeInHierarchy;
                foreach (var r in item.Renderers)
                    r.forceRenderingOff = !show;
                item.Shell.forceRenderingOff = !show;
                if (show && item.Entry.Active && (float)activity["rate"] > 0)
                    item.Entry.Preview.Animate(motionTime, Mathf.Sin(motionTime * 1.8f));
            }
            if (!InsideWorld && !Creating && !N("portalReturn"))
                UpdateVisibility();
            if (ready && ReadDevices && Mouse.current != null)
            {
                var m = Mouse.current;
                bool escape = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
                Vector2 pointer = m.position.ReadValue();
#if UNITY_EDITOR
                if (!m.leftButton.isPressed && !m.rightButton.isPressed && !m.middleButton.isPressed && (UnityEditor.EditorWindow.mouseOverWindow == null || UnityEditor.EditorWindow.mouseOverWindow.GetType().Name != "GameView"))
                    pointer = new Vector2(-100, -100);
#endif
                StepPointer(pointer, m.leftButton.isPressed, m.rightButton.isPressed, m.middleButton.isPressed, m.scroll.ReadValue().y, escape);
            }
            ApplyCamera();
            FinishTreeReturn();
            CheckEntry();
            UpdateWorld();
            using (AppearanceMarker.Auto())
                UpdateAppearance();
            if (navigationDirty && !IsDragging && Time.unscaledTime >= saveAfter)
            {
                SaveArrangement();
                navigationDirty = false;
            }
        }
        void LateUpdate()
        {
            if (stage != null)
                stage.SetSurfaces(entryBlend, stage.Shown != null, InsideWorld);
        }
        float HomeDistance
        {
            get
            {
                // A single enclosing sphere keeps the Cinemachine Follow radius constant through every elevation.
                float center = GalleryCenterY, radius = Mathf.Abs(2.1f - center) + F("diameter") * .5f;
                foreach (var layer in layers)
                    if (layer.gameObject.activeSelf)
                    {
                        float dy = layer.transform.position.y - center;
                        radius = Mathf.Max(radius, Mathf.Sqrt(layer.Radius * layer.Radius + dy * dy) + Mathf.Max(F("diameter"), F("width")) * .5f);
                    }
                float vertical = 16 * Mathf.Deg2Rad, horizontal = Mathf.Atan(Mathf.Tan(vertical) * Mathf.Max(.1f, output.aspect));
                return radius / Mathf.Sin(Mathf.Min(vertical, horizontal));
            }
        }
        void ApplyCamera(bool immediate = false)
        {
            if (IsDragging)
                return;
            float blend = immediate ? 1 : 1 - Mathf.Exp(-F("response") * Time.unscaledDeltaTime);
            var target = new Vector3((float)view["x"], (float)view["y"], (float)view["z"]);
            if (!immediate && CinematicReturn)
                ApplyCinematicReturn();
            else
            {
                currentZoom = immediate ? (float)view["zoom"] : Mathf.SmoothDamp(currentZoom, (float)view["zoom"], ref zoomVelocity, profile.ZoomSmoothTime, 100, Time.unscaledDeltaTime);
                var anchor = Read("GalleryTreeSettings", "GalleryCameraAnchor");
                if ((bool)anchor["active"] && !entryArmed)
                {
                    WriteFloat("GalleryTreeSettings", "GalleryCameraMeasure", currentZoom);
                    InvalidateEntity("GalleryTreeSettings"); // reactive rules may have changed other settings this frame
                    float t = ReadFloat("GalleryTreeSettings", "GalleryRecenterState", "weight");
                    float ease = t * t * t * (t * (t * 6 - 15) + 10);
                    currentCenter = Vector3.Lerp(new Vector3((float)anchor["x"], (float)anchor["y"], (float)anchor["z"]), new Vector3(0, (float)anchor["centerY"], 0), ease);
                    centerVelocity = Vector3.zero;
                }
                else
                    currentCenter = immediate ? target : Vector3.SmoothDamp(currentCenter, target, ref centerVelocity, profile.FocusSmoothTime, profile.FocusMaxSpeed, Time.unscaledDeltaTime);
                currentYaw = Mathf.LerpAngle(currentYaw, (float)view["yaw"], blend);
                currentPitch = Mathf.Lerp(currentPitch, (float)view["pitch"], blend);
            }
            cameraTarget.position = currentCenter;
            cameraRig.rotation = OrbitRotation(currentYaw, currentPitch);
            follow.FollowOffset = cameraRig.rotation * new Vector3(0, 0, -HomeDistance / Mathf.Max(.08f, currentZoom));
            var lens = virtualCamera.Lens;
            lens.FieldOfView = 32;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            virtualCamera.Lens = lens;
        }
        void UpdateAppearance()
        {
            UpdateDestination();
            exitFeedback.Update(Time.unscaledDeltaTime, N("exitArmed") && !N("portalReturn") ? (float?)navigation["exitPull"] ?? 0 : 0, profile, GlassProgress);
            stage.SetGlass(GlassProgress, profile.GlassPassStrength, (bool)Read("GalleryTreeSettings", "GalleryGlassIntent")["entering"], Theme.Glass);
            bool hideTree = InsideWorld || Creating || N("portalReturn");
            layoutRoot.gameObject.SetActive(!hideTree || entryBlend < .98f && !Creating);
            invitation.gameObject.SetActive(!hideTree);
            Vector3 facing = -output.transform.forward;
            facing.y = 0;
            float horizon = Mathf.Clamp01(facing.magnitude * 3);
            facing.Normalize();
            foreach (var item in items)
            {
                if (!item.Entry.Anchor.gameObject.activeInHierarchy)
                    continue;
                bool pair = IsDragging && swapTarget != null && (item == dragged || item == swapTarget);
                bool selected = !IsDragging && item.Entry.Id == (string)focus["preview"];
                float alpha = pair || selected ? 1 : IsDragging ? profile.UnselectedWorldOpacityDuringSwap : 1;
                if (item.Entry.Active && !pair && !selected && item != dragged)
                    alpha *= item.Visibility;
                alpha *= RevealOpacity(item.Entry.Id);
                item.Alpha = item.OpacitySpring.Step(alpha, profile.SwapFadeTime, Time.unscaledDeltaTime);
                alpha = item.Alpha;
                item.Highlight = item.HighlightSpring.Step(pair ? 1 : selected ? ApproachHighlight(item) : 0, profile.SwapFadeTime, Time.unscaledDeltaTime);
                for (int r = 0; r < item.Renderers.Length; r++)
                    for (int m = 0; m < item.Colors[r].Length; m++)
                    {
                        item.Properties.Clear();
                        var color = item.Colors[r][m];
                        color.a *= alpha;
                        item.Properties.SetColor("_BaseColor", color);
                        item.Renderers[r].SetPropertyBlock(item.Properties, m);
                    }
                item.Properties.Clear();
                item.Properties.SetFloat("_Opacity", F("sphereOpacity"));
                item.Properties.SetFloat("_Rim", F("rim"));
                item.Properties.SetFloat("_Frost", F("frost"));
                item.Properties.SetFloat("_WorldOpacity", alpha);
                item.Properties.SetFloat("_Loaded", IsDragging ? 0 : LoadedHighlight(item.Entry.Id) * ApproachHighlight(item));
                item.Properties.SetColor("_LoadedTint", Theme.LoadedWorld.linear);
                item.Properties.SetColor("_Tint", Color.Lerp(Theme.Glass, Theme.Selection, item.Highlight).linear);
                item.Shell.SetPropertyBlock(item.Properties);
            }
            for (int l = 0; l < art.Bands.Count; l++)
            {
                var band = art.Bands[l];
                if (!band.Root.gameObject.activeSelf)
                    continue;
                int n = 0;
                foreach (var item in items)
                    if (item.Level == l + 1 && item.Entry.Active && n < 32)
                        band.Centers[n++] = item.Entry.Anchor.position;
                var p = band.Properties;
                p.Clear();
                p.SetInt("_SphereCount", n);
                p.SetVectorArray("_SphereCenters", band.Centers);
                p.SetVector("_Origin", band.Root.position);
                p.SetVector("_Facing", facing);
                p.SetFloat("_Radius", (l + 1) * (F("diameter") + F("worldGap")));
                p.SetFloat("_Clearance", F("diameter") * .5f);
                p.SetFloat("_Feather", F("feather"));
                p.SetFloat("_Opacity", F("opacity"));
                p.SetFloat("_RearFade", F("rearFade") * horizon);
                var glow = Read(LayerId(l + 1), "GalleryLayerIntent");
                p.SetFloat("_Phase", motionTime);
                p.SetFloat("_PhaseOffset", (float)glow["glowPhase"]);
                p.SetFloat("_TravelDirection", (float)glow["glowRate"]);
                p.SetFloat("_Mode", (GalleryLayoutProfile.GuideStyle)(int)config["style"] == GalleryLayoutProfile.GuideStyle.Ribbon ? 1 : (GalleryLayoutProfile.GuideStyle)(int)config["style"] == GalleryLayoutProfile.GuideStyle.Dashes ? 2 : (GalleryLayoutProfile.GuideStyle)(int)config["style"] == GalleryLayoutProfile.GuideStyle.Particles ? 3 : (GalleryLayoutProfile.GuideStyle)(int)config["style"] == GalleryLayoutProfile.GuideStyle.GalaxyDust ? 4 : 0);
                p.SetFloat("_TubeRadius", F("width") * .5f);
                p.SetFloat("_DustDensity", profile.GalaxyDensity);
                p.SetFloat("_DustScale", profile.GalaxyDetail);
                ApplyBandReveal(p);
                p.SetFloat("_SurfaceSoftness", profile.RingSurfaceSoftness);
                p.SetFloat("_TravelSpeed", profile.RingTravelSpeed);
                p.SetFloat("_GlowSharpness", profile.RingGlowSharpness);
                p.SetFloat("_GlowIntensity", profile.RingGlowIntensity);
                p.SetColor("_Accent", WaveAccent(l + 1, motionTime * profile.RingTravelSpeed * (float)glow["glowRate"] + (float)glow["glowPhase"]).linear);
                p.SetColor("_Tint", Theme.Ring.linear);
                band.Renderer.SetPropertyBlock(p);
                band.Renderer.enabled = profile.LayerGuide != GalleryLayoutProfile.GuideStyle.Hidden;
            }
            invitationAlpha = invitationSpring.Step((IsDragging ? profile.UnselectedWorldOpacityDuringSwap : 1) * RevealAt(invitation.position), profile.SwapFadeTime, Time.unscaledDeltaTime);
            var invite = invitationProperties ??= new MaterialPropertyBlock();
            invite.Clear();
            invite.SetFloat("_Opacity", F("sphereOpacity"));
            invite.SetFloat("_Rim", F("rim"));
            invite.SetFloat("_Frost", F("frost"));
            invite.SetFloat("_WorldOpacity", invitationAlpha);
            invite.SetColor("_Tint", Theme.Glass.linear);
            invitationShell.SetPropertyBlock(invite);
            invite.Clear();
            invite.SetFloat("_Opacity", invitationAlpha);
            invite.SetColor("_Tint", Theme.SeedTint.linear);
            foreach (var r in seedRenderers)
                r.SetPropertyBlock(invite);
            invitationCore.localRotation = Quaternion.Euler(0, Mathf.Sin(motionTime * profile.InvitationPulseFrequency * Mathf.PI * 2) * profile.InvitationPulseAmplitude * 35, 0);

        }
        MaterialPropertyBlock invitationProperties;
        void QueueSave()
        {
            navigationDirty = true;
            saveAfter = Time.unscaledTime + .4f;
        }
        void OnApplicationQuit()
        {
            if (initialized && world && world.Session != null && !world.Session.IsDisposed)
                SaveArrangement();
        }
        void OnDestroy()
        {
            if (disposed)
                return;
            disposed = true;
            if (profile)
                Destroy(profile);
            ClearGhost();
            exitFeedback?.Dispose();
            stage?.Dispose();
            experiences?.Dispose();
            art?.Dispose();
            foreach (var item in items)
                item.Entry.Preview?.Dispose();
            foreach (var m in materials)
                if (m)
                    Destroy(m);
            if (uiDocument)
                Destroy(uiDocument.gameObject);
            if (uiPanel)
                Destroy(uiPanel);
        }
    }
}
#endif

