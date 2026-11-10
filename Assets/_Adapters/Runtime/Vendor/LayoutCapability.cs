#if ECATY_CAP_FLEXALON
using System;
using Ecaty.Flecs.Capabilities;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Flexalon;
namespace Ecaty.Flecs.PluginAdapters {
    public sealed class LayoutCapability:CapabilityInstance {
        FlexalonCircleLayout layout;bool owned;readonly System.Collections.Generic.Dictionary<ulong,FlexalonObject> children=new();readonly System.Collections.Generic.List<FlexalonObject> created=new();
        public LayoutCapability(CapabilityContext c):base(c){layout=Target.GetComponent<FlexalonCircleLayout>();if(layout)throw new InvalidOperationException("Target already has a layout owner.");layout=Target.gameObject.AddComponent<FlexalonCircleLayout>();owned=true;}
        void BindChildren(){
            var seen=new System.Collections.Generic.HashSet<ulong>();
            foreach(var row in JObject.Parse(Context.Session.Query("Position, (ChildOf, "+Context.Session.EntityPath(Context.TargetEntity)+")"))["results"]){
                string path=row["parent"]==null?(string)row["name"]:(string)row["parent"]+"."+(string)row["name"];ulong id=Context.Session.Lookup(path);seen.Add(id);if(children.ContainsKey(id))continue;
                var child=Context.Resolve(id);if(!child)continue;if(child.GetComponent<CharacterController>()||child.GetComponent<Rigidbody>())throw new InvalidOperationException("Layout cannot own an active motor/body child.");if(!Context.Runtime.World.AcquirePose(id,this))throw new InvalidOperationException("Layout child already has a movement owner.");var item=child.GetComponent<FlexalonObject>();if(!item){item=child.gameObject.AddComponent<FlexalonObject>();created.Add(item);}children[id]=item;
            }
            foreach(var id in new System.Collections.Generic.List<ulong>(children.Keys))if(!seen.Contains(id)){Context.Runtime.World.ReleasePose(id,this);children.Remove(id);}
            foreach(Transform child in Target)if(child==Host.transform||child.name=="Visual"){var skip=child.GetComponent<FlexalonObject>();if(!skip){skip=child.gameObject.AddComponent<FlexalonObject>();created.Add(skip);}skip.SkipLayout=true;}
        }
        public override void Apply(JObject j){layout.Radius=Mathf.Max(.01f,j.F("radius",1));layout.SpacingDegrees=Mathf.Clamp(j.F("spacing",45),0,360);BindChildren();}
        public override void Tick(float delta)=>BindChildren();
        public override void Dispose(){foreach(var id in children.Keys)Context.Runtime.World.ReleasePose(id,this);foreach(var item in created)if(item)UnityEngine.Object.Destroy(item);if(owned&&layout)UnityEngine.Object.Destroy(layout);}
    }

}
#endif
