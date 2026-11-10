using UnityEngine;
namespace Ecaty
{
    [DefaultExecutionOrder(-30000)]
    public sealed class RuntimePoseReset : MonoBehaviour
    {
        public RuntimePoseBridge Bridge { get; set; }
        private void Update() { if (Bridge) Bridge.Restore(); }
    }
}
