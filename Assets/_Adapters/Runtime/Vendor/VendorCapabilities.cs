using Ecaty.Flecs.Capabilities;
namespace Ecaty.Flecs.PluginAdapters {
    public static class VendorCapabilities {
        public static void Register(CapabilityRuntime runtime){
#if ECATY_CAP_SPLINES
            runtime.Register(new Adapter<SplineCapability>("CapSpline","spline geometry",0,c=>new(c)));
#endif
#if ECATY_CAP_COZY
            runtime.Register(new Adapter<BuildingCapability>("CapBuilding","buildings",10,c=>new(c)));
#endif
#if ECATY_CAP_BROCCOLI
            runtime.Register(new Adapter<VegetationCapability>("CapVegetation","vegetation",15,c=>new(c)));
#endif
#if ECATY_CAP_FLEXALON
            runtime.Register(new Adapter<LayoutCapability>("CapLayout","spatial layouts",45,c=>new(c)));
#endif
#if ECATY_CAP_FEEL
            runtime.Register(new Adapter<FeedbackCapability>("CapFeedback","feedback",45,c=>new(c)));
#endif
#if ECATY_CAP_UNISTORM
            runtime.Register(new Adapter<WeatherCapability>("CapWeather","weather",45,c=>new(c)));
#endif
#if ECATY_CAP_FEEL
            runtime.Register(new Adapter<SequenceCapability>("CapSequence","feedback sequencing",45,c=>new(c)));
#endif
#if ECATY_CAP_CREST
            runtime.Register(new Adapter<OceanCapability>("CapOcean","ocean rendering",45,c=>new(c)));
#endif
        }
    }
}
