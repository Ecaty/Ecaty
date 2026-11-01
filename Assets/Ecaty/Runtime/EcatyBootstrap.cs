using Ecaty.Flecs;
using UnityEngine;

namespace Ecaty
{
    public static class EcatyBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Object.FindFirstObjectByType<FlecsWorld>()) return;
            Application.runInBackground = true;
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>("EcatyWorld"), Resources.Load<Material>("EcatyMaterial"));
            var cameraObject = new GameObject("Ecaty Camera");
            Object.DontDestroyOnLoad(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1, -10);
            camera.orthographic = true;
            camera.orthographicSize = 4;
            camera.depth = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-flecsSmoke") >= 0)
                world.gameObject.AddComponent<PlayerSmoke>();
        }
    }
}
