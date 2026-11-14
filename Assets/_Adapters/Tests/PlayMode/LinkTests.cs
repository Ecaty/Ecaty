using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Runtime.Capabilities;
using Runtime.Flecs;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
namespace Game.Tests
{
    // The coin game of the links concept on screen: the physics adapter reports touches, the world's rules answer, Stop
    // rewinds the world. Physics runs by script, one fixed step at a time, like BodyTests.
    //
    // World sizes (everything is a child of the land, which has gravity only and no scale):
    //   ground  (0, -0.5, 0)    40 x 1 x 40, pinned, top at y = 0
    //   fox     (-4, 0.55, 0)   unit sphere, free, starts 0.05 above the ground and settles on it (a real touch of the ground)
    //   coin1   (-1, 0.25, 0)   sphere of diameter 0.5, pinned, on the ground
    //   coin2   (2, 0.25, 0)    same
    //   spook   (20, 0.55, 0)   unit sphere, free, far from the path          (coin game)
    //   spook   (1.05, 0.55, 0)  unit sphere, density 100, free                (bump game: the fox at (0, 0.55, 0))
    //   wall    (2.1, 1, 0)     1 x 2 x 2, pinned, behind the spook so that it cannot be pushed away (bump game)
    public sealed class LinkTests
    {
        const float Step = 0.02f;
        const string Kinds =
            "prefab Fox { Free: {1}; Primitive: {1}; Scale: {1, 1, 1}; Position: {0, 0, 0} }\n"
            + "prefab Coin { Pinned; Primitive: {1}; Scale: {0.5, 0.5, 0.5}; Position: {0, 0, 0} }\n"
            + "prefab Spook { Free: {1}; Primitive: {1}; Scale: {1, 1, 1}; Position: {0, 0, 0} }\n"
            + "prefab Heavy { Free: {100}; Primitive: {1}; Scale: {1, 1, 1}; Position: {0, 0, 0} }\n";
        const string Rules =
            "Star { Count: {start: 0} }\n"
            + "Heart { Count: {start: 3} }\n"
            + "rules {\n"
            + "  collect { (RuleOf, kinds.Fox); Rule: {when: Touches, that: kinds.Coin, thens: [{verb: Removes}, {verb: Counts, count: Star, by: 1}]} }\n"
            + "  hurt { (RuleOf, kinds.Fox); Rule: {when: Touches, that: kinds.Spook, thens: [{verb: Counts, count: Heart, by: -1}]} }\n"
            + "  hurtHeavy { (RuleOf, kinds.Fox); Rule: {when: Touches, that: kinds.Heavy, thens: [{verb: Counts, count: Heart, by: -1}]} }\n"
            + "  win { (RuleOf, Star); Rule: {when: Reaches, n: 2, thens: [{verb: Win}]} }\n"
            + "  lose { (RuleOf, Heart); Rule: {when: Reaches, n: 0, thens: [{verb: Lose}]} }\n"
            + "}\n";
        const string Land =
            "land { Gravity: {0, -9.8, 0}; Position: {0, 0, 0}\n"
            + "  ground { Pinned; Position: {0, -0.5, 0}; Scale: {40, 1, 40}; Primitive: {0} }\n";
        const string CoinGame =
            Land
            + "  fox : kinds.Fox { Position: {-4, 0.55, 0} }\n"
            + "  coin1 : kinds.Coin { Position: {-1, 0.25, 0} }\n"
            + "  coin2 : kinds.Coin { Position: {2, 0.25, 0} }\n"
            + "  spook : kinds.Spook { Position: {20, 0.55, 0} }\n}\n" + Rules;
        const string BumpGame =
            Land
            + "  fox : kinds.Fox { Position: {0, 0.55, 0} }\n"
            + "  spook : kinds.Heavy { Position: {1.05, 0.55, 0} }\n"
            + "  wall { Pinned; Position: {2.1, 1, 0}; Scale: {1, 2, 2}; Primitive: {0} }\n}\n" + Rules;

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public BodyPresenter Bodies;
            public ulong Game;
            readonly SimulationMode mode;
            readonly Dictionary<string, ulong> ids = new();

            public Scene(string scene)
            {
                mode = Physics.simulationMode;
                Physics.simulationMode = SimulationMode.Script;
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Game = Session.CreateWorld("game");
                Assert.That(Session.TryApplySource("worlds.game.sources.Kinds", "worlds.game.kinds", Kinds, out _, out string error, out _, out _), "kinds refused: " + error);
                Session.RunWorldScript(Game, scene);
                Session.Tick(Step);
                World.SyncViews();
                Resolve();
            }

            // Ids are found once after setup (and again after Stop, which replaces every entity).
            public void Resolve()
            {
                ids.Clear();
                foreach (var name in new[] { "land", "land.ground", "land.fox", "land.coin1", "land.coin2", "land.spook", "land.wall", "Star", "Heart" })
                {
                    ulong id = Session.Lookup("worlds.game." + name);
                    if (id != 0)
                        ids[name.Substring(name.LastIndexOf('.') + 1)] = id;
                }
            }

            // The presenter is built a frame after the scene: the primitive views' own colliders are destroyed at the end of the frame.
            public void Begin()
            {
                Bodies = new BodyPresenter(World) { TraceContacts = true };
                Bodies.Step(Step);
                Physics.SyncTransforms();
                var pinned = new List<Collider>();
                foreach (var name in new[] { "ground", "coin1", "coin2", "wall" })
                    if (ids.ContainsKey(name))
                        pinned.Add(View(name).GetComponent<Collider>());
                foreach (var name in new[] { "fox", "spook" })
                {
                    var body = Body(name);
                    var shape = body.GetComponent<Collider>();
                    foreach (var other in pinned)
                    {
                        // A touching contact is not an overlap: only a penetration deeper than 0.01 counts.
                        bool inside = Physics.ComputePenetration(shape, body.position, body.rotation, other, other.transform.position, other.transform.rotation, out _, out float depth);
                        Assert.That(inside && depth >= 0.01f, Is.False,
                            name + " starts inside " + other.name + " by " + depth + " (tolerance 0.01): body " + shape.bounds + ", shape " + other.bounds + ". " + Info);
                    }
                }
                foreach (var rigid in World.GetComponentsInChildren<Rigidbody>(true))
                    Assert.That(rigid.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), rigid.name + " must have exactly one collider. " + Info);
            }

            public string Info => Bodies.Diagnostics + " free bodies: " + Session.QueryOnce("Free");

            public ulong Id(string name)
            {
                Assert.That(ids.TryGetValue(name, out ulong id), "no entity " + name + " in the game. " + Info);
                return id;
            }
            public GameObject ViewObject(string name)
            {
                var view = World.FindView(Id(name));
                Assert.That(view, Is.Not.Null, "no view for " + name + ". " + Info);
                return view;
            }
            public Transform View(string name) => ViewObject(name).transform;
            public Rigidbody Body(string name)
            {
                var body = Bodies.BodyOf(Id(name));
                Assert.That(body, Is.Not.Null, "no Rigidbody for " + name + ". " + Info);
                return body;
            }

            public float Count(string name)
            {
                string json = Session.ComponentJson(Session.Lookup("worlds.game." + name), "Count");
                Assert.That(json, Is.Not.Null, "no count " + name + ". " + Info);
                return JObject.Parse(json)["value"].Value<float>();
            }
            public bool Has(string pair) => Session.QueryOnce(pair).Contains("\"game\"");

            // Advances by fixed steps. `each` runs before every step (it drives a body), `until` ends the run early.
            public void Advance(float seconds, Action each = null, Func<bool> until = null)
            {
                int steps = Mathf.RoundToInt(seconds / Step);
                for (int i = 0; i < steps; i++)
                {
                    each?.Invoke();
                    Bodies.Step(Step);
                    Physics.SyncTransforms();
                    Physics.Simulate(Step);
                    Session.Tick(Step);
                    World.SyncViews();
                    if (until != null && until())
                        return;
                }
            }

            // Sets the horizontal speed of a body for the next step and keeps its fall.
            public Action Drive(string name, float speed)
            {
                var body = Body(name);
                return () =>
                {
                    var v = body.linearVelocity;
                    body.linearVelocity = new Vector3(speed, v.y, 0f);
                };
            }

            public int Total<T>() where T : Component => World.GetComponentsInChildren<T>(true).Length;

            public void Dispose()
            {
                Bodies?.Dispose();
                if (World)
                    Object.Destroy(World.gameObject);
                Physics.simulationMode = mode;
            }
        }

        [UnityTest]
        public IEnumerator CoinGamePlaysOnScreen()
        {
            yield return null;
            using var scene = new Scene(CoinGame);
            yield return null;
            scene.Begin();
            var fox = scene.Body("fox");
            Vector3 start = fox.position;
            ulong coin1 = scene.Id("coin1");
            var coin1View = scene.ViewObject("coin1");
            // Before Play a touch changes nothing.
            scene.Advance(1.5f, scene.Drive("fox", 3f));
            Assert.That(fox.position.x, Is.GreaterThan(-1.5f), "the fox moved along the ground to the coin. " + scene.Info);
            Assert.That(scene.Session.Lookup("worlds.game.land.coin1"), Is.EqualTo(coin1), "the coin is still there before Play. " + scene.Info);
            Assert.That(scene.Count("Star"), Is.EqualTo(0f), "no star before Play. " + scene.Info);
            // Back to the start, apart from the coin for a while, then Play.
            fox.position = start;
            fox.linearVelocity = Vector3.zero;
            scene.Advance(0.5f);
            scene.Session.PlayWorld(scene.Game);
            scene.Advance(0.1f);
            Assert.That(scene.Has("Playing"), "the world plays. " + scene.Info);
            // The fox settled on the ground: a real touch of the pair (fox, ground), and no rule is about the ground.
            Assert.That(scene.Count("Star"), Is.EqualTo(0f), "touching the ground does not count a star. " + scene.Info);
            Assert.That(scene.Count("Heart"), Is.EqualTo(3f), "and costs no heart. " + scene.Info);
            scene.Advance(3f, scene.Drive("fox", 3f), () => scene.Count("Star") >= 1f);
            Assert.That(scene.Count("Star"), Is.EqualTo(1f), "the first coin counts a star. " + scene.Info);
            scene.Advance(0.1f);
            Assert.That(scene.Session.Lookup("worlds.game.land.coin1"), Is.EqualTo(0UL), "the coin's entity is gone. " + scene.Info);
            Assert.That(scene.World.FindView(coin1), Is.Null, "and its view. " + scene.Info);
            yield return null;
            Assert.That(coin1View == null, "and its GameObject is destroyed. " + scene.Info);
            scene.Advance(4f, scene.Drive("fox", 3f), () => scene.Has("(GameOver, Won)"));
            Assert.That(scene.Count("Star"), Is.EqualTo(2f), "the second coin counts a star. " + scene.Info);
            Assert.That(scene.Has("(GameOver, Won)"), "the world has (GameOver, Won). " + scene.Info);
            Assert.That(scene.Has("(GameOver, Lost)"), Is.False);
        }

        [UnityTest]
        public IEnumerator OneBumpCountsOnce()
        {
            yield return null;
            using var scene = new Scene(BumpGame);
            yield return null;
            scene.Begin();
            var fox = scene.Body("fox");
            scene.Session.PlayWorld(scene.Game);
            scene.Advance(0.1f);
            // The fox leans on the spook for a full second.
            scene.Advance(1f, scene.Drive("fox", 1f));
            Assert.That(scene.Count("Heart"), Is.EqualTo(2f), "a second of leaning on the spook costs exactly one heart. " + scene.Info);
            // Apart for half a second, then touching again.
            scene.Advance(0.5f, scene.Drive("fox", -2f));
            Assert.That(Vector3.Distance(fox.position, scene.Body("spook").position), Is.GreaterThan(1.5f), "the fox backed away. " + scene.Info);
            scene.Advance(2f, scene.Drive("fox", 1.5f), () => scene.Count("Heart") < 2f);
            scene.Advance(0.6f, scene.Drive("fox", 1f));
            Assert.That(scene.Count("Heart"), Is.EqualTo(1f), "touching again costs a second heart, no more. " + scene.Info);
        }

        [UnityTest]
        public IEnumerator StopPutsEverythingBack()
        {
            yield return null;
            using var scene = new Scene(CoinGame);
            yield return null;
            scene.Begin();
            Vector3 fox = scene.Body("fox").position, coin1 = scene.View("coin1").position, coin2 = scene.View("coin2").position;
            scene.Advance(2 * Step);
            yield return null;
            int bodies = scene.Total<Rigidbody>(), colliders = scene.Total<Collider>(), volumes = scene.Total<PlaceVolume>();
            Assert.That(bodies, Is.EqualTo(2), "setup: the fox and the spook have a body. " + scene.Info);
            scene.Session.PlayWorld(scene.Game);
            scene.Advance(0.1f);
            scene.Advance(6f, scene.Drive("fox", 3f), () => scene.Has("(GameOver, Won)"));
            Assert.That(scene.Has("(GameOver, Won)"), "the game was won before Stop. " + scene.Info);
            ulong oldFox = scene.Id("fox");
            Assert.That(scene.Session.TryStopWorld(scene.Game, out ulong restored, out string error), "Stop: " + error);
            scene.Game = restored;
            scene.Resolve();
            Assert.That(scene.Id("fox"), Is.Not.EqualTo(oldFox), "every entity of the world has a new id. " + scene.Info);
            scene.Advance(3 * Step);
            yield return null;
            scene.Advance(Step);
            Assert.That(scene.Has("Playing"), Is.False, "the world is not playing. " + scene.Info);
            Assert.That(scene.Count("Star"), Is.EqualTo(0f), "Star is 0 again. " + scene.Info);
            Assert.That(scene.Count("Heart"), Is.EqualTo(3f), "Heart is 3 again. " + scene.Info);
            Assert.That(Vector3.Distance(scene.View("coin1").position, coin1), Is.LessThan(0.01f), "coin1 is back where it was. " + scene.Info);
            Assert.That(Vector3.Distance(scene.View("coin2").position, coin2), Is.LessThan(0.01f), "coin2 is back where it was. " + scene.Info);
            Assert.That(Vector3.Distance(scene.Body("fox").position, fox), Is.LessThan(0.1f), "the fox is back at its start (within 0.1: it falls 0.05 onto the ground), with a body. " + scene.Info);
            Assert.That(scene.Total<Rigidbody>(), Is.EqualTo(bodies), "no Rigidbody was left or lost. " + scene.Info);
            Assert.That(scene.Total<Collider>(), Is.EqualTo(colliders), "no collider was left or lost. " + scene.Info);
            Assert.That(scene.Total<PlaceVolume>(), Is.EqualTo(volumes), "no trigger component was left or lost. " + scene.Info);
        }
    }
}
