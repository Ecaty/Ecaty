using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Creates, steps (in FixedUpdate) and disposes the body presenter of one FlecsWorld.
    /// </summary>
    public sealed class BodyAdapter : MonoBehaviour
    {
        public BodyPresenter Bodies
        {
            get; private set;
        }

        public void Initialize(FlecsWorld world)
        {
            Shutdown();
            Bodies = new BodyPresenter(world);
        }

        void FixedUpdate() => Bodies?.Step(Time.fixedDeltaTime);

        void Shutdown()
        {
            Bodies?.Dispose();
            Bodies = null;
        }

        void OnDestroy() => Shutdown();
    }
}
