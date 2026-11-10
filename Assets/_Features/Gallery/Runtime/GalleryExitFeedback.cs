#if ECATY_CAP_FLEXALON
using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace Ecaty
{
    /// <summary>Owned presentation only. Native navigation owns the gesture and feedback target.</summary>
    public sealed class GalleryExitFeedback:IDisposable
    {
        readonly Camera camera;readonly Transform overlay;readonly Renderer renderer;readonly Material material;
        readonly GalleryFeedbackSpring spring=new();
        public float Value {get;private set;}
        public float GlassAmount {get;private set;}
        public GalleryExitFeedback(Camera output)
        {
            camera=output;var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name="World exit elastic vignette";go.layer=2;overlay=go.transform;overlay.SetParent(output.transform,false);overlay.localPosition=new Vector3(0,0,.06f);UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            renderer=go.GetComponent<Renderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;material=new Material(Resources.Load<Shader>("Gallery/Shaders/GalleryExitFeedback"));renderer.sharedMaterial=material;renderer.enabled=false;
        }
        public void Update(float deltaTime,float target,GalleryLayoutProfile profile,float glassProgress)
        {
            Value=spring.Step(Mathf.Clamp01(target),1/(Mathf.PI*6),deltaTime,.85f);
            GlassAmount=Mathf.Sin(Mathf.Clamp01(glassProgress)*Mathf.PI)*profile.GlassPassStrength;renderer.enabled=Value*profile.ExitVignette>.001f||GlassAmount>.001f;if(!renderer.enabled)return;
            float height=2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*.06f;overlay.localScale=new Vector3(height*camera.aspect,height,1);material.SetFloat("_GlassProgress",glassProgress);material.SetFloat("_GlassStrength",profile.GlassPassStrength);material.SetColor("_GlassTint",(profile.Theme?profile.Theme:EcatyTheme.Default).Glass.linear);material.SetFloat("_Strength",Value*profile.ExitVignette);material.SetColor("_Tint",(profile.Theme?profile.Theme:EcatyTheme.Default).Background.linear);
        }
        public void Dispose(){if(overlay)UnityEngine.Object.Destroy(overlay.gameObject);if(material)UnityEngine.Object.Destroy(material);}
    }
}
#endif
