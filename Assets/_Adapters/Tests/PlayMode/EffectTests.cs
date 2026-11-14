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
    // The effect adapter against a real native world: one particle system per emitter, the shared material, the forces of
    // the place, and the end of particles at a pinned shape.
    public sealed class EffectTests
    {
        const string Particle = "prefab particle { Scale: {0.1, 0.4, 0.1}; Look: {colour: {h: 210, s: 0.5, l: 0.7}} }\n";
        const string Rain = "  rain { Particles: {rate: 50, life: 3}; Position: {0, 10, 0}; Scale: {4, 1, 4}; Primitive: {0}\n    " + Particle + "  }\n";
        const string Ground = "  ground { Pinned; Position: {0, -0.5, 0}; Scale: {40, 1, 40}; Primitive: {0} }\n";
        const string Land = "land { Gravity: {0, -9.8, 0}; Position: {0, 0, 0}\n";

        sealed class Scene : IDisposable
        {
            public FlecsWorld World;
            public FlecsSession Session;
            public LookMaterials Materials;
            public LookPresenter Looks;
            public EffectPresenter Effects;
            public BodyPresenter Bodies;

            public Scene(string script)
            {
                World = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
                Session = World.Session;
                Session.SetManagedScript("Effects", script);
                Session.Tick(0.02f);
                World.SyncViews();
            }

            // Call it a frame after the scene exists: a new primitive view carries its default collider until the frame ends.
            public void Begin(bool looks = false, bool bodies = false)
            {
                Materials = new LookMaterials();
                if (looks)
                    Looks = new LookPresenter(World, Materials);
                Effects = new EffectPresenter(World, Materials);
                if (bodies)
                    Bodies = new BodyPresenter(World);
                Settle();
            }

            public void Settle()
            {
                for (int i = 0; i < 4; i++)
                {
                    Session.Tick(0.02f);
                    World.SyncViews();
                    Bodies?.Step(0.02f);
                    Looks?.Tick();
                    Effects?.Tick();
                }
                Physics.SyncTransforms();
            }

            public ulong Id(string path) => Session.Lookup(path);
            public ParticleSystem System(string path) => Effects.SystemOf(Id(path));
            public string Info => Effects?.Diagnostics + " " + Bodies?.Diagnostics;
            public void SetJson(string path, string type, string json) => Session.SetComponentJson(path, type, json);

            public void Dispose()
            {
                Effects?.Dispose();
                Looks?.Dispose();
                Bodies?.Dispose();
                Materials?.Dispose();
                if (World)
                    Object.Destroy(World.gameObject);
            }
        }

        static string LookOf(int hue) => "{\"colour\":{\"h\":" + hue + ",\"s\":0.5,\"l\":0.7}}";

        [UnityTest]
        public IEnumerator AnEmitterGetsOneParticleSystemWithTheMappedValues()
        {
            yield return null;
            using var scene = new Scene(Land + Rain + "}\n");
            yield return null;
            scene.Begin();
            var ps = scene.System("land.rain");
            Assert.That(ps, Is.Not.Null, scene.Info);
            var view = scene.World.FindView(scene.Id("land.rain"));
            int children = view.transform.childCount;
            Assert.That(ps.emission.rateOverTime.constant, Is.EqualTo(50f), scene.Info);
            Assert.That(ps.main.startLifetime.constant, Is.EqualTo(3f), scene.Info);
            Assert.That(ps.main.maxParticles, Is.EqualTo(225), scene.Info);
            Assert.That(ps.main.startSizeX.constant, Is.EqualTo(0.1f).Within(1e-5f), scene.Info);
            Assert.That(ps.main.startSizeY.constant, Is.EqualTo(0.4f).Within(1e-5f), scene.Info);
            Assert.That(ps.main.startSizeZ.constant, Is.EqualTo(0.1f).Within(1e-5f), scene.Info);
            Assert.That(ps.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World), scene.Info);
            Assert.That(ps.shape.shapeType, Is.EqualTo(ParticleSystemShapeType.Box), scene.Info);
            Assert.That(ps.transform.parent, Is.SameAs(view.transform), "the system is a child of the view");
            Assert.That(ps.GetComponent<ParticleSystemRenderer>().sharedMaterial, Is.Not.Null, scene.Info);
            Assert.That(view.GetComponentsInChildren<ParticleSystem>().Length, Is.EqualTo(1), scene.Info);
            ps.Simulate(2f, true, true);
            Assert.That(ps.particleCount, Is.GreaterThan(10), "particles are alive: " + scene.Info);
            Assert.That(view.transform.childCount, Is.EqualTo(children), "a particle is not a game object: " + scene.Info);
            Assert.That(Object.FindObjectsByType<ParticleSystem>().Length, Is.EqualTo(1), scene.Info);
        }

        [UnityTest]
        public IEnumerator EqualLooksShareOneMaterialWithEachOtherAndWithAnElement()
        {
            yield return null;
            string two = "  hail { Particles: {rate: 20, life: 2}; Position: {10, 10, 0}; Scale: {2, 1, 2}; Primitive: {0}\n    " + Particle + "  }\n"
                + "  ball { Position: {-5, 4, 0}; Scale: {1, 1, 1}; Primitive: {1}; Look: {colour: {h: 210, s: 0.5, l: 0.7}} }\n";
            using var scene = new Scene(Land + Rain + two + "}\n");
            yield return null;
            scene.Begin(looks: true);
            var a = scene.System("land.rain").GetComponent<ParticleSystemRenderer>().sharedMaterial;
            var b = scene.System("land.hail").GetComponent<ParticleSystemRenderer>().sharedMaterial;
            var ball = scene.World.FindView(scene.Id("land.ball")).transform.Find("Visual").GetComponent<Renderer>().sharedMaterial;
            Assert.That(a, Is.Not.Null, scene.Info);
            Assert.That(b, Is.SameAs(a), scene.Info);
            Assert.That(ball, Is.SameAs(a), scene.Info);
            Assert.That(scene.Materials.Count, Is.EqualTo(1), scene.Info);
            Assert.That(scene.Materials.References(a), Is.EqualTo(3), scene.Info);
        }

        [UnityTest]
        public IEnumerator ChangingTheParticleLookChangesTheMaterialAndReleasesTheOldOne()
        {
            yield return null;
            using var scene = new Scene(Land + Rain + "}\n");
            yield return null;
            scene.Begin();
            var renderer = scene.System("land.rain").GetComponent<ParticleSystemRenderer>();
            var before = renderer.sharedMaterial;
            Assert.That(before, Is.Not.Null, scene.Info);
            scene.SetJson("land.rain.particle", "Look", LookOf(30));
            scene.Settle();
            var after = renderer.sharedMaterial;
            Assert.That(after, Is.Not.SameAs(before), scene.Info);
            Assert.That(scene.Materials.Count, Is.EqualTo(1), "the old material is released: " + scene.Info);
            Assert.That(scene.Materials.References(before), Is.EqualTo(0), scene.Info);
            yield return null;
            Assert.That(before == null, "the unused material is destroyed");
        }

        [UnityTest]
        public IEnumerator ForcesComeFromThePlaceAndFollowItsChanges()
        {
            yield return null;
            using var scene = new Scene(Land + Rain + "}\n" + "smoke { Particles: {rate: 5, life: 2}; Position: {50, 5, 0}; Scale: {1, 1, 1}; Primitive: {0} }\n");
            yield return null;
            scene.Begin();
            var rain = scene.System("land.rain");
            var smoke = scene.System("smoke");
            Assert.That(rain, Is.Not.Null, scene.Info);
            Assert.That(smoke, Is.Not.Null, scene.Info);
            Assert.That(rain.forceOverLifetime.space, Is.EqualTo(ParticleSystemSimulationSpace.World), scene.Info);
            Assert.That(rain.forceOverLifetime.y.constant, Is.EqualTo(-9.8f).Within(1e-4f), scene.Info);
            Assert.That(rain.forceOverLifetime.x.constant, Is.EqualTo(0f), scene.Info);
            Assert.That(smoke.forceOverLifetime.y.constant, Is.EqualTo(0f), "the empty world pulls nothing: " + scene.Info);
            scene.SetJson("land", "Gravity", "{\"x\":0,\"y\":-2,\"z\":0}");
            scene.Settle();
            Assert.That(rain.forceOverLifetime.y.constant, Is.EqualTo(-2f).Within(1e-4f), scene.Info);
            scene.SetJson("land", "Wind", "{\"x\":3,\"y\":0,\"z\":0}");
            scene.Settle();
            Assert.That(rain.forceOverLifetime.x.constant, Is.EqualTo(3f).Within(1e-4f), scene.Info);
        }

        [UnityTest]
        public IEnumerator ParticlesEndAtAPinnedShape()
        {
            yield return null;
            using var scene = new Scene(Land + Ground + Rain + "}\n");
            yield return null;
            scene.Begin(bodies: true);
            var ps = scene.System("land.rain");
            Assert.That(ps, Is.Not.Null, scene.Info);
            Assert.That(ps.collision.enabled && ps.collision.type == ParticleSystemCollisionType.World, scene.Info);
            Assert.That(scene.World.FindView(scene.Id("land.ground")).GetComponent<Collider>(), Is.Not.Null, "the ground has a collider: " + scene.Info);
            ps.Simulate(3f, true, true);
            var particles = new ParticleSystem.Particle[ps.main.maxParticles];
            int count = ps.GetParticles(particles);
            Assert.That(count, Is.GreaterThan(0), "particles are alive: " + scene.Info);
            float lowest = float.MaxValue;
            for (int i = 0; i < count; i++)
                lowest = Mathf.Min(lowest, particles[i].position.y);
            Assert.That(lowest, Is.GreaterThanOrEqualTo(-0.05f), "no particle below the ground top (y = 0), alive " + count + ": " + scene.Info);
        }

        [UnityTest]
        public IEnumerator RemovingParticlesDestroysTheSystemAndDisposeLeavesNone()
        {
            yield return null;
            using var scene = new Scene(Land + Rain + "}\n");
            yield return null;
            scene.Begin(looks: true);
            var ps = scene.System("land.rain");
            Assert.That(ps, Is.Not.Null, scene.Info);
            Assert.That(scene.Materials.Count, Is.EqualTo(1), scene.Info);
            Assert.That(scene.Session.RemoveComponent(scene.Id("land.rain"), scene.Session.TypeId("Particles")), Is.True);
            scene.Settle();
            yield return null;
            Assert.That(ps == null, "the particle system object is destroyed: " + scene.Info);
            Assert.That(scene.Effects.EmitterCount, Is.EqualTo(0), scene.Info);
            Assert.That(scene.Materials.Count, Is.EqualTo(0), "the material is released: " + scene.Info);
            // Put it back, then dispose: nothing stays behind.
            scene.SetJson("land.rain", "Particles", "{\"rate\":10,\"life\":2}");
            scene.Settle();
            Assert.That(scene.System("land.rain"), Is.Not.Null, scene.Info);
            scene.Effects.Dispose();
            yield return null;
            Assert.That(Object.FindObjectsByType<ParticleSystem>().Length, Is.EqualTo(0), scene.Info);
            Assert.That(scene.Materials.Count, Is.EqualTo(0), scene.Info);
        }
    }
}
