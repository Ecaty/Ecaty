#if ECATY_CAP_FLEXALON
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Ecaty
{
    public sealed partial class CrystalWorldGallery
    {
        string heldObject;Vector2 objectDown;
        void PrepareExperienceContext(GalleryPreviewStage.Context context)
        {
            if(experiences.ResidentId==context.Id&&context.NativeGeneration==experiences.Generation){experiences.SetVisible(context.Id,true);return;}
            if(!experiences.Has(context.Id)){experiences.Unload();return;}
            experiences.Activate(context.Id,context.Root.transform.position);
            context.Preview.UseNativeTargets(experiences.FocusTargets());context.NativeGeneration=experiences.Generation;
            context.Visibility=show=>experiences.SetVisible(context.Id,show);
        }
        void StepExperiencePointer(Vector2 point,bool start,bool release,bool held,Vector2 delta)
        {
            if(!experiences.Has(CurrentWorldId)||!experiences.ResidentRoot)return;
            var ray=stage.Camera.ViewportPointToRay(new Vector3(point.x/output.pixelWidth,point.y/output.pixelHeight,0));
            if(start){objectDown=point;heldObject=null;float nearest=float.PositiveInfinity;foreach(var target in experiences.FocusTargets())foreach(var mesh in target.GetComponentsInChildren<MeshFilter>()){
                if(!mesh.sharedMesh)continue;var localRay=new Ray(mesh.transform.InverseTransformPoint(ray.origin),mesh.transform.InverseTransformVector(ray.direction).normalized);
                if(mesh.sharedMesh.bounds.IntersectRay(localRay,out float localHit)){float hitDepth=Vector3.Dot(mesh.transform.TransformPoint(localRay.GetPoint(localHit))-ray.origin,ray.direction);if(hitDepth>=0&&hitDepth<nearest){nearest=hitDepth;heldObject=target.name.Split('.').Last();}}}if(heldObject!=null)experiences.Select(heldObject);}
            var root=experiences.ResidentRoot;
            if(held&&heldObject!=null&&delta.sqrMagnitude>0&&new Plane(Vector3.up,root.position).Raycast(ray,out float distance)){var local=root.InverseTransformPoint(ray.GetPoint(distance));experiences.Move(heldObject,local);}
            if(release){if(heldObject==null&&(point-objectDown).sqrMagnitude<49&&new Plane(Vector3.up,root.position).Raycast(ray,out float dropDistance)){var local=root.InverseTransformPoint(ray.GetPoint(dropDistance));if(local.sqrMagnitude<10000)experiences.Place(local);}heldObject=null;SaveArrangement();}
        }
        public string ReuseWorldObject(string from,string name,string to){var result=experiences.Reuse(from,name,to);RefreshExperiencePreview(to);SaveArrangement();return result;}
        void RefreshExperiencePreview(string id)
        {
            if(!byId.TryGetValue(id,out var item)||!experiences.Has(id))return;
            // Recreate this disposable preview only; persistent world data remains native.
            var entry=item.Entry;entry.Preview.Dispose();var previousContent=entry.Content;previousContent.SetParent(null);previousContent.gameObject.SetActive(false);Destroy(previousContent.gameObject);
            entry.Content=new GameObject("Identifying saved world features").transform;entry.Content.SetParent(entry.Anchor,false);
            entry.SavedFeatures=experiences.Features(id);entry.Preview=new GalleryMockPreview(entry.Content,entry.SavedFeatures,materials);
            entry.NaturalBounds=entry.Preview.MeasureBounds();entry.TableScale=F("diameter")*.4f/Mathf.Max(.1f,entry.NaturalBounds.extents.magnitude);
            entry.Content.localScale=Vector3.one*entry.TableScale;entry.Content.localPosition=-entry.NaturalBounds.center*entry.TableScale;
            foreach(var collider in entry.Content.GetComponentsInChildren<Collider>())collider.enabled=false;
            item.Renderers=entry.Content.GetComponentsInChildren<Renderer>();foreach(var renderer in item.Renderers){renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;}item.Colors=item.Renderers.Select(r=>r.sharedMaterials.Select(m=>m.color).ToArray()).ToArray();
        }
    }
}
#endif
