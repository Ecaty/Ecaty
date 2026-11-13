using System;
using System.IO;
using System.Linq;
using Runtime.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace Game.Editor
{
    public static class ProjectLayoutVerification
    {
        [MenuItem("Runtime/Tests/Modular layout")]
        public static void Menu() => Debug.Log(Run());
        public static object Run()
        {
            var manifest = JObject.Parse(File.ReadAllText("Tests/Fixtures/modular-layout-migration.json"));
            var failures = new JArray();
            int checkedAssets = 0, retired = 0;
            foreach (var row in manifest["assets"])
            {
                string path = (string)row["to"];
                if ((bool?)row["retired"] == true)
                {
                    retired++;
                    if (File.Exists(path) || Directory.Exists(path))
                        failures.Add("Retired asset remains: " + path);
                    continue;
                }
                checkedAssets++;
                if (AssetDatabase.AssetPathToGUID(path) != (string)row["guid"])
                    failures.Add("GUID mismatch: " + path);
            }
            foreach (string old in new[] { "Assets/Scenes", "Assets/Settings", "Packages/com.runtime.flecs/Runtime/Capabilities" })
                if (Directory.Exists(old))
                    failures.Add("Old owner remains: " + old);
            if (typeof(CapabilityRuntime).Assembly.GetName().Name != "Runtime.Capabilities")
                failures.Add("Capabilities were not extracted to their own assembly.");
            if (typeof(Runtime.Flecs.FlecsWorld).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Runtime.Capabilities"))
                failures.Add("Bridge depends on capabilities.");
            if (typeof(Runtime.Agents.UI.AgentChatPanel).Assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("Runtime.Flecs") || a.Name == "Runtime.Capabilities"))
                failures.Add("Generic agent depends on Flecs.");
            var catalog = RuntimeComposition.Current.Capabilities;
            if (!catalog || catalog.Entries.Length == 0)
                failures.Add("Capability catalog did not survive the assembly move.");
            var report = new JObject { ["passed"] = failures.Count == 0, ["assets"] = checkedAssets, ["retired"] = retired, ["failures"] = failures };
            File.WriteAllText("Verification/modular-layout-checks.json", report.ToString());
            if (failures.Count > 0)
                throw new InvalidOperationException(report.ToString());
            return report;
        }
    }
}
