using Ecaty.Flecs.Capabilities;
#if ECATY_CAP_COZY
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using ProjectDawn.CozyBuilder;
#endif
namespace Ecaty.Flecs.PluginAdapters
{
    // Always compiled: the schema and saved configurations stay valid without the plugin.
    [Capability("CapBuilding", "buildings", 10, Gate = "ECATY_CAP_COZY", Probe = "Packages/com.projectdawn.cozy-builder/package.json")]
    public sealed class BuildingConfig
    {
        [AssetKey] public string prefab = "";
        public string points = "";
    }
#if ECATY_CAP_COZY
    public sealed class BuildingCapability : CapabilityInstance<BuildingConfig>
    {
        GameObject building; string prefabKey; CozyRenderer renderer; System.Collections.Generic.List<Material> materials = new();
        public BuildingCapability(CapabilityContext c) : base(c) { }
        protected override void Apply(BuildingConfig j)
        {
            if (building && prefabKey != j.prefab)
                Dispose();
            if (!building)
            {
                prefabKey = j.prefab;
                var kit = Context.Asset<GameObject>(j.prefab);
                if (!kit)
                    throw new ArgumentException("Register a Cozy construction-kit prefab.");
                building = UnityEngine.Object.Instantiate(kit, Host.transform);
                renderer = building.GetComponentInChildren<CozyRenderer>();
                if (!renderer)
                    throw new ArgumentException("Construction kit has no CozyRenderer.");
                foreach (var draw in building.GetComponentsInChildren<CozyDraw>())
                if (draw.OverrideMaterial && !draw.OverrideMaterial.enableInstancing)
                {
                    var clone = new Material(draw.OverrideMaterial) { enableInstancing = true };
                    materials.Add(clone);
                    draw.OverrideMaterial = clone;
                }
            }
            if (!string.IsNullOrEmpty(j.points))
            {
                var spline = building.GetComponentInChildren<CozySpline>();
                if (!spline)
                    throw new ArgumentException("Kit has no editable CozySpline.");
                var points = Fields.Points(j.points, 2);
                foreach (var p in spline.Points)
                if (p)
                    UnityEngine.Object.Destroy(p.gameObject);
                spline.Points.Clear();
                foreach (JArray p in points)
                {
                    var go = new GameObject("Construction point");
                    go.transform.SetParent(spline.transform, false);
                    go.transform.localPosition = new Vector3((float)p[0], (float)p[1], (float)p[2]);
                    spline.Points.Add(go.AddComponent<CozyPoint>());
                }
            }
            renderer.Build();
        }
        public override void Dispose()
        {
            if (building)
            {
                building.SetActive(false);
                UnityEngine.Object.Destroy(building);
                building = null;
            }
            renderer = null;
            foreach (var material in materials)
                UnityEngine.Object.Destroy(material);
            materials.Clear();
        }
    }
#endif
}
