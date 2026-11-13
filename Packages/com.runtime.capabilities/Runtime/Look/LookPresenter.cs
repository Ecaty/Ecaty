using System;
using System.Collections.Generic;
using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Shows every entity's Look as a shared material on the renderer of its view. Nothing runs when no Look changed. The
    /// native side already copies a kind's Look to its instances, so this only reacts to component changes on entities.
    /// </summary>
    public sealed class LookPresenter : IDisposable
    {
        const string Query = "Look";
        const string OwnerPath = "core.look";
        // Entities whose view or renderer is not there yet are tried again now and then, not every frame.
        const int RetryTicks = 15;

        enum Phase { Waiting, Shown, Skipped }

        sealed class Tracked
        {
            public LookKey Key;
            public Phase Phase;
            public int Seen;
            public bool Owned;
            public Renderer Renderer;
            public Material Original;
            public Material Shown;
        }

        readonly FlecsWorld world;
        readonly LookMaterials materials;
        readonly bool ownsMaterials;
        readonly Dictionary<ulong, Tracked> tracked = new();
        readonly List<ulong> scratch = new();
        readonly List<ulong> waiting = new();
        FlecsSession session;
        int handle, generation, ticks, lastRetry, viewCount;
        ulong owner;
        ulong[] entities = new ulong[64], targets = new ulong[64];
        LookValues[] rows = new LookValues[64];

        public LookMaterials Materials => materials;
        public int TrackedCount => tracked.Count;

        public LookPresenter(FlecsWorld world) : this(world, new LookMaterials(), true)
        {
        }

        public LookPresenter(FlecsWorld world, LookMaterials materials, bool ownsMaterials = false)
        {
            this.world = world;
            this.materials = materials;
            this.ownsMaterials = ownsMaterials;
        }

        /// <summary>Call once per frame after the world has synced its views.</summary>
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
                owner = session.Lookup(OwnerPath);
                // A native library without the look module has nothing to show.
                if (owner == 0)
                    return;
                // Checks the mirror against the native layout (throws on a mismatch).
                session.TryRead<LookValues>(owner, "Look", out _);
                handle = session.QueryHandle(Query);
            }
            if (handle == 0)
                return;
            ticks++;
            bool changed = session.QueryChanged(handle);
            if (changed)
                Read();
            // The first pass of a change may find a view or renderer missing; look again when the views grew or after a while.
            if (waiting.Count > 0 && (changed || world.ViewCount != viewCount || ticks - lastRetry >= RetryTicks))
                Retry();
        }

        void Read()
        {
            int count = session.ReadQuery(Query, null, ref entities, ref targets, ref rows);
            generation++;
            waiting.Clear();
            for (int i = 0; i < count; i++)
            {
                ulong entity = entities[i];
                var key = materials.KeyOf(rows[i]);
                if (!tracked.TryGetValue(entity, out var item))
                    tracked.Add(entity, item = new Tracked { Key = key, Phase = Phase.Waiting });
                else if (item.Key == key && item.Phase == Phase.Shown && item.Renderer)
                {
                    item.Seen = generation;
                    continue;
                }
                else if (item.Key != key || item.Phase == Phase.Waiting)
                {
                    item.Key = key;
                    if (item.Phase == Phase.Skipped)
                        item.Phase = Phase.Waiting;
                }
                item.Seen = generation;
                if (!Show(entity, item))
                    waiting.Add(entity);
            }
            // What was not in the rows lost its Look or no longer exists.
            scratch.Clear();
            foreach (var pair in tracked)
                if (pair.Value.Seen != generation)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                Remove(scratch[i]);
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        void Retry()
        {
            for (int i = waiting.Count - 1; i >= 0; i--)
                if (!tracked.TryGetValue(waiting[i], out var item) || Show(waiting[i], item))
                    waiting.RemoveAt(i);
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        // True when the entity is settled (shown or deliberately skipped); false when its view or renderer is not there yet.
        bool Show(ulong entity, Tracked item)
        {
            var view = world.FindView(entity);
            if (!view)
                return false;
            var renderer = RendererOf(view);
            if (!renderer)
                return false;
            if (!item.Owned)
            {
                if (!world.AcquireAppearance(entity, owner))
                {
                    item.Phase = Phase.Skipped;
                    return true;
                }
                item.Owned = true;
            }
            // The view's color arrives as a property block; it would tint the look.
            if (renderer != item.Renderer)
            {
                item.Renderer = renderer;
                item.Original = renderer.sharedMaterial;
                renderer.SetPropertyBlock(null);
            }
            var next = materials.Acquire(item.Key);
            var previous = item.Shown;
            item.Shown = next;
            renderer.sharedMaterial = next;
            if (previous)
                materials.Release(previous);
            item.Phase = Phase.Shown;
            return true;
        }

        // The view's own renderer: on the view object, or on its "Visual" child (the primitive). Child entities are not touched.
        static Renderer RendererOf(GameObject view)
        {
            if (view.TryGetComponent<Renderer>(out var own))
                return own;
            var visual = view.transform.Find("Visual");
            return visual && visual.TryGetComponent<Renderer>(out var child) ? child : null;
        }

        void Remove(ulong entity)
        {
            if (!tracked.TryGetValue(entity, out var item))
                return;
            tracked.Remove(entity);
            if (item.Renderer && item.Original)
                item.Renderer.sharedMaterial = item.Original;
            if (item.Owned && session != null && !session.IsDisposed)
                world.ReleaseAppearance(entity, owner);
            if (item.Shown)
                materials.Release(item.Shown);
        }

        // The session was replaced or disposed: the views are gone with it, so only the cache is given back.
        void Forget()
        {
            foreach (var item in tracked.Values)
            {
                if (item.Renderer && item.Original)
                    item.Renderer.sharedMaterial = item.Original;
                if (item.Shown)
                    materials.Release(item.Shown);
            }
            tracked.Clear();
            waiting.Clear();
            session = null;
            handle = 0;
            owner = 0;
        }

        public void Dispose()
        {
            if (session != null && !session.IsDisposed)
            {
                scratch.Clear();
                scratch.AddRange(tracked.Keys);
                for (int i = 0; i < scratch.Count; i++)
                    Remove(scratch[i]);
            }
            Forget();
            if (ownsMaterials)
                materials.Dispose();
        }
    }
}
