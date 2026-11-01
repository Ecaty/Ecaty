using Ecaty.Flecs;
using Ecaty.Agents;
using Ecaty.Agents.UI;
using Ecaty.Agents.Diagnostics;
using Ecaty.Agents.Providers.ChatGPT;
using UnityEngine;

namespace Ecaty
{
    public static class EcatyBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Object.FindAnyObjectByType<FlecsWorld>()) return;
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
            try
            {
                AgentTrace.Configure(System.IO.Path.Combine(Application.dataPath, "../Logs/Agents"), Application.isEditor);
                var provider = new ChatGptProvider("Ecaty");
                AgentChatPanel.Create(Resources.Load<AgentUiAssets>("EcatyAgentUi"), provider, provider,
                    Resources.Load<TextAsset>("AgentInstructions").text, EcatyAgentTools.Create(world, new AgentMainThread()),
                    "Continue with ChatGPT", "");
            }
            catch (System.Exception error) { Debug.LogException(error); }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-flecsSmoke") >= 0)
                world.gameObject.AddComponent<PlayerSmoke>();
        }
    }
}
