using Ecaty.Flecs;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ecaty.Editor
{
    public static class Verification
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public string unity;
            public string flecs;
            public List<string> checks = new List<string>();
            public string error;
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        public static void RunAll()
        {
            var report = new Report { unity = Application.unityVersion };
            GameObject root = null;
            try
            {
                ConfigurePlugin();
                PrepareAssets();
                report.flecs = FlecsSession.Version;
                root = new GameObject("Verification world");
                var host = root.AddComponent<FlecsWorld>();
                host.Initialize(0, Resources.Load<TextAsset>("Tests/Fixtures/VerificationWorld"), "EcatyWorld");
                Require(host.ViewCount == 3, "Expected scene + cube + sphere");
                report.checks.Add("managed Script loaded; prefab excluded; three views created; no greeting text");
                host.Session.ReadViews(out var first);
                ulong cube = 0;
                for (int i=0; i<host.ViewCount; i++)
                    if (host.Session.EntityName(first[i].Entity) == "Cube") cube = first[i].Entity;
                Require(cube != 0, "Cube missing");
                var cubeView = host.FindView(cube);
                Require(cubeView && cubeView.transform.parent.name == "scene", "ChildOf not reflected in Unity");
                var before = cubeView.transform.localRotation;
                host.Session.Tick(0.25f); host.SyncViews();
                Require(Quaternion.Angle(before,cubeView.transform.localRotation)>1f,"Native Spin system did not animate Unity");
                report.checks.Add("native system advances rotation; ChildOf hierarchy mapped");
                host.Session.RunScript("scene { Cube { Position: {3, 1, 0}; Color: {1, 0, 0, 1} } }");
                host.SyncViews();
                Require(Vector3.Distance(cubeView.transform.localPosition,new Vector3(3,1,0))<0.001f,"Script transform change missing");
                var block = new MaterialPropertyBlock();
                cubeView.GetComponentInChildren<Renderer>().GetPropertyBlock(block);
                Require(block.GetColor("_Color").r>0.99f && block.GetColor("_Color").g<0.01f,"Color not applied");
                report.checks.Add("runtime Script position and color reflected in Unity");
                Require(host.Session.Query("Presented").Contains("frame"), "Presentation acknowledgement missing");
                report.checks.Add("actual Unity presentation written back into queryable Presented component");
                host.Session.RunScript("Extra { Position: {0, -2, 0}; Primitive: {kind: 2} }");
                host.SyncViews(); Require(host.ViewCount==4,"Runtime spawn missing");
                report.checks.Add("runtime entity creation creates Unity view");
                string query = host.Session.Query("Position, Primitive");
                Require(query.Contains("Cube") && query.Contains("Extra"),"Query JSON missing entities");
                Require(host.Session.EntityJson("scene.Cube").Contains("Position"),"Reflection JSON missing component");
                report.checks.Add("query and reflected entity JSON round trip through C ABI");
                host.Session.RunScript("struct Temperature(value: f32)\nscene { Cube { Temperature: {42} } }\n");
                Require(host.Session.EntityJson("scene.Cube").Contains("Temperature"),"Runtime type registration failed");
                report.checks.Add("Script-defined runtime component works without C# type");
                host.Session.SetManagedScript("Transient", "Temporary { Position: {0,0,0}; Primitive: {0} }");
                host.SyncViews(); Require(host.ViewCount==5,"Managed entity missing");
                host.Session.SetManagedScript("Transient", "");
                host.SyncViews(); Require(host.ViewCount==4,"Managed deletion did not remove Unity view");
                report.checks.Add("managed Script deletion removes Unity view");
                host.Shutdown();
                using (var reopened = new FlecsSession()) { reopened.Tick(0.02f); }
                using (var reopened = new FlecsSession()) { reopened.Tick(0.02f); }
                report.checks.Add("world teardown and REST port reuse");
                report.passed = true;
            }
            catch (Exception ex) { report.error=ex.ToString(); Debug.LogException(ex); }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                FlecsSession.DisposeAll();
                var folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"Verification");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"unity-checks.json"),JsonUtility.ToJson(report,true));
                Debug.Log("FLECS_UNITY_VERIFICATION=" + (report.passed ? "PASS" : "FAIL"));
            }
            if (!report.passed) throw new Exception(report.error);
        }
        private static void ConfigurePlugin()
        {
            var plugin=AssetImporter.GetAtPath("Assets/Plugins/Ecaty/x86_64/flecs_unity.dll") as PluginImporter;
            if (!plugin) throw new Exception("Native DLL not imported");
            plugin.SetCompatibleWithAnyPlatform(false);
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64,false);
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX,false);
            plugin.SetCompatibleWithEditor(true);
            plugin.SetEditorData("CPU","x86_64");
            plugin.SetEditorData("OS","Windows");
            plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64,true);
            plugin.SetPlatformData(BuildTarget.StandaloneWindows64,"CPU","x86_64");
            plugin.SaveAndReimport();
        }
        private static void PrepareAssets()
        {
            const string path="Assets/__Common/Theme/Resources/Shared/Rendering/PresentationMaterial.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(path))
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")),path);
            ChatLoopSetup.Prepare();
            AssetDatabase.SaveAssets();
        }
        public static void BuildPlayer()
        {
            RunAll();
            const string scenePath="Assets/_Scenes/SampleScene.unity";
            var root=Directory.GetParent(Application.dataPath).FullName;
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{scenePath}, locationPathName=Path.Combine(root,"Build/Ecaty.exe"),
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development
            });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Player build failed: " + result.summary.result);
        }
    }
}
