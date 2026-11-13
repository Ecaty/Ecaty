using Runtime.Capabilities;
#if CAP_BROCCOLI
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Broccoli.Factory;
using Broccoli.Pipe;
#endif
using Runtime.Flecs;
namespace Game.Adapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapVegetation", "vegetation", 15, Gate = "CAP_BROCCOLI", Probe = "Assets/Waldemarst/Broccoli/Factory/TreeFactory.cs")]
    public sealed class VegetationConfig
    {
        [AssetKey] public string pipeline = "";
        public int seed = 1;
    }
#if CAP_BROCCOLI
    public sealed class VegetationCapability : CapabilityInstance<VegetationConfig>
    {
        TreeFactory factory;
        GameObject tree;
        // Broccoli clones the pipeline and creates per-tree meshes/materials that outlive their GameObjects.
        CreatedObjects generated;
        public VegetationCapability(CapabilityContext c) : base(c)
        {
            factory = Host.AddComponent<TreeFactory>();
        }
        protected override void Apply(VegetationConfig j)
        {
            string key = j.pipeline;
            var pipeline = Context.Asset<Pipeline>(key);
            if (!pipeline)
                pipeline = Resources.Load<Pipeline>(string.IsNullOrEmpty(key) ? "BroccoliRuntimeExamplePipeline" : key);
            if (!pipeline)
                throw new ArgumentException("Register a Broccoli generation pipeline.");
            var random = UnityEngine.Random.state;
            GameObject next = null;
            try
            {
                var created = CreatedObjects.During(() =>
                {
                    var copy = pipeline.Clone();
                    factory.SetInstanceAsActive();
                    factory.LoadPipeline(copy, false);
                    next = factory.Spawn(j.seed);
                    if (!next)
                        throw new InvalidOperationException("Broccoli did not produce a tree.");
                });
                next.transform.SetParent(Host.transform, false);
                if (tree)
                    UnityEngine.Object.Destroy(tree);
                generated?.Destroy();
                tree = next;
                generated = created;
            }
            catch
            {
                if (next)
                    UnityEngine.Object.Destroy(next);
                throw;
            }
            finally
            {
                UnityEngine.Random.state = random;
            }
        }
        public override void Dispose()
        {
            if (tree)
                UnityEngine.Object.Destroy(tree);
            tree = null;
            generated?.Destroy();
            generated = null;
        }
    }
#endif
}
