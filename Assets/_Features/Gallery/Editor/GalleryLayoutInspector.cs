#if ODIN_INSPECTOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
namespace Ecaty.Editor
{
    [CustomEditor(typeof(GalleryLayoutProfile)), CanEditMultipleObjects]
    public sealed class GalleryLayoutInspector : OdinEditor
    {
    }
}
#endif
