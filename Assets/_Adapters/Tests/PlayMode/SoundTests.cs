using System;
using System.Collections;
using Runtime.Flecs;
using Runtime.Capabilities;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    // The sound adapter against a real native world: one AudioSource per element, a render shared by equal values and made
    // off the main thread, the effect of size and rough on the samples, and a play for every rise of the Played count.
    public sealed class SoundTests
    {
        const int Rate = 44100;
        const string Clips = "  tone { SoundClip: {from: 1, uri: \"tone\", seconds: 0.5} }\n  noise { SoundClip: {from: 1, uri: \"noise\", seconds: 0.5} }\n";
        const string Room = "snd { Position: {0, 0, 0}\n";

        static string Element(string name, int x, string clip, float size, float rough) =>
            "  " + name + " { Position: {" + x + ", 0, 0}; Scale: {1, 1, 1}; Primitive: {1}; Played: {count: 0}; Sound: {clip: " + clip + ", size: " + size.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", rough: " + rough.ToString(System.Globalization.CultureInfo.InvariantCulture) + "} }\n";

        static float[] Tone()
        {
            var samples = new float[Rate / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.6f * Mathf.Sin(2f * Mathf.PI * 440f * i / Rate);
            return samples;
        }

        static float[] Noise()
        {
            var samples = new float[Rate / 2];
            uint seed = 7;
            for (int i = 0; i < samples.Length; i++)
            {
                seed = seed * 1664525u + 1013904223u;
                samples[i] = 0.5f * ((seed >> 8) / 8388608f - 1f);
            }
            return samples;
        }

        static float[] Data(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            return data;
        }

        // Upward zero crossings in the middle half.
        static int Crossings(float[] samples)
        {
            int count = 0;
            for (int i = samples.Length / 4 + 1; i < samples.Length * 3 / 4; i++)
                if (samples[i - 1] <= 0f && samples[i] > 0f)
                    count++;
            return count;
        }

        static float Change(float[] samples)
        {
            float sum = 0f;
            for (int i = 1; i < samples.Length; i++)
                sum += Mathf.Abs(samples[i] - samples[i - 1]);
            return sum;
        }

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public SoundSources Sources = new SoundSources();
            public SoundPresenter Sounds;
            public bool TimedOut;

            public Scene(string script)
            {
                Sources.Register("tone", Tone(), Rate);
                Sources.Register("noise", Noise(), Rate);
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Session.SetManagedScript("Sounds", script);
                Session.Tick(0.02f);
                World.SyncViews();
            }

            // Call it a frame after the scene exists.
            public void Begin()
            {
                Sounds = new SoundPresenter(World, Sources);
                Settle();
            }

            public void Settle()
            {
                for (int i = 0; i < 4; i++)
                    Step();
            }

            void Step()
            {
                Session.Tick(0.02f);
                World.SyncViews();
                Sounds?.Tick();
            }

            // Runs frames until the condition holds or ten seconds pass (TimedOut).
            public IEnumerator Until(Func<bool> done)
            {
                float end = Time.realtimeSinceStartup + 10f;
                TimedOut = false;
                while (!done())
                {
                    if (Time.realtimeSinceStartup > end)
                    {
                        TimedOut = true;
                        yield break;
                    }
                    yield return null;
                    Step();
                }
            }

            public ulong Id(string path) => Session.Lookup(path);
            public string Info => Sounds?.Diagnostics + " " + Sounds?.Renders.Diagnostics;
            public AudioClip Clip(string path) => Sounds.ClipOf(Id(path));

            public void SetSound(string path, float size, float rough)
            {
                ulong id = Id(path);
                Assert.That(Session.TryRead(id, "Sound", out SoundValues v), Is.True, "the element has Sound");
                v.size = size;
                v.rough = rough;
                Session.WriteBatch("Sound", new[] { id }, new[] { v }, 1);
            }

            public void SetPlayed(string path, int count) => Session.SetComponentJson(path, "Played", "{\"count\":" + count + "}");

            public IEnumerator AllClips(params string[] paths)
            {
                yield return Until(() =>
                {
                    foreach (var path in paths)
                        if (!Sounds.ClipOf(Id(path)))
                            return false;
                    return true;
                });
            }

            public void Dispose()
            {
                Sounds?.Dispose();
                if (World)
                    Object.Destroy(World.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AnElementWithSoundGetsASourceAndARenderedClipMadeOffTheMainThread()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "tone", 0, 0) + "}\n");
            yield return null;
            scene.Begin();
            var view = scene.World.FindView(scene.Id("snd.a"));
            Assert.That(view, Is.Not.Null, scene.Info);
            var source = view.GetComponent<AudioSource>();
            Assert.That(source, Is.Not.Null, scene.Info);
            Assert.That(source.playOnAwake, Is.False, scene.Info);
            Assert.That(source.loop, Is.False, scene.Info);
            Assert.That(source.spatialBlend, Is.EqualTo(1f), scene.Info);
            Assert.That(scene.Sounds.SourceOf(scene.Id("snd.a")), Is.SameAs(source), scene.Info);
            yield return scene.AllClips("snd.a");
            Assert.That(scene.TimedOut, Is.False, "the render finishes: " + scene.Info);
            var clip = scene.Clip("snd.a");
            Assert.That(clip.samples, Is.EqualTo(Rate / 2), scene.Info);
            Assert.That(clip.frequency, Is.EqualTo(Rate), scene.Info);
            Assert.That(clip.channels, Is.EqualTo(1), scene.Info);
            Assert.That(scene.Sounds.Renders.ShapedOffMainThread, Is.EqualTo(1), "shaping ran on a worker: " + scene.Info);
            Assert.That(scene.Sounds.Renders.ShapedOnMainThread, Is.EqualTo(0), scene.Info);
            Assert.That(scene.Sounds.Plays, Is.EqualTo(0), "nothing plays by itself: " + scene.Info);
        }

        [UnityTest]
        public IEnumerator EqualValuesShareOneRenderAndAnotherSizeGetsItsOwn()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "tone", 0.5f, 0) + Element("b", 3, "tone", 0.5f, 0) + Element("c", 6, "tone", -0.5f, 0) + "}\n");
            yield return null;
            scene.Begin();
            yield return scene.AllClips("snd.a", "snd.b", "snd.c");
            Assert.That(scene.TimedOut, Is.False, scene.Info);
            Assert.That(scene.Clip("snd.b"), Is.SameAs(scene.Clip("snd.a")), "one clip for equal values: " + scene.Info);
            Assert.That(scene.Clip("snd.c"), Is.Not.SameAs(scene.Clip("snd.a")), scene.Info);
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(2), scene.Info);
            Assert.That(scene.Sounds.Renders.Count, Is.EqualTo(2), scene.Info);
            Assert.That(scene.Sounds.Renders.IdleCount, Is.EqualTo(0), scene.Info);
        }

        [UnityTest]
        public IEnumerator SizeMovesThePitchAndSmoothTakesTheHissAway()
        {
            yield return null;
            string elements = Element("low", 0, "tone", 0, 0) + Element("high", 3, "tone", 1, 0) + Element("plain", 6, "noise", 0, 0) + Element("soft", 9, "noise", 0, -1);
            using var scene = new Scene(Room + Clips + elements + "}\n");
            yield return null;
            scene.Begin();
            yield return scene.AllClips("snd.low", "snd.high", "snd.plain", "snd.soft");
            Assert.That(scene.TimedOut, Is.False, scene.Info);
            int low = Crossings(Data(scene.Clip("snd.low"))), high = Crossings(Data(scene.Clip("snd.high")));
            Assert.That(low, Is.GreaterThan(0), scene.Info);
            float ratio = high / (float)low;
            Assert.That(ratio, Is.InRange(2.3f, 3.3f), "pitch ratio " + ratio + " (" + low + " and " + high + " crossings): " + scene.Info);
            float plain = Change(Data(scene.Clip("snd.plain"))), soft = Change(Data(scene.Clip("snd.soft")));
            Assert.That(soft, Is.LessThan(plain * 0.4f), "change " + soft + " against " + plain + ": " + scene.Info);
        }

        [UnityTest]
        public IEnumerator ChangingSizeStartsANewRenderAndTheOldOneWaitsInTheIdleList()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "tone", 0, 0) + "}\n");
            yield return null;
            scene.Begin();
            yield return scene.AllClips("snd.a");
            var first = scene.Clip("snd.a");
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(1), scene.Info);
            scene.SetSound("snd.a", 0.5f, 0);
            scene.Settle();
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(2), "a new render: " + scene.Info);
            yield return scene.Until(() => scene.Sounds.ClipOf(scene.Id("snd.a")) != first && scene.Sounds.ClipOf(scene.Id("snd.a")));
            Assert.That(scene.TimedOut, Is.False, scene.Info);
            // The design: a released render goes to the idle list (up to eight) and is destroyed only when older than those.
            Assert.That(scene.Sounds.Renders.Count, Is.EqualTo(2), scene.Info);
            Assert.That(scene.Sounds.Renders.IdleCount, Is.EqualTo(1), "the old render is idle: " + scene.Info);
            Assert.That(first != null, "the idle clip is still alive");
            // Going back to the old value reuses it: no third render.
            scene.SetSound("snd.a", 0, 0);
            scene.Settle();
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(2), scene.Info);
            Assert.That(scene.Clip("snd.a"), Is.SameAs(first), scene.Info);
            Assert.That(scene.Sounds.Renders.IdleCount, Is.EqualTo(1), scene.Info);
        }

        [UnityTest]
        public IEnumerator EveryRiseOfThePlayedCountStartsOnePlay()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "tone", 0, 0) + "}\n");
            yield return null;
            scene.Begin();
            yield return scene.AllClips("snd.a");
            var source = scene.Sounds.SourceOf(scene.Id("snd.a"));
            Assert.That(scene.Sounds.Plays, Is.EqualTo(0), scene.Info);
            scene.SetPlayed("snd.a", 1);
            scene.Settle();
            Assert.That(scene.Sounds.Plays, Is.EqualTo(1), scene.Info);
            Assert.That(source.clip, Is.SameAs(scene.Clip("snd.a")), scene.Info);
            scene.SetPlayed("snd.a", 1);
            scene.Settle();
            Assert.That(scene.Sounds.Plays, Is.EqualTo(1), "the same count again does not play: " + scene.Info);
            scene.SetPlayed("snd.a", 2);
            scene.Settle();
            Assert.That(scene.Sounds.Plays, Is.EqualTo(2), scene.Info);
        }

        [UnityTest]
        public IEnumerator APlayBeforeTheRenderIsDoneWaitsForIt()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "noise", 0.25f, 0.25f) + "}\n");
            yield return null;
            scene.Begin();
            scene.SetPlayed("snd.a", 1);
            scene.Settle();
            yield return scene.Until(() => scene.Sounds.Plays > 0);
            Assert.That(scene.TimedOut, Is.False, scene.Info);
            Assert.That(scene.Sounds.Plays, Is.EqualTo(1), scene.Info);
            Assert.That(scene.Sounds.SourceOf(scene.Id("snd.a")).clip, Is.SameAs(scene.Clip("snd.a")), scene.Info);
        }

        [UnityTest]
        public IEnumerator AClipWithoutASourceIsSkippedWithAReasonAndAnUnsafeNameIsRefused()
        {
            yield return null;
            string script = Room + Clips + "  lost { SoundClip: {from: 3, uri: \"nothing-here\", seconds: 1} }\n" + Element("a", 0, "lost", 0, 0) + "}\n";
            using var scene = new Scene(script);
            yield return null;
            scene.Begin();
            scene.Settle();
            Assert.That(scene.Sounds.LastSkip, Is.Not.Null.And.Not.Empty, "a reason is kept: " + scene.Info);
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(0), scene.Info);
            Assert.That(scene.Sounds.ClipOf(scene.Id("snd.a")), Is.Null, scene.Info);
            scene.Sounds.Tick();
            Assert.That(scene.Sounds.Plays, Is.EqualTo(0), scene.Info);
            // Registering the missing source later is noticed without any other change.
            scene.Sources.Register("nothing-here", Tone(), Rate);
            scene.Settle();
            Assert.That(scene.Sounds.Renders.Started, Is.EqualTo(1), "the source appeared: " + scene.Info);
            foreach (var bad in new[] { "../x", "..", "a/b", "a\\b", "C:x", "", null })
            {
                Assert.That(scene.Sources.Register(bad, Tone(), Rate), Is.False, "register refuses '" + bad + "'");
                Assert.That(scene.Sources.TryGet(bad, out var samples, out _, out var reason), Is.False, "get refuses '" + bad + "'");
                Assert.That(samples, Is.Null);
                Assert.That(reason, Is.Not.Null.And.Not.Empty);
            }
            Assert.That(new SoundSources("anywhere").TryGet("..\\secret.wav", out _, out _, out _), Is.False, "a folder does not make a path acceptable");
        }

        [Test]
        public void ASixteenBitWavIsReadAndStereoIsMixedToMono()
        {
            var bytes = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(bytes);
            short[] frames = { 1000, -1000, 8000, 0 };
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            w.Write(36 + frames.Length * 2);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)2);
            w.Write(22050);
            w.Write(22050 * 4);
            w.Write((short)4);
            w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            w.Write(frames.Length * 2);
            foreach (var frame in frames)
                w.Write(frame);
            Assert.That(SoundSources.TryReadWav(bytes.ToArray(), out var samples, out int rate), Is.True);
            Assert.That(rate, Is.EqualTo(22050));
            Assert.That(samples.Length, Is.EqualTo(2));
            Assert.That(samples[0], Is.EqualTo(0f).Within(1e-6f));
            Assert.That(samples[1], Is.EqualTo(4000f / 32768f).Within(1e-6f));
            Assert.That(SoundSources.TryReadWav(new byte[] { 1, 2, 3 }, out _, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator RemovingSoundRemovesTheAddedSourceAndDisposingLeavesNothingBehind()
        {
            yield return null;
            using var scene = new Scene(Room + Clips + Element("a", 0, "tone", 0, 0) + Element("b", 3, "noise", 0.5f, 0) + "}\n");
            yield return null;
            scene.Begin();
            yield return scene.AllClips("snd.a", "snd.b");
            var view = scene.World.FindView(scene.Id("snd.a"));
            var other = scene.World.FindView(scene.Id("snd.b"));
            var clipA = scene.Clip("snd.a");
            var clipB = scene.Clip("snd.b");
            Assert.That(view.GetComponent<AudioSource>(), Is.Not.Null, scene.Info);
            Assert.That(scene.Session.RemoveComponent(scene.Id("snd.a"), scene.Id("Sound")), Is.True, scene.Info);
            scene.Settle();
            yield return null;
            Assert.That(view.GetComponent<AudioSource>() == null, "the added source is gone: " + scene.Info);
            Assert.That(scene.Sounds.Held, Is.EqualTo(1), scene.Info);
            Assert.That(scene.Sounds.SourceOf(scene.Id("snd.a")), Is.Null, scene.Info);
            Assert.That(other.GetComponent<AudioSource>(), Is.Not.Null, "the other element keeps its source: " + scene.Info);
            scene.Sounds.Dispose();
            yield return null;
            Assert.That(other.GetComponent<AudioSource>() == null, "no source is left behind");
            Assert.That(clipA == null && clipB == null, "no clip is left behind");
            Assert.That(scene.Sounds.Renders.Count, Is.EqualTo(0), scene.Info);
            Assert.That(scene.Sounds.Held, Is.EqualTo(0), scene.Info);
        }
    }
}
