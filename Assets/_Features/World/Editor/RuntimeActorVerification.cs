using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ecaty.Agents;
using Ecaty.Flecs;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Ecaty.Agents.UI;
using FIMSpace.FProceduralAnimation;
using Object = UnityEngine.Object;

namespace Ecaty.Editor
{
    // Plugin Updates restore rest poses. Observe only after all LateUpdate pose writers.
    [DefaultExecutionOrder(30000)]
    public sealed class RuntimePoseProbe : MonoBehaviour
    {
        public Transform Body, Foot, Ear;
        public Vector3 FootLocal;
        public Quaternion EarRotation;
        public Vector3 EarLocal;
        public LegsAnimator Rig;
        public float LeftPhase, RightPhase, ContactError;
        public bool LeftPlanted, RightPlanted;
        private void LateUpdate()
        {
            if (Rig && Rig.LegsInitialized && Rig.Legs.Count == 2)
            {
                var left = Rig.Legs[0]; var right = Rig.Legs[1];
                LeftPhase = left.G_GlueInternalTransition; RightPhase = right.G_GlueInternalTransition;
                LeftPlanted = left.G_Attached && LeftPhase > .98f; RightPlanted = right.G_Attached && RightPhase > .98f;
                ContactError = 0;
                if (LeftPlanted) ContactError = Mathf.Max(ContactError, Mathf.Abs(left.BoneEnd.TransformPoint(left.AnkleToHeel).y));
                if (RightPlanted) ContactError = Mathf.Max(ContactError, Mathf.Abs(right.BoneEnd.TransformPoint(right.AnkleToHeel).y));
            }
            if (Body && Foot) FootLocal = Body.InverseTransformPoint(Foot.position);
            if (Ear) { EarRotation = Ear.localRotation; EarLocal = Ear.localPosition; }
        }
    }

    public static class RuntimeActorVerification
    {
        public static bool Running { get; private set; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static async Task Frames(int count)
        {
            int first = Time.frameCount;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (Time.frameCount < first + count)
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Play Mode stopped advancing.");
                await Task.Yield();
            }
        }
        [InitializeOnLoadMethod]
        private static void HookPlayMode() => EditorApplication.playModeStateChanged += OnPlayMode;
        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("Ecaty.ActorVerification", false))
                RunInPlay();
        }
        [MenuItem("Runtime/Runtime engine/Verify workflow")]
        public static void Start()
        {
            if (Running) return;
            if (EditorApplication.isPlaying) { RunInPlay(); return; }
            SessionState.SetBool("Ecaty.ActorVerification", true);
            EditorApplication.isPlaying = true;
        }
        private static async void RunInPlay()
        {
            if (Running) return;
            Running = true;
            var report = new JObject { ["testedUtc"] = DateTime.UtcNow.ToString("O"), ["passed"] = false, ["checks"] = new JArray() };
            var checks = (JArray)report["checks"];
            var runtimeErrors = new List<string>();
            Application.LogCallback capture = (message, stack, type) => {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    runtimeErrors.Add(message);
            };
            Application.logMessageReceived += capture;
            GameObject root = null, wall = null;
            Gamepad pad = null;
            Keyboard keyboard = null;
            bool startPlay = SessionState.GetBool("Ecaty.ActorVerification", false);
            SessionState.EraseBool("Ecaty.ActorVerification");
            try
            {
                Application.runInBackground = true;
                await Frames(3);
                var chat = Object.FindAnyObjectByType<AgentChatPanel>();
                Require(chat && !chat.Expanded && !chat.IsEditingText, "Startup chat stole gameplay focus.");
                VisualElement ui = null;
                PanelRenderer.VersionedUIReloadCallback getUI = (renderer, tree, version) => ui = tree;
                var chatRenderer = chat.GetComponent<PanelRenderer>();
                chatRenderer.RegisterUIReloadCallback(getUI); chatRenderer.UnregisterUIReloadCallback(getUI);
                var launcher = ui.Q<Button>("workshopSettings");
                Require(launcher != null && launcher.resolvedStyle.width >= 80 &&
                    System.Linq.Enumerable.Any(launcher.resolvedStyle.backdropFilter),
                    "The glass settings launcher style did not resolve.");
                Require(ui.Q<Button>("chatLauncher") == null && ui.Q<TextField>("messageInput") == null,
                    "A standalone chat is still exposed.");
                chat.Controller.ShowAccounts();
                Require(chat.Expanded && !ui.Q<VisualElement>("accounts").ClassListContains("agent-chat__accounts--hidden"),
                    "The account settings could not be opened.");
                chat.SetExpanded(false);

                using (var move = NavigationMoveEvent.GetPooled(Vector2.down, EventModifiers.None)) ui.SendEvent(move);
                await Frames(2);
                Require(!chat.IsEditingText && !chat.Expanded, "Closing/navigation returned focus to chat.");
                checks.Add("the glass settings launcher opens account settings and closing it returns gameplay focus");
                var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"),
                    Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                root = world.gameObject;
                root.name = "Runtime actor verification";
                Require(world.Session.Query("Primitive").Contains("Ground") && !world.Session.Query("Position").Contains("Sphere"),
                    "Startup contains the old presentation fixtures.");
                var clay = root.AddComponent<ClayCreation>(); clay.SaveAcceptedRecipes = false;
                var engine = root.AddComponent<DioramaRuntime>();
                engine.Initialize(world, clay);
                engine.ReadDevices = false;
                engine.AutoStep = false;
                Require(!clay.PresentationRoot, "An old photo creation was restored.");
                checks.Add("empty diorama Script produces ground and a real collider, with no automatic photo restore");

                var tools = EcatyAgentTools.Create(world, new AgentMainThread(), clay, engine);
                var apply = System.Linq.Enumerable.First(tools, t => t.Name == "apply_photo_object");
                var configure = System.Linq.Enumerable.First(tools, t => t.Name == "configure_actor");
                await apply.ExecuteAsync(new JObject { ["recipe"] = Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text }.ToString(), default);
                await configure.ExecuteAsync(new JObject { ["config"] = Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text }.ToString(), default);
                await Frames(8);
                Require(engine.Motor && engine.Legs && engine.Legs.LegsInitialized && engine.Secondary,
                    "The runtime motor or procedural plugins failed to initialize.");
                Require(world.Session.Query("ActorRig").Contains("PhotoClay"), "Bindings were not recorded in Flecs.");
                checks.Add("same tools used by chat build the MudBun creature and configure FImpossible plus Boing entirely in Play Mode");

                Require(engine.Legs.Legs[0].OppositeLegIndex == 1 && engine.Legs.Legs[1].OppositeLegIndex == 0,
                    "Runtime legs are not paired as opposites.");
                var gaitProbe = root.AddComponent<RuntimePoseProbe>();
                gaitProbe.Rig = engine.Legs;
                engine.SetDriver(0);
                pad = InputSystem.AddDevice<Gamepad>();
                InputSystem.QueueDeltaStateEvent(pad.leftStick, new Vector2(1, 0));
                engine.ReadDevices = true;
                await Frames(12);
                Require((int)JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"]["ActorControl"]["mode"] == 1,
                    "Gamepad input did not select the player driver.");
                Require(!chat.IsEditingText && !chat.Expanded, "Gamepad movement activated chat.");
                InputSystem.RemoveDevice(pad); pad = null;
                engine.SetDriver(0);
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(Key.D));
                await Frames(8);
                Require(!chat.IsEditingText && !chat.Expanded, "WASD activated chat.");
                Require((int)JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"]["ActorControl"]["mode"] == 1,
                    $"WASD did not activate the player driver. Test D pressed: {keyboard.dKey.isPressed}; test keyboard current: {Keyboard.current == keyboard}.");
                InputSystem.RemoveDevice(keyboard); keyboard = null;
                checks.Add("real Input System WASD/gamepad events leave the minimized chat unfocused");
                engine.ReadDevices = false;
                world.Session.RunScript("Input { PlayerMotion: {1,0} }");
                var start = engine.Motor.transform.position;
                int leftOnly = 0, rightOnly = 0, stanceSamples = 0;
                float contactError = 0;
                for (int i = 0; i < 140; i++)
                {
                    engine.StepMotor(1f/60); await Frames(1);
                    if (i < 30) continue;
                    bool leftSwing = gaitProbe.LeftPhase > .01f && gaitProbe.LeftPhase < .98f;
                    bool rightSwing = gaitProbe.RightPhase > .01f && gaitProbe.RightPhase < .98f;
                    if (leftSwing && !rightSwing) leftOnly++;
                    if (rightSwing && !leftSwing) rightOnly++;
                    if (gaitProbe.LeftPlanted || gaitProbe.RightPlanted) { stanceSamples++; contactError += gaitProbe.ContactError; }
                }
                report["gait"] = new JObject { ["leftOnlyFrames"] = leftOnly, ["rightOnlyFrames"] = rightOnly,
                    ["stanceSamples"] = stanceSamples, ["meanGroundContactError"] = contactError / Mathf.Max(1, stanceSamples) };
                Require(leftOnly > 2 && rightOnly > 2, "Feet did not take independent alternating steps.");
                Require(stanceSamples > 10 && contactError / stanceSamples < .08f, "Planted feet did not track the ground.");
                float facing = Vector3.Dot(clay.PresentationRoot.TransformDirection(Vector3.back), Vector3.right);
                Require(facing > .9f, "The sample front does not face its movement.");
                report["sampleFrontDotMovement"] = facing;
                checks.Add("runtime facing correction, opposite-leg steps and measured planted-foot ground contact");
                Require(engine.Motor.transform.position.x > start.x + 1, "Player input did not move the actual motor.");
                Require(Mathf.Abs(engine.Motor.transform.position.y) < .1f, "The actor fell through the diorama.");
                checks.Add("Input System gamepad selects control; runtime Flecs input moves the same grounded motor");

                wall = new GameObject("Verification wall");
                wall.transform.position = engine.Motor.transform.position + new Vector3(1.5f, 1, 0);
                var collider = wall.AddComponent<BoxCollider>();
                collider.size = new Vector3(.3f, 2, 4);
                Physics.SyncTransforms();
                for (int i = 0; i < 90; i++) { engine.StepMotor(1f/60); await Frames(1); }
                Require(engine.Motor.transform.position.x < wall.transform.position.x - .2f, "The motor passed through a wall.");
                var state = JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"]["ActorMotion"];
                Require(Mathf.Abs((float)state["x"] - engine.Motor.transform.position.x) < .001f, "Actual motion was not returned to Flecs.");
                checks.Add("collision stops the same player motor; actual pose and velocity return to queryable ECS data");
                Object.DestroyImmediate(wall); wall = null;

                var foot = clay.FindPart("left_foot");
                var ear = clay.FindPart("ear_tip");
                var probe = root.AddComponent<RuntimePoseProbe>();
                probe.Body = engine.Motor.transform; probe.Foot = foot; probe.Ear = ear;
                await Frames(2);
                var firstFoot = probe.FootLocal;
                var firstEar = probe.EarRotation;
                var firstEarLocal = probe.EarLocal;
                float earDisplacement = 0;
                float footChange = 0, earChange = 0;
                engine.SetGoal(new Vector3(-3, 0, 3));
                for (int i = 0; i < 150; i++)
                {
                    engine.StepMotor(1f/60);
                    await Frames(1);
                    footChange = Mathf.Max(footChange, Vector3.Distance(firstFoot, probe.FootLocal));
                    earChange = Mathf.Max(earChange, Quaternion.Angle(firstEar, probe.EarRotation));
                    earDisplacement = Mathf.Max(earDisplacement, Vector3.Distance(firstEarLocal, probe.EarLocal));
                }
                Require(engine.Motor.transform.position.z > 1, "The reactive follow driver did not steer the motor.");
                report["footMotion"] = footChange; report["earRotationDegrees"] = earChange; report["earDisplacement"] = earDisplacement;
                report["legDiagnostics"] = new JObject { ["blend"] = engine.Legs._MainBlend, ["grounded"] = engine.Legs.IsGrounded,
                    ["rayHit"] = engine.Legs.Legs[0].RaycastHitted, ["scale"] = engine.Legs.ScaleReference };
                Require(footChange > .02f, "FImpossible did not articulate a foot relative to the moving body.");
                Require(earChange > .05f || earDisplacement > .005f, "Boing did not articulate the secondary branch.");
                report["footMotion"] = footChange; report["earRotationDegrees"] = earChange; report["earDisplacement"] = earDisplacement;
                checks.Add("reactive Script follow driver steers the motor while procedural legs and a separate Boing chain keep animating");
                engine.SetDriver(0);
                await Frames(6);
                var before = engine.Motor.transform.position;
                for (int i = 0; i < 45; i++) engine.StepMotor(1f/60);
                Require(Vector3.Distance(before, engine.Motor.transform.position) < .4f, "Stopping did not clear the driver's movement request.");

                // New behavior source writes the same public motor protocol.
                engine.SetDriver(3);
                world.Session.SetManagedScript("CustomMotion", "ScriptInput { MoveIntent: {0,-1} }");
                float beforeZ = engine.Motor.transform.position.z;
                for (int i = 0; i < 50; i++) { engine.StepMotor(1f/60); await Frames(1); }
                Require(engine.Motor.transform.position.z < beforeZ - .4f, "Custom runtime Script could not drive the same motor.");
                checks.Add("idle and custom Script modes share the control protocol with no added C++ system");

                var pose = engine.Motor.transform.position;
                var recipe = JObject.Parse(clay.Inspect());
                recipe["parts"][2]["color"] = new JArray(.8,.4,.2);
                clay.Apply(recipe.ToString(), null);
                await Frames(8);
                Require(engine.Legs.LegsInitialized && Vector3.Distance(pose, engine.Motor.transform.position) < .5f,
                    "Editing the runtime model broke the configured motor or animation bindings.");
                checks.Add("editing a generated model rebinds animation to its live pivot hierarchy");
                var rig = JObject.Parse((string)JObject.Parse(world.Session.EntityJson("PhotoClay"))["components"]["ActorRig"]["json"]);
                rig["boing"] = new JArray();
                string encoded = Newtonsoft.Json.JsonConvert.SerializeObject(rig.ToString(Newtonsoft.Json.Formatting.None)).Replace("{", "\\{").Replace("$", "\\$");
                world.Session.RunScript("PhotoClay { ActorRig: {" + encoded + "} }");
                await Frames(8);
                Require(!engine.Secondary && engine.Legs.LegsInitialized, "A direct Flecs rig edit did not rebind the runtime plugins.");
                checks.Add("direct Flecs ActorRig edits rebuild bindings without a new chat call or compiled code");
                Require(runtimeErrors.Count == 0, "Runtime plugin errors: " + string.Join("; ", runtimeErrors.GetRange(0, Math.Min(3, runtimeErrors.Count))));
                report["passed"] = true;
                report["limits"] = new JArray("Windows Editor; synthetic input and supplied recipes, no live AI call",
                    "one generated actor; ground motor only; follow steers directly without pathfinding",
                    "phone, vehicle motor, Unity Animation Rigging and full experience persistence remain separate");
            }
            catch (Exception error) { report["error"] = error.ToString(); Debug.LogException(error); }
            finally
            {
                if (pad != null) InputSystem.RemoveDevice(pad);
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (wall) Object.DestroyImmediate(wall);
                if (root) Object.DestroyImmediate(root);
                Application.logMessageReceived -= capture;
                report["runtimeErrors"] = JArray.FromObject(runtimeErrors);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Verification/runtime-actor-checks.json")), report.ToString());
                Running = false;
                if (startPlay) EditorApplication.isPlaying = false;
            }
        }
    }
}
