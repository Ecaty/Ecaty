#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
namespace Ecaty.Editor
{
    [CustomEditor(typeof(EcatyTheme)), CanEditMultipleObjects]
    public sealed class ThemeInspector : OdinEditor
    {
    }
}
#endif
