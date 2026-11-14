using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runtime.Flecs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Runtime.Capabilities
{
    /// <summary>One rendered sound: a clip shaped by size and rough. Owned and counted by SoundRenders.</summary>
    public sealed class SoundRender
    {
        // 0 running, 1 shaped (worker), 2 failed (worker), 3 clip made, 4 failed (seen by Tick).
        internal int State;
        internal int Refs, Rate;
        internal bool Discarded;
        internal float[] Input, Buffer;
        internal (string, int, int) Key;

        /// <summary>The clip, once the render is done; null before.</summary>
        public AudioClip Clip
        {
            get; internal set;
        }
        public bool Ready => State == 3 && Clip;
        public bool Failed => State == 4;
        public int References => Refs;
    }

    /// <summary>
    /// The cache of rendered sounds, keyed by (clip, size, rough) with the values quantised to 1/32. Shaping runs on a worker
    /// thread; the AudioClip is made on the main thread in Tick. Equal keys share one render. Renders nobody holds stay in a
    /// short idle list (the newest eight) so a value that returns is not rendered again.
    /// </summary>
    public sealed class SoundRenders : IDisposable
    {
        public const int MaxIdle = 8;
        public const int Steps = 32;

        readonly Dictionary<(string, int, int), SoundRender> cache = new();
        readonly List<SoundRender> idle = new();
        readonly List<SoundRender> pending = new();
        readonly int mainThread = Thread.CurrentThread.ManagedThreadId;
        int shapedOffMain, shapedOnMain;
        bool disposed;

        /// <summary>Renders in the cache, held or idle or still running.</summary>
        public int Count => cache.Count;
        /// <summary>Renders nobody holds, kept for reuse.</summary>
        public int IdleCount => idle.Count;
        public int PendingCount => pending.Count;
        public int Started
        {
            get; private set;
        }
        public int Finished
        {
            get; private set;
        }
        public int FailedCount
        {
            get; private set;
        }
        /// <summary>Shaping calls that ran on a thread other than the main thread.</summary>
        public int ShapedOffMainThread => Volatile.Read(ref shapedOffMain);
        public int ShapedOnMainThread => Volatile.Read(ref shapedOnMain);

        static int Quantise(float value) => Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * Steps);

        /// <summary>The render for these inputs: a held one, a reused idle one, or a new one that starts at once.</summary>
        public SoundRender Request(string clipUri, float[] samples, int sampleRate, float size, float rough)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SoundRenders));
            var key = (clipUri, Quantise(size), Quantise(rough));
            if (cache.TryGetValue(key, out var render))
            {
                // A uri registered again with other samples is a new sound once nobody holds the old one.
                if (render.Refs == 0 && !ReferenceEquals(render.Input, samples))
                    Destroy(render);
                else
                {
                    if (render.Refs == 0)
                        idle.Remove(render);
                    render.Refs++;
                    return render;
                }
            }
            render = new SoundRender { Key = key, Refs = 1, Input = samples };
            cache.Add(key, render);
            pending.Add(render);
            Started++;
            float shapeSize = key.Item2 / (float)Steps, shapeRough = key.Item3 / (float)Steps;
            Task.Run(() => Shape(render, samples, sampleRate, shapeSize, shapeRough));
            return render;
        }

        void Shape(SoundRender render, float[] input, int rate, float size, float rough)
        {
            float[] output = null;
            bool ok = false;
            try
            {
                output = new float[input.Length];
                ok = FlecsSession.ShapeSound(input, rate, size, rough, output);
            }
            catch (Exception)
            {
                ok = false;
            }
            if (Thread.CurrentThread.ManagedThreadId == mainThread)
                Interlocked.Increment(ref shapedOnMain);
            else
                Interlocked.Increment(ref shapedOffMain);
            render.Buffer = ok ? output : null;
            render.Rate = rate;
            Volatile.Write(ref render.State, ok ? 1 : 2);
        }

        /// <summary>Gives a render back. At zero it goes to the idle list; older idle renders beyond the limit are destroyed.</summary>
        public void Release(SoundRender render)
        {
            if (render == null || render.Discarded || render.Refs <= 0)
                return;
            if (--render.Refs > 0)
                return;
            idle.Add(render);
            while (idle.Count > MaxIdle)
            {
                var oldest = idle[0];
                idle.RemoveAt(0);
                Destroy(oldest);
            }
        }

        /// <summary>Makes the clips of the renders that finished. Returns how many finished. Cheap when nothing is running.</summary>
        public int Tick()
        {
            int done = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var render = pending[i];
                int state = Volatile.Read(ref render.State);
                if (state == 0)
                    continue;
                int last = pending.Count - 1;
                pending[i] = pending[last];
                pending.RemoveAt(last);
                if (render.Discarded)
                    continue;
                if (state == 1)
                {
                    var clip = AudioClip.Create("sound", render.Buffer.Length, 1, render.Rate, false);
                    clip.SetData(render.Buffer, 0);
                    render.Clip = clip;
                    render.State = 3;
                    Finished++;
                }
                else
                {
                    render.State = 4;
                    FailedCount++;
                }
                render.Buffer = null;
                done++;
            }
            return done;
        }

        void Destroy(SoundRender render)
        {
            cache.Remove(render.Key);
            idle.Remove(render);
            render.Discarded = true;
            render.Buffer = null;
            if (render.Clip)
                DestroyObject(render.Clip);
            render.Clip = null;
        }

        public string Diagnostics => "renders{cached=" + cache.Count + ", idle=" + idle.Count + ", running=" + pending.Count + ", started=" + Started
            + ", finished=" + Finished + ", failed=" + FailedCount + ", shaped off main=" + ShapedOffMainThread + ", on main=" + ShapedOnMainThread + "}";

        /// <summary>Destroys every clip. Renders still running are dropped when they finish.</summary>
        public void Dispose()
        {
            disposed = true;
            var all = new List<SoundRender>(cache.Values);
            foreach (var render in all)
                Destroy(render);
            idle.Clear();
            // A running render is discarded above; Tick would only drop it, so they can go now.
            pending.Clear();
        }

        // Play mode destroys at the end of the frame; Edit mode has no frame, and Destroy there logs an error and leaks.
        static void DestroyObject(Object value)
        {
            if (!value)
                return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }
    }
}
