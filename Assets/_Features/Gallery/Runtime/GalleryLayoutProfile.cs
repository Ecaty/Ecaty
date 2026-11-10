using UnityEngine;
#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace Ecaty
{
    /// <summary>Designer defaults for the proposed gallery presentation, not live world or save state.</summary>
    [CreateAssetMenu(menuName = "Runtime/Gallery Layout Profile", fileName = "GalleryLayoutProfile")]
    public sealed class GalleryLayoutProfile : ScriptableObject
    {
        public enum GuideStyle { Particles, Ribbon, Orbit, Hidden, TravelingGlow, Dashes, GalaxyDust }

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Normalized world cells")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Cells", SdfIconType.Grid3x3Gap, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float WorldDiameter = 1.65f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Cells", SdfIconType.Grid3x3Gap, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(0)] public float WorldSpacing = .77f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Cells", SdfIconType.Grid3x3Gap, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float LayerSpacing = 2.9f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Layer guides")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public GuideStyle LayerGuide = GuideStyle.TravelingGlow;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public bool MatchGuideWidthToWorld = false;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.01f)] public float LayerGuideWidth = 1f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float LayerOpacity = .5f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public Material LayerMaterial;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.1f,2)] public float GalaxyDensity=1;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.2f,5)] public float GalaxyDetail=1;

#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public Material ParticleMaterial;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(0)] public float ParticleSize = .025f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(1)] public int ParticlesPerLayer = 180;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Traveling ring effect")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public float RingTravelSpeed = .22f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(2,64)] public float RingGlowSharpness = 18;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0,3)] public float RingGlowIntensity = 1.25f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Visibility and picking")]
#endif
        [Tooltip("Covered fraction needed for full occlusion fade. Lower values fade at the first overlap.")]
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Visibility", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.005f,.5f)] public float OcclusionFadeCoverage=.08f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Rings", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public int RingTravelSeed=1977;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Visibility", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.98f,1)] public float MinimumPickOpacity = .98f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Visibility", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.8f,2)] public float RingHitWidthInWorlds = 1;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Visibility", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.02f)] public float VisibilityRefreshInterval = .05f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("World spheres")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public Material WorldSphereMaterial;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float WorldSphereOpacity = .72f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float SphereOpacity = .2f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float RearWorldFade = .98f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public bool Frosted = true;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        public EcatyTheme Theme;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [HideInInspector] public Color RingTint = new Color(.57f, .67f, .63f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.04f)] public float RingClearanceFeather = .35f;

#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Spheres", SdfIconType.Circle, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.2f, 3)] public float RingSurfaceSoftness = 1.6f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Navigation and transitions")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float FocusSmoothTime = .65f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Return", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.5f)] public float ReturnSeconds=1.9f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Return", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.3f)] public float ReturnRevealSeconds=1.55f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Return", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [UnityEngine.Serialization.FormerlySerializedAs("ReturnRecallStrength")]
        [Range(0,1)] public float LoadedWorldStrength=.9f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Return", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.2f)] public float GlassPassSeconds=.7f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Return", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0,1)] public float GlassPassStrength=.55f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Visibility", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.2f,.9f)] public float ApproachHighlightFade=.55f;

#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(1)] public float FocusMaxSpeed = 10;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float ZoomSmoothTime = .48f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float SwapFadeTime = .28f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(.6f, 1)] public float FullTreeZoom = .9f;
        [Tooltip("Brief separation between the elastic boundary and the next outward push; no long quiet-scroll lock.")]
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.02f)] public float ExitBoundaryPause = .08f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(1.1f)] public float ExitDistanceMultiplier = 2.2f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0,.25f)] public float ExitElasticZoom = .055f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0,1)] public float ExitVignette = .3f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Camera", SdfIconType.Camera, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.1f)] public float ExitAlignmentSeconds = .45f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Idle rotation")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Motion", SdfIconType.ArrowsMove, TabLayouting = TabLayouting.Shrink)]
#endif
        public bool IdleRotationEnabled = true;
        [Tooltip("Continuous gallery rotation, including during hover. Held placement freezes the layout.")]
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Motion", SdfIconType.ArrowsMove, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(0)] public float IdleDegreesPerSecond = .974028f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Motion", SdfIconType.ArrowsMove, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.01f)] public float LayoutResponse = 7;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Motion", SdfIconType.ArrowsMove, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(.02f)] public float RingRotationSmoothTime = .18f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Creation invitation")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Creation", SdfIconType.Stars, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, .25f)] public float InvitationPulseAmplitude = .12f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Creation", SdfIconType.Stars, TabLayouting = TabLayouting.Shrink)]
#endif
        [Min(0)] public float InvitationPulseFrequency = .2f;

#if !UNITY_EDITOR || !ODIN_INSPECTOR
        [Header("Placement preview")]
#endif
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Placement", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float HeldGhostOpacity = .25f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Placement", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float DestinationGhostOpacity = .58f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Gallery", "Placement", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float UnselectedWorldOpacityDuringSwap = .16f;

        public float EffectiveGuideWidth => MatchGuideWidthToWorld ? WorldDiameter : LayerGuideWidth;
        public float WorldPitch => WorldDiameter + WorldSpacing;

        void OnValidate()
        {
            RingRotationSmoothTime=Positive(RingRotationSmoothTime,.18f,.02f);
            MinimumPickOpacity=Mathf.Clamp(MinimumPickOpacity,.98f,1);
            FocusSmoothTime = Positive(FocusSmoothTime,.65f,.1f);
            FocusMaxSpeed = Positive(FocusMaxSpeed,10,1);
            ZoomSmoothTime = Positive(ZoomSmoothTime,.48f,.1f);
            SwapFadeTime = Positive(SwapFadeTime,.28f,.1f);
            ExitBoundaryPause = Positive(ExitBoundaryPause,.08f,.02f);
            ExitDistanceMultiplier = Positive(ExitDistanceMultiplier,2.2f,1.1f);
            ExitAlignmentSeconds=Positive(ExitAlignmentSeconds,.45f,.1f);
            VisibilityRefreshInterval=Positive(VisibilityRefreshInterval,.05f,.02f);
            RingSurfaceSoftness = Mathf.Clamp(Positive(RingSurfaceSoftness,1.6f,.2f),.2f,3);
            ReturnSeconds=Positive(ReturnSeconds,1.9f,.5f);
            ReturnRevealSeconds=Positive(ReturnRevealSeconds,1.55f,.3f);
            LoadedWorldStrength=Unit(LoadedWorldStrength,.9f);GlassPassSeconds=Positive(GlassPassSeconds,.7f,.2f);GlassPassStrength=Unit(GlassPassStrength,.55f);ApproachHighlightFade=Mathf.Clamp(Positive(ApproachHighlightFade,.55f,.2f),.2f,.9f);
            GalaxyDensity=Mathf.Clamp(Positive(GalaxyDensity,1,.1f),.1f,2);
            GalaxyDetail=Mathf.Clamp(Positive(GalaxyDetail,1,.2f),.2f,5);
            FullTreeZoom = Mathf.Clamp(Positive(FullTreeZoom,.9f,.6f),.6f,1);
            WorldDiameter = Positive(WorldDiameter, 1.65f, .1f);
            WorldSpacing = Positive(WorldSpacing, .38f, 0);
            LayerSpacing = Positive(LayerSpacing, 2, .1f);
            LayerGuideWidth = Positive(LayerGuideWidth, 1.65f, .01f);
            ParticleSize = Positive(ParticleSize, .025f, 0);
            ParticlesPerLayer = Mathf.Max(1, ParticlesPerLayer);
            IdleDegreesPerSecond = Positive(IdleDegreesPerSecond, .5f, 0);
            LayoutResponse = Positive(LayoutResponse, 7, .01f);
            InvitationPulseFrequency = Positive(InvitationPulseFrequency, .2f, 0);
            LayerOpacity = Unit(LayerOpacity, .5f);
            SphereOpacity = Unit(SphereOpacity, .2f);
            RearWorldFade = Unit(RearWorldFade, .98f);
            RingClearanceFeather = Positive(RingClearanceFeather, .35f, .04f);
            WorldSphereOpacity = Unit(WorldSphereOpacity, .42f);
            HeldGhostOpacity = Unit(HeldGhostOpacity, .25f);
            DestinationGhostOpacity = Unit(DestinationGhostOpacity, .58f);
            UnselectedWorldOpacityDuringSwap = Unit(UnselectedWorldOpacityDuringSwap, .16f);
        }

        static float Positive(float value, float fallback, float minimum) =>
            float.IsFinite(value) ? Mathf.Max(minimum, value) : Mathf.Max(minimum, fallback);
        static float Unit(float value, float fallback) => float.IsFinite(value) ? Mathf.Clamp01(value) : fallback;
    }
}
