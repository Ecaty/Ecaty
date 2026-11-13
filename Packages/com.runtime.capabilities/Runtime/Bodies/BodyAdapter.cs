using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Creates, steps and disposes the body presenter of one FlecsWorld. It notes the poses of free bodies just before the
    /// world syncs its views (Update runs before FlecsWorld's), steps in FixedUpdate and puts the bodies' views back under
    /// the world root after the sync (LateUpdate).
    /// </summary>
    [DefaultExecutionOrder(-100)]
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

        void Update() => Bodies?.Capture();

        void FixedUpdate() => Bodies?.Step(Time.fixedDeltaTime);

        void LateUpdate() => Bodies?.Restore();

        void Shutdown()
        {
            Bodies?.Dispose();
            Bodies = null;
        }

        void OnDestroy() => Shutdown();
    }
}
