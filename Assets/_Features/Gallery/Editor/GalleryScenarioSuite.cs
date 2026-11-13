#if CAP_FLEXALON
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Editor
{
    public static class GalleryScenarioSuite
    {
        public static async Task<object> Run(string artifactsDirectory = null)
        {
            var g = UnityEngine.Object.FindAnyObjectByType<CrystalWorldGallery>();
            var world = UnityEngine.Object.FindAnyObjectByType<FlecsWorld>();
            if (!g)
                throw new Exception("Gallery not running");
            bool readDevices = g.ReadDevices;
            g.ReadDevices = false;
            string baseline = g.CaptureArrangement();
            bool saved = File.Exists(CrystalWorldGallery.SavePath);
            byte[] original = saved ? File.ReadAllBytes(CrystalWorldGallery.SavePath) : null;
            var artifacts = new GalleryRunArtifacts(artifactsDirectory);
            var report = new JObject();
            var checks = new JArray();
            int start = Time.frameCount;
            float diameter = g.Profile.WorldDiameter;
            JObject Read(string id, string type) => (JObject)JObject.Parse(world.Session.EntityJson(id))["components"][type];
            void Check(bool pass, string name)
            {
                checks.Add(new JObject { ["name"] = name, ["passed"] = pass });
                if (!pass)
                    throw new Exception(name);
            }
            Vector2 Point(CrystalWorldGallery.Item item)
            {
                var p = g.ViewCamera.WorldToScreenPoint(item.Entry.Anchor.position);
                return new Vector2(p.x, p.y);
            }
            try
            {
                // Use disposable fixtures even when invoked inside a user's edited/resumed world.
                var fixture = JObject.Parse(baseline);
                fixture["worlds"] = new JArray();
                fixture["focus"] = new JObject { ["preview"] = "", ["current"] = "", ["target"] = 0, ["visit"] = 0, ["inside"] = false, ["following"] = false };
                fixture["activity"] = new JObject { ["rate"] = 1, ["draft"] = false, ["sequence"] = 0, ["elapsed"] = 0, ["started"] = 0 };
                fixture["view"] = new JObject { ["x"] = 0, ["y"] = -3.7, ["z"] = 0, ["yaw"] = 35, ["pitch"] = 44, ["zoom"] = 1, ["columns"] = 6 };
                fixture["returnView"] = fixture["view"].DeepClone();
                fixture["cameraAnchor"] = new JObject { ["active"] = false, ["x"] = 0, ["y"] = -3.7, ["z"] = 0, ["zoom"] = 1, ["minimum"] = g.Profile.FullTreeZoom, ["centerY"] = -2.25 };
                fixture["navigation"] = new JObject { ["approach"] = false, ["returning"] = false, ["exitArmed"] = false, ["exitIdle"] = false, ["exitAt"] = 0, ["boundary"] = 30, ["target"] = "", ["exitPull"] = 0, ["entryDistance"] = 0, ["portalReturn"] = false, ["portalAt"] = 0 };
                var fixtureEntries = new JArray();
                var ids = new[] { "GalleryAquarium", "GallerySpace", "GalleryGarden", "GalleryRain", "GalleryMarch", "GalleryMusic", "GalleryOcean", "GalleryOrbit" };
                for (int index = 0; index < 30; index++)
                {
                    string id = index < 8 ? ids[index] : "GalleryTreeSample" + index;
                    fixtureEntries.Add(new JObject { ["id"] = id, ["state"] = new JObject { ["order"] = index, ["width"] = 1, ["depth"] = 1, ["kind"] = index % 8, ["active"] = true }, ["track"] = new JObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }, ["camera"] = new JObject { ["x"] = 0, ["y"] = .5, ["z"] = 0, ["yaw"] = 0, ["pitch"] = 23, ["zoom"] = 4.7, ["columns"] = 6 }, ["sample"] = new JObject { ["time"] = 0 }, ["residence"] = new JObject { ["elapsed"] = 0, ["started"] = 0, ["target"] = 0 } });
                }
                fixture["entries"] = fixtureEntries;
                g.RestoreArrangement(fixture.ToString());
                await Task.Delay(350);
                Check(Time.frameCount > start, "Live frames advance");
                artifacts.Capture(g, "01-gallery");
                float before = (float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"];
                await Task.Delay(300);
                Check((float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"] > before, "Native Clock drives layer rotation");
                Check(g.Items.Count >= 30 && g.Layers.Count == 3, "Thirty worlds on three Flexalon tiers");
                Check(g.Items.All(i => i.Shell && i.Entry.Anchor.GetComponentsInChildren<Renderer>().Count(r => r.sharedMaterial.shader.name == "Runtime/Gallery Crystal") == 1), "Exactly one shell per world");
                Check(g.Art.Bands.All(b => b.Mesh.bounds.size.y > .02f), "Orbit meshes have real vertical thickness");
                Check(g.Items.Where(i => i.Entry.Active).All(i => JObject.Parse(world.Session.EntityJson(i.Entry.Id))["pairs"]?["GalleryOnLayer"] != null), "Native layer relationships survive rebuilds");
                var candidates = g.Items.Where(i => i.Entry.Active && Point(i).x > 20 && Point(i).x < g.ViewCamera.pixelWidth - 20 && Point(i).y > 20 && Point(i).y < g.ViewCamera.pixelHeight - 20 && g.Pick(Point(i)) == i).OrderByDescending(i => i.Alpha).ToArray();
                Check(candidates.Length >= 2, "Visible worlds are pickable");
                var a = candidates[0];
                var b = candidates.First(i => i != a);
                Vector2 pa = Point(a), pb = Point(b);
                var orders = g.Items.ToDictionary(i => i.Entry.Id, i => i.Entry.Order);
                var locals = g.Items.ToDictionary(i => i.Entry.Id, i => i.Entry.Anchor.localPosition);
                g.StepPointer(pa, true);
                g.StepPointer(pa + Vector2.right * 20, true);
                await Task.Delay(100);
                Check(g.IsDragging, "Drag creates a ghost");
                artifacts.Capture(g, "02-held-ghost");
                var positions = g.Items.ToDictionary(i => i.Entry.Id, i => i.Entry.Anchor.position);
                Vector3 camera = g.ViewCamera.transform.position;
                float angle = (float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"];
                await Task.Delay(300);
                Check(g.Items.All(i => Vector3.Distance(positions[i.Entry.Id], i.Entry.Anchor.position) < .0005f), "All originals stay fixed while held");
                Check(Vector3.Distance(camera, g.ViewCamera.transform.position) < .00001f, "Camera stays fixed while held");
                Check(Mathf.Abs((float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"] - angle) < .000001f, "Native idle turn freezes during drag");
                g.StepPointer(pb, true);
                Check(g.SwapTargetId == b.Entry.Id, "Destination identifies swap pair");
                await Task.Delay(180);
                artifacts.Capture(g, "03-swap-preview");
                g.StepPointer(pb, false);
                await Task.Delay(500);
                Check(a.Entry.Order == orders[b.Entry.Id] && b.Entry.Order == orders[a.Entry.Id], "Only the two orders swap");
                Check(g.Items.Where(i => i != a && i != b).All(i => i.Entry.Order == orders[i.Entry.Id]), "Unrelated orders stay unchanged");
                Check(g.Items.Where(i => i != a && i != b && i.Entry.Active).All(i => Vector3.Distance(locals[i.Entry.Id], i.Entry.Anchor.localPosition) < .01f), "Unrelated places stay unchanged");
                for (int t = 0; t < 40 && (Vector3.Distance(a.Entry.Anchor.localPosition, locals[b.Entry.Id]) > .01f || Vector3.Distance(b.Entry.Anchor.localPosition, locals[a.Entry.Id]) > .01f); t++)
                    await Task.Delay(100);
                Check(Vector3.Distance(a.Entry.Anchor.localPosition, locals[b.Entry.Id]) < .01f && Vector3.Distance(b.Entry.Anchor.localPosition, locals[a.Entry.Id]) < .01f, "Swapped worlds settle in each other's places");
                g.Swap(a.Entry.Id, b.Entry.Id);
                await Task.Delay(300);
                pa = Point(a);
                g.StepPointer(pa, true);
                g.StepPointer(pa + Vector2.right * 20, true);
                g.CancelDrag();
                g.StepPointer(pa, false);
                Check(!g.IsDragging && !(bool)Read("GalleryTreeSettings", "GalleryDrag")["active"], "Cancel removes native drag and ghost");
                camera = g.ViewCamera.transform.position;
                g.SetVisible(a.Entry.Id, false);
                await Task.Delay(150);
                Check(!a.Entry.Anchor.gameObject.activeSelf && !(bool)Read(a.Entry.Id, "GalleryEntry")["active"], "Inactive catalog records stay saved and absent from the gallery");
                Check(Vector3.Distance(camera, g.ViewCamera.transform.position) < .001f, "Changing catalog visibility does not move the camera");
                g.SetVisible(a.Entry.Id, true);
                await Task.Delay(300);
                Check(Type.GetType("Game.WorldGalleryPrototype, Assembly-CSharp") == null && Type.GetType("Game.GalleryWorkbenchPresentation, Assembly-CSharp") == null, "Superseded furniture presenter types are absent from the application");
                var dock = UnityEngine.Object.FindAnyObjectByType<WorkshopDock>();
                dock.ShowSettings();
                g.StepPointer(Vector2.zero, false, cancel: true);
                Check(!dock.SettingsOpen && !g.InsideWorld, "Settings Escape closes only Settings");
                var snapshot = g.CaptureArrangement();
                var entering = g.Items.First(i => i.Entry.Active && (float)Read(i.Entry.Id, "GalleryResidence")["elapsed"] <= 0);
                var landmarks = new[] { entering.Entry.NaturalBounds.center, entering.Entry.NaturalBounds.center + Vector3.right * .2f, entering.Entry.NaturalBounds.center + Vector3.up * .2f };
                Vector3[] expected = landmarks.Select(p => g.ViewCamera.WorldToViewportPoint(entering.Entry.Content.TransformPoint(p))).ToArray();
                Check(g.EnterWorld(entering.Entry.Id), "World entry succeeds");
                float error = 0;
                for (int i = 0; i < landmarks.Length; i++)
                {
                    var actual = g.Stage.Camera.WorldToViewportPoint(g.Stage.Resident.Root.transform.TransformPoint(landmarks[i]));
                    error = Mathf.Max(error, Vector2.Distance(expected[i], actual));
                }
                report["entryViewportError"] = error;
                Check(error < .003f, "Fresh entry camera matches preview orientation, scale and projection");
                await Task.Delay(150);
                artifacts.Capture(g, "04-inside-world");
                g.ReturnToGallery();
                Check(g.Stage.Resident?.Id == entering.Entry.Id, "Returning retains the current hidden world");
                var savedCamera = Read(entering.Entry.Id, "GalleryView");
                g.EnterWorld(entering.Entry.Id);
                Check(JToken.DeepEquals(savedCamera, Read(entering.Entry.Id, "GalleryView")), "Previously entered world restores saved camera");
                Check(g.Stage.ContextCount <= 2, "World resume keeps bounded context count");
                g.ReturnToGallery();
                g.RestoreArrangement(snapshot);
                await Task.Delay(150);
                Check(g.Items.Count == 30, "Arrangement reconstructs all thirty worlds");
                g.BeginCreation();
                Check(g.Creating && g.Items.Count == 30, "Entering invitation does not save a world");
                string created = g.CreateFirstObject();
                Check(g.Items.Count == 31 && world.Session.Lookup(created) != 0 && g.InsideWorld, "First object promotes the invitation to a native world");
                Check((int)Read(created, "GalleryEntry")["order"] == 0 && (int)Read(created, "GalleryPlacement")["layer"] == 1, "New worlds enter the first ring and shift existing native order");
                var contentPath = created + "Content.Object1";
                Check(g.Experiences.ResidentId == created && world.Session.Lookup(contentPath) != 0, "Creation loads real owned native entities, not only preview geometry");
                float nativeRotation = (float)Read(contentPath, "Rotation")["y"];
                await Task.Delay(200);
                Check((float)Read(contentPath, "Rotation")["y"] > nativeRotation, "Existing native Spin system drives the real world object");
                await Task.Delay(2800);
                artifacts.Capture(g, "08-native-world");
                var nativeTransform = world.FindView(world.Session.Lookup(contentPath)).transform;
                var projected = g.Stage.Camera.WorldToViewportPoint(nativeTransform.position);
                var objectPixel = new Vector2(projected.x * g.ViewCamera.pixelWidth, projected.y * g.ViewCamera.pixelHeight);
                var beforeDrag = Read(contentPath, "Position");
                g.StepPointer(objectPixel, true);
                g.StepPointer(objectPixel + Vector2.right * 45, true);
                g.StepPointer(objectPixel + Vector2.right * 45, false);
                Check(!JToken.DeepEquals(beforeDrag, Read(contentPath, "Position")), "Production left drag updates native real-world placement");
                var emptyProjection = g.Stage.Camera.WorldToViewportPoint(g.Experiences.ResidentRoot.TransformPoint(new Vector3(-1.6f, 0, 1)));
                var emptyPixel = new Vector2(emptyProjection.x * g.ViewCamera.pixelWidth, emptyProjection.y * g.ViewCamera.pixelHeight);
                g.StepPointer(emptyPixel, true);
                g.StepPointer(emptyPixel, false);
                Check(world.Session.Lookup(created + "Content.Object2") != 0, "Clicking empty world space creates an owned native shape");
                g.Experiences.Move("Object1", new Vector3(.7f, .2f, -.3f));
                g.Experiences.Select("Object1");
                var realSnapshot = g.CaptureArrangement();
                var nativeObject = Read(contentPath, "Position");
                var savedRealView = JObject.Parse(realSnapshot)["entries"].First(e => (string)e["id"] == created)["camera"];
                g.RestoreArrangement(realSnapshot);
                Check(JToken.DeepEquals(nativeObject, Read(contentPath, "Position")), "Saved world reconstructs real native object placement");
                Check((string)Read(created, "GallerySavedWorld")["selected"] == "Object1", "Object editing selection survives reconstruction");
                var realViewAfter = Read(created, "GalleryView");
                float realCameraError = new[] { "x", "y", "z", "yaw", "pitch", "zoom" }.Max(key => Mathf.Abs((float)savedRealView[key] - (float)realViewAfter[key]));
                report["realWorldCameraError"] = realCameraError;
                Check(realCameraError < .001f, "Real-world camera context survives reconstruction");
                g.ReturnToGallery();
                for (int wait = 0; wait < 80 && (g.Returning || g.Stage.Shown != null); wait++)
                    await Task.Delay(100);
                Check(g.Experiences.ResidentId == created && !g.Experiences.Visible && world.Session.Lookup(contentPath) != 0, "Returning hides and retains the resident native world");
                artifacts.Capture(g, "09-saved-world-gallery");
                float pausedRotation = (float)Read(contentPath, "Rotation")["y"];
                await Task.Delay(150);
                Check(Mathf.Abs((float)Read(contentPath, "Rotation")["y"] - pausedRotation) < .001f, "Hidden resident simulation is paused in native component data");
                g.BeginCreation();
                string secondReal = g.CreateFirstObject();
                Check(world.Session.Lookup(contentPath) == 0 && g.Experiences.ResidentId == secondReal, "Entering another world unloads the previous private scope");
                Check(world.Session.Lookup("GallerySharedShape") != 0, "Shared prefab remains available across private-world unloading");
                var reuse = g.ReuseWorldObject(created, "Object1", secondReal);
                Check(g.Experiences.Features(secondReal).Any(o => (string)o["name"] == reuse), "Another world reuses the saved object definition");
                Check(g.EnterWorld(created), "Saved first world can reopen after unloading");
                Check(JToken.DeepEquals(nativeObject, Read(contentPath, "Position")), "Unloaded world reconstructs its latest authored placement");
                // Capability graph persistence: world-owned bindings with a dependency survive unload, re-entry and a disk reload.
                {
                    string glow = created + "Content.Glow";
                    string glow2 = created + "Content.Glow2";
                    string owned = "GalleryWorld_" + created + ".Lights";
                    var warnings = new List<string>();
                    Application.LogCallback capture = (message, stack, type) =>
                    {
                        if (type != LogType.Log && message.Contains(created))
                            warnings.Add(message);
                    };
                    Application.logMessageReceived += capture;
                    try
                    {
                        async Task<bool> Applied()
                        {
                            for (int wait = 0; wait < 40; wait++)
                            {
                                var a = world.Session.Lookup(glow) != 0 ? Read(glow, "CapState") : null;
                                var b = world.Session.Lookup(glow2) != 0 ? Read(glow2, "CapState") : null;
                                if ((int?)a?["phase"] == 1 && (int?)b?["phase"] == 1)
                                    return true;
                                await Task.Delay(100);
                            }
                            return false;
                        }
                        string DependsOn() => (string)JObject.Parse(world.Session.EntityJson(glow2))["pairs"]?["CapDependsOn"];
                        int Lights() => world.FindView(world.Session.Lookup(contentPath))?.GetComponentsInChildren<Light>(true).Length ?? 0;
                        async Task<bool> Resident(string id)
                        {
                            if (g.Experiences.ResidentId != id || world.Session.Lookup(id + "Content") == 0)
                                g.EnterWorld(id);
                            await Task.Delay(150);
                            return g.Experiences.ResidentId == id && world.Session.Lookup(contentPath) != 0;
                        }
                        world.Session.SetManagedScript(owned, created + "Content {\n" +
                            "    Glow { CapBinding: {target: " + contentPath + ", enabled: true, revision: 1}; CapLight: {kind: 0, intensity: 2, range: 5, colorR: 1, colorG: 0.6, colorB: 0.2} }\n" +
                            "    Glow2 { CapBinding: {target: " + contentPath + ", enabled: true, revision: 1}; CapLight: {kind: 0, intensity: 1, range: 3, colorR: 1, colorG: 1, colorB: 1}; (CapDependsOn, " + glow + ") }\n}\n");
                        Check(await Applied() && Lights() >= 2, "World-owned capability bindings apply real Unity lights");
                        var light = Read(glow, "CapLight");
                        var light2 = Read(glow2, "CapLight");
                        Check(DependsOn() == glow, "World-owned binding declares its dependency");
                        // Leaving for another world captures the graph and unloads it with the private scope.
                        Check(g.EnterWorld(secondReal), "Another world opens while capability bindings are resident");
                        var graphText = (string)Read(created, "GallerySavedWorld")["graph"];
                        var graph = string.IsNullOrEmpty(graphText) ? null : JObject.Parse(graphText);
                        Check(graph != null && graph["entities"].Count() == 2 && graph["entities"].Any(e => (string)e["path"] == glow2 && e["dependsOn"].Any(t => (string)t == glow)) && graph["sources"].Any(s => (string)s["path"] == owned),
                            "Saved world records its bindings, dependency relation and owned source");
                        Check(world.Session.Lookup(glow) == 0 && world.Session.ManagedScriptSource(owned) == null, "Unloading removes world-owned bindings and sources");
                        // Re-entering reconstructs the same configuration, relation and applied state.
                        Check(await Resident(created) && await Applied(), "Re-entered world reapplies its capability bindings");
                        Check(JToken.DeepEquals(light, Read(glow, "CapLight")) && JToken.DeepEquals(light2, Read(glow2, "CapLight")) && DependsOn() == glow && Lights() >= 2,
                            "Re-entered bindings keep configuration, dependency and real output");
                        // Disk round trip: the saved arrangement file reconstructs the graph after the world is unloaded.
                        g.SaveArrangement();
                        string onDisk = File.ReadAllText(CrystalWorldGallery.SavePath);
                        g.EnterWorld(secondReal);
                        g.RestoreArrangement(onDisk);
                        await Task.Delay(150);
                        Check(await Resident(created) && await Applied() && JToken.DeepEquals(light, Read(glow, "CapLight")) && DependsOn() == glow,
                            "Saved arrangement file reconstructs capability bindings and dependency");
                        // A record written before graphs existed (no graph member) still loads and enters.
                        var legacy = JObject.Parse(g.CaptureArrangement());
                        foreach (var row in legacy["worlds"])
                            ((JObject)row["record"]).Remove("graph");
                        g.EnterWorld(secondReal);
                        g.RestoreArrangement(legacy.ToString());
                        await Task.Delay(150);
                        Check((string)Read(created, "GallerySavedWorld")["graph"] == "" && await Resident(created) && world.Session.Lookup(glow) == 0,
                            "Legacy saved world without a capability graph loads and enters");
                        Check(warnings.Count == 0, "Capability graph restore reports no problems: " + string.Join(" | ", warnings));
                    }
                    finally
                    {
                        Application.logMessageReceived -= capture;
                    }
                }
                g.ReturnToGallery();
                // Golden save: seeded once from this fixture state (no user data), then committed. Later builds must keep loading it.
                string goldenPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tests/Fixtures/Saves/gallery-arrangement-v1.json"));
                if (!File.Exists(goldenPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(goldenPath));
                    File.WriteAllText(goldenPath, g.CaptureArrangement());
                    report["goldenSeeded"] = goldenPath;
                }
                string goldenText = File.ReadAllText(goldenPath);
                var golden = JObject.Parse(goldenText);
                g.RestoreArrangement(goldenText);
                await Task.Delay(150);
                Check(g.Items.Count == ((JArray)golden["entries"]).Count &&
                    ((JArray)golden["worlds"]).Where(w => !string.IsNullOrEmpty((string)w["record"]?["source"])).All(w => g.Experiences.Has((string)w["id"])),
                    "Committed version 1 arrangement (Tests/Fixtures/Saves/gallery-arrangement-v1.json) restores every entry and saved world");
                g.RestoreArrangement(snapshot);
                await Task.Delay(150);
                Check(g.Items.Count == 30 && world.Session.Lookup(created) == 0, "Snapshot reconstruction releases test-owned creation");
                Check(GalleryConfigScript.IsCurrent(), "Shipped GalleryConfig.flecs matches Gallery.asset");
                var designer = GalleryConfigScript.EditorAsset;
                float oldGap = designer.LayerSpacing;
                try
                {
                    designer.LayerSpacing = oldGap + .15f;
                    await Task.Delay(300);
                    Check(Mathf.Abs(g.Profile.LayerSpacing - (oldGap + .15f)) < .001f && Mathf.Abs((float)Read(GalleryConfigScript.Entity, GalleryConfigScript.Component)["LayerSpacing"] - (oldGap + .15f)) < .001f, "Designer asset edits reach the runtime profile through Flecs Script");
                    Check(Mathf.Abs((float)Read("GalleryTreeSettings", "GalleryTreeConfig")["layerGap"] - (oldGap + .15f)) < .001f, "Designer profile updates effective native configuration");
                }
                finally { designer.LayerSpacing = oldGap; await Task.Delay(250); }
                // Actual production pointer/wheel route; inspect authoritative native state and rendered transforms.
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                var eligible = g.Items.Where(i => i.Entry.Active && Point(i).x > 25 && Point(i).x < g.ViewCamera.pixelWidth - 25 && Point(i).y > 25 && Point(i).y < g.ViewCamera.pixelHeight - 25 && g.Pick(Point(i)) == i).ToArray();
                var ringItem = eligible.First(i => i.Level > 1);
                var ringPoint = Point(ringItem);
                string ringId = "GalleryTreeLayer" + ringItem.Level;
                float firstTurn = (float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"], hoverTurn = (float)Read(ringId, "GalleryLayerState")["turn"];
                var ringOrigin = g.Layers[ringItem.Level - 1].transform.position;
                var ringOffset = ringItem.Entry.Anchor.position - ringOrigin;
                var ringEnd = (Vector2)g.ViewCamera.WorldToScreenPoint(ringOrigin + Quaternion.AngleAxis(-18, Vector3.up) * ringOffset);
                g.StepPointer(ringPoint, false, middle: true);
                g.StepPointer(ringEnd, false, middle: true);
                g.StepPointer(ringEnd, false);
                Check(Mathf.Abs((float)Read(ringId, "GalleryLayerIntent")["angle"] - hoverTurn - 18 * Mathf.Deg2Rad) < .04f, "Middle drag follows the pointer's world-plane angle");
                Check(Mathf.Abs((float)Read("GalleryTreeLayer1", "GalleryLayerIntent")["angle"] - firstTurn) < .04f, "Another tier is not manually rotated");
                await Task.Delay(30);
                float earlyRotationError = Mathf.Abs(Mathf.DeltaAngle(g.Layers[ringItem.Level - 1].transform.localEulerAngles.y, -(float)Read(ringId, "GalleryLayerState")["turn"] * Mathf.Rad2Deg));
                Check(earlyRotationError > 1, "Rendered ring eases toward the native drag target rather than snapping");
                await Task.Delay(350);
                float laterRotationError = Mathf.Abs(Mathf.DeltaAngle(g.Layers[ringItem.Level - 1].transform.localEulerAngles.y, -(float)Read(ringId, "GalleryLayerState")["turn"] * Mathf.Rad2Deg));
                Check(laterRotationError < earlyRotationError, "Ring rotation smoothly converges after release");
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                eligible = g.Items.Where(i => i.Entry.Active && g.Pick(Point(i)) == i && Point(i).x > 20 && Point(i).x < g.ViewCamera.pixelWidth - 20 && Point(i).y > 20 && Point(i).y < g.ViewCamera.pixelHeight - 20).ToArray();
                var targetA = eligible.First();
                var targetB = eligible.OrderByDescending(i => Vector3.Distance(i.Entry.Anchor.position, targetA.Entry.Anchor.position)).First();
                float wheel = UnityEngine.InputSystem.InputSystem.settings.scrollDeltaBehavior == UnityEngine.InputSystem.InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange ? 120 : 1;
                Vector3 pivot = g.OrbitCenter;
                g.StepPointer(Point(targetA), false, scroll: wheel);
                Check(Vector3.Distance(pivot, g.OrbitCenter) < .001f, "Zoom intent never teleports the rendered pivot");
                await Task.Delay(180);
                pivot = g.OrbitCenter;
                g.StepPointer(Point(targetB), false, scroll: wheel);
                Check(Vector3.Distance(pivot, g.OrbitCenter) < .001f, "Changing zoom target preserves camera continuity");
                Check((string)Read("GalleryTreeSettings", "GalleryTreeNavigation")["target"] == targetB.Entry.Id, "Flecs owns the selected zoom destination");
                await Task.Delay(120);
                float phase = (float)Read("GalleryTreeSettings", "GalleryTreeMotion")["time"], turn = (float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"];
                var frozenPositions = g.Items.ToDictionary(i => i.Entry.Id, i => i.Entry.Anchor.position);
                await Task.Delay(250);
                Check(g.Items.All(i => Vector3.Distance(frozenPositions[i.Entry.Id], i.Entry.Anchor.position) < .0001f) && Mathf.Abs((float)Read("GalleryTreeSettings", "GalleryTreeMotion")["time"] - phase) < .001f && Mathf.Abs((float)Read("GalleryTreeLayer1", "GalleryLayerState")["turn"] - turn) < .001f, "Native animation phase, tier rotation and preview transforms freeze during approach");
                Vector3 closeCenter = g.OrbitCenter;
                float closeZoom = (float)Read("GalleryTreeSettings", "GalleryView")["zoom"];
                g.StepPointer(Point(targetB), false, scroll: -wheel);
                Check(!g.Returning, "One outward wheel changes zoom without starting an automatic pullback");
                Check((float)Read("GalleryTreeSettings", "GalleryView")["zoom"] > g.Profile.FullTreeZoom, "A small wheel gesture does not jump to full-tree framing");
                await Task.Delay(160);
                Check(Vector3.Distance(g.OrbitCenter, closeCenter) < 1, "Initial zoom-out does not immediately recenter on the tree");
                {
                    var pair = g.Items.Where(i => i.Entry.Active && i != targetA && i != targetB).Take(2).ToArray();
                    Check(pair.Length == 2 && !string.IsNullOrEmpty((string)Read("GalleryTreeSettings", "GalleryTreeNavigation")["target"]), "Focused gallery has a swap pair");
                    if (pair.Length == 2)
                    {
                        var sa = pair[0];
                        var sb = pair[1];
                        Vector3 la = sa.Entry.Anchor.localPosition, lb = sb.Entry.Anchor.localPosition;
                        g.Swap(sa.Entry.Id, sb.Entry.Id);
                        for (int t = 0; t < 40 && (Vector3.Distance(sa.Entry.Anchor.localPosition, lb) > .01f || Vector3.Distance(sb.Entry.Anchor.localPosition, la) > .01f); t++)
                            await Task.Delay(100);
                        Check(Vector3.Distance(sa.Entry.Anchor.localPosition, lb) < .01f && Vector3.Distance(sb.Entry.Anchor.localPosition, la) < .01f, "Swap completes while zoomed onto a world instead of hanging mid-way");
                        g.Swap(sa.Entry.Id, sb.Entry.Id);
                        for (int t = 0; t < 40 && Vector3.Distance(sa.Entry.Anchor.localPosition, la) > .01f; t++)
                            await Task.Delay(100);
                    }
                }
                g.StepPointer(Vector2.zero, false, scroll: -wheel * 100);
                for (int i = 0; i < 12; i++)
                    g.StepPointer(Vector2.zero, false, scroll: -wheel * 4);
                await Task.Delay(1800);
                Check(Mathf.Abs(g.OrbitCenter.x) < .03f && Mathf.Abs(g.OrbitCenter.z) < .03f, "Camera reaches the tree center at maximum explicit zoom-out");
                bool FullyVisible(CrystalWorldGallery.Item item)
                {
                    float radius = diameter * .5f;
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        var point = g.ViewCamera.WorldToViewportPoint(item.Entry.Anchor.position + new Vector3(x, y, z) * radius);
                        if (point.z <= 0 || point.x < 0 || point.x > 1 || point.y < 0 || point.y > 1)
                            return false;
                    }
                    return true;
                }
                Check(g.Items.Where(i => i.Entry.Active).All(FullyVisible), "Maximum manual zoom-out frames the complete tree");
                g.EnterWorld(targetA.Entry.Id);
                await Task.Delay(100);
                for (int i = 0; i < 50 && !(bool)Read("GalleryTreeSettings", "GalleryTreeNavigation")["exitArmed"]; i++)
                    g.StepPointer(Vector2.zero, false, scroll: -wheel * 4);
                Check(g.InsideWorld && (bool)Read("GalleryTreeSettings", "GalleryTreeNavigation")["exitArmed"], "Outward wheel reaches the world boundary without exiting");
                await Task.Delay(220);
                Check(g.ExitFeedbackAmount > .01f, "Exit boundary drives visible elastic feedback");
                artifacts.Capture(g, "05-exit-feedback");
                g.StepPointer(Vector2.zero, false);
                g.StepPointer(Vector2.zero, false, scroll: -wheel);
                Check(!g.InsideWorld && g.Returning, "One extra outward push exits without a long quiet-scroll lock");
                for (int i = 0; i < 25 && !(bool)Read("GalleryTreeSettings", "GalleryReturnIntent")["active"]; i++)
                    await Task.Delay(50);
                Check((string)Read("GalleryTreeSettings", "GalleryReturnIntent")["world"] == targetA.Entry.Id, "Flecs records the returned world's reveal and recall identity");
                for (int i = 0; i < 40 && g.ReturnReveal < .4f; i++)
                    await Task.Delay(40);
                var returned = g.Items.First(i => i.Entry.Id == targetA.Entry.Id);
                var others = g.Items.Where(i => i.Entry.Active && i != returned).OrderBy(i => Vector3.Distance(i.Entry.Anchor.position, returned.Entry.Anchor.position)).ToArray();
                var nearReturn = others.First();
                var farReturn = others.Last();
                Check(g.ReturnProgress > 0 && g.ReturnProgress < 1 && g.Returning, "Native return clock drives an intermediate cinematic phase");
                Check(g.RevealOpacity(nearReturn.Entry.Id) > g.RevealOpacity(farReturn.Entry.Id) + .1f, "Nearby worlds reveal before distant worlds in spatial order");
                Check(!farReturn.Pickable && !farReturn.Hit.enabled && !(bool)Read(farReturn.Entry.Id, "GalleryVisibility")["pickable"], "Unrevealed worlds are absent from Unity and native input eligibility");
                Check(returned.Alpha > .9f && g.RevealOpacity(returned.Entry.Id) == 1, "The returned preview remains visible throughout the reveal");
                Check((float)Read("GalleryTreeSettings", "GalleryTreeActivity")["rate"] == 0, "Tree simulation remains paused while the return clock advances");
                artifacts.Capture(g, "10-return-reveal");
                for (int i = 0; i < 70 && g.Returning; i++)
                    await Task.Delay(100);
                Check(!g.Returning, "Exit completes without forcing whole-tree framing");
                Check(Mathf.Abs(Mathf.Asin(Mathf.Clamp(-g.ViewCamera.transform.forward.y, -1, 1)) * Mathf.Rad2Deg - 90) < 1, "World exit reaches a true 90-degree top-down view");
                var recalled = new MaterialPropertyBlock();
                returned.Shell.GetPropertyBlock(recalled);
                Check(g.LoadedHighlight(returned.Entry.Id) > .5f && recalled.GetFloat("_Loaded") > .5f, "The loaded world receives a persistent clearly visible marker");
                Check(g.Items.Where(i => i.Entry.Active).All(i => g.RevealOpacity(i.Entry.Id) > .999f), "Every reveal finishes before normal tree motion resumes");
                Check(Mathf.Abs((float)Read("GalleryTreeSettings", "GalleryView")["zoom"] - (float)Read("GalleryExitGallery", "GalleryView")["zoom"]) < .001f, "Exit holds its matched preview distance instead of auto zooming out");
                Check(g.ExitProjectionError < .003f, "World and preview preserve projected landmark scale across exit");
                report["exitProjectionError"] = g.ExitProjectionError;
                var galleryResume = g.CaptureArrangement();
                g.RestoreArrangement(galleryResume, true);
                await Task.Delay(100);
                Check(!g.InsideWorld && Vector3.Dot(g.ViewCamera.transform.forward, Vector3.down) > .999f && g.LoadedHighlight(targetA.Entry.Id) > .5f, "Gallery launch preserves zoom/context, forces top-down and marks the last world");
                Check(g.EnterWorld(targetA.Entry.Id), "Marked last world can immediately re-enter");
                await Task.Delay(120);
                var insideResume = g.CaptureArrangement();
                g.RestoreArrangement(insideResume, true);
                Check(g.InsideWorld, "A launch saved inside a world resumes inside that world");
                g.RestoreArrangement(galleryResume);
                await Task.Delay(100);
                Check(!UnityEditor.ShaderUtil.ShaderHasError(g.Art.Bands[0].Renderer.sharedMaterial.shader), "Gallery volume shader compiles for the active renderer");
                artifacts.Capture(g, "11-return-recall");
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                var fading = g.Items.Where(i => i.Entry.Active && g.Pick(Point(i)) == i).OrderByDescending(i => i.Alpha).First();
                float alphaBefore = g.InvitationOpacity;
                var fp = Point(fading);
                g.StepPointer(fp, true);
                g.StepPointer(fp + Vector2.right * 20, true);
                Check(Mathf.Abs(g.InvitationOpacity - alphaBefore) < .001f, "Seed fade does not snap at drag start");
                await Task.Delay(160);
                Check(g.InvitationOpacity < alphaBefore && g.InvitationOpacity > g.Profile.UnselectedWorldOpacityDuringSwap, "Protected seed participates in the gradual fade");
                var other = g.Items.First(i => i != fading && i.Entry.Active && g.Pick(Point(i)) == i);
                float alpha = other.Alpha;
                g.StepPointer(Point(other), true);
                Check(Mathf.Abs(other.Alpha - alpha) < .001f, "Potential swap highlight does not snap");
                g.StepPointer(g.ViewCamera.WorldToScreenPoint(g.InvitationPosition), true);
                Check(g.SwapTargetId == null, "Seed invitation cannot be a swap target");
                g.CancelDrag();
                g.StepPointer(Vector2.zero, false);
                var top = g.transform.Find("New world invitation - protected");
                var topShell = top.Find("Single invitation shell").GetComponent<Renderer>();
                var topProperties = new MaterialPropertyBlock();
                topShell.GetPropertyBlock(topProperties);
                Check(Mathf.Abs(topProperties.GetFloat("_Opacity") - g.Profile.SphereOpacity) < .001f && top.Find("New world seed") != null, "Top invitation uses identical glass and a baked seed");
                Check(top.GetComponent<SphereCollider>().isTrigger && System.Linq.Enumerable.All(g.Items, i => i.Hit.isTrigger),
                    "Gallery pick volumes are triggers and never block motors or physics in an open world");
                var theme = g.Theme;
                Color oldRing = theme.Ring;
                try
                {
                    theme.Ring = Color.magenta;
                    await Task.Delay(200);
                    var props = new MaterialPropertyBlock();
                    g.Art.Bands[0].Renderer.GetPropertyBlock(props);
                    Check(Vector4.Distance(props.GetColor("_Tint"), Color.magenta.linear) < .001f, "Shared theme updates the presented ring material");
                }
                finally { theme.Ring = oldRing; }
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                // Constant radius and stable 90-degree orientation, using the same Follow component.
                var cameraSnapshot = JObject.Parse(snapshot);
                var orbitView = (JObject)cameraSnapshot["view"];
                orbitView["x"] = 0;
                orbitView["y"] = g.OrbitCenter.y;
                orbitView["z"] = 0;
                orbitView["zoom"] = g.Profile.FullTreeZoom;
                orbitView["pitch"] = 15;
                g.RestoreArrangement(cameraSnapshot.ToString());
                await Task.Delay(150);
                float radiusLow = Vector3.Distance(g.ViewCamera.transform.position, g.OrbitCenter);
                orbitView["pitch"] = 90;
                g.RestoreArrangement(cameraSnapshot.ToString());
                await Task.Delay(150);
                float radiusTop = Vector3.Distance(g.ViewCamera.transform.position, g.OrbitCenter);
                Check(Mathf.Abs(radiusLow - radiusTop) < .02f, "Cinemachine Follow orbit radius stays constant from low angle to the pole");
                Check(Vector3.Dot(g.ViewCamera.transform.forward, Vector3.down) > .999f, "Top-down orbit reaches a true stable 90 degrees");
                artifacts.Capture(g, "06-top-view");
                var poleWorld = g.Items.First(i => i.Entry.Active && g.Pick(Point(i)) == i);
                Vector3 landmark = poleWorld.Entry.NaturalBounds.center + Vector3.right * .2f;
                Vector3 expectedPole = g.ViewCamera.WorldToViewportPoint(poleWorld.Entry.Content.TransformPoint(landmark));
                g.EnterWorld(poleWorld.Entry.Id);
                // Portal-local pose must preserve yaw even where Euler decomposition is ambiguous.
                var localPole = (JObject)JObject.Parse(g.CaptureArrangement())["portal"]["local"];
                g.Stage.Pose(localPole, 0, 0, true);
                Vector3 actualPole = g.Stage.Camera.WorldToViewportPoint(g.Stage.Resident.Root.transform.TransformPoint(landmark));
                Check(Vector2.Distance(expectedPole, actualPole) < .003f, "Entry from the pole preserves the preview camera orientation");
                g.RestoreArrangement(cameraSnapshot.ToString());
                await Task.Delay(150);
                for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    var layer = g.Layers[1];
                    float quadrantAngle = quadrant * 90 + 25;
                    Vector3 origin = layer.transform.position;
                    Vector3 from = origin + new Vector3(Mathf.Cos(quadrantAngle * Mathf.Deg2Rad), 0, Mathf.Sin(quadrantAngle * Mathf.Deg2Rad)) * layer.Radius;
                    Vector3 to = origin + new Vector3(Mathf.Cos((quadrantAngle + 12) * Mathf.Deg2Rad), 0, Mathf.Sin((quadrantAngle + 12) * Mathf.Deg2Rad)) * layer.Radius;
                    var aPoint = (Vector2)g.ViewCamera.WorldToScreenPoint(from);
                    var bPoint = (Vector2)g.ViewCamera.WorldToScreenPoint(to);
                    if (g.PickLayer(aPoint) != 2)
                        continue;
                    float beforeAngle = -layer.transform.localEulerAngles.y * Mathf.Deg2Rad;
                    g.StepPointer(aPoint, false, middle: true);
                    g.StepPointer(bPoint, false, middle: true);
                    g.StepPointer(bPoint, false);
                    Check(Mathf.Abs((float)Read("GalleryTreeLayer2", "GalleryLayerIntent")["angle"] - beforeAngle - 12 * Mathf.Deg2Rad) < .04f, "Top-down ring drag follows angular direction in quadrant " + quadrant);
                    await Task.Delay(250);
                }
                var accentItem = g.Items.First(i => i.Entry.Active);
                var accentOffset = accentItem.Entry.Anchor.position - g.Layers[accentItem.Level - 1].transform.position;
                Check(Vector4.Distance(g.WaveAccent(accentItem.Level, Mathf.Atan2(accentOffset.z, accentOffset.x)), accentItem.Entry.Preview.Theme) < .001f, "Traveling highlight samples the actual world's accent at its position");
                var topRotation = g.ViewCamera.transform.rotation;
                g.StepPointer(new Vector2(200, 200), false, right: true);
                g.StepPointer(new Vector2(240, 200), false, right: true);
                g.StepPointer(new Vector2(240, 200), false);
                await Task.Delay(180);
                Check(Quaternion.Angle(topRotation, g.ViewCamera.transform.rotation) < 35, "Yaw at the pole remains continuous without a LookRotation flip");
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                var highlighted = g.Items.Where(i => i.Entry.Active && g.Pick(Point(i)) == i).First();
                g.StepPointer(Point(highlighted), false);
                await Task.Delay(500);
                Check(highlighted.Highlight > .7f, "Hover feedback appears while pointing at a world");
                float lit = highlighted.Highlight;
                g.StepPointer(new Vector2(-200, -200), false);
                Check((string)Read("GalleryTreeSettings", "GalleryFocus")["preview"] == "" && Mathf.Abs(highlighted.Highlight - lit) < .001f, "Leaving a sphere clears native hover without snapping the presentation");
                await Task.Delay(850);
                Check(highlighted.Highlight < .1f, "Hover feedback returns to neutral after the pointer leaves");
                // Exact projected overlap: an obscured world fades, disables its collider and cannot steal the ray.
                var hp = Point(highlighted);
                g.StepPointer(hp, true);
                g.StepPointer(hp + Vector2.right * 20, true);
                var hidden = g.Items.First(i => i != highlighted && i.Entry.Active);
                var cam = g.ViewCamera.transform;
                highlighted.Entry.Anchor.position = cam.position + cam.forward * 12;
                hidden.Entry.Anchor.position = cam.position + cam.forward * 18;
                g.RefreshVisibility();
                Check(hidden.Coverage > .9f && !hidden.Pickable && !hidden.Hit.enabled, "Occluded preview publishes visibility and disables interaction");
                Check(g.Pick(new Vector2(g.ViewCamera.pixelWidth * .5f, g.ViewCamera.pixelHeight * .5f)) == highlighted, "Near visible world wins over a disabled obscured sphere");
                Check((bool)Read(hidden.Entry.Id, "GalleryVisibility")["pickable"] == false, "Visibility measurement is present in native Flecs");
                g.CancelDrag();
                g.StepPointer(Vector2.zero, false);
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                // Thick ring volume wins over an unrelated sphere farther down the same ray.
                var ringSource = g.Items.First(i => i.Entry.Active && g.Pick(Point(i)) == i);
                var ringSourcePoint = Point(ringSource);
                g.StepPointer(ringSourcePoint, true);
                g.StepPointer(ringSourcePoint + Vector2.right * 20, true);
                Vector3 ringSpot = Vector3.zero;
                int hitLevel = 0;
                Vector2 ringPixel = Vector2.zero;
                for (int level = 1; level <= g.Layers.Count && hitLevel == 0; level++)
                for (int segment = 0; segment < 128; segment++)
                {
                    float ringAngle = segment * Mathf.PI / 64;
                    var layer = g.Layers[level - 1];
                    var spot = layer.transform.position + new Vector3(Mathf.Cos(ringAngle) * layer.Radius, diameter * .3f, Mathf.Sin(ringAngle) * layer.Radius);
                    Vector3 pixel = g.ViewCamera.WorldToScreenPoint(spot);
                    if (pixel.z <= 0 || pixel.x < 20 || pixel.x > g.ViewCamera.pixelWidth - 20 || pixel.y < 20 || pixel.y > g.ViewCamera.pixelHeight - 20)
                        continue;
                    if (g.Pick(pixel) == null && g.PickLayer(pixel) == level)
                    {
                        ringSpot = spot;
                        ringPixel = pixel;
                        hitLevel = level;
                        break;
                    }
                }
                Check(hitLevel > 0, "Thick volumetric ring hit area works between spheres");
                var behindRing = g.Items.First(i => i != ringSource && i.Entry.Active && i.Level != hitLevel);
                foreach (var otherWorld in g.Items)
                if (otherWorld != behindRing)
                    otherWorld.Entry.Anchor.gameObject.SetActive(false);
                Ray ringRay = g.ViewCamera.ScreenPointToRay(ringPixel);
                float spotDistance = Vector3.Dot(ringSpot - ringRay.origin, ringRay.direction);
                behindRing.Entry.Anchor.position = ringRay.GetPoint(spotDistance + diameter * 3);
                g.RefreshVisibility();
                report["ringDepth"] = new JObject { ["expectedLayer"] = hitLevel, ["actualLayer"] = g.PickLayer(ringPixel), ["sphereLayer"] = behindRing.Level, ["spherePick"] = g.Pick(ringPixel)?.Entry.Id, ["expectedSphere"] = behindRing.Entry.Id };
                Check(g.Pick(ringPixel) == behindRing && g.PickLayer(ringPixel) == hitLevel, "A farther world collider cannot steal middle-drag from the visible ring volume");
                Check(CrystalWorldGallery.RayCapsule(new Ray(new Vector3(0, .8f, -3), Vector3.forward), Vector3.left, Vector3.right, diameter * .5f, out _), "Ring capsule thickness matches the sphere diameter");
                g.CancelDrag();
                g.StepPointer(Vector2.zero, false);
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                // Reversing manual gallery zoom immediately starts approaching the pointed world.
                var pick = g.Items.First(i => i.Entry.Active && g.Pick(Point(i)) == i);
                g.StepPointer(Point(pick), false, scroll: -wheel);
                g.StepPointer(Point(pick), false, scroll: wheel);
                Check(!g.Returning && (bool)Read("GalleryTreeSettings", "GalleryTreeNavigation")["approach"], "Zoom-in immediately reverses manual gallery zoom");
                g.RestoreArrangement(snapshot);
                await Task.Delay(200);
                // Rotating inside a world must not rotate the outside exit portal behind the camera.
                pick = g.Items.First(i => i.Entry.Active && g.Pick(Point(i)) == i);
                g.EnterWorld(pick.Entry.Id);
                var portal = (JObject)JObject.Parse(g.CaptureArrangement())["portal"]["gallery"];
                g.StepPointer(new Vector2(200, 200), false, right: true);
                g.StepPointer(new Vector2(950, 300), false, right: true);
                g.StepPointer(new Vector2(950, 300), false);
                await Task.Delay(180);
                var edited = g.Stage.ActualView;
                g.ReturnToGallery();
                Check((bool)Read("GalleryTreeSettings", "GalleryTreeNavigation")["portalReturn"] && g.Stage.EntryBlend > .5f, "World remains on screen while aligning to its original entry portal");
                await Task.Delay(Mathf.CeilToInt(g.Profile.ExitAlignmentSeconds * 1000) + 200);
                var afterPortal = (JObject)JObject.Parse(g.CaptureArrangement())["portal"]["gallery"];
                Check(JToken.DeepEquals(portal, afterPortal), "Internal orbit cannot overwrite the recorded gallery entry view");
                Check(Mathf.Abs(Mathf.DeltaAngle((float)Read("GalleryTreeSettings", "GalleryView")["yaw"], (float)portal["yaw"])) < .01f, "Outside pullback uses entry orientation after an internal orbit");
                var storedEditing = Read(pick.Entry.Id, "GalleryView");
                float editingError = new[] { "x", "y", "z", "yaw", "pitch", "zoom" }.Max(key => Mathf.Abs((float)edited[key] - (float)storedEditing[key]));
                report["editingCameraError"] = editingError;
                Check(editingError < .001f, "Portal transition preserves the saved editing camera for resume");
                g.RestoreArrangement(snapshot);
                await Task.Delay(150);
                var glowStates = Enumerable.Range(1, g.Layers.Count).Select(level => Read("GalleryTreeLayer" + level, "GalleryLayerIntent")).ToArray();
                Check(glowStates.Select(value => (float)value["glowPhase"]).Distinct().Count() == g.Layers.Count, "Every ring has a distinct seeded travelling-glow phase");
                Check(glowStates.Any(value => (float)value["glowRate"] < 0) && glowStates.Any(value => (float)value["glowRate"] > 0), "Ring travelling glows include both directions");
                var glowSave = g.CaptureArrangement();
                var beforeGlow = (JArray)JObject.Parse(glowSave)["layers"];
                g.RestoreArrangement(glowSave);
                var afterGlow = (JArray)JObject.Parse(g.CaptureArrangement())["layers"];
                Check(Enumerable.Range(0, beforeGlow.Count).All(level => (float)beforeGlow[level]["glowPhase"] == (float)afterGlow[level]["glowPhase"] && (float)beforeGlow[level]["glowRate"] == (float)afterGlow[level]["glowRate"]), "Glow phase/direction survive reconstruction without restarting");
                float shallow = CrystalWorldGallery.CircleCoverage(Vector2.zero, 1, new Vector2(1.95f, 0), 1);
                Check(shallow > 0 && shallow < .01f, "Analytic overlap detects a barely covered sphere edge");
                // Move two actual previews into slight projected overlap while held, keeping the camera fixed.
                highlighted = g.Items.First(i => i.Entry.Active && g.Pick(Point(i)) == i);
                var startHover = Point(highlighted);
                g.StepPointer(startHover, true);
                g.StepPointer(startHover + Vector2.right * 20, true);
                var behind = g.Items.First(i => i != highlighted && i.Entry.Active);
                var cameraTransform = g.ViewCamera.transform;
                float r = diameter * .5f;
                foreach (var extra in g.Items)
                if (extra != highlighted && extra != behind)
                    extra.Entry.Anchor.gameObject.SetActive(false);
                highlighted.Entry.Anchor.position = cameraTransform.position + cameraTransform.forward * 12;
                behind.Entry.Anchor.position = cameraTransform.position + cameraTransform.forward * 13 + cameraTransform.right * (r * (1 + 13f / 12) * .975f);
                g.RefreshVisibility();
                Check(behind.Coverage > 0 && behind.Coverage < .1f && behind.Visibility < .95f, "A small real preview overlap already produces visible fading");
                Check(!behind.Pickable, "A faded preview cannot intercept pointer input");
                var nav = Read("GalleryTreeSettings", "GalleryTreeNavigation");
                nav["target"] = behind.Entry.Id;
                world.Session.SetComponentJson("GalleryTreeSettings", "GalleryTreeNavigation", nav.ToString());
                g.RefreshVisibility();
                Check(!behind.Pickable, "A previously targeted world cannot bypass occlusion picking");
                g.CancelDrag();
                behind.Entry.Anchor.position = g.ViewCamera.transform.position + g.ViewCamera.transform.forward * 12;
                highlighted.Entry.Anchor.gameObject.SetActive(false);
                behind.Alpha = .1f;
                g.RefreshVisibility();
                Check(!behind.Pickable, "A world still visually fading in is not interactive yet");
                g.CancelDrag();
                g.StepPointer(Vector2.zero, false);
                g.RestoreArrangement(snapshot);
                artifacts.Capture(g, "07-final-gallery");
                Check(artifacts.Count == 11, "Local screenshot artifacts are generated without AI");
                report["passed"] = true;
            }
            catch (Exception e) { report["passed"] = false; report["failure"] = e.ToString(); artifacts.Capture(g, "failure-state"); }
            finally
            {
                try
                {
                    g.CancelDrag();
                    g.RestoreArrangement(baseline);
                }
                catch (Exception e) { report["cleanupFailure"] = e.Message; report["passed"] = false; }
                if (saved)
                    File.WriteAllBytes(CrystalWorldGallery.SavePath, original);
                else if (File.Exists(CrystalWorldGallery.SavePath))
                    File.Delete(CrystalWorldGallery.SavePath);
                g.ReadDevices = readDevices;
                report["checks"] = checks;
                report["frames"] = Time.frameCount - start;
                artifacts.Finish(report);
                File.WriteAllText(Path.Combine(Application.dataPath, "../Verification/crystal-gallery-checks.json"), report.ToString());
            }
            return report;
        }
    }

}

#endif

