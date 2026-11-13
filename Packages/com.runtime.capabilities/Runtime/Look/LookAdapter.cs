using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Creates, ticks and disposes the look presenters of one FlecsWorld. Runs after the world has synced its views, so a new
    /// element's material is set in the same frame its view appears.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class LookAdapter : MonoBehaviour
    {
        FlecsWorld world;

        public LookPresenter Looks
        {
            get; private set;
        }
        public WorldStylePresenter Style
        {
            get; private set;
        }

        /// <summary>The camera is only used to find the Volume layer and to switch post-processing on while a style is shown.</summary>
        public void Initialize(FlecsWorld world, Camera camera = null)
        {
            Shutdown();
            this.world = world;
            Looks = new LookPresenter(world);
            Style = new WorldStylePresenter(world, camera);
        }

        void LateUpdate()
        {
            Looks?.Tick();
            Style?.Tick();
        }

        void Shutdown()
        {
            Looks?.Dispose();
            Style?.Dispose();
            Looks = null;
            Style = null;
        }

        void OnDestroy() => Shutdown();
    }
}
