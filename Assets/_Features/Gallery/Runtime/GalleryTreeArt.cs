#if CAP_FLEXALON
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
namespace Game
{
    public sealed class GalleryTreeArt : IDisposable
    {
        public sealed class Band
        {
            public Transform Root; public Renderer Renderer; public Mesh Mesh; public readonly MaterialPropertyBlock Properties = new(); public readonly Vector4[] Centers = new Vector4[32];
        }
        readonly List<Band> bands = new();
        readonly Material orbit, crystal;
        public IReadOnlyList<Band> Bands => bands;
        public GalleryTreeArt(GalleryLayoutProfile profile)
        {
            orbit = new Material(Resources.Load<Shader>("Gallery/Shaders/GalleryOrbit"));
            crystal = new Material(Resources.Load<Shader>("Gallery/Shaders/GalleryCrystal"));
        }
        public Renderer Sphere(Transform parent, float diameter, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Collider>().enabled = false;
            Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = crystal;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }
        public void EnsureBands(Transform parent, int count, float pitch, float gap, float top, float width)
        {
            while (bands.Count < count)
            {
                var go = new GameObject("Soft volumetric orbit " + (bands.Count + 1), typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, false);
                var b = new Band { Root = go.transform, Renderer = go.GetComponent<Renderer>() };
                b.Renderer.sharedMaterial = orbit;
                b.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                bands.Add(b);
            }
            for (int i = 0; i < bands.Count; i++)
            {
                var b = bands[i];
                b.Root.gameObject.SetActive(i < count);
                if (i >= count)
                    continue;
                float radius = (i + 1) * pitch;
                b.Root.localPosition = new Vector3(0, top - (i + 1) * gap, 0);
                if (b.Mesh)
                    Object.Destroy(b.Mesh);
                b.Mesh = Tube(radius, width * .5f);
                b.Root.GetComponent<MeshFilter>().sharedMesh = b.Mesh;
            }
        }
        static Mesh Tube(float radius, float tube)
        {
            const int segments = 192, sides = 16;
            var v = new Vector3[(segments + 1) * (sides + 1)];
            var normals = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            var triangles = new int[segments * sides * 6];
            int n = 0;
            for (int i = 0; i <= segments; i++)
                for (int j = 0; j <= sides; j++)
                {
                    float u = i * 2 * Mathf.PI / segments, t = j * 2 * Mathf.PI / sides, c = Mathf.Cos(t), s = Mathf.Sin(t);
                    int k = i * (sides + 1) + j;
                    v[k] = new Vector3(Mathf.Cos(u) * (radius + tube * c), tube * s * .38f, Mathf.Sin(u) * (radius + tube * c));
                    normals[k] = new Vector3(Mathf.Cos(u) * c, s / .38f, Mathf.Sin(u) * c).normalized;
                    uv[k] = new Vector2(i / (float)segments, j / (float)sides);
                    if (i < segments && j < sides)
                    {
                        int b = k + sides + 1;
                        triangles[n++] = k;
                        triangles[n++] = k + 1;
                        triangles[n++] = b;
                        triangles[n++] = k + 1;
                        triangles[n++] = b + 1;
                        triangles[n++] = b;
                    }
                }
            var mesh = new Mesh { name = "Rounded gallery orbit" };
            mesh.vertices = v;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
        public void Dispose()
        {
            foreach (var b in bands)
            {
                if (b.Mesh)
                    Object.Destroy(b.Mesh);
                if (b.Root)
                    Object.Destroy(b.Root.gameObject);
            }
            Object.Destroy(orbit);
            Object.Destroy(crystal);
        }
    }
}
#endif
