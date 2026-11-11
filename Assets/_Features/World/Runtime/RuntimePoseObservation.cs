using UnityEngine;
namespace Ecaty
{
    [DefaultExecutionOrder(25000)]
    public sealed class RuntimePoseObservation : MonoBehaviour
    {
        public RuntimePoseBridge Bridge
        {
            get; set;
        }
        private void LateUpdate()
        {
            if (Bridge)
                Bridge.Publish();
        }
    }
}
