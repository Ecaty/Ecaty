#if ECATY_CAP_SPLINES
using System;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using sc.splinemesher.pro.runtime;
namespace Ecaty.Flecs.PluginAdapters {
    public sealed class SplineCapability:CapabilityInstance {
        readonly SplineContainer container;SplineMesher mesher;bool fill;
        public SplineCapability(CapabilityContext c):base(c){container=Host.AddComponent<SplineContainer>();}
        public override void Apply(JObject j){
            var points=Fields.Points(j.S("points"),j.B("closed")?3:2);
            var spline=new Spline();foreach(var p in points){var a=(JArray)p;spline.Add(new BezierKnot(new float3((float)a[0],(float)a[1],(float)a[2])),TangentMode.Linear);}spline.Closed=j.B("closed");container.Spline=spline;
            if(mesher&&fill!=j.B("fill"))throw new ArgumentException("Remove/recreate the capability when changing curve to filled area.");fill=j.B("fill");
            if(!mesher){mesher=fill?Host.AddComponent<SplineFillMesher>():Host.AddComponent<SplineCurveMesher>();mesher.root=Host.transform;mesher.rebuildTriggers=0;mesher.SetSplineContainer(container);}
            var profile=Context.Asset<Mesh>(j.S("profile"));
            if(mesher is SplineCurveMesher curve){curve.settings.input.SetDefaults();curve.settings.input.shape=profile?CurveMeshSettings.Shape.Custom:CurveMeshSettings.Shape.Plane;curve.settings.input.mesh=profile;curve.settings.input.plane.width=Mathf.Clamp(j.F("width",1),.05f,100);curve.settings.renderer.materials=new[]{Context.Runtime.World.presentationMaterial};}
            if(mesher is SplineFillMesher area){if(!spline.Closed)throw new ArgumentException("Filled areas need a closed spline.");area.settings.renderer.material=Context.Runtime.World.presentationMaterial;}
            mesher.Rebuild();
        }
        public override void Dispose(){if(mesher)mesher.Dispose();}
    }

}
#endif
