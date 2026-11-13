using System;
using System.Collections;
using Runtime.Flecs;
using Runtime.Capabilities;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    // The physics adapter for places and bodies against a real native world. Physics runs by script, one fixed step at a
    // time, so the results do not depend on frame timing.
    //
    // The land is a box 20 wide centred at (0, 5, 0): x and z from -10 to 10, y from -5 to 15. Its ground is a pinned slab
    // whose top is y = 0. The pond is a 6 wide box centred at (4, 2, 0): y from -1 to 5. Children sit in their parent's
    // scaled space, so their positions in the script are world units divided by 20 (land) and by 6 (pond).
    public sealed class BodyTests
    {
        const float Step = 0.02f;
        const string Land =
            "land { Ghost; Gravity: {0, -9.8, 0}; Position: {0, 5, 0}; Scale: {20, 20, 20}; Primitive: {0}\n"
            + "  ground { Pinned; Position: {0, -0.3, 0}; Scale: {1, 0.1, 1}; Primitive: {0} }\n"
            + "  pond { Ghost; Float: {1}; Drag: {2.5}; Position: {0.2, -0.15, 0}; Scale: {0.3, 0.3, 0.3}; Primitive: {0}\n"
            + "    fish { Free: {1}; Position: {0, 0, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n"
            + "    rock { Free: {3}; Position: {0.25, 0.1, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n"
            + "    ship { Free: {0.4}; Position: {-0.25, -0.1, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n"
            + "  }\n";
        const string BallTail = "Position: {-0.2, -0.1, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";
        const string Dropper = "  dropper { Free: {1}; Position: {0.3, 0.15, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";
        const string Drifter = "}\ndrifter { Free: {1}; Position: {30, 50, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";

        static string Scenario(bool ballFree = true, bool dropper = false) =>
            Land + "  ball { " + (ballFree ? "Free: {1}; " : "") + BallTail + (dropper ? Dropper : "") + Drifter;

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public BodyPresenter Bodies;
            readonly SimulationMode mode;

            public Scene(string script)
            {
                mode = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Session.SetManagedScript("Bodies", script);
                Session.Tick(Step);
                World.SyncViews();
                Bodies = new BodyPresenter(World);
            }

            public ulong Id(string path) => Session.Lookup(path);
            public Rigidbody Body(string path) => Bodies.BodyOf(Id(path));
            public Transform View(string path) => World.FindView(Id(path)).transform;

            // The order the adapter runs in a frame: step, physics, note poses, native tick and view sync, put views back.
            public void Advance(float seconds)
            {
                int steps = Mathf.RoundToInt(seconds / Step);
                for (int i = 0; i < steps; i++)
                {
                    Bodies.Step(Step);
                    Physics.SyncTransforms();
                    Physics.Simulate(Step);
                    Bodies.Capture();
                    Session.Tick(Step);
                    World.SyncViews();
                    Bodies.Restore();
                }
            }

            // Brings the poses written back up to date with the last physics step.
            public void Flush()
            {
                Bodies.Step(Step);
                Session.Tick(Step);
                World.SyncViews();
                Bodies.Restore();
            }

            // True when the entity is a child of the parent, asked of the world itself.
            public bool IsChild(string child, string parent) =>
                Session.QueryOnce("Free, (ChildOf, " + parent + ")").Contains("\"" + child + "\"");

            public Vector3 Flecs(string path)
            {
                var position = JObject.Parse(Session.ComponentJson(Id(path), "Position"));
                return new Vector3(position["x"].Value<float>(), position["y"].Value<float>(), position["z"].Value<float>());
            }

            public void Dispose()
            {
                Bodies?.Dispose();
                if (World)
                    Object.Destroy(World.gameObject);
                Physics.simulationMode = mode;
            }
        }

        [UnityTest]
        public IEnumerator BallRestsOnTheGroundAndADrifterStaysWhereItIs()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            Vector3 drifter = scene.World.FindView(scene.Id("drifter")).transform.position;
            scene.Advance(0.2f);
            Assert.That(scene.Bodies.TrackedCount, Is.GreaterThanOrEqualTo(8), "shapes and bodies were built");
            Assert.That(scene.Body("land.ball").position.y, Is.LessThan(3f), "the ball is falling");
            scene.Advance(2.8f);
            var ball = scene.Body("land.ball");
            Assert.That(ball.position.y, Is.EqualTo(0.5f).Within(0.1f), "the ball rests on the ground (top at 0, radius 0.5)");
            Assert.That(ball.linearVelocity.magnitude, Is.LessThan(0.1f), "and holds still");
            var drifterBody = scene.Body("drifter");
            Assert.That(drifterBody.position, Is.EqualTo(drifter).Within(0.001f), "a body in an empty world has no force");
            Assert.That(drifterBody.useGravity, Is.False, "Unity's gravity is not used");
        }

        [UnityTest]
        public IEnumerator RockSinksFishStaysAndShipFloats()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(4f);
            float rock = scene.Body("land.pond.rock").position.y, fish = scene.Body("land.pond.fish").position.y;
            var ship = scene.Body("land.pond.ship");
            Assert.That(rock, Is.LessThan(fish - 0.5f), "the rock ends lower than the fish");
            Assert.That(ship.position.y, Is.GreaterThan(fish + 0.5f), "the ship ends higher than both");
            float top = scene.View("land.pond").GetComponent<BoxCollider>().bounds.max.y;
            Assert.That(top, Is.EqualTo(5f).Within(0.01f));
            var bounds = ship.GetComponent<Collider>().bounds;
            Assert.That(bounds.max.y, Is.GreaterThan(top + 0.1f), "part of the ship is above the pond's top");
            Assert.That(bounds.min.y, Is.LessThan(top - 0.1f), "and part of it is below");
        }

        [UnityTest]
        public IEnumerator BodiesChangePlaceWhenTheyCrossAGhost()
        {
            yield return null;
            using var scene = new Scene(Scenario(dropper: true));
            Assert.That(scene.IsChild("dropper", "land"));
            scene.Advance(3f);
            Assert.That(scene.IsChild("dropper", "land.pond"), "a ball dropped into the pond is in the pond");
            Assert.That(scene.IsChild("fish", "land.pond"));
            Assert.That(scene.IsChild("ball", "land"));
            var force = JObject.Parse(scene.Session.ComponentJson(scene.Id("land.pond.dropper"), "EffectiveForce"));
            Assert.That(force["fluid"].Value<float>(), Is.EqualTo(1f), "it gets the pond's fluid");
            // A body pushed out of the pond sideways is in the land again.
            var fish = scene.Body("land.pond.fish");
            fish.position = new Vector3(8.5f, 2f, 0f);
            fish.linearVelocity = Vector3.zero;
            scene.Advance(0.5f);
            Assert.That(scene.IsChild("fish", "land"), "the fish is a child of the land");
            Assert.That(scene.IsChild("fish", "land.pond"), Is.False);
            var land = JObject.Parse(scene.Session.ComponentJson(scene.Id("land.fish"), "EffectiveForce"));
            Assert.That(land["fluid"].Value<float>(), Is.EqualTo(0f), "and it has no fluid");
        }

        [UnityTest]
        public IEnumerator FlecsPositionIsTheBodysPoseInItsParentsSpace()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(2.5f);
            scene.Flush();
            foreach (var (path, parent) in new[] { ("land.pond.ship", "land.pond"), ("land.pond.rock", "land.pond"), ("land.ball", "land"), ("drifter", (string)null) })
            {
                var space = parent == null ? scene.World.transform : scene.View(parent);
                Vector3 world = space.TransformPoint(scene.Flecs(path));
                Assert.That(Vector3.Distance(world, scene.Body(path).position), Is.LessThan(0.05f), path);
            }
            Assert.That(scene.Body("land.pond.ship").position.y, Is.GreaterThan(3f), "the ship moved");
        }

        [UnityTest]
        public IEnumerator ChangingGravityMovesARestingBody()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(3f);
            var ball = scene.Body("land.ball");
            Assert.That(ball.position.y, Is.LessThan(0.7f));
            scene.Session.SetComponentJson("land", "Gravity", "{\"x\":0,\"y\":9.8,\"z\":0}");
            scene.Advance(1f);
            Assert.That(ball.position.y, Is.GreaterThan(2f), "the ball rises");
            Assert.That(ball.linearVelocity.y, Is.GreaterThan(1f));
        }

        [UnityTest]
        public IEnumerator RemovingFreeGivesTheBodyBack()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(1f);
            var view = scene.World.FindView(scene.Id("land.ball"));
            Assert.That(view.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(view.transform.parent, Is.SameAs(scene.World.transform), "a simulated body hangs under the world root");
            Assert.That(scene.World.AcquirePose(scene.Id("land.ball"), scene.Id("land")), Is.False, "its pose is held");
            scene.Session.SetManagedScript("Bodies", Scenario(ballFree: false));
            scene.Advance(0.1f);
            yield return null;
            Assert.That(scene.Body("land.ball"), Is.Null);
            Assert.That(view.GetComponent<Rigidbody>(), Is.Null, "the Rigidbody is gone");
            Assert.That(view.GetComponent<Collider>(), Is.Null, "and so is the collider");
            Assert.That(view.transform.parent, Is.SameAs(scene.View("land")), "the view is under its parent again");
            // The view follows Flecs again.
            Assert.That(Vector3.Distance(view.transform.position, scene.View("land").TransformPoint(scene.Flecs("land.ball"))), Is.LessThan(0.01f));
            scene.Session.SetComponentJson("land.ball", "Position", "{\"x\":0.1,\"y\":-0.2,\"z\":0}");
            scene.Advance(0.1f);
            Assert.That(Vector3.Distance(view.transform.localPosition, new Vector3(0.1f, -0.2f, 0f)), Is.LessThan(0.001f));
            Assert.That(scene.World.AcquirePose(scene.Id("land.ball"), scene.Id("land")), Is.True, "the pose can be claimed by someone else");
            scene.World.ReleasePose(scene.Id("land.ball"), scene.Id("land"));
        }

        [UnityTest]
        public IEnumerator DisposingLeavesNothingBehind()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(1f);
            var fish = scene.World.FindView(scene.Id("land.pond.fish"));
            Assert.That(fish.GetComponent<Rigidbody>(), Is.Not.Null);
            Assert.That(scene.View("land.pond").GetComponent<PlaceVolume>(), Is.Not.Null);
            scene.Bodies.Dispose();
            yield return null;
            Assert.That(scene.Bodies.TrackedCount, Is.EqualTo(0));
            Assert.That(scene.World.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(scene.World.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(scene.World.GetComponentsInChildren<PlaceVolume>(true), Is.Empty);
            Assert.That(fish.transform.parent, Is.SameAs(scene.View("land.pond")), "views are under their Flecs parents again");
            Assert.That(scene.World.AcquirePose(scene.Id("land.pond.fish"), scene.Id("land")), Is.True, "the pose was given back");
            scene.World.ReleasePose(scene.Id("land.pond.fish"), scene.Id("land"));
        }
    }
}
