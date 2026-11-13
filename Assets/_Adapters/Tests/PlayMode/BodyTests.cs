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
    // World sizes first, then the local numbers. Scales multiply down the hierarchy, so only the pond is scaled and nothing
    // that physics shapes sits below a scaled parent. The land is a place with gravity only: no scale and no shape of its own.
    //
    //   entity   parent  world centre      world size      local Position   local Scale
    //   land     -       (0, 0, 0)         -               (0, 0, 0)        1
    //   ground   land    (0, -0.5, 0)      40 x 1 x 40     (0, -0.5, 0)     (40, 1, 40)   pinned, top at y = 0
    //   pond     land    (5, 3, 0)         6 x 6 x 6       (5, 3, 0)        (6, 6, 6)     ghost: x 2..8, y 0..6, z -3..3
    //   fish     land    (5, 3, 0)         unit sphere     (5, 3, 0)        1             density 1, inside the pond
    //   rock     land    (6.5, 3.6, 0)     unit sphere     (6.5, 3.6, 0)    1             density 3, inside the pond
    //   ship     land    (3.5, 1.5, 0)     unit sphere     (3.5, 1.5, 0)    1             density 0.4, inside the pond
    //   ball     land    (-5, 4, 0)        unit sphere     (-5, 4, 0)       1             above the ground, outside the pond
    //   dropper  land    (7, 10, 0)        unit sphere     (7, 10, 0)       1             above the pond (optional)
    //   drifter  -       (30, 50, 0)       unit sphere     (30, 50, 0)      1             in no place
    // The bodies in the pond start as children of the land: the first crossings move them into the pond.
    public sealed class BodyTests
    {
        const float Step = 0.02f;
        const string Land =
            "land { Gravity: {0, -9.8, 0}; Position: {0, 0, 0}\n"
            + "  ground { Pinned; Position: {0, -0.5, 0}; Scale: {40, 1, 40}; Primitive: {0} }\n"
            + "  pond { Ghost; Float: {1}; Drag: {2.5}; Position: {5, 3, 0}; Scale: {6, 6, 6}; Primitive: {0} }\n"
            + "  fish { Free: {1}; Position: {5, 3, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n"
            + "  rock { Free: {3}; Position: {6.5, 3.6, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n"
            + "  ship { Free: {0.4}; Position: {3.5, 1.5, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";
        const string BallTail = "Position: {-5, 4, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";
        const string Dropper = "  dropper { Free: {1}; Position: {7, 10, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";
        const string Drifter = "}\ndrifter { Free: {1}; Position: {30, 50, 0}; Scale: {1, 1, 1}; Primitive: {1} }\n";

        static string Scenario(bool ballFree = true, bool dropper = false) =>
            Land + "  ball { " + (ballFree ? "Free: {1}; " : "") + BallTail + (dropper ? Dropper : "") + Drifter;

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public BodyPresenter Bodies;
            readonly SimulationMode mode;
            readonly System.Collections.Generic.Dictionary<string, ulong> ids = new();

            public Scene(string script)
            {
                mode = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Session.SetManagedScript("Bodies", script);
                Session.Tick(Step);
                World.SyncViews();
                // Ids are found once: a body's path changes when it changes place.
                foreach (var name in new[] { "land", "land.ground", "land.pond", "land.fish", "land.rock", "land.ship", "land.ball", "land.dropper", "drifter" })
                {
                    ulong id = Session.Lookup(name);
                    if (id != 0)
                        ids[name.Substring(name.LastIndexOf('.') + 1)] = id;
                }
                Bodies = new BodyPresenter(World);
                CheckGeometry(script.Contains("dropper"));
            }

            // Fails early, with the world bounds, when the numbers of the scene are wrong: no free body may start inside
            // the ground, and the ones meant for the pond must start inside it.
            void CheckGeometry(bool dropper)
            {
                Bodies.Step(Step);
                Physics.SyncTransforms();
                var ground = View("ground").GetComponent<Collider>();
                var pond = View("pond").GetComponent<Collider>();
                Assert.That(ground && pond, "the shapes were not built. " + Info);
                var names = new System.Collections.Generic.List<string> { "fish", "rock", "ship", "ball", "drifter" };
                if (dropper)
                    names.Add("dropper");
                foreach (var name in names)
                {
                    var body = Body(name);
                    var shape = body.GetComponent<Collider>();
                    string bounds = name + " at " + body.position + " bounds " + shape.bounds + "; ground " + ground.bounds + "; pond " + pond.bounds;
                    Assert.That(Physics.ComputePenetration(shape, body.position, body.rotation, ground, ground.transform.position, ground.transform.rotation, out _, out _),
                        Is.False, "starts inside the ground: " + bounds);
                    bool inPond = name == "fish" || name == "rock" || name == "ship";
                    Assert.That(pond.bounds.Contains(body.position), Is.EqualTo(inPond), (inPond ? "should start inside the pond: " : "should start outside the pond: ") + bounds);
                }
            }

            public string Info => Bodies.Diagnostics + " free bodies in the world: " + Session.QueryOnce("Free");

            // Each lookup says what is missing and what the presenter holds, instead of failing on a null later.
            public ulong Id(string name)
            {
                Assert.That(ids.TryGetValue(name, out ulong id), "no entity " + name + " in the scene. " + Info);
                return id;
            }
            public GameObject ViewObject(string path)
            {
                var view = World.FindView(Id(path));
                Assert.That(view, Is.Not.Null, "no view for " + path + ". " + Info);
                return view;
            }
            public Rigidbody Body(string path)
            {
                var body = Bodies.BodyOf(Id(path));
                Assert.That(body, Is.Not.Null, "no Rigidbody for " + path + ". " + Info);
                return body;
            }
            public Transform View(string path) => ViewObject(path).transform;

            // The order the adapter runs in a frame: step, physics, native tick and view sync.
            public void Advance(float seconds)
            {
                int steps = Mathf.RoundToInt(seconds / Step);
                for (int i = 0; i < steps; i++)
                {
                    Bodies.Step(Step);
                    Physics.SyncTransforms();
                    Physics.Simulate(Step);
                    Session.Tick(Step);
                    World.SyncViews();
                }
            }

            // Brings the poses written back up to date with the last physics step.
            public void Flush()
            {
                Bodies.Step(Step);
                Session.Tick(Step);
                World.SyncViews();
            }

            // The path of the place a body is in now: the pond or the land.
            public string ParentOf(string name)
            {
                foreach (var place in new[] { "land.pond", "land" })
                    if (Session.QueryOnce("Free, (ChildOf, " + place + ")").Contains("\"" + name + "\""))
                        return place;
                Assert.Fail(name + " is in neither place. " + Info);
                return null;
            }

            // True when the entity is a child of the parent, asked of the world itself.
            public void AssertChild(string child, string parent, bool expected = true) =>
                Assert.That(Session.QueryOnce("Free, (ChildOf, " + parent + ")").Contains("\"" + child + "\""), Is.EqualTo(expected),
                    child + (expected ? " should be" : " should not be") + " a child of " + parent + ". " + Info);

            public Vector3 Flecs(string path)
            {
                string json = Session.ComponentJson(Id(path), "Position");
                Assert.That(json, Is.Not.Null, path + " has no Position. " + Info);
                var position = JObject.Parse(json);
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
            Vector3 drifter = scene.View("drifter").position;
            scene.Advance(0.2f);
            Assert.That(scene.Bodies.TrackedCount, Is.EqualTo(7), "five bodies, the ground and the pond were built. " + scene.Info);
            Assert.That(scene.Body("ball").position.y, Is.LessThan(3.9f), "the ball is falling (it starts at y = 4)");
            scene.Advance(2.8f);
            var ball = scene.Body("ball");
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
            scene.Advance(5f);
            float rock = scene.Body("rock").position.y, fish = scene.Body("fish").position.y;
            var ship = scene.Body("ship");
            Assert.That(Mathf.Abs(ship.position.x - 3.5f), Is.LessThan(1f), "the ship rises straight up from x = 3.5, it is at " + ship.position + ". " + scene.Info);
            Assert.That(rock, Is.LessThan(fish - 0.5f), "the rock ends lower than the fish");
            Assert.That(ship.position.y, Is.GreaterThan(fish + 0.5f), "the ship ends higher than both");
            float top = scene.View("pond").GetComponent<BoxCollider>().bounds.max.y;
            Assert.That(top, Is.EqualTo(6f).Within(0.01f));
            var bounds = ship.GetComponent<Collider>().bounds;
            Assert.That(bounds.max.y, Is.GreaterThan(top + 0.1f), "part of the ship is above the pond's top");
            Assert.That(bounds.min.y, Is.LessThan(top - 0.1f), "and part of it is below");
        }

        [UnityTest]
        public IEnumerator BodiesChangePlaceWhenTheyCrossAGhost()
        {
            yield return null;
            using var scene = new Scene(Scenario(dropper: true));
            scene.AssertChild("dropper", "land");
            scene.Advance(3f);
            scene.AssertChild("dropper", "land.pond");
            scene.AssertChild("fish", "land.pond");
            scene.AssertChild("ball", "land");
            var force = JObject.Parse(scene.Session.ComponentJson(scene.Id("dropper"), "EffectiveForce"));
            Assert.That(force["fluid"].Value<float>(), Is.EqualTo(1f), "it gets the pond's fluid");
            // A body pushed out of the pond sideways is in the land again.
            var fish = scene.Body("fish");
            fish.position = new Vector3(9.5f, 3f, 0f);
            fish.linearVelocity = Vector3.zero;
            scene.Advance(0.5f);
            scene.AssertChild("fish", "land");
            scene.AssertChild("fish", "land.pond", false);
            var land = JObject.Parse(scene.Session.ComponentJson(scene.Id("fish"), "EffectiveForce"));
            Assert.That(land["fluid"].Value<float>(), Is.EqualTo(0f), "and it has no fluid");
        }

        [UnityTest]
        public IEnumerator FlecsPositionIsTheBodysPoseInItsParentsSpace()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(2.5f);
            scene.Flush();
            foreach (var path in new[] { "ship", "rock", "ball", "drifter" })
            {
                // The space of the place the body is in now (the ship may be above the pond), or the world.
                var space = path == "drifter" ? scene.World.transform : scene.View(scene.ParentOf(path) == "land.pond" ? "pond" : "land");
                Vector3 world = space.TransformPoint(scene.Flecs(path));
                Assert.That(Vector3.Distance(world, scene.Body(path).position), Is.LessThan(0.05f), path);
            }
            Assert.That(scene.Body("ship").position.y, Is.GreaterThan(3f), "the ship moved");
        }

        [UnityTest]
        public IEnumerator ChangingGravityMovesARestingBody()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(3f);
            var ball = scene.Body("ball");
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
            var view = scene.ViewObject("ball");
            Assert.That(view.GetComponent<Rigidbody>(), Is.Not.Null, "no Rigidbody on the view. " + scene.Info);
            Assert.That(view.transform.parent, Is.SameAs(scene.World.transform), "a simulated body hangs under the world root");
            Assert.That(scene.World.AcquirePose(scene.Id("ball"), scene.Id("land")), Is.False, "its pose is held");
            Assert.That(scene.Session.RemoveComponent(scene.Id("ball"), scene.Session.TypeId("Free")), Is.True, "the ball had Free");
            scene.Advance(0.1f);
            yield return null;
            Assert.That(scene.Bodies.BodyOf(scene.Id("ball")), Is.Null);
            Assert.That(view.GetComponent<Rigidbody>(), Is.Null, "the Rigidbody is gone");
            Assert.That(view.GetComponent<Collider>(), Is.Null, "and so is the collider");
            Assert.That(view.transform.parent, Is.SameAs(scene.View("land")), "the view is under its parent again");
            // The view follows Flecs again.
            Assert.That(Vector3.Distance(view.transform.position, scene.View("land").TransformPoint(scene.Flecs("ball"))), Is.LessThan(0.01f));
            scene.Session.SetComponentJson("land.ball", "Position", "{\"x\":0.1,\"y\":-0.2,\"z\":0}");
            scene.Advance(0.1f);
            Assert.That(Vector3.Distance(view.transform.localPosition, new Vector3(0.1f, -0.2f, 0f)), Is.LessThan(0.001f));
            Assert.That(scene.World.AcquirePose(scene.Id("ball"), scene.Id("land")), Is.True, "the pose can be claimed by someone else");
            scene.World.ReleasePose(scene.Id("ball"), scene.Id("land"));
        }

        [UnityTest]
        public IEnumerator DisposingLeavesNothingBehind()
        {
            yield return null;
            using var scene = new Scene(Scenario());
            scene.Advance(1f);
            var fish = scene.ViewObject("fish");
            Assert.That(fish.GetComponent<Rigidbody>(), Is.Not.Null, "no Rigidbody on the fish. " + scene.Info);
            Assert.That(scene.View("pond").GetComponent<PlaceVolume>(), Is.Not.Null, "no trigger on the pond. " + scene.Info);
            scene.Bodies.Dispose();
            yield return null;
            Assert.That(scene.Bodies.TrackedCount, Is.EqualTo(0));
            Assert.That(scene.World.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(scene.World.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(scene.World.GetComponentsInChildren<PlaceVolume>(true), Is.Empty);
            Assert.That(fish.transform.parent, Is.SameAs(scene.View("pond")), "views are under their Flecs parents again");
            Assert.That(scene.World.AcquirePose(scene.Id("fish"), scene.Id("land")), Is.True, "the pose was given back");
            scene.World.ReleasePose(scene.Id("fish"), scene.Id("land"));
        }
    }
}
