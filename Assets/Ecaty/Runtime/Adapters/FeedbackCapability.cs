#if ECATY_CAP_FEEL
using System;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MoreMountains.Feedbacks;
namespace Ecaty.Flecs.PluginAdapters {
    public sealed class FeedbackCapability:CapabilityInstance {
        GameObject instance;string prefabKey;MMF_Player player;int trigger=int.MinValue;
        public FeedbackCapability(CapabilityContext c):base(c){}
        public override void Apply(JObject j){if(player&&prefabKey!=j.S("prefab"))Dispose();if(!player){prefabKey=j.S("prefab");trigger=int.MinValue;var prefab=Context.Asset<GameObject>(j.S("prefab"));if(!prefab)throw new ArgumentException("Register a Feel feedback palette prefab.");instance=UnityEngine.Object.Instantiate(prefab,Host.transform);player=instance.GetComponent<MMF_Player>();if(!player)throw new ArgumentException("Feedback palette has no MMF_Player.");player.Initialization();}if(trigger!=j.I("trigger")){trigger=j.I("trigger");player.PlayFeedbacks(Target.position);}}
        public override void Dispose(){if(player)player.StopFeedbacks();if(instance){instance.SetActive(false);UnityEngine.Object.Destroy(instance);}instance=null;player=null;}
    }

}
#endif
