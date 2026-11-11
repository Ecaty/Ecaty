using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEditor.Compilation;
namespace Ecaty.Editor
{
    public static class PlayerCodeVerification
    {
        [MenuItem("Runtime/Tests/Player scripts")]
        public static void Menu() => UnityEngine.Debug.Log(Run("Tests/Results/PlayerScripts"));
        public static object Run(string outputDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before compiling player code.");
            string[] owners = { "Assets/__Common/", "Assets/_Features/", "Assets/_Settings/", "Assets/_Adapters/", "Assets/_Tests/", "Packages/com.ecaty.flecs-unity/", "Packages/com.ecaty.ai-agent/", "Packages/com.runtime.capabilities/" };
            var player = CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var leaked = player.SelectMany(a => a.sourceFiles).Select(p => p.Replace('\\', '/')).Where(p => owners.Any(o => p.StartsWith(o, StringComparison.Ordinal)) && p.Contains("/Editor/")).ToArray();
            if (leaked.Length > 0)
                throw new InvalidOperationException("Editor sources entered player assemblies: " + string.Join(", ", leaked));
            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            string[] required = { "Assembly-CSharp.dll", "Runtime.Capabilities.dll", "Ecaty.Flecs.dll", "Ecaty.Agents.dll" };
            var targets = new JArray();
            bool passed = true;
            foreach (var (target, group, name) in new[] { (BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Windows64"), (BuildTarget.Android, BuildTargetGroup.Android, "Android") })
            {
                string targetDirectory = Path.Combine(directory, name);
                Directory.CreateDirectory(targetDirectory);
                var settings = new ScriptCompilationSettings { target = target, group = group, options = ScriptCompilationOptions.DevelopmentBuild };
                var compiled = PlayerBuildInterface.CompilePlayerScripts(settings, targetDirectory);
                var names = compiled.assemblies.Select(Path.GetFileName).ToArray();
                var missing = required.Where(n => !names.Contains(n)).ToArray();
                passed &= missing.Length == 0;
                targets.Add(new JObject { ["target"] = name, ["assemblyCount"] = names.Length, ["missing"] = new JArray(missing) });
            }
            var report = new JObject { ["passed"] = passed, ["targets"] = targets, ["editorSourceLeaks"] = new JArray(leaked), ["directory"] = directory };
            File.WriteAllText("Verification/player-script-checks.json", report.ToString());
            if (!passed)
                throw new InvalidOperationException(report.ToString());
            return report;
        }
    }
}
