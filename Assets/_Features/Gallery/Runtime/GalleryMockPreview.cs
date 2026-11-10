#if ECATY_CAP_FLEXALON
using System;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ecaty
{
    // Procedural, disposable stand-ins. Real preview recipes replace this whole class later.
    public sealed class GalleryMockPreview : IDisposable
    {
        readonly Transform root;
        readonly int kind;
        readonly float width, depth;
        readonly List<Transform> moving = new();
        readonly List<Vector3> rest = new();
        readonly List<Material> materials;
        readonly List<Mesh> meshes = new();
        bool savedContent;
        Color baseColor;
        public Transform Focus(int index) => moving.Count == 0 || kind == 3 ? root : kind == 6 ? moving[moving.Count - 1] : moving[Math.Abs(index) % moving.Count];
        public int FocusCount => kind is 3 or 6 ? 1 : Mathf.Max(1, moving.Count);
        public Color Theme => baseColor;
        public Bounds MeasureBounds()
        {
            var filters = root.GetComponentsInChildren<MeshFilter>(true); var bounds = new Bounds(); bool first = true;
            for (int sample = 0; sample < 9; sample++)
            {
                Animate(sample * .8f, Mathf.Sin(sample * .8f * 1.8f));
                foreach (var filter in filters)
                {
                    var mesh = filter.sharedMesh; if (!mesh) continue; var b = mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 local = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(local));
                        if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
                    }
                }
            }
            Animate(0, 0); bounds.Expand(.05f); return bounds;
        }

        public GalleryMockPreview(Transform parent,JArray features,List<Material> owned)
        {
            root=parent;kind=8;width=depth=1;materials=owned;savedContent=true;baseColor=new Color(.48f,.64f,.57f);
            if(features.Count>0&&features[0]["Color"] is JObject accent)baseColor=new Color((float)accent["r"],(float)accent["g"],(float)accent["b"]);
            foreach(JObject feature in features){int type=(int)feature["Primitive"]["kind"];var p=feature["Position"];var z=feature["Scale"];var c=feature["Color"];var r=feature["Rotation"];
                var item=Primitive(new[]{PrimitiveType.Cube,PrimitiveType.Sphere,PrimitiveType.Capsule,PrimitiveType.Cylinder,PrimitiveType.Plane,PrimitiveType.Quad}[type],new Vector3((float)p["x"],(float)p["y"],(float)p["z"]),new Vector3((float)z["x"],(float)z["y"],(float)z["z"]),Material(new Color((float)c["r"],(float)c["g"],(float)c["b"],(float)c["a"])));
                item.localRotation=Quaternion.Euler((float)r["x"],(float)r["y"],(float)r["z"]);if(type!=4)Moving(item);
            }
        }
        public void UseNativeTargets(Transform[] targets){foreach(var renderer in root.GetComponentsInChildren<Renderer>())renderer.enabled=false;moving.Clear();moving.AddRange(targets);}

        public GalleryMockPreview(Transform parent, int type, int columns, int rows, List<Material> owned)
        {
            root = parent; kind = type; materials = owned;
            width = columns * 2.3f - .65f;
            depth = rows * 2.3f - .65f;
            Color[] palette = { new(.10f,.42f,.48f), new(.075f,.11f,.23f), new(.44f,.59f,.31f), new(.31f,.43f,.52f), new(.62f,.47f,.31f), new(.31f,.27f,.46f), new(.12f,.43f,.65f), new(.49f,.32f,.48f) };
            baseColor = palette[type % palette.Length];
            switch (kind)
            {
                case 0: Aquarium(); break;
                case 1: Space(); break;
                case 2: Garden(); break;
                case 3: Rain(); break;
                case 4: March(); break;
                case 5: Music(); break;
                case 6: Ocean(); break;
                case 7: Orbit(); break;
                case 8: Moving(Primitive(PrimitiveType.Sphere,new Vector3(0,.45f,0),Vector3.one*.5f,Material(new Color(.45f,.72f,.76f)))); break;
            }
        }

        Material Material(Color color, bool transparent = false)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit"));
            material.color = color;
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .32f);
            if (transparent)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            materials.Add(material); return material;
        }
        Transform Primitive(PrimitiveType type, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = "Mock " + type;
            item.transform.SetParent(parent ? parent : root, false);
            item.transform.localPosition = position; item.transform.localScale = scale;
            var collider = item.GetComponent<Collider>(); collider.enabled = false; Object.Destroy(collider);
            item.GetComponent<Renderer>().sharedMaterial = material;
            return item.transform;
        }
        Transform Triangle(Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var mesh = new Mesh { name = "Disposable gallery triangle" };
            mesh.vertices = new[] { new Vector3(-.5f,0,-.45f), new Vector3(.5f,0,-.45f), new Vector3(0,0,.6f), new Vector3(0,.23f,-.24f) };
            mesh.triangles = new[] { 0,2,1, 0,1,3, 1,2,3, 2,0,3 }; mesh.RecalculateNormals(); meshes.Add(mesh);
            var go = new GameObject("Mock triangular form", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent ? parent : root, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
        void Moving(Transform item) { moving.Add(item); rest.Add(item.localPosition); }

        void Aquarium()
        {
            var orange = Material(new Color(1,.57f,.17f)); var blue = Material(new Color(.28f,.67f,.96f));
            for(int i=0;i<9;i++)
            {
                var fish = new GameObject("Swimming fish").transform; fish.SetParent(root,false);
                fish.localPosition=new Vector3(0,.35f+(i%3)*.21f,(i%2-.5f)*.45f);
                Primitive(PrimitiveType.Sphere,Vector3.zero,new Vector3(.32f,.17f,.12f),i%2==0?orange:blue,fish);
                var tail=Triangle(new Vector3(-.2f,0,0),new Vector3(.19f,.7f,.2f),orange,fish); tail.localRotation=Quaternion.Euler(90,0,90);
                Moving(fish);
            }
        }
        void Space()
        {
            var stars=Material(new Color(.87f,.88f,1)); var ships=Material(new Color(.80f,.83f,.90f));
            for(int i=0;i<25;i++)
                Primitive(PrimitiveType.Sphere,new Vector3(Mathf.Sin(i*5.4f)*width*.44f,.1f+(i%7)*.16f,Mathf.Cos(i*3.7f)*depth*.42f),Vector3.one*(i%5==0?.035f:.018f),stars);
            var planet=Primitive(PrimitiveType.Sphere,new Vector3(-width*.32f,.5f,depth*.45f),Vector3.one*.67f,Material(new Color(.54f,.41f,.72f)));
            for(int i=0;i<3;i++)
            {
                var ship=Triangle(new Vector3(.4f,.35f+i*.18f,0),new Vector3(.40f,.5f,.65f),ships);
                Moving(ship);
            }
        }
        void Garden()
        {
            var moss=Primitive(PrimitiveType.Sphere,new Vector3(0,-.035f,0),new Vector3(1.25f,.13f,1.16f),Material(new Color(.37f,.48f,.27f)));
            moss.name="Organic moss";
            var bark=Material(new Color(.39f,.27f,.17f)); var leaves=Material(new Color(.31f,.57f,.28f));
            Primitive(PrimitiveType.Cylinder,new Vector3(0,.30f,0),new Vector3(.15f,.3f,.15f),bark);
            var canopy=Primitive(PrimitiveType.Sphere,new Vector3(0,.82f,0),new Vector3(.94f,.93f,.84f),leaves); Moving(canopy);
            var flowers=Material(new Color(1,.72f,.45f));
            for(int i=0;i<9;i++) Primitive(PrimitiveType.Sphere,new Vector3(Mathf.Sin(i*2.3f)*.59f,.08f,Mathf.Cos(i*2.3f)*.55f),Vector3.one*.10f,flowers);
        }
        void Rain()
        {
            var grass=Material(new Color(.32f,.49f,.39f));
            var rock=Material(new Color(.46f,.51f,.55f));
            Triangle(new Vector3(-.5f,.07f,0),new Vector3(1.2f,2.6f,1.2f),rock);
            Triangle(new Vector3(.35f,.07f,.15f),new Vector3(1.1f,3.3f,1.0f),grass);
            var cloud=Material(new Color(.68f,.74f,.79f));
            for(int i=0;i<5;i++) Primitive(PrimitiveType.Sphere,new Vector3(-1+i*.48f,1.25f,.1f),new Vector3(.74f,.35f,.50f),cloud);
            var rain=Material(new Color(.49f,.76f,.94f));
            for(int i=0;i<26;i++)
            {
                var drop=Primitive(PrimitiveType.Cube,new Vector3(Mathf.Sin(i*4.8f)*width*.43f,(i%9)*.13f,Mathf.Cos(i*2.6f)*depth*.4f),new Vector3(.014f,.12f,.014f),rain);
                Moving(drop);
            }
        }
        void March()
        {
            var body=Material(new Color(.22f,.36f,.57f)); var head=Material(new Color(.94f,.72f,.48f));
            var flag=Material(new Color(.85f,.29f,.23f));
            for(int i=0;i<12;i++)
            {
                var actor=new GameObject("Marching figure").transform;actor.SetParent(root,false);
                actor.localPosition=new Vector3((i%4-1.5f)*.62f,.07f,(i/4-1)*.38f);
                Primitive(PrimitiveType.Capsule,new Vector3(0,.20f,0),new Vector3(.16f,.20f,.16f),body,actor);
                Primitive(PrimitiveType.Sphere,new Vector3(0,.47f,0),Vector3.one*.18f,head,actor);
                Primitive(PrimitiveType.Cube,new Vector3(-.06f,.05f,0),new Vector3(.065f,.14f,.13f),body,actor);
                Primitive(PrimitiveType.Cube,new Vector3(.06f,.05f,0),new Vector3(.065f,.14f,.13f),body,actor);
                Moving(actor);
            }
            Primitive(PrimitiveType.Cylinder,new Vector3(width*.43f,.60f,0),new Vector3(.025f,.60f,.025f),head);
            Primitive(PrimitiveType.Cube,new Vector3(width*.43f-.23f,1.0f,0),new Vector3(.43f,.28f,.035f),flag);
        }
        void Music()
        {
            var colors=new[]{new Color(.95f,.49f,.43f),new Color(.98f,.73f,.33f),new Color(.44f,.75f,.70f),new Color(.49f,.61f,.86f),new Color(.78f,.55f,.85f)};
            for(int i=0;i<5;i++) Moving(Primitive(PrimitiveType.Cylinder,new Vector3((i-2)*.25f,.35f,0),new Vector3(.15f,.35f,.15f),Material(colors[i])));
        }
        void Ocean()
        {
            var waves=Material(new Color(.49f,.81f,.88f));
            for(int i=0;i<9;i++) Moving(Primitive(PrimitiveType.Cube,new Vector3((i%3-1)*1.05f,.06f,(i/3-1)*.45f),new Vector3(.65f,.035f,.035f),waves));
            var hull=Triangle(new Vector3(.5f,.15f,0),new Vector3(.58f,.7f,.88f),Material(new Color(.58f,.32f,.22f)));
            Primitive(PrimitiveType.Cylinder,new Vector3(0,.32f,0),new Vector3(.03f,.36f,.03f),Material(new Color(.88f,.80f,.62f)),hull);
            var sail=Triangle(new Vector3(.16f,.46f,0),new Vector3(.65f,1,.7f),Material(new Color(1,.91f,.69f)),hull);sail.localRotation=Quaternion.Euler(90,0,0);
            Moving(hull);
        }
        void Orbit()
        {
            var center=Material(new Color(.97f,.73f,.39f));
            Primitive(PrimitiveType.Sphere,new Vector3(0,.62f,0),Vector3.one*.5f,center);
            for(int i=0;i<6;i++) Moving(Primitive(PrimitiveType.Sphere,new Vector3(0,.6f,0),Vector3.one*.15f,Material(Color.HSVToRGB(i/6f,.35f,.95f))));
        }

        public void Animate(float time, float wave)
        {
            if(savedContent)return;
            for(int i=0;i<moving.Count;i++)
            {
                var item=moving[i]; var position=rest[i]; float phase=time+i*.73f;
                switch(kind)
                {
                    case 0:
                        position.x=Mathf.Sin(phase*.72f)*width*.37f;
                        item.localRotation=Quaternion.Euler(0,Mathf.Cos(phase*.72f)>0?0:180,0); break;
                    case 1:
                        position.x=Mathf.Sin(phase*.5f)*width*.34f;
                        position.z=Mathf.Cos(phase*.5f)*depth*.29f;
                        item.localRotation=Quaternion.Euler(0,-phase*.5f*Mathf.Rad2Deg,0); break;
                    case 2: item.localRotation=Quaternion.Euler(wave*3,0,wave*5); break;
                    case 3: position.y=1.15f-Mathf.Repeat(time*1.1f+i*.12f,1.1f);position.x-=Mathf.Repeat(time*.25f, .12f);break;
                    case 4:
                        position.y+=Mathf.Abs(Mathf.Sin(time*5+(i%2)*Mathf.PI))*.055f;
                        position.x+=Mathf.Sin(time*.6f)*.12f;
                        item.localRotation=Quaternion.Euler(0,90,Mathf.Sin(time*5+i)*4);break;
                    case 5:
                        float height=.38f+.24f*Mathf.Sin(time*2.3f+i);
                        position.y=height;item.localScale=new Vector3(.15f,height,.15f);break;
                    case 6:
                        position.y+=Mathf.Sin(phase*1.5f)*.045f;
                        if(i==moving.Count-1)item.localRotation=Quaternion.Euler(Mathf.Sin(phase)*5,15,wave*4);break;
                    case 7:
                        position=new Vector3(Mathf.Cos(phase*.65f)*.60f,.62f+Mathf.Sin(phase)*.27f,Mathf.Sin(phase*.65f)*.56f);break;
                }
                item.localPosition=position;
            }
        }
        public void Highlight(bool selected, bool dragging)
        {
            // Selection is an independent spatial outline; no common platform is imposed on worlds.
        }
        public void SetArchivedPresentation(bool archived, float time)
        {
            Animate(time, Mathf.Sin(time * 1.8f));
        }
        public void Dispose() { foreach(var mesh in meshes)if(mesh)Object.Destroy(mesh); }
    }
}
#endif
