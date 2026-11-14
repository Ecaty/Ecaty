using Runtime.Flecs;
using UnityEngine;

namespace Runtime.Capabilities
{
    /// <summary>
    /// Creates, ticks and disposes the sound presenter of one FlecsWorld, with the sources that give it samples. Runs after the
    /// world has synced its views.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class SoundAdapter : MonoBehaviour
    {
        public SoundSources Sources
        {
            get; private set;
        }
        public SoundPresenter Sounds
        {
            get; private set;
        }

        /// <param name="folder">The folder with the sound files (WAV), for example a Sounds folder under the app's data path.</param>
        public void Initialize(FlecsWorld world, string folder = null)
        {
            Shutdown();
            Sources = new SoundSources(folder);
            Sounds = new SoundPresenter(world, Sources);
        }

        void LateUpdate() => Sounds?.Tick();

        void Shutdown()
        {
            Sounds?.Dispose();
            Sounds = null;
            Sources = null;
        }

        void OnDestroy() => Shutdown();
    }
}
