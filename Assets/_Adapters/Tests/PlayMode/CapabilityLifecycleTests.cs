using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Runtime.Flecs;
using Runtime.Capabilities;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests
{
    // Every compiled adapter, with its default configuration, must leave no runtime objects behind once its binding
    // is disabled. Configurations that need registered assets or targets may fail; failure must clean up as well.
    public sealed class CapabilityLifecycleTests
    {
        // Objects present now that were absent from the earlier snapshot. Scene objects owned by the separately
        // running application (outside the isolated world) are not counted.
        static List<string> Added(HashSet<Object> before, FlecsWorld world)
        {
            var added = new List<string>();
            foreach (var o in Resources.FindObjectsOfTypeAll<Object>())
            {
                if (!o || before.Contains(o))
                    continue;
                var go = o as GameObject ?? (o as Component)?.gameObject;
                if (go && !go.transform.IsChildOf(world.transform))
                    continue;
                added.Add(o.GetType().Name + " " + o.name);
            }
            return added;
        }
        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }
        [UnityTest]
        public IEnumerator CreateApplyDisposeLeavesNothingBehind()
        {
            LogAssert.ignoreFailingMessages = true;
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            var leaks = new List<string>();
            try
            {
                var runtime = world.gameObject.AddComponent<CapabilityRuntime>();
                runtime.Initialize(world);
                runtime.RegisterDiscovered();
                Assert.That(runtime.Adapters.Count, Is.GreaterThan(10), "Typed adapters were not discovered");
                world.Session.SetManagedScript("LifecycleTargetModule", "LifecycleTarget { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }");
                world.SyncViews();
                foreach (var adapter in runtime.Adapters.ToArray())
                {
                    var descriptor = CapabilityCatalog.Find(adapter.Component);
                    var config = System.Activator.CreateInstance(descriptor.Config);
                    foreach (var field in CapabilityConfig.Members(descriptor.Config).Where(f => descriptor.EntityFields.Contains(f.Name)))
                        field.SetValue(config, "LifecycleTarget");
                    HashSet<Object> warmed = null;
                    // The first cycle warms shared caches (plugin singletons, shader variants); the second must return to it.
                    for (int cycle = 0; cycle < 2; cycle++)
                    {
                        world.Session.SetManagedScript("LifecycleModule", "LifecycleBinding { CapBinding: {target: LifecycleTarget, enabled: true, revision: " + (cycle + 1) + "} }");
                        world.Session.SetComponentJson("LifecycleBinding", adapter.Component, CapabilityConfig.Write(config).ToString());
                        runtime.Reconcile();
                        yield return Frames(3);
                        var state = JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("LifecycleBinding"), "CapState"));
                        Assert.That((int)state["phase"], Is.Not.EqualTo(0), adapter.Component + " neither applied nor reported a failure");
                        world.Session.SetManagedScript("LifecycleModule", "");
                        world.Session.DeleteEntity("LifecycleBinding");
                        runtime.Reconcile();
                        yield return Frames(3);
                        Assert.That(runtime.BindingCount, Is.EqualTo(0), adapter.Component + " binding survived removal");
                        if (cycle == 0)
                            warmed = new HashSet<Object>(Resources.FindObjectsOfTypeAll<Object>());
                        else
                        {
                            var added = Added(warmed, world);
                            // One line per adapter so every leaking adapter is visible in a truncated report.
                            if (added.Count > 0)
                                leaks.Add(adapter.Component + " +" + added.Count + ": " + string.Join(", ", added.GroupBy(o => o).Select(g => g.Count() + "x " + g.Key).Take(6)));
                        }
                    }
                }
                Assert.That(leaks, Is.Empty, "Runtime objects remained after dispose:\n" + string.Join("\n", leaks));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator IdleReconcileAllocatesNothing()
        {
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                var runtime = world.gameObject.AddComponent<CapabilityRuntime>();
                runtime.Initialize(world);
                runtime.RegisterDiscovered();
                world.Session.SetManagedScript("IdleModule", "IdleTarget { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }\nIdleLight { CapBinding: {target: IdleTarget, enabled: true, revision: 1}; CapLight: {kind: 0, intensity: 1, range: 4, colorR: 1, colorG: 1, colorB: 1} }");
                world.SyncViews();
                runtime.Reconcile();
                yield return Frames(3);
                Assert.That(runtime.BindingCount, Is.EqualTo(1));
                // Settle table moves from the first CapState write and CapDirty acknowledgement, then measure.
                for (int i = 0; i < 5; i++)
                    runtime.Reconcile(false);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 20; i++)
                    runtime.Reconcile(false);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.EqualTo(0), "Idle capability reconcile allocated " + allocated + " bytes");
                var edit = JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("IdleLight"), "CapLight"));
                edit["intensity"] = 3;
                world.Session.SetComponentJson("IdleLight", "CapLight", edit.ToString());
                runtime.Reconcile(false);
                var state = JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("IdleLight"), "CapState"));
                Assert.That((int)state["generation"], Is.GreaterThan(1), "A configuration edit must still be applied by the change-gated pass");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        // struct CapReadback(x: f32, y: f32, z: f32, vx: f32, vy: f32, vz: f32, contacts: i32) in CapabilitySchema.flecs
        struct ReadbackMirror
        {
            public float x, y, z, vx, vy, vz; public int contacts;
        }
        // Ticking capabilities (a dynamic body publishing CapReadback and reading CapDrag/CapForce each fixed step, a light)
        // must neither allocate nor parse JSON once settled. The ticks run through the same code as Update/FixedUpdate.
        [UnityTest]
        public IEnumerator SteadyStateTicksAllocateNothing()
        {
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                var runtime = world.gameObject.AddComponent<CapabilityRuntime>();
                runtime.Initialize(world);
                runtime.RegisterDiscovered();
                world.Session.SetManagedScript("SteadyModule", "SteadyTarget { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1}; CapDrag: {x: 0, y: 0, z: 0, active: false} }\n"
                    + "SteadyBody { CapBinding: {target: SteadyTarget, enabled: true, revision: 1}; CapBody: {mass: 1, radius: 0.5, height: 1, gravity: 1, kinematic: false}; CapForce: {x: 0, y: 0, z: 0, sequence: 1} }\n"
                    + "SteadyLight { CapBinding: {target: SteadyTarget, enabled: true, revision: 1}; CapLight: {kind: 0, intensity: 1, range: 4, colorR: 1, colorG: 1, colorB: 1} }");
                world.SyncViews();
                runtime.Reconcile();
                yield return Frames(3);
                Assert.That(runtime.BindingCount, Is.EqualTo(2));
                Assert.That((int)JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("SteadyBody"), "CapState"))["phase"], Is.EqualTo(1), "The body binding did not apply");
                // Settle: first CapReadback write, CapState/CapDirty table moves, JIT of the generic paths. Real frames
                // pass between steps so the 20 Hz readback and reconcile timers fire.
                for (int i = 0; i < 20; i++)
                {
                    runtime.StepForTests();
                    yield return null;
                }
                long allocated = 0;
                for (int i = 0; i < 300; i++)
                {
                    long before = System.GC.GetAllocatedBytesForCurrentThread();
                    runtime.StepForTests();
                    allocated += System.GC.GetAllocatedBytesForCurrentThread() - before;
                    if (i % 3 == 0)
                        yield return null;
                }
                Assert.That(allocated, Is.EqualTo(0), "Steady-state capability ticks allocated " + allocated + " bytes");
                Assert.That(world.Session.TryRead(world.Session.Lookup("SteadyBody"), "CapReadback", out ReadbackMirror readback), Is.True, "The body did not publish CapReadback");
                Assert.That(float.IsFinite(readback.y));
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        // One binding more than the budget must fail by itself (CapState phase -1) without stopping the others,
        // and must be applied once a slot is free.
        [UnityTest]
        public IEnumerator BindingBeyondTheBudgetFailsAloneAndIsRetried()
        {
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                var runtime = world.gameObject.AddComponent<CapabilityRuntime>();
                runtime.Initialize(world);
                runtime.RegisterDiscovered();
                int count = CapabilityRuntime.MaxBindings + 1;
                var script = new System.Text.StringBuilder("BudgetTarget { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }\n");
                for (int i = 0; i < count; i++)
                    script.Append("BudgetLight").Append(i).Append(" { CapBinding: {target: BudgetTarget, enabled: true, revision: 1}; CapLight: {kind: 0, intensity: 1, range: 10, colorR: 1, colorG: 1, colorB: 1} }\n");
                world.Session.SetManagedScript("BudgetModule", script.ToString());
                world.SyncViews();
                Assert.DoesNotThrow(() => runtime.Reconcile(), "One binding over the budget stopped capability processing");
                yield return Frames(3);
                JObject State(int index) => JObject.Parse(world.Session.ComponentJson(world.Session.Lookup("BudgetLight" + index), "CapState"));
                Assert.That(runtime.BindingCount, Is.EqualTo(CapabilityRuntime.MaxBindings));
                var refused = Enumerable.Range(0, count).Where(i => (int)State(i)["phase"] == -1).ToList();
                Assert.That(refused.Count, Is.EqualTo(1), "Exactly the extra binding must report a failure");
                Assert.That((string)State(refused[0])["message"], Does.Contain("budget"));
                Assert.That(Enumerable.Range(0, count).Count(i => (int)State(i)["phase"] == 1), Is.EqualTo(CapabilityRuntime.MaxBindings));
                world.Session.DeleteEntity("BudgetLight" + Enumerable.Range(0, count).First(i => i != refused[0]));
                runtime.Reconcile(false);
                runtime.Reconcile(false);
                yield return Frames(3);
                Assert.That(runtime.BindingCount, Is.EqualTo(CapabilityRuntime.MaxBindings));
                Assert.That((int)State(refused[0])["phase"], Is.EqualTo(1), "The refused binding was not applied after a slot became free");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        static Color ShownColor(FlecsWorld world, ulong entity)
        {
            var renderer = world.FindView(entity).GetComponentInChildren<Renderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor(Shader.PropertyToID("_BaseColor"));
        }
        static void AssertShows(Color shown, float red, float green, string what)
        {
            Assert.That(shown.r, Is.EqualTo(red).Within(1e-4f), what + " (red)");
            Assert.That(shown.g, Is.EqualTo(green).Within(1e-4f), what + " (green)");
        }
        // A kind is one text. Editing it reaches every instance and its parts on screen; an own value stays.
        [UnityTest]
        public IEnumerator KindEditReachesEveryInstanceOnScreen()
        {
            const int count = 50;
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                string Kind(string red) => "prefab Craft {\n  Position: {0,0,0}\n  Scale: {1,1,1}\n  Color: {" + red + ",0,0,1}\n  Primitive: {0}\n"
                    + "  prefab wing { Position: {1,0,0}; Scale: {0.5,0.5,0.5}; Color: {0,1,0,1}; Primitive: {1} }\n}";
                var session = world.Session;
                session.ApplySource("sources.Craft", "library", Kind("1"));
                ulong kind = session.Lookup("library.Craft");
                Assert.That(kind, Is.Not.EqualTo(0UL));
                int before = world.ViewCount;
                var instances = new ulong[count];
                for (int i = 0; i < count; i++)
                {
                    instances[i] = session.Spawn(kind, 0, "CraftInstance" + i);
                    session.SetComponentJson("CraftInstance" + i, "Position", "{\"x\":" + i + ",\"y\":0,\"z\":0}");
                }
                // One instance has its own colour before the edit.
                session.SetComponentJson("CraftInstance7", "Color", "{\"r\":0.25,\"g\":0,\"b\":0,\"a\":1}");
                world.SyncViews();
                yield return null;
                Assert.That(world.ViewCount - before, Is.EqualTo(2 * count), "Every instance and every part needs a view");
                var parts = new ulong[count];
                for (int i = 0; i < count; i++)
                {
                    parts[i] = session.Lookup("CraftInstance" + i + ".wing");
                    Assert.That(world.FindView(instances[i]), Is.Not.Null, "Instance " + i + " has no object");
                    Assert.That(world.FindView(parts[i]), Is.Not.Null, "Part " + i + " has no object");
                    Assert.That(world.FindView(instances[i]).transform.localPosition.x, Is.EqualTo((float)i));
                    AssertShows(ShownColor(world, instances[i]), i == 7 ? 0.25f : 1f, 0, "Instance " + i + " shows the kind's colour");
                    AssertShows(ShownColor(world, parts[i]), 0, 1f, "Part " + i + " shows the kind's colour");
                }
                Assert.That(session.ApplySource("sources.Craft", "library", Kind("0.5")), Is.False, "An edit of a value removes nothing");
                world.SyncViews();
                yield return null;
                for (int i = 0; i < count; i++)
                {
                    AssertShows(ShownColor(world, instances[i]), i == 7 ? 0.25f : 0.5f, 0, i == 7 ? "The instance with its own colour kept it" : "Instance " + i + " shows the edited colour");
                    AssertShows(ShownColor(world, parts[i]), 0, 1f, "Part " + i + " keeps its colour");
                    Assert.That(world.FindView(instances[i]).transform.localPosition.x, Is.EqualTo((float)i), "An edit must not move instance " + i);
                }
                Assert.That(world.ViewCount - before, Is.EqualTo(2 * count), "An edit must not create or remove objects");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        // A world saved as a document and loaded again replaces the first one: the objects of the old entities are gone
        // and the new entities show the saved values.
        [UnityTest]
        public IEnumerator WorldDocumentRoundTripsOnScreen()
        {
            const int count = 10;
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                var session = world.Session;
                ulong lab = session.CreateWorld("lab");
                session.ApplySource("worlds.lab.sources.Lamp", "worlds.lab.kinds",
                    "prefab Lamp {\n  Position: {0,0,0}\n  Scale: {1,1,1}\n  Color: {1,1,0,1}\n  Primitive: {0}\n"
                    + "  prefab shade { Position: {0,1,0}; Scale: {0.5,0.5,0.5}; Color: {0,1,0,1}; Primitive: {1} }\n}");
                ulong kind = session.Lookup("worlds.lab.kinds.Lamp");
                Assert.That(kind, Is.Not.EqualTo(0UL));
                world.SyncViews();
                int before = world.ViewCount;
                var oldIds = new List<ulong>();
                for (int i = 0; i < count; i++)
                {
                    oldIds.Add(session.Spawn(kind, lab, "Lamp" + i));
                    session.SetComponentJson("worlds.lab.Lamp" + i, "Position", "{\"x\":" + (i + 1) + ",\"y\":0,\"z\":" + 2 * i + "}");
                }
                world.SyncViews();
                yield return null;
                for (int i = 0; i < count; i++)
                {
                    oldIds.Add(session.Lookup("worlds.lab.Lamp" + i + ".shade"));
                    Assert.That(world.FindView(oldIds[i]), Is.Not.Null, "Lamp " + i + " has no object");
                    Assert.That(world.FindView(oldIds[count + i]), Is.Not.Null, "Part " + i + " has no object");
                }
                Assert.That(world.ViewCount - before, Is.EqualTo(2 * count), "Every instance and every part needs a view");
                string document = session.SaveWorld(lab);
                session.SetComponentJson("worlds.lab.Lamp2", "Position", "{\"x\":99,\"y\":99,\"z\":99}");
                session.SetComponentJson("worlds.lab.Lamp5", "Position", "{\"x\":-5,\"y\":-5,\"z\":-5}");
                session.DeleteEntity("worlds.lab.Lamp8");
                world.SyncViews();
                yield return null;
                Assert.That(world.ViewCount - before, Is.EqualTo(2 * (count - 1)), "The deleted lamp and its part must lose their objects");
                Assert.That(session.TryLoadWorld("lab", document, out ulong loaded, out string error), Is.True, error);
                Assert.That(loaded, Is.Not.EqualTo(0UL));
                world.SyncViews();
                yield return null;
                Assert.That(world.ViewCount - before, Is.EqualTo(2 * count), "The loaded world needs every instance and part again");
                foreach (ulong old in oldIds)
                    Assert.That(world.FindView(old), Is.Null, "An object of the replaced world was left");
                for (int i = 0; i < count; i++)
                {
                    ulong lamp = session.Lookup("worlds.lab.Lamp" + i), shade = session.Lookup("worlds.lab.Lamp" + i + ".shade");
                    Assert.That(world.FindView(lamp), Is.Not.Null, "Loaded lamp " + i + " has no object");
                    Assert.That(world.FindView(shade), Is.Not.Null, "Loaded part " + i + " has no object");
                    Assert.That(world.FindView(lamp).transform.localPosition, Is.EqualTo(new Vector3(i + 1, 0, 2 * i)), "Lamp " + i + " is not at its saved position");
                    AssertShows(ShownColor(world, lamp), 1f, 1f, "Lamp " + i + " shows the kind's colour");
                }
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
        // Ownership is an exclusive relationship in the world: one owner per view, and it ends with its owner entity.
        [UnityTest]
        public IEnumerator PoseOwnershipIsExclusiveAndEndsWithItsOwner()
        {
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("Runtime/Examples/World"), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"), 0);
            try
            {
                world.Session.SetManagedScript("OwnershipModule", "OwnedView { Position: {0,0,0}; Scale: {1,1,1}; Primitive: {-1} }\nOwnerA {}\nOwnerB {}");
                world.SyncViews();
                ulong view = world.Session.Lookup("OwnedView"), a = world.Session.Lookup("OwnerA"), b = world.Session.Lookup("OwnerB");
                var t = world.FindView(view).transform;
                Assert.That(world.AcquirePose(view, a), Is.True);
                Assert.That(world.AcquirePose(view, b), Is.False, "A second owner must be refused");
                t.localPosition = new Vector3(5, 0, 0);
                world.Session.SetComponentJson("OwnedView", "Position", "{\"x\":1,\"y\":2,\"z\":3}");
                world.SyncViews();
                Assert.That(t.localPosition, Is.EqualTo(new Vector3(5, 0, 0)), "An owned view must not follow the world");
                world.Session.DeleteEntity("OwnerA");
                world.SyncViews();
                Assert.That(t.localPosition, Is.EqualTo(new Vector3(1, 2, 3)), "Ownership must end with its owner and show the world's pose");
                Assert.That(world.AcquirePose(view, b), Is.True, "A free view can be claimed");
                t.localPosition = new Vector3(7, 0, 0);
                world.ReleasePose(view, b);
                Assert.That(t.localPosition, Is.EqualTo(new Vector3(1, 2, 3)), "Release must snap back to the world's pose at once");
            }
            finally
            {
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
    }
}
