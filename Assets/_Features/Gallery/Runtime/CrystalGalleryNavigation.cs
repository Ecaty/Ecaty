#if ECATY_CAP_FLEXALON
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Ecaty
{
    public sealed partial class CrystalWorldGallery
    {
        public EcatyTheme Theme=>RuntimeComposition.Current.Theme?RuntimeComposition.Current.Theme:EcatyTheme.Default;
        bool N(string key)=>(bool?)navigation?[key]==true;
        bool entryArmed {get=>N("approach");set{if(navigation==null)return;navigation["approach"]=value;WriteNavigation();}}
        public bool Returning=>N("returning");
        bool FocusedGallery=>!string.IsNullOrEmpty((string)navigation?["target"])&&currentZoom>profile.FullTreeZoom+.015f;
        public Vector3 OrbitCenter=>currentCenter;
        public float InvitationOpacity=>invitationAlpha;
        void WriteNavigation()=>Write("GalleryTreeSettings","GalleryTreeNavigation",navigation);
        void WriteAnchor(bool active,Vector3 origin,float zoom) {
            Write("GalleryTreeSettings","GalleryCameraAnchor",new JObject{["active"]=active,["x"]=origin.x,["y"]=origin.y,["z"]=origin.z,["zoom"]=Mathf.Max(profile.FullTreeZoom*1.001f,zoom),["minimum"]=profile.FullTreeZoom,["centerY"]=GalleryCenterY});
        }
        void DeactivateZoomAnchor(){WriteAnchor(false,currentCenter,currentZoom);view["x"]=currentCenter.x;view["y"]=currentCenter.y;view["z"]=currentCenter.z;}
        void ZoomOutGallery(float ticks) {
            if(CinematicReturn)CancelCinematicReturn();
            if(!(bool)Read("GalleryTreeSettings","GalleryCameraAnchor")["active"])WriteAnchor(true,currentCenter,currentZoom);
            view["zoom"]=Mathf.Max(profile.FullTreeZoom,(float)view["zoom"]*Mathf.Exp(Mathf.Clamp(ticks,-4,0)*.12f));WriteView();
            if(currentZoom<=profile.FullTreeZoom+.015f){navigation["target"]="";WriteNavigation();}
        }
        void StartTreeReturn(bool cinematic=false) {
            entryArmed=false;navigation["returning"]=true;navigation["exitArmed"]=false;navigation["exitPull"]=0;WriteNavigation();
            if(cinematic)BeginCinematicReturn();else{Write("GalleryExitGallery","GalleryView",view);BeginCinematicReturn();}
            view["pitch"]=90;WriteView();SetPaused(true);QueueSave();
        }
        void FinishTreeReturn() {
            if(!Returning||InsideWorld||Creating||IsDragging||N("portalReturn")||ReturnProgress<.999f||ReturnReveal<.999f)return;
            currentPitch=90;view["pitch"]=90;navigation["returning"]=false;navigation["target"]=CurrentWorldId??"";WriteNavigation();EndCinematicReturn();
            WriteAnchor(true,currentCenter,currentZoom);WriteView();SetPaused(FocusedGallery);QueueSave();
        }
        void MatchReturnCamera() {
            var portal=Read("GalleryExitGallery","GalleryView");view=(JObject)portal.DeepClone();DeactivateZoomAnchor();view=(JObject)portal.DeepClone();WriteView();ApplyCamera(true);
        }
        bool TryRingAngle(Vector2 point,int layer,out float angle) {
            angle=0;if(layer<=0||layer>layers.Count)return false;
            var origin=layers[layer-1].transform.position;var ray=output.ScreenPointToRay(point);
            if(!new Plane(Vector3.up,origin).Raycast(ray,out float distance))return false;
            var offset=ray.GetPoint(distance)-origin;if(new Vector2(offset.x,offset.z).magnitude<layers[layer-1].Radius*.12f)return false;
            angle=Mathf.Atan2(offset.z,offset.x)*Mathf.Rad2Deg;return true;
        }
        void ApplyDestinationOpacity() {
            if(!destination)return;var properties=new MaterialPropertyBlock();properties.SetFloat("_WorldOpacity",destinationOpacity);properties.SetFloat("_Opacity",F("sphereOpacity"));properties.SetFloat("_Rim",F("rim"));properties.SetFloat("_Frost",F("frost"));properties.SetColor("_Tint",Theme.Selection.linear);
            foreach(var renderer in destination.GetComponentsInChildren<Renderer>())renderer.SetPropertyBlock(properties);destination.gameObject.SetActive(destinationOpacity>.001f);
        }
        void UpdateDestination(){if(!destination)return;destinationOpacity=destinationSpring.Step(swapTarget!=null?profile.DestinationGhostOpacity:0,profile.SwapFadeTime,Time.unscaledDeltaTime);ApplyDestinationOpacity();}
        static float OrbitYaw(Quaternion rotation){var right=rotation*Vector3.right;return Mathf.Atan2(-right.z,right.x)*Mathf.Rad2Deg;}
        static float OrbitPitch(Quaternion rotation)=>Mathf.Asin(Mathf.Clamp(-(rotation*Vector3.forward).y,-1,1))*Mathf.Rad2Deg;
        public static Quaternion OrbitRotation(float yaw,float pitch)=>Quaternion.AngleAxis(yaw,Vector3.up)*Quaternion.AngleAxis(pitch,Vector3.right);
    }
}
#endif
