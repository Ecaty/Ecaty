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
        public static void Menu()=>UnityEngine.Debug.Log(Run("Tests/Results/PlayerScripts"));
        public static object Run(string outputDirectory) {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before compiling player code.");
            string[] owners={"Assets/__Common/","Assets/_Features/","Assets/_Settings/","Assets/_Adapters/","Assets/_Tests/","Packages/com.ecaty.flecs-unity/","Packages/com.ecaty.ai-agent/","Packages/com.runtime.capabilities/"};
            var player=CompilationPipeline.GetAssemblies(AssembliesType.Player);
            var leaked=player.SelectMany(a=>a.sourceFiles).Select(p=>p.Replace('\\','/')).Where(p=>owners.Any(o=>p.StartsWith(o,StringComparison.Ordinal))&&p.Contains("/Editor/")).ToArray();
            if(leaked.Length>0)throw new InvalidOperationException("Editor sources entered player assemblies: "+string.Join(", ",leaked));
            string directory=Path.GetFullPath(outputDirectory);Directory.CreateDirectory(directory);
            var settings=new ScriptCompilationSettings {target=BuildTarget.StandaloneWindows64,group=BuildTargetGroup.Standalone,options=ScriptCompilationOptions.DevelopmentBuild};
            var compiled=PlayerBuildInterface.CompilePlayerScripts(settings,directory);
            var names=compiled.assemblies.Select(Path.GetFileName).ToArray();
            string[] required={"Assembly-CSharp.dll","Runtime.Capabilities.dll","Ecaty.Flecs.dll","Ecaty.Agents.dll"};
            var missing=required.Where(n=>!names.Contains(n)).ToArray();
            var report=new JObject{["passed"]=missing.Length==0,["target"]="Windows64",["assemblyCount"]=names.Length,["editorSourceLeaks"]=new JArray(leaked),["missing"]=new JArray(missing),["directory"]=directory};
            File.WriteAllText("Verification/player-script-checks.json",report.ToString());
            if(missing.Length>0)throw new InvalidOperationException(report.ToString());return report;
        }
    }
}
