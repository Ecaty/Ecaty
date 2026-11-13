using Runtime.Capabilities;
#if CAP_CREST
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
#endif
using Runtime.Flecs;
namespace Game.Adapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapOcean", "ocean rendering", 45, Gate = "CAP_CREST", Probe = "Assets/Crest/Crest/Scripts/OceanRenderer.cs")]
    public sealed class OceanConfig
    {
        [AssetKey] public string prefab = "";
        public float level;
    }
#if CAP_CREST
    public sealed class OceanCapability : CapabilityInstance<OceanConfig>
    {
        Crest.OceanRenderer ocean;
        public OceanCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(OceanConfig j)
        {
            if (!ocean)
            {
                if (Crest.OceanRenderer.Instance)
                    throw new InvalidOperationException("Reuse the existing global Crest ocean capability.");
                var prefab = Context.Asset<GameObject>(j.prefab);
                if (!prefab)
                    throw new ArgumentException("Register a configured Crest ocean prefab.");
                ocean = UnityEngine.Object.Instantiate(prefab, Host.transform).GetComponent<Crest.OceanRenderer>();
                if (!ocean)
                    throw new ArgumentException("Ocean prefab has no Crest renderer.");
            }
            ocean.transform.position = new Vector3(ocean.transform.position.x, j.level, ocean.transform.position.z);
        }
    }
#endif
}
