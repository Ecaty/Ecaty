using System.Collections;
using Runtime.Flecs;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests
{
    // Motion loops against a real native world: the module writes an offset, the view stream sends rest plus offset and
    // Unity shows it. The rest pose in the world is not changed.
    public sealed class MotionTests
    {
        static FlecsWorld NewWorld() =>
            FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);

        // Frames of 0.02 s: the native tick, then the views Unity is sent.
        static void Frames(FlecsWorld world, int count)
        {
            for (int i = 0; i < count; i++)
            {
                world.Session.Tick(0.02f);
                world.SyncViews();
            }
        }

        // A little more than one period of the loops below (1 s) in frames of 0.02 s.
        const int LoopFrames = 55;

        static JObject Component(FlecsWorld world, string path, string type) =>
            JObject.Parse(world.Session.ComponentJson(world.Session.Lookup(path), type));

        [UnityTest]
        public IEnumerator LoopMovesAPartOnScreen()
        {
            var world = NewWorld();
            try
            {
                var session = world.Session;
                // A smooth loop of one second without overshoot: over one period the part reaches its second pose, wherever the loop starts.
                session.RunScript(
                    "motionProbe {\n  Position: {5,0,0}\n  Scale: {1,1,1}\n  Primitive: {-1}\n  MotionLoop: {snap: 0, bounce: 0, period: 1, phase: 0.5}\n"
                    + "  mover { Position: {0,1,0}; Rotation: {0,0,0}; Scale: {1,1,1}; Primitive: {1}\n"
                    + "    SecondPose: {move: {x: 0, y: 2, z: 0}, turn: {x: 0, y: 90, z: 0}, stretch: 1} }\n"
                    + "  still { Position: {0,2,0}; Rotation: {0,0,0}; Scale: {1,1,1}; Primitive: {1} }\n}");
                world.SyncViews();
                yield return null;
                var mover = world.FindView(session.Lookup("motionProbe.mover"));
                var still = world.FindView(session.Lookup("motionProbe.still"));
                Assert.That(mover, Is.Not.Null, "the moving part has no object");
                Assert.That(still, Is.Not.Null, "the other part has no object");
                var stillPosition = still.transform.localPosition;
                var stillScale = still.transform.localScale;
                var stillRotation = still.transform.localRotation;
                float height = 0, size = 0, angle = 0;
                for (int i = 0; i < LoopFrames; i++)
                {
                    Frames(world, 1);
                    height = Mathf.Max(height, mover.transform.localPosition.y);
                    size = Mathf.Max(size, mover.transform.localScale.x);
                    angle = Mathf.Max(angle, Quaternion.Angle(mover.transform.localRotation, Quaternion.identity));
                }
                yield return null;
                Assert.That(height, Is.GreaterThan(2.5f), "the part moved up from its rest height of 1");
                Assert.That(size, Is.GreaterThan(1.7f), "the part grew");
                Assert.That(angle, Is.GreaterThan(30f), "the part turned");
                Assert.That(still.transform.localPosition, Is.EqualTo(stillPosition), "a part without a second pose moved");
                Assert.That(still.transform.localScale, Is.EqualTo(stillScale));
                Assert.That(still.transform.localRotation, Is.EqualTo(stillRotation));
                var rest = Component(world, "motionProbe.mover", "Position");
                Assert.That(rest["x"].Value<float>(), Is.EqualTo(0f).Within(1e-5f), "the rest pose in the world was written");
                Assert.That(rest["y"].Value<float>(), Is.EqualTo(1f).Within(1e-5f), "the rest pose in the world was written");
                Assert.That(rest["z"].Value<float>(), Is.EqualTo(0f).Within(1e-5f), "the rest pose in the world was written");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator KindLoopReachesInstances()
        {
            const int count = 5;
            var world = NewWorld();
            try
            {
                string Kind(string lift) => "prefab Mover {\n  Position: {0,0,0}\n  Scale: {1,1,1}\n  Primitive: {-1}\n  MotionLoop: {snap: 0, bounce: 0, period: 1, phase: 0.5}\n"
                    + "  prefab part { Position: {0,1,0}; Scale: {1,1,1}; Primitive: {1}; SecondPose: {move: {x: 0, y: " + lift + ", z: 0}} }\n}";
                var session = world.Session;
                Assert.That(session.TryApplySource("sources.Mover", "library", Kind("2"), out _, out string error, out _, out _), error);
                ulong kind = session.Lookup("library.Mover");
                Assert.That(kind, Is.Not.EqualTo(0UL));
                var parts = new GameObject[count];
                for (int i = 0; i < count; i++)
                    session.Spawn(kind, 0, "motionKind" + i);
                world.SyncViews();
                yield return null;
                for (int i = 0; i < count; i++)
                {
                    parts[i] = world.FindView(session.Lookup("motionKind" + i + ".part"));
                    Assert.That(parts[i], Is.Not.Null, "instance " + i + " has no part object");
                }
                // The highest point of each part over one loop: close to rest (1) plus move, wherever the loop is.
                var first = new float[count];
                for (int f = 0; f < LoopFrames; f++)
                {
                    Frames(world, 1);
                    for (int i = 0; i < count; i++)
                        first[i] = Mathf.Max(first[i], parts[i].transform.localPosition.y);
                }
                for (int i = 0; i < count; i++)
                    Assert.That(first[i], Is.GreaterThan(2.5f), "the part of instance " + i + " did not move");
                // A larger move in the kind's text reaches every instance's part.
                Assert.That(session.TryApplySource("sources.Mover", "library", Kind("4"), out _, out error, out _, out _), error);
                var second = new float[count];
                for (int f = 0; f < LoopFrames; f++)
                {
                    Frames(world, 1);
                    for (int i = 0; i < count; i++)
                        second[i] = Mathf.Max(second[i], parts[i].transform.localPosition.y);
                }
                yield return null;
                // The moves differ by 2; the peaks must differ by at least 60 percent of that (a smooth loop peaks at about 97 percent of the move).
                for (int i = 0; i < count; i++)
                    Assert.That(second[i], Is.GreaterThan(first[i] + 1.2f), "the part of instance " + i + " did not follow the edit");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
    }
}
