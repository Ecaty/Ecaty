using Ecaty.Agents.UI;
using UnityEditor;

namespace Ecaty.Editor
{
    public static class ClayPhotoMenu
    {
        [MenuItem("Runtime/Attach reference photo…", true)]
        private static bool CanChoose()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
            return EditorApplication.isPlaying && panel && panel.Controller.CanPickPhoto;
        }

        [MenuItem("Runtime/Attach reference photo…")]
        private static void Choose()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
            if (panel) panel.Controller.PickPhoto();
        }

        [MenuItem("Runtime/Remove reference photo", true)]
        private static bool CanRemove()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
            return EditorApplication.isPlaying && panel && panel.Controller.HasPhotoAttachment && !panel.Controller.IsBusy;
        }

        [MenuItem("Runtime/Remove reference photo")]
        private static void Remove()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<AgentChatPanel>();
            if (panel) panel.Controller.RemovePhotoAttachment();
        }
    }
}
