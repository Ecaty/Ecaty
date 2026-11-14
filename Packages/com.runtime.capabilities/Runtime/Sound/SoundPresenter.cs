using System;
using System.Collections.Generic;
using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Gives every element with Sound an AudioSource on its view and plays the rendered sound when its Played count goes up.
    /// The render (clip shaped by size and rough) comes from SoundRenders; the samples come from SoundSources. Nothing runs
    /// when no query reports a change and no render is waiting. A missing clip or source is skipped with a reason.
    /// </summary>
    public sealed class SoundPresenter : IDisposable
    {
        const string SoundPath = "Sound";
        const string PlayedPath = "Played";
        const string ClipPath = "SoundClip";
        const int RetryTicks = 15;

        [Serializable]
        sealed class ClipJson
        {
            public string uri;
        }

        sealed class Item
        {
            public GameObject View;
            public AudioSource Source;
            public bool Added, Dirty = true, Settled, Wanted, Unresolved;
            public SoundValues Values;
            public SoundRender Render;
            public int Seen;
        }

        struct Mark
        {
            public int Count, Seen;
        }

        readonly FlecsWorld world;
        readonly SoundSources sources;
        readonly SoundRenders renders = new();
        readonly Dictionary<ulong, Item> items = new();
        readonly Dictionary<ulong, Mark> played = new();
        readonly List<ulong> scratch = new();
        FlecsSession session;
        int soundHandle, playedHandle, clipHandle;
        int generation, playedGeneration, ticks, lastRetry, viewCount, sourceVersion, waiting;
        bool pending;
        string lastSkip;
        ulong[] entities = new ulong[64], targets = new ulong[64];
        SoundValues[] rows = new SoundValues[64];
        ulong[] playedEntities = new ulong[64], playedTargets = new ulong[64];
        PlayedValues[] playedRows = new PlayedValues[64];

        public SoundPresenter(FlecsWorld world, SoundSources sources)
        {
            this.world = world;
            this.sources = sources;
            sourceVersion = sources.Version;
        }

        public SoundRenders Renders => renders;
        /// <summary>Elements with Sound that the presenter holds.</summary>
        public int Held => items.Count;
        /// <summary>Plays started: an AudioSource got its clip and Play was called.</summary>
        public int Plays
        {
            get; private set;
        }
        public string LastSkip => lastSkip;

        /// <summary>The AudioSource of an element, or null.</summary>
        public AudioSource SourceOf(ulong entity) => items.TryGetValue(entity, out var item) && item.Source ? item.Source : null;

        /// <summary>The rendered clip of an element, or null while it is not done.</summary>
        public AudioClip ClipOf(ulong entity) => items.TryGetValue(entity, out var item) && item.Render != null ? item.Render.Clip : null;

        public string Diagnostics => "sound{held=" + items.Count + ", renders cached=" + renders.Count + " (idle " + renders.IdleCount + "), started="
            + renders.Started + ", finished=" + renders.Finished + ", plays=" + Plays + ", last skip=" + (lastSkip ?? "none") + "}";

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
                // A native library without Sound has no sounds.
                if (session.Lookup(SoundPath) == 0)
                {
                    lastSkip = "no Sound component";
                    return;
                }
                soundHandle = session.QueryHandle(SoundPath);
                clipHandle = session.Lookup(ClipPath) != 0 ? session.QueryHandle(ClipPath) : 0;
                playedHandle = session.Lookup(PlayedPath) != 0 ? session.QueryHandle(PlayedPath) : 0;
            }
            if (soundHandle == 0)
                return;
            ticks++;
            int finished = renders.Tick();
            bool sounds = session.QueryChanged(soundHandle);
            bool clips = clipHandle != 0 && session.QueryChanged(clipHandle);
            bool plays = playedHandle != 0 && session.QueryChanged(playedHandle);
            bool known = sources.Version != sourceVersion;
            if (sounds)
                Reconcile();
            if (clips || known)
            {
                sourceVersion = sources.Version;
                foreach (var item in items.Values)
                    if (clips || item.Unresolved)
                        item.Dirty = true;
            }
            if (plays)
                ReadPlayed();
            bool retry = pending && (world.ViewCount != viewCount || ticks - lastRetry >= RetryTicks);
            bool ready = finished > 0 && waiting > 0;
            if (!sounds && !clips && !known && !plays && !retry && !ready)
                return;
            pending = false;
            waiting = 0;
            foreach (var pair in items)
                if (!Apply(pair.Key, pair.Value))
                    pending = true;
            lastRetry = ticks;
            viewCount = world.ViewCount;
        }

        void Reconcile()
        {
            int count = session.ReadQuery(SoundPath, null, ref entities, ref targets, ref rows);
            generation++;
            for (int i = 0; i < count; i++)
            {
                if (!items.TryGetValue(entities[i], out var item))
                {
                    items.Add(entities[i], item = new Item());
                    item.Values = rows[i];
                    item.Dirty = true;
                }
                else if (item.Values.clip != rows[i].clip || item.Values.size != rows[i].size || item.Values.rough != rows[i].rough)
                {
                    item.Values = rows[i];
                    item.Dirty = true;
                }
                item.Seen = generation;
            }
            scratch.Clear();
            foreach (var pair in items)
                if (pair.Value.Seen != generation)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                Remove(scratch[i]);
        }

        // A count that went up since the last read asks the element to play.
        void ReadPlayed()
        {
            int count = session.ReadQuery(PlayedPath, null, ref playedEntities, ref playedTargets, ref playedRows);
            playedGeneration++;
            for (int i = 0; i < count; i++)
            {
                played.TryGetValue(playedEntities[i], out var mark);
                if (playedRows[i].count > mark.Count && items.TryGetValue(playedEntities[i], out var item))
                    item.Wanted = true;
                played[playedEntities[i]] = new Mark { Count = playedRows[i].count, Seen = playedGeneration };
            }
            scratch.Clear();
            foreach (var pair in played)
                if (pair.Value.Seen != playedGeneration)
                    scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++)
                played.Remove(scratch[i]);
        }

        // False when the element's view is not there yet (tried again later).
        bool Apply(ulong entity, Item item)
        {
            if (item.Settled && item.Source && !item.Dirty && !item.Wanted)
                return true;
            var view = world.FindView(entity);
            if (!view)
            {
                lastSkip = "no view for " + entity;
                item.Settled = false;
                return false;
            }
            if (!item.Source || item.View != view)
                Build(view, item);
            item.Settled = true;
            if (item.Dirty)
            {
                item.Dirty = false;
                Resolve(item);
            }
            if (item.Wanted)
                Play(item);
            return true;
        }

        void Build(GameObject view, Item item)
        {
            item.View = view;
            item.Source = view.GetComponent<AudioSource>();
            item.Added = !item.Source;
            if (!item.Added)
                return;
            var source = view.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            item.Source = source;
        }

        // Finds the samples of the element's clip and asks for the render; the previous render is released after.
        void Resolve(Item item)
        {
            SoundRender next = null;
            string reason = null;
            if (item.Values.clip == 0)
                reason = "no clip entity";
            else
            {
                string json = null;
                try
                {
                    json = session.ComponentJson(item.Values.clip, ClipPath);
                }
                catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
                {
                    reason = "the clip cannot be read (" + e.GetType().Name + ")";
                }
                if (reason == null)
                {
                    string uri = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<ClipJson>(json)?.uri;
                    if (string.IsNullOrEmpty(uri))
                        reason = "the clip entity has no SoundClip uri";
                    else if (sources.TryGet(uri, out var samples, out int rate, out var why))
                        next = renders.Request(uri, samples, rate, item.Values.size, item.Values.rough);
                    else
                        reason = why;
                }
            }
            var old = item.Render;
            item.Render = next;
            if (old != null)
                renders.Release(old);
            item.Unresolved = next == null;
            if (reason != null)
                lastSkip = reason;
        }

        void Play(Item item)
        {
            var render = item.Render;
            if (render == null || render.Failed)
            {
                item.Wanted = false;
                lastSkip = render == null ? "nothing to play: no render for the element" : "nothing to play: the render failed";
                return;
            }
            if (!render.Ready)
            {
                waiting++;
                return;
            }
            item.Wanted = false;
            item.Source.clip = render.Clip;
            item.Source.Play();
            Plays++;
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
            if (item.Source)
            {
                item.Source.Stop();
                item.Source.clip = null;
                if (item.Added)
                    UnityEngine.Object.Destroy(item.Source);
            }
            item.Source = null;
            if (item.Render != null)
                renders.Release(item.Render);
            item.Render = null;
        }

        // The session was replaced or disposed: the views are gone with it.
        void Forget()
        {
            foreach (var item in items.Values)
                Release(item);
            items.Clear();
            played.Clear();
            session = null;
            soundHandle = playedHandle = clipHandle = 0;
            pending = false;
            waiting = 0;
        }

        public void Dispose()
        {
            Forget();
            renders.Dispose();
        }
    }
}
