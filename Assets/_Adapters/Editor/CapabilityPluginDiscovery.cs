using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
namespace Ecaty.Editor
{
    [InitializeOnLoad]
    public static class CapabilityPluginDiscovery
    {
        static CapabilityPluginDiscovery()
        {
            EditorApplication.delayCall += Refresh;
            EditorUserBuildSettings.activeBuildTargetChanged += Refresh;
        }
        public static void Refresh()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var target = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var symbols = new HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s => !string.IsNullOrWhiteSpace(s)));
            bool changed = false;
            void Set(string name, string path)
            {
                bool installed = File.Exists(path);
                if (installed)
                    changed |= symbols.Add(name);
                else
                    changed |= symbols.Remove(name);
            }
            Set("ECATY_CAP_SPLINES", "Packages/xyz.staggart-creations.splinemesher-pro/package.json");
            Set("ECATY_CAP_COZY", "Packages/com.projectdawn.cozy-builder/package.json");
            Set("ECATY_CAP_BROCCOLI", "Assets/Waldemarst/Broccoli/Factory/TreeFactory.cs");
            Set("ECATY_CAP_FLEXALON", "Assets/Flexalon/Runtime/Layouts/FlexalonCircleLayout.cs");
            Set("ECATY_CAP_FEEL", "Assets/Feel/MMFeedbacks/MMFeedbacks/Sequencing/Scripts/MMSoundSequencer.cs");
            Set("ECATY_CAP_UNISTORM", "Assets/UniStorm Weather System/Scripts/System/UniStormSystem.cs");
            Set("ECATY_CAP_CREST", "Assets/Crest/Crest/Scripts/OceanRenderer.cs");
            Set("ECATY_CAP_ALINE", "Packages/com.arongranberg.aline/package.json");
            if (changed)
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols.OrderBy(s => s)));
        }
    }
    public sealed class CapabilityPluginImports : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previous)
        {
            if (imported.Length + deleted.Length + moved.Length > 0)
                EditorApplication.delayCall += CapabilityPluginDiscovery.Refresh;
        }
    }
}
