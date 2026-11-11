using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_FEEL
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MoreMountains.Feedbacks;
#endif
namespace Ecaty.Flecs.PluginAdapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapFeedback", "feedback", 45, Gate = "ECATY_CAP_FEEL", Probe = "Assets/Feel/MMFeedbacks/MMFeedbacks/Sequencing/Scripts/MMSoundSequencer.cs")]
    public sealed class FeedbackConfig
    {
        [AssetKey] public string prefab = "";
        public int trigger;
    }
#if ECATY_CAP_FEEL
    public sealed class FeedbackCapability : CapabilityInstance<FeedbackConfig>
    {
        GameObject instance; string prefabKey; MMF_Player player; int trigger = int.MinValue;
        public FeedbackCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(FeedbackConfig j)
        {
            if (player && prefabKey != j.prefab)
                Dispose();
            if (!player)
            {
                prefabKey = j.prefab;
                trigger = int.MinValue;
                var prefab = Context.Asset<GameObject>(j.prefab);
                if (!prefab)
                    throw new ArgumentException("Register a Feel feedback palette prefab.");
                instance = UnityEngine.Object.Instantiate(prefab, Host.transform);
                player = instance.GetComponent<MMF_Player>();
                if (!player)
                    throw new ArgumentException("Feedback palette has no MMF_Player.");
                player.Initialization();
            }
            if (trigger != j.trigger)
            {
                trigger = j.trigger;
                player.PlayFeedbacks(Target.position);
            }
        }
        public override void Dispose()
        {
            if (player)
                player.StopFeedbacks();
            if (instance)
            {
                instance.SetActive(false);
                UnityEngine.Object.Destroy(instance);
            }
            instance = null;
            player = null;
        }
    }
#endif
}
