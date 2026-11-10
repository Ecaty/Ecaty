using UnityEditor;
using UnityEngine;

namespace Ecaty.Editor
{
    public static class RuntimeActorMenu
    {
        [MenuItem("Runtime/Runtime engine/Start creature test", true)]
        private static bool Available() => EditorApplication.isPlaying && Object.FindAnyObjectByType<DioramaRuntime>();
        [MenuItem("Runtime/Runtime engine/Start creature test")]
        private static void Start()
        {
            if (Object.FindAnyObjectByType<RuntimeExperience>()?.Dock?.EnsureWorld() == false) return;
            var engine = Object.FindAnyObjectByType<DioramaRuntime>();
            engine.GetComponent<ClayCreation>().Apply(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreature").text, null);
            engine.Configure(Resources.Load<TextAsset>("Runtime/Examples/RuntimeCreatureControl").text);
        }
        [MenuItem("Runtime/Runtime engine/Start spatial demo", true)]
        private static bool SpatialAvailable() => Available();
        [MenuItem("Runtime/Runtime engine/Start spatial demo")]
        private static void Spatial()
        {
            Start();
            var engine = Object.FindAnyObjectByType<DioramaRuntime>();
            engine.Configure("{\"driver\":\"script\",\"motion\":\"spatial\",\"gravityScale\":0}");
            var session = engine.GetComponent<Ecaty.Flecs.FlecsWorld>().Session;
            session.SetManagedScript("DemoSpatialSettings", "struct HoverParameters(height: f32, sway: f32)\nMotionTuning { HoverParameters: {1.8,0.25} }");
            session.SetManagedScript(EcatyAgentTools.BehaviorEntity, Resources.Load<TextAsset>("Runtime/Behaviors/RuntimeSpatialBehavior").text);
        }
        [MenuItem("Runtime/Runtime engine/Land spatial demo")]
        private static void Land()
        {
            var engine = Object.FindAnyObjectByType<DioramaRuntime>();
            if (!engine || !engine.Motor) return;
            engine.GetComponent<Ecaty.Flecs.FlecsWorld>().Session.SetManagedScript(EcatyAgentTools.BehaviorEntity, "// Stopped");
            engine.Configure("{\"driver\":\"idle\",\"motion\":\"ground\",\"gravityScale\":1}");
        }
        [MenuItem("Runtime/Runtime engine/Take player control")]
        private static void Player() => Object.FindAnyObjectByType<DioramaRuntime>()?.SetDriver(1);
        [MenuItem("Runtime/Runtime engine/Follow target")]
        private static void Follow() => Object.FindAnyObjectByType<DioramaRuntime>()?.SetGoal(new Vector3(3,0,2));
        [MenuItem("Runtime/Runtime engine/Stop")]
        private static void Stop() => Object.FindAnyObjectByType<DioramaRuntime>()?.SetDriver(0);
    }
}
