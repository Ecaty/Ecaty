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
    }
}
