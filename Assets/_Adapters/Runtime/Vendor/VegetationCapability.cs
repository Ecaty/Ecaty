using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_BROCCOLI
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Broccoli.Factory;
using Broccoli.Pipe;
#endif
namespace Ecaty.Flecs.PluginAdapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapVegetation", "vegetation", 15, Gate = "ECATY_CAP_BROCCOLI", Probe = "Assets/Waldemarst/Broccoli/Factory/TreeFactory.cs")]
    public sealed class VegetationConfig
    {
        [AssetKey] public string pipeline = "";
        public int seed = 1;
    }
#if ECATY_CAP_BROCCOLI
    public sealed class VegetationCapability : CapabilityInstance<VegetationConfig>
    {
        TreeFactory factory; GameObject tree; PipelineElement[] ownedElements = Array.Empty<PipelineElement>();
        public VegetationCapability(CapabilityContext c) : base(c) { factory = Host.AddComponent<TreeFactory>(); }
        protected override void Apply(VegetationConfig j)
        {
            string key = j.pipeline;
            var pipeline = Context.Asset<Pipeline>(key);
            if (!pipeline)
                pipeline = Resources.Load<Pipeline>(string.IsNullOrEmpty(key) ? "BroccoliRuntimeExamplePipeline" : key);
            if (!pipeline)
                throw new ArgumentException("Register a Broccoli generation pipeline.");
            var random = UnityEngine.Random.state;
            try
            {
                var copy = pipeline.Clone();
                var previous = ownedElements;
                ownedElements = copy.GetElements().Where(e => !pipeline.GetElements().Contains(e)).ToArray();
                factory.SetInstanceAsActive();
                factory.LoadPipeline(copy, false);
                foreach (var element in previous)
                if (element)
                    UnityEngine.Object.Destroy(element);
                var next = factory.Spawn(j.seed);
                if (!next)
                    throw new InvalidOperationException("Broccoli did not produce a tree.");
                next.transform.SetParent(Host.transform, false);
                if (tree)
                    UnityEngine.Object.Destroy(tree);
                tree = next;
            }
            finally { UnityEngine.Random.state = random; }
        }
        public override void Dispose()
        {
            foreach (var element in ownedElements)
            if (element)
                UnityEngine.Object.Destroy(element);
            ownedElements = Array.Empty<PipelineElement>();
        }
    }
#endif
}
