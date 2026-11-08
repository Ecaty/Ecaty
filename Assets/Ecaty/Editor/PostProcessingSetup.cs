using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ecaty.Editor
{
    public sealed class PostProcessingSetup : IPreprocessBuildWithReport
    {
        private const string AssetPath = "Assets/Ecaty/Resources/WorkshopPostProcessing.asset";
        public int callbackOrder => -1000;

        [MenuItem("Ecaty/Workshop/Prepare post-processing")]
        public static void Prepare()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);
            if (profile) return;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "WorkshopPostProcessing";
            profile.Add<ColorAdjustments>(true);
            profile.Add<WhiteBalance>(true);
            profile.Add<Bloom>(true).intensity.Override(.2f);
            profile.Add<Vignette>(true).intensity.Override(.2f);
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            AssetDatabase.CreateAsset(profile, AssetPath);
            foreach (var component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        public void OnPreprocessBuild(BuildReport report) => Prepare();
    }
}
