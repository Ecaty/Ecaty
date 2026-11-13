using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using Runtime.Capabilities;
namespace Game.Editor
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
            void Set(string name, bool installed)
            {
                if (installed)
                    changed |= symbols.Add(name);
                else
                    changed |= symbols.Remove(name);
            }
            // Gates are declared on always-compiled [Capability] configurations next to each adapter.
            var gates = new Dictionary<string, bool>();
            foreach (var type in TypeCache.GetTypesWithAttribute<CapabilityAttribute>())
            {
                var info = (CapabilityAttribute)Attribute.GetCustomAttribute(type, typeof(CapabilityAttribute));
                if (string.IsNullOrEmpty(info.Gate))
                    continue;
                bool present = File.Exists(info.Probe);
                gates[info.Gate] = gates.TryGetValue(info.Gate, out bool other) ? other && present : present;
            }
            foreach (var gate in gates)
                Set(gate.Key, gate.Value);
            // Drawing support without a capability adapter.
            Set("CAP_ALINE", File.Exists("Packages/com.arongranberg.aline/package.json"));
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
