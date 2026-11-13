#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
namespace Game.Editor
{
    [CustomEditor(typeof(GameTheme)), CanEditMultipleObjects]
    public sealed class ThemeInspector : OdinEditor
    {
    }
}
#endif
