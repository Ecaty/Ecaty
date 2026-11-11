using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ecaty.Flecs;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace Ecaty.Tests
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
                            leaks.AddRange(Added(warmed, world).Select(o => adapter.Component + ": " + o));
                    }
                }
                Assert.That(leaks, Is.Empty, "Runtime objects remained after dispose");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Object.Destroy(world.gameObject);
            }
            yield return null;
        }
    }
}
