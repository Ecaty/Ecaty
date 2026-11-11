using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace Ecaty.Editor
{
    public static class ResourceVerification
    {
        [MenuItem("Runtime/Tests/Resource references")]
        public static void Menu() => Debug.Log(Run());
        public static object Run()
        {
            var rows = JArray.Parse(File.ReadAllText("Tests/Fixtures/resource-load-manifest.json"));
            var checks = new JArray();
            foreach (var row in rows)
            {
                string path = (string)row["path"], key = (string)row["key"];
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                var loaded = row["composition"] != null ? RuntimeComposition.Current.ResolveReference((string)row["composition"]) : Resources.Load(key);
                bool pass = asset && loaded == asset && AssetDatabase.AssetPathToGUID(path) == (string)row["guid"];
                checks.Add(new JObject { ["resource"] = key, ["passed"] = pass });
                if (!pass)
                    throw new Exception("Missing/mismatched resource: " + key);
            }
            var report = new JObject { ["passed"] = true, ["count"] = rows.Count, ["checks"] = checks, ["layout"] = JToken.FromObject(ProjectLayoutVerification.Run()) };
            File.WriteAllText("Verification/resource-reference-checks.json", report.ToString());
            return report;
        }
    }
}
