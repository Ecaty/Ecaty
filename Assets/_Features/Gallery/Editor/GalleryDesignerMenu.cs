using UnityEditor;
using UnityEngine;
namespace Game.Editor
{
    public static class GalleryDesignerMenu
    {
        [MenuItem("Runtime/Gallery/Layout and ring controls")]
        public static void Layout()
        {
            Selection.activeObject = GalleryConfigScript.EditorAsset;
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
        [MenuItem("Runtime/Gallery/Shuffle ring travel")]
        public static void Shuffle()
        {
            var layout = GalleryConfigScript.EditorAsset;
            Undo.RecordObject(layout, "Shuffle ring travel");
            layout.RingTravelSeed = new System.Random().Next();
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            GalleryConfigScript.Export(layout);
        }
        [MenuItem("Runtime/Gallery/Export config")]
        public static void Export()
        {
            GalleryConfigScript.Export();
        }
        [MenuItem("Runtime/Gallery/Theme colours")]
        public static void Theme()
        {
            Selection.activeObject = ThemeScript.EditorAsset;
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
    // Builds always ship a GalleryConfig.flecs that matches Gallery.asset.
    sealed class GalleryConfigBuildStep : UnityEditor.Build.IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            GalleryConfigScript.Export();
        }
    }
}
