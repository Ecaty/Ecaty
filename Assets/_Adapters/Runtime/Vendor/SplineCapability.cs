using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_SPLINES
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using sc.splinemesher.pro.runtime;
#endif
namespace Ecaty.Flecs.PluginAdapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapSpline", "spline geometry", 0, Gate = "ECATY_CAP_SPLINES", Probe = "Packages/xyz.staggart-creations.splinemesher-pro/package.json")]
    public sealed class SplineConfig
    {
        public string points = "";
        public float width = 1;
        [AssetKey] public string profile = "";
        public bool closed, fill;
    }
#if ECATY_CAP_SPLINES
    public sealed class SplineCapability : CapabilityInstance<SplineConfig>
    {
        readonly SplineContainer container; SplineMesher mesher; bool fill;
        public SplineCapability(CapabilityContext c) : base(c) { container = Host.AddComponent<SplineContainer>(); }
        protected override void Apply(SplineConfig j)
        {
            var points = Fields.Points(j.points, j.closed ? 3 : 2);
            var spline = new Spline();
            foreach (var p in points)
            {
                var a = (JArray)p;
                spline.Add(new BezierKnot(new float3((float)a[0], (float)a[1], (float)a[2])), TangentMode.Linear);
            }
            spline.Closed = j.closed;
            container.Spline = spline;
            if (mesher && fill != j.fill)
                throw new ArgumentException("Remove/recreate the capability when changing curve to filled area.");
            fill = j.fill;
            if (!mesher)
            {
                mesher = fill ? Host.AddComponent<SplineFillMesher>() : Host.AddComponent<SplineCurveMesher>();
                mesher.root = Host.transform;
                mesher.rebuildTriggers = 0;
                mesher.SetSplineContainer(container);
            }
            var profile = Context.Asset<Mesh>(j.profile);
            if (mesher is SplineCurveMesher curve)
            {
                curve.settings.input.SetDefaults();
                curve.settings.input.shape = profile ? CurveMeshSettings.Shape.Custom : CurveMeshSettings.Shape.Plane;
                curve.settings.input.mesh = profile;
                curve.settings.input.plane.width = Mathf.Clamp(j.width, .05f, 100);
                curve.settings.renderer.materials = new[] { Context.Runtime.World.presentationMaterial };
            }
            if (mesher is SplineFillMesher area)
            {
                if (!spline.Closed)
                    throw new ArgumentException("Filled areas need a closed spline.");
                area.settings.renderer.material = Context.Runtime.World.presentationMaterial;
            }
            mesher.Rebuild();
        }
        public override void Dispose()
        {
            if (mesher)
                mesher.Dispose();
        }
    }
#endif
}
