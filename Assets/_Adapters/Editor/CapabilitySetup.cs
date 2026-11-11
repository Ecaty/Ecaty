using System;
using System.Collections.Generic;
using System.IO;
using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_FEEL
using MoreMountains.Feedbacks;
#endif
#if ECATY_CAP_COZY
using ProjectDawn.CozyBuilder;
#endif
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
namespace Ecaty.Editor
{
    public static class CapabilitySetup
    {
        [MenuItem("Runtime/Runtime engine/Prepare capability assets")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/_Features/World/Resources/Runtime/Examples/Capabilities");
            AssetDatabase.Refresh();
            const string catalogPath = "Assets/_Adapters/Adapters.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<CapabilityAssets>(catalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<CapabilityAssets>();
                AssetDatabase.CreateAsset(catalog, catalogPath);
            }
            var entries = new List<CapabilityAssets.Entry>(catalog.Entries);
            void Add(string key, UnityEngine.Object asset)
            {
                if (!asset)
                    return;
                int index = entries.FindIndex(e => e.Key == key);
                if (index < 0)
                    entries.Add(new CapabilityAssets.Entry { Key = key, Asset = asset });
                else if (!entries[index].Asset)
                    entries[index] = new CapabilityAssets.Entry { Key = key, Asset = asset };
            }
            var material = Resources.Load<Material>("Shared/Rendering/PresentationMaterial");
            Add("default", material);
            const string fxPath = "Assets/_Features/World/Resources/Runtime/Examples/Capabilities/PeacefulParticles.vfx";
            if (!File.Exists(fxPath))
            {
                var package = System.Array.Find(UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages(), p => p.name == "com.unity.visualeffectgraph");
                if (package != null)
                    AssetDatabase.CopyAsset(package.assetPath + "/Editor/Templates/Minimal_System.vfx", fxPath);
            }
            Add("particles", AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(fxPath));
#if ECATY_CAP_FEEL
            const string burstPath = "Assets/_Features/World/Resources/Runtime/Examples/Capabilities/BurstFeedback.prefab";
            if (!File.Exists(burstPath))
            {
                var go = new GameObject("Burst feedback");
                try
                {
                    var particles = go.AddComponent<ParticleSystem>();
                    var main = particles.main;
                    main.playOnAwake = false;
                    main.startLifetime = .4f;
                    main.startSpeed = 1;
                    main.startSize = .07f;
                    var emission = particles.emission;
                    emission.rateOverTime = 0;
                    var player = go.AddComponent<MMF_Player>();
                    player.AddFeedback(new MMF_Particles { BoundParticleSystem = particles, Mode = MMF_Particles.Modes.Emit, EmitCount = 12 });
                    PrefabUtility.SaveAsPrefabAsset(go, burstPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            Add("burst", AssetDatabase.LoadAssetAtPath<GameObject>(burstPath));
#endif
#if ECATY_CAP_COZY
            const string constructionMaterialPath = "Assets/_Features/World/Resources/Runtime/Examples/Capabilities/ConstructionMaterial.mat";
            var constructionMaterial = AssetDatabase.LoadAssetAtPath<Material>(constructionMaterialPath);
            if (!constructionMaterial)
            {
                constructionMaterial = new Material(material) { name = "Construction material", enableInstancing = true };
                AssetDatabase.CreateAsset(constructionMaterial, constructionMaterialPath);
            }
            Add("construction-material", constructionMaterial);
            const string brickPath = "Assets/_Features/World/Resources/Runtime/Examples/Capabilities/Brick.prefab", kitPath = "Assets/_Features/World/Resources/Runtime/Examples/Capabilities/ConstructionKit.prefab";
            if (!File.Exists(brickPath))
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                try
                {
                    go.name = "Brick";
                    go.GetComponent<Renderer>().sharedMaterial = material;
                    PrefabUtility.SaveAsPrefabAsset(go, brickPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            if (!File.Exists(kitPath))
            {
                var go = new GameObject("Construction kit");
                try
                {
                    var renderer = go.AddComponent<CozyRenderer>();
                    renderer.BuildMode = CozyBuildMode.Never;
                    var spline = go.AddComponent<CozySpline>();
                    foreach (var v in new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0) })
                    {
                        var p = new GameObject("Control point");
                        p.transform.SetParent(go.transform, false);
                        p.transform.localPosition = v;
                        spline.Points.Add(p.AddComponent<CozyPoint>());
                    }
                    var segments = go.AddComponent<CozySegments>();
                    segments.Spacing = .5f;
                    var draw = go.AddComponent<CozyDraw>();
                    draw.Prefabs = new() { AssetDatabase.LoadAssetAtPath<GameObject>(brickPath) };
                    draw.OverrideMaterial = constructionMaterial;
                    PrefabUtility.SaveAsPrefabAsset(go, kitPath);
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
            var contents = PrefabUtility.LoadPrefabContents(kitPath);
            try
            {
                bool changed = false;
                foreach (var draw in contents.GetComponentsInChildren<CozyDraw>())
                if (draw.OverrideMaterial == material)
                {
                    draw.OverrideMaterial = constructionMaterial;
                    changed = true;
                }
                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(contents, kitPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Add("building", AssetDatabase.LoadAssetAtPath<GameObject>(kitPath));
#endif
#if ECATY_CAP_BROCCOLI
            Add("tree", Resources.Load<Broccoli.Pipe.Pipeline>("BroccoliRuntimeExamplePipeline") ?? AssetDatabase.LoadAssetAtPath<Broccoli.Pipe.Pipeline>("Assets/Waldemarst/Broccoli/Catalog/Pipelines/br_lp_birch_01.asset"));
#endif
            Add("chime", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/_Features/World/Resources/Runtime/Examples/Capabilities/Chime.wav"));
            catalog.Entries = entries.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Capability assets prepared: " + entries.Count);
        }
    }
}
