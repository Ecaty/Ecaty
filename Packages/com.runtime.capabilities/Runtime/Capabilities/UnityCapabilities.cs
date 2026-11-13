using System;
using System.Collections.Generic;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Unity.Cinemachine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.Animations.Rigging;
using UnityEngine.VFX;
namespace Runtime.Capabilities
{
    public sealed class ContactReadback : MonoBehaviour
    {
        readonly HashSet<Collider> touching = new(); public int Count => touching.Count;
        void OnCollisionEnter(Collision c) => touching.Add(c.collider);
        void OnCollisionExit(Collision c) => touching.Remove(c.collider);
        void OnDisable() => touching.Clear();
    }
    // Plain-data mirrors of CapabilitySchema.flecs components, read and written without JSON.
    // struct CapDrag(x: f32, y: f32, z: f32, active: bool)
    struct CapDragData
    {
        public float x, y, z; public bool active;
    }
    // struct CapForce(x: f32, y: f32, z: f32, sequence: i32)
    struct CapForceData
    {
        public float x, y, z; public int sequence;
    }
    // struct CapReadback(x: f32, y: f32, z: f32, vx: f32, vy: f32, vz: f32, contacts: i32)
    struct CapReadbackData
    {
        public float x, y, z, vx, vy, vz; public int contacts;
    }
    // struct CapPath(x: f32, y: f32, z: f32, status: i32)
    struct CapPathData
    {
        public float x, y, z; public int status;
    }
    // struct CapRigResult(x: f32, y: f32, z: f32, error: f32)
    struct CapRigResultData
    {
        public float x, y, z, error;
    }
    // struct MoveIntent(x: f32, z: f32) in ActorSchema.flecs
    struct MoveIntentData
    {
        public float x, z;
    }
    static class Typed
    {
        // An older saved schema may lack a command component: that means no command, as the JSON read did. Writes still throw.
        public static bool TryReadOptional<T>(FlecsSession session, ulong entity, string type, out T value) where T : unmanaged
        {
            if (session.TypeId(type) == 0)
            {
                value = default;
                return false;
            }
            return session.TryRead(entity, type, out value);
        }
    }
    // One-element arrays for FlecsSession.WriteBatch, so a publish allocates nothing.
    sealed class Publisher<T> where T : unmanaged
    {
        readonly ulong[] entities = new ulong[1]; readonly T[] values = new T[1];
        public void Write(FlecsSession session, string type, ulong entity, T value)
        {
            entities[0] = entity;
            values[0] = value;
            session.WriteBatch(type, entities, values, 1);
        }
    }
    [Capability("CapBody", "physics", 20)]
    public sealed class BodyConfig
    {
        public float mass = 1, radius = .5f, height = 1, gravity = 1;
        public bool kinematic;
    }
    public sealed class BodyCapability : CapabilityInstance<BodyConfig>
    {
        Rigidbody body; CapsuleCollider collider; ContactReadback contacts; float gravity; int forceSequence = int.MinValue;
        bool drag, wasKinematic; float lastReport; readonly Publisher<CapReadbackData> readback = new();
        public BodyCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(BodyConfig j)
        {
            if (j.mass <= 0 || j.radius <= 0)
                throw new ArgumentException("Mass/radius must be positive.");
            if (!body)
            {
                if (Target.GetComponent<CharacterController>() || Target.GetComponent<Rigidbody>())
                    throw new InvalidOperationException("Target already has a motor/body; remove its binding before switching owner.");
                Context.OwnPose();
                body = Target.gameObject.AddComponent<Rigidbody>();
                collider = Target.gameObject.AddComponent<CapsuleCollider>();
                contacts = Target.gameObject.AddComponent<ContactReadback>();
            }
            body.mass = Mathf.Clamp(j.mass, .001f, 10000);
            body.useGravity = false;
            body.isKinematic = j.kinematic;
            collider.radius = Mathf.Clamp(j.radius, .01f, 100);
            collider.height = Mathf.Clamp(j.height, collider.radius * 2, 200);
            gravity = Mathf.Clamp(j.gravity, -20, 20);
        }
        public override void FixedTick(float delta)
        {
            if (!body)
                return;
            bool hasDrag = Typed.TryReadOptional(Context.Session, Context.TargetEntity, "CapDrag", out CapDragData d);
            bool active = hasDrag && d.active;
            if (active && !drag)
            {
                wasKinematic = body.isKinematic;
                body.isKinematic = true;
                drag = true;
            }
            if (active)
            {
                body.MovePosition(new Vector3(Fields.Finite(d.x, "x"), Fields.Finite(d.y, "y"), Fields.Finite(d.z, "z")));
                return;
            }
            if (drag)
            {
                body.isKinematic = wasKinematic;
                drag = false;
            }
            if (!body.isKinematic)
                body.AddForce(Physics.gravity * gravity, ForceMode.Acceleration);
            if (Typed.TryReadOptional(Context.Session, Context.Entity, "CapForce", out CapForceData f) && f.sequence != forceSequence)
            {
                forceSequence = f.sequence;
                if (!body.isKinematic)
                    body.AddForce(Vector3.ClampMagnitude(new Vector3(Fields.Finite(f.x, "x"), Fields.Finite(f.y, "y"), Fields.Finite(f.z, "z")), 10000), ForceMode.Impulse);
            }
            if (Time.unscaledTime - lastReport > .05f)
            {
                lastReport = Time.unscaledTime;
                var p = body.position;
                var v = body.linearVelocity;
                readback.Write(Context.Session, "CapReadback", Context.Entity, new CapReadbackData { x = p.x, y = p.y, z = p.z, vx = v.x, vy = v.y, vz = v.z, contacts = contacts.Count });
            }
        }
        public override JObject CaptureState()
        {
            if (!body)
                return null;
            return new JObject { ["position"] = new JArray(body.position.x, body.position.y, body.position.z), ["rotation"] = new JArray(body.rotation.x, body.rotation.y, body.rotation.z, body.rotation.w), ["velocity"] = new JArray(body.linearVelocity.x, body.linearVelocity.y, body.linearVelocity.z), ["angularVelocity"] = new JArray(body.angularVelocity.x, body.angularVelocity.y, body.angularVelocity.z) };
        }
        public override void RestoreState(JObject j)
        {
            var p = (JArray)j["position"];
            var q = (JArray)j["rotation"];
            var v = (JArray)j["velocity"];
            body.position = new Vector3((float)p[0], (float)p[1], (float)p[2]);
            body.rotation = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
            if (!body.isKinematic)
            {
                body.linearVelocity = new Vector3((float)v[0], (float)v[1], (float)v[2]);
                if (j["angularVelocity"] is JArray a)
                    body.angularVelocity = new Vector3((float)a[0], (float)a[1], (float)a[2]);
            }
        }
        public override void Dispose()
        {
            if (body)
                UnityEngine.Object.Destroy(body);
            if (collider)
                UnityEngine.Object.Destroy(collider);
            if (contacts)
                UnityEngine.Object.Destroy(contacts);
            Context.ReleasePose();
        }
    }
    [Capability("CapMaterial", "appearance", 40)]
    public sealed class MaterialConfig
    {
        public float colorR = 1, colorG = 1, colorB = 1, metallic, smoothness;
        [AssetKey] public string material = "";
    }
    public sealed class MaterialCapability : CapabilityInstance<MaterialConfig>
    {
        readonly List<(Renderer, Material, MaterialPropertyBlock)> originals = new();
        public MaterialCapability(CapabilityContext c) : base(c) { if (!c.Runtime.World.AcquireAppearance(c.TargetEntity, this)) throw new InvalidOperationException("Target already has an appearance owner."); }
        protected override void Apply(MaterialConfig j)
        {
            var color = new Color(j.colorR, j.colorG, j.colorB);
            foreach (var r in Target.GetComponentsInChildren<Renderer>())
            {
                if (!originals.Exists(x => x.Item1 == r))
                {
                    var old = new MaterialPropertyBlock();
                    r.GetPropertyBlock(old);
                    originals.Add((r, r.sharedMaterial, old));
                }
                var material = Context.Asset<Material>(j.material);
                if (material)
                    r.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                block.SetFloat("_Metallic", Mathf.Clamp01(j.metallic));
                block.SetFloat("_Smoothness", Mathf.Clamp01(j.smoothness));
                r.SetPropertyBlock(block);
            }
        }
        public override void Dispose()
        {
            Context.Runtime.World.ReleaseAppearance(Context.TargetEntity, this);
            foreach (var item in originals)
            if (item.Item1)
            {
                item.Item1.sharedMaterial = item.Item2;
                item.Item1.SetPropertyBlock(item.Item3);
            }
        }
    }
    [Capability("CapParticles", "effects", 40)]
    public sealed class ParticleConfig
    {
        public float rate = 10, lifetime = 1, speed = 1, size = .1f, colorR = 1, colorG = 1, colorB = 1;
    }
    public sealed class ParticleCapability : CapabilityInstance<ParticleConfig>
    {
        ParticleSystem particles;
        public ParticleCapability(CapabilityContext c) : base(c) { particles = Host.AddComponent<ParticleSystem>(); particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        protected override void Apply(ParticleConfig j)
        {
            var m = particles.main;
            m.maxParticles = 2048;
            m.startLifetime = Mathf.Clamp(j.lifetime, .01f, 60);
            m.startSpeed = j.speed;
            m.startSize = Mathf.Clamp(j.size, .001f, 10);
            m.startColor = new Color(j.colorR, j.colorG, j.colorB);
            var e = particles.emission;
            e.rateOverTime = Mathf.Clamp(j.rate, 0, 1000);
            particles.Play();
        }
    }
    [Capability("CapLight", "lighting", 40)]
    public sealed class LightConfig
    {
        public int kind;
        public float intensity = 1, range = 10, colorR = 1, colorG = 1, colorB = 1;
    }
    public sealed class LightCapability : CapabilityInstance<LightConfig>
    {
        readonly Light light; public LightCapability(CapabilityContext c) : base(c) { light = Host.AddComponent<Light>(); }
        protected override void Apply(LightConfig j)
        {
            light.type = j.kind == 1 ? LightType.Directional : j.kind == 2 ? LightType.Spot : LightType.Point;
            light.color = new Color(j.colorR, j.colorG, j.colorB);
            light.intensity = Mathf.Clamp(j.intensity, 0, 100);
            light.range = Mathf.Clamp(j.range, .01f, 1000);
        }
    }
    [Capability("CapNavSurface", "navigation surface", 30)]
    public sealed class SurfaceConfig
    {
        public float radius = 10, height = 10;
        public int rebuild;
    }
    public sealed class SurfaceCapability : CapabilityInstance<SurfaceConfig>
    {
        readonly NavMeshSurface surface;
        public SurfaceCapability(CapabilityContext c) : base(c) { surface = Host.AddComponent<NavMeshSurface>(); surface.collectObjects = CollectObjects.Volume; surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; }
        protected override void Apply(SurfaceConfig j)
        {
            float width = Mathf.Clamp(j.radius * 2, 1, 1000);
            surface.size = new Vector3(width, Mathf.Clamp(j.height, 1, 1000), width);
            // Each build creates a new NavMeshData asset; RemoveData only unregisters it from the NavMesh.
            var previous = surface.navMeshData;
            surface.BuildNavMesh();
            if (previous && previous != surface.navMeshData)
                UnityEngine.Object.Destroy(previous);
        }
        public override void Dispose()
        {
            if (!surface)
                return;
            surface.RemoveData();
            if (surface.navMeshData)
                UnityEngine.Object.Destroy(surface.navMeshData);
            surface.navMeshData = null;
        }
    }
    [Capability("CapNavigation", "navigation intent", 35)]
    public sealed class NavigationConfig
    {
        [EntityField] public string command = "";
        public float x, y, z, speed = 1, stopping = .1f;
    }
    public sealed class NavigationCapability : CapabilityInstance<NavigationConfig>
    {
        readonly NavMeshPath path = new(); Vector3 goal; float speed, stopping; ulong command; float next;
        readonly Vector3[] corners = new Vector3[2]; readonly Publisher<CapPathData> pathOut = new(); readonly Publisher<MoveIntentData> intentOut = new();
        public NavigationCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(NavigationConfig j)
        {
            goal = new Vector3(j.x, j.y, j.z);
            speed = Mathf.Clamp(j.speed, 0, 100);
            stopping = Mathf.Max(.01f, j.stopping);
            command = Context.ResolveEntity(j.command);
            next = 0;
        }
        public override void Tick(float delta)
        {
            if (Time.unscaledTime < next)
                return;
            next = Time.unscaledTime + .15f;
            int status = 0;
            Vector3 direction = Vector3.zero;
            if (NavMesh.SamplePosition(Target.position, out var start, 2, NavMesh.AllAreas) && NavMesh.CalculatePath(start.position, goal, NavMesh.AllAreas, path) && path.GetCornersNonAlloc(corners) > 1)
            {
                direction = corners[1] - Target.position;
                direction.y = 0;
                direction = Vector3.Distance(Target.position, goal) > stopping ? direction.normalized * speed : Vector3.zero;
                status = (int)path.status + 1;
            }
            pathOut.Write(Context.Session, "CapPath", Context.Entity, new CapPathData { x = direction.x, y = direction.y, z = direction.z, status = status });
            if (command != 0)
                intentOut.Write(Context.Session, "MoveIntent", command, new MoveIntentData { x = direction.x, z = direction.z });
        }
    }
    [Capability("CapCamera", "camera", 40)]
    public sealed class CameraConfig
    {
        public float distance = 8, height = 4;
        public int priority = 10;
    }
    public sealed class CameraCapability : CapabilityInstance<CameraConfig>
    {
        readonly CinemachineCamera camera; readonly CinemachineFollow follow;
        public CameraCapability(CapabilityContext c) : base(c) { camera = Host.AddComponent<CinemachineCamera>(); follow = Host.AddComponent<CinemachineFollow>(); Host.AddComponent<CinemachineRotationComposer>(); camera.Follow = Target; camera.LookAt = Target; }
        protected override void Apply(CameraConfig j)
        {
            follow.FollowOffset = new Vector3(0, j.height, -Mathf.Max(.1f, j.distance));
            camera.Priority = j.priority;
        }
    }
    [Capability("CapTimeline", "presentation", 40)]
    public sealed class TimelineConfig
    {
        public float duration = 1;
        public bool playing;
        public float time;
        [AssetKey] public string audio = "";
    }
    public sealed class TimelineCapability : CapabilityInstance<TimelineConfig>
    {
        readonly PlayableDirector director; TimelineAsset timeline;
        public TimelineCapability(CapabilityContext c) : base(c) { director = Host.AddComponent<PlayableDirector>(); director.playOnAwake = false; }
        // Tracks and clip assets are separate ScriptableObjects that do not die with the timeline.
        void DestroyTimeline()
        {
            if (!timeline)
                return;
            foreach (var track in timeline.GetOutputTracks())
            {
                foreach (var clip in track.GetClips())
                    if (clip.asset)
                        UnityEngine.Object.Destroy(clip.asset);
                UnityEngine.Object.Destroy(track);
            }
            UnityEngine.Object.Destroy(timeline);
            timeline = null;
        }
        protected override void Apply(TimelineConfig j)
        {
            director.Stop();
            DestroyTimeline();
            timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = Mathf.Clamp(j.duration, .01f, 3600);
            var clip = Context.Asset<AudioClip>(j.audio);
            if (clip)
            {
                var track = timeline.CreateTrack<AudioTrack>(null, "Audio");
                var tc = track.CreateClip<AudioPlayableAsset>();
                ((AudioPlayableAsset)tc.asset).clip = clip;
                tc.duration = Math.Min(clip.length, timeline.fixedDuration);
                var source = Host.GetComponent<AudioSource>() ?? Host.AddComponent<AudioSource>();
                director.SetGenericBinding(track, source);
            }
            director.playableAsset = timeline;
            director.time = Math.Clamp(j.time, 0, timeline.fixedDuration);
            director.Evaluate();
            if (j.playing)
                director.Play();
        }
        public override void Dispose()
        {
            director.Stop();
            DestroyTimeline();
        }
    }
    [Capability("CapRig", "IK constraints", 40)]
    public sealed class RigConfig
    {
        [EntityField] public string root = "", mid = "", tip = "", goal = "";
        public float weight = 1;
    }
    public sealed class RigCapability : CapabilityInstance<RigConfig>
    {
        RigBuilder builder; Rig rig; TwoBoneIKConstraint constraint; bool ownBuilder, ownAnimator; float nextReport; readonly Publisher<CapRigResultData> result = new();
        public RigCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(RigConfig j)
        {
            Transform root = Context.Resolve(j.root);
            Transform mid = Context.Resolve(j.mid);
            Transform tip = Context.Resolve(j.tip);
            Transform goal = Context.Resolve(j.goal);
            if (!root || !mid || !tip || !goal || !mid.IsChildOf(root) || !tip.IsChildOf(mid))
                throw new ArgumentException("IK needs a valid root/mid/tip chain and target.");
            var animator = Target.GetComponent<Animator>();
            if (!animator)
            {
                animator = Target.gameObject.AddComponent<Animator>();
                ownAnimator = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (!builder)
            {
                builder = Target.GetComponent<RigBuilder>();
                if (!builder)
                {
                    builder = Target.gameObject.AddComponent<RigBuilder>();
                    ownBuilder = true;
                }
                rig = Host.AddComponent<Rig>();
                var go = new GameObject("Two bone constraint");
                go.transform.SetParent(Host.transform, false);
                constraint = go.AddComponent<TwoBoneIKConstraint>();
                builder.layers.Add(new RigLayer(rig));
            }
            builder.Clear();
            var data = constraint.data;
            data.root = root;
            data.mid = mid;
            data.tip = tip;
            data.target = goal;
            data.targetPositionWeight = 1;
            data.targetRotationWeight = 0;
            constraint.data = data;
            constraint.weight = Mathf.Clamp01(j.weight);
            if (!builder.Build())
                throw new InvalidOperationException("Runtime rig graph could not build.");
        }
        public override void LateTick(float delta)
        {
            if (Time.unscaledTime < nextReport || !constraint)
                return;
            nextReport = Time.unscaledTime + .05f;
            var data = constraint.data;
            var p = data.tip.position;
            result.Write(Context.Session, "CapRigResult", Context.Entity, new CapRigResultData { x = p.x, y = p.y, z = p.z, error = Vector3.Distance(p, data.target.position) });
        }
        public override void Dispose()
        {
            if (builder)
            {
                builder.Clear();
                builder.layers.RemoveAll(l => l.rig == rig);
                if (ownBuilder)
                    UnityEngine.Object.Destroy(builder);
                else
                    builder.Build();
            }
            if (ownAnimator)
                UnityEngine.Object.Destroy(Target.GetComponent<Animator>());
        }
    }
    [Capability("CapVfx", "GPU effects", 40)]
    public sealed class VfxConfig
    {
        [AssetKey] public string asset = "";
        public float rate = 10;
        public string @event = "";
        public int trigger;
    }
    public sealed class VfxCapability : CapabilityInstance<VfxConfig>
    {
        readonly VisualEffect vfx; int trigger = int.MinValue;
        public VfxCapability(CapabilityContext c) : base(c) { vfx = Host.AddComponent<VisualEffect>(); }
        protected override void Apply(VfxConfig j)
        {
            var asset = Context.Asset<VisualEffectAsset>(j.asset);
            if (!asset)
                throw new ArgumentException("Register a compiled VFX asset in the capability asset catalog.");
            vfx.visualEffectAsset = asset;
            if (vfx.HasFloat("Rate"))
                vfx.SetFloat("Rate", j.rate);
            if (j.trigger != trigger)
            {
                trigger = j.trigger;
                vfx.SendEvent(string.IsNullOrEmpty(j.@event) ? "OnPlay" : j.@event);
            }
        }
    }
    public sealed class WaterVolume : MonoBehaviour
    {
        public float Level, Density = 1000;
    }
    [Capability("CapWater", "water volume", 20)]
    public sealed class WaterConfig
    {
        public float level, density = 1000;
    }
    public sealed class WaterCapability : CapabilityInstance<WaterConfig>
    {
        readonly WaterVolume water; public WaterCapability(CapabilityContext c) : base(c) { water = Host.AddComponent<WaterVolume>(); }
        protected override void Apply(WaterConfig j)
        {
            water.Level = j.level;
            water.Density = Mathf.Clamp(j.density, 1, 20000);
        }
    }
    [Capability("CapBuoyancy", "buoyancy", 25)]
    public sealed class BuoyancyConfig
    {
        [EntityField] public string volume = "";
        public float displacement = .001f, drag = 1;
    }
    public sealed class BuoyancyCapability : CapabilityInstance<BuoyancyConfig>
    {
        WaterVolume water; Rigidbody body; float displacement, drag;
        public BuoyancyCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(BuoyancyConfig j)
        {
            var volume = Context.Resolve(j.volume);
            water = volume ? volume.GetComponentInChildren<WaterVolume>() : null;
            body = Target.GetComponent<Rigidbody>();
            if (!water || !body)
                throw new ArgumentException("Buoyancy needs a bound body and water volume.");
            displacement = Mathf.Clamp(j.displacement, 0, 100);
            drag = Mathf.Clamp(j.drag, 0, 100);
        }
        public override void FixedTick(float delta)
        {
            if (body.isKinematic)
                return;
            float submerged = Mathf.Clamp01(water.Level - body.worldCenterOfMass.y + .5f);
            body.AddForce(-Physics.gravity * water.Density * displacement * submerged - body.linearVelocity * drag * submerged, ForceMode.Force);
        }
    }
}
