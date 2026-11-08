using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ecaty.Flecs;
using Ecaty.Agents.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ecaty.Editor
{
    public static class WorkshopChainVerification
    {
        private const string Pending = "Ecaty.WorkshopChainVerification";
        private static bool running;

        [InitializeOnLoadMethod]
        private static void Hook() => EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                EditorApplication.delayCall += Verify;
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending + ".exit", false))
                EditorApplication.Exit(SessionState.GetInt(Pending + ".code", 1));
        };

        [MenuItem("Ecaty/Runtime engine/Start workshop chain", true)]
        private static bool CanStart() => EditorApplication.isPlaying && !running;

        [MenuItem("Ecaty/Runtime engine/Start workshop chain")]
        private static void Start()
        {
            var experience = Object.FindAnyObjectByType<RuntimeExperience>();
            if (!experience) throw new InvalidOperationException("Ecaty has no running experience.");
            Require(experience.Dock && experience.Dock.StartBellAndGate(), "Could not open the Bell & Gate example.");
        }

        [MenuItem("Ecaty/Runtime engine/Verify workshop chain", true)]
        private static bool CanVerify() => !running && !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Ecaty/Runtime engine/Verify workshop chain")]
        public static void StartVerification()
        {
            if (!CanVerify()) return;
            SessionState.SetBool(Pending, true);
            EditorApplication.isPlaying = true;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static async Task Until(Func<bool> condition, string message)
        {
            var deadline = DateTime.UtcNow.AddSeconds(18);
            while (!condition())
            {
                Require(DateTime.UtcNow < deadline && EditorApplication.isPlaying, message);
                await Task.Yield();
            }
        }

        private static async Task FrameCount(int count)
        {
            int end = Time.frameCount + count;
            await Until(() => Time.frameCount >= end, "Play Mode stopped advancing.");
        }

        private static async Task CheckCamera(RuntimeExperience experience, JArray checks)
        {
            var camera = experience.Camera;
            var navigation = experience.Navigation;
            Require(navigation, "The Cinemachine workshop camera was not created.");
            await FrameCount(4);
            var center = new Vector3(-2.5f, 0, 1);
            Vector2 click = camera.WorldToScreenPoint(center);
            var initialPivot = navigation.Center;
            navigation.StepPointer(click, true, false, false, now: 1);
            navigation.StepPointer(click, false, false, false, now: 1.05f);
            Require(navigation.Center == initialPivot && !navigation.IsNavigating, "A single click took camera ownership.");
            navigation.StepPointer(click, true, false, false, now: 1.15f);
            navigation.StepPointer(click, false, false, false, now: 1.20f);
            var ground = experience.GetComponent<FlecsWorld>();
            var baseBounds = ground.FindView(ground.Session.Lookup("diorama.Ground")).GetComponentInChildren<Renderer>().bounds;
            Require(Vector3.Distance(navigation.Center, baseBounds.center) < .08f && navigation.IsNavigating,
                $"The double click did not center the world base. Actual: {navigation.Center}, expected: {baseBounds.center}.");
            await Until(() => Mathf.Abs(camera.orthographicSize - navigation.TargetZoom) < .01f,
                "World framing did not settle.");
            foreach (float x in new[] { baseBounds.min.x, baseBounds.max.x })
            foreach (float y in new[] { baseBounds.min.y, baseBounds.max.y })
            foreach (float z in new[] { baseBounds.min.z, baseBounds.max.z })
            {
                Vector3 corner = camera.WorldToViewportPoint(new Vector3(x, y, z));
                Require(corner.x > 0 && corner.x < 1 && corner.y > 0 && corner.y < 1,
                    $"World framing clipped a base corner at {corner}.");
            }
            navigation.ResetView();
            await FrameCount(3);
            float overview = navigation.TargetZoom;
            float wheelNotch = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms ? 1f : 120f;
            var wheelTarget = new Vector3(1.5f, 0, .3f);
            Vector2 wheel = camera.WorldToScreenPoint(wheelTarget);
            navigation.StepPointer(wheel, false, false, false, wheelNotch, now: 2f);
            Require(navigation.TargetZoom < overview * .83f && Vector3.Distance(navigation.Center, wheelTarget) < .08f,
                "Wheel zoom did not pick the world point under the cursor.");
            var wheelPivot = navigation.Center;
            await FrameCount(2);
            Require(Vector2.Distance(camera.WorldToScreenPoint(wheelPivot), wheel) < 3f,
                "The wheel anchor drifted on screen during the zoom transition.");
            await Until(() => Mathf.Abs(camera.orthographicSize - navigation.TargetZoom) < .04f,
                "Wheel zoom did not reach its target.");
            Require(Vector2.Distance(camera.WorldToScreenPoint(wheelPivot), wheel) < 3f,
                "The wheel anchor moved on screen after zooming.");
            navigation.StepPointer(wheel, false, false, false, -wheelNotch, now: 2.1f);
            Require(Mathf.Abs(navigation.TargetZoom - overview) < .02f && navigation.Center == wheelPivot,
                "A wheel burst re-picked the pivot without cursor motion.");
            await FrameCount(3);

            var rightTarget = new Vector3(-1.1f, 0, 1.1f);
            Vector2 zoomStart = camera.WorldToScreenPoint(rightTarget);
            var up = Vector2.up * (camera.pixelHeight * .20f);
            navigation.StepPointer(zoomStart, false, false, true);
            navigation.StepPointer(zoomStart + up, false, false, true);
            Require(navigation.TargetZoom < overview * .5f && Vector3.Distance(navigation.Center, rightTarget) < .08f,
                "Right drag did not zoom from the point under the mouse when the drag started.");
            var pivot = navigation.Center;
            float closeZoom = navigation.TargetZoom;
            navigation.StepPointer(zoomStart + up * .6f, false, false, true);
            Require(navigation.TargetZoom > closeZoom && navigation.Center == pivot, "Moving the drag retargeted the pivot.");
            navigation.StepPointer(zoomStart + up * .6f, false, false, false);
            float zoomed = navigation.TargetZoom;
            await FrameCount(2);
            Require(Vector2.Distance(camera.WorldToScreenPoint(pivot), zoomStart) < 3f,
                "The right-drag anchor drifted on screen during the zoom transition.");
            await Until(() => Mathf.Abs(camera.orthographicSize - zoomed) < .06f, "Cinemachine did not apply the drag zoom.");


            var initialRotation = camera.transform.rotation;
            navigation.StepPointer(click, true, false, false);
            navigation.StepPointer(click + new Vector2(110, -24), true, false, false);
            navigation.StepPointer(click + new Vector2(110, -24), false, false, false);
            await Until(() => Quaternion.Angle(initialRotation, camera.transform.rotation) > 12, "Cinemachine did not orbit.");
            Require(navigation.Center == pivot, "The orbit drag picked a new center.");
            var panStart = camera.pixelRect.center;
            navigation.StepPointer(panStart, false, true, false);
            navigation.StepPointer(panStart + new Vector2(90, 10), false, true, false);
            navigation.StepPointer(panStart + new Vector2(90, 10), false, false, false);
            Require(Vector3.Distance(navigation.Center, pivot) > .1f, "Middle drag did not pan.");
            navigation.ResetView();
            await Until(() => Mathf.Abs(camera.orthographicSize - 5.5f) < .05f &&
                Vector2.Distance(camera.WorldToScreenPoint(navigation.Center), camera.pixelRect.center) < 8,
                "The camera did not settle back to the overview.");
            checks.Add("Single clicks preserve the pivot and a double click picks once. Wheel and right-drag keep their picked world point at the same screen pixel through the zoom transition. Orbit, pan and overview reset work.");
        }


        private static async Task Assemble(RuntimeExperience experience, JArray checks)
        {
            var tools = experience.Workshop;
            var camera = experience.Camera;
            var world = experience.GetComponent<FlecsWorld>();
            Require(tools, "Workshop authoring was not created.");
            float now = Time.unscaledTime;
            void Pointer(Vector3 worldPoint, bool down, float advance = .1f)
            {
                now += advance;
                tools.StepPointer(camera.WorldToScreenPoint(worldPoint), down, now);
            }

            var empty = new Vector3(3, 0, 0);
            Pointer(empty, true);
            Pointer(empty, true, .45f);
            Pointer(empty, false);
            var miniature = GameObject.Find("ChainBell preview");
            Require(miniature, "Holding empty ground did not reveal the 3D library.");
            await FrameCount(2);
            Pointer(miniature.transform.position, true);
            Pointer(miniature.transform.position, false);
            Pointer(empty, true);
            Pointer(empty, false);
            Require(world.Session.Lookup("ChainBell") != 0, "The chosen miniature did not create a Flecs prop.");
            Require(tools.Place("ChainBunny", Vector3.zero) && tools.Place("ChainPlate", Vector3.zero) &&
                tools.Place("ChainGate", new Vector3(0, 0, .65f)) && tools.Place("ChainDuck", new Vector3(0, 0, -1.5f)),
                "Library recipes did not create the other workshop pieces.");
            Require(!tools.Connect("ChainPlate", "ChainGate"), "A wire with a missing dependency was allowed.");
            await FrameCount(3);

            var bell = new Vector3(3, .28f, 0);
            Pointer(bell, true);
            Pointer(bell, false);
            Pointer(bell, false);
            var port = GameObject.Find("Workshop link port");
            Require(port, "Selecting a compatible source did not reveal its wire port.");
            Pointer(port.transform.position, true);
            var rabbitBounds = experience.Models["ChainBunny"].Creation.WorldBounds();
            var rabbit = rabbitBounds.center + Vector3.up * (rabbitBounds.extents.y * .70f);
            Pointer(rabbit, true);
            Pointer(rabbit, false);
            var wireView = GameObject.Find("Workshop wire");
            Require(wireView && !wireView.GetComponent<LineRenderer>().enabled, "The wire preview remained visible after dropping it.");
            var link = world.Session.ComponentJson(world.Session.Lookup("ChainBellLink"), "WorkshopConnection");
            Require(!string.IsNullOrEmpty(link) && (string)JObject.Parse(link)["target"] == "ChainBunny",
                "Dragging to the bunny produced the wrong connection: " + link);
            Require(tools.Connect("ChainBunny", "ChainPlate"), "Could not connect the bunny to the plate.");
            Require(tools.Connect("ChainPlate", "ChainGate"), "Could not connect the plate to the gate.");
            Require(tools.Connect("ChainGate", "ChainDuck"), "Could not connect the gate to the duck.");
            await FrameCount(3);
            checks.Add("Held empty space, picked a 3D miniature, placed it, drew a wire onto a character and installed the full chain offline.");
        }


        private static async Task CheckDock(RuntimeExperience experience, JArray checks)
        {
            var chat = Object.FindAnyObjectByType<AgentChatPanel>();
            Require(chat && !chat.Expanded, "Chat started expanded.");
            VisualElement tree = null;
            var renderer = chat.GetComponent<PanelRenderer>();
            PanelRenderer.VersionedUIReloadCallback getUi = (_, root, _) => tree = root;
            renderer.RegisterUIReloadCallback(getUi);
            try
            {
                await Until(() => tree?.Q<Button>("workshopSettings")?.resolvedStyle.width > 0,
                    "The bottom dock did not lay out.");
                var settings = tree.Q<Button>("workshopSettings");
                Require(settings != null && tree.Q<Button>("workshopSeed") == null && tree.Q<Button>("chatLauncher") == null &&
                    tree.Q<TextField>("messageInput") == null &&
                    Mathf.Abs(settings.worldBound.center.x - tree.worldBound.center.x) < 3,
                    "The single settings launcher or account-only view did not resolve.");
                void Submit(string name)
                {
                    var button = tree.Q<Button>(name);
                    Require(button != null && button.enabledInHierarchy, "Missing dock action: " + name);
                    using var submit = NavigationSubmitEvent.GetPooled();
                    submit.target = button;
                    button.SendEvent(submit);
                }

                Submit("workshopSettings");
                Require(tree.Q<Button>("workshopSound") == null && tree.Q<Button>("workshopDebug") == null &&
                    Mathf.Approximately(AudioListener.volume, 1f), "Settings should leave sound on and hide development controls.");
                Submit("workshopAI");
                Require(chat.Expanded && !tree.Q<VisualElement>("accounts").ClassListContains("agent-chat__accounts--hidden"),
                    "Settings did not open the existing AI account chooser.");
                chat.SetExpanded(false);

                var dock = experience.Dock;
                var world = experience.GetComponent<FlecsWorld>();
                Require(dock && !dock.HasWorld, "The world chooser opened after a world had already started.");
                string save = RuntimeExperience.SavePath;
                bool saveExisted = File.Exists(save);
                var saveTime = File.GetLastWriteTimeUtc(save);

                GameObject Icon(WorkshopIcon icon) => GameObject.Find("Seed choice " + icon);

                void Tap(Vector2 point, bool fastTap = false)
                {
                    if (!fastTap) experience.Workshop.RoutePointer(point, true, 10f);
                    experience.Workshop.RoutePointer(point, false, 10.1f, pressedThisFrame: fastTap);
                }

                void ClickIcon(WorkshopIcon icon)
                {
                    var view = Icon(icon);
                    Require(view, "Missing 3D seed icon: " + icon);
                    Tap(experience.Camera.WorldToScreenPoint(view.transform.position));
                }

                async Task ClickSeed(bool fastTap = false)
                {
                    var view = GameObject.Find("World seed");
                    Require(view, "The world seed was not visible.");
                    Tap(experience.Camera.WorldToScreenPoint(view.transform.position), fastTap);
                    Require(dock.OwnsSeedPointer, "The workshop pointer route did not capture the seed.");
                    await Until(() => Icon(WorkshopIcon.NewWorld) != null, "Flecs did not open the 3D chooser.");
                    Require((Icon(WorkshopIcon.OpenWorld) != null) == saveExisted &&
                        GameObject.Find("Workshop miniatures") == null &&
                        tree.Q<VisualElement>("workshopPopover").style.display.value == DisplayStyle.None,
                        "The world chooser should use only 3D icons.");
                }

                var seedView = GameObject.Find("World seed");
                var mesh = seedView.GetComponentInChildren<MeshFilter>().sharedMesh;
                Require(seedView.GetComponentsInChildren<MeshRenderer>().Length == 1 &&
                    mesh == Resources.Load<Mesh>("WorkshopSeedArtMesh") && mesh.vertexCount > 0 &&
                    mesh.GetIndexCount(0) / 3 < 750,
                    "The seed must use one lightweight MudBun-baked mesh.");
                var pose = world.FindView(world.Session.Lookup("WorkshopSeedMotion")).transform;
                Quaternion firstRotation = pose.rotation;
                await Until(() => Quaternion.Angle(firstRotation, pose.rotation) > 1f, "Flecs did not rotate the seed.");
                Quaternion sampledPose = pose.rotation;
                await FrameCount(1);
                Require(Quaternion.Angle(seedView.transform.rotation, sampledPose) < .05f ||
                    Quaternion.Angle(seedView.transform.rotation, pose.rotation) < .05f,
                    "The visible seed does not use the Flecs pose.");
                await ClickSeed(fastTap: true);
                Require(JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("WorkshopSeedMotion"), "WorkshopSeedRequest"))
                    .Value<int>("count") == 1, "The seed click bypassed the Flecs request.");
                checks.Add($"One baked MudBun seed mesh ({mesh.GetIndexCount(0) / 3} triangles) follows Flecs rotation and opens a text-free 3D chooser.");
                ClickIcon(WorkshopIcon.Bell);
                await FrameCount(4);
                Require(dock.HasWorld && world.Session.Lookup("ChainBell") != 0 &&
                    world.Session.Lookup("ChainGateLink") != 0 && !GameObject.Find("World seed"),
                    "The bundled Bell & Gate world did not start or the seed stayed visible.");

                async Task Recall()
                {
                    var previousKeyboard = Keyboard.current;
                    var keyboard = InputSystem.AddDevice<Keyboard>();
                    try
                    {
                        keyboard.MakeCurrent();
                        InputSystem.QueueStateEvent(keyboard,
                            new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.Escape));
                        await FrameCount(2);
                        InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                        await FrameCount(2);
                    }
                    finally
                    {
                        InputSystem.RemoveDevice(keyboard);
                        previousKeyboard?.MakeCurrent();
                    }
                    Require(experience.Navigation.TargetZoom < 3f, "Recall zoomed out instead of framing the seed.");
                    await ClickSeed();
                }

                await Recall();
                Require(Icon(WorkshopIcon.SaveWorld), "An active world did not expose saving.");
                ClickIcon(WorkshopIcon.Environment);
                var cameraData = experience.Camera.GetUniversalAdditionalCameraData();
                bool previousPostProcessing = cameraData.renderPostProcessing;
                await FrameCount(3);
                Color baseline = SampleFrame(experience.Camera.targetTexture);
                ClickIcon(WorkshopIcon.Honey);
                await Until(() => FindLook(out _), "The Flecs look did not reach the URP Volume.");
                await FrameCount(4);
                Color graded = SampleFrame(experience.Camera.targetTexture);
                Require(Mathf.Abs(baseline.r - graded.r) + Mathf.Abs(baseline.g - graded.g) + Mathf.Abs(baseline.b - graded.b) > .01f,
                    $"Post-processing did not change a rendered pixel. Before {baseline}; after {graded}.");

                world.Session.SetManagedScript("SecondWorkshopLook",
                    "SecondWorkshopLookBinding { CapBinding: {target: diorama, enabled: true, revision: 1}; " +
                    "CapPostProcessing: {exposure: 0.1, bloom: 0.1, threshold: 0.9} }");
                var capabilities = world.GetComponent<Ecaty.Flecs.Capabilities.CapabilityRuntime>();
                capabilities.Reconcile();
                Require(JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("SecondWorkshopLookBinding"), "CapState"))
                    .Value<int>("phase") == 1, "A second grade did not bind.");
                world.Session.SetManagedScript("WorkshopWorldLook", "// Remove the first grade");
                capabilities.Reconcile();
                Require(cameraData.renderPostProcessing, "Removing one grade disabled the other grade's camera.");
                world.Session.SetManagedScript("SecondWorkshopLook", "// Remove the last grade");
                capabilities.Reconcile();
                Require(cameraData.renderPostProcessing == previousPostProcessing,
                    "Removing the last grade did not restore the original camera state.");
                ClickIcon(WorkshopIcon.Honey);
                await Until(() => FindLook(out _), "The primary grade did not reactivate.");

                var savedLook = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-ecaty-look.json");
                try
                {
                    experience.Save(savedLook);
                    Require(dock.StartSavedWorld(savedLook), "Could not reload the graded world.");
                    await Until(() => FindLook(out _), "The grade did not rebuild after save/reopen.");
                }
                finally { if (File.Exists(savedLook)) File.Delete(savedLook); }
                await Recall();
                ClickIcon(WorkshopIcon.Environment);
                ClickIcon(WorkshopIcon.Neutral);
                await Until(() => !FindLook(out _) && cameraData.renderPostProcessing == previousPostProcessing,
                    "Reset did not remove the owned Volume and restore camera state.");
                await ClickSeed();
                checks.Add("A 3D world icon applies a Flecs-authored URP grade, changes rendered pixels, survives save/reopen and resets cleanly.");

                bool FindLook(out Volume found)
                {
                    found = Object.FindObjectsByType<Volume>().FirstOrDefault(candidate =>
                        candidate && candidate.enabled && candidate.name == "CapPostProcessing binding");
                    return found && cameraData.renderPostProcessing && found.sharedProfile &&
                        found.sharedProfile.TryGet<WhiteBalance>(out var balance) && balance.temperature.overrideState &&
                        Mathf.Approximately(balance.temperature.value, 24);
                }

                var original = world.Session;
                ClickIcon(WorkshopIcon.Playground);
                Require(world.Session == original && world.Session.Lookup("ChainBell") != 0,
                    "The seed flow replaced the live world before confirmation.");
                ClickIcon(WorkshopIcon.Confirm);
                await FrameCount(4);
                Require(experience.Models.ContainsKey("CreatureA") && experience.Models.ContainsKey("CreatureB") &&
                    world.Session.Lookup("ChainBell") == 0 && !GameObject.Find("World seed"),
                    "The Creature Playground did not replace the old world.");

                await Recall();
                ClickIcon(WorkshopIcon.NewWorld);
                ClickIcon(WorkshopIcon.Confirm);
                await FrameCount(3);
                Require(dock.HasWorld && world.Session.Lookup("CreatureA") == 0 &&
                    world.Session.Lookup("CreatureB") == 0 && !GameObject.Find("World seed") && !chat.Expanded,
                    "The blank world did not start with the seed hidden.");
                Require(File.Exists(save) == saveExisted && File.GetLastWriteTimeUtc(save) == saveTime,
                    "Switching worlds modified the existing save.");
                checks.Add("3D icons launch both examples, recall frames the seed, switching requires confirmation, and a blank world preserves the local save.");
            }
            finally { renderer.UnregisterUIReloadCallback(getUi); }
        }


        private static Color SampleFrame(RenderTexture frame)
        {
            var previous = RenderTexture.active;
            var sample = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = frame;
                sample.ReadPixels(new Rect(25, 25, 1, 1), 0, 0);
                return sample.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(sample);
            }
        }

        private static async void Verify()
        {
            if (running) return;
            running = true;
            bool owned = SessionState.GetBool(Pending, false);
            SessionState.EraseBool(Pending);
            var checks = new JArray();
            var report = new JObject { ["passed"] = false, ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["checks"] = checks };
            RenderTexture canvas = null;
            Camera camera = null;
            try
            {
                await Until(() => Object.FindAnyObjectByType<RuntimeExperience>() != null, "Runtime did not start.");
                var experience = Object.FindAnyObjectByType<RuntimeExperience>();
                var world = experience.GetComponent<FlecsWorld>();
                camera = experience.Camera;
                canvas = new RenderTexture(960, 540, 24);
                camera.targetTexture = canvas;
                Require(world.Session.Lookup("WorldSeed") != 0 && world.Session.Lookup("diorama") == 0 &&
                    world.Session.Lookup("diorama.Ground") == 0, "Startup must have only the seed.");
                await FrameCount(3);
                await CheckDock(experience, checks);
                await CheckCamera(experience, checks);
                await Assemble(experience, checks);
                float BellX() => (float)Component("ChainBell", "Position")["x"];
                int Rings() => (int)Component("ChainBellLink", "WorkshopBell.mut")["rings"];
                bool GateOpen() => (bool)Component("ChainGateLink", "WorkshopGateState")["open"];
                JObject Component(string path, string type) => JObject.Parse(world.Session.ComponentJson(world.Session.Lookup(path), type));
                Vector2 At(float x) => experience.Camera.WorldToScreenPoint(new Vector3(x, 0.28f, 0));
                ulong BellId() => world.Session.Lookup("ChainBell");
                void Tap(float x)
                {
                    var screen = At(x);
                    ulong picked = experience.ForwardWorldPointer(screen, true, true);
                    Require(picked == BellId(), $"Bell was not picked at {screen}. Picked {world.Session.EntityPath(picked) ?? "nothing"}.");
                    experience.ForwardWorldPointer(screen, false, true);
                }

                int installedAt = Time.frameCount;
                await Until(() => Time.frameCount >= installedAt + 3, "The runtime did not arm the script listeners.");
                var orbit = experience.Navigation;
                var start = At(3);
                var destination = At(1.45f);
                var bellView = world.FindView(BellId());
                var bellBounds = bellView.GetComponentInChildren<Renderer>().bounds;
                var bellPoint = (Vector2)camera.WorldToScreenPoint(bellBounds.center);
                orbit.StepPointer(bellPoint, true, false, false, now: 20f);
                orbit.StepPointer(bellPoint, false, false, false, now: 20.05f);
                orbit.StepPointer(bellPoint, true, false, false, now: 20.15f);
                orbit.StepPointer(bellPoint, false, false, false, now: 20.2f);
                Require(Vector3.Distance(orbit.Center, bellBounds.center) < .08f && orbit.TargetZoom < 1.5f,
                    "Double click did not frame the full bell at object scale.");
                checks.Add("Double-click frames the whole world base or prop at its own scale.");
                orbit.ResetView();
                await FrameCount(3);
                var oldCenter = orbit.Center;
                orbit.StepPointer(start, true, false, false);
                experience.ForwardWorldPointer(start, true, now: 1);
                orbit.StepPointer(destination, true, false, false);
                Require(orbit.IsNavigating, "A normal drag over the bell failed to orbit.");
                experience.ForwardWorldPointer(destination, true, now: 1.05f);
                orbit.StepPointer(destination, false, false, false);
                experience.ForwardWorldPointer(destination, false, now: 1.10f);
                Require(Mathf.Abs(BellX() - 3f) < .05f && Rings() == 0 && orbit.Center == oldCenter,
                    "Orbiting over a bell moved it, rang it, or re-picked the pivot.");
                orbit.ResetView();
                await FrameCount(3);
                var rabbit = experience.Models["ChainBunny"].Creation.WorldBounds();
                Vector2 actorClick = camera.WorldToScreenPoint(rabbit.center + Vector3.up * rabbit.extents.y * .65f);
                experience.ForwardWorldPointer(actorClick, true, true);
                experience.ForwardWorldPointer(actorClick, false, true);
                await FrameCount(3);
                Require(Rings() == 0 && !GateOpen(), "A character click started the bell chain.");
                checks.Add("Left drag over a prop orbits without moving it. Clicking a character leaves the bell chain idle.");


                Tap(3);
                await Until(() => Rings() == 1, "A world-pointer tap did not ring the bell.");
                Require(!GateOpen(), "Out-of-range bell released the pressure plate.");
                checks.Add("Unity pointer picks a Flecs prop; out-of-range ringing leaves the gate closed.");

                var startGrab = At(3);
                var endGrab = At(1.45f);
                orbit.StepPointer(startGrab, true, false, false);
                experience.ForwardWorldPointer(startGrab, true, now: 2);
                experience.ForwardWorldPointer(startGrab, true, now: 2.4f);
                Require(experience.WorldDragActive, "The held bell did not claim the drag.");
                orbit.StepPointer(endGrab, true, false, false);
                Require(!orbit.IsNavigating, "An active prop drag started orbit.");
                experience.ForwardWorldPointer(endGrab, true, now: 2.45f);
                orbit.StepPointer(endGrab, false, false, false);
                experience.ForwardWorldPointer(endGrab, false, now: 2.5f);

                Require(Mathf.Abs(BellX() - 1.45f) < 0.12f && Rings() == 1, "Dragging failed or accidentally rang the bell.");
                await Until(() => Mathf.Abs(world.FindView(BellId()).transform.position.x - BellX()) < 0.02f,
                    "The Flecs placement did not reach the Unity view.");
                Tap(BellX());
                await Until(() => Rings() == 2 && GateOpen(), "The motor never vacated the plate.");
                await Until(() => experience.Models["ChainDuck"].Actor.Motor.transform.position.z > 1.65f,
                    "The duck did not cross in the running world.");
                var gate = world.FindView(world.Session.Lookup("ChainGate"));
                Require(gate && gate.transform.position.y > 1.4f, "Gate state did not reach the Unity view.");
                await Until(() =>
                {
                    var json = world.Session.ComponentJson(world.Session.Lookup("ChainBellLink.chime"), "CapState");
                    return !string.IsNullOrEmpty(json) && (int?)JObject.Parse(json)["phase"] == 1;
                }, "The Unity audio adapter never bound the chime.");
                var chime = Component("ChainBellLink.chime", "CapAudioNote");
                Require((int)chime["sequence"] == 2, "A sound command was not emitted on the second ring.");
                checks.Add("Live drag and tap drive actual motors, a Unity gate view and an active synthesized audio binding.");

                string saved = Path.GetTempFileName();
                try
                {
                    experience.Save(saved);
                    Require(experience.Dock.StartSavedWorld(saved), "The seed could not open the saved fixture.");
                }
                finally { File.Delete(saved); }
                int reloadedAt = Time.frameCount;
                await Until(() => Time.frameCount >= reloadedAt + 3 &&
                    GateOpen() && experience.Models["ChainDuck"].Actor.Motor.transform.position.z > 1.65f &&
                    world.FindView(BellId()) && Mathf.Abs(world.FindView(BellId()).transform.position.x - BellX()) < 0.02f,
                    "Saved world did not reconstruct.");
                Require(Rings() == 2 && Mathf.Abs(BellX() - 1.45f) < 0.12f, "Placement or rule state was lost.");
                Tap(BellX());
                await Until(() => Rings() == 3, "Rebuilt links did not accept a new event.");
                checks.Add("Capture and reconstruction preserve the placement and links and continue accepting pointer events.");
                var tools = experience.Workshop;
                Require(tools.Disconnect("ChainBunny") && tools.Disconnect("ChainBell"),
                    "Existing wires could not be detached after reload.");
                Require(world.Session.Lookup("RuntimeBehaviors.wireGate") == 0 &&
                    world.Session.Lookup("RuntimeBehaviors.wireCrossing") == 0, "Dependent wires survived a detached sensor.");
                Require(tools.Connect("ChainBell", "ChainDuck") && tools.Connect("ChainDuck", "ChainPlate") &&
                    tools.Connect("ChainPlate", "ChainGate") && tools.Connect("ChainGate", "ChainBunny"),
                    "The fixture could not be rewired with the characters swapping roles.");
                Require((string)Component("ChainBellLink", "WorkshopConnection")["target"] == "ChainDuck" &&
                    (string)Component("ChainCrossLink", "WorkshopConnection")["target"] == "ChainBunny",
                    "Rewiring did not change the actual Flecs connections.");
                checks.Add("Detached a wire with dependent links and remixed the diorama by swapping actor roles offline.");

                report["passed"] = true;
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (camera) camera.targetTexture = null;
                if (canvas) Object.Destroy(canvas);
                try { File.WriteAllText("Verification/workshop-chain-unity-checks.json", report.ToString()); }
                finally
                {
                    running = false;
                    if (owned)
                    {
                        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ecatyWorkshopChainVerify") >= 0)
                        {
                            SessionState.SetBool(Pending + ".exit", true);
                            SessionState.SetInt(Pending + ".code", (bool)report["passed"] ? 0 : 1);
                        }
                        EditorApplication.isPlaying = false;
                    }
                }
            }
        }
    }
}

