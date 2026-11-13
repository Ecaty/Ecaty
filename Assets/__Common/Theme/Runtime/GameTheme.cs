using Runtime.Agents.UI;
using UnityEngine;
#if UNITY_EDITOR && ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
namespace Game
{
    /// <summary>Shared product presentation template; world-authored colours remain world data.</summary>
    [CreateAssetMenu(menuName = "Runtime/Theme", fileName = "Theme")]
    public sealed class GameTheme : ScriptableObject
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
        static GameTheme fallback, runtime;
#if UNITY_EDITOR
        static long signature;
#endif

        /// <summary>The theme loaded from ThemeConfig.flecs through Flecs when available, otherwise the code defaults.</summary>
        public static GameTheme Default
        {
            get
            {
                if (runtime)
                    return runtime;
                if (!fallback)
                    fallback = Create();
                return fallback;
            }
        }

        static GameTheme Create()
        {
            var theme = CreateInstance<GameTheme>();
            theme.hideFlags = HideFlags.HideAndDontSave;
            return theme;
        }

        /// <summary>Loads ThemeConfig.flecs (generated live from Theme.asset in the Editor) into the runtime theme.</summary>
        public static void LoadRuntime(Runtime.Flecs.FlecsSession session)
        {
            string source = null;
#if UNITY_EDITOR
            var asset = ThemeScript.EditorAsset;
            if (asset)
            {
                signature = FieldSignature.Of(asset);
                source = ThemeScript.Generate(asset);
            }
#endif
            source ??= RuntimeComposition.Current.ThemeConfig
                ? RuntimeComposition.Current.ThemeConfig.text
                : throw new System.InvalidOperationException("Theme config script missing.");
            if (!runtime)
                runtime = Create();
            ThemeScript.Load(session, source, runtime);
        }

#if UNITY_EDITOR
        /// <summary>Reloads the runtime theme when Theme.asset changed. Returns true when it reloaded.</summary>
        public static bool RefreshFromEditor(Runtime.Flecs.FlecsSession session)
        {
            var asset = ThemeScript.EditorAsset;
            if (!asset || (runtime && FieldSignature.Of(asset) == signature))
                return false;
            LoadRuntime(session);
            return true;
        }

        void OnValidate()
        {
            // Keep the shipped Flecs Script (ThemeConfig.flecs) in step with this authoring asset.
            if (UnityEditor.AssetDatabase.GetAssetPath(this) == ThemeScript.AssetPath)
                UnityEditor.EditorApplication.delayCall += () => { if (this) ThemeScript.Export(this); };
        }
#endif
    }
}
