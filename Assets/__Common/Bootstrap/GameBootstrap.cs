using Runtime.Flecs;
using Runtime.Capabilities;
using Game.Adapters;
using Runtime.Agents;
using Runtime.Agents.UI;
using Runtime.Agents.Diagnostics;
using Runtime.Agents.Providers.ChatGPT;
using Runtime.Agents.Providers.OpenRouter;
using Runtime.Agents.Providers.Gemini;
using Runtime.Agents.Providers.Local;
using Runtime.Agents.Speech;
using UnityEngine;

namespace Game
{
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Object.FindAnyObjectByType<FlecsWorld>())
                return;
            Application.runInBackground = true;
            var args = System.Environment.GetCommandLineArgs();
            bool smoke = System.Array.IndexOf(args, "-flecsSmoke") >= 0;
            bool worldFixture = System.Array.IndexOf(args, "-capabilitySmokeResult") >= 0 ||
                System.Array.IndexOf(args, "-runtimeEngineSmoke") >= 0 ||
                System.Array.IndexOf(args, "-localAiSmokeResult") >= 0;
            bool gallery = !smoke && !worldFixture;
#if UNITY_EDITOR
            gallery &= !UnityEditor.SessionState.GetBool("Game.WorkshopChainVerification", false)
                && !UnityEditor.SessionState.GetBool("Game.VerifyCapabilities", false)
                && !UnityEditor.SessionState.GetBool("Game.CapabilityWorkshop", false);
#endif
#if !CAP_FLEXALON
            gallery = false;
#endif
            string source = smoke ? "Tests/Fixtures/VerificationWorld" : worldFixture ? "Runtime/Examples/World" : gallery ? "Gallery/Examples/GalleryCatalog" : "Workshop/Runtime/WorkshopLobby";
            var world = FlecsBootstrap.Create(Resources.Load<TextAsset>(source), Resources.Load<Material>("Shared/Rendering/PresentationMaterial"));
            GameTheme.LoadRuntime(world.Session);
            var cameraObject = new GameObject("World Camera");
            Object.DontDestroyOnLoad(cameraObject);
            var camera = cameraObject.AddComponent<Camera>();
            if (!Object.FindAnyObjectByType<AudioListener>())
                cameraObject.AddComponent<AudioListener>();
            camera.transform.position = smoke ? new Vector3(0, 1, -10) : new Vector3(0, 7, -10);
            camera.transform.LookAt(new Vector3(0, .5f, 0));
            camera.orthographic = true;
            camera.orthographicSize = smoke ? 4 : worldFixture ? 5.5f : 2.4f;
            camera.depth = 100;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.1f);
            try
            {
                AgentTraceBrowser.Initialize(Application.isEditor
                    ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Logs/Agents"))
                    : System.IO.Path.Combine(Application.persistentDataPath, "Diagnostics/Agents"),
                    "Runtime · " + Application.platform);
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-agentTraceBrowser") >= 0)
                    AgentTraceBrowser.Current?.OpenBrowser();
                string localRoot = Application.isEditor ? System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../LocalData/AI")) : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../LocalAI"));
                int localAt = System.Array.IndexOf(args, "-localAiRoot");
                if (localAt >= 0 && localAt + 1 < args.Length)
                    localRoot = args[localAt + 1];
                var local = new LocalAiServices(localRoot);
                var provider = new AgentProviderHub(new[] {
                    new AgentProviderRegistration("local","On device",()=>new LlamaCppProvider(local.LlamaEndpoint,local.StartLlamaAsync,local.StopLlama),"Private, offline AI. Uses this device's memory.",supportsPhotos:true,isLocal:true),
                    new AgentProviderRegistration("openrouter", "OpenRouter", () => new OpenRouterProvider(Application.productName, "cohere/north-mini-code:free"), "Free coding model · Account limits apply."),
                    new AgentProviderRegistration("chatgpt", "ChatGPT", () => new ChatGptProvider(Application.productName), "Uses your eligible ChatGPT plan.", canResetSignIn: true, supportsPhotos: true),
                    new AgentProviderRegistration("gemini", "Gemini", () => new GeminiProvider(Application.productName),
                        "Use a free-tier key. Your Google project controls billing and data use.", "https://aistudio.google.com/api-keys", supportsPhotos: true)
                }, PlayerPrefs.GetString("Agent.Provider", "local"), id => { PlayerPrefs.SetString("Agent.Provider", id); PlayerPrefs.Save(); });
                var clay = world.gameObject.AddComponent<ClayCreation>();
                var engine = world.gameObject.AddComponent<DioramaRuntime>();
                engine.Initialize(world, clay);
                engine.InteractionCamera = camera;
                var experience = world.gameObject.AddComponent<RuntimeExperience>();
                experience.Camera = camera;
                experience.Initialize(world, clay, engine);
                var capabilities = world.gameObject.AddComponent<CapabilityRuntime>();
                capabilities.ViewCamera = camera;
                capabilities.AudioBankProvider = () => System.IO.File.ReadAllBytes(System.IO.Path.Combine(Application.streamingAssetsPath, "Audio/GeneralUser-GS.sf2"));
                capabilities.Assets = RuntimeComposition.Current.Capabilities;
                capabilities.Initialize(world);
                capabilities.RegisterDiscovered();
                world.gameObject.AddComponent<LookAdapter>().Initialize(world, camera);
                capabilities.TargetResolver = id =>
                {
                    foreach (var model in experience.Models.Values)
                    {
                        if (model.Creation.PresentationRoot && world.Session.Lookup(model.Creation.PresentationRoot.name) == id)
                            return model.Actor.Motor ? model.Actor.Motor.transform : model.Creation.PresentationRoot.transform;
                        foreach (var part in model.Creation.RuntimeNodes)
                        if (world.Session.Lookup(part.Key) == id)
                            return part.Value;
                    }
                    return null;
                };
                cameraObject.AddComponent<Unity.Cinemachine.CinemachineBrain>();
                if (!smoke && !gallery)
                {
                    var orbit = cameraObject.AddComponent<WorkshopOrbit>();
                    orbit.Initialize(camera, experience);
                    experience.Navigation = orbit;
                    experience.Workshop = world.gameObject.AddComponent<WorkshopTools>();
                    experience.Workshop.Initialize(experience, orbit);
                }
                var chat = AgentChatPanel.Create(RuntimeComposition.Current.AgentUi, provider, provider,
                    Resources.Load<TextAsset>("Agent/Prompts/AgentInstructions").text, WorldAgentTools.Create(world, new AgentMainThread(), clay, engine),
                    "Connect AI", "", ownedServices: local);
                if (!smoke)
                {
                    experience.Dock = world.gameObject.AddComponent<WorkshopDock>();
                    experience.Dock.Initialize(chat, experience, gallery);
                }
#if CAP_FLEXALON
                if (gallery)
                {
                    experience.enabled = engine.enabled = clay.enabled = false;
                    world.gameObject.AddComponent<CrystalWorldGallery>().Initialize(world, experience, chat);
                }
#endif
                chat.ConfigureSpeech(new WhisperSpeechToText(local.WhisperEndpoint, local.StartWhisperAsync));
                int localTest = System.Array.IndexOf(args, "-localAiSmokeResult");
                if (localTest >= 0 && localTest + 1 < args.Length)
                    world.gameObject.AddComponent<LocalAiSmoke>().Initialize(local, world, args[localTest + 1]);
#if UNITY_EDITOR
                if (ClayCreation.Available)
                    chat.Controller.ConfigurePhotoPicker(ReferencePhotoPicker.Pick,
                        "Create an editable 3D object from this photo.", "apply_photo_object");
#endif
            }
            catch (System.Exception error) { Debug.LogException(error); }
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-flecsSmoke") >= 0)
                world.gameObject.AddComponent<PlayerSmoke>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-capabilitySmokeResult") >= 0)
                world.gameObject.AddComponent<CapabilitySmoke>();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-runtimeEngineSmoke") >= 0)
                world.gameObject.AddComponent<RuntimeExperienceSmoke>();
        }
    }
}
