using UnityEditor;
using UnityEngine;
namespace Ecaty.Editor
{
    public static class GalleryDesignerMenu
    {
        [MenuItem("Runtime/Gallery/Layout and ring controls")]
        public static void Layout(){Selection.activeObject=RuntimeComposition.Current.Gallery;EditorGUIUtility.PingObject(Selection.activeObject);}
        [MenuItem("Runtime/Gallery/Shuffle ring travel")]
        public static void Shuffle(){var layout=RuntimeComposition.Current.Gallery;Undo.RecordObject(layout,"Shuffle ring travel");layout.RingTravelSeed=new System.Random().Next();EditorUtility.SetDirty(layout);AssetDatabase.SaveAssets();}
        [MenuItem("Runtime/Gallery/Theme colours")]
        public static void Theme(){Selection.activeObject=RuntimeComposition.Current.Theme;EditorGUIUtility.PingObject(Selection.activeObject);}
    }
}
