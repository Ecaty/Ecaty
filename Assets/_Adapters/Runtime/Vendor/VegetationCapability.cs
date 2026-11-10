#if ECATY_CAP_BROCCOLI
using System;
using System.Linq;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Broccoli.Factory;
using Broccoli.Pipe;
namespace Ecaty.Flecs.PluginAdapters {
    public sealed class VegetationCapability:CapabilityInstance {
        TreeFactory factory;GameObject tree;PipelineElement[] ownedElements=Array.Empty<PipelineElement>();
        public VegetationCapability(CapabilityContext c):base(c){factory=Host.AddComponent<TreeFactory>();}
        public override void Apply(JObject j){
            string key=j.S("pipeline");var pipeline=Context.Asset<Pipeline>(key);
            if(!pipeline)pipeline=Resources.Load<Pipeline>(string.IsNullOrEmpty(key)?"BroccoliRuntimeExamplePipeline":key);
            if(!pipeline)throw new ArgumentException("Register a Broccoli generation pipeline.");
            var random=UnityEngine.Random.state;try{var copy=pipeline.Clone();var previous=ownedElements;ownedElements=copy.GetElements().Where(e=>!pipeline.GetElements().Contains(e)).ToArray();factory.SetInstanceAsActive();factory.LoadPipeline(copy,false);foreach(var element in previous)if(element)UnityEngine.Object.Destroy(element);var next=factory.Spawn(j.I("seed",1));if(!next)throw new InvalidOperationException("Broccoli did not produce a tree.");next.transform.SetParent(Host.transform,false);if(tree)UnityEngine.Object.Destroy(tree);tree=next;}finally{UnityEngine.Random.state=random;}
        }
        public override void Dispose(){foreach(var element in ownedElements)if(element)UnityEngine.Object.Destroy(element);ownedElements=Array.Empty<PipelineElement>();}
    }

}
#endif
