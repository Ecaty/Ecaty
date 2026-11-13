using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Development APK (IL2CPP, arm64) into Build/Android. Switching the active platform
    /// reimports assets; switch back to Windows afterwards for the regular test suites.
    /// </summary>
    public static class AndroidPlayerBuild
    {
        private const string NativePlugin = "Assets/Plugins/Flecs/Android/arm64-v8a/libflecs_unity.so";

        [MenuItem("Tools/Build/Android development APK")]
        public static void Menu() => Debug.Log(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before building.");
            if (!File.Exists(NativePlugin))
                throw new FileNotFoundException("Run Native/Build-Android.ps1 first.", NativePlugin);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string output = Path.Combine(root, "Build", "Android", "app-development.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            });
            var summary = report.summary;
            long apkBytes = File.Exists(output) ? new FileInfo(output).Length : 0;
            string text = $"Android build {summary.result}: {output} ({apkBytes / (1024 * 1024)} MB, {summary.totalTime.TotalMinutes:0.0} min, {summary.totalErrors} errors)";
            File.WriteAllText(Path.Combine(root, "Build", "Android", "build-summary.txt"), text);
            if (summary.result != BuildResult.Succeeded)
                throw new Exception(text);
            return text;
        }
    }
}
