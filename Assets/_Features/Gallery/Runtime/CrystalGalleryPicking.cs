#if ECATY_CAP_FLEXALON
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Ecaty
{
    public sealed partial class CrystalWorldGallery
    {
        float nextVisibility;
        public static float CircleCoverage(Vector2 center,float radius,Vector2 occluder,float otherRadius)
        {
            float distance=Vector2.Distance(center,occluder);if(radius<=0||otherRadius<=0||distance>=radius+otherRadius)return 0;
            if(distance<=Mathf.Abs(radius-otherRadius))return Mathf.Clamp01(otherRadius*otherRadius/(radius*radius));
            float r2=radius*radius,o2=otherRadius*otherRadius,d2=distance*distance;
            float first=Mathf.Acos(Mathf.Clamp((d2+r2-o2)/(2*distance*radius),-1,1)),second=Mathf.Acos(Mathf.Clamp((d2+o2-r2)/(2*distance*otherRadius),-1,1));
            float lens=r2*first+o2*second-.5f*Mathf.Sqrt(Mathf.Max(0,(-distance+radius+otherRadius)*(distance+radius-otherRadius)*(distance-radius+otherRadius)*(distance+radius+otherRadius)));
            return Mathf.Clamp01(lens/(Mathf.PI*r2));
        }
        public void RefreshVisibility(){nextVisibility=0;UpdateVisibility();}
        void UpdateVisibility()
        {
            if(Time.unscaledTime<nextVisibility)return;nextVisibility=Time.unscaledTime+profile.VisibilityRefreshInterval;
            float radius=F("diameter")*.5f,projection=output.pixelHeight/(2*Mathf.Tan(output.fieldOfView*Mathf.Deg2Rad*.5f));
            foreach(var item in items){var point=output.WorldToScreenPoint(item.Entry.Anchor.position);item.ScreenCenter=new Vector2(point.x,point.y);item.ScreenDepth=point.z;item.ScreenRadius=projection*radius/Mathf.Max(radius,point.z);}
            foreach(var item in items){
                if(!item.Entry.Active||!item.Entry.Anchor.gameObject.activeInHierarchy)continue;
                float coverage=0;
                foreach(var other in items){if(other==item||RevealOpacity(other.Entry.Id)<.98f||!other.Entry.Active||!other.Entry.Anchor.gameObject.activeInHierarchy||other.ScreenDepth<=0||other.ScreenDepth>=item.ScreenDepth-.002f)continue;
                    coverage+=CircleCoverage(item.ScreenCenter,item.ScreenRadius,other.ScreenCenter,other.ScreenRadius);if(coverage>=1){coverage=1;break;}}
                
                float visibility=1-F("rearFade")*Mathf.Sqrt(Mathf.Clamp01(coverage/Mathf.Max(.005f,profile.OcclusionFadeCoverage)));
                float reveal=RevealOpacity(item.Entry.Id);
                bool pickable=reveal>=.999f&&item.ScreenDepth>0&&visibility>=Mathf.Max(.98f,profile.MinimumPickOpacity)&&(IsDragging||item.Alpha>=Mathf.Max(.98f,profile.MinimumPickOpacity));
                bool changed=Mathf.Abs(item.PublishedOpacity-visibility*reveal)>.02f||Mathf.Abs(item.Coverage-coverage)>.02f||Mathf.Abs(item.Visibility-visibility)>.02f||item.Pickable!=pickable;
                item.Coverage=coverage;item.Visibility=visibility;item.Pickable=pickable;item.Hit.enabled=pickable;
                if(changed){item.PublishedOpacity=visibility*reveal;Write(item.Entry.Id,"GalleryVisibility",new JObject{["covered"]=coverage,["opacity"]=item.PublishedOpacity,["pickable"]=pickable});}
            }
        }
        public Item Pick(Vector2 screen)
        {
            UpdateVisibility();var ray=output.ScreenPointToRay(screen);float nearest=float.PositiveInfinity;Item result=null;
            foreach(var item in items)if(item.Pickable&&item.Entry.Anchor.gameObject.activeInHierarchy&&RaySphere(ray,item.Entry.Anchor.position,F("diameter")*.5f,out float distance)&&distance<nearest){nearest=distance;result=item;}return result;
        }
        public int PickLayer(Vector2 screen)
        {
            UpdateVisibility();var ray=output.ScreenPointToRay(screen);float nearest=float.PositiveInfinity;int selected=0;
            foreach(var item in items)if(item.Pickable&&item.Entry.Active&&item.Entry.Anchor.gameObject.activeInHierarchy&&RaySphere(ray,item.Entry.Anchor.position,F("diameter")*.5f,out float distance)&&distance<nearest){nearest=distance;selected=item.Level;}
            Vector3 facing=-output.transform.forward;facing.y=0;
            float thickness=F("diameter")*.5f*profile.RingHitWidthInWorlds;
            for(int i=0;i<layers.Count;i++){
                var layer=layers[i];if(!layer.gameObject.activeInHierarchy)continue;
                const int segments=64;Vector3 origin=layer.transform.position;
                for(int n=0;n<segments;n++){
                    float a=n*2*Mathf.PI/segments,b=(n+1)*2*Mathf.PI/segments;
                    Vector3 start=origin+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*layer.Radius,end=origin+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*layer.Radius;
                    if(RevealAt((start+end)*.5f)<.999f||RearMultiplier((start+end)*.5f-origin,facing,F("rearFade"))<profile.MinimumPickOpacity)continue;
                    if(RayCapsule(ray,start,end,thickness,out float distance)&&distance<nearest){nearest=distance;selected=i+1;}
                }
            }
            return selected;
        }
        // Presentation-space intersection tests avoid stale PhysX transforms and extra scene colliders.
        public static bool RaySphere(Ray ray,Vector3 center,float radius,out float distance)
        {
            Vector3 offset=ray.origin-center;float b=Vector3.Dot(offset,ray.direction),c=offset.sqrMagnitude-radius*radius,h=b*b-c;
            distance=0;if(h<0)return false;float near=-b-Mathf.Sqrt(h),far=-b+Mathf.Sqrt(h);if(far<0)return false;distance=Mathf.Max(0,near);return true;
        }
        public static bool RayCapsule(Ray ray,Vector3 start,Vector3 end,float radius,out float distance)
        {
            Vector3 ba=end-start,oa=ray.origin-start;float baba=Vector3.Dot(ba,ba),bard=Vector3.Dot(ba,ray.direction),baoa=Vector3.Dot(ba,oa),rdoa=Vector3.Dot(ray.direction,oa),oaoa=Vector3.Dot(oa,oa);
            float a=baba-bard*bard,b=baba*rdoa-baoa*bard,c=baba*oaoa-baoa*baoa-radius*radius*baba,h=b*b-a*c;distance=0;
            if(Mathf.Abs(a)>.000001f&&h>=0){float t=(-b-Mathf.Sqrt(h))/a,y=baoa+t*bard;if(t>=0&&y>=0&&y<=baba){distance=t;return true;}}
            bool hitA=RaySphere(ray,start,radius,out float ta),hitB=RaySphere(ray,end,radius,out float tb);distance=hitA&&hitB?Mathf.Min(ta,tb):hitA?ta:tb;return hitA||hitB;
        }
    }
}
#endif
