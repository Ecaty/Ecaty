#if ECATY_CAP_UNISTORM
using System;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UniStorm;
namespace Ecaty.Flecs.PluginAdapters
{
    public sealed class WeatherCapability : CapabilityInstance
    {
        UniStormSystem system; UniStormManager manager; JObject config;
        public WeatherCapability(CapabilityContext c) : base(c) { }
        public override void Apply(JObject j)
        {
            config = (JObject)j.DeepClone();
            if (!system)
            {
                if (UniStormSystem.Instance)
                    throw new InvalidOperationException("This UniStorm renderer supports one global sky. Reuse its capability.");
                var prefab = Resources.Load<GameObject>("UniStorm System");
                if (!prefab)
                    throw new ArgumentException("UniStorm system prefab is missing.");
                var instance = UnityEngine.Object.Instantiate(prefab, Host.transform);
                system = instance.GetComponent<UniStormSystem>();
                manager = instance.GetComponent<UniStormManager>();
                var camera = Context.Runtime.ViewCamera ? Context.Runtime.ViewCamera : Camera.main ?? UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (!camera)
                    throw new ArgumentException("Weather needs a rendering camera.");
                system.PlayerCamera = camera;
                system.PlayerTransform = camera.transform;
            }
        }
        public override void Tick(float delta)
        {
            if (config == null || !system.UniStormInitialized)
                return;
            manager.SetTime(Mathf.Clamp(config.I("hour", 12), 0, 23), Mathf.Clamp(config.I("minute"), 0, 59));
            manager.UpdateCloudType(config.I("cloud") == 1 ? UniStormSystem.CloudTypeEnum._2D : UniStormSystem.CloudTypeEnum.Volumetric);
            var weather = Context.Asset<WeatherType>(config.S("weather"));
            if (weather)
                manager.ChangeWeatherWithTransition(weather);
            config = null;
        }
    }

}
#endif
