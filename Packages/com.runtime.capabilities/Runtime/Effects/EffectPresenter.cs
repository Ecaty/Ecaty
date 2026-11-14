using System;
using System.Collections.Generic;
using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Shows every shape with Particles as one ParticleSystem on a child object of its view, never one object per particle. The
    /// shape is the spawn volume. The child prefab named particle gives every particle its Look (one shared material from the
    /// look cache) and its Scale. The forces come from the emitter's EffectiveForce. Particles end at pinned shapes (world
    /// collision, no dynamic colliders, all life lost) and inside the fluid volume the emitter is in (trigger kill). Nothing
    /// runs when no query reports a change.
    /// </summary>
    public sealed class EffectPresenter : IDisposable
    {
        const string ParticlesPath = "Particles";
        const string ForcePath = "EffectiveForce";
        const string ParticleName = ".particle";
        const string EmitterQuery = "Particles";
        const string ForceQuery = "EffectiveForce, Particles";
        // A prefab is skipped by ordinary queries, so these name it.
        const string LookQuery = "Look, Prefab";
        const string ScaleQuery = "Scale, Prefab";
        const int RetryTicks = 15;
        const float MinRate = 0f, MinLife = 0.05f;

        sealed class Item
        {
            public GameObject View, Object;
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
            public Material Material;
            public LookKey Key;
            public bool HasKey;
            public ulong Prefab;
            public ParticlesValues Values;
            public EffectiveForceValues Force;
            public bool HasForce, Dirty = true, Settled;
            public int Primitive = int.MinValue, MaxParticles;
            public Vector3 Size = Vector3.one, AppliedForce;
            public float AppliedDrag = -1f;
            public Collider Fluid;
            public bool WantsFluid;
            public int Seen;
        }

        readonly FlecsWorld world;
        readonly LookMaterials materials;
        readonly Dictionary<ulong, Item> items = new();
        readonly List<ulong> scratch = new();
        FlecsSession session;
        int emitterHandle, forceHandle, lookHandle, scaleHandle;
        int generation, ticks, lastRetry, viewCount, rowCount;
        bool pending;
        string lastSkip;
        ulong[] entities = new ulong[64], targets = new ulong[64];
        ParticlesValues[] rows = new ParticlesValues[64];

        public EffectPresenter(FlecsWorld world, LookMaterials materials)
        {
            this.world = world;
            this.materials = materials;
        }

        /// <summary>Emitters with a particle system now.</summary>
        public int EmitterCount
        {
            get
            {
                int count = 0;
                foreach (var item in items.Values)
                    if (item.System)
                        count++;
                return count;
            }
        }

        /// <summary>The particle system of an emitter, or null.</summary>
        public ParticleSystem SystemOf(ulong entity) => items.TryGetValue(entity, out var item) && item.System ? item.System : null;

        /// <summary>Read-only state for failure messages.</summary>
        public string Diagnostics
        {
            get
            {
                int built = 0, held = 0, waiting = 0;
                foreach (var item in items.Values)
                {
                    if (item.System)
                        built++;
                    if (item.Material)
                        held++;
                    if (!item.Settled)
                        waiting++;
                }
                return "effects{tracked=" + items.Count + ", built=" + built + ", materials held=" + held + " (cache " + materials.Count
                    + "), waiting=" + waiting + ", rows=" + rowCount + ", last skip=" + (lastSkip ?? "none") + "}";
            }
        }

        /// <summary>Call once per frame after the world has synced its views and the look presenter has run.</summary>
        public void Tick()
        {
            if (!world)
                return;
            var current = world.Session;
            if (current == null || current.IsDisposed)
            {
                if (session != null)
                    Forget();
                return;
            }
            if (!ReferenceEquals(current, session))
            {
                Forget();
                session = current;
                // A native library without Particles has no effects.
                if (session.Lookup(ParticlesPath) == 0)
                {
                    lastSkip = "no Particles component";
                    return;
                }
                emitterHandle = session.QueryHandle(EmitterQuery);
                forceHandle = session.QueryHandle(ForceQuery);
                lookHandle = session.QueryHandle(LookQuery);
                scaleHandle = session.QueryHandle(ScaleQuery);
            }
            if (emitterHandle == 0)
                return;
            ticks++;
            bool emitters = session.QueryChanged(emitterHandle);
            bool anyChange = session.QueryChanged(forceHandle);
            anyChange |= session.QueryChanged(lookHandle);
            anyChange |= session.QueryChanged(scaleHandle);
            if (emitters)
                Reconcile();
            if (emitters || anyChange)
                foreach (var item in items.Values)
                    item.Dirty = true;
            // A destroyed object or a lost fluid collider needs a rebuild; checked cheaply and only for what can go missing.
            bool rebuild = false;
            foreach (var item in items.Values)
                if (item.Settled && (!item.Object || (item.WantsFluid && !item.Fluid)))
                {
                    item.Dirty = true;
                    rebuild = true;
                }
            bool retry = pending && (world.ViewCount != viewCount || ticks - lastRetry >= RetryTicks);
            if (!emitters && !anyChange && !rebuild && !retry)
                return;
            pending = false;
            scratch.Clear();
            foreach (var pair in items)
                if (pair.Value.Dirty)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                if (!Apply(scratch[i], items[scratch[i]]))
                    pending = true;
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        void Reconcile()
        {
            rowCount = session.ReadQuery(EmitterQuery, null, ref entities, ref targets, ref rows);
            generation++;
            for (int i = 0; i < rowCount; i++)
            {
                if (!items.TryGetValue(entities[i], out var item))
                    items.Add(entities[i], item = new Item());
                item.Seen = generation;
                item.Values = rows[i];
            }
            scratch.Clear();
            foreach (var pair in items)
                if (pair.Value.Seen != generation)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                Remove(scratch[i]);
        }

        // False when the emitter's view is not there yet (tried again later).
        bool Apply(ulong entity, Item item)
        {
            item.Dirty = false;
            var view = world.FindView(entity);
            if (!view)
            {
                lastSkip = "no view for " + entity;
                item.Settled = false;
                item.Dirty = true;
                return false;
            }
            if (!item.Object || item.View != view)
                Build(view, item);
            item.Settled = true;
            var ps = item.System;
            var v = item.Values;
            int primitive = session.TryRead<PrimitiveValues>(entity, "Primitive", out var p) ? p.kind : -1;
            if (primitive != item.Primitive)
            {
                item.Primitive = primitive;
                var shape = ps.shape;
                shape.shapeType = primitive == 1 ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Box;
                shape.radius = 0.5f;
                shape.radiusThickness = 1f;
                shape.scale = Vector3.one;
            }
            float life = Mathf.Max(MinLife, v.life), rate = Mathf.Max(MinRate, v.rate);
            var main = ps.main;
            if (main.startLifetime.constant != life)
                main.startLifetime = life;
            int max = Mathf.Clamp(Mathf.CeilToInt(rate * life * 1.5f), 16, 20000);
            if (item.MaxParticles != max)
            {
                item.MaxParticles = max;
                main.maxParticles = max;
            }
            var emission = ps.emission;
            if (emission.rateOverTime.constant != rate)
                emission.rateOverTime = rate;
            ApplyParticle(entity, item, main);
            ApplyForce(entity, item, ps);
            return ApplyFluid(item, ps);
        }

        void Build(GameObject view, Item item)
        {
            if (item.Object)
                DestroyObject(item.Object);
            item.View = view;
            item.Object = new GameObject("Particles");
            item.Object.transform.SetParent(view.transform, false);
            item.System = item.Object.AddComponent<ParticleSystem>();
            item.Renderer = item.Object.GetComponent<ParticleSystemRenderer>();
            var ps = item.System;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // The view's scale sizes the spawn volume only, not the particles.
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startLifetime = -1f;
            item.MaxParticles = 0;
            item.Primitive = int.MinValue;
            item.AppliedForce = new Vector3(float.NaN, 0f, 0f);
            item.AppliedDrag = -1f;
            item.HasKey = false;
            item.Size = new Vector3(float.NaN, 0f, 0f);
            var shape = ps.shape;
            shape.enabled = true;
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = -1f;
            // Particles end at pinned shapes: static colliders only, all life lost, no bounce.
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.collidesWith = ~0;
            collision.enableDynamicColliders = false;
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            collision.dampen = 0f;
            collision.sendCollisionMessages = false;
            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            var trigger = ps.trigger;
            trigger.enabled = false;
            trigger.inside = ParticleSystemOverlapAction.Kill;
            trigger.outside = ParticleSystemOverlapAction.Ignore;
            trigger.enter = ParticleSystemOverlapAction.Ignore;
            trigger.exit = ParticleSystemOverlapAction.Ignore;
            var renderer = item.Renderer;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (item.Material)
                renderer.sharedMaterial = item.Material;
            ps.Play();
        }

        // The look and size of one particle come from the child prefab named particle.
        void ApplyParticle(ulong entity, Item item, ParticleSystem.MainModule main)
        {
            item.Prefab = session.Lookup(session.EntityPath(entity) + ParticleName);
            var look = LookValues.Default;
            var size = Vector3.one;
            if (item.Prefab != 0)
            {
                if (session.TryRead(item.Prefab, "Look", out LookValues l))
                    look = l;
                if (session.TryRead(item.Prefab, "Scale", out Float3 s))
                    size = s.ToVector();
            }
            if (size != item.Size)
            {
                item.Size = size;
                main.startSizeX = size.x;
                main.startSizeY = size.y;
                main.startSizeZ = size.z;
            }
            var key = materials.KeyOf(look);
            if (item.HasKey && item.Key == key && item.Material)
                return;
            var next = materials.Acquire(key);
            var previous = item.Material;
            item.Material = next;
            item.Key = key;
            item.HasKey = true;
            item.Renderer.sharedMaterial = next;
            if (previous)
                materials.Release(previous);
        }

        void ApplyForce(ulong entity, Item item, ParticleSystem ps)
        {
            item.HasForce = session.TryRead(entity, ForcePath, out EffectiveForceValues force);
            if (!item.HasForce)
                force = default;
            item.Force = force;
            var push = force.gravity.ToVector() + force.wind.ToVector();
            if (push != item.AppliedForce)
            {
                item.AppliedForce = push;
                var module = ps.forceOverLifetime;
                module.x = push.x;
                module.y = push.y;
                module.z = push.z;
            }
            float drag = Mathf.Max(0f, force.drag);
            if (drag != item.AppliedDrag)
            {
                item.AppliedDrag = drag;
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = drag > 0f;
                if (drag > 0f)
                {
                    // Only the drag acts: the speed limit is far out of reach and does not clamp.
                    limit.limit = 1e6f;
                    limit.dampen = 0f;
                    limit.drag = drag;
                    limit.multiplyDragByParticleSize = false;
                    limit.multiplyDragByParticleVelocity = true;
                }
            }
        }

        // In a fluid the particles die where they enter the volume's collider, which is the surface from above.
        bool ApplyFluid(Item item, ParticleSystem ps)
        {
            var force = item.Force;
            item.WantsFluid = item.HasForce && force.fluid > 0f && force.volume != 0;
            if (!item.WantsFluid)
            {
                item.Fluid = null;
                var off = ps.trigger;
                if (off.enabled)
                    off.enabled = false;
                return true;
            }
            var volume = world.FindView(force.volume);
            var collider = volume ? volume.GetComponent<Collider>() : null;
            if (!collider)
            {
                lastSkip = "fluid volume " + force.volume + " has no collider yet";
                item.Fluid = null;
                item.Settled = false;
                item.Dirty = true;
                return false;
            }
            var trigger = ps.trigger;
            if (!ReferenceEquals(item.Fluid, collider) || !trigger.enabled)
            {
                item.Fluid = collider;
                trigger.enabled = true;
                trigger.SetCollider(0, collider);
            }
            return true;
        }

        void Remove(ulong entity)
        {
            if (!items.TryGetValue(entity, out var item))
                return;
            items.Remove(entity);
            Release(item);
        }

        void Release(Item item)
        {
            if (item.Object)
                DestroyObject(item.Object);
            item.Object = null;
            item.System = null;
            if (item.Material)
                materials.Release(item.Material);
            item.Material = null;
        }

        // The session was replaced or disposed: the views are gone with it.
        void Forget()
        {
            foreach (var item in items.Values)
                Release(item);
            items.Clear();
            session = null;
            emitterHandle = forceHandle = lookHandle = scaleHandle = 0;
            pending = false;
        }

        public void Dispose() => Forget();

        // Play mode destroys at the end of the frame; Edit mode has no frame, and Destroy there logs an error and leaks.
        static void DestroyObject(UnityEngine.Object value)
        {
            if (!value)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(value);
            else
                UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
