#if ECATY_CAP_FLEXALON
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Ecaty
{
    // One disposable preview plus one retained mock world. Real content streaming is a later adapter.
    public sealed class GalleryPreviewStage : IDisposable
    {
        public sealed class Context : IDisposable
        {
            public string Id;
            public int Kind;
            public int NativeGeneration = -1;
            public GameObject Root;
            public GalleryMockPreview Preview;
            public Action<bool> Visibility;
            public readonly List<Material> Materials = new();
            public void Dispose()
            {
                Visibility?.Invoke(false);
                if (Root)
                {
                    Root.SetActive(false);
                    Object.Destroy(Root);
                }
                Preview?.Dispose();
                foreach (var material in Materials)
                    if (material)
                        Object.Destroy(material);
            }
        }
        public const int Layer = 30;
        readonly Transform root;
        readonly Camera output;
        readonly int previousCullingMask;
        readonly Transform backdrop, foreground;
        readonly Material backMaterial, frontMaterial;
        RenderTexture texture;
        Context preview, shown, resident;
        float nextRender, sampledTime = float.NaN;
        bool dirty;
        JObject actualView;
        Vector3 renderedCenter;
        float renderedDistance, renderedYaw, renderedPitch;
        bool poseInitialized;
        public Action<Context> PrepareContext;
        public Camera Camera
        {
            get;
        }
        public Context Shown => shown;
        public Context Resident => resident;
        public int RenderCount
        {
            get; private set;
        }
        public int ContextCount => (resident == null ? 0 : 1) + (preview == null || preview == resident ? 0 : 1);
        public RenderTexture Texture => texture;
        public JObject ActualView => actualView == null ? null : (JObject)actualView.DeepClone();
        public float EntryBlend
        {
            get; private set;
        }

        public GalleryPreviewStage(Transform owner, Camera view)
        {
            output = view;
            previousCullingMask = view.cullingMask;
            root = new GameObject("Isolated disposable world previews").transform;
            root.SetParent(owner, false);
            root.position = new Vector3(0, -800, 0);
            Camera = new GameObject("Gallery preview and world camera").AddComponent<Camera>();
            Camera.transform.SetParent(root, false);
            Camera.enabled = false;
            Camera.orthographic = false;
            Camera.fieldOfView = 42;
            Camera.nearClipPlane = .035f;
            Camera.farClipPlane = 180;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.cullingMask = 1 << Layer;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;
            data.SetRenderer(0);
            output.cullingMask &= ~(1 << Layer);
            backdrop = Screen("World background projection", 90, out backMaterial);
            foreground = Screen("Matched entry handoff", .3f, out frontMaterial);
            SetSurfaces(0, false, false);
            RenderPipelineManager.beginCameraRendering += BeforeCameraRender;
        }
        void BeforeCameraRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera == output)
                SetSurfaces(EntryBlend, shown != null, false);
        }

        Transform Screen(string name, float distance, out Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(output.transform, false);
            go.transform.localPosition = new Vector3(0, 0, distance);
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            material = distance < 1 ? new Material(Resources.Load<Shader>("Gallery/Shaders/GalleryGlassHandoff")) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            go.GetComponent<Renderer>().sharedMaterial = material;
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.GetComponent<Renderer>().receiveShadows = false;
            return go.transform;
        }
        void SetLayer(GameObject go)
        {
            go.layer = Layer;
            foreach (Transform child in go.transform)
                SetLayer(child.gameObject);
        }
        Context Create(GalleryPreviewEntry entry)
        {
            var context = new Context { Id = entry.Id, Kind = entry.Kind, Root = new GameObject("Mock world " + entry.Id) };
            context.Root.transform.SetParent(root, false);
            context.Preview = entry.SavedFeatures != null ? new GalleryMockPreview(context.Root.transform, entry.SavedFeatures, context.Materials) : new GalleryMockPreview(context.Root.transform, entry.Kind, entry.Width, entry.Depth, context.Materials);
            if (entry.SavedFeatures == null && entry.Kind != 1 && entry.Kind != 7 && entry.Kind != 5)
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Disposable world environment";
                ground.transform.SetParent(context.Root.transform, false);
                ground.transform.localPosition = new Vector3(0, entry.Kind == 0 ? -.9f : -.08f, 0);
                ground.transform.localScale = new Vector3(5, 1, 5);
                Object.Destroy(ground.GetComponent<Collider>());
                Color color = entry.Kind == 0 ? new Color(.30f, .45f, .43f) : Color.Lerp(context.Preview.Theme, new Color(.57f, .55f, .46f), .5f);
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                context.Materials.Add(material);
                ground.GetComponent<Renderer>().sharedMaterial = material;
            }
            SetLayer(context.Root);
            return context;
        }
        public void Prepare(GalleryPreviewEntry entry, float time)
        {
            Context requested;
            if (resident?.Id == entry.Id)
                requested = resident;
            else
            {
                if (preview?.Id != entry.Id)
                {
                    preview?.Dispose();
                    preview = Create(entry);
                }
                requested = preview;
            }
            if (shown != requested)
            {
                if (shown?.Root)
                    shown.Root.SetActive(false);
                shown?.Visibility?.Invoke(false);
                shown = requested;
                sampledTime = float.NaN;
                dirty = true;
                poseInitialized = false;
            }
            if (shown.Root)
                shown.Root.SetActive(true);
            PrepareContext?.Invoke(shown);
            shown.Visibility?.Invoke(true);
            if (!Mathf.Approximately(time, sampledTime))
            {
                shown.Preview.Animate(time, Mathf.Sin(time * 1.8f));
                sampledTime = time;
                dirty = true;
            }
        }
        public void Promote()
        {
            if (shown == null || resident == shown)
                return;
            resident?.Dispose();
            resident = shown;
            if (preview == resident)
                preview = null;
        }
        public void ClearPreview()
        {
            if (shown?.Root)
                shown.Root.SetActive(false);
            shown?.Visibility?.Invoke(false);
            shown = null;
        }
        public void ResetContexts()
        {
            preview?.Dispose();
            resident?.Dispose();
            preview = shown = resident = null;
            sampledTime = float.NaN;
            SetSurfaces(0, false, false);
        }
        public void Pose(JObject view, int target, float cinematic, bool immediate = false, bool orthographic = false)
        {
            if (shown == null)
                return;
            var center = new Vector3((float)view["x"], (float)view["y"], (float)view["z"]);
            Transform subject = shown.Preview.Focus(target);
            Vector3 subjectLocal = root.InverseTransformPoint(subject.position);
            center = Vector3.Lerp(center, subjectLocal, cinematic);
            float distance = Mathf.Lerp((float)view["zoom"], Mathf.Clamp((float)view["zoom"] * .56f, orthographic ? .20f : 1.6f, 5), cinematic);
            float blend = immediate || !poseInitialized ? 1 : 1 - Mathf.Exp(-6 * Time.unscaledDeltaTime);
            renderedCenter = Vector3.Lerp(renderedCenter, center, blend);
            renderedDistance = Mathf.Lerp(renderedDistance, distance, blend);
            renderedYaw = Mathf.LerpAngle(renderedYaw, (float)view["yaw"], blend);
            renderedPitch = Mathf.LerpAngle(renderedPitch, (float)view["pitch"], blend);
            poseInitialized = true;
            var rotation = Quaternion.Euler(renderedPitch, renderedYaw, 0);
            Camera.orthographic = orthographic;
            Camera.orthographicSize = renderedDistance;
            Vector3 position = root.TransformPoint(renderedCenter) + rotation * new Vector3(0, 0, -(orthographic ? Mathf.Max(20, renderedDistance * 3) : renderedDistance));
            if (Vector3.Distance(Camera.transform.position, position) > .0001f || Quaternion.Angle(Camera.transform.rotation, rotation) > .001f)
                dirty = true;
            Camera.transform.SetPositionAndRotation(position, rotation);
            actualView = (JObject)view.DeepClone();
            actualView["x"] = renderedCenter.x;
            actualView["y"] = renderedCenter.y;
            actualView["z"] = renderedCenter.z;
            actualView["zoom"] = renderedDistance;
            actualView["yaw"] = renderedYaw;
            actualView["pitch"] = renderedPitch;
            Camera.backgroundColor = shown.Kind is 1 or 7 ? new Color(.045f, .055f, .11f) : Color.Lerp(shown.Preview.Theme, new Color(.64f, .75f, .80f), .55f);
        }
        public void SetGlass(float progress, float strength, bool entering, Color tint)
        {
            frontMaterial.SetFloat("_GlassProgress", progress);
            frontMaterial.SetFloat("_GlassStrength", strength);
            frontMaterial.SetFloat("_GlassDirection", entering ? 1 : -1);
            frontMaterial.SetColor("_GlassTint", tint.linear);
        }
        public void SetSurfaces(float blend, bool hasPreview, bool inside)
        {
            EntryBlend = blend;
            backdrop.gameObject.SetActive(false);
            foreground.gameObject.SetActive(hasPreview && blend > .001f);
            backMaterial.color = new Color(1, 1, 1, .26f);
            frontMaterial.color = new Color(1, 1, 1, blend);
            float height = output.orthographic ? output.orthographicSize * 2 : 2 * Mathf.Tan(output.fieldOfView * Mathf.Deg2Rad * .5f) * .3f;
            foreach (var surface in new[] { backdrop, foreground })
            {
                float surfaceHeight = output.orthographic ? height : height * surface.localPosition.z / .3f;
                surface.localScale = new Vector3(surfaceHeight * output.aspect, surfaceHeight, 1);
            }
        }
        public void Render(bool live)
        {
            if (shown == null)
                return;
            int width = Mathf.Clamp(output.pixelWidth, 320, 1280), height = Mathf.Max(180, Mathf.RoundToInt(width / Mathf.Max(.1f, output.aspect)));
            if (!texture || texture.width != width || texture.height != height)
            {
                if (texture)
                {
                    if (Camera)
                        Camera.targetTexture = null;
                    texture.Release();
                    Object.Destroy(texture);
                }
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Reusable gallery projection", antiAliasing = 1 };
                texture.Create();
                Camera.targetTexture = texture;
                Camera.aspect = width / (float)height;
                backMaterial.SetTexture("_BaseMap", texture);
                frontMaterial.SetTexture("_BaseMap", texture);
                dirty = true;
            }
            if (!dirty || !live && Time.unscaledTime < nextRender)
                return;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            if (!RenderPipeline.SupportsRenderRequest(Camera, request))
                throw new InvalidOperationException("URP camera render requests unavailable for the gallery.");
            RenderPipeline.SubmitRenderRequest(Camera, request);
            RenderCount++;
            dirty = false;
            nextRender = Time.unscaledTime + 1f / 12;
        }
        public Vector3 FocusPosition(int index) => shown == null ? Vector3.zero : shown.Preview.Focus(index).position;
        public Vector3 FocusLocalPosition(int index) => root.InverseTransformPoint(FocusPosition(index));
        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
            if (output)
                output.cullingMask = previousCullingMask;
            preview?.Dispose();
            resident?.Dispose();
            if (texture)
            {
                if (Camera)
                    Camera.targetTexture = null;
                texture.Release();
                Object.Destroy(texture);
            }
            if (backdrop)
                Object.Destroy(backdrop.gameObject);
            if (foreground)
                Object.Destroy(foreground.gameObject);
            Object.Destroy(backMaterial);
            Object.Destroy(frontMaterial);
            if (root)
                Object.Destroy(root.gameObject);
        }
    }
}
#endif
