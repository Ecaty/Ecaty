using Ecaty.Agents.UI;
using UnityEngine;
#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
namespace Ecaty
{
    /// <summary>Shared product presentation template; world-authored colours remain world data.</summary>
    [CreateAssetMenu(menuName = "Runtime/Theme", fileName = "Theme")]
    public sealed class EcatyTheme : ScriptableObject
    {
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color Background = new(.094f, .145f, .165f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color Glass = new(.72f, .83f, .84f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color Ring = new(.57f, .67f, .63f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color Selection = new(.9f, .81f, .6f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        [UnityEngine.Serialization.FormerlySerializedAs("Recall")]
        public Color LoadedWorld = new(.55f, .84f, 1);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Gallery", SdfIconType.Palette, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color SeedTint = Color.white;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Tools", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        public Color ToolAccent = new(.80f, .88f, .79f);
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Tools", SdfIconType.Mouse, TabLayouting = TabLayouting.Shrink)]
#endif
        [Range(0, 1)] public float ToolTintStrength = .18f;
#if UNITY_EDITOR && ODIN_INSPECTOR
        [TabGroup("Theme", "Interface", SdfIconType.Sliders, TabLayouting = TabLayouting.Shrink)]
#endif
        public AgentUiPalette UI = new();
        static EcatyTheme fallback;
        public static EcatyTheme Default
        {
            get
            {
                var asset = RuntimeComposition.Current.Theme;
                if (asset)
                    return asset;
                if (!fallback)
                {
                    fallback = CreateInstance<EcatyTheme>();
                    fallback.hideFlags = HideFlags.HideAndDontSave;
                }
                return fallback;
            }
        }
    }
}
