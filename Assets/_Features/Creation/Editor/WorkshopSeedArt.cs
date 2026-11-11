using System;
using MudBun;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Ecaty.Editor
{
    public sealed class WorkshopSeedArt : EditorWindow
    {
        private const string Folder = "Assets/_Features/Creation/Resources/";

        [MenuItem("Runtime/Workshop/Icon Studio")]
        private static void OpenStudio() => GetWindow<WorkshopSeedArt>("Workshop Icons");

        private void CreateGUI()
        {
            var slot = new EnumField("Icon slot", WorkshopIcon.Seed);
            var source = new ObjectField("Sculpt or mesh")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                value = Selection.activeGameObject
            };
            var density = new Slider("MudBun resolution", 6, 24) { value = 12 };
            var result = new Label();
            var preview = new ObjectField("Baked prefab") { objectType = typeof(GameObject) };
            preview.SetEnabled(false);
            void Refresh() => preview.SetValueWithoutNotify(LoadIcon((WorkshopIcon)slot.value));
            slot.RegisterValueChangedCallback(_ => Refresh());
            rootVisualElement.Add(slot);
            rootVisualElement.Add(source);
            rootVisualElement.Add(density);
            rootVisualElement.Add(new Button(() =>
            {
                var icon = (WorkshopIcon)slot.value;
                var sculpt = new GameObject(icon + " sculpt");
                sculpt.AddComponent<MudRenderer>().VoxelDensity = density.value;
                Sculpt(sculpt.transform, icon);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sculpt.scene);
                Selection.activeGameObject = sculpt;
                source.value = sculpt;
            })
            {
                text = "Create editable sculpt"
            });
            rootVisualElement.Add(new Button(() =>
            {
                try
                {
                    var icon = (WorkshopIcon)slot.value;
                    var prefab = Bake((GameObject)source.value, icon, density.value);
                    var mesh = prefab.GetComponentInChildren<MeshFilter>().sharedMesh;
                    result.text = $"{mesh.vertexCount} vertices · {mesh.GetIndexCount(0) / 3} triangles";
                    Refresh();
                }
                catch (Exception error) { result.text = error.Message; }
            })
            {
                text = "Bake into slot"
            });
            rootVisualElement.Add(preview);
            rootVisualElement.Add(new Button(() => { var asset = LoadIcon((WorkshopIcon)slot.value); if (asset) EditorGUIUtility.PingObject(asset); })
            {
                text = "Find baked asset"
            });
            rootVisualElement.Add(result);
            Refresh();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        private static string Resource(WorkshopIcon icon) =>
            icon == WorkshopIcon.Seed ? "Workshop/Art/Seed/WorkshopSeedArt" : "Workshop/Art/Icons/" + icon;

        private static GameObject LoadIcon(WorkshopIcon icon) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(Folder + Resource(icon) + ".prefab");

        [MenuItem("Runtime/Workshop/Bake selected model as seed", true)]
        private static bool CanBake() => CanBake(Selection.activeGameObject);

        private static bool CanBake(GameObject source) =>
            source && !EditorApplication.isPlayingOrWillChangePlaymode &&
            (source.TryGetComponent<MudRenderer>(out _) ||
             source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh &&
             source.TryGetComponent<MeshRenderer>(out _));

        [MenuItem("Runtime/Workshop/Bake selected model as seed")]
        private static void BakeSelection() => Selection.activeObject = Bake(Selection.activeGameObject, WorkshopIcon.Seed);

        [MenuItem("Runtime/Workshop/Bake default MudBun seed")]
        public static void BakeDefault() => BakeStock(WorkshopIcon.Seed);

        [MenuItem("Runtime/Workshop/Bake stock icons")]
        public static void BakeStockIcons()
        {
            foreach (WorkshopIcon icon in Enum.GetValues(typeof(WorkshopIcon)))
                BakeStock(icon);
        }

        private static void BakeStock(WorkshopIcon icon)
        {
            var source = new GameObject(icon + " sculpt") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                source.AddComponent<MudRenderer>();
                Sculpt(source.transform, icon);
                Bake(source, icon, icon == WorkshopIcon.Seed ? 11 : 13);
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static void Sculpt(Transform root, WorkshopIcon icon)
        {
            Color mint = new(.29f, .79f, .55f), gold = new(.98f, .66f, .27f), cream = new(.91f, .85f, .66f);
            Color teal = new(.32f, .67f, .80f), violet = new(.57f, .52f, .88f), dark = new(.22f, .28f, .40f);
            MudSolid Oval(float x, float y, float z, float width, float height, float depth, Color color, float tilt = 0) =>
                Brush<MudSphere>(root, new Vector3(x, y, z), new Vector3(width, height, depth), color, tilt);
            void Box(float x, float y, float z, float width, float height, float depth, Color color, float tilt = 0) =>
                Brush<MudBox>(root, new Vector3(x, y, z), new Vector3(width, height, depth), color, tilt).Round = .035f;

            switch (icon)
            {
                case WorkshopIcon.Seed:
                    Oval(0, 0, 0, .62f, .95f, .37f, new Color(.62f, .39f, .22f), -25);
                    Oval(-.035f, .015f, -.176f, .065f, .59f, .035f, cream, -25).Blend = .025f;
                    Oval(.23f, .34f, -.025f, .27f, .12f, .16f, mint, 32).Blend = .055f;
                    break;
                case WorkshopIcon.NewWorld:
                    Box(0, 0, 0, .76f, .17f, .16f, mint);
                    Box(0, 0, 0, .17f, .76f, .16f, mint);
                    break;
                case WorkshopIcon.OpenWorld:
                    Box(0, -.03f, .08f, .76f, .49f, .16f, gold);
                    Box(-.21f, .24f, .08f, .29f, .14f, .16f, gold);
                    Box(0, -.1f, -.08f, .8f, .31f, .18f, cream, 6);
                    break;
                case WorkshopIcon.SaveWorld:
                    Box(0, 0, .02f, .65f, .67f, .17f, teal);
                    Box(0, .15f, -.09f, .36f, .19f, .07f, dark);
                    Box(0, -.18f, -.09f, .37f, .18f, .07f, cream);
                    break;
                case WorkshopIcon.Bell:
                    Oval(0, .02f, 0, .57f, .67f, .38f, gold);
                    Box(0, -.23f, 0, .65f, .12f, .37f, gold);
                    Oval(0, -.35f, 0, .18f, .18f, .19f, cream);
                    Oval(0, .32f, 0, .16f, .15f, .16f, cream);
                    break;
                case WorkshopIcon.Playground:
                    Oval(-.19f, -.10f, .01f, .47f, .43f, .26f, violet);
                    Oval(-.19f, .18f, -.03f, .36f, .35f, .29f, violet);
                    Oval(.20f, -.15f, -.09f, .44f, .36f, .26f, teal);
                    Oval(.20f, .10f, -.12f, .33f, .32f, .27f, teal);
                    break;
                case WorkshopIcon.Environment:
                    Oval(-.18f, .19f, .05f, .52f, .51f, .25f, gold);
                    Oval(.08f, -.07f, -.08f, .66f, .33f, .27f, teal).Blend = .10f;
                    Oval(.20f, .03f, -.08f, .39f, .38f, .26f, teal).Blend = .10f;
                    break;
                case WorkshopIcon.Confirm:
                    Box(-.16f, -.11f, 0, .30f, .15f, .17f, mint, -42);
                    Box(.15f, .04f, 0, .58f, .15f, .17f, mint, 45);
                    break;
                case WorkshopIcon.Paper:
                    Box(.04f, -.03f, .05f, .53f, .67f, .13f, gold, -12);
                    Box(-.06f, .02f, -.09f, .53f, .67f, .13f, cream, 9);
                    break;
                case WorkshopIcon.Honey:
                    Oval(0, 0, 0, .46f, .46f, .27f, gold);
                    for (int i = 0; i < 8; i++)
                    {
                        float radians = i * Mathf.PI / 4;
                        Box(Mathf.Cos(radians) * .34f, Mathf.Sin(radians) * .34f, 0, .17f, .105f, .14f, gold, 45 * i);
                    }
                    break;
                case WorkshopIcon.Twilight:
                    Oval(0, 0, 0, .72f, .76f, .32f, violet);
                    Oval(.24f, .16f, 0, .62f, .65f, .71f, violet).Operator = SdfBrush.OperatorEnum.Subtract;
                    break;
                case WorkshopIcon.Neutral:
                    Oval(0, 0, 0, .73f, .73f, .19f, cream);
                    Oval(0, 0, 0, .47f, .47f, .57f, cream).Operator = SdfBrush.OperatorEnum.Subtract;
                    Box(0, 0, 0, .61f, .105f, .17f, cream, 45);
                    break;
            }
        }

        private static T Brush<T>(Transform parent, Vector3 position, Vector3 scale, Color color, float angle)
            where T : MudSolid
        {
            var part = new GameObject(typeof(T).Name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localRotation = Quaternion.Euler(0, 0, angle);
            var brush = part.AddComponent<T>();
            var material = part.GetComponent<MudMaterial>();
            material.Color = color;
            material.Smoothness = .16f;
            return brush;
        }

        private static GameObject Bake(GameObject source, WorkshopIcon icon, float density = 0)
        {
            if (!CanBake(source))
                throw new InvalidOperationException("Select a MudBun renderer or a mesh in Edit Mode.");
            string basePath = Folder + Resource(icon);
            EnsureFolder(System.IO.Path.GetDirectoryName(basePath).Replace('\\', '/'));
            string meshPath = basePath + "Mesh.asset";
            GameObject temporary = null;
            GameObject prefabRoot = null;
            Mesh generated = null;
            try
            {
                Material[] materials;
                if (source.TryGetComponent<MudRenderer>(out _))
                {
                    temporary = Object.Instantiate(source);
                    temporary.hideFlags = HideFlags.HideAndDontSave;
                    var renderer = temporary.GetComponent<MudRenderer>();
                    if (density > 0)
                        renderer.VoxelDensity = density;
                    generated = renderer.GenerateMesh(MudRendererBase.GeneratedMeshType.Standard, async: false,
                        weldVertices: true, optimizeMeshForRendering: true);
                    materials = new[] { ResourcesUtil.DefaultLockedMeshMaterial };
                }
                else
                {
                    generated = Object.Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
                    materials = source.GetComponent<MeshRenderer>().sharedMaterials;
                }

                if (!generated || generated.vertexCount == 0)
                    throw new InvalidOperationException("The selection produced no renderable mesh.");

                var baked = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                generated.name = System.IO.Path.GetFileNameWithoutExtension(meshPath);
                if (baked)
                {
                    CopyMesh(generated, baked);
                    EditorUtility.SetDirty(baked);
                }
                else
                {
                    AssetDatabase.CreateAsset(generated, meshPath);
                    baked = generated;
                    generated = null;
                }

                prefabRoot = new GameObject(icon.ToString());
                var visual = new GameObject("Baked surface");
                visual.transform.SetParent(prefabRoot.transform, false);
                var bounds = baked.bounds;
                float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                float scale = .96f / Mathf.Max(.001f, size);
                visual.transform.localScale = Vector3.one * scale;
                visual.transform.localPosition = -bounds.center * scale;
                visual.AddComponent<MeshFilter>().sharedMesh = baked;
                var surface = visual.AddComponent<MeshRenderer>();
                surface.sharedMaterials = materials;
                surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, basePath + ".prefab");
                AssetDatabase.SaveAssets();
                Debug.Log($"Baked {icon}: {baked.vertexCount} vertices, {baked.GetIndexCount(0) / 3} triangles.", prefab);
                return prefab;
            }
            finally
            {
                if (prefabRoot)
                    Object.DestroyImmediate(prefabRoot);
                if (temporary)
                    Object.DestroyImmediate(temporary);
                if (generated)
                    Object.DestroyImmediate(generated);
            }
        }

        private static void CopyMesh(Mesh source, Mesh target)
        {
            using var read = Mesh.AcquireReadOnlyMeshData(source);
            var output = Mesh.AllocateWritableMeshData(1);
            try
            {
                var data = output[0];
                data.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                    read[0].GetVertexData<byte>(stream).CopyTo(data.GetVertexData<byte>(stream));
                var indices = read[0].GetIndexData<byte>();
                int indexSize = source.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16 ? 2 : 4;
                data.SetIndexBufferParams(indices.Length / indexSize, source.indexFormat);
                indices.CopyTo(data.GetIndexData<byte>());
                data.subMeshCount = source.subMeshCount;
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                    data.SetSubMesh(submesh, read[0].GetSubMesh(submesh),
                        UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            }
            catch
            {
                output.Dispose();
                throw;
            }
            Mesh.ApplyAndDisposeWritableMeshData(output, target);
            target.bounds = source.bounds;
            target.name = source.name;
        }

        [MenuItem("Runtime/Workshop/Select baked seed prefab")]
        private static void SelectArt() => Selection.activeObject = LoadIcon(WorkshopIcon.Seed);
    }
}
