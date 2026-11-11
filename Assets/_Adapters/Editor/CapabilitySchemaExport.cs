using System.IO;
using Ecaty.Flecs.Capabilities;
using UnityEditor;
using UnityEngine;
namespace Ecaty.Editor
{
    // Keeps the committed configuration schema export in step with the [Capability] classes after each compile.
    // The runtime generates its schema from the classes; the export serves tools that run without Unity.
    [InitializeOnLoad]
    public static class CapabilitySchemaExport
    {
        static CapabilitySchemaExport()
        {
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Export(false);
            };
        }
        public static bool IsCurrent() => File.Exists(CapabilityCatalog.ExportPath) && Normalize(File.ReadAllText(CapabilityCatalog.ExportPath)) == CapabilityCatalog.ExportText();
        static string Normalize(string text) => text.Replace("\r\n", "\n");
        [MenuItem("Runtime/Runtime engine/Export capability schema")]
        static void Menu() => Export(true);
        public static void Export(bool log)
        {
            if (IsCurrent())
            {
                if (log)
                    Debug.Log("Capability schema export is current.");
                return;
            }
            File.WriteAllText(CapabilityCatalog.ExportPath, CapabilityCatalog.ExportText());
            AssetDatabase.ImportAsset(CapabilityCatalog.ExportPath);
            Debug.Log("Updated " + CapabilityCatalog.ExportPath);
        }
    }
}
