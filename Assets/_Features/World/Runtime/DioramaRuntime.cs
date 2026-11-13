using System;
using System.Linq;
using Runtime.Agents.UI;
using Runtime.Flecs;
using FIMSpace;
using FIMSpace.FProceduralAnimation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    // Input and motor run before procedural pose updates. Flecs retains commands and observations.
    [DefaultExecutionOrder(-100)]
    public sealed class DioramaRuntime : MonoBehaviour
    {
        private FlecsWorld world;
        private ClayCreation creation;
        private GameObject body;
        private Transform poseRoot;
        private CharacterController motor;
        private BoxCollider ground;
        private LegsAnimator legs;
        private LeaningAnimator leaning;
        private BoingKit.BoingBones secondary;
        private JObject configuration;
        private string appliedRig;
        private Vector3 velocity, publishedPosition;
        private AgentChatPanel chat;
        private Vector2 groundPressPoint;
        private bool groundClickPending;
        public string EntityPath => creation ? creation.EntityPath : ClayCreation.RootName;
        public string InputPath => EntityPath == ClayCreation.RootName ? "Input" : EntityPath + "Input";
        public string GoalPath => EntityPath == ClayCreation.RootName ? "Goal" : EntityPath + "Goal";
        public string CommandPath => EntityPath == ClayCreation.RootName ? "ScriptInput" : EntityPath + "Command";
        private string DriverModule => EntityPath == ClayCreation.RootName ? "ActorDriver" : "Drivers." + EntityPath;
        private struct ControlData
        {
            public int Mode; public float Speed, Acceleration;
        }
        private struct Direction2
        {
            public float X, Z;
        }
        private struct Direction3
        {
            public float X, Y, Z;
        }
        private struct PolicyData
        {
            public byte Spatial; public float Gravity;
        }
        private struct MotionData
        {
            public float X, Y, Z, Yaw, Vx, Vy, Vz; public byte Grounded;
        }
        private readonly ulong[] singleId = new ulong[1];
        private readonly ControlData[] controlWrite = new ControlData[1];
        private readonly Direction2[] inputWrite = new Direction2[1];
        private readonly float[] liftWrite = new float[1];
        private readonly MotionData[] motionWrite = new MotionData[1];
        public string Error
        {
            get; private set;
        }
        public CharacterController Motor => motor;
        public LegsAnimator Legs => legs;
        public BoingKit.BoingBones Secondary => secondary;
        public bool ReadDevices { get; set; } = true;
        public bool AutoStep { get; set; } = true;
        public Camera InteractionCamera
        {
            get; set;
        }
        public string Inspect()
        {
            var result = new JObject { ["configuration"] = configuration?.DeepClone(), ["error"] = Error, ["poseError"] = GetComponent<RuntimePoseBridge>()?.Error };
            if (creation && creation.PresentationRoot)
                result["components"] = JObject.Parse(world.Session.EntityJson(EntityPath))["components"];
            return result.ToString(Formatting.None);
        }

        public string Capabilities() => world.Session.Lookup("EngineCapability") != 0 ? world.Session.Query("EngineCapability, BoundCapability") : LegacyCapabilities();
        private string LegacyCapabilities() => new JObject
        {
            ["version"] = 1,
            ["motion"] = new JObject
            {
                ["settings"] = "PhotoClay.MotorPolicy{spatial:bool,gravityScale:0..4}; ActorControl{mode,speed,acceleration}",
                ["input"] = "mode 3: ScriptInput.SpatialIntent{x,y,z} for spatial movement, ScriptInput.MoveIntent{x,z} for ground movement; normalized direction, speed in metres/second",
                ["solver"] = "one collision-resolving CharacterController; controlled 3D velocity, not an aerodynamic solver",
                ["observed"] = "PhotoClay.ActorMotion actual motor pose/velocity/grounding"
            },
            ["pose"] = new JObject
            {
                ["command"] = "PoseOffset{x,y,z,rx,ry,rz} on a behavior-owned command entity linked with (PoseTarget, existingViewEntity). Additive local translation/Euler degrees AFTER procedural animation. Removing/replacing the owning behavior removes its command entities and restores the base pose; multiple command channels sum per target",
                ["observed"] = "PoseObserved world position/Euler/lossy scale and local position, offsetApplied and Unity frame; sampled after MudBun's update",
                ["ownership"] = "ClayRest is authored rest data, not a live animation command. Model structure/style use apply_photo_object. Visual offsets do not move the motor collider."
            },
            ["limits"] = new JArray("one generated actor", "ground slab bounds in x/z", "no aerodynamics, vehicle dynamics, pathfinding or full session checkpoint"),
            ["poseError"] = GetComponent<RuntimePoseBridge>()?.Error
        }.ToString(Formatting.None);

        public void Initialize(FlecsWorld owner, ClayCreation model)
        {
            world = owner;
            creation = model;
            if (world.Session.ManagedScriptSource("ActorSchema") == null)
                world.Session.SetManagedScript("ActorSchema", Resources.Load<TextAsset>("Runtime/Schemas/ActorSchema").text);
            creation.Rebuilding -= SuspendAnimations;
            creation.Rebuilt -= OnModelRebuilt;
            creation.Rebuilding += SuspendAnimations;
            creation.Rebuilt += OnModelRebuilt;
            SyncGround();
        }

        public void SyncGround()
        {
            if (world == null || world.Session == null || world.Session.IsDisposed)
                return;
            world.SyncViews();
            // Runs every frame: re-check only when the ground entity or the sources changed (no per-view names or JSON).
            ulong id = world.Session.Lookup("diorama.Ground");
            if (id == groundEntity && world.Session.SourceRevision == groundRevision && (ground || !groundTagged))
                return;
            groundEntity = id;
            groundRevision = world.Session.SourceRevision;
            groundTagged = false;
            ground = null;
            if (id == 0)
                return;
            var data = JObject.Parse(world.Session.EntityJson("diorama.Ground"));
            groundTagged = (data["tags"] as JArray)?.Any(t => (string)t == "GroundSurface") ?? false;
            var view = groundTagged ? world.FindView(id) : null;
            if (!view)
                return;
            ground = view.GetComponent<BoxCollider>();
            if (!ground)
                ground = view.AddComponent<BoxCollider>();
            ground.size = Vector3.one;
        }
        ulong groundEntity;
        int groundRevision = -1;
        bool groundTagged;

        public string Configure(string json)
        {
            if (!creation.PresentationRoot)
                throw new ArgumentException("Create a model before configuring its control.");
            var patch = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            string[] allowed = { "driver", "speed", "acceleration", "hips", "legs", "lean", "boing", "spawn", "facingYaw", "motion", "gravityScale" };
            if (patch.Properties().Any(p => !allowed.Contains(p.Name)))
                throw new ArgumentException("Unknown actor configuration field.");
            var next = configuration == null
                ? JObject.Parse(@"{""driver"":""player"",""speed"":2,""acceleration"":8,""hips"":null,""legs"":[],""lean"":null,""boing"":[],""spawn"":[0,0,0],""facingYaw"":0,""motion"":""ground"",""gravityScale"":1}")
                : (JObject)configuration.DeepClone();
            foreach (var property in patch.Properties())
                next[property.Name] = property.Value.DeepClone();
            string driver = (string)next["driver"];
            int mode = Array.IndexOf(new[] { "idle", "player", "follow", "script" }, driver);
            if (mode < 0)
                throw new ArgumentException("Driver must be idle, player, follow or script.");
            if (next["motion"] == null)
                next["motion"] = "ground";
            if (next["gravityScale"] == null)
                next["gravityScale"] = 1;
            if ((string)next["motion"] != "ground" && (string)next["motion"] != "spatial")
                throw new ArgumentException("motion must be ground or spatial.");
            ValidateNumber(next["gravityScale"], 0, 4, "gravityScale");
            if (next["facingYaw"] == null)
                next["facingYaw"] = 0;
            ValidateNumber(next["facingYaw"], -180, 180, "facingYaw");
            ValidateNumber(next["speed"], .1, 8, "speed");
            ValidateNumber(next["acceleration"], .1, 40, "acceleration");
            if (!(next["spawn"] is JArray spawn) || spawn.Count != 3)
                throw new ArgumentException("spawn needs x, y and z.");
            foreach (var coordinate in spawn)
                ValidateNumber(coordinate, -6, 6, "spawn");
            if (!(next["legs"] is JArray chains) || chains.Count > 12 || !(next["boing"] is JArray roots) || roots.Count > 8)
                throw new ArgumentException("Use up to 12 legs and 8 secondary chains.");
            Transform hips = OptionalPivot(next["hips"]);
            Transform spine = OptionalPivot(next["lean"]);
            if (spine && (!hips || spine == hips || !spine.IsChildOf(hips)))
                throw new ArgumentException("The leaning pivot must be a descendant of the hips.");
            foreach (var chain in chains)
            {
                if (!(chain is JArray ids) || ids.Count != 3 || !hips)
                    throw new ArgumentException("Each leg needs three pivot IDs and a hips pivot.");
                var upper = Pivot(ids[0]);
                var knee = Pivot(ids[1]);
                var foot = Pivot(ids[2]);
                if (upper == hips || knee == upper || foot == knee || !upper.IsChildOf(hips) ||
                    !knee.IsChildOf(upper) || !foot.IsChildOf(knee) ||
                    Vector3.Distance(upper.position, knee.position) < .05f || Vector3.Distance(knee.position, foot.position) < .05f)
                    throw new ArgumentException("Leg pivots must form a nonzero hips > upper > knee > foot chain.");
            }
            foreach (var id in roots)
            {
                var branch = Pivot(id);
                if (hips && (branch == hips || hips.IsChildOf(branch)))
                    throw new ArgumentException("Secondary motion needs a separate branch from the hips.");
                foreach (var chain in chains)
                    foreach (var legId in chain)
                    {
                        var pivot = Pivot(legId);
                        if (branch == pivot || branch.IsChildOf(pivot) || pivot.IsChildOf(branch))
                            throw new ArgumentException("Secondary motion cannot own a leg chain.");
                    }
            }

            bool rebuild = configuration == null || !JToken.DeepEquals(configuration["hips"], next["hips"]) ||
                !JToken.DeepEquals(configuration["legs"], next["legs"]) || !JToken.DeepEquals(configuration["lean"], next["lean"]) ||
                !JToken.DeepEquals(configuration["boing"], next["boing"]) ||
                !JToken.DeepEquals(configuration["facingYaw"], next["facingYaw"]);
            GetComponent<RuntimePoseBridge>()?.Restore();
            EnsureMotor(next);
            if (rebuild)
                BindAnimation(next, hips, spine);
            configuration = next;
            appliedRig = next.ToString(Formatting.None);
            var state = new JObject
            {
                ["x"] = body.transform.position.x,
                ["y"] = body.transform.position.y,
                ["z"] = body.transform.position.z,
                ["yaw"] = body.transform.eulerAngles.y
            };
            var script = EntityPath + " {\nActorControl: {" + mode + "," + Number((float)next["speed"]) + "," + Number((float)next["acceleration"]) + "}\n" +
                "MotorPolicy: {" + ((string)next["motion"] == "spatial" ? "true" : "false") + "," + Number((float)next["gravityScale"]) + "}\n" +
                "ActorRig: {" + JsonConvert.SerializeObject(next.ToString(Formatting.None)).Replace("{", "\\{").Replace("$", "\\$") + "}\n";
            script += "ActorMotion: {" + Number(body.transform.position.x) + "," + Number(body.transform.position.y) + "," +
                    Number(body.transform.position.z) + ",0,0,0,0,false}\nMoveIntent: {0,0}\nSpatialIntent: {0,0,0}\n";

            if (world.Session.Lookup(InputPath) == 0 || EntityPath != ClayCreation.RootName && world.Session.Lookup(CommandPath) == 0)
                world.Session.SetManagedScript("Inputs." + EntityPath, InputPath + " { PlayerMotion: {0,0}; LiftIntent: {0} }\n" + GoalPath +
                    " { MoveGoal: {3,2,0.25} }\n" + CommandPath + " { MoveIntent: {0,0}; SpatialIntent: {0,0,0} }");
            world.Session.SetManagedScript("ActorSetup" + EntityPath, script + "}\n");
            if (world.Session.ManagedScriptSource("MotorLibrary") == null)
                world.Session.SetManagedScript("MotorLibrary", Resources.Load<TextAsset>("Runtime/Behaviors/ActorBehavior").text);
            if (world.Session.ManagedScriptSource(DriverModule) == null)
                world.Session.SetManagedScript(DriverModule, EntityPath + " { RuntimeMotorDriver: {" + InputPath + "," + GoalPath + "," + CommandPath + "} }");
            Error = null;
            return new JObject
            {
                ["configured"] = true,
                ["driver"] = driver,
                ["legs"] = chains.Count,
                ["secondaryChains"] = roots.Count,
                ["state"] = state
            }.ToString(Formatting.None);
        }

        private void EnsureMotor(JObject next)
        {
            if (!body)
            {
                body = new GameObject("Actor body");
                body.layer = 2; // Keep the body's own capsule out of ground and pointer rays.
                body.transform.SetParent(transform, false);
                body.transform.position = new Vector3((float)next["spawn"][0], Mathf.Max(0, (float)next["spawn"][1]), (float)next["spawn"][2]);
                motor = body.AddComponent<CharacterController>();
                poseRoot = new GameObject("Procedural pose").transform;
                poseRoot.SetParent(body.transform, false);
            }
            var bounds = creation.LocalBounds();
            motor.enabled = false;
            motor.radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * .4f, .15f, .65f);
            motor.height = Mathf.Max(motor.radius * 2, bounds.size.y);
            motor.center = new Vector3(0, motor.height * .5f + .02f, 0);
            motor.skinWidth = .025f;
            motor.stepOffset = Mathf.Min(.25f, motor.height * .15f);
            motor.minMoveDistance = 0;
            poseRoot.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            creation.PresentationRoot.SetParent(poseRoot, false);
            creation.PresentationRoot.localRotation = Quaternion.Euler(0, (float)next["facingYaw"], 0);
            creation.PresentationRoot.localPosition = new Vector3(0, -bounds.min.y + .02f, 0);
            motor.enabled = true;
        }

        private void BindAnimation(JObject next, Transform hips, Transform spine)
        {
            SuspendAnimations();
            if (legs)
                DestroyImmediate(legs);
            if (leaning)
                DestroyImmediate(leaning);
            if (secondary)
                DestroyImmediate(secondary);
            var visual = creation.PresentationRoot.gameObject;
            visual.SetActive(false);
            try
            {
                if (((JArray)next["legs"]).Count > 0)
                {
                    legs = visual.AddComponent<LegsAnimator>();
                    legs.Hips = hips;
                    legs.Initialize_BaseTransform(body.transform);
                    legs.GroundMask = 1; // Default-layer ground, excluding the motor capsule.
                    // With no walking clips, use the plugin's procedural replanting mode.
                    legs.GlueMode = LegsAnimator.EGlueMode.Idle;
                    legs.LegAnimatingSettings.RefreshDefaultCurves();
                    foreach (JArray chain in next["legs"])
                    {
                        legs.Legs_AddNewLeg();
                        var leg = legs.Legs[legs.Legs.Count - 1];
                        leg.BoneStart = Pivot(chain[0]);
                        leg.BoneMid = Pivot(chain[1]);
                        leg.BoneEnd = Pivot(chain[2]);
                    }
                    foreach (var leg in legs.Legs)
                        leg.DefineLegSide(legs);
                    legs.Finder_AutoDefineOppositeLegs();
                    legs.Finders_RefreshAllLegsAnkleAxes();
                }
                if (spine)
                {
                    leaning = visual.AddComponent<LeaningAnimator>();
                    leaning.Parameters.BaseTransform = body.transform;
                    leaning.Parameters.SpineStart = spine;
                    leaning.Parameters.FootsOrigin = poseRoot;
                }
                if (((JArray)next["boing"]).Count > 0)
                {
                    secondary = visual.AddComponent<BoingKit.BoingBones>();
                    var geometry = JObject.Parse(creation.Inspect())["parts"].Where(p => (string)p["shape"] != "pivot")
                        .Select(p => creation.FindPart((string)p["id"])).Where(t => t).ToArray();
                    secondary.BoneChains = next["boing"].Select(id => new BoingKit.BoingBones.Chain
                    {
                        Root = Pivot(id),
                        Exclusion = geometry,
                        LooseRoot = false
                    }).ToArray();
                    secondary.EnablePositionEffect = true;
                    secondary.EnableRotationEffect = true;
                }
            }
            finally { visual.SetActive(true); }
        }

        private Transform OptionalPivot(JToken id) => id == null || id.Type == JTokenType.Null ? null : Pivot(id);
        private Transform Pivot(JToken id)
        {
            if (id?.Type != JTokenType.String)
                throw new ArgumentException("A pivot reference must be an ID.");
            var part = JObject.Parse(creation.Inspect())["parts"].FirstOrDefault(p => (string)p["id"] == (string)id);
            if ((string)part?["shape"] != "pivot" || !creation.FindPart((string)id))
                throw new ArgumentException("Missing pivot: " + (string)id);
            return creation.FindPart((string)id);
        }
        private static void ValidateNumber(JToken value, double min, double max, string field)
        {
            if ((value?.Type != JTokenType.Integer && value?.Type != JTokenType.Float) || !((double)value >= min && (double)value <= max))
                throw new ArgumentException(field + " is outside its supported range.");
        }
        private static string Number(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        public void SetDriver(int mode)
        {
            if (mode < 0 || mode > 3 || !motor)
                return;
            singleId[0] = world.Session.Lookup(EntityPath);
            if (!world.Session.TryRead(singleId[0], "ActorControl", out ControlData control) || control.Mode == mode)
                return;
            control.Mode = mode;
            controlWrite[0] = control;
            world.Session.WriteBatch("ActorControl", singleId, controlWrite, 1);
            configuration["driver"] = new[] { "idle", "player", "follow", "script" }[mode];
        }
        public void SetGoal(Vector3 point)
        {
            world.Session.RunScript(GoalPath + " { MoveGoal: {" + Number(point.x) + "," + Number(point.z) + ",0.25} }");
            SetDriver(2);
        }

        // Changes from chat tools, Explorer or new runtime rules use the same binding path.
        private void RefreshRig()
        {
            if (!creation.PresentationRoot)
                return;
            string rigJson = world.Session.ComponentJson(world.Session.Lookup(EntityPath), "ActorRig");
            string rig = rigJson == null ? null : (string)JObject.Parse(rigJson)["json"];
            if (string.IsNullOrEmpty(rig) || rig == appliedRig)
                return;
            appliedRig = rig; // Reject a bad edit once; retry only when its data changes.
            try
            {
                var next = JObject.Parse(rig);
                var components = JObject.Parse(world.Session.EntityJson(EntityPath))["components"];
                var control = components["ActorControl"];
                if (control != null)
                {
                    int mode = (int)control["mode"];
                    if (mode >= 0 && mode <= 3)
                        next["driver"] = new[] { "idle", "player", "follow", "script" }[mode];
                    next["speed"] = control["speed"].DeepClone();
                    next["acceleration"] = control["acceleration"].DeepClone();
                    var policy = components["MotorPolicy"];
                    if (policy != null)
                    {
                        next["motion"] = (bool)policy["spatial"] ? "spatial" : "ground";
                        next["gravityScale"] = policy["gravityScale"].DeepClone();
                    }
                }
                Configure(next.ToString(Formatting.None));
            }
            catch (Exception error) { Error = error.Message; Debug.LogWarning("Actor binding: " + Error); }
        }

        private void Update()
        {
            if (world == null || world.Session == null || world.Session.IsDisposed)
                return;
            SyncGround();
            if (!creation.PresentationRoot)
            {
                if (motor)
                    ResetActor();
                return;
            }
            RefreshRig();
            if (motor && world.Session.Lookup("CapDrag") != 0)
            {
                var request = world.Session.ComponentJson(world.Session.Lookup(creation.EntityPath), "CapDrag");
                if (!string.IsNullOrEmpty(request))
                {
                    var drag = JObject.Parse(request);
                    if ((bool?)drag["active"] == true)
                    {
                        RestoreMotorPose(new Vector3((float)drag["x"], (float)drag["y"], (float)drag["z"]), motor.transform.rotation);
                        return;
                    }
                }
            }
            if (!motor || configuration == null)
                return;
            if (ReadDevices)
                CaptureInput();
            if (AutoStep)
                StepMotor(Mathf.Min(Time.deltaTime, .05f));
        }

        private void CaptureInput()
        {
            if (!chat)
                chat = FindAnyObjectByType<AgentChatPanel>();
            var input = Vector2.zero;
            float lift = 0;
            if (!chat || !chat.IsEditingText)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    lift = (keyboard.spaceKey.isPressed ? 1 : 0) - (keyboard.leftCtrlKey.isPressed ? 1 : 0);
                    input = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                        (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                }
                if (Gamepad.current != null && Gamepad.current.leftStick.ReadValue().sqrMagnitude > input.sqrMagnitude)
                    input = Gamepad.current.leftStick.ReadValue();
                if (Gamepad.current != null && Mathf.Abs(Gamepad.current.rightTrigger.ReadValue() - Gamepad.current.leftTrigger.ReadValue()) > Mathf.Abs(lift))
                    lift = Gamepad.current.rightTrigger.ReadValue() - Gamepad.current.leftTrigger.ReadValue();
            }
            input = Vector2.ClampMagnitude(input, 1);
            if (input.sqrMagnitude > .01f || Mathf.Abs(lift) > .1f)
                SetDriver(1);
            singleId[0] = world.Session.Lookup(InputPath);
            inputWrite[0] = new Direction2 { X = input.x, Z = input.y };
            liftWrite[0] = lift;
            world.Session.WriteBatch("PlayerMotion", singleId, inputWrite, 1);
            world.Session.WriteBatch("LiftIntent", singleId, liftWrite, 1);
            var experience = world.GetComponent<RuntimeExperience>();
            if (experience && experience.Navigation)
            {
                groundClickPending = false;
                return; // The workshop uses short clicks for camera focus.
            }
            var pointer = Pointer.current;
            var camera = InteractionCamera ? InteractionCamera : Camera.main;
            if (pointer == null || !camera)
            {
                groundClickPending = false;
                return;
            }
            Vector2 screen = pointer.position.ReadValue();
            bool claimed = experience && (experience.WorkshopOwnsPointer || experience.Navigation && experience.Navigation.IsNavigating);
            var ray = camera.ScreenPointToRay(screen);
            if (pointer.press.wasPressedThisFrame)
            {
                groundPressPoint = screen;
                groundClickPending = !claimed && (!chat || !chat.CoversScreenPoint(screen)) &&
                    (!experience || !experience.HitsWorldObject(ray));
            }
            if (claimed || (screen - groundPressPoint).sqrMagnitude > 81)
                groundClickPending = false;
            if (pointer.press.wasReleasedThisFrame)
            {
                if (groundClickPending && (!chat || !chat.CoversScreenPoint(screen)) &&
                    Physics.Raycast(ray, out var hit, 100, 1) && hit.collider == ground)
                    SetGoal(hit.point);
                groundClickPending = false;
            }
        }

        public void StepMotor(float delta)
        {
            if (!motor || !ground || delta <= 0)
                return;
            ulong entity = world.Session.Lookup(EntityPath);
            if (!world.Session.TryRead(entity, "ActorControl", out ControlData control) || !world.Session.TryRead(entity, "MoveIntent", out Direction2 intent))
                return;
            var policy = new PolicyData { Gravity = 1 };
            if (world.Session.TryRead(entity, "MotorPolicy", out PolicyData currentPolicy))
                policy = currentPolicy;
            bool spatial = policy.Spatial != 0;
            float gravityScale = Mathf.Clamp(policy.Gravity, 0, 4);
            var direction = new Vector3(intent.X, 0, intent.Z);
            if (spatial && world.Session.TryRead(entity, "SpatialIntent", out Direction3 space))
                direction = new Vector3(space.X, space.Y, space.Z);
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || !float.IsFinite(direction.z))
                direction = Vector3.zero;
            direction = Vector3.ClampMagnitude(direction, 1);
            float speed = Mathf.Clamp(control.Speed, 0, 8);
            float acceleration = Mathf.Clamp(control.Acceleration, .1f, 40);
            if (!float.IsFinite(speed) || !float.IsFinite(acceleration) || !float.IsFinite(gravityScale))
                return;
            if (spatial)
            {
                velocity = Vector3.MoveTowards(velocity, direction * speed, acceleration * delta);
                velocity.y = Mathf.Max(-20, velocity.y + Physics.gravity.y * gravityScale * delta);
            }
            else
                velocity = Vector3.MoveTowards(new Vector3(velocity.x, 0, velocity.z), direction * speed, acceleration * delta) +
                    Vector3.up * (motor.isGrounded ? -1f : Mathf.Max(-20, velocity.y + Physics.gravity.y * gravityScale * delta));
            var before = body.transform.position;
            var movement = velocity * delta;
            var limits = ground.bounds;
            movement.x = Mathf.Clamp(before.x + movement.x, limits.min.x + motor.radius, limits.max.x - motor.radius) - before.x;
            movement.z = Mathf.Clamp(before.z + movement.z, limits.min.z + motor.radius, limits.max.z - motor.radius) - before.z;
            motor.Move(movement);
            var actual = (body.transform.position - before) / delta;
            var planar = new Vector3(actual.x, 0, actual.z);
            if (planar.sqrMagnitude > .001f)
                body.transform.rotation = Quaternion.RotateTowards(body.transform.rotation, Quaternion.LookRotation(planar), 360 * delta);
            if (legs)
            {
                legs.User_SetIsGrounded(motor.isGrounded);
                legs.User_SetIsMoving(planar.sqrMagnitude > .01f);
                legs.User_SetDesiredMovementDirection(planar.normalized);
            }
            publishedPosition = body.transform.position;
            var p = publishedPosition;
            singleId[0] = entity;
            motionWrite[0] = new MotionData
            {
                X = p.x,
                Y = p.y,
                Z = p.z,
                Yaw = body.transform.eulerAngles.y,
                Vx = actual.x,
                Vy = actual.y,
                Vz = actual.z,
                Grounded = motor.isGrounded ? (byte)1 : (byte)0
            };
            world.Session.WriteBatch("ActorMotion", singleId, motionWrite, 1);
        }

        public void RestoreMotorPose(Vector3 position, Quaternion rotation)
        {
            if (!motor)
                return;
            motor.enabled = false;
            body.transform.SetPositionAndRotation(position, rotation);
            motor.enabled = true;
            velocity = Vector3.zero;
        }
        public void ResetActor()
        {
            SuspendAnimations();
            if (body)
            {
                body.SetActive(false);
                Destroy(body);
            }
            body = null;
            motor = null;
            poseRoot = null;
            legs = null;
            leaning = null;
            secondary = null;
            configuration = null;
            appliedRig = null;
            velocity = Vector3.zero;
        }
        private void SuspendAnimations()
        {
            if (legs)
                legs.enabled = false;
            if (leaning)
                leaning.enabled = false;
            if (secondary)
                secondary.enabled = false;
        }
        private void OnModelRebuilt()
        {
            if (configuration == null)
                return;
            try
            {
                var saved = (JObject)configuration.DeepClone();
                configuration = null;
                Configure(saved.ToString(Formatting.None));
            }
            catch (Exception error) { Error = error.Message; configuration = null; SuspendAnimations(); Debug.LogWarning("Actor rebind: " + Error); }
        }
        private void OnDestroy()
        {
            if (creation)
            {
                creation.Rebuilding -= SuspendAnimations;
                creation.Rebuilt -= OnModelRebuilt;
            }
            if (body)
                Destroy(body);
        }
    }
}
