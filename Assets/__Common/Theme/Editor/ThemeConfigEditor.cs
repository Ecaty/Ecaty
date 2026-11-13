using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
#if ODIN_VALIDATOR
using Sirenix.OdinInspector.Editor.Validation;
[assembly: RegisterValidator(typeof(Game.Editor.ThemeConfigValidator))]
#endif
namespace Game.Editor
{
    public static class ThemeConfigMenu
    {
        [MenuItem("Runtime/Theme/Export config")]
        public static void Export()
        {
            ThemeScript.Export();
        }
    }

    // Builds always ship a ThemeConfig.flecs that matches Theme.asset.
    sealed class ThemeConfigBuildStep : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            ThemeScript.Export();
        }
    }
#if ODIN_VALIDATOR
    public sealed class ThemeConfigValidator : RootObjectValidator<GameTheme>
    {
        protected override void Validate(ValidationResult result)
        {
            if (AssetDatabase.GetAssetPath(Value) == ThemeScript.AssetPath && !ThemeScript.IsCurrent())
                result.AddError("ThemeConfig.flecs is out of date with Theme.asset; edit any value or run Runtime > Theme > Export config.");
        }
    }
#endif
}
