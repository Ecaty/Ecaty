using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_UNISTORM
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UniStorm;
#endif
namespace Ecaty.Flecs.PluginAdapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapWeather", "weather", 45, Gate = "ECATY_CAP_UNISTORM", Probe = "Assets/UniStorm Weather System/Scripts/System/UniStormSystem.cs")]
    public sealed class WeatherConfig
    {
        public int hour = 12, minute, cloud;
        [AssetKey] public string weather = "";
    }
#if ECATY_CAP_UNISTORM
    public sealed class WeatherCapability : CapabilityInstance<WeatherConfig>
    {
        UniStormSystem system; UniStormManager manager; WeatherConfig config;
        public WeatherCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(WeatherConfig j)
        {
            config = j;
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
            manager.SetTime(Mathf.Clamp(config.hour, 0, 23), Mathf.Clamp(config.minute, 0, 59));
            manager.UpdateCloudType(config.cloud == 1 ? UniStormSystem.CloudTypeEnum._2D : UniStormSystem.CloudTypeEnum.Volumetric);
            var weather = Context.Asset<WeatherType>(config.weather);
            if (weather)
                manager.ChangeWeatherWithTransition(weather);
            config = null;
        }
    }
#endif
}
