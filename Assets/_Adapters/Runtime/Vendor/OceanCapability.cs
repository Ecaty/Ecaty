#if ECATY_CAP_CREST
using System;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Ecaty.Flecs.PluginAdapters
{
    public sealed class OceanCapability : CapabilityInstance
    {
        Crest.OceanRenderer ocean;
        public OceanCapability(CapabilityContext c) : base(c) { }
        public override void Apply(JObject j)
        {
            if (!ocean)
            {
                if (Crest.OceanRenderer.Instance)
                    throw new InvalidOperationException("Reuse the existing global Crest ocean capability.");
                var prefab = Context.Asset<GameObject>(j.S("prefab"));
                if (!prefab)
                    throw new ArgumentException("Register a configured Crest ocean prefab.");
                ocean = UnityEngine.Object.Instantiate(prefab, Host.transform).GetComponent<Crest.OceanRenderer>();
                if (!ocean)
                    throw new ArgumentException("Ocean prefab has no Crest renderer.");
            }
            ocean.transform.position = new Vector3(ocean.transform.position.x, j.F("level"), ocean.transform.position.z);
        }
    }
}
#endif
